<#
Build the current-user installer from a new portable publish directory.
Usage: .\scripts\build-installer.ps1 -PublishRoot <publish-output> -Compiler <ISCC.exe>
Does not download tools, overwrite an existing installer or install the product.
Keep ASCII-only for Windows PowerShell 5.1.
#>
param(
    [Parameter(Mandatory = $true)] [string] $PublishRoot,
    [string] $Compiler
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$PublishRoot = (Resolve-Path -LiteralPath $PublishRoot).Path
$publishDir = Join-Path $PublishRoot 'BlossomRest-win-x64'
$exe = Join-Path $publishDir 'TransitShelf.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Publish the portable build before creating the installer.' }
$version = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Invalid published version.' }
if (-not $Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Compiler = $command.Source }
}
if (-not $Compiler) { throw 'Supply -Compiler with the path to the Inno Setup 6 ISCC.exe compiler.' }
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
$setup = Join-Path $PublishRoot ("BlossomRest-{0}-win-x64-setup.exe" -f $version)
if (Test-Path -LiteralPath $setup) { throw 'Installer already exists; select a new publish output directory.' }
foreach ($required in @('LICENSE.txt', 'USER-GUIDE.md', 'RELEASE-NOTES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) { throw "Missing package attachment: $required" }
}
& $Compiler ('/DAppVersion=' + $version) ('/DPublishDir=' + $publishDir) ('/O' + $PublishRoot) (Join-Path $PSScriptRoot 'installer\BlossomRest.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compiler failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $setup)) { throw 'The compiler did not generate the expected installer.' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($setup + '.sha256', ($hash + ' *' + [IO.Path]::GetFileName($setup) + [Environment]::NewLine), [Text.Encoding]::ASCII)
Write-Host ('Installer: ' + $setup)
Write-Host ('SHA256: ' + $hash)
