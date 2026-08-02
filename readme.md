# Simpit Launcher

<p align="center">
  <img src="docs/screenshot.png" alt="Simpit Launcher main window" width="520" />
</p>

Windows desktop app for sim-pit setups: start and stop an ordered checklist of apps, webhooks, Shelly relays, COM commands, and system tweaks per profile (default: **Flight** and **Racing**). Replace ad-hoc batch files with one **START** / **STOP**, tray actions, or the command line.

Repo: [github.com/stuart11n/simpit-launcher](https://github.com/stuart11n/simpit-launcher)

## What it does

- Runs an ordered list of enabled tasks for the active profile.
- **START** / **STOP** walk the list top to bottom (non-delayed tasks run one after another).
- Per-task **delay (seconds)**: if greater than zero, that step is scheduled in the background after the delay while the rest of the list continues.
- Profiles (tabs) each have their own task list and display name (Rename).
- Settings: `%AppData%\SimpitLauncher\tasks.json`  
  (On first run, settings are copied from the legacy `%AppData%\FlightLauncher\` folder if present.)

## Task types

### 1) Executable

- Launch an `.exe`, `.bat`, `.cmd`, URI (e.g. `steam://...`), or leave Path empty for stop-only rows.
- Working directory is set automatically to the file's folder.
- Options:
  - Run as administrator
  - Kill process before launching (optional Force)
  - On Stop: Kill, Force kill, or custom command line
- Kill image names support wildcards and comma-separated lists (e.g. `SPAD.neXt*`, `PimaxClient.exe`, `DeviceSetting.exe`).

### 2) Webhook

- HTTP GET Start URL and/or Stop URL (either may be empty).
- Useful for arbitrary HTTP endpoints.

### 3) Shelly

- IP address only (validated).
- **START:** `http://<IP>/relay/0?turn=on`
- **STOP:** `http://<IP>/relay/0?turn=off`

### 4) COM command

- COM port (e.g. `COM17`); writes via `\\.\COMn`.
- **START** / **STOP** each write a configured text string (either may be empty to skip).
- Appends CRLF when the text has no trailing newline.
- Baud rate **0** (default) leaves port settings unchanged; set a rate only to force baud.
- Escape sequences: `\r`, `\n`, `\t`, `\\`.

### 5) System (built-in)

Each option has a **START** action (sim session on) and a matching **STOP** action (restore). Most need UAC once if the app is not already elevated. Check **Disable stop action** on a system task to run START only and skip restore on STOP.

| Option | START | STOP |
| --- | --- | --- |
| **Disable firewall** | Firewall off (all profiles via `INetFwPolicy2`) | Firewall on |
| **Disable Realtime Threat Scanning** | Defender realtime off (`MSFT_MpPreference` / `DisableRealtimeMonitoring`) | Defender realtime on |
| **Disable USB power saving** | USB selective suspend off (registry; `powercfg` when present) + per-device USB “allow turn off to save power” unchecked (`MSPower_DeviceEnable`) | Re-enable selective suspend + per-device power saving |
| **Max CPU performance** | `powercfg /s` High performance | `powercfg /s` Balanced |
| **Max GPU performance** | `nvidia-smi -pl` start watts (editable) | `nvidia-smi -pl` stop watts (editable) |

Notes on system options:

- **Realtime Threat Scanning**: Tamper Protection can block the preference change — turn it off temporarily in Windows Security if START fails.
- **USB power saving**: Many power schemes omit the USB `powercfg` setting; the app still applies service registry keys and per-device WMI updates.
- **Max GPU performance**: Requires NVIDIA drivers / `nvidia-smi` on `PATH` (or discoverable).

## Main window

- Green **START** / red **STOP** (left half); live **Status** panel (right half): GPU power limit, CPU plan, firewall, realtime threat scanning.
- Status refreshes on load, every 5 seconds, and after runs.
- Progress bar + log while a run is in progress (full log on the **Log** tab).
- **Tasks** / **Log** tabs: task list with Flight / Racing profiles, or the run log.
- Profile toolbar (right of the tabs): **Add** dropdown (Executable, Webhook, Shelly, COM command, System option), **Desktop shortcuts**, **Rename**.
- **Desktop shortcuts**: creates Start and Stop desktop shortcuts for the active profile.
- Per row: enable, summary (includes `Delay Ns · …` when set), type badge, move ↑/↓, start/stop icons, delete icon, drag reorder.
  - ↑ / ↓ move one step; **Ctrl+↑** / **Ctrl+↓** jump to top / bottom.
- Double-click a row to edit (includes delay, Test start/stop — tests run immediately, ignoring delay).
- Start on login (HKCU Run).

## Tray

- Close or minimize hides to the system tray (does not exit).
- Double-click tray icon to show the window.
- Tray menu: Show, Start, Stop, Exit.
- Exit from the tray fully quits the app.

## Command line

Useful for scripts and shortcuts.

| Switch | Description |
| --- | --- |
| `--profile flight\|racing` | Select profile (aliases: `--mode`, `-p`, `--flight`, `--racing`) |
| `--start` | Run START for the selected/active profile |
| `--stop` | Run STOP for the selected/active profile |
| `--exit` | Exit after `--start`/`--stop` completes |
| `--minimized` | Start hidden in the tray |
| `--help` | Show help in the log |

Examples:

```text
SimpitLauncher.exe --profile flight --start --exit
SimpitLauncher.exe --racing --stop --exit
SimpitLauncher.exe -p flight --start
```

## Notes

- Admin actions (firewall, Defender, USB power saving, some kills, `nvidia-smi`) may show UAC. Firewall/Defender/USB use in-process COM/WMI/registry (no console); if not elevated, the app relaunches itself briefly for those jobs.
- Soft Kill may not close apps that ignore close requests; Force Kill uses `taskkill /F /T` and can retry elevated.
- Profile tab names are stored as `modes[].name`; CLI/internal ids remain `flight` and `racing`.
- First launch seeds a Flight list inspired by typical `flight.bat` / `off.bat` workflows; Racing starts empty.

## Build / run

```powershell
dotnet build SimpitLauncher.csproj -c Debug -p:Platform=x64
```

Output:

```text
bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\SimpitLauncher.exe
```

## Installer

Unpackaged WinUI: self-contained publish bundles .NET and Windows App SDK, then optionally wrap with Inno Setup.

### 1) Publish

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

Output: `artifacts\publish\win-x64\`

`publish.ps1` cleans before publish (so the merged WinUI `.pri` regenerates), and verifies `SimpitLauncher.pri` plus `.xbf` files are present.

### 2) Optional Setup.exe (Inno Setup 6)

- https://jrsoftware.org/isinfo.php
- Run `publish.ps1`, then build `installer\SimpitLauncher.iss`

Output: `artifacts\installer\SimpitLauncherSetup.exe`

## GitHub Actions

Workflow: `.github/workflows/build.yml`

On push/PR to `master` or `main` (and **Run workflow**):

1. Restore and build Release x64
2. `publish.ps1` (self-contained win-x64)
3. Build `SimpitLauncherSetup.exe`
4. Upload artifacts:
   - `SimpitLauncher-win-x64`
   - `SimpitLauncherSetup`

**Actions** → workflow run → **Artifacts**.

### Draft release from a version tag

Push a semver tag to cut a **draft** GitHub Release (installer + zip attached):

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Then open **Releases**, review the draft notes/assets, and click **Publish release**.

The installer `AppVersion` is taken from the tag (e.g. `v1.2.3` → `1.2.3`).

## License

MIT — see [LICENSE](LICENSE).
