using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace NO_ATC_Mod.Core
{
    public static class GameAssetsLoader
    {
        private const string IconsSubfolder = "icons";
        private const int FallbackSize = 16;
        private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Texture2D, Sprite> SpriteCache = new Dictionary<Texture2D, Sprite>();
        private static string _iconsFolder = string.Empty;

        public static void SetIconsFolder(string pluginDirectory)
        {
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                _iconsFolder = string.Empty;
                return;
            }
            string path = Path.Combine(pluginDirectory, IconsSubfolder);
            if (_iconsFolder == path) return;
            _iconsFolder = path;
            ClearCache();
        }

        public static Texture2D LoadTexture(string textureFileName)
        {
            if (string.IsNullOrWhiteSpace(textureFileName))
                return null;
            if (TextureCache.TryGetValue(textureFileName, out Texture2D cached))
                return cached;

            if (!string.IsNullOrEmpty(_iconsFolder))
            {
                string fullPath = Path.Combine(_iconsFolder, textureFileName);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(fullPath);
                        var tex = new Texture2D(2, 2);
                        bool loaded = false;
                        var loadImageMethod = typeof(Texture2D).GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(byte[]) }, null);
                        if (loadImageMethod != null)
                            loaded = (bool)(loadImageMethod.Invoke(tex, new object[] { bytes }) ?? false);
                        else
                        {
                            var imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                            var loadStatic = imageConversion?.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D), typeof(byte[]) }, null);
                            if (loadStatic != null)
                                loaded = (bool)(loadStatic.Invoke(null, new object[] { tex, bytes }) ?? false);
                        }
                        if (loaded)
                        {
                            tex.filterMode = FilterMode.Bilinear;
                            TextureCache[textureFileName] = tex;
                            return tex;
                        }
                        UnityEngine.Object.Destroy(tex);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[GameAssetsLoader] Failed to load '{textureFileName}': {ex.Message}");
                    }
                }
            }

            Texture2D fallback = MakeFallbackIcon(textureFileName);
            if (fallback != null)
                TextureCache[textureFileName] = fallback;
            return fallback;
        }

        private static Texture2D MakeFallbackIcon(string name)
        {
            Color c;
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("friendly")) c = new Color(0.2f, 0.9f, 0.3f, 1f);
            else if (n.Contains("hostile")) c = new Color(0.95f, 0.2f, 0.2f, 1f);
            else c = new Color(0.5f, 0.55f, 0.6f, 1f);
            return MakeSolidTexture(FallbackSize, FallbackSize, c);
        }

        private static Texture2D MakeSolidTexture(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            tex.SetPixels(pix);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        public static Texture2D GetUnitIconTexture(Unit unit, bool isFriendly)
        {
            if (unit == null)
                return LoadTexture("contact.png");
            if (unit is Aircraft)
                return LoadTexture(isFriendly ? "aircraft_friendly.png" : "aircraft_hostile.png");
            return LoadTexture(isFriendly ? "unit_friendly.png" : "unit_hostile.png");
        }

        public static Sprite GetOrCreateSprite(Texture2D tex)
        {
            if (tex == null) return null;
            if (SpriteCache.TryGetValue(tex, out Sprite sprite))
                return sprite;
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            SpriteCache[tex] = sprite;
            return sprite;
        }

        public static void ClearCache()
        {
            foreach (var sprite in SpriteCache.Values)
            {
                if (sprite != null)
                    UnityEngine.Object.Destroy(sprite);
            }
            SpriteCache.Clear();
            foreach (var tex in TextureCache.Values)
            {
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
            }
            TextureCache.Clear();
        }
    }
}
