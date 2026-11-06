<#
.SYNOPSIS
    Build script for the Maho toolchain (miryo, mahoc, Maho) for Windows and cross-platform PowerShell.

.DESCRIPTION
    Compiles the Maho toolchain into the dist/ directory following the ~/.maho layout.
    Supports host platform builds, single-file packaging, debug builds, and multi-platform compilation.

.PARAMETER All
    Compiles for all major platforms (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64).

.PARAMETER Platform
    Compiles for a specific platform target (e.g. linux-arm64, win-x64, osx-arm64).

.PARAMETER SingleFile
    Packages the toolchain for distribution using shared runtime in dist/runtime.

.PARAMETER Debug
    Builds with Debug configuration and emits PDB symbol files.

.PARAMETER Install
    Installs the built toolchain directly into ~/.maho.

.EXAMPLE
    .\build-maho.ps1
    Builds the host platform toolchain into dist\ (fast incremental test build).

.EXAMPLE
    .\build-maho.ps1 -SingleFile
    Builds standalone distribution package into dist\.

.EXAMPLE
    .\build-maho.ps1 -Platform win-x64
    Builds the toolchain for Windows x64 into dist\win-x64\.

.EXAMPLE
    .\build-maho.ps1 -Install
    Builds and installs into ~/.maho.
#>

[CmdletBinding()]
param(
    [Alias("a")]
    [switch]$All,

    [Alias("p")]
    [string]$Platform,

    [Alias("s", "Single")]
    [switch]$SingleFile,

    [Alias("d")]
    [switch]$Debug,

    [Alias("i")]
    [switch]$Install,

    [Alias("h")]
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$CliProject = Join-Path $RepoRoot "src\MahoCli\MahoCli.csproj"
$ServerProject = Join-Path $RepoRoot "src\MahoServer\MahoServer.csproj"
$ServerHostProject = Join-Path $RepoRoot "src\MahoServerHost\MahoServerHost.csproj"
$MiryoProject = Join-Path $RepoRoot "src\Miryo\Miryo.csproj"
$MiryoUpdateProject = Join-Path $RepoRoot "src\MiryoUpdate\MiryoUpdate.csproj"
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

function Publish-Target([string]$rid, [string]$outDir) {
    $config = if ($Debug) { "Debug" } else { "Release" }
    $toolVersion = (Select-String -Path $CliProject -Pattern "<Version>(.*?)</Version>").Matches.Groups[1].Value.Trim()
    if (-not $toolVersion) { $toolVersion = "0.2.0" }
    $miryoVersion = (Select-String -Path $MiryoProject -Pattern "<Version>(.*?)</Version>").Matches.Groups[1].Value.Trim()
    if (-not $miryoVersion) { $miryoVersion = "1.0.0" }
    $baseArgs = @()

    if ($Debug) {
        $baseArgs += "-p:DebugType=portable"
        $baseArgs += "-p:GenerateDependencyFile=true"
    }
    else {
        $baseArgs += "-p:DebugType=None"
        $baseArgs += "-p:GenerateDependencyFile=false"
    }

    $binArgs = @($baseArgs)
    $toolsArgs = @($baseArgs)

    if ($SingleFile) {
        $binArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true", "-p:AppHostRelativeDotNet=../runtime")
        $toolsArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true", "-p:AppHostRelativeDotNet=../../runtime")
    }
    elseif ($rid) {
        $binArgs += @("-r", $rid, "--no-self-contained")
        $toolsArgs += @("-r", $rid, "--no-self-contained")
    }

    $binDir = Join-Path $outDir "bin"
    $toolsDir = Join-Path $outDir "tools\$toolVersion"
    $shimsDir = Join-Path $outDir "shims"
    $compilerDir = Join-Path $outDir "compiler\$toolVersion"
    $runtimeDir = Join-Path $outDir "runtime"

    foreach ($d in @($binDir, $toolsDir, $shimsDir, $compilerDir)) {
        if (-not (Test-Path $d)) {
            New-Item -ItemType Directory -Path $d -Force | Out-Null
        }
    }

    # 1. Publish bin tools: miryo and miryo-update
    dotnet publish "$MiryoProject" -c $config -o "$binDir" @binArgs --nologo -v q
    dotnet publish "$MiryoUpdateProject" -c $config -o "$binDir" @binArgs --nologo -v q
    $binManifest = @"
{
  "version": "$miryoVersion",
  "miryo": "miryo",
  "miryoUpdate": "miryo-update",
  "channel": "stable"
}
"@
    Set-Content -Path (Join-Path $binDir "manifest.json") -Value $binManifest

    # 2. Publish versioned tools: mahoc and maho-server
    dotnet publish "$CliProject" -c $config -o "$toolsDir" @toolsArgs --nologo -v q
    dotnet publish "$ServerHostProject" -c $config -o "$toolsDir" @toolsArgs --nologo -v q

    if ($rid -like "win-*") {
        Set-Content -Path (Join-Path $toolsDir "maho-ls.cmd") -Value "@echo maho-ls language server is not yet implemented.`r`n@exit /b 1"
    }
    else {
        Set-Content -Path (Join-Path $toolsDir "maho-ls") -Value "#!/usr/bin/env sh`necho `"maho-ls language server is not yet implemented.`" >&2`nexit 1"
    }

    $toolManifest = @"
{
  "version": "$toolVersion",
  "targetCompilerVersion": "$toolVersion",
  "tools": {
    "mahoc": "mahoc",
    "maho-server": "maho-server",
    "maho-ls": "maho-ls"
  }
}
"@
    Set-Content -Path (Join-Path $toolsDir "manifest.json") -Value $toolManifest

    # 3. Publish compiler library: Maho.dll
    dotnet publish "$MahoProject" -c $config -o "$compilerDir" @baseArgs --nologo -v q
    $compilerManifest = @"
{
  "version": "$toolVersion",
  "assembly": "Maho.dll"
}
"@
    Set-Content -Path (Join-Path $compilerDir "manifest.json") -Value $compilerManifest

    # 4. If in distribution mode (-s), deploy shared runtime to $runtimeDir
    if ($SingleFile) {
        Write-Host "    -> Deploying shared runtime into $runtimeDir..."
        $sharedDir = Join-Path $runtimeDir "shared"
        if (-not (Test-Path $sharedDir)) { New-Item -ItemType Directory -Path $sharedDir -Force | Out-Null }

        $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
        if (-not $dotnetPath) {
            if (Test-Path "/home/luserus/Runtimes/dotnet/dotnet") {
                $dotnetPath = "/home/luserus/Runtimes/dotnet/dotnet"
            }
        }
        if ($dotnetPath) {
            $dotnetDir = Split-Path $dotnetPath -Parent
            $hostSrc = Join-Path $dotnetDir "host"
            $sharedAppSrc = Join-Path $dotnetDir "shared\Microsoft.NETCore.App"
            if (Test-Path $hostSrc) {
                Copy-Item -Path $hostSrc -Destination $runtimeDir -Recurse -Force
            }
            if (Test-Path $sharedAppSrc) {
                Copy-Item -Path $sharedAppSrc -Destination $sharedDir -Recurse -Force
            }
            $dotnetExe = Join-Path $dotnetDir "dotnet.exe"
            $dotnetBin = Join-Path $dotnetDir "dotnet"
            if (Test-Path $dotnetExe) {
                Copy-Item -Path $dotnetExe -Destination $runtimeDir -Force
            }
            elseif (Test-Path $dotnetBin) {
                Copy-Item -Path $dotnetBin -Destination $runtimeDir -Force
            }
        }
    }

    # 5. Publish server component: MahoServer.dll into server\
    $serverDir = Join-Path $outDir "server"
    if (-not (Test-Path $serverDir)) {
        New-Item -ItemType Directory -Path $serverDir -Force | Out-Null
    }
    dotnet publish "$ServerProject" -c $config -o "$serverDir" @baseArgs --nologo -v q
    $rootManifest = @"
{
  "activeCompilerVersion": "$toolVersion",
  "installedTools": ["$toolVersion"],
  "installedCompilers": ["$toolVersion"]
}
"@
    Set-Content -Path (Join-Path $serverDir "manifest.json") -Value $rootManifest

    # 6. Generate shims
    if ($rid -like "win-*") {
        Set-Content -Path (Join-Path $shimsDir "mahoc.cmd") -Value "@`"%~dp0..\tools\$toolVersion\mahoc.exe`" %*"
        Set-Content -Path (Join-Path $shimsDir "maho-server.cmd") -Value "@`"%~dp0..\tools\$toolVersion\maho-server.exe`" %*"
        Set-Content -Path (Join-Path $shimsDir "maho-ls.cmd") -Value "@`"%~dp0..\tools\$toolVersion\maho-ls.cmd`" %*"
    }
    else {
        Set-Content -Path (Join-Path $shimsDir "mahoc") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$toolVersion/mahoc`" `"`$@`""
        Set-Content -Path (Join-Path $shimsDir "maho-server") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$toolVersion/maho-server`" `"`$@`""
        Set-Content -Path (Join-Path $shimsDir "maho-ls") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$toolVersion/maho-ls`" `"`$@`""
    }

    # 7. Generate miryo.config inside bin/ folder alongside miryo
    $configContent = @"
# Miryo Toolchain Configuration

[tools]
mahoc = ["../tools/$toolVersion/mahoc", "../shims/mahoc", "mahoc"]
mahoc_server = ["../tools/$toolVersion/maho-server", "../shims/maho-server", "maho-server"]
mahoc_compiler = ["../compiler/$toolVersion"]

[templates]
bin_template = "EntryFile : `"src/Program.mh`";\nImplicitTopLevel : true;\n"
lib_template = "EntryFile : `"src/Lib.mh`";\nImplicitTopLevel : false;\n"
bin_source_path = "src/Program.mh"
bin_source = "println(`"Hello from Maho!`");\n"
lib_source_path = "src/Lib.mh"
lib_source = "public struct Lib;\n"
gitignore = "target/\nbin/\nobj/\ndist/\n"
"@
    Set-Content -Path (Join-Path $binDir "miryo.config") -Value $configContent
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
        Publish-Target $rid $platformDir
    }

    Write-Host "  -> Publishing for host platform into $DistDir..."
    Publish-Target $hostRid $DistDir
}
elseif ($Platform) {
    $normRid = Normalize-Rid $Platform
    Write-Host "==> Building Maho toolchain for platform '$normRid'..." -ForegroundColor Cyan
    $targetDir = Join-Path $DistDir $normRid
    Publish-Target $normRid $targetDir
}
else {
    Write-Host "==> Building Maho toolchain for host platform ($hostRid)..." -ForegroundColor Cyan
    Publish-Target $hostRid $DistDir
}

if ($Install) {
    $targetHome = if ($env:MAHO_HOME) { $env:MAHO_HOME } else { Join-Path $HOME ".maho" }
    Write-Host "==> Installing Maho toolchain to $targetHome..." -ForegroundColor Green
    Copy-Item -Recurse -Force (Join-Path $DistDir "*") $targetHome
    Write-Host "Installation complete! Add '$targetHome\bin' and '$targetHome\shims' to your PATH." -ForegroundColor Green
}
