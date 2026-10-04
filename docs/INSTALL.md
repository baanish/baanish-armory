# Install baanish-armory

Install Nuclear Option 0.34.2, BepInEx 5 for Windows x64 and [Blueprinter 2.0.1](https://github.com/nikkorap/NOBlueprinter-Releases) first. Keep Blueprinter enabled.

## Install or upgrade to 0.3.0

1. Close Nuclear Option.
2. If an older version is installed, back up its folder outside the game's `BepInEx` directory, then remove the old installation. NOMM may have moved the 0.2.0 addon into `BepInEx/plugins/Blueprinter/addons`; uninstall that entry through NOMM if it manages it. Also remove `BepInEx/plugins/baanish-karambit-prototype` if you installed the separate local prototype. Keep backups outside enabled, disabled and addon folders.
3. Download `baanish-armory_0.3.0-manual-install.zip` from [releases](https://github.com/baanish/baanish-armory/releases) and extract it to a temporary folder.
4. Copy the ZIP's single `baanish-armory` folder into the game's `BepInEx/plugins` directory. Keep the Armory bundle, Karambit bundle, `Baanish.Karambit.dll` and metadata together in that folder. The DLL supplies Karambit's custom guidance.
5. Launch the game and check the loadouts:
   - On the EW-25 Medusa, select **Outer Wing Pylons > Eyeball-XL**. You get two missiles at $2.5 million each.
   - On the FS-20 Vortex, select **Inner wing pylons > AGK-4 Lance x4**. Each pod holds four rounds at $150,000 each. The [README](https://github.com/baanish/baanish-armory#agk-4-lance) lists every pylon that takes the pod.
   - Select **SAAM-18 Karambit** on the Revoker, Vortex or Ifrit. Maximum capacities are 20, 20 and 28 rounds respectively, at $500,000 each. The Revoker carries eight internally and twelve on its wing pylons. Its wingtips do not take Karambits.

Keep only one installed copy. The release is one plugin folder, not separate Armory and Karambit entries. The Karambit bundle's internal name retains `prototype0.1.0`. It belongs with the 0.3.0 release DLL.

NOMM 3.1.0 catalog and package handling for this combined release have not been verified. Use the manual extraction above. Check Blueprinter's version yourself.

## Fire Karambits at a salvo

Select the incoming missile tracks, choose SAAM-18, and use the game's native burst firing. Each shot prioritizes its designated track when the radar seeker can detect it. The seeker may choose a fallback return if the designation is unavailable, so separate selections do not guarantee separate kills.

The 12 nautical mile envelope is nominal. Target motion, terrain, drag and the Mach 2.5 thrust cutoff affect the shot.

## Disable or remove

Close the game, then move the whole `BepInEx/plugins/baanish-armory` folder outside `BepInEx` to disable it, or delete that folder to uninstall it. Disable the DLL and bundles together. Leave Blueprinter installed for other mods that use it.

## Feedback

I haven't tested multiplayer. I'd like to hear whether the loadouts and prices feel worth it, how the XL's spotting holds up, how the Lance does against SPAAGs, and how Karambit handles missile salvos. If something breaks, include your game, Blueprinter and mod versions and the steps that led to it.
