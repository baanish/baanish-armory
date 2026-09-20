# Install Eyeball-XL

These are the installation steps I use with Nuclear Option Mod Manager 3.1.0. You'll also need Nuclear Option 0.34.2, BepInEx 5 for Windows x64 and Blueprinter 2.0.1 installed separately.

## Install with NOMM

1. Close Nuclear Option. Install and enable Blueprinter 2.0.1 in Nuclear Option Mod Manager.
2. If `baanish-armory` is already installed, back up its folder outside the game's `BepInEx` directory, then uninstall that entry in NOMM.
3. Download `baanish-armory_0.1.5-manual-install.zip` from [releases](https://github.com/baanish/baanish-armory/releases) and extract it into a temporary folder. Copy its single `baanish-armory` folder into the game's `BepInEx/plugins` directory.
4. Refresh NOMM's Library. Confirm one `baanish-armory` entry at version 0.1.5 and enable it. Keep Blueprinter enabled too.
5. Launch the game. On the EW-25 Medusa, select **Outer Wing Pylons > Eyeball-XL**. The pair carries two missiles total, at $2.5 million each and $5 million for the pair.

Keep only one installed copy. NOMM scans enabled, disabled and nested addon folders and may delete duplicate IDs during refresh. Keep backups outside those folders.

NOMM 3.1.0's **Add from file** moves a selected file into plugins; it does not unpack this ZIP. **Import Modpack** changes the enabled mod set and is not the route for this package. Extract the folder manually as described above.

I haven't set up a catalog listing or automatic updates yet. Check that Blueprinter is version 2.0.1, since NOMM's local dependency check doesn't enforce that version.

## Disable or remove

Use the `baanish-armory` toggle in NOMM to disable it, or its uninstall action to remove it. NOMM may move an enabled addon under Blueprinter's `addons` folder. Manage the entry through NOMM instead of creating another copy in `plugins`.

## Feedback

My playtest went well, but I haven't tested multiplayer. I'd appreciate feedback on spotting, rearming and whether the loadout and price feel worthwhile. If something breaks, include your game, Blueprinter and mod versions and the steps that led to it.
