#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory, [switch]$PassThru)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent $PSScriptRoot
$modPath = Join-Path $workspacePath 'unity/Assets/Blueprinter/Mods/baanish-armory'
$version = (Get-Content -LiteralPath (Join-Path $modPath 'modinfo.json') -Raw | ConvertFrom-Json).version
if ($version -ne '0.1.5') { throw 'Review the packaging contract before preparing a different version.' }
$buildPath = [IO.Path]::GetFullPath($BuildDirectory, $PWD.Path)
$bundleName = "baanish-armory_$version.nobp"
$sourceName = "baanish-armory_$version.source.zip"

function Get-ReleaseHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ReleaseFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing file: $Path" }
    for ($current = $Path; $current; $current = Split-Path -Parent $current) {
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Redirected release input: $current"
        }
    }
}

$checksums = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $buildPath 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$' -or $checksums.ContainsKey($Matches[2])) {
        throw 'Malformed or duplicate build checksum.'
    }
    $checksums[$Matches[2]] = $Matches[1]
}
if ($checksums.Count -ne 2) { throw 'Expected exactly the bundle and Blueprinter source checksums.' }
foreach ($name in @($bundleName, $sourceName)) {
    $path = Join-Path $buildPath $name
    Assert-ReleaseFile $path
    if (-not $checksums.ContainsKey($name) -or (Get-ReleaseHash $path) -ne $checksums[$name]) {
        throw "Build checksum mismatch: $name"
    }
}
$manifest = Get-Content -LiteralPath (Join-Path $buildPath 'patch_manifest.json') -Raw | ConvertFrom-Json
if ($manifest.modName -ne 'baanish-armory' -or $manifest.modVersion -ne $version -or $manifest.gameVersion -ne '0.34.2') {
    throw 'Build manifest does not match the release candidate.'
}
$expectedAssets = @{}
Get-ChildItem -LiteralPath $modPath -File | Where-Object { $_.Name -notin @('modinfo.json', 'modinfo.json.meta', '.prototype-receipt.json') } | ForEach-Object {
    $expectedAssets['Assets/Blueprinter/Mods/baanish-armory/' + $_.Name] = $_.FullName
}
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $buildPath $sourceName))
try {
    if ($expectedAssets.Count -ne 16 -or $archive.Entries.Count -ne 17) { throw 'Expected eight authored assets, their metas and a source manifest.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if (-not $seen.Add($entry.FullName)) { throw 'Duplicate Blueprinter source entry.' }
        $stream = $entry.Open()
        try {
            if ($entry.FullName -eq 'source_manifest.json') {
                $reader = [IO.StreamReader]::new($stream)
                try { $sourceInfo = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                if ($sourceInfo.modName -ne 'baanish-armory' -or $sourceInfo.displayName -ne 'baanish-armory' -or $sourceInfo.version -ne $version) {
                    throw 'Blueprinter source identity differs from the candidate.'
                }
            } else {
                if (-not $expectedAssets.ContainsKey($entry.FullName)) { throw "Unexpected Blueprinter source entry: $($entry.FullName)" }
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                if ($hash -ne (Get-ReleaseHash $expectedAssets[$entry.FullName])) { throw "Build source differs from the working copy: $($entry.FullName)" }
            }
        } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }

# A release source archive follows the reviewed public tree, never the whole workspace.
$gitWorkspace = $workspacePath.Replace('\', '/')
$sourceFiles = @(git -c "safe.directory=$gitWorkspace" -C $workspacePath ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0 -or $sourceFiles.Count -eq 0) { throw 'Could not enumerate public source files.' }
$reviewedFiles = @(Get-Content -LiteralPath (Join-Path $workspacePath 'config/release-source-files.txt'))
if ((Compare-Object $reviewedFiles $sourceFiles -CaseSensitive) -or $reviewedFiles.Count -ne $sourceFiles.Count) {
    throw 'The public source file list changed. Review it and update config/release-source-files.txt before packaging.'
}
foreach ($name in $sourceFiles) {
    if ($name -match '(^|/)(\.local|\.git|_donotship|Library|Temp|UserSettings|Logs|BlueprinterCache|Generated|nuclearoption|TextMesh Pro|155mmrailgun|Example)(/|$)' -or
        $name -match '(?i)\.(dll|pdb|nobp|zip|blend\d*)$|\.prototype-receipt\.json$' -or
        $name -match '(^|/)\.\.?(/|$)|\\|:' -or
        $name -notmatch '^(\.gitattributes|\.gitignore|\.github/workflows/ci\.yml|README\.md|LICENSE|CONTRIBUTING\.md|THIRD-PARTY-NOTICES\.md|docs/[^/]+\.md|config/(prototype-baseline-0\.1\.5\.json|game-asset-references\.json|release-source-files\.txt)|scripts/(Build-Prototype|Prepare-Release|Test-Source|Publish-Release)\.ps1|art/references/eyeball-xl-lineart-clean-reference\.png|unity/.+)$') {
        throw "Unexpected public source file: $name"
    }
    Assert-ReleaseFile (Join-Path $workspacePath $name)
}
foreach ($required in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', 'docs/INSTALL.md', 'docs/BUILD.md', 'unity/LICENSE', 'config/prototype-baseline-0.1.5.json')) {
    if ($required -notin $sourceFiles) { throw "Required public source is excluded: $required" }
}

$outputPath = Join-Path $workspacePath ('.local/releases/' + $version + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
for ($current = Split-Path -Parent $outputPath; $current; $current = Split-Path -Parent $current) {
    if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Redirected release output: $current"
    }
}
if (Test-Path -LiteralPath $outputPath) { throw 'Release output already exists.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$metadataPath = Join-Path $outputPath 'meta.json'
[ordered]@{
    id = 'baanish-armory'
    artifact = [ordered]@{
        fileName = $bundleName; version = $version; category = 'preRelease'; type = 'addon'
        gameVersion = '0.34.2'; downloadUrl = ''; hash = 'sha256:' + $checksums[$bundleName]
        extends = @{ id = 'com.nikkorap.blueprinter'; version = '2.0.1' }
        dependencies = @(); incompatibilities = @()
    }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM

function Write-ReleaseArchive([string]$Name, [System.Collections.IDictionary]$Files) {
    $archivePath = Join-Path $outputPath $Name
    $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entryName in @($Files.Keys | Sort-Object)) {
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($Files[$entryName])
            try {
                $outputStream = $entry.Open()
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            } finally { $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($archive.Entries.Count -ne $Files.Count) { throw "Archive entry count differs: $Name" }
        foreach ($entry in $archive.Entries) {
            if (-not $Files.Contains($entry.FullName)) { throw "Unexpected archive entry: $($entry.FullName)" }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-ReleaseHash $Files[$entry.FullName])) { throw "Archive bytes differ: $($entry.FullName)" }
        }
    } finally { $archive.Dispose() }
}

$manualFiles = [ordered]@{
    "baanish-armory/$bundleName" = Join-Path $buildPath $bundleName
    'baanish-armory/meta.json' = $metadataPath
    'baanish-armory/README.md' = Join-Path $workspacePath 'docs/INSTALL.md'
    'baanish-armory/LICENSE' = Join-Path $workspacePath 'LICENSE'
    'baanish-armory/THIRD-PARTY-NOTICES.md' = Join-Path $workspacePath 'THIRD-PARTY-NOTICES.md'
    'baanish-armory/unity/LICENSE' = Join-Path $workspacePath 'unity/LICENSE'
}
Write-ReleaseArchive "baanish-armory_$version-manual-install.zip" $manualFiles
$repositoryFiles = [ordered]@{}
foreach ($name in $sourceFiles) { $repositoryFiles["baanish-armory/$name"] = Join-Path $workspacePath $name }
Write-ReleaseArchive "baanish-armory_$version-repository-source.zip" $repositoryFiles
foreach ($name in @($bundleName, $sourceName)) {
    Copy-Item -LiteralPath (Join-Path $buildPath $name) -Destination (Join-Path $outputPath $name)
    if ((Get-ReleaseHash (Join-Path $outputPath $name)) -ne $checksums[$name]) { throw "Copy checksum mismatch: $name" }
}
$artifacts = @(Get-ChildItem -LiteralPath $outputPath -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = Get-ReleaseHash $_.FullName }
})
$sourceSnapshot = @($sourceFiles | ForEach-Object { [ordered]@{ path = $_; sha256 = Get-ReleaseHash (Join-Path $workspacePath $_) } })
[ordered]@{ mod = 'baanish-armory'; version = $version; published = $false; sourceFileCount = $sourceFiles.Count; sourceFiles = $sourceSnapshot; artifacts = $artifacts } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'release-manifest.json') -Encoding utf8NoBOM
Get-ChildItem -LiteralPath $outputPath -File | Sort-Object Name | ForEach-Object { (Get-ReleaseHash $_.FullName) + '  ' + $_.Name } |
    Set-Content -LiteralPath (Join-Path $outputPath 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Host "Verified local release package: $outputPath"
if ($PassThru) { $outputPath }
