using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SimpitLauncher.Services;

/// <summary>
/// In-process firewall (INetFwPolicy2) and Defender preference (MSFT_MpPreference) control.
/// When the UI process is not elevated, relaunches this WinExe with --elevated-job (UAC, no console).
/// </summary>
public static class WindowsProtectionService
{
    private const int NetFwProfile2Domain = 1;
    private const int NetFwProfile2Private = 2;
    private const int NetFwProfile2Public = 4;

    public static void SetFirewallEnabled(bool enabled)
    {
        if (!IsProcessElevated())
        {
            RunElevatedJobAndWait(enabled ? "firewall-on" : "firewall-off");
            return;
        }

        SetFirewallEnabledCore(enabled);
    }

    public static void SetDefenderRealtimeDisabled(bool disabled)
    {
        if (!IsProcessElevated())
        {
            RunElevatedJobAndWait(disabled ? "defender-off" : "defender-on");
            return;
        }

        SetDefenderRealtimeDisabledCore(disabled);
    }

    /// <summary>
    /// Start: disable USB selective suspend + per-device "allow turn off to save power" for USB*.
    /// Stop: re-enable both.
    /// </summary>
    public static void SetUsbPowerSavingEnabled(bool enabled)
    {
        if (!IsProcessElevated())
        {
            RunElevatedJobAndWait(enabled ? "usb-power-on" : "usb-power-off");
            return;
        }

        SetUsbPowerSavingEnabledCore(enabled);
    }

    private static string ElevatedErrorFilePath =>
        Path.Combine(Path.GetTempPath(), "simpit-launcher-elevated-error.txt");

    /// <summary>Runs a one-shot elevated job and returns a process exit code (0 = success).</summary>
    public static int ExecuteElevatedJob(string job)
    {
        try
        {
            TryDelete(ElevatedErrorFilePath);
            switch (job.Trim().ToLowerInvariant())
            {
                case "firewall-on":
                    SetFirewallEnabledCore(true);
                    return 0;
                case "firewall-off":
                    SetFirewallEnabledCore(false);
                    return 0;
                case "defender-on":
                    // defender-on => realtime protection ON => DisableRealtimeMonitoring false
                    SetDefenderRealtimeDisabledCore(disabled: false);
                    return 0;
                case "defender-off":
                    SetDefenderRealtimeDisabledCore(disabled: true);
                    return 0;
                case "usb-power-on":
                    SetUsbPowerSavingEnabledCore(enabled: true);
                    return 0;
                case "usb-power-off":
                    SetUsbPowerSavingEnabledCore(enabled: false);
                    return 0;
                default:
                    return 3;
            }
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Tamper Protection", StringComparison.OrdinalIgnoreCase))
        {
            TryWriteElevatedError(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            TryWriteElevatedError(ex.Message);
            return 1;
        }
    }

    private static void TryWriteElevatedError(string message)
    {
        try { File.WriteAllText(ElevatedErrorFilePath, message); }
        catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } }
        catch { /* ignore */ }
    }

    private static string? TryReadElevatedError()
    {
        try
        {
            if (!File.Exists(ElevatedErrorFilePath))
            {
                return null;
            }

            var text = File.ReadAllText(ElevatedErrorFilePath).Trim();
            TryDelete(ElevatedErrorFilePath);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void SetFirewallEnabledCore(bool enabled)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                    ?? throw new InvalidOperationException("HNetCfg.FwPolicy2 COM type not found.");
                dynamic fw = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("Failed to create FwPolicy2.");

                // Must set each profile individually (bitmasks are rejected).
                fw.FirewallEnabled[NetFwProfile2Domain] = enabled;
                fw.FirewallEnabled[NetFwProfile2Private] = enabled;
                fw.FirewallEnabled[NetFwProfile2Public] = enabled;

                Marshal.FinalReleaseComObject(fw);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new InvalidOperationException(
                $"Failed to set firewall {(enabled ? "ON" : "OFF")}: {error.Message}", error);
        }
    }

    private static void SetDefenderRealtimeDisabledCore(bool disabled)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT * FROM MSFT_MpPreference"));

            using var results = searcher.Get();
            ManagementObject? instance = null;
            foreach (ManagementObject obj in results)
            {
                instance = obj;
                break;
            }

            if (instance is null)
            {
                throw new InvalidOperationException("MSFT_MpPreference instance not found.");
            }

            using (instance)
            {
                var inParams = instance.GetMethodParameters("Set");
                inParams["DisableRealtimeMonitoring"] = disabled;
                using var outParams = instance.InvokeMethod("Set", inParams, null);
                var returnValue = outParams?["ReturnValue"];
                if (returnValue is not null and not (uint)0 and not 0)
                {
                    throw new InvalidOperationException($"MSFT_MpPreference.Set returned {returnValue}.");
                }
            }

            // Confirm preference stuck (Tamper Protection often blocks silently).
            using var verifySearcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT DisableRealtimeMonitoring FROM MSFT_MpPreference"));
            using var verifyResults = verifySearcher.Get();
            foreach (ManagementObject obj in verifyResults)
            {
                using (obj)
                {
                    var actual = obj["DisableRealtimeMonitoring"];
                    var actualBool = actual is bool b && b;
                    if (actualBool != disabled)
                    {
                        throw new InvalidOperationException(
                            "Defender preference did not change (check Tamper Protection).");
                    }
                }

                break;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to set Defender realtime {(disabled ? "OFF" : "ON")}: {ex.Message}", ex);
        }
    }

    private static void SetUsbPowerSavingEnabledCore(bool enabled)
    {
        // Best-effort: many schemes (esp. High Performance / modern Win11) omit the USB
        // selective-suspend powercfg setting entirely — do not fail the job on that.
        TrySetUsbSelectiveSuspendPowerCfg(enabled);
        SetUsbSelectiveSuspendRegistry(!enabled);

        // Per-device "Allow the computer to turn off this device to save power" for USB*.
        // Enable=false => power saving off (checkbox unchecked).
        var allowTurnOff = enabled;
        var seen = 0;
        var changed = 0;
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT * FROM MSPower_DeviceEnable"));
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var instanceName = obj["InstanceName"]?.ToString() ?? string.Empty;
                    if (!instanceName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    seen++;
                    try
                    {
                        obj["Enable"] = allowTurnOff;
                        obj.Put();
                        changed++;
                    }
                    catch
                    {
                        // Some instances are read-only; skip and continue.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to update USB device power management: {ex.Message}", ex);
        }

        if (seen == 0)
        {
            throw new InvalidOperationException("No USB MSPower_DeviceEnable instances found.");
        }

        if (changed == 0)
        {
            throw new InvalidOperationException(
                $"Found {seen} USB power instances but none could be updated (access denied?).");
        }
    }

    private static void TrySetUsbSelectiveSuspendPowerCfg(bool enabled)
    {
        const string UsbSubgroup = "2a737bb5-f847-439b-8109-961c8acba9d3";
        const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        var value = enabled ? "1" : "0";
        var powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");

        // Ignore failures — setting may not exist on the active scheme.
        _ = RunHidden(powercfg, $"/SETACVALUEINDEX SCHEME_CURRENT {UsbSubgroup} {UsbSelectiveSuspend} {value}", throwOnError: false);
        _ = RunHidden(powercfg, $"/SETDCVALUEINDEX SCHEME_CURRENT {UsbSubgroup} {UsbSelectiveSuspend} {value}", throwOnError: false);
        _ = RunHidden(powercfg, "/SETACTIVE SCHEME_CURRENT", throwOnError: false);
    }

    /// <summary>
    /// Global USB selective-suspend kill switches used when powercfg USB settings are absent.
    /// disableSelectiveSuspend=true means power saving off.
    /// </summary>
    private static void SetUsbSelectiveSuspendRegistry(bool disableSelectiveSuspend)
    {
        var dword = disableSelectiveSuspend ? 1 : 0;
        string[] keys =
        [
            @"SYSTEM\CurrentControlSet\Services\USB",
            @"SYSTEM\CurrentControlSet\Services\usbhub",
            @"SYSTEM\CurrentControlSet\Services\usbhub3",
            @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters",
        ];

        foreach (var keyPath in keys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, writable: true)
                    ?? Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyPath, writable: true);
                key?.SetValue("DisableSelectiveSuspend", dword, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch
            {
                // Service key may not exist on all builds; skip.
            }
        }
    }

    private static int RunHidden(string fileName, string arguments, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{fileName} timed out.");
        }

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} exited with code {process.ExitCode}.");
        }

        return process.ExitCode;
    }

    private static void RunElevatedJobAndWait(string job)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException("Could not locate SimpitLauncher.exe for elevation.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--elevated-job={job}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start elevated job.");
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return;
            }

            var detail = TryReadElevatedError();
            if (process.ExitCode == 2)
            {
                throw new InvalidOperationException(
                    detail ?? "Defender preference did not change (check Tamper Protection).");
            }

            throw new InvalidOperationException(
                detail is not null
                    ? $"Elevated job '{job}' failed: {detail}"
                    : $"Elevated job '{job}' failed with exit code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("UAC cancelled.", ex);
        }
    }
}
