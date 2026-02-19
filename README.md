# K-win — Windows 11 Optimization Tool

A safe, reliable Windows 11 performance, privacy, and cleanup optimization tool built with C# .NET 8 Windows Forms.

## Features

### ⚡ Performance Tab
- **Power Plan Selector** — Switch between Balanced, High Performance, and Ultimate Performance plans
- **Visual Effects Optimizer** — Set Windows to "Best Performance" visual settings and disable transparency
- **Startup Program Manager** — View and disable startup programs from `HKCU\...\Run`
- **One-Click Performance Boost** — Applies High Performance plan + Best visual effects + Disable transparency in one step

### 🔒 Privacy Tab
- **Telemetry Control** — Reduce Windows telemetry to Basic level (AllowTelemetry = 1) and stop DiagTrack service
- **Advertising ID** — Disable personalized advertising ID via Group Policy registry key
- **Activity History** — Clear Timeline activity records and Connected Devices Platform data
- **Windows Recall** — Disable Windows Recall/AI data analysis on Windows 11 24H2+
- **Windows Security** — Quick link to open Windows Security settings

### 🧹 Cleanup Tab
- **Temporary File Cleaner** — Clean Windows Temp and User Temp with space preview
- **Browser Cache Cleaner** — Clean Edge, Chrome, and Firefox cache directories
- **Microsoft Store Cache** — Reset via `wsreset.exe`
- **Recycle Bin** — Empty with confirmation
- **System File Checker** — Run `sfc /scannow` with progress indicator
- **DISM Cleanup** — Run component cleanup to free disk space

## Requirements

- **OS:** Windows 11 22H2, 23H2, or 24H2 (x64 only)
- **Permissions:** Administrator (UAC prompt on launch)
- **Runtime:** Self-contained — no .NET installation required

## Installation

1. Download `K-win.exe` from the latest release
2. Right-click → **Run as administrator** (or the UAC prompt will appear automatically)
3. The app verifies Windows 11 compatibility on startup

## Building from Source

### Prerequisites
- Visual Studio 2022 (17.8+) with ".NET desktop development" workload
- .NET 8 SDK

### Build
```bash
dotnet build -c Release
```

### Publish (single-file EXE)
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

The output `publish/K-win.exe` is a self-contained single-file executable (~65 MB).

## Safety Architecture

K-win is designed with safety as a non-negotiable requirement:

1. **System Restore Point** — Created automatically via WMI before any system modification
2. **Registry Backup** — Every registry write exports the target key to `%AppData%\K-win\backups\` first
3. **Undo Stack** — Up to 5 operations can be undone from the main UI
4. **Preview Dialog** — Every operation shows a detailed list of changes before applying
5. **Operation Logging** — All operations logged as JSON Lines to `%AppData%\K-win\logs\`
6. **Timeouts** — 30-second timeout on all process executions
7. **Validation** — All registry paths, file paths, and service names are validated before modification
8. **Critical Service Protection** — 20+ critical Windows services are blocklisted from being disabled

## Data Locations

| Path | Contents |
|------|----------|
| `%AppData%\K-win\logs\` | Daily JSON Lines log files (`kwin_YYYY-MM-DD.log`) |
| `%AppData%\K-win\backups\` | Registry `.reg` backup files with timestamps |

## Approved System Tools

K-win only executes these built-in Windows tools:

| Tool | Purpose |
|------|---------|
| `powercfg.exe` | Power plan management |
| `cleanmgr.exe` | Disk Cleanup |
| `sfc.exe` | System File Checker |
| `dism.exe` | DISM image servicing |
| `wsreset.exe` | Microsoft Store cache reset |
| `reg.exe` | Registry import/export for backups |

## Theme Support

K-win automatically detects your Windows 11 light/dark mode preference via:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme
```

You can override this in Settings (⚙ button).

## License

This project is provided as-is for personal use. See SAFETY.md for a complete list of all system modifications.
