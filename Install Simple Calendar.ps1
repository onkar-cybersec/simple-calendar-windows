param([switch]$Elevated)

$ErrorActionPreference = 'Stop'

function Find-ReleaseFile([string[]]$Names) {
    foreach ($name in $Names) {
        $candidate = Join-Path $PSScriptRoot $name
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Required release file is missing: $($Names -join ' or ')"
}

$certificatePath = Find-ReleaseFile @('Simple.Calendar.Certificate.cer', 'Simple Calendar Certificate.cer')
$packagePath = Find-ReleaseFile @('Simple.Calendar.Live.Tile.msix', 'Simple Calendar Live Tile.msix')

if (-not $Elevated) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw 'Installation was cancelled or failed.' }
    return
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$hasCodeSigningPurpose = $false
foreach ($extension in $certificate.Extensions) {
    if ($extension.Oid.Value -eq '2.5.29.37' -and $extension.Format($false) -match [regex]::Escape($codeSigningOid)) {
        $hasCodeSigningPurpose = $true
    }
}
if (-not $hasCodeSigningPurpose) { throw 'The certificate is not restricted to code signing.' }

Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

$package = Get-AppxPackage -Name 'SimpleCalendar.LiveTile'
$manifest = Get-AppxPackageManifest -Package $package.PackageFullName
$appId = $manifest.Package.Applications.Application.Id
Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!$appId"

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show(
    "Simple Calendar 1.1.2 is installed. Your pinned live tile is preserved.",
    'Simple Calendar installed', 'OK', 'Information'
) | Out-Null
