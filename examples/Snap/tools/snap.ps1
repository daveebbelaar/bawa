# Snapshots of every page in light and dark, with seed data, on a fresh test store.
# Usage: tools/snap.ps1                     every page and Settings
#        tools/snap.ps1 -Pages all,settings
#        tools/snap.ps1 -Accent green       another accent from UI/Theme.cs
#        tools/snap.ps1 -Grid               draw the grid and the layout probes
# Writes %TEMP%\Snap-shots\<page>-light.png and -dark.png, each with a -layout.json.
param([string]$Pages = '', [string]$Accent = '', [switch]$Grid)
. "$PSScriptRoot\common.ps1"
$store = Join-Path $env:TEMP "$AppName-test.db"
$shots = Join-Path $env:TEMP "$AppName-shots"
# Seed data only fills an empty store, so start from nothing.
Remove-Store $store
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
$vars = @{ APP_STORE = $store; APP_SEED = '1'; APP_SNAPSHOT = $shots }
if ($Pages) { $vars.APP_SNAPSHOT_PAGES = $Pages }
if ($Accent) { $vars.APP_ACCENT = $Accent }
if ($Grid) { $vars.APP_GRID = '1' }
$code = Invoke-App -Environment $vars
exit $code
