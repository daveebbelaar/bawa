# Creates a new Windows app from the template next to this script.
# Usage: new-app.ps1 <AppName> [parent-folder]
#   new-app.ps1 Tasks $HOME\Developer
# Creates <parent-folder>\<AppName>. If that folder exists, it must not have a .slnx yet;
# existing files (like docs\BRIEF.md) are kept.
# AppName must be one word in UpperCamelCase (letters and digits only).
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Parent = (Get-Location).Path
)
$ErrorActionPreference = 'Stop'
if ($Name -cnotmatch '^[A-Z][A-Za-z0-9]*$') { Write-Output 'AppName must be UpperCamelCase letters and digits, like Tasks or DailyLog'; exit 1 }
$template = Join-Path (Split-Path $PSScriptRoot -Parent) 'template'
$dest = Join-Path $Parent $Name
if (Test-Path (Join-Path $dest "$Name.slnx")) { Write-Output "$dest already has a $Name.slnx"; exit 1 }

New-Item -ItemType Directory -Force $dest | Out-Null
$text = '.cs', '.xaml', '.csproj', '.slnx', '.props', '.ps1', '.md', '.manifest', '.svg', '.json', '.gitignore', '.gitattributes'
Get-ChildItem $template -Recurse -File -Force | ForEach-Object {
    $relative = $_.FullName.Substring($template.Length + 1).Replace('__APP_NAME__', $Name)
    $target = Join-Path $dest $relative
    if (Test-Path $target) { return }   # keep what is already there
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    if ($text -contains $_.Extension -or $_.Name -like '.git*') {
        $content = [IO.File]::ReadAllText($_.FullName).Replace('__APP_NAME__', $Name)
        [IO.File]::WriteAllText($target, $content, (New-Object Text.UTF8Encoding $false))
    } else {
        Copy-Item $_.FullName $target
    }
}

Push-Location $dest
try {
    if (-not (Test-Path .git)) { git init -q -b main }
    git add -A
    git commit -qm "Create $Name from the Windows app template"
} finally { Pop-Location }
Write-Output "created $dest"
Write-Output "next: cd `"$dest`"; ./build.ps1 -Open"
