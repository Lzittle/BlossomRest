# Resolve build paths from the owning target configuration. Keep ASCII-only.
function Get-AppBuildExe {
    param([ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug')
    $buildRoot = Split-Path -Parent $PSScriptRoot
    [xml] $properties = Get-Content -LiteralPath (Join-Path $buildRoot 'Directory.Build.props') -Encoding UTF8 -Raw
    $framework = [string] $properties.Project.PropertyGroup.TargetFramework
    if ($framework -notmatch '^net\d+\.\d+-windows$') { throw 'Unexpected application target framework.' }
    return Join-Path $buildRoot ('src\TransitShelf.App\bin\' + $Configuration + '\' + $framework + '\TransitShelf.exe')
}
