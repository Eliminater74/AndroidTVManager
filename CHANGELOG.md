# Changelog

All notable changes to Android TV Manager are documented here.

The project follows a beta-first release cycle while real Android TV hardware validation is completed.

## Unreleased

### Added

- Source-attributed knowledge rules for Chromecast, NVIDIA Shield, Sony Bravia, TCL, Cultraview/Zeasn, Homatics/SEI, TiVo, Xiaomi, Google TV Streamer dependencies, and Yandex TV.
- A maintained source catalog with retrieval dates and attribution, separating inventory evidence, tested behavior, regression reports, and anecdotal reports.
- Added Homatics/SEI, TiVo, Xiaomi, Fire TV, ONN, Google TV Streamer, and Yandex source references; only the package roles supported by evidence received actionable rules.
- Added Skyworth/Coocaa namespace recognition and provenance for Sharp, JVC, Philips input regressions, Fire TV television model codes, Hisense, and secondary TV-control research without creating unverified Safe rules.
- Deployment Profiles with managed APK assets, SHA-256 identity, split-package deployment, compatibility checks, preview/confirmation, and step-level execution history.
- A typed ADB Remote with D-pad, media, volume, text entry, repeat actions, and favorite app launch buttons.
- A streaming device Logcat page with bounded buffering, filters, save, clear, and problem capture.
- Redacted/full Diagnostic Bundle generation with display, transport, network, codec, package, configuration, logcat, manifest, and checksum evidence.
- Advanced diagnostics for boot/Fastboot state, shared storage, network, codecs, device comparison, and screen recording.

### Changed

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

## Unreleased

- Added first-pass conservative Hisense and Philips package rules using vendor-specific evidence and feature-impact explanations.
- Added the ADB Transport Doctor with selectable 10, 25, and 50-probe stability tests and per-probe latency/failure results.
- Added the Display / HDMI Diagnostics page with Good State, Bad State, comparison, capture history, HDR/HDCP/CEC/audio evidence, SurfaceFlinger modes, vendor display properties, export, and a 10-second watcher.
- Added the Backup / Restore page with capability-aware report, configuration, APK/split-APK, shared-storage, legacy app-data, and APK restore workflows.
- Added visible Debloat preset choices with preselected known candidates and manual selection for reviewed Unknown/private packages.
- Kept Critical packages and active device roles locked from manual debloat selection.

Planned work is tracked in [TODO.md](TODO.md) and [docs/ROADMAP.md](docs/ROADMAP.md).
