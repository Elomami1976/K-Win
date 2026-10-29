# K-Win 1.5 — Windows 11 Manual Validation Checklist

Release requirement: every item below must be checked on **real Windows 11** (22H2, 23H2 or 24H2)
with a **Release build** before K-Win 1.5 is published. The Debug-only `--dev-ui-preview` mode does
not count: it is read-only and skips the Windows 11 check.

**Use a test PC or a virtual machine with a snapshot.** Several steps change real system settings.

| Field | Value |
|---|---|
| Tester | |
| Date | |
| Windows edition / version / build | |
| Display scaling(s) tested | |
| K-Win build (commit) | |

Mark each item: ✅ pass · ❌ fail (write what happened) · ➖ not applicable.

---

## 0. Preparation

- [ ] Take a VM snapshot (or create a manual restore point).
- [ ] System Protection is **on** for drive C: (needed for restore-point tests).
- [ ] Remove `%AppData%\K-win` to start from a first run.
- [ ] Copy the Release `K-win.exe` to a normal folder (for example `Downloads`).

## 1. Launch without administrator rights

- [ ] Double-click `K-win.exe`: **no UAC prompt** appears.
- [ ] The language picker shows only English, Français, العربية; pick English.
- [ ] Main window opens on the Dashboard; status bar shows **Administrator: No**.
- [ ] Dashboard shows the real Windows 11 edition, version (for example 23H2) and build.
- [ ] CPU, Memory, Disk and GPU tiles update every 2 s; charts fill from the right.
- [ ] GPU shows a percentage or "Unavailable" — never a made-up value; label says "Busiest engine, all GPUs".
- [ ] Minimize for 30 s: Task Manager shows K-Win near 0% CPU (monitoring paused).

## 2. Elevation workflow

- [ ] Click **Administrator: No** → explanation dialog → **Yes** → UAC prompt.
- [ ] Decline UAC: K-Win stays open, status says it was not restarted.
- [ ] Accept UAC: K-Win reopens on the same page; status bar shows **Administrator: Yes**.
- [ ] As a normal user, try an HKLM setting (for example Diagnostic data): K-Win asks to restart as administrator instead of failing.

## 3. Registry settings: apply and rollback

For each setting below: note the state shown, apply, confirm the preview, check the result in
Windows, then **Undo**, and confirm Windows shows the original state again.

| Setting | Apply OK | Verified in Windows | Undo restores | Notes |
|---|---|---|---|---|
| Visual effects (Best performance) | | Sign out/in: animations off | | |
| Game Mode | | Settings > Gaming | | |
| Fast Startup (as admin) | | Control Panel > Power Options | | |
| File name extensions | | File Explorer | | |
| Dark mode | | Settings > Personalization | | |
| Transparency | | Settings > Personalization | | |
| Widgets button | | Taskbar (may be blocked on new builds: K-Win must report failure) | | |
| Chat button (21H2/22H2) | | Taskbar | | |
| Menu delay | | after sign-out | | |
| Shutdown wait times (as admin) | | registry values | | |
| End hung apps automatically | | registry value | | |
| Classic right-click menu | | Explorer restarts; right-click shows full menu | | |

- [ ] A failed write (for example Widgets on a protected build) is shown as **failed**, not as success.
- [ ] Close and reopen K-Win, then **Undo**: registry values are still restored (undo survives restarts).
- [ ] History page: each operation lists original → new values; per-operation **Undo** is refused with a clear reason when a later operation changed the same value.

## 4. Optimize Now

- [ ] Dashboard → **Optimize Now**: the list shows only settings that differ from recommendations; preferences are not listed.
- [ ] Only low-risk items are pre-selected; medium-risk items are unselected; items changed outside K-Win are unselected.
- [ ] Preview lists every registry change, service/power steps and side effects; **Cancel** changes nothing.
- [ ] Apply (as admin): restore point is created (or "reused from the last 24 h" is reported); summary shows each item as Applied or Failed.
- [ ] **Undo** reverses the whole run; History marks it as undone.
- [ ] Run Optimize Now again: applied items are now in "already recommended".

## 5. Startup entries

- [ ] Startup Manager lists entries from: current-user Run, all-users Run (64-bit and 32-bit), both Startup folders — compare with Task Manager > Startup apps.
- [ ] Publisher and "file missing" flags are correct; search and column sorting work; details box shows the full command.
- [ ] Disable a current-user entry → Task Manager shows it **Disabled**; the Run value still exists (regedit).
- [ ] Re-enable → Task Manager shows **Enabled**.
- [ ] Disable an all-users entry: requires administrator; works after elevation.
- [ ] Disable then **Undo**: original state restored. Sign out/in: disabled entries do not start.

## 6. Cleanup

- [ ] Cleanup page scans automatically; sizes appear; Recycle Bin is **not** selected.
- [ ] "Windows temporary files" shows "Needs administrator" as a normal user and is cleanable as admin.
- [ ] Preview lists categories and sizes; Cancel at the preview changes nothing.
- [ ] Start a large cleanup and press **Cancel**: it stops; the report states what was already deleted.
- [ ] Completed cleanup report: files deleted, space freed, files skipped. Browser passwords/cookies/history still work.
- [ ] A junction placed inside `%TEMP%` pointing to a folder with a test file: the test file is **not** deleted.

## 7. Privacy

- [ ] Diagnostic data (as admin): `AllowTelemetry` policy set; DiagTrack service stopped; Undo restores the value (service restored while K-Win stays open).
- [ ] Advertising ID: Settings > Privacy > General shows the option managed by policy; Undo removes the policy.
- [ ] Activity history: policy values set; "Clear activity data" asks for confirmation (irreversible).
- [ ] Recall (24H2+ only): policy set; on 23H2 or older the card says "not available".
- [ ] DNS: apply Cloudflare as admin → `ipconfig /all` shows 1.1.1.1; "Automatic (DHCP)" restores.
- [ ] Local Group Policy: set "Allow Telemetry" in `gpedit.msc` (Pro/Enterprise) → the K-Win card shows "Set by local Group Policy" and cannot change it; Optimize Now skips it.

## 8. Memory compression

- [ ] As a normal user: card shows "Probably on/off (inferred)" with the inference note.
- [ ] As administrator: card shows the measured state ("Measured with Get-MMAgent").
- [ ] Turn off → restart Windows → `Get-MMAgent` shows MemoryCompression False; turn on again → True after restart.

## 9. Restore point creation

- [ ] With System Protection on and no restore point in the last 24 h: an admin change creates one (visible in `rstrui.exe`), History shows "Created".
- [ ] A second change within 24 h: History shows "Recent one reused" (Windows limit), not "Created".
- [ ] With System Protection **off**: K-Win reports the failure and asks whether to continue; choosing No changes nothing.

## 10. Themes

- [ ] Dark and Light mode on every page (Dashboard, Performance, Cleanup, Privacy, Startup, Health, Tools ×3, Settings, History, Optimize dialog): no unreadable text, no light boxes in dark mode.
- [ ] The title bar follows the theme (Windows 11 caption color).
- [ ] Theme choice is remembered after restart.

## 11. Languages

- [ ] English, Français, العربية: switch from the top bar; K-Win restarts on the same page.
- [ ] No untranslated keys (text like `dash.cpu`) anywhere.
- [ ] Arabic: sidebar on the right, cards and buttons mirrored, numbers/paths/commands read correctly (for example "358.5 GB / 673.6 GB", registry paths in History).
- [ ] French: long labels wrap, nothing clipped; status bar readable.

## 12. DPI scaling (Settings > Display > Scale)

Check at **100%, 125% and 150%** (sign out/in between if Windows asks):

| Check | 100% | 125% | 150% |
|---|---|---|---|
| Sidebar fully visible, no clipped items | | | |
| Dashboard cards and tiles readable, charts drawn | | | |
| Setting cards: no clipped text or buttons | | | |
| Lists (Startup, Apps, History) readable, headers dark | | | |
| Dialogs (Preview, Optimize, Updates) fit the screen | | | |
| Window resizable to minimum size without overlaps | | | |
| Move window between monitors with different scaling (if available) | | | |

## 13. Updates (optional feature)

- [ ] Off by default (Settings > Updates).
- [ ] **Check now** online: shows "up to date" or the new version with notes; "Yes" opens only `github.com/Elomami1976/K-Win/releases/...`.
- [ ] **Check now** offline: friendly message, no crash; everything else still works.

## 14. Wrap-up

- [ ] `%AppData%\K-win` contains only: logs, backups, history.json, undo.json, settings.json, language.json.
- [ ] No network traffic except an update check you started (check with Resource Monitor).
- [ ] Revert the VM snapshot.

**Result:** ☐ All required items pass ☐ Issues found (list below)
