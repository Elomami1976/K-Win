# K-win — Testing Documentation

## Test Environment

| Property | Required |
|----------|----------|
| **OS** | Windows 11 22H2, 23H2, or 24H2 (x64) |
| **Editions** | Home and Pro |
| **Privileges** | Administrator |
| **Runtime** | Self-contained (no .NET required on target) |

## Build Verification

```bash
# Build (should complete with 0 errors, 0 warnings)
dotnet build -c Release

# Publish single-file
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

| Check | Expected |
|-------|----------|
| Build completes | 0 errors, 0 warnings |
| Output exists | `publish/K-win.exe` (~65 MB) |
| Manifest embedded | UAC prompt on launch |

---

## Functional Test Checklist

### Startup & Compatibility

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 1 | Windows 11 check | Run on Windows 10 | Error dialog: "requires Windows 11" | [ ] |
| 2 | Admin check | Run without admin | Warning dialog: "requires administrator" | [ ] |
| 3 | Normal launch | Run as admin on Win11 | Main form opens, 3 tabs visible | [ ] |
| 4 | Version display | Check footer label | Shows correct build (e.g., "Windows 11 23H2 (Build 22631)") | [ ] |
| 5 | Theme detection | System in dark mode | App starts in dark theme | [ ] |
| 6 | Theme detection | System in light mode | App starts in light theme | [ ] |

### Safety System

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 7 | Restore point | Apply any optimization | Restore point created (verify via `rstrui.exe`) | [ ] |
| 8 | Registry backup | Apply visual effects opt. | `.reg` file created in `%AppData%\K-win\backups\` | [ ] |
| 9 | Operation logging | Apply any optimization | Entry appears in `%AppData%\K-win\logs\kwin_[date].log` | [ ] |
| 10 | Log format | Read log file | Valid JSON on each line with correct fields | [ ] |
| 11 | Preview dialog | Click any "Apply" button | Preview dialog shows list of changes before execution | [ ] |
| 12 | Preview cancel | Click Cancel in preview | No changes applied | [ ] |
| 13 | Undo button | After applying operation | Undo button shows count, tooltip shows description | [ ] |
| 14 | Undo action | Click Undo | Previous state restored, status confirmed | [ ] |
| 15 | Undo stack limit | Apply 6+ operations | Only last 5 available for undo | [ ] |

### Performance Tab

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 16 | Power plans load | Open Performance tab | Combo box shows available plans, active plan selected | [ ] |
| 17 | Change power plan | Select & apply High Perf. | Plan changes (verify via `powercfg /getactivescheme`) | [ ] |
| 18 | Battery warning | On laptop on battery | Warning shown about power consumption | [ ] |
| 19 | Visual effects | Click "Best Performance" | VisualFXSetting = 2, EnableTransparency = 0 in registry | [ ] |
| 20 | Startup list | Open Performance tab | Shows programs from `HKCU\...\Run` | [ ] |
| 21 | Disable startup | Check items, click Disable | Registry values removed, undo registers backup | [ ] |
| 22 | One-Click Boost | Click button | Preview shows 3 changes, applies all on confirm | [ ] |
| 23 | Boost undo | Undo after One-Click | Power plan reverted, registry values restored | [ ] |

### Privacy Tab

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 24 | Telemetry status | Open Privacy tab | Shows current AllowTelemetry value | [ ] |
| 25 | Reduce telemetry | Click "Reduce to Basic" | AllowTelemetry = 1, DiagTrack stopped & disabled | [ ] |
| 26 | Telemetry undo | Undo via button | AllowTelemetry restored from backup | [ ] |
| 27 | Ad ID status | Open Privacy tab | Shows enabled/disabled status | [ ] |
| 28 | Disable Ad ID | Click "Disable" | DisabledByGroupPolicy = 1, status updates | [ ] |
| 29 | Activity history | Click "Clear" | Confirmation dialog appears (warns irreversible) | [ ] |
| 30 | Activity clear | Confirm | CDP files deleted, status shows count | [ ] |
| 31 | Recall (24H2) | On 24H2, click Disable | DisableAIDataAnalysis = 1 | [ ] |
| 32 | Recall (<24H2) | On 23H2 or earlier | Group disabled, label says "Not available" | [ ] |
| 33 | Windows Security | Click "Open" | Windows Security settings app opens | [ ] |

### Cleanup Tab

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 34 | Temp preview | Open Cleanup tab | Shows estimated size for temp files | [ ] |
| 35 | Browser preview | Open Cleanup tab | Shows estimated size for browser caches | [ ] |
| 36 | Clean temp files | Click "Clean" | Preview shown, files deleted, count displayed | [ ] |
| 37 | Clean browser cache | Click "Clean" | Preview shown, cache files deleted | [ ] |
| 38 | Browser running warn | With Chrome open | Warning about locked files in preview | [ ] |
| 39 | Store cache reset | Click "Reset" | `wsreset.exe` runs, completes within 30s | [ ] |
| 40 | Recycle bin | Click "Empty" | Confirmation dialog, bin emptied | [ ] |
| 41 | SFC scan | Click "Run sfc" | Preview shown, marquee progress, output displayed | [ ] |
| 42 | DISM cleanup | Click "Run DISM" | Preview shown, marquee progress, completes | [ ] |

### Error Handling & Edge Cases

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 43 | Timeout handling | Simulate stuck process | Error after 30s, process killed | [ ] |
| 44 | Permission denied | Attempt HKLM write without admin | Graceful error, logged | [ ] |
| 45 | Missing registry key | Read non-existent key | Returns null/not-set, no crash | [ ] |
| 46 | Locked temp files | Clean while files in use | Skips locked files, reports count | [ ] |
| 47 | Defender scan | Submit `K-win.exe` to VirusTotal | No detections | [ ] |
| 48 | Multiple clicks | Double-click Apply quickly | UI disabled during operation, no duplicate execution | [ ] |
| 49 | Close during op | Close form during SFC | Graceful shutdown, operation logged | [ ] |

### Settings

| # | Test | Steps | Expected | Pass |
|---|------|-------|----------|------|
| 50 | Theme toggle | Toggle dark/light in Settings | Theme applies immediately on Save | [ ] |
| 51 | Restore point toggle | Disable auto-restore point | Operations skip restore point creation | [ ] |
| 52 | Open log folder | Click button | Explorer opens log directory | [ ] |
| 53 | Open backup folder | Click button | Explorer opens backup directory | [ ] |
| 54 | Backup cleanup | Set 7 days retention, Save | Backups older than 7 days removed | [ ] |

---

## Windows Edition Compatibility

| Feature | Win11 Home | Win11 Pro | Notes |
|---------|-----------|-----------|-------|
| Power plan change | ✅ | ✅ | |
| Visual effects | ✅ | ✅ | |
| Startup manager | ✅ | ✅ | |
| Telemetry (policy) | ✅ | ✅ | Policy key works on both editions |
| DiagTrack service | ✅ | ✅ | |
| Advertising ID | ✅ | ✅ | |
| Activity history | ✅ | ✅ | |
| Windows Recall | ✅ | ✅ | 24H2+ only |
| All cleanup features | ✅ | ✅ | |

---

## Non-Disruptive Verification

After running K-win optimizations, verify these still work:

| System Component | Verification |
|-----------------|--------------|
| Windows Update | Settings → Windows Update → Check for updates |
| Microsoft Store | Open Store, browse/install an app |
| Windows Security | Open Defender, run Quick Scan |
| Network | Browse internet, verify DNS resolution |
| User login | Sign out and sign back in |
| Start Menu | Open, search for an app |
