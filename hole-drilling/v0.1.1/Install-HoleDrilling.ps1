$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'HoleDrilling.dll'
$destination = Join-Path $env:ProgramFiles 'CamBam plus 1.0\plugins'

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Plugin DLL not found: $source. Run Build-HoleDrilling.cmd first."
}
if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
    throw "CamBam plugin folder not found: $destination"
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait
    exit
}

Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'HoleDrilling.dll') -Force
Write-Host "Installed HoleDrilling.dll to $destination"
Write-Host 'Start or restart CamBam and check Plugins > Hole Drilling...'
pause
