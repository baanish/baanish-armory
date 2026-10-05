#Requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixturePath = Join-Path $workspacePath ('.local/tests/prepare-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixturePath | Out-Null

function Assert-ReleaseTest([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-TestHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$gitWorkspace = $workspacePath.Replace('\', '/')
$publicFiles = @(git -c "safe.directory=$gitWorkspace" -C $workspacePath ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0 -or $publicFiles.Count -eq 0) { throw 'Could not enumerate the packaging fixture source.' }
$publicFiles = @($publicFiles | Where-Object { Test-Path -LiteralPath (Join-Path $workspacePath $_) -PathType Leaf })
foreach ($name in $publicFiles) {
    $destination = Join-Path $fixturePath $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspacePath $name) -Destination $destination
}
git -C $fixturePath init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the packaging fixture repository.' }
[IO.File]::WriteAllLines((Join-Path $fixturePath 'config/release-source-files.txt'), [string[]]$publicFiles, [Text.UTF8Encoding]::new($false))

$mainBuild = Join-Path $fixturePath '.local/main-build'
$karambitBuild = Join-Path $fixturePath '.local/karambit-build'
New-Item -ItemType Directory -Path $mainBuild, (Join-Path $karambitBuild 'runtime') | Out-Null

function New-TestBuild([string]$Directory, [string]$ModName, [string]$Version) {
    $assetDirectory = Join-Path $fixturePath "unity/Assets/Blueprinter/Mods/$ModName"
    [IO.File]::WriteAllText((Join-Path $Directory "$($ModName)_$Version.nobp"), "fixture bundle $ModName $Version")
    @{ modName = $ModName; modVersion = $Version; gameVersion = '0.34.2' } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $Directory 'patch_manifest.json') -Encoding utf8NoBOM
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $Directory "$($ModName)_$Version.source.zip"), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $assetDirectory -File -Force |
            Where-Object Name -NotIn @('modinfo.json', 'modinfo.json.meta', '.prototype-receipt.json')) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "Assets/Blueprinter/Mods/$ModName/" + $file.Name)
        }
        $entry = $archive.CreateEntry('source_manifest.json')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write((@{ modName = $ModName; displayName = $ModName; version = $Version } | ConvertTo-Json)) }
        finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
}

New-TestBuild $mainBuild 'baanish-armory' '0.3.0'
New-TestBuild $karambitBuild 'baanish-karambit-prototype' '0.1.0'
$runtimeProject = Join-Path $fixturePath '.local/runtime-project'
$offlineSource = Join-Path $runtimeProject 'offline-source'
New-Item -ItemType Directory -Path $offlineSource | Out-Null
$projectPath = Join-Path $runtimeProject 'Fixture.csproj'
[IO.File]::WriteAllText($projectPath, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>Baanish.Karambit</AssemblyName><Version>0.3.0</Version></PropertyGroup></Project>')
dotnet build $projectPath -c Release --nologo "-p:RestoreSources=$offlineSource" -o (Join-Path $karambitBuild 'runtime') | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Fixture DLL compilation failed; packaging checks did not run.' }
$runtimePath = Join-Path $karambitBuild 'runtime/Baanish.Karambit.dll'
Assert-ReleaseTest (Test-Path -LiteralPath $runtimePath -PathType Leaf) 'Fixture DLL compilation produced no runtime.'

function Write-TestChecksums([string]$Directory, [string[]]$Names) {
    $lines = foreach ($name in $Names) { (Get-TestHash (Join-Path $Directory $name)) + '  ' + $name }
    [IO.File]::WriteAllLines((Join-Path $Directory 'SHA256SUMS.txt'), [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

$mainNames = @('baanish-armory_0.3.0.nobp', 'baanish-armory_0.3.0.source.zip')
$karambitNames = @('baanish-karambit-prototype_0.1.0.nobp', 'baanish-karambit-prototype_0.1.0.source.zip', 'runtime/Baanish.Karambit.dll')
Write-TestChecksums $mainBuild $mainNames
Write-TestChecksums $karambitBuild $karambitNames
# Extra build outputs must never become install payloads.
[IO.File]::WriteAllText((Join-Path $karambitBuild 'runtime/Assembly-CSharp.dll'), 'excluded game assembly')
[IO.File]::WriteAllText((Join-Path $karambitBuild 'runtime/TestHelper.dll'), 'excluded test helper')

$prepareScript = Join-Path $fixturePath 'scripts/Prepare-Release.ps1'
$releasePath = & $prepareScript -BuildDirectory $mainBuild -KarambitBuildDirectory $karambitBuild -PassThru
Assert-ReleaseTest ($releasePath -is [string] -and (Test-Path -LiteralPath $releasePath -PathType Container)) 'Packaging did not return one release directory.'
$expectedFiles = @(
    'baanish-armory_0.3.0-manual-install.zip', 'baanish-armory_0.3.0-repository-source.zip',
    'baanish-armory_0.3.0.nobp', 'baanish-armory_0.3.0.source.zip',
    'baanish-karambit-prototype_0.1.0.nobp', 'baanish-karambit-prototype_0.1.0.source.zip',
    'Baanish.Karambit.dll', 'meta.json', 'release-manifest.json', 'SHA256SUMS.txt'
)
$actualFiles = @(Get-ChildItem -LiteralPath $releasePath -File | Select-Object -ExpandProperty Name)
Assert-ReleaseTest ($actualFiles.Count -eq 10 -and -not (Compare-Object $expectedFiles $actualFiles -CaseSensitive)) 'Release exports differ from the ten-file contract.'
$metadata = Get-Content -LiteralPath (Join-Path $releasePath 'meta.json') -Raw | ConvertFrom-Json
$artifact = $metadata.artifact
Assert-ReleaseTest ($metadata.id -ceq 'baanish-armory' -and $artifact.fileName -ceq 'Baanish.Karambit.dll' -and
    $artifact.version -ceq '0.3.0' -and $artifact.type -ceq 'plugin' -and $artifact.category -ceq 'preRelease' -and
    $artifact.gameVersion -ceq '0.34.2' -and $artifact.hash -ceq ('sha256:' + (Get-TestHash $runtimePath))) 'NOMM metadata does not identify the coupled runtime.'
Assert-ReleaseTest ('extends' -cnotin $artifact.PSObject.Properties.Name -and $artifact.dependencies.Count -eq 1 -and
    $artifact.dependencies[0].id -ceq 'com.nikkorap.blueprinter' -and $artifact.dependencies[0].version -ceq '2.0.1') 'NOMM metadata must depend on Blueprinter without extending it.'
$manualArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $releasePath 'baanish-armory_0.3.0-manual-install.zip'))
try {
    $expectedEntries = @('baanish-armory/Baanish.Karambit.dll', 'baanish-armory/baanish-armory_0.3.0.nobp',
        'baanish-armory/baanish-karambit-prototype_0.1.0.nobp', 'baanish-armory/meta.json', 'baanish-armory/README.md',
        'baanish-armory/LICENSE', 'baanish-armory/THIRD-PARTY-NOTICES.md', 'baanish-armory/unity/LICENSE')
    $actualEntries = @($manualArchive.Entries | Select-Object -ExpandProperty FullName)
    Assert-ReleaseTest ($actualEntries.Count -eq 8 -and -not (Compare-Object $expectedEntries $actualEntries -CaseSensitive)) 'Manual ZIP must contain exactly the coupled payload and notices.'
    foreach ($entry in $manualArchive.Entries | Where-Object FullName -Match '\.(dll|nobp)$') {
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        Assert-ReleaseTest ($hash -ceq (Get-TestHash (Join-Path $releasePath (Split-Path -Leaf $entry.FullName)))) 'Manual ZIP payload bytes differ from verified exports.'
    }
} finally { $manualArchive.Dispose() }

function Assert-RejectedInput([string]$ExpectedError) {
    $before = @(Get-ChildItem -LiteralPath (Join-Path $fixturePath '.local/releases') -Directory).Count
    $failure = $null
    try { & $prepareScript -BuildDirectory $mainBuild -KarambitBuildDirectory $karambitBuild -PassThru | Out-Null }
    catch { $failure = $_.Exception.Message }
    Assert-ReleaseTest ($null -ne $failure -and $failure.Contains($ExpectedError, [StringComparison]::Ordinal)) "Expected rejection containing '$ExpectedError', got '$failure'."
    $after = @(Get-ChildItem -LiteralPath (Join-Path $fixturePath '.local/releases') -Directory).Count
    Assert-ReleaseTest ($before -eq $after) 'Invalid input created a release output directory.'
}

$savedRuntime = [IO.File]::ReadAllBytes($runtimePath)
Remove-Item -LiteralPath $runtimePath
Assert-RejectedInput 'Missing file:'
[IO.File]::WriteAllBytes($runtimePath, $savedRuntime)
$mainBundle = Join-Path $mainBuild $mainNames[0]
$savedBundle = [IO.File]::ReadAllBytes($mainBundle)
[IO.File]::AppendAllText($mainBundle, 'tampered')
Assert-RejectedInput 'Build checksum mismatch:'
[IO.File]::WriteAllBytes($mainBundle, $savedBundle)
$karambitSource = Join-Path $karambitBuild $karambitNames[1]
$savedSource = [IO.File]::ReadAllBytes($karambitSource)
$archive = [IO.Compression.ZipFile]::Open($karambitSource, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entryName = 'Assets/Blueprinter/Mods/baanish-karambit-prototype/baanish_karambit.prefab'
    $entry = $archive.GetEntry($entryName)
    Assert-ReleaseTest ($null -ne $entry) 'The source fixture has no Karambit flight prefab.'
    $entry.Delete()
    $writer = [IO.StreamWriter]::new($archive.CreateEntry($entryName).Open())
    try { $writer.Write('stale motor allocation') } finally { $writer.Dispose() }
} finally { $archive.Dispose() }
Write-TestChecksums $karambitBuild $karambitNames
Assert-RejectedInput 'Build source differs from the working copy:'
[IO.File]::WriteAllBytes($karambitSource, $savedSource)
Write-TestChecksums $karambitBuild $karambitNames
Write-Host "PASS: coupled payload, NOMM dependency metadata, ten exports, missing runtime, tampered artifact and stale source checks. Fixture: $fixturePath"
