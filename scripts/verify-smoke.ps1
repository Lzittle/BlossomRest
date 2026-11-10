<#
Repeatable smoke / stability check for the desktop app.

Usage:
  .\scripts\dev.ps1 build -c Release
  powershell -ExecutionPolicy Bypass -File scripts\verify-smoke.ps1 `
      -Exe src\TransitShelf.App\bin\Release\net10.0-windows\TransitShelf.exe

Checks:
  NFR-03  cold start to a visible window < 1.5 s
  FR-03   main window is a non-activating tool window (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)
  NFR-01  idle CPU ~ 0% and private memory <= 110 MB
  FR-01   closing the window keeps the tray process alive (ShutdownMode=OnExplicitShutdown)
  NFR-06  orphan cache file is removed at startup
  NFR-06  a corrupt state.json does not prevent startup

The real state.json and cache are backed up first and restored afterwards, so the
check does not destroy whatever is on the shelf right now.

IMPORTANT: keep this file ASCII-only (see dev.ps1).
#>
param(
    [string] $StateFile,
    [string] $Exe,
    [ValidateSet('zh-CN', 'en')] [string] $Language = 'zh-CN'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification-context.ps1')

Add-Type -Namespace Smoke -Name Api -MemberDefinition @'
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
public static string Bounds(IntPtr h) {
  Rect r; GetWindowRect(h, out r);
  return (r.Right-r.Left) + "x" + (r.Bottom-r.Top) + " at " + r.Left + "," + r.Top;
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

$rootDir = Split-Path -Parent $StateFile
$cacheDir = Join-Path $rootDir 'cache'
$backupDir = Join-Path $env:TEMP ('transitshelf-smoke-' + [Guid]::NewGuid().ToString('n').Substring(0, 8))
$results = New-Object System.Collections.Generic.List[string]

function Stop-App {
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $running = @(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
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
# Overwrite with a retry instead of deleting, so a transient lock cannot abort the run.
function Write-State([string] $content) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Set-Content -LiteralPath $StateFile -Value $content -Encoding UTF8 -Force -ErrorAction Stop
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
}

function Start-AppTimed {
    $process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -WindowStyle Hidden -PassThru
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $handle = [IntPtr]::Zero
    while ($watch.ElapsedMilliseconds -lt 15000) {
        Start-Sleep -Milliseconds 25
        if ($process.HasExited) { break }
        $handle = [Smoke.Api]::FindToolWindow($process.Id)
        if ($handle -ne [IntPtr]::Zero) { break }
    }
    $watch.Stop()
    return @{ Process = $process; Handle = $handle; StartMs = $watch.ElapsedMilliseconds }
}

function Add-Check([string] $name, [bool] $passed, [string] $detail) {
    $results.Add($name.PadRight(42) + ': ' + $(if ($passed) { 'PASS' } else { 'FAIL' }) + ' ' + $detail)
}

Stop-App
Backup-Data

try {
    # Start from a clean shelf so the numbers are independent of whatever is currently stored.
    Write-State ('{ "schemaVersion": 3, "settings": { "language": "' + $Language + '" }, "items": [] }')
    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    Get-ChildItem -LiteralPath $cacheDir -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Set-Content -LiteralPath (Join-Path $cacheDir 'orphan-smoke.bin') -Value 'orphan' -Encoding ASCII
    $orphan = Join-Path $cacheDir 'orphan-smoke.bin'

    $run = Start-AppTimed
    $process = $run.Process
    $handle = $run.Handle

    # ---------- 1. cold start (NFR-03) ----------
    Add-Check 'NFR-03 cold start < 1500 ms' ($run.StartMs -lt 1500) ($run.StartMs.ToString() + ' ms')

    # ---------- 2. window style (FR-03) ----------
    $styleOk = $false
    $styleText = 'window not found'
    if ($handle -ne [IntPtr]::Zero) {
        $style = [Smoke.Api]::GetWindowLong($handle, -20)
        $styleText = ('0x' + $style.ToString('X'))
        $styleOk = (($style -band 0x80) -ne 0) -and (($style -band 0x08000000) -ne 0)
    }
    Add-Check 'FR-03 tool window + no-activate' $styleOk $styleText

    # ---------- 3. orphan cache cleanup (NFR-06) ----------
    Start-Sleep -Seconds 3
    Add-Check 'NFR-06 orphan cache removed' (-not (Test-Path -LiteralPath $orphan)) ''

    # ---------- 4. idle CPU / memory (NFR-01) ----------
    $process.Refresh()
    $cpu0 = $process.TotalProcessorTime
    Write-Host ('DIAGNOSTIC window bounds: ' + [Smoke.Api]::Bounds($handle))
    for ($sample = 1; $sample -le 10; $sample++) {
        Start-Sleep -Seconds 1
        $process.Refresh()
        Write-Host ('DIAGNOSTIC private memory t=' + $sample + 's: ' + ('{0:N1}' -f ($process.PrivateMemorySize64 / 1MB)) + ' MB')
    }
    $process.Refresh()
    $cpu1 = $process.TotalProcessorTime
    $cpuPercent = [math]::Round((($cpu1 - $cpu0).TotalMilliseconds / 10000) / [Environment]::ProcessorCount * 100, 2)
    $privateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
    $workingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 1)
    Add-Check 'NFR-01 idle CPU < 5%' ($cpuPercent -lt 5) ($cpuPercent.ToString() + ' %')
    Add-Check 'NFR-01 private memory <= 110 MB' ($privateMb -le 110) ($privateMb.ToString() + ' MB (working set ' + $workingSetMb + ' MB)')

    # ---------- 5. process is still alive ----------
    Add-Check 'stability process alive after idle' (-not $process.HasExited) ''

    # ---------- 6. close window keeps the tray process (FR-01) ----------
    if ($handle -ne [IntPtr]::Zero) {
        [void][Smoke.Api]::PostMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    Start-Sleep -Seconds 2
    $windowGone = ([Smoke.Api]::FindToolWindow($process.Id) -eq [IntPtr]::Zero)
    $trayAlive = -not $process.HasExited
    Add-Check 'FR-01 close window -> window gone' $windowGone ''
    Add-Check 'FR-01 close window -> tray alive' $trayAlive ''

    # ---------- 7. corrupt state.json does not block startup (NFR-06) ----------
    Stop-App
    Write-State '{ this is not json'
    $run = Start-AppTimed
    $corruptOk = ($run.Handle -ne [IntPtr]::Zero) -and (-not $run.Process.HasExited)
    Add-Check 'NFR-06 corrupt state still starts' $corruptOk ($run.StartMs.ToString() + ' ms')
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
