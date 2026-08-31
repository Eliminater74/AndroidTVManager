[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidatePattern("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    [string]$Runtime = "win-x64",
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$solution = Join-Path $repoRoot "AndroidTVManager.sln"
$project = Join-Path $repoRoot "src\AndroidTVManager.App\AndroidTVManager.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)

function Resolve-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent)
    if (-not $fullParent.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullParent += [System.IO.Path]::DirectorySeparatorChar
    }
    if (-not $fullPath.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate on path outside '$Parent': $fullPath"
    }
    return $fullPath
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "A release version is required."
}

$projectVersion = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
$informationalVersion = [string]($projectXml.Project.PropertyGroup.InformationalVersion | Select-Object -First 1)
if ($Version -ne $projectVersion -or $Version -ne $informationalVersion) {
    throw "Release version '$Version' does not match project metadata ($projectVersion / $informationalVersion)."
}

$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$publishDir = Resolve-ChildPath -Path (Join-Path $publishRoot $Runtime) -Parent $publishRoot
$releaseDir = Resolve-ChildPath -Path (Join-Path $artifactsRoot "release") -Parent $artifactsRoot
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
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    if (-not ($zip.Entries | Where-Object { $_.FullName -eq "AndroidTVManager.exe" })) {
        throw "Portable ZIP does not contain AndroidTVManager.exe."
    }
}
finally {
    $zip.Dispose()
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
    $versionedInstaller = Get-ChildItem -LiteralPath $releaseDir -Filter "*-Setup.exe" -File |
        Select-Object -First 1
    if ($null -eq $versionedInstaller) {
        throw "Inno Setup completed without producing an installer."
    }
    Copy-Item -LiteralPath $versionedInstaller.FullName `
        -Destination (Join-Path $releaseDir "AndroidTVManager-Setup.exe") `
        -Force
}

$publishedExecutable = Join-Path $publishDir "AndroidTVManager.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published application executable was not created."
}
$embeddedVersion = (Get-Item -LiteralPath $publishedExecutable).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($embeddedVersion) -or -not $embeddedVersion.StartsWith($Version, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Published executable version '$embeddedVersion' does not match release version '$Version'."
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

foreach ($line in $checksums) {
    $fields = $line -split "\s+", 2
    $actual = (Get-FileHash -LiteralPath (Join-Path $releaseDir $fields[1]) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $fields[0]) {
        throw "Checksum validation failed for $($fields[1])."
    }
}

Write-Host ""
Write-Host "Release artifacts:"
Get-ChildItem -LiteralPath $releaseDir -File | Select-Object Name, Length
