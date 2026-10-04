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
    public static class KarambitAssetBuild
    {
        private const string ModName = "baanish-karambit-prototype";
        private const string ModFolder = "Assets/Blueprinter/Mods/" + ModName;
        private const string Prefix = ModFolder + "/baanish_karambit";
        private const string Version = "0.1.0";
        private const string WeaponName = "SAAM-18 Karambit";
        private const string Description = "Small active-radar interceptor for airborne targets. Prototype nominal envelope: 12 nautical miles. Requires the Karambit guidance plugin.";
        private const float Mass = 62.97749f, Cost = 0.5f, MaxRange = 22224f;
        private const float BoosterThrust = 8772.262f, SustainerThrust = 4879.1055f;
        private const float BoosterFuelMass = 8.772262f, SustainerFuelMass = 10.455226f;
        private const float TurnRate = 10f, ManeuverGLimit = 15f;
        private const float LaunchPitchDegrees = 5f;
        private const float ExternalTandemGap = .02f;
        private const float ExternalBodyClearance = .01f;
        private static readonly Color BandColor = new Color32(45, 83, 170, 255);
        private const float BandSmoothness = .2f;
        private static readonly Vector3 LengthScale = new Vector3(1f, 1f, 0.5f);
        private static readonly Quaternion LaunchRotation = Quaternion.Euler(LaunchPitchDegrees, 0, 0);

        private sealed class MountSpec
        {
            public readonly string key, stock;
            public readonly int rounds;
            public readonly bool internalMount;
            public readonly float drag, emptyDrag, rcs, emptyRcs;
            public readonly (string aircraft, int[] sets)[] hardpoints;

            public MountSpec(string key, string stock, int rounds, bool internalMount,
                float drag, float emptyDrag, float rcs, float emptyRcs,
                params (string aircraft, int[] sets)[] hardpoints)
            {
                this.key = key;
                this.stock = stock;
                this.rounds = rounds;
                this.internalMount = internalMount;
                this.drag = drag;
                this.emptyDrag = emptyDrag;
                this.rcs = rcs;
                this.emptyRcs = emptyRcs;
                this.hardpoints = hardpoints;
            }
        }

        // Two half-length rounds occupy each stock Scythe lane. External drag and
        // RCS scale per lane as an explicit prototype balance policy.
        private static readonly MountSpec[] Mounts =
        {
            new MountSpec("6_external", "AAM2_triple", 6, false, .525f, .18f, .09f, .045f,
                ("Fighter1", new[] { 2 })),
            new MountSpec("4_external", "AAM2_double", 4, false, .35f, .12f, .06f, .03f,
                ("SmallFighter1", new[] { 3 }), ("Multirole1", new[] { 4, 5 })),
            new MountSpec("2_external", "AAM2_single", 2, false, .175f, .06f, .03f, .015f,
                ("SmallFighter1", new[] { 3, 4 }), ("Multirole1", new[] { 4, 5 })),
            new MountSpec("4_internal", "AAM2_double_internal", 4, true, 0, 0, 0, 0,
                ("Fighter1", new[] { 1 })),
            new MountSpec("2_internal", "AAM2_single_internal", 2, true, 0, 0, 0, 0,
                ("SmallFighter1", new[] { 1, 2 })),
            new MountSpec("6_internal", "AAM2_triple_internal", 6, true, 0, 0, 0, 0,
                ("Multirole1", new[] { 1, 2 })),
        };

        [Serializable] private sealed class EncyclopediaPayload { public AssetRef[] entries; }
        [Serializable] private sealed class ModelSubmesh { public int[] indices; }
        [Serializable] private sealed class Model
        {
            public float[] vertices, normals, uv;
            public ModelSubmesh[] submeshes;
        }
        private static string Stock(string relative) => BlueprinterSettings.GameAssetRootFolder + "/" + relative;
        private static string MountPath(MountSpec mount) => Prefix + "_" + mount.key;

        [MenuItem("Baanish Armory/Author SAAM-18 Karambit prototype")]
        public static void Author()
        {
            GameAssetSetup.InitializeGameTasks();
            BlueprinterAssets.EnsureFolder(ModFolder);
            var stockInfo = new SerializedObject(Load<Object>(Stock("MonoBehaviour/AAM2_info_PLACEHOLDER.asset")));
            if (stockInfo.FindProperty("weaponName").stringValue != "AAM-29 Scythe")
                throw new InvalidDataException("The Karambit source must be the stock AAM-29 Scythe.");

            RefreshCopy("GameObject/AAM2_PLACEHOLDER.prefab", Prefix + ".prefab");
            RefreshCopy("MonoBehaviour/AAM2_PLACEHOLDER.asset", Prefix + "_definition.asset");
            RefreshCopy("MonoBehaviour/AAM2_info_PLACEHOLDER.asset", Prefix + "_info.asset");
            foreach (var mount in Mounts)
            {
                RefreshCopy("GameObject/" + mount.stock + "_PLACEHOLDER.prefab", MountPath(mount) + ".prefab");
                RefreshCopy("MonoBehaviour/" + mount.stock + "_PLACEHOLDER.asset", MountPath(mount) + ".asset");
            }
            var stockMesh = Load<Mesh>(Stock("Mesh/AAM2_PLACEHOLDER.asset"));
            var shortMesh = AuthorMesh();
            var bandMaterial = AuthorBandMaterial();
            var icon = AuthorIcon();
            var remap = new Dictionary<Object, Object>
            {
                [Load<GameObject>(Stock("GameObject/AAM2_PLACEHOLDER.prefab"))] = Load<GameObject>(Prefix + ".prefab"),
                [Load<Object>(Stock("MonoBehaviour/AAM2_PLACEHOLDER.asset"))] = Load<Object>(Prefix + "_definition.asset"),
                [Load<Object>(Stock("MonoBehaviour/AAM2_info_PLACEHOLDER.asset"))] = Load<Object>(Prefix + "_info.asset"),
                [stockMesh] = shortMesh,
                [Load<Sprite>(Stock("Sprite/weaponIcon_AAM2_PLACEHOLDER.asset"))] = icon,
            };
            foreach (var mount in Mounts)
            {
                remap[Load<GameObject>(Stock("GameObject/" + mount.stock + "_PLACEHOLDER.prefab"))] = Load<GameObject>(MountPath(mount) + ".prefab");
                remap[Load<Object>(Stock("MonoBehaviour/" + mount.stock + "_PLACEHOLDER.asset"))] = Load<Object>(MountPath(mount) + ".asset");
            }
            EditAsset(Prefix + "_definition.asset", remap, so =>
            {
                Set(so, "jsonKey", "baanish_karambit");
                Set(so, "unitName", WeaponName);
                Set(so, "description", Description);
                Set(so, "length", shortMesh.bounds.size.z);
                Set(so, "value", Cost);
                Set(so, "mass", Mass);
            });
            EditAsset(Prefix + "_info.asset", remap, so =>
            {
                Set(so, "weaponName", WeaponName);
                Set(so, "shortName", "SAAM-18");
                Set(so, "description", Description);
                Set(so, "targetRequirements.maxRange", MaxRange);
                Set(so, "costPerRound", Cost);
                Set(so, "massPerRound", Mass);
            });
            EditPrefab(Prefix + ".prefab", remap, root =>
            {
                root.GetComponent<MeshRenderer>().sharedMaterials = new[]
                {
                    Load<Material>(Stock("Material/Weapons4_PLACEHOLDER.mat")), bandMaterial,
                };
                // Bake the mesh scale; keeping the root at unit scale also keeps
                // multiplayer spawns correct when stock SendScale is disabled.
                var positions = root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform)
                    .Select(t => (transform: t, position: root.transform.InverseTransformPoint(t.position))).ToArray();
                foreach (var entry in positions)
                    entry.transform.position = root.transform.TransformPoint(Vector3.Scale(entry.position, LengthScale));
                ShortenColliders(root);
                foreach (var component in Components(root))
                {
                    var so = new SerializedObject(component);
                    if (component.GetType().FullName == "Missile")
                    {
                        if (so.FindProperty("motors").arraySize != 2)
                            throw new InvalidDataException("Expected the Scythe's two-stage motor.");
                        Set(so, "mass", Mass);
                        Set(so, "motors.Array.data[0].thrust", BoosterThrust);
                        Set(so, "motors.Array.data[0].burnTime", 2f);
                        Set(so, "motors.Array.data[1].thrust", SustainerThrust);
                        Set(so, "motors.Array.data[1].burnTime", 6f);
                        Set(so, "motors.Array.data[0].topSpeed", 850f);
                        Set(so, "motors.Array.data[1].topSpeed", 850f);
                        Set(so, "motors.Array.data[0].fuelMass", BoosterFuelMass);
                        Set(so, "motors.Array.data[1].fuelMass", SustainerFuelMass);
                        Set(so, "finArea", .15f);
                        Set(so, "maxTurnRate", TurnRate);
                        Set(so, "gLimit", ManeuverGLimit);
                        Set(so, "blastYield", 8f);
                    }
                    else if (component.GetType().FullName == "ARHSeeker")
                    {
                        Set(so, "loftAmount", 0f);
                        Set(so, "jamTolerance", .6f);
                        Set(so, "homeOnJam", false);
                        Set(so, "jinkEvasion.amount", 0f);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                root.GetComponent<Rigidbody>().mass = Mass;
            });
            foreach (var mount in Mounts)
            {
                var emptyMass = new SerializedObject(Load<Object>(Stock("MonoBehaviour/" + mount.stock + "_PLACEHOLDER.asset")))
                    .FindProperty("emptyMass").floatValue;
                EditAsset(MountPath(mount) + ".asset", remap, so =>
                {
                    Set(so, "jsonKey", "baanish_karambit_" + mount.key);
                    Set(so, "mountName", WeaponName + " x" + mount.rounds);
                    Set(so, "ammo", mount.rounds);
                    Set(so, "mass", emptyMass + mount.rounds * Mass);
                    Set(so, "drag", mount.drag);
                    Set(so, "emptyDrag", mount.emptyDrag);
                    Set(so, "RCS", mount.rcs);
                    Set(so, "emptyRCS", mount.emptyRcs);
                });
                EditPrefab(MountPath(mount) + ".prefab", remap, root => AuthorSeats(root, mount, stockMesh));
                AuthorHardpointOp(mount);
            }
            AssetDatabase.SaveAssets();
            if (!PrefabHashWriter.Write(new[] { Prefix + ".prefab" }.Concat(Mounts.Select(m => MountPath(m) + ".prefab")).ToArray()))
                throw new InvalidDataException("Blueprinter could not write the Karambit prefab hashes.");
            Validate();
            Debug.Log("[BaanishArmory] Authored SAAM-18 Karambit prototype assets.");
        }

        [MenuItem("Baanish Armory/Build SAAM-18 Karambit prototype")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Finish play mode or compilation before building.");
            GameAssetSetup.InitializeGameTasks();
            Validate();
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var output = Path.Combine(workspace, ".local", "builds", "karambit-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
            Directory.CreateDirectory(output);
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, kind) =>
            {
                if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                    errors.Add(message + "\n" + stack);
            };
            Application.logMessageReceived += capture;
            try
            {
                DependencyBundleBuild.Build(ModName, ModName, Version, output);
                if (errors.Count != 0)
                    throw new InvalidDataException("Karambit bundle build failed:\n" + string.Join("\n", errors));
                var bundle = Path.Combine(output, ModName + "_" + Version + ".nobp");
                if (!File.Exists(bundle) || new FileInfo(bundle).Length == 0)
                    throw new FileNotFoundException("Blueprinter did not produce the Karambit bundle.", bundle);
                SourceExporter.ExportMod(ModName, ModName, Version, Path.Combine(output, ModName + "_" + Version + ".source.zip"));
                var manifestPath = BlueprinterSettings.GeneratedFolder + "/patch_manifest.json";
                ValidateManifest(JsonUtility.FromJson<PatchManifest>(File.ReadAllText(manifestPath)));
                File.Copy(manifestPath, Path.Combine(output, "patch_manifest.json"));
                File.WriteAllText(Path.Combine(output, "asset-validation.txt"),
                    "SAAM-18 Karambit: reference-shaped 1.814801 m missile, 120 mm body, long strakes and compact tail fins.\n" +
                    "62.97749 kg wet, 43.75 kg dry; motors 8772.262 N/2 s and 4879.1055 N/6 s.\n" +
                    "Fuel 8.772262 kg booster and 10.455226 kg sustainer; stock exhaust speeds 2000/2800 m/s.\n" +
                    "Ideal stage delta-v 300 and 600 m/s; total 900 m/s.\n" +
                    "Stock aerodynamic curves; native motor cutoff at Mach 2.5 through runtime guidance.\n" +
                    "Fin .15; turn 10; g 15; blast 8; price .5 million; nominal range 22224 m.\n" +
                    "Revoker 20; Vortex 20; Ifrit 28. Internal drag/RCS zero.\n" +
                    "Every physical seat matches ammo and uses the authored missile info.\n" +
                    "Every neutral mounted launch axis is 5 degrees nose-down.\n" +
                    "External tandem lanes slope 5 degrees beneath one shared beam at the native aircraft pylon.\n" +
                    "External missile meshes clear the hardpoint plane by 10 mm; neutral internal asset centers are unchanged.\n" +
                    "Mounted body capsules clear each other before and after aircraft-relative pitch correction.\n" +
                    "Stock bay rails and doors retained. Flight profile/seeker supplied by runtime plugin.\n");
                Debug.Log("[BaanishArmory] Karambit build complete: " + output);
            }
            finally { Application.logMessageReceived -= capture; }
        }

        public static void AuthorAndBuild()
        {
            Author();
            Build();
        }

        private static Mesh AuthorMesh()
        {
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(Path.Combine(workspace, ".local", "karambit-art", "karambit.json")));
            var count = model.vertices.Length / 3;
            if (count < 100 || model.vertices.Length % 3 != 0 || model.normals.Length != count * 3 ||
                model.uv.Length != count * 2 || model.submeshes.Length != 2)
                throw new InvalidDataException("Invalid generated Karambit mesh.");
            var mesh = new Mesh { name = "baanish_karambit_mesh" };
            mesh.SetVertices(Enumerable.Range(0, count).Select(i => new Vector3(model.vertices[i * 3], model.vertices[i * 3 + 1], model.vertices[i * 3 + 2])).ToList());
            mesh.SetNormals(Enumerable.Range(0, count).Select(i => new Vector3(model.normals[i * 3], model.normals[i * 3 + 1], model.normals[i * 3 + 2])).ToList());
            mesh.SetUVs(0, Enumerable.Range(0, count).Select(i => new Vector2(model.uv[i * 2], model.uv[i * 2 + 1])).ToList());
            mesh.subMeshCount = model.submeshes.Length;
            for (var i = 0; i < model.submeshes.Length; i++)
                mesh.SetTriangles(model.submeshes[i].indices, i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return SaveMesh(mesh, Prefix + "_mesh.asset");
        }

        private static Material AuthorBandMaterial()
        {
            var path = Prefix + "_blue_band.mat";
            var paint = new Material(Load<Material>(Stock("Material/Weapons4_PLACEHOLDER.mat")))
            {
                name = "baanish_karambit_blue_band",
            };
            paint.SetColor("_BaseColor", BandColor);
            paint.SetColor("_Color", BandColor);
            paint.SetTexture("_MetallicGlossMap", null);
            paint.DisableKeyword("_METALLICSPECGLOSSMAP");
            paint.SetFloat("_Metallic", 0f);
            paint.SetFloat("_Smoothness", BandSmoothness);
            paint.SetTexture("_BumpMap", null);
            paint.DisableKeyword("_NORMALMAP");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
                AssetDatabase.CreateAsset(paint, path);
            else
            {
                EditorUtility.CopySerialized(paint, existing);
                Object.DestroyImmediate(paint);
                paint = existing;
                EditorUtility.SetDirty(paint);
            }
            return paint;
        }

        private static Sprite AuthorIcon()
        {
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var path = Prefix + "_icon.png";
            File.Copy(Path.Combine(workspace, ".local", "karambit-art", "karambit-icon.png"), path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return Load<Sprite>(path);
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
                AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        private static void AuthorSeats(GameObject root, MountSpec mount, Mesh stockMesh)
        {
            var seats = Components(root).Where(c => c.GetType().FullName == "MountedMissile").ToArray();
            if (seats.Length * 2 != mount.rounds)
                throw new InvalidDataException("Unexpected stock seat count for " + mount.stock);
            for (var i = 0; i < seats.Length; i++)
            {
                var front = seats[i];
                front.GetComponent<MeshRenderer>().sharedMaterials = new[]
                {
                    Load<Material>(Stock("Material/Weapons4_PLACEHOLDER.mat")), Load<Material>(Prefix + "_blue_band.mat"),
                };
                var transform = front.transform;
                var mountedRotation = mount.internalMount ? transform.rotation : root.transform.rotation * LaunchRotation;
                var frontCenter = SeatCenter(root.transform, transform, mount, stockMesh, true);
                var rearCenter = SeatCenter(root.transform, transform, mount, stockMesh, false);
                ShortenColliders(front.gameObject);
                var rear = Object.Instantiate(front.gameObject, transform.parent, false);
                front.name = "karambit_lane" + (i + 1) + "_front";
                rear.name = "karambit_lane" + (i + 1) + "_rear";
                transform.position = frontCenter;
                rear.transform.position = rearCenter;
                transform.rotation = mountedRotation;
                rear.transform.rotation = mountedRotation;
                SetPriority(front, 0);
                SetPriority(Components(rear).Single(c => c.GetType().FullName == "MountedMissile"), 10);
            }
            if (!mount.internalMount)
                AuthorExternalRack(root, mount);
            RenameDuplicateChildren(root.transform);
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!mount.internalMount)
                {
                    var renderers = root.GetComponentsInChildren<Renderer>(true);
                    var levels = lod.GetLODs();
                    for (var i = 0; i < levels.Length; i++) levels[i].renderers = renderers;
                    lod.SetLODs(levels);
                }
                lod.RecalculateBounds();
            }
        }

        private static Vector3 SeatCenter(Transform root, Transform source, MountSpec mount, Mesh stockMesh, bool front)
        {
            var separation = stockMesh.bounds.size.z * .25f + (mount.internalMount ? 0 : ExternalTandemGap * .5f);
            var offset = stockMesh.bounds.center.z * .5f + separation * (front ? 1 : -1);
            var vector = root.InverseTransformVector(source.TransformVector(new Vector3(0, 0, offset)));
            if (!mount.internalMount)
                vector = LaunchRotation * (Vector3.forward * separation * (front ? 1 : -1) - GetMissileCenterOfMass());
            var anchor = root.InverseTransformPoint(source.position);
            if (!mount.internalMount)
            {
                anchor.y = Mathf.Min(anchor.y, -Load<Mesh>(Prefix + "_mesh.asset").bounds.max.y -
                    ExternalBodyClearance - ExternalLaneDrop(stockMesh));
                anchor.z = 0;
            }
            return root.TransformPoint(anchor + vector);
        }

        private static Vector3 GetMissileCenterOfMass()
        {
            var missile = Load<GameObject>(Prefix + ".prefab");
            var capsule = missile.GetComponent<CapsuleCollider>();
            var solidColliders = missile.GetComponentsInChildren<Collider>(true).Where(collider => !collider.isTrigger).ToArray();
            // Automatic mass distribution is defined by the sole solid capsule, including its root offset.
            if (capsule == null || solidColliders.Length != 1 || solidColliders[0] != capsule ||
                !new SerializedObject(missile.GetComponent<Rigidbody>()).FindProperty("m_ImplicitCom").boolValue)
                throw new InvalidDataException("Karambit attachment balance requires automatic center of mass from its sole solid capsule.");
            return capsule.center;
        }

        private static float ExternalLaneDrop(Mesh stockMesh)
        {
            var mesh = Load<Mesh>(Prefix + "_mesh.asset");
            var rearOffset = -GetMissileCenterOfMass() - Vector3.forward * (stockMesh.bounds.size.z * .25f + ExternalTandemGap * .5f);
            return Mathf.Max(0, mesh.vertices.Max(vertex =>
                (LaunchRotation * (vertex + rearOffset)).y) - mesh.bounds.max.y);
        }

        private static void AuthorExternalRack(GameObject root, MountSpec mount)
        {
            var stock = Load<Mesh>(Stock("Mesh/launchpylon1_PLACEHOLDER.asset"));
            var rails = root.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh == stock).ToArray();
            Object.DestroyImmediate(root.GetComponent<MeshFilter>());
            var radians = LaunchPitchDegrees * Mathf.Deg2Rad;
            var massCenter = GetMissileCenterOfMass();
            Mesh mesh = null;
            for (var laneIndex = 0; laneIndex < rails.Length; laneIndex++)
            {
                var rail = rails[laneIndex];
                if (mount.stock == "AAM2_triple") mesh = null;
                if (mesh == null)
                {
                    var laneSeats = Components(mount.stock == "AAM2_triple" ? root : rail.gameObject)
                        .Where(component => component.GetType().FullName == "MountedMissile");
                    if (mount.stock == "AAM2_triple")
                    {
                        // Stock triple seats are root siblings; retaining that
                        // placement avoids shear from the rolled, nonuniform rails.
                        var front = laneSeats.Where(seat => seat.name.EndsWith("_front"))
                            .OrderBy(seat => Vector2.Distance(seat.transform.position, rail.transform.position)).First();
                        laneSeats = laneSeats.Where(seat => seat == front || seat.name == front.name.Replace("_front", "_rear"));
                    }
                    var centers = laneSeats
                        .Select(seat => root.transform.InverseTransformPoint(seat.transform.TransformPoint(massCenter))).ToArray();
                    if (centers.Length != 2)
                        throw new InvalidDataException("Each external rail must support exactly two Karambits: " + mount.key);
                    var rearZ = centers.Min(point => point.z);
                    var frontZ = centers.Max(point => point.z);
                    var lane = centers.Aggregate(Vector3.zero, (sum, point) => sum + point) / centers.Length;
                    var nativeVertices = stock.vertices;
                    var triangles = stock.triangles;
                    var normal = root.transform.InverseTransformDirection(rail.transform.up).normalized;
                    var width = root.transform.InverseTransformDirection(rail.transform.right).normalized;
                    var floors = new Dictionary<float, float>();
                    mesh = Object.Instantiate(stock);
                    mesh.name = "baanish_karambit_" + mount.key + "_pylon_mesh";
                    mesh.vertices = nativeVertices.Select(vertex =>
                    {
                        if (!floors.TryGetValue(vertex.z, out var floor))
                        {
                            MeshVerticalSpan(nativeVertices, triangles, 0,
                                Mathf.Clamp(vertex.z, stock.bounds.min.z + .000001f, stock.bounds.max.z - .000001f), out floor, out _);
                            floors.Add(vertex.z, floor);
                        }
                        var native = root.transform.InverseTransformPoint(rail.transform.TransformPoint(vertex));
                        var nativeBottom = root.transform.InverseTransformPoint(rail.transform.TransformPoint(new Vector3(0, floor, vertex.z))).y;
                        var weight = Mathf.Clamp01((vertex.y - floor) / (stock.bounds.max.y - floor));
                        native.z = Mathf.Lerp(rearZ, frontZ, Mathf.InverseLerp(stock.bounds.min.z, stock.bounds.max.z, vertex.z));
                        if (mount.stock == "AAM2_triple")
                        {
                            var contact = lane - Vector3.up * (Mathf.Tan(radians) * native.z) +
                                normal * (.056f / Mathf.Sqrt(1 - normal.y * normal.y * Mathf.Sin(radians) * Mathf.Sin(radians)));
                            var underside = root.transform.InverseTransformPoint(rail.transform.TransformPoint(new Vector3(0, floor, vertex.z)));
                            var correction = contact - underside;
                            correction.z = 0;
                            native -= width * (Vector3.Dot(native - underside, width) * .55f);
                            native += correction * (1 - weight);
                        }
                        else
                        {
                            native.x = lane.x + (native.x - lane.x) * .45f;
                            var bottom = lane.y + .056f / Mathf.Cos(radians) - Mathf.Tan(radians) * native.z;
                            // Keep the stock upper pins and tapered adapter. Only the
                            // rail underside follows the smaller tandem bodies.
                            native.y = Mathf.Min(0, Mathf.Min(0, native.y) + (bottom - nativeBottom) * (1 - weight));
                        }
                        return rail.transform.InverseTransformPoint(root.transform.TransformPoint(native));
                    }).ToArray();
                    mesh.RecalculateBounds();
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    mesh = SaveMesh(mesh, ExternalRailMeshPath(mount, laneIndex));
                }
                rail.sharedMesh = mesh;
                foreach (var collider in rail.GetComponents<Collider>()) Object.DestroyImmediate(collider);
                var convex = rail.gameObject.AddComponent<MeshCollider>();
                convex.sharedMesh = mesh;
                convex.convex = true;
            }
        }

        private static string ExternalRailMeshPath(MountSpec mount, int laneIndex) =>
            MountPath(mount) + (mount.stock == "AAM2_triple" ? "_pylon_lane" + (laneIndex + 1) : "_pylon") + "_mesh.asset";

        private static void SetPriority(Component seat, int priority)
        {
            // Fire all front rounds before the tandem rounds behind them, so an
            // external missile never launches through a loaded forward round.
            var so = new SerializedObject(seat);
            Set(so, "priority", priority);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ShortenColliders(GameObject root)
        {
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                box.center = Vector3.Scale(box.center, LengthScale);
                box.size = Vector3.Scale(box.size, LengthScale);
            }
            foreach (var capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
            {
                if (capsule.direction != 2)
                    throw new InvalidDataException("Expected a longitudinal Scythe capsule.");
                var mesh = Load<Mesh>(Prefix + "_mesh.asset");
                capsule.center = mesh.bounds.center;
                capsule.height = mesh.bounds.size.z;
                capsule.radius = .06f;
            }
        }

        private static void RenameDuplicateChildren(Transform root)
        {
            var names = new HashSet<string>();
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!names.Add(child.name))
                {
                    child.name += "_" + i;
                    if (!names.Add(child.name))
                        throw new InvalidDataException("Ambiguous mount hierarchy: " + child.name);
                }
                RenameDuplicateChildren(child);
            }
        }

        private static void AuthorHardpointOp(MountSpec mount)
        {
            var path = ModFolder + "/OpAddWeaponToHardpoint_" + mount.key + ".asset";
            var op = AssetDatabase.LoadAssetAtPath<OpAddWeaponToHardpoint>(path);
            if (op == null)
            {
                op = ScriptableObject.CreateInstance<OpAddWeaponToHardpoint>();
                AssetDatabase.CreateAsset(op, path);
            }
            op.weaponJsonKey = "baanish_karambit_" + mount.key;
            op.aircraft = mount.hardpoints.Select(h => new OpAddWeaponToHardpoint.AircraftTarget
            {
                aircraftJsonKey = h.aircraft,
                hardpointIndices = h.sets.ToList(),
            }).ToList();
            EditorUtility.SetDirty(op);
        }

        private static void Validate()
        {
            var stockMesh = Load<Mesh>(Stock("Mesh/AAM2_PLACEHOLDER.asset"));
            var mesh = Load<Mesh>(Prefix + "_mesh.asset");
            if (Mathf.Abs(mesh.bounds.size.z - stockMesh.bounds.size.z * .5f) > .00001f ||
                Mathf.Abs(mesh.bounds.center.z - stockMesh.bounds.center.z * .5f) > .00001f ||
                mesh.bounds.size.x > stockMesh.bounds.size.x || mesh.bounds.size.y > stockMesh.bounds.size.y || mesh.subMeshCount != 2)
                throw new InvalidDataException("Reference-shaped Karambit must retain its half-Scythe length and fit the stock carriage envelope.");
            var bodyMaterial = Load<Material>(Stock("Material/Weapons4_PLACEHOLDER.mat"));
            var bandMaterial = Load<Material>(Prefix + "_blue_band.mat");
            if (bandMaterial.shader != bodyMaterial.shader || bandMaterial.GetColor("_BaseColor") != BandColor ||
                new[] { "_BaseMap", "_OcclusionMap" }.Any(property =>
                    bandMaterial.GetTexture(property) != bodyMaterial.GetTexture(property)) ||
                new[] { "_MetallicGlossMap", "_BumpMap" }.Any(property => bandMaterial.GetTexture(property) != null) ||
                bandMaterial.IsKeywordEnabled("_METALLICSPECGLOSSMAP") || bandMaterial.IsKeywordEnabled("_NORMALMAP") ||
                bandMaterial.GetFloat("_Metallic") != 0f || bandMaterial.GetFloat("_Smoothness") != BandSmoothness)
                throw new InvalidDataException("Blue bands must be nonmetallic paint with smoothness 0.2, stock shader, albedo and occlusion.");
            var missileRenderer = Load<GameObject>(Prefix + ".prefab").GetComponent<MeshRenderer>();
            if (!missileRenderer.sharedMaterials.SequenceEqual(new[] { bodyMaterial, bandMaterial }) ||
                new SerializedObject(Load<Object>(Prefix + "_info.asset")).FindProperty("weaponIcon").objectReferenceValue != Load<Sprite>(Prefix + "_icon.png"))
                throw new InvalidDataException("Karambit must use the stock Weapons4 material and matching authored silhouette.");
            RequireFloat(new SerializedObject(Load<Object>(Prefix + "_info.asset")), "targetRequirements.maxRange", MaxRange);
            RequireFloat(new SerializedObject(Load<Object>(Prefix + "_info.asset")), "massPerRound", Mass);
            RequireFloat(new SerializedObject(Load<Object>(Prefix + "_definition.asset")), "mass", Mass);
            var missile = Components(Load<GameObject>(Prefix + ".prefab")).Single(c => c.GetType().FullName == "Missile");
            RequireFloat(new SerializedObject(missile.GetComponent<Rigidbody>()), "m_Mass", Mass);
            var so = new SerializedObject(missile);
            foreach (var value in new[]
            {
                ("mass", Mass), ("finArea", .15f), ("maxTurnRate", TurnRate), ("gLimit", ManeuverGLimit), ("blastYield", 8f),
                ("motors.Array.data[0].thrust", BoosterThrust), ("motors.Array.data[0].burnTime", 2f),
                ("motors.Array.data[1].thrust", SustainerThrust), ("motors.Array.data[1].burnTime", 6f),
                ("motors.Array.data[0].topSpeed", 850f), ("motors.Array.data[1].topSpeed", 850f),
                ("motors.Array.data[0].fuelMass", BoosterFuelMass), ("motors.Array.data[1].fuelMass", SustainerFuelMass),
            }) RequireFloat(so, value.Item1, value.Item2);
            var stockMissile = Components(Load<GameObject>(Stock("GameObject/AAM2_PLACEHOLDER.prefab"))).Single(c => c.GetType().FullName == "Missile");
            var stockDrag = new SerializedObject(stockMissile).FindProperty("dragCurve").animationCurveValue.keys;
            var authoredDrag = so.FindProperty("dragCurve").animationCurveValue.keys;
            if (!authoredDrag.SequenceEqual(stockDrag))
                throw new InvalidDataException("Karambit must retain the stock aerodynamic drag curve.");
            // Allocate 300/600 m/s at Scythe's exhaust speeds while retaining 43.75 kg of dry hardware.
            double DeltaV(double thrust, double seconds, double fuelMass, double wetMass) =>
                thrust * seconds / fuelMass * Math.Log(wetMass / (wetMass - fuelMass));
            var boosterDeltaV = DeltaV(BoosterThrust, 2, BoosterFuelMass, Mass);
            var sustainerDeltaV = DeltaV(SustainerThrust, 6, SustainerFuelMass, (double)Mass - BoosterFuelMass);
            if (Math.Abs(boosterDeltaV - 300) > .0001 || Math.Abs(sustainerDeltaV - 600) > .0001 ||
                Math.Abs(boosterDeltaV + sustainerDeltaV - 900) > .0001 ||
                Math.Abs((double)Mass - BoosterFuelMass - SustainerFuelMass - 43.75) > .0001 ||
                Math.Abs((double)BoosterThrust * 2 / BoosterFuelMass - 2000) > .0001 ||
                Math.Abs((double)SustainerThrust * 6 / SustainerFuelMass - 2800) > .0001)
                throw new InvalidDataException("Motor balance must retain 43.75 kg dry mass and stock 2000/2800 m/s exhaust speeds for 300/600 m/s ideal delta-v.");
            var seeker = new SerializedObject(Components(missile.gameObject).Single(c => c.GetType().FullName == "ARHSeeker"));
            RequireFloat(seeker, "loftAmount", 0);
            RequireFloat(seeker, "jamTolerance", .6f);
            if (seeker.FindProperty("homeOnJam").boolValue)
                throw new InvalidDataException("Karambit must not home on jammers.");
            var capacities = new Dictionary<(string aircraft, int set), int>();
            foreach (var mount in Mounts)
            {
                var operation = Load<OpAddWeaponToHardpoint>(ModFolder + "/OpAddWeaponToHardpoint_" + mount.key + ".asset");
                if (operation.weaponJsonKey != "baanish_karambit_" + mount.key || operation.aircraft.Count != mount.hardpoints.Length ||
                    mount.hardpoints.Any(target => !operation.aircraft.Any(actual => actual.aircraftJsonKey == target.aircraft &&
                        actual.hardpointIndices.SequenceEqual(target.sets))))
                    throw new InvalidDataException("Unexpected Karambit hardpoint registration: " + mount.key);
                var asset = new SerializedObject(Load<Object>(MountPath(mount) + ".asset"));
                if (asset.FindProperty("ammo").intValue != mount.rounds || asset.FindProperty("missileBay").boolValue != mount.internalMount)
                    throw new InvalidDataException("Wrong ammunition or bay identity: " + mount.key);
                RequireFloat(asset, "drag", mount.drag);
                RequireFloat(asset, "emptyDrag", mount.emptyDrag);
                RequireFloat(asset, "RCS", mount.rcs);
                RequireFloat(asset, "emptyRCS", mount.emptyRcs);
                RequireFloat(asset, "mass", asset.FindProperty("emptyMass").floatValue + mount.rounds * Mass);
                var prefab = Load<GameObject>(MountPath(mount) + ".prefab");
                var seats = Components(prefab).Where(c => c.GetType().FullName == "MountedMissile").ToArray();
                if (seats.Length != mount.rounds || seats.Any(seat => new SerializedObject(seat).FindProperty("info").objectReferenceValue != Load<Object>(Prefix + "_info.asset")))
                    throw new InvalidDataException("Physical launcher count or info mismatch: " + mount.key);
                var stockSeats = Components(Load<GameObject>(Stock("GameObject/" + mount.stock + "_PLACEHOLDER.prefab")))
                    .Where(c => c.GetType().FullName == "MountedMissile").ToArray();
                if (stockSeats.Length * 2 != mount.rounds)
                    throw new InvalidDataException("Unexpected stock Scythe lane count: " + mount.stock);
                var capsule = missile.GetComponent<CapsuleCollider>();
                ValidateSeatPackaging(prefab, capsule, mount.key + " neutral");
                for (var lane = 0; lane < stockSeats.Length; lane++)
                foreach (var suffix in new[] { "front", "rear" })
                {
                    var component = seats.Single(c => c.name == "karambit_lane" + (lane + 1) + "_" + suffix);
                    var seat = new SerializedObject(component);
                    var original = new SerializedObject(stockSeats[lane]);
                    var originalTransform = stockSeats[lane].transform;
                    var expectedCenter = SeatCenter(originalTransform.root, originalTransform, mount, stockMesh, suffix == "front");
                    expectedCenter = originalTransform.root.InverseTransformPoint(expectedCenter);
                    if (Vector3.Distance(prefab.transform.InverseTransformPoint(component.transform.position), expectedCenter) > .00001f)
                        throw new InvalidDataException("Unexpected " + (mount.internalMount ? "compact" : "sloped") + " tandem center: " + mount.key + "/" + component.name);
                    var expectedRotation = mount.internalMount
                        ? Quaternion.Inverse(originalTransform.root.rotation) * originalTransform.rotation
                        : LaunchRotation;
                    if (Quaternion.Angle(Quaternion.Inverse(prefab.transform.rotation) * component.transform.rotation, expectedRotation) > .001f)
                        throw new InvalidDataException("Mounted axis must follow " + (mount.internalMount ? "the stock bay rail" : "the 5-degree external beam") + ": " + mount.key + "/" + component.name);
                    if (mount.stock == "AAM2_triple" && new[] { Vector3.right, Vector3.up, Vector3.forward }.Any(axis =>
                        Vector3.Distance(prefab.transform.InverseTransformVector(component.transform.TransformVector(axis)), LaunchRotation * axis) > .00001f))
                        throw new InvalidDataException("The rendered six-round missile axes must retain unit scale and 5-degree pitch: " + component.name);
                    if (component.GetComponent<MeshFilter>().sharedMesh != mesh)
                        throw new InvalidDataException("Mounted rounds must use the authored Karambit mesh: " + mount.key);
                    if (!component.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(new[] { bodyMaterial, bandMaterial }))
                        throw new InvalidDataException("Mounted Karambit must use the stock Weapons4 material: " + mount.key);
                    foreach (var field in new[] { "railLength", "railSpeed", "railDelay", "doorOpenDuration" })
                        RequireFloat(seat, field, original.FindProperty(field).floatValue);
                    if (seat.FindProperty("railDirection").intValue != original.FindProperty("railDirection").intValue)
                        throw new InvalidDataException("Stock launch direction changed: " + mount.key);
                }
                if (!mount.internalMount)
                    ValidateExternalLanes(prefab, stockSeats, mesh, mount);
                var paths = prefab.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, prefab.transform)).ToArray();
                if (paths.Distinct().Count() != paths.Length)
                    throw new InvalidDataException("Ambiguous Blueprinter hierarchy: " + mount.key);
                foreach (var target in mount.hardpoints)
                {
                    var aircraft = Load<GameObject>(Stock("GameObject/" + target.aircraft + "_PLACEHOLDER.prefab"));
                    var manager = Components(aircraft).Single(c => c.GetType().FullName == "WeaponManager");
                    var sets = new SerializedObject(manager).FindProperty("hardpointSets");
                    foreach (var index in target.sets)
                    {
                        var set = sets.GetArrayElementAtIndex(index);
                        var options = set.FindPropertyRelative("weaponOptions");
                        var sources = (mount.internalMount || mount.stock == "AAM2_triple" ? new[] { mount.stock } : new[] { "AAM2_single", "AAM2_double" })
                            .Select(key => Load<Object>(Stock("MonoBehaviour/" + key + "_PLACEHOLDER.asset"))).ToArray();
                        if (!Enumerable.Range(0, options.arraySize).Any(i => sources.Contains(options.GetArrayElementAtIndex(i).objectReferenceValue)))
                            throw new InvalidDataException("Hardpoint does not carry source Scythe mount: " + target.aircraft + "/" + index);
                        var hardpoints = set.FindPropertyRelative("hardpoints");
                        for (var i = 0; i < hardpoints.arraySize; i++)
                        {
                            var hardpoint = (Transform)hardpoints.GetArrayElementAtIndex(i).FindPropertyRelative("transform").objectReferenceValue;
                            ValidateAircraftPitch(prefab, aircraft.transform, hardpoint, capsule, !mount.internalMount,
                                target.aircraft + "/" + index + "/" + i);
                        }
                        var station = (target.aircraft, index);
                        capacities.TryGetValue(station, out var count);
                        capacities[station] = Mathf.Max(count, mount.rounds * hardpoints.arraySize);
                    }
                }
            }
            var totals = capacities.GroupBy(entry => entry.Key.aircraft).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Value));
            if (totals["Fighter1"] != 20 || totals["SmallFighter1"] != 20 || totals["Multirole1"] != 28)
                throw new InvalidDataException("Expected Revoker 20, Vortex 20 and Ifrit 28 rounds.");
            var revoker = Mounts.SelectMany(mount => mount.hardpoints.Where(target => target.aircraft == "Fighter1")
                .SelectMany(target => target.sets.Select(set => (mount.key, set)))).ToArray();
            if (revoker.Length != 2 || !revoker.Contains(("4_internal", 1)) || !revoker.Contains(("6_external", 2)) ||
                capacities[("Fighter1", 1)] != 8 || capacities[("Fighter1", 2)] != 12)
                throw new InvalidDataException("Revoker must carry 8 internal and 12 wing Karambits, with no wingtip option.");
        }

        private static void ValidateExternalLanes(GameObject prefab, Component[] originals, Mesh missileMesh, MountSpec mount)
        {
            var seats = Components(prefab).Where(c => c.GetType().FullName == "MountedMissile").ToArray();
            var railMeshes = Enumerable.Range(0, mount.stock == "AAM2_triple" ? 3 : 1)
                .Select(lane => Load<Mesh>(ExternalRailMeshPath(mount, lane))).ToArray();
            var supports = prefab.GetComponentsInChildren<MeshFilter>(true).Where(filter =>
                !seats.Any(seat => seat.transform == filter.transform)).ToArray();
            var native = Load<Mesh>(Stock("Mesh/launchpylon1_PLACEHOLDER.asset"));
            var adapter = Load<Mesh>(Stock("Mesh/pylon_double1_PLACEHOLDER.asset"));
            var nativePrefab = Load<GameObject>(Stock("GameObject/" + mount.stock + "_PLACEHOLDER.prefab"));
            var nativeRails = nativePrefab.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh == native).ToArray();
            var adapterCount = originals.Length > 1 ? 1 : 0;
            if (nativeRails.Length != mount.rounds / 2 ||
                supports.Length != originals.Length + adapterCount ||
                supports.Count(filter => railMeshes.Contains(filter.sharedMesh)) != originals.Length ||
                supports.Count(filter => filter.sharedMesh == adapter) != adapterCount ||
                supports.Any(filter => filter.GetComponent<MeshRenderer>() == null ||
                    !filter.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(new[] { Load<Material>(Stock("Material/Missiles1_PLACEHOLDER.mat")) })) ||
                native.vertexCount != 848 || native.triangles.Length != 1944 || adapter.vertexCount != 620 || adapter.triangles.Length != 1848 ||
                railMeshes.Any(mesh => !mesh.triangles.SequenceEqual(native.triangles) || !mesh.uv.SequenceEqual(native.uv) || mesh.vertexCount != native.vertexCount) ||
                supports.Sum(filter => filter.sharedMesh.triangles.Length / 3) != (originals.Length == 3 ? 2560 : originals.Length == 2 ? 1912 : 648))
                throw new InvalidDataException("External rack must retain the native tapered adapter, per-lane rails, UVs, materials and triangle budget: " + mount.key);
            for (var i = 0; i < nativeRails.Length; i++)
            {
                var actual = supports.Where(filter => railMeshes.Contains(filter.sharedMesh)).ElementAt(i).transform;
                var original = nativeRails[i].transform;
                if (actual.localPosition != original.localPosition || actual.localRotation != original.localRotation || actual.localScale != original.localScale ||
                    supports.Where(filter => railMeshes.Contains(filter.sharedMesh)).ElementAt(i).sharedMesh != railMeshes[originals.Length == 3 ? i : 0])
                    throw new InvalidDataException("Native rail transforms and lane meshes must remain matched: " + mount.key);
            }
            if (adapterCount != 0)
            {
                var actual = supports.Single(filter => filter.sharedMesh == adapter).transform;
                var stockAdapter = Load<GameObject>(Stock("GameObject/" + mount.stock + "_PLACEHOLDER.prefab"))
                    .GetComponentsInChildren<MeshFilter>(true).Single(filter => filter.sharedMesh == adapter).transform;
                if (actual.localPosition != stockAdapter.localPosition || actual.localRotation != stockAdapter.localRotation || actual.localScale != stockAdapter.localScale)
                    throw new InvalidDataException("The native adapter must remain unchanged: " + mount.key);
            }
            var supportGeometry = supports.Select(filter => (vertices: filter.sharedMesh.vertices.Select(vertex =>
                prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex))).ToArray(), triangles: filter.sharedMesh.triangles)).ToArray();
            if (supportGeometry.SelectMany(part => part.vertices).Max(vertex => vertex.y) > .00002f)
                throw new InvalidDataException("External support must meet the native aircraft pylon without extending above its attachment plane: " + mount.key);
            var authoredVertices = missileMesh.vertices;
            var finTriangles = missileMesh.triangles.Select((index, i) => (index, triangle: i / 3))
                .GroupBy(entry => entry.triangle).Where(triangle => triangle.Any(entry =>
                {
                    var vertex = authoredVertices[entry.index];
                    return vertex.x * vertex.x + vertex.y * vertex.y > .061f * .061f;
                })).SelectMany(triangle => triangle.Select(entry => entry.index)).ToArray();
            var massCenter = GetMissileCenterOfMass();
            for (var lane = 0; lane < originals.Length; lane++)
            {
                var front = seats.Single(c => c.name == "karambit_lane" + (lane + 1) + "_front");
                var rear = seats.Single(c => c.name == "karambit_lane" + (lane + 1) + "_rear");
                var frontMass = prefab.transform.InverseTransformPoint(front.transform.TransformPoint(massCenter));
                var rearMass = prefab.transform.InverseTransformPoint(rear.transform.TransformPoint(massCenter));
                var rail = mount.stock == "AAM2_triple"
                    ? supports.Where(filter => railMeshes.Contains(filter.sharedMesh))
                        .OrderBy(filter => Vector2.Distance(front.transform.position, filter.transform.position)).First()
                    : front.transform.parent.GetComponent<MeshFilter>();
                if (rail == null || !railMeshes.Contains(rail.sharedMesh) ||
                    (mount.stock == "AAM2_triple"
                        ? front.transform.parent != prefab.transform || rear.transform.parent != prefab.transform ||
                            front.transform.localScale != Vector3.one || rear.transform.localScale != Vector3.one
                        : rear.transform.parent != rail.transform) ||
                    rail.GetComponent<MeshCollider>() == null || !rail.GetComponent<MeshCollider>().convex || rail.GetComponent<MeshCollider>().sharedMesh != rail.sharedMesh)
                    throw new InvalidDataException("Each tandem lane must keep its own native rail and convex collider: " + mount.key);
                var railMesh = rail.sharedMesh;
                var railVertices = railMesh.vertices.Select(vertex => prefab.transform.InverseTransformPoint(rail.transform.TransformPoint(vertex))).ToArray();
                if (Mathf.Abs(frontMass.z - railVertices.Max(vertex => vertex.z)) > .00001f ||
                    Mathf.Abs(rearMass.z - railVertices.Min(vertex => vertex.z)) > .00001f ||
                    Mathf.Abs((frontMass.z + rearMass.z) * .5f) > .00001f)
                    throw new InvalidDataException("External rail ends must align with the two missile centers of mass: " + mount.key);
                var direction = prefab.transform.InverseTransformVector(front.transform.position - rear.transform.position).normalized;
                if (Vector3.Distance(direction, LaunchRotation * Vector3.forward) > .00001f ||
                    frontMass.y >= rearMass.y ||
                    new SerializedObject(front).FindProperty("priority").intValue != 0 ||
                    new SerializedObject(rear).FindProperty("priority").intValue != 10)
                    throw new InvalidDataException("External tandem lane must slope 5 degrees with front firing first: " + mount.key);
                var top = new[] { front.transform, rear.transform }.SelectMany(seat => missileMesh.vertices.Select(vertex =>
                    prefab.transform.InverseTransformPoint(seat.TransformPoint(vertex)).y)).Max();
                if (top > -ExternalBodyClearance + .00001f)
                    throw new InvalidDataException("External missile meshes must clear the hardpoint plane by 10 mm: " + mount.key);
                foreach (var seat in new[] { front, rear })
                {
                    var center = prefab.transform.InverseTransformPoint(seat.transform.TransformPoint(massCenter));
                    var missileVertices = missileMesh.vertices.Select(vertex => prefab.transform.InverseTransformPoint(seat.transform.TransformPoint(vertex))).ToArray();
                    var contactRotation = Quaternion.FromToRotation(prefab.transform.InverseTransformDirection(rail.transform.up), Vector3.up);
                    var contactCenter = contactRotation * center;
                    MeshVerticalSpan(railVertices.Select(vertex => contactRotation * vertex).ToArray(), railMesh.triangles,
                        contactCenter.x, contactCenter.z - Mathf.Sign(contactCenter.z) * .000001f, out var underside, out _);
                    MeshVerticalSpan(missileVertices.Select(vertex => contactRotation * vertex).ToArray(), missileMesh.triangles,
                        contactCenter.x, contactCenter.z, out _, out var bodyTop);
                    if (bodyTop - underside < .002f || bodyTop - underside > .006f)
                        throw new InvalidDataException("Native rail must contact the rendered body at each COM end with 2-6 mm overlap: " + mount.key + "/" + seat.name);
                    foreach (var support in supportGeometry)
                        ValidateFinClearance(support.vertices, support.triangles, missileVertices, finTriangles, mount.key + "/" + seat.name);
                }
            }
            Debug.Log("[BaanishArmory] Karambit " + mount.key + ": native adapter and shaped rails; rack triangles=" +
                supports.Sum(filter => filter.sharedMesh.triangles.Length / 3) + "; tandem gap=" + ExternalTandemGap + "; minimum fin clearance=0.003 m.");
        }

        private static void MeshVerticalSpan(Vector3[] vertices, int[] triangles, float x, float z, out float bottom, out float top)
        {
            bottom = float.PositiveInfinity;
            top = float.NegativeInfinity;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var ab = vertices[triangles[i + 1]] - a;
                var ac = vertices[triangles[i + 2]] - a;
                var determinant = ab.x * ac.z - ab.z * ac.x;
                if (Mathf.Abs(determinant) < .000000000001f) continue;
                var u = ((x - a.x) * ac.z - (z - a.z) * ac.x) / determinant;
                var v = (ab.x * (z - a.z) - ab.z * (x - a.x)) / determinant;
                if (u < -.000001f || v < -.000001f || u + v > 1.000001f) continue;
                var y = a.y + u * ab.y + v * ac.y;
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }
            if (float.IsInfinity(bottom)) throw new InvalidDataException("No rendered surface at the rail contact section.");
        }

        private static void ValidateFinClearance(Vector3[] supports, int[] supportTriangles, Vector3[] missile, int[] finTriangles, string label)
        {
            for (var i = 0; i < supportTriangles.Length; i += 3)
            {
                var a = supports[supportTriangles[i]];
                var b = supports[supportTriangles[i + 1]];
                var c = supports[supportTriangles[i + 2]];
                var bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                bounds.Expand(.006f);
                for (var j = 0; j < finTriangles.Length; j += 3)
                {
                    var d = missile[finTriangles[j]];
                    var e = missile[finTriangles[j + 1]];
                    var f = missile[finTriangles[j + 2]];
                    var other = new Bounds(d, Vector3.zero);
                    other.Encapsulate(e);
                    other.Encapsulate(f);
                    if (bounds.Intersects(other) &&
                        Mathf.Min(SegmentTriangleDistanceSquared(a, b, d, e, f), SegmentTriangleDistanceSquared(b, c, d, e, f),
                            SegmentTriangleDistanceSquared(c, a, d, e, f), SegmentTriangleDistanceSquared(d, e, a, b, c),
                            SegmentTriangleDistanceSquared(e, f, a, b, c), SegmentTriangleDistanceSquared(f, d, a, b, c)) < .003f * .003f)
                        throw new InvalidDataException("Native support must retain at least 3 mm of actual rendered fin clearance: " + label);
                }
            }
        }

        private static float PointSegmentDistanceSquared(Vector3 point, Vector3 a, Vector3 b)
        {
            var edge = b - a;
            return (point - a - edge * Mathf.Clamp01(Vector3.Dot(point - a, edge) / Mathf.Max(edge.sqrMagnitude, 1e-20f))).sqrMagnitude;
        }

        private static float PointTriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var normal = Vector3.Cross(ab, ac);
            var projected = point - normal * (Vector3.Dot(point - a, normal) / Mathf.Max(normal.sqrMagnitude, 1e-20f));
            var u = Vector3.Dot(ab, ab);
            var v = Vector3.Dot(ab, ac);
            var w = Vector3.Dot(ac, ac);
            var determinant = u * w - v * v;
            if (determinant > 1e-20f)
            {
                var d = Vector3.Dot(projected - a, ab);
                var e = Vector3.Dot(projected - a, ac);
                var s = (w * d - v * e) / determinant;
                var t = (u * e - v * d) / determinant;
                if (s >= 0 && t >= 0 && s + t <= 1) return (point - projected).sqrMagnitude;
            }
            return Mathf.Min(PointSegmentDistanceSquared(point, a, b), PointSegmentDistanceSquared(point, b, c), PointSegmentDistanceSquared(point, c, a));
        }

        private static float SegmentDistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var minimum = Mathf.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d),
                PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b));
            var u = b - a;
            var v = d - c;
            var w = a - c;
            var aa = Vector3.Dot(u, u);
            var bb = Vector3.Dot(u, v);
            var cc = Vector3.Dot(v, v);
            var dd = Vector3.Dot(u, w);
            var ee = Vector3.Dot(v, w);
            var determinant = aa * cc - bb * bb;
            if (determinant > 1e-20f)
            {
                var s = (bb * ee - cc * dd) / determinant;
                var t = (aa * ee - bb * dd) / determinant;
                if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
                    minimum = Mathf.Min(minimum, (w + s * u - t * v).sqrMagnitude);
            }
            return minimum;
        }

        private static float SegmentTriangleDistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 e)
        {
            var normal = Vector3.Cross(d - c, e - c);
            var denominator = Vector3.Dot(b - a, normal);
            if (Mathf.Abs(denominator) > 1e-20f)
            {
                var t = Vector3.Dot(c - a, normal) / denominator;
                if (t >= 0 && t <= 1 && PointTriangleDistanceSquared(a + t * (b - a), c, d, e) < 1e-14f) return 0;
            }
            return Mathf.Min(PointTriangleDistanceSquared(a, c, d, e), PointTriangleDistanceSquared(b, c, d, e),
                SegmentDistanceSquared(a, b, c, d), SegmentDistanceSquared(a, b, d, e), SegmentDistanceSquared(a, b, e, c));
        }

        private static void ValidateAircraftPitch(GameObject prefab, Transform aircraft, Transform hardpoint, CapsuleCollider capsule, bool levelExternal, string label)
        {
            var frame = new GameObject("Karambit pitch validation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                frame.transform.rotation = Quaternion.Euler(7, 23, -5);
                var hardpointFrame = new GameObject("Hardpoint").transform;
                hardpointFrame.SetParent(frame.transform, false);
                hardpointFrame.localPosition = aircraft.InverseTransformPoint(hardpoint.position);
                hardpointFrame.localRotation = Quaternion.Inverse(aircraft.rotation) * hardpoint.rotation;
                hardpointFrame.localScale = Vector3.one;
                var mounted = Object.Instantiate(prefab, hardpointFrame, false);
                if (levelExternal)
                    mounted.transform.rotation = frame.transform.rotation;
                foreach (var seat in Components(mounted).Where(c => c.GetType().FullName == "MountedMissile"))
                {
                    var center = seat.transform.position;
                    var mountedRotation = seat.transform.rotation;
                    if (levelExternal)
                        seat.transform.rotation = frame.transform.rotation * LaunchRotation;
                    var expectedRotation = levelExternal ? frame.transform.rotation * LaunchRotation : mountedRotation;
                    if (Vector3.Distance(center, seat.transform.position) > .00001f ||
                        Quaternion.Angle(seat.transform.rotation, expectedRotation) > .001f)
                        throw new InvalidDataException("Mounted correction must preserve centers and the bay or external beam axis: " + label);
                }
                ValidateSeatPackaging(mounted, capsule, label + " corrected");
            }
            finally { Object.DestroyImmediate(frame); }
        }

        private static void ValidateSeatPackaging(GameObject root, CapsuleCollider capsule, string label)
        {
            if (capsule == null || capsule.direction != 2)
                throw new InvalidDataException("Expected the Karambit longitudinal body capsule.");
            var seats = Components(root).Where(c => c.GetType().FullName == "MountedMissile").Select(c => c.transform).ToArray();
            var minimumClearance = float.PositiveInfinity;
            for (var i = 0; i < seats.Length; i++)
            for (var j = i + 1; j < seats.Length; j++)
            {
                var a = seats[i];
                var b = seats[j];
                if (Vector3.Distance(a.forward, b.forward) > .00001f)
                    throw new InvalidDataException("Expected parallel Karambit launch axes: " + label);
                var distance = b.TransformPoint(capsule.center) - a.TransformPoint(capsule.center);
                var along = Mathf.Abs(Vector3.Dot(distance, a.forward));
                var radiusA = capsule.radius * Mathf.Max(Mathf.Abs(a.lossyScale.x), Mathf.Abs(a.lossyScale.y));
                var radiusB = capsule.radius * Mathf.Max(Mathf.Abs(b.lossyScale.x), Mathf.Abs(b.lossyScale.y));
                var segmentA = Mathf.Max(0, capsule.height * Mathf.Abs(a.lossyScale.z) * .5f - radiusA);
                var segmentB = Mathf.Max(0, capsule.height * Mathf.Abs(b.lossyScale.z) * .5f - radiusB);
                var endGap = Mathf.Max(0, along - segmentA - segmentB);
                var clearance = Mathf.Sqrt(Mathf.Max(0, distance.sqrMagnitude - along * along) + endGap * endGap) - radiusA - radiusB;
                minimumClearance = Mathf.Min(minimumClearance, clearance);
                // Tangent tandem capsules acquire float rounding after aircraft-space transforms.
                if (clearance < -.00001f)
                    throw new InvalidDataException("Karambit mounted body capsules overlap: " + label + "/" + a.name + "/" + b.name);
            }
            var points = seats.SelectMany(seat => seat.GetComponent<MeshFilter>().sharedMesh.vertices
                .Select(vertex => root.transform.InverseTransformPoint(seat.TransformPoint(vertex)))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            Debug.Log("[BaanishArmory] Karambit packaging " + label + ": bounds=" + bounds + "; minimum body clearance=" + minimumClearance);
        }

        private static void ValidateManifest(PatchManifest manifest)
        {
            if (manifest.modName != ModName || manifest.modVersion != Version || manifest.schemaVersion != 3 ||
                manifest.Ops.Count(op => op.opId == "OpAddWeaponToHardpoint") != Mounts.Length)
                throw new InvalidDataException("Unexpected Karambit manifest or hardpoint operations.");
            var entries = manifest.Ops.Where(op => op.opId == "OpAddToEncyclopedia")
                .SelectMany(op => JsonUtility.FromJson<EncyclopediaPayload>(op.payloadJson).entries).Select(e => e.locator).ToArray();
            var expected = new[] { Prefix + "_definition.asset" }.Concat(Mounts.Select(m => MountPath(m) + ".asset"));
            if (entries.Length != Mounts.Length + 1 || !new HashSet<string>(entries).SetEquals(expected))
                throw new InvalidDataException("Expected the Karambit definition and all six mounts in the encyclopedia.");
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Missing asset: " + path);

        private static IEnumerable<Component> Components(GameObject root)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            if (components.Any(c => c == null))
                throw new InvalidDataException("Missing component in " + root.name);
            return components;
        }

        private static void RefreshCopy(string source, string target)
        {
            File.Copy(Stock(source), target, true);
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
                root.name = Path.GetFileNameWithoutExtension(path);
                foreach (var component in Components(root))
                    Remap(new SerializedObject(component), remap);
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RequireFloat(SerializedObject so, string path, float expected)
        {
            if (so.FindProperty(path)?.floatValue != expected)
                throw new InvalidDataException("Expected " + path + "=" + expected + " on " + so.targetObject.name);
        }

        private static void Set(SerializedObject so, string path, object value)
        {
            var property = so.FindProperty(path) ?? throw new InvalidDataException("Missing field " + path + " on " + so.targetObject.name);
            switch (value)
            {
                case string text: property.stringValue = text; break;
                case int number: property.intValue = number; break;
                case float number: property.floatValue = number; break;
                case bool flag: property.boolValue = flag; break;
                default: throw new ArgumentException("Unsupported value for " + path);
            }
        }
    }
}
