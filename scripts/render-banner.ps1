# Renders docs/assets/banner.html to banner-light.png and banner-dark.png (2560 x 1120),
# and docs/assets/icon.html to .claude-plugin/icon.png (1024 x 1024), with headless Edge.
# Usage: scripts/render-banner.ps1
# Edge logs to stderr; Windows PowerShell 5.1 would stop on that under 'Stop'.
$ErrorActionPreference = 'Continue'
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\assets'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { Write-Output 'Microsoft Edge not found'; exit 1 }
$page = ([Uri](Join-Path $assets 'banner.html')).AbsoluteUri
$profile = Join-Path $env:TEMP 'bawa-banner-edge'
foreach ($mode in 'light', 'dark') {
    $out = Join-Path $assets "banner-$mode.png"
    $url = if ($mode -eq 'dark') { "$page#dark" } else { $page }
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 --window-size=1280,560 `
        "--user-data-dir=$profile" "--screenshot=$out" $url 2>$null | Out-Null
    Start-Sleep -Milliseconds 500
    Write-Output "wrote $out"
}
# The plugin icon, 1024 x 1024 with a transparent background.
$icon = Join-Path (Split-Path $PSScriptRoot -Parent) '.claude-plugin\icon.png'
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 --window-size=512,512 --default-background-color=00000000 `n    "--user-data-dir=$profile" "--screenshot=$env:TEMP\bawa-icon.png" ([Uri](Join-Path $assets 'icon.html')).AbsoluteUri 2>$null | Out-Null
Start-Sleep -Milliseconds 500
# Edge cannot always write into dot folders; copy from %TEMP%.
Copy-Item "$env:TEMP\bawa-icon.png" $icon -Force
Write-Output "wrote $icon"
exit 0
