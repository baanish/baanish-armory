using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Baanish.Karambit;

[BepInPlugin(PluginGuid, "SAAM-18 Karambit", "0.3.0")]
public sealed class KarambitPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "dev.baanish.karambit";
    private static readonly Dictionary<ARHSeeker, KarambitSeeker> Seekers = new();
    private static ManualLogSource log = null!;
    private static int launches, locks, reacquires, inboundWarmstarts;
    private static float inboundCruiseSpeed;
    private Harmony harmony = null!;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(KarambitPlugin).Assembly);
        Logger.LogInfo("SAAM-18 Karambit ready; key=baanish_karambit motor_mach_limit=2.5 cue=1500m ground_below_agl=304.8m air=aircraft/anti_air/ballistic/loft cruise_clearance=3.048m terrain_lookahead=6s terminal=1250m jam_tolerance=0.60 lock_persistence=4s");
    }

    private void OnDestroy()
    {
        foreach (KarambitSeeker seeker in Seekers.Values)
            seeker.Unsubscribe();
        Seekers.Clear();
        harmony.UnpatchSelf();
    }

    public Dictionary<string, object> AgenticState()
    {
        var active = new Dictionary<string, object>();
        foreach (KarambitSeeker seeker in Seekers.Values)
            if (!seeker.Missile.disabled)
                active[seeker.Missile.persistentID.ToString()] = seeker.Telemetry();
        return new Dictionary<string, object>
        {
            ["launches"] = launches,
            ["locks"] = locks,
            ["reacquires"] = reacquires,
            ["inbound_warmstarts"] = inboundWarmstarts,
            ["inbound_cruise_speed_mps"] = inboundCruiseSpeed,
            ["active_count"] = active.Count,
            ["missiles"] = active
        };
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnSavedMissile))]
    private static class InboundWarmstartPatch
    {
        private static void Prefix(Spawner __instance, GameObject prefab, GlobalPosition globalPosition, Quaternion rotation,
            string targetName, string uniqueName, ref Vector3 startingVel, out bool __state)
        {
            Missile source = prefab.GetComponent<Missile>();
            bool hasTestIfrit = UnitRegistry.allUnits.Exists(unit => unit.UniqueName == "Karambit_Ifrit" && unit is Aircraft);
            __state = __instance.IsServer && source != null && KarambitPolicy.WarmStartInbound(
                MissionManager.CurrentMission?.Name ?? "", uniqueName, source.definition.jsonKey, targetName, hasTestIfrit);
            if (!__state || source == null)
                return;
            float speed = source.GetTopSpeed(globalPosition.y, globalPosition.y);
            if (!float.IsFinite(speed) || speed <= 0f)
                throw new InvalidOperationException("AShM-300 test inbound has no finite cruise speed.");
            startingVel = rotation * Vector3.forward * speed;
            inboundCruiseSpeed = speed;
        }

        private static void Postfix(Missile __result, bool __state, string targetName)
        {
            if (!__state)
                return;
            Missile missile = __result;
            AccessTools.Property(typeof(Missile), nameof(Missile.timeSinceSpawn)).SetValue(missile, 10f);
            missile.SetTangible(true);
            missile.Arm();
            var motors = (Array)AccessTools.Field(typeof(Missile), "motors").GetValue(missile);
            foreach (object motor in motors)
                AccessTools.Field(motor.GetType(), "delayTimer").SetValue(motor, 0f);
            var fins = (Array)AccessTools.Field(typeof(Missile), "foldingFins").GetValue(missile);
            foreach (object fin in fins)
            {
                Type type = fin.GetType();
                AccessTools.Field(type, "deployedAmount").SetValue(fin, 1f);
                Transform transform = (Transform)AccessTools.Field(type, "fin").GetValue(fin);
                transform.localEulerAngles = (Vector3)AccessTools.Field(type, "deployAngle").GetValue(fin);
            }
            missile.DeployFins();
            AccessTools.Field(typeof(Missile), "currentFinArea").SetValue(missile, missile.GetFinArea());
            OpticalSeekerCruiseMissile optical = missile.GetComponent<OpticalSeekerCruiseMissile>();
            if (optical != null)
            {
                Unit? target = UnitRegistry.allUnits.Find(unit => unit.UniqueName == targetName);
                if (target != null)
                {
                    GlobalPosition destination = target.GlobalPosition();
                    AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "knownPos").SetValue(optical, destination);
                    AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "aimPos").SetValue(optical, destination);
                }
                AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "finsDeployed").SetValue(optical, true);
                AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "guidance").SetValue(optical, true);
                missile.UpdateRadarAlt();
                optical.PreTerminalMode();
            }
            inboundWarmstarts++;
            log.LogInfo($"Karambit inbound warmstart name={missile.UniqueName} speed_mps={inboundCruiseSpeed:F3} age_s=10 full_fin_area={missile.GetFinArea():F3}");
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Initialize))]
    private static class InitializePatch
    {
        private static bool Prefix(ARHSeeker __instance, Unit target, GlobalPosition aimpoint, Missile ___missile)
        {
            if (___missile.definition == null || ___missile.definition.jsonKey != KarambitPolicy.MissileKey)
                return true;
            if (!___missile.IsServer || !___missile.LocalSim)
                return false;
            if (Seekers.TryGetValue(__instance, out KarambitSeeker previous))
                previous.Unsubscribe();
            Seekers[__instance] = new KarambitSeeker(__instance, ___missile, target, aimpoint);
            launches++;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.GetTopSpeed))]
    private static class TopSpeedEstimatePatch
    {
        private static void Postfix(Missile __instance, float launchAltitude, float targetAltitude, ref float __result)
        {
            if (__instance.definition?.jsonKey == KarambitPolicy.MissileKey)
                __result = Mathf.Min(__result, KarambitPolicy.MotorSpeedLimit(
                    LevelInfo.GetSpeedOfSound(Mathf.Lerp(launchAltitude, targetAltitude, 0.5f))));
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    private static class KarambitMountPitchPatch
    {
        private static void Postfix(Hardpoint __instance, GameObject __result, Aircraft aircraft, WeaponMount weaponMount)
        {
            if (__result == null || weaponMount.missileBay || !weaponMount.jsonKey.StartsWith("baanish_karambit_", StringComparison.Ordinal))
                return;
            __result.transform.rotation = aircraft.transform.rotation;
            foreach (MountedMissile seat in __result.GetComponentsInChildren<MountedMissile>())
                seat.transform.rotation = aircraft.transform.rotation * Quaternion.Euler(KarambitPolicy.LaunchAngleDegrees, 0f, 0f);
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissile), new[]
    {
        typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit)
    })]
    private static class KarambitLaunchPitchPatch
    {
        private static void Prefix(GameObject missile, Unit owner, ref Quaternion rotation)
        {
            // Native rail traversal finishes before spawning the free missile.
            if (owner is Aircraft aircraft && missile.GetComponent<Missile>()?.definition?.jsonKey == KarambitPolicy.MissileKey)
                rotation = aircraft.transform.rotation * Quaternion.Euler(KarambitPolicy.LaunchAngleDegrees, 0f, 0f);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Seek))]
    private static class SeekPatch
    {
        private static bool Prefix(ARHSeeker __instance)
        {
            if (!Seekers.TryGetValue(__instance, out KarambitSeeker seeker))
                return true;
            if (seeker.Missile.IsServer && seeker.Missile.LocalSim && !seeker.Missile.disabled)
                seeker.Seek();
            return false;
        }
    }

    private sealed class KarambitSeeker
    {
        public readonly Missile Missile;
        private readonly ARHSeeker seeker;
        private readonly RadarParams radar;
        private readonly Array motors;
        private readonly FieldInfo motorTopSpeed;
        private readonly Unit? designated;
        private readonly float trackingAngle, activationRange, maxLead, minimumSpeed, turnRate, gLimit, armDelay, guidanceDelay;
        private readonly List<RadarCandidate> candidates = new();
        private Unit? locked;
        private GlobalPosition cue, knownPosition;
        private Vector3 cueVelocity, knownVelocity;
        private float nextCueUpdate, nextRadarCheck, nextSlowCheck, nextTerrainCheck;
        private float terrainAheadHeight;
        private bool terrainBlocked;
        private GuidanceProfile profile = GuidanceProfile.UnknownAltitude;
        private float targetAltitudeAgl;
        private string targetAltitudeSource = "cue_terrain";
        private string designatedRadarStatus = "not_checked", lockedRadarStatus = "not_checked";
        private float cruiseTrim, previousCruiseGoal;
        private readonly float launchPitchDegrees, launchYawDegrees;
        private float lastReturnTime, jam, signal, surfaceHeight, aimAltitude, commandPitchDegrees, nearestApproach = float.MaxValue;
        private bool armed, guidance, terminal, everLocked, wasJammed;

        public KarambitSeeker(ARHSeeker seeker, Missile missile, Unit? target, GlobalPosition aimpoint)
        {
            this.seeker = seeker;
            Missile = missile;
            motors = (Array)AccessTools.Field(typeof(Missile), "motors").GetValue(missile);
            motorTopSpeed = AccessTools.Field(motors.GetType().GetElementType(), "topSpeed");
            UpdateMotorSpeedLimit();
            Vector3 launchDirection = missile.owner != null ? missile.owner.transform.InverseTransformDirection(missile.transform.forward) : missile.transform.forward;
            launchPitchDegrees = Mathf.Atan2(launchDirection.y, new Vector2(launchDirection.x, launchDirection.z).magnitude) * Mathf.Rad2Deg;
            launchYawDegrees = Mathf.Atan2(launchDirection.x, launchDirection.z) * Mathf.Rad2Deg;
            designated = target;
            radar = seeker.GetRadarParams();
            trackingAngle = (float)AccessTools.Field(typeof(ARHSeeker), "maxTrackingAngle").GetValue(seeker);
            activationRange = (float)AccessTools.Field(typeof(ARHSeeker), "terminalRange").GetValue(seeker);
            maxLead = (float)AccessTools.Field(typeof(ARHSeeker), "maxLead").GetValue(seeker);
            minimumSpeed = (float)AccessTools.Field(typeof(ARHSeeker), "selfDestructAtSpeed").GetValue(seeker);
            turnRate = (float)AccessTools.Field(typeof(Missile), "maxTurnRate").GetValue(missile);
            gLimit = (float)AccessTools.Field(typeof(Missile), "gLimit").GetValue(missile);
            armDelay = (float)AccessTools.Field(typeof(ARHSeeker), "armDelay").GetValue(seeker);
            guidanceDelay = (float)AccessTools.Field(typeof(ARHSeeker), "guidanceDelay").GetValue(seeker);
            cue = aimpoint;
            UpdateCue(seed: true);
            knownPosition = cue;
            Missile.NetworkseekerMode = Missile.SeekerMode.passive;
            Missile.onJam += OnJam;
            Missile.onDisableUnit += OnDisabled;
            log.LogInfo($"Karambit launch id={Missile.persistentID} designated={target?.persistentID.ToString() ?? "none"} cue={cue}");
        }

        public Dictionary<string, object> Telemetry() => new()
        {
            ["target_id"] = locked?.persistentID.Id ?? 0u,
            ["designated_id"] = designated?.persistentID.Id ?? 0u,
            ["target_type"] = (locked ?? designated)?.definition.jsonKey ?? "none",
            ["target_agl_m"] = targetAltitudeAgl,
            ["target_agl_source"] = targetAltitudeSource,
            ["target_vertical_speed_mps"] = knownVelocity.y,
            ["guidance_profile"] = profile == GuidanceProfile.Ground ? "ground" : "air",
            ["profile_reason"] = profile.ToString(),
            ["designated_radar_status"] = designatedRadarStatus,
            ["locked_radar_status"] = lockedRadarStatus,
            ["seeker_mode"] = Missile.seekerMode.ToString(),
            ["last_return_age_s"] = everLocked ? Time.timeSinceLevelLoad - lastReturnTime : -1f,
            ["designated_eligibility"] = TargetEligibility(designated),
            ["designated_cue_distance_m"] = designated == null ? -1f : (designated.GlobalPosition() - cue).magnitude,
            ["designated_in_seeker"] = designated != null && SeekerCovers(designated.GlobalPosition() - Missile.GlobalPosition()),
            ["cue_alt_m"] = cue.y,
            ["known_alt_m"] = knownPosition.y,
            ["launch_pitch_deg"] = launchPitchDegrees,
            ["launch_yaw_deg"] = launchYawDegrees,
            ["alt_m"] = Missile.GlobalPosition().y,
            ["clearance_m"] = Missile.GlobalPosition().y - surfaceHeight,
            ["terrain_ahead_m"] = terrainAheadHeight,
            ["terrain_blocked"] = terrainBlocked,
            ["cruise_alt_m"] = KarambitPolicy.CruiseAltitude(terrainAheadHeight, knownPosition.y),
            ["aim_alt_m"] = aimAltitude,
            ["speed_mps"] = Missile.speed,
            ["mach"] = Missile.speed / LevelInfo.GetSpeedOfSound(Missile.GlobalPosition().y),
            ["air_density"] = LevelInfo.GetAirDensity(Missile.GlobalPosition().y),
            // EngineOn reports a burning motor even while its speed cutoff suppresses acceleration.
            ["engine_on"] = Missile.EngineOn(),
            ["motor_speed_limit_mps"] = KarambitPolicy.MotorSpeedLimit(LevelInfo.GetSpeedOfSound(Missile.GlobalPosition().y)),
            ["time_since_spawn_s"] = Missile.timeSinceSpawn,
            ["vertical_speed_mps"] = Missile.rb.velocity.y,
            ["body_pitch_deg"] = Mathf.Asin(Mathf.Clamp(Missile.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg,
            ["command_pitch_deg"] = commandPitchDegrees,
            ["phase"] = terminal ? "terminal" : "cruise",
            ["return_strength"] = signal,
            ["jam_accumulation"] = jam,
            ["range_m"] = (knownPosition - Missile.GlobalPosition()).magnitude
        };

        public void Unsubscribe()
        {
            Missile.onJam -= OnJam;
            Missile.onDisableUnit -= OnDisabled;
            if (locked != null)
                locked.onAddRadarChaff -= OnChaff;
        }

        private void OnDisabled(Unit unit)
        {
            log.LogInfo($"Karambit ended id={Missile.persistentID} target={locked?.persistentID.ToString() ?? "none"} closest_m={(nearestApproach == float.MaxValue ? -1f : nearestApproach):F1} alt_m={Missile.GlobalPosition().y:F1}");
            Unsubscribe();
            Seekers.Remove(seeker);
        }

        private string TargetEligibility(Unit? target)
        {
            if (target == null)
                return "no_target";
            if (target.disabled)
                return "disabled";
            if (target.NetworkHQ == null || Missile.NetworkHQ == null)
                return "no_hq";
            if (target.NetworkHQ == Missile.NetworkHQ)
                return "friendly";
            if (target.rb == null || target.rb.isKinematic)
                return "no_dynamic_body";
            if (target is Aircraft aircraft)
                return aircraft.IsLanded() ? "landed" : "eligible";
            if (target is not Missile missile)
                return "not_aircraft_or_missile";
            if (!missile.IsTangible())
                return "not_tangible";
            return target.GlobalPosition().y > 0f ? "eligible" : "below_sea_level";
        }

        private bool EnemyAirborne(Unit? target) => TargetEligibility(target) == "eligible";

        private void UpdateCue(bool seed = false)
        {
            if (designated != null && !designated.disabled && Missile.NetworkHQ != null)
            {
                bool tracked = Missile.NetworkHQ.IsTargetBeingTracked(designated);
                if ((seed || tracked) && Missile.NetworkHQ.TryGetKnownPosition(designated, out GlobalPosition position))
                {
                    cue = position;
                    if (tracked)
                        cueVelocity = designated.rb != null ? designated.rb.velocity : Vector3.zero;
                }
            }
            nextCueUpdate = Time.timeSinceLevelLoad + 1f;
        }

        private void OnJam(Unit.JamEventArgs args)
        {
            if (args.jammingUnit != null && SeekerCovers(args.jammingUnit.GlobalPosition() - Missile.GlobalPosition()))
                jam += args.jamAmount;
        }

        private void OnChaff(RadarChaff chaff)
        {
            if (locked == null || Missile.seekerMode != Missile.SeekerMode.activeLock)
                return;
            float range = (locked.GlobalPosition() - Missile.GlobalPosition()).magnitude;
            Vector3 line = (locked.transform.position - Missile.transform.position).normalized;
            Vector3 chaffLine = (locked.transform.position - chaff.transform.position).normalized;
            jam += Mathf.Clamp01(1f - range / radar.maxRange) * Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(line, chaffLine))) / (1f + KarambitPolicy.JamTolerance);
        }

        private float RadarReturn(Unit target)
        {
            float measured = RadarReturn(target, out string status);
            if (target == designated)
                designatedRadarStatus = status;
            if (target == locked)
                lockedRadarStatus = status;
            return measured;
        }

        private float RadarReturn(Unit target, out string status)
        {
            status = TargetEligibility(target);
            if (status != "eligible")
                return 0f;
            status = "no_radar_return";
            if (target is not IRadarReturn radarReturn)
                return 0f;
            status = "jammed";
            if (jam > KarambitPolicy.JamTolerance)
                return 0f;
            GlobalPosition source = Missile.GlobalPosition();
            GlobalPosition destination = target.GlobalPosition();
            Vector3 relative = destination - source;
            float range = relative.magnitude;
            float horizon = Mathf.Sqrt(12742000f * Mathf.Max(source.y, 0f));
            float targetHorizon = Mathf.Sqrt(12742000f * Mathf.Max(destination.y, 0f));
            status = "outside_radar_range";
            if (range > radar.maxRange)
                return 0f;
            status = "below_radar_horizon";
            if (range > horizon + targetHorizon)
                return 0f;
            status = "outside_seeker";
            if (!SeekerCovers(relative))
                return 0f;
            status = "terrain_occluded";
            if (!TargetCalc.LineOfSight(Missile.transform, target.transform, 10f))
                return 0f;
            target.CheckRadarAlt();
            float altitude = Mathf.Max(target.radarAlt, 0.1f);
            relative.y = 0f;
            float horizontalRange = relative.magnitude;
            float clutter = target.maxRadius * target.maxRadius * 2f / (altitude * altitude);
            if (horizontalRange < horizon && destination.y < source.y * (1f - horizontalRange / horizon))
                clutter += Mathf.Min(range, 1000f) * (source.y - destination.y) / Mathf.Max(range * altitude, 0.1f);
            float measured = radarReturn.GetRadarReturn(Missile.transform.position, null, Missile, Mathf.Max(range, 0.1f), clutter, radar, triggerWarning: true);
            status = float.IsFinite(measured) && measured > radar.minSignal ? "detectable" : "weak_return";
            return measured;
        }

        private bool SeekerCovers(Vector3 direction)
        {
            Vector3 local = Missile.transform.InverseTransformDirection(direction);
            float azimuth = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            return direction.sqrMagnitude > 0f && KarambitPolicy.InSeekerEnvelope(azimuth, elevation, trackingAngle);
        }

        private void CheckRadar(float now)
        {
            nextRadarCheck = now + 0.5f;
            designatedRadarStatus = TargetEligibility(designated);
            if (designatedRadarStatus == "eligible")
                designatedRadarStatus = (designated!.GlobalPosition() - cue).sqrMagnitude > KarambitPolicy.CueRadius * KarambitPolicy.CueRadius
                    ? "outside_cue" : "not_scanned";
            if (locked != null && designated != null && locked != designated && EnemyAirborne(designated) &&
                (designated.GlobalPosition() - cue).sqrMagnitude <= KarambitPolicy.CueRadius * KarambitPolicy.CueRadius)
            {
                float designatedSignal = RadarReturn(designated);
                // Sticky fallback locks yield to the original cue when its target becomes detectable again.
                if (designatedSignal > radar.minSignal)
                {
                    LockTarget(designated, designatedSignal, now);
                    return;
                }
            }
            if (locked != null)
            {
                signal = RadarReturn(locked);
                if (signal > radar.minSignal)
                {
                    lastReturnTime = now;
                    knownPosition = locked.GlobalPosition();
                    knownVelocity = locked.rb.velocity;
                    Missile.NetworkseekerMode = Missile.SeekerMode.activeLock;
                    return;
                }
                Missile.NetworkseekerMode = Missile.SeekerMode.activeSearch;
                if (KarambitPolicy.KeepLock(EnemyAirborne(locked), now - lastReturnTime))
                    return;
                log.LogInfo($"Karambit lost_lock id={Missile.persistentID} target={locked.persistentID} jam={jam:F2}");
                locked.onAddRadarChaff -= OnChaff;
                locked = null;
                lockedRadarStatus = "no_lock";
                terminal = false;
                Missile.SetTarget(null);
                Missile.SetProxyFuse(null, null);
            }
            if ((cue - Missile.GlobalPosition()).magnitude > activationRange)
            {
                if (designatedRadarStatus == "not_scanned")
                    designatedRadarStatus = "outside_activation_range";
                return;
            }
            Missile.NetworkseekerMode = Missile.SeekerMode.activeSearch;
            candidates.Clear();
            foreach (Unit target in UnitRegistry.allUnits)
            {
                if (!EnemyAirborne(target))
                    continue;
                float cueDistanceSquared = (target.GlobalPosition() - cue).sqrMagnitude;
                if (cueDistanceSquared <= KarambitPolicy.CueRadius * KarambitPolicy.CueRadius)
                    candidates.Add(new RadarCandidate(target.persistentID.Id, true, true, cueDistanceSquared, RadarReturn(target)));
            }
            uint selected = KarambitPolicy.PickCandidate(candidates, radar.minSignal, designated?.persistentID.Id ?? 0u);
            if (selected == 0 || !UnitRegistry.TryGetUnit(new PersistentID { Id = selected }, out Unit acquired))
                return;
            LockTarget(acquired, candidates.Find(candidate => candidate.TargetId == selected).Signal, now);
        }

        private void LockTarget(Unit acquired, float measuredSignal, float now)
        {
            if (locked != null)
            {
                locked.onAddRadarChaff -= OnChaff;
                if (locked != acquired)
                    terminal = false;
            }
            locked = acquired;
            lastReturnTime = now;
            knownPosition = acquired.GlobalPosition();
            knownVelocity = acquired.rb.velocity;
            signal = measuredSignal;
            lockedRadarStatus = "detectable";
            acquired.onAddRadarChaff += OnChaff;
            Missile.SetTarget(acquired);
            if (seeker.proximityFuse)
                Missile.SetProxyFuse(acquired.GetRandomPart().transform, acquired.rb);
            Missile.NetworkseekerMode = Missile.SeekerMode.activeLock;
            locks++;
            if (everLocked)
                reacquires++;
            everLocked = true;
            log.LogInfo($"Karambit lock id={Missile.persistentID} target={acquired.persistentID} source={(acquired == designated ? "designated" : "greedy")} type={acquired.definition.jsonKey} signal={signal:F3} range_m={(knownPosition - Missile.GlobalPosition()).magnitude:F1}");
        }

        public void Seek()
        {
            UpdateMotorSpeedLimit();
            float now = Time.timeSinceLevelLoad;
            float dt = Time.fixedDeltaTime;
            cue += cueVelocity * dt;
            knownPosition += knownVelocity * dt;
            if (now >= nextCueUpdate)
                UpdateCue();
            jam = KarambitPolicy.DecayJam(jam, dt);
            bool isJammed = jam > KarambitPolicy.JamTolerance;
            if (wasJammed != isJammed)
            {
                log.LogInfo($"Karambit jam id={Missile.persistentID} blocked={isJammed} accumulation={jam:F2}");
                wasJammed = isJammed;
            }
            if (!armed && Missile.timeSinceSpawn > armDelay)
            {
                armed = true;
                Missile.Arm();
                Missile.SetTangible(true);
            }
            if (!guidance && Missile.timeSinceSpawn > guidanceDelay)
            {
                guidance = true;
                Missile.DeployFins();
            }
            if (!guidance)
            {
                Missile.SetAimpoint(Missile.GlobalPosition() + Missile.transform.forward * 10000f, Vector3.zero);
                return;
            }
            if (now >= nextRadarCheck)
                CheckRadar(now);
            if (locked == null)
            {
                knownPosition = cue;
                knownVelocity = cueVelocity;
            }
            float range = (knownPosition - Missile.GlobalPosition()).magnitude;
            if (locked != null)
                nearestApproach = Mathf.Min(nearestApproach, (locked.GlobalPosition() - Missile.GlobalPosition()).magnitude);
            bool nextTerminal = locked != null && (terminal || range <= KarambitPolicy.TerminalRange);
            if (nextTerminal != terminal)
            {
                terminal = nextTerminal;
                log.LogInfo($"Karambit profile id={Missile.persistentID} phase={(terminal ? "terminal" : "cruise")} alt_m={Missile.GlobalPosition().y:F1} range_m={range:F1}");
            }
            GlobalPosition aimpoint = knownPosition + TargetCalc.GetLeadVectorWithAccel(knownPosition, Missile.GlobalPosition(), knownVelocity, Missile.rb.velocity, Vector3.zero, maxLead);
            Vector3 direction = aimpoint - Missile.GlobalPosition();
            direction.y = 0f;
            if (now >= nextTerrainCheck)
            {
                nextTerrainCheck = now + 0.1f;
                Unit? guidanceTarget = locked != null ? locked : designated != null ? designated : null;
                targetAltitudeAgl = knownPosition.y - SurfaceHeight(knownPosition);
                targetAltitudeSource = "cue_terrain";
                if (locked != null && now - lastReturnTime <= 0.5f)
                {
                    locked.CheckRadarAlt();
                    targetAltitudeAgl = Mathf.Max(locked.radarAlt + knownPosition.y - locked.GlobalPosition().y, 0f);
                    targetAltitudeSource = "radar";
                }
                GuidanceProfile nextProfile = KarambitPolicy.SelectProfile(guidanceTarget is Aircraft,
                    guidanceTarget is Missile && guidanceTarget.definition.roleIdentity.antiAir > 0f,
                    guidanceTarget is Missile && guidanceTarget.GetComponent<BallisticMissileGuidance>() != null,
                    targetAltitudeAgl, knownVelocity.y, knownVelocity.magnitude);
                if (nextProfile != profile)
                {
                    profile = nextProfile;
                    cruiseTrim = 0f;
                    log.LogInfo($"Karambit guidance_profile id={Missile.persistentID} target={guidanceTarget?.persistentID.ToString() ?? "none"} profile={(profile == GuidanceProfile.Ground ? "ground" : "air")} reason={profile} target_agl_m={targetAltitudeAgl:F1} target_vertical_speed_mps={knownVelocity.y:F1}");
                }
                surfaceHeight = SurfaceHeight(Missile.GlobalPosition());
                Vector3 heading = Vector3.RotateTowards(new Vector3(Missile.rb.velocity.x, 0f, Missile.rb.velocity.z).normalized,
                    direction.normalized, 10f * Mathf.Deg2Rad, 0f);
                GlobalPosition terrainAhead = Missile.GlobalPosition() + heading * KarambitPolicy.TerrainLookAhead(Missile.speed, direction.magnitude);
                terrainAheadHeight = surfaceHeight;
                int samples = Mathf.Max(1, Mathf.CeilToInt((terrainAhead - Missile.GlobalPosition()).magnitude / 500f));
                for (int sample = 1; sample <= samples; sample++)
                    terrainAheadHeight = Mathf.Max(terrainAheadHeight, SurfaceHeight(Missile.GlobalPosition() +
                        (terrainAhead - Missile.GlobalPosition()) * ((float)sample / samples)));
                Vector3 from = Missile.transform.position - Vector3.up * 2f;
                Vector3 to = terrainAhead.ToLocalPosition();
                to.y = Datum.LocalSeaY + (profile == GuidanceProfile.Ground && !terminal
                    ? KarambitPolicy.CruiseAltitude(terrainAheadHeight, knownPosition.y)
                    : Mathf.Max(aimpoint.y, terrainAheadHeight + KarambitPolicy.CruiseClearance));
                terrainBlocked = Physics.Linecast(from, to, out RaycastHit obstacle, TerrainMask);
                if (terrainBlocked)
                    terrainAheadHeight = Mathf.Max(terrainAheadHeight, ObstacleHeight(obstacle, heading));
                // Probe the bounded descent too: a level ray and spaced height samples can miss narrow obstacles.
                float speed = Mathf.Max(Missile.speed, 100f);
                float pitch = Mathf.Asin(Mathf.Clamp(Missile.rb.velocity.y / speed, -1f, 1f));
                float pitchRate = Mathf.Min(turnRate * Mathf.Deg2Rad, 9.81f * gLimit / speed);
                Vector3 predicted = Missile.transform.position;
                float remaining = KarambitPolicy.TerrainLookAhead(speed, direction.magnitude);
                for (int segment = 0; segment < 24 && remaining > 0f; segment++)
                {
                    float step = Mathf.Min(0.25f, remaining / speed);
                    Vector3 remainingAim = aimpoint.ToLocalPosition() - predicted;
                    float desiredPitch = terminal || profile != GuidanceProfile.Ground
                        ? Mathf.Atan2(remainingAim.y, new Vector2(remainingAim.x, remainingAim.z).magnitude)
                        : KarambitPolicy.CruisePitch(predicted.y - Datum.LocalSeaY, terrainAheadHeight,
                            speed, Mathf.Sin(pitch) * speed, turnRate, gLimit, knownPosition.y);
                    float nextPitch = Mathf.MoveTowards(pitch, desiredPitch, pitchRate * step);
                    float midpointPitch = (pitch + nextPitch) * 0.5f;
                    Vector3 next = predicted + (heading * Mathf.Cos(midpointPitch) + Vector3.up * Mathf.Sin(midpointPitch)) * (speed * step);
                    pitch = nextPitch;
                    if (Physics.Linecast(predicted - Vector3.up * 2f, next - Vector3.up * 2f, out obstacle, TerrainMask))
                    {
                        terrainBlocked = true;
                        terrainAheadHeight = Mathf.Max(terrainAheadHeight, ObstacleHeight(obstacle, heading));
                        break;
                    }
                    predicted = next;
                    remaining -= speed * step;
                }
            }
            if (!terminal && profile == GuidanceProfile.Ground)
            {
                float lookAhead = Mathf.Min(direction.magnitude, Mathf.Max(Missile.speed * 0.5f, 200f));
                aimpoint = Missile.GlobalPosition() + direction.normalized * lookAhead;
                float pitch = KarambitPolicy.CruisePitch(Missile.GlobalPosition().y, terrainAheadHeight, Missile.speed, Missile.rb.velocity.y, turnRate, gLimit, knownPosition.y);
                float cruiseGoal = KarambitPolicy.CruiseAltitude(terrainAheadHeight, knownPosition.y);
                if (Mathf.Abs(cruiseGoal - previousCruiseGoal) > 0.1f)
                    cruiseTrim = 0f;
                previousCruiseGoal = cruiseGoal;
                cruiseTrim = KarambitPolicy.CruiseTrim(cruiseTrim, cruiseGoal - Missile.GlobalPosition().y, Missile.rb.velocity.y, dt);
                float leadTime = lookAhead / Mathf.Max(Missile.speed, 1f);
                aimpoint.y = Missile.GlobalPosition().y + Mathf.Tan(pitch) * lookAhead + 4.905f * leadTime * leadTime + cruiseTrim;
            }
            else if (terrainBlocked || Missile.GlobalPosition().y < terrainAheadHeight + KarambitPolicy.CruiseClearance)
            {
                float pitch = KarambitPolicy.CruisePitch(Missile.GlobalPosition().y, terrainAheadHeight, Missile.speed, Missile.rb.velocity.y, turnRate, gLimit);
                float lookAhead = Mathf.Min(direction.magnitude, Mathf.Max(Missile.speed * 0.5f, 200f));
                float leadTime = lookAhead / Mathf.Max(Missile.speed, 1f);
                float safeAltitude = Missile.GlobalPosition().y + Mathf.Tan(pitch) * lookAhead + 4.905f * leadTime * leadTime;
                if (safeAltitude > aimpoint.y)
                {
                    aimpoint = Missile.GlobalPosition() + direction.normalized * lookAhead;
                    aimpoint.y = safeAltitude;
                }
            }
            aimAltitude = aimpoint.y;
            Vector3 aimDirection = aimpoint - Missile.GlobalPosition();
            commandPitchDegrees = Mathf.Atan2(aimDirection.y, new Vector2(aimDirection.x, aimDirection.z).magnitude) * Mathf.Rad2Deg;
            Missile.SetAimpoint(aimpoint, knownVelocity);
            if (now >= nextSlowCheck)
            {
                nextSlowCheck = now + 1f;
                if (Missile.timeSinceSpawn > 120f ||
                    (Missile.timeSinceSpawn > 10f || !Missile.EngineOn() && Missile.timeSinceSpawn > 2f) &&
                    (Missile.speed < minimumSpeed || Missile.LosingGround() || Missile.MissedTarget()))
                {
                    log.LogInfo($"Karambit self_destruct id={Missile.persistentID} age_s={Missile.timeSinceSpawn:F1} speed_mps={Missile.speed:F1}");
                    Missile.Detonate(Missile.rb.velocity, hitArmor: false, hitTerrain: false);
                }
            }
        }

        private void UpdateMotorSpeedLimit()
        {
            // Native MotorThrust precedes Seek. Initialization covers its first tick; Seek sets the next tick's cutoff.
            float limit = KarambitPolicy.MotorSpeedLimit(LevelInfo.GetSpeedOfSound(Missile.GlobalPosition().y));
            foreach (object motor in motors)
                motorTopSpeed.SetValue(motor, limit);
        }

        private static readonly int TerrainMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ExclusionZonesMask;

        private static float ObstacleHeight(RaycastHit obstacle, Vector3 heading) => obstacle.collider is TerrainCollider
            ? SurfaceHeight((obstacle.point + heading).ToGlobalPosition())
            : Mathf.Max(obstacle.collider.bounds.max.y - Datum.LocalSeaY, 0f);

        private static float SurfaceHeight(GlobalPosition position)
        {
            Vector3 local = position.ToLocalPosition();
            local.y = Datum.LocalSeaY;
            return Physics.Linecast(local + Vector3.up * 5000f, local - Vector3.up * 5000f, out RaycastHit hit,
                TerrainMask) ? Mathf.Max(hit.point.y - Datum.LocalSeaY, 0f) : 0f;
        }
    }
}
