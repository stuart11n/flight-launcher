# Simpit Launcher

<p align="center">
  <img src="docs/screenshot.png" alt="Simpit Launcher main window" width="520" />
</p>

Windows desktop app for sim-pit setups: start and stop an ordered checklist of apps, webhooks, and system tweaks per profile (default: **Flight** and **Racing**). Replace ad-hoc batch files with one **START** / **STOP**, tray actions, or the command line.

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
- Useful for smart relays (e.g. `turn=on` / `turn=off`).

### 3) System (built-in)

- **Disable firewall** — `INetFwPolicy2` off / on (all profiles)
- **Disable Realtime Threat Scanning** — `MSFT_MpPreference` / WMI (`DisableRealtimeMonitoring`)  
  Tamper Protection can block this; turn it off temporarily if the preference does not change.
- **Max CPU performance** — `powercfg /s` High performance / Balanced
- **Max GPU performance** — `nvidia-smi -pl` start watts / stop watts (configurable)

## Main window

- Green **START** / red **STOP** (left half); live **Status** panel (right half): GPU power limit, CPU plan, firewall, realtime threat scanning.
- Status refreshes on load, every 5 seconds, and after runs.
- Progress bar + log while a run is in progress (full log on the **Log** tab).
- **Tasks** / **Log** tabs: task list with Flight / Racing profiles, or the run log.
- Add executable / webhook / system option.
- Per row: enable, summary (includes `Delay Ns · …` when set), type badge, move up/down, start/stop icons, delete, drag reorder.
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

- Admin actions (firewall, Defender, some kills, `nvidia-smi`) may show UAC. Firewall/Defender use in-process COM/WMI (no console); if not elevated, the app relaunches itself briefly for those jobs.
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
