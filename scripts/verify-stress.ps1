<#
Repeatable stress check: a large batch of file references, rapid hotkeys, persistence,
crash recovery and zero-copy / zero-modification guarantees.

Usage:
  .\scripts\dev.ps1 build -c Release
  powershell -ExecutionPolicy Bypass -File scripts\verify-stress.ps1 `
      -Exe src\TransitShelf.App\bin\Release\net8.0-windows\TransitShelf.exe

Checks:
  FR-10 / NFR-09  120 files collected as references, capacity trims to 99, no copies on disk
  NFR-05          source file SHA-256 values unchanged
  NFR-09          private memory growth after the batch is reported (target <= 30 MB)
  NFR-06          repeated collect / toggle hotkeys do not crash the UI
  NFR-06          force kill + restart restores state and removes orphan cache

Note: -Exe defaults to the Debug build; pass the Release path for the memory number.
The real state.json and cache are backed up first and restored afterwards.

IMPORTANT: keep this file ASCII-only (see dev.ps1).
#>
param(
    [string] $StateFile = (Join-Path $env:LOCALAPPDATA 'TransitShelf\state.json'),
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TransitShelf.App\bin\Debug\net8.0-windows\TransitShelf.exe'),
    [int] $FileCount = 120,
    [int] $Capacity = 99
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

Add-Type -Namespace Stress -Name Api -MemberDefinition @'
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

$rootDir = Split-Path -Parent $StateFile
$cacheDir = Join-Path $rootDir 'cache'
$backupDir = Join-Path $env:TEMP ('transitshelf-stress-' + [Guid]::NewGuid().ToString('n').Substring(0, 8))
$workDir = Join-Path $env:TEMP ('transitshelf-stress-files-' + [Guid]::NewGuid().ToString('n').Substring(0, 8))
$hotKeyCollect = 0x5454
$hotKeyToggle = 0x5453
$results = New-Object System.Collections.Generic.List[string]

function Stop-App {
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $running = @(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) {
            Start-Sleep -Milliseconds 1200
            return
        }
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    throw 'a TransitShelf process survived Stop-App; refusing to continue'
}

# Antivirus / indexer can hold a freshly written state.json for a moment.
function Write-State([object] $state) {
    $json = $state | ConvertTo-Json -Depth 6
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Set-Content -LiteralPath $StateFile -Value $json -Encoding UTF8 -Force -ErrorAction Stop
            return
        }
        catch {
            Start-Sleep -Milliseconds 300
        }
    }
    throw 'state.json stayed locked; refusing to continue'
}

function Backup-Data {
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    if (Test-Path -LiteralPath $StateFile) {
        Copy-Item -LiteralPath $StateFile -Destination (Join-Path $backupDir 'state.json')
    }
    if (Test-Path -LiteralPath $cacheDir) {
        Copy-Item -LiteralPath $cacheDir -Destination (Join-Path $backupDir 'cache') -Recurse
    }
}

function Restore-Data {
    Stop-App
    if (Test-Path -LiteralPath (Join-Path $backupDir 'state.json')) {
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                Copy-Item -LiteralPath (Join-Path $backupDir 'state.json') -Destination $StateFile -Force -ErrorAction Stop
                break
            }
            catch {
                Start-Sleep -Milliseconds 300
            }
        }
    }
    if (Test-Path -LiteralPath $cacheDir) {
        Get-ChildItem -LiteralPath $cacheDir -File -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $cacheBackup = Join-Path $backupDir 'cache'
    if (Test-Path -LiteralPath $cacheBackup) {
        Get-ChildItem -LiteralPath $cacheBackup -File -ErrorAction SilentlyContinue |
            Copy-Item -Destination $cacheDir -Force
    }
    Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Start-App {
    $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    return (Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru)
}

function Wait-Window($process, [int] $timeoutMs) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $timeoutMs) {
        Start-Sleep -Milliseconds 50
        if ($process.HasExited) { return [IntPtr]::Zero }
        $handle = [Stress.Api]::FindToolWindow($process.Id)
        if ($handle -ne [IntPtr]::Zero) { return $handle }
    }
    return [IntPtr]::Zero
}

function Read-StateSafe {
    try {
        if (-not (Test-Path -LiteralPath $StateFile)) { return $null }
        return (Get-Content -LiteralPath $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

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

function Add-Check([string] $name, [bool] $passed, [string] $detail) {
    $results.Add($name.PadRight(42) + ': ' + $(if ($passed) { 'PASS' } else { 'FAIL' }) + ' ' + $detail)
}

Stop-App
Backup-Data

try {
    # ---------- prepare: empty shelf with a high capacity ----------
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null
    $files = New-Object System.Collections.Generic.List[string]
    $hashes = @{}
    for ($i = 0; $i -lt $FileCount; $i++) {
        $file = Join-Path $workDir ('stress-file-{0:D4}.txt' -f $i)
        Set-Content -LiteralPath $file -Value ('payload-' + $i) -Encoding ASCII
        $files.Add($file)
        $hashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    }

    $state = @{
        schemaVersion = 2
        settings = @{
            capacity = $Capacity
            timeToLiveHours = 24
            watchDownloads = $false
            watchWeChat = $false
            extraWatchDirectories = @()
        }
        items = @()
    }
    Write-State $state

    $process = Start-App
    $handle = Wait-Window $process 15000
    Start-Sleep -Seconds 2

    # Prime the expanded layout once: WPF allocates the expanded visual tree / render
    # resources on first show. Measuring that separately keeps NFR-09 about the references.
    [void][Stress.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyToggle, [IntPtr]::Zero)
    Start-Sleep -Seconds 8
    $process.Refresh()
    $basePrivate = $process.PrivateMemorySize64
    $baseWorkingSet = $process.WorkingSet64

    # ---------- 1. big batch of file references (FR-10 / NFR-09) ----------
    $list = New-Object System.Collections.Specialized.StringCollection
    foreach ($file in $files) { [void]$list.Add($file) }
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetFileDropList($list) }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    [void][Stress.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyCollect, [IntPtr]::Zero)
    $state = $null
    while ($watch.ElapsedMilliseconds -lt 15000) {
        Start-Sleep -Milliseconds 200
        $state = Read-StateSafe
        if ($state -and @($state.items).Count -ge $Capacity) { break }
    }
    $watch.Stop()
    $items = @($state.items)
    Add-Check 'FR-10 batch collected' ($items.Count -eq $Capacity) ($items.Count.ToString() + ' items in ' + $watch.ElapsedMilliseconds + ' ms (save debounce included)')
    Add-Check 'FR-16 capacity trim' (@($items | Where-Object { $_.groupId -eq $null }).Count -eq $Capacity) ''

    Start-Sleep -Seconds 3
    $process.Refresh()
    $batchPrivateMb = [math]::Round(($process.PrivateMemorySize64 - $basePrivate) / 1MB, 1)
    $batchWorkingSetMb = [math]::Round(($process.WorkingSet64 - $baseWorkingSet) / 1MB, 1)
    Add-Check 'NFR-09 batch memory growth <= 30 MB' ($batchPrivateMb -le 30) ('private +' + $batchPrivateMb + ' MB, working set +' + $batchWorkingSetMb + ' MB, UI already primed')

    # ---------- 2. no copies / no source modification (R3 / NFR-05) ----------
    $copied = @(Get-ChildItem -LiteralPath $rootDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'stress-file-*' })
    Add-Check 'R3 zero-copy (no files in app dir)' ($copied.Count -eq 0) ($copied.Count.ToString() + ' copies')

    $cacheFiles = @(Get-ChildItem -LiteralPath $cacheDir -File -ErrorAction SilentlyContinue)
    Add-Check 'R3 no cache files for file refs' ($cacheFiles.Count -eq 0) ($cacheFiles.Count.ToString() + ' cache files')

    $changed = 0
    foreach ($file in $files) {
        if (Test-Path -LiteralPath $file) {
            $now = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
            if ($now -ne $hashes[$file]) { $changed++ }
        }
        else {
            $changed++
        }
    }
    Add-Check 'NFR-05 source files unchanged' ($changed -eq 0) ($changed.ToString() + ' changed or missing')

    # ---------- 3. rapid collect hotkeys (NFR-06) ----------
    Set-ClipboardSafe { [System.Windows.Forms.Clipboard]::SetText('stress-rapid') }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 200; $i++) {
        [void][Stress.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyCollect, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    $watch.Stop()
    Start-Sleep -Seconds 3
    $process.Refresh()
    $state = Read-StateSafe
    Add-Check 'NFR-06 200 rapid collect hotkeys' ((-not $process.HasExited) -and ($null -ne $state) -and (@($state.items).Count -le $Capacity)) (($watch.ElapsedMilliseconds.ToString()) + ' ms')

    # ---------- 4. rapid expand / collapse hotkeys (NFR-06) ----------
    for ($i = 0; $i -lt 100; $i++) {
        [void][Stress.Api]::PostMessage($handle, 0x0312, [IntPtr]$hotKeyToggle, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Seconds 2
    $process.Refresh()
    $alive = -not $process.HasExited
    $window = [Stress.Api]::FindToolWindow($process.Id)
    Add-Check 'NFR-06 100 rapid toggle hotkeys' ($alive -and ($window -ne [IntPtr]::Zero)) ''

    # ---------- 5. force kill + restart (NFR-06) ----------
    $process | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    $orphan = Join-Path $cacheDir 'orphan-stress.bin'
    Set-Content -LiteralPath $orphan -Value 'orphan' -Encoding ASCII

    $process = Start-App
    $handle = Wait-Window $process 15000
    Start-Sleep -Seconds 3
    $state = Read-StateSafe
    $restoredCount = @($state.items).Count
    Add-Check 'NFR-06 restart after force kill' (($handle -ne [IntPtr]::Zero) -and ($restoredCount -eq $Capacity)) ($restoredCount.ToString() + ' items restored')
    Add-Check 'NFR-06 orphan cache removed after crash' (-not (Test-Path -LiteralPath $orphan)) ''
}
finally {
    Restore-Data
}

$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -match ': FAIL' }).Count
Write-Host ''
if ($failed -eq 0) {
    Write-Host "ALL PASS ($($results.Count) checks)" -ForegroundColor Green
} else {
    Write-Host "$failed check(s) FAILED" -ForegroundColor Red
}
exit $failed
