# Install baanish-armory

These steps use Nuclear Option Mod Manager (NOMM) 3.1.0. Install Nuclear Option 0.34.2, BepInEx 5 for Windows x64 and Blueprinter 2.0.1 first.

## Install with NOMM

1. Close Nuclear Option. In NOMM, install and enable Blueprinter 2.0.1.
2. If an older `baanish-armory` is installed, back up its folder outside the game's `BepInEx` directory, then uninstall it in NOMM.
3. Download `baanish-armory_0.2.0-manual-install.zip` from [releases](https://github.com/baanish/baanish-armory/releases) and extract it to a temporary folder. Copy the `baanish-armory` folder inside it into the game's `BepInEx/plugins` directory.
4. Refresh NOMM's Library. Check that there's one `baanish-armory` entry at version 0.2.0, then enable it. Leave Blueprinter enabled.
5. Launch the game and check both weapons:
   - On the EW-25 Medusa, select **Outer Wing Pylons > Eyeball-XL**. You get two missiles at $2.5 million each.
   - On the FS-20 Vortex, select **Inner wing pylons > AGK-4 Lance x4**. Each pod holds four rounds at $150,000 each. The [README](https://github.com/baanish/baanish-armory#agk-4-lance) lists every pylon that takes the pod.

Keep only one installed copy. NOMM scans enabled, disabled and nested addon folders, and it may delete duplicate IDs when it refreshes. Keep backups outside those folders.

Don't use NOMM 3.1.0's **Add from file** or **Import Modpack** for this package. Add from file moves the ZIP into `plugins` without unpacking it, and Import Modpack changes your enabled mod set. Extract the folder by hand as in step 3.

NOMM doesn't enforce Blueprinter's version, so check that it's 2.0.1 yourself.

## Disable or remove

Use the `baanish-armory` toggle in NOMM to disable it, or its uninstall action to remove it. NOMM may move an enabled addon into Blueprinter's `addons` folder, so manage the entry through NOMM rather than adding a second copy to `plugins`.

## Feedback

I haven't tested multiplayer. I'd like to hear whether the loadouts and prices feel worth it, how the XL's spotting holds up, and how the Lance does against SPAAGs. If something breaks, include your game, Blueprinter and mod versions and the steps that led to it.
