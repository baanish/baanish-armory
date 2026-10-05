# baanish-armory 0.3.0 release candidate

SAAM-18 Karambit joins Eyeball-XL and AGK-4 Lance in one manual-install package. Karambit is a small radar interceptor for cruise-missile salvos, with tandem rounds on adapted Scythe rails and a custom seeker that prioritizes the designated track.

## Karambit

- Revoker capacity is 20 rounds, eight internally and twelve on two six-round wing racks. Its wingtips do not take Karambits. Vortex capacity is 20 and Ifrit capacity is 28.
- Select incoming tracks and use native burst firing to distribute shots. The seeker prioritizes each designation and can fall back to another qualifying return.
- Low cruise missiles use terrain avoidance with a 10 ft clearance goal. Aircraft, SAMs, lofting missiles and ballistic missiles use air interception.
- Ideal delta-v is 900 m/s, split into 300 m/s from the booster and 600 m/s from the sustainer. Launch mass is 62.98 kg and dry mass 43.75 kg. Thrust cuts off at Mach 2.5. The 12 nautical mile envelope is nominal, not a guaranteed interception range.
- Each round costs $500,000, with blast yield 8 and pierce damage 400.
- The procedural reference-based model is 1.8148 m long with a 120 mm body and two matte blue bands. It uses the stock Weapons4 material and adapted Scythe rails and adapters.

## Install

Requires Nuclear Option 0.34.2, BepInEx 5 for Windows x64 and Blueprinter 2.0.1.

Use `baanish-armory_0.3.0-manual-install.zip`. Close the game, back up and uninstall any old Armory addon, and remove the separate Karambit prototype if installed. Copy the ZIP's one `baanish-armory` folder into `BepInEx/plugins`.

Keep both bundles, `Baanish.Karambit.dll` and metadata together. Disable or remove the whole folder. The internal Karambit bundle name retains `prototype0.1.0` and is included in this release candidate. NOMM 3.1.0 catalog support has not been verified. See the [installation guide](INSTALL.md) for the 0.2.0 addon migration.

## Testing

Single-player manual playtests include the current 900 m/s Karambit balance. Earlier automated checks destroyed a full 20-missile salvo with the previous motor balance and four-round external racks. Those results do not establish the current balance's kill rate, cost per kill or Revoker six-round rack performance. Multiplayer remains untested.

All three weapons remain experimental. Bug reports should include the game, Blueprinter and mod versions, the aircraft and loadout, and reproduction steps.
