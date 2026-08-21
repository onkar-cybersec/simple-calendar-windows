param(
    [switch]$Package,
    [switch]$Store
)

$ErrorActionPreference = 'Stop'
$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$windowsKit = Join-Path $programFilesX86 'Windows Kits\10'
$workspaceTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools'
$localSdk = Get-ChildItem -LiteralPath $workspaceTools -Directory -Filter 'Microsoft.Windows.SDK.BuildTools.*' -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1

if (-not (Test-Path -LiteralPath $frameworkCompiler)) {
    throw 'The .NET Framework 64-bit C# compiler was not found.'
}

$unionMetadata = Join-Path $windowsKit 'UnionMetadata'
$referencesDirectory = Join-Path $windowsKit 'References'
$windowsWinMd = if (Test-Path -LiteralPath $unionMetadata) {
    Get-ChildItem -LiteralPath $unionMetadata -Recurse -Filter 'Windows.winmd' |
        Sort-Object FullName -Descending | Select-Object -First 1
} else {
    Get-Item -LiteralPath (Join-Path $env:WINDIR 'Lenovo\ImController\Service\Windows.winmd') -ErrorAction SilentlyContinue
}
$universalContract = if (Test-Path -LiteralPath $referencesDirectory) {
    Get-ChildItem -LiteralPath $referencesDirectory -Recurse -Filter 'Windows.Foundation.UniversalApiContract.winmd' |
        Sort-Object FullName -Descending | Select-Object -First 1
}

if ($null -eq $windowsWinMd) {
    throw 'Windows metadata references were not found.'
}

$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'
$systemRuntime = Get-ChildItem -LiteralPath (Join-Path $gac 'System.Runtime') -Recurse -Filter 'System.Runtime.dll' | Select-Object -First 1
$windowsInterop = Get-ChildItem -LiteralPath (Join-Path $gac 'System.Runtime.InteropServices.WindowsRuntime') -Recurse -Filter 'System.Runtime.InteropServices.WindowsRuntime.dll' | Select-Object -First 1
$frameworkWinRt = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll'

$dist = Join-Path $PSScriptRoot 'dist'
$packageDirectory = Join-Path $dist $(if ($Store) { 'store-package' } else { 'package' })
$assetDirectory = Join-Path $packageDirectory 'Assets'
New-Item -ItemType Directory -Path $dist, $packageDirectory, $assetDirectory -Force | Out-Null

$executable = Join-Path $dist 'Simple Calendar.exe'
$compilerArguments = @(
    '/nologo', '/target:winexe', '/optimize+',
    "/out:$executable",
    "/win32icon:$PSScriptRoot\SimpleCalendar.ico",
    "/win32manifest:$PSScriptRoot\app.manifest",
    "/reference:$($windowsWinMd.FullName)",
    "/reference:$($systemRuntime.FullName)",
    "/reference:$($windowsInterop.FullName)",
    "/reference:$frameworkWinRt",
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'TileService.cs'),
    (Join-Path $PSScriptRoot 'TileAssets.cs')
)

if ($null -ne $universalContract) {
    $compilerArguments = $compilerArguments[0..5] + "/reference:$($universalContract.FullName)" + $compilerArguments[6..($compilerArguments.Length - 1)]
}

& $frameworkCompiler $compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

$assetProcess = Start-Process -FilePath $executable -ArgumentList @('--make-assets', $assetDirectory) -Wait -PassThru
if ($assetProcess.ExitCode -ne 0) { throw 'Tile asset generation failed.' }

Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory 'Simple Calendar.exe') -Force
$manifestName = if ($Store) { 'AppxManifest.Store.xml' } else { 'AppxManifest.xml' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $manifestName) -Destination (Join-Path $packageDirectory 'AppxManifest.xml') -Force

if ($Package -or $Store) {
    $makeAppxRoots = @((Join-Path $windowsKit 'bin'))
    if ($null -ne $localSdk) { $makeAppxRoots += (Join-Path $localSdk.FullName 'bin') }
    $makeAppx = $makeAppxRoots | Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -Filter 'MakeAppx.exe' } |
        Where-Object FullName -Match '\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $makeAppx) { throw 'MakeAppx.exe was not found in the Windows SDK.' }
    $msixName = if ($Store) { 'Dayframe Calendar 1.1.6.0 x64.msix' } else { 'Simple Calendar Live Tile unsigned.msix' }
    $msix = Join-Path $dist $msixName
    & $makeAppx.FullName pack /d $packageDirectory /p $msix /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
}

Write-Host "Build completed: $dist"
