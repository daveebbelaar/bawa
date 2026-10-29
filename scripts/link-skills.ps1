# Links every skill in this repo into ~/.claude/skills and ~/.agents/skills,
# so edits in the repo show up in Claude Code and Codex at once.
# Uses directory junctions, which need no administrator rights or Developer Mode.
# Usage: scripts/link-skills.ps1
$repo = Split-Path $PSScriptRoot -Parent
foreach ($target in (Join-Path $HOME '.claude\skills'), (Join-Path $HOME '.agents\skills')) {
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($skill in Get-ChildItem (Join-Path $repo 'skills') -Directory | Get-ChildItem -Directory) {
        $link = Join-Path $target $skill.Name
        if (Test-Path $link) { (Get-Item $link).Delete() }
        New-Item -ItemType Junction -Path $link -Target $skill.FullName | Out-Null
        Write-Output $link
    }
}
