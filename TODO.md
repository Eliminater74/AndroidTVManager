# TODO

This file tracks concrete work items. Larger product direction belongs in [docs/ROADMAP.md](docs/ROADMAP.md).

## Unreleased — App Installer

Turn the old Install APK page into a real sideload installer. Do not treat this as hardware certification.

- [x] Rename the page to App Installer and route browse/analyze/install through `IBulkApkService`.
- [x] Support APK, split APK sets, APKS, APKM, and XAPK, including folder selection of splits.
- [x] Install split sets with `install-multiple`; fail before install when a split set has no base APK.
- [x] Fail closed on APKS archives with multiple unmatched standalone variants.
- [x] Copy XAPK OBB files only to `/sdcard/Android/obb/<package>/` after a successful APK install; ignore `Android/data` and other unknown payloads.
- [x] Report partial success when the APK installs but OBB copy fails; do not auto-uninstall.
- [x] Keep archive extraction limits (2 GB APKS, 8 GB XAPK/APKM), traversal/symlink/duplicate rejection, and temp cleanup on success/failure/cancel.
- [x] Verify known package identity after ADB success; keep Recovery / Sideload separate.
- [ ] Sideload a normal APK, a split set, and an XAPK with OBB on the physical Shield; confirm scoped-storage OBB copy honestly if the firmware blocks `Android/obb`.

## Unreleased — Shield inspection and Debloat correctness

Code-side, fixture-backed fixes on top of 1.0.0-B16. Do not treat this as hardware certification.

- [x] Parse Shield meminfo so swap free and swap used are distinct.
- [x] Parse `dumpsys display` `fps=` modes, active refresh, and HDR type 2; keep logical vs physical resolution.
- [x] Read Vulkan from `pm list features`; do not treat `darcy` as a SoC.
- [x] Replace inspection `sh -c` compound probes with separate commands.
- [x] Stock non-root, missing `gsi_tool`, HDMI/CEC Supported, and missing DRM service classified honestly.
- [x] Device Status labels for RAM, logical/physical display, HDR, swap, and unknown SoC.
- [x] Shield Debloat profile matches NVIDIA + SHIELD Android TV / `darcy` without FriendlyName; Simple empty selection is explained.
- [x] Package mutation readback and Debloat preview refresh after execute/restore.
- [ ] Re-check Device Status and a Debloat preview on the physical Shield without mutating packages.

## Unreleased — Google TV Streamer, Chromecast 4K, and onn. 4K Box profiles

Device-family identification and conservative package overlays. Inspection stays generic. This is not hardware certification and does not replace the B17 MainWindow/session split.

- [x] Match Google TV Streamer 4K from kirkwood / GRS6B hardware evidence, never FriendlyName.
- [x] Match Chromecast with Google TV 4K from sabrina / sabrina_prod_stable, excluding boreal HD and kirkwood.
- [x] Match onn. Google TV 4K Box (YOC / onn_4k_gtv / DV6105Z), excluding dopinder, jarvis/SNA 4K Pro, and XNA Full HD.
- [x] Keep Google TV core separate from sabrina, kirkwood, and YOC overlays; Keep/Critical wins on conflict.
- [x] Chromecast-only diagnostics stay off the Streamer overlay; Streamer and YOC overlays are Keep-only until a physical dump.
- [ ] Capture read-only diagnostic bundles on physical Google TV Streamer 4K, Chromecast with Google TV 4K, and onn. Google TV 4K Box.

## Beta 16 — current: confirmation dialog hotfix

- [x] Keep confirmation dialogs at a fixed width with a work-area-capped message pane and vertical scrollbar so long release notes do not hide Cancel and Continue.

Physical smoke from Beta 15 still applies, plus installer upgrade from B15 → B16.

## Beta 15 — hardening and disconnect

Shipped. Smoke the exact flows on hardware, then start the shell split.

- [x] Restore previous Platform-Tools after a failed activation and verify the active tools directory.
- [x] Kill timed-out ADB clients without tearing down the shared ADB server process tree.
- [x] Share `ISensitiveDataRedactor` across logs, journals, diagnostic bundles, and ADB arguments.
- [x] Fail closed when deployment required ABI/TV evidence is missing or incompatible.
- [x] Share `IPackageSafetyGate` across Applications, Scripts, Deployment Profiles, and Debloat, and pass the prepared build fingerprint.
- [x] Verify APK restore against the expected SHA-256 set and package catalog.
- [x] Delete untrusted updater downloads and reject installers larger than 200 MB.
- [x] Compare recovery ZIP pre-device metadata with the live product before sideload.
- [x] Snapshot SQLite before schema migrations and prune `.pre-migrate.bak` WAL/SHM sidecars.
- [x] Shut down through a coordinator that stops the tracker, recovers sessions, and flushes logs.
- [x] Disconnect Network and Wireless Debugging targets from the TARGET header and Devices list without removing saved devices.
- [x] Own the live list, TARGET selection, preferred endpoint, and disconnect workflow on `IAdbDeviceSession`. Pages still receive `SelectedDevice` from `MainWindowViewModel`.

### Physical smoke before calling B15 done

- [ ] Shield network connect, disconnect, and reconnect while an emulator stays attached; confirm TARGET fallback after disconnect.
- [ ] Test USB discovery with a physical Android TV device.
- [ ] Test traditional TCP/IP ADB and saved-device reconnect.
- [ ] Test Android Wireless Debugging pairing on supported Android versions.
- [ ] Exercise package inventory and one harmless disable/restore on a disposable device.
- [ ] Confirm updater check, backup verification, and a normal application exit (tracker stop, session recovery, log flush).
- [ ] Verify installer upgrade from B15 → B16 and uninstall.
- [ ] Validate deep inspection on physical TV/Shield and ATOTO firmware; confirm failed probes remain clearly labeled.
- [ ] Verify Device Status values against at least one Google TV and one manufacturer TV.
- [ ] Validate the [recovery hardware checklist](docs/RECOVERY-SIDELOAD.md#hardware-acceptance) with the exact Pixel C ROM/recovery build.
- [ ] Complete the [device support audit hardware acceptance checklist](docs/DEVICE-SUPPORT-AUDIT.md#hardware-acceptance-checklist).

## Beta 17 — next: device session and shell

This is the next code project after the B16 hotfix. The Unreleased Google TV hardware-family work is real-device correctness that landed before that split; it does not replace it.

- [ ] Extract remaining `MainWindowViewModel` page construction behind `IAdbDeviceSession` and typed navigation.
- [ ] Let pages subscribe to the shared session instead of `MainWindowViewModel` pushing `SelectedDevice` into each page VM.
- [ ] Add explicit device capabilities and user scope to the header after that context exists (`SHIELD Android TV · Network · User 0 · Android TV · Authorized`, with warning badges for secondary user, Automotive, offline, unauthorized, and unknown capability).

## After B17 — safety, quality, and release

- [ ] Add explicit Fully Reversible, Partially Reversible, and Not Reversible states to recommendation scoring.
- [ ] Add device restore points that capture package state, runtime roles, relevant settings, fingerprint, and the ruleset version before mutations.
- [ ] Add feature-aware debloat protection (Plex hosting, casting, voice, game streaming, accessibility) so those dependencies stay protected even under Aggressive cleanup.
- [ ] Add a small WPF smoke set: startup, navigation pages render, theme switch, device selection, tracker rebuild retains selection, network disconnect fallback, saved-device reconnect, close-to-tray/restore, settings persistence, and clean shutdown.
- [ ] Review accessibility names, keyboard navigation, visible focus, Narrator behavior, high-contrast, and 125–200% DPI.
- [ ] Strengthen CI with `dotnet list package --vulnerable --include-transitive`, analyzers, coverage for safety-sensitive projects, and CodeQL. Pin important GitHub Actions by commit SHA.
- [ ] Pin the Inno Setup Chocolatey version used by the release workflow.
- [ ] Add Authenticode signing for the application and installer when a release certificate is available, and have the updater verify the expected publisher/certificate.

## Later features

- [ ] Add touch/gesture remote support for tablets and compatible head units.
- [ ] Add QR-based Wireless Debugging pairing where platform support is reliable.
- [ ] Add Xiaomi, Yandex, Fire TV, and additional per-model reference baselines.
- [ ] Add a richer backup history browser and validation for more backup artifact types.
- [ ] Improve package icon extraction for more APK/resource formats.
- [ ] Add richer file browsing with explicit user-selected paths.
- [ ] Add representative ADB fixtures for more Android TV vendors and API levels.
- [ ] Keep release artifacts and runtime data out of source control.

## Already shipped

- [x] Add optional deep device inspection and full searchable/exportable command evidence (Beta 13).
- [x] Switch the active ADB target when a second device connects alongside an emulator (Beta 14).
- [x] Add guided Lineage Recovery file selection/sideload and guarded Pixel C recovery flashing (Beta 13).
- [x] Add reviewed Shield TV profile coverage and verified animation controls with journal recovery.
- [x] Correct connection acknowledgements and User 0 package capture/restore ownership.
- [x] Document tablet/head-unit support boundaries and Automotive safeguards.
- [x] Restore separate WPF page views that rendered blank because their XAML content was never initialized.
- [x] Cover tabs, tables, list views, default buttons, scrollbars, and the native window chrome with Dark, Pure Black, and White theme behavior.
- [x] Add regression checks for page-view initialization and shared native-control theme coverage.
- [x] Prevent non-owner `dumpsys device_policy` package lists from marking normal apps as device owners.
- [x] Build debloat previews from the selected device identity, not only from the last cached inspection snapshot.
- [x] Surface reference-profile origin, role, observed-on, dependency, and match-count evidence in debloat preview rows.
- [x] Let reviewed reference-profile risk/action metadata produce conservative debloat recommendations.
- [x] Lock Keep-side, Critical, and reference-protected packages consistently across debloat previews, execution, and Applications actions.
- [x] Correct copied Philips demo-package source attribution.
- [x] Recheck runtime-role protection immediately before executing a debloat plan.
- [x] Complete repository maintenance audit and harden privacy, backups, scripts, process lifecycle, and release validation.
- [x] Add source-attributed package knowledge with conservative vendor and device-family rules.
- [x] Add layered Reference Baseline Catalog with AOSP TV generations, Google TV, SoC, and OEM references.
- [x] Add read-only reference package dump export for device contributors.
- [x] Add baseline reference recommendation scoring without treating imported evidence as automatic Safe.
- [x] Add package-data backup warnings so package-state restoration is not presented as application-data restoration.
