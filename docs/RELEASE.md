# Release process

Android TV Manager releases are built on Windows by GitHub Actions. A release tag is the source of the public version name.

## Version rules

1. Update the `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, and `<InformationalVersion>` values in `src/AndroidTVManager.App/AndroidTVManager.App.csproj`.
2. Add a matching section to `CHANGELOG.md`.
3. Make sure the version is not already tagged.
4. Use a tag with the `v` prefix, for example `v1.0.0-B3`.

The tag version without `v` must match the project `<Version>` value. Beta tags such as `v1.0.0-B3` automatically create GitHub pre-releases.

## Local validation

Install the .NET 10 SDK and Inno Setup 6, then run:

```powershell
dotnet restore
dotnet build AndroidTVManager.sln -c Debug
dotnet test AndroidTVManager.sln -c Debug
dotnet build AndroidTVManager.sln -c Release
dotnet test AndroidTVManager.sln -c Release
.\scripts\package-release.ps1 -Version 1.0.0-B3 -RequireInstaller
```

The script writes only generated output under `artifacts\`, which is ignored by Git:

- `AndroidTVManager-{version}-Setup.exe`
- `AndroidTVManager-Setup.exe` (stable installer filename for README download links)
- `AndroidTVManager-{version}-win-x64.zip`
- `SHA256SUMS.txt`

## Publishing

After committing and validating the release:

```powershell
git push origin main
git tag -a v1.0.0-B3 -m "Release Android TV Manager 1.0.0-B3"
git push origin v1.0.0-B3
```

Pushing the tag starts `.github/workflows/release.yml`. The workflow restores, builds, tests, publishes the self-contained application, installs Inno Setup, creates the installer and portable ZIP, generates SHA-256 checksums, and creates the GitHub release with all generated assets.

The same workflow can be rerun from GitHub Actions with `workflow_dispatch` by supplying an existing version tag. It will not create a second release with the same tag.

## Release checklist

- [ ] Project and tag versions match.
- [ ] `CHANGELOG.md` contains the release notes.
- [ ] Debug and Release builds pass.
- [ ] Debug and Release tests pass.
- [ ] Installer and portable ZIP are produced.
- [ ] SHA-256 checksums are attached.
- [ ] No database, logs, Platform-Tools, APKs, or generated output are committed.
- [ ] GitHub Actions completes successfully.
- [ ] The release page contains the expected assets and pre-release status.
