param(
    [string]$NotesFile = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "build\AssemblingManager.sln"
$artifactsDir = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifactsDir "publish"

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) {
        throw $message
    }
}

Write-Host "=========================================="
Write-Host " Assembling Manager - Release"
Write-Host "=========================================="

# --- 1. Version -------------------------------------------------------------
$version = (Get-Content (Join-Path $repoRoot "Version.txt") -Raw).Trim()
if (-not $version) {
    throw "Version.txt is empty."
}
$tag = "v$version"
Write-Host "[1/6] Release version: $version (tag $tag)"

# --- 2. Prerequisites -------------------------------------------------------
Write-Host "[2/6] Checking prerequisites..."
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) not found. Install it from https://cli.github.com/ and run 'gh auth login'."
}
gh auth status | Out-Null
Assert-LastExitCode "gh is not authenticated. Run 'gh auth login' first."

$isccCandidates = @(
    Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe",
    Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isinfo.php"
}
Write-Host "      gh CLI OK, ISCC found: $iscc"

# --- 3. Build all configurations --------------------------------------------
$configs = @(
    @{ Config = "Release.R21"; Year = "2021"; Tag = "R21" },
    @{ Config = "Release.R22"; Year = "2022"; Tag = "R22" },
    @{ Config = "Release.R23"; Year = "2023"; Tag = "R23" },
    @{ Config = "Release.R24"; Year = "2024"; Tag = "R24" },
    @{ Config = "Release.R25"; Year = "2025"; Tag = "R25" }
)

Write-Host "[3/6] Building all configurations..."
foreach ($item in $configs) {
    Write-Host "      Building $($item.Config)..."
    dotnet build $solution -c $item.Config --verbosity quiet
    Assert-LastExitCode "Build failed: $($item.Config)"
}

# --- 4. Collect artifacts ----------------------------------------------------
Write-Host "[4/6] Collecting artifacts..."
if (Test-Path $artifactsDir) {
    Remove-Item $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

foreach ($item in $configs) {
    $source = Join-Path $repoRoot "bin\$($item.Config)\$($item.Year)"
    $dest = Join-Path $publishDir "AssemblingManager-$($item.Tag)"
    if (-not (Test-Path (Join-Path $source "AssemblingManager.dll"))) {
        throw "Build output not found: $source"
    }
    New-Item -ItemType Directory -Path $dest | Out-Null
    Copy-Item (Join-Path $source "*") $dest -Recurse -Force
    Compress-Archive -Path (Join-Path $dest "*") -DestinationPath (Join-Path $artifactsDir "AssemblingManager-$($item.Tag).zip") -Force
    Write-Host "      Packed AssemblingManager-$($item.Tag).zip"
}

$updaterSource = Join-Path $repoRoot "bin\Release\Updater"
if (-not (Test-Path (Join-Path $updaterSource "AssemblingManager.Updater.exe"))) {
    throw "Updater build output not found: $updaterSource"
}
New-Item -ItemType Directory -Path (Join-Path $publishDir "Updater") | Out-Null
Copy-Item (Join-Path $updaterSource "*") (Join-Path $publishDir "Updater") -Recurse -Force

Write-Host "      Building installer with ISCC..."
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot "installer\AssemblingManager-Setup.iss") | Out-Null
Assert-LastExitCode "Installer compilation failed."
$setupExe = Join-Path $artifactsDir "AssemblingManager-$version-setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Installer not found after compilation: $setupExe"
}
Write-Host "      Packed AssemblingManager-$version-setup.exe"

# --- 5. Release notes ---------------------------------------------------------
Write-Host "[5/6] Preparing release notes..."
$notesPath = Join-Path $artifactsDir "release-notes.md"
if ($NotesFile -and (Test-Path $NotesFile)) {
    Copy-Item $NotesFile $notesPath -Force
} elseif (Test-Path (Join-Path $PSScriptRoot "release-notes.md")) {
    Copy-Item (Join-Path $PSScriptRoot "release-notes.md") $notesPath -Force
} else {
    $lastTag = git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -ne 0) {
        $logRange = @("HEAD")
    } else {
        $logRange = @("$lastTag..HEAD")
    }
    $commits = git log --pretty=format:"- %s" @logRange
    $notes = "# Assembling Manager $version`n`n" + ($commits -join "`n") + "`n"
    [System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($true))
}
Write-Host "      Notes: $notesPath"

# --- 6. Publish ----------------------------------------------------------------
Write-Host "[6/6] Publishing..."
$assets = @(
    (Join-Path $artifactsDir "AssemblingManager-R21.zip"),
    (Join-Path $artifactsDir "AssemblingManager-R22.zip"),
    (Join-Path $artifactsDir "AssemblingManager-R23.zip"),
    (Join-Path $artifactsDir "AssemblingManager-R24.zip"),
    (Join-Path $artifactsDir "AssemblingManager-R25.zip"),
    $setupExe
)

if ($DryRun) {
    Write-Host "      DRY RUN: skipping 'gh release create'. Files that would be uploaded:"
    foreach ($asset in $assets) {
        Write-Host "        $asset"
    }
    Write-Host "      Notes file: $notesPath"
    Write-Host "=========================================="
    Write-Host " DRY RUN completed successfully"
    Write-Host "=========================================="
    exit 0
}

gh release create $tag $assets --title "Assembling Manager $version" --notes-file $notesPath
Assert-LastExitCode "gh release create failed."

Write-Host ""
Write-Host "=========================================="
Write-Host " Release $tag published successfully!"
Write-Host "=========================================="
