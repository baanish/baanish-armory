# EW pod concept

Status: shelved. Nothing is built. I'm collecting feedback before deciding whether it's worth making.

## The idea

A pair of EW pods for the outer wing pylons of fighters, built from the EW-25 Medusa's wingtip pods. They would be reshaped into streamlined fairings that sit against the wing rather than hanging from a pylon.

You fire it like the offensive jamming pods. With your own radar off, acting as the third receiver, any surface radar that paints you goes on datalink. It draws about 100 kW, so listening costs roughly all of your jammer charge, and radars don't paint constantly. You trade capacitor for information and risk getting caught with little left for the jammer.

## Why it's shelved

Most fighters already locate surface radars that paint them. The Revoker, Vortex, Ifrit, Alkyon, Darkreach and Vagrant carry the same `RadarLocator` component as the Medusa, limited to surface emitters. On those jets the pod would add nothing.

Locating airborne radars would fix that, but it's the Medusa's main advantage. Giving it to any fighter would undercut the Medusa.

## How the game handles this (0.34.2)

These points come from reading the game code. An in-game check confirms the locator: in free flight with no other friendly unit in line of sight, a fighter with its radar off, painted by an enemy Hyperion carrier about 25 miles away, showed both the paint line and the carrier on the map with its speed and heading.

- The Medusa doesn't triangulate. When an enemy radar paints an aircraft that has a `RadarLocator`, the server puts that radar's exact position on the faction map, accurate for about 4 seconds. One paint is enough.
- Being painted has a range, set by the enemy radar rather than by your aircraft. An enemy radar checks you once per scan (every 4 to 5 seconds on stock radars) when you're within twice its detection range, inside its scan cone, above its horizon and not hidden by terrain. Your radar cross-section and jamming don't affect the warning, only whether that radar actually detects you. A SAM radar with 25 km detection range gives your locator its position out to 50 km.
- The Medusa's locator also reports airborne radars and active radar missiles. Other fighters' locators report surface radars only. The Brawler, Cricket, trainer and helicopters have no locator.
- The locator ignores your own radar. Turning your radar off only hides you from anti-radiation missiles and AI anti-radar targeting.
- Every candidate jet has a capacitor (`PowerSupply`). Fighters hold 400 kJ plus 100 kJ from the built-in jammer. The Medusa holds 1,070 kJ plus 100 kJ. Output tapers below 70% charge, so a 100 kW draw drains a fighter in a few seconds.
- Stock power draws, for scale: jamming pod 13 kW, ECM pod 25 kW, the Medusa's built-in jammer 50 kW.

## What building it would take

- A small BepInEx plugin. Blueprinter can't do this alone, because a pylon item only connects radars, countermeasures, weapons and turrets to the aircraft carrying it. Blueprinter can load the mod's bundle embedded in the plugin's DLL.
- The plugin installed on the server and every client.
- New pod geometry. The Medusa's wingtip pod is the end cap of its wing, with the navigation light and wingtip vortex attached, so it needs reshaping into a closed fairing for each jet's pylon position.

Starting points in the game code:

- `JammingPod` is the stock fire-button pod. Its fire state is already synced in multiplayer.
- `Hardpoint.SpawnMount` shows what a mount gets connected to. A `Countermeasure` mount receives the aircraft carrying it through `AttachToUnit`.
- `RadarLocator` subscribes to `Aircraft.onRadarWarning` and reports through `NetworkHQ.CmdUpdateTrackingInfo`. A pod would hook the same event.
- `PowerSupply.DrawPower` takes kW. `RadarJammer` shows a power draw per fire.
- The Medusa's pod meshes are `EW1_wingtip_L` and `EW1_wingtip_R`.
- Each jet's outer pylon hardpoint index still needs looking up.

## A version that isn't redundant

Replace passive listening with a one-shot sweep. Press fire, pay a fixed chunk of charge, and every emitting surface radar within some range goes on datalink for a few seconds, including radars that aren't painting you. Stock fighters can't do that today.

## Feedback I'm after

- Would you carry it over a missile pair?
- Is there a version that doesn't step on the Medusa?
