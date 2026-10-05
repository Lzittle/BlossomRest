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
    'tauri plugin drag out file',
    'electron drag file out of window',
    'autohotkey drag and drop shelf',
    'droppoint drag and drop shelf',
    'drop target shelf electron',
    'clipboard shelf text image windows',
    'wechat file drop helper'
)
foreach ($q in $queries) {
    W ""
    W ('## QUERY: ' + $q)
    $r = Search-GH $q
    if ($null -eq $r) { W '   (failed)'; continue }
    foreach ($it in $r.items) {
        $lang = if ($it.language) { $it.language } else { '?' }
        W ('- {0} | {1} | {2} | pushed {3} | {4}' -f $it.stargazers_count, $it.full_name, $lang, ([string]$it.pushed_at).Substring(0, 10), (Cut $it.description 140))
    }
    Start-Sleep -Seconds 8
}

W ""
W '## CLOSE-MATCH REPOS'
$repos = @('rwrife/drop-shelf', 'w1lls47/ghost-drop', 'Mo3taz901/ClipDock', 'hjosugi/yeet', 'manish10079/-droppointplus', 'noamsrv-cmd/dropshelf')
$kw = 'drag|drop|shelf|window|clipboard|image|file|text|拖|暂存|中转|windows|feature|known|limit'
foreach ($rp in $repos) {
    W ""
    W ('### ' + $rp)
    try {
        $info = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $rp) -Headers $hdr -TimeoutSec 25
        W ('stars={0} forks={1} lang={2} license={3} pushed={4} created={5} size_kb={6}' -f $info.stargazers_count, $info.forks_count, $info.language, $info.license.spdx_id, ([string]$info.pushed_at).Substring(0, 10), ([string]$info.created_at).Substring(0, 10), $info.size)
        W ('desc: ' + (Cut $info.description 260))
    } catch { W ('info failed: ' + $_.Exception.Message) }
    try {
        $rd = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $rp + "/readme") -Headers @{ 'User-Agent' = 'codex-research'; 'Accept' = 'application/vnd.github.raw' } -TimeoutSec 25
        $hits = ($rd -split "`n") | Where-Object { $_ -match $kw -and $_.Trim().Length -gt 3 } | Select-Object -First 20
        foreach ($h in $hits) { W ('  * ' + (Cut $h.Trim() 190)) }
    } catch { W ('readme failed') }
    Start-Sleep -Seconds 3
}

W ""
W '## STACK CAPABILITY DOCS'
try {
    $html = (Invoke-WebRequest -Uri 'https://www.electronjs.org/docs/latest/api/web-contents' -Headers @{ 'User-Agent' = 'Mozilla/5.0' } -TimeoutSec 30).Content
    $txt = ($html -replace '<[^>]+>', ' ') -replace '\s+', ' '
    $idx = $txt.IndexOf('startDrag')
    if ($idx -ge 0) { W ('electron startDrag: ' + (Cut $txt.Substring([Math]::Max(0, $idx - 200), 700) 900)) }
    else { W 'electron startDrag: not found in page' }
} catch { W ('electron doc failed: ' + $_.Exception.Message) }

$lines -join "`n" | Set-Content -Path (Join-Path $PSScriptRoot 'gh-raw2.md') -Encoding UTF8
Write-Output 'SAVED research/gh-raw2.md'
