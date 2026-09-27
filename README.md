# baanish-armory

A Nuclear Option mod that adds two weapons: Eyeball-XL, a larger passive reconnaissance missile for the EW-25 Medusa, and the AGK-4 Lance, a laser-guided kinetic rocket for killing armor and SPAAGs. It needs Nuclear Option 0.34.2 and Blueprinter 2.0.1.

Download the manual-install ZIP from [releases](https://github.com/baanish/baanish-armory/releases) and follow the [installation guide](docs/INSTALL.md).

## Eyeball-XL

I wanted a bigger Eyeball with a real loadout trade-off. Eyeball-XL uses the ARAD-116's body and flight model, the Eyeball's optical guidance, and a custom nose with three downward-facing windows.

It fits only on the Medusa's outer wing pylons, one missile per rack, so you get two XLs where you could carry four ARAD-116s. The inner pylons stay free, and XL can't go in an internal bay. Each missile costs $2.5 million.

The passive sensor has a 12 km search setting at 1x magnification. How far it actually spots something depends on the target's visibility and terrain. It has no active radar.

I've considered giving enemy aircraft a brief optical-lock warning when an XL launches, without the missile homing on them. I can't tell yet whether that would be a fair drawback or a free decoy, especially if it warned everyone across the map, so it isn't in the mod.

## AGK-4 Lance

The Lance is an AGR-24 Kingpin stretched to twice the length with half the frontal area. Its motor burns for just over 5 seconds instead of 1.5, and it carries a small charge behind a kinetic penetrator. It kills tanks and SPAAGs, usually in one hit. Against a ship it only destroys the part it hits, such as a magazine, turret or radar, and barely scratches the hull.

Fired from a helicopter, it covers 8 km in about 6 seconds, where a Kingpin takes about 21. In exchange, it turns half as well and its launch circle is half the size, so line up before you fire and keep the target lased. A helicopter's shot stays just under the speed where SPAAG spotters stop seeing it, while a supersonic jet's outruns them for most of a normal shot. Ground lasers can't burn it down.

It comes in a four-round pod at $150,000 a round. The pod fits the Ifrit outer wing, Vortex inner wing, Revoker wing, Brawler inner and outer fuselage, Chicane stub tip, Ibis fuselage, Vagrant centre, Compass inner wing and Cricket fuselage pylons.

The numbers, and the game-code findings behind them, are in the [Lance concept notes](docs/LANCE-CONCEPT.md).

## Status

Both weapons are experimental. My single-player playtests went well, but I haven't tested multiplayer, and price, range and drag may still change.

I'd appreciate bug reports and balance feedback. For a bug, include your game, Blueprinter and mod versions, what happened and how to reproduce it.

## Requirements

- Nuclear Option 0.34.2.
- BepInEx 5 for Windows x64.
- [Blueprinter 2.0.1](https://github.com/nikkorap/NOBlueprinter-Releases), enabled alongside this mod.
- Nuclear Option Mod Manager 3.1.0 for the installation guide.

NOMM can manage the installed folder, but there's no catalog listing or automatic update yet. To build or modify the mod, see the [build guide](docs/BUILD.md), and read the [contribution notes](CONTRIBUTING.md) before sending changes.

## AI art disclosure

I used AI for all the custom art because I don't have the modeling or art skills to make it myself. GPT-6 Astra made the Eyeball-XL's nose and icon. Claude Opus 5.5 made the Lance's model, pod, icon and stencil, which the scripts in `art/source/lance` generate.

If you'd like to contribute human-made models or art, get in touch before putting a lot of work in so we can agree on the design. The reused Nuclear Option geometry and materials are the work of the game's creators.

## Licensing

I've licensed the original code and documentation under [MIT](LICENSE). Nuclear Option assets remain the property of their respective owners, and the Blueprinter template keeps its [own MIT license](unity/LICENSE). See [asset and dependency notices](THIRD-PARTY-NOTICES.md).
