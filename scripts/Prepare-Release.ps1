#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$KarambitBuildDirectory,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent $PSScriptRoot
$modPath = Join-Path $workspacePath 'unity/Assets/Blueprinter/Mods/baanish-armory'
$version = (Get-Content -LiteralPath (Join-Path $modPath 'modinfo.json') -Raw | ConvertFrom-Json).version
if ($version -ne '0.3.0') { throw 'Review the packaging contract before preparing a different version.' }
$buildPath = [IO.Path]::GetFullPath($BuildDirectory, $PWD.Path)
$karambitBuildPath = [IO.Path]::GetFullPath($KarambitBuildDirectory, $PWD.Path)
$bundleName = "baanish-armory_$version.nobp"
$sourceName = "baanish-armory_$version.source.zip"
$karambitBundleName = 'baanish-karambit-prototype_0.1.0.nobp'
$karambitSourceName = 'baanish-karambit-prototype_0.1.0.source.zip'
$runtimeName = 'Baanish.Karambit.dll'
$runtimeRelativePath = 'runtime/' + $runtimeName

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

function Read-BuildChecksums([string]$Directory, [string[]]$Names) {
    $checksumPath = Join-Path $Directory 'SHA256SUMS.txt'
    Assert-ReleaseFile $checksumPath
    $checksums = @{}
    foreach ($line in Get-Content -LiteralPath $checksumPath) {
        if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_./-]+)$' -or
            $Matches[2] -cnotin $Names -or $checksums.ContainsKey($Matches[2])) {
            throw 'Malformed, unexpected or duplicate build checksum.'
        }
        $checksums[$Matches[2]] = $Matches[1]
    }
    if ($checksums.Count -ne $Names.Count) { throw 'Build checksums do not cover the exact release inputs.' }
    foreach ($name in $Names) {
        $path = Join-Path $Directory $name
        Assert-ReleaseFile $path
        if ((Get-ReleaseHash $path) -cne $checksums[$name]) { throw "Build checksum mismatch: $name" }
    }
    $checksums
}

function Assert-BuildSource([string]$Directory, [string]$ModName, [string]$ModVersion, [string]$SourceName, [int]$AssetCount) {
    $manifestPath = Join-Path $Directory 'patch_manifest.json'
    Assert-ReleaseFile $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.modName -cne $ModName -or $manifest.modVersion -cne $ModVersion -or $manifest.gameVersion -cne '0.34.2') {
        throw "Build manifest does not match the release candidate: $ModName"
    }
    $assetDirectory = Join-Path $workspacePath "unity/Assets/Blueprinter/Mods/$ModName"
    $expectedAssets = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    Get-ChildItem -LiteralPath $assetDirectory -File -Force |
        Where-Object Name -NotIn @('modinfo.json', 'modinfo.json.meta', '.prototype-receipt.json') | ForEach-Object {
            Assert-ReleaseFile $_.FullName
            $expectedAssets["Assets/Blueprinter/Mods/$ModName/" + $_.Name] = $_.FullName
        }
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $SourceName))
    try {
        if ($expectedAssets.Count -ne $AssetCount -or $archive.Entries.Count -ne $AssetCount + 1) {
            throw "Unexpected authored asset or source entry count: $ModName"
        }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if (-not $seen.Add($entry.FullName)) { throw 'Duplicate Blueprinter source entry.' }
            $stream = $entry.Open()
            try {
                if ($entry.FullName -ceq 'source_manifest.json') {
                    $reader = [IO.StreamReader]::new($stream)
                    try { $sourceInfo = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                    if ($sourceInfo.modName -cne $ModName -or $sourceInfo.displayName -cne $ModName -or $sourceInfo.version -cne $ModVersion) {
                        throw 'Blueprinter source identity differs from the candidate.'
                    }
                } else {
                    if (-not $expectedAssets.ContainsKey($entry.FullName)) { throw "Unexpected Blueprinter source entry: $($entry.FullName)" }
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                    if ($hash -cne (Get-ReleaseHash $expectedAssets[$entry.FullName])) { throw "Build source differs from the working copy: $($entry.FullName)" }
                }
            } finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
}

$checksums = Read-BuildChecksums $buildPath @($bundleName, $sourceName)
$karambitChecksums = Read-BuildChecksums $karambitBuildPath @($karambitBundleName, $karambitSourceName, $runtimeRelativePath)
Assert-BuildSource $buildPath 'baanish-armory' $version $sourceName 46
Assert-BuildSource $karambitBuildPath 'baanish-karambit-prototype' '0.1.0' $karambitSourceName 58
$runtimePath = Join-Path $karambitBuildPath $runtimeRelativePath
$assembly = [Reflection.AssemblyName]::GetAssemblyName($runtimePath)
if ($assembly.Name -cne 'Baanish.Karambit' -or $assembly.Version -ne [Version]'0.3.0.0') {
    throw 'Karambit runtime identity or compiled version differs from the release.'
}

# A release source archive follows the reviewed public tree, never the whole workspace.
& (Join-Path $PSScriptRoot 'Test-Source.ps1') | Out-Host
$sourceFiles = @(Get-Content -LiteralPath (Join-Path $workspacePath 'config/release-source-files.txt'))

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
        fileName = $runtimeName; version = $version; category = 'preRelease'; type = 'plugin'
        gameVersion = '0.34.2'; downloadUrl = ''; hash = 'sha256:' + $karambitChecksums[$runtimeRelativePath]
        dependencies = @(@{ id = 'com.nikkorap.blueprinter'; version = '2.0.1' }); incompatibilities = @()
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
    "baanish-armory/$karambitBundleName" = Join-Path $karambitBuildPath $karambitBundleName
    "baanish-armory/$runtimeName" = $runtimePath
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
foreach ($name in @($karambitBundleName, $karambitSourceName, $runtimeRelativePath)) {
    $destination = Join-Path $outputPath (Split-Path -Leaf $name)
    Copy-Item -LiteralPath (Join-Path $karambitBuildPath $name) -Destination $destination
    if ((Get-ReleaseHash $destination) -cne $karambitChecksums[$name]) { throw "Copy checksum mismatch: $name" }
}
$artifacts = @(Get-ChildItem -LiteralPath $outputPath -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = Get-ReleaseHash $_.FullName }
})
$sourceSnapshot = @($sourceFiles | ForEach-Object { [ordered]@{ path = $_; sha256 = Get-ReleaseHash (Join-Path $workspacePath $_) } })
[ordered]@{ mod = 'baanish-armory'; version = $version; published = $false; sourceFileCount = $sourceFiles.Count; sourceFiles = $sourceSnapshot; artifacts = $artifacts } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'release-manifest.json') -Encoding utf8NoBOM
Get-ChildItem -LiteralPath $outputPath -File | Sort-Object Name | ForEach-Object { (Get-ReleaseHash $_.FullName) + '  ' + $_.Name } |
    Set-Content -LiteralPath (Join-Path $outputPath 'SHA256SUMS.txt') -Encoding utf8NoBOM
if (@(Get-ChildItem -LiteralPath $outputPath -File).Count -ne 10) { throw 'Expected exactly ten verified release files.' }
Write-Host "Verified local release package: $outputPath"
if ($PassThru) { $outputPath }
