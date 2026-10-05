#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Template,
    [string]$MissionRoot = (Join-Path $env:USERPROFILE 'AppData/LocalLow/Shockfront/NuclearOption/Missions'),
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = Split-Path -Parent $PSScriptRoot
$templateJson = [IO.File]::ReadAllText($Template)
$templateMission = $templateJson | ConvertFrom-Json
$players = @($templateMission.aircraft | Where-Object playerControlled)
if ($templateMission.JsonVersion -ne 6 -or $players.Count -ne 1 -or $players[0].type -cne 'Multirole1') {
    throw 'Expected the native JsonVersion 6 airborne Ifrit template.'
}

$name = 'SAAM-18 Karambit - Ignus Interception'
$targetName = 'Karambit_Target'
$player = $players[0]
$player.UniqueName = 'Karambit_Ifrit'
$player.globalPosition = [ordered]@{ x = -60000.0; y = 609.6; z = 0.0 }
$player.rotation = [ordered]@{ x = 0.0; y = 0.0; z = 0.0; w = 1.0 }
$player.startingSpeed = 220.0
$mounts = @('autocannon_27mm_internal', 'baanish_karambit_6_internal', 'baanish_karambit_6_internal', '',
    'baanish_karambit_4_external', 'AAM2_double', 'TailHook_Multirole1')
$player.savedLoadout.Selected = @($mounts | ForEach-Object { [ordered]@{ Key = $_ } })
$medusa = ($player | ConvertTo-Json -Depth 30) | ConvertFrom-Json
$medusa.type = 'EW1'
$medusa.UniqueName = 'Karambit_Medusa'
$medusa.playerControlled = $false
$medusa.playerControlledPriority = 1
$medusa.globalPosition = [ordered]@{ x = -65000.0; y = 750.0; z = -2000.0 }
$medusa.bravery = 0.0
$medusaMounts = @('', '', 'Radome1', '', 'ARM1_single')
$medusa.savedLoadout.Selected = @($medusaMounts | ForEach-Object { [ordered]@{ Key = $_ } })

$missiles = @(for ($i = 0; $i -lt 20; $i++) {
    $x = -60000.0 + (($i % 10) - 4.5) * 200.0
    $z = 24076.0 + [Math]::Floor($i / 10) * 350.0
    $yaw = [Math]::Atan2(-60000.0 - $x, -6000.0 - $z)
    [ordered]@{
        type = 'AShM1'; faction = 'Boscali'; UniqueName = ('Karambit_Inbound_{0:D2}' -f ($i + 1))
        globalPosition = [ordered]@{ x = $x; y = 12.0; z = $z }
        rotation = [ordered]@{ x = 0.0; y = [Math]::Round([Math]::Sin($yaw / 2), 8); z = 0.0; w = [Math]::Round([Math]::Cos($yaw / 2), 8) }
        startingSpeed = 350.0; targetUnitName = $targetName; guidingUnit = ''
    }
})

$factions = @($templateMission.factions | Where-Object factionName -in 'Primeva', 'Boscali')
if ($factions.Count -ne 2) { throw 'Expected Primeva and Boscali factions.' }
foreach ($faction in $factions) {
    $faction.supplies = @([ordered]@{ UnitType = 'Multirole1'; Count = 99 })
    $faction.AIAircraftLimit = 0
    $faction.startingWarheads = 0
    $faction.preventJoin = $faction.factionName -eq 'Boscali'
}

$settings = $templateMission.missionSettings
$settings.playerMode = 'Singleplayer'
$settings.description = 'SAAM-18 Karambit prototype: Ifrit at 2,000 ft (609.6 m) over the western Ignus sea, heading north at 220 m/s, carrying 20 Karambits and four stock AAM-29 Scythes on two mirrored outer wing racks for comparison. A friendly EW-25 Medusa starts 5 km west and 2 km behind you at 750 m MSL, carrying a Radome and two ARAD-116s. It patrols around a point 5 km west and 2 km ahead while its radar shares contacts with Primeva. The incoming optical-guided missiles present no ARAD target. Twenty Boscali AShM-300s start 13 nautical miles (24.076 km) ahead at the stock maximum cruise speed and fly toward the friendly patrol boat 6 km behind you. A one-time initial datalink cue reveals the salvo after 3 seconds so you can designate incoming tracks. The prototype runtime initializes their cruise state; the native file carries 350 m/s as its fallback. Select incoming tracks and defend the boat. Restart Mission resets the salvo and ammunition. The patrol boat has short-range gun defenses.'

# Cruise guidance needs its faction to know the target before Initialize reads its position.
# Native objectives preserve that order in the saved mission, without framework commands.
$mission = [ordered]@{
    JsonVersion = 6
    MapKey = [ordered]@{ Type = 'GameWorldPrefab'; Path = 'Terrain_naval' }
    missionSettings = $settings
    environment = $templateMission.environment
    aircraft = @($player, $medusa)
    vehicles = @()
    ships = @([ordered]@{
        type = 'PatrolBoat1'; faction = 'Primeva'; UniqueName = $targetName
        globalPosition = [ordered]@{ x = -60000.0; y = 2.5; z = -6000.0 }
        rotation = [ordered]@{ x = 0.0; y = 0.0; z = 0.0; w = 1.0 }
        holdPosition = $true; skill = 0.0; waypoints = @()
    })
    buildings = @()
    scenery = @()
    containers = @()
    missiles = $missiles
    pilots = @()
    factions = $factions
    airbases = @()
    objectives = @(
        [ordered]@{ Type = 'None'; UniqueName = 'Mission Start'; Faction = ''; DisplayName = 'Mission Start'; Hidden = $true; Outcomes = @('Karambit Start Timers') }
        [ordered]@{ Type = 'WaitSeconds'; UniqueName = 'Karambit Reveal Target'; Faction = 'Boscali'; DisplayName = ''; Hidden = $true; seconds = 1.0; Outcomes = @('Karambit Target Track') }
        [ordered]@{ Type = 'WaitSeconds'; UniqueName = 'Karambit Start Salvo'; Faction = 'Boscali'; DisplayName = ''; Hidden = $true; seconds = 2.0; Outcomes = @('Karambit Spawn Salvo') }
        [ordered]@{ Type = 'WaitSeconds'; UniqueName = 'Karambit Initial Cue'; Faction = 'Primeva'; DisplayName = ''; Hidden = $true; seconds = 3.0; Outcomes = @('Karambit Reveal Salvo') }
        # Zero range keeps this native AI patrol destination active even if the player flies through it.
        [ordered]@{ Type = 'ReachWaypoints'; UniqueName = 'Karambit Medusa Patrol'; Faction = 'Primeva'; DisplayName = ''; Hidden = $true; Outcomes = @(); completeOrder = 'InOrder'; completeSomePercent = 0.5; completeOnEnterRange = $true; waypoints = @(
            [ordered]@{ Position = [ordered]@{ x = -65000.0; y = 750.0; z = 2000.0 }; Range = 0.0 }
        ) }
    )
    outcomes = @(
        [ordered]@{ Type = 'StartObjective'; UniqueName = 'Karambit Start Timers'; objectivesToStart = @('Karambit Reveal Target', 'Karambit Start Salvo', 'Karambit Initial Cue', 'Karambit Medusa Patrol') }
        [ordered]@{ Type = 'RevealUnit'; UniqueName = 'Karambit Target Track'; UnitsToReveal = @($targetName) }
        [ordered]@{ Type = 'SpawnUnit'; UniqueName = 'Karambit Spawn Salvo'; UnitsToSpawn = @($missiles.UniqueName) }
        [ordered]@{ Type = 'RevealUnit'; UniqueName = 'Karambit Reveal Salvo'; UnitsToReveal = @($missiles.UniqueName) }
    )
}
$missionJson = ($mission | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
$check = $missionJson | ConvertFrom-Json
if ($check.aircraft.Count -ne 2 -or @($check.aircraft | Where-Object playerControlled).Count -ne 1 -or
    -not $check.aircraft[0].playerControlled -or
    $check.aircraft[0].globalPosition.y -ne 609.6 -or $check.aircraft[0].startingSpeed -ne 220.0 -or
    ($check.aircraft[0].savedLoadout.Selected.Key -join '|') -cne ($mounts -join '|') -or
    $check.aircraft[1].UniqueName -cne 'Karambit_Medusa' -or $check.aircraft[1].type -cne 'EW1' -or
    $check.aircraft[1].faction -cne 'Primeva' -or $check.aircraft[1].playerControlled -or
    $check.aircraft[1].globalPosition.x -ne -65000.0 -or $check.aircraft[1].globalPosition.y -ne 750.0 -or
    $check.aircraft[1].globalPosition.z -ne -2000.0 -or $check.aircraft[1].startingSpeed -ne 220.0 -or
    ($check.aircraft[1].savedLoadout.Selected.Key -join '|') -cne ($medusaMounts -join '|') -or
    $check.MapKey.Path -cne 'Terrain_naval' -or $check.missiles.Count -ne 20 -or
    $check.missiles[0].globalPosition.z -ne 24076.0 -or $check.missiles[10].globalPosition.z -ne 24426.0 -or
    @($check.missiles | Where-Object { $_.type -cne 'AShM1' -or $_.targetUnitName -cne $targetName -or $_.globalPosition.y -ne 12.0 -or $_.startingSpeed -ne 350.0 }).Count -ne 0 -or
    ($check.outcomes[2].UnitsToSpawn -join '|') -cne ($check.missiles.UniqueName -join '|') -or
    $check.objectives[3].Faction -cne 'Primeva' -or $check.objectives[3].seconds -ne 3.0 -or
    ($check.outcomes[3].UnitsToReveal -join '|') -cne ($check.missiles.UniqueName -join '|') -or
    $check.objectives[4].Type -cne 'ReachWaypoints' -or $check.objectives[4].Faction -cne 'Primeva' -or
    -not $check.objectives[4].Hidden -or $check.objectives[4].waypoints.Count -ne 1 -or
    $check.objectives[4].waypoints[0].Range -ne 0.0 -or
    $check.objectives[4].waypoints[0].Position.x -ne -65000.0 -or
    $check.objectives[4].waypoints[0].Position.y -ne 750.0 -or
    $check.objectives[4].waypoints[0].Position.z -ne 2000.0 -or
    $check.outcomes[0].objectivesToStart -cnotcontains 'Karambit Medusa Patrol') {
    throw 'Generated mission validation failed.'
}

$fixture = Join-Path $workspace 'tests/agentic/karambit.mission.json'
$output = Join-Path $workspace '.local/karambit-mission'
$humanMission = Join-Path $output $name
$files = [ordered]@{
    $fixture = $missionJson
    (Join-Path $workspace 'tests/agentic/karambit-terrain.mission.json') = $missionJson
    (Join-Path $workspace 'tests/agentic/karambit-terrain-narrow.mission.json') = $missionJson
    (Join-Path $humanMission ($name + '.json')) = $missionJson
    (Join-Path $humanMission 'meta.json') = (([ordered]@{ FileName = $name } | ConvertTo-Json -Compress) + "`n")
}
foreach ($file in $files.GetEnumerator()) {
    if ($CheckOnly) {
        if (-not [IO.File]::Exists($file.Key) -or [IO.File]::ReadAllText($file.Key).Replace("`r`n", "`n") -cne $file.Value) {
            throw "Generated mission is missing or out of date: $($file.Key)"
        }
    } else {
        $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $file.Key))
        [IO.File]::WriteAllText($file.Key, $file.Value, [Text.UTF8Encoding]::new($false))
    }
}
if ([IO.File]::ReadAllText($Template) -cne $templateJson) { throw 'Source template changed during generation.' }
[ordered]@{
    name = $name; fixture = $fixture; missionFolder = $humanMission
    destination = [IO.Path]::GetFullPath((Join-Path $MissionRoot $name), $PWD.Path)
    altitudeFeet = 2000; altitudeMeters = 609.6; karambitCount = 20; scytheCount = 4; scytheRackCount = 2; incomingCount = 20; inboundDistanceNm = 13; inboundDistanceMeters = 24076; initialCueSeconds = 3; nativeFallbackSpeedMps = 350.0
    medusaName = 'Karambit_Medusa'; medusaAltitudeMeters = 750; medusaRadomeKey = 'Radome1'; medusaAradCount = 2
    sourceSha256 = (Get-FileHash -LiteralPath $Template -Algorithm SHA256).Hash
    missionSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    status = $(if ($CheckOnly) { 'verified' } else { 'generated' })
} | ConvertTo-Json
