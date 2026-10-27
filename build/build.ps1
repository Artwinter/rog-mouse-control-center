#Requires -Version 5.1
<#
.SYNOPSIS
    Builds ROG Mouse Control Center with the .NET Framework compiler that ships with Windows.

.DESCRIPTION
    No SDK, NuGet or Visual Studio needed: csc.exe from the installed .NET Framework does the job.
    Output is a single x86 exe with the icon, manifest and XAML/hero image embedded.

.PARAMETER Out
    Output exe path. Defaults to dist\ROGMouseControlCenter.exe

.EXAMPLE
    .\build\build.ps1
    .\build\build.ps1 -Out C:\temp\test.exe
#>
param(
    [string]$Out
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Out) { $Out = Join-Path $root 'dist\ROGMouseControlCenter.exe' }

# csc.exe lives in the Framework directory; prefer the 64-bit one, fall back to 32-bit.
$fwCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319')
)
$fw = $fwCandidates | Where-Object { Test-Path (Join-Path $_ 'csc.exe') } | Select-Object -First 1
if (-not $fw) {
    throw 'csc.exe not found. .NET Framework 4.x is required (Windows 10/11 ships with it).'
}

$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
if (-not (Test-Path $gac)) { throw "GAC not found at $gac" }

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
if (Test-Path $Out) { Remove-Item $Out -Force }

# WPF assemblies come from the GAC; System.Drawing / System.Windows.Forms are
# pulled in automatically by csc and must not be referenced explicitly
# (duplicate reference => error CS1703).
$refs = @(
    (Join-Path $gac 'GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll'),
    (Join-Path $gac 'GAC_32\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'),
    (Join-Path $gac 'GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'),
    (Join-Path $gac 'GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll')
)
foreach ($r in $refs) {
    if (-not (Test-Path $r)) { throw "Missing reference: $r" }
}

$src = @(
    'NativeMethods.cs', 'AsusHidDevice.cs', 'AsusProtocol.cs',
    'Connection.cs', 'AutoStart.cs', 'TrayIcon.cs', 'RazerBlackSharkProtocol.cs',
    'RazerHidDevice.cs', 'HeadsetService.cs', 'HeadsetPageState.cs',
    'WindowFit.cs', 'HeadsetTransactionPlan.cs', 'HeadsetController.cs', 'App.cs', 'MainController.cs'
) | ForEach-Object { Join-Path $root "src\$_" }

$args = @('/nologo', '/target:winexe', '/platform:x86', '/optimize+', "/out:$Out")
foreach ($r in $refs) { $args += "/r:$r" }
$args += $src
$args += "/resource:$(Join-Path $root 'ui\MainWindow.xaml'),ui.MainWindow.xaml"
$args += "/resource:$(Join-Path $root 'ui\hero.png'),ui.hero.png"
$args += "/win32icon:$(Join-Path $root 'ui\app.ico')"
$args += "/win32manifest:$(Join-Path $root 'ui\app.manifest')"

& (Join-Path $fw 'csc.exe') @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

$size = (Get-Item $Out).Length
Write-Host ("Built {0} ({1:N0} bytes)" -f $Out, $size)
