# Android TV Manager

[![Build](https://github.com/Eliminater74/AndroidTVManager/actions/workflows/release.yml/badge.svg)](https://github.com/Eliminater74/AndroidTVManager/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/Eliminater74/AndroidTVManager)](https://github.com/Eliminater74/AndroidTVManager/releases)
[![Downloads](https://img.shields.io/github/downloads/Eliminater74/AndroidTVManager/total?label=downloads)](https://github.com/Eliminater74/AndroidTVManager/releases)
[![License](https://img.shields.io/github/license/Eliminater74/AndroidTVManager)](LICENSE)

Android TV Manager is a Windows-first Android TV / Google TV device management toolbox. It provides a native WPF interface for ADB device discovery, saved devices, device intelligence, package management, cautious debloating, APK installation, scripts, configuration inspection, and connection history.

This is not adbLink and it is not a Kodi utility. Kodi-specific backup, database, userdata, and compatibility features are intentionally out of scope.

## Current release

### 1.0.0-B5 — Beta 5

Download the latest published build from the [GitHub Releases page](https://github.com/Eliminater74/AndroidTVManager/releases). Beta 5 is the current release and is marked as the repository's latest release.

Available assets:

- [Download AndroidTVManager-Setup.exe](https://github.com/Eliminater74/AndroidTVManager/releases/download/v1.0.0-B5/AndroidTVManager-Setup.exe) — current Beta 5 installer link
- `AndroidTVManager-1.0.0-B5-Setup.exe` — versioned self-contained Windows installer
- `AndroidTVManager-1.0.0-B5-win-x64.zip` — portable self-contained build
- `SHA256SUMS.txt` — SHA-256 checksums for the release assets

The installer is currently unsigned. Windows SmartScreen may display a warning until a code-signing certificate and reputation are available; verify the checksum and download only from this repository.

## Highlights

- USB, traditional TCP/IP ADB, and Android 11+ Wireless Debugging pairing
- Saved, renamed, favorited devices that remain visible while offline
- Evidence-backed Device Status with hardware, Android, security, root feasibility, OEM unlock, network, Bluetooth, HDMI/CEC, DRM, services, packages, and raw evidence
- Display / HDMI Diagnostics with Good State, Bad State, comparison, HDR/HDCP/CEC evidence, SurfaceFlinger modes, and a resolution-change watcher
- ADB Transport Doctor with 10/25/50-probe stability tests and failed transport evidence
- Configuration Explorer for read-only property provenance, conflicts, snapshots, and reports
- Complete package inventory including system, user, enabled, disabled, and uninstalled-for-user views
- Optional cached package icons
- Device-aware debloat previews with Safe, Caution, High Risk, Critical, and Unknown classifications
- Deployment Profiles for repeatable APK/split installation and package setup after a reset
- ADB Remote, live device Logcat, redacted Diagnostic Bundles, codec/network inspection, and shared-storage tools
- Disable-first debloat actions, captured serials, drift checks, transaction history, and restore
- Backup / Restore page for device reports, configuration snapshots, APKs and split APKs, shared storage, legacy app-data attempts, and APK restore
- APK installation through ADB with accurate package-manager errors
- ADB Command Center, scripts, screenshots, live application logs, and system-tray controls
- Dark, Pure Black, and White themes

## Debloat knowledge provenance

The package knowledge database is conservative and source-attributed. It currently cross-references Android TV / Google TV, Chromecast, NVIDIA Shield, Sony Bravia, TCL, Cultraview/Zeasn, Homatics/SEI, TiVo, Xiaomi, Yandex, and related Fire TV/ONN research. The source catalog is stored in `src/AndroidTVManager.Infrastructure/Data/package-knowledge-sources.json`.

Public package dumps and community guides are treated as evidence, not as proof that an action is safe. Each rule can record observed models, source type, impact notes, and whether Android TV Manager has verified the behavior on hardware. Internet-derived rules are never marked hardware-verified automatically; unknown packages remain unknown, active device roles override database suggestions, and disable is preferred over uninstall.

Actionable rules currently cover the strongest evidence for TCL, Philips, Hisense, Sony, NVIDIA Shield, Chromecast/Google TV, Cultraview/Zeasn, Homatics/SEI, TiVo, Xiaomi, and Yandex families. Fire TV/ONN device research and Skyworth/Coocaa, Sharp, JVC, Element, Insignia, and Toshiba identifiers are retained as research provenance or namespace recognition until package behavior is independently verified.

Reference baseline analysis is a separate layer beneath debloat decisions. It compares an inventory with versioned AOSP TV Core and Chromecast-generation Google TV references, plus initial SoC and TCL platform references, and reports origin, role, observed devices, dependencies, and evidence without changing the classifier's risk or action. From Applications, `Export reference dump` creates a read-only, account-free JSON contribution containing device identity, package states, UIDs, APK paths, and runtime-role flags.

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
.\scripts\package-release.ps1 -Version 1.0.0-B5
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
