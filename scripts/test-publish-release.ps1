$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SkuMaster-release-tests-' + [guid]::NewGuid().ToString('N'))
$releaseDir = Join-Path $testRoot 'artifacts/releases/1.2.3'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $testRoot 'scripts') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-release.ps1') -Destination (Join-Path $testRoot 'scripts/publish-release.ps1')
foreach ($name in @('SkuMaster-win-Setup.exe', 'SkuMaster-win-Portable.zip', 'SkuMaster-1.2.3-full.nupkg')) {
    Set-Content -LiteralPath (Join-Path $releaseDir $name) -Value 'Test fixture only'
}
Set-Content -LiteralPath (Join-Path $releaseDir 'releases.win.json') -Value '{"Assets":[{"Version":"1.2.3","Type":"Full"}]}'

function gh {
    $calls.Add(($args[0..1] -join ' '))
    $global:LASTEXITCODE = 0
    switch ($args[1]) {
        'list' {
            if ($scenario -eq 'published') { '[{"tagName":"v1.2.3","isDraft":false}]' }
            elseif ($scenario -eq 'newer') { '[{"tagName":"v1.2.4","isDraft":false}]' }
            elseif ($scenario -eq 'resume') { '[{"tagName":"v1.2.3","isDraft":true}]' }
            else { '[]' }
        }
        'upload' { if ($scenario -eq 'upload-failed') { $global:LASTEXITCODE = 1 } }
        'view' {
            $items = @(Get-ChildItem -LiteralPath $releaseDir -File | ForEach-Object { @{ name = $_.Name; size = $_.Length } })
            if ($scenario -eq 'incomplete') { $items[0].size = 0 }
            @{ assets = $items } | ConvertTo-Json -Depth 5 -Compress
        }
    }
}
try {
    foreach ($scenario in @('success', 'resume', 'published', 'newer', 'upload-failed', 'incomplete')) {
        $calls = [System.Collections.Generic.List[string]]::new()
        $failed = $false
        try {
            & (Join-Path $testRoot 'scripts/publish-release.ps1') -Version '1.2.3' -Repository 'example/test' -Commit ('a' * 40) | Out-Null
        } catch { $failed = $true; $failureMessage = $_.Exception.Message }
        $shouldPublish = $scenario -in @('success', 'resume')
        if ($failed -eq $shouldPublish) { throw "Unexpected outcome in scenario ${scenario}: $failureMessage" }
        if ($calls.Contains('release edit') -ne $shouldPublish) { throw "Unsafe publication in scenario: $scenario" }
        if ($scenario -eq 'resume' -and $calls.Contains('release create')) { throw 'Retry must reuse the existing draft.' }
        if ($shouldPublish -and $calls.IndexOf('release view') -gt $calls.IndexOf('release edit')) { throw 'Assets must be verified before publication.' }
    }
    Write-Output 'Release publishing checks: 6 passed (GitHub mocked; no network or publication).'
} finally {
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (!$resolved.StartsWith($tempBase, [System.StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('SkuMaster-release-tests-')) { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
