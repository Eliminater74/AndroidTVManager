# Deep device inspection

Status: included in Beta 13. No physical device was queried during implementation.

Connect an authorized ADB device, open **Device Status**, select it and choose **Deep scan**. Standard automatic inspection remains available; deep scanning is explicit because it runs additional diagnostics. Up to four commands run concurrently with a 20-second host timeout each. Cancel stops the scan, and changing targets invalidates previous results and progress.

## Coverage

Standard inspection already includes Android properties and identity/build/security evidence, CPU/memory, graphics/display, storage, network, Bluetooth, HDMI/CEC, DRM, battery/thermal/runtime state, features, package summaries and running services.

Deep scan adds 32 fixed read-only commands:

| Area | Additional evidence |
|---|---|
| Kernel / CPU | Kernel/version, online cores, exposed frequency policy readings and governors |
| Storage / memory | Partition listing, mounted filesystems, swap, storage volumes/disks, process memory summary |
| Input / peripherals | Kernel input devices, Android input state, USB, sensors and camera service diagnostics |
| Media | AudioFlinger, audio policy, codec service and TV input diagnostics |
| Power | Power manager and device-idle diagnostics |
| Network | Connectivity, Ethernet and Wi-Fi status |
| Users / packages | Android users, foreground user, package paths/UIDs/version codes and shared libraries |
| Vendor / services | Available dumpsys services, Binder services and exposed hardware services |

The exact command catalog is `src/AndroidTVManager.Core/Models/DeepInspectionCatalog.cs`. Returned service names/properties are evidence only; they never become executable commands. The scan does not root, reboot, flash, modify settings, install software or send vehicle-control commands.

## Reading and exporting results

The coverage summary counts commands with usable output. Permission denial, missing/unsupported commands, timeouts, failures and empty successful responses remain visible. A completed command does not prove every field is present or that physical hardware works.

Expand **RAW ADB EVIDENCE (EXPERT)**. Search categories, command names or full output; select a command to read/copy its complete scrollable stdout and stderr. The previous 120-pixel clipped output has been replaced. Search terms such as `ATOTO`, `MCU`, `CAN`, `codec`, `USB` or a property name can locate relevant evidence.

**Export report** writes a JSON snapshot containing structured results, raw properties, commands, states, timings, capture time, target serial and whether deep scanning was requested. The normal local snapshot cache also retains results. Reports contain device identifiers and may contain network names, app names and other sensitive diagnostic details; review before sharing. They are not automatically uploaded or universally redacted.

## What cannot be promised

This is a broad inventory of information exposed to the current ADB shell, not literally everything inside the device. Vendor firmware and Android versions differ. Missing services do not necessarily mean missing hardware. A codec service dump is not a certified list of working playback formats.

ATOTO and other head units may expose model/build/MCU hints in Android properties or service names. These remain reported evidence, not proof of MCU model, CAN protocol compatibility or access to vehicle controllers. Hidden MCU firmware, private app data, passwords, DRM keys and a complete firmware backup are not provided by this scan. No automated root escalation or unrestricted filesystem crawl is attempted.

## Verification

Device-independent tests cover deep versus standard scope, exact target routing, vendor property retention, empty/denied/unsupported outputs, timeout/cancellation states, report serialization and stale-result suppression across device changes. Physical-device checks remain required for actual firmware coverage and performance.

Android documents service discovery and selective diagnostics in [dumpsys](https://developer.android.com/tools/dumpsys), and target selection in [ADB](https://developer.android.com/tools/adb). Individual commands and available output vary by build.
