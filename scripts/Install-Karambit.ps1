#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$GameDir = 'D:/SteamLibrary/steamapps/common/Nuclear Option',
    [string]$MissionRoot = (Join-Path $env:USERPROFILE 'AppData/LocalLow/Shockfront/NuclearOption/Missions'),
    [switch]$Update
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = Split-Path -Parent $PSScriptRoot
$build = Join-Path $workspace '.local/karambit-build'
$missionName = 'SAAM-18 Karambit - Ignus Interception'
$missionSource = Join-Path $workspace ".local/karambit-mission/$missionName"
$bep = Join-Path $GameDir 'BepInEx'
$pluginsPath = Join-Path $bep 'plugins'
$destination = Join-Path $pluginsPath 'baanish-karambit-prototype'
$metadataPath = Join-Path $destination 'meta.json'
$missionDestination = Join-Path $MissionRoot $missionName
$blueprinterEnabled = Join-Path $bep 'plugins/com.nikkorap.blueprinter'
$blueprinterDisabled = Join-Path $bep 'disabledPlugins/com.nikkorap.blueprinter'
function Assert-PlainPath([string]$Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = Split-Path -Parent $cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Redirected installation path: $cursor"
        }
    }
}
foreach ($path in @($build, $missionSource, $destination, $missionDestination, $blueprinterEnabled, $blueprinterDisabled)) {
    Assert-PlainPath $path
}
foreach ($folder in @($destination, $missionDestination)) {
    if (Test-Path -LiteralPath $folder) {
        foreach ($file in Get-ChildItem -LiteralPath $folder -File -Force) { Assert-PlainPath $file.FullName }
    }
}
if (Test-Path -LiteralPath $pluginsPath) {
    $destinationPrefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($destination)) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $pluginsPath -File -Recurse -Force) {
        if ($file.Name -in @('Baanish.Karambit.dll', 'baanish-karambit-prototype_0.1.0.nobp') -and
            -not [IO.Path]::GetFullPath($file.FullName).StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Karambit is already enabled outside the prototype destination: $($file.FullName)"
        }
    }
}
if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) { throw 'Close Nuclear Option before installing.' }
$runtimeFiles = @(Get-ChildItem -LiteralPath (Join-Path $build 'runtime') -Filter '*.dll')
if ($runtimeFiles.Count -ne 1) { throw 'Build the Karambit runtime first.' }
$bundle = Join-Path $build 'baanish-karambit-prototype_0.1.0.nobp'
if (-not (Test-Path -LiteralPath $bundle)) { throw 'Build the Karambit bundle first.' }
$checksums = @(Get-Content -LiteralPath (Join-Path $build 'SHA256SUMS.txt'))
$files = @($runtimeFiles[0].FullName, $bundle)
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($build, $file).Replace('\', '/')
    $expected = (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() + '  ' + $relative
    if ($expected -cnotin $checksums) { throw "Build checksum mismatch: $file" }
}
$missionFiles = @(Get-ChildItem -LiteralPath $missionSource -File)
if ($missionFiles.Count -ne 2 -or 'meta.json' -cnotin $missionFiles.Name -or
    ($missionName + '.json') -cnotin $missionFiles.Name) { throw 'Generate the saved Karambit mission first.' }
$receiptPath = Join-Path $build 'install-receipt.json'
Assert-PlainPath $receiptPath
if ($Update) {
    $previous = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($previous.prototype -ine $destination -or $previous.mission -ine $missionDestination) {
        throw 'The installation receipt belongs to different destination paths.'
    }
    if (@($previous.files | Where-Object { $_.path -ieq $metadataPath }).Count -ne 1) {
        throw 'The receipt does not verify prototype metadata. Remove the development prototype and saved mission, then reinstall without -Update to create a new receipt.'
    }
    foreach ($file in $previous.files) {
        if ((Get-FileHash -LiteralPath $file.path).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "Installed file changed since installation: $($file.path)"
        }
    }
    $oldMeta = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($oldMeta.id -cne 'dev.baanish.karambit' -or
        @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 3 -or
        @(Get-ChildItem -LiteralPath $missionDestination -Force).Count -ne 2) {
        throw 'The existing prototype or mission contains unexpected files.'
    }
} else {
    if (Test-Path -LiteralPath $destination) { throw "Prototype already installed: $destination. Use -Update with its installation receipt." }
    if (Test-Path -LiteralPath $missionDestination) { throw "Saved mission already exists: $missionDestination" }
}
$blueprinterSource = if (Test-Path -LiteralPath $blueprinterEnabled) { $blueprinterEnabled } else { $blueprinterDisabled }
$bpMeta = Get-Content -LiteralPath (Join-Path $blueprinterSource 'meta.json') -Raw | ConvertFrom-Json
if ($bpMeta.id -cne 'com.nikkorap.blueprinter' -or $bpMeta.artifact.version -cne '2.0.1' -or
    ('sha256:' + (Get-FileHash -LiteralPath (Join-Path $blueprinterSource $bpMeta.artifact.fileName)).Hash.ToLowerInvariant()) -cne $bpMeta.artifact.hash) {
    throw 'Blueprinter 2.0.1 installation verification failed.'
}
$receipt = [ordered]@{
    prototype = $destination
    mission = $missionDestination
    blueprinter = $blueprinterEnabled
    blueprinterWasDisabled = ($blueprinterSource -eq $blueprinterDisabled)
    files = @()
}
if ($Update) {
    $backup = Join-Path $workspace ('.local/karambit-backups/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    Assert-PlainPath $backup
    foreach ($entry in @(@{ source = $destination; folder = 'prototype' }, @{ source = $missionDestination; folder = 'mission' })) {
        $targetFolder = Join-Path $backup $entry.folder
        New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $entry.source -File) {
            $target = Join-Path $targetFolder $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination $target
            if ((Get-FileHash -LiteralPath $target).Hash -cne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                throw "Backup verification failed: $target"
            }
        }
    }
    $receipt.backup = $backup
} else { New-Item -ItemType Directory -Path $destination | Out-Null }
foreach ($file in $files) {
    $target = Join-Path $destination (Split-Path -Leaf $file)
    Copy-Item -LiteralPath $file -Destination $target -Force
    $hash = (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
    if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -cne $hash) { throw "Installed hash mismatch: $target" }
    $receipt.files += [ordered]@{ path = $target; sha256 = $hash }
}
$meta = [ordered]@{
    id = 'dev.baanish.karambit'
    artifact = [ordered]@{
        fileName = $runtimeFiles[0].Name
        version = '0.3.0'
        category = 'preRelease'
        type = 'plugin'
        gameVersion = '0.34.2'
        hash = ('sha256:' + (Get-FileHash -LiteralPath $runtimeFiles[0].FullName).Hash.ToLowerInvariant())
    }
}
[IO.File]::WriteAllText($metadataPath, ($meta | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$receipt.files += [ordered]@{ path = $metadataPath; sha256 = (Get-FileHash -LiteralPath $metadataPath).Hash.ToLowerInvariant() }
if (-not $Update) { New-Item -ItemType Directory -Path $missionDestination | Out-Null }
foreach ($file in $missionFiles) {
    $target = Join-Path $missionDestination $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -cne $hash) { throw "Saved mission hash mismatch: $target" }
    $receipt.files += [ordered]@{ path = $target; sha256 = $hash }
}
if ($receipt.blueprinterWasDisabled) {
    Move-Item -LiteralPath $blueprinterDisabled -Destination $blueprinterEnabled
}
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host "Installed SAAM-18 Karambit and saved mission: $missionName"
Write-Host "Receipt: $receiptPath"
