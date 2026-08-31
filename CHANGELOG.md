# Changelog

All notable changes to Android TV Manager are documented here.

The project follows a beta-first release cycle while real Android TV hardware validation is completed.

## Unreleased

Planned work is tracked in [TODO.md](TODO.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

## [1.0.0-B6] - 2026-08-31

### Fixed

- Placed the About navigation action in a dedicated visible sidebar row instead of allowing a tall navigation scroll view to push it below the window.

## [1.0.0-B4] - 2026-08-30

### Fixed

- Restored a permanently visible About navigation action.
- Made the About screen explicitly identify Eliminater74 and the current application identity.

## [1.0.0-B3] - 2026-08-30

### Added in Beta 3

- Source-attributed knowledge rules for Chromecast, NVIDIA Shield, Sony Bravia, TCL, Cultraview/Zeasn, Homatics/SEI, TiVo, Xiaomi, Google TV Streamer dependencies, and Yandex TV.
- A maintained source catalog with retrieval dates and attribution, separating inventory evidence, tested behavior, regression reports, and anecdotal reports.
- Added Homatics/SEI, TiVo, Xiaomi, Fire TV, ONN, Google TV Streamer, and Yandex source references; only the package roles supported by evidence received actionable rules.
- Added Skyworth/Coocaa namespace recognition and provenance for Sharp, JVC, Philips input regressions, Fire TV television model codes, Hisense, and secondary TV-control research without creating unverified Safe rules.
- Added a layered Reference Baseline Catalog with versioned AOSP TV generations, Chromecast Google TV, SEI/Droidlogic, and TCL references, plus per-inventory origin summaries.
- Added account-free read-only reference dump export with device identity, package states, UIDs, APK paths, and runtime-role flags.
- Deployment Profiles with managed APK assets, SHA-256 identity, split-package deployment, compatibility checks, preview/confirmation, and step-level execution history.
- A typed ADB Remote with D-pad, media, volume, text entry, repeat actions, and favorite app launch buttons.
- A streaming device Logcat page with bounded buffering, filters, save, clear, and problem capture.
- Redacted/full Diagnostic Bundle generation with display, transport, network, codec, package, configuration, logcat, manifest, and checksum evidence.
- Advanced diagnostics for boot/Fastboot state, shared storage, network, codecs, device comparison, and screen recording.

### Changed in Beta 3

- APK backup restoration now verifies SHA-256 checksums before installing.

## [1.0.0-B2] - 2026-08-30

### Added

- Evidence-backed Device Status inspection with hardware, Android, security, network, Bluetooth, HDMI/CEC, DRM, services, thermal, and package sections.
- Separate OEM unlock option, setting, and bootloader capability states.
- Conservative root feasibility guidance without attempting escalation or unlock operations.
- Complete merged package inventory, package role detection, package notes, user overrides, and optional package icons.
- Device-aware debloat planning with preview, risk classification, drift checks, transaction restore, and active-role protection.
- Android Developer Verification evidence and installation guidance.
- Configuration Explorer with runtime/partition property provenance, conflict detection, redaction, snapshots, comparisons, search, and export.
- Saved device management with friendly names, reported device names, MAC addresses, favorites, offline visibility, reconnect, notes, and Wireless Debugging pairing.
- Live application log viewer, themed tray menu, branded application icon, and Dark, Pure Black, and White themes.
- Nested scrolling support for diagnostic panels, lists, and configuration views.

### Changed

- Centralized application version and About metadata around the assembly version.
- Improved Device Status and Applications automatic loading when a target is selected.
- Improved package and diagnostic failure tolerance with command evidence.
- Improved theme contrast and WPF control styling.

### Safety

- Passive inspection does not run root, unlock, fastboot, reboot, or `su -c` commands.
- Debloat never automatically selects Critical or Unknown packages.
- Wireless Debugging pairing codes are not persisted or logged.

## [1.0.0-B1]

The initial Beta 1 foundation established managed Platform-Tools, ADB connections, live device tracking, SQLite persistence, APK installation, package actions, scripts, screenshots, tray support, and the first WPF application shell.
