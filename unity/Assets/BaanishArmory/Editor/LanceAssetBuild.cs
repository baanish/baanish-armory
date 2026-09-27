using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blueprinter;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaanishArmory.Editor
{
    // Authors the AGK-4 Lance from the stock AGR-24 Kingpin: copies of the Kingpin flight prefab, pod prefab,
    // unit definition, weapon info and 4-pod mount, re-pointed at the Lance meshes, icon and stats.
    // Meshes come from art/source/lance/export_lance_meshes.py (Blender), written to .local/lance-export.
    // Re-runnable: each run refreshes the copies from stock and keeps their GUIDs.
    public static class LanceAssetBuild
    {
        private const string ModFolder = "Assets/Blueprinter/Mods/baanish-armory";
        private const string Prefix = ModFolder + "/baanish_agk4_lance";
        private const string StencilPrefix = ModFolder + "/baanish_agk4_stencil";
        private const string XlIcon = ModFolder + "/baanish_eyeball_xl_icon.png";
        private const string XlHardpointOp = ModFolder + "/OpAddWeaponToHardpoint.asset";

        // Lance stats, as in docs/LANCE-CONCEPT.md.
        private const float Thrust = 28000, BurnTime = 5.15f, FuelMass = 16, RoundMass = 60, FinArea = 0.035f;
        private const float BlastYield = 2, Pierce = 3000, SeekerAngle = 5, Length = 3.92f, Diameter = 0.09f;
        // Fire armour as on the GPO-500: no ground laser (LADS, laser trailer, carrier) can burn through it.
        private const float FireArmor = 6, FireTolerance = 0.3f;
        private const float MaxSpeed = 2200, MinRange = 1000, MaxRange = 12000, LaunchArc = 4, Cost = 0.15f;
        // Follows the stock AGR rocket descriptions.
        private const string Description = "Long range 90mm kinetic rocket with a laser guidance package providing very limited maneuverability. " +
            "Its penetrator defeats heavy armor but carries little explosive, making it ineffective against naval targets.";

        // Stock hardpoint sets (by index) that carry the Lance pod. PrototypeBuild checks the manifest against this.
        internal static readonly (string aircraft, int[] sets)[] Hardpoints =
        {
            ("Multirole1", new[] { 5 }),        // KR-67 Ifrit: Outer Wing Pylons
            ("SmallFighter1", new[] { 3 }),     // FS-20 Vortex: Inner wing pylons
            ("Fighter1", new[] { 2 }),          // FS-12 Revoker: Wing Pylons
            ("CAS1", new[] { 2, 3 }),           // A-19 Brawler: Inner and Outer Fuselage Pylons
            ("AttackHelo1", new[] { 3, 4 }),    // SAH-46 Chicane: Left and Right Stub Tip Pylon
            ("UtilityHelo1", new[] { 0, 1 }),   // UH-90 Ibis: Left and Right Fuselage Pylon
            ("VTOLTrainer1", new[] { 0 }),      // VT-7 Vagrant: Center Pylon
            ("trainer", new[] { 1 }),           // T/A-30 Compass: Inner Wing Pylons
            ("COIN", new[] { 2 }),              // CI-22 Cricket: Fuselage Pylons
        };

        [Serializable] private sealed class Submesh { public int[] indices; }

        [Serializable]
        private sealed class Model
        {
            public string name;
            public float[] vertices;
            public float[] normals;
            public float[] uv;
            public Submesh[] submeshes;
        }

        [Serializable] private sealed class Seats { public float[] rounds; }

        private static string Stock(string relative) => BlueprinterSettings.GameAssetRootFolder + "/" + relative;

        [MenuItem("Baanish Armory/Author AGK-4 Lance")]
        public static void Author()
        {
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var export = Path.Combine(workspace, ".local", "lance-export");
            var art = Path.Combine(workspace, "art", "source", "lance");
            GameAssetSetup.InitializeGameTasks();

            var copies = new[]
            {
                ("GameObject/Rocket2_PLACEHOLDER.prefab", Prefix + ".prefab"),
                ("GameObject/Rocket2_4Pod_PLACEHOLDER.prefab", Prefix + "_4pod.prefab"),
                ("MonoBehaviour/Rocket2_PLACEHOLDER.asset", Prefix + "_definition.asset"),
                ("MonoBehaviour/info_rocket2_PLACEHOLDER.asset", Prefix + "_info.asset"),
                ("MonoBehaviour/Rocket2_4Pod_PLACEHOLDER.asset", Prefix + "_4pod.asset"),
                ("Material/Missiles4_PLACEHOLDER.mat", StencilPrefix + ".mat"),
            };
            foreach (var (source, target) in copies)
                RefreshCopy(Stock(source), target);

            var lance = ImportMesh(Path.Combine(export, "lance.json"), Prefix + "_mesh.asset");
            var folded = ImportMesh(Path.Combine(export, "lance_folded.json"), Prefix + "_folded_mesh.asset");
            var pod = ImportMesh(Path.Combine(export, "lance_4pod.json"), Prefix + "_4pod_mesh.asset");
            var icon = ImportIcon(Path.Combine(art, "agk4_lance_icon.png"), Prefix + "_icon.png");
            var stencil = AuthorStencilMaterial(Path.Combine(art, "agk4_stencil.png"));

            var remap = new Dictionary<Object, Object>
            {
                [Load<GameObject>(Stock("GameObject/Rocket2_PLACEHOLDER.prefab"))] = Load<GameObject>(Prefix + ".prefab"),
                [Load<GameObject>(Stock("GameObject/Rocket2_4Pod_PLACEHOLDER.prefab"))] = Load<GameObject>(Prefix + "_4pod.prefab"),
                [Load<Object>(Stock("MonoBehaviour/Rocket2_PLACEHOLDER.asset"))] = Load<Object>(Prefix + "_definition.asset"),
                [Load<Object>(Stock("MonoBehaviour/info_rocket2_PLACEHOLDER.asset"))] = Load<Object>(Prefix + "_info.asset"),
                [Load<Object>(Stock("MonoBehaviour/Rocket2_4Pod_PLACEHOLDER.asset"))] = Load<Object>(Prefix + "_4pod.asset"),
                [Load<Mesh>(Stock("Mesh/rocket2_PLACEHOLDER.asset"))] = lance,
                [Load<Mesh>(Stock("Mesh/rocket2_folded_PLACEHOLDER.asset"))] = folded,
                [Load<Mesh>(Stock("Mesh/rocket2_4launcher1_PLACEHOLDER.asset"))] = pod,
                [Load<Sprite>(Stock("Sprite/weaponIcon_rocket2_PLACEHOLDER.asset"))] = icon,
            };
            var body = Load<Material>(Stock("Material/Missiles4_PLACEHOLDER.mat"));
            var optics = Load<Material>(Stock("Material/Weapons5_PLACEHOLDER.mat"));

            EditAsset(Prefix + "_definition.asset", remap, so =>
            {
                Set(so, "jsonKey", "baanish_agk4_lance");
                Set(so, "unitName", "AGK-4 Lance");
                Set(so, "description", Description);
                Set(so, "length", Length);
                Set(so, "width", Diameter);
                Set(so, "height", Diameter);
                Set(so, "value", Cost);
                Set(so, "mass", RoundMass);
            });
            EditAsset(Prefix + "_info.asset", remap, so =>
            {
                Set(so, "weaponName", "AGK-4 Lance");
                Set(so, "shortName", "AGK-4");
                Set(so, "description", Description);
                Set(so, "maxSpeed", MaxSpeed);
                Set(so, "targetRequirements.minRange", MinRange);
                Set(so, "targetRequirements.maxRange", MaxRange);
                Set(so, "targetRequirements.minAlignment", LaunchArc);
                Set(so, "costPerRound", Cost);
                Set(so, "massPerRound", RoundMass);
            });
            EditAsset(Prefix + "_4pod.asset", remap, so =>
            {
                Set(so, "jsonKey", "baanish_agk4_lance_4pod");
                Set(so, "mountName", "AGK-4 Lance x4");
                Set(so, "ammo", 4);
                Set(so, "emptyMass", 95f);
                Set(so, "mass", 95f + 4 * RoundMass);
                Set(so, "drag", 0.08f);
                Set(so, "emptyDrag", 0.08f);
                Set(so, "RCS", 0.005f);
                Set(so, "emptyRCS", 0.005f);
            });

            EditPrefab(Prefix + ".prefab", remap, root =>
            {
                root.name = Path.GetFileNameWithoutExtension(Prefix);
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    var type = component.GetType().FullName;
                    var so = new SerializedObject(component);
                    if (type == "Missile")
                    {
                        Set(so, "motors.Array.data[0].thrust", Thrust);
                        Set(so, "motors.Array.data[0].burnTime", BurnTime);
                        Set(so, "motors.Array.data[0].fuelMass", FuelMass);
                        Set(so, "mass", RoundMass);
                        Set(so, "finArea", FinArea);
                        Set(so, "blastYield", BlastYield);
                        Set(so, "pierceDamage", Pierce);
                        Set(so, "armorProperties.fireArmor", FireArmor);
                        Set(so, "armorProperties.fireTolerance", FireTolerance);
                    }
                    else if (so.FindProperty("maxSeekerAngle") != null)
                        Set(so, "maxSeekerAngle", SeekerAngle);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (var capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
                {
                    capsule.radius = Diameter / 2;
                    capsule.height = Length;
                }
                // Exhaust effects and the missile camera sit near the ends; the round is twice as long.
                foreach (var child in root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform))
                    if (Mathf.Abs(child.localPosition.z) > 0.3f)
                        child.localPosition = Vector3.Scale(child.localPosition, new Vector3(1, 1, Length / 1.959f));
                SetMaterials(root, lance, body, optics);
            });
            var seats = JsonUtility.FromJson<Seats>(File.ReadAllText(Path.Combine(export, "seats.json"))).rounds;
            EditPrefab(Prefix + "_4pod.prefab", remap, root =>
            {
                root.name = Path.GetFileNameWithoutExtension(Prefix + "_4pod");
                // Seats are exported in the prefab root's space; the root sits at the origin while editing.
                for (var i = 0; i < 4; i++)
                {
                    var seat = root.transform.Find("pod/rocket" + (i + 1)) ?? throw new InvalidDataException("Missing pod seat rocket" + (i + 1));
                    seat.position = new Vector3(seats[i * 3], seats[i * 3 + 1], seats[i * 3 + 2]);
                }
                SetMaterials(root, folded, body, optics);
                SetMaterials(root, pod, body, stencil);
            });
            AuthorHardpointOp();
            AssetDatabase.SaveAssets();
            // The copied flight prefab still carries the Kingpin's network _prefabHash. Write the mod's own now, as
            // the Blueprinter build does, so the build doesn't rewrite the authored file.
            if (!PrefabHashWriter.Write(new[] { Prefix + ".prefab", Prefix + "_4pod.prefab" }))
                throw new InvalidDataException("Blueprinter could not write the Lance prefab hashes.");
            Debug.Log("[BaanishArmory] Authored AGK-4 Lance assets.");
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Missing asset: " + path);

        // Overwrites the file with the stock source but keeps an existing .meta, so the GUID survives re-runs.
        private static void RefreshCopy(string source, string target)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException("Missing stock placeholder: " + source);
            File.Copy(source, target, true);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Remap(SerializedObject so, Dictionary<Object, Object> remap)
        {
            var property = so.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                    remap.TryGetValue(property.objectReferenceValue, out var replacement))
                    property.objectReferenceValue = replacement;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EditAsset(string path, Dictionary<Object, Object> remap, Action<SerializedObject> edit)
        {
            var asset = Load<Object>(path);
            asset.name = Path.GetFileNameWithoutExtension(path);
            var so = new SerializedObject(asset);
            Remap(so, remap);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void EditPrefab(string path, Dictionary<Object, Object> remap, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null)
                        Remap(new SerializedObject(component), remap);
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetMaterials(GameObject root, Mesh mesh, params Material[] materials)
        {
            var found = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh == mesh))
            {
                filter.GetComponent<MeshRenderer>().sharedMaterials = materials;
                found++;
            }
            if (found == 0)
                throw new InvalidDataException("No renderer uses " + mesh.name + " in " + root.name);
        }

        private static void Set(SerializedObject so, string path, object value)
        {
            var property = so.FindProperty(path) ?? throw new InvalidDataException("Missing field " + path + " on " + so.targetObject.name);
            switch (value)
            {
                case string text: property.stringValue = text; break;
                case int number: property.intValue = number; break;
                case float number: property.floatValue = number; break;
                default: throw new ArgumentException("Unsupported value for " + path);
            }
        }

        private static Mesh ImportMesh(string jsonPath, string assetPath)
        {
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(jsonPath));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            var created = mesh == null;
            if (created)
                mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(assetPath);
            var count = model.vertices.Length / 3;
            mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Enumerable.Range(0, count).Select(i => new Vector3(model.vertices[i * 3], model.vertices[i * 3 + 1], model.vertices[i * 3 + 2])).ToList());
            mesh.SetNormals(Enumerable.Range(0, count).Select(i => new Vector3(model.normals[i * 3], model.normals[i * 3 + 1], model.normals[i * 3 + 2])).ToList());
            mesh.SetUVs(0, Enumerable.Range(0, count).Select(i => new Vector2(model.uv[i * 2], model.uv[i * 2 + 1])).ToList());
            mesh.subMeshCount = model.submeshes.Length;
            for (var i = 0; i < model.submeshes.Length; i++)
                mesh.SetTriangles(model.submeshes[i].indices, i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (created)
                AssetDatabase.CreateAsset(mesh, assetPath);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Sprite ImportIcon(string source, string target)
        {
            File.Copy(source, target, true);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            var reference = (TextureImporter)AssetImporter.GetAtPath(XlIcon);
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            var settings = new TextureImporterSettings();
            reference.ReadTextureSettings(settings);
            importer.SetTextureSettings(settings);
            importer.maxTextureSize = reference.maxTextureSize;
            importer.textureCompression = reference.textureCompression;
            importer.SaveAndReimport();
            return Load<Sprite>(target);
        }

        // The stencil decal uses the stock ColorableAttachment shader (copied from Missiles4) with its own small
        // maps: not metallic at the pod paint's smoothness, full AO, and no faction paint.
        private static Material AuthorStencilMaterial(string stencilSource)
        {
            File.Copy(stencilSource, StencilPrefix + ".png", true);
            AssetDatabase.ImportAsset(StencilPrefix + ".png", ImportAssetOptions.ForceSynchronousImport);
            var metallic = SolidTexture(StencilPrefix + "_metallic.png", new Color32(0, 0, 0, 125));
            var ao = SolidTexture(StencilPrefix + "_ao.png", new Color32(255, 255, 255, 255));
            var paint = SolidTexture(StencilPrefix + "_paint.png", new Color32(0, 0, 0, 0));
            var material = Load<Material>(StencilPrefix + ".mat");
            material.name = Path.GetFileNameWithoutExtension(StencilPrefix);
            material.SetTexture("_BaseColor", Load<Texture2D>(StencilPrefix + ".png"));
            material.SetTexture("_Metallic", metallic);
            material.SetTexture("_AO", ao);
            material.SetTexture("_Paint", paint);
            material.SetTexture("_Normal", null);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D SolidTexture(string path, Color32 colour)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.SetPixels32(Enumerable.Repeat(colour, 16).ToArray());
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return Load<Texture2D>(path);
        }

        private static void AuthorHardpointOp()
        {
            var path = ModFolder + "/OpAddWeaponToHardpoint_agk4_lance.asset";
            var op = AssetDatabase.LoadMainAssetAtPath(path);
            if (op == null)
            {
                op = ScriptableObject.CreateInstance(Load<Object>(XlHardpointOp).GetType());
                AssetDatabase.CreateAsset(op, path);
            }
            op.name = Path.GetFileNameWithoutExtension(path);
            var so = new SerializedObject(op);
            Set(so, "weaponJsonKey", "baanish_agk4_lance_4pod");
            var aircraft = so.FindProperty("aircraft");
            aircraft.arraySize = Hardpoints.Length;
            for (var i = 0; i < Hardpoints.Length; i++)
            {
                var entry = aircraft.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("aircraftJsonKey").stringValue = Hardpoints[i].aircraft;
                var indices = entry.FindPropertyRelative("hardpointIndices");
                indices.arraySize = Hardpoints[i].sets.Length;
                for (var j = 0; j < Hardpoints[i].sets.Length; j++)
                    indices.GetArrayElementAtIndex(j).intValue = Hardpoints[i].sets[j];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(op);
        }
    }
}
