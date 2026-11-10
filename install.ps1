param([string]$Destination = "$env:APPDATA\Autodesk\Revit\Addins\2025")
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'src\PcfExporter.Revit\bin\Release\net8.0-windows'
if (!(Test-Path "$source\PcfExporter.Revit.dll")) { throw 'Build Release first: dotnet build src/PcfExporter.Revit/PcfExporter.Revit.csproj -c Release' }
$plugin = Join-Path $Destination 'PcfExporter'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
Copy-Item "$source\PcfExporter.*.dll" $plugin -Force
Copy-Item "$source\PcfExporter.Revit.deps.json" $plugin -Force
Copy-Item (Join-Path $PSScriptRoot 'PcfExporter.addin') $Destination -Force
Write-Host "Installed to $plugin. Mapping JSON is created here when saved. Existing mappings were preserved."
