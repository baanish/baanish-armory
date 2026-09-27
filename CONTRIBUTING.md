# Contributing

Bug reports, balance feedback and contributions are welcome. Talk to me before starting a larger change, especially a new weapon or replacement model.

## Models and art

I used AI for the custom art because I don't have the skills to make it myself. GPT-6 Astra made the Eyeball-XL's art and Claude Opus 5.5 made the AGK-4 Lance's. If you want to contribute human-made models or artwork, I'd like to talk about it.

For Eyeball-XL, I want to keep the ARAD-sized body and three recessed windows on the lower nose. The centre window faces straight down. The side windows face down and outward at 45 degrees. The upper nose stays clear. Please discuss changes to that shape before making a finished model.

Scripts in `art/source/lance` generate the Lance's model, pod, icon and stencil, and its dimensions live in `lance_shape.py`. The length and diameter are repeated in `lance_pod.py` (`LANCE_LENGTH` and `SLIM`) and in `LanceAssetBuild.cs` (`Length` and `Diameter`, which size the collider and launch rail), and the length is also in the rail check in `PrototypeBuild.cs`, so change them together. To change the Lance, edit those scripts and rebuild as the [build guide](docs/BUILD.md#rebuild-the-agk-4-lance-assets) describes. Don't edit its exported meshes by hand, since the next rebuild overwrites them.

Include editable source files and explain which parts you created and which depend on existing game assets. Don't submit extracted game textures, assemblies, asset dumps or third-party reference photographs as original contributions.

## Code and testing

Keep changes focused and explain what they fix. Tell me what you tested in game, such as equipping, launching, spotting or rearming. If you tested multiplayer, say so.

Follow the [build guide](docs/BUILD.md) to set up the Unity project. The build checks the mod assets against a list of file hashes. If you change those assets, update `config/prototype-baseline-0.2.0.json` in the same change so I can review both together.

Run `./scripts/Test-Source.ps1` before submitting a change. GitHub runs the same source checks on pushes and pull requests. Unity builds happen locally, so make any release build on your own prepared PC.
