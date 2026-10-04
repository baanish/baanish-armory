#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$GameDir = $env:NUCLEAR_OPTION_DIR,
    [switch]$BuildHelper,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $workspace 'tests/agentic/karambit.mission.json'
$sourceJson = [IO.File]::ReadAllText($sourcePath)
$mission = $sourceJson | ConvertFrom-Json
$players = @($mission.aircraft | Where-Object playerControlled)
$boats = @($mission.ships | Where-Object UniqueName -ceq 'Karambit_Target')
if ($mission.JsonVersion -ne 6 -or $players.Count -ne 1 -or $players[0].UniqueName -cne 'Karambit_Ifrit' -or
    $players[0].type -cne 'Multirole1' -or $mission.missiles.Count -ne 20 -or
    @($mission.missiles | Where-Object { $_.type -cne 'AShM1' -or $_.targetUnitName -cne 'Karambit_Target' }).Count -ne 0 -or
    $boats.Count -ne 1 -or -not $boats[0].holdPosition -or $boats[0].globalPosition.x -ne -60000 -or
    $boats[0].globalPosition.z -ne -6000) {
    throw 'Expected the native Karambit Ifrit mission with twenty AShM-300s.'
}
$mounts = @('autocannon_27mm_internal', 'baanish_karambit_6_internal', 'baanish_karambit_6_internal', '',
    'baanish_karambit_4_external', '', 'TailHook_Multirole1')
$players[0].savedLoadout.Selected = @($mounts | ForEach-Object { [ordered]@{ Key = $_ } })
$mission.missionSettings.description = $mission.missionSettings.description.Replace('carrying 20 Karambits and four stock AAM-29 Scythes on two mirrored outer wing racks for comparison',
    'carrying 20 Karambits for one native twenty-target salvo')
$missionJson = ($mission | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
$inboundNames = @(1..20 | ForEach-Object { 'Karambit_Inbound_{0:D2}' -f $_ })
if (($mission.missiles.UniqueName -join '|') -cne ($inboundNames -join '|')) {
    throw 'The native salvo must have the twenty distinct incoming track names 01..20.'
}
$state = 'mods.dev.baanish.karambit.salvo-test.'
$trackConditions = @(foreach ($name in $inboundNames) {
    [ordered]@{ path = "contacts.$name.live"; eq = $true }
    [ordered]@{ path = "contacts.$name.range_m"; lt = 22224 }
})
$steps = @(
    [ordered]@{ require = [ordered]@{ path = 'own.type'; eq = 'Multirole1' }; label = 'the player is in the Ifrit' }
    [ordered]@{ fly = [ordered]@{ mode = 'hold'; alt_msl_m = 609.6; hdg_deg = 0; throttle = 0.7 } }
    [ordered]@{ act = [ordered]@{ station = 1 } }
    [ordered]@{ require = [ordered]@{ all = @(
        [ordered]@{ path = 'own.station.name'; eq = 'SAAM-18 Karambit' }
        [ordered]@{ path = 'own.station.ammo'; eq = 20 }
    ) }; label = 'the derivative Ifrit has exactly twenty Karambits' }
    [ordered]@{ wait = [ordered]@{ path = 'mods.dev.baanish.karambit.inbound_warmstarts'; eq = 20 }; timeout = 10; label = 'all twenty native inbounds started in flight' }
    [ordered]@{ wait = [ordered]@{ all = $trackConditions }; timeout = 35; label = 'all twenty incoming tracks are live inside the 12 nm envelope' }
    [ordered]@{ config = [ordered]@{ guid = 'dev.baanish.karambit.salvo-test'; section = 'Test'; key = 'SelectSalvo'; value = $true } }
    [ordered]@{ wait = [ordered]@{ all = @(
        [ordered]@{ path = ($state + 'selected_count'); eq = 20 }
        [ordered]@{ path = ($state + 'selected_distinct_count'); eq = 20 }
        [ordered]@{ path = ($state + 'selected_names'); eq = ($inboundNames -join ' ') }
        [ordered]@{ path = ($state + 'error'); eq = '' }
        [ordered]@{ path = 'own.target.count'; eq = 20 }
        [ordered]@{ path = 'own.target'; eq = ($inboundNames -join ' ') }
    ) }; timeout = 2; label = 'the actual native HUD and weapon lists contain all twenty distinct tracks' }
    [ordered]@{ wait = [ordered]@{ path = 'own.station.ready'; eq = $true }; timeout = 10; label = 'the selected weapon can start its native salvo' }
    [ordered]@{ require = [ordered]@{ path = 'truth.missiles.count'; eq = 20 }; label = 'all twenty incoming missiles are alive before the trigger' }
    [ordered]@{ mark = [ordered]@{ label = 'salvo' } }
    [ordered]@{ act = [ordered]@{ fire = 5 } }
    [ordered]@{ wait = [ordered]@{ path = ($state + 'mission_time_s'); between = @(45, 50) }; timeout = 50; label = 'measure at 45 s before the patrol boat can engage the incoming missiles' }
    [ordered]@{ check = [ordered]@{ custom = [ordered]@{ name = 'CruiseSalvoObserved'; args = [ordered]@{ since = 'salvo' } } }; within = 1; label = 'report original inbound survivors and actual launches before asserting success' }
    [ordered]@{ record = [ordered]@{ path = 'truth.missiles.count' }; label = 'uncapped final live missile count, including remaining Karambits' }
    [ordered]@{ record = [ordered]@{ path = 'own.station.ammo' }; label = 'ammunition remaining after the continuous trigger' }
    [ordered]@{ record = [ordered]@{ path = ($state + 'mission_time_s') }; label = 'native mission time of the survivor measurement' }
    [ordered]@{ screenshot = [ordered]@{ name = 'karambit-salvo-result' } }
    [ordered]@{ require = [ordered]@{ custom = [ordered]@{ name = 'CruiseSalvoDestroyed'; args = [ordered]@{ since = 'salvo' } } }; label = 'twenty distinct designated launches remove all twenty original AShM-300s before boat defense range' }
)
$scenario = [ordered]@{
    mission = 'karambit-salvo.mission.json'
    checks = @('karambit_salvo.cs')
    mods = 'clean'
    plugins = @(
        'installed:com.nikkorap.blueprinter'
        '../../.local/karambit-build/runtime'
        '../../.local/karambit-build/salvo-control'
        'content:../../.local/karambit-build/baanish-karambit-prototype_0.1.0.nobp host=com.nikkorap.blueprinter keys=baanish_karambit,baanish_karambit_6_internal,baanish_karambit_4_external'
    )
    timeout = 150
    steps = $steps
}
$files = [ordered]@{
    (Join-Path $workspace 'tests/agentic/karambit-salvo.mission.json') = $missionJson
    (Join-Path $workspace 'tests/agentic/karambit-salvo.scenario.jsonc') = (($scenario | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n")
}
foreach ($file in $files.GetEnumerator()) {
    if ($CheckOnly) {
        if (-not [IO.File]::Exists($file.Key) -or [IO.File]::ReadAllText($file.Key).Replace("`r`n", "`n") -cne $file.Value) {
            throw "Generated salvo test is missing or out of date: $($file.Key)"
        }
    } else {
        [IO.File]::WriteAllText($file.Key, $file.Value, [Text.UTF8Encoding]::new($false))
    }
}
if ([IO.File]::ReadAllText($sourcePath) -cne $sourceJson) { throw 'The original Scythe comparison mission changed during generation.' }
if ($BuildHelper) {
    if (-not $GameDir) { throw 'BuildHelper requires GameDir or NUCLEAR_OPTION_DIR.' }
    & dotnet build (Join-Path $workspace 'tests/KarambitSalvoControl/KarambitSalvoControl.csproj') -c Release "-p:GameDir=$GameDir" -o (Join-Path $workspace '.local/karambit-build/salvo-control') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The native salvo selector build failed.' }
}
[ordered]@{
    status = $(if ($CheckOnly) { 'verified' } else { 'generated' })
    mission = 'tests/agentic/karambit-salvo.mission.json'
    scenario = 'tests/agentic/karambit-salvo.scenario.jsonc'
    helper = '.local/karambit-build/salvo-control/Baanish.Karambit.SalvoTest.dll'
    selectedTracks = 20; karambitCount = 20; incomingCount = 20; continuousTriggerSeconds = 5
    measurementMissionSeconds = 45; latestEvidenceMissionSeconds = 50
} | ConvertTo-Json
