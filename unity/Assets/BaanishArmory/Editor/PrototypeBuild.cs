using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blueprinter;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaanishArmory.Editor
{
    public static class PrototypeBuild
    {
        private const string ModName = "baanish-armory";
        private const string ModFolder = "Assets/Blueprinter/Mods/" + ModName;

        [Serializable]
        private sealed class ModInfo
        {
            public string displayName;
            public string version;
        }

        [Serializable]
        private sealed class BaselineFile { public string path; public string sha256; }

        [Serializable]
        private sealed class SourceBaseline
        {
            public int schemaVersion;
            public string modVersion;
            public string unityVersion;
            public string hashPolicy;
            public BaselineFile[] files;
        }

        [Serializable]
        private sealed class SourceArchiveInfo { public string modName; public string displayName; public string version; }

        [Serializable]
        private sealed class HardpointPayload
        {
            public string weaponJsonKey;
            public AircraftStations[] aircraft;
        }

        [Serializable]
        private sealed class AircraftStations
        {
            public string aircraftJsonKey;
            public int[] hardpointIndices;
        }

        [Serializable]
        private sealed class EncyclopediaPayload
        {
            public AssetRef[] entries;
        }

        [MenuItem("Baanish Armory/Build prototype")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Finish play mode or compilation before building.");

            var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText(ModFolder + "/modinfo.json"));
            if (info.displayName != ModName || info.version != "0.2.0" || !ModBuilder.ValidateVersion(info.version, out var version))
                throw new InvalidDataException("Expected the authored baanish-armory 0.2.0 source.");
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var baseline = JsonUtility.FromJson<SourceBaseline>(File.ReadAllText(Path.Combine(workspace, "config", "prototype-baseline-0.2.0.json")));
            ValidateSourceBaseline(workspace, baseline);
            if (Application.unityVersion != baseline.unityVersion)
                throw new InvalidDataException("Use Unity " + baseline.unityVersion + " for this source baseline.");

            GameAssetSetup.InitializeGameTasks();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameAssetSetup.VerifyImportedReferences();
            var assets = BlueprinterAssets.GetModAssetPaths(ModName);
            if (assets.Length != 23)
                throw new InvalidDataException("Unexpected authored asset count for this prototype version.");
            foreach (var path in assets)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    throw new InvalidDataException("Unity could not load " + path);
                if (asset is GameObject prefab)
                {
                    var paths = new HashSet<string>();
                    foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        var relative = AnimationUtility.CalculateTransformPath(child, prefab.transform);
                        if (!paths.Add(relative))
                            throw new InvalidDataException("Ambiguous Blueprinter hierarchy path: " + path + "/" + relative);
                    }
                }
            }
            ValidatePrototypeSettings();
            ValidateVisualModel();

            var output = Path.Combine(workspace, ".local", "builds", "prototype-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
            if (Directory.Exists(output))
                throw new IOException("Build output already exists: " + output);
            Directory.CreateDirectory(output);

            // Blueprinter's void APIs may log an error and return without throwing.
            var errors = new List<string>();
            var editorDiagnostics = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors.Add(message);
                    editorDiagnostics.Add(message + "\n" + stack);
                }
            };
            Application.logMessageReceived += capture;
            try
            {
                DependencyBundleBuild.Build(ModName, info.displayName, version, output);
                File.WriteAllText(Path.Combine(output, "editor-diagnostics.txt"), string.Join("\n", editorDiagnostics));
                ThrowBuildErrors(errors);
                var bundle = Path.Combine(output, ModName + "_" + version + ".nobp");
                RequireFile(bundle);
                var cache = "BlueprinterCache/Assetbundles/" + ModName;
                if (HashFile(bundle) != HashFile(cache))
                    throw new InvalidDataException("Output bundle differs from Unity's build cache.");
                var manifestPath = "Assets/Blueprinter/Generated/patch_manifest.json";
                ValidateManifest(JsonUtility.FromJson<PatchManifest>(File.ReadAllText(manifestPath)), version);
                File.Copy(manifestPath, Path.Combine(output, "patch_manifest.json"));

                var source = Path.Combine(output, ModName + "_" + version + ".source.zip");
                SourceExporter.ExportMod(ModName, info.displayName, version, source);
                ThrowBuildErrors(errors);
                RequireFile(source);
                ValidateSourceArchive(source, assets, version);
                ValidateSourceBaseline(workspace, baseline);
                File.WriteAllText(Path.Combine(output, "SHA256SUMS.txt"),
                    HashFile(bundle) + "  " + Path.GetFileName(bundle) + "\n" +
                    HashFile(source) + "  " + Path.GetFileName(source) + "\n");
                Debug.Log("[BaanishArmory] Build and source export complete: " + output);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
        }

        private static void ValidateManifest(PatchManifest manifest, string version)
        {
            if (manifest.modName != ModName || manifest.modVersion != version ||
                manifest.schemaVersion != 3 || manifest.gameVersion != "0.34.2" ||
                manifest.Ops.Any(op => op.opId != "OpAddWeaponToHardpoint" && op.opId != "OpAddToEncyclopedia"))
                throw new InvalidDataException("Unexpected prototype manifest identity or operation count.");
            var targets = new HashSet<string>();
            foreach (var patch in manifest.Patches)
            foreach (var location in patch.PatchLocations)
            {
                if (location.memberPath == "weaponIcon")
                    throw new InvalidDataException("The authored icon must remain a bundle reference, not a stock sprite patch.");
                var key = string.Join("|", location.asset.locator, location.hierarchyPath,
                    location.componentType, location.componentIndex, location.memberPath);
                if (!targets.Add(key))
                    throw new InvalidDataException("Duplicate runtime patch target: " + key);
            }
            var registrations = manifest.Ops.Where(op => op.opId == "OpAddWeaponToHardpoint")
                .Select(op => JsonUtility.FromJson<HardpointPayload>(op.payloadJson)).ToList();
            if (registrations.Count != 2)
                throw new InvalidDataException("Expected one hardpoint registration per weapon.");
            RequireRegistration(registrations, "baanish_eyeball_xl_single", new[] { ("EW1", new[] { 4 }) });
            RequireRegistration(registrations, "baanish_agk4_lance_4pod", LanceAssetBuild.Hardpoints);
            var entries = manifest.Ops.Where(op => op.opId == "OpAddToEncyclopedia")
                .SelectMany(op => JsonUtility.FromJson<EncyclopediaPayload>(op.payloadJson).entries).Select(entry => entry.locator).ToList();
            var expected = new[] { "baanish_eyeball_xl_definition", "baanish_eyeball_xl_single", "baanish_agk4_lance_definition", "baanish_agk4_lance_4pod" }
                .Select(name => ModFolder + "/" + name + ".asset");
            if (entries.Count != 4 || !new HashSet<string>(entries).SetEquals(expected))
                throw new InvalidDataException("Expected each weapon's independent missile definition and mount in the encyclopedia.");
        }

        private static void RequireRegistration(List<HardpointPayload> registrations, string weapon, (string aircraft, int[] sets)[] expected)
        {
            var registration = registrations.Find(candidate => candidate.weaponJsonKey == weapon)
                ?? throw new InvalidDataException("Missing hardpoint registration for " + weapon);
            var actual = registration.aircraft.Select(entry => entry.aircraftJsonKey + ":" + string.Join(",", entry.hardpointIndices)).ToList();
            if (!actual.SequenceEqual(expected.Select(entry => entry.aircraft + ":" + string.Join(",", entry.sets))))
                throw new InvalidDataException("Unexpected hardpoints for " + weapon + ": " + string.Join(" ", actual));
        }

        private static void ValidatePrototypeSettings()
        {
            var mount = AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_eyeball_xl_single.asset");
            if (mount == null)
                throw new InvalidDataException("Missing single-missile mount.");
            var serializedMount = new SerializedObject(mount);
            if (serializedMount.FindProperty("ammo").intValue != 1 || serializedMount.FindProperty("missileBay").boolValue)
                throw new InvalidDataException("Eyeball-XL must carry one missile per external rack.");
            if (serializedMount.FindProperty("mass").floatValue != 425 || serializedMount.FindProperty("emptyMass").floatValue != 25)
                throw new InvalidDataException("Expected the ARAD single rack mass: 425 kg loaded, 25 kg empty.");
            var info = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_eyeball_xl_info.asset"));
            var definition = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_eyeball_xl_definition.asset"));
            if (serializedMount.FindProperty("mountName").stringValue != "Eyeball-XL" ||
                info.FindProperty("weaponName").stringValue != "Eyeball-XL" || info.FindProperty("shortName").stringValue != "Eyeball-XL" ||
                definition.FindProperty("unitName").stringValue != "Eyeball-XL")
                throw new InvalidDataException("All displayed weapon names must be Eyeball-XL.");
            if (info.FindProperty("costPerRound").floatValue != 2.5f || definition.FindProperty("value").floatValue != 2.5f ||
                serializedMount.FindProperty("emptyCost").floatValue != 0)
                throw new InvalidDataException("Expected $2.5 million per missile and $5 million for the outer-pylon pair.");

            var rack = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/baanish_eyeball_xl_single.prefab");
            if (rack == null)
                throw new InvalidDataException("Missing single-missile rack prefab.");
            var launchers = 0;
            foreach (var component in rack.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    throw new InvalidDataException("Rack contains a missing component.");
                if (component.GetType().FullName == "MountedMissile") launchers++;
            }
            if (launchers != 1)
                throw new InvalidDataException("Expected exactly one physical missile launcher per rack.");

            var missile = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/baanish_eyeball_xl.prefab");
            if (missile == null)
                throw new InvalidDataException("Missing flight prefab.");
            var detectors = 0;
            var opticalSeekers = 0;
            var missileComponents = 0;
            foreach (var component in missile.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    throw new InvalidDataException("Flight prefab contains a missing component.");
                var type = component.GetType().FullName;
                if (type == "ARMSeeker" || type == "Radar")
                    throw new InvalidDataException("Eyeball-XL must use passive optical reconnaissance.");
                if (type == "OpticalSeeker") opticalSeekers++;
                if (type == "Missile")
                {
                    missileComponents++;
                    var flight = new SerializedObject(component);
                    if (flight.FindProperty("mass").floatValue != 400 || flight.FindProperty("seekerMode").intValue != 1 ||
                        flight.FindProperty("blastYield").floatValue != 0 || flight.FindProperty("pierceDamage").floatValue != 300)
                        throw new InvalidDataException("Expected ARAD mass with Eyeball guidance and non-explosive collision behavior.");
                }
                if (type != "TargetDetector") continue;
                detectors++;
                var detector = new SerializedObject(component);
                if (detector.FindProperty("visualRange").floatValue != 12000 || detector.FindProperty("magnification").floatValue != 1)
                    throw new InvalidDataException("Expected a 12 km passive search setting and 1x magnification.");
            }
            if (detectors != 1 || opticalSeekers != 1 || missileComponents != 1)
                throw new InvalidDataException("Expected one missile, optical seeker and passive detector.");
            ValidateLanceSettings();
        }

        // The AGK-4 Lance numbers from docs/LANCE-CONCEPT.md, as authored by LanceAssetBuild.
        private static void ValidateLanceSettings()
        {
            var mount = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_agk4_lance_4pod.asset"));
            if (mount.FindProperty("ammo").intValue != 4 || mount.FindProperty("mass").floatValue != 335 ||
                mount.FindProperty("emptyMass").floatValue != 95 || mount.FindProperty("drag").floatValue != 0.08f ||
                mount.FindProperty("RCS").floatValue != 0.005f)
                throw new InvalidDataException("Expected four Lance rounds per pod: 335 kg loaded, 95 kg empty, drag 0.08, RCS 0.005.");
            var info = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_agk4_lance_info.asset"));
            var definition = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_agk4_lance_definition.asset"));
            if (info.FindProperty("weaponName").stringValue != "AGK-4 Lance" || info.FindProperty("shortName").stringValue != "AGK-4" ||
                definition.FindProperty("unitName").stringValue != "AGK-4 Lance" || mount.FindProperty("mountName").stringValue != "AGK-4 Lance x4")
                throw new InvalidDataException("Lance display names must be AGK-4 Lance.");
            if (info.FindProperty("costPerRound").floatValue != 0.15f || definition.FindProperty("value").floatValue != 0.15f ||
                info.FindProperty("targetRequirements.minAlignment").floatValue != 4)
                throw new InvalidDataException("Expected $150,000 per Lance round and a 4-degree launch arc.");

            var missile = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/baanish_agk4_lance.prefab");
            var flights = 0;
            var seekers = 0;
            foreach (var component in missile.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    throw new InvalidDataException("Lance flight prefab contains a missing component.");
                var type = component.GetType().FullName;
                var serialized = new SerializedObject(component);
                if (type == "Missile")
                {
                    flights++;
                    if (serialized.FindProperty("pierceDamage").floatValue != 3000 || serialized.FindProperty("blastYield").floatValue != 2 ||
                        serialized.FindProperty("motors.Array.data[0].thrust").floatValue != 28000 ||
                        serialized.FindProperty("motors.Array.data[0].burnTime").floatValue != 5.15f ||
                        serialized.FindProperty("finArea").floatValue != 0.035f ||
                        serialized.FindProperty("armorProperties.fireArmor").floatValue != 6 ||
                        serialized.FindProperty("armorProperties.fireTolerance").floatValue != 0.3f)
                        throw new InvalidDataException("Expected the Lance motor, fins, penetrator and GPO-500 fire armor.");
                    var burn = serialized.FindProperty("motors.Array.data[0].burnTime").floatValue;
                    var effects = References<ParticleSystem>(serialized, "motors.Array.data[0].particleSystems");
                    if (effects.Any(effect => !effect.main.loop && effect.main.duration < burn))
                        throw new InvalidDataException("A Lance motor effect stops before burnout.");
                    var sounds = References<AudioSource>(serialized, "motors.Array.data[0].audioSources");
                    if (sounds.Count != 2 || sounds.Count(sound => sound.loop) != 1)
                        throw new InvalidDataException("Expected the Lance motor's one-shot launch sound and a motor loop until burnout.");
                    var trails = References<Object>(serialized, "motors.Array.data[0].trailEmitters");
                    if (trails.Any(trail => new SerializedObject(trail).FindProperty("emitLifetime").floatValue < burn))
                        throw new InvalidDataException("The Lance motor trail stops before burnout.");
                }
                if (type == "LaserSeeker")
                {
                    seekers++;
                    if (serialized.FindProperty("maxSeekerAngle").floatValue != 5)
                        throw new InvalidDataException("Expected the Lance's 5-degree laser seeker cone.");
                }
            }
            if (flights != 1 || seekers != 1)
                throw new InvalidDataException("Expected one Lance missile and one laser seeker.");
            var pod = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/baanish_agk4_lance_4pod.prefab");
            var launchers = pod.GetComponentsInChildren<Component>(true).Where(component => component != null && component.GetType().FullName == "MountedMissile").ToList();
            if (launchers.Count != 4)
                throw new InvalidDataException("Expected four physical Lance launchers per pod.");
            if (launchers.Any(launcher => new SerializedObject(launcher).FindProperty("railLength").floatValue < 3.92f))
                throw new InvalidDataException("Each Lance must clear its 3.92 m tube before the flight round spawns.");
            var podMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ModFolder + "/baanish_agk4_lance_4pod_mesh.asset");
            var podFilters = pod.GetComponentsInChildren<MeshFilter>(true).Where(filter => podMesh != null && filter.sharedMesh == podMesh).ToList();
            if (podFilters.Count != 1)
                throw new InvalidDataException("Expected one Lance pod model in its prefab.");
            var length = podMesh.bounds.size.z;
            var capsule = podFilters[0].GetComponent<CapsuleCollider>();
            if (capsule == null || capsule.direction != 2 || Mathf.Abs(capsule.height - length) > 0.01f)
                throw new InvalidDataException("The Lance pod collider must span the pod's length.");
            var lod = pod.GetComponentInChildren<LODGroup>(true);
            if (lod == null || Mathf.Abs(lod.size - length) > 0.01f)
                throw new InvalidDataException("The Lance pod LOD group must measure the pod's length.");
        }

        // Every assigned object in a serialized reference array; a missing or mistyped entry is a broken asset.
        private static List<T> References<T>(SerializedObject owner, string path) where T : Object
        {
            var array = owner.FindProperty(path) ?? throw new InvalidDataException("Missing field " + path);
            var result = new List<T>();
            for (var i = 0; i < array.arraySize; i++)
                result.Add(array.GetArrayElementAtIndex(i).objectReferenceValue as T
                    ?? throw new InvalidDataException("Unassigned or mistyped reference in " + path));
            return result;
        }

        private static void ThrowBuildErrors(List<string> errors)
        {
            if (errors.Count > 0)
                throw new InvalidOperationException("Build logged errors: " + string.Join("\n", errors));
        }

        private static void ValidateSourceArchive(string path, string[] assets, string version)
        {
            var expected = new HashSet<string>(assets.SelectMany(asset => new[] { asset, asset + ".meta" }), StringComparer.Ordinal)
                { "source_manifest.json" };
            using (var archive = ZipFile.OpenRead(path))
            {
                if (archive.Entries.Count != expected.Count)
                    throw new InvalidDataException("Source ZIP has an unexpected entry count.");
                foreach (var entry in archive.Entries)
                {
                    if (!expected.Remove(entry.FullName))
                        throw new InvalidDataException("Unexpected or duplicate source ZIP entry: " + entry.FullName);
                    using (var stream = entry.Open())
                    {
                        if (entry.FullName == "source_manifest.json")
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var info = JsonUtility.FromJson<SourceArchiveInfo>(reader.ReadToEnd());
                                if (info == null || info.modName != ModName || info.displayName != ModName || info.version != version)
                                    throw new InvalidDataException("Source ZIP manifest identity differs from the bundle.");
                            }
                        }
                        else
                        {
                            using (var sha = SHA256.Create())
                                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != HashFile(entry.FullName))
                                    throw new InvalidDataException("Source ZIP bytes differ from authored source: " + entry.FullName);
                        }
                    }
                }
            }
        }

        private static void ValidateSourceBaseline(string workspace, SourceBaseline baseline)
        {
            if (baseline == null || baseline.schemaVersion != 1 || baseline.modVersion != "0.2.0" ||
                baseline.unityVersion != "2022.3.62f2" || baseline.hashPolicy != "png-bytes;other-files-utf8-lf-no-bom" ||
                baseline.files == null || baseline.files.Length != 49)
                throw new InvalidDataException("Expected the checked-in 0.2.0 source baseline with 49 files.");
            var relativeMod = "unity/" + ModFolder;
            var modPath = Path.GetFullPath(Path.Combine(workspace, relativeMod));
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in baseline.files)
            {
                if (file == null || string.IsNullOrEmpty(file.path) || Path.IsPathRooted(file.path) || file.path.Contains("\\") ||
                    file.path.Split('/').Any(part => part.Length == 0 || part == "." || part == "..") ||
                    file.sha256?.Length != 64 || file.sha256.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidDataException("Invalid relative baseline path or SHA256.");
                var path = Path.GetFullPath(Path.Combine(workspace, file.path));
                if ((file.path != relativeMod + ".meta" && Path.GetDirectoryName(path) != modPath) ||
                    Path.GetFileName(path) == ".prototype-receipt.json" || !expected.Add(path))
                    throw new InvalidDataException("Repeated or out-of-scope baseline path: " + file.path);
                for (var current = path; current != null; current = Path.GetDirectoryName(current))
                    if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Redirected source path: " + current);
                if (!File.Exists(path) || HashBaselineFile(path) != file.sha256.ToLowerInvariant())
                    throw new InvalidDataException("Authored source differs from the checked-in baseline: " + file.path);
            }
            var actual = Directory.GetFiles(modPath).Where(path => Path.GetFileName(path) != ".prototype-receipt.json")
                .Concat(new[] { modPath + ".meta" });
            if (Directory.GetDirectories(modPath).Length != 0 || !expected.SetEquals(actual))
                throw new InvalidDataException("Unexpected authored files or missing asset metas; update the reviewed baseline for intentional source changes.");
        }

        private static string HashBaselineFile(string path)
        {
            // Git line-ending conversion must not invalidate otherwise identical Unity text assets.
            var bytes = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? File.ReadAllBytes(path) : Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n"));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void ValidateVisualModel()
        {
            EyeballVisualImport.ValidateAuthoredIcon();
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ModFolder + "/baanish_eyeball_xl_mesh.asset");
            var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d4e7589a01e37824ea77fda6b56b8245"));
            var opticsMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("2f6bf9049589566438b752ff290ea93a"));
            if (mesh == null || mesh.subMeshCount != 2 || mesh.vertexCount <= 692 ||
                mesh.GetIndexCount(0) <= 568 * 3 || mesh.GetIndexCount(1) == 0 ||
                bodyMaterial == null || opticsMaterial == null)
                throw new InvalidDataException("Expected the validated faceted nose and three integrated windows with their stock materials.");
            if (mesh.vertexCount != 5059 || mesh.GetIndexCount(0) != 6906 * 3 || mesh.GetIndexCount(1) != 96 * 3)
                throw new InvalidDataException("The Eyeball-XL model requires 5059 vertices, 6906 body triangles and 96 optical-window triangles.");
            foreach (var name in new[] { "baanish_eyeball_xl.prefab", "baanish_eyeball_xl_single.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/" + name);
                var matches = 0;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != mesh) continue;
                    matches++;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || renderer.sharedMaterials.Length != 2 ||
                        renderer.sharedMaterials[0] != bodyMaterial || renderer.sharedMaterials[1] != opticsMaterial)
                        throw new InvalidDataException("Custom missile must use Missiles2 body and Weapons5 optics materials: " + name);
                }
                if (matches != 1)
                    throw new InvalidDataException("Expected one custom missile model in " + name);
            }
            ValidateLanceVisuals();
        }

        private static void ValidateLanceVisuals()
        {
            EyeballVisualImport.ValidateAuthoredIcon(ModFolder + "/baanish_agk4_lance_icon.png", ModFolder + "/baanish_agk4_lance_info.asset");
            var body = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("ce7d6ad143c11934aad61cbd4b0f20d3"));
            var optics = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("2f6bf9049589566438b752ff290ea93a"));
            var stencil = AssetDatabase.LoadAssetAtPath<Material>(ModFolder + "/baanish_agk4_stencil.mat");
            var models = new[]
            {
                (prefab: "baanish_agk4_lance.prefab", mesh: "baanish_agk4_lance_mesh.asset", materials: new[] { body, optics }, renderers: 1),
                (prefab: "baanish_agk4_lance_4pod.prefab", mesh: "baanish_agk4_lance_folded_mesh.asset", materials: new[] { body, optics }, renderers: 4),
                (prefab: "baanish_agk4_lance_4pod.prefab", mesh: "baanish_agk4_lance_4pod_mesh.asset", materials: new[] { body, stencil }, renderers: 1),
            };
            foreach (var model in models)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ModFolder + "/" + model.mesh);
                if (mesh == null || mesh.subMeshCount != 2 || model.materials.Any(material => material == null))
                    throw new InvalidDataException("Expected two-material Lance model " + model.mesh);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModFolder + "/" + model.prefab);
                var renderers = prefab.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh == mesh)
                    .Select(filter => filter.GetComponent<MeshRenderer>()).ToList();
                if (renderers.Count != model.renderers || renderers.Any(renderer => renderer == null || !renderer.sharedMaterials.SequenceEqual(model.materials)))
                    throw new InvalidDataException("Lance model " + model.mesh + " must use its stock and stencil materials in " + model.prefab);
            }
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("Expected nonempty build output: " + path);
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
