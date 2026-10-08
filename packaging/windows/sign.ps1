<#
.SYNOPSIS
  Signs files with the self-signed LaunchHeim certificate and checks the result.

.DESCRIPTION
  The .pfx comes from LAUNCHHEIM_SIGNING_PFX and its password from LAUNCHHEIM_SIGNING_PASSWORD, never
  from the command line: build.ps1 calls this for LaunchHeim.exe and its DLLs, and Inno Setup's
  compiler for the setup and its uninstaller. ISCC prints the sign command it runs, so a password in
  it would end up in the build log. See packaging/README.md, "Code signing".

.EXAMPLE
  .\packaging\windows\sign.ps1 dist\LaunchHeim\LaunchHeim.exe dist\LaunchHeim\LaunchHeim.dll
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory, ValueFromRemainingArguments)]
  [string[]]$Files
)

$ErrorActionPreference = 'Stop'
$pfx = $env:LAUNCHHEIM_SIGNING_PFX
if (-not $pfx) { throw 'LAUNCHHEIM_SIGNING_PFX is not set.' }

# The committed certificate is what the download page and the release notes tell people to expect,
# so a .pfx holding any other key (a stale secret, a test key) fails here instead of shipping.
$expected = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $PSScriptRoot 'launchheim-codesign.cer'))
# EphemeralKeySet: only the thumbprint is needed here, so the key isn't written to the user's key store.
$actual = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx, $env:LAUNCHHEIM_SIGNING_PASSWORD,
  [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
if ($actual.Thumbprint -ne $expected.Thumbprint) {
  throw "$pfx holds certificate $($actual.Thumbprint), not $($expected.Thumbprint) from launchheim-codesign.cer."
}

# On PATH in a Developer PowerShell (and after msvc-dev-cmd in CI), otherwise in the newest Windows SDK.
$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if (-not $signtool) {
  $signtool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\*\x64\signtool.exe') -ErrorAction SilentlyContinue |
    Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $signtool) { throw 'signtool.exe was not found. It comes with the Windows SDK.' }

# The time stamp keeps a signature valid after the certificate expires (2036). A second server in case
# the first is down; signing again simply replaces a signature from a failed attempt. signtool's
# arguments aren't echoed on failure: they hold the password.
$signed = $false
foreach ($timestampServer in 'http://timestamp.digicert.com', 'http://timestamp.sectigo.com') {
  & $signtool sign /fd sha256 /f $pfx /p $env:LAUNCHHEIM_SIGNING_PASSWORD /tr $timestampServer /td sha256 `
    /d LaunchHeim /du 'https://codeisnotevil.github.io/MyMonoRepo/' @Files
  if ($LASTEXITCODE -eq 0) { $signed = $true; break }
  Write-Host "Signing with the time stamp server $timestampServer failed."
}
if (-not $signed) { throw 'signtool could not sign the files.' }

# Windows doesn't trust a self-signed certificate, so the status is UnknownError rather than Valid. What
# matters is that the signature covers the file as shipped (no HashMismatch), is ours, and is time stamped.
foreach ($file in $Files) {
  $signature = Get-AuthenticodeSignature $file
  if ($signature.Status -in 'NotSigned', 'HashMismatch', 'NotSupportedFileFormat' -or
    $signature.SignerCertificate.Thumbprint -ne $expected.Thumbprint -or -not $signature.TimeStamperCertificate) {
    throw "$file has no valid LaunchHeim signature: $($signature.Status) $($signature.StatusMessage)"
  }
}
Write-Host "Signed $($Files.Count) files with certificate $($expected.Thumbprint)"
