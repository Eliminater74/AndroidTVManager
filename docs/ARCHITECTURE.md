# Architecture

## Boundaries

The solution uses a small four-project structure. Core has no WPF dependency and defines the models, parsers, and contracts used by the application. Infrastructure owns operating-system and persistence concerns. App is the WPF composition root, views, view models, navigation, theme, dialogs, and tray behavior. Tests exercise parsers, scripts, and persistence without requiring a physical device.

## UI and MVVM

The shell is a single WPF window with a left navigation rail and a status bar. Pages are view models selected by navigation. `CommunityToolkit.Mvvm` provides observable state and commands. View models call interfaces, not `Process`, SQL, or filesystem APIs directly. The dark dashboard style uses native WPF resources with cyan/violet accent colors and high-contrast state tiles.

## ADB process architecture

`IAdbProcessRunner` is the only path to `adb.exe`. It uses `ProcessStartInfo.ArgumentList`, redirected output, cancellation, timeouts, and a structured result. Pairing codes and local payload paths are redacted from diagnostics. Timed-out clients are killed without `entireProcessTree`, so the shared ADB server stays up. Command-specific services capture the target serial before starting work.

## Device tracker

`IAdbDeviceTracker` owns a long-lived `adb track-devices -l` process. It parses streamed snapshots, deduplicates unchanged state, backs off after unexpected exits, and publishes device changes. Metadata enrichment is cached and performed asynchronously.

## Platform-Tools

The tools manager stores official Google Platform-Tools under LocalAppData, downloads into a staging directory, validates `adb.exe` and `fastboot.exe`, then swaps the active folder transactionally. `adb version` is run from the active directory after the swap. If verification fails, the previous installation is restored. The repository never contains the downloaded binaries.

## Database

SQLite is stored in `%LOCALAPPDATA%\AndroidTVManager\Data`. Migrations are explicit and transactional, foreign keys are enabled, and WAL mode is used for normal operation. Before a schema upgrade the current file is copied to a `.pre-migrate.bak` (two newest retained) and `PRAGMA integrity_check` must return `ok`. Devices, sessions, connection events, pairing history, settings, scripts, executions, actions, and snapshots are represented in the schema. Repositories keep SQL out of view models.

## Backups

APK restore verifies the expected `apks/` set in `SHA256SUMS.txt` against the files on disk, including missing, extra, and hash-mismatched files. Verification failure installs nothing and reports zero restored packages. The operation copies APK files only; it does not restore app data.

In-app updates download the GitHub `-Setup.exe` into LocalAppData Temp, require a SHA-256 from the release digest or `SHA256SUMS.txt`, reject payloads over 200 MB, and delete the file unless the verified installer is actually started.

## Recovery sideload

Sideload ZIP inspection reads `META-INF/com/android/metadata` (`pre-device`) and `updater-script` `ro.product.device` checks. Declared targets are compared to live `getprop ro.product.device` before the package is sent. A mismatch or unreadable live identity fails closed. A ZIP with no device declaration is Unknown, not Compatible.

## History and transactions

Device arrival and connection transitions create historical records without writing duplicate unchanged events. Script executions are transaction records. Actions retain previous state, requested state, result, and undo status so undo can reverse only the changes made by that execution.

## Runtime folders

All mutable files use LocalAppData: `Data`, `Logs`, `Tools\PlatformTools`, `Scripts`, `Snapshots`, `Screenshots`, `Recordings`, and `Temp`. The repository's `/TEMP/` directory is unrelated and remains untouched.

## Tray behavior

The WPF application uses a hosted WinForms `NotifyIcon` for a small dependency footprint. It can minimize or close to the tray, restores on double-click, exposes Open, Settings, Restart ADB Server, and Exit, and disposes the icon during real shutdown. A named mutex prevents accidental duplicate instances.
