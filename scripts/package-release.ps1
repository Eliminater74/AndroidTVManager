[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "AndroidTVManager.sln"
$project = Join-Path $repoRoot "src\AndroidTVManager.App\AndroidTVManager.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "A release version is required."
}

$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\release"
$portableZip = Join-Path $releaseDir "AndroidTVManager-$Version-$Runtime.zip"

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDir, $releaseDir -Force | Out-Null

Write-Host "Restoring solution..."
dotnet restore $solution --runtime $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host "Building $Configuration..."
dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Write-Host "Running tests..."
dotnet test $solution --configuration $Configuration --no-build --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

Write-Host "Publishing self-contained $Runtime application..."
dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Creating portable ZIP..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZip -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $portableZip)) {
    throw "Portable ZIP was not created."
}

$isccCandidates = @(
    $env:ISCC_PATH,
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($null -eq $iscc) {
    if ($RequireInstaller) {
        throw "Inno Setup compiler (ISCC.exe) was not found."
    }
    Write-Warning "Inno Setup was not found; portable ZIP was created but no installer was built."
}
else {
    Write-Host "Building installer with $iscc..."
    & $iscc `
        "/DAppVersion=$Version" `
        "/DPublishDir=$publishDir" `
        (Join-Path $repoRoot "installer\AndroidTVManager.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
}

$releaseFiles = Get-ChildItem -LiteralPath $releaseDir -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object Name
if ($releaseFiles.Count -eq 0) {
    throw "No release artifacts were created."
}
$checksums = $releaseFiles | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
[System.IO.File]::WriteAllLines(
    (Join-Path $releaseDir "SHA256SUMS.txt"),
    [string[]]$checksums,
    [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Release artifacts:"
Get-ChildItem -LiteralPath $releaseDir -File | Select-Object Name, Length
