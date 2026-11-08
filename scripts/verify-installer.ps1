<#
Install, overwrite-install and uninstall the test package in a fresh workspace directory.
Temporarily registers this product under the current user. Refuses an existing installation.
Runs no real-user data migrations and keeps fixture data for inspection.
Keep ASCII-only for Windows PowerShell 5.1.
#>
param([Parameter(Mandatory = $true)] [string] $Setup)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$root = Split-Path -Parent $PSScriptRoot
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C2E5A94-0D3B-4F68-9A1E-5B8C7D2F4A61}_is1'
if (Test-Path -LiteralPath $registration) { throw 'An existing installation is registered. Refusing to alter it.' }
if (@(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Exit TransitShelf before installer verification.' }
$testRoot = Join-Path $root ('workbench\verification\installer-' + [Guid]::NewGuid().ToString('n'))
$target = Join-Path $testRoot 'program'
$data = Join-Path $testRoot 'data'
$exe = Join-Path $target 'TransitShelf.exe'
$realRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TransitShelf'
$oldDataRoot = $env:TRANSITSHELF_DATA_ROOT
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$testProcess = $null
$failures = 0
New-Item -ItemType Directory -Force -Path (Join-Path $data 'cache'), (Join-Path $testRoot 'temp') | Out-Null
$env:TRANSITSHELF_DATA_ROOT = $data
$env:TEMP = Join-Path $testRoot 'temp'
$env:TMP = $env:TEMP
function Fingerprint([string] $directory) {
    $rows = @()
    $state = Join-Path $directory 'state.json'
    if (Test-Path -LiteralPath $state) { $rows += 'state:' + (Get-FileHash -LiteralPath $state -Algorithm SHA256).Hash }
    $cache = Join-Path $directory 'cache'
    if (Test-Path -LiteralPath $cache) {
        foreach ($file in Get-ChildItem -LiteralPath $cache -File -Recurse | Sort-Object FullName) {
            $rows += $file.FullName.Substring($cache.Length) + ':' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return ($rows -join "`n")
}
function Check([string] $name, [bool] $ok) {
    if (-not $ok) { $script:failures++ }
    Write-Host ($name + ': ' + $(if ($ok) { 'PASS' } else { 'FAIL' }))
}
function Run-Setup([string] $label) {
    $log = Join-Path $testRoot ($label + '.log')
    $process = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', ('/DIR="' + $target + '"'), ('/LOG="' + $log + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'Installer did not finish within 60 seconds; inspect its window.' }
    $process.Refresh()
    Write-Host ($label + ' exit=' + $process.ExitCode)
    return $process.ExitCode
}
function Start-Fixture {
    $process = Start-Process -FilePath $exe -WorkingDirectory $target -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw 'Installed application exited unexpectedly.' }
    return $process
}
function Stop-Fixture {
    if ($script:testProcess -and -not $script:testProcess.HasExited) {
        Stop-Process -Id $script:testProcess.Id -Force -ErrorAction Stop
        $script:testProcess.WaitForExit()
    }
    $script:testProcess = $null
    Start-Sleep -Milliseconds 700
}
$realBefore = Fingerprint $realRoot
Start-Transcript -Path (Join-Path $testRoot 'commands.log') | Out-Null
try {
    Check 'fresh install exit zero' ((Run-Setup 'install') -eq 0)
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Installed executable missing.' }
    $sourceExe = Join-Path (Split-Path -Parent $Setup) 'BlossomRest-win-x64\TransitShelf.dll'
    Check 'installed payload matches published build' ((Get-FileHash -LiteralPath $sourceExe).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target 'TransitShelf.dll')).Hash)
    Check 'current-user uninstall registration' (Test-Path -LiteralPath $registration)
    foreach ($name in @('LICENSE.txt', 'USER-GUIDE.md', 'RELEASE-NOTES.md')) {
        Check ('installed attachment ' + $name) (Test-Path -LiteralPath (Join-Path $target $name))
    }
    $png = Join-Path $data 'cache\fixture.png'
    [IO.File]::WriteAllBytes($png, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8X8AAAAASUVORK5CYII='))
    $groupId = [Guid]::NewGuid().ToString('n')
    $state = @{
        schemaVersion = 3
        settings = @{ enableWorkspace = $true; clearOnExit = $false }
        groups = @(@{ id = $groupId; name = 'installer-fixture'; createdAt = [DateTimeOffset]::Now.ToString('O') })
        items = @(@{ id = [Guid]::NewGuid().ToString('n'); kind = 'file'; displayName = 'fixture.png'; cachePath = $png; createdAt = [DateTimeOffset]::Now.ToString('O'); pinned = $true; groupId = $groupId })
    }
    [IO.File]::WriteAllText((Join-Path $data 'state.json'), ($state | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    $testProcess = Start-Fixture
    Check 'installed application starts' (-not $testProcess.HasExited)
    Check 'running application blocks installation' ((Run-Setup 'blocked-upgrade') -ne 0)
    Check 'blocked upgrade keeps application alive' (-not $testProcess.HasExited)
    Stop-Fixture
    $fixtureBefore = Fingerprint $data
    Check 'same-version overwrite install' ((Run-Setup 'overwrite-install') -eq 0)
    Check 'overwrite install preserves state and cache' ($fixtureBefore -ceq (Fingerprint $data))
    $testProcess = Start-Fixture
    $saved = Get-Content -LiteralPath (Join-Path $data 'state.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    Check 'workspace group survives restart' (@($saved.groups).Count -eq 1 -and $saved.groups[0].id -eq $groupId)
    Check 'cached item survives restart' (@($saved.items).Count -eq 1 -and (Test-Path -LiteralPath $png))
    Stop-Fixture
    $fixtureBefore = Fingerprint $data
    $uninstaller = Join-Path $target 'unins000.exe'
    $uninstallLog = Join-Path $testRoot 'uninstall.log'
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $uninstallLog + '"')) -WindowStyle Hidden -PassThru
    if (-not $uninstall.WaitForExit(60000)) { throw 'Uninstaller did not finish within 60 seconds.' }
    $uninstall.Refresh()
    Check 'uninstall exit zero' ($uninstall.ExitCode -eq 0)
    Check 'uninstall removes application' (-not (Test-Path -LiteralPath $exe))
    Check 'uninstall preserves state and cache' ($fixtureBefore -ceq (Fingerprint $data))
    Check 'uninstall removes test registration' (-not (Test-Path -LiteralPath $registration))
}
finally {
    Stop-Fixture
    Check 'real user data unchanged' ($realBefore -ceq (Fingerprint $realRoot))
    Stop-Transcript | Out-Null
    $env:TRANSITSHELF_DATA_ROOT = $oldDataRoot
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    Write-Host ('REPORT: ' + (Join-Path $testRoot 'commands.log'))
}
if ($failures -gt 0) { throw ("Installer checks failed: {0}" -f $failures) }
Write-Host 'INSTALLER VERIFICATION PASS'
