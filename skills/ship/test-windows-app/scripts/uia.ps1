# Reads and drives a running app from outside through UI Automation, the way Narrator sees it.
# Usage: uia.ps1 -Process Tasks                      list every named control: type, name, enabled, shortcut
#        uia.ps1 -Process Tasks -Invoke "New item"   press a button or menu item by name
#        uia.ps1 -Process Tasks -Select "Groceries"  select a list row by name
# Works in Windows PowerShell 5.1. Run it against a seeded test copy (tools/snap.ps1 settings), never real data.
param(
    [Parameter(Mandatory = $true)][string]$Process,
    [string]$Invoke = '',
    [string]$Select = ''
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process -Name $Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "no window for process $Process"; exit 1 }
$root = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$all = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)

function Find-Named([string]$name) {
    foreach ($e in $all) { if ($e.Current.Name -eq $name) { return $e } }
    return $null
}

if ($Invoke) {
    $e = Find-Named $Invoke
    if (-not $e) { Write-Output "FAIL no control named '$Invoke'"; exit 1 }
    if (-not $e.Current.IsEnabled) { Write-Output "FAIL '$Invoke' is disabled"; exit 1 }
    $pattern = $null
    if ($e.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    elseif ($e.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { $pattern.Toggle() }
    else { Write-Output "FAIL '$Invoke' cannot be invoked"; exit 1 }
    Write-Output "PASS invoked '$Invoke'"
    exit 0
}

if ($Select) {
    $e = Find-Named $Select
    if (-not $e) { Write-Output "FAIL no row named '$Select'"; exit 1 }
    $pattern = $null
    # A row's name can sit on its text; walk up to the list item that can be selected.
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    while ($e -and -not $e.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $e = $walker.GetParent($e) }
    if (-not $pattern) { Write-Output "FAIL '$Select' is not selectable"; exit 1 }
    $pattern.Select()
    Write-Output "PASS selected '$Select'"
    exit 0
}

foreach ($e in $all) {
    $c = $e.Current
    if (-not $c.Name) { continue }
    $type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
    $state = if ($c.IsEnabled) { 'enabled' } else { 'disabled' }
    $keys = if ($c.AcceleratorKey) { " [$($c.AcceleratorKey)]" } else { '' }
    Write-Output ("{0,-12} {1,-9} {2}{3}" -f $type, $state, $c.Name, $keys)
}
