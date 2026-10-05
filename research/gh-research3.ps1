$ErrorActionPreference = 'Stop'
$hdr = @{ 'User-Agent' = 'codex-research'; 'Accept' = 'application/vnd.github+json' }
$lines = New-Object System.Collections.Generic.List[string]
function W($s) { $lines.Add([string]$s); $s | Write-Output }
function Cut([string]$s, [int]$n) {
    if (-not $s) { return '' }
    $s = $s -replace '\s+', ' '
    if ($s.Length -gt $n) { return $s.Substring(0, $n) + '...' }
    return $s
}
function Search-GH([string]$q) {
    $u = "https://api.github.com/search/repositories?q=" + [uri]::EscapeDataString($q) + "&sort=stars&order=desc&per_page=6"
    for ($i = 0; $i -lt 5; $i++) {
        try { return Invoke-RestMethod -Uri $u -Headers $hdr -TimeoutSec 25 } catch { Start-Sleep -Seconds 22 }
    }
    return $null
}

$queries = @(
    'filename mojibake repair gbk utf8',
    'wechat file organizer export',
    'tauri drag out files plugin',
    'clipboard monitor new file folder watcher desktop'
)
foreach ($q in $queries) {
    W ""
    W ('## QUERY: ' + $q)
    $r = Search-GH $q
    if ($null -eq $r) { W '   (failed)'; continue }
    foreach ($it in $r.items) {
        W ('- {0} | {1} | {2} | pushed {3} | {4}' -f $it.stargazers_count, $it.full_name, $it.language, ([string]$it.pushed_at).Substring(0, 10), (Cut $it.description 140))
    }
    Start-Sleep -Seconds 8
}

W ""
W '## ELECTRON startDrag API (official docs source)'
try {
    $md = Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/electron/electron/main/docs/api/web-contents.md' -Headers @{ 'User-Agent' = 'codex-research' } -TimeoutSec 30
    $idx = $md.IndexOf('startDrag')
    $seg = $md.Substring([Math]::Max(0, $idx - 800), 1800)
    W (($seg -split "`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -First 40) -join "`n")
} catch { W ('electron docs failed: ' + $_.Exception.Message) }

W ""
W '## WINDOWS SHELF / CLIPBOARD OUT-DRAG PRODUCTS'
$repos = @('GameGodS3/DropPoint', 'aungkokomm/Bucket', 'HTanoda/TNDrop')
foreach ($rp in $repos) {
    W ""
    W ('### ' + $rp)
    try {
        $info = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $rp) -Headers $hdr -TimeoutSec 25
        W ('stars={0} lang={1} license={2} pushed={3} open_issues={4} archived={5}' -f $info.stargazers_count, $info.language, $info.license.spdx_id, ([string]$info.pushed_at).Substring(0, 10), $info.open_issues_count, $info.archived)
        W ('desc: ' + (Cut $info.description 260))
    } catch { W ('info failed') }
    Start-Sleep -Seconds 2
}

$lines -join "`n" | Set-Content -Path (Join-Path $PSScriptRoot 'gh-raw3.md') -Encoding UTF8
Write-Output 'SAVED research/gh-raw3.md'
