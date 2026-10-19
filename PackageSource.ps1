param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$target = [IO.Path]::GetFullPath((Join-Path $dist "Nexus-Admin-Tools-Source-$version.zip"))
if (-not $target.StartsWith($dist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe output path' }
$files = @(Get-ChildItem -LiteralPath $root -File | Where-Object {
    ($_.Extension -in @('.md','.ps1','.bat','.png','.json','.yml','.yaml') -or $_.Name -in @('.gitignore','LICENSE')) -and
    $_.Name -notin @('VALIDATION.json','HudPreviewValidation.json','HudPreview.png')
})
foreach ($folder in @('src','tests','package_template')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]' -and
        $_.Extension -in @('.cs','.csproj','.ps1','.py','.json','.md','.png','.yml','.yaml') -or $_.Name -eq 'LICENSE'
    })
}
if (Test-Path -LiteralPath (Join-Path $root '.github')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $root '.github') -Recurse -File | Where-Object {
        $_.Extension -in @('.yml','.yaml','.md')
    })
}
foreach ($log in @('BuildLog.txt','StaticTestLog.txt','NetworkTestLog.txt','CleanupTestLog.txt','GameApiTestLog.txt')) {
    $file = Join-Path $root $log
    if (Test-Path -LiteralPath $file) { $files += Get-Item -LiteralPath $file }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($target, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($file in $files | Sort-Object FullName -Unique) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName,
            "Nexus-Admin-Tools-Source-$version/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $zip.Dispose(); $stream.Dispose() }
Write-Output "Source ZIP created: $target"
