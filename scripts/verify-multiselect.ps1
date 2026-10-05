<#
Repeatable acceptance check for multi-select path list (FR-21).

Usage:  powershell -ExecutionPolicy Bypass -File scripts\verify-multiselect.ps1
Setup:  .\scripts\dev.ps1 build   (build first)

How it works: pushes two files and one text item into the shelf through the collect
global hotkey (Ctrl+Alt+C = WM_HOTKEY id 0x5454), then drives the real UI through UI
Automation plus real mouse clicks: enter multi-select, tick every item, click the
"copy path list" button and compare the clipboard with the expected absolute paths.
The text item must be skipped.

Ticking the items uses real clicks on purpose: UI Automation's SelectionItemPattern
Select() replaces the previous selection even when the list is in multi-select mode,
so it cannot produce a multi-selection.

The existing state.json is backed up first and restored afterwards, so the check does
not destroy whatever is on the shelf right now.

IMPORTANT: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 files without a
BOM as ANSI (GBK on zh-CN systems) and non-ASCII text breaks parsing. Chinese UI labels
are built from code points instead.
#>
param(
    [string] $StateFile = (Join-Path $env:LOCALAPPDATA 'TransitShelf\state.json'),
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TransitShelf.App\bin\Debug\net8.0-windows\TransitShelf.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Verify -Name Api -MemberDefinition @'
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
public static void ClickAt(int x, int y) {
  SetCursorPos(x, y);
  System.Threading.Thread.Sleep(150);
  mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
  System.Threading.Thread.Sleep(60);
  mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
}
public static IntPtr FindToolWindow(int pid) {
  IntPtr found = IntPtr.Zero;
  EnumWindows(delegate(IntPtr h, IntPtr l) {
    uint p; GetWindowThreadProcessId(h, out p);
    if (p == (uint)pid && IsWindowVisible(h) && (GetWindowLong(h, -20) & 0x80) != 0) { found = h; return false; }
    return true;
  }, IntPtr.Zero);
  return found;
}
'@

# UI labels, spelled out by code point (see the header note about ASCII-only scripts):
#   U+591A U+9009 = "multi select"      U+5B8C U+6210 = "done"
#   U+590D U+5236 U+8DEF U+5F84 U+6E05 U+5355 = "copy path list"
#   U+5DF2 U+9009 ... U+9879 = "selected N items"
#   U+5DF2 U+590D U+5236 = "copied" (status line prefix)
$labelMultiSelect = -join ([char]0x591A, [char]0x9009)
$labelCopyPaths = -join ([char]0x590D, [char]0x5236, [char]0x8DEF, [char]0x5F84, [char]0x6E05, [char]0x5355)
$labelDone = -join ([char]0x5B8C, [char]0x6210)
$countSelectedPrefix = -join ([char]0x5DF2, [char]0x9009)
$countSelectedSuffix = -join ([char]0x9879)
$countSelectedLabel = $countSelectedPrefix + ' 3 ' + $countSelectedSuffix
$copiedPrefix = (-join ([char]0x5DF2, [char]0x590D, [char]0x5236)) + ' 2'

# U+9A8C U+8BC1 U+6587 U+672C = "verify text" in Chinese
$chineseText = -join ([char]0x9A8C, [char]0x8BC1, [char]0x6587, [char]0x672C)

$hotKeyCollect = 0x5454
$cacheDir = Join-Path (Split-Path -Parent $StateFile) 'cache'
$backupFile = "$StateFile.verify-backup"
$results = New-Object System.Collections.Generic.List[string]

function Stop-App {
    # The app is single-instance: a survivor of an earlier run would keep the shelf and the
    # hotkeys, and the freshly started copy would silently exit. Make sure it is really gone.
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $running = @(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) {
            Start-Sleep -Milliseconds 500
            return
        }
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    throw 'a TransitShelf process survived Stop-App; refusing to continue'
}

function Start-App {
    $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) | Out-Null
    Start-Sleep -Seconds 5
}

# Park the cursor away from the panel: a leftover hover can expand or collapse the window
# and make the run depend on where the previous run left the pointer.
function Reset-Cursor { [void][Verify.Api]::SetCursorPos(40, 40) }

function Invoke-Collect {
    $process = Get-Process -Name TransitShelf | Select-Object -First 1
    $handle = [Verify.Api]::FindToolWindow($process.Id)
    [void][Verify.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyCollect, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
}

# The clipboard is a shared resource; another process may hold it briefly.
function Set-ClipboardSafe([scriptblock] $Action) {
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            & $Action
            return
        }
        catch [System.Runtime.InteropServices.ExternalException] {
            Start-Sleep -Milliseconds 300
        }
    }
    throw 'clipboard stayed locked; aborting'
}

function Get-ClipboardTextSafe {
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            return [System.Windows.Forms.Clipboard]::GetText()
        }
        catch [System.Runtime.InteropServices.ExternalException] {
            Start-Sleep -Milliseconds 300
        }
    }
    throw 'clipboard stayed locked; aborting'
}

# ---------- UI Automation helpers ----------

function Get-Panel {
    $process = Get-Process -Name TransitShelf | Select-Object -First 1
    $handle = [Verify.Api]::FindToolWindow($process.Id)
    if ($handle -eq [IntPtr]::Zero) { throw 'tool window not found' }
    return [System.Windows.Automation.AutomationElement]::FromHandle($handle)
}

function Find-Element($panel, $property, $value) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
    return $panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-Button($panel, [string] $name) {
    return Find-Element $panel ([System.Windows.Automation.AutomationElement]::NameProperty) $name
}

function Invoke-Button($panel, [string] $name) {
    $button = Find-Button $panel $name
    if (-not $button) {
        return $false
    }
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 600
    return $true
}

function Get-ItemElements($panel) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Select-AllItems($panel) {
    $items = Get-ItemElements $panel
    for ($index = 0; $index -lt $items.Count; $index++) {
        $rect = $items[$index].Current.BoundingRectangle
        # 24px from the left edge: on the icon, clear of the hover buttons and the pin button.
        [Verify.Api]::ClickAt([int]($rect.Left + 24), [int]($rect.Top + ($rect.Height / 2)))

        # Wait for the counter instead of sleeping blindly; UIA and the click both need a moment.
        $want = $countSelectedPrefix + (' ' + ($index + 1) + ' ') + $countSelectedSuffix
        if (-not (Wait-ForText $panel $want 2000)) {
            return $index
        }
    }
    return $items.Count
}

function Wait-ForText($panel, [string] $name, [int] $timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        if (Find-Element $panel ([System.Windows.Automation.AutomationElement]::NameProperty) $name) {
            return $true
        }
        Start-Sleep -Milliseconds 120
    }
    return $false
}

function Wait-ForTextStartingWith($panel, [string] $prefix, [int] $timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)

    while ((Get-Date) -lt $deadline) {
        foreach ($node in $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            if ($node.Current.Name -like "$prefix*") { return $true }
        }
        Start-Sleep -Milliseconds 120
    }
    return $false
}

function Wait-ForClipboardPath([int] $timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        $text = Get-ClipboardTextSafe
        if ($text -and ($text -notlike '*SENTINEL*')) { return $text }
        Start-Sleep -Milliseconds 200
    }
    return (Get-ClipboardTextSafe)
}

# ---------- run ----------

Reset-Cursor
Stop-App
if (Test-Path -LiteralPath $backupFile) { Remove-Item -LiteralPath $backupFile -Force }
if (Test-Path $StateFile) { Copy-Item -LiteralPath $StateFile -Destination $backupFile -Force }
if (Test-Path $StateFile) { Remove-Item -LiteralPath $StateFile -Force }

# Start from a clean error log so a failure dump points at this run only.
$errorLog = Join-Path (Split-Path -Parent $StateFile) 'error.log'
if (Test-Path $errorLog) { Remove-Item -LiteralPath $errorLog -Force }

$tempOne = Join-Path $env:TEMP 'transitshelf-verify-multi-one.txt'
$tempTwo = Join-Path $env:TEMP 'transitshelf-verify-multi-two.txt'
Set-Content -LiteralPath $tempOne -Value 'verify one' -Encoding UTF8
Set-Content -LiteralPath $tempTwo -Value 'verify two' -Encoding UTF8

try {
    Start-App

    # Two file items through the collect hotkey...
    $list = New-Object System.Collections.Specialized.StringCollection
    [void]$list.Add($tempOne)
    [void]$list.Add($tempTwo)
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetFileDropList($list) }
    Invoke-Collect

    # ...plus one text item, which must NOT end up in the path list.
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetText($chineseText) }
    Invoke-Collect

    $panel = Get-Panel

    # Collecting through the hotkey must leave the panel expanded; otherwise every later
    # step would fail for the wrong reason, so check it explicitly.
    $panelReady = [bool](Find-Button $panel $labelMultiSelect)
    $results.Add('precondition panel expanded     : ' + $(if ($panelReady) { 'PASS' } else { 'FAIL' }))

    $entered = Invoke-Button $panel $labelMultiSelect
    $results.Add('FR-21 enter multi-select mode   : ' + $(if ($entered) { 'PASS' } else { 'FAIL' }))

    $selectedCount = Select-AllItems $panel
    $results.Add('FR-21 tick every item           : ' + $(if ($selectedCount -eq 3) { "PASS ($selectedCount)" } else { "FAIL ($selectedCount)" }))

    $counterOk = Wait-ForText $panel $countSelectedLabel 2000
    $results.Add('FR-21 batch bar shows count     : ' + $(if ($counterOk) { 'PASS' } else { 'FAIL' }))

    # Put a sentinel on the clipboard first: a stale file-drop clipboard can also be read
    # back as text, so "the clipboard holds the paths" only proves the app copied if the
    # previous content is gone.
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetText('SENTINEL-NOT-A-PATH') }
    Start-Sleep -Milliseconds 400

    $copied = Invoke-Button $panel $labelCopyPaths
    $statusOk = Wait-ForTextStartingWith $panel $copiedPrefix 2500
    $results.Add('FR-21 status reports success    : ' + $(if ($statusOk) { 'PASS' } else { 'FAIL' }))

    $clipboardText = Wait-ForClipboardPath 2500
    $lines = @($clipboardText -split "`r`n|`n" | Where-Object { $_ -ne '' })

    $expected = @($tempOne, $tempTwo)
    $pathsOk = ($lines.Count -eq 2) -and
        (@($lines | Where-Object { $expected -contains $_ }).Count -eq 2) -and
        (@($lines | Where-Object { $_ -notmatch '^[A-Za-z]:\\' }).Count -eq 0) -and
        ($clipboardText -notlike '*SENTINEL*')
    $results.Add('FR-21 clipboard = 2 abs paths   : ' + $(if ($copied -and $pathsOk) { 'PASS' } else { "FAIL -> [$clipboardText]" }))

    $textSkipped = -not ($clipboardText -like "*$chineseText*")
    $results.Add('FR-21 text item skipped         : ' + $(if ($textSkipped) { 'PASS' } else { 'FAIL' }))

    $exited = Invoke-Button $panel $labelDone
    Start-Sleep -Milliseconds 300
    $backToNormal = [bool](Find-Button $panel $labelMultiSelect)
    $results.Add('FR-21 done restores header      : ' + $(if ($exited -and $backToNormal) { 'PASS' } else { 'FAIL' }))

    # Leave the panel in its normal state and let the app write its state file back.
    Start-Sleep -Milliseconds 800

    if (-not $backToNormal) {
        Write-Host '--- diagnostics for the failed "done" check ---'
        Write-Host ("app alive          : {0}" -f [bool](Get-Process -Name TransitShelf -ErrorAction SilentlyContinue))
        if (Test-Path $errorLog) {
            Write-Host '--- error.log ---'
            Get-Content $errorLog | Select-Object -First 25 | ForEach-Object { Write-Host "  $_" }
            Write-Host '---'
        }
    }
}
finally {
    Reset-Cursor
    Stop-App
}

# ---------- cleanup ----------
if (Test-Path -LiteralPath $backupFile) {
    Move-Item -LiteralPath $backupFile -Destination $StateFile -Force
}
elseif (Test-Path $StateFile) {
    Remove-Item -LiteralPath $StateFile -Force
}

foreach ($temp in @($tempOne, $tempTwo)) {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}

if (Test-Path $cacheDir) {
    Get-ChildItem $cacheDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -match 'FAIL' }).Count
Write-Host ''
if ($failed -eq 0) {
    Write-Host "ALL PASS ($($results.Count) checks)" -ForegroundColor Green
} else {
    Write-Host "$failed check(s) FAILED" -ForegroundColor Red
}
exit $failed
