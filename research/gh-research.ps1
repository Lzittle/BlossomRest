$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$hdr = @{ 'User-Agent' = 'codex-research'; 'Accept' = 'application/vnd.github+json' }
$lines = New-Object System.Collections.Generic.List[string]

function W($s) { $lines.Add([string]$s); $s | Write-Output }

function Search-GH([string]$q) {
    $u = "https://api.github.com/search/repositories?q=" + [uri]::EscapeDataString($q) + "&sort=stars&order=desc&per_page=8"
    for ($i = 0; $i -lt 5; $i++) {
        try { return Invoke-RestMethod -Uri $u -Headers $hdr -TimeoutSec 25 }
        catch {
            Start-Sleep -Seconds 22
        }
    }
    return $null
}

function Cut([string]$s, [int]$n) {
    if (-not $s) { return '' }
    $s = $s -replace '\s+', ' '
    if ($s.Length -gt $n) { return $s.Substring(0, $n) + '...' }
    return $s
}

$queries = @(
    'clipboard manager drag and drop',
    'drag and drop shelf windows',
    'dropzone alternative windows',
    'temporary file shelf desktop',
    'clipboard drag out to application',
    'always on top file drop target windows',
    '文件中转 拖拽 桌面',
    'drag files between apps helper'
)

foreach ($q in $queries) {
    W ""
    W ('## QUERY: ' + $q)
    $r = Search-GH $q
    if ($null -eq $r) { W '   (failed / rate limited)'; continue }
    foreach ($it in $r.items) {
        $lang = if ($it.language) { $it.language } else { '?' }
        W ('- {0} ★ | {1} | {2} | pushed {3} | {4}' -f $it.stargazers_count, $it.full_name, $lang, ([string]$it.pushed_at).Substring(0, 10), (Cut $it.description 150))
    }
    Start-Sleep -Seconds 8
}

W ""
W '## TARGETED REPO DETAIL'
$repos = @(
    'hluk/CopyQ',
    'PasteBar/PasteBarApp',
    'sabrogden/Ditto',
    'jimuzhe/tiez-clipboard',
    'MuQY1818/OpenYoink',
    '0PandaDEV/Qopy'
)

$kw = 'drag|drop|shelf|window|clipboard|image|file|拖|暂存|中转|out of|to other'

foreach ($rp in $repos) {
    W ""
    W ('### ' + $rp)
    try {
        $info = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $rp) -Headers $hdr -TimeoutSec 25
        W ('stars={0} forks={1} lang={2} license={3} pushed={4} issues={5} homepage={6}' -f $info.stargazers_count, $info.forks_count, $info.language, $info.license.spdx_id, ([string]$info.pushed_at).Substring(0, 10), $info.open_issues_count, $info.homepage)
        W ('desc: ' + (Cut $info.description 300))
        W ('topics: ' + ($info.topics -join ', '))
    } catch { W ('repo info failed: ' + $_.Exception.Message) }
    try {
        $rd = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $rp + "/readme") -Headers @{ 'User-Agent' = 'codex-research'; 'Accept' = 'application/vnd.github.raw' } -TimeoutSec 25
        $hits = ($rd -split "`n") | Where-Object { $_ -match $kw -and $_.Trim().Length -gt 3 } | Select-Object -First 25
        foreach ($h in $hits) { W ('  * ' + (Cut $h.Trim() 200)) }
    } catch { W ('readme failed: ' + $_.Exception.Message) }
    Start-Sleep -Seconds 3
}

$lines -join "`n" | Set-Content -Path (Join-Path $PSScriptRoot 'gh-raw.md') -Encoding UTF8
Write-Output 'SAVED to research/gh-raw.md'
