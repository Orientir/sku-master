param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$RepositoryUrl = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
if ($RepositoryUrl -and $RepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'RepositoryUrl must be a public GitHub repository URL.' }
$sdk = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
if ($sdk -ne 'dotnet') { $env:DOTNET_ROOT = Split-Path $sdk -Parent }
$publish = Join-Path $projectRoot "artifacts/publish/$Version"
$release = Join-Path $projectRoot "artifacts/releases/$Version"
& $sdk publish src/SkuMaster.Desktop/SkuMaster.Desktop.csproj -c Release -r win-x64 --self-contained true -o $publish "-p:Version=$Version" "-p:UpdateRepository=$RepositoryUrl" "-p:RestoreConfigFile=$projectRoot/NuGet.Config" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$vpk = Join-Path $projectRoot '.tools/vpk/vpk.exe'
if (!(Test-Path $vpk)) {
    & $sdk tool install vpk --version 1.2.0 --tool-path .tools/vpk --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Velopack tool installation failed.' }
}
& $vpk pack --packId SkuMaster --packVersion $Version --packDir $publish --mainExe SkuMaster.exe --packTitle 'SKU Майстер' --packAuthors 'SKU Майстер' --outputDir $release
if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
Write-Output "Installer and portable archive: $release"
