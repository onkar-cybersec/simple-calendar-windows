param([switch]$Package)

$ErrorActionPreference = 'Stop'
$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$windowsKit = Join-Path $programFilesX86 'Windows Kits\10'

if (-not (Test-Path -LiteralPath $frameworkCompiler)) {
    throw 'The .NET Framework 64-bit C# compiler was not found.'
}

$windowsWinMd = Get-ChildItem -LiteralPath (Join-Path $windowsKit 'UnionMetadata') -Recurse -Filter 'Windows.winmd' |
    Sort-Object FullName -Descending | Select-Object -First 1
$universalContract = Get-ChildItem -LiteralPath (Join-Path $windowsKit 'References') -Recurse -Filter 'Windows.Foundation.UniversalApiContract.winmd' |
    Sort-Object FullName -Descending | Select-Object -First 1

if ($null -eq $windowsWinMd -or $null -eq $universalContract) {
    throw 'Windows SDK WinMD references were not found. Install the Windows 10 or 11 SDK.'
}

$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'
$systemRuntime = Get-ChildItem -LiteralPath (Join-Path $gac 'System.Runtime') -Recurse -Filter 'System.Runtime.dll' | Select-Object -First 1
$windowsInterop = Get-ChildItem -LiteralPath (Join-Path $gac 'System.Runtime.InteropServices.WindowsRuntime') -Recurse -Filter 'System.Runtime.InteropServices.WindowsRuntime.dll' | Select-Object -First 1
$frameworkWinRt = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll'

$dist = Join-Path $PSScriptRoot 'dist'
$packageDirectory = Join-Path $dist 'package'
$assetDirectory = Join-Path $packageDirectory 'Assets'
New-Item -ItemType Directory -Path $dist, $packageDirectory, $assetDirectory -Force | Out-Null

$executable = Join-Path $dist 'Simple Calendar.exe'
$compilerArguments = @(
    '/nologo', '/target:winexe', '/optimize+',
    "/out:$executable",
    "/win32icon:$PSScriptRoot\SimpleCalendar.ico",
    "/win32manifest:$PSScriptRoot\app.manifest",
    "/reference:$($windowsWinMd.FullName)",
    "/reference:$($universalContract.FullName)",
    "/reference:$($systemRuntime.FullName)",
    "/reference:$($windowsInterop.FullName)",
    "/reference:$frameworkWinRt",
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'TileService.cs'),
    (Join-Path $PSScriptRoot 'TileAssets.cs')
)

& $frameworkCompiler $compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

& $executable --make-assets $assetDirectory
if ($LASTEXITCODE -ne 0) { throw 'Tile asset generation failed.' }

Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory 'Simple Calendar.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination (Join-Path $packageDirectory 'AppxManifest.xml') -Force

if ($Package) {
    $makeAppx = Get-ChildItem -LiteralPath (Join-Path $windowsKit 'bin') -Recurse -Filter 'MakeAppx.exe' |
        Where-Object FullName -Match '\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $makeAppx) { throw 'MakeAppx.exe was not found in the Windows SDK.' }
    $msix = Join-Path $dist 'Simple Calendar Live Tile unsigned.msix'
    & $makeAppx.FullName pack /d $packageDirectory /p $msix /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
}

Write-Host "Build completed: $dist"
