# Icon\AppIcon.svg -> src\Snap\Assets\AppIcon.ico + AppIcon.png, and a review sheet.
# Usage: tools/make-icon.ps1            build.ps1 runs it when the SVG changed
#        tools/make-icon.ps1 -Review    also writes %TEMP%\Snap-icon-review.png: every size on light and dark
param([switch]$Review)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = 'Snap'
$toolArgs = @("$root\Icon\AppIcon.svg", "$root\src\$name\Assets")
if ($Review) { $toolArgs += (Join-Path $env:TEMP "$name-icon-review.png") }
dotnet run --project "$PSScriptRoot\IconTool\IconTool.csproj" -c Release -v q '--' @toolArgs
exit $LASTEXITCODE
