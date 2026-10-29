# Changelog

All notable changes to K-Win are listed here.

## [1.5.0] — Enhanced Edition — 2026-10-29

### Added
- New interface: sidebar navigation, dashboard, card-based pages, dark and light themes, native dark title bar.
- Live monitoring of CPU, memory, disk and GPU from Windows performance counters. Monitoring pauses when the window is minimized or off the dashboard, and shows "Unavailable" instead of invented values.
- **Optimize Now**: recommendations grouped by risk, only low-risk items pre-selected, a full preview, each item applied and verified on its own, a summary, and a single undo.
- **History page**: original → new values, warnings, restore point status, and per-operation undo with conflict detection.
- **Startup Manager** (current user, all users 64/32-bit, Startup folders), using Task Manager's enable/disable flags.
- Measured cleanup preview, with cancel and a deletion report.
- Detection of settings managed by an organization or by local Group Policy (shown read-only).
- Optional update check (off by default; GitHub release information only, no downloads).
- Arabic (right-to-left) alongside English and French; all text is in .resx resources.
- 168 automated tests: rollback, cleanup safety, uninstall parsing, monitoring, recommendations, update checker, localization.

### Changed
- K-Win starts **without administrator rights** and asks to elevate only when needed.
- Registry changes: verified .reg backup, read-back verification, exact rollback. The undo history survives restarts.
- Restore points are verified. When Windows reuses one from the last 24 hours, K-Win says so.
- Memory compression state is measured (as administrator) or clearly labeled as inferred.
- Removing apps for all users is an opt-in advanced option.
- Only fully translated languages are offered (English, Français, العربية).

### Removed
- "Clear cached RAM" (no real benefit).
- The hosts-file blocklist download.
- The Spanish, German and Portuguese entries, which only showed English.

### Security
- Cleanup and activity-data clearing never follow junctions or symbolic links.
- Uninstall commands are parsed and validated; no command interpreter is used.
- Processes are launched with fixed arguments. The update checker opens only official release pages.

### Known limitations
- Not yet validated on Windows 11 or at 125% / 150% display scaling (see WINDOWS11_VALIDATION_CHECKLIST.md).
- Service and power-plan steps can be undone only while K-Win stays open.
- Some technical error details are in English inside translated messages.
- The executable is not code-signed, so SmartScreen shows a warning.

## [1.0.0]
- Initial release: power plans, visual effects, startup programs, privacy settings, cleanup, SFC/DISM, restore points and undo.
