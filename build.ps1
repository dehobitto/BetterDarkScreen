param([switch]$Small)
$ErrorActionPreference = 'Stop'
$a = @('publish', "$PSScriptRoot\src\BetterDarkScreen.csproj", '-c', 'Release', '-o', "$PSScriptRoot\publish")
if ($Small) { $a += '-p:EnableCompressionInSingleFile=true' }
dotnet @a
Get-Item "$PSScriptRoot\publish\BetterDarkScreen.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
