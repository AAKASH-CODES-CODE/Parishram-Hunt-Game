using System;
using System.Collections.Generic;
using System.IO;
using CoachingPropHunt.CameraRig;
using CoachingPropHunt.Core;
using CoachingPropHunt.Networking;
using CoachingPropHunt.Props;
using CoachingPropHunt.UI;
using TMPro;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CoachingPropHunt.Editor
{
    public static class CoachingMapImporter
    {
        private const string ManifestPath = "Assets/Art/CoachingMap/map_props_layout.json";
        private const string PrefabsFolder = "Assets/Prefabs/Props";
        private const string RegistryAssetPath = "Assets/Data/PropRegistry.asset";
        private const string SkyMatPath = "Assets/Materials/CoachingCampusSky.mat";
        private const string ScenePath = "Assets/Scenes/CoachingCampus.unity";
        private const string ResourcesKeepFolder = "Assets/Resources";

        [Serializable]
        private class PropTypeJson
        {
            public string id;
            public string glb;
            public int maxHealth;
            public float moveSpeedMultiplier;
            public float[] sizeBlender;
        }

        [Serializable]
        private class PlacementJson
        {
            public string propId;
            public float[] blenderPos;
            public float yawDeg;
            public string room;
        }

        [Serializable]
        private class SpawnsJson
        {
            public float[][] hunterSpawns;
        }

        [Serializable]
        private class ManifestJson
        {
            public string shellGlb;
            public PropTypeJson[] propTypes;
            public PlacementJson[] placements;
        }

        [MenuItem("CoachingPropHunt/Build Full Coaching Map & Scene")]
        public static void BuildAllFromMenu()
        {
            BuildAll();
        }

        [MenuItem("CoachingPropHunt/Rebuild Character Prefabs Only")]
        public static void RebuildCharacterPrefabsFromMenu()
        {
            AssetDatabase.Refresh();
            var used = new HashSet<Shader>();
            BuildCharacterPrefabs(used);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static string BuildAll()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException($"Missing manifest: {ManifestPath}");

            string rawJson = File.ReadAllText(ManifestPath);
            var manifest = JsonUtility.FromJson<ManifestJson>(rawJson);
            ParseSpawnsManual(rawJson, out List<Vector3> hunterBlenderSpawns, out List<Vector3> propBlenderSpawns);

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabsFolder);
            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Scenes");
            EnsureFolder(ResourcesKeepFolder);

            // 1. Apply branding ("Parishram Hunt" + cropped app icon), texture optimization & grounded LOD prefabs
            ApplyProjectBranding();
            MaterialAndAtlasConsolidator.OptimizeTextureImportersAndMipStreaming();
            AssetDatabase.Refresh();
            var usedShaders = new HashSet<Shader>();
            var propPrefabs = new List<GameObject>();
            var definitions = BuildPropPrefabs(manifest, usedShaders, propPrefabs);
            BuildCharacterPrefabs(usedShaders);
            var registry = CreateOrUpdateRegistry(definitions);

            // 2. Preserve glTFast & particle shaders in Resources so Android IL2CPP builds never strip them
            PreserveCriticalShaders(manifest.shellGlb, usedShaders);
            EnsureRendererSSAO("Assets/Settings/Mobile_Renderer.asset");
            EnsureRendererSSAO("Assets/Settings/PC_Renderer.asset");

            // 3. Build CoachingCampus scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = CreateOrUpdateSkyboxMaterial();
            ConfigureLightingAndEnvironment();

            var mainCamGo = new GameObject("Main Camera");
            mainCamGo.tag = "MainCamera";
            var mainCam = mainCamGo.AddComponent<Camera>();
            mainCam.clearFlags = CameraClearFlags.Skybox;
            mainCam.nearClipPlane = 0.08f;
            mainCam.farClipPlane = 140f;
            mainCam.fieldOfView = 58f;
            mainCamGo.transform.position = new Vector3(3.8f, 5.6f, 20.5f);
            mainCamGo.transform.LookAt(new Vector3(0.4f, 3.1f, 5.2f));
            var urpCam = mainCamGo.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = true;
            mainCamGo.AddComponent<AudioListener>();

            BuildSunAndPostProcessing();
            BuildAmbientDustMotes();

            var systemsGo = BuildSystemsRoot();
            var gsm = systemsGo.GetComponent<GameStateManager>();

            var mapRoot = new GameObject("CoachingCampusMap");
            var shellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.shellGlb);
            if (shellPrefab == null)
                throw new InvalidOperationException($"Failed to load shell GLB at {manifest.shellGlb}");

            var shellInst = (GameObject)PrefabUtility.InstantiatePrefab(shellPrefab, mapRoot.transform);
            shellInst.name = "CoachingCenter_Shell";
            if (PrefabUtility.IsPartOfPrefabInstance(shellInst))
                PrefabUtility.UnpackPrefabInstance(shellInst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            ConfigureShellCollidersAndStaticFlags(shellInst);
            SetupCeilingFanSpinners(shellInst);
            BuildExteriorGateBarriers(mapRoot.transform);
            CreateRoomNamePlaques(mapRoot.transform);

            AddInteriorLightingAndProbes(mapRoot.transform);

            // Build Floor 0 (Ground) and Floor 1 (First Floor) modular hierarchies
            var gfModule = CreateFloorModule("Floor_0_Ground", 0, "Ground Floor", 0f, mapRoot.transform);
            var f1Module = CreateFloorModule("Floor_1_First", 1, "First Floor", 4.0f, mapRoot.transform);

            var hunterTransforms = SpawnMarkers("HunterSpawns", hunterBlenderSpawns, gfModule.transform, f1Module.transform);
            var propTransforms = SpawnMarkers("PropSpawns", propBlenderSpawns, gfModule.transform, f1Module.transform);

            PlaceAllMapProps(manifest.placements, registry, gfModule.transform, f1Module.transform);
            MaterialAndAtlasConsolidator.ConsolidateMaterialsAndEnableInstancing(propPrefabs, shellInst);
            PopulateCampusSectorStreamer(systemsGo.GetComponent<CampusSectorStreamer>(), mapRoot, shellInst);

            // Bake NavMesh covering Ground Floor + Stairs + First Floor
            var navSurface = mapRoot.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navSurface.BuildNavMesh();

            gsm.ConfigureMapData(registry, hunterTransforms.ToArray(), propTransforms.ToArray());
            EditorUtility.SetDirty(gsm);

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings(ScenePath);

            // Bake Static Occlusion Culling umbra tree so walls/floors occlude behind-wall props
            StaticOcclusionCulling.smallestOccluder = 1.5f;
            StaticOcclusionCulling.smallestHole = 0.25f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            bool occOk = StaticOcclusionCulling.Compute();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"OCCLUSION_BAKE: ok={occOk}, umbraBytes={StaticOcclusionCulling.umbraDataSize}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return $"BUILD_SCENE_OK: propTypes={definitions.Count}, placements={manifest.placements.Length}, hunterSpawns={hunterTransforms.Count}, propSpawns={propTransforms.Count}";
        }

        private static List<PropDefinition> BuildPropPrefabs(ManifestJson manifest, HashSet<Shader> usedShaders, List<GameObject> createdPrefabs)
        {
            var list = new List<PropDefinition>();
            if (manifest.propTypes == null) return list;

            foreach (var pt in manifest.propTypes)
            {
                var glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(pt.glb);
                if (glbAsset == null)
                {
                    Debug.LogWarning($"Skipping missing prop GLB: {pt.glb}");
                    continue;
                }

                var tempRoot = new GameObject(pt.id);
                var visualInst = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset, tempRoot.transform);
                visualInst.name = "Visual";
                visualInst.transform.localPosition = Vector3.zero;
                visualInst.transform.localRotation = Quaternion.identity;

                // Measure true world-space mesh bounds and shift Visual so min.y == 0.000f
                var renderers = visualInst.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = new Bounds();
                bool hasBounds = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var mr = renderers[i];
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat != null && mat.shader != null) usedShaders.Add(mat.shader);
                    }
                    if (!hasBounds)
                    {
                        bounds = mr.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(mr.bounds);
                    }
                }

                float minYOffset = hasBounds ? -bounds.min.y : 0f;
                visualInst.transform.localPosition = new Vector3(0f, minYOffset, 0f);
                Vector3 localCenter = hasBounds
                    ? new Vector3(bounds.center.x, bounds.center.y + minYOffset, bounds.center.z)
                    : new Vector3(0f, 0.25f, 0f);
                Vector3 size = hasBounds ? bounds.size : Vector3.one * 0.4f;
                size = new Vector3(Mathf.Max(0.08f, size.x), Mathf.Max(0.06f, size.y), Mathf.Max(0.08f, size.z));

                var box = tempRoot.AddComponent<BoxCollider>();
                box.center = localCenter;
                box.size = size;

                var ip = tempRoot.AddComponent<InteractiveProp>();
                ip.Configure(pt.id, "Prefab");

                MeshLodUtility.EnsureLodGroupOnPropRoot(tempRoot, visualInst, pt.id);

                string prefabPath = $"{PrefabsFolder}/{pt.id}.prefab";
                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, prefabPath);
                if (savedPrefab != null && createdPrefabs != null) createdPrefabs.Add(savedPrefab);
                UnityEngine.Object.DestroyImmediate(tempRoot);

                float vol = size.x * size.y * size.z;
                PropSizeTier tier = vol < 0.03f ? PropSizeTier.Small : (vol < 0.28f ? PropSizeTier.Medium : PropSizeTier.Large);

                list.Add(new PropDefinition
                {
                    propId = pt.id,
                    displayName = PrettifyPropName(pt.id),
                    sizeTier = tier,
                    prefab = savedPrefab,
                    maxHealth = pt.maxHealth > 0 ? pt.maxHealth : 60,
                    moveSpeedMultiplier = pt.moveSpeedMultiplier > 0.1f ? pt.moveSpeedMultiplier : 1.0f,
                    boundsSize = size,
                    pivotBottomOffset = minYOffset
                });
            }
            return list;
        }

        private static PropRegistrySO CreateOrUpdateRegistry(List<PropDefinition> defs)
        {
            var reg = AssetDatabase.LoadAssetAtPath<PropRegistrySO>(RegistryAssetPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<PropRegistrySO>();
                AssetDatabase.CreateAsset(reg, RegistryAssetPath);
            }
            reg.SetEntries(defs);
            EditorUtility.SetDirty(reg);
            return reg;
        }

        private static void ConfigureShellCollidersAndStaticFlags(GameObject shellInst)
        {
            var filters = shellInst.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                var go = mf.gameObject;
                string n = go.name;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.receiveShadows = true;
                }

                // Keep ceiling fans dynamic so CoachingFanSpinner can rotate them cleanly
                bool isFan = n.IndexOf("Fan_", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isFan)
                {
                    GameObjectUtility.SetStaticEditorFlags(go,
                        StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.ContributeGI |
                        StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic);
                }
                else
                {
                    GameObjectUtility.SetStaticEditorFlags(go, 0);
                }

                // Skip thin non-blocking ceiling ornaments & tiny glass planes/signs
                bool skipCollider = n.Contains("TubeLight") || isFan || n.Contains("Sign") || n.Contains("Banner") || n.Contains("Whiteboard") || n.Contains("NoticeBoard");
                if (skipCollider) continue;

                if (go.GetComponent<Collider>() == null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                else if (go.GetComponent<Collider>() == null)
                {
                    go.AddComponent<BoxCollider>();
                }
            }
        }

        private static void SetupCeilingFanSpinners(GameObject shellInst)
        {
            var renderers = shellInst.GetComponentsInChildren<MeshRenderer>(true);
            var fanParts = new List<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.gameObject.name.IndexOf("Fan_", StringComparison.OrdinalIgnoreCase) >= 0)
                    fanParts.Add(r);
            }

            var visited = new bool[fanParts.Count];
            int fanIdx = 0;
            for (int i = 0; i < fanParts.Count; i++)
            {
                if (visited[i]) continue;
                visited[i] = true;
                var cluster = new List<MeshRenderer> { fanParts[i] };
                Bounds b = fanParts[i].bounds;

                // Merge hub & blades sharing same spatial ceiling fan (~0.95m XZ radius)
                bool expanded;
                do
                {
                    expanded = false;
                    for (int j = 0; j < fanParts.Count; j++)
                    {
                        if (visited[j]) continue;
                        Vector3 d = fanParts[j].bounds.center - b.center;
                        if (new Vector2(d.x, d.z).magnitude <= 1.05f && Mathf.Abs(d.y) <= 0.55f)
                        {
                            visited[j] = true;
                            cluster.Add(fanParts[j]);
                            b.Encapsulate(fanParts[j].bounds);
                            expanded = true;
                        }
                    }
                }
                while (expanded);

                var pivot = new GameObject($"CeilingFanPivot_{fanIdx++}");
                pivot.transform.SetParent(shellInst.transform, false);
                pivot.transform.position = b.center;
                for (int k = 0; k < cluster.Count; k++)
                {
                    cluster[k].transform.SetParent(pivot.transform, worldPositionStays: true);
                }
                var spinner = pivot.AddComponent<CoachingFanSpinner>();
                spinner.SetSpeed(205f + (fanIdx % 3) * 18f);
            }
        }

        private static void BuildExteriorGateBarriers(Transform mapRoot)
        {
            var root = new GameObject("GateExteriorBarriers");
            root.transform.SetParent(mapRoot, false);

            void AddWall(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = center;
                var col = go.AddComponent<BoxCollider>();
                col.size = size;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }

            // Enclose front paved gate apron perimeter so neither humans nor bots fall off the map edge
            AddWall("GF_Gate_Boundary_Front", new Vector3(0f, 2.5f, 17.05f), new Vector3(20.2f, 5.0f, 0.5f));
            AddWall("GF_Gate_Boundary_Left", new Vector3(-9.55f, 2.5f, 15.3f), new Vector3(0.5f, 5.0f, 4.2f));
            AddWall("GF_Gate_Boundary_Right", new Vector3(9.55f, 2.5f, 15.3f), new Vector3(0.5f, 5.0f, 4.2f));
        }

        private static void CreateRoomNamePlaques(Transform mapRoot)
        {
            var root = new GameObject("RoomNamePlaques");
            root.transform.SetParent(mapRoot, false);

            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            string plaqueMatPath = "Assets/Materials/RoomPlaqueMat.mat";
            var backMat = AssetDatabase.LoadAssetAtPath<Material>(plaqueMatPath);
            if (backMat == null)
            {
                backMat = new Material(lit);
                AssetDatabase.CreateAsset(backMat, plaqueMatPath);
            }
            backMat.shader = lit;
            backMat.color = Palette.HunterNavy;
            backMat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(backMat);

            var entries = new (string name, Vector3 pos, float yaw, string title, float w)[]
            {
                ("GF_Plaque_Gate",       new Vector3( 0.0f, 3.35f, 13.70f),   0f, "PARISHRAM COACHING", 2.8f),
                ("GF_Plaque_CycleStand", new Vector3(-4.8f, 2.68f,  9.40f),  90f, "CYCLE STAND",        1.55f),
                ("GF_Plaque_Office",     new Vector3( 4.6f, 2.68f, -4.15f),   0f, "OFFICE ROOM",        1.55f),
                ("1F_Plaque_BigClass",   new Vector3( 2.2f, 6.68f, -4.15f),   0f, "BIG CLASSROOM",      1.75f),
                ("1F_Plaque_Class2",     new Vector3(-1.6f, 6.68f, -8.85f),  90f, "CLASSROOM 2",        1.55f),
                ("1F_Plaque_SelfStudy",  new Vector3(-3.2f, 6.68f, -4.80f),  90f, "SELF STUDY",         1.50f),
                ("1F_Plaque_Staff",      new Vector3(-2.4f, 6.68f,  1.55f),   0f, "STAFF ROOM",         1.50f),
                ("1F_Plaque_Kitchen",    new Vector3( 4.6f, 6.68f,  1.55f),   0f, "KITCHEN",            1.30f),
                ("1F_Plaque_DoubtClass", new Vector3( 1.2f, 6.68f,  6.95f),   0f, "DOUBT CLASS",        1.60f)
            };

            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = e.name;
                UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
                board.transform.SetParent(root.transform, false);
                board.transform.position = e.pos;
                board.transform.rotation = Quaternion.Euler(0f, e.yaw, 0f);
                board.transform.localScale = new Vector3(e.w, 0.34f, 0.05f);
                board.GetComponent<MeshRenderer>().sharedMaterial = backMat;

                for (int side = 0; side < 2; side++)
                {
                    var txtGo = new GameObject(side == 0 ? "FrontText" : "BackText");
                    txtGo.transform.SetParent(board.transform, false);
                    txtGo.transform.localPosition = new Vector3(0f, 0f, side == 0 ? -0.58f : 0.58f);
                    txtGo.transform.localRotation = Quaternion.Euler(0f, side == 0 ? 0f : 180f, 0f);
                    // Counteract parent non-uniform scale so text stays crisp
                    txtGo.transform.localScale = new Vector3(1f / e.w, 1f / 0.34f, 1f / 0.05f);

                    var tmp = txtGo.AddComponent<TextMeshPro>();
                    tmp.text = e.title;
                    tmp.fontSize = 1.65f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = Palette.AccentGold;
                    tmp.rectTransform.sizeDelta = new Vector2(e.w - 0.1f, 0.32f);
                }
            }
        }

        private static void EnsureRendererSSAO(string rendererAssetPath)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererAssetPath);
            if (data == null) return;

            ScreenSpaceAmbientOcclusion existing = null;
            foreach (var f in data.rendererFeatures)
            {
                if (f is ScreenSpaceAmbientOcclusion s)
                {
                    existing = s;
                    break;
                }
            }

            var dataSo = new SerializedObject(data);
            dataSo.Update();
            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                existing.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(existing, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing, out _, out long localId);

                var featsProp = dataSo.FindProperty("m_RendererFeatures");
                featsProp.arraySize++;
                featsProp.GetArrayElementAtIndex(featsProp.arraySize - 1).objectReferenceValue = existing;

                var mapProp = dataSo.FindProperty("m_RendererFeatureMap");
                mapProp.arraySize++;
                mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue = localId;
                dataSo.ApplyModifiedProperties();
            }

            existing.SetActive(true);
            var ssaoSo = new SerializedObject(existing);
            ssaoSo.Update();
            var settings = ssaoSo.FindProperty("m_Settings");
            if (settings != null)
            {
                var down = settings.FindPropertyRelative("Downsample");
                if (down != null) down.boolValue = true;
                var inten = settings.FindPropertyRelative("Intensity");
                if (inten != null) inten.floatValue = 0.65f;
                var rad = settings.FindPropertyRelative("Radius");
                if (rad != null) rad.floatValue = 0.35f;

                void SetEnumByName(string rel, string targetName)
                {
                    var p = settings.FindPropertyRelative(rel);
                    if (p == null || p.enumNames == null) return;
                    int idx = Array.IndexOf(p.enumNames, targetName);
                    if (idx >= 0) p.enumValueIndex = idx;
                }
                SetEnumByName("Source", "Depth");
                SetEnumByName("Samples", "Low");
                SetEnumByName("BlurQuality", "Low");
            }
            ssaoSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(existing);
            EditorUtility.SetDirty(data);
        }

        private static void PlaceAllMapProps(PlacementJson[] placements, PropRegistrySO reg, Transform gfRoot, Transform f1Root)
        {
            if (placements == null) return;

            // Sort lower-elevation props first so tables/counters/benches settle before surface items snap onto them!
            var sorted = new List<PlacementJson>(placements);
            sorted.Sort((a, b) => a.blenderPos[2].CompareTo(b.blenderPos[2]));

            int floorStandingPass = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    var item = sorted[i];
                    float bz = item.blenderPos[2];
                    bool isElevatedItem = !(Mathf.Abs(bz) < 0.05f || Mathf.Abs(bz - 4.0f) < 0.05f || Mathf.Abs(bz - 4.254f) < 0.05f);
                    if ((pass == 0 && isElevatedItem) || (pass == 1 && !isElevatedItem)) continue;

                    var def = reg.GetById(item.propId);
                    if (def == null || def.prefab == null) continue;

                    Vector3 pos = BlenderToUnityPos(item.blenderPos[0], item.blenderPos[1], bz);
                    Quaternion rot = Quaternion.Euler(0f, -item.yawDeg, 0f);
                    Transform parent = bz >= 3.5f ? f1Root : gfRoot;

                    if (isElevatedItem)
                    {
                        // Snap flush onto underlying desk/bench/counter top collider
                        var probe = pos + Vector3.up * 0.25f;
                        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 0.85f, ~0, QueryTriggerInteraction.Ignore))
                            pos.y = hit.point.y + 0.002f;
                    }

                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(def.prefab, parent);
                    inst.name = $"{item.room}_{item.propId}_{i}";
                    inst.transform.SetPositionAndRotation(pos, rot);
                    var ip = inst.GetComponent<InteractiveProp>();
                    if (ip == null) ip = inst.AddComponent<InteractiveProp>();
                    ip.Configure(item.propId, item.room);

                    AssignPropLayerAndStaticFlags(inst, def.sizeTier);
                    floorStandingPass++;
                }
                if (pass == 0) Physics.SyncTransforms();
            }
        }

        private static void AssignPropLayerAndStaticFlags(GameObject inst, PropSizeTier tier)
        {
            int smallLayer = LayerMask.NameToLayer("SmallProp");
            int medLayer = LayerMask.NameToLayer("MediumProp");
            int targetLayer = 0;
            if (tier == PropSizeTier.Small && smallLayer >= 0) targetLayer = smallLayer;
            else if (tier == PropSizeTier.Medium && medLayer >= 0) targetLayer = medLayer;

            StaticEditorFlags lod0Flags = tier == PropSizeTier.Small
                ? (StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic)
                : (StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            StaticEditorFlags lodFarFlags = StaticEditorFlags.OccludeeStatic;

            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = targetLayer;
                bool isLodFar = t.name.StartsWith("LOD1_", StringComparison.Ordinal) ||
                                t.name.StartsWith("LOD2_", StringComparison.Ordinal) ||
                                (t.parent != null && (t.parent.name.StartsWith("LOD1_", StringComparison.Ordinal) || t.parent.name.StartsWith("LOD2_", StringComparison.Ordinal)));
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, isLodFar ? lodFarFlags : lod0Flags);
            }
        }

        private static void PopulateCampusSectorStreamer(CampusSectorStreamer streamer, GameObject mapRoot, GameObject shellInst)
        {
            if (streamer == null || mapRoot == null) return;

            var sectorOrder = new[]
            {
                ("GF_GateAndYard",          0, new Bounds(new Vector3(0f, 1.6f, 11f),  new Vector3(22f, 3.6f, 12f))),
                ("GF_HallAndCycle",         0, new Bounds(new Vector3(0f, 1.6f, 0.5f), new Vector3(20f, 3.6f, 10f))),
                ("GF_OfficeAndStairs",      0, new Bounds(new Vector3(0f, 1.6f, -9f),  new Vector3(20f, 3.6f, 11f))),
                ("1F_DoubtAndFront",        1, new Bounds(new Vector3(0f, 5.4f, 9.5f), new Vector3(20f, 4.0f, 11f))),
                ("1F_CorridorStaffKitchen", 1, new Bounds(new Vector3(0f, 5.4f, 0f),   new Vector3(20f, 4.0f, 9f))),
                ("1F_ClassroomsAndStudy",   1, new Bounds(new Vector3(0f, 5.4f, -9.5f),new Vector3(20f, 4.0f, 11f))),
            };

            var built = new Dictionary<string, CampusSectorStreamer.CampusSector>(8);
            var list = new List<CampusSectorStreamer.CampusSector>(8);
            foreach (var spec in sectorOrder)
            {
                var sec = new CampusSectorStreamer.CampusSector
                {
                    sectorId = spec.Item1,
                    floorIndex = spec.Item2,
                    bounds = spec.Item3,
                    neighborIds = CampusSectorStreamer.GetDefaultNeighbors(spec.Item1)
                };
                built[sec.sectorId] = sec;
                list.Add(sec);
            }

            CampusSectorStreamer.CampusSector ResolveSector(Vector3 pos)
            {
                string id = CampusSectorStreamer.ClassifySectorId(pos);
                return built.TryGetValue(id, out var s) ? s : list[0];
            }

            // Group placed props by sector
            foreach (var ip in mapRoot.GetComponentsInChildren<InteractiveProp>(true))
            {
                var sec = ResolveSector(ip.transform.position);
                foreach (var mr in ip.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!sec.renderers.Contains(mr)) sec.renderers.Add(mr);
                    sec.bounds.Encapsulate(mr.bounds);
                }
            }

            // Group compact interior ceiling fans and plaques by sector
            foreach (var fan in mapRoot.GetComponentsInChildren<CoachingFanSpinner>(true))
            {
                var sec = ResolveSector(fan.transform.position);
                sec.fans.Add(fan);
                foreach (var mr in fan.GetComponentsInChildren<MeshRenderer>(true))
                    if (!sec.renderers.Contains(mr)) sec.renderers.Add(mr);
            }

            var plaqueRoot = GameObject.Find("RoomNamePlaques");
            if (plaqueRoot != null)
            {
                foreach (var r in plaqueRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var sec = ResolveSector(r.bounds.center);
                    if (!sec.renderers.Contains(r)) sec.renderers.Add(r);
                }
            }

            foreach (var l in mapRoot.GetComponentsInChildren<Light>(true))
            {
                if (l.type != LightType.Point) continue;
                var sec = ResolveSector(l.transform.position);
                if (!sec.lights.Contains(l)) sec.lights.Add(l);
            }

            // Add compact interior shell sub-meshes (< 10m extent) to sector culling while leaving large spanning slabs always active
            if (shellInst != null)
            {
                foreach (var mr in shellInst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.GetComponentInParent<CoachingFanSpinner>() != null) continue;
                    if (mr.bounds.size.magnitude < 10.5f)
                    {
                        var sec = ResolveSector(mr.bounds.center);
                        if (!sec.renderers.Contains(mr)) sec.renderers.Add(mr);
                    }
                }
            }

            var gfRoot = GameObject.Find("Floor_0_Ground");
            if (gfRoot != null) built["GF_HallAndCycle"].staticBatchRoots.Add(gfRoot);
            var f1Root = GameObject.Find("Floor_1_First");
            if (f1Root != null) built["1F_ClassroomsAndStudy"].staticBatchRoots.Add(f1Root);

            streamer.SetSectorsFromEditor(list);
            EditorUtility.SetDirty(streamer);
        }

        private static List<Transform> SpawnMarkers(string groupName, List<Vector3> blenderPts, Transform gfRoot, Transform f1Root)
        {
            var result = new List<Transform>();
            var gfHolder = new GameObject(groupName + "_GF").transform;
            gfHolder.SetParent(gfRoot, false);
            var f1Holder = new GameObject(groupName + "_1F").transform;
            f1Holder.SetParent(f1Root, false);

            for (int i = 0; i < blenderPts.Count; i++)
            {
                Vector3 bp = blenderPts[i];
                Vector3 up = BlenderToUnityPos(bp.x, bp.y, bp.z);
                Transform parent = bp.z >= 3.5f ? f1Holder : gfHolder;
                var m = new GameObject($"{groupName}_{i}").transform;
                m.SetParent(parent, false);
                m.position = up;
                m.rotation = Quaternion.Euler(0f, bp.z < 1f ? 180f : 0f, 0f);
                result.Add(m);
            }
            return result;
        }

        private static CoachingFloorModule CreateFloorModule(string name, int idx, string label, float elevY, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mod = go.AddComponent<CoachingFloorModule>();
            mod.Configure(idx, label, elevY, null, null);
            return mod;
        }

        private static GameObject BuildSystemsRoot()
        {
            var netPlayerPrefab = EnsureNetworkPlayerPrefab();
            var netPrefabList = EnsureNetworkPrefabsList(netPlayerPrefab);

            var go = new GameObject("GameSystems");
            go.AddComponent<GameStateManager>();
            go.AddComponent<ObjectPoolManager>();
            go.AddComponent<CampusSectorStreamer>();
            go.AddComponent<AudioSynthManager>();
            go.AddComponent<MultiplayerRoomManager>();
            go.AddComponent<MatchCoordinator>();
            go.AddComponent<PlayerCameraController>();
            go.AddComponent<GameUIManager>();

            // Separate NetworkSessionManager keeps NetworkManager DontDestroyOnLoad from dragging GameSystems across scene reloads!
            var nmGo = new GameObject("NetworkSessionManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            transport.ConnectTimeoutMS = 2000;
            transport.MaxConnectAttempts = 30;
            transport.HeartbeatTimeoutMS = 1500;
            transport.DisconnectTimeoutMS = 45000;
            transport.MaxPayloadSize = 16384;
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = netPlayerPrefab,
                ForceSamePrefabs = true,
                AutoSpawnPlayerPrefabClientSide = true
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(netPrefabList);

            var syncGo = new GameObject("NetworkMatchSync");
            syncGo.AddComponent<NetworkObject>();
            syncGo.AddComponent<NetworkMatchSync>();
            return go;
        }

        private static GameObject EnsureNetworkPlayerPrefab()
        {
            const string path = "Assets/Prefabs/NetworkPlayer.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<NetworkObject>() != null && existing.GetComponent<NetworkPlayerState>() != null)
                return existing;

            var temp = new GameObject("NetworkPlayer");
            temp.AddComponent<NetworkObject>();
            temp.AddComponent<NetworkPlayerState>();
            var saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            return saved;
        }

        private static NetworkPrefabsList EnsureNetworkPrefabsList(GameObject playerPrefab)
        {
            const string path = "Assets/Data/DefaultNetworkPrefabs.asset";
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }
            bool contains = false;
            foreach (var np in list.PrefabList)
            {
                if (np != null && np.Prefab == playerPrefab)
                {
                    contains = true;
                    break;
                }
            }
            if (!contains && playerPrefab != null)
            {
                list.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(list);
            }
            return list;
        }

        private static Material CreateOrUpdateSkyboxMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, SkyMatPath);
            }
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", Palette.SkyZenith);
            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", Palette.CampusGround);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 0.85f);
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.15f);
            if (mat.HasProperty("_SunSize")) mat.SetFloat("_SunSize", 0.045f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigureLightingAndEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.FillCool * 0.92f;
            RenderSettings.ambientEquatorColor = Palette.SkyHorizon * 0.72f;
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.22f, 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Palette.SkyHorizon * 0.88f;
            RenderSettings.fogDensity = 0.0055f;

            var pcRp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (pcRp != null)
            {
                pcRp.supportsHDR = true;
                pcRp.shadowDistance = 58f;
                pcRp.renderScale = 1.0f;
                pcRp.msaaSampleCount = 2;
                EditorUtility.SetDirty(pcRp);
            }

            var mobileRp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobileRp != null)
            {
                mobileRp.supportsHDR = false;
                mobileRp.shadowDistance = 36f;
                mobileRp.renderScale = 0.90f;
                mobileRp.msaaSampleCount = 1;
                mobileRp.maxAdditionalLightsCount = 2;
                EditorUtility.SetDirty(mobileRp);
            }

            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null && urpAsset != mobileRp)
            {
                urpAsset.supportsHDR = true;
                urpAsset.shadowDistance = 58f;
                urpAsset.renderScale = 1.0f;
                urpAsset.msaaSampleCount = 2;
                EditorUtility.SetDirty(urpAsset);
            }
        }

        private static void BuildSunAndPostProcessing()
        {
            var sunGo = new GameObject("SunLight_GoldenHour");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunWarm;
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sunGo.transform.rotation = Quaternion.Euler(42f, -34f, 0f);

            var volGo = new GameObject("GlobalVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile == null) profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;
        }

        private static void AddInteriorLightingAndProbes(Transform parent)
        {
            // Warm interior ceiling fills across Ground Floor & 1st Floor rooms (zero shadow cost on mobile!)
            var lightRoot = new GameObject("InteriorFillLights").transform;
            lightRoot.SetParent(parent, false);
            var points = new (string name, Vector3 pos, float range, float intensity)[]
            {
                ("GF_FrontGate_Fill", new Vector3(0f, 3.25f, 9.5f), 14f, 1.35f),
                ("GF_CycleStand_Fill", new Vector3(-5.0f, 3.25f, -1.5f), 14f, 1.25f),
                ("GF_Room_Fill", new Vector3(6.0f, 3.25f, -9.0f), 10f, 1.15f),
                ("GF_Stairs_Fill", new Vector3(5.2f, 2.75f, -0.5f), 10f, 1.15f),
                ("F1_BigClass_Fill", new Vector3(4.4f, 7.15f, -9.0f), 13f, 1.40f),
                ("F1_SmallClass_Fill", new Vector3(-4.2f, 7.15f, -11.3f), 10f, 1.30f),
                ("F1_Study_Fill", new Vector3(-5.8f, 7.15f, -6.2f), 10f, 1.25f),
                ("F1_Corridor_Fill", new Vector3(-0.5f, 7.15f, -1.2f), 14f, 1.35f),
                ("F1_Staff_Fill", new Vector3(-4.8f, 7.15f, 4.2f), 10f, 1.20f),
                ("F1_Kitchen_Fill", new Vector3(6.2f, 7.15f, 4.4f), 9f, 1.20f),
                ("F1_DoubtClass_Fill", new Vector3(-2.0f, 7.15f, 10.2f), 13f, 1.40f),
                ("F1_Room_Fill", new Vector3(6.2f, 7.15f, 10.2f), 9f, 1.15f)
            };
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                var lGo = new GameObject(p.name);
                lGo.transform.SetParent(lightRoot, false);
                lGo.transform.position = p.pos;
                var l = lGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = Palette.SunWarm;
                l.range = p.range;
                l.intensity = p.intensity;
                l.shadows = LightShadows.None;
            }

            // Dense 2-Floor LightProbeGroup so morphed player props shade identically to real map props
            var probeGo = new GameObject("CampusLightProbes");
            probeGo.transform.SetParent(parent, false);
            var lpg = probeGo.AddComponent<LightProbeGroup>();
            var probePos = new List<Vector3>();
            float[] ys = { 0.6f, 2.4f, 4.6f, 6.4f };
            for (int iy = 0; iy < ys.Length; iy++)
            {
                for (float x = -7.5f; x <= 7.6f; x += 3.0f)
                {
                    for (float z = -12.5f; z <= 12.6f; z += 4.2f)
                        probePos.Add(new Vector3(x, ys[iy], z));
                }
            }
            lpg.probePositions = probePos.ToArray();
        }

        private static void BuildAmbientDustMotes()
        {
            var go = new GameObject("AmbientDustMotes");
            go.transform.position = new Vector3(0f, 4.0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = Palette.DustMoteCream;
            main.maxParticles = 24;

            var emission = ps.emission;
            emission.rateOverTime = 2.6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(15f, 6.5f, 24f);

            var psr = go.GetComponent<ParticleSystemRenderer>();
            string dustMatPath = "Assets/Materials/DustMoteMat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(dustMatPath);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, dustMatPath);
            }
            mat.shader = sh;
            mat.color = Palette.DustMoteCream;
            EditorUtility.SetDirty(mat);
            psr.sharedMaterial = mat;
        }

        private static void PreserveCriticalShaders(string shellGlb, HashSet<Shader> usedShaders)
        {
            var shell = AssetDatabase.LoadAssetAtPath<GameObject>(shellGlb);
            if (shell != null)
            {
                foreach (var mr in shell.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (var m in mr.sharedMaterials)
                    {
                        if (m != null && m.shader != null) usedShaders.Add(m.shader);
                    }
                }
            }
            var extraShaders = new[] { "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Universal Render Pipeline/Lit", "Skybox/Procedural", "TextMeshPro/Mobile/Distance Field" };
            foreach (var sname in extraShaders)
            {
                var sh = Shader.Find(sname);
                if (sh != null) usedShaders.Add(sh);
            }

            int idx = 0;
            foreach (var sh in usedShaders)
            {
                if (sh == null) continue;
                string matPath = $"{ResourcesKeepFolder}/KeepShader_{idx++}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing == null)
                {
                    existing = new Material(sh);
                    AssetDatabase.CreateAsset(existing, matPath);
                }
                else
                {
                    existing.shader = sh;
                    EditorUtility.SetDirty(existing);
                }
            }
        }

        private static void BuildCharacterPrefabs(HashSet<Shader> usedShaders)
        {
            const string charResFolder = "Assets/Resources/Characters";
            EnsureFolder(ResourcesKeepFolder);
            EnsureFolder(charResFolder);

            var specs = new (string glbPath, string prefabName, bool groundToZero)[]
            {
                ("Assets/Art/Characters/Hunter_Character.glb", "HunterCharacter", true),
                ("Assets/Art/Characters/Prop_Student_Character.glb", "PropStudentCharacter", true),
                ("Assets/Art/Characters/Hunter_Blaster.glb", "HunterBlaster", false)
            };

            foreach (var s in specs)
            {
                var glb = AssetDatabase.LoadAssetAtPath<GameObject>(s.glbPath);
                if (glb == null)
                {
                    Debug.LogWarning($"Character GLB missing at {s.glbPath}; skipping.");
                    continue;
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(glb);
                if (PrefabUtility.IsPartOfPrefabInstance(inst))
                    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = s.prefabName;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;

                // Strip any generated colliders & align lighting probes cleanly
                foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(c);

                var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
                Bounds totalB = new Bounds();
                bool hasB = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var mr = renderers[i];
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat != null && mat.shader != null) usedShaders.Add(mat.shader);
                    }
                    if (!hasB)
                    {
                        totalB = mr.bounds;
                        hasB = true;
                    }
                    else
                    {
                        totalB.Encapsulate(mr.bounds);
                    }
                }

                if (s.groundToZero && hasB && Mathf.Abs(totalB.min.y) > 0.002f)
                {
                    for (int c = 0; c < inst.transform.childCount; c++)
                        inst.transform.GetChild(c).position += new Vector3(0f, -totalB.min.y, 0f);
                }

                string dstPath = $"{charResFolder}/{s.prefabName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, dstPath);
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        private static Vector3 BlenderToUnityPos(float bx, float by, float bz)
        {
            return new Vector3(-bx, bz, -by);
        }

        private static string PrettifyPropName(string propId)
        {
            return propId.Replace("Prop_", "").Replace("_", " ");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void ApplyProjectBranding()
        {
            PlayerSettings.productName = "Parishram Hunt";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.parishram.hunt");
            PlayerSettings.bundleVersion = "0.3.0";
            PlayerSettings.Android.bundleVersionCode = 3;

            string[] iconPaths = { "Assets/Art/UI/ParishramHuntIcon.png", "Assets/Resources/UI/ParishramHuntIcon.png" };
            for (int p = 0; p < iconPaths.Length; p++)
            {
                var ti = AssetImporter.GetAtPath(iconPaths[p]) as TextureImporter;
                if (ti == null) continue;
                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (!ti.isReadable) { ti.isReadable = true; changed = true; }
                if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; changed = true; }
                if (changed) ti.SaveAndReimport();
            }

            var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPaths[0]);
            if (iconTex == null) return;

            string bgPath = "Assets/Art/UI/ParishramHuntIcon_AdaptiveBg.png";
            string fgPath = "Assets/Art/UI/ParishramHuntIcon_AdaptiveFg.png";
            EnsureAdaptiveIconTextures(iconTex, bgPath, fgPath);

            var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);
            var fgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(fgPath);

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { iconTex }, IconKind.Any);

            if (bgTex != null && fgTex != null)
            {
                var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
                if (adaptive != null && adaptive.Length > 0)
                {
                    for (int i = 0; i < adaptive.Length; i++)
                        adaptive[i].SetTextures(bgTex, fgTex);
                    PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive, adaptive);
                }
            }

#pragma warning disable CS0618
            var round = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round);
            if (round != null && round.Length > 0)
            {
                for (int i = 0; i < round.Length; i++)
                    round[i].SetTexture(iconTex);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round, round);
            }

            var legacy = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy);
            if (legacy != null && legacy.Length > 0)
            {
                for (int i = 0; i < legacy.Length; i++)
                    legacy[i].SetTexture(iconTex);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy, legacy);
            }
#pragma warning restore CS0618
        }

        private static void EnsureAdaptiveIconTextures(Texture2D sourceIcon, string bgAssetPath, string fgAssetPath)
        {
            const int size = 512;
            Color32 bgColor = new Color32(0x08, 0x0D, 0x1B, 0xFF);
            var bgTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var bgPixels = new Color32[size * size];
            for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = bgColor;
            bgTex.SetPixels32(bgPixels);
            bgTex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), bgAssetPath.Replace('/', Path.DirectorySeparatorChar)), bgTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(bgTex);

            var fgTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var fgPixels = new Color32[size * size];
            int targetSize = Mathf.RoundToInt(size * 0.68f);
            int margin = (size - targetSize) / 2;
            for (int y = 0; y < targetSize; y++)
            {
                float v = (y + 0.5f) / targetSize;
                for (int x = 0; x < targetSize; x++)
                {
                    float u = (x + 0.5f) / targetSize;
                    Color c = sourceIcon.GetPixelBilinear(u, v);
                    fgPixels[(margin + y) * size + (margin + x)] = (Color32)c;
                }
            }
            fgTex.SetPixels32(fgPixels);
            fgTex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), fgAssetPath.Replace('/', Path.DirectorySeparatorChar)), fgTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(fgTex);

            AssetDatabase.ImportAsset(bgAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(fgAssetPath, ImportAssetOptions.ForceUpdate);

            string[] paths = { bgAssetPath, fgAssetPath };
            for (int i = 0; i < paths.Length; i++)
            {
                var ti = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.isReadable = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
        }

        private static void RegisterSceneInBuildSettings(string scenePath)
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
        }

        private static void ParseSpawnsManual(string json, out List<Vector3> hunters, out List<Vector3> props)
        {
            hunters = new List<Vector3>();
            props = new List<Vector3>();
            void ExtractSection(string key, List<Vector3> dest)
            {
                int idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
                if (idx < 0) return;
                int open = json.IndexOf('[', idx);
                if (open < 0) return;
                int depth = 0;
                int p = open;
                while (p < json.Length)
                {
                    if (json[p] == '[') depth++;
                    else if (json[p] == ']')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    p++;
                }
                string slice = json.Substring(open, p - open + 1);
                var matches = System.Text.RegularExpressions.Regex.Matches(slice, @"\[\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\]");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    float y = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    float z = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    dest.Add(new Vector3(x, y, z));
                }
            }
            ExtractSection("hunterSpawns", hunters);
            ExtractSection("propSpawns", props);
        }
    }
}
