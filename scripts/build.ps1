param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$sdk = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
foreach ($project in @('tests/SkuMaster.Tests/SkuMaster.Tests.csproj', 'tests/SkuMaster.Desktop.Tests/SkuMaster.Desktop.Tests.csproj')) {
    & $sdk test $project -c $Configuration --nologo "-p:RestoreConfigFile=$projectRoot/NuGet.Config"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
}
& $sdk build src/SkuMaster.Desktop/SkuMaster.Desktop.csproj -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
