# How the Karambit finds a target

The stock Scythe follows one designated target. Karambit prefers that designation, but can search nearby enemy aircraft and missiles without a usable designated return. Each shot chooses independently. Twenty selected tracks give twenty launch designations, not twenty guaranteed interceptions.

The designation is the track you selected. Its cue is the last reported position and motion. A radar return is what the missile itself detects; a lock means it has accepted that return for tracking.

Stock facts were checked against installed Nuclear Option **0.34.2** `Assembly-CSharp` local decompilation and extracted `AAM2_PLACEHOLDER.prefab` and `AAM2_info_PLACEHOLDER.asset`. Class and method names identify that evidence. These rules are not measured hit probabilities or endurance.

| Rule | Stock AAM-29 Scythe | SAAM-18 Karambit |
|---|---|---|
| Initial search | Measures its designated target only | Prefers the designation, otherwise picks a nearby return |
| Radar activation | Within 12,000 m of remembered target position | New acquisition within 12,000 m of the cue |
| Radar range and threshold | 15,000 m maximum, signal above 0.5 to acquire | Same inherited radar parameters |
| Nose coverage | Circular cone, 60° from forward | Azimuth ±60°, elevation −10° to +60° |
| Lost return | 3 s perseverance, immediate target-ID clearing for blocked line of sight or horizon | Holds an eligible target through 4 s without a return |
| Jamming | Tolerance 0.85, home-on-jam disabled | Tolerance 0.60, no home-on-jam |
| Flight profile | Lead pursuit with loft | Low surface missiles use terrain cruise; aircraft, SAMs and ballistic or lofting missiles use air intercept lead |

[Asset authoring](../unity/Assets/BaanishArmory/Editor/KarambitAssetBuild.cs), `Author`, copies the Scythe prefab. [Runtime](../src/Karambit/KarambitPlugin.cs), `InitializePatch` and `SeekPatch`, replaces its guidance only for `baanish_karambit`. Prefab values override stock field initializers.

## A designation tells the missile where to look

A HUD contact can come from aircraft sensors, friendly sensors or the test mission's initial reveal. Faction tracking is separate from seeker detection. Aircraft `Radar.RadarCheck`, `DetectorManager.RequestRadarCheck` and `DetectionRequest.ProcessResult` apply their own scanner geometry, horizon, terrain and signal checks.

Stock `ARHSeeker.Initialize` seeds faction knowledge with a random positional offset up to 200 m. `DatalinkMode` checks each second, updating velocity while tracked and position when knowledge passes its 2,000 m accuracy check. An 80° nose-relative datalink limit can replace the cue with a point ahead. A cue over 2,000 m from the actual target clears the designation.

Karambit `UpdateCue` seeds available faction knowledge, refreshes each second while tracked and extrapolates the last supplied velocity. It adds no random offset or datalink-angle rejection. A stale cue can drift away.

## A return must pass geometry and signal checks

Both seekers reject targets beyond radar range, beyond the combined altitude-based horizon or behind static geometry. `TargetCalc.LineOfSight` tolerates a static hit within 10 m of the target. A HUD contact cannot bypass terrain.

Both add clutter for low radar altitude and downward viewing. `RadarParams.GetSignalStrength` combines range and radar cross-section, subtracts clutter and boosts sufficient returns using target world velocity along the sightline. This is not missile-relative closing speed. `Aircraft.GetRadarReturn` subtracts ECM. `Missile.GetRadarReturn` supplies zero ECM.

Karambit `EnemyAirborne` requires active physics and excludes friendly, disabled and ground units, landed aircraft, and intangible or nonpositive-altitude missiles. `RadarReturn` retains stock signal calculations. Clutter still affects airborne returns.

Stock `GetRadarReturn` checks a circular nose cone. Karambit `SeekerCovers` checks azimuth and elevation separately in missile-local coordinates. Its rectangular envelope includes corners outside the stock cone. Coverage rotates with the missile's nose and roll, not the world horizon. [Policy](../src/Karambit/KarambitPolicy.cs), `InSeekerEnvelope`, pins the limits.

## Acquisition is designation first, then greedy fallback

Stock `GetRadarReturn` measures only `targetUnit`. After a weak return, it blocks reacquisition inside 250 m. It also blocks a zero-return attempt with target ECM above 2 and cached target distance below 5 km. `TerminalMode` extrapolates or uses faction knowledge during loss, clearing the target ID after over 3 s or immediately for horizon or terrain failure. Subsequent `Seek` calls follow the remembered trajectory without acquisition.

Karambit `CheckRadar` runs every 0.5 s. Without a lock, it searches enemy airborne units within **1,500 m of the cue**, provided the missile is within 12 km of that cue. [Policy](../src/Karambit/KarambitPolicy.cs), `PickCandidate`, first chooses a detectable designation. Otherwise it chooses the highest finite signal strictly above 0.5. Equal signals choose the lower persistent target ID. Distance within the cue does not rank candidates.

A good lock stays sticky outside the cue sphere. Missing returns preserve last position and velocity through 4 s while the target remains eligible. After expiry, acquisition resumes. Fallback locks yield to detectable original designations inside the cue sphere. Existing-lock checks and this switch bypass the 12 km activation gate. Karambit omits the 250 m reacquisition restriction.

Preference fails for ineligible, displaced, out-of-envelope, occluded, distant or weak designations. [Policy tests](../tests/Karambit/Program.cs) characterize selection boundaries and fallback.

## Twenty tracks can still produce overlapping shots

For twenty incoming cruise missiles, the [test selector](../tests/KarambitSalvoControl/KarambitSalvoControlPlugin.cs), `SelectNativeSalvo`, checks twenty unique live HUD tracks and retains the native target lists. Stock `WeaponManager.SalvoFire` launches against successive selected targets.

With detectable designations, shots keep their targets despite stronger neighboring returns. Otherwise, overlapping cue spheres can produce the same strongest fallback. No shared reservations or incoming-shot count prevents duplicates. Recovered designations can pull shots back. This predicts possible duplicates, not their frequency.

## Target type and radar altitude choose the flight profile

Aircraft, anti-air missiles and missiles with `BallisticMissileGuidance` always use the air profile, including below 1,000 ft. The air profile retains the stock `TargetCalc.GetLeadVectorWithAccel` intercept aimpoint and the target's vertical velocity. SAM identity comes from the game's authored anti-air role, rather than treating a missile object as an aircraft object.

Other missiles use air guidance while climbing at least **5°** and **5 m/s**, or when at or above **1,000 ft radar altitude**, exactly **304.8 m above the surface**. Below that boundary, a level or descending surface missile selects the ground profile. Unknown altitude keeps air lead. [Policy tests](../tests/Karambit/Program.cs) pin the boundary and type overrides.

The ground profile aims for the higher of terrain plus **3.048 m** and target altitude minus **20 m**. It samples six seconds ahead, capped at target distance, and probes predicted turns for obstacles. A lock within **1,250 m** starts terminal pursuit. Both profiles retain terrain avoidance. Commanded clearance is not guaranteed clearance.

The Scythe adds loft with `loftAmount = 0.7`. Karambit removes loft and stock jink guidance, retaining missile physics. It self-destructs after 120 s or qualifying low-speed, losing-ground or missed-target checks. That timer is not measured endurance.

Runtime telemetry reports `guidance_profile`, `profile_reason` and `target_agl_m`. It also distinguishes a weak radar return from a designation outside the cue, seeker field or activation range, terrain occlusion, horizon failure and jamming. SAMs still need a detectable return to lock.

Stock `ARHSeeker_OnJam` can retarget a jammer only when home-on-jam is enabled, which the Scythe disables. Karambit `OnJam` accumulates jamming inside its own envelope, and `OnChaff` can add interference during a lock. Above 0.60, radar returns become zero until interference decays. It never redirects toward the jammer.

Asset authoring's **22,224 m / 12 nautical mile** nominal launch envelope is separate from the 15 km radar limit, 12 km acquisition activation, 1.25 km terminal transition and flight performance.
