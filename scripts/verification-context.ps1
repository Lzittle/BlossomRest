# Shared preflight for acceptance scripts. Keep ASCII-only for Windows PowerShell 5.1.
# Dot-source after setting ErrorActionPreference; caller supplies Exe and optional StateFile.
. (Join-Path $PSScriptRoot 'build-paths.ps1')
if (-not $Exe) { $Exe = Get-AppBuildExe }
$Exe = (Resolve-Path -LiteralPath $Exe -ErrorAction Stop).Path
$env:DOTNET_DISABLE_GUI_ERRORS = '1'
# Framework-dependent development builds must use the same local runtime as the SDK.
# Self-contained release packages resolve their bundled runtime independently.
$localRuntime = Join-Path (Split-Path -Parent $PSScriptRoot) '.dotnet-sdk'
$runtimeConfig = [IO.Path]::ChangeExtension($Exe, 'runtimeconfig.json')
if ((Test-Path -LiteralPath $runtimeConfig) -and (Test-Path -LiteralPath (Join-Path $localRuntime 'dotnet.exe'))) {
    $runtimeOptions = (Get-Content -LiteralPath $runtimeConfig -Raw -Encoding UTF8 | ConvertFrom-Json).runtimeOptions
    if ($runtimeOptions.frameworks) {
        $env:DOTNET_ROOT = $localRuntime
        $env:DOTNET_ROOT_X64 = $localRuntime
        Write-Host ('VerificationRuntime: ' + $localRuntime)
    }
}
if (@(Get-Process -Name TransitShelf -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'TransitShelf is running. Exit it from the tray before verification; no process was stopped.'
}
$verificationBase = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'workbench\verification'))
if (-not $StateFile) {
    $StateFile = Join-Path $verificationBase ([Guid]::NewGuid().ToString('n') + '\state.json')
}
$StateFile = [IO.Path]::GetFullPath($StateFile)
if (-not $StateFile.StartsWith($verificationBase + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($StateFile) -ne 'state.json') {
    throw 'StateFile must be state.json inside the repository workbench\verification directory.'
}
$isolatedRoot = Split-Path -Parent $StateFile
# Reject existing directory junctions, including an ancestor that could escape the workspace.
$directoryToCheck = $isolatedRoot
while ($directoryToCheck -and $directoryToCheck.Length -ge $verificationBase.Length) {
    if ((Test-Path -LiteralPath $directoryToCheck) -and
        ((Get-Item -LiteralPath $directoryToCheck).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Verification data directories must not be junctions or symbolic links.'
    }
    $directoryToCheck = Split-Path -Parent $directoryToCheck
}
New-Item -ItemType Directory -Force -Path $isolatedRoot | Out-Null
$env:TRANSITSHELF_DATA_ROOT = $isolatedRoot
$env:TEMP = Join-Path $verificationBase ('_temp\' + [Guid]::NewGuid().ToString('n'))
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
Write-Host ('IsolatedData: ' + $isolatedRoot)
