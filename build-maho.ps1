<#
.SYNOPSIS
    Build script for the Maho toolchain (miryo, mahoc, Maho) for Windows and cross-platform PowerShell.

.DESCRIPTION
    Compiles the Maho toolchain into the dist/ directory.
    Supports host platform builds, single-file packaging, debug builds, and multi-platform compilation.

.PARAMETER All
    Compiles for all major platforms (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64).

.PARAMETER Platform
    Compiles for a specific platform target (e.g. linux-arm64, win-x64, osx-arm64).

.PARAMETER SingleFile
    Packages the toolchain into fast, single-file decoupled binaries (framework-dependent).

.PARAMETER SelfContained
    Packages the toolchain into standalone self-contained, single-file, trimmed executables.

.PARAMETER Debug
    Builds with Debug configuration and emits PDB symbol files.

.EXAMPLE
    .\build-maho.ps1
    Builds the host platform toolchain into dist\.

.EXAMPLE
    .\build-maho.ps1 -Platform win-x64
    Builds the toolchain for Windows x64 into dist\win-x64\.

.EXAMPLE
    .\build-maho.ps1 -SingleFile
    Builds single-file executables into dist\.

.EXAMPLE
    .\build-maho.ps1 -All
    Builds the toolchain for all major platforms into dist\<platform>\.
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
$MiryoProject = Join-Path $RepoRoot "src\Miryo\Miryo.csproj"
$MahoProject = Join-Path $RepoRoot "src\Maho\Maho.csproj"
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

function Find-Binary([string]$dir, [string]$name = "mahoc") {
    $exePath = Join-Path $dir "$name.exe"
    $unixPath = Join-Path $dir $name

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

    # 1. Publish mahoc
    dotnet publish "$CliProject" -c $config -o "$outDir" @extraArgs --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish mahoc failed with exit code $LASTEXITCODE"
    }

    # 2. Publish miryo
    dotnet publish "$MiryoProject" -c $config -o "$outDir" @extraArgs --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish miryo failed with exit code $LASTEXITCODE"
    }

    # 3. Publish Maho library
    dotnet publish "$MahoProject" -c $config -o "$outDir" @debugProps --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish Maho failed with exit code $LASTEXITCODE"
    }

    # 4. Generate miryo.config
    $configContent = @"
# Miryo Toolchain Configuration

[tools]
mahoc = ["./mahoc", "mahoc"]
il2llvmir = ["./il2llvmir", "il2llvmir"]
llvm_opt = ["opt", "/usr/bin/opt"]
llvm_clang = ["clang", "/usr/bin/clang"]

[templates]
bin_template = "EntryFile : `"src/Program.mh`";\nImplicitTopLevel : true;\n"
lib_template = "EntryFile : `"src/Lib.mh`";\nImplicitTopLevel : false;\n"
bin_source_path = "src/Program.mh"
bin_source = "println(`"Hello from Maho!`");\n"
lib_source_path = "src/Lib.mh"
lib_source = "public struct Lib;\n"
gitignore = "target/\nbin/\nobj/\ndist/\n"
"@
    Set-Content -Path (Join-Path $outDir "miryo.config") -Value $configContent
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

    Write-Host "==> Building Maho toolchain for all major platforms..." -ForegroundColor Cyan

    foreach ($rid in $platforms) {
        Write-Host "  -> Publishing for $rid..."
        $platformDir = Join-Path $DistDir $rid
        if (-not (Test-Path $platformDir)) {
            New-Item -ItemType Directory -Path $platformDir -Force | Out-Null
        }
        Publish-Target -rid $rid -outDir $platformDir
    }

    Write-Host "  -> Publishing for host platform into $DistDir..."
    Publish-Target -rid $hostRid -outDir $DistDir

    $hostMahoc = Find-Binary $DistDir "mahoc"
    $hostMiryo = Find-Binary $DistDir "miryo"

    Write-Host ""
    Write-Host "Maho toolchain successfully built for all major platforms:" -ForegroundColor Green
    Write-Host "  Host Compiler (mahoc): $hostMahoc"
    Write-Host "  Host Project (miryo):  $hostMiryo"
    Write-Host ""
    Write-Host "Execute host toolchain via:"
    Write-Host "  $hostMiryo [command] [options]"
    Write-Host "  $hostMahoc [options] [source-paths...]"
} elseif ($Platform) {
    $targetPlatform = Normalize-Rid $Platform
    $targetDir = Join-Path $DistDir $targetPlatform
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Write-Host "==> Building Maho toolchain for platform '$targetPlatform'..." -ForegroundColor Cyan
    Publish-Target -rid $targetPlatform -outDir $targetDir

    $mahocBin = Find-Binary $targetDir "mahoc"
    $miryoBin = Find-Binary $targetDir "miryo"

    Write-Host ""
    Write-Host "Maho toolchain successfully built for $targetPlatform:" -ForegroundColor Green
    Write-Host "  mahoc: $mahocBin"
    Write-Host "  miryo: $miryoBin"
    Write-Host ""
    if ($targetPlatform -eq $hostRid) {
        Write-Host "Execute toolchain via:"
        Write-Host "  $miryoBin [command] [options]"
        Write-Host "  $mahocBin [options] [source-paths...]"
    }
} else {
    Write-Host "==> Building Maho toolchain for host platform ($hostRid)..." -ForegroundColor Cyan

    Publish-Target -rid $hostRid -outDir $DistDir

    $mahocBin = Find-Binary $DistDir "mahoc"
    $miryoBin = Find-Binary $DistDir "miryo"

    Write-Host ""
    Write-Host "Maho toolchain successfully built:" -ForegroundColor Green
    Write-Host "  mahoc:         $mahocBin"
    Write-Host "  miryo:         $miryoBin"
    Write-Host "  Core Library:  $(Join-Path $DistDir 'Maho.dll')"
    Write-Host "  Configuration: $(Join-Path $DistDir 'miryo.config')"
    Write-Host ""
    Write-Host "Execute toolchain via:"
    Write-Host "  $miryoBin [command] [options]"
    Write-Host "  $mahocBin [options] [source-paths...]"
}
