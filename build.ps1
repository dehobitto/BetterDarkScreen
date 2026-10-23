param([switch]$Small)
$ErrorActionPreference = 'Stop'

$proj = "$PSScriptRoot\src\BetterDarkScreen.csproj"
$out = "$PSScriptRoot\publish"

# Version comes from <Version> in the .csproj (same source CI uses for releases).
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $proj" }
$version = $version.Trim()

$a = @('publish', $proj, '-c', 'Release', '-o', $out)
if ($Small) { $a += '-p:EnableCompressionInSingleFile=true' }
dotnet @a
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# publish\BetterDarkScreen.exe -> publish\BetterDarkScreen-<version>-win-x64[-small].exe (same names as CI)
$suffix = if ($Small) { '-small' } else { '' }
$exe = Join-Path $out "BetterDarkScreen-$version-win-x64$suffix.exe"
Move-Item (Join-Path $out 'BetterDarkScreen.exe') $exe -Force
Get-Item $exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
