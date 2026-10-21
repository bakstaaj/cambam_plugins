$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Tapping.dll'
$catalogPath = Join-Path $PSScriptRoot 'ThreadMillCatalog.xml'
$destination = Join-Path $env:ProgramFiles 'CamBam plus 1.0\plugins'

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Plugin DLL not found: $source. Run Build-Tapping.cmd first."
}
if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
    throw "CamBam plugin folder not found: $destination"
}
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    throw "Thread mill catalog not found: $catalogPath"
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait
    exit
}

Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'Tapping.dll') -Force
Copy-Item -LiteralPath $catalogPath -Destination (Join-Path $destination 'ThreadMillCatalog.xml') -Force

[xml]$catalog = Get-Content -LiteralPath $catalogPath -Raw
$toolDirs = @(
    (Join-Path $env:ProgramData 'CamBam plus 1.0\tools'),
    (Join-Path $env:ProgramFiles 'CamBam plus 1.0\tools')
) | Select-Object -Unique
$updatedLibraries = @()
foreach ($toolDir in $toolDirs) {
    foreach ($unit in @('mm', 'in')) {
        $libraryPath = Join-Path $toolDir "Default-$unit.xml"
        if (-not (Test-Path -LiteralPath $libraryPath -PathType Leaf)) { continue }

        [xml]$library = Get-Content -LiteralPath $libraryPath -Raw
        $original = [System.IO.File]::ReadAllText($libraryPath)
        $existingCodes = @{}
        $usedIndexes = @{}
        foreach ($definition in $library.ToolLibrary.ToolDefinition) {
            $indexNode = $definition.SelectSingleNode('Index')
            if ($indexNode) { $usedIndexes[[string]$indexNode.InnerText] = $true }
            $partNode = $definition.SelectSingleNode('PartCode')
            if ($partNode -and $partNode.InnerText) { $existingCodes[$partNode.InnerText] = $definition }
        }
        $nextIndex = 1
        foreach ($tool in $catalog.ThreadMillCatalog.Tool) {
            $code = [string]$tool.PartCode
            if ($existingCodes.ContainsKey($code)) {
                $definition = $existingCodes[$code]
                $indexNode = $definition.SelectSingleNode('Index')
                $toolIndex = [int]$indexNode.InnerText
            } else {
                while ($usedIndexes.ContainsKey([string]$nextIndex)) { $nextIndex++ }
                $toolIndex = $nextIndex
                $usedIndexes[[string]$toolIndex] = $true
                $nextIndex++
                $definition = $library.CreateElement('ToolDefinition')
                $library.ToolLibrary.AppendChild($definition) | Out-Null
            }

            $scale = if ($unit -eq 'in') { 1.0 / 25.4 } else { 1.0 }
            $diameter = [double]$tool.DiameterMm * $scale
            $shank = [double]$tool.ShankMm * $scale
            $fluteLength = [double]$tool.FluteLengthMm * $scale
            $length = [double]$tool.LengthMm * $scale
            $decimals = if ($unit -eq 'in') { '0.0000' } else { '0.00' }
            $sizeLabel = $diameter.ToString($decimals, [Globalization.CultureInfo]::InvariantCulture)
            $unitLabel = if ($unit -eq 'in') { 'in' } else { 'mm' }
            $name = if ($unit -eq 'in') { '{0} 60deg d{1}" {2} 4F' -f ([string]$tool.Name).Substring(0,5), $sizeLabel, [string]$tool.PitchLabel } else { [string]$tool.Name }
            $definition.SetAttribute('Name', $name)

            $values = [ordered]@{
                Index = $toolIndex; Diameter = $diameter; ToolProfile = 'EndMill'; Flutes = [int]$tool.Flutes
                FluteLength = $fluteLength; Length = $length; ShankDiameter = $shank; HelixAngle = 0
                VeeAngle = 60; MaxRampAngle = 0; ToothLoad = 0; AxialDepthOfCut = 0; RadialDepthOfCut = 0
                Comment = "$code | 60 degree single-profile thread mill";
                Notes = "Supported pitch range: $($tool.PitchLabel). Catalog range is manufacturer capability.";
                Material = 'Carbide'; Coating = ''; PartCode = $code
            }
            foreach ($entry in $values.GetEnumerator()) {
                $node = $definition.SelectSingleNode([string]$entry.Key)
                if (-not $node) { $node = $library.CreateElement([string]$entry.Key); $definition.AppendChild($node) | Out-Null }
                $node.InnerText = [string]$entry.Value
            }
        }

        if ($original -ne $library.OuterXml) {
            $backupPath = "$libraryPath.before-tapping-tools.bak"
            if (-not (Test-Path -LiteralPath $backupPath)) { Copy-Item -LiteralPath $libraryPath -Destination $backupPath }
            $settings = New-Object System.Xml.XmlWriterSettings
            $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
            $settings.Indent = $true
            $writer = [System.Xml.XmlWriter]::Create($libraryPath, $settings)
            try { $library.Save($writer) } finally { $writer.Dispose() }
            $updatedLibraries += $libraryPath
        }
    }
}

Write-Host "Installed Tapping.dll and ThreadMillCatalog.xml to $destination"
if ($updatedLibraries.Count -eq 0) { Write-Host 'Thread-mill tools were already present in the discovered default libraries.' }
else { $updatedLibraries | ForEach-Object { Write-Host "Updated tool library: $_" } }
Write-Host 'Start or restart CamBam and check Plugins > Tapping...'
pause
