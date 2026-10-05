using System.Collections.Generic;
using BepInEx;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Baanish.Karambit.SalvoTest;

[BepInPlugin(PluginGuid, "Karambit terrain test ridge", "0.1.0")]
[BepInDependency("com.baanish.nuclearoption.agenticframework")]
public sealed class KarambitTerrainFixturePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "dev.baanish.karambit.terrain-test";
    private GameObject? ridge;
    private TerrainData? data;
    private float minimumClearance = float.PositiveInfinity, maximumGround;
    private bool crossed;

    private void Awake() => MissionManager.onMissionLoad += ResetFixture;

    private void ResetFixture(Mission? _)
    {
        if (ridge != null) Destroy(ridge);
        if (data != null) Destroy(data);
        ridge = null;
        data = null;
        minimumClearance = float.PositiveInfinity;
        maximumGround = 0f;
        crossed = false;
    }

    private void FixedUpdate()
    {
        MissionManager.onMissionLoad -= ResetFixture;
        MissionManager.onMissionLoad += ResetFixture;
        string? missionName = MissionManager.CurrentMission?.Name;
        bool narrow = missionName == "_agentic karambit-terrain-narrow";
        if ((!narrow && missionName != "_agentic karambit-terrain") || !MissionManager.IsRunning)
            return;
        if (ridge == null)
        {
            minimumClearance = float.PositiveInfinity;
            maximumGround = 0f;
            crossed = false;
            if (narrow)
            {
                ridge = new GameObject("Karambit 320 m narrow test obstacle") { isStatic = true };
                ridge.AddComponent<BoxCollider>().size = new Vector3(8000f, 320f, 25f);
            }
            else
            {
                data = new TerrainData { heightmapResolution = 65, size = new Vector3(8000f, 90f, 4000f) };
                var heights = new float[65, 65];
                for (int z = 0; z < 65; z++)
                    for (int x = 0; x < 65; x++)
                        heights[z, x] = Mathf.Sin(Mathf.PI * z / 64f);
                data.SetHeights(0, 0, heights);
                ridge = Terrain.CreateTerrainGameObject(data);
                ridge.name = "Karambit 90 m test ridge";
            }
            ridge.layer = PhysicsLayers.Statics;
            ridge.transform.SetParent(Datum.origin);
            ridge.transform.position = (narrow ? new GlobalPosition(-60000f, 160f, 3200f) :
                new GlobalPosition(-64000f, 0f, 7000f)).ToLocalPosition();
            Physics.SyncTransforms();
            Logger.LogInfo(narrow ? "Created isolated 320 m static box obstacle at z=3187.5..3212.5 m." :
                "Created isolated 90 m terrain collider ridge at z=7000..11000 m.");
        }
        foreach (Unit unit in UnitRegistry.allUnits)
        {
            if (unit is not Missile missile || missile.disabled || missile.definition.jsonKey != "baanish_karambit")
                continue;
            GlobalPosition position = missile.GlobalPosition();
            if (position.x < -64000f || position.x > -56000f)
                continue;
            float start = narrow ? 3187.5f : 7000f;
            float end = narrow ? 3212.5f : 11000f;
            if (position.z >= start && position.z <= end && Physics.Raycast(missile.transform.position, Vector3.down,
                out RaycastHit ground, 5000f, PhysicsLayers.StaticsMask))
            {
                minimumClearance = Mathf.Min(minimumClearance, ground.distance);
                maximumGround = Mathf.Max(maximumGround, ground.point.GlobalY());
            }
            if (position.z > (narrow ? 3250f : 11100f) && maximumGround > (narrow ? 319f : 85f))
                crossed = true;
        }
    }

    public Dictionary<string, object> AgenticState() => new()
    {
        ["ready"] = ridge != null,
        ["minimum_clearance_m"] = float.IsPositiveInfinity(minimumClearance) ? -1f : minimumClearance,
        ["maximum_ground_m"] = maximumGround,
        ["crossed"] = crossed
    };

    private void OnDestroy()
    {
        MissionManager.onMissionLoad -= ResetFixture;
        ResetFixture(MissionManager.CurrentMission);
    }
}
