# Device support audit — 2026-09-07

This audit adds Shield TV debloat coverage, a Tweaks page, and fixes in connection reporting and package recovery. Product version remains **1.0.0-B11**. All changes are local commits; nothing was pushed or released.

This is a source, automated-test and WPF-render audit, not a hardware certification. No ADB commands were sent to a physical device, and no installed packages or device settings were changed during validation. Firmware-specific behavior still requires the hardware checks below.

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
| Applications / Scripts / Deployment Profiles | Debloat safety is not a universal policy for every mutation path. Applications uses its selected assessment; arbitrary scripts and deployment steps do not share all live debloat checks. Add a common live package-policy service before claiming fleet-wide or vehicle-safe cleanup. |
| User profiles | Debloat and journaled package state now consistently target User 0. Other commands and detail parsing still have single-user assumptions. A complete user selector must carry the user ID through inventory, roles, actions, history, restore and UI; merely changing a command to `--user current` is insufficient. |
| Restore | Package reinstallation is not a guarantee of restoring app data. Scripts are sequential, not atomic; a later failure can leave earlier actions applied. Review the journal and undo. |
| Device identity | Shield matching excludes editable aliases. Some older generic reference families still permit manufacturer fallback and friendly-name matching. Audit these before introducing more consequential profiles. |
| Deployment compatibility | ABI and TV-feature requirements can currently produce warnings rather than full capability validation. Add live feature/ABI checks and block incompatible mandatory steps. |
| Backups | Existing APK/split/shared-storage/report support remains. Modern app-private data and complete device images are not generally provided by ordinary ADB. |
| Remote | Existing D-pad/media/text controls suit Shield. Touch coordinates, gestures, rotation and multi-display targeting would materially improve tablet/head-unit usefulness. |
| Display and media | Existing HDMI/HDR/CEC evidence, codecs and network diagnostics are reusable on Shield. OEM dumps can be incomplete; returned commands are not proof of real playback quality or negotiated output. |
| Platform-Tools activation | Download uses Google's official endpoint and validates the staged executable's version. The failure path does not explicitly restore the previous tools directory after every possible activation failure. Add fault-injection coverage and rollback. |
| Updates | Installer SHA-256 validation exists. Signing, release-asset delivery and actual installation were not exercised. |
| Database / privacy | Parameterized repository SQL and existing redaction tests retained. No production database was inspected. Full diagnostic exports and advanced shell output can contain sensitive device information; these are not a universal redaction guarantee. |
| Dependencies | NuGet vulnerability scan including transitive packages reported none from the configured feeds on this date. This is not proof of absence of all security defects. |
| Shutdown | Async `OnExit` and logger disposal deserve a separate process-exit test; static inspection alone cannot prove journal/log flush completion. |

## Most useful next improvements

1. **Capability and user summary in the header.** Show TV/tablet/Automotive evidence, transport, authorization, foreground user and unsupported operations. Separate detected facts from saved labels.
2. **Feature-based cleanup choices.** Let users declare “I use Plex hosting / casting / voice / game streaming / accessibility,” then protect the corresponding dependencies even in Aggressive mode.
3. **One package-policy service for every structured action.** Recheck live roles, user scope, build identity and reviewed dependencies for Applications, Scripts and Deployment Profiles, not just Debloat.
4. **Touch remote with screenshot mapping.** Tap/swipe on a current screenshot, account for rotation and display ID, and refresh coordinates after resolution changes. Keep TV D-pad controls available.
5. **Connection doctor with device-specific instructions.** Add mDNS discovery, explain unauthorized/offline states and detect likely pairing-versus-debug-port mistakes. Avoid automatic changes to network security.
6. **Before/after health comparisons.** Compare measured free space, memory, thermal readings and frame/transport evidence. Label unavailable sensors and avoid invented performance percentages.
7. **Reviewed device contributions.** Accept redacted inventory snapshots with exact model/build, tested feature impacts and recovery results. Keep suggested rules separate from hardware-validated rules.
8. **Recoverable operation queue.** Show captured target, user, expected changes and available recovery before execution. Support cancellation, partial completion and resumable failed undo.

These are design recommendations, not features represented as already implemented.

## Validation

- Clean starting checkout; baseline Release suite: **116/116**.
- Final Debug and Release restore/build: **zero warnings/errors**; tests **149/149** in each configuration.
- Added meaningful coverage for Shield profile selection and exclusion, failed connection acknowledgements, User 0 queries, Automotive/role-read blocks, both debloat entry gates, animation verification/recovery, exact package state and a WPF dispatcher render.
- The Tweaks page was rendered offscreen and visually inspected for layout, wrapping and legibility. This is not full interactive app testing or high-DPI/accessibility certification.
- `dotnet list AndroidTVManager.sln package --vulnerable --include-transitive`: no known vulnerable packages reported.
- Before each commit: restore/build/test, staged-diff inspection and whitespace checks. No generated files, binaries, runtime data or `/TEMP/` staged.
- No version bump, push, tag, installer packaging, deployment or physical-device mutation.

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
