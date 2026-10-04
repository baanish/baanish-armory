using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace Baanish.Karambit.SalvoTest;

[BepInPlugin(PluginGuid, "Karambit native salvo test selector", "0.1.0")]
[BepInDependency("com.baanish.nuclearoption.agenticframework")]
public sealed class KarambitSalvoControlPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "dev.baanish.karambit.salvo-test";
    private const string MissionName = "_agentic karambit-salvo";
    private static readonly string[] InboundNames = Enumerable.Range(1, 20)
        .Select(i => "Karambit_Inbound_" + i.ToString("D2")).ToArray();
    private ConfigEntry<bool> selectSalvo = null!;
    private string error = "";
    private int selections;

    private void Awake()
    {
        selectSalvo = Config.Bind("Test", "SelectSalvo", false,
            "Select the twenty live incoming HUD tracks in the isolated karambit-salvo agentic mission.");
    }

    private void Update()
    {
        if (!selectSalvo.Value)
            return;
        selectSalvo.Value = false;
        try
        {
            SelectNativeSalvo();
            error = "";
            selections++;
            Logger.LogInfo("Selected all twenty native HUD tracks for the Karambit salvo test.");
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Logger.LogError("Karambit salvo selection refused: " + error);
        }
    }

    private static void SelectNativeSalvo()
    {
        if (MissionManager.CurrentMission?.Name != MissionName || !MissionManager.IsRunning ||
            !GameManager.GetLocalAircraft(out Aircraft own) || own.disabled || own.UniqueName != "Karambit_Ifrit" ||
            own.definition.jsonKey != "Multirole1" || own.weaponManager == null)
            throw new InvalidOperationException("Requires the running " + MissionName + " mission and its own Karambit_Ifrit.");
        var hud = SceneSingleton<CombatHUD>.i;
        if (hud == null)
            throw new InvalidOperationException("The Ifrit combat HUD is not available.");

        var targets = new List<Unit>(20);
        foreach (string name in InboundNames)
        {
            var matches = UnitRegistry.allUnits.Where(unit => unit != null && !unit.disabled && unit.UniqueName == name).ToArray();
            if (matches.Length != 1 || !(matches[0] is Missile) || matches[0].definition.jsonKey != "AShM1" || !hud.MarkerExists(matches[0]))
                throw new InvalidOperationException(name + " must be one live AShM-300 with a selectable HUD marker.");
            targets.Add(matches[0]);
        }

        // SelectUnit inserts at the front, so reverse selection leaves the native order 01..20.
        hud.DeselectAll();
        for (int i = targets.Count - 1; i >= 0; i--)
            hud.SelectUnit(targets[i]);
        if (!hud.GetTargetList().SequenceEqual(targets) || !own.weaponManager.GetTargetList().SequenceEqual(targets))
            throw new InvalidOperationException("The native HUD and weapon target lists did not retain all twenty tracks.");
    }

    public Dictionary<string, object> AgenticState()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        var targets = hud != null ? hud.GetTargetList() : new List<Unit>();
        string[] names = targets.Where(unit => unit != null).Select(unit => unit.UniqueName).ToArray();
        return new Dictionary<string, object>
        {
            ["selected_count"] = targets.Count,
            ["selected_distinct_count"] = names.Distinct(StringComparer.Ordinal).Count(),
            ["selected_names"] = string.Join(" ", names),
            ["selections"] = selections,
            ["error"] = error,
            ["mission_time_s"] = NetworkSceneSingleton<MissionManager>.i != null ? NetworkSceneSingleton<MissionManager>.i.MissionTime : -1f
        };
    }
}
