# Device support audit — 2026-09-07

This audit adds Shield TV debloat coverage, a Tweaks page, and fixes in connection reporting and package recovery. At audit completion, product version remained **1.0.0-B11** and changes were local only. These changes are now included in the authorized **1.0.0-B12** release; the original audit evidence and hardware limitations below remain applicable.

This is a source, automated-test and WPF-render audit, not a hardware certification. No ADB commands were sent to a physical device, and no installed packages or device settings were changed during validation. Firmware-specific behavior still requires the hardware checks below.

## Follow-up status — 1.0.0-B15

Several original findings are closed in Beta 15 and should not be treated as open work:

| Original finding | Status |
|---|---|
| Platform-Tools activation does not restore the previous tools directory after a failed swap | **Closed.** Failed activation restores the previous install and verifies `adb version` plus `fastboot.exe` before discarding the backup. |
| Debloat safety is not a universal policy for Applications, Scripts, and Deployment Profiles | **Closed.** `IPackageSafetyGate` is a required `PackageManager` dependency. Applications, Scripts, and Deployment pass the prepared build fingerprint so live User 0 inventory, Automotive/user evidence, roles, Keep/Critical locks, and fingerprint drift are re-checked immediately before a mutation. |
| Deployment ABI/TV-feature requirements can warn instead of blocking | **Closed.** Unknown required evidence is no longer a bypassable warning; incompatible mandatory steps fail closed. |
| Shutdown flush of the tracker, sessions, and logger is unproven from static inspection | **Closed in code.** `ApplicationShutdownCoordinator` stops the tracker, recovers open sessions, and flushes the file logger. Unknown UI exceptions are logged and then terminate the process. Physical exit still needs a B15 smoke check. |
| APK restore does not verify the expected on-disk set | **Closed.** Restore compares `SHA256SUMS.txt` plus the backup catalog, installs nothing on mismatch, and copy is not presented as app-data restore. |
| Updater can leave untrusted installers in Temp | **Closed.** Missing/mismatched checksums and payloads over 200 MB are deleted. |
| Sideload can treat missing live product evidence as compatible | **Closed.** ZIP `pre-device` / updater-script names are compared to live `ro.product.device` and fail closed when unverified. |
| SQLite schema upgrades have no pre-migration snapshot | **Closed.** `.pre-migrate.bak` copies are kept (two newest), WAL/SHM sidecars are pruned, and `PRAGMA integrity_check` must return `ok`. |
| Network/Wireless devices cannot be disconnected from the TARGET header | **Closed.** Network and Wireless Debugging targets disconnect from the header and Devices list without deleting saved devices. USB stays attached. |

Beta 15 also introduces `IAdbDeviceSession` for the live list, TARGET selection, sticky preferred endpoint, and the shared disconnect workflow. Pages still receive the selected device from `MainWindowViewModel`; finishing that subscribe model is Beta 16, not more B15 feature work.

Physical-device certification remains open. Automated tests and WPF rendering are not a substitute for the [hardware acceptance checklist](#hardware-acceptance-checklist).

## Follow-up status — unreleased after 1.0.0-B16

Parser and Debloat correctness from a real Shield (`darcy`) dump. These items are closed in code with fixtures; they do not complete hardware acceptance.

| Original finding | Status |
|---|---|
| Device Status swap used/free, display fps=/HDR type 2, and Vulkan GLES-extension false positive | **Closed in code.** Shield meminfo, `dumpsys display` modes, and `pm list features` Vulkan parsing are fixture-covered. Re-check values on the live Shield. |
| Compound `sh -c` probes (`which gsi_tool && …`) fail on the device | **Closed in code.** Inspection uses separate `id`, `which su`, `gsi_tool status`, and discrete CPU frequency files. |
| Stock non-root, missing `gsi_tool`, HDMI/CEC, and missing `media.drm` looked like Partial/failure | **Closed in code.** Optional missing tools do not Partial Security/Root/GSI; HDMI/CEC is Supported when feature + `hdmi_control` agree; missing DRM is Unavailable. |
| Debloat Shield matching and post-execute package state | **Closed in code.** Profile matches NVIDIA + SHIELD Android TV or `darcy`/`foster` product/device, never FriendlyName. Disable readback fails if the package stays enabled. Debloat reloads the preview after execute/restore. Simple may select 0 packages. |

Do not mark the [hardware acceptance checklist](#hardware-acceptance-checklist) complete until Device Status and a Debloat preview are confirmed on the physical Shield.

## Support matrix

| Device | Connection | Cleanup and tweaks | Limits |
|---|---|---|---|
| NVIDIA Shield TV | Existing USB/network ADB transport, when available on that model | Dedicated 17-entry Shield TV reference profile, nine added rules, generic Android rules, verified animation controls and Shield settings guidance | Package presence, accessory behavior and media features need firmware-specific testing |
| Android tablet | USB debugging or Wireless Debugging where exposed | User 0 inventory, existing reviewed package rules, animation controls | No tablet-specific debloat catalog; secondary/work-profile management is not implemented |
| Standalone Android car/truck head unit | USB or network ADB only if the manufacturer exposes and authorizes it | Generic inspection; User 0 cleanup only where existing reviewed rules apply | No MCU/CAN, camera, radio, climate, steering-control or vehicle package validation; unknown vendor packages are not automatic candidates |
| Android Automotive OS | Only when OEM ADB access is available | Generic read-only inspection; guided debloat and global animation tuning blocked | Driver users can differ from system User 0; no vehicle-service management support |
| Android Auto / CarPlay projection screen | Projection alone does not make the screen an ADB device | Not a standalone management target | Connecting an Android phone manages that phone, not the vehicle's projection display |

ADB connectivity is independent of TV branding: `AdbConnectionService` sends ordinary connect/pair requests and `AdbDeviceTracker` parses ADB devices without a TV-only filter. Device authorization, Windows USB drivers, cables, network isolation and OEM firmware restrictions still determine whether a real device connects.

## Implemented sections

### Shield debloat

The prior catalog contained eight NVIDIA Keep rules but no dedicated Shield reference profile. The new profile exposes those protections alongside the new reviewed packages in Debloat's loaded and matched profile lists.

| Packages | Policy |
|---|---|
| `com.nvidia.stats`, `com.nvidia.diagtools`, `com.nvidia.feedback` | Caution; disable candidates in Medium/Aggressive, not Simple |
| `com.nvidia.tegrazone3`, `com.plexapp.mediaserver.smb` | HighRisk; Aggressive candidates with gaming/hosting feature-loss notes |
| `com.nvidia.osc`, `com.nvidia.shieldtech.hooks`, `com.nvidia.nvgamecast` | Keep; insufficient evidence to recommend removal |
| `com.nvidia.shieldtech.accessoryhost` | Critical/Keep |
| Existing platform, audio, package management, launcher, remote, update and pairing entries | Keep protections retained |

The profile requires reported NVIDIA TV identity or a recognized Shield TV device codename. A friendly name cannot enable it; Shield tablets and unrelated manufacturers are excluded. Only packages returned by inventory appear as candidates. Existing active-role, Keep and already-disabled protections continue to apply. Community evidence is not labeled Safe or hardware-verified.

### Connections and inventory

- Connect/pair now require an acknowledgement in ADB output. A failure message with exit code zero no longer becomes a success or a saved successful connection.
- Connections explains USB debugging, separate Wireless Debugging pairing/connection ports, Shield network debugging and the vehicle/projection distinction.
- Package lists, HOME resolution, input methods and accessibility queries explicitly read User 0, matching debloat's mutation target.
- Preview and execution reject missing inventory evidence, unknown/nonzero foreground users and Android Automotive. A failed preview clears the previous plan.
- Preview captures its device before waiting for settings initialization.

### Tweaks and recovery

- New native WPF Tweaks navigation page adjusts window, transition and animator scales together: off, half-duration or normal duration.
- Reads feature/user context and all three values before applying; rejects unreadable settings, Automotive, unsupported foreground users and arbitrary input values.
- Uses the existing script journal, requires prior-state capture, verifies setting writes and reports verification failures without claiming successful tuning.
- Undo restores the recorded values, including deleting settings that were previously absent. Restore readback is checked. Partial undo can retry only failed actions.
- Device target is captured; undo requires the original serial. Controls are disabled without a connected target or while an operation is running. Scripts history retains recovery across application sessions.
- Shield guidance covers upscaling, frame-rate matching, CEC/audio, processor/fan choices and package feature impacts. Vendor controls remain device-menu guidance because a universal supported ADB control was not established.

Animation timing changes UI transitions; it is not an FPS, decoder, network or CPU performance improvement. Changes affect Android global settings across users.

### Package journal repair

An existing substring comparison could mistake a similarly prefixed disabled package for the requested package, recording the wrong undo state. Capture now uses parsed exact package names. Installation state is read from User 0's installed list instead of an arbitrary user's `dumpsys` flag. Script enable/restore and their undo commands, plus Applications enable, now explicitly target User 0.

## Broader audit findings and remaining work

| Area reviewed | Finding / next action |
|---|---|
| Architecture and responsiveness | WPF/MVVM, DI and service boundaries retained. Tracker result reads occur after `Task.WhenAll`. This was not a profiler-based responsiveness audit of every page. |
| ADB execution | Device commands use the central runner with argument lists, timeouts and cancellation. Tracker and Platform-Tools version probing own specialized process paths. Raw shell remains an explicitly advanced feature. |
| Applications / Scripts / Deployment Profiles | Closed in B15. Debloat safety is no longer Debloat-only: `IPackageSafetyGate` is required on `PackageManager`, and Applications/Scripts/Deployment pass the live build fingerprint. A complete user selector (inventory, roles, history, restore, UI) is still missing. |
| User profiles | Debloat and journaled package state now consistently target User 0. Other commands and detail parsing still have single-user assumptions. A complete user selector must carry the user ID through inventory, roles, actions, history, restore and UI; merely changing a command to `--user current` is insufficient. |
| Restore | Package reinstallation is not a guarantee of restoring app data. Scripts are sequential, not atomic; a later failure can leave earlier actions applied. Review the journal and undo. B15 restore confirmation copy states that APK copy does not restore app data, accounts, or settings. |
| Device identity | Shield matching excludes editable aliases. Some older generic reference families still permit manufacturer fallback and friendly-name matching. Audit these before introducing more consequential profiles. |
| Deployment compatibility | Closed in B15. Mandatory ABI and Android TV/Google TV requirements fail closed when live evidence is missing or incompatible. |
| Backups | Existing APK/split/shared-storage/report support remains. Modern app-private data and complete device images are not generally provided by ordinary ADB. B15 APK restore verifies the expected SHA-256 set and catalog. |
| Remote | Existing D-pad/media/text controls suit Shield. Touch coordinates, gestures, rotation and multi-display targeting would materially improve tablet/head-unit usefulness. |
| Display and media | Existing HDMI/HDR/CEC evidence, codecs and network diagnostics are reusable on Shield. OEM dumps can be incomplete; returned commands are not proof of real playback quality or negotiated output. |
| Platform-Tools activation | Closed in B15. Failed activation restores the previous tools directory and verifies the active `adb`/`fastboot` before discarding the backup. |
| Updates | Installer SHA-256 validation exists, oversized or untrusted downloads are deleted, and signing/publisher checks are still open before stable 1.0. |
| Database / privacy | Parameterized repository SQL and shared redaction remain. No production database was inspected. Full diagnostic exports and advanced shell output can contain sensitive device information; these are not a universal redaction guarantee. Schema upgrades now snapshot `.pre-migrate.bak` files. |
| Dependencies | NuGet vulnerability scan including transitive packages reported none from the configured feeds on this date. This is not proof of absence of all security defects. CI still does not run that scan on every build. |
| Shutdown | Closed in code in B15 (`ApplicationShutdownCoordinator`). Confirm tracker stop, session recovery, and log flush on a real application exit during hardware smoke. |

## Remaining work after B15

Do not add another large subsystem to Beta 15. Smoke the hardening release on hardware, then continue in this order:

1. **B16 shell architecture.** Pages should subscribe to `IAdbDeviceSession` instead of `MainWindowViewModel` pushing `SelectedDevice` into twenty page VMs. Typed navigation belongs with that split. Capability badges come immediately after the shared session exists.
2. **Capability and user summary in the header.** Show facts such as `SHIELD Android TV · Network · User 0 · Android TV · Authorized`, with warning badges for `Secondary user`, `Automotive`, `Offline`, `Unauthorized`, and `Unknown capability`. Separate detected facts from saved labels.
3. **Restore points and reversibility.** Tell the user whether an operation is Fully reversible, Partially reversible, or Not reversible, and capture package state, runtime roles, relevant settings, fingerprint, and ruleset before a batch mutation.
4. **Feature-based cleanup choices.** Let users declare “I use Plex hosting / casting / voice / game streaming / accessibility,” then protect the corresponding dependencies even in Aggressive mode.
5. **Targeted WPF quality pass.** Automate a small smoke set (startup, navigation render, theme switch, device selection, tracker rebuild, disconnect fallback, saved-device reconnect, close-to-tray, settings persistence, clean shutdown). Add visible keyboard focus, useful `AutomationProperties`, keyboard-only navigation, Narrator checks, and 125–200% DPI testing.
6. **CI before stable.** Add `dotnet list package --vulnerable --include-transitive`, analyzers, coverage for safety-sensitive projects, and CodeQL. Pin important GitHub Actions by commit SHA.
7. **Release pipeline.** Pin the Inno Setup Chocolatey version. Before stable 1.0, Authenticode-sign the application and installer, and have the updater verify the expected publisher/certificate. SHA-256 proves the download matches the release; signing is a separate authenticity layer.
8. **Hardware acceptance matrix.** Physical Shield/TV deep inspection, USB Android TV, TCP/IP ADB, Wireless Debugging, Device Status values, package inventory, debloat restore, Pixel C recovery, and installer upgrade/uninstall.
9. **Later product expansion.** `.atmprofile` export/import, richer backup history, better APK icons, richer file browsing, QR pairing, touch/gesture remote, LAN discovery, scrcpy, and multi-device operations. Touch remote is high once the shell is cleaner.

These are design recommendations, not features represented as already implemented. Item 3 from the original B12 list (one package-policy service) shipped in B15.

## Validation

- Clean starting checkout; baseline Release suite: **116/116**.
- Final Debug and Release restore/build: **zero warnings/errors**; tests **149/149** in each configuration.
- Added meaningful coverage for Shield profile selection and exclusion, failed connection acknowledgements, User 0 queries, Automotive/role-read blocks, both debloat entry gates, animation verification/recovery, exact package state and a WPF dispatcher render.
- The Tweaks page was rendered offscreen and visually inspected for layout, wrapping and legibility. This is not full interactive app testing or high-DPI/accessibility certification.
- `dotnet list AndroidTVManager.sln package --vulnerable --include-transitive`: no known vulnerable packages reported.
- Before each commit: restore/build/test, staged-diff inspection and whitespace checks. No generated files, binaries, runtime data or `/TEMP/` staged.
- The audit itself performed no version bump, push, tag, installer packaging, deployment or physical-device mutation. Subsequent B12 release preparation is documented in the changelog.

## Hardware acceptance checklist

Use a recoverable device. For vehicle hardware, work while parked and follow the manufacturer's service procedure.

1. Verify USB/network connection, authorization prompt, reconnect after sleep, and the captured serial. For Wireless Debugging, test distinct pairing and connection ports.
2. Export a read-only reference dump with exact model, firmware and package inventory. Confirm Shield profile activation and protected remote/audio/update packages.
3. Disable one optional package at a time. Test HOME, remote/controller pairing, voice, casting, actual streaming playback, audio formats and any Plex clients. Restore and confirm behavior.
4. Read animation scales, apply one setting choice, verify visible behavior and restore original values. Interrupt a test connection and confirm truthful failure/recovery.
5. On a tablet, test foreground User 0 and a secondary profile; confirm the latter blocks guided debloat. Test APK/split compatibility and USB drivers separately.
6. On a head unit, establish whether it is standalone Android, Automotive or projection. Confirm vendor ADB access and automotive detection; do not treat a successful connection as permission to remove vehicle packages.

## Sources checked

- [Android ADB connection and wireless debugging documentation](https://developer.android.com/tools/adb)
- [Android for Cars platform overview](https://developer.android.com/training/cars)
- [Android Automotive multi-user support](https://source.android.com/docs/automotive/users_accounts/multi_user)
- [Android Settings.Global API](https://developer.android.com/reference/android/provider/Settings.Global)
- [NVIDIA AI upscaling](https://www.nvidia.com/en-us/shield/support/shield-tv/ai-upscaling/)
- [NVIDIA Match Frame Rate](https://www.nvidia.com/en-us/shield/support/shield-tv/match-frame-rate/)
- [NVIDIA performance settings guidance](https://support-shield.nvidia.com/shield-tv-pro-user-guide/How_to_Optimize_Internet_and_Video_Performance.htm)
- [Shield Optimizer package observations](https://github.com/bryanroscoe/shield_optimizer) — community evidence, not hardware validation by this project
- [Shield 2017 package dump](https://gist.github.com/roblav96/340991668988cba1591ce4bad3fad66e) — historical inventory evidence, not proof of safe removal
