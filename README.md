# Android TV Manager

[![Build](https://github.com/Eliminater74/AndroidTVManager/actions/workflows/release.yml/badge.svg)](https://github.com/Eliminater74/AndroidTVManager/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/Eliminater74/AndroidTVManager?include_prereleases)](https://github.com/Eliminater74/AndroidTVManager/releases)
[![License](https://img.shields.io/github/license/Eliminater74/AndroidTVManager)](LICENSE)

Android TV Manager is a Windows-first Android TV / Google TV device management toolbox. It provides a native WPF interface for ADB device discovery, saved devices, device intelligence, package management, cautious debloating, APK installation, scripts, configuration inspection, and connection history.

This is not adbLink and it is not a Kodi utility. Kodi-specific backup, database, userdata, and compatibility features are intentionally out of scope.

## Current release

### 1.0.0-B2 — Beta 2

Download the latest published build from the [GitHub Releases page](https://github.com/Eliminater74/AndroidTVManager/releases). Beta 2 is currently published as the latest release; future beta tags may be marked as pre-releases while hardware validation continues.

Available assets:

- `AndroidTVManager-1.0.0-B2-Setup.exe` — self-contained Windows installer
- `AndroidTVManager-1.0.0-B2-win-x64.zip` — portable self-contained build
- `SHA256SUMS.txt` — SHA-256 checksums for the release assets

The installer is currently unsigned. Windows SmartScreen may display a warning until a code-signing certificate and reputation are available; verify the checksum and download only from this repository.

## Highlights

- USB, traditional TCP/IP ADB, and Android 11+ Wireless Debugging pairing
- Saved, renamed, favorited devices that remain visible while offline
- Evidence-backed Device Status with hardware, Android, security, root feasibility, OEM unlock, network, Bluetooth, HDMI/CEC, DRM, services, packages, and raw evidence
- Configuration Explorer for read-only property provenance, conflicts, snapshots, and reports
- Complete package inventory including system, user, enabled, disabled, and uninstalled-for-user views
- Optional cached package icons
- Device-aware debloat previews with Safe, Caution, High Risk, Critical, and Unknown classifications
- Disable-first debloat actions, captured serials, drift checks, transaction history, and restore
- Backup / Restore page for device reports, configuration snapshots, APKs and split APKs, shared storage, legacy app-data attempts, and APK restore
- APK installation through ADB with accurate package-manager errors
- ADB Command Center, scripts, screenshots, live application logs, and system-tray controls
- Dark, Pure Black, and White themes

## Requirements

- Windows 10 or later, x64
- An Android TV / Google TV / Android device with ADB enabled
- Network access on first run if Platform-Tools are not already installed

The application downloads official Android SDK Platform-Tools from Google's Android repository infrastructure. It does not retrieve a mystery third-party ADB binary.

## Install and run

1. Download the installer from [Releases](https://github.com/Eliminater74/AndroidTVManager/releases).
2. Run the installer and launch Android TV Manager.
3. Connect one disposable or recoverable test device over USB, TCP/IP ADB, or Wireless Debugging.
4. Confirm the target serial in the header before running device actions.

The portable ZIP can be extracted to any user-writable folder and run without installation.

## Safety boundaries

Device inspection is read-only and reports `Unknown` when Android does not expose reliable evidence. It does not run `adb root`, `su -c`, unlock commands, fastboot checks, or bootloader changes during passive inspection.

Debloat always creates a preview, captures one target serial, rechecks package state before execution, prefers disabling for User 0, and protects critical, active-role, and Unknown packages from automatic selection. Restore uses the transaction journal.

APK installation uses ADB and is separate from Android's manual unverified-developer installation policy. Android TV Manager never bypasses verification, waiting periods, device administration, or package-manager policy.

## Runtime data

Mutable data is stored under `%LOCALAPPDATA%\AndroidTVManager\`:

- `Data\androidtvmanager.db` — SQLite database and migrations
- `Logs\` — bounded application logs
- `Tools\PlatformTools\` — managed official Platform-Tools
- `Scripts\`, `Snapshots\`, `Screenshots\`, `Recordings\`, `Backups\`, and `Temp\`

These files are runtime data and are intentionally excluded from Git.

## Build from source

Install the .NET 10 SDK and, for local installer creation, [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet restore
dotnet build AndroidTVManager.sln -c Debug
dotnet test AndroidTVManager.sln -c Debug
dotnet run --project src/AndroidTVManager.App
```

Create release artifacts locally:

```powershell
.\scripts\package-release.ps1 -Version 1.0.0-B2
```

The script always creates a portable ZIP and checksum file. It creates the installer when `ISCC.exe` is installed; use `-RequireInstaller` to fail if the installer compiler is unavailable.

## Automated releases

Pushing a tag matching `v*` starts [the release workflow](.github/workflows/release.yml). It restores, builds, tests, publishes a self-contained `win-x64` application, compiles the Inno Setup installer, creates a portable ZIP, generates checksums, and creates a GitHub release with the assets attached.

The release process is documented in [docs/RELEASE.md](docs/RELEASE.md). Version metadata is maintained in the application project and the tag must match it.

## Documentation

- [Changelog](CHANGELOG.md)
- [Roadmap](docs/ROADMAP.md)
- [TODO](TODO.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Release process](docs/RELEASE.md)
- [Contributing](CONTRIBUTING.md)

## License

Android TV Manager is released under the [MIT License](LICENSE).

Copyright © 2026 Eliminater74.
