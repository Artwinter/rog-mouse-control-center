#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root '_test'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $fw)) {
    $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $fw)) { throw 'csc.exe not found' }

$sources = @(
    'src\RazerBlackSharkProtocol.cs',
    'src\NativeMethods.cs',
    'src\RazerHidDevice.cs',
    'tests\HeadsetProtocolTests.cs'
) | ForEach-Object { Join-Path $root $_ }

$exe = Join-Path $outDir 'HeadsetProtocolTests.exe'
& $fw /nologo /target:exe /platform:x86 "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $exe
exit $LASTEXITCODE
