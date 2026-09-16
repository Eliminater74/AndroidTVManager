# Changelog

All notable changes to Android TV Manager are documented here.

The project follows a beta-first release cycle while real Android TV hardware validation is completed.

## Unreleased

Planned work is tracked in [TODO.md](TODO.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

## [1.0.0-B16] - 2026-09-16

Beta 16 is a hotfix so in-app updates can show the full release notes and still reach Continue.

### Fixed

- Confirmation dialogs keep a fixed 560px width, cap the message pane to the work area, and show a vertical scrollbar so long changelog text no longer hides Cancel and Continue.

### Release and validation

- Updated app and installer metadata to 1.0.0-B16 (assembly/file version 1.0.0.16), release documentation, and download links. Package rules remain at the Beta 12 revision.
- Debug/Release builds and 239 device-independent tests cover the confirmation viewport limits.
- If Beta 15's in-app Install dialog blocked Continue, download this installer from GitHub Releases. Physical-device validation remains pending.

## [1.0.0-B15] - 2026-09-16

Beta 15 is the hardening and disconnect release. It does not add another large product subsystem.

### Fixed

- Restore the previous Platform-Tools install if activation fails after the live tools folder has already been moved aside, and verify `adb version` plus `fastboot.exe` from the active directory before discarding the backup.
- Kill timed-out or canceled ADB client processes without tearing down the shared ADB server process tree.
- Redact pairing codes, credentials, local user paths, and APK/sideload/push/pull file arguments in command logs and script journals. `RedactOutput()` now uses the shared redactor instead of concatenating raw stdout/stderr.
- Block deployment when a profile's mandatory ABI or Android TV/Google TV requirements cannot be verified. Unknown required evidence is no longer treated as a bypassable warning.
- Block APK restore unless every `apks/` file listed in `SHA256SUMS.txt` is present with a matching hash and no extra files exist. Failed verification reports zero restored and zero failed packages, and nothing is installed.
- Restore confirmation copy now states that APK copy does not restore app data, accounts, or settings.
- Delete downloaded update installers when the checksum is missing, mismatched, or the payload exceeds 200 MB, and do not leave those files in Temp.
- Sideload refuses a ZIP that declares `pre-device` / updater-script product names when the live `ro.product.device` is missing or does not match. Missing device metadata is reported as unverified, not compatible.
- Copy the SQLite file to a `.pre-migrate.bak` before applying schema upgrades, keep the two newest copies, delete their `-wal`/`-shm` sidecars when pruning, run `PRAGMA integrity_check`, and fail closed if the database is unreadable.
- Shut down through a coordinator that stops the device tracker, recovers open sessions, and flushes the file logger. Unknown UI exceptions now close the app after they are logged.
- Network and Wireless Debugging targets can be disconnected from the header TARGET picker and the Devices list. USB devices stay attached, saved devices remain saved, and the global target falls back to another connected device.

### Added

- Shared `ISensitiveDataRedactor` for logs, script journals, diagnostic bundles, and ADB argument lists, including IPv6 and local user-path redaction.
- Shared `IPackageSafetyGate` so Applications, Scripts, Deployment Profiles, and Debloat all re-check live User 0 inventory, Automotive/user evidence, active roles, Keep/Critical locks, and build fingerprint before package mutations. Enable/restore remain allowed for locked packages so recovery still works. `PackageManager` now requires the gate, and Applications/Scripts/Deployment pass the prepared build fingerprint so drift is checked on the mutation itself.
- Shared `IAdbDeviceSession` owns the live ADB list, TARGET selection, preferred-endpoint sticky selection, and Network/Wireless disconnect. The header TARGET command and the Devices page both run that workflow so disconnect logging, preferred-target clearing, and tracker refresh stay in one place. Pages still receive the selected device from `MainWindowViewModel`; finishing that subscribe model is Beta 16.
- Backup manifests now catalog expected package and APK-file counts so restore can compare the on-disk set against what the backup claimed.
- Recovery ZIP inspection parses `META-INF/com/android/metadata` and updater-script product checks so sideload can fail closed on incompatible or unverifiable device identity.

### Release and validation

- Updated app and installer metadata to 1.0.0-B15 (assembly/file version 1.0.0.15), release documentation, and download links. Package rules remain at the Beta 12 revision.
- Debug/Release builds and 237 device-independent tests cover Platform-Tools rollback, package safety, backup verification, recovery ZIP identity, SQLite snapshots, shutdown, Network/Wireless disconnect, and the shared ADB session.
- Physical-device validation remains pending. Smoke Shield network connect/disconnect/reconnect while an emulator stays attached, TARGET fallback, package inventory, one harmless disable/restore, updater check, backup verification, clean exit, and installer upgrade from B14 → B15.

## [1.0.0-B14] - 2026-09-15

Beta 14 makes a newly connected network device, such as an NVIDIA Shield, selectable while an emulator is already attached.

### Fixed

- Refresh the live ADB device list after a successful network connect, pair, or reconnect, and select that endpoint as the active TARGET.
- Keep the chosen device across list rebuilds by matching serial or endpoint, instead of snapping back to the first connected emulator.
- Publish newly connected devices from `track-devices` by refreshing `adb devices -l` on tracker output and a short poll, so a Shield appears without a manual Refresh.
- Sync the header TARGET across Devices, Device Status, Applications, Install APK, and the other device pages. Clicking a live device or **Use as target** switches the rest of the app.
- Show model and transport/serial in the TARGET picker so an emulator and a Shield are easy to tell apart.

### Release and validation

- Updated app and installer metadata to 1.0.0-B14 (assembly/file version 1.0.0.14), release documentation, and download links. Package rules remain at the Beta 12 revision.
- Debug/Release builds and device-independent tests cover serial/endpoint target matching and connect-and-select behavior.
- Physical-device validation remains pending. Connecting a Shield still requires Network debugging enabled and this PC authorized on the TV.

## [1.0.0-B13] - 2026-09-08

Beta 13 adds guided recovery/sideload and deeper device inspection for ADB-capable TVs, tablets and head units.

### Added

- Device Status Deep scan: 32 additional fixed read-only probes, searchable full stdout/stderr, per-command coverage states and JSON report export. Preserve reported vendor/head-unit properties without inferring hidden hardware support.
- Distinguish permission-denied, unavailable, timed-out, failed and empty command results. Prevent stale scans/progress from overwriting a newly selected device and preserve cancellation through snapshot persistence.
- Recovery / Sideload page with explicit ADB/Fastboot target selection, native IMG/ZIP file pickers, SHA-256 review, recovery reboot and bounded waiting for Lineage Recovery's Apply from ADB mode.
- Pixel C recovery flashing gated by live product `dragon`, confirmed unlocked bootloader and recovery partition size. Selected files are checked again and held against modification during transfer.
- Cancellation for recovery waiting/sideload, exact-serial routing, failure diagnostics and explicit installation verification on the tablet. Wipes, unlocking and automatic post-install reboot are excluded.
- Device-independent recovery regression coverage and native WPF page rendering checks. Hardware validation remains pending.

### Release and validation

- Updated app and installer metadata to 1.0.0-B13 (assembly/file version 1.0.0.13), release documentation, download links and feature guides. Package rules remain at the Beta 12 revision.
- Debug/Release builds and 175 device-independent tests cover recovery safety, exact-serial routing, deep-scan evidence, stale-result suppression and WPF rendering.
- Physical-device validation remains pending. Pixel C flashing requires compatible files and confirmed bootloader/partition evidence; no Lineage 22 compatibility is claimed. Deep inspection reports exposed data and does not provide hidden MCU/CAN-bus firmware access.

## [1.0.0-B12] - 2026-09-07

Beta 12 expands NVIDIA Shield TV support and improves connection, tuning and recovery reliability.

### Added

- Dedicated 17-entry NVIDIA Shield TV debloat reference profile and nine reviewed package rules. Telemetry candidates require Medium or Aggressive; gaming and Plex hosting candidates require Aggressive. Platform, audio, remote, update and accessory protections remain in place.
- Native WPF Tweaks page with read/apply/undo for Android animation timing, captured previous values, write verification and Shield picture/audio/performance guidance.
- Tablet and standalone Android head-unit connection guidance, with explicit distinctions for Android Automotive and projection displays.
- Device support audit with a support matrix, remaining findings, hardware acceptance checklist and prioritized improvement ideas.

### Fixed

- Require ADB connection/pairing acknowledgements instead of treating zero-exit failure messages as successful connections.
- Scope debloat inventory and active-role queries to User 0. Block incomplete or empty feature evidence, unsupported foreground users and Android Automotive before preview/execution.
- Clear stale debloat previews and capture the target before asynchronous initialization.
- Capture exact package identities and User 0 installation state for recovery; explicitly scope enable/restore commands and undo to User 0.
- Verify setting restoration and allow partial undo to retry only failed actions.

### Release and validation

- Updated application/installer metadata to 1.0.0-B12 (assembly/file version 1.0.0.12), current documentation and download links. Package ruleset revision is vendor-tv-sourced-2026-09-07-v5.
- Debug and Release builds and all 149 device-independent tests pass, including WPF rendering and stateful tuning/recovery regressions.
- Physical Shield/tablet/head-unit acceptance remains open. ADB connectivity requires vendor-exposed debugging; vehicle-specific package management and secondary-user workflows are not certified. Vendor-specific Shield settings are guidance, not universal ADB switches.

## [1.0.0-B11] - 2026-09-01

Beta 11 focuses on making debloat previews behave like a profile-driven recommendation engine instead of a static package list.

### Added

- Added an Android TV 16 / API 36 emulator reference profile sourced from a live Google `sdk_google_atv64_x86_64` package inventory.
- Added Debloat page visibility for loaded reference profiles, active profile matches, origin, generation, and matched package counts.
- Added Android TV 16 emulator package recommendations for reversible optional packages including Basic Dreams, Backdrop, TV Recommendations, Play Games, YouTube TV, Google Calendar Sync, and CTS shim packages.

### Fixed

- Current AOSP Android TV reference coverage now applies to Android TV 15+ API levels, including Android TV 16 emulator builds.
- Reference-derived reviewed recommendations now map to the correct debloat operation, including `Uninstall for user 0` when a rule explicitly calls for it.
- Already-disabled packages are no longer auto-selected in debloat previews.
- Enabled input-method packages are now captured alongside the default input method so voice/keyboard services are protected as active device roles.

### Tests

- Added regression coverage for Android TV 16 emulator profile matching, optional package selection, core launcher locking, already-disabled candidates, reviewed uninstall actions, reference profile listing, and enabled input-method role detection.

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
