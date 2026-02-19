# K-win — Safety Documentation

Complete list of all registry changes, service modifications, and file operations performed by K-win.

## Guiding Principles

1. **Every write is preceded by a backup** — Registry keys are exported to `.reg` files before modification
2. **Every batch of changes creates a Restore Point** — Via WMI `SystemRestore.CreateRestorePoint`
3. **Nothing critical is ever disabled** — 20+ Windows services are blocklisted
4. **AllowTelemetry is never set to 0** — Only Basic (1), to avoid breaking Windows Update
5. **No third-party tools are executed** — Only built-in Windows executables
6. **No boot configuration changes** — BCD, Secure Boot, TPM are never modified

---

## Registry Modifications

### Performance — Visual Effects

| Key | Value | Data | Kind | Purpose |
|-----|-------|------|------|---------|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` | `VisualFXSetting` | `2` | DWORD | Set to "Best Performance" (disables animations, shadows, etc.) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | `EnableTransparency` | `0` | DWORD | Disable transparency effects |

### Performance — Startup Programs

| Key | Value | Data | Purpose |
|-----|-------|------|---------|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | *(varies)* | *(deleted)* | Disable user startup programs by removing their Run entries |

**Safety:** Values are exported to `.reg` backup before deletion. Undo restores the backup.

### Privacy — Telemetry

| Key | Value | Data | Kind | Purpose |
|-----|-------|------|------|---------|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `AllowTelemetry` | `1` | DWORD | Reduce telemetry to Basic level. **Never set to 0** to preserve Windows Update functionality. |

**Service modification:**
| Service | Action | Purpose |
|---------|--------|---------|
| `DiagTrack` (Connected User Experiences and Telemetry) | Stop + Disable (Start = 4) | Reduces background telemetry data collection |

### Privacy — Advertising ID

| Key | Value | Data | Kind | Purpose |
|-----|-------|------|------|---------|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo` | `DisabledByGroupPolicy` | `1` | DWORD | Prevents apps from using the advertising ID for personalized ads |

### Privacy — Activity History

| Key | Value | Data | Kind | Purpose |
|-----|-------|------|------|---------|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `Start_TrackDocs` | `0` | DWORD | Disable document tracking |

**File deletion:**
| Path | Purpose |
|------|---------|
| `%LOCALAPPDATA%\ConnectedDevicesPlatform\*` | Clear Timeline/activity history data |

**Warning:** Activity history clearing is irreversible (files are permanently deleted). The registry change is backed up.

### Privacy — Windows Recall (24H2+ only)

| Key | Value | Data | Kind | Purpose |
|-----|-------|------|------|---------|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` | `DisableAIDataAnalysis` | `1` | DWORD | Disable Windows Recall AI snapshot feature |

**Availability:** Only applied on Windows 11 24H2 (build 26100+).

---

## Power Plan Changes

| Tool | Command | Purpose |
|------|---------|---------|
| `powercfg.exe` | `/setactive {GUID}` | Switch active power plan |

**Approved GUIDs only:**
| GUID | Plan |
|------|------|
| `381b4222-f694-41f0-9685-ff5bb260df2e` | Balanced |
| `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` | High Performance |
| `e9a42b02-d5df-448d-aa00-03f14749eb61` | Ultimate Performance |

---

## File Cleanup Operations

### Temporary Files
| Path | Action |
|------|--------|
| `%TEMP%\*` | Delete files (skip locked files) |
| `%LOCALAPPDATA%\Temp\*` | Delete files (skip locked files) |

### Browser Caches
| Browser | Path | Action |
|---------|------|--------|
| Microsoft Edge | `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache\*` | Delete |
| Microsoft Edge | `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache\*` | Delete |
| Google Chrome | `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache\*` | Delete |
| Google Chrome | `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache\*` | Delete |
| Mozilla Firefox | `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2\*` | Delete |

**Warning:** Running browsers may lock cache files. K-win warns if browsers are detected running.

### System Tools Executed
| Tool | Arguments | Timeout | Purpose |
|------|-----------|---------|---------|
| `wsreset.exe` | *(none)* | 30s | Reset Microsoft Store cache |
| `cleanmgr.exe` | `/sagerun:1` | 120s | Windows Disk Cleanup with saved profile |
| `sfc.exe` | `/scannow` | 600s | System File Checker |
| `dism.exe` | `/Online /Cleanup-Image /StartComponentCleanup` | 600s | DISM component cleanup |

### Recycle Bin
| API | Flags | Purpose |
|-----|-------|---------|
| `SHEmptyRecycleBin` | `SHERB_NOCONFIRMATION \| SHERB_NOPROGRESSUI \| SHERB_NOSOUND` | Empty Recycle Bin (after user confirmation dialog) |

---

## Blocked Operations (Never Performed)

K-win explicitly validates and blocks the following:

### Critical Services (never disabled)
`wuauserv`, `WinDefend`, `Winmgmt`, `RpcSs`, `PlugPlay`, `EventLog`, `Dhcp`, `Dnscache`, `nsi`, `LanmanWorkstation`, `CryptSvc`, `TrustedInstaller`, `BFE`, `mpssvc`, `Schedule`, `SENS`, `Power`, `ProfSvc`, `SamSs`, `lsass`

### Protected Registry Paths (never written)
- `*\SYSTEM\CurrentControlSet\Control\LSA\*`
- `*\SYSTEM\CurrentControlSet\Services\lsass\*`
- `*\SYSTEM\CurrentControlSet\Control\SecureBoot\*`
- `*\SYSTEM\CurrentControlSet\Control\CI\*`
- `*\BCD00000000\*`

### Protected File Paths (never deleted)
- `%WINDIR%\System32\*`
- `%WINDIR%\SysWOW64\*`
- `%WINDIR%\WinSxS\*`
- `%ProgramFiles%\*` (except known cache subdirectories)

### Forbidden Actions
- No third-party executable downloads or execution
- No unsigned PowerShell script execution
- No boot configuration (BCD) modification
- No TPM or Secure Boot modification
- No removal of built-in Windows apps (Edge, Store, etc.)
- No Windows Update service disabling

---

## Backup & Recovery

### Automatic Backups
- **Location:** `%AppData%\K-win\backups\`
- **Format:** Standard Windows `.reg` files (importable via `reg.exe import`)
- **Naming:** `YYYYMMDD_HHmmss_fff_[keypath].reg`
- **Retention:** Configurable in Settings (default 30 days)

### Manual Recovery
If K-win's undo function is unavailable, backups can be restored manually:
```cmd
reg.exe import "%APPDATA%\K-win\backups\[filename].reg"
```

### System Restore
Every batch operation creates a System Restore Point named:
```
K-win Win11: [Operation Name] on YYYY-MM-DD HH:mm
```
These can be accessed via **System Properties → System Restore** or `rstrui.exe`.

---

## Logging

- **Location:** `%AppData%\K-win\logs\kwin_YYYY-MM-DD.log`
- **Format:** JSON Lines (one JSON object per line)
- **Fields:** `timestamp`, `level`, `operation`, `target`, `status`, `old_value`, `new_value`, `backup_created`, `backup_path`, `error`, `operation_id`

Example:
```json
{"timestamp":"2026-01-15T10:30:00Z","level":"INFO","operation":"SetValueWithBackup","target":"HKCU\\Software\\...\\VisualFXSetting","status":"success","old_value":"0","new_value":"2","backup_created":"true","backup_path":"C:\\Users\\...\\backups\\20260115_103000_HKCU_Software....reg"}
```
