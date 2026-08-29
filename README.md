# Android TV Manager

Android TV Manager is a Windows-first Android TV / Google TV device management toolbox. It focuses on the useful parts of ADB with a fast native WPF interface, live device tracking, safe APK workflows, package management, saved devices, connection history, and reversible script transactions.

This is not adbLink and it is not a Kodi utility. Kodi-specific backup, database, userdata, and compatibility features are intentionally out of scope.

## Beta 1

Current product version: **1.0.0-B1**

Beta 1 provides the application shell, local data foundation, managed Google Platform-Tools bootstrap, asynchronous ADB infrastructure, live device tracking, connection history, native navigation, dashboard, and Windows tray behavior. Device actions and the script/undo foundation are being expanded through the remaining MVP milestones.

## Requirements

- Windows 10 or later
- .NET 10 Desktop Runtime for framework-dependent runs, or use the self-contained publish output
- An Android TV / Google TV device with ADB enabled
- Network access on first run if Platform-Tools are not already installed

The application downloads official Android SDK Platform-Tools from Google's Android repository infrastructure. It does not ship or retrieve a mystery third-party ADB binary.

## Supported connection direction

- USB ADB devices
- Traditional network ADB (`host:port`, normally port 5555)
- Android 11+ Wireless Debugging pairing (`adb pair`) with a follow-up debugging endpoint
- Live state changes through `adb track-devices -l`

## Build and run

```powershell
dotnet restore
dotnet build -c Debug
dotnet test -c Debug
dotnet run --project src/AndroidTVManager.App
```

Release validation:

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet publish src/AndroidTVManager.App -c Release -r win-x64 --self-contained true
```

## Runtime data

Mutable data is stored under `%LOCALAPPDATA%\AndroidTVManager\`:

- `Data\androidtvmanager.db` — SQLite database and migrations
- `Logs\` — bounded application logs
- `Tools\PlatformTools\` — managed official Platform-Tools
- `Scripts\`, `Snapshots\`, `Screenshots\`, `Recordings\`, and `Temp\`

These files are runtime data and are intentionally excluded from Git.

## Product direction

The MVP includes managed ADB bootstrap, USB and network devices, Wireless Debugging pairing, saved devices, SQLite history, APK installation, package management, tray support, script preview/execution, and best-effort undo. Later work may add scrcpy, richer file operations, logcat, device comparisons, multi-device actions, and script packs.
