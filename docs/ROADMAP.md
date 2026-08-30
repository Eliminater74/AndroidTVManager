# Roadmap

## Beta 2 — current

- Managed official Android SDK Platform-Tools bootstrap
- USB, traditional network ADB, and Android Wireless Debugging pairing
- Saved, renamed, favorited devices with offline visibility
- SQLite devices, sessions, events, package inventory, and inspection history
- Device intelligence, Configuration Explorer, and capability evidence
- APK installation and complete package management
- Conservative debloat planning with transaction restore
- System tray, settings, logs, themes, scripts, screenshots, and device tools
- Release automation, self-contained Windows packaging, and installer distribution

## Next

- Backup/Restore follow-up: user-selected file backup with progress, cancellation, and checksums
- Export/import of app settings and saved-device definitions
- Device comparison across inspection/configuration snapshots
- More hardware fixtures and physical-device validation

## Later

- scrcpy integration
- QR Wireless Debugging pairing
- richer file manager
- separate logcat viewer
- automatic LAN discovery
- multi-device operations with explicit per-device confirmation
- script packs and a safe import library
- code-signed installer and update notifications

Android TV Manager will not promise a full device image when standard ADB cannot provide one. Root- or recovery-dependent operations will remain explicitly labeled and opt-in.

An unnecessary plugin architecture is intentionally not planned for the MVP.
