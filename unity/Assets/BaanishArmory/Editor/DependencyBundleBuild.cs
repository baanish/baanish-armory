using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace BaanishArmory.Editor
{
    public static class DependencyBundleBuild
    {
        public static void Build(string modName, string displayName, string version, string outputFolder)
        {
            var assets = BlueprinterAssets.GetModAssetPaths(modName);
            var included = Invoke<HashSet<string>>("GetIncludedAssetPaths", (object)assets);
            if (included == null || !PrefabHashWriter.Write(assets))
                throw new InvalidDataException("Blueprinter could not prepare mod assets.");
            var manifest = Invoke<PatchManifest>("BuildPatchManifest", displayName, version, assets, included);
            if (manifest == null)
                throw new InvalidDataException("Blueprinter could not resolve game references.");

            const string manifestPath = BlueprinterSettings.GeneratedFolder + "/patch_manifest.json";
            BlueprinterAssets.EnsureFolder(BlueprinterSettings.GeneratedFolder);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceUpdate);
            var targetAssets = assets.Concat(new[] { manifestPath }).OrderBy(path => path, StringComparer.Ordinal).ToArray();

            // Keep stock references in Blueprinter's separate, unshipped bundle. Unrelated
            // exported assets include compiled compute shaders that Unity cannot rebuild.
            var gameAssets = AssetDatabase.GetDependencies(targetAssets, true)
                .Where(BlueprinterAssets.IsGameAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            const string cache = "BlueprinterCache/Assetbundles";
            Directory.CreateDirectory(cache);
            var bundleName = modName.ToLowerInvariant();
            Debug.Log("[BaanishArmory] Building " + gameAssets.Length + " required game dependencies.");
            if (!Invoke<bool>("BuildAssetBundles", cache, bundleName, targetAssets, gameAssets))
                throw new InvalidDataException("Blueprinter dependency bundle build failed.");
            Invoke<object>("CopyBuiltMod", cache, bundleName, displayName, version, outputFolder);
        }

        // The pinned Blueprinter revision exposes only the all-game-assets build publicly.
        // Reuse its manifest and bundle methods so runtime patch semantics stay upstream.
        private static T Invoke<T>(string name, params object[] arguments)
        {
            var method = typeof(ModBuilder).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
                null, arguments.Select(argument => argument.GetType()).ToArray(), null);
            if (method == null)
                throw new MissingMethodException("Pinned Blueprinter build method is unavailable: " + name);
            return (T)method.Invoke(null, arguments);
        }
    }
}
