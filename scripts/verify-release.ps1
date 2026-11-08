<#
Run Release build, unit tests and acceptance scripts with isolated application data.
Usage: powershell -ExecutionPolicy Bypass -File scripts\verify-release.ps1 [-Exe <published-exe>] [-SkipBuild]
Reports and temporary data stay in the ignored local verification directory.
Keep ASCII-only for Windows PowerShell 5.1.
#>
param([string] $Exe, [switch] $SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (@(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Exit TransitShelf from the tray before release verification. No process was stopped.'
}
$runRoot = Join-Path $root ('workbench\verification\release-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$originalDataRoot = $env:TRANSITSHELF_DATA_ROOT
$env:TEMP = Join-Path $runRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
$realRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TransitShelf'
function Get-RealDataFingerprint {
    $entries = @()
    $state = Join-Path $realRoot 'state.json'
    if (Test-Path -LiteralPath $state) { $entries += 'state:' + (Get-FileHash -LiteralPath $state -Algorithm SHA256).Hash }
    $cache = Join-Path $realRoot 'cache'
    if (Test-Path -LiteralPath $cache) {
        foreach ($file in Get-ChildItem -LiteralPath $cache -File -Recurse -ErrorAction Stop | Sort-Object FullName) {
            $entries += $file.FullName.Substring($cache.Length) + ':' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return ($entries -join "`n")
}
$before = Get-RealDataFingerprint
$failed = @()
Start-Transcript -Path (Join-Path $runRoot 'commands.log') | Out-Null
try {
    if (-not $SkipBuild) {
        Write-Host 'COMMAND: .\scripts\dev.ps1 build -c Release --no-restore -warnaserror'
        $buildLog = Join-Path $runRoot 'build.log'
        & (Join-Path $PSScriptRoot 'dev.ps1') build -c Release --no-restore -warnaserror 2>&1 | Out-File -LiteralPath $buildLog -Encoding UTF8
        Get-Content -LiteralPath $buildLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
        Write-Host 'COMMAND: .\scripts\dev.ps1 test -c Release --no-build --no-restore'
        $testLog = Join-Path $runRoot 'unit-tests.log'
        & (Join-Path $PSScriptRoot 'dev.ps1') test -c Release --no-build --no-restore 2>&1 | Out-File -LiteralPath $testLog -Encoding UTF8
        Get-Content -LiteralPath $testLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    }
    if (-not $Exe) { $Exe = Join-Path $root 'src\TransitShelf.App\bin\Release\net8.0-windows\TransitShelf.exe' }
    $Exe = (Resolve-Path -LiteralPath $Exe).Path
    foreach ($case in @('collect', 'watch', 'multiselect', 'smoke', 'stress')) {
        $script = Join-Path $PSScriptRoot ("verify-{0}.ps1" -f $case)
        $state = Join-Path $runRoot ($case + '\state.json')
        Write-Host ("COMMAND: powershell -NoProfile -ExecutionPolicy Bypass -File {0} -Exe {1} -StateFile {2}" -f $script, $Exe, $state)
        $caseLog = Join-Path $runRoot ($case + '.log')
        & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Exe $Exe -StateFile $state 2>&1 | Out-File -LiteralPath $caseLog -Encoding UTF8
        $code = $LASTEXITCODE
        Get-Content -LiteralPath $caseLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
        Write-Host ("RESULT: {0} exit={1}" -f $case, $code)
        if ($code -ne 0) { $failed += $case }
    }
}
finally {
    $after = Get-RealDataFingerprint
    $unchanged = $before -ceq $after
    Write-Host ('REAL_DATA_UNCHANGED=' + $unchanged)
    Stop-Transcript | Out-Null
    $env:TEMP = $originalTemp
    $env:TMP = $originalTmp
    $env:TRANSITSHELF_DATA_ROOT = $originalDataRoot
    if (-not $unchanged) { throw 'Real user data changed during verification. Inspect before proceeding.' }
}
Write-Host ('REPORT: ' + (Join-Path $runRoot 'commands.log'))
if ($failed.Count -gt 0) { throw ('Acceptance failures: ' + ($failed -join ', ')) }
Write-Host 'RELEASE VERIFICATION PASS'
