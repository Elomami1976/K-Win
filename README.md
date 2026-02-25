# K-win — Windows 11 Optimization Tool

![Windows 11](https://img.shields.io/badge/Windows-11-blue?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8-purple)
![Build](https://img.shields.io/badge/Status-Stable-brightgreen)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

<p align="center">
  <img src="https://elomami1976.github.io/K-Win/assets/og-image.png" alt="K-win Windows 11 Optimization Tool" width="600">
</p>

A safe, transparent Windows 11 optimization tool focused on performance, privacy, and cleanup.
Built with C# and .NET 8 Windows Forms.

## 📥 Download

[![Download K-win](https://img.shields.io/badge/Download-K--win%20v1.0.0-blue)](https://github.com/Elomami1976/K-Win/releases/latest/download/K-win.exe)

**[⬇️ Download Latest Release](https://github.com/Elomami1976/K-Win/releases/latest)**

## ✨ Features

### ⚡ Performance
- Power Plan Selector (Balanced / High Performance / Ultimate Performance)
- Visual Effects Optimizer (Best Performance + transparency control)
- Startup Program Manager
- One-Click Performance Boost

### 🔒 Privacy
- Reduce Telemetry to Basic level (safe policy value)
- Disable or reset Advertising ID
- Clear Activity History data
- Disable Windows Recall / AI data analysis (Windows 11 24H2+)
- Quick access to Windows Security

### 🧹 Cleanup
- Temporary file cleanup
- Browser cache cleanup (Edge / Chrome / Firefox)
- Microsoft Store cache reset
- Recycle Bin cleanup
- System File Checker (SFC)
- DISM component cleanup

## 📊 What K-win Does

| Category | Optimizations | Typical Result |
|----------|---------------|----------------|
| **Performance** | Power plans, visual effects, startup apps | Faster boot and snappier UI |
| **Privacy** | Telemetry, activity history, ad tracking | Reduced background data collection |
| **Cleanup** | Temp files, browser cache, Store cache | More free disk space |
| **Gaming** | Game Mode and related settings | Better system responsiveness for games |

## 🛡️ Safety First

K-win is designed with safety as a core requirement:

- ✅ Automatic Restore Points before major operations
- ✅ Backup-first changes for registry operations
- ✅ Undo support for recent operations
- ✅ Preview dialog before applying changes
- ✅ No risky “registry cleaner” behavior
- ✅ Open-source transparency
- ✅ No telemetry from the app itself (works offline)

For full technical details, see `SAFETY.md`.

## 🚀 Getting Started

1. Download `K-win.exe` from the latest release
2. Right-click and choose **Run as administrator**
3. Review changes in preview dialogs
4. Apply desired optimizations
5. Restart Windows if prompted

## ✅ Requirements

- **OS:** Windows 11 (22H2, 23H2, 24H2)
- **Architecture:** x64
- **Privileges:** Administrator rights required
- **Runtime:** Self-contained build (no separate .NET install required)

## 🏗️ Build from Source

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

## ❓ Troubleshooting

**“Windows Defender blocked K-win”**
→ Click **More info** → **Run anyway** (project is open source)

**“Changes are not applying”**
→ Run K-win as **Administrator**

**“How do I undo changes?”**
→ Use the **Undo** button in the app, or restore from a System Restore Point

## 🧪 Testing

See `TESTING.md` for a full validation checklist.

## 🤝 Contributing

Contributions are welcome.
You can open issues, suggest improvements, or submit pull requests.

---

<p align="center">
	<a href="https://elomami1976.github.io/K-Win/">🌐 Website</a> •
	<a href="https://github.com/Elomami1976/K-Win/releases">⬇️ Download</a> •
	<a href="https://github.com/Elomami1976/K-Win/issues">🐛 Report Bug</a>
</p>
