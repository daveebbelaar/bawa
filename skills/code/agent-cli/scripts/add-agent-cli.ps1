# Adds the agent CLI and MCP server to an app made from the bawa template.
# Usage: add-agent-cli.ps1 <app-folder> <cli-name>
#   add-agent-cli.ps1 $HOME\Developer\Tasks tasks
# Copies src\<App>\Agent\*.cs (core, pipe, commands, handlers, host) and the console project
# src\<App>.Cli, adds that project to the .slnx, starts the pipe in App.xaml.cs, and makes
# build.ps1 install %USERPROFILE%\.local\bin\<cli-name>.exe.
# The example commands fit the template's Item model. Replace them with the app's own
# (agent-cli skill, step 3).
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$AppFolder,
    [Parameter(Mandatory = $true, Position = 1)][string]$CliName
)
$ErrorActionPreference = 'Stop'
if ($CliName -cnotmatch '^[a-z][a-z0-9-]*$') { Write-Output 'cli-name must be lowercase letters, digits, and dashes, like tasks or mail'; exit 1 }
$template = Join-Path (Split-Path $PSScriptRoot -Parent) 'template'
$app = (Resolve-Path $AppFolder).Path
$slnx = Get-ChildItem $app -Filter '*.slnx' | Select-Object -First 1
if (-not $slnx) { Write-Output "no .slnx in ${app}: is this an app from the bawa template?"; exit 1 }
$name = $slnx.BaseName
$src = Join-Path $app "src\$name"
$appCs = Join-Path $src 'App.xaml.cs'
$build = Join-Path $app 'build.ps1'
if (-not (Test-Path $appCs)) { Write-Output "no $appCs`: is this an app from the bawa template?"; exit 1 }
if (Test-Path (Join-Path $src 'Agent')) { Write-Output "$src\Agent already exists"; exit 1 }
if (Test-Path (Join-Path $app "src\$name.Cli")) { Write-Output "src\$name.Cli already exists"; exit 1 }

$utf8 = New-Object Text.UTF8Encoding $false
function Copy-Filled([string]$From, [string]$To) {
    $text = [IO.File]::ReadAllText($From).Replace('__APP_NAME__', $name).Replace('__CLI_NAME__', $CliName)
    New-Item -ItemType Directory -Force (Split-Path $To -Parent) | Out-Null
    [IO.File]::WriteAllText($To, $text, $utf8)
}
Get-ChildItem (Join-Path $template 'Agent') -Filter '*.cs' | ForEach-Object { Copy-Filled $_.FullName (Join-Path $src "Agent\$($_.Name)") }
Get-ChildItem (Join-Path $template 'Cli') -File | ForEach-Object {
    Copy-Filled $_.FullName (Join-Path $app ("src\$name.Cli\" + $_.Name.Replace('__APP_NAME__', $name)))
}

# The solution builds the CLI too.
$sln = [IO.File]::ReadAllText($slnx.FullName)
$line = "    <Project Path=`"src/$name.Cli/$name.Cli.csproj`" />"
if ($sln -notmatch [regex]::Escape("$name.Cli.csproj")) {
    $core = "<Project Path=`"src/$name.Core/$name.Core.csproj`" />"
    $eol = if ($sln.Contains("`r`n")) { "`r`n" } else { "`n" }
    if ($sln.Contains($core)) { $sln = $sln.Replace($core, "$core$eol$line") } else { Write-Output "note: add $line to $($slnx.Name) by hand" }
    [IO.File]::WriteAllText($slnx.FullName, $sln, $utf8)
}

# App.xaml.cs: start the pipe after the window exists, and open in the background when the CLI started the app.
$code = [IO.File]::ReadAllText($appCs)
$started = $false
$activate = [regex]'(?m)^([ \t]*)MainWindow\.Activate\(\);(\r?\n)'
$found = $activate.Match($code)
if ($code -notmatch 'AgentHost\.Start' -and $found.Success) {
    $indent = $found.Groups[1].Value
    $eol = $found.Groups[2].Value
    $lines = "${indent}AgentHost.Activate(MainWindow);   // minimized when the agent CLI started the app$eol" +
        "${indent}AgentHost.Start(Model, MainWindow);  // the agent CLI's pipe$eol"
    $code = $code.Substring(0, $found.Index) + $lines + $code.Substring($found.Index + $found.Length)
    if ($code -notmatch "using $name\.Agent;") { $code = "using $name.Agent;$eol" + $code }
    [IO.File]::WriteAllText($appCs, $code, $utf8)
    $started = $true
}

# build.ps1 installs the CLI on PATH.
$script = [IO.File]::ReadAllText($build)
if ($script -notmatch [regex]::Escape(".local\bin")) {
    $block = @'
# The agent CLI (bawa agent-cli skill): one console exe in %USERPROFILE%\.local\bin, on PATH.
# dotnet publish is fine here: the CLI is a plain console app, without .pri or XAML files.
$cliName = '__CLI_NAME__'
$cliOut = Join-Path $env:LOCALAPPDATA "Dev\$name\cli"
$output = dotnet publish "$here\src\$name.Cli\$name.Cli.csproj" -c $config -r "win-$arch" --self-contained false -p:PublishSingleFile=true -o $cliOut --nologo "-p:SourceRevisionId=$stamp" 2>&1
$code = $LASTEXITCODE
$output | Where-Object { $_ -match ': (error|warning) ' } | ForEach-Object { ($_ -replace '\s+\[[^\]]+\.csproj.*\]$', '').Trim() } | Sort-Object -Unique
if ($code -ne 0) { Write-Output 'cli build failed'; exit 1 }
$cliBin = Join-Path $HOME '.local\bin'
New-Item -ItemType Directory -Force $cliBin | Out-Null
$cliExe = Join-Path $cliBin "$cliName.exe"
# An MCP client may be running the old CLI. Windows cannot overwrite a running exe, but it can rename it.
Get-ChildItem $cliBin -Filter "$cliName.exe.*.old" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path $cliExe) {
    try { Remove-Item $cliExe -Force -ErrorAction Stop } catch { Move-Item $cliExe "$cliExe.$stamp.old" -Force }
}
Copy-Item (Join-Path $cliOut "$cliName.exe") $cliExe
Write-Output "installed $cliExe"
if (($env:PATH -split ';') -notcontains $cliBin) { Write-Output "note: $cliBin is not on PATH. Add it to the user Path, then open a new terminal" }

'@
    $eol = if ($script.Contains("`r`n")) { "`r`n" } else { "`n" }
    $block = ($block.Replace('__CLI_NAME__', $CliName) -replace "`r?`n", $eol) + $eol
    $anchor = 'if ($Open)'
    $at = $script.IndexOf($anchor)
    if ($at -ge 0) {
        $script = $script.Substring(0, $at) + $block + $script.Substring($at)
        [IO.File]::WriteAllText($build, $script, $utf8)
    } else {
        Write-Output 'note: add the CLI install block to build.ps1 by hand (see the agent-cli skill)'
    }
}

Write-Output "added the agent CLI to $name as $CliName. Next (agent-cli skill):"
Write-Output "  3. replace the example commands in src\$name\Agent\AgentCommands.cs and AgentHandlers.cs"
if (-not $started) { Write-Output "  4. in App.xaml.cs OnLaunched, after new MainWindow: AgentHost.Activate(MainWindow); AgentHost.Start(Model, MainWindow);" }
Write-Output "  5. ./build.ps1, then $CliName help"
