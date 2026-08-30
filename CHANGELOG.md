# Changelog

All notable changes to Android TV Manager are documented here.

The project follows a beta-first release cycle while real Android TV hardware validation is completed.

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

- Added the Display / HDMI Diagnostics page with Good State, Bad State, comparison, capture history, HDR/HDCP/CEC/audio evidence, SurfaceFlinger modes, vendor display properties, export, and a 10-second watcher.
- Added the Backup / Restore page with capability-aware report, configuration, APK/split-APK, shared-storage, legacy app-data, and APK restore workflows.
- Added visible Debloat preset choices with preselected known candidates and manual selection for reviewed Unknown/private packages.
- Kept Critical packages and active device roles locked from manual debloat selection.

Planned work is tracked in [TODO.md](TODO.md) and [docs/ROADMAP.md](docs/ROADMAP.md).
