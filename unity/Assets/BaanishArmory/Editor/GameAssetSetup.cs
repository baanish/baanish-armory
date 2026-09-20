using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace BaanishArmory.Editor
{
    public static class GameAssetSetup
    {
        [Serializable]
        private sealed class ReferenceObject { public long fileId; public string type; }

        [Serializable]
        private sealed class ReferenceAsset
        {
            public string exportPath;
            public string path;
            public string guid;
            public ReferenceObject[] objects;
        }

        [Serializable]
        private sealed class ReferenceMap
        {
            public int schemaVersion;
            public string modVersion;
            public string gameVersion;
            public ReferenceAsset[] assets;
        }

        private static readonly Regex GuidPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);

        [MenuItem("Baanish Armory/Import game assets with stable references")]
        private static void SelectExport()
        {
            var folder = EditorUtility.OpenFolderPanel("Select AssetRipper ExportedProject/Assets", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(folder))
                Import(folder);
        }

        public static void Import(string assetsFolder)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var map = LoadReferenceMap();
            var exportRoot = Path.GetFullPath(assetsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var seeds = Preflight(projectRoot, exportRoot, map, AssetDatabase.GUIDToAssetPath);
            InitializeGameTasks();
            BlueprinterAssets.EnsureFolder(BlueprinterSettings.GameAssetRootFolder);
            if (!AssetDatabase.IsValidFolder(BlueprinterSettings.GameAssetRootFolder))
                throw new IOException("Unity could not register the game asset folder.");
            var written = new List<string>();
            var errors = new List<string>();
            var imported = false;
            Application.LogCallback capture = (message, trace, kind) =>
            {
                if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                    errors.Add(message);
                if (message == "[Blueprinter] Imported game assets")
                    imported = true;
            };
            AssetDatabase.DisallowAutoRefresh();
            Application.logMessageReceived += capture;
            try
            {
                // Blueprinter reads these identities before writing complete imported metadata.
                foreach (var seed in seeds)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(seed.Key));
                    using (var stream = new FileStream(seed.Key, FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(seed.Value);
                    written.Add(seed.Key);
                }
                AssetRipperImporter.Import(exportRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (!imported || errors.Count != 0)
                    throw new InvalidDataException("Game asset import failed: " + string.Join("\n", errors));
                VerifyImportedReferences(map);
                Debug.Log("[BaanishArmory] Verified 20 game asset GUIDs and 21 object references.");
            }
            finally
            {
                Application.logMessageReceived -= capture;
                try
                {
                    foreach (var path in written)
                        if (!File.Exists(path.Substring(0, path.Length - 5)) && File.Exists(path) && File.ReadAllText(path) == seeds[path])
                            File.Delete(path);
                }
                finally { AssetDatabase.AllowAutoRefresh(); }
            }
        }

        public static void VerifyImportedReferences() => VerifyImportedReferences(LoadReferenceMap());

        private static ReferenceMap LoadReferenceMap()
        {
            var mapPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../config/game-asset-references.json"));
            RequireUnredirectedPath(mapPath);
            var map = JsonUtility.FromJson<ReferenceMap>(File.ReadAllText(mapPath));
            if (map == null || map.schemaVersion != 1 || map.modVersion != "0.1.5" || map.gameVersion != "0.34.2" ||
                map.assets == null || map.assets.Length != 20 ||
                map.assets.Any(asset => asset == null || asset.objects == null) ||
                map.assets.Sum(asset => asset.objects.Length) != 21)
                throw new InvalidDataException("Expected the reviewed 0.1.5 game asset reference map.");
            return map;
        }

        internal static void InitializeGameTasks()
        {
            // The imported runtime DLL lacks UniTask's editor bootstrap. Stock UI
            // OnValidate callbacks still schedule work, so its runtime init must run first.
            var helper = Type.GetType("Cysharp.Threading.Tasks.PlayerLoopHelper, UniTask", true);
            var init = helper.GetMethod("Init", BindingFlags.Static | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (init == null)
                throw new MissingMethodException("The imported game UniTask initializer is unavailable.");
            init.Invoke(null, null);
        }

        private static Dictionary<string, string> Preflight(string projectRoot, string exportRoot, ReferenceMap map, Func<string, string> findGuid)
        {
            if (map == null || map.schemaVersion != 1 || map.modVersion != "0.1.5" || map.gameVersion != "0.34.2" ||
                map.assets == null || map.assets.Length != 20)
                throw new InvalidDataException("Expected the reviewed 0.1.5 game asset reference map.");
            RequireUnredirectedPath(projectRoot);
            RequireUnredirectedPath(exportRoot);
            if (!Directory.Exists(exportRoot))
                throw new DirectoryNotFoundException("Select the AssetRipper ExportedProject/Assets directory.");
            var exportFiles = new HashSet<string>(EnumerateSafeFiles(exportRoot), StringComparer.OrdinalIgnoreCase);
            var expectedGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seeds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var objectCount = 0;
            foreach (var asset in map.assets)
            {
                if (asset == null || !GuidPattern.IsMatch(asset.guid ?? string.Empty) || asset.objects == null || asset.objects.Length == 0)
                    throw new InvalidDataException("Invalid game asset mapping.");
                var source = ResolveRelativePath(exportRoot, asset.exportPath);
                var projectPath = BlueprinterSettings.GameAssetRootFolder + "/" + asset.path;
                var destination = ResolveRelativePath(projectRoot, projectPath);
                ResolveRelativePath(exportRoot, asset.path);
                if (GetPlaceholderPath(asset.exportPath) != asset.path || !destinations.Add(destination) || expectedGuids.ContainsKey(asset.guid))
                    throw new InvalidDataException("Repeated or incorrect placeholder mapping: " + asset.path);
                expectedGuids.Add(asset.guid, destination);
                if (!exportFiles.Contains(source) || !exportFiles.Contains(source + ".meta"))
                    throw new FileNotFoundException("Export is missing a required asset or meta: " + asset.exportPath);
                ReadGuid(source + ".meta");
                var objectIds = new HashSet<long>();
                foreach (var reference in asset.objects)
                {
                    if (reference == null || reference.fileId == 0 || string.IsNullOrEmpty(reference.type) || !objectIds.Add(reference.fileId))
                        throw new InvalidDataException("Invalid object reference: " + asset.path);
                    objectCount++;
                }
                var owner = findGuid(asset.guid);
                if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, projectPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Required GUID already belongs to " + owner + ". Use a fresh project; existing dependencies are not remapped.");
                var meta = destination + ".meta";
                RequireUnredirectedPath(meta);
                if (File.Exists(meta))
                {
                    if (ReadGuid(meta) != asset.guid)
                        throw new InvalidDataException("Existing asset has a different GUID: " + projectPath + ". Use a fresh project; existing dependencies are not remapped.");
                }
                else
                {
                    if (File.Exists(destination))
                        throw new InvalidDataException("Existing asset has no metadata: " + projectPath + ". Use a fresh project.");
                    seeds.Add(meta, "fileFormatVersion: 2\nguid: " + asset.guid + "\n");
                }
            }
            if (objectCount != 21)
                throw new InvalidDataException("Expected 21 reviewed game object references.");
            var exportPaths = map.assets.ToDictionary(asset => asset.path, asset => asset.exportPath, StringComparer.OrdinalIgnoreCase);
            foreach (var file in exportFiles)
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = file.Substring(exportRoot.Length + 1).Replace('\\', '/');
                if (exportPaths.TryGetValue(GetPlaceholderPath(relative), out var expected) && relative != expected)
                    throw new InvalidDataException("Export contains a competing placeholder source: " + relative);
            }
            // Also catch unimported metadata and junctions before Blueprinter traverses Assets.
            foreach (var file in EnumerateSafeFiles(Path.Combine(projectRoot, "Assets")))
            {
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                var match = Regex.Match(File.ReadAllText(file), @"(?m)^guid: ([0-9a-f]{32})\r?$");
                if (match.Success && expectedGuids.TryGetValue(match.Groups[1].Value, out var destination) &&
                    !string.Equals(file, destination + ".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Required GUID is duplicated at " + file + ". Use a fresh project.");
            }
            return seeds;
        }

        private static void VerifyImportedReferences(ReferenceMap map)
        {
            foreach (var asset in map.assets)
            {
                var projectPath = BlueprinterSettings.GameAssetRootFolder + "/" + asset.path;
                if (AssetDatabase.AssetPathToGUID(projectPath) != asset.guid)
                    throw new InvalidDataException("Imported GUID differs: " + projectPath);
                foreach (var reference in asset.objects)
                {
                    var resolved = SourceArchive.ResolveAsset(projectPath, reference.fileId);
                    if (resolved == null || BlueprinterAssets.GetRuntimeTypeName(resolved.GetType()) != reference.type ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resolved, out var guid, out long fileId) ||
                        guid != asset.guid || fileId != reference.fileId)
                        throw new InvalidDataException("Imported object does not resolve: " + projectPath + " fileID " + reference.fileId);
                }
            }
        }

        private static string GetPlaceholderPath(string exportPath)
        {
            var name = Path.GetFileNameWithoutExtension(exportPath);
            if (name.Length > 2 && name.EndsWith("_0", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 2);
            var directory = Path.GetDirectoryName(exportPath)?.Replace('\\', '/');
            return (string.IsNullOrEmpty(directory) ? string.Empty : directory + "/") + name + BlueprinterSettings.PlaceholderSuffix + Path.GetExtension(exportPath);
        }

        private static string ReadGuid(string metaPath)
        {
            var matches = Regex.Matches(File.ReadAllText(metaPath), @"(?m)^guid: ([0-9a-f]{32})\r?$");
            if (matches.Count != 1)
                throw new InvalidDataException("Missing or invalid asset GUID: " + metaPath);
            return matches[0].Groups[1].Value;
        }

        private static string ResolveRelativePath(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains("\\") || relative.Contains(":") ||
                relative.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
                throw new InvalidDataException("Invalid relative asset path: " + relative);
            var path = Path.GetFullPath(Path.Combine(root, relative));
            RequireUnredirectedPath(path);
            return path;
        }

        private static void RequireUnredirectedPath(string path)
        {
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Redirected asset path: " + current);
        }

        private static IEnumerable<string> EnumerateSafeFiles(string directory)
        {
            if (!Directory.Exists(directory))
                yield break;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Redirected asset path: " + path);
                if ((attributes & FileAttributes.Directory) == 0)
                    yield return Path.GetFullPath(path);
                else
                    foreach (var file in EnumerateSafeFiles(path))
                        yield return file;
            }
        }
    }
}
