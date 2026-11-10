<#
Repeatable acceptance check for the collection entry points
(FR-13 clipboard collect / FR-12 sourceless image / FR-72 capacity setting).

Usage:  powershell -ExecutionPolicy Bypass -File scripts\verify-collect.ps1
Setup:  .\scripts\dev.ps1 build   (build first)

How it works: the script puts content on the clipboard, sends the window message that
matches the "collect" global hotkey (Ctrl+Alt+C = WM_HOTKEY id 0x5454), then checks
state.json for the expected item.

IMPORTANT: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 files without a
BOM as ANSI (GBK on zh-CN systems) and non-ASCII text breaks parsing. The Chinese test
payload is built from code points instead.
#>
param(
    [string] $StateFile,
    [string] $Exe
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification-context.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Verify -Name Api -MemberDefinition @'
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
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

# U+9A8C U+8BC1 U+6587 U+672C = "verify text" in Chinese
$chineseText = -join ([char]0x9A8C, [char]0x8BC1, [char]0x6587, [char]0x672C)

$hotKeyCollect = 0x5454
$cacheDir = Join-Path (Split-Path -Parent $StateFile) 'cache'
$backupDir = Join-Path $env:TEMP ('transitshelf-collect-' + [Guid]::NewGuid().ToString('n').Substring(0, 8))
$backupState = Join-Path $backupDir 'state.json'
$backupCache = Join-Path $backupDir 'cache'
$hadState = $false
$hadCache = $false
$results = New-Object System.Collections.Generic.List[string]

function Stop-App {
    Get-Process -Name TransitShelf -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}

# The real state.json / cache belong to the user. Back them up before the test
# writes anything, and put them back afterwards (same contract as verify-smoke).
function Backup-Data {
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    if (Test-Path -LiteralPath $StateFile) {
        Copy-Item -LiteralPath $StateFile -Destination $backupState -Force -ErrorAction Stop
        $script:hadState = $true
    }
    if (Test-Path -LiteralPath $cacheDir) {
        Copy-Item -LiteralPath $cacheDir -Destination $backupCache -Recurse -Force -ErrorAction Stop
        $script:hadCache = $true
    }
}

function Restore-Data {
    Stop-App
    if ($script:hadState) {
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                Copy-Item -LiteralPath $backupState -Destination $StateFile -Force -ErrorAction Stop
                break
            }
            catch {
                Start-Sleep -Milliseconds 300
            }
        }
    }
    elseif (Test-Path -LiteralPath $StateFile) {
        Remove-Item -LiteralPath $StateFile -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $cacheDir) {
        Get-ChildItem -LiteralPath $cacheDir -File -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    if ($script:hadCache) {
        Get-ChildItem -LiteralPath $backupCache -File -ErrorAction SilentlyContinue |
            Copy-Item -Destination $cacheDir -Force
    }
    Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Start-App {
    Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 5
}

function Invoke-Collect {
    $process = Get-Process -Name TransitShelf | Select-Object -First 1
    $handle = [Verify.Api]::FindToolWindow($process.Id)
    [void][Verify.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyCollect, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
}

function Read-State {
    Start-Sleep -Milliseconds 800
    if (-not (Test-Path $StateFile)) { return $null }
    return (Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json)
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

Stop-App
Backup-Data

try {
# ---------- 1. clipboard text (Chinese payload) ----------
Start-App
Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetText($chineseText) }
Invoke-Collect
$state = Read-State
$hit = $state.items | Where-Object { $_.text -eq $chineseText }
$results.Add('FR-13 collect clipboard text      : ' + $(if ($hit) { 'PASS' } else { 'FAIL' }))

# ---------- 2. clipboard file ----------
$tempFile = Join-Path $env:TEMP 'transitshelf-verify-file.txt'
Set-Content -LiteralPath $tempFile -Value 'verify' -Encoding UTF8
$list = New-Object System.Collections.Specialized.StringCollection
[void]$list.Add($tempFile)
Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetFileDropList($list) }
Invoke-Collect
$state = Read-State
$hit = $state.items | Where-Object { $_.path -eq $tempFile }
$results.Add('FR-13 collect clipboard file      : ' + $(if ($hit) { 'PASS' } else { 'FAIL' }))

# ---------- 3. clipboard image -> cached PNG ----------
$bitmap = New-Object System.Drawing.Bitmap 120, 80
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::CornflowerBlue)
$graphics.Dispose()
Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetImage($bitmap) }
Invoke-Collect
$state = Read-State
$imageItem = $state.items | Where-Object { $_.cachePath }
$cacheOk = $false
if ($imageItem) { $cacheOk = Test-Path -LiteralPath $imageItem.cachePath }
$results.Add('FR-12 sourceless image -> PNG     : ' + $(if ($cacheOk) { 'PASS' } else { 'FAIL' }))

# ---------- 4. capacity setting (settings.capacity = 3) ----------
Stop-App
$state = Read-State
$state.settings.capacity = 3
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $StateFile -Encoding UTF8
Start-App
foreach ($i in 1..5) {
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetText("capacity-$i") }
    Invoke-Collect
}
$state = Read-State
$count = @($state.items).Count
$results.Add('FR-72 capacity limit = 3          : ' + $(if ($count -eq 3) { "PASS ($count items)" } else { "FAIL ($count items)" }))

}
finally {
    if ($tempFile -and (Test-Path -LiteralPath $tempFile)) {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }
    Restore-Data
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
