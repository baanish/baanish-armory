# Build baanish-armory 0.2.0

Use this project to build the baanish-armory Blueprinter bundle and source archive, with Eyeball-XL and the AGK-4 Lance. You'll need a local game installation and asset export. The repository does not include extracted game assets.

## Prepare the local dependencies

Use Windows, PowerShell 7, Git and Unity Editor **2022.3.62f2**. You need a purchased copy of Nuclear Option **0.34.2** to obtain the game assemblies and placeholder assets. The project pins Blueprinter Editor to commit `9a4a8e509480cbcde901a577985d5e9cbc570d91` through Unity's package manifest.

Keep the repository and export in plain local folders. Symlinks, junctions and OneDrive Files On-Demand locations are rejected by the path checks.

1. Open this repository's `unity` folder in Unity Hub. Let Unity restore its packages.
2. Export your game installation using **AssetRipper 2.0.0**, with Script Content Level **Level 1** and Script Export Format **Decompilation**. Keep the export path short enough for Unity's Windows path limit. Other versions have not been checked for compatible asset paths and object IDs.
3. In Unity, open **Blueprinter > Project Setup**. Enter game version `0.34.2`.
4. Choose **Import Game Assemblies** and select your `NuclearOption.exe`. Let Unity finish compilation and reload before continuing. Copying a generated `Packages/nuclearoption` directory into a fresh project does not replace this step.
5. Use **Baanish Armory > Import game assets with stable references** and select the export's `ExportedProject/Assets` directory. This helper seeds the 28 game asset IDs required by the authored files before calling Blueprinter's importer. A plain Blueprinter import accepts new AssetRipper IDs, which do not match these files.
6. Import **TMP Essentials** when prompted, then choose **Refresh Op References**.

Use this repository's Unity project. Its import helper and build script keep the mod's game references intact.

Imported game assets, assemblies, TMP resources and Unity caches stay local. Do not include them in a pull request or source archive.

If a fresh project cannot load the missile definition despite having the DLLs, repeat **Import Game Assemblies** through Project Setup and let Unity reload. This registers the game types with Unity; reimporting the missile asset alone does not fix missing type registration.

## Build the authored source

Close this project's Unity editor. From the repository root, run:

```powershell
./scripts/Build-Prototype.ps1
```

The script reads the required editor version from the project and looks for a matching Windows installation in the registry, the standard Unity Hub folder, then `PATH`. To select an executable explicitly, use:

```powershell
./scripts/Build-Prototype.ps1 -UnityPath 'X:/Unity/2022.3.62f2/Editor/Unity.exe'
```

The build writes a timestamped directory under `.local/builds` with the `.nobp` bundle, Blueprinter `.source.zip`, patch manifest, checksums and editor diagnostics.

The baseline manifest pins the mod assets. If you change an asset, include its baseline update in the same change. Do not bypass validation to produce a release.

Every build also verifies the 28 imported stock assets and 29 object references. Missing or remapped dependencies stop the build before it creates output, even when the authored files still match the baseline.

A Blueprinter `.source.zip` contains the mod assets for importing into another prepared Blueprinter project. It is separate from the repository source archive, which includes this build code and documentation.

`DependencyBundleBuild` limits Blueprinter's separate stock bundle to the mod's recursive game dependencies. The stock bundle stays local. This avoids compiling unrelated exported compute shaders and material variants. The helper calls private methods in the pinned Blueprinter editor revision, so upgrading that dependency requires checking the helper and rebuilding from a fresh cache.

## Rebuild the AGK-4 Lance assets

The Lance's model, pod, icon and stencil come from scripts in `art/source/lance`, and the Unity assets are authored from them. You only need this after changing the Lance's shape, art or stats. Blender 4.2 or later runs the model scripts, and Python 3 with Pillow (`pip install pillow`) runs the icon and stencil scripts. `EXPORT` is your AssetRipper `ExportedProject/Assets` directory.

1. Build the model and its preview renders: `blender --background --factory-startup --python art/source/lance/build_lance.py -- <preview folder> <EXPORT>`.
2. Export the Unity meshes: `blender --background --factory-startup <preview folder>/lance.blend --python art/source/lance/export_lance_meshes.py -- .local/lance-export <EXPORT>`.
3. Redraw the icon and stencil if they changed: `python art/source/lance/lance_icon.py` and `python art/source/lance/lance_stencil.py`.
4. With the Unity project closed, run **Baanish Armory > Author AGK-4 Lance** through `Unity.exe -batchmode -quit -projectPath unity -executeMethod BaanishArmory.Editor.LanceAssetBuild.Author`. It refreshes the Kingpin-based copies and keeps their GUIDs.
5. Update `config/prototype-baseline-0.2.0.json` for the changed files, then build.

`render_comparison.py` and `render_vortex_loadout.py` render the Lance beside the stock AGRs and on an FS-20 Vortex for review.

## Prepare local release packages

After a successful build, pass its output directory to:

```powershell
./scripts/Prepare-Release.ps1 -BuildDirectory '.local/builds/prototype-<timestamp>'
```

The command verifies the build hashes and exported source, then creates a new directory under `.local/releases`. It contains the loose bundle, Blueprinter source ZIP, repository source ZIP, manual-install ZIP, local NOMM metadata and checksums. Git's public file list must exactly match `config/release-source-files.txt`; review changes to that list before packaging. The release manifest records a SHA-256 hash for every included source file.

The script can package an uncommitted working tree. Before publishing, verify that the source package matches the release commit.

The script writes local packages. Use the [installation guide](INSTALL.md) to install the manual ZIP through NOMM.

## CI and releases

I build releases on my own PC. Contributors need to build their releases on their own prepared machines too. GitHub Actions runs source checks on pushes and pull requests: the public file list, PowerShell syntax and the pinned asset hashes. It does not run Unity or produce a playable download.

To run those checks locally:

```powershell
./scripts/Test-Source.ps1
```

Commit your changes, then build and package a release in one command:

```powershell
./scripts/Publish-Release.ps1
```

That command uploads nothing. To build and publish a new version, install the GitHub CLI, sign in with `gh auth login`, and push the release commit to the repository's default branch. Save your release notes in an ignored file, then run:

```powershell
./scripts/Publish-Release.ps1 -Publish -NotesFile '.local/release-notes.md'
```

The command builds with your local Unity installation, packages the output, uploads a draft, verifies every uploaded file's SHA-256 hash, then publishes it as a prerelease. It requires a clean working tree and a local commit matching the GitHub default branch. It refuses to replace an existing version tag. A failed upload stays in draft for inspection.

The current build and packaging checks pin version 0.2.0. For a new version, update the mod metadata, baseline, game reference map and matching validation checks together. Passing `-UnityPath` selects a specific editor installation. Repository visibility is never changed by these scripts.

## Verify in game

With Blueprinter and the mod enabled, select **EW-25 Medusa > Outer Wing Pylons > Eyeball-XL**. Check one missile on each rack, two rounds total, usable inner pylons and no internal XL option. Confirm $2.5 million per missile and $5 million per pair. Launch both missiles and inspect the aircraft, missile model and loadout icon. Check spotting, rearming and the original Eyeball on an aircraft that normally carries it.

For the Lance, select **FS-20 Vortex > Inner wing pylons > AGK-4 Lance x4** and check four rounds per pod at $150,000 each, the loadout icon, the AGK-4 stencil and the rocket noses in the tube mouths. Check that the pod is offered on each listed hardpoint and that the VT-7 Vagrant's centre pylon clears the landing gear. Fire at a lased tank and a lased SPAAG from a helicopter and from a supersonic jet, and confirm the laser windows show the same purple as the Eyeball-XL's.

The passive detector's 12 km search setting is not a measured detection radius. Target visibility and terrain affect spotting. I haven't tested multiplayer. Balance and exact endurance still need testing.

## Validation scope

The build checks asset hashes, imported game references, both weapons' display names, costs and hardpoint registrations, the Eyeball-XL's $2.5 million per-round and $5 million pair costs, and the Lance's motor, penetrator, seeker, pod and materials. Packaging checks the exported source and ZIP contents against the inputs and rejects files outside `config/release-source-files.txt`.

These checks catch missing dependencies and unintended asset changes. They don't replace an in-game test. Bundle bytes and ZIP timestamps are not guaranteed to reproduce identically.
