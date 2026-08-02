using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FlightLauncher.Services;

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

    /// <summary>Runs a one-shot elevated job and returns a process exit code (0 = success).</summary>
    public static int ExecuteElevatedJob(string job)
    {
        try
        {
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
                default:
                    return 3;
            }
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Tamper Protection", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        catch (Exception)
        {
            return 1;
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

    private static void RunElevatedJobAndWait(string job)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException("Could not locate FlightLauncher.exe for elevation.");
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

            throw new InvalidOperationException(
                process.ExitCode == 2
                    ? "Defender preference did not change (check Tamper Protection)."
                    : $"Elevated job '{job}' failed with exit code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("UAC cancelled.", ex);
        }
    }
}
