# Checks an app's agent CLI against the agent-cli rules, on a test database.
# Usage: check-agent-cli.ps1 <cli>        the CLI name on PATH, or a path to the CLI exe
#   check-agent-cli.ps1 tasks
#   check-agent-cli.ps1 $HOME\.local\bin\tasks.exe
# Starts a test copy of the app (APP_STORE in %TEMP%), never the real one, and closes it
# at the end. Prints PASS or FAIL lines, and exits 1 on any FAIL.
param([Parameter(Mandatory = $true, Position = 0)][string]$Cli)
# Continue, not Stop: Windows PowerShell 5.1 turns a native command's stderr into errors under Stop.
$ErrorActionPreference = 'Continue'
$script:fails = 0
function Pass([string]$Text) { Write-Output "PASS $Text" }
function Fail([string]$Text) { Write-Output "FAIL $Text"; $script:fails++ }
function Read-Json([string[]]$Lines) {
    try { return (($Lines -join "`n") | ConvertFrom-Json) } catch { return $null }
}

if (-not (Get-Command $Cli -ErrorAction SilentlyContinue)) { Write-Output "FAIL no command $Cli on PATH. Run ./build.ps1"; exit 1 }

# Every process this script starts sees the test store, and so does the app the CLI starts.
$store = Join-Path $env:TEMP "agent-cli-check-$PID.db"
$oldStore = $env:APP_STORE
$env:APP_STORE = $store
$appPid = $null
$started = Get-Date
try {
    $out = & $Cli help 2>$null
    if ($LASTEXITCODE -eq 0 -and $out) { Pass 'help' } else { Fail 'help exits 0 and prints the commands' }

    $schema = Read-Json (& $Cli help --json 2>$null)
    $commands = @($schema.commands)
    $good = $commands.Count -gt 0
    foreach ($c in $commands) {
        if (-not ($c.description -and $c.description.Contains('Example:') -and $c.inputSchema.type -eq 'object')) { $good = $false; Write-Output "  $($c.name): needs a summary, an example, and a schema" }
    }
    if ($good) { Pass 'help --json: every command has a summary, an example, and a schema' } else { Fail 'help --json lists every command with a summary, example, and schema' }

    & $Cli no-such-command 2>$null | Out-Null
    if ($LASTEXITCODE -eq 2) { Pass 'unknown command exits 2' } else { Fail "unknown command exits 2 (got $LASTEXITCODE)" }
    $err = Read-Json (& $Cli no-such-command --json 2>$null)
    if ($err.error.code -and $err.error.message) { Pass 'errors with --json are {"error":{code,message}} on stdout' } else { Fail 'errors with --json are {"error":{code,message}} on stdout' }

    $reads = @($commands | Where-Object { $_.readOnly -and @($_.inputSchema.required).Count -eq 0 } | ForEach-Object { $_.name })
    if ($reads.Count -gt 0) {
        $words = $reads[0] -split ' '
        & $Cli @words --no-launch --json 2>$null | Out-Null
        if ($LASTEXITCODE -eq 4) { Pass '--no-launch without a running app exits 4' } else { Fail "--no-launch without a running app exits 4 (got $LASTEXITCODE)" }
    }

    # Every read-only command without required params runs and prints one JSON object.
    # The first one starts the test copy of the app.
    foreach ($name in $reads) {
        $words = $name -split ' '
        $res = & $Cli @words --json 2>$null
        $code = $LASTEXITCODE
        if ($code -eq 0 -and (Read-Json $res)) { Pass "$name --json" } else { Fail "$name --json exits 0 with one JSON object (got exit $code)" }
    }

    $version = Read-Json (& $Cli version --json 2>$null)
    if ($version.app.running) {
        $appPid = $version.app.pid
        if ($version.app.version -eq $version.version) { Pass 'version: the CLI and the app are the same build' } else { Fail "version: the CLI is $($version.version), the app is $($version.app.version). Run ./build.ps1" }
    } else { Fail 'version --json reports the running test copy of the app' }

    $messages = @(
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"check","version":"1"}}}',
        '{"jsonrpc":"2.0","method":"notifications/initialized"}',
        '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
    )
    if ($reads.Count -gt 0) { $messages += '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"' + ($reads[0] -replace ' ', '_') + '","arguments":{}}}' }
    $replies = @($messages | & $Cli mcp 2>$null | Where-Object { $_.Trim() } | ForEach-Object { Read-Json $_ })
    $ok = $replies.Count -ge 3 -and $null -ne $replies[0].result.capabilities.tools
    $tools = if ($ok) { @($replies[1].result.tools) } else { @() }
    $ok = $ok -and $tools.Count -eq $commands.Count
    foreach ($t in $tools) {
        if ($t.name -notmatch '^[A-Za-z0-9_-]{1,64}$' -or $null -eq $t.annotations.readOnlyHint) { $ok = $false; Write-Output "  tool $($t.name): needs a short name and readOnlyHint" }
    }
    if ($ok) { Pass 'mcp: initialize and tools/list, one tool per command' } else { Fail 'mcp answers initialize and tools/list with one tool per command' }
    if ($reads.Count -gt 0) {
        $call = $replies | Where-Object { $_.id -eq 3 }
        if ($call -and -not $call.result.isError) { Pass "mcp: tools/call $($reads[0]) reaches the app" } else { Fail "mcp: tools/call $($reads[0]) returns a result without isError" }
    }
}
finally {
    # Close the test copy only: the process that answered on the test store's pipe.
    # Without a pid (version failed): a process the CLI started (--agent) since this script began.
    $pids = if ($appPid) { @($appPid) } else {
        @(Get-CimInstance Win32_Process -Filter "CommandLine LIKE '%--agent%'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationDate -ge $started } | ForEach-Object { $_.ProcessId })
    }
    foreach ($id in $pids) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $id -Timeout 10 -ErrorAction SilentlyContinue
    }
    # The files can stay locked for a moment after the process ends (a virus scan, the OS closing handles).
    $files = "$store", "$store-wal", "$store-shm", "$store.settings.json"
    for ($i = 0; $i -lt 20 -and ($files | Where-Object { Test-Path $_ }); $i++) {
        Remove-Item $files -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 250
    }
    $env:APP_STORE = $oldStore
}

if ($script:fails -eq 0) { Write-Output 'PASS agent CLI'; exit 0 }
Write-Output "FAIL agent CLI: $($script:fails) checks"
exit 1
