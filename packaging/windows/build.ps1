<#
.SYNOPSIS
  Builds the Windows version of LaunchHeim into a portable folder and zip.

.DESCRIPTION
  1. Builds Qml.Net's native library (QmlNet.dll) from source with qmlnet-signal-fix.patch. The
     QmlNet.dll on NuGet has the signal bug that native/signal_fix.cpp works around on Linux, and on
     Windows it cannot be worked around from outside because MSVC does not export the functions the
     Linux fix calls.
  2. Publishes LaunchHeim self-contained for win-x64 and swaps in the patched QmlNet.dll.
  3. Copies the Qt 5.15 it was built against next to LaunchHeim.exe (windeployqt), plus the Visual C++
     runtime, so the folder runs on any Windows 10 or 11 without installing anything.
  4. Zips it, and with -Smoke starts it offscreen and saves a screenshot of every page.

  The GitHub workflow .github/workflows/launchheim-windows.yml runs exactly this script.

  Needs: Visual Studio 2022 (or its Build Tools) with the C++ workload, Qt 5.15.2 msvc2019_64
  (the Qt online installer or `aqt install-qt windows desktop 5.15.2 win64_msvc2019_64`), the .NET 10
  SDK and git.

.EXAMPLE
  .\packaging\windows\build.ps1 -QtDir C:\Qt\5.15.2\msvc2019_64
#>
[CmdletBinding()]
param(
  # The Qt 5.15 kit, the folder that contains bin\qmake.exe.
  [string]$QtDir = $(if ($env:QT_ROOT_DIR) { $env:QT_ROOT_DIR } elseif ($env:Qt5_DIR) { $env:Qt5_DIR } else { 'C:\Qt\5.15.2\msvc2019_64' }),
  [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist'),
  [switch]$SkipTests,
  [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
# Resolved once, so paths compare equal to what Windows records (the registry holds no "..").
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path
$app = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$work = Join-Path $app 'packaging\windows\build'

# The qmlnet-native commit that Qml.Net 0.11.0 (the NuGet release LaunchHeim uses) was built from.
$qmlNetNativeRepo = 'https://github.com/qmlnet/qmlnet-native.git'
$qmlNetNativeCommit = 'e2ff96713b659ed295710f4e94cef96b2f7e020a'

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) { throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# The MSVC compiler, from a Developer PowerShell if this is one, otherwise found through vswhere.
function Enter-MsvcEnvironment {
  if (Get-Command cl.exe -ErrorAction SilentlyContinue) { return }
  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
  if (-not $vs) { throw 'Visual Studio with the C++ workload was not found.' }
  Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
  Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
}

$qmake = Join-Path $QtDir 'bin\qmake.exe'
if (-not (Test-Path $qmake)) { throw "qmake.exe was not found in $QtDir\bin. Pass -QtDir." }
Enter-MsvcEnvironment
$env:PATH = "$(Join-Path $QtDir 'bin');$env:PATH"

$version = ([xml](Get-Content (Join-Path $app 'src\Desktop\LaunchHeim.Desktop.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "LaunchHeim $version for Windows, Qt from $QtDir"

# 1. QmlNet.dll with the signal fix
$native = Join-Path $work 'qmlnet-native'
if (-not (Test-Path (Join-Path $native '.git'))) {
  # LF endings as in the repository, or the patch does not apply on a checkout with core.autocrlf=true.
  Invoke-Checked git @('-c', 'core.autocrlf=false', 'clone', '--quiet', $qmlNetNativeRepo, $native)
}
Invoke-Checked git @('-C', $native, 'checkout', '--quiet', '--force', $qmlNetNativeCommit)
Invoke-Checked git @('-C', $native, 'clean', '-fdxq')
Invoke-Checked git @('-C', $native, 'apply', (Join-Path $PSScriptRoot 'qmlnet-signal-fix.patch'))

$nativeBuild = Join-Path $work 'qmlnet-native-build'
Remove-Item -Recurse -Force $nativeBuild -ErrorAction SilentlyContinue
New-Item -ItemType Directory $nativeBuild | Out-Null
Push-Location $nativeBuild
try {
  Invoke-Checked $qmake @((Join-Path $native 'QmlNet.pro'), 'CONFIG+=release', 'CONFIG-=debug_and_release')
  Invoke-Checked nmake @('/nologo')
}
finally {
  Pop-Location
}
$qmlNetDll = Get-ChildItem -Recurse -Filter QmlNet.dll $nativeBuild | Select-Object -First 1
if (-not $qmlNetDll) { throw 'The native build produced no QmlNet.dll.' }

# 2. The app
if (-not $SkipTests) {
  Invoke-Checked dotnet @('test', (Join-Path $app 'tests\Core.Tests'), '-c', 'Release')
}

$package = Join-Path $OutputDir 'LaunchHeim'
Remove-Item -Recurse -Force $package -ErrorAction SilentlyContinue
Invoke-Checked dotnet @('publish', (Join-Path $app 'src\Desktop'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
  '-p:ContinuousIntegrationBuild=true', '-o', $package)
Copy-Item -Force $qmlNetDll.FullName (Join-Path $package 'QmlNet.dll')

# 3. Qt and the Visual C++ runtime next to LaunchHeim.exe
Invoke-Checked (Join-Path $QtDir 'bin\windeployqt.exe') @('--release', '--no-translations', '--no-system-d3d-compiler',
  '--qmldir', (Join-Path $app 'src\Desktop\qml'), '--dir', $package, (Join-Path $package 'QmlNet.dll'))
$crt = Get-ChildItem -Directory (Join-Path $env:VCToolsRedistDir 'x64') -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1
if (-not $crt) { throw "The Visual C++ runtime was not found in $env:VCToolsRedistDir." }
Copy-Item (Join-Path $crt.FullName '*.dll') $package

# 4. Zip, then optionally a smoke test
$zip = Join-Path $OutputDir "LaunchHeim-$version-win-x64.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $package -DestinationPath $zip
Write-Host "Built $zip"

if ($Smoke) {
  # The real Windows platform, as users get it. Qt's offscreen platform has no font database on
  # Windows and renders no text at all. Without a GPU (CI runners) Qt falls back to opengl32sw.dll,
  # which windeployqt ships, so that fallback is tested too.
  $exe = Join-Path $package 'LaunchHeim.exe'
  $failed = $false

  foreach ($page in 'library', 'browse', 'settings') {
    $shot = Join-Path $OutputDir "test-windows-$page.png"
    $log = Join-Path $OutputDir "test-windows-$page.log"
    $env:LAUNCHHEIM_SCREENSHOT = $shot
    $env:LAUNCHHEIM_SCREENSHOT_PAGE = $page
    $env:LAUNCHHEIM_SCREENSHOT_DELAY = '8000'
    $process = Start-Process $exe -PassThru -RedirectStandardError $log -RedirectStandardOutput "$log.out"
    if (-not $process.WaitForExit(120000)) { $process.Kill(); Write-Host "::error::$page timed out"; $failed = $true }
    Get-Content $log, "$log.out" -ErrorAction SilentlyContinue | Write-Host
    if (-not (Test-Path $shot)) { Write-Host "::error::no screenshot of $page"; $failed = $true }
    if (Select-String -Quiet -Path $log -Pattern 'without the signal fix') { Write-Host '::error::QmlNet.dll lacks the signal fix'; $failed = $true }
  }
  Remove-Item Env:\LAUNCHHEIM_SCREENSHOT, Env:\LAUNCHHEIM_SCREENSHOT_PAGE, Env:\LAUNCHHEIM_SCREENSHOT_DELAY

  # nxm:// registration writes HKCU\Software\Classes\nxm pointing at this exe.
  $register = Start-Process $exe -ArgumentList '--register-desktop' -Wait -PassThru
  $command = (Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command' -ErrorAction SilentlyContinue).'(default)'
  Write-Host "nxm handler: $command"
  if ($register.ExitCode -ne 0 -or -not "$command".Contains("`"$exe`"")) { Write-Host '::error::nxm:// registration failed'; $failed = $true }

  if (Test-Path (Join-Path $env:USERPROFILE '.qmlnet-qt-runtimes')) { Write-Host '::error::downloaded a Qt runtime instead of using the shipped one'; $failed = $true }
  if ($failed) { throw 'The smoke test failed.' }
}
