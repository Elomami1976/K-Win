# K-Win v1.5.0 Enhanced Edition

A redesigned, safer K-Win: a new interface, Optimize Now with risk-based recommendations, a History page with per-operation undo, and English, Français and العربية.

## Download

| File | Size | SHA-256 |
|---|---|---|
| `K-Win-1.5.0.exe` | 63.6 MB (66,732,535 bytes) | `66213E433940A8DD8AC80D1D403CBDA47365F61F6F374ECA864E8FE27E044D14` |

Portable, self-contained Windows x64 executable. Nothing to install and no .NET runtime needed. Verify with:

```powershell
Get-FileHash .\K-Win-1.5.0.exe -Algorithm SHA256
```

**The executable is unsigned.** Windows SmartScreen may show "Windows protected your PC": click **More info → Run anyway**.

## What changed since 1.0

- **New interface:** sidebar, dashboard with live CPU / memory / disk / GPU monitoring, dark and light themes.
- **Optimize Now:** reads your real settings, groups recommendations by risk and pre-selects only low-risk ones. It previews every change, applies and verifies each item, and lets you undo the whole run.
- **History:** original → new values for every operation; undo a single operation when it is safe.
- **Least privilege:** starts as a normal user and asks to restart as administrator only when a change needs it.
- **Safer changes:** verified registry backups, read-back checks, precise rollback, junction-safe cleanup. Settings controlled by your organization or by local Group Policy are left alone.
- **Startup Manager** that disables entries the way Task Manager does (nothing is deleted).
- **Languages:** English, Français, العربية (right-to-left).
- **Optional update check**, off by default.

Removed: "Clear cached RAM" and the hosts-file blocklist download.

## Quality

- 168 automated tests passed (registry rollback, cleanup safety, recommendations, history, update checker, translations).
- Release build: 0 errors, 0 warnings.

## Important limitations

- **Requires Windows 11. Manual Windows 11 validation is pending.** This release was built and tested on Windows 10 (build 19045) with 168 automated tests and a read-only developer preview. It has not been run on a Windows 11 PC or VM, or at 125% / 150% scaling. See `WINDOWS11_VALIDATION_CHECKLIST.md`. Take a restore point or VM snapshot before use.
- File deletions (cleanup, activity data) cannot be undone.
- Service and power-plan steps can be undone only while K-Win stays open. Registry changes can be undone at any time.
- Some technical error details appear in English inside translated messages.

Full list of changes: `CHANGELOG.md`. Safety details: `SAFETY.md`. License: MIT.
