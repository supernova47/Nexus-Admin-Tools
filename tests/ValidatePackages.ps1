param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
$dll = Join-Path $root 'src\bin\Release\ValheimSpawner.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -ne "$version.0") { throw 'Assembly version mismatch' }
$plugin = Get-Content -LiteralPath (Join-Path $root 'src\Plugin.cs') -Raw
if (-not $plugin.Contains("PluginVersion = `"$version`"")) { throw 'Plugin version mismatch' }
foreach ($document in @('manifest.json','README.md')) {
    $main = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root $document)).Hash
    $template = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root "package_template\$document")).Hash
    if ($main -ne $template) { throw "Template mismatch: $document" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$results = @()
foreach ($name in @("Nexus-Admin-Tools-Thunderstore-$version.zip", "Nexus-Admin-Tools-Source-$version.zip")) {
    $file = Join-Path $root "dist\$name"
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        $fileNames = @($zip.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\','/') })
        if (($names | Sort-Object -Unique).Count -ne $names.Count) { throw "Duplicate ZIP entries: $name" }
        if ($names | Where-Object { $_ -match '(^|/)(\.\.|bin|obj|\.dotnet-cli-home|\.git)(/|$)' }) { throw "Unexpected internal entry in $name" }
        if ($name -notmatch 'Source') {
            foreach ($required in @('manifest.json','README.md','icon.png')) {
                if ($names -notcontains $required) { throw "Missing Thunderstore root entry: $required" }
            }
            $packageManifestEntry = $zip.GetEntry('manifest.json')
            $manifestReader = [IO.StreamReader]::new($packageManifestEntry.Open(), [Text.Encoding]::UTF8, $true)
            try { $packageManifest = $manifestReader.ReadToEnd() | ConvertFrom-Json }
            finally { $manifestReader.Dispose() }
            if ($packageManifest.name -notmatch '^[A-Za-z0-9_]{1,128}$' -or $packageManifest.version_number -ne $version -or
                [string]::IsNullOrWhiteSpace($packageManifest.description) -or $packageManifest.description.Length -gt 250) {
                throw 'Invalid Thunderstore manifest name, version, or description'
            }
            $iconEntry = $zip.GetEntry('icon.png')
            $iconStream = $iconEntry.Open()
            try {
                $header = New-Object byte[] 24
                if ($iconStream.Read($header, 0, 24) -ne 24 -or [Text.Encoding]::ASCII.GetString($header, 1, 3) -ne 'PNG' -or
                    [BitConverter]::ToUInt32([byte[]]@($header[19],$header[18],$header[17],$header[16]), 0) -ne 256 -or
                    [BitConverter]::ToUInt32([byte[]]@($header[23],$header[22],$header[21],$header[20]), 0) -ne 256) {
                    throw 'Thunderstore icon must be a 256x256 PNG'
                }
            }
            finally { $iconStream.Dispose() }
            $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq 'BepInEx/plugins/ValheimSpawner/ValheimSpawner.dll' })
            if ($entry.Count -ne 1 -or @($names | Where-Object { $_ -like '*.dll' }).Count -ne 1) { throw 'Package must contain exactly one plugin DLL' }
            $hash = [Security.Cryptography.SHA256]::Create()
            $inputStream = $entry[0].Open()
            try { $digest = [BitConverter]::ToString($hash.ComputeHash($inputStream)).Replace('-','') }
            finally { $inputStream.Dispose(); $hash.Dispose() }
            if ($digest -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash) { throw 'Packaged DLL differs from built DLL' }
            $allowed = @('manifest.json','README.md','icon.png','BepInEx/plugins/ValheimSpawner/ValheimSpawner.dll')
            if ($fileNames.Count -ne $allowed.Count -or @($fileNames | Where-Object { $_ -notin $allowed }).Count -ne 0) { throw 'Thunderstore package contains unexpected files' }
        }
        else {
            $prefix = "Nexus-Admin-Tools-Source-$version/"
            foreach ($source in Get-ChildItem -LiteralPath (Join-Path $root 'src') -File | Where-Object Extension -in @('.cs','.csproj')) {
                if ($names -notcontains ($prefix + 'src/' + $source.Name)) { throw "Source missing from archive: $($source.Name)" }
            }
            foreach ($entry in $zip.Entries) {
                if (-not $entry.Name) { continue }
                if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected source prefix' }
                $relative = $entry.FullName.Substring($prefix.Length)
                $disk = [IO.Path]::GetFullPath((Join-Path $root $relative))
                if (-not $disk.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe source ZIP path' }
                $hash = [Security.Cryptography.SHA256]::Create()
                $inputStream = $entry.Open()
                try { $digest = [BitConverter]::ToString($hash.ComputeHash($inputStream)).Replace('-','') }
                finally { $inputStream.Dispose(); $hash.Dispose() }
                if ($digest -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $disk).Hash) { throw "Source mismatch: $relative" }
            }
        }
        $results += [pscustomobject]@{ name=$name; entries=$zip.Entries.Count; bytes=(Get-Item -LiteralPath $file).Length; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash }
    }
    finally { $zip.Dispose() }
}
$results | Format-Table -AutoSize
Write-Output 'PASS assembly/plugin/template metadata, package contents, DLL hash and every archived source hash'
