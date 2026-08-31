# Roadmap

## Beta 9 — current

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
- Debloat previews use selected-device identity, reference-profile recommendations, source evidence, and current runtime-role checks before execution
- Android TV core settings/provider/framework packages and all Keep-side recommendations are locked across previews, execution, and direct package actions
- Read-only reference package dump export for device contributors
- Scrollable About navigation labeled simply `About`, with developer identity on the About page
- Release automation, self-contained Windows packaging, installer distribution, and metadata validation
- Privacy-redacted support bundles, isolated backups, reviewed script execution, and safer process lifecycle handling
- Restored separate page bodies and closed native WPF control theme gaps for Dark, Pure Black, and White

## Next

- Profile export/import (`.atmprofile`) with optional APK assets
- More hardware fixtures and physical-device validation
- Explicit reversibility states, restore points, and package-data limitations for recommendation scoring

## Later

- scrcpy integration
- QR Wireless Debugging pairing
- automatic LAN discovery
- multi-device operations with explicit per-device confirmation
- script packs and a safe import library
- code-signed installer and update notifications

Android TV Manager will not promise a full device image when standard ADB cannot provide one. Root- or recovery-dependent operations will remain explicitly labeled and opt-in.

An unnecessary plugin architecture is intentionally not planned for the MVP.
