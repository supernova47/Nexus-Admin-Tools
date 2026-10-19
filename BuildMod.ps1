param(
    [string]$ValheimDir = '',
    [string]$BepInExDir = '',
    [string]$JotunnDll = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root 'src\ValheimSpawner.csproj'

function Test-ValheimDir([string]$path) {
    if (-not $path) { return $false }
    return (Test-Path (Join-Path $path 'valheim.exe')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.CoreModule.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.AudioModule.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.IMGUIModule.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.TextRenderingModule.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.InputLegacyModule.dll')) -and
        (Test-Path (Join-Path $path 'valheim_Data\Managed\UnityEngine.PhysicsModule.dll'))
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install .NET SDK 8 or newer and run BuildMod.bat again.'
}

$valheimCandidates = @(
    'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    'C:\Program Files\Steam\steamapps\common\Valheim',
    (Join-Path $env:ProgramFiles 'Steam\steamapps\common\Valheim'),
    'D:\SteamLibrary\steamapps\common\Valheim',
    'E:\SteamLibrary\steamapps\common\Valheim',
    'F:\SteamLibrary\steamapps\common\Valheim'
) | Select-Object -Unique

if (-not $ValheimDir) {
    $ValheimDir = $valheimCandidates | Where-Object { Test-ValheimDir $_ } | Select-Object -First 1
}
if (-not $ValheimDir) {
    $ValheimDir = Read-Host 'Enter the full Valheim folder path (the folder containing valheim.exe)'
}
if (-not (Test-ValheimDir $valheimDir)) {
    throw "Valheim or required Unity DLLs were not found in: $valheimDir"
}

# GUIStyle.fontStyle / UnityEngine.FontStyle requires UnityEngine.TextRenderingModule
# with Unity 6. Fail with a readable message before invoking the compiler.
$textRenderingAssembly = Join-Path $valheimDir 'valheim_Data\Managed\UnityEngine.TextRenderingModule.dll'
if (-not (Test-Path $textRenderingAssembly)) {
    throw "Required Unity text-rendering assembly was not found: $textRenderingAssembly. Verify the Valheim game files."
}

# JsonUtility lives in the JSONSerialize module, not CoreModule.
$jsonAssembly = Join-Path $valheimDir 'valheim_Data\Managed\UnityEngine.JSONSerializeModule.dll'
if (-not (Test-Path $jsonAssembly)) {
    throw "Required Unity JSON serialization assembly was not found: $jsonAssembly. Check your Valheim installation files."
}

$profileRoot = Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles'
$bepinexCandidates = @()
if (Test-Path $profileRoot) {
    $bepinexCandidates = Get-ChildItem $profileRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'BepInEx' } |
        Where-Object { Test-Path (Join-Path $_ 'core\BepInEx.dll') }
}
if (-not $BepInExDir) {
    $BepInExDir = $bepinexCandidates | Select-Object -First 1
}
if (-not $BepInExDir) {
    $BepInExDir = Read-Host 'Enter the BepInEx folder for your r2modman profile (it must contain core\BepInEx.dll)'
}
if (-not (Test-Path (Join-Path $bepinexDir 'core\BepInEx.dll'))) {
    throw "BepInEx.dll was not found in $bepinexDir\core"
}
if (-not (Test-Path (Join-Path $bepinexDir 'core\0Harmony.dll'))) {
    throw "0Harmony.dll was not found in $bepinexDir\core"
}

Write-Host "Valheim: $valheimDir"
Write-Host "BepInEx: $bepinexDir"
if (-not $JotunnDll) {
    $JotunnDll = Get-ChildItem -LiteralPath (Join-Path $BepInExDir 'plugins') -Filter Jotunn.dll -Recurse -File |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $JotunnDll -or -not (Test-Path -LiteralPath $JotunnDll)) { throw 'Jotunn.dll not found. Install Jotunn or pass -JotunnDll FULL_PATH.' }
Write-Host "Jotunn: $JotunnDll"
Write-Host 'Building Nexus Admin Tools...'

& dotnet build $csproj --configuration Release "-p:ValheimDir=$valheimDir" "-p:BepInExDir=$bepinexDir" "-p:JotunnDll=$JotunnDll" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed. See compiler output above.' }

$dll = Join-Path $root 'src\bin\Release\ValheimSpawner.dll'
if (-not (Test-Path $dll)) { throw "Built DLL was not found: $dll" }

# One source of truth for the installable package's version: manifest.json.
$manifest = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
if (-not $version) { throw 'manifest.json contains no version_number' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$packageRoot = Join-Path $dist 'staging'
if (Test-Path -LiteralPath $packageRoot) {
    $resolved = [IO.Path]::GetFullPath($packageRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($dist) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
    Remove-Item -LiteralPath $resolved -Force -Recurse
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$pluginDir = Join-Path $packageRoot 'BepInEx\plugins\ValheimSpawner'
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $dll (Join-Path $pluginDir 'ValheimSpawner.dll') -Force
Copy-Item (Join-Path $root 'manifest.json') (Join-Path $packageRoot 'manifest.json') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $packageRoot 'README.md') -Force
Copy-Item (Join-Path $root 'changelog.md') (Join-Path $packageRoot 'changelog.md') -Force
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $packageRoot 'LICENSE') -Force
Copy-Item (Join-Path $root 'icon.png') (Join-Path $packageRoot 'icon.png') -Force
$zip = Join-Path $dist ("Nexus-Admin-Tools-Thunderstore-$version.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zip -CompressionLevel Optimal
$resolved = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolved.StartsWith([IO.Path]::GetFullPath($dist) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
Remove-Item -LiteralPath $resolved -Force -Recurse
Write-Host "`nThunderstore package ZIP created: $zip" -ForegroundColor Green
Write-Host 'The ZIP has Thunderstore metadata at its root and can be uploaded or imported locally for testing.' -ForegroundColor Cyan
