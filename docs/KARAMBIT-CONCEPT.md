# Karambit concept

Status: design reference. A [SAAM-18 dev-art prototype](KARAMBIT-PROTOTYPE.md) now implements the seeker and carriage, with a nominal 12 nm launch envelope. The numbers below record the initial design from reading the game code, not tested balance.

## The idea

The Karambit is a small active-radar missile for killing cruise missiles, modeled on Raytheon's Peregrine: half an AMRAAM's length, under half its mass. Its point is volume. A Revoker or Vortex carries 20 and an Ifrit 28, at $500,000 a round.

It flies low and looks up, subject to its terrain-clearance floor. Its seeker prefers the enemy airborne track you designated, then falls back to the biggest detectable return nearby. It holds its lock through short tracking interruptions.

The external rack slopes five degrees nose-down, with tandem rounds following the same inclined rail. The rear round sits higher and the front round lower, following the layout in the [rack sketch](../art/references/karambit-angled-rack-sketch.png). One shallow beam attaches below the aircraft's native pylon. Every round launches five degrees nose-down relative to the aircraft; internal bays retain compact seat spacing.

It's meant for holding airspace you already own, over a base or against cheap opponents. It turns badly and jams easily, and its external racks wreck a stealth jet's radar cross-section, so it's a poor weapon against real fighters.

## Why stock missiles don't already cover this

Stock missiles can kill a cruise missile once something tracks it. The MMR-S3 locks every cruise missile in the game, because their motors are IR sources that never switch off (`Missile.cs`, `IRSeeker.cs`) and they drop no flares. Its blast kills a cruise missile from 13 to 19 m away.

The gap is detection. Radar clutter makes sea-skimmers nearly invisible from above:

- A radar looking down at a target adds clutter of `min(S, 1000) * dH / (S * h)`, where `S` is slant range, `dH` the radar's height above the target and `h` the target's radar altitude (`DetectorManager.RequestRadarCheck`, `ARHSeeker.cs:192-243`).
- With the signal formula in `RadarParams.cs`, a radar only sees the target while `dH < maxRange * RCS^0.25 * h / (1000 * clutterFactor)`, at any range.
- Cruise missiles fly at 6 to 12 m. A fighter radar sees them only from within about 360 to 1,300 m above, depending on the jet and the missile. A fighter at 1 to 6 km is effectively blind.
- Ground radars see them only when sited near sea level. A radar on a hill is blind.
- The Scythe lofts (`loftAmount` 0.7), which climbs its seeker into the clutter. That matches the wiki's note that it misses targets below the radar floor.

The Karambit's niche is carrying many cheap rounds whose seekers do the finding themselves. The test is cost per cruise-missile kill against the MMR-S3, measured in game.

## The numbers

| | AAM-29 Scythe (stock) | MMR-S3 (stock) | Karambit |
|---|---|---|---|
| Seeker | active radar | IR | active radar, custom |
| Length, diameter | 3.63 m, 180 mm | 2.87 m, 126 mm | 1.8 m, about 170 mm |
| Mass | 160 kg | 100 kg | 70 kg |
| Motor | 30 kN for 2 s, then 14 kN for 6 s | 28 kN for 2.2 s | 13 kN for 2 s, then 6.2 kN for 6 s |
| Fin area | 0.5 | 0.3 | 0.15 |
| `maxTurnRate`, `gLimit` | 20, 30 | 180, 80 | 15, 25 |
| Blast yield | 19 | 11 | 8 |
| `jamTolerance` | 0.85 | n/a | below 0.85, no home-on-jam |
| `loftAmount` | 0.7 | n/a | 0, replaced by the flight profile |
| Range | 35 km (AI) | 15 km (AI) | about 25 km, not yet simulated |
| Cost per round | $890,000 | $360,000 | $500,000 |

The motor keeps the Scythe's acceleration and ideal delta-v at 44% of its mass. The smaller fin area per kilogram cuts coast drag by about a third, in line with Peregrine's claim of AMRAAM-like range from half the size. Flying at deck height costs range, so the motor needs tuning once the profile exists.

The game's air-to-air missiles are true to scale. The Scythe matches the AIM-120 and the MMR-S3 the AIM-9L, within 18% on mass. The Karambit follows Peregrine: about 1.8 m and 68 kg. Peregrine's diameter isn't published. Keeping volume consistent with its length and mass ratios gives about 0.95 of AMRAAM's.

## It turns badly on purpose

Its targets don't dodge much, so it doesn't need to turn well. The turn caps limit body rate to `min(maxTurnRate, 9.81 * gLimit / speed)` (`Missile.cs:1273-1281`). Fin lift only becomes the limit at high altitude.

| Achieved turn | 700 m/s | 450 m/s |
|---|---|---|
| Scythe | 25 g | 16 g |
| Karambit | 18.7 g | 12 g |

The AShM-300 weaves 1.25 to 5 km from its target ship. Every 2 s it shifts its aim by up to 5% of range, horizontally (`JinkEvasion.cs`). That demands bursts of about 4.5 to 9 g, a 2 to 4 times margin for the Karambit.

The AGM-99 is harder. It weaves 20% of range every second, pops up for a top attack and boosts at the end. The Karambit may not catch it.

## Flight profile

One rule covers every target. The missile cruises 20 m below its target's known altitude, with a floor of 10 ft, 3.048 m, above the ground or sea. That puts it below the stock 6-12 m sea-skimmers. Like the ALM-C450, it probes terrain six seconds ahead and checks the path for obstacles; terrain clearance takes priority when land rises ahead. Inside 1.25 km it pursues the target directly, with an obstruction safety override.

## Tracking

At seeker activation, the Karambit prefers the designated track when it is a detectable enemy airborne return within 1.5 km of the cue. Otherwise it takes the biggest qualifying radar return in that area. It only considers enemy aircraft and missiles; ships, ground units and friendlies cannot become targets. A fallback lock gives way when the designated track becomes detectable again. Brief tracking interruptions retain the current lock for four seconds.

The [radar field](../art/references/karambit-upward-radar-sketch.png) follows the missile's nose, with elevation from ten degrees down to sixty degrees up and sixty degrees to either side. This deliberately gives the seeker more upward coverage than downward coverage. Terrain still blocks line of sight, and the same field limits jammer reception.

Biggest-return-first drifts it toward cheap targets and away from stealth jets:

| Target | RCS |
|---|---|
| FS-20 Vortex | 0.001 |
| Cruise missiles | 0.005 to 0.008 |
| FS-12 Revoker | 0.08 |
| T/A-30 Compass, Vagrant | 0.12 |
| CI-22 Cricket | 0.5 |
| A-19 Brawler | 0.6 |

Against a cruise missile's 0.005 RCS, a seeker with the Scythe's 15 km `maxRange` locks within about 8 km. Beyond that the Karambit flies on datalink, like the Scythe.

## Against aircraft

The game has no chaff. Aircraft defend against radar missiles with jammers. Every aircraft's built-in jammer has the same `jammingIntensity` of 8. That covers everything from trainers to the Darkreach, so jam tolerance can't separate cheap aircraft from fighters.

With jam tolerance below the Scythe's, any aircraft with a jammer can break the Karambit's lock. The Cricket has no jammer, so it's the one aircraft the Karambit reliably kills. The Brawler, Compass and Vagrant have to jam it off and can't out-turn it. Real fighters jam and out-turn it.

## Carriage

It fits the FS-12 Revoker, FS-20 Vortex and KR-67 Ifrit, at double the Scythe's count in every position: a 4-round mount where the Scythe takes 2 or 3, and a 2-round mount where it takes 1. Internal mounts carry 4 rounds in a Revoker bay, 2 in each Vortex bay and 6 in each Ifrit bay.

| Aircraft | Scythe max (internal) | Karambit internal | Karambit external | Karambit total |
|---|---|---|---|---|
| FS-12 Revoker | 12 (4) | 8 | 12 (wings 4 + 4, tips 2 + 2) | 20 |
| FS-20 Vortex | 10 (4) | 8 | 12 (inner 4 + 4, outer 2 + 2) | 20 |
| KR-67 Ifrit | 14 (6) | 12 | 16 (four pylons of 4) | 28 |

The Revoker's wing takes a Karambit rack of 4, not 6. The Ifrit bays need a new 6-round internal mount.

## The external rack is the price

Internal mounts add no drag or RCS, as on every stock bay. External racks are deliberately bad. A rack's empty values apply while it's mounted (`Hardpoint.cs:93-96`), each loaded round adds its share on top (`WeaponMount.cs:167-174`), and RCS adds linearly to the aircraft (`Unit.cs:665`).

| Mount | Drag, loaded / empty | RCS, loaded / empty |
|---|---|---|
| Scythe x2 (stock) | 0.15 / 0.05 | 0.01 / 0.001 |
| AGM-48 x4 (stock quad) | 0.24 / 0.02 | 0.015 / 0.003 |
| AShM-300 x2 (stock, highest drag) | 0.35 | 0.0055 |
| AGM-68 x3 (stock, highest RCS) | 0.25 | 0.075 |
| Karambit x4 | 0.35 / 0.12 | 0.06 / 0.03 |

After firing, four empty racks put a Vortex at an RCS of about 0.12, the size of a Compass on radar. The x2 rack's values are still to be set.

## Model

The model follows the real Peregrine: a slim body with tail control fins and no mid-body wings. It uses stock materials and styling cues so it sits with the game's missiles. The racks follow the same approach. Like the Lance, scripts would generate the meshes, stencil and icon.

## How the game handles this (0.34.2)

These points come from reading the decompiled game code.

- There's no player-selectable weapon mode anywhere. `MissileOptions` is a mission-editor class.
- A missile uses whatever `MissileSeeker` component its prefab has (`Missile.cs`, `GetComponent<MissileSeeker>()`), and `Seek()` is virtual. A custom seeker can set the flight profile through `missile.SetAimpoint`, and can check the target's type, as `ARHSeeker` already does with `targetUnit is Aircraft`.
- Missiles are radar targets. `Missile` implements `IRadarReturn` and registers with the faction's radar returns (`Missile.cs:843`, `FactionHQ.cs:1156`).
- Every detection goes to the faction's tracking database whatever its `shared` flag says (`TargetDetector.cs:237`), and players can target missiles (`TargetScreenUI.cs:228`).
- Blast damage is `25000 / (d / yield^(1/3))^3 - blastArmor`. It is reduced by `blastArmor` again and divided by `max(tolerance, 0.1)`, against 100 hit points (`DamageEffects.cs:42-113`). Proximity fuzes detonate at closest approach.
- The AI intercepts a missile only when `target.value * pK` exceeds the round's cost (`CombatAI.cs`). At $500,000, an AGM-99 (value $1.25M) needs a kill probability above 40%. Fighters' `roleIdentity` has `antiMissile` 0, so AI fighters rarely pick cruise missiles anyway. In practice the Karambit is a player's weapon.

## What building it would take

- A BepInEx plugin for the seeker, installed on the server and every client. It's the mod's first plugin. Blueprinter can load the mod's bundle from the plugin's DLL.
- A missile prefab, weapon info, unit definition and mounts copied from the Scythe, with the numbers above.
- New missile and rack meshes, plus the 6-round internal mount for the Ifrit.

## Open questions

- Does the deck profile reach about 25 km, and what motor gets it there?
- Does it catch the AGM-99?
- Is cost per kill against cruise missiles better than the MMR-S3's, from the same aircraft and track?
- Is the name worth keeping? The mobile game Modern Warships has a fictional "Karambit" anti-ship missile.
