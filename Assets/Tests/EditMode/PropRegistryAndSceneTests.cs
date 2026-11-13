using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Props;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoachingPropHunt.Tests.EditMode
{
    public class PropRegistryAndSceneTests
    {
        [Test]
        public void PropRegistry_ContainsAllGroundedPrefabsWithValidColliders()
        {
            var reg = AssetDatabase.LoadAssetAtPath<PropRegistrySO>("Assets/Data/PropRegistry.asset");
            Assert.IsNotNull(reg, "PropRegistry.asset should exist at Assets/Data/PropRegistry.asset");
            Assert.GreaterOrEqual(reg.Entries.Count, 28, "Expected at least 28 Coaching props registered.");

            foreach (var def in reg.Entries)
            {
                Assert.IsNotNull(def.prefab, $"Prefab missing for {def.propId}");
                var inst = Object.Instantiate(def.prefab);
                try
                {
                    var ip = inst.GetComponent<InteractiveProp>();
                    Assert.IsNotNull(ip, $"InteractiveProp missing on {def.propId}");
                    Assert.AreEqual(def.propId, ip.PropId);
                    Assert.IsNotNull(inst.GetComponent<Collider>(), $"Collider missing on {def.propId}");

                    var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
                    Assert.Greater(renderers.Length, 0, $"No MeshRenderer on {def.propId}");
                    float minY = float.MaxValue;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Assert.AreEqual(LightProbeUsage.BlendProbes, renderers[i].lightProbeUsage, $"LightProbe parity mismatch on {def.propId}");
                        Assert.AreNotEqual("Default-Material", renderers[i].sharedMaterial != null ? renderers[i].sharedMaterial.name : "", $"Default-Material on {def.propId}");
                        minY = Mathf.Min(minY, renderers[i].bounds.min.y);
                    }
                    Assert.LessOrEqual(Mathf.Abs(minY), 0.025f, $"Prop {def.propId} pivot not grounded at minY=0 (was {minY})");
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }
        }

        [Test]
        public void CoachingCampusScene_HasNavMeshBetweenFloorsAndZeroDefaultMaterials()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/CoachingCampus.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());

            var placedProps = Object.FindObjectsByType<InteractiveProp>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(placedProps.Length, 200, "Expected >= 200 static hiding props across the 2 floors.");

            var gsm = Object.FindFirstObjectByType<Core.GameStateManager>();
            Assert.IsNotNull(gsm, "GameStateManager missing from scene.");
            Assert.IsNotNull(gsm.PropRegistry, "PropRegistry reference missing on GameStateManager.");
            Assert.AreEqual(60f, gsm.HideDuration, 0.01f, "Hide phase duration must be 60s.");
            Assert.AreEqual(300f, gsm.HuntDuration, 0.01f, "Hunt phase duration must be 300s (5 min).");

            var nm = Object.FindFirstObjectByType<Unity.Netcode.NetworkManager>();
            Assert.IsNotNull(nm, "NetworkManager missing on GameSystems.");
            Assert.IsNotNull(nm.NetworkConfig.PlayerPrefab, "NetworkPlayer PlayerPrefab must be registered.");
            var netSync = Object.FindFirstObjectByType<Networking.NetworkMatchSync>();
            Assert.IsNotNull(netSync, "NetworkMatchSync missing from scene.");
            Assert.IsNotNull(netSync.GetComponent<Unity.Netcode.NetworkObject>(), "NetworkObject missing on NetworkMatchSync.");
            Assert.IsNull(gsm.GetComponent<Unity.Netcode.NetworkObject>(), "NetworkManager/GameSystems must not share GameObject with NetworkObject.");

            for (int i = 0; i < 6; i++)
            {
                var hs = gsm.GetHunterSpawn(i);
                Assert.IsNotNull(hs);
                Assert.Less(hs.position.y, 1.5f, $"HunterSpawn[{i}] should sit at Ground Floor Gate, got y={hs.position.y}");
            }
            for (int i = 0; i < 16; i++)
            {
                Assert.IsNotNull(gsm.GetPropSpawn(i), $"PropSpawn[{i}] should not be null.");
            }

            // Check skybox and renderers have no default-material leaks
            Assert.IsNotNull(RenderSettings.skybox, "Custom Palette skybox missing.");
            var allRends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var r in allRends)
            {
                if (r.sharedMaterial != null)
                    Assert.AreNotEqual("Default-Material", r.sharedMaterial.name, $"Default-Material found on {r.gameObject.name}");
            }

            // Check baked NavMesh spans Ground Floor (y=0) up Stairs to First Floor (y=4) AND Classroom center aisle!
            bool hasGfNav = NavMesh.SamplePosition(new Vector3(0f, 0.1f, 8.0f), out NavMeshHit gfHit, 3.0f, NavMesh.AllAreas);
            bool hasF1Nav = NavMesh.SamplePosition(new Vector3(-1.5f, 4.1f, 0.5f), out NavMeshHit f1Hit, 3.5f, NavMesh.AllAreas);
            bool hasDoorNav = NavMesh.SamplePosition(new Vector3(-2.45f, 4.1f, -9.2f), out NavMeshHit doorHit, 1.2f, NavMesh.AllAreas);
            // 1F_ClassRoom center aisle in Blender is y=11.35 -> Unity z=-11.35, x=-5.15, y=4.05
            bool hasClassAisle = NavMesh.SamplePosition(new Vector3(-5.15f, 4.1f, -11.35f), out NavMeshHit aisleHit, 0.55f, NavMesh.AllAreas);
            Assert.IsTrue(hasGfNav, "Ground Floor NavMesh sample failed near entrance.");
            Assert.IsTrue(hasF1Nav, "First Floor NavMesh sample failed near corridor.");
            Assert.IsTrue(hasDoorNav, "First Floor Classroom doorway NavMesh sample failed.");
            Assert.IsTrue(hasClassAisle, "Classroom center aisle between desk columns must be walkable on NavMesh.");
            var p1 = new NavMeshPath();
            NavMesh.CalculatePath(gfHit.position, f1Hit.position, NavMesh.AllAreas, p1);
            var p2 = new NavMeshPath();
            NavMesh.CalculatePath(f1Hit.position, doorHit.position, NavMesh.AllAreas, p2);
            var p3 = new NavMeshPath();
            NavMesh.CalculatePath(doorHit.position, aisleHit.position, NavMesh.AllAreas, p3);
            Assert.IsTrue(p1.status == NavMeshPathStatus.PathComplete, $"GF->1F failed: {p1.status} (gf={gfHit.position}, f1={f1Hit.position})");
            Assert.IsTrue(p2.status == NavMeshPathStatus.PathComplete, $"1F Corridor->Door failed: {p2.status} (f1={f1Hit.position}, door={doorHit.position})");
            Assert.IsTrue(p3.status == NavMeshPathStatus.PathComplete, $"Door->ClassAisle failed: {p3.status} (door={doorHit.position}, aisle={aisleHit.position})");
        }

        [Test]
        public void CoachingCampus_HasGateBarriers_SpinningFans_RoomPlaques_AndSSAO()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/CoachingCampus.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());

            // 1. Invisible Exterior Gate Boundary Walls
            string[] wallNames = { "GF_Gate_Boundary_Front", "GF_Gate_Boundary_Left", "GF_Gate_Boundary_Right" };
            foreach (var wn in wallNames)
            {
                var w = GameObject.Find(wn);
                Assert.IsNotNull(w, $"Exterior gate barrier {wn} missing.");
                Assert.IsNotNull(w.GetComponent<BoxCollider>(), $"{wn} must have a BoxCollider.");
                Assert.IsNull(w.GetComponent<MeshRenderer>(), $"{wn} must remain invisible (no MeshRenderer).");
            }

            // 2. Dynamic non-static spinning ceiling fans
            var fans = Object.FindObjectsByType<CoachingFanSpinner>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(fans.Length, 1, "Expected CoachingFanSpinner components on ceiling fans.");
            foreach (var fan in fans)
            {
                foreach (var mr in fan.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Assert.IsFalse(GameObjectUtility.AreStaticEditorFlagsSet(mr.gameObject, StaticEditorFlags.BatchingStatic),
                        $"Fan mesh {mr.gameObject.name} must not be BatchingStatic or runtime rotation is frozen.");
                }
            }

            // 3. 3D Collider-free Room Name Plaques
            var plaqueRoot = GameObject.Find("RoomNamePlaques");
            Assert.IsNotNull(plaqueRoot, "RoomNamePlaques root missing from scene.");
            var tmps = plaqueRoot.GetComponentsInChildren<TextMeshPro>(true);
            Assert.GreaterOrEqual(tmps.Length, 14, "Expected double-sided 3D room plaques above coaching doorways.");
            Assert.AreEqual(0, plaqueRoot.GetComponentsInChildren<Collider>(true).Length, "Room name plaques must not carry blocking colliders.");

            // 4. Mobile URP Renderer SSAO feature enabled
            var mobRenderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/Mobile_Renderer.asset");
            Assert.IsNotNull(mobRenderer, "Mobile_Renderer.asset not found.");
            bool hasSsao = false;
            foreach (var feat in mobRenderer.rendererFeatures)
            {
                if (feat is ScreenSpaceAmbientOcclusion s && s.isActive)
                    hasSsao = true;
            }
            Assert.IsTrue(hasSsao, "Mobile_Renderer.asset must have an active ScreenSpaceAmbientOcclusion renderer feature.");
        }

        [Test]
        public void BlenderCharacterPrefabs_Exist_FaceForward_AndAttachBlasterCleanly()
        {
            var hunterRes = Resources.Load<GameObject>("Characters/HunterCharacter");
            var studentRes = Resources.Load<GameObject>("Characters/PropStudentCharacter");
            var blasterRes = Resources.Load<GameObject>("Characters/HunterBlaster");
            Assert.IsNotNull(hunterRes, "Resources/Characters/HunterCharacter.prefab missing.");
            Assert.IsNotNull(studentRes, "Resources/Characters/PropStudentCharacter.prefab missing.");
            Assert.IsNotNull(blasterRes, "Resources/Characters/HunterBlaster.prefab missing.");

            var hGo = new GameObject("TestHunterAvatar");
            var sGo = new GameObject("TestStudentAvatar");
            try
            {
                var hVis = hGo.AddComponent<PlayerAvatarVisual>();
                hVis.BuildVisual(TeamRole.Hunter);
                Assert.IsTrue(hVis.UsesBlenderRig, "Hunter must load sculpted Blender GLB rig.");
                hVis.SetVisible(true, firstPersonHideBody: false);
                hVis.UpdateMotion(Vector3.forward * 2.0f, 3.5f, 0f, 0.05f, isGrounded: true, aimPitchDeg: 0f);
                hVis.UpdateMotion(Vector3.forward * 2.0f, 3.5f, 0f, 0.25f, isGrounded: true, aimPitchDeg: 0f);

                Assert.Greater(hVis.MuzzleWorldForward.z, 0.65f, $"Hunter Blaster muzzle should point forward (+Z), got {hVis.MuzzleWorldForward}");
                Assert.Greater(hVis.MuzzleWorldPos.z, hGo.transform.position.z + 0.10f, "Blaster muzzle should protrude cleanly in front of Hunter.");

                var sVis = sGo.AddComponent<PlayerAvatarVisual>();
                sVis.BuildVisual(TeamRole.Prop);
                Assert.IsTrue(sVis.UsesBlenderRig, "Prop Student must load sculpted Blender GLB rig.");
            }
            finally
            {
                Object.DestroyImmediate(hGo);
                Object.DestroyImmediate(sGo);
            }
        }

        [Test]
        public void BrandingAndNetworkTransport_ConfiguredForParishramHuntAndMobileStability()
        {
            Assert.AreEqual("Parishram Hunt", PlayerSettings.productName, "Product name must be branded 'Parishram Hunt'.");
            Assert.AreEqual("com.parishram.hunt", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), "Android package ID must be 'com.parishram.hunt'.");
            Assert.GreaterOrEqual(PlayerSettings.Android.bundleVersionCode, 2, "Android versionCode must be bumped >= 2.");

            var iconTex = Resources.Load<Texture2D>("UI/ParishramHuntIcon");
            Assert.IsNotNull(iconTex, "Cropped ParishramHuntIcon must exist in Assets/Resources/UI/");
            Assert.AreEqual(512, iconTex.width);
            Assert.AreEqual(512, iconTex.height);
            var corner = iconTex.GetPixel(0, 0);
            Assert.Less(corner.r, 0.45f, "Cropped icon corner must not retain white border.");

            var iconSprite = Resources.Load<Sprite>("UI/ParishramHuntIcon");
            Assert.IsNotNull(iconSprite, "ParishramHuntIcon must load as a Single Sprite for TitleScreen.");

            var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
            Assert.IsNotNull(adaptive);
            Assert.Greater(adaptive.Length, 0);
            Assert.IsNotNull(adaptive[0].GetTexture(0), "Adaptive background icon must be populated.");
            Assert.IsNotNull(adaptive[0].GetTexture(1), "Adaptive foreground icon must be populated.");

#pragma warning disable CS0618
            var round = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round);
            Assert.IsNotNull(round);
            Assert.Greater(round.Length, 0);
            Assert.IsNotNull(round[0].GetTexture(0), "Android Round icon must be populated.");

            var legacy = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy);
            Assert.IsNotNull(legacy);
            Assert.Greater(legacy.Length, 0);
            Assert.IsNotNull(legacy[0].GetTexture(0), "Android Legacy icon must be populated.");
#pragma warning restore CS0618

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/CoachingCampus.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());
            var utp = Object.FindFirstObjectByType<Unity.Netcode.Transports.UTP.UnityTransport>();
            Assert.IsNotNull(utp, "UnityTransport missing from scene.");
            Assert.GreaterOrEqual(utp.DisconnectTimeoutMS, 30000, "Mobile DisconnectTimeoutMS must tolerate brief jitter.");
            Assert.GreaterOrEqual(utp.MaxPayloadSize, 16384);
        }

        [Test]
        public void OptimizationInfrastructure_Lods_Occlusion_Streaming_Pooling_And_TextureBudget_Configured()
        {
            var reg = AssetDatabase.LoadAssetAtPath<PropRegistrySO>("Assets/Data/PropRegistry.asset");
            Assert.IsNotNull(reg);
            int lodPropCount = 0;
            int multiTierLodCount = 0;
            foreach (var e in reg.Entries)
            {
                Assert.IsNotNull(e.prefab, $"Prefab missing for {e.propId}");
                var lod = e.prefab.GetComponentInChildren<LODGroup>(true);
                Assert.IsNotNull(lod, $"Prop prefab {e.propId} must have a LODGroup.");
                Assert.GreaterOrEqual(lod.lodCount, 1, $"Prop prefab {e.propId} must define at least 1 LOD + cull tier.");
                if (lod.lodCount >= 3) multiTierLodCount++;
                lodPropCount++;
            }
            Assert.GreaterOrEqual(lodPropCount, 28);
            Assert.GreaterOrEqual(multiTierLodCount, 10, "High-poly prop prefabs must have 3-tier LOD0/LOD1/LOD2 meshes.");

            var lodMeshFiles = System.IO.Directory.Exists("Assets/Art/LODMeshes")
                ? System.IO.Directory.GetFiles("Assets/Art/LODMeshes", "*.asset")
                : System.Array.Empty<string>();
            Assert.Greater(lodMeshFiles.Length, 10, "Generated LOD1/LOD2 simplified meshes should exist under Assets/Art/LODMeshes.");

            Assert.IsTrue(QualitySettings.streamingMipmapsActive, "Texture Mipmap Streaming must be active in QualitySettings.");

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/CoachingCampus.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());

            var occAssetPath = "Assets/Scenes/CoachingCampus/OcclusionCullingData.asset";
            Assert.IsTrue(System.IO.File.Exists(occAssetPath), "Baked Umbra OcclusionCullingData.asset must exist for CoachingCampus.");
            Assert.Greater(new System.IO.FileInfo(occAssetPath).Length, 50000L, "Baked Umbra OcclusionCullingData.asset must contain non-trivial culling tree data.");
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(occAssetPath), "OcclusionCullingData.asset must be a valid imported Unity asset.");

            var poolMgr = Object.FindFirstObjectByType<ObjectPoolManager>();
            Assert.IsNotNull(poolMgr, "ObjectPoolManager must be baked onto GameSystems.");

            var streamer = Object.FindFirstObjectByType<CampusSectorStreamer>();
            Assert.IsNotNull(streamer, "CampusSectorStreamer must be baked onto GameSystems.");
            Assert.GreaterOrEqual(streamer.SectorCount, 6, "CampusSectorStreamer must define GF + 1F spatial sectors.");

            int smallLayer = LayerMask.NameToLayer("SmallProp");
            int medLayer = LayerMask.NameToLayer("MediumProp");
            Assert.GreaterOrEqual(smallLayer, 6, "SmallProp layer must be registered in TagManager.");
            Assert.GreaterOrEqual(medLayer, 6, "MediumProp layer must be registered in TagManager.");
        }
    }
}
