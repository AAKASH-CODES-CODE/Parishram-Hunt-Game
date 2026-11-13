using System;
using System.Collections.Generic;
using System.IO;
using CoachingPropHunt.Core;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoachingPropHunt.Editor
{
    /// <summary>
    /// Editor utility that generates baked 3-tier LOD meshes (LOD0 / LOD1 ~45% / LOD2 ~15%)
    /// using fast grid vertex clustering with submesh, normal, and UV preservation,
    /// and attaches CrossFade LODGroups to prop prefabs for smooth mobile performance.
    /// Also guards Mobile_Renderer SSAO asset state across Editor PlayMode transitions.
    /// </summary>
    [InitializeOnLoad]
    public static class MeshLodUtility
    {
        private const string LodMeshesFolder = "Assets/Art/LODMeshes";

        static MeshLodUtility()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    RestoreRendererSsaoActive("Assets/Settings/Mobile_Renderer.asset");
                    RestoreRendererSsaoActive("Assets/Settings/PC_Renderer.asset");
                }
            };
        }

        public static void RestoreRendererSsaoActive(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null) return;
            foreach (var f in data.rendererFeatures)
            {
                if (f is ScreenSpaceAmbientOcclusion s && !s.isActive)
                    s.SetActive(true);
            }
        }

        public static void EnsureLodGroupOnPropRoot(GameObject propRoot, GameObject lod0Visual, string propId)
        {
            if (propRoot == null || lod0Visual == null) return;
            EnsureFolder("Assets/Art");
            EnsureFolder(LodMeshesFolder);

            var lod0Renderers = lod0Visual.GetComponentsInChildren<MeshRenderer>(true);
            if (lod0Renderers.Length == 0) return;

            int totalTris = 0;
            for (int i = 0; i < lod0Renderers.Length; i++)
            {
                var mf = lod0Renderers[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    totalTris += mf.sharedMesh.triangles.Length / 3;
            }

            // Very tiny simple primitives (e.g. <= 48 tris) still get a 2-tier cull LODGroup without inflating assets
            var lod1Renderers = new List<Renderer>(lod0Renderers.Length);
            var lod2Renderers = new List<Renderer>(lod0Renderers.Length);

            if (totalTris > 48)
            {
                var lod1Root = BuildLodTierClone(lod0Visual, propRoot.transform, "LOD1_Visual", propId, 1, gridCells: 9, lod0Renderers, lod1Renderers);
                var lod2Root = BuildLodTierClone(lod0Visual, propRoot.transform, "LOD2_Visual", propId, 2, gridCells: 4, lod0Renderers, lod2Renderers);
                if (lod1Root != null) lod1Root.transform.localPosition = lod0Visual.transform.localPosition;
                if (lod2Root != null) lod2Root.transform.localPosition = lod0Visual.transform.localPosition;
            }

            var lg = propRoot.GetComponent<LODGroup>();
            if (lg == null) lg = propRoot.AddComponent<LODGroup>();
            lg.fadeMode = LODFadeMode.CrossFade;
            lg.animateCrossFading = true;

            var baseList = new Renderer[lod0Renderers.Length];
            for (int i = 0; i < lod0Renderers.Length; i++) baseList[i] = lod0Renderers[i];

            if (lod1Renderers.Count > 0 && lod2Renderers.Count > 0)
            {
                lg.SetLODs(new[]
                {
                    new LOD(0.28f, baseList),
                    new LOD(0.11f, lod1Renderers.ToArray()),
                    new LOD(0.03f, lod2Renderers.ToArray())
                });
            }
            else if (lod1Renderers.Count > 0)
            {
                lg.SetLODs(new[]
                {
                    new LOD(0.24f, baseList),
                    new LOD(0.03f, lod1Renderers.ToArray())
                });
            }
            else
            {
                lg.SetLODs(new[]
                {
                    new LOD(0.035f, baseList)
                });
            }
            lg.RecalculateBounds();
        }

        private static GameObject BuildLodTierClone(
            GameObject sourceVisual,
            Transform parent,
            string cloneName,
            string propId,
            int tierIndex,
            int gridCells,
            MeshRenderer[] sourceRenderers,
            List<Renderer> outRenderers)
        {
            var clone = UnityEngine.Object.Instantiate(sourceVisual, parent, false);
            clone.name = cloneName;
            clone.transform.localRotation = sourceVisual.transform.localRotation;
            clone.transform.localScale = sourceVisual.transform.localScale;

            var cloneRenderers = clone.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < cloneRenderers.Length; i++)
            {
                var mr = cloneRenderers[i];
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                mr.shadowCastingMode = tierIndex >= 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                mr.receiveShadows = true;

                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Mesh simplified = SimplifyMeshByVertexClustering(mf.sharedMesh, gridCells, $"{propId}_LOD{tierIndex}_{i}");
                    if (simplified != null) mf.sharedMesh = simplified;
                }
                outRenderers.Add(mr);
            }
            return clone;
        }

        public static Mesh SimplifyMeshByVertexClustering(Mesh source, int gridResolution, string assetKey)
        {
            if (source == null) return null;
            int srcTriCount = source.triangles.Length / 3;
            if (srcTriCount <= 24) return source;

            string safeKey = SanitizeFileName(assetKey);
            string meshPath = $"{LodMeshesFolder}/{safeKey}.asset";

            using var readData = Mesh.AcquireReadOnlyMeshData(source);
            var meshData = readData[0];
            int vertCount = meshData.vertexCount;
            if (vertCount == 0) return source;

            using var verts = new NativeArray<Vector3>(vertCount, Allocator.Temp);
            meshData.GetVertices(verts);

            using var normals = new NativeArray<Vector3>(vertCount, Allocator.Temp);
            bool hasNormals = source.HasVertexAttribute(VertexAttribute.Normal);
            if (hasNormals)
                meshData.GetNormals(normals);

            using var uvs = new NativeArray<Vector2>(vertCount, Allocator.Temp);
            bool hasUv = source.HasVertexAttribute(VertexAttribute.TexCoord0);
            if (hasUv) meshData.GetUVs(0, uvs);

            Bounds b = source.bounds;
            Vector3 size = new Vector3(
                Mathf.Max(0.001f, b.size.x),
                Mathf.Max(0.001f, b.size.y),
                Mathf.Max(0.001f, b.size.z));

            int res = Mathf.Clamp(gridResolution, 3, 24);
            var cellToNewIdx = new Dictionary<long, int>(vertCount / 2);
            var oldToNew = new int[vertCount];
            var accumPos = new List<Vector3>(vertCount / 2);
            var accumNrm = new List<Vector3>(vertCount / 2);
            var accumUv = new List<Vector2>(vertCount / 2);
            var accumCount = new List<int>(vertCount / 2);

            for (int i = 0; i < vertCount; i++)
            {
                Vector3 norm = new Vector3(
                    (verts[i].x - b.min.x) / size.x,
                    (verts[i].y - b.min.y) / size.y,
                    (verts[i].z - b.min.z) / size.z);
                int gx = Mathf.Clamp(Mathf.FloorToInt(norm.x * res), 0, res);
                int gy = Mathf.Clamp(Mathf.FloorToInt(norm.y * res), 0, res);
                int gz = Mathf.Clamp(Mathf.FloorToInt(norm.z * res), 0, res);
                long key = ((long)gx << 40) | ((long)gy << 20) | (uint)gz;

                Vector3 srcNrm = hasNormals ? normals[i] : Vector3.up;
                Vector2 srcUv = hasUv ? uvs[i] : Vector2.zero;
                if (!cellToNewIdx.TryGetValue(key, out int ni))
                {
                    ni = accumPos.Count;
                    cellToNewIdx[key] = ni;
                    accumPos.Add(verts[i]);
                    accumNrm.Add(srcNrm);
                    accumUv.Add(srcUv);
                    accumCount.Add(1);
                }
                else
                {
                    accumPos[ni] += verts[i];
                    accumNrm[ni] += srcNrm;
                    accumUv[ni] += srcUv;
                    accumCount[ni] += 1;
                }
                oldToNew[i] = ni;
            }

            if (accumPos.Count >= vertCount * 0.92f)
                return source; // Mesh already coarse enough

            for (int i = 0; i < accumPos.Count; i++)
            {
                float inv = 1f / accumCount[i];
                accumPos[i] *= inv;
                Vector3 n = accumNrm[i] * inv;
                accumNrm[i] = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.up;
                accumUv[i] *= inv;
            }

            int subCount = source.subMeshCount;
            var newSubTris = new List<int>[subCount];
            var seenTris = new HashSet<long>();
            int totalKeptTris = 0;

            for (int s = 0; s < subCount; s++)
            {
                var desc = source.GetSubMesh(s);
                using var idxArr = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                meshData.GetIndices(idxArr, s);
                var outTris = new List<int>(desc.indexCount);

                for (int t = 0; t + 2 < idxArr.Length; t += 3)
                {
                    int a = oldToNew[idxArr[t]];
                    int c = oldToNew[idxArr[t + 1]];
                    int d = oldToNew[idxArr[t + 2]];
                    if (a == c || c == d || a == d) continue;

                    // Deduplicate identical clustered triangles regardless of cyclic order
                    int minV = Mathf.Min(a, Mathf.Min(c, d));
                    int maxV = Mathf.Max(a, Mathf.Max(c, d));
                    int midV = a + c + d - minV - maxV;
                    long triHash = ((long)minV << 42) | ((long)midV << 21) | (uint)maxV;
                    if (!seenTris.Add(triHash)) continue;

                    outTris.Add(a);
                    outTris.Add(c);
                    outTris.Add(d);
                    totalKeptTris++;
                }
                newSubTris[s] = outTris;
            }

            if (totalKeptTris == 0) return source;

            var lodMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (lodMesh == null)
            {
                lodMesh = new Mesh();
                AssetDatabase.CreateAsset(lodMesh, meshPath);
            }
            lodMesh.Clear();
            lodMesh.name = safeKey;
            lodMesh.indexFormat = accumPos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            lodMesh.SetVertices(accumPos);
            lodMesh.SetNormals(accumNrm);
            if (hasUv) lodMesh.SetUVs(0, accumUv);
            lodMesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
                lodMesh.SetTriangles(newSubTris[s], s, calculateBounds: false);
            lodMesh.RecalculateBounds();
            EditorUtility.SetDirty(lodMesh);
            return lodMesh;
        }

        private static string SanitizeFileName(string raw)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c, '_');
            return raw.Replace(' ', '_');
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
