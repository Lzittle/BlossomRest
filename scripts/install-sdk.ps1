<#
Install the pinned Windows x64 SDK into .dotnet-sdk without changing the system.
Downloads only from Microsoft and verifies the official SHA-512 before extract.
Existing SDK installations are never replaced. Keep this script ASCII-only.
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sdk.version
if ($version -notmatch '^10\.0\.\d+$') { throw 'Expected a stable .NET 10 SDK in global.json.' }
$destination = Join-Path $root '.dotnet-sdk'
if (Test-Path -LiteralPath $destination) {
    throw 'Repository SDK directory already exists. Use scripts\dev.ps1 to check it; no files were replaced.'
}
$metadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$metadata = Invoke-RestMethod -Uri $metadataUrl
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $version }) | Select-Object -First 1
if (-not $sdk) { throw 'Pinned SDK not found in official release metadata.' }
$file = @($sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' }) | Select-Object -First 1
if (-not $file -or $file.hash -notmatch '^[0-9a-fA-F]{128}$') { throw 'SDK zip or SHA-512 missing in official metadata.' }
$downloadUri = [Uri] $file.url
if ($downloadUri.Scheme -ne 'https' -or $downloadUri.Host -ne 'builds.dotnet.microsoft.com') { throw 'Unexpected SDK download host.' }
$downloads = Join-Path $root '.dotnet-home\downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$archive = Join-Path $downloads ('dotnet-sdk-' + $version + '-win-x64.zip')
Write-Host ('SDK_VERSION=' + $version)
Write-Host ('METADATA_URL=' + $metadataUrl)
Write-Host ('DOWNLOAD_URL=' + $downloadUri.AbsoluteUri)
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $downloadUri.AbsoluteUri -OutFile $archive -UseBasicParsing }
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
if ($actualHash -ine $file.hash) { throw 'SDK SHA-512 does not match Microsoft metadata. Archive was retained for inspection.' }
Write-Host ('SDK_SHA512=' + $actualHash.ToLowerInvariant())
Expand-Archive -LiteralPath $archive -DestinationPath $destination
$exe = Join-Path $destination 'dotnet.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Extracted dotnet.exe signature is not valid Microsoft signing.' }
Write-Host 'MICROSOFT_SIGNATURE=Valid'
Write-Host ('SDK_INSTALLED=' + $destination)
