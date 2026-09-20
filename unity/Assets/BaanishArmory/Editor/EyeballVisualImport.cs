using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BaanishArmory.Editor
{
    public static class EyeballVisualImport
    {
        private const string ModFolder = "Assets/Blueprinter/Mods/baanish-armory";
        private const string IconPath = ModFolder + "/baanish_eyeball_xl_icon.png";

        public static void ValidateAuthoredIcon()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (sprite == null) throw new InvalidDataException("Missing imported icon Sprite.");
            var failures = new List<string>();
            // Unity's 1774-to-512 import rounds sprite bounds by at most 0.000031 pixels.
            if (sprite.rect.x != 0 || sprite.rect.y != 0 || Mathf.Abs(sprite.rect.width - 512) > 0.0001f || Mathf.Abs(sprite.rect.height - 256) > 0.0001f)
                failures.Add($"rect: {sprite.rect.x:R},{sprite.rect.y:R},{sprite.rect.width:R},{sprite.rect.height:R}");
            if (Mathf.Abs(sprite.pivot.x - 256) > 0.0001f || Mathf.Abs(sprite.pivot.y - 128) > 0.0001f)
                failures.Add($"pivot: {sprite.pivot.x:R},{sprite.pivot.y:R}");
            if (Mathf.Abs(sprite.pixelsPerUnit - 100f * 512 / 1774) > 0.00001f) failures.Add($"runtimePPU: {sprite.pixelsPerUnit:R}, expected {100f * 512 / 1774:R}");
            if (importer.spritePixelsPerUnit != 100) failures.Add($"importerPPU: {importer.spritePixelsPerUnit:R}");
            if (importer.spriteImportMode != SpriteImportMode.Single) failures.Add("spriteImportMode");
            if (importer.mipmapEnabled) failures.Add("mipmaps");
            if (importer.filterMode != FilterMode.Bilinear) failures.Add("filterMode");
            if (importer.wrapMode != TextureWrapMode.Clamp) failures.Add("wrapMode");
            if (importer.alphaSource != TextureImporterAlphaSource.None) failures.Add("alphaSource");
            if (importer.npotScale != TextureImporterNPOTScale.None) failures.Add("npotScale");
            if (importer.maxTextureSize != 512) failures.Add("maxTextureSize");
            if (importer.isReadable) failures.Add("isReadable");
            if (sprite.texture.width != 512 || sprite.texture.height != 256) failures.Add("textureDimensions");
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) failures.Add("compression");
            if (!importer.sRGBTexture || importer.alphaIsTransparency) failures.Add("colorImport");
            if (failures.Count != 0) throw new InvalidDataException("Icon import settings changed: " + string.Join("; ", failures));
            var info = AssetDatabase.LoadMainAssetAtPath(ModFolder + "/baanish_eyeball_xl_info.asset");
            if (new SerializedObject(info).FindProperty("weaponIcon").objectReferenceValue != sprite)
                throw new InvalidDataException("Weapon info does not reference the authored icon Sprite.");
        }
    }
}
