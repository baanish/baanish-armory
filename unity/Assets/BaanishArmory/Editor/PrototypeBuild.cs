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
            if (info.displayName != ModName || info.version != "0.1.5" || !ModBuilder.ValidateVersion(info.version, out var version))
                throw new InvalidDataException("Expected the authored baanish-armory 0.1.5 source.");
            var workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var baseline = JsonUtility.FromJson<SourceBaseline>(File.ReadAllText(Path.Combine(workspace, "config", "prototype-baseline-0.1.5.json")));
            ValidateSourceBaseline(workspace, baseline);
            if (Application.unityVersion != baseline.unityVersion)
                throw new InvalidDataException("Use Unity " + baseline.unityVersion + " for this source baseline.");

            GameAssetSetup.InitializeGameTasks();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameAssetSetup.VerifyImportedReferences();
            var assets = BlueprinterAssets.GetModAssetPaths(ModName);
            if (assets.Length != 8)
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
                manifest.schemaVersion != 3 || manifest.gameVersion != "0.34.2" || manifest.Ops.Count != 2)
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
            var hardpoint = manifest.Ops.Find(op => op.opId == "OpAddWeaponToHardpoint");
            var encyclopedia = manifest.Ops.Find(op => op.opId == "OpAddToEncyclopedia");
            if (hardpoint == null || encyclopedia == null)
                throw new InvalidDataException("Missing weapon registration operations.");
            var registration = JsonUtility.FromJson<HardpointPayload>(hardpoint.payloadJson);
            if (registration.weaponJsonKey != "baanish_eyeball_xl_single" || registration.aircraft.Length != 1 ||
                registration.aircraft[0].aircraftJsonKey != "EW1" || registration.aircraft[0].hardpointIndices.Length != 1 ||
                registration.aircraft[0].hardpointIndices[0] != 4)
                throw new InvalidDataException("Expected Medusa outer-wing registration only.");
            var entries = JsonUtility.FromJson<EncyclopediaPayload>(encyclopedia.payloadJson).entries;
            if (entries.Length != 2 ||
                Array.Find(entries, entry => entry.locator == ModFolder + "/baanish_eyeball_xl_definition.asset") == null ||
                Array.Find(entries, entry => entry.locator == ModFolder + "/baanish_eyeball_xl_single.asset") == null)
                throw new InvalidDataException("Expected the independent missile definition and mount.");
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
            if (baseline == null || baseline.schemaVersion != 1 || baseline.modVersion != "0.1.5" ||
                baseline.unityVersion != "2022.3.62f2" || baseline.hashPolicy != "png-bytes;other-files-utf8-lf-no-bom" ||
                baseline.files == null || baseline.files.Length != 19)
                throw new InvalidDataException("Expected the checked-in 0.1.5 source baseline with 19 files.");
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
                throw new InvalidDataException("The 0.1.5 source baseline requires 5059 vertices, 6906 body triangles and 96 optical-window triangles.");
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
