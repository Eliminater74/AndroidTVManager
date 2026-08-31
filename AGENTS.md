# Android TV Manager Agent Rules

## Product boundaries

Android TV Manager is a Windows-first C# / .NET 10 / WPF application for Android TV, Google TV, Chromecast with Google TV, Google TV Streamer, ONN, Philips, NVIDIA Shield, and other ADB-capable entertainment devices.

Do not add Qt, Electron, React as the desktop shell, Java desktop UI, or Kodi-specific functionality. Do not copy or reverse engineer adbLink code.

## Architecture

- `src/AndroidTVManager.App` contains WPF views, view models, resources, navigation, tray integration, and startup.
- `src/AndroidTVManager.Core` contains platform-neutral models, parsers, and service contracts.
- `src/AndroidTVManager.Infrastructure` contains ADB processes, Platform-Tools management, SQLite, repositories, storage, and device tracking.
- `tests/AndroidTVManager.Tests` contains device-independent tests.

Use MVVM and dependency injection. ADB, SQLite, file, network, and package operations must never block the WPF UI thread. Use async APIs, cancellation tokens, parameterized SQL, proper disposal, and captured target serials for device operations.

## Safety

- Never commit `/TEMP/`, runtime databases, logs, generated output, APKs, or downloaded Platform-Tools.
- Never store or log Wireless Debugging pairing codes, credentials, or machine-specific paths.
- Launch `adb.exe` directly through the central process runner; do not use `cmd /c` or concatenate untrusted arguments.
- Prefer reversible actions and capture previous device state before changing it.
- Do not casually delete user files or runtime databases.

## Product conventions

The current version is `1.0.0-B7`. Keep the UI flashy but legible: dark navy/black surfaces, cyan and violet accents, clear sidebar navigation, restrained animation, strong focus states, and a useful About page. Preserve native WPF behavior and accessibility.

Before each commit, run the relevant restore/build/test commands, inspect the staged diff, and confirm that generated files, binaries, runtime data, and `/TEMP/` are not staged. Use conventional commit subjects.
