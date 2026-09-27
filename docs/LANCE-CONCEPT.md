# AGK-4 Lance concept

Status: built in 0.2.0 and not yet tested in game. The numbers below are what the build uses, not balance I've settled on.

## The idea

The AGK-4 Lance (air-ground kinetic) is a laser-guided anti-armor rocket built from the AGR-24 Kingpin. It's twice as long with half the frontal area, has much smaller fins, a longer burn, and a small charge behind a penetrator. It kills tanks and SPAAGs. Against ships it only knocks out the parts it hits directly, like a radar or magazine, and barely scratches the hull.

It's about three times as fast as the Kingpin, but it turns half as well and the launch circle is half the size, so you have to line up carefully.

## Proposed numbers

| | AGR-24 Kingpin (stock) | AGK-4 Lance |
|---|---|---|
| Rounds per mount | 4, or 12 on the triple rack | 4, no triple rack |
| Mount drag, loaded and empty | 0.1 | 0.08 |
| Mount RCS, loaded and empty | 0.006 | 0.005 |
| Mount mass, loaded (empty) | 292 kg (60 kg) | 335 kg (95 kg) |
| Thrust, burn time | 28 kN for 1.5 s | 28 kN for 5.15 s |
| Fuel, total mass | 4.5 kg, 58 kg | 16 kg, 60 kg |
| Game delta-v, before drag | 754 m/s | 2,795 m/s |
| Fin area (drag and lift) | 0.07 | 0.035 |
| Pierce | 1,700 | 3,000 |
| Fire armor / tolerance (laser resistance) | 1 / 0.1 | 6 / 0.3, as the GPO-500 |
| Blast yield | 10 (43 m radius) | 2 (25 m radius) |
| Launch arc (`minAlignment`) | 8° | 4° |
| Seeker cone (`maxSeekerAngle`) | 10° | 5° |
| Minimum range | 500 m | 1,000 m |
| AI maximum range | 8 km | see [range](#range) |
| Cost per round | $90,000 | $150,000 |

The motor keeps Kingpin's thrust, so launch feels the same, and burns about 3.4 times as long. The burn time is tuned so a helicopter shot peaks at 1,999 m/s, just under the SPAAG spotters' cutoff.

The length and width change the model only. The game computes drag and lift from fin area and ignores the unit's dimensions. Twice the length at half the frontal area also keeps the volume, so the mass stays about the same.

For a real-world reference, burnout from a helicopter comes to about 2,000 m/s, or Mach 5.9. Kinetic-energy missiles like LOSAT's KEM ran at about 1,500 m/s, and CKEM was meant to go faster.

## Speed depends on the launch

SPAAG and CIWS spotters ignore anything moving faster than their speed limit: 2,000 m/s on the AeroSentry, Anvil and AFV6 AA, and 1,000 m/s on the CRAM truck. Lance's motor is sized around that limit. A slow launcher stays under it, and a fast one pushes past it. Once the rocket slows back under 2,000 m/s, spotters can see it again, so a long shot always ends visible.

These come from a level-flight sim at 60 Hz using the game's drag formula. Treat them as rough:

| Launch | Peak speed | Hidden from SPAAGs | 4 km | 8 km | 12 km |
|---|---|---|---|---|---|
| Kingpin, helicopter, 70 m/s | 773 m/s | never | 7.5 s | 21.0 s | doesn't reach |
| Kingpin, supersonic jet, 566 m/s at 3 km | 1,166 m/s | never | 4.3 s | 11.0 s | 22.6 s |
| Lance, helicopter, 70 m/s at 300 m | 1,999 m/s | never | 4.1 s | 6.3 s | 9.4 s |
| Lance, subsonic jet, 250 m/s at 3 km | 2,212 m/s | until 8.1 km | 3.7 s | 5.7 s | 8.0 s |
| Lance, supersonic jet, 566 m/s (1,100 kt) at 3 km | 2,315 m/s | until 9.8 km | 3.2 s | 5.1 s | 7.2 s |
| Lance, supersonic jet, 566 m/s at 6 km | 2,518 m/s | until 12.2 km | 3.2 s | 5.0 s | 6.7 s |

A 1,999 m/s peak is a knife edge. A helicopter in a dive or at speed will cross the cutoff, and the sim is only rough, so the burn time needs tuning in game. A hovering or slow helicopter's Lance is always visible, so a SPAAG at long range gets a shot at it. A supersonic jet's Lance stays hidden for most of a normal shot. The rocket itself has no speed cap. It inherits the launcher's velocity and its motor accelerates it against drag. The spotters' 2,000 m/s cutoff is a hard edge in their code, not a gradual penalty, so the in-game test should confirm where it lands.

## Range

The game already works out range from the rocket's flight model. The laser HUD calls `Missile.CalcRange` once a second with your speed, your altitude and the target's altitude, then shows MAX and OUT OF RANGE from the result. The player-facing range falls out of the motor and fin numbers above, and changes with every shot.

That estimate runs out until the rocket drops below 200 m/s, the point where the seeker self-destructs it. For Lance that's 36 km from a hover and 50 km or more from a jet, far beyond anything you can lase. Kingpin reads 11 to 19 km on the same calculation.

The real limit is guidance. A laser rocket only locks a lased target within 15 km, inside its seeker cone and in line of sight. For Lance, that 15 km lock limit sets the effective range, not fuel.

The one hand-set number is `targetRequirements.maxRange`, which only tells the AI when to fire. Kingpin's 8 km is about three quarters of its kinematic range from a hover. For Lance I'd set it by time of flight rather than kinematics: 12 km, where a helicopter shot still arrives in about 10 seconds.

## Why speed and not warhead

A stock Kingpin already kills every SPAAG in one hit, with pierce to spare even at a glancing angle. The warhead isn't what makes SPAAGs hard for it. The flight time is: 21 seconds to cover 8 km from a hover against a 30 mm gun that shoots down rockets, and gives triple priority to one aimed at itself. Lance covers the same 8 km in under 7 seconds.

The extra pierce is for heavy tanks. A Kingpin needs two square hits on a Spearhead's turret. Lance kills it with one hit within about 53° of square, and it kills the Spearhead hull and every Type-12 part at any angle.

## Ships

Pierce damage is `(pierce - part armor) / part tolerance`, taken from the part's 100 hit points. Ship hulls have tolerances of 250 to 2,000, where tanks have 3 or 4, so the hull shrugs off penetrators. Only the part you hit takes damage, and the blast is cut to 2, so a Lance does nothing to a ship except destroy what it hits.

What one square hit does at 3,000 pierce, with a hit counted as a kill at 100 damage:

| Part | Armor / tolerance | Damage | Kill? |
|---|---|---|---|
| Magazines, all ships | 100 to 350 / 1 to 3 | 880 to 2,900 | yes |
| Radar: corvette, both carriers | 50 to 100 / 3 to 10 | 295 to 967 | yes |
| CIWS, SAM and gun turrets | 100 to 150 / 3 to 5 | 580 to 967 | yes |
| Radar: destroyer | 150 / 100 | 28 | 4 hits |
| Bridge: destroyer, fleet carrier, patrol boat | 120 to 200 / 100 | 28 | 4 hits |
| Radar and bridge: frigate | 190 / 1,000 | 3 | no |
| Destroyer hull | 300 / 1,000 | 3 | no |
| Corvette critical hull | 75 / 250 | 12 | 9 hits |

I'd like a penetrator through a bridge to take it out, but I'm keeping 3,000. One-shotting bridges and the destroyer radar takes about 10,500 pierce. That would also one-shot patrol boats, whose bridge, bow and stern are critical parts with the same tolerance, and kill a corvette in three hits. That makes it a ship weapon. The frigate's radar and bridge are armored like hull, so no pierce value makes them one-shot anyway.

## How the game handles this (0.34.2)

These points come from reading the game code.

- Speed doesn't add to damage. Pierce is a flat number, scaled only by impact angle, down to half at a glancing hit.
- High speed doesn't make it pass through targets. Each physics step at 60 Hz casts a line 1.1 times the distance the rocket travels. At 2,500 m/s that's 46 m, much longer than a 5.6 m tank hull.
- Turn radius at full lift depends on mass divided by fin area, not on speed. Halving fin area doubles it: about 2.8 km at sea level, against 1.35 km for Kingpin. At three times the speed, it also has a third of the time to correct.
- The HUD launch circle is `min(minAlignment, distance × 0.002)` degrees, so it's `minAlignment` beyond 4 km at stock and 2 km at 4°.
- A longer burn keeps the plume visible about 3.4 times as long. While burning, the rocket's visual range grows by about 6.7 km.
- Laser guidance leads the target using the weapon's listed max speed. That field has to match the real top speed or the rocket will lead wrong.
- AI weapon choice uses armor tiers, and ships (tier 4 and 5) sit below tanks (tier 6). I can't keep AI from firing it at ships through tiers alone.

## Pod drag and RCS

The game doesn't derive these from the pod's shape. Each mount has hand-set drag and RCS values that the carrying aircraft adds to its own, and pods keep them when empty. The stock values are coarse: the Kingpin x4 and the smaller Lynchpin x7 pods both use drag 0.1.

The Lance pod has about half the Kingpin pod's frontal area and a longer pointed front, but twice its length adds skin friction. I've put it at drag 0.08. Its RCS is 0.005, like the Lynchpin pod: slimmer and pointier, but longer from the side. Its empty mass rises with the longer body.

## Lasers

Every missile has 100 hit points. A laser hits every 0.2 s for its damage times 0.2, scaled down linearly with range, and a missile loses `(tick - fire armor) / fire tolerance` per tick. The LADS truck, laser trailer and carrier laser top out at 6 per tick, and the Medusa's laser at 8.

The Lance uses the GPO-500's fire armor, 6 with tolerance 0.3, so a solid penetrator shrugs off heat. No ground laser can burn it down. Only the Medusa's laser can, in about 3 s at point blank and not at all beyond 5 km. A Kingpin dies to a LADS in under a second.

## Radar cross-section

I first wanted a larger radar cross-section as a drawback. It doesn't work against the targets that matter: no SPAAG or CIWS has a radar, and they spot by sight. A pod's RCS adds to the carrying aircraft's, so a bigger one mostly penalizes stealth jets against SAM radars. The 4-round limit, the price and the narrow launch circle carry the drawback instead.

## What building it would take

Every change is a data field on stock components: the Kingpin's motor, aero, warhead, laser seeker, weapon info and mount. Blueprinter can do it without a plugin, like Eyeball-XL.

- A stretched Kingpin model: twice the length, 0.71 times the diameter, with smaller fins. The 4-round pod needs lengthening to match.
- A new weapon info and a 4-round mount, added to the same aircraft that carry Kingpin.
- Set the weapon info's `maxSpeed` to the measured top speed from a jet launch.
