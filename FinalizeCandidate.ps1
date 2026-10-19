param([string]$BaselineFolder = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$version = (Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$baselines = @()
if ($BaselineFolder) {
    $baselineFolder = [IO.Path]::GetFullPath($BaselineFolder)
    foreach ($name in @('SuperNova-ValheimNexus-Source-1.6.0-buildfix1.zip','SuperNova-ValheimNexus-1.6.0.zip')) {
        $file = Join-Path $baselineFolder $name
        $baselines += [ordered]@{ name=$name; bytes=(Get-Item -LiteralPath $file).Length; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash }
    }
    $baselines | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'BASELINE_INFO.json') -Encoding utf8
} else {
    $baselineInfo = Join-Path $root 'BASELINE_INFO.json'
    if (-not (Test-Path -LiteralPath $baselineInfo)) { throw 'Pass -BaselineFolder or provide BASELINE_INFO.json.' }
    $baselines = Get-Content -LiteralPath $baselineInfo -Raw | ConvertFrom-Json
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
& (Join-Path $root 'PackageSource.ps1')
& (Join-Path $root 'tests\ValidatePackages.ps1')
$dll = Join-Path $root 'src\bin\Release\ValheimSpawner.dll'
$testResults = Get-Content -LiteralPath (Join-Path $root 'TEST_RESULTS.json') -Raw | ConvertFrom-Json
$packages = [ordered]@{}
$sums = @()
foreach ($name in @("Nexus-Admin-Tools-Thunderstore-$version.zip", "Nexus-Admin-Tools-Source-$version.zip")) {
    $file = Join-Path $root "dist\$name"
    $digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
    $packages[$name] = [ordered]@{ bytes=(Get-Item -LiteralPath $file).Length; sha256=$digest }
    $sums += "$digest  $name"
}
$validation = [ordered]@{
    version=$version; status='compiled_candidate_not_game_tested'; date='2026-10-19';
    baselines=$baselines;
    dll=[ordered]@{ name='ValheimSpawner.dll'; assembly_version=[Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(); sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash };
    checks=$testResults; packages=$packages;
    package_validation='passed'; game_launched=$false; game_profile_modified=$false; worlds_modified=$false;
    characters_modified=$false; live_multiplayer_tested=$false;
    manual_test_plan='TEST_PLAN_1.8.7_RU.md'
}
$validation | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'VALIDATION.json') -Encoding utf8
$sums | Set-Content -LiteralPath (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding utf8
Write-Output 'Candidate finalized; no installation or game launch performed.'
