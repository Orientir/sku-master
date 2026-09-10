param([switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$uiRoot = Join-Path $projectRoot 'src/SkuMaster.Ui'
$bundledNode = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
$node = if (Test-Path $bundledNode) { $bundledNode } else { 'node' }
$npmCli = Join-Path ${env:ProgramFiles} 'nodejs/node_modules/npm/bin/npm-cli.js'
Push-Location $uiRoot
try {
    if ($Install -or !(Test-Path 'node_modules')) {
        if (Test-Path $npmCli) { & $node $npmCli ci --ignore-scripts } else { & npm.cmd ci --ignore-scripts }
        if ($LASTEXITCODE -ne 0) { throw 'UI dependency installation failed.' }
    }
    & $node node_modules/typescript/bin/tsc --noEmit
    if ($LASTEXITCODE -ne 0) { throw 'UI type checking failed.' }
    $notices = [Text.StringBuilder]::new("Third-party licenses for UI dependencies and build tooling.`r`nFonts include separate OFL notices in fonts/.`r`n")
    Get-ChildItem -LiteralPath 'node_modules' -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING)(\.|$)' -and $_.Length -lt 1MB } | Sort-Object FullName | ForEach-Object {
        [void]$notices.AppendLine("`r`n--- " + $_.FullName.Substring($uiRoot.Length + 1) + " ---`r`n")
        [void]$notices.AppendLine([IO.File]::ReadAllText($_.FullName))
    }
    [IO.File]::WriteAllText((Join-Path $uiRoot 'public/THIRD-PARTY-NOTICES.txt'), $notices.ToString())
    & $node node_modules/vite/bin/vite.js build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
} finally { Pop-Location }
