# Contributing

Thank you for contributing to Android TV Manager.

## Development requirements

- Windows 10 or later
- .NET 10 SDK
- An Android SDK Platform-Tools installation is optional for unit tests and is downloaded by the application when needed
- Inno Setup 6 is required only for local installer packaging

## Before opening a change

Run the relevant checks:

```powershell
dotnet restore
dotnet build AndroidTVManager.sln -c Debug
dotnet test AndroidTVManager.sln -c Debug
dotnet build AndroidTVManager.sln -c Release
dotnet test AndroidTVManager.sln -c Release
```

Keep ADB, SQLite, file, network, and package operations asynchronous. Use the central ADB process runner, captured target serials, cancellation tokens, parameterized SQL, and typed models.

## Safety expectations

- Do not commit runtime databases, logs, APKs, Platform-Tools, generated output, or `/TEMP/`.
- Never persist Wireless Debugging pairing codes or credentials.
- Do not add inspection commands that mutate device state.
- Destructive device actions require explicit confirmation and should capture prior state where possible.
- Do not label unknown device or package evidence as safe.

## Style and commits

Use the existing MVVM and dependency-injection architecture. Keep changes focused and use conventional commit subjects such as `feat:`, `fix:`, `test:`, `docs:`, or `chore:`.

Pull requests should explain the user-visible behavior, tests run, and any hardware validation performed. Do not claim physical-device testing unless it actually happened.
