<#
.SYNOPSIS
    Build script for the Maho compiler (mahoc) for Windows and cross-platform PowerShell.

.DESCRIPTION
    Compiles the Maho compiler CLI into the dist/ directory.
    Supports host platform builds, single-file packaging, debug builds, and multi-platform compilation.

.PARAMETER All
    Compiles for all major platforms (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64).

.PARAMETER Platform
    Compiles for a specific platform target (e.g. linux-arm64, win-x64, osx-arm64).

.PARAMETER SingleFile
    Packages the compiler into a fast, single-file binary (framework-dependent).

.PARAMETER SelfContained
    Packages the compiler into a standalone self-contained, single-file, trimmed executable.

.PARAMETER Debug
    Builds with Debug configuration and emits PDB symbol files.

.EXAMPLE
    .\build-maho.ps1
    Builds the host platform compiler into dist\mahoc.exe.

.EXAMPLE
    .\build-maho.ps1 -Platform win-x64
    Builds the compiler for Windows x64 into dist\win-x64\mahoc.exe.

.EXAMPLE
    .\build-maho.ps1 -SingleFile
    Builds a single-file executable into dist\mahoc.exe.

.EXAMPLE
    .\build-maho.ps1 -All
    Builds the compiler for all major platforms into dist\<platform>\.
#>

[CmdletBinding()]
param(
    [Alias("a")]
    [switch]$All,

    [Alias("p")]
    [string]$Platform,

    [Alias("sc", "SelfContained")]
    [switch]$SelfContained,

    [Alias("s", "Single")]
    [switch]$SingleFile,

    [Alias("d")]
    [switch]$Debug,

    [Alias("h")]
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$CliProject = Join-Path $RepoRoot "src\MahoCli\MahoCli.csproj"
$DistDir = Join-Path $RepoRoot "dist"

if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path
    exit 0
}

function Get-HostRid {
    $isArm = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64
    $arch = if ($isArm) { "arm64" } else { "x64" }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-$arch"
    }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return "osx-$arch"
    }
    else {
        return "linux-$arch"
    }
}

function Find-Binary([string]$dir) {
    $exePath = Join-Path $dir "mahoc.exe"
    $unixPath = Join-Path $dir "mahoc"

    if (Test-Path $exePath) { return $exePath }
    if (Test-Path $unixPath) { return $unixPath }
    return $exePath
}

$config = if ($Debug) { "Debug" } else { "Release" }
$debugProps = if ($Debug) {
    @("-p:DebugType=portable", "-p:GenerateDependencyFile=true")
} else {
    @("-p:DebugType=None", "-p:GenerateDependencyFile=false")
}

function Publish-Target([string]$rid, [string]$outDir) {
    $extraArgs = @($debugProps)

    if ($SelfContained) {
        $extraArgs += @("-r", $rid, "--self-contained", "-p:PublishSingleFile=true", "-p:PublishTrimmed=true")
    } elseif ($SingleFile) {
        $extraArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true")
    } elseif (-not [string]::IsNullOrEmpty($rid)) {
        $extraArgs += @("-r", $rid, "--no-self-contained")
    }

    dotnet publish "$CliProject" -c $config -o "$outDir" @extraArgs --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

function Normalize-Rid([string]$inputRid) {
    switch ($inputRid) {
        { $_ -in "win", "windows" } { return "win-x64" }
        { $_ -in "windows-x64", "win64" } { return "win-x64" }
        "windows-arm64" { return "win-arm64" }
        "linux" { return "linux-x64" }
        "linux-aarch64" { return "linux-arm64" }
        { $_ -in "osx", "mac", "macos" } { return "osx-x64" }
        { $_ -in "macos-x64", "mac-x64" } { return "osx-x64" }
        { $_ -in "macos-arm64", "mac-arm64" } { return "osx-arm64" }
        default { return $inputRid }
    }
}

$hostRid = Get-HostRid

if ($All -and $Platform) {
    throw "Cannot specify both -All and -Platform"
}

if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

if ($All) {
    $platforms = @("linux-x64", "linux-arm64", "win-x64", "osx-x64", "osx-arm64")

    Write-Host "==> Building Maho compiler (mahoc) for all major platforms..." -ForegroundColor Cyan

    foreach ($rid in $platforms) {
        Write-Host "  -> Publishing for $rid..."
        $platformDir = Join-Path $DistDir $rid
        if (-not (Test-Path $platformDir)) {
            New-Item -ItemType Directory -Path $platformDir -Force | Out-Null
        }
        Publish-Target -rid $rid -outDir $platformDir
    }

    Write-Host "  -> Publishing for host platform into $DistDir..."
    if ($SingleFile -or $SelfContained) {
        Publish-Target -rid $hostRid -outDir $DistDir
    } else {
        dotnet publish "$CliProject" -c $config -o "$DistDir" @debugProps --nologo -v q
    }

    $hostBin = Find-Binary $DistDir

    Write-Host ""
    Write-Host "Maho compiler (mahoc) successfully built for all major platforms:" -ForegroundColor Green
    Write-Host "  Host:         $hostBin"
    Write-Host "  linux-x64:    $(Find-Binary (Join-Path $DistDir 'linux-x64'))"
    Write-Host "  linux-arm64:  $(Find-Binary (Join-Path $DistDir 'linux-arm64'))"
    Write-Host "  win-x64:      $(Find-Binary (Join-Path $DistDir 'win-x64'))"
    Write-Host "  osx-x64:      $(Find-Binary (Join-Path $DistDir 'osx-x64'))"
    Write-Host "  osx-arm64:    $(Find-Binary (Join-Path $DistDir 'osx-arm64'))"
    Write-Host ""
    Write-Host "Execute host compiler via:"
    Write-Host "  $hostBin [options] [source-path]"
} elseif ($Platform) {
    $targetPlatform = Normalize-Rid $Platform
    $targetDir = Join-Path $DistDir $targetPlatform
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Write-Host "==> Building Maho compiler (mahoc) for platform '$targetPlatform'..." -ForegroundColor Cyan
    Publish-Target -rid $targetPlatform -outDir $targetDir

    $binPath = Find-Binary $targetDir

    Write-Host ""
    Write-Host "Maho compiler (mahoc) successfully built for $targetPlatform:" -ForegroundColor Green
    Write-Host "  $binPath"
    Write-Host ""
    if ($targetPlatform -eq $hostRid) {
        Write-Host "Execute compiler via:"
        Write-Host "  $binPath [options] [source-path]"
    }
} else {
    Write-Host "==> Building Maho compiler (mahoc) for host platform ($hostRid)..." -ForegroundColor Cyan

    if ($SingleFile -or $SelfContained) {
        Publish-Target -rid $hostRid -outDir $DistDir
    } else {
        dotnet publish "$CliProject" -c $config -o "$DistDir" @debugProps --nologo -v q
    }

    $binPath = Find-Binary $DistDir

    Write-Host ""
    Write-Host "Maho compiler (mahoc) successfully built:" -ForegroundColor Green
    Write-Host "  $binPath"
    Write-Host ""
    Write-Host "Execute compiler via:"
    Write-Host "  $binPath [options] [source-path]"
}
