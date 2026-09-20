# baanish-armory

My Nuclear Option mod, starting with Eyeball-XL: a larger passive reconnaissance missile for the EW-25 Medusa.

I wanted a bigger Eyeball with a real loadout trade-off. Eyeball-XL uses the ARAD-116's body and flight model, the Eyeball's optical guidance, and a custom nose with three downward-facing windows.

It fits only on the Medusa's outer wing pylons, one missile per rack. You get two XLs where you could carry four ARAD-116s. The inner pylons stay available, and XL can't go in an internal bay. Each missile costs $2.5 million, or $5 million for the pair.

The passive sensor has a 12 km search setting at 1x magnification. How far it actually spots something still depends on the target's visibility and terrain. It has no active radar.

This is an experimental release. My playtest went well, but I haven't tested multiplayer. I'm still working out the balance, so price, range and drag may change.

One balance idea I've considered is giving enemy aircraft a brief optical-lock warning when an XL launches, without the missile homing on them. I'm unsure whether that would be a useful drawback or an unintended decoy advantage, especially if it warns everyone across the map. This isn't implemented, and I haven't decided whether to pursue it.

## Requirements

- Nuclear Option 0.34.2.
- BepInEx 5 for Windows x64.
- [Blueprinter 2.0.1](https://github.com/nikkorap/NOBlueprinter-Releases), enabled alongside this mod.
- Nuclear Option Mod Manager 3.1.0 for the linked installation guide.

Download the manual-install ZIP from [releases](https://github.com/baanish/baanish-armory/releases) and follow the [installation guide](docs/INSTALL.md). NOMM can manage the installed folder, but I haven't added a catalog listing or automatic updates yet.

If you want to build or modify it, see [the build guide](docs/BUILD.md).

## AI art disclosure

I used AI, specifically GPT-6 Astra, for all the custom art. I don't have the modeling or art skills to make it myself.

If you'd like to contribute human-made models or art, I'm open to talking about it. Please get in touch before putting a lot of work into something so we can agree on the design.

The reused Nuclear Option geometry and materials are the work of the game's original creators. The AI disclosure applies to my custom art.

## Contributing

I'd appreciate bug reports and balance feedback. Include your game, Blueprinter and mod versions, plus what happened and how to reproduce it. See [contribution notes](CONTRIBUTING.md) for code and art changes.

## Licensing

I've licensed the original code and documentation under [MIT](LICENSE). Nuclear Option assets remain the property of their respective owners, and the Blueprinter template keeps its [own MIT license](unity/LICENSE). See [asset and dependency notices](THIRD-PARTY-NOTICES.md).
