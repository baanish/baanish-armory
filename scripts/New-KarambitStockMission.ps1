#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Template = (Join-Path (Split-Path -Parent $PSScriptRoot) '.local/karambit-mission/stock-cruise-interception.mission.json'),
    [string]$MissionRoot = (Join-Path $env:USERPROFILE 'AppData/LocalLow/Shockfront/NuclearOption/Missions'),
    [switch]$CheckOnly,
    [switch]$Install,
    [switch]$Update
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($CheckOnly -and $Install) { throw 'CheckOnly and Install are mutually exclusive.' }
if ($CheckOnly -and $Update) { throw 'CheckOnly and Update are mutually exclusive.' }
$workspace = Split-Path -Parent $PSScriptRoot
$name = 'SAAM-18 Karambit - Cruise Missile Interception'
$sourceHash = '17c519dcd6d4e86c3f8b2f8a11a3c170882880b83712f7ef1298a2c680ec06dc'
$previousMissionHash = '832b8860f926eebd7c5493b3f07e1d1d323a1edad1e184d5d842abd3360dfd6f'
$output = Join-Path $workspace ('.local/karambit-mission/' + $name)
$destination = Join-Path ([IO.Path]::GetFullPath($MissionRoot)) $name
$receiptPath = Join-Path $workspace '.local/karambit-mission/stock-interception-receipt.json'
$encoding = [Text.UTF8Encoding]::new($false)

function Assert-NoRedirect([string]$Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = Split-Path -Parent $current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Redirected path rejected: $current" }
    }
}
function Get-BytesHash([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
function Assert-TwoFileFolder([string]$Folder) {
    Assert-NoRedirect $Folder
    if (Test-Path -LiteralPath $Folder) {
        if (-not [IO.Directory]::Exists($Folder)) { throw "Mission folder is not a directory: $Folder" }
        foreach ($entry in Get-ChildItem -LiteralPath $Folder -Force) {
            if ($entry.PSIsContainer -or $entry.Name -cnotin @(($name + '.json'), 'meta.json')) {
                throw "Unexpected mission folder entry: $($entry.FullName)"
            }
            Assert-NoRedirect $entry.FullName
        }
    }
}

Assert-NoRedirect $Template
$templateBytes = [IO.File]::ReadAllBytes($Template)
if ((Get-BytesHash $templateBytes) -cne $sourceHash) { throw 'Stock intercept template SHA256 mismatch.' }
$original = $encoding.GetString($templateBytes) | ConvertFrom-Json
$mission = $encoding.GetString($templateBytes) | ConvertFrom-Json
$players = @($mission.aircraft | Where-Object playerControlled)
$originalPlayers = @($original.aircraft | Where-Object playerControlled)
if ($mission.JsonVersion -ne 6 -or $players.Count -ne 1 -or $players[0].type -cne 'Fighter1' -or
    $players[0].UniqueName -cne 'Fighter1' -or
    ($players[0].savedLoadout.Selected.Key -join '|') -cne 'gun_20mm_internal|AAM1_double_internal|AAM1_double|AAM2_single') {
    throw 'Expected the native JsonVersion 6 Cruise Missile Interception Revoker loadout.'
}
$mounts = @('gun_20mm_internal', 'baanish_karambit_4_internal', 'baanish_karambit_6_external', 'AAM2_single')
$players[0].savedLoadout.Selected = @($mounts | ForEach-Object { [ordered]@{ Key = $_ } })
$mission.missionSettings.description = 'Stock Cruise Missile Interception with the FS-12 Revoker carrying 20 SAAM-18 Karambits: eight in its internal bays and twelve on its wing pylons, plus two stock AAM-29 Scythes on its wingtips and its stock 20mm cannon. Intercept the cruise missiles launched by two Primeva Darkreaches before they strike North Boscali Airbase. The stock Heartland mission start, enemy loadouts, objectives, timers and no-respawn rule are retained. Requires the SAAM-18 Karambit prototype bundle and runtime.'
$missionJson = ($mission | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
$check = $missionJson | ConvertFrom-Json
$checkPlayers = @($check.aircraft | Where-Object playerControlled)
if ($checkPlayers.Count -ne 1 -or $checkPlayers[0].type -cne 'Fighter1' -or
    $checkPlayers[0].UniqueName -cne 'Fighter1' -or
    ($checkPlayers[0].savedLoadout.Selected.Key -join '|') -cne ($mounts -join '|')) {
    throw 'Generated Revoker loadout validation failed.'
}
$checkPlayers[0].savedLoadout.Selected = $originalPlayers[0].savedLoadout.Selected
$check.missionSettings.description = $original.missionSettings.description
if (($check | ConvertTo-Json -Depth 100 -Compress) -cne ($original | ConvertTo-Json -Depth 100 -Compress)) {
    throw 'Generated mission changed stock data beyond the player loadout and description.'
}
$metaJson = ([ordered]@{ FileName = $name } | ConvertTo-Json -Compress) + "`n"
$receipt = [ordered]@{ name = $name; template = [IO.Path]::GetFullPath($Template).Replace('\', '/'); sourceSha256 = $sourceHash
    missionFolder = $output.Replace('\', '/'); karambitCount = 20; preservedStockData = $true
    missionSha256 = Get-BytesHash ($encoding.GetBytes($missionJson)); metaSha256 = Get-BytesHash ($encoding.GetBytes($metaJson)) }
$files = [ordered]@{ (Join-Path $output ($name + '.json')) = $missionJson; (Join-Path $output 'meta.json') = $metaJson
    $receiptPath = (($receipt | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n") }
$previousReceipt = $receipt | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$previousReceipt.missionSha256 = $previousMissionHash
$previousFiles = @{
    (Join-Path $output ($name + '.json')) = $previousMissionHash
    $receiptPath = Get-BytesHash ($encoding.GetBytes((($previousReceipt | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n")))
}
Assert-TwoFileFolder $output
if ($Install) {
    if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) { throw 'Close Nuclear Option before installing the mission.' }
    Assert-TwoFileFolder $destination
    $files[(Join-Path $destination ($name + '.json'))] = $missionJson
    $files[(Join-Path $destination 'meta.json')] = $metaJson
    $previousFiles[(Join-Path $destination ($name + '.json'))] = $previousMissionHash
}
$replacements = @()
foreach ($file in $files.GetEnumerator()) {
    Assert-NoRedirect $file.Key
    if ([IO.File]::Exists($file.Key)) {
        $existingHash = Get-BytesHash ([IO.File]::ReadAllBytes($file.Key))
        if ($existingHash -cne (Get-BytesHash ($encoding.GetBytes($file.Value)))) {
            if (-not $Update -or -not $previousFiles.ContainsKey($file.Key) -or $existingHash -cne $previousFiles[$file.Key]) {
                throw "Existing file differs; refusing to overwrite: $($file.Key)"
            }
            $replacements += $file.Key
        }
    } elseif ($CheckOnly) { throw "Generated file is missing: $($file.Key)" }
}
if ((Get-BytesHash ([IO.File]::ReadAllBytes($Template))) -cne $sourceHash) { throw 'Source template changed during generation.' }
if ($replacements.Count -gt 0) {
    $backup = Join-Path $workspace ('.local/karambit-mission/backups/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    Assert-NoRedirect $backup
    $null = [IO.Directory]::CreateDirectory($backup)
    foreach ($path in $replacements) {
        $target = Join-Path $backup (([Array]::IndexOf($replacements, $path)).ToString() + '-' + (Split-Path -Leaf $path))
        Copy-Item -LiteralPath $path -Destination $target
        if ((Get-BytesHash ([IO.File]::ReadAllBytes($target))) -cne $previousFiles[$path]) {
            throw "Mission backup verification failed: $target"
        }
    }
}
foreach ($file in $files.GetEnumerator()) {
    Assert-NoRedirect $file.Key
    if (-not $CheckOnly -and (-not [IO.File]::Exists($file.Key) -or $file.Key -cin $replacements)) {
        $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $file.Key))
        $mode = if ($file.Key -cin $replacements) { [IO.FileMode]::Create } else { [IO.FileMode]::CreateNew }
        $stream = [IO.FileStream]::new($file.Key, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = $encoding.GetBytes($file.Value); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    }
    if ((Get-BytesHash ([IO.File]::ReadAllBytes($file.Key))) -cne (Get-BytesHash ($encoding.GetBytes($file.Value)))) {
        throw "Output hash verification failed: $($file.Key)"
    }
}
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 2) { throw 'Generated mission must contain exactly two files.' }
if ($Install -and @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 2) { throw 'Installed mission must contain exactly two files.' }
if ((Get-BytesHash ([IO.File]::ReadAllBytes($Template))) -cne $sourceHash) { throw 'Source template changed during output.' }
$receipt | ConvertTo-Json
