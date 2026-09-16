# Roadmap

## Beta 19 — current: App Installer XAPK metadata and themed disabled buttons

- App Installer uses XAPK `split_apks[].id == "base"` metadata so APKCombo-style archives with a package-named base APK install through `install-multiple`. Filename heuristics remain the fallback. Physical Shield confirmation of a real APKCombo XAPK is still open.
- Disabled Ghost and Accent buttons keep Dark / Pure Black / Light colors.

## Beta 18 — device-specific one-click Debloat

- Debloat main workflow is Safe / Recommended / Deep / Custom. Recommended is one-click for reviewed device profiles; Custom keeps the package checklist. Physical Shield Recommended + restore confirmation is still open. Streamer/kirkwood and unmatched hardware may show zero reviewed actions.

## Beta 17 — App Installer, Shield parsers, and Google TV families

- App Installer sideloads APK, split APK, APKS, APKM, and XAPK after an analyze-before-install preview. Split sets use `install-multiple`. XAPK OBB copies only to `/sdcard/Android/obb/<package>/`. Physical Shield sideload and OBB copy remain open.
- Shield Device Status parsers, honest missing-tool states, Debloat darcy matching, and package readback. Physical Device Status confirmation is still open.
- Hardware-family matching and conservative package overlays for Google TV Streamer 4K, Chromecast with Google TV 4K, and onn. Google TV 4K Box. Physical verification of those three devices is pending.

## Beta 20 — next: device session and shell

- Extract remaining `MainWindowViewModel` page construction behind `IAdbDeviceSession` and typed navigation.
- Pages subscribe to the shared session instead of the shell pushing `SelectedDevice` into each page VM.
- Immediately after that context exists, add capability and user-scope header badges.

## After B20

- Restore points and Fully / Partially / Not reversible classifications before batch mutations.
- Feature-aware debloat protection (Plex, casting, voice, game streaming, accessibility).
- A small WPF smoke set, keyboard focus, Narrator, and DPI checks — not a giant UI suite.
- CI: vulnerability scan, analyzers, coverage, CodeQL, and SHA-pinned Actions.
- Pin Inno Setup; Authenticode-sign the app and installer; updater publisher/certificate check.
- Finish the physical hardware acceptance matrix before stable 1.0.

See [Device support audit](DEVICE-SUPPORT-AUDIT.md) for closed B15 findings and remaining hardware limits.

## Beta 15 — safety hardening and disconnect

- Platform-Tools rollback, shared secret redaction, live package safety gate, and fail-closed deployment compatibility.
- APK restore verifies the expected SHA-256 set; untrusted updater payloads are deleted; recovery ZIPs fail closed on product mismatch.
- SQLite pre-migration snapshots, deterministic shutdown, and Network/Wireless Debugging disconnect from the TARGET header and Devices list.
- `IAdbDeviceSession` owns the live list, TARGET, preferred endpoint, and disconnect workflow. Pages still receive the selected device from `MainWindowViewModel`.
- Pending physical smoke: Shield connect/disconnect/reconnect beside an emulator, TARGET fallback, inventory, one harmless disable/restore, updater check, backup verification, clean exit, and B15 → B16 installer upgrade.

## Beta 14 — multi-device targeting

- Implemented live-list refresh after network connect/pair/reconnect, serial/endpoint target matching, and a TARGET picker that distinguishes emulator USB from Shield network devices.
- Connecting a second ADB device now selects it for Device Status, Applications, and the other device pages instead of remaining on the first emulator.
- Pending physical Shield/TV validation of connect-and-switch while an Android Studio emulator is attached.

## Beta 13 — device inspection

- Implemented Device Status Deep scan with 32 additional read-only probes, full searchable evidence, explicit command coverage and JSON export.
- Pending physical TV/Shield/tablet/head-unit validation, especially vendor-specific MCU and service visibility.
- Next: build-specific parsers for exposed vendor data based on reviewed hardware reports.

## Beta 13 — recovery workflow

- Implemented file-picker sideload with recovery reboot, exact-serial waiting and package hash review.
- Implemented guarded Pixel C recovery flashing; Lineage Recovery wipe/menu steps remain explicit on-device checkpoints.
- Pending physical Pixel C validation against the exact installed Lineage build; no Lineage 22 compatibility claim is made.
- Future improvements: optional expected-checksum comparison, live transfer output and a reviewed device/build recipe catalog.

See [Recovery / Sideload](RECOVERY-SIDELOAD.md). These additions are included in Beta 13.

## Beta 12 — previous release

- Dedicated Shield TV reference profile and reviewed optional-package rules
- Verified animation timing controls, exact-value undo and Shield settings guidance
- Honest ADB connection acknowledgements, User 0 inventory and conservative Automotive gates
- Exact package-state journaling and consistent User 0 enable/restore commands

See [Device support audit](DEVICE-SUPPORT-AUDIT.md) for evidence, limitations and the full prioritized backlog. These improvements are included in 1.0.0-B12; hardware acceptance work remains open.

## Existing capabilities through Beta 11

- Managed official Android SDK Platform-Tools bootstrap
- USB, traditional network ADB, and Android Wireless Debugging pairing
- Saved, renamed, favorited devices with offline visibility
- SQLite devices, sessions, events, package inventory, and inspection history
- Device intelligence, Configuration Explorer, and capability evidence
- Display / HDMI Diagnostics with named captures, comparison, and change watching
- ADB Transport Doctor with repeated stability probes and transport failure evidence
- APK installation and complete package management
- Conservative debloat planning with transaction restore
- System tray, settings, logs, themes, scripts, screenshots, and device tools
- Deployment Profiles with copied APK assets, split-package installs, compatibility warnings, and execution history
- Typed ADB Remote with repeat controls and persisted favorite apps
- Streaming device Logcat with bounded filtering, save, and problem capture
- Redacted Diagnostic Bundles with inspection, display, transport, network, codec, and checksum evidence
- Advanced diagnostics for boot/Fastboot state, shared storage, network, codecs, device comparison, and screen recording
- Source-attributed debloat knowledge for Chromecast, Shield, Sony, TCL, Cultraview/Zeasn, Homatics/SEI, TiVo, Xiaomi, Yandex, Fire TV, and ONN research
- Research-only recognition for Skyworth/Coocaa, Sharp, JVC, Element, Insignia, and Toshiba families pending package-level verification
- Layered Reference Baseline Catalog with AOSP TV generations, Chromecast Google TV, SoC/SEI, and TCL references
- Debloat previews use selected-device identity, loaded/matched reference-profile visibility, source evidence, breakage notes, and current runtime-role checks before execution. One-click Safe / Recommended / Deep profiles orchestrate that planner; Custom remains the detailed package list.
- Android TV 16 emulator profile coverage for AOSP/Google TV core packages and reversible optional-package recommendations
- Android TV core settings/provider/framework packages and all Keep-side recommendations are locked across previews, execution, and direct package actions
- Read-only reference package dump export for device contributors
- Scrollable About navigation labeled simply `About`, with developer identity on the About page
- Release automation, self-contained Windows packaging, installer distribution, and metadata validation
- Privacy-redacted support bundles, isolated backups, reviewed script execution, and safer process lifecycle handling
- Restored separate page bodies and closed native WPF control theme gaps for Dark, Pure Black, and White

## Later

- Profile export/import (`.atmprofile`) with optional APK assets
- Touch/gesture remote once the shell split is in place
- QR Wireless Debugging pairing and automatic LAN discovery
- Richer backup history, file browsing, and APK icon extraction
- scrcpy integration
- multi-device operations with explicit per-device confirmation
- script packs and a safe import library
- code-signed installer and update publisher verification

Android TV Manager will not promise a full device image when standard ADB cannot provide one. Root- or recovery-dependent operations will remain explicitly labeled and opt-in.

An unnecessary plugin architecture is intentionally not planned for the MVP.
