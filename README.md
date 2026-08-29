# Android TV Manager

Android TV Manager is a Windows-first Android TV / Google TV device management toolbox. It focuses on the useful parts of ADB with a fast native WPF interface, live device tracking, safe APK workflows, package management, saved devices, connection history, and reversible script transactions.

This is not adbLink and it is not a Kodi utility. Kodi-specific backup, database, userdata, and compatibility features are intentionally out of scope.

## Beta 2

Current product version: **1.0.0-B2**

Beta 2 adds evidence-backed Device Status inspection, cached snapshots, complete package inventory, package role detection, conservative debloat previews, guarded package mutations, package preferences/notes, and the ADB Command Center.

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

## Device intelligence and safety

Device Status runs standard ADB diagnostics asynchronously and records command evidence,
including partial failures. A value shown as `Unknown` is intentionally not inferred from
an absent package or a vendor-specific property. Expert diagnostics can inspect the source
command for each section.

Debloat always targets one captured serial, creates a preview, prefers disabling for User 0,
and rechecks package state before execution. Critical, active-role, and Unknown packages are
never automatically selected. Aggressive mode is still only a preview until the user confirms
it. Restore uses the existing script transaction journal; a device build or package-state drift
invalidates the plan.

Android TV Manager installs APKs through ADB. It does not patch, disable, uninstall, spoof, or
bypass Android Developer Verification or any waiting period used by manual on-device installs.
Manual installation guidance is device/version dependent and must be completed in Android Settings
when required.

## First hardware-test checklist

1. Connect one disposable or recoverable Android TV target and confirm its serial in the header.
2. Run Device Status and verify the Overview, security, installation, package, and service sections.
3. Refresh Applications and test only read-only package details first.
4. Use a non-critical test package to verify Disable, Enable, Restore, and Undo behavior.
5. Create a Simple debloat preview; do not execute Medium/Aggressive until every selected item is reviewed.
6. Confirm the ADB installer reports the real package-manager error if a policy blocks an install.

## Product direction

The MVP includes managed ADB bootstrap, USB and network devices, Wireless Debugging pairing, saved devices, SQLite history, APK installation, package management, tray support, script preview/execution, and best-effort undo. Later work may add scrcpy, richer file operations, logcat, device comparisons, multi-device actions, and script packs.
