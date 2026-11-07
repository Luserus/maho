<#
.SYNOPSIS
    Build script for the Maho toolchain (miryo, mahoc, Maho) for Windows and cross-platform PowerShell.

.DESCRIPTION
    Compiles the Maho toolchain into the bin/ directory with separate maho/ and miryo/ layouts.
    Supports host platform builds, single-file packaging, debug builds, and multi-platform compilation.

.PARAMETER All
    Compiles for all major platforms (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64).

.PARAMETER Platform
    Compiles for a specific platform target (e.g. linux-arm64, win-x64, osx-arm64).

.PARAMETER SingleFile
    Packages the toolchain for distribution using shared runtime in maho/runtime and miryo/runtime.

.PARAMETER Debug
    Builds with Debug configuration and emits PDB symbol files.

.PARAMETER Install
    Installs the built toolchain directly into ~/.maho.

.EXAMPLE
    .\build-maho.ps1
    Builds the host platform toolchain into bin\ (fast incremental test build).

.EXAMPLE
    .\build-maho.ps1 -SingleFile
    Builds standalone distribution package into bin\.

.EXAMPLE
    .\build-maho.ps1 -Platform win-x64
    Builds the toolchain for Windows x64 into bin\win-x64\.

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
$ServerManagerProject = Join-Path $RepoRoot "src\MahoServerManager\MahoServerManager.csproj"
$MiryoProject = Join-Path $RepoRoot "src\Miryo\Miryo.csproj"
$MiryoUpdateProject = Join-Path $RepoRoot "src\MiryoUpdate\MiryoUpdate.csproj"
$MahoProject = Join-Path $RepoRoot "src\Maho\Maho.csproj"
$BinDir = Join-Path $RepoRoot "bin"

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
    $layoutVersion = ($toolVersion -split "\.")[0..1] -join "."
    if (-not $layoutVersion) { $layoutVersion = "0.2" }
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
    $serverMgrArgs = @($baseArgs)

    if ($SingleFile) {
        $binArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true", "-p:AppHostRelativeDotNet=runtime")
        $toolsArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true", "-p:AppHostRelativeDotNet=../../runtime")
        $serverMgrArgs += @("-r", $rid, "--no-self-contained", "-p:PublishSingleFile=true", "-p:AppHostRelativeDotNet=../runtime")
    }
    elseif ($rid) {
        $binArgs += @("-r", $rid, "--no-self-contained")
        $toolsArgs += @("-r", $rid, "--no-self-contained")
        $serverMgrArgs += @("-r", $rid, "--no-self-contained")
    }

    $mahoDir = Join-Path $outDir "maho"
    $miryoDir = Join-Path $outDir "miryo"
    $toolsDir = Join-Path $mahoDir "tools\$layoutVersion"
    $shimsDir = Join-Path $mahoDir "shims"
    $compilerDir = Join-Path $mahoDir "compiler\$layoutVersion"
    $serverDir = Join-Path $mahoDir "server"
    $mahoRuntimeDir = Join-Path $mahoDir "runtime"
    $miryoRuntimeDir = Join-Path $miryoDir "runtime"

    foreach ($d in @($miryoDir, $toolsDir, $shimsDir, $compilerDir, $serverDir)) {
        if (-not (Test-Path $d)) {
            New-Item -ItemType Directory -Path $d -Force | Out-Null
        }
    }

    # 1. Publish miryo tools: miryo and miryo-update into miryo/
    dotnet publish "$MiryoProject" -c $config -o "$miryoDir" @binArgs --nologo -v q
    dotnet publish "$MiryoUpdateProject" -c $config -o "$miryoDir" @binArgs --nologo -v q
    $binManifest = @"
{
  "version": "$miryoVersion",
  "miryo": "miryo",
  "miryoUpdate": "miryo-update",
  "channel": "stable"
}
"@
    Set-Content -Path (Join-Path $miryoDir "manifest.json") -Value $binManifest

    # 2. Publish versioned tools: mahoc and maho-server into maho/tools/{version}
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
  "targetCompilerVersion": "$layoutVersion",
  "tools": {
    "mahoc": "mahoc",
    "maho-server": "maho-server",
    "maho-ls": "maho-ls"
  }
}
"@
    Set-Content -Path (Join-Path $toolsDir "manifest.json") -Value $toolManifest

    # 3. Publish compiler library: Maho.dll into maho/compiler/{version}
    dotnet publish "$MahoProject" -c $config -o "$compilerDir" @baseArgs --nologo -v q
    $compilerManifest = @"
{
  "version": "$toolVersion",
  "assembly": "Maho.dll"
}
"@
    Set-Content -Path (Join-Path $compilerDir "manifest.json") -Value $compilerManifest

    # 4. If in distribution mode (-s), deploy shared runtime to both runtime directories
    if ($SingleFile) {
        Write-Host "    -> Deploying shared runtime into $mahoRuntimeDir and $miryoRuntimeDir..."
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
            $dotnetExe = Join-Path $dotnetDir "dotnet.exe"
            $dotnetBin = Join-Path $dotnetDir "dotnet"

            foreach ($rdir in @($mahoRuntimeDir, $miryoRuntimeDir)) {
                $sharedDir = Join-Path $rdir "shared"
                if (-not (Test-Path $sharedDir)) { New-Item -ItemType Directory -Path $sharedDir -Force | Out-Null }
                if (Test-Path $hostSrc) {
                    Copy-Item -Path $hostSrc -Destination $rdir -Recurse -Force
                }
                if (Test-Path $sharedAppSrc) {
                    Copy-Item -Path $sharedAppSrc -Destination $sharedDir -Recurse -Force
                }
                if (Test-Path $dotnetExe) {
                    Copy-Item -Path $dotnetExe -Destination $rdir -Force
                }
                elseif (Test-Path $dotnetBin) {
                    Copy-Item -Path $dotnetBin -Destination $rdir -Force
                }
            }
        }
    }

    # 5. Publish server components: MahoServer.dll and server-manager into maho/server/
    dotnet publish "$ServerProject" -c $config -o "$serverDir" @baseArgs --nologo -v q
    dotnet publish "$ServerManagerProject" -c $config -o "$serverDir" @serverMgrArgs --nologo -v q
    $rootManifest = @"
{
  "activeCompilerVersion": "$layoutVersion",
  "installedTools": ["$layoutVersion"],
  "installedCompilers": ["$layoutVersion"]
}
"@
    Set-Content -Path (Join-Path $serverDir "manifest.json") -Value $rootManifest

    # 6. Generate shims in maho/shims/
    if ($rid -like "win-*") {
        Set-Content -Path (Join-Path $shimsDir "mahoc.cmd") -Value "@`"%~dp0..\tools\$layoutVersion\mahoc.exe`" %*"
        Set-Content -Path (Join-Path $shimsDir "maho-server.cmd") -Value "@`"%~dp0..\tools\$layoutVersion\maho-server.exe`" %*"
        Set-Content -Path (Join-Path $shimsDir "maho-ls.cmd") -Value "@`"%~dp0..\tools\$layoutVersion\maho-ls.cmd`" %*"
    }
    else {
        Set-Content -Path (Join-Path $shimsDir "mahoc") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$layoutVersion/mahoc`" `"`$@`""
        Set-Content -Path (Join-Path $shimsDir "maho-server") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$layoutVersion/maho-server`" `"`$@`""
        Set-Content -Path (Join-Path $shimsDir "maho-ls") -Value "#!/usr/bin/env sh`nexec `"`$HOME/.maho/tools/$layoutVersion/maho-ls`" `"`$@`""
    }

    # 7. Generate miryo.config inside miryo folder alongside miryo
    $configContent = @"
# Miryo Toolchain Configuration

[tools]
mahoc = ["../maho/tools/$layoutVersion/mahoc", "../maho/shims/mahoc", "mahoc"]
mahoc_server = ["../maho/tools/$layoutVersion/maho-server", "../maho/shims/maho-server", "maho-server"]
mahoc_compiler = ["../maho/compiler/$layoutVersion"]

[templates]
bin_template = "EntryFile : `"src/Program.mh`";\nImplicitTopLevel : true;\n"
lib_template = "EntryFile : `"src/Lib.mh`";\nImplicitTopLevel : false;\n"
bin_source_path = "src/Program.mh"
bin_source = "println(`"Hello from Maho!`");\n"
lib_source_path = "src/Lib.mh"
lib_source = "public struct Lib;\n"
gitignore = "target/\nbin/\nobj/\n"
"@
    Set-Content -Path (Join-Path $miryoDir "miryo.config") -Value $configContent
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

$cliVer = (Select-String -Path $CliProject -Pattern "<Version>(.*?)</Version>").Matches.Groups[1].Value.Trim()
if (-not $cliVer) { $cliVer = "0.2.0" }
$layoutVersion = ($cliVer -split "\.")[0..1] -join "."
if (-not $layoutVersion) { $layoutVersion = "0.2" }

if (-not (Test-Path $BinDir)) {
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
}

if ($All) {
    $platforms = @("linux-x64", "linux-arm64", "win-x64", "osx-x64", "osx-arm64")

    Write-Host "==> Building Maho toolchain for all major platforms..." -ForegroundColor Cyan

    foreach ($rid in $platforms) {
        Write-Host "  -> Publishing for $rid..."
        $platformDir = Join-Path $BinDir $rid
        Publish-Target $rid $platformDir
    }

    Write-Host "  -> Publishing for host platform into $BinDir..."
    Publish-Target $hostRid $BinDir

    $hostMiryo = Find-Binary (Join-Path $BinDir "miryo") "miryo"
    $hostMahoc = Find-Binary (Join-Path $BinDir "maho\tools\$layoutVersion") "mahoc"
    $hostServer = Find-Binary (Join-Path $BinDir "maho\tools\$layoutVersion") "maho-server"
    $hostServerMgr = Find-Binary (Join-Path $BinDir "maho\server") "server-manager"

    Write-Host ""
    Write-Host "Maho toolchain successfully built for all major platforms:"
    Write-Host "  miryo:          $hostMiryo"
    Write-Host "  mahoc:          $hostMahoc"
    Write-Host "  maho-server:    $hostServer"
    Write-Host "  server-manager: $hostServerMgr"
    Write-Host "  Layout:         $BinDir (maho\ and miryo\)"
}
elseif ($Platform) {
    $normRid = Normalize-Rid $Platform
    Write-Host "==> Building Maho toolchain for platform '$normRid'..." -ForegroundColor Cyan
    $targetDir = Join-Path $BinDir $normRid
    Publish-Target $normRid $targetDir

    $miryoBin = Find-Binary (Join-Path $targetDir "miryo") "miryo"
    $mahocBin = Find-Binary (Join-Path $targetDir "maho\tools\$layoutVersion") "mahoc"
    $serverBin = Find-Binary (Join-Path $targetDir "maho\tools\$layoutVersion") "maho-server"
    $serverMgrBin = Find-Binary (Join-Path $targetDir "maho\server") "server-manager"

    Write-Host ""
    Write-Host "Maho toolchain successfully built for $normRid:"
    Write-Host "  miryo:          $miryoBin"
    Write-Host "  mahoc:          $mahocBin"
    Write-Host "  maho-server:    $serverBin"
    Write-Host "  server-manager: $serverMgrBin"
    Write-Host "  Layout:         $targetDir (maho\ and miryo\)"
}
else {
    Write-Host "==> Building Maho toolchain for host platform ($hostRid)..." -ForegroundColor Cyan
    Publish-Target $hostRid $BinDir

    $miryoBin = Find-Binary (Join-Path $BinDir "miryo") "miryo"
    $mahocBin = Find-Binary (Join-Path $BinDir "maho\tools\$layoutVersion") "mahoc"
    $serverBin = Find-Binary (Join-Path $BinDir "maho\tools\$layoutVersion") "maho-server"
    $serverMgrBin = Find-Binary (Join-Path $BinDir "maho\server") "server-manager"

    Write-Host ""
    Write-Host "Maho toolchain successfully built:"
    Write-Host "  miryo:          $miryoBin"
    Write-Host "  mahoc:          $mahocBin"
    Write-Host "  maho-server:    $serverBin"
    Write-Host "  server-manager: $serverMgrBin"
    Write-Host "  Layout:         $BinDir (maho\ and miryo\)"
}

if ($Install) {
    $targetHome = if ($env:MAHO_HOME) { $env:MAHO_HOME } else { Join-Path $HOME ".maho" }
    Write-Host "==> Installing Maho toolchain to $targetHome..." -ForegroundColor Green
    Copy-Item -Recurse -Force (Join-Path $BinDir "maho\*") $targetHome
    $targetBin = Join-Path $targetHome "bin"
    if (-not (Test-Path $targetBin)) { New-Item -ItemType Directory -Path $targetBin -Force | Out-Null }
    Copy-Item -Recurse -Force (Join-Path $BinDir "miryo\*") $targetBin
    Write-Host "Installation complete! Add '$targetHome\bin' and '$targetHome\shims' to your PATH." -ForegroundColor Green
}
