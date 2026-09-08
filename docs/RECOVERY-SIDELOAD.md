# Recovery / Sideload

Status: included in Beta 13. Automated tests use fake transports; no physical device has been flashed, wiped or updated during development.

## Update using Lineage Recovery

1. Install managed Platform-Tools in Settings. Connect the tablet by USB with the appropriate Windows ADB driver. Open **Recovery / Sideload**, refresh and select its serial explicitly.
2. Choose **Choose ZIP and sideload…** and select your device's ROM ZIP. Review the filename, target and SHA-256; compare the hash with your build publisher's checksum. Structural checks do not prove authenticity or device compatibility.
3. Confirm the operation. If Android is running, the app requests recovery reboot. It waits up to five minutes for the same serial to enter sideload mode.
4. On the tablet, choose **Apply Update → Apply from ADB**. The app sends the ZIP when it sees that mode. Any required clean-install wipe must be completed on the tablet first, following the instructions for your exact build.
5. Read recovery's installation result. ADB completion is not proof of successful installation; a transfer error near 47% is not automatically a failed installation either. Select another ZIP for required add-ons, re-enter Apply from ADB, then reboot from recovery after checking the result.

The tool does not assume an ordinary update needs a wipe, erase system, format every partition, unlock the bootloader or automatically reboot after sending a ZIP. The requested setup uses **Lineage Recovery**, not TWRP; its exact Lineage version/build is still unconfirmed.

## Optional Pixel C recovery preparation

Use the recovery image supplied for your exact Pixel C build. Choose the IMG, review its hash, and use **Reboot to bootloader** while Android/recovery exposes ADB. Refresh and explicitly select the Fastboot target; a changed serial is never selected automatically.

**Flash Pixel C recovery** replaces the recovery partition after confirmation. It requires live Fastboot product `dragon`, an already-unlocked bootloader and a reported recovery partition large enough for the Android-format image. Missing/ambiguous information blocks flashing. The command is limited to `fastboot -s <selected-serial> flash recovery <selected-image>`; no erase/unlock commands are issued.

After flashing, enter Recovery using the Pixel C bootloader menu, then refresh. Flash interruption can leave recovery unusable; the UI disables its Stop button during flashing. A process timeout or application/device failure can still interrupt it. This is not a firmware backup or rollback facility.

## Targeting, files and limitations

- ADB recovery, sideload and Fastboot targets have a separate explicit picker from normal device management. Every mutation rechecks the selected serial; operations never fall back to the only remaining device.
- Mode changes that expose a different serial require refresh and explicit selection. The wait is five minutes; transfer timeout is thirty minutes.
- IMG headers and ZIP update markers are checked, SHA-256 is recomputed before mutation and the file remains open without write sharing during transfer. Files are not copied into application storage or persisted in settings.
- Stop cancels the PC wait/transfer, not installation already running on the tablet. Inspect recovery before retrying.
- Status reports phases, not byte-level progress. Device screen confirmation remains authoritative. Device-specific recovery flashing is limited to Pixel C; other ADB-capable devices may use ZIP sideload only with their own compatible recovery/build instructions.

## Hardware acceptance

- Confirm the exact ROM/recovery publisher, build and checksums before testing. Validate Windows drivers separately in Android, Fastboot and recovery modes.
- With a second device connected, prove selection remains on the intended serial across transitions and disconnects.
- Verify regular update sideload, add-on sideload, recovery error reporting and canceled waiting without unnecessary data wipe.
- Verify Pixel C getvar responses and partition size before explicitly authorizing a real recovery flash; confirm the resulting recovery boots using the device menu.
- Test any clean-install procedure separately using that build's instructions and backed-up data. No universal wipe recipe is supplied.

## References

- [Android ADB documentation](https://developer.android.com/tools/adb) documents device selection and host commands.
- [Historical LineageOS Pixel C install guide](https://lineageos.github.io/lineage_wiki/devices/dragon/install/) documents the Pixel C recovery partition and bootloader-menu transition. It targets an old LineageOS release, **not LineageOS 22**; its recovery/wipe recipe is not adopted here.

Useful next additions are optional publisher-checksum comparison, live transfer output, and reviewed device/build recipes that explain every step before execution.
