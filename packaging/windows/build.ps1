<#
.SYNOPSIS
  Builds the Windows version of LaunchHeim into a portable folder and zip.

.DESCRIPTION
  1. Builds Qml.Net's native library (QmlNet.dll) from source with qmlnet-signal-fix.patch. The
     QmlNet.dll on NuGet has the signal bug that native/signal_fix.cpp works around on Linux, and on
     Windows it cannot be worked around from outside because MSVC does not export the functions the
     Linux fix calls.
  2. Publishes LaunchHeim self-contained for win-x64, swaps in the patched QmlNet.dll and compiles
     native/app_icon.cpp, which gives the window and the taskbar button the LaunchHeim icon.
  3. Copies the Qt 5.15 it was built against next to LaunchHeim.exe (windeployqt), plus the Visual C++
     runtime, so the folder runs on any Windows 10 or 11 without installing anything.
  4. Signs LaunchHeim.exe and the DLLs this build compiled with the self-signed LaunchHeim certificate
     (sign.ps1), when LAUNCHHEIM_SIGNING_PFX names its .pfx (password in LAUNCHHEIM_SIGNING_PASSWORD).
     See packaging/README.md, "Code signing".
  5. Zips it.
  6. Compiles the setup (launchheim.iss) from the same folder with Inno Setup 6, signed the same way.
  With -Smoke it then starts the app offscreen and saves a screenshot of every page, and installs,
  updates and uninstalls the setup for the current user.

  The GitHub workflow .github/workflows/launchheim-windows.yml runs exactly this script.

  Needs: Visual Studio 2022 (or its Build Tools) with the C++ workload, Qt 5.15.2 msvc2019_64
  (the Qt online installer or `aqt install-qt windows desktop 5.15.2 win64_msvc2019_64`), the .NET 10
  SDK, Inno Setup 6 (`winget install JRSoftware.InnoSetup`; GitHub's Windows runners have it) and git.

.EXAMPLE
  .\packaging\windows\build.ps1 -QtDir C:\Qt\5.15.2\msvc2019_64
#>
[CmdletBinding()]
param(
  # The Qt 5.15 kit, the folder that contains bin\qmake.exe.
  [string]$QtDir = $(if ($env:QT_ROOT_DIR) { $env:QT_ROOT_DIR } elseif ($env:Qt5_DIR) { $env:Qt5_DIR } else { 'C:\Qt\5.15.2\msvc2019_64' }),
  [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist'),
  # The code signing .pfx. Its password is read from LAUNCHHEIM_SIGNING_PASSWORD only, so it never
  # sits on a command line or in the shell history. Without a .pfx the build is unsigned.
  [string]$SigningPfx = $env:LAUNCHHEIM_SIGNING_PFX,
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

$version = ([xml](Get-Content (Join-Path $app 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
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

# The window icon, and on Windows the dark title bar (the csproj builds the same file on Linux). The .exe's own icon only reaches Explorer:
# Qt looks for a resource named IDI_ICON1, and the .NET SDK stores <ApplicationIcon> under a number.
# Built in its own folder, because the linker also writes an import library and .exp next to the DLL.
# Same flags as qmake uses for QmlNet.dll, against the same Qt, whose Qt5Gui.dll windeployqt ships.
$appIconBuild = Join-Path $work 'app-icon'
Remove-Item -Recurse -Force $appIconBuild -ErrorAction SilentlyContinue
New-Item -ItemType Directory $appIconBuild | Out-Null
Invoke-Checked cl.exe @('/nologo', '/LD', '/MD', '/O2', '/EHsc', '/permissive-', '/Zc:__cplusplus', '/DQT_NO_DEBUG',
  "/I$(Join-Path $QtDir 'include')", (Join-Path $app 'src\Desktop\native\app_icon.cpp'),
  "/Fo$(Join-Path $appIconBuild 'app_icon.obj')", "/Fe$(Join-Path $appIconBuild 'LaunchHeimAppIcon.dll')",
  '/link', "/LIBPATH:$(Join-Path $QtDir 'lib')", 'Qt5Gui.lib', 'Qt5Core.lib', 'dwmapi.lib')
Copy-Item (Join-Path $appIconBuild 'LaunchHeimAppIcon.dll') $package
# LICENSE.txt, THIRD-PARTY-NOTICES.txt and licenses\ come from the publish (see the csproj).

# 3. Qt and the Visual C++ runtime next to LaunchHeim.exe
Invoke-Checked (Join-Path $QtDir 'bin\windeployqt.exe') @('--release', '--no-translations', '--no-system-d3d-compiler',
  '--qmldir', (Join-Path $app 'src\Desktop\qml'), '--dir', $package, (Join-Path $package 'QmlNet.dll'))
$crt = Get-ChildItem -Directory (Join-Path $env:VCToolsRedistDir 'x64') -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1
if (-not $crt) { throw "The Visual C++ runtime was not found in $env:VCToolsRedistDir." }
Copy-Item (Join-Path $crt.FullName '*.dll') $package

# 4. Authenticode signatures, so anyone can see these files come from LaunchHeim's build and weren't
# changed since. Only what this build compiled is signed: Qt's DLLs and the Visual C++ runtime stay as their
# makers shipped them, and .NET's own files already carry Microsoft's signature.
if ($SigningPfx) {
  # sign.ps1 reads both from the environment, and so does the copy of it that ISCC starts in step 6.
  $env:LAUNCHHEIM_SIGNING_PFX = $SigningPfx
  & (Join-Path $PSScriptRoot 'sign.ps1') @('LaunchHeim.exe', 'LaunchHeim.dll', 'LaunchHeim.Core.dll', 'QmlNet.dll', 'LaunchHeimAppIcon.dll' |
    ForEach-Object { Join-Path $package $_ })
}
else {
  Write-Host 'Not signed: no LAUNCHHEIM_SIGNING_PFX.'
}

# 5. The portable zip
$zip = Join-Path $OutputDir "LaunchHeim-$version-win-x64.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $package -DestinationPath $zip
Write-Host "Built $zip"

# 6. The setup, from the same signed folder
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
  $iscc = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it with: winget install JRSoftware.InnoSetup' }
$setup = Join-Path $OutputDir "LaunchHeim-$version-win-x64-setup.exe"
Remove-Item -Force $setup -ErrorAction SilentlyContinue
$isccArguments = @('/Q', "/DAppVersion=$version", "/DSourceDir=$package", "/DOutputDir=$OutputDir")
if ($SigningPfx) {
  # ISCC runs this for the setup and the uninstaller; $q is its quote and $f the quoted file. This same
  # PowerShell runs sign.ps1, which takes the password from the environment: ISCC echoes the command.
  $powershell = (Get-Process -Id $PID).Path
  $isccArguments += '/DSign', "/Slaunchheim=`$q$powershell`$q -NoProfile -ExecutionPolicy Bypass -File `$q$(Join-Path $PSScriptRoot 'sign.ps1')`$q `$f"
}
Invoke-Checked $iscc ($isccArguments + (Join-Path $PSScriptRoot 'launchheim.iss'))
if (-not (Test-Path $setup)) { throw "ISCC produced no $setup." }
Write-Host "Built $setup"

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
    if (Select-String -Quiet -Path $log -Pattern 'no app icon') { Write-Host '::error::the window has no app icon'; $failed = $true }
  }
  Remove-Item Env:\LAUNCHHEIM_SCREENSHOT, Env:\LAUNCHHEIM_SCREENSHOT_PAGE, Env:\LAUNCHHEIM_SCREENSHOT_DELAY

  # The title bar in dark mode: Windows draws it, so the screenshots above never show it. Only in CI,
  # where switching the runner's app mode to dark costs nothing; on a developer's PC it would flip
  # theirs. Windows itself is asked whether the window got the dark frame, and the window is copied off
  # the screen for a look.
  if ($env:GITHUB_ACTIONS) {
    $personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    if (-not (Test-Path $personalize)) { New-Item -Force $personalize | Out-Null }
    Set-ItemProperty $personalize -Name AppsUseLightTheme -Value 0 -Type DWord
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DarkFrameProbe {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
  public static void Capture(IntPtr hwnd, string file) {
    Rect r; GetWindowRect(hwnd, out r);
    using (var bitmap = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top))
    using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
      graphics.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
      bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }
  }
}
'@
    $log = Join-Path $OutputDir 'test-windows-dark-frame.log'
    $env:LAUNCHHEIM_SCREENSHOT = Join-Path $OutputDir 'test-windows-dark.png'
    $env:LAUNCHHEIM_SCREENSHOT_PAGE = 'library'
    $env:LAUNCHHEIM_SCREENSHOT_DELAY = '15000'
    $process = Start-Process $exe -PassThru -RedirectStandardError $log -RedirectStandardOutput "$log.out"
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 60 -and $hwnd -eq [IntPtr]::Zero; $i++) {
      Start-Sleep -Milliseconds 500
      $process.Refresh()
      $hwnd = $process.MainWindowHandle
    }
    if ($hwnd -eq [IntPtr]::Zero) {
      Write-Host '::error::LaunchHeim showed no window in dark mode'; $failed = $true
    }
    else {
      Start-Sleep -Seconds 5
      $dark = 0
      $result = [DarkFrameProbe]::DwmGetWindowAttribute($hwnd, 20, [ref]$dark, 4)
      Write-Host "Dark title bar: DwmGetWindowAttribute returned $result, value $dark"
      if ($result -eq 0 -and $dark -eq 0) { Write-Host '::error::the title bar is light in dark mode'; $failed = $true }
      try { [DarkFrameProbe]::Capture($hwnd, (Join-Path $OutputDir 'test-windows-dark-frame.png')) }
      catch { Write-Host "Could not copy the window off the screen: $_" }
    }
    if (-not $process.WaitForExit(60000)) { $process.Kill() }
    Get-Content $log, "$log.out" -ErrorAction SilentlyContinue | Write-Host
    if (Select-String -Quiet -Path $log -Pattern "keeps Windows' light colors") { Write-Host '::error::the dark title bar could not be set'; $failed = $true }
    Remove-Item Env:\LAUNCHHEIM_SCREENSHOT, Env:\LAUNCHHEIM_SCREENSHOT_PAGE, Env:\LAUNCHHEIM_SCREENSHOT_DELAY
    Set-ItemProperty $personalize -Name AppsUseLightTheme -Value 1 -Type DWord
  }

  # nxm:// registration writes HKCU\Software\Classes\nxm pointing at this exe.
  $register = Start-Process $exe -ArgumentList '--register-desktop' -Wait -PassThru
  $command = (Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command' -ErrorAction SilentlyContinue).'(default)'
  Write-Host "nxm handler: $command"
  if ($register.ExitCode -ne 0 -or -not "$command".Contains("`"$exe`"")) { Write-Host '::error::nxm:// registration failed'; $failed = $true }

  if (Test-Path (Join-Path $env:USERPROFILE '.qmlnet-qt-runtimes')) { Write-Host '::error::downloaded a Qt runtime instead of using the shipped one'; $failed = $true }

  # The setup: a first install with both shortcuts, an update that drops the desktop one, an update that
  # changes nothing (it must remember the choices), then the uninstall. Per user, so neither CI nor a
  # developer's machine gets a UAC prompt. A machine where LaunchHeim is already installed is left alone.
  $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{AA9F6EE2-43EF-4C30-A1B6-44AEFFC95ECB}_is1'
  if (Test-Path $uninstallKey) {
    Write-Host '::warning::LaunchHeim is installed for this user, so the setup was not tested.'
  }
  else {
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\LaunchHeim'
    $installedExe = Join-Path $installed 'LaunchHeim.exe'
    $startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'LaunchHeim.lnk'
    $desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'LaunchHeim.lnk'
    $quiet = '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER'

    function Test-Setup([string]$Step, [string[]]$Arguments, [bool]$StartMenu, [bool]$Desktop) {
      $log = Join-Path $OutputDir "test-windows-setup-$Step.log"
      $run = Start-Process $setup -ArgumentList ($quiet + $Arguments + "/LOG=`"$log`"") -Wait -PassThru
      $ok = $run.ExitCode -eq 0 -and (Test-Path $installedExe) -and -not (Test-Path (Join-Path $installed 'vc_redist.x64.exe')) -and
        (Test-Path $startMenuLink) -eq $StartMenu -and (Test-Path $desktopLink) -eq $Desktop -and
        (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayVersion -eq $version
      Write-Host "setup ($Step): exit $($run.ExitCode), Start menu $(Test-Path $startMenuLink), desktop $(Test-Path $desktopLink)"
      if (-not $ok) { Write-Host "::error::the setup's $Step went wrong, see $log"; $script:failed = $true }
    }

    Test-Setup 'install' @('/TASKS=startmenu,desktopicon') $true $true

    # The installed copy runs, and knows the setup installed it ("Update now" depends on that).
    $env:LAUNCHHEIM_SCREENSHOT = Join-Path $OutputDir 'test-windows-installed.png'
    $env:LAUNCHHEIM_SCREENSHOT_PAGE = 'settings'
    $env:LAUNCHHEIM_SCREENSHOT_DELAY = '8000'
    $log = Join-Path $OutputDir 'test-windows-installed.log'
    $process = Start-Process $installedExe -PassThru -RedirectStandardError $log -RedirectStandardOutput "$log.out"
    if (-not $process.WaitForExit(120000)) { $process.Kill(); Write-Host '::error::the installed copy timed out'; $failed = $true }
    Remove-Item Env:\LAUNCHHEIM_SCREENSHOT, Env:\LAUNCHHEIM_SCREENSHOT_PAGE, Env:\LAUNCHHEIM_SCREENSHOT_DELAY
    # Info lines only go to LaunchHeim's own log; the newest "Installed as" is this start's.
    $appLog = Join-Path $env:LOCALAPPDATA 'LaunchHeim\Logs\launchheim.log'
    $match = Select-String -Path $appLog -Pattern 'Installed as (\w+)\.' -ErrorAction SilentlyContinue | Select-Object -Last 1
    $kind = if ($match) { $match.Matches[0].Groups[1].Value } else { 'nothing logged' }
    Write-Host "installed copy: installed as $kind"
    if ($kind -ne 'WindowsSetup') {
      Get-Content $log, "$log.out" -ErrorAction SilentlyContinue | Write-Host
      Write-Host "::error::the installed copy doesn't know the setup installed it"; $failed = $true
    }

    Test-Setup 'update' @('/TASKS=startmenu') $true $false
    Test-Setup 'update-again' @() $true $false

    # Uninstalling also removes the nxm:// handler, as long as it still points at the installed copy.
    Start-Process $installedExe -ArgumentList '--register-desktop' -Wait | Out-Null
    # The uninstaller runs a copy of itself from %TEMP%, so the folder may outlive the process briefly.
    Start-Process (Join-Path $installed 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait | Out-Null
    for ($i = 0; $i -lt 60 -and (Test-Path $installedExe); $i++) { Start-Sleep -Seconds 1 }
    $left = @($installedExe, $startMenuLink, $desktopLink, $uninstallKey, 'HKCU:\Software\Classes\nxm') | Where-Object { Test-Path $_ }
    Write-Host "uninstall: left behind $(if ($left) { $left -join ', ' } else { 'nothing' })"
    if ($left) { Write-Host '::error::the uninstall left LaunchHeim behind'; $failed = $true }
    # Instances and settings belong to the user and outlive the app.
    if (-not (Test-Path (Join-Path $env:LOCALAPPDATA 'LaunchHeim'))) { Write-Host '::error::the uninstall removed LaunchHeim''s data'; $failed = $true }
  }

  if ($failed) { throw 'The smoke test failed.' }
}
