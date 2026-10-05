<#
Repeatable acceptance check for directory watching (FR-15).

Usage:  powershell -ExecutionPolicy Bypass -File scripts\verify-watch.ps1
Setup:  .\scripts\dev.ps1 build   (build first)

How it works: a temporary directory is registered as an extra watch target through
state.json, the app is started, files are created there, and the result is checked
against state.json. Using a temp directory keeps the real Downloads folder untouched.

IMPORTANT: keep this file ASCII-only (see dev.ps1).
#>
param(
    [string] $StateFile = (Join-Path $env:LOCALAPPDATA 'TransitShelf\state.json'),
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TransitShelf.App\bin\Debug\net8.0-windows\TransitShelf.exe')
)

$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[string]
$watchDir = Join-Path $env:TEMP ('transitshelf-watch-' + [Guid]::NewGuid().ToString('n').Substring(0, 8))

function Stop-App {
    Get-Process -Name TransitShelf -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}

function Start-App {
    $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) | Out-Null
    Start-Sleep -Seconds 5
}

function Read-State {
    if (-not (Test-Path $StateFile)) { return $null }
    return (Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json)
}

Stop-App
New-Item -ItemType Directory -Force -Path $watchDir | Out-Null

$state = @{
    schemaVersion = 2
    settings = @{
        capacity = 9
        timeToLiveHours = 24
        watchDownloads = $false
        watchWeChat = $false
        extraWatchDirectories = @($watchDir)
    }
    items = @()
}
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StateFile -Encoding UTF8

Start-App

# ---------- 1. a normal file dropped into the watched directory ----------
$file1 = Join-Path $watchDir 'watched-one.txt'
Set-Content -LiteralPath $file1 -Value 'hello' -Encoding UTF8
Start-Sleep -Seconds 6
$state = Read-State
$hits = @($state.items | Where-Object { $_.path -eq $file1 })
$results.Add('FR-15 watched file appears        : ' + $(if ($hits.Count -eq 1) { 'PASS' } else { "FAIL ($($hits.Count))" }))

# ---------- 2. temp files must be ignored ----------
$file2 = Join-Path $watchDir 'half-downloaded.tmp'
Set-Content -LiteralPath $file2 -Value 'partial' -Encoding UTF8
Start-Sleep -Seconds 4
$state = Read-State
$hits = @($state.items | Where-Object { $_.path -eq $file2 })
$results.Add('FR-15 .tmp file ignored           : ' + $(if ($hits.Count -eq 0) { 'PASS' } else { 'FAIL' }))

# ---------- 3. a file still being written is collected exactly once, after it settles ----------
$file3 = Join-Path $watchDir 'growing.bin'
$stream = [System.IO.File]::Create($file3)
$bytes = New-Object byte[] 4096
for ($i = 1; $i -le 6; $i++) {
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    Start-Sleep -Milliseconds 400
}
$stream.Close()
Start-Sleep -Seconds 6
$state = Read-State
$hits = @($state.items | Where-Object { $_.path -eq $file3 })
$results.Add('FR-15 settled file collected once : ' + $(if ($hits.Count -eq 1) { 'PASS' } else { "FAIL ($($hits.Count))" }))

# ---------- 4. settings survive the round trip ----------
$state = Read-State
$kept = @($state.settings.extraWatchDirectories)
$results.Add('FR-72 watch settings persist      : ' + $(if ($kept -contains $watchDir) { 'PASS' } else { 'FAIL' }))

Stop-App
if (Test-Path $StateFile) { Remove-Item -LiteralPath $StateFile -Force }
if (Test-Path $watchDir) { Remove-Item -LiteralPath $watchDir -Recurse -Force }

$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -match 'FAIL' }).Count
Write-Host ''
if ($failed -eq 0) {
    Write-Host "ALL PASS ($($results.Count) checks)" -ForegroundColor Green
} else {
    Write-Host "$failed check(s) FAILED" -ForegroundColor Red
}
exit $failed
