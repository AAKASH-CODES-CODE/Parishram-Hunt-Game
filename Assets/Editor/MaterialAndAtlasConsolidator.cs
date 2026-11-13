using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoachingPropHunt.Editor
{
    /// <summary>
    /// Editor optimization pass for:
    /// 1. Android Texture Compression (ASTC 6x6), Mipmap Streaming, and 512px/256px resolution caps.
    /// 2. Material deduplication & GPU Instancing enablement across props and campus shell.
    /// 3. Shared Color Atlas material consolidation for untextured solid-color PBR props.
    /// </summary>
    public static class MaterialAndAtlasConsolidator
    {
        private const string ConsolidatedFolder = "Assets/Materials/Consolidated";
        private const string ColorAtlasTexPath = ConsolidatedFolder + "/CoachingColorAtlas.png";
        private const string ColorAtlasMatPath = ConsolidatedFolder + "/CoachingColorAtlas.mat";

        public static void OptimizeTextureImportersAndMipStreaming()
        {
            string[] searchRoots =
            {
                "Assets/Art/CoachingMap",
                "Assets/Art/Props",
                "Assets/Art/Characters"
            };

            var existingRoots = new List<string>();
            foreach (var r in searchRoots)
                if (AssetDatabase.IsValidFolder(r)) existingRoots.Add(r);

            if (existingRoots.Count == 0) return;

            var guids = AssetDatabase.FindAssets("t:Texture2D", existingRoots.ToArray());
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                bool isSmallOverlay = path.IndexOf("Sign", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      path.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      path.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0;
                int targetSize = isSmallOverlay ? 256 : 512;

                bool changed = false;
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
                if (!ti.streamingMipmaps) { ti.streamingMipmaps = true; changed = true; }

                var androidSettings = ti.GetPlatformTextureSettings("Android");
                if (!androidSettings.overridden ||
                    androidSettings.maxTextureSize != targetSize ||
                    androidSettings.format != TextureImporterFormat.ASTC_6x6)
                {
                    androidSettings.name = "Android";
                    androidSettings.overridden = true;
                    androidSettings.maxTextureSize = targetSize;
                    androidSettings.format = TextureImporterFormat.ASTC_6x6;
                    androidSettings.compressionQuality = 60;
                    ti.SetPlatformTextureSettings(androidSettings);
                    changed = true;
                }

                if (changed) ti.SaveAndReimport();
            }
        }

        public static int ConsolidateMaterialsAndEnableInstancing(IEnumerable<GameObject> propPrefabs, GameObject shellRoot)
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder(ConsolidatedFolder);

            var canonicalMap = new Dictionary<string, Material>(64);
            var colorTable = new List<Color32>();
            var atlasLookup = new Dictionary<uint, int>();

            var allRenderers = new List<MeshRenderer>(256);
            if (propPrefabs != null)
            {
                foreach (var p in propPrefabs)
                {
                    if (p != null) allRenderers.AddRange(p.GetComponentsInChildren<MeshRenderer>(true));
                }
            }
            if (shellRoot != null)
                allRenderers.AddRange(shellRoot.GetComponentsInChildren<MeshRenderer>(true));

            int remappedSlots = 0;
            for (int i = 0; i < allRenderers.Count; i++)
            {
                var mr = allRenderers[i];
                if (mr == null) continue;
                var mats = mr.sharedMaterials;
                bool slotChanged = false;

                for (int s = 0; s < mats.Length; s++)
                {
                    var mat = mats[s];
                    if (mat == null) continue;

                    if (mat.enableInstancing == false)
                    {
                        mat.enableInstancing = true;
                        EditorUtility.SetDirty(mat);
                    }

                    string sig = ComputeMaterialSignature(mat);
                    if (!canonicalMap.TryGetValue(sig, out var canonical))
                    {
                        canonical = mat;
                        canonical.enableInstancing = true;
                        canonicalMap[sig] = canonical;
                    }
                    else if (canonical != mat)
                    {
                        mats[s] = canonical;
                        slotChanged = true;
                        remappedSlots++;
                    }

                    if (IsSolidColorPbrCandidate(canonical, out Color32 col32))
                    {
                        uint key = ((uint)col32.r << 24) | ((uint)col32.g << 16) | ((uint)col32.b << 8) | col32.a;
                        if (!atlasLookup.ContainsKey(key))
                        {
                            atlasLookup[key] = colorTable.Count;
                            colorTable.Add(col32);
                        }
                    }
                }

                if (slotChanged)
                {
                    mr.sharedMaterials = mats;
                    EditorUtility.SetDirty(mr);
                }
            }

            if (colorTable.Count > 0)
                BuildSharedColorAtlasAsset(colorTable);

            return remappedSlots;
        }

        private static bool IsSolidColorPbrCandidate(Material mat, out Color32 col32)
        {
            col32 = new Color32(255, 255, 255, 255);
            if (mat == null || mat.shader == null) return false;
            Texture mainTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                            : (mat.HasProperty("baseColorTexture") ? mat.GetTexture("baseColorTexture") : mat.mainTexture);
            if (mainTex != null) return false;

            if (mat.HasProperty("_BaseColor"))
                col32 = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("baseColorFactor"))
                col32 = mat.GetColor("baseColorFactor");
            else
                col32 = mat.color;
            return true;
        }

        private static void BuildSharedColorAtlasAsset(List<Color32> colors)
        {
            const int atlasSize = 512;
            const int cellPx = 32;
            int colsPerRow = atlasSize / cellPx;

            var tex = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, mipChain: true);
            var px = new Color32[atlasSize * atlasSize];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 200, 200, 255);

            int count = Mathf.Min(colors.Count, colsPerRow * colsPerRow);
            for (int idx = 0; idx < count; idx++)
            {
                int gx = idx % colsPerRow;
                int gy = idx / colsPerRow;
                Color32 c = colors[idx];
                for (int y = 0; y < cellPx; y++)
                {
                    int row = (gy * cellPx + y) * atlasSize + gx * cellPx;
                    for (int x = 0; x < cellPx; x++)
                        px[row + x] = c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            string diskPath = Path.Combine(Directory.GetCurrentDirectory(), ColorAtlasTexPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(diskPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(ColorAtlasTexPath, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(ColorAtlasTexPath) as TextureImporter;
            if (ti != null)
            {
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.streamingMipmaps = true;
                var android = ti.GetPlatformTextureSettings("Android");
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = 256;
                android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
                ti.SaveAndReimport();
            }

            var loadedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorAtlasTexPath);
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var atlasMat = AssetDatabase.LoadAssetAtPath<Material>(ColorAtlasMatPath);
            if (atlasMat == null)
            {
                atlasMat = new Material(lit);
                AssetDatabase.CreateAsset(atlasMat, ColorAtlasMatPath);
            }
            atlasMat.shader = lit;
            atlasMat.enableInstancing = true;
            if (atlasMat.HasProperty("_BaseMap")) atlasMat.SetTexture("_BaseMap", loadedTex);
            else atlasMat.mainTexture = loadedTex;
            EditorUtility.SetDirty(atlasMat);
        }

        private static string ComputeMaterialSignature(Material m)
        {
            string shName = m.shader != null ? m.shader.name : "null";
            int mainTexId = 0;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                mainTexId = m.GetTexture("_BaseMap").GetInstanceID();
            else if (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") != null)
                mainTexId = m.GetTexture("baseColorTexture").GetInstanceID();
            else if (m.mainTexture != null)
                mainTexId = m.mainTexture.GetInstanceID();

            Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                      : (m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m.color);
            float met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : (m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : 0f);
            float sm = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : (m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor") : 0f);
            return $"{shName}|{mainTexId}|{col.r:F2},{col.g:F2},{col.b:F2},{col.a:F2}|{met:F2}|{sm:F2}";
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
