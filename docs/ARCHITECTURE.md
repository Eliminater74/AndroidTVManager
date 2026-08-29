# Architecture

## Boundaries

The solution uses a small four-project structure. Core has no WPF dependency and defines the models, parsers, and contracts used by the application. Infrastructure owns operating-system and persistence concerns. App is the WPF composition root, views, view models, navigation, theme, dialogs, and tray behavior. Tests exercise parsers, scripts, and persistence without requiring a physical device.

## UI and MVVM

The shell is a single WPF window with a left navigation rail and a status bar. Pages are view models selected by navigation. `CommunityToolkit.Mvvm` provides observable state and commands. View models call interfaces, not `Process`, SQL, or filesystem APIs directly. The dark dashboard style uses native WPF resources with cyan/violet accent colors and high-contrast state tiles.

## ADB process architecture

`IAdbProcessRunner` is the only path to `adb.exe`. It uses `ProcessStartInfo.ArgumentList`, redirected output, cancellation, timeouts, and a structured result. Pairing codes are passed in memory and redacted from diagnostics. Command-specific services capture the target serial before starting work.

## Device tracker

`IAdbDeviceTracker` owns a long-lived `adb track-devices -l` process. It parses streamed snapshots, deduplicates unchanged state, backs off after unexpected exits, and publishes device changes. Metadata enrichment is cached and performed asynchronously.

## Platform-Tools

The tools manager stores official Google Platform-Tools under LocalAppData, downloads into a staging directory, validates `adb.exe`, parses `adb version`, and activates only a successful installation. The repository never contains the downloaded binaries.

## Database

SQLite is stored in `%LOCALAPPDATA%\AndroidTVManager\Data`. Migrations are explicit and transactional, foreign keys are enabled, and WAL mode is used for normal operation. Devices, sessions, connection events, pairing history, settings, scripts, executions, actions, and snapshots are represented in the schema. Repositories keep SQL out of view models.

## History and transactions

Device arrival and connection transitions create historical records without writing duplicate unchanged events. Script executions are transaction records. Actions retain previous state, requested state, result, and undo status so undo can reverse only the changes made by that execution.

## Runtime folders

All mutable files use LocalAppData: `Data`, `Logs`, `Tools\PlatformTools`, `Scripts`, `Snapshots`, `Screenshots`, `Recordings`, and `Temp`. The repository's `/TEMP/` directory is unrelated and remains untouched.

## Tray behavior

The WPF application uses a hosted WinForms `NotifyIcon` for a small dependency footprint. It can minimize or close to the tray, restores on double-click, exposes Open, Settings, Restart ADB Server, and Exit, and disposes the icon during real shutdown. A named mutex prevents accidental duplicate instances.
