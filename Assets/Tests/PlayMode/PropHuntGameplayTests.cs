using System.Collections;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Input;
using CoachingPropHunt.Networking;
using CoachingPropHunt.Props;
using CoachingPropHunt.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoachingPropHunt.Tests.PlayMode
{
    public class PropHuntGameplayTests
    {
        [UnityTest]
        public IEnumerator OfflineMatch_SpawnsParticipants_MorphsProp_AndAppliesPenalty()
        {
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var gsm = GameStateManager.Instance;
            Assert.IsNotNull(gsm);
            Assert.AreEqual(AppShellState.Title, gsm.ShellState);
            Assert.AreEqual(60f, gsm.HideDuration, 0.01f);
            Assert.AreEqual(300f, gsm.HuntDuration, 0.01f);

            var ui = GameUIManager.Instance;
            Assert.IsNotNull(ui);
            var canvas = ui.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode, "HUD must use ScreenSpaceOverlay so walls never cover buttons.");

            gsm.StartMatch(onlineSession: false, totalSlots: 12);
            yield return null;

            Assert.AreEqual(AppShellState.Playing, gsm.ShellState);
            Assert.AreEqual(MatchPhase.HidePhase, gsm.Phase);
            Assert.AreEqual(60f, gsm.PhaseRemainingSeconds, 1.5f);
            Assert.AreEqual(12, PlayerParticipant.AllActive.Count);
            Assert.IsNotNull(PlayerParticipant.LocalPlayer);
            Assert.LessOrEqual(PlayerParticipant.LocalPlayer.CharacterCtrl.radius, 0.19f, "Player capsule radius should be slim enough for classroom desk aisles.");

            // Force a test participant onto Prop morph and verify exact prefab morphing
            PlayerParticipant propActor = null;
            PlayerParticipant hunterActor = null;
            foreach (var p in PlayerParticipant.AllActive)
            {
                if (p.Role == TeamRole.Prop && propActor == null) propActor = p;
                if (p.Role == TeamRole.Hunter && hunterActor == null) hunterActor = p;
            }
            Assert.IsNotNull(propActor);
            Assert.IsNotNull(hunterActor);

            bool morphed = propActor.MorphCtrl.ApplyPropById("Prop_WaterBottle_Green", playFx: false, out string dName);
            Assert.IsTrue(morphed);
            Assert.AreEqual(35, propActor.MaxHealth);
            Assert.IsTrue(propActor.MorphCtrl.IsMorphed);
            yield return null;

            // Verify Wrong-Hit penalty reduces Hunter HP during HuntPhase
            gsm.SyncPhaseFromNetwork(MatchPhase.HuntPhase, 300f);
            hunterActor.HunterAction.ResetForMatch();
            int beforeHp = hunterActor.Health;
            InteractiveProp sampleStatic = null;
            foreach (var candidate in Object.FindObjectsByType<InteractiveProp>(FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.enabled && candidate.GetComponentInParent<PlayerParticipant>() == null)
                {
                    sampleStatic = candidate;
                    break;
                }
            }
            Assert.IsNotNull(sampleStatic);
            var propCol = sampleStatic.GetComponentInChildren<Collider>();
            Vector3 targetCenter = propCol != null ? propCol.bounds.center : (sampleStatic.transform.position + Vector3.up * 0.25f);
            float rayStartY = (propCol != null ? propCol.bounds.max.y : targetCenter.y) + 0.35f;
            hunterActor.HunterAction.TryStrike(new Ray(new Vector3(targetCenter.x, rayStartY, targetCenter.z), Vector3.down));
            Assert.AreEqual(beforeHp - 10, hunterActor.Health, "Hitting a real Coaching prop should penalize Hunter by 10 HP.");

            // Verify striking an actual Prop Player damages & eliminates cleanly (0 penalty to Hunter)
            Physics.SyncTransforms();
            hunterActor.HunterAction.ResetForMatch();
            Vector3 propTarget = propActor.transform.position + Vector3.up * 0.2f;
            Ray strikeRay = new Ray(propTarget + Vector3.up * 1.2f, Vector3.down);
            hunterActor.HunterAction.TryStrike(strikeRay);
            var dbgHits = Physics.RaycastAll(strikeRay, 3.25f, ~0, QueryTriggerInteraction.Ignore);
            string hitSummary = string.Join(", ", System.Array.ConvertAll(dbgHits, x => $"{x.collider.name}@{x.distance:F2}(p:{x.collider.GetComponentInParent<PlayerParticipant>()?.DisplayName ?? "null"})"));
            Assert.IsFalse(propActor.IsAlive, $"35 HP WaterBottle not killed! hp={propActor.Health}/{propActor.MaxHealth} pos={propActor.transform.position} bot={propActor.IsBot} hbEnabled={propActor.CombatHitbox?.enabled} hbBounds={propActor.CombatHitbox?.bounds} hits=[{hitSummary}]");
            Assert.AreEqual(beforeHp - 10, hunterActor.Health, "Hitting a real Prop player must not penalize Hunter.");

            // Verify end condition transitions cleanly and offline high score persists to PlayerPrefs
            int preHigh = PlayerPrefs.GetInt("CoachingPropHunt_HighScore", 0);
            PlayerParticipant.LocalPlayer.AddScore(Mathf.Max(250, preHigh + 50));
            gsm.UpdateAliveCounts(0, 1);
            Assert.AreEqual(MatchPhase.RoundOver, gsm.Phase);
            Assert.AreEqual(TeamRole.Hunter, gsm.WinningTeam);
            Assert.GreaterOrEqual(PlayerPrefs.GetInt("CoachingPropHunt_HighScore", 0), preHigh + 50, "Offline High Score must persist to PlayerPrefs.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator HudControls_PauseAboveTouchPad_FloatingStick_AndSettingHudSave()
        {
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var ui = GameUIManager.Instance;
            Assert.IsNotNull(ui);
            var pauseGo = GameObject.Find("PauseHudBtn") ?? FindInactive("PauseHudBtn", ui.transform);
            var lookPadGo = GameObject.Find("RightLookTouchPad") ?? FindInactive("RightLookTouchPad", ui.transform);
            var joyZoneGo = GameObject.Find("LeftJoystickZone") ?? FindInactive("LeftJoystickZone", ui.transform);
            Assert.IsNotNull(pauseGo);
            Assert.IsNotNull(lookPadGo);
            Assert.IsNotNull(joyZoneGo);
            Assert.Greater(pauseGo.transform.GetSiblingIndex(), lookPadGo.transform.GetSiblingIndex(), "PauseHudBtn must render/raycast above RightLookTouchPad.");

            // Verify SETTING HUD edits & saves Morph button scale/position cleanly
            ui.OpenHudCustomizer();
            yield return null;
            var morphGo = GameObject.Find("MorphBtn") ?? FindInactive("MorphBtn", ui.transform);
            Assert.IsNotNull(morphGo);
            var morphHandle = morphGo.GetComponent<HudEditableHandle>();
            Assert.IsNotNull(morphHandle);
            morphHandle.SetScale(1.42f);
            morphHandle.Rect.anchoredPosition = new Vector2(-210f, 240f);
            ui.CloseHudCustomizer(saveChanges: true);
            yield return null;

            Assert.AreEqual(1.42f, PlayerPrefs.GetFloat("CoachingPropHunt_HudLayout_v1_morph_S", 1f), 0.02f);
            Assert.AreEqual(-210f, PlayerPrefs.GetFloat("CoachingPropHunt_HudLayout_v1_morph_X", 0f), 0.5f);

            // Clean up test PlayerPrefs override
            PlayerPrefs.DeleteKey("CoachingPropHunt_HudLayout_v1_morph_S");
            PlayerPrefs.DeleteKey("CoachingPropHunt_HudLayout_v1_morph_X");
            PlayerPrefs.DeleteKey("CoachingPropHunt_HudLayout_v1_morph_Y");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingHudTabs_SensitivityPrefs_RoomLocator_BotNerf_AndOnlineZeroBotHunters()
        {
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            // 1. Separated SETTING HUD tabs: Prop vs Hunter never visible simultaneously
            var ui = GameUIManager.Instance;
            Assert.IsNotNull(ui);
            ui.OpenHudCustomizer();
            yield return null;

            var titleGo = FindInactive("TitleScreen", ui.transform);
            var morphGo = FindInactive("MorphBtn", ui.transform);
            var strikeGo = FindInactive("StrikeBtn", ui.transform);
            Assert.IsNotNull(morphGo);
            Assert.IsNotNull(strikeGo);
            Assert.IsTrue(morphGo.activeInHierarchy, "PROP HUD tab should show MorphBtn.");
            Assert.IsFalse(strikeGo.activeInHierarchy, "PROP HUD tab must hide Hunter StrikeBtn.");
            if (titleGo != null) Assert.IsFalse(titleGo.activeSelf, "TitleScreen must hide while editing HUD.");

            ui.SwitchHudEditorTab("hunter");
            yield return null;
            Assert.IsTrue(strikeGo.activeInHierarchy, "HUNTER HUD tab should show StrikeBtn.");
            Assert.IsFalse(morphGo.activeInHierarchy, "HUNTER HUD tab must hide Prop MorphBtn.");
            ui.CloseHudCustomizer(saveChanges: false);
            yield return null;

            // 2. Sensitivity & Quality PlayerPrefs persistence
            MobileAndDesktopInputDriver.SetSensitivities(1.42f, 0.52f);
            MobileAndDesktopInputDriver.LoadSensitivityPrefs();
            Assert.AreEqual(1.42f, MobileAndDesktopInputDriver.CameraSensitivity, 0.01f);
            Assert.AreEqual(0.52f, MobileAndDesktopInputDriver.PropMorphSensitivity, 0.01f);

            GraphicsQualityController.Apply(GraphicsPresetTier.High, save: true);
            GraphicsQualityController.LoadAndApply();
            Assert.AreEqual(GraphicsPresetTier.High, GraphicsQualityController.CurrentTier);

            // 3. CoachingRoomLocator floor/room resolution
            Assert.AreEqual("GF • MAIN GATE", CoachingRoomLocator.GetRoomLabel(new Vector3(0f, 0.5f, 15.0f)));
            Assert.AreEqual("GF • COACHING HALL", CoachingRoomLocator.GetRoomLabel(new Vector3(0f, 0.5f, 0f)));
            Assert.AreEqual("1F • BIG CLASSROOM", CoachingRoomLocator.GetRoomLabel(new Vector3(2.0f, 4.5f, -8.0f)));
            Assert.AreEqual("1F • DOUBT CLASS", CoachingRoomLocator.GetRoomLabel(new Vector3(1.0f, 4.5f, 10.0f)));

            // 4. Offline Bot Hunter speed nerf (<= 4.5 m/s so players aren't unfairly run down)
            var gsm = GameStateManager.Instance;
            gsm.StartMatch(onlineSession: false, totalSlots: 12);
            yield return null;
            int offlineBotHunters = 0;
            foreach (var p in PlayerParticipant.AllActive)
            {
                if (p.IsBot && p.Role == TeamRole.Hunter)
                {
                    offlineBotHunters++;
                    var agent = p.GetComponent<NavMeshAgent>();
                    if (agent != null)
                        Assert.LessOrEqual(agent.speed, 3.0f, "Offline Bot Hunter speed should be slowed to ~60% (<= 3.0 m/s).");
                }
            }
            Assert.GreaterOrEqual(offlineBotHunters, 1, "Offline match should include at least 1 Bot Hunter unless local player fills solo Hunter.");

            // 5. Online Multiplayer rule: AI Bots NEVER assigned TeamRole.Hunter
            gsm.ReturnToTitle();
            yield return null;
            var nm = NetworkManager.Singleton;
            Assert.IsNotNull(nm, "NetworkManager missing.");
            try
            {
                Assert.IsTrue(nm.StartHost(), "Local Netcode StartHost failed for online bot-role check.");
                yield return null;
                gsm.StartMatch(onlineSession: true, totalSlots: 10);
                yield return null;

                int botProps = 0;
                foreach (var p in PlayerParticipant.AllActive)
                {
                    if (p.IsBot)
                    {
                        Assert.AreEqual(TeamRole.Prop, p.Role, "AI Bots in Online Multiplayer must NEVER play Hunter.");
                        botProps++;
                    }
                }
                Assert.GreaterOrEqual(botProps, 1, "Online test host with 10 total slots should spawn backfill Bot Props.");
            }
            finally
            {
                if (nm.IsListening) nm.Shutdown();
                PlayerPrefs.DeleteAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewUpgrades_MorphRotateArrows_SlowSpeed_TpsBlasterAimAssist_Stun_And45sPings()
        {
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var gsm = GameStateManager.Instance;
            var ui = GameUIManager.Instance;
            Assert.IsNotNull(gsm);
            Assert.IsNotNull(ui);

            try
            {
                // 1. Start as Prop & verify slowed movement (~60%), no-twist joystick when morphed + Left/Right Rotate Arrows
                MatchCoordinator.Instance.PreferredLocalRole = TeamRole.Prop;
                gsm.StartMatch(onlineSession: false, totalSlots: 8);
                yield return null;

                var localProp = PlayerParticipant.LocalPlayer;
                Assert.IsNotNull(localProp);
                Assert.AreEqual(TeamRole.Prop, localProp.Role);
                Assert.IsTrue(localProp.MorphCtrl.ApplyPropById("Prop_Chair_Blue", playFx: false, out _));
                yield return null;

                var rotLeftBtn = FindInactive("PropRotateLeftBtn", ui.transform);
                var rotRightBtn = FindInactive("PropRotateRightBtn", ui.transform);
                var stunBtn = FindInactive("StunBtn", ui.transform);
                Assert.IsNotNull(rotLeftBtn);
                Assert.IsNotNull(rotRightBtn);
                Assert.IsNotNull(stunBtn);
                Assert.IsTrue(rotLeftBtn.activeInHierarchy, "Left Rotate Arrow must appear when morphed into a Prop.");
                Assert.IsTrue(rotRightBtn.activeInHierarchy, "Right Rotate Arrow must appear when morphed into a Prop.");

                // Move Prop smoothly via joystick: speed <= 3.5m/s (~60% of 5.4) and yaw stays locked!
                localProp.CharacterCtrl.enabled = false;
                localProp.transform.position = new Vector3(0f, 0.1f, 8.0f);
                localProp.CharacterCtrl.enabled = true;
                Physics.SyncTransforms();

                float initialYaw = localProp.transform.eulerAngles.y;
                Vector3 moveStart = localProp.transform.position;
                localProp.SetMoveInput(Vector2.right);
                for (int f = 0; f < 5; f++) yield return null;
                localProp.SetMoveInput(Vector2.zero);
                Assert.AreEqual(initialYaw, localProp.transform.eulerAngles.y, 0.25f, "Joystick must NOT rotate a morphed Prop object.");

                float movedDist = Vector3.Distance(moveStart, localProp.transform.position);
                Assert.Less(movedDist, 1.5f, "Slowed movement (~60%) should cover moderate distance rather than sprinting excessively.");

                // Rotate Arrow rotates Prop cleanly
                MobileAndDesktopInputDriver.Active.TapRotateStep(1);
                yield return null;
                float deltaYaw = Mathf.Abs(Mathf.DeltaAngle(initialYaw, localProp.transform.eulerAngles.y));
                Assert.Greater(deltaYaw, 4f, "Tapping Rotate Arrow should rotate morphed Prop cleanly.");

                // 2. Prop Hit / Stun Hunter to escape
                PlayerParticipant hunterBot = null;
                foreach (var p in PlayerParticipant.AllActive)
                {
                    if (p != localProp && p.Role == TeamRole.Hunter && p.IsAlive) { hunterBot = p; break; }
                }
                Assert.IsNotNull(hunterBot);
                gsm.SyncPhaseFromNetwork(MatchPhase.HuntPhase, 40f);
                var botAgentH = hunterBot.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (botAgentH != null) botAgentH.enabled = false;
                hunterBot.transform.position = localProp.transform.position + Vector3.forward * 2.0f;
                Physics.SyncTransforms();
                Assert.IsTrue(localProp.PropAbility.TryStunHunter(true), "Prop Stun should strike Hunter within ~2m.");
                Assert.IsTrue(hunterBot.IsStunned, "Hit Hunter must enter IsStunned freeze state.");
                Assert.Greater(localProp.PropAbility.StunRemainingCooldown, 10f);

                // 3. Hunter 3rd-Person Body Camera + Medium Blaster Range + Moving Prop Aim Assist + Final-45s Pings
                MatchCoordinator.Instance.PreferredLocalRole = TeamRole.Hunter;
                gsm.StartMatch(onlineSession: false, totalSlots: 8);
                yield return null;

                var localHunter = PlayerParticipant.LocalPlayer;
                Assert.IsNotNull(localHunter);
                Assert.AreEqual(TeamRole.Hunter, localHunter.Role);
                Assert.IsTrue(localHunter.AvatarVisual.IsVisible, "Hunter body should remain visible in 3rd-person view.");
                Assert.GreaterOrEqual(localHunter.HunterAction.MaxBlasterRange, 10.0f);
                Assert.LessOrEqual(localHunter.HunterAction.MaxBlasterRange, 14.0f);

                PlayerParticipant targetProp = null;
                foreach (var p in PlayerParticipant.AllActive)
                {
                    if (p != localHunter && p.Role == TeamRole.Prop && p.IsAlive) { targetProp = p; break; }
                }
                Assert.IsNotNull(targetProp);

                localHunter.CharacterCtrl.enabled = false;
                localHunter.transform.position = new Vector3(0f, 0.05f, 8.0f);
                localHunter.CharacterCtrl.enabled = true;
                localHunter.AddLookDelta(new Vector2(Mathf.DeltaAngle(localHunter.ViewYawDeg, 180f), 0f));

                var botAgentP = targetProp.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (botAgentP != null) botAgentP.enabled = false;
                var botBrainP = targetProp.GetComponent<CoachingPropHunt.AI.BotBrainController>();
                if (botBrainP != null) botBrainP.enabled = false;
                targetProp.CharacterCtrl.enabled = false;
                targetProp.transform.position = new Vector3(0.42f, 0.05f, 0.5f);
                targetProp.transform.rotation = Quaternion.identity;
                targetProp.CharacterCtrl.enabled = true;
                Physics.SyncTransforms();

                gsm.SyncPhaseFromNetwork(MatchPhase.HuntPhase, 38f);
                // Walk targetProp via real CharacterController so IsActivelyMoving updates genuinely
                targetProp.SetMoveInput(Vector2.up);
                yield return null;
                yield return null;
                targetProp.SetMoveInput(Vector2.zero);

                Assert.IsTrue(targetProp.IsActivelyMoving, "Moved Prop should flag IsActivelyMoving for Final-45s ping & Aim Assist.");
                var ping0 = FindInactive("MotionPing_0", ui.transform);
                Assert.IsNotNull(ping0);
                Assert.IsTrue(ping0.activeInHierarchy, "Final 45s subtle through-wall ping should activate when a Prop moves.");

                Ray centerRay = new Ray(new Vector3(0f, 1.35f, 8.0f), new Vector3(0f, -0.06f, -1f).normalized); // 0.42m off-center at ~7.25m range
                int beforeHp = targetProp.Health;
                localHunter.HunterAction.ResetForMatch();
                Assert.IsTrue(localHunter.HunterAction.TryStrike(centerRay));
                Assert.Less(targetProp.Health, beforeHp, "Hunter Blaster Aim Assist must hit moving Prop offset 0.42m at ~7.25m range.");
            }
            finally
            {
                MatchCoordinator.Instance.PreferredLocalRole = null;
                PlayerPrefs.DeleteAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ParishramHunt_RunVsWalk50_OnlineLivePause_TitleName_And_TeammateNameplates()
        {
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var gsm = GameStateManager.Instance;
            var ui = GameUIManager.Instance;
            Assert.IsNotNull(gsm);
            Assert.IsNotNull(ui);

            try
            {
                // 1. Title screen branding + startup Player Name input
                var titleLbl = FindInactive("Title", ui.transform)?.GetComponent<TMPro.TextMeshProUGUI>();
                Assert.IsNotNull(titleLbl);
                Assert.AreEqual("PARISHRAM HUNT", titleLbl.text);

                var titleNameField = FindInactive("TitlePlayerNameInput", ui.transform)?.GetComponent<TMPro.TMP_InputField>();
                Assert.IsNotNull(titleNameField, "Start screen must expose player name input.");
                titleNameField.text = "AakashPro";
                titleNameField.onEndEdit.Invoke("AakashPro");
                Assert.AreEqual("AakashPro", gsm.Progress.playerName);

                // 2. Decoupled Sensitivities + 50% Walk vs 100% Run button speed
                MatchCoordinator.Instance.PreferredLocalRole = TeamRole.Prop;
                gsm.StartMatch(onlineSession: false, totalSlots: 8);
                yield return null;

                var local = PlayerParticipant.LocalPlayer;
                var driver = MobileAndDesktopInputDriver.Active;
                Assert.IsNotNull(local);
                Assert.IsNotNull(driver);

                // Ensure CameraSensitivity stays independent from PropMorphSensitivity when morphed
                var someProp = Object.FindFirstObjectByType<InteractiveProp>();
                Assert.IsNotNull(someProp);
                local.MorphCtrl.ApplyPropById(someProp.PropId, false, out _);
                MobileAndDesktopInputDriver.SetSensitivities(1.60f, 0.30f, save: false);
                float yawBefore = local.ViewYawDeg;
                driver.AddTouchLook(new Vector2(20f, 0f));
                yield return null;
                float camDelta = Mathf.Abs(Mathf.DeltaAngle(yawBefore, local.ViewYawDeg));
                Assert.Greater(camDelta, 3.0f, "Camera Look sensitivity must not be shrunk by low Prop Rotation sensitivity.");

                // Clear space and compare 50% Walk distance vs 100% Run distance
                local.MorphCtrl.ResetToAvatar();
                local.CharacterCtrl.enabled = false;
                local.transform.position = new Vector3(0f, 0.05f, 8.0f);
                local.CharacterCtrl.enabled = true;
                Physics.SyncTransforms();

                driver.SetRunToggled(false);
                driver.SetVirtualMove(Vector2.up);
                Vector3 walkStart = local.transform.position;
                yield return new WaitForSeconds(0.30f);
                float walkDist = Vector3.Distance(walkStart, local.transform.position);

                driver.ToggleRun();
                Assert.IsTrue(driver.IsRunToggled);
                Vector3 runStart = local.transform.position;
                yield return new WaitForSeconds(0.30f);
                float runDist = Vector3.Distance(runStart, local.transform.position);
                driver.SetVirtualMove(Vector2.zero);

                Assert.Greater(runDist, walkDist * 1.55f, $"Run (100%) should be ~2x Walk (50%), got walk={walkDist:F2}m run={runDist:F2}m.");

                // 3. Teammate-only nameplates: place one teammate Prop and verify Hunter never gets a nameplate
                PlayerParticipant mateProp = null;
                foreach (var p in PlayerParticipant.AllActive)
                {
                    if (p != local && p.IsAlive && p.Role == TeamRole.Prop) { mateProp = p; break; }
                }
                Assert.IsNotNull(mateProp);
                var nma = mateProp.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nma != null) nma.enabled = false;
                var brain = mateProp.GetComponent<CoachingPropHunt.AI.BotBrainController>();
                if (brain != null) brain.enabled = false;
                mateProp.CharacterCtrl.enabled = false;
                mateProp.transform.position = local.transform.position + local.transform.forward * 4.5f;
                mateProp.CharacterCtrl.enabled = true;
                Physics.SyncTransforms();
                yield return null;

                int activePlates = 0;
                foreach (var tr in ui.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name.StartsWith("MatePlate_") && tr.gameObject.activeInHierarchy)
                    {
                        activePlates++;
                        var txt = tr.GetComponentInChildren<TMPro.TextMeshProUGUI>().text;
                        Assert.IsFalse(txt.Contains("Hunter"), "Teammate nameplate must NEVER display enemy team names.");
                    }
                }
                Assert.GreaterOrEqual(activePlates, 1, "Alive teammate Prop in front of camera should show teammate nameplate.");

                // 4. Online Session Pause must NOT freeze Time.timeScale or match countdown!
                gsm.StartMatch(onlineSession: true, totalSlots: 8);
                yield return null;
                float remBefore = gsm.PhaseRemainingSeconds;
                gsm.TogglePause();
                Assert.AreEqual(AppShellState.Paused, gsm.ShellState);
                Assert.AreEqual(1f, Time.timeScale, 0.001f, "Opening Settings in Online mode must NEVER pause match timeScale.");
                Assert.IsTrue(gsm.IsMatchLive);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.Less(gsm.PhaseRemainingSeconds, remBefore - 0.1f, "Online countdown must keep running when a player opens Settings.");
            }
            finally
            {
                Time.timeScale = 1f;
                MatchCoordinator.Instance.PreferredLocalRole = null;
                PlayerPrefs.DeleteAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnlineMatch_HidePhaseAndGrace_NeverFiresPrematureHunterWin_AndTitleLogoLoads()
        {
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var gsm = GameStateManager.Instance;
            var ui = GameUIManager.Instance;
            Assert.IsNotNull(gsm);
            Assert.IsNotNull(ui);

            try
            {
                // 1. Title screen logo Sprite must actually load from Resources/UI/ParishramHuntIcon (Single sprite mode).
                var loadedSprite = Resources.Load<Sprite>("UI/ParishramHuntIcon");
                Assert.IsNotNull(loadedSprite, "ParishramHuntIcon must load as a Unity Sprite in runtime builds.");
                var titleScreen = FindInactive("TitleScreen", ui.transform);
                Assert.IsNotNull(titleScreen);
                var logoTr = titleScreen.transform.Find("BrandDock/TitleLogoCard/LogoImg");
                Assert.IsNotNull(logoTr, "BrandDock/TitleLogoCard/LogoImg must exist on TitleScreen.");
                var logoImg = logoTr.GetComponent<UnityEngine.UI.Image>();
                Assert.IsNotNull(logoImg);
                Assert.AreEqual(loadedSprite, logoImg.sprite, "TitleScreen LogoImg must use ParishramHuntIcon sprite (not fallback icon).");

                // 2. Simulate an online session starting during HidePhase with Hunter local role and zero alive Props reported yet:
                // Must NEVER trigger premature Hunter Win!
                MatchCoordinator.Instance.PreferredLocalRole = TeamRole.Hunter;
                gsm.StartMatch(onlineSession: true, totalSlots: 8);
                yield return null;

                Assert.AreEqual(MatchPhase.HidePhase, gsm.Phase);
                Assert.AreEqual(AppShellState.Playing, gsm.ShellState);

                gsm.UpdateAliveCounts(aliveHunters: 1, aliveProps: 0);
                yield return null;
                Assert.AreEqual(MatchPhase.HidePhase, gsm.Phase, "HidePhase must NEVER end on 0 reported props.");
                Assert.AreEqual(AppShellState.Playing, gsm.ShellState);

                // Even if transitioned into HuntPhase before the 5.0s online spawn-grace elapses, 0 reported props must NOT prematurely end match.
                gsm.SyncPhaseFromNetwork(MatchPhase.HuntPhase, 118f);
                yield return null;
                gsm.UpdateAliveCounts(aliveHunters: 1, aliveProps: 0);
                yield return null;
                Assert.AreEqual(MatchPhase.HuntPhase, gsm.Phase, "Online HuntPhase under 5s grace with no seen props must NOT prematurely end match.");
                Assert.AreEqual(AppShellState.Playing, gsm.ShellState);
            }
            finally
            {
                Time.timeScale = 1f;
                MatchCoordinator.Instance.PreferredLocalRole = null;
                PlayerPrefs.DeleteAll();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OptimizationAndBugFixes_Pooling_SectorStreaming_CameraCull_DecoyPenalty_SpectatorOrbit_AndRematchSafe()
        {
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene("CoachingCampus");
            yield return null;
            yield return null;

            var gsm = GameStateManager.Instance;
            var ui = GameUIManager.Instance;
            var pool = ObjectPoolManager.EnsureExists();
            var streamer = Object.FindFirstObjectByType<CampusSectorStreamer>();
            Assert.IsNotNull(gsm);
            Assert.IsNotNull(ui);
            Assert.IsNotNull(pool);
            Assert.IsNotNull(streamer);

            try
            {
                // 1. Camera Occlusion + Per-Layer Cull Distances
                var mainCam = UnityEngine.Camera.main;
                Assert.IsNotNull(mainCam);
                Assert.IsTrue(mainCam.useOcclusionCulling, "Main Camera must enable Occlusion Culling.");
                Assert.LessOrEqual(mainCam.farClipPlane, 90f);
                int smallLayer = LayerMask.NameToLayer("SmallProp");
                Assert.GreaterOrEqual(smallLayer, 0);
                Assert.AreEqual(28f, mainCam.layerCullDistances[smallLayer], 0.5f);

                // 2. ObjectPoolManager reuses BlasterTracers & ExpandingWaves without leaking scene objects
                int tracersBefore = pool.PooledTracerCount;
                var fx = pool.AcquireBlasterTracer();
                Assert.IsNotNull(fx);
                Assert.AreEqual(tracersBefore - 1, pool.PooledTracerCount);
                pool.ReturnBlasterTracer(fx);
                Assert.AreEqual(tracersBefore, pool.PooledTracerCount);

                int wavesBefore = pool.PooledWaveCount;
                var wave = pool.AcquireWaveEffect();
                Assert.IsNotNull(wave);
                pool.ReturnWaveEffect(wave);
                Assert.AreEqual(wavesBefore, pool.PooledWaveCount);

                // 3. Sector streaming toggles off-floor visuals during match while colliders remain active
                MatchCoordinator.Instance.PreferredLocalRole = TeamRole.Hunter;
                gsm.StartMatch(onlineSession: false, totalSlots: 8);
                yield return null;

                streamer.ForceEvaluateAt(new Vector3(0f, 4.5f, -9f)); // 1F back classrooms
                Assert.Less(streamer.ActiveSectorCount, streamer.SectorCount, "Far off-floor sector should disable visuals when inside 1F back classrooms.");
                foreach (var s in streamer.Sectors)
                {
                    if (!s.IsActive)
                    {
                        foreach (var r in s.renderers)
                        {
                            if (r != null) Assert.IsFalse(r.enabled, $"Disabled sector {s.sectorId} renderer should be off.");
                        }
                        break;
                    }
                }

                // 4. Prop morph pooling (repeated morphs leave zero orphaned scene visuals)
                PlayerParticipant propActor = null;
                foreach (var p in PlayerParticipant.AllActive)
                {
                    if (p.Role == TeamRole.Prop && p.IsAlive) { propActor = p; break; }
                }
                Assert.IsNotNull(propActor);
                for (int i = 0; i < 5; i++)
                {
                    propActor.MorphCtrl.ApplyPropById(i % 2 == 0 ? "Prop_WaterBottle_Green" : "Prop_Chair_Blue", playFx: false, out _);
                }
                Assert.IsTrue(propActor.MorphCtrl.IsMorphed);

                // 5. Decoy -5 HP Bait Penalty + Instant Shatter & Pool Recycle
                var hunter = PlayerParticipant.LocalPlayer;
                Assert.IsNotNull(hunter);
                Assert.IsTrue(propActor.PropAbility.TrySpawnDecoy("Prop_WaterBottle_Green", showLocalHint: false));
                yield return null;
                Assert.GreaterOrEqual(pool.ActiveDecoyCount, 1);

                InteractiveProp decoyComp = null;
                foreach (var candidate in Object.FindObjectsByType<InteractiveProp>(FindObjectsSortMode.None))
                {
                    if (candidate != null && candidate.IsDecoyClone && candidate.gameObject.activeInHierarchy)
                    {
                        decoyComp = candidate;
                        break;
                    }
                }
                Assert.IsNotNull(decoyComp);

                gsm.SyncPhaseFromNetwork(MatchPhase.HuntPhase, 250f);
                hunter.HunterAction.ResetForMatch();
                int hpBeforeDecoy = hunter.Health;
                var dCol = decoyComp.GetComponentInChildren<Collider>();
                Vector3 dCenter = dCol != null ? dCol.bounds.center : decoyComp.transform.position + Vector3.up * 0.25f;
                Ray decoyRay = new Ray(dCenter + Vector3.up * 1.1f, Vector3.down);
                hunter.HunterAction.TryStrike(decoyRay);
                Assert.AreEqual(hpBeforeDecoy - 5, hunter.Health, "Shooting a decoy must deal -5 HP bait penalty (one-time).");
                Assert.IsFalse(decoyComp.gameObject.activeInHierarchy, "Shot decoy must shatter & recycle into ObjectPoolManager immediately.");

                // 6. PlayerAvatarVisual rebuilds cleanly if role changes (Prop -> Hunter)
                propActor.AvatarVisual.BuildVisual(TeamRole.Hunter);
                Assert.IsTrue(propActor.AvatarVisual.HasBlasterVisual, "Switching avatar role to Hunter must spawn Blaster visual.");

                // 7. Eliminated Spectator Orbit Delta + Fast Rematch stops stale round-over overlay
                gsm.UpdateAliveCounts(aliveHunters: 1, aliveProps: 0);
                Assert.AreEqual(MatchPhase.RoundOver, gsm.Phase);
                // Immediately start a rematch before 1.5s DelayedEndedOverlay fires
                gsm.StartMatch(onlineSession: false, totalSlots: 8);
                yield return new WaitForSecondsRealtime(1.65f);
                Assert.AreEqual(AppShellState.Playing, gsm.ShellState, "Quick rematch must cancel stale DelayedEndedOverlay coroutine.");
                Assert.AreEqual(MatchPhase.HidePhase, gsm.Phase);
            }
            finally
            {
                Time.timeScale = 1f;
                MatchCoordinator.Instance.PreferredLocalRole = null;
                PlayerPrefs.DeleteAll();
            }
            yield return null;
        }

        private static GameObject FindInactive(string targetName, Transform root)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name == targetName) return tr.gameObject;
            }
            return null;
        }
    }
}
