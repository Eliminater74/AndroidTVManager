# TODO

This file tracks concrete work items. Larger product direction belongs in [docs/ROADMAP.md](docs/ROADMAP.md).

## Before hardware validation

- [ ] Test USB discovery with a physical Android TV device.
- [ ] Test traditional TCP/IP ADB and saved-device reconnect.
- [ ] Test Android Wireless Debugging pairing on supported Android versions.
- [ ] Verify Device Status values against at least one Google TV and one manufacturer TV.
- [ ] Exercise package inventory and read-only package details before package mutations.
- [ ] Test Simple debloat preview and restore using a disposable device.
- [ ] Verify installer upgrade and uninstall behavior.
- [ ] Add code signing when a release certificate is available.

## Beta 2 follow-up

- [ ] Add backup integrity verification and a richer backup history browser.
- [ ] Add restore warnings and validation for more backup artifact types.
- [ ] Add device comparison between saved inspection/configuration snapshots.
- [ ] Improve package icon extraction for more APK/resource formats.
- [ ] Add richer file browsing with explicit user-selected paths.
- [ ] Add logcat viewing separately from the application log viewer.
- [ ] Add QR-based Wireless Debugging pairing where platform support is reliable.

## Quality

- [ ] Add UI automation coverage for theme switching and saved-device target synchronization.
- [ ] Add representative ADB fixtures for more Android TV vendors and API levels.
- [ ] Review accessibility names, keyboard navigation, focus states, and high-contrast behavior.
- [ ] Keep release artifacts and runtime data out of source control.
