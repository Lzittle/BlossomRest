<#
Build and start TransitShelf (development use).
Keep this file ASCII-only (see the note in dev.ps1).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'dev.ps1') run --project (Join-Path $root 'src/TransitShelf.App')
