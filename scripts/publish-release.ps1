param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$releaseDir = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/releases/$Version"
$tag = "v$Version"
$required = @('SkuMaster-win-Setup.exe', 'SkuMaster-win-Portable.zip', 'releases.win.json', "SkuMaster-$Version-full.nupkg")
foreach ($name in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseDir $name))) { throw "Required release asset is missing: $name" }
}
$feed = Get-Content -LiteralPath (Join-Path $releaseDir 'releases.win.json') -Raw | ConvertFrom-Json
if (!(@($feed.Assets) | Where-Object { $_.Version -eq $Version -and $_.Type -eq 'Full' })) { throw 'Update feed does not contain this version.' }
$releases = gh release list --repo $Repository --limit 1000 --json tagName,isDraft
if ($LASTEXITCODE -ne 0) { throw 'Unable to check existing releases.' }
$existing = @($releases | ConvertFrom-Json) | Where-Object tagName -eq $tag
if ($existing -and !$existing.isDraft) { throw "Release $tag is already public; use a new version." }
foreach ($published in @($releases | ConvertFrom-Json)) {
    if (!$published.isDraft -and $published.tagName -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -gt [version]$Version) {
        throw 'A newer version is already published. Refusing to replace the latest release with an older one.'
    }
}
if (!$existing) {
    gh release create $tag --repo $Repository --target $Commit --title "SKU Майстер $Version" --draft --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create draft release.' }
}
$assets = Get-ChildItem -LiteralPath $releaseDir -File | Select-Object -ExpandProperty FullName
gh release upload $tag @assets --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed. The release remains a draft.' }
$uploadedJson = gh release view $tag --repo $Repository --json assets
if ($LASTEXITCODE -ne 0) { throw 'Unable to verify uploaded assets.' }
$uploaded = ($uploadedJson | ConvertFrom-Json).assets
foreach ($asset in $assets) {
    $local = Get-Item -LiteralPath $asset
    $remote = @($uploaded | Where-Object name -eq $local.Name)
    if ($remote.Count -ne 1 -or $remote[0].size -ne $local.Length) { throw "Uploaded asset is missing or incomplete: $($local.Name)" }
}
gh release edit $tag --repo $Repository --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Unable to publish draft release.' }
Write-Output "Published https://github.com/$Repository/releases/tag/$tag"
