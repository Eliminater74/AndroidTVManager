# Changelog

All notable changes to Android TV Manager are documented here.

The project follows a beta-first release cycle while real Android TV hardware validation is completed.

## Unreleased

Planned work is tracked in [TODO.md](TODO.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

## [1.0.0-B10] - 2026-08-31

Beta 10 focuses on debloat intelligence, source attribution, and safety locks for Android TV core packages.

### Fixed

- Tightened package role parsing so packages mentioned in `dumpsys device_policy` policy lists are not misclassified as device owners.
- Debloat previews now use the selected device identity when applying manufacturer/model rules instead of relying only on a cached inspection snapshot.
- Debloat preview rows now carry reference-profile origin, role, observed-device, dependency, and match-count evidence.
- Reference-protected TV core/framework roles are locked in debloat previews even when they are not present in the flat package-rule list.
- Debloat execution now rechecks selected packages against current runtime-role protection before running disable actions.
- Android TV Settings, Settings Provider, TV Provider, TV framework stubs, permission/package installers, and Live TV packages now have explicit Keep-side core rules.
- Reference baseline matches can now contribute reviewed Caution/High Risk recommendations instead of only promoting protected packages to Critical.
- Destructive package actions in Applications are blocked for Critical, protected, and Keep-side package assessments.
- Corrected the Philips EPOP/demo rule so it no longer carries copied Sony Katniss/TCL source attribution.
- Conflicting Google Katniss voice-search evidence now resolves to High Risk / Keep instead of an Aggressive preset candidate.
- Debloat presets no longer auto-select packages whose reviewed action is Keep, even in Aggressive mode.

### Tests

- Added regression coverage for IPTV/player apps that appear in non-owner policy output, selected-device profile matching, and reference-protected TV core packages.
- Added coverage for reference-derived recommendations, imported Safe-to-Caution capping, Keep-rule locks, Android TV core classifications, and corrected Philips demo attribution.

## [1.0.0-B9] - 2026-08-31

### Fixed

- Restored rendered page bodies for Deployment Profiles, Remote, Device Logcat, Diagnostic Bundles, Advanced Diagnostics, and Device Comparison.
- Removed light-theme fallback surfaces from native WPF tabs, data grids, list views, table headers, scrollbars, and plain buttons.
- Applied the current Dark, Pure Black, or White theme to the native Windows title bar where supported.
- Prevented long top-bar page descriptions from overlapping the target selector.

### Tests

- Added regression checks for page-view XAML initialization and shared native-control theme coverage.

## [1.0.0-B8] - 2026-08-31

### Fixed

- Restored the visible About page identity, developer, and version details.
- Redacted the selected device serial from support diagnostic bundles.
- Prevented the updater from terminating unrelated `adb.exe` processes.
- Isolated each backup in a unique directory and rejected cross-device APK restores.
- Added validation and explicit confirmation before scripts can run.
- Hardened shared-storage path boundaries, IPv6 endpoint handling, logcat buffering, recording state, startup cleanup, and device update processing.

### Release

- Added normal push and pull-request build/test validation.
- Made release packaging validate project metadata, executable version, portable ZIP contents, and generated checksums.
- Made tag-triggered release publishing use the tagged source revision and support safe asset replacement on reruns.

## [1.0.0-B7] - 2026-08-31

### Fixed

- Moved About into the normal scrollable Main navigation list and simplified its label to `About`.

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
