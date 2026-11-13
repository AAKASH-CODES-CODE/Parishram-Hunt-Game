using System;
using System.Collections;
using System.Collections.Generic;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Input;
using CoachingPropHunt.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoachingPropHunt.UI
{
    /// <summary>
    /// Complete Palette-styled ScreenSpaceCamera UI shell & mobile touch HUD:
    /// Title Screen, Online/Bots Room Browser, Hide/Hunt HUD with animated bars & contextual buttons,
    /// Pause & Audio Settings overlay, Round End summary, and Toast/Flash/Milestone overlays.
    /// </summary>
    public sealed class GameUIManager : MonoBehaviour
    {
        public static GameUIManager Instance { get; private set; }

        private Canvas _canvas;
        private RectTransform _hudSafeArea;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private CanvasGroup _titlePanel;
        private CanvasGroup _roomPanel;
        private CanvasGroup _hudPanel;
        private CanvasGroup _hudEditorPanel;
        private CanvasGroup _pausePanel;
        private CanvasGroup _endPanel;

        // Title UI
        private TMP_InputField _titleNameInput;
        private TextMeshProUGUI _titleStatsText;
        private TextMeshProUGUI _titleSlotsLabel;
        private Button _btnRoleRandom;
        private Button _btnRoleProp;
        private Button _btnRoleHunter;

        // Room Browser & Online Pre-Match Lobby UI
        private TMP_InputField _nameInput;
        private TMP_InputField _roomNameInput;
        private TMP_InputField _joinCodeInput;
        private TextMeshProUGUI _roomStatusText;
        private Button _sameWifiBtn;
        private TextMeshProUGUI _sameWifiLabel;
        private RectTransform _roomListContent;
        private Slider _botCountSlider;
        private TextMeshProUGUI _botCountLabel;
        private GameObject _roomBrowserBody;
        private GameObject _roomLobbyOverlay;
        private TextMeshProUGUI _lobbyHeaderText;
        private TextMeshProUGUI _lobbyCodeText;
        private TextMeshProUGUI _lobbyPlayersText;
        private Button _lobbyStartBtn;
        private TextMeshProUGUI _lobbyWaitText;
        private TMP_InputField _lobbyChatInput;
        private TextMeshProUGUI _lobbyChatLogText;
        private readonly System.Text.StringBuilder _chatHistory = new System.Text.StringBuilder();

        // Gameplay HUD UI
        private VirtualThumbstick _thumbstick;
        private TouchLookPad _lookPad;
        private readonly HudLayoutCustomizer _hudCustomizer = new HudLayoutCustomizer();
        private readonly List<GameObject> _hudEditHiddenChrome = new List<GameObject>();
        private GameObject _hudEditorScrim;
        private Button _btnHudTabProp;
        private Button _btnHudTabHunter;
        private TextMeshProUGUI _hudSelectedLabel;
        private Slider _hudScaleSlider;
        private bool _updatingScaleSlider;
        private bool _isEditingHud;

        private TextMeshProUGUI _phaseTimerText;
        private TextMeshProUGUI _teamAliveText;
        private GameObject _roomCodeBadgeGo;
        private TextMeshProUGUI _roomCodeBadgeText;
        private TextMeshProUGUI _roomLocationText;
        private Image _hpFillBar;
        private TextMeshProUGUI _hpText;
        private TextMeshProUGUI _scoreText;
        private TextMeshProUGUI _rolePropText;
        private TextMeshProUGUI _aimPromptText;
        private GameObject _spectatorBar;
        private TextMeshProUGUI _spectatorTargetText;
        private GameObject _hunterBlindOverlay;
        private TextMeshProUGUI _hunterBlindTimerText;
        private GameObject _propButtonsGroup;
        private GameObject _hunterButtonsGroup;
        private Button _unmorphBtn;
        private Button _propRotateLeftBtn;
        private Button _propRotateRightBtn;
        private Button _stunBtn;
        private Button _runBtn;
        private Image _runBtnBg;
        private TextMeshProUGUI _runBtnLabel;
        private TextMeshProUGUI _lockButtonLabel;
        private TextMeshProUGUI _whistleCdText;
        private TextMeshProUGUI _decoyCdText;
        private TextMeshProUGUI _stunCdText;
        private TextMeshProUGUI _radarCdText;
        private Image _strikeCooldownFill;
        private Image _reticleImg;
        private TMP_Text _hitmarkerText;
        private float _hitmarkerTimer;
        private HunterActionController _hookedHunter;
        private RectTransform _killFeedRoot;
        private readonly MorphTargetHighlighter _morphHighlighter = new MorphTargetHighlighter();

        private sealed class PooledKillFeedSlot
        {
            public GameObject root;
            public CanvasGroup cg;
            public TextMeshProUGUI label;
            public Coroutine fadeRoutine;
        }
        private readonly List<PooledKillFeedSlot> _killFeedPool = new List<PooledKillFeedSlot>(4);
        private int _killFeedCursor;

        private struct ActiveMotionPing
        {
            public Image ringImg;
            public TextMeshProUGUI distTxt;
            public Vector3 worldPos;
            public float startTime;
            public bool active;
        }

        private RectTransform _motionPingContainer;
        private readonly List<ActiveMotionPing> _motionPingPool = new List<ActiveMotionPing>();
        private readonly Dictionary<int, float> _lastPropPingTimes = new Dictionary<int, float>();
        private bool _final45ToastShown;

        private struct TeammateNameplateSlot
        {
            public RectTransform root;
            public Image bg;
            public TextMeshProUGUI text;
        }
        private RectTransform _nameplateContainer;
        private readonly List<TeammateNameplateSlot> _nameplatePool = new List<TeammateNameplateSlot>();

        // Overlays
        private Image _flashOverlay;
        private CanvasGroup _toastGroup;
        private TextMeshProUGUI _toastText;
        private CanvasGroup _milestoneGroup;
        private RectTransform _milestoneRect;
        private TextMeshProUGUI _milestoneText;

        // Pause & Settings UI
        private TextMeshProUGUI _pauseTitleText;
        private Slider _musicSlider;
        private Slider _sfxSlider;
        private Slider _camSensSlider;
        private Slider _propSensSlider;
        private TextMeshProUGUI _musicValText;
        private TextMeshProUGUI _sfxValText;
        private TextMeshProUGUI _camSensValText;
        private TextMeshProUGUI _propSensValText;
        private Button _btnQualLow;
        private Button _btnQualMed;
        private Button _btnQualHigh;
        private Button _pauseResumeBtn;
        private TextMeshProUGUI _pauseResumeLbl;
        private Button _pauseQuitBtn;
        private TextMeshProUGUI _endWinnerText;
        private TextMeshProUGUI _endReasonText;
        private TextMeshProUGUI _endSummaryText;

        private float _displayedHpRatio = 1f;
        private float _displayedScore;
        private float _nextAimScanTime;
        private string _cachedAimPrompt = "";
        private Coroutine _flashRoutine;
        private Coroutine _milestoneRoutine;
        private Coroutine _toastRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EnsureEventSystem();
            BuildCanvasHierarchy();
        }

        private void Start()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                if (_nameInput != null) _nameInput.SetTextWithoutNotify(gsm.Progress.playerName);
                if (_titleNameInput != null) _titleNameInput.SetTextWithoutNotify(gsm.Progress.playerName);
                gsm.OnShellStateChanged += HandleShellStateChanged;
                gsm.OnToast += ShowToast;
                gsm.OnScreenFlash += TriggerFlash;
                gsm.OnMilestoneBanner += ShowMilestone;
                HandleShellStateChanged(gsm.ShellState);
            }
            if (MultiplayerRoomManager.Instance != null)
                MultiplayerRoomManager.Instance.OnRoomStateChanged += RefreshOnlineLobbyState;
            NetworkPlayerState.OnPeersChanged += RefreshOnlineLobbyState;

            if (Networking.SocketIO.SocketIOUnityClient.Instance != null)
            {
                Networking.SocketIO.SocketIOUnityClient.Instance.OnNewChat += msg =>
                {
                    AppendLobbyChat(msg.sender_name, msg.message, msg.sender_role);
                };
            }

            PlayerParticipant.OnAnyKillFeed += HandleKillFeedEvent;
            ApplySafeArea();
        }

        private void OnDestroy()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.OnShellStateChanged -= HandleShellStateChanged;
                gsm.OnToast -= ShowToast;
                gsm.OnScreenFlash -= TriggerFlash;
                gsm.OnMilestoneBanner -= ShowMilestone;
            }
            if (MultiplayerRoomManager.Instance != null)
                MultiplayerRoomManager.Instance.OnRoomStateChanged -= RefreshOnlineLobbyState;
            NetworkPlayerState.OnPeersChanged -= RefreshOnlineLobbyState;
            PlayerParticipant.OnAnyKillFeed -= HandleKillFeedEvent;
            if (_hookedHunter != null) _hookedHunter.OnStrikeHitFeedback -= HandleStrikeHitFeedback;
        }

        private void Update()
        {
            ApplySafeArea();
            if (_isEditingHud) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive)
            {
                _morphHighlighter.UpdateTarget(null, 0f);
                ClearAllNameplates();
                return;
            }
            UpdateGameplayHud(gsm);
        }

        private void ApplySafeArea()
        {
            if (_hudSafeArea == null) return;
            Rect sa = Screen.safeArea;
            var res = new Vector2Int(Screen.width, Screen.height);
            if (sa == _lastSafeArea && res == _lastScreenSize) return;
            _lastSafeArea = sa;
            _lastScreenSize = res;
            if (res.x <= 0 || res.y <= 0) return;

            Vector2 min = sa.position;
            Vector2 max = sa.position + sa.size;
            min.x /= res.x;
            min.y /= res.y;
            max.x /= res.x;
            max.y /= res.y;
            _hudSafeArea.anchorMin = min;
            _hudSafeArea.anchorMax = max;
            _hudSafeArea.offsetMin = Vector2.zero;
            _hudSafeArea.offsetMax = Vector2.zero;
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildCanvasHierarchy()
        {
            var canvasGo = new GameObject("MainGameCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            Transform root = canvasGo.transform;
            _titlePanel = BuildTitleScreen(root);
            _roomPanel = BuildRoomBrowserScreen(root);
            _hudPanel = BuildGameplayHud(root);
            _hudEditorPanel = BuildHudEditorToolbar(root);
            _pausePanel = BuildPauseOverlay(root);
            _endPanel = BuildRoundEndOverlay(root);
            BuildTransientOverlays(root);
        }

        private CanvasGroup BuildTitleScreen(Transform parent)
        {
            var cg = CreateScreenGroup("TitleScreen", parent);

            // Subtle top & bottom gradient vignettes ONLY — keeps center 60% wide open for the looping drone shot!
            var topVig = CreateCard("TopVignette", cg.transform, Vector2.zero, new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.56f));
            topVig.anchorMin = new Vector2(0f, 0.78f);
            topVig.anchorMax = new Vector2(1f, 1f);
            topVig.offsetMin = Vector2.zero;
            topVig.offsetMax = Vector2.zero;
            topVig.GetComponent<Image>().raycastTarget = false;

            var botVig = CreateCard("BottomVignette", cg.transform, Vector2.zero, new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.72f));
            botVig.anchorMin = new Vector2(0f, 0f);
            botVig.anchorMax = new Vector2(1f, 0.27f);
            botVig.offsetMin = Vector2.zero;
            botVig.offsetMax = Vector2.zero;
            botVig.GetComponent<Image>().raycastTarget = false;

            // Top-Left Identity + Game Logo + Player Name Input + Career Chip
            var brandDock = new GameObject("BrandDock", typeof(RectTransform)).GetComponent<RectTransform>();
            brandDock.SetParent(cg.transform, false);
            brandDock.anchorMin = new Vector2(0f, 1f);
            brandDock.anchorMax = new Vector2(0f, 1f);
            brandDock.pivot = new Vector2(0f, 1f);
            brandDock.anchoredPosition = new Vector2(44f, -26f);
            brandDock.sizeDelta = new Vector2(820f, 186f);

            var logoCard = CreateCard("TitleLogoCard", brandDock, new Vector2(96f, 96f), Palette.PanelDark);
            logoCard.anchorMin = new Vector2(0f, 1f);
            logoCard.anchorMax = new Vector2(0f, 1f);
            logoCard.pivot = new Vector2(0f, 1f);
            logoCard.anchoredPosition = new Vector2(0f, -6f);
            var logoImgGo = new GameObject("LogoImg", typeof(RectTransform), typeof(Image));
            logoImgGo.transform.SetParent(logoCard, false);
            var logoRect = logoImgGo.GetComponent<RectTransform>();
            logoRect.anchorMin = Vector2.zero;
            logoRect.anchorMax = Vector2.one;
            logoRect.offsetMin = new Vector2(4f, 4f);
            logoRect.offsetMax = new Vector2(-4f, -4f);
            var logoImg = logoImgGo.GetComponent<Image>();
            var brandSprite = LoadBrandLogoSprite();
            if (brandSprite != null)
            {
                logoImg.sprite = brandSprite;
                logoImg.preserveAspect = true;
                logoImg.color = Color.white;
            }
            else
            {
                logoImg.sprite = HudIcons.Get(HudIconKind.Play);
                logoImg.color = Palette.AccentGold;
            }
            logoImg.raycastTarget = false;

            CreateLabel("Badge", brandDock, "PARISHRAM COACHING CAMPUS  •  LIVE CAMPUS TOUR", 18, Palette.PropAccent, TextAlignmentOptions.Left, new Vector2(56f, 66f), new Vector2(690f, 28f));
            CreateLabel("Title", brandDock, "PARISHRAM HUNT", 46, Palette.AccentGold, TextAlignmentOptions.Left, new Vector2(56f, 26f), new Vector2(690f, 54f));
            _titleStatsText = CreateLabel("StatsChip", brandDock, "Best Score: 0  |  Matches: 0  |  Survived: 0", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(56f, -14f), new Vector2(690f, 30f));

            var nameCard = CreateCard("TitleNameDock", brandDock, new Vector2(440f, 56f), Palette.PanelGlass);
            nameCard.anchorMin = new Vector2(0f, 0f);
            nameCard.anchorMax = new Vector2(0f, 0f);
            nameCard.pivot = new Vector2(0f, 0f);
            nameCard.anchoredPosition = new Vector2(0f, 0f);
            CreateLabel("TitleNameLbl", nameCard, "NAME:", 18, Palette.PropAccent, TextAlignmentOptions.Left, new Vector2(-150f, 0f), new Vector2(110f, 34f));
            string savedName = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
            _titleNameInput = CreateInputField("TitlePlayerNameInput", nameCard, string.IsNullOrEmpty(savedName) ? "Student" : savedName, new Vector2(42f, 0f), new Vector2(288f, 42f));
            _titleNameInput.onValueChanged.AddListener(v =>
            {
                if (_nameInput != null) _nameInput.SetTextWithoutNotify(v);
            });
            _titleNameInput.onEndEdit.AddListener(v =>
            {
                GameStateManager.Instance?.SetPlayerName(v);
                string saved = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
                _titleNameInput.SetTextWithoutNotify(saved);
                if (_nameInput != null) _nameInput.SetTextWithoutNotify(saved);
            });

            // Top-Right Utility Bar: Match Slots Stepper + Settings Button
            var utilDock = CreateCard("TopUtilityCard", cg.transform, new Vector2(540f, 78f), Palette.PanelGlass);
            utilDock.anchorMin = new Vector2(1f, 1f);
            utilDock.anchorMax = new Vector2(1f, 1f);
            utilDock.pivot = new Vector2(1f, 1f);
            utilDock.anchoredPosition = new Vector2(-42f, -34f);

            _titleSlotsLabel = CreateLabel("TitleSlotsLbl", utilDock, "PLAYERS: 12", 21, Palette.TextLight, TextAlignmentOptions.Center, new Vector2(-135f, 0f), new Vector2(160f, 40f));
            CreateButton("TitleSlotsMinus", utilDock, "-", new Vector2(-232f, 0f), new Vector2(44f, 44f), Palette.PanelMid, () => AdjustMatchSlots(-2));
            CreateButton("TitleSlotsPlus", utilDock, "+", new Vector2(-40f, 0f), new Vector2(44f, 44f), Palette.PanelMid, () => AdjustMatchSlots(2));
            CreateButtonWithIcon("SettingsBtn", utilDock, "SETTINGS", HudIconKind.Settings, new Vector2(142f, 0f), new Vector2(214f, 52f), Palette.HunterNavy, OpenSettingsMenu);

            // Bottom-Left Team Preference Dock
            var teamDock = CreateCard("TeamSelectorDock", cg.transform, new Vector2(640f, 124f), Palette.PanelGlass);
            teamDock.anchorMin = new Vector2(0f, 0f);
            teamDock.anchorMax = new Vector2(0f, 0f);
            teamDock.pivot = new Vector2(0f, 0f);
            teamDock.anchoredPosition = new Vector2(44f, 36f);

            CreateLabel("TeamPrefLbl", teamDock, "PREFERRED TEAM ROLE", 18, Palette.TextMuted, TextAlignmentOptions.Left, new Vector2(-150f, 36f), new Vector2(290f, 28f));
            _btnRoleRandom = CreateButton("RoleRandom", teamDock, "RANDOM", new Vector2(-202f, -14f), new Vector2(186f, 54f), Palette.PanelMid, () => SelectPreferredRole(null));
            _btnRoleProp = CreateButton("RoleProp", teamDock, "PROP", new Vector2(0f, -14f), new Vector2(186f, 54f), Palette.PropTeal, () => SelectPreferredRole(TeamRole.Prop));
            _btnRoleHunter = CreateButton("RoleHunter", teamDock, "HUNTER", new Vector2(202f, -14f), new Vector2(186f, 54f), Palette.HunterNavy, () => SelectPreferredRole(TeamRole.Hunter));
            HighlightRoleButtons(null);

            // Bottom-Right Play Action Cards
            var actionsDock = new GameObject("PlayActionsDock", typeof(RectTransform)).GetComponent<RectTransform>();
            actionsDock.SetParent(cg.transform, false);
            actionsDock.anchorMin = new Vector2(1f, 0f);
            actionsDock.anchorMax = new Vector2(1f, 0f);
            actionsDock.pivot = new Vector2(1f, 0f);
            actionsDock.anchoredPosition = new Vector2(-42f, 36f);
            actionsDock.sizeDelta = new Vector2(780f, 132f);

            CreateButtonWithIcon("OnlineRoomsBtn", actionsDock, "ONLINE ROOMS / CODE", HudIconKind.Online, new Vector2(-195f, 0f), new Vector2(360f, 92f), Palette.HunterNavy, () =>
            {
                GameStateManager.Instance?.OpenRoomBrowser();
                RefreshOpenRooms();
            });

            CreateButtonWithIcon("PlayOfflineBtn", actionsDock, "PLAY WITH AI BOTS", HudIconKind.Play, new Vector2(195f, 0f), new Vector2(360f, 92f), Palette.PropTeal, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                int slots = GameStateManager.Instance != null ? GameStateManager.Instance.TargetTotalPlayers : 12;
                GameStateManager.Instance?.StartMatch(false, slots);
            });

            return cg;
        }

        private CanvasGroup BuildRoomBrowserScreen(Transform parent)
        {
            var cg = CreateScreenGroup("RoomBrowserScreen", parent);
            var backdrop = cg.gameObject.AddComponent<Image>();
            backdrop.sprite = Palette.GetRoundedRectSprite();
            backdrop.type = Image.Type.Sliced;
            backdrop.color = new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.55f);

            var card = CreateCard("RoomCard", cg.transform, Vector2.zero, new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.90f));
            card.anchorMin = new Vector2(0.04f, 0.05f);
            card.anchorMax = new Vector2(0.96f, 0.95f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = Vector2.zero;

            var headerLbl = CreateLabel("Header", card, "MULTIPLAYER ROOMS & BOT MATCH", 38, Palette.AccentGold, TextAlignmentOptions.Left, Vector2.zero, new Vector2(780f, 56f));
            headerLbl.rectTransform.anchorMin = new Vector2(0f, 1f);
            headerLbl.rectTransform.anchorMax = new Vector2(0f, 1f);
            headerLbl.rectTransform.anchoredPosition = new Vector2(420f, -46f);

            var backBtn = CreateButton("BackBtn", card, "BACK", Vector2.zero, new Vector2(160f, 52f), Palette.DangerRed, () =>
            {
                _ = MultiplayerRoomManager.Instance?.LeaveRoomAsync();
                GameStateManager.Instance?.ReturnToTitle();
            });
            var backRect = backBtn.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(1f, 1f);
            backRect.anchorMax = new Vector2(1f, 1f);
            backRect.anchoredPosition = new Vector2(-115f, -46f);

            var bodyGo = new GameObject("RoomBrowserBody", typeof(RectTransform));
            bodyGo.transform.SetParent(card, false);
            var bodyRect = bodyGo.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0.02f, 0.03f);
            bodyRect.anchorMax = new Vector2(0.98f, 0.89f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;
            _roomBrowserBody = bodyGo;

            // Left column: Create / Player Name / Join by Code (clean anchored container)
            var leftCol = new GameObject("LeftPane", typeof(RectTransform)).GetComponent<RectTransform>();
            leftCol.SetParent(bodyRect, false);
            leftCol.anchorMin = new Vector2(0f, 0f);
            leftCol.anchorMax = new Vector2(0.45f, 1f);
            leftCol.offsetMin = Vector2.zero;
            leftCol.offsetMax = Vector2.zero;

            CreateLabel("NameLbl", leftCol, "PLAYER NAME", 20, Palette.TextMuted, TextAlignmentOptions.Left, new Vector2(0f, 290f), new Vector2(460f, 30f));
            _nameInput = CreateInputField("PlayerNameInput", leftCol, "Student", new Vector2(0f, 245f), new Vector2(460f, 52f));

            CreateLabel("RoomNameLbl", leftCol, "CREATE ONLINE ROOM NAME", 20, Palette.TextMuted, TextAlignmentOptions.Left, new Vector2(0f, 178f), new Vector2(460f, 30f));
            _roomNameInput = CreateInputField("RoomNameInput", leftCol, "PatnaCoaching16", new Vector2(0f, 132f), new Vector2(460f, 52f));

            _botCountLabel = CreateLabel("PlayersSliderLbl", leftCol, "MATCH SLOTS (ONLINE + BOTS): 12", 20, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(0f, 68f), new Vector2(460f, 30f));
            _botCountSlider = CreateSlider("SlotsSlider", leftCol, new Vector2(0f, 26f), new Vector2(460f, 30f), 4f, 16f, 12f, true, v =>
            {
                int count = Mathf.RoundToInt(v);
                if (GameStateManager.Instance != null) GameStateManager.Instance.TargetTotalPlayers = count;
                if (_botCountLabel != null) _botCountLabel.text = $"MATCH SLOTS (ONLINE + BOTS): {count}";
                if (_titleSlotsLabel != null) _titleSlotsLabel.text = $"PLAYERS: {count}";
            });

            CreateButtonWithIcon("CreateOnlineBtn", leftCol, "CREATE ONLINE ROOM", HudIconKind.Online, new Vector2(0f, -26f), new Vector2(460f, 58f), Palette.PropTeal, OnCreateOnlineClicked);

            _sameWifiBtn = CreateButton("SameWifiToggleBtn", leftCol, "SAME WI-FI / HOTSPOT FAST MODE: OFF", new Vector2(0f, -84f), new Vector2(460f, 42f), Palette.PanelMid, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                var mrm = MultiplayerRoomManager.Instance;
                if (mrm != null) mrm.UseSameWifiDirect = !mrm.UseSameWifiDirect;
                RefreshSameWifiButton();
            });
            _sameWifiLabel = _sameWifiBtn.GetComponentInChildren<TextMeshProUGUI>();
            RefreshSameWifiButton();

            CreateLabel("CodeLbl", leftCol, "JOIN BY ROOM CODE", 20, Palette.TextMuted, TextAlignmentOptions.Left, new Vector2(0f, -132f), new Vector2(460f, 28f));
            _joinCodeInput = CreateInputField("JoinCodeInput", leftCol, "ENTER CODE...", new Vector2(-78f, -178f), new Vector2(304f, 52f));
            _joinCodeInput.onValueChanged.AddListener(v =>
            {
                string upper = v.ToUpperInvariant();
                if (upper != v) _joinCodeInput.text = upper;
            });
            CreateButton("JoinCodeBtn", leftCol, "JOIN", new Vector2(158f, -178f), new Vector2(144f, 52f), Palette.HunterAccent, OnJoinByCodeClicked);

            CreateButtonWithIcon("PlayOfflineFallbackBtn", leftCol, "LAUNCH WITH AI BOTS", HudIconKind.Play, new Vector2(0f, -258f), new Vector2(460f, 64f), Palette.AccentGold, () =>
            {
                SavePlayerName();
                GameStateManager.Instance?.StartMatch(false, Mathf.RoundToInt(_botCountSlider.value));
            });

            // Right column: Scrollable Open Room Browser list
            var rightCol = new GameObject("RightPane", typeof(RectTransform)).GetComponent<RectTransform>();
            rightCol.SetParent(bodyRect, false);
            rightCol.anchorMin = new Vector2(0.47f, 0f);
            rightCol.anchorMax = new Vector2(1f, 1f);
            rightCol.offsetMin = Vector2.zero;
            rightCol.offsetMax = Vector2.zero;

            var openLbl = CreateLabel("OpenRoomsLbl", rightCol, "OPEN COACHING ROOMS", 24, Palette.PropAccent, TextAlignmentOptions.Left, Vector2.zero, new Vector2(440f, 38f));
            openLbl.rectTransform.anchorMin = new Vector2(0f, 1f);
            openLbl.rectTransform.anchorMax = new Vector2(0f, 1f);
            openLbl.rectTransform.anchoredPosition = new Vector2(230f, -28f);

            var refBtn = CreateButton("RefreshRoomsBtn", rightCol, "REFRESH", Vector2.zero, new Vector2(170f, 48f), Palette.PanelMid, RefreshOpenRooms);
            var refRect = refBtn.GetComponent<RectTransform>();
            refRect.anchorMin = new Vector2(1f, 1f);
            refRect.anchorMax = new Vector2(1f, 1f);
            refRect.anchoredPosition = new Vector2(-95f, -28f);

            var listBox = CreateCard("RoomListContainer", rightCol, Vector2.zero, Palette.PanelMid);
            listBox.anchorMin = new Vector2(0f, 0.12f);
            listBox.anchorMax = new Vector2(1f, 0.91f);
            listBox.offsetMin = Vector2.zero;
            listBox.offsetMax = Vector2.zero;
            var scroll = listBox.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(listBox, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-10f, -10f);
            scroll.viewport = viewport;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
            _roomListContent = contentGo.GetComponent<RectTransform>();
            _roomListContent.anchorMin = new Vector2(0f, 1f);
            _roomListContent.anchorMax = new Vector2(1f, 1f);
            _roomListContent.pivot = new Vector2(0.5f, 1f);
            _roomListContent.anchoredPosition = Vector2.zero;
            _roomListContent.sizeDelta = new Vector2(0f, 0f);
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _roomListContent;

            _roomStatusText = CreateLabel("RoomStatus", rightCol, "Choose Quick Bots or Create/Join an Online Room (up to 16 players).", 20, Palette.TextMuted, TextAlignmentOptions.Left, Vector2.zero, new Vector2(700f, 60f));
            _roomStatusText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _roomStatusText.rectTransform.anchorMax = new Vector2(1f, 0.10f);
            _roomStatusText.rectTransform.offsetMin = Vector2.zero;
            _roomStatusText.rectTransform.offsetMax = Vector2.zero;

            // Pre-Match Online Lobby Sub-Panel (shown while waiting inside a connected room)
            var lobbyGo = new GameObject("OnlineLobbyOverlay", typeof(RectTransform));
            lobbyGo.transform.SetParent(card, false);
            var lr = lobbyGo.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;
            _roomLobbyOverlay = lobbyGo;

            _lobbyHeaderText = CreateLabel("LobbyHeader", lr, "ROOM: COACHING MATCH", 34, Palette.PropAccent, TextAlignmentOptions.Center, new Vector2(0f, 240f), new Vector2(1000f, 50f));
            var codeCard = CreateCard("LobbyCodeCard", lr, new Vector2(560f, 94f), Palette.PanelMid);
            codeCard.anchoredPosition = new Vector2(-120f, 145f);
            _lobbyCodeText = CreateLabel("LobbyCodeTxt", codeCard, "ROOM CODE: ------", 36, Palette.AccentGold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(520f, 60f));
            CreateButton("CopyCodeBtn", lr, "COPY CODE", new Vector2(280f, 145f), new Vector2(200f, 74f), Palette.HunterNavy, () =>
            {
                string code = MultiplayerRoomManager.Instance?.CurrentJoinCode ?? "";
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    ShowToast($"Copied Join Code: {code}", Palette.AccentGold);
                }
            });

            var playersBox = CreateCard("LobbyPlayersCard", lr, new Vector2(480f, 260f), Palette.PanelMid);
            playersBox.anchoredPosition = new Vector2(-240f, -55f);
            _lobbyPlayersText = CreateLabel("LobbyPlayersTxt", playersBox, "Connected Players:\n1. Student (Host)", 22, Palette.TextLight, TextAlignmentOptions.TopLeft, new Vector2(0f, 0f), new Vector2(440f, 220f));

            var chatBox = CreateCard("LobbyChatCard", lr, new Vector2(460f, 260f), Palette.PanelMid);
            chatBox.anchoredPosition = new Vector2(250f, -55f);
            _lobbyChatLogText = CreateLabel("LobbyChatTxt", chatBox, "--- ROOM TEXT CHAT ---\nWelcome to the room!", 18, Palette.AccentGold, TextAlignmentOptions.TopLeft, new Vector2(0f, 20f), new Vector2(430f, 170f));
            _lobbyChatInput = CreateInputField("LobbyChatInput", chatBox, "Type message...", new Vector2(-60f, -95f), new Vector2(290f, 44f));
            CreateButton("SendChatBtn", chatBox, "SEND", new Vector2(150f, -95f), new Vector2(90f, 44f), Palette.PropTeal, OnSendLobbyChatClicked);

            _lobbyStartBtn = CreateButton("LobbyStartBtn", lr, "START MATCH NOW", new Vector2(-165f, -265f), new Vector2(360f, 72f), Palette.PropTeal, OnHostStartLobbyMatchClicked);
            _lobbyWaitText = CreateLabel("LobbyWaitTxt", lr, "Waiting for Host to start the match...", 26, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(-165f, -265f), new Vector2(520f, 64f));
            CreateButton("LeaveLobbyBtn", lr, "LEAVE ROOM", new Vector2(240f, -265f), new Vector2(260f, 72f), Palette.DangerRed, async () =>
            {
                if (MultiplayerRoomManager.Instance != null) await MultiplayerRoomManager.Instance.LeaveRoomAsync();
                RefreshOnlineLobbyState();
            });
            lobbyGo.SetActive(false);
            return cg;
        }

        private CanvasGroup BuildGameplayHud(Transform parent)
        {
            var cg = CreateScreenGroup("GameplayHUD", parent);

            var safeGo = new GameObject("HudSafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(cg.transform, false);
            _hudSafeArea = safeGo.GetComponent<RectTransform>();
            _hudSafeArea.anchorMin = Vector2.zero;
            _hudSafeArea.anchorMax = Vector2.one;
            _hudSafeArea.offsetMin = Vector2.zero;
            _hudSafeArea.offsetMax = Vector2.zero;

            _hudEditHiddenChrome.Clear();

            // Dark scrim sits at sibling 0 behind buttons ONLY while SETTING HUD is open
            var scrimGo = new GameObject("HudEditorScrim", typeof(RectTransform), typeof(Image));
            scrimGo.transform.SetParent(_hudSafeArea, false);
            var scrimImg = scrimGo.GetComponent<Image>();
            scrimImg.sprite = Palette.GetRoundedRectSprite();
            scrimImg.color = new Color(0.03f, 0.05f, 0.10f, 0.80f);
            scrimImg.raycastTarget = false;
            var scrimRect = scrimGo.GetComponent<RectTransform>();
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;
            _hudEditorScrim = scrimGo;
            _hudEditorScrim.SetActive(false);

            // Touch zones FIRST so every visible HUD button renders & intercepts input above them!
            var joyZoneGo = new GameObject("LeftJoystickZone", typeof(RectTransform), typeof(Image), typeof(LeftJoystickZone));
            joyZoneGo.transform.SetParent(_hudSafeArea, false);
            joyZoneGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            var joyRect = joyZoneGo.GetComponent<RectTransform>();
            joyRect.anchorMin = new Vector2(0f, 0f);
            joyRect.anchorMax = new Vector2(0.45f, 0.78f);
            joyRect.offsetMin = Vector2.zero;
            joyRect.offsetMax = Vector2.zero;

            var lookPadGo = new GameObject("RightLookTouchPad", typeof(RectTransform), typeof(Image), typeof(TouchLookPad));
            lookPadGo.transform.SetParent(_hudSafeArea, false);
            lookPadGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            var lookRect = lookPadGo.GetComponent<RectTransform>();
            lookRect.anchorMin = new Vector2(0.42f, 0f);
            lookRect.anchorMax = new Vector2(1f, 1f);
            lookRect.offsetMin = Vector2.zero;
            lookRect.offsetMax = Vector2.zero;
            _lookPad = lookPadGo.GetComponent<TouchLookPad>();

            // Top center timer + alive count pill + online room code badge
            var topPill = CreateCard("TopStatusPill", _hudSafeArea, new Vector2(540f, 92f), Palette.PanelGlass);
            topPill.anchorMin = new Vector2(0.5f, 1f);
            topPill.anchorMax = new Vector2(0.5f, 1f);
            topPill.anchoredPosition = new Vector2(0f, -60f);
            _phaseTimerText = CreateLabel("PhaseTimer", topPill, "HIDE PHASE  01:00", 30, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(0f, 16f), new Vector2(500f, 38f));
            _teamAliveText = CreateLabel("AliveTeams", topPill, "PROPS: 0   |   HUNTERS: 0", 22, Palette.TextLight, TextAlignmentOptions.Center, new Vector2(0f, -20f), new Vector2(500f, 32f));
            _hudEditHiddenChrome.Add(topPill.gameObject);

            var codeBadge = CreateCard("RoomCodeHudBadge", _hudSafeArea, new Vector2(230f, 36f), Palette.PanelDark);
            codeBadge.anchorMin = new Vector2(0.5f, 1f);
            codeBadge.anchorMax = new Vector2(0.5f, 1f);
            codeBadge.anchoredPosition = new Vector2(0f, -126f);
            _roomCodeBadgeText = CreateLabel("CodeBadgeLbl", codeBadge, "CODE: ------", 19, Palette.PropAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(215f, 30f));
            _roomCodeBadgeGo = codeBadge.gameObject;
            _roomCodeBadgeGo.SetActive(false);

            // Top left player HP & disguised prop chip + live Campus Room chip
            var statsCard = CreateCard("PlayerStatsCard", _hudSafeArea, new Vector2(390f, 126f), Palette.PanelGlass);
            statsCard.anchorMin = new Vector2(0f, 1f);
            statsCard.anchorMax = new Vector2(0f, 1f);
            statsCard.anchoredPosition = new Vector2(220f, -76f);
            _hudEditHiddenChrome.Add(statsCard.gameObject);

            var locCard = CreateCard("RoomLocationChip", _hudSafeArea, new Vector2(390f, 38f), Palette.PanelDark);
            locCard.anchorMin = new Vector2(0f, 1f);
            locCard.anchorMax = new Vector2(0f, 1f);
            locCard.anchoredPosition = new Vector2(220f, -168f);
            _roomLocationText = CreateLabel("RoomLocTxt", locCard, "GF • COACHING HALL", 18, Palette.PropAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(365f, 30f));
            _hudEditHiddenChrome.Add(locCard.gameObject);

            // Top-right fading elimination kill feed (below PauseHudBtn)
            var kfGo = new GameObject("KillFeedPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            kfGo.transform.SetParent(_hudSafeArea, false);
            _killFeedRoot = kfGo.GetComponent<RectTransform>();
            _killFeedRoot.anchorMin = new Vector2(1f, 1f);
            _killFeedRoot.anchorMax = new Vector2(1f, 1f);
            _killFeedRoot.pivot = new Vector2(1f, 1f);
            _killFeedRoot.anchoredPosition = new Vector2(-28f, -120f);
            _killFeedRoot.sizeDelta = new Vector2(390f, 160f);
            var kfLayout = kfGo.GetComponent<VerticalLayoutGroup>();
            kfLayout.spacing = 6f;
            kfLayout.childAlignment = TextAnchor.UpperRight;
            kfLayout.childControlHeight = false;
            kfLayout.childForceExpandHeight = false;
            kfLayout.childControlWidth = false;
            _hudEditHiddenChrome.Add(kfGo);

            _killFeedPool.Clear();
            for (int i = 0; i < 3; i++)
            {
                var card = CreateCard($"KillFeedSlot_{i}", _killFeedRoot, new Vector2(390f, 38f), Palette.PanelDark);
                var img = card.GetComponent<Image>();
                img.raycastTarget = false;
                var cgSlot = card.gameObject.AddComponent<CanvasGroup>();
                cgSlot.alpha = 0f;
                var lbl = CreateLabel("KfLbl", card, "", 16, Palette.AccentGold, TextAlignmentOptions.Right, Vector2.zero, new Vector2(366f, 30f));
                card.gameObject.SetActive(false);
                _killFeedPool.Add(new PooledKillFeedSlot
                {
                    root = card.gameObject,
                    cg = cgSlot,
                    label = lbl,
                    fadeRoutine = null
                });
            }

            var hpBg = new GameObject("HpBarBg", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            hpBg.transform.SetParent(statsCard, false);
            Palette.StylePanel(hpBg, Palette.PanelDark);
            hpBg.rectTransform.anchoredPosition = new Vector2(0f, 26f);
            hpBg.rectTransform.sizeDelta = new Vector2(340f, 26f);

            var hpFillGo = new GameObject("HpBarFill", typeof(RectTransform), typeof(Image));
            hpFillGo.transform.SetParent(hpBg.transform, false);
            _hpFillBar = hpFillGo.GetComponent<Image>();
            _hpFillBar.sprite = Palette.GetRoundedRectSprite();
            _hpFillBar.type = Image.Type.Filled;
            _hpFillBar.fillMethod = Image.FillMethod.Horizontal;
            _hpFillBar.color = Palette.SuccessEmerald;
            _hpFillBar.rectTransform.anchorMin = Vector2.zero;
            _hpFillBar.rectTransform.anchorMax = Vector2.one;
            _hpFillBar.rectTransform.offsetMin = Vector2.zero;
            _hpFillBar.rectTransform.offsetMax = Vector2.zero;

            _hpText = CreateLabel("HpText", statsCard, "HP 100 / 100", 20, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(-80f, -4f), new Vector2(180f, 28f));
            _scoreText = CreateLabel("ScoreText", statsCard, "SCORE: 0", 22, Palette.AccentGold, TextAlignmentOptions.Right, new Vector2(85f, -4f), new Vector2(170f, 28f));
            _rolePropText = CreateLabel("RolePropText", statsCard, "DISGUISE: HUMAN", 20, Palette.PropAccent, TextAlignmentOptions.Left, new Vector2(0f, -36f), new Vector2(340f, 28f));

            // Center crosshair & prompt
            var reticle = new GameObject("CrosshairRing", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            reticle.transform.SetParent(_hudSafeArea, false);
            reticle.sprite = Palette.GetRingSprite();
            reticle.color = new Color(Palette.TextLight.r, Palette.TextLight.g, Palette.TextLight.b, 0.78f);
            reticle.rectTransform.sizeDelta = new Vector2(28f, 28f);
            reticle.raycastTarget = false;
            _reticleImg = reticle;
            _hudEditHiddenChrome.Add(reticle.gameObject);

            var hitTmp = CreateLabel("HitmarkerX", _hudSafeArea, "X", 34, Palette.HunterAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(46f, 46f));
            hitTmp.alpha = 0f;
            _hitmarkerText = hitTmp;
            _hudEditHiddenChrome.Add(hitTmp.gameObject);

            // Subtle Last-45s Through-Wall Moving Prop Motion Ping Container (HUD overlay, non-blocking)
            var pingGo = new GameObject("MotionPingLayer", typeof(RectTransform));
            pingGo.transform.SetParent(_hudSafeArea, false);
            _motionPingContainer = pingGo.GetComponent<RectTransform>();
            _motionPingContainer.anchorMin = Vector2.zero;
            _motionPingContainer.anchorMax = Vector2.one;
            _motionPingContainer.offsetMin = Vector2.zero;
            _motionPingContainer.offsetMax = Vector2.zero;
            _hudEditHiddenChrome.Add(pingGo);
            _motionPingPool.Clear();
            for (int i = 0; i < 8; i++)
            {
                var pItem = new GameObject($"MotionPing_{i}", typeof(RectTransform), typeof(Image));
                pItem.transform.SetParent(_motionPingContainer, false);
                var pImg = pItem.GetComponent<Image>();
                pImg.sprite = Palette.GetRingSprite();
                pImg.color = new Color(Palette.HunterAccent.r, Palette.HunterAccent.g, Palette.HunterAccent.b, 0f);
                pImg.raycastTarget = false;
                var mpRect = pImg.rectTransform;
                mpRect.sizeDelta = new Vector2(56f, 56f);
                var dTxt = CreateLabel("Dist", pItem.transform, "", 13, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(0f, -34f), new Vector2(96f, 22f));
                pItem.SetActive(false);
                _motionPingPool.Add(new ActiveMotionPing { ringImg = pImg, distTxt = dTxt, active = false });
            }

            _aimPromptText = CreateLabel("AimPrompt", _hudSafeArea, "", 24, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(0f, -54f), new Vector2(560f, 38f));
            _hudEditHiddenChrome.Add(_aimPromptText.gameObject);

            // Pooled Teammate-Only Nameplate Layer (Hunter sees Hunters, Prop sees Props)
            var npGo = new GameObject("TeammateNameplateLayer", typeof(RectTransform));
            npGo.transform.SetParent(_hudSafeArea, false);
            _nameplateContainer = npGo.GetComponent<RectTransform>();
            _nameplateContainer.anchorMin = Vector2.zero;
            _nameplateContainer.anchorMax = Vector2.one;
            _nameplateContainer.offsetMin = Vector2.zero;
            _nameplateContainer.offsetMax = Vector2.zero;
            _hudEditHiddenChrome.Add(npGo);
            _nameplatePool.Clear();
            for (int i = 0; i < 16; i++)
            {
                var pCard = CreateCard($"MatePlate_{i}", _nameplateContainer, new Vector2(168f, 30f), new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.78f));
                var pImg = pCard.GetComponent<Image>();
                pImg.raycastTarget = false;
                var pTxt = CreateLabel("Name", pCard, "", 16, Palette.PropAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(158f, 26f));
                pCard.gameObject.SetActive(false);
                _nameplatePool.Add(new TeammateNameplateSlot { root = pCard, bg = pImg, text = pTxt });
            }

            // Spectator control bar (shown when eliminated)
            var specCard = CreateCard("SpectatorBar", _hudSafeArea, new Vector2(540f, 68f), Palette.PanelDark);
            specCard.anchorMin = new Vector2(0.5f, 0f);
            specCard.anchorMax = new Vector2(0.5f, 0f);
            specCard.anchoredPosition = new Vector2(0f, 52f);
            _spectatorTargetText = CreateLabel("SpecTargetTxt", specCard, "SPECTATING: TEAM", 22, Palette.PropAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(310f, 44f));
            CreateButton("SpecPrevBtn", specCard, "<", new Vector2(-215f, 0f), new Vector2(76f, 48f), Palette.PanelMid, () => CameraRig.PlayerCameraController.Instance?.CycleSpectatorTarget(-1));
            CreateButton("SpecNextBtn", specCard, ">", new Vector2(215f, 0f), new Vector2(76f, 48f), Palette.PanelMid, () => CameraRig.PlayerCameraController.Instance?.CycleSpectatorTarget(1));
            _spectatorBar = specCard.gameObject;
            _spectatorBar.SetActive(false);

            // Left floating thumbstick
            var stickBg = new GameObject("LeftThumbstick", typeof(RectTransform), typeof(Image), typeof(VirtualThumbstick)).GetComponent<Image>();
            stickBg.transform.SetParent(_hudSafeArea, false);
            stickBg.sprite = Palette.GetRingSprite();
            stickBg.color = new Color(Palette.TextLight.r, Palette.TextLight.g, Palette.TextLight.b, 0.42f);
            var stickRect = stickBg.rectTransform;
            stickRect.anchorMin = Vector2.zero;
            stickRect.anchorMax = Vector2.zero;
            stickRect.anchoredPosition = new Vector2(185f, 185f);
            stickRect.sizeDelta = new Vector2(210f, 210f);

            var knob = new GameObject("Knob", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            knob.transform.SetParent(stickBg.transform, false);
            knob.sprite = Palette.GetCircleSprite();
            knob.color = new Color(Palette.AccentGold.r, Palette.AccentGold.g, Palette.AccentGold.b, 0.85f);
            knob.rectTransform.sizeDelta = new Vector2(86f, 86f);
            knob.raycastTarget = false;
            _thumbstick = stickBg.GetComponent<VirtualThumbstick>();
            _thumbstick.Configure(knob.rectTransform, 68f);
            joyZoneGo.GetComponent<LeftJoystickZone>().Bind(_thumbstick);
            _hudCustomizer.Register(stickBg.gameObject, "stick", "LEFT JOYSTICK", "shared", _canvas, _hudSafeArea);

            // Left-Side Run / Sprint Toggle Button (Normal Joystick = 50% speed, Run = 100% speed!)
            _runBtn = CreateActionCircle("RunHudBtn", _hudSafeArea, "WALK 50%", Vector2.zero, 94f, Palette.PanelGlass, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                MobileAndDesktopInputDriver.Active?.ToggleRun();
            }, HudIconKind.Run);
            var runRect = _runBtn.GetComponent<RectTransform>();
            runRect.anchorMin = Vector2.zero;
            runRect.anchorMax = Vector2.zero;
            runRect.anchoredPosition = new Vector2(325f, 160f);
            _runBtnBg = _runBtn.GetComponent<Image>();
            _runBtnLabel = _runBtn.GetComponentInChildren<TextMeshProUGUI>();
            _hudCustomizer.Register(_runBtn.gameObject, "run", "RUN / SPRINT", "shared", _canvas, _hudSafeArea);

            // Bottom Right Contextual Buttons — Prop Mode (full stretch parent so every button moves cleanly anywhere!)
            _propButtonsGroup = new GameObject("PropButtons", typeof(RectTransform));
            _propButtonsGroup.transform.SetParent(_hudSafeArea, false);
            var pGrpRect = _propButtonsGroup.GetComponent<RectTransform>();
            pGrpRect.anchorMin = Vector2.zero;
            pGrpRect.anchorMax = Vector2.one;
            pGrpRect.offsetMin = Vector2.zero;
            pGrpRect.offsetMax = Vector2.zero;

            var morphBtn = CreateCornerAction("MorphBtn", _propButtonsGroup.transform, "MORPH", new Vector2(-130f, 155f), 144f, Palette.PropTeal, () => MobileAndDesktopInputDriver.Active?.TriggerPrimaryAction(), HudIconKind.Morph);
            _unmorphBtn = CreateCornerAction("UnmorphBtn", _propButtonsGroup.transform, "HUMAN", new Vector2(-275f, 405f), 92f, Palette.PanelMid, () => MobileAndDesktopInputDriver.Active?.TriggerUnmorph(), HudIconKind.Unmorph);
            var lockBtn = CreateCornerAction("LockBtn", _propButtonsGroup.transform, "LOCK YAW", new Vector2(-295f, 125f), 104f, Palette.PanelMid, () => MobileAndDesktopInputDriver.Active?.TriggerSecondaryAction(), HudIconKind.Lock);
            _lockButtonLabel = lockBtn.GetComponentInChildren<TextMeshProUGUI>();
            var wBtn = CreateCornerAction("WhistleBtn", _propButtonsGroup.transform, "WHISTLE", new Vector2(-115f, 315f), 112f, Palette.AccentGold, () => MobileAndDesktopInputDriver.Active?.TriggerWhistle(), HudIconKind.Whistle);
            _whistleCdText = wBtn.GetComponentInChildren<TextMeshProUGUI>();
            var dBtn = CreateCornerAction("DecoyBtn", _propButtonsGroup.transform, "DECOY", new Vector2(-260f, 275f), 102f, Palette.PropAccent, () => MobileAndDesktopInputDriver.Active?.TriggerDecoy(), HudIconKind.Decoy);
            _decoyCdText = dBtn.GetComponentInChildren<TextMeshProUGUI>();
            _stunBtn = CreateCornerAction("StunBtn", _propButtonsGroup.transform, "STUN", new Vector2(-400f, 220f), 110f, Palette.DangerRed, () => MobileAndDesktopInputDriver.Active?.TriggerStun(), HudIconKind.Stun);
            _stunCdText = _stunBtn.GetComponentInChildren<TextMeshProUGUI>();
            var jumpPBtn = CreateCornerAction("JumpPropBtn", _propButtonsGroup.transform, "JUMP", new Vector2(-420f, 95f), 96f, Palette.PanelGlass, () => MobileAndDesktopInputDriver.Active?.TriggerJump(), HudIconKind.Jump);

            // Left-Side Morphed Prop Rotation Arrows (Joystick moves smoothly without twisting Prop!)
            _propRotateLeftBtn = CreateActionCircle("PropRotateLeftBtn", _propButtonsGroup.transform, "ROT L", Vector2.zero, 86f, Palette.PropTeal, null, HudIconKind.RotateLeft);
            var rotLRect = _propRotateLeftBtn.GetComponent<RectTransform>();
            rotLRect.anchorMin = Vector2.zero;
            rotLRect.anchorMax = Vector2.zero;
            rotLRect.anchoredPosition = new Vector2(115f, 365f);
            var holdL = _propRotateLeftBtn.gameObject.AddComponent<HoldButtonHandler>();
            holdL.OnPressedDown = () => MobileAndDesktopInputDriver.Active?.TapRotateStep(-1);
            holdL.OnHoldStateChanged = down => MobileAndDesktopInputDriver.Active?.SetHoldRotate(down ? -1 : 0);

            _propRotateRightBtn = CreateActionCircle("PropRotateRightBtn", _propButtonsGroup.transform, "ROT R", Vector2.zero, 86f, Palette.PropTeal, null, HudIconKind.RotateRight);
            var rotRRect = _propRotateRightBtn.GetComponent<RectTransform>();
            rotRRect.anchorMin = Vector2.zero;
            rotRRect.anchorMax = Vector2.zero;
            rotRRect.anchoredPosition = new Vector2(225f, 365f);
            var holdR = _propRotateRightBtn.gameObject.AddComponent<HoldButtonHandler>();
            holdR.OnPressedDown = () => MobileAndDesktopInputDriver.Active?.TapRotateStep(1);
            holdR.OnHoldStateChanged = down => MobileAndDesktopInputDriver.Active?.SetHoldRotate(down ? 1 : 0);

            _hudCustomizer.Register(morphBtn.gameObject, "morph", "MORPH", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(_unmorphBtn.gameObject, "unmorph", "UNMORPH (HUMAN)", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(lockBtn.gameObject, "lock", "LOCK YAW", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(wBtn.gameObject, "whistle", "WHISTLE", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(dBtn.gameObject, "decoy", "DECOY", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(_stunBtn.gameObject, "prop_stun", "STUN HUNTER", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(jumpPBtn.gameObject, "jump_prop", "JUMP (PROP)", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(_propRotateLeftBtn.gameObject, "prop_rot_left", "ROTATE PROP LEFT", "prop", _canvas, _hudSafeArea);
            _hudCustomizer.Register(_propRotateRightBtn.gameObject, "prop_rot_right", "ROTATE PROP RIGHT", "prop", _canvas, _hudSafeArea);

            // Bottom Right Contextual Buttons — Hunter Mode
            _hunterButtonsGroup = new GameObject("HunterButtons", typeof(RectTransform));
            _hunterButtonsGroup.transform.SetParent(_hudSafeArea, false);
            var hGrpRect = _hunterButtonsGroup.GetComponent<RectTransform>();
            hGrpRect.anchorMin = Vector2.zero;
            hGrpRect.anchorMax = Vector2.one;
            hGrpRect.offsetMin = Vector2.zero;
            hGrpRect.offsetMax = Vector2.zero;

            var strikeBtn = CreateCornerAction("StrikeBtn", _hunterButtonsGroup.transform, "SHOOT", new Vector2(-130f, 160f), 152f, Palette.HunterAccent, () => MobileAndDesktopInputDriver.Active?.TriggerPrimaryAction(), HudIconKind.Tag);
            var strikeCdGo = new GameObject("StrikeCdFill", typeof(RectTransform), typeof(Image));
            strikeCdGo.transform.SetParent(strikeBtn.transform, false);
            _strikeCooldownFill = strikeCdGo.GetComponent<Image>();
            _strikeCooldownFill.sprite = Palette.GetCircleSprite();
            _strikeCooldownFill.type = Image.Type.Filled;
            _strikeCooldownFill.fillMethod = Image.FillMethod.Radial360;
            _strikeCooldownFill.fillOrigin = (int)Image.Origin360.Top;
            _strikeCooldownFill.fillClockwise = false;
            _strikeCooldownFill.fillAmount = 0f;
            _strikeCooldownFill.color = new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.58f);
            _strikeCooldownFill.raycastTarget = false;
            var scRect = _strikeCooldownFill.rectTransform;
            scRect.anchorMin = Vector2.zero;
            scRect.anchorMax = Vector2.one;
            scRect.offsetMin = new Vector2(8f, 8f);
            scRect.offsetMax = new Vector2(-8f, -8f);

            var rBtn = CreateCornerAction("RadarBtn", _hunterButtonsGroup.transform, "RADAR", new Vector2(-295f, 235f), 116f, Palette.HunterNavy, () => MobileAndDesktopInputDriver.Active?.TriggerSecondaryAction(), HudIconKind.Radar);
            _radarCdText = rBtn.GetComponentInChildren<TextMeshProUGUI>();
            var jumpHBtn = CreateCornerAction("JumpHuntBtn", _hunterButtonsGroup.transform, "JUMP", new Vector2(-360f, 100f), 98f, Palette.PanelGlass, () => MobileAndDesktopInputDriver.Active?.TriggerJump(), HudIconKind.Jump);

            _hudCustomizer.Register(strikeBtn.gameObject, "hunter_attack", "SHOOT BLASTER", "hunter", _canvas, _hudSafeArea);
            _hudCustomizer.Register(rBtn.gameObject, "radar", "RADAR", "hunter", _canvas, _hudSafeArea);
            _hudCustomizer.Register(jumpHBtn.gameObject, "jump_hunt", "JUMP (HUNTER)", "hunter", _canvas, _hudSafeArea);

            // Hunter Blind Curtain during 60s Hide Phase (placed BEFORE PauseHudBtn so Pause stays reachable!)
            _hunterBlindOverlay = new GameObject("HunterBlindCurtain", typeof(RectTransform), typeof(Image));
            _hunterBlindOverlay.transform.SetParent(_hudSafeArea, false);
            var blindImg = _hunterBlindOverlay.GetComponent<Image>();
            blindImg.sprite = Palette.GetRoundedRectSprite();
            blindImg.color = Palette.PanelDark;
            var bRect = _hunterBlindOverlay.GetComponent<RectTransform>();
            bRect.anchorMin = Vector2.zero;
            bRect.anchorMax = Vector2.one;
            bRect.offsetMin = Vector2.zero;
            bRect.offsetMax = Vector2.zero;
            _hunterBlindTimerText = CreateLabel("BlindText", _hunterBlindOverlay.transform, "PROPS ARE HIDING... 60s", 48, Palette.AccentGold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(960f, 140f));
            _hunterBlindOverlay.SetActive(false);

            // Top right pause button LAST so nothing ever blocks it!
            var pauseBtn = CreateActionCircle("PauseHudBtn", _hudSafeArea, "", Vector2.zero, 76f, Palette.PanelGlass, () => GameStateManager.Instance?.TogglePause(), HudIconKind.Pause);
            var pRect = pauseBtn.GetComponent<RectTransform>();
            pRect.anchorMin = new Vector2(1f, 1f);
            pRect.anchorMax = new Vector2(1f, 1f);
            pRect.anchoredPosition = new Vector2(-68f, -68f);
            _hudCustomizer.Register(pauseBtn.gameObject, "pause", "PAUSE BUTTON", "shared", _canvas, _hudSafeArea);

            // In-Game Team Voice Chat: Mic Toggle Button (🎙️ Mic On / 🔇 Mic Muted)
            var micBtn = CreateButton("MicHudBtn", _hudSafeArea, "MIC ON", new Vector2(-168f, -68f), new Vector2(104f, 54f), Palette.SuccessEmerald, () =>
            {
                var vc = Networking.Voice.TeamVoiceChatManager.Instance;
                if (vc != null)
                {
                    vc.ToggleMicMute();
                    var lbl = _hudSafeArea.Find("MicHudBtn")?.GetComponentInChildren<TextMeshProUGUI>();
                    if (lbl != null) lbl.text = vc.IsMicMuted ? "MIC OFF" : "MIC ON";
                    var img = _hudSafeArea.Find("MicHudBtn")?.GetComponent<Image>();
                    if (img != null) img.color = vc.IsMicMuted ? Palette.DangerRed : Palette.SuccessEmerald;
                    ShowToast(vc.IsMicMuted ? "Microphone Muted" : "Microphone Active (Team Only)", vc.IsMicMuted ? Palette.DangerRed : Palette.SuccessEmerald);
                }
            });
            var mRect = micBtn.GetComponent<RectTransform>();
            mRect.anchorMin = new Vector2(1f, 1f);
            mRect.anchorMax = new Vector2(1f, 1f);
            mRect.anchoredPosition = new Vector2(-168f, -68f);

            // In-Game Team Voice Chat: Speaker Toggle Button (🔊 Sound On / 🔈 Sound Deafen)
            var spkBtn = CreateButton("SpeakerHudBtn", _hudSafeArea, "AUDIO ON", new Vector2(-282f, -68f), new Vector2(110f, 54f), Palette.PropTeal, () =>
            {
                var vc = Networking.Voice.TeamVoiceChatManager.Instance;
                if (vc != null)
                {
                    vc.ToggleSpeakerMute();
                    var lbl = _hudSafeArea.Find("SpeakerHudBtn")?.GetComponentInChildren<TextMeshProUGUI>();
                    if (lbl != null) lbl.text = vc.IsSpeakerMuted ? "MUTED" : "AUDIO ON";
                    var img = _hudSafeArea.Find("SpeakerHudBtn")?.GetComponent<Image>();
                    if (img != null) img.color = vc.IsSpeakerMuted ? Palette.PanelMid : Palette.PropTeal;
                    ShowToast(vc.IsSpeakerMuted ? "Team Audio Deafen" : "Team Audio Active", vc.IsSpeakerMuted ? Palette.PanelMid : Palette.PropTeal);
                }
            });
            var sRect = spkBtn.GetComponent<RectTransform>();
            sRect.anchorMin = new Vector2(1f, 1f);
            sRect.anchorMax = new Vector2(1f, 1f);
            sRect.anchoredPosition = new Vector2(-282f, -68f);

            _thumbstick.CacheRestPosition();
            return cg;
        }

        private CanvasGroup BuildHudEditorToolbar(Transform parent)
        {
            var cg = CreateScreenGroup("HudEditorOverlay", parent);
            var bar = CreateCard("HudEditorBar", cg.transform, new Vector2(1040f, 212f), Palette.PanelDark);
            bar.anchorMin = new Vector2(0.5f, 1f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -122f);

            CreateLabel("EditorHdr", bar, "SETTING HUD — SELECT TEAM TAB, DRAG BUTTONS, SLIDE TO RESIZE", 20, Palette.PropAccent, TextAlignmentOptions.Center, new Vector2(0f, 80f), new Vector2(990f, 30f));

            // Strict separated role tabs — never mix Prop and Hunter controls together!
            _btnHudTabProp = CreateButton("TabPropHud", bar, "PROP HUD", new Vector2(-135f, 34f), new Vector2(240f, 44f), Palette.PropTeal, () => SwitchHudEditorTab("prop"));
            _btnHudTabHunter = CreateButton("TabHunterHud", bar, "HUNTER HUD", new Vector2(135f, 34f), new Vector2(240f, 44f), Palette.PanelMid, () => SwitchHudEditorTab("hunter"));

            _hudSelectedLabel = CreateLabel("SelectedHudLbl", bar, "SELECTED: MORPH (1.00x)", 20, Palette.AccentGold, TextAlignmentOptions.Left, new Vector2(-245f, -14f), new Vector2(440f, 32f));
            _hudScaleSlider = CreateSlider("HudSizeSlider", bar, new Vector2(195f, -14f), new Vector2(390f, 30f), 0.65f, 1.65f, 1.0f, false, v =>
            {
                if (!_updatingScaleSlider) _hudCustomizer.SetSelectedScale(v);
            });

            CreateButton("HudResetBtn", bar, "RESET THIS TAB", new Vector2(-290f, -68f), new Vector2(250f, 48f), Palette.PanelMid, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                _hudCustomizer.ResetTabDefaults(_hudCustomizer.ActiveRoleTab);
            });
            CreateButton("HudCancelBtn", bar, "CANCEL", new Vector2(10f, -68f), new Vector2(210f, 48f), Palette.DangerRed, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                CloseHudCustomizer(saveChanges: false);
            });
            CreateButton("HudSaveBtn", bar, "SAVE & DONE", new Vector2(285f, -68f), new Vector2(250f, 48f), Palette.PropTeal, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                CloseHudCustomizer(saveChanges: true);
            });

            _hudCustomizer.OnSelectionChanged += h =>
            {
                if (_hudSelectedLabel != null)
                {
                    _hudSelectedLabel.text = h != null
                        ? $"SELECTED: {h.DisplayTitle} ({h.CurrentScale:0.00}x)"
                        : "TAP ANY BUTTON OR JOYSTICK";
                }
                if (_hudScaleSlider != null && h != null)
                {
                    _updatingScaleSlider = true;
                    _hudScaleSlider.value = h.CurrentScale;
                    _updatingScaleSlider = false;
                }
            };
            return cg;
        }

        private CanvasGroup BuildPauseOverlay(Transform parent)
        {
            var cg = CreateScreenGroup("PauseOverlay", parent);
            var bg = cg.gameObject.AddComponent<Image>();
            bg.sprite = Palette.GetRoundedRectSprite();
            bg.color = new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.78f);

            var card = CreateCard("PauseCard", cg.transform, new Vector2(1080f, 640f), Palette.PanelDark);
            _pauseTitleText = CreateLabel("PauseTitle", card, "SETTINGS, SENSITIVITY & GRAPHICS", 32, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(0f, 265f), new Vector2(960f, 48f));

            var divider = CreateCard("PauseDivider", card, new Vector2(3f, 440f), new Color(1f, 1f, 1f, 0.12f));
            divider.anchoredPosition = new Vector2(0f, -20f);
            divider.GetComponent<Image>().raycastTarget = false;

            float mVol = AudioSynthManager.Instance != null ? AudioSynthManager.Instance.MusicVolume : 0.55f;
            float sVol = AudioSynthManager.Instance != null ? AudioSynthManager.Instance.SfxVolume : 0.85f;
            float cSens = MobileAndDesktopInputDriver.CameraSensitivity;
            float pSens = MobileAndDesktopInputDriver.PropMorphSensitivity;

            // Left Column: Audio & Touch Sensitivity Controls
            CreateLabel("MusicLbl", card, "BACKGROUND MUSIC VOLUME", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(-285f, 188f), new Vector2(360f, 28f));
            _musicValText = CreateLabel("MusicVal", card, $"{Mathf.RoundToInt(mVol * 100f)}%", 19, Palette.PropAccent, TextAlignmentOptions.Right, new Vector2(-105f, 188f), new Vector2(110f, 28f));
            _musicSlider = CreateSlider("MusicSlider", card, new Vector2(-255f, 150f), new Vector2(440f, 30f), 0f, 1f, mVol, false, v =>
            {
                AudioSynthManager.Instance?.SetVolumes(v, _sfxSlider != null ? _sfxSlider.value : sVol);
                if (_musicValText != null) _musicValText.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateLabel("SfxLbl", card, "SFX & FOOTSTEPS VOLUME", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(-285f, 88f), new Vector2(360f, 28f));
            _sfxValText = CreateLabel("SfxVal", card, $"{Mathf.RoundToInt(sVol * 100f)}%", 19, Palette.PropAccent, TextAlignmentOptions.Right, new Vector2(-105f, 88f), new Vector2(110f, 28f));
            _sfxSlider = CreateSlider("SfxSlider", card, new Vector2(-255f, 50f), new Vector2(440f, 30f), 0f, 1f, sVol, false, v =>
            {
                AudioSynthManager.Instance?.SetVolumes(_musicSlider != null ? _musicSlider.value : mVol, v);
                if (_sfxValText != null) _sfxValText.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });

            CreateLabel("CamSensLbl", card, "1. CAMERA LOOK SENSITIVITY", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(-285f, -14f), new Vector2(360f, 28f));
            _camSensValText = CreateLabel("CamSensVal", card, $"{cSens:0.00}x", 19, Palette.AccentGold, TextAlignmentOptions.Right, new Vector2(-105f, -14f), new Vector2(110f, 28f));
            _camSensSlider = CreateSlider("CamSensSlider", card, new Vector2(-255f, -52f), new Vector2(440f, 30f), 0.25f, 2.00f, cSens, false, v =>
            {
                float p = _propSensSlider != null ? _propSensSlider.value : MobileAndDesktopInputDriver.PropMorphSensitivity;
                MobileAndDesktopInputDriver.SetSensitivities(v, p);
                if (_camSensValText != null) _camSensValText.text = $"{MobileAndDesktopInputDriver.CameraSensitivity:0.00}x";
            });

            CreateLabel("PropSensLbl", card, "2. PROP ROTATION SENSITIVITY", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(-285f, -116f), new Vector2(380f, 28f));
            _propSensValText = CreateLabel("PropSensVal", card, $"{pSens:0.00}x", 19, Palette.AccentGold, TextAlignmentOptions.Right, new Vector2(-105f, -116f), new Vector2(110f, 28f));
            _propSensSlider = CreateSlider("PropSensSlider", card, new Vector2(-255f, -154f), new Vector2(440f, 30f), 0.25f, 1.50f, pSens, false, v =>
            {
                float c = _camSensSlider != null ? _camSensSlider.value : MobileAndDesktopInputDriver.CameraSensitivity;
                MobileAndDesktopInputDriver.SetSensitivities(c, v);
                if (_propSensValText != null) _propSensValText.text = $"{MobileAndDesktopInputDriver.PropMorphSensitivity:0.00}x";
            });

            CreateLabel("SensTipLbl", card, "1: Screen swipe look speed  •  2: ROT L / ROT R button turn speed", 16, Palette.TextMuted, TextAlignmentOptions.Left, new Vector2(-255f, -214f), new Vector2(450f, 44f));

            // Right Column: Graphics Quality Preset, HUD Customizer & Session Buttons
            CreateLabel("QualLbl", card, "GRAPHICS QUALITY (FPS / MSAA / BLOOM)", 19, Palette.TextLight, TextAlignmentOptions.Left, new Vector2(255f, 188f), new Vector2(440f, 28f));
            _btnQualLow = CreateButton("QualLowBtn", card, "LOW", new Vector2(115f, 136f), new Vector2(138f, 54f), Palette.PanelMid, () => SelectGraphicsTier(GraphicsPresetTier.Low));
            _btnQualMed = CreateButton("QualMedBtn", card, "MEDIUM", new Vector2(260f, 136f), new Vector2(138f, 54f), Palette.PropTeal, () => SelectGraphicsTier(GraphicsPresetTier.Medium));
            _btnQualHigh = CreateButton("QualHighBtn", card, "HIGH", new Vector2(405f, 136f), new Vector2(138f, 54f), Palette.PanelMid, () => SelectGraphicsTier(GraphicsPresetTier.High));

            CreateButton("SettingHudBtn", card, "SETTING HUD (PROP / HUNTER)", new Vector2(260f, 34f), new Vector2(430f, 64f), Palette.HunterNavy, OpenHudCustomizer);

            _pauseResumeBtn = CreateButton("ResumeBtn", card, "RESUME GAME", new Vector2(260f, -64f), new Vector2(430f, 64f), Palette.PropTeal, () =>
            {
                AudioSynthManager.Instance?.PlayClick();
                if (GameStateManager.Instance != null && GameStateManager.Instance.ShellState == AppShellState.Paused)
                    GameStateManager.Instance.TogglePause();
                else
                    SetGroupVisible(_pausePanel, false, 0.20f);
            });
            _pauseResumeLbl = _pauseResumeBtn.GetComponentInChildren<TextMeshProUGUI>();

            _pauseQuitBtn = CreateButton("QuitMenuBtn", card, "LEAVE TO MENU", new Vector2(260f, -158f), new Vector2(430f, 60f), Palette.DangerRed, () =>
            {
                SetGroupVisible(_pausePanel, false, 0.15f);
                _ = MultiplayerRoomManager.Instance?.LeaveRoomAsync();
                GameStateManager.Instance?.ReturnToTitle();
            });

            RefreshQualityButtons();
            return cg;
        }

        private static Button CreateCornerAction(string name, Transform parent, string label, Vector2 bottomRightOffset, float diameter, Color bgCol, Action onClick, HudIconKind? icon = null)
        {
            var btn = CreateActionCircle(name, parent, label, Vector2.zero, diameter, bgCol, onClick, icon);
            var r = btn.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.anchoredPosition = bottomRightOffset;
            return btn;
        }

        private CanvasGroup BuildRoundEndOverlay(Transform parent)
        {
            var cg = CreateScreenGroup("RoundEndOverlay", parent);
            var bg = cg.gameObject.AddComponent<Image>();
            bg.sprite = Palette.GetRoundedRectSprite();
            bg.color = new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.78f);

            var card = CreateCard("EndCard", cg.transform, new Vector2(780f, 540f), Palette.PanelDark);
            _endWinnerText = CreateLabel("WinnerTxt", card, "PROPS WIN!", 54, Palette.AccentGold, TextAlignmentOptions.Center, new Vector2(0f, 180f), new Vector2(680f, 70f));
            _endReasonText = CreateLabel("ReasonTxt", card, "Time Up! Props Survived.", 26, Palette.PropAccent, TextAlignmentOptions.Center, new Vector2(0f, 108f), new Vector2(680f, 54f));
            _endSummaryText = CreateLabel("SummaryTxt", card, "Score: 0  |  Best Score: 0", 26, Palette.TextLight, TextAlignmentOptions.Center, new Vector2(0f, 12f), new Vector2(660f, 96f));

            CreateButton("PlayAgainBtn", card, "PLAY NEXT ROUND", new Vector2(-175f, -165f), new Vector2(310f, 68f), Palette.PropTeal, () =>
            {
                var gsm = GameStateManager.Instance;
                if (gsm == null) return;
                if (gsm.IsOnlineSession && NetworkMatchSync.Instance != null && NetworkMatchSync.Instance.IsSpawned)
                {
                    if (NetworkMatchSync.Instance.IsServer)
                        NetworkMatchSync.Instance.RequestHostStartRound(UnityEngine.Random.Range(1, 999999), gsm.TargetTotalPlayers);
                    else
                        ShowToast("Only Host can start the next online round!", Palette.AccentGold);
                }
                else
                {
                    gsm.StartMatch(false, gsm.TargetTotalPlayers);
                }
            });
            CreateButton("BackToTitleBtn", card, "MAIN MENU", new Vector2(175f, -165f), new Vector2(310f, 68f), Palette.HunterNavy, () =>
            {
                _ = MultiplayerRoomManager.Instance?.LeaveRoomAsync();
                GameStateManager.Instance?.ReturnToTitle();
            });
            return cg;
        }

        private void BuildTransientOverlays(Transform parent)
        {
            // Fullscreen flash
            var fGo = new GameObject("ScreenFlash", typeof(RectTransform), typeof(Image));
            fGo.transform.SetParent(parent, false);
            _flashOverlay = fGo.GetComponent<Image>();
            _flashOverlay.sprite = Palette.GetRoundedRectSprite();
            _flashOverlay.color = new Color(1f, 1f, 1f, 0f);
            _flashOverlay.raycastTarget = false;
            var fRect = _flashOverlay.rectTransform;
            fRect.anchorMin = Vector2.zero;
            fRect.anchorMax = Vector2.one;
            fRect.offsetMin = Vector2.zero;
            fRect.offsetMax = Vector2.zero;

            // Toast banner
            _toastGroup = CreateScreenGroup("ToastGroup", parent);
            _toastGroup.blocksRaycasts = false;
            var tCard = CreateCard("ToastCard", _toastGroup.transform, new Vector2(660f, 56f), Palette.PanelDark);
            tCard.anchorMin = new Vector2(0.5f, 1f);
            tCard.anchorMax = new Vector2(0.5f, 1f);
            tCard.anchoredPosition = new Vector2(0f, -142f);
            _toastText = CreateLabel("ToastTxt", tCard, "", 22, Palette.PropAccent, TextAlignmentOptions.Center, Vector2.zero, new Vector2(620f, 44f));

            // Milestone banner
            _milestoneGroup = CreateScreenGroup("MilestoneGroup", parent);
            _milestoneGroup.blocksRaycasts = false;
            _milestoneRect = CreateCard("MilestoneCard", _milestoneGroup.transform, new Vector2(640f, 118f), Palette.PanelDark);
            _milestoneRect.anchoredPosition = new Vector2(0f, 120f);
            _milestoneText = CreateLabel("MilestoneTxt", _milestoneRect, "HUNT BEGINS!", 48, Palette.AccentGold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(600f, 88f));
        }

        private void OpenSettingsMenu()
        {
            AudioSynthManager.Instance?.PlayClick();
            RefreshPauseOverlayButtons();
            if (_pausePanel != null) SetGroupVisible(_pausePanel, true, 0.20f);
        }

        private void RefreshPauseOverlayButtons()
        {
            var gsm = GameStateManager.Instance;
            bool inMatch = gsm != null
                && (gsm.ShellState == AppShellState.Paused || gsm.ShellState == AppShellState.Playing);
            bool onlineLive = inMatch && gsm.IsOnlineSession;
            if (_pauseTitleText != null)
                _pauseTitleText.text = onlineLive ? "SETTINGS (ONLINE MATCH CONTINUES LIVE)" : "SETTINGS, SENSITIVITY & GRAPHICS";
            if (_pauseResumeLbl != null)
                _pauseResumeLbl.text = inMatch ? "RESUME GAME" : "CLOSE";
            if (_pauseQuitBtn != null)
                _pauseQuitBtn.gameObject.SetActive(inMatch);
            if (_camSensSlider != null)
                _camSensSlider.SetValueWithoutNotify(MobileAndDesktopInputDriver.CameraSensitivity);
            if (_propSensSlider != null)
                _propSensSlider.SetValueWithoutNotify(MobileAndDesktopInputDriver.PropMorphSensitivity);
            if (_camSensValText != null)
                _camSensValText.text = $"{MobileAndDesktopInputDriver.CameraSensitivity:0.00}x";
            if (_propSensValText != null)
                _propSensValText.text = $"{MobileAndDesktopInputDriver.PropMorphSensitivity:0.00}x";
            RefreshQualityButtons();
        }

        public void OpenHudCustomizer()
        {
            AudioSynthManager.Instance?.PlayClick();
            _isEditingHud = true;
            if (_thumbstick != null)
            {
                _thumbstick.ResetStick();
                _thumbstick.IsEditingHud = true;
            }
            if (_lookPad != null) _lookPad.IsEditingHud = true;

            SetGroupVisible(_pausePanel, false, 0.10f);
            SetGroupVisible(_titlePanel, false, 0.08f);
            SetGroupVisible(_roomPanel, false, 0.08f);
            SetGroupVisible(_hudPanel, true, 0.12f);
            SetGroupVisible(_hudEditorPanel, true, 0.12f);

            if (_hudEditorScrim != null) _hudEditorScrim.SetActive(true);
            for (int i = 0; i < _hudEditHiddenChrome.Count; i++)
            {
                if (_hudEditHiddenChrome[i] != null) _hudEditHiddenChrome[i].SetActive(false);
            }
            if (_roomCodeBadgeGo != null) _roomCodeBadgeGo.SetActive(false);
            if (_hunterBlindOverlay != null) _hunterBlindOverlay.SetActive(false);
            if (_spectatorBar != null) _spectatorBar.SetActive(false);

            var local = PlayerParticipant.LocalPlayer;
            string initialTab = local != null && local.Role == TeamRole.Hunter ? "hunter" : "prop";
            _hudCustomizer.BeginEditSession(initialTab);
            ApplyHudEditorTabVisuals(initialTab);
        }

        public void SwitchHudEditorTab(string tab)
        {
            AudioSynthManager.Instance?.PlayClick();
            string cleanTab = tab == "hunter" ? "hunter" : "prop";
            _hudCustomizer.SetActiveRoleTab(cleanTab);
            ApplyHudEditorTabVisuals(cleanTab);
        }

        private void ApplyHudEditorTabVisuals(string tab)
        {
            bool isProp = tab != "hunter";
            if (_propButtonsGroup != null) _propButtonsGroup.SetActive(isProp);
            if (_hunterButtonsGroup != null) _hunterButtonsGroup.SetActive(!isProp);
            if (_unmorphBtn != null) _unmorphBtn.gameObject.SetActive(isProp);
            if (_propRotateLeftBtn != null) _propRotateLeftBtn.gameObject.SetActive(isProp);
            if (_propRotateRightBtn != null) _propRotateRightBtn.gameObject.SetActive(isProp);

            if (_btnHudTabProp != null)
            {
                var img = _btnHudTabProp.GetComponent<Image>();
                if (img != null) img.color = isProp ? Palette.PropTeal : Palette.PanelMid;
            }
            if (_btnHudTabHunter != null)
            {
                var img = _btnHudTabHunter.GetComponent<Image>();
                if (img != null) img.color = !isProp ? Palette.HunterAccent : Palette.PanelMid;
            }
        }

        public void CloseHudCustomizer(bool saveChanges)
        {
            _isEditingHud = false;
            if (saveChanges)
            {
                _hudCustomizer.SaveAndCloseEditSession();
                _thumbstick?.CacheRestPosition();
                ShowToast("HUD Layout Saved!", Palette.SuccessEmerald);
            }
            else
            {
                _hudCustomizer.CancelEditSession();
            }

            if (_thumbstick != null) _thumbstick.IsEditingHud = false;
            if (_lookPad != null) _lookPad.IsEditingHud = false;

            if (_hudEditorScrim != null) _hudEditorScrim.SetActive(false);
            for (int i = 0; i < _hudEditHiddenChrome.Count; i++)
            {
                if (_hudEditHiddenChrome[i] != null) _hudEditHiddenChrome[i].SetActive(true);
            }

            SetGroupVisible(_hudEditorPanel, false, 0.12f);

            var gsm = GameStateManager.Instance;
            bool inGame = gsm != null && (gsm.ShellState == AppShellState.Playing || gsm.ShellState == AppShellState.Paused);
            SetGroupVisible(_hudPanel, inGame, 0.15f);
            if (!inGame && gsm != null)
            {
                SetGroupVisible(_titlePanel, gsm.ShellState == AppShellState.Title, 0.15f);
                SetGroupVisible(_roomPanel, gsm.ShellState == AppShellState.RoomLobby, 0.15f);
            }
            RefreshPauseOverlayButtons();
            SetGroupVisible(_pausePanel, true, 0.18f);
        }

        private void HandleShellStateChanged(AppShellState next)
        {
            if (_isEditingHud && next != AppShellState.Title && next != AppShellState.Paused)
                CloseHudCustomizer(saveChanges: false);

            SetGroupVisible(_titlePanel, next == AppShellState.Title, 0.25f);
            SetGroupVisible(_roomPanel, next == AppShellState.RoomLobby, 0.25f);
            SetGroupVisible(_hudPanel, next == AppShellState.Playing || next == AppShellState.Paused, 0.25f);
            RefreshPauseOverlayButtons();
            SetGroupVisible(_pausePanel, next == AppShellState.Paused, 0.22f);
            SetGroupVisible(_endPanel, next == AppShellState.Ended, 0.25f);

            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            if (next == AppShellState.Title || next == AppShellState.RoomLobby)
            {
                _final45ToastShown = false;
                _lastPropPingTimes.Clear();
                ClearAllMotionPings();
                ClearAllNameplates();
                _hitmarkerTimer = 0f;
                if (_hitmarkerText != null) _hitmarkerText.alpha = 0f;
                HideAllKillFeedSlots();
            }

            if (next == AppShellState.RoomLobby)
            {
                if (_nameInput != null && !string.IsNullOrEmpty(gsm.Progress.playerName))
                    _nameInput.SetTextWithoutNotify(gsm.Progress.playerName);
                MultiplayerRoomManager.Instance?.PrewarmMultiplayerAsync();
                RefreshSameWifiButton();
                RefreshOnlineLobbyState();
            }

            if (next == AppShellState.Title)
            {
                if (_titleNameInput != null && !string.IsNullOrEmpty(gsm.Progress.playerName))
                    _titleNameInput.SetTextWithoutNotify(gsm.Progress.playerName);
                if (_titleStatsText != null)
                    _titleStatsText.text = $"Best Score: {gsm.Progress.highScore}  |  Matches: {gsm.Progress.matchesPlayed}  |  Survived: {gsm.Progress.roundsSurvived}";
            }

            if (next == AppShellState.Ended)
            {
                var local = PlayerParticipant.LocalPlayer;
                _endWinnerText.text = gsm.WinningTeam == TeamRole.Prop ? "PROPS WIN!" : "HUNTERS WIN!";
                _endWinnerText.color = gsm.WinningTeam == TeamRole.Prop ? Palette.PropAccent : Palette.HunterAccent;
                _endReasonText.text = gsm.WinReason;
                int myScore = local != null ? local.Score : 0;
                int myKills = local != null ? local.Eliminations : 0;
                _endSummaryText.text = $"Your Score: {myScore}   |   Tags: {myKills}\nSurviving Props: {gsm.AlivePropsCount}   |   High Score: {gsm.Progress.highScore}";
            }
        }

        private void UpdateGameplayHud(GameStateManager gsm)
        {
            int sec = Mathf.CeilToInt(gsm.PhaseRemainingSeconds);
            string mmss = $"{sec / 60:00}:{sec % 60:00}";
            string phaseLbl = gsm.Phase == MatchPhase.HidePhase ? "HIDE PHASE" : (gsm.Phase == MatchPhase.HuntPhase ? "HUNT PHASE" : "ROUND OVER");
            _phaseTimerText.text = $"{phaseLbl}  {mmss}";
            _phaseTimerText.color = gsm.Phase == MatchPhase.HidePhase ? Palette.PropAccent : Palette.AccentGold;
            _teamAliveText.text = $"PROPS ALIVE: {gsm.AlivePropsCount}   |   HUNTERS: {gsm.AliveHuntersCount}";

            if (_roomCodeBadgeGo != null)
            {
                string jCode = gsm.IsOnlineSession ? MultiplayerRoomManager.Instance?.CurrentJoinCode : "";
                bool showCode = gsm.IsOnlineSession && !string.IsNullOrEmpty(jCode);
                _roomCodeBadgeGo.SetActive(showCode);
                if (showCode && _roomCodeBadgeText != null) _roomCodeBadgeText.text = $"CODE: {jCode}";
            }

            var local = PlayerParticipant.LocalPlayer;
            if (local == null)
            {
                _morphHighlighter.UpdateTarget(null, 0f);
                return;
            }

            if (_roomLocationText != null)
                _roomLocationText.text = CoachingRoomLocator.GetRoomLabel(local.transform.position);

            if (_runBtn != null)
            {
                _runBtn.gameObject.SetActive(local.IsAlive);
                if (local.IsAlive)
                {
                    bool sprint = MobileAndDesktopInputDriver.Active != null && MobileAndDesktopInputDriver.Active.IsSprintActive;
                    if (_runBtnBg != null) _runBtnBg.color = sprint ? Palette.AccentGold : Palette.PanelGlass;
                    if (_runBtnLabel != null)
                    {
                        _runBtnLabel.text = sprint ? "RUN 100%" : "WALK 50%";
                        _runBtnLabel.color = sprint ? Palette.PanelDark : Palette.TextLight;
                    }
                }
            }

            UpdateTeammateNameplates(local);

            float targetRatio = local.MaxHealth > 0 ? Mathf.Clamp01((float)local.Health / local.MaxHealth) : 0f;
            _displayedHpRatio = Mathf.MoveTowards(_displayedHpRatio, targetRatio, Time.deltaTime * 3.5f);
            _hpFillBar.fillAmount = _displayedHpRatio;
            _hpFillBar.color = _displayedHpRatio > 0.35f ? Palette.SuccessEmerald : Palette.DangerRed;
            _hpText.text = $"HP {local.Health}/{local.MaxHealth}";

            _displayedScore = Mathf.MoveTowards(_displayedScore, local.Score, Time.deltaTime * 220f);
            _scoreText.text = $"SCORE: {Mathf.RoundToInt(_displayedScore)}";

            bool isHunter = local.Role == TeamRole.Hunter;
            _propButtonsGroup.SetActive(!isHunter && local.IsAlive);
            _hunterButtonsGroup.SetActive(isHunter && local.IsAlive);
            _hunterBlindOverlay.SetActive(isHunter && gsm.Phase == MatchPhase.HidePhase && local.IsAlive);
            if (_hunterBlindOverlay.activeSelf)
                _hunterBlindTimerText.text = $"HUNTERS BLINDED\nPROPS HIDING... {sec}s";

            if (_spectatorBar != null)
            {
                _spectatorBar.SetActive(!local.IsAlive);
                if (!local.IsAlive && _spectatorTargetText != null)
                {
                    var spec = CameraRig.PlayerCameraController.Instance?.CurrentSpectatorTarget;
                    _spectatorTargetText.text = spec != null ? $"WATCHING: {spec.DisplayName}" : "SPECTATING CAMPUS";
                }
            }

            if (!isHunter)
            {
                if (_reticleImg != null) _reticleImg.color = new Color(Palette.TextLight.r, Palette.TextLight.g, Palette.TextLight.b, 0.78f);
                ClearAllMotionPings();

                bool morphed = local.MorphCtrl.IsMorphed && local.MorphCtrl.CurrentProp != null;
                string propName = morphed ? local.MorphCtrl.CurrentProp.displayName : "Student (Unmorphed)";
                _rolePropText.text = $"PROP: {propName}";
                if (_unmorphBtn != null) _unmorphBtn.gameObject.SetActive(local.IsAlive && morphed);
                if (_propRotateLeftBtn != null) _propRotateLeftBtn.gameObject.SetActive(local.IsAlive && morphed);
                if (_propRotateRightBtn != null) _propRotateRightBtn.gameObject.SetActive(local.IsAlive && morphed);

                if (Time.unscaledTime >= _nextAimScanTime)
                {
                    _nextAimScanTime = Time.unscaledTime + 0.10f;
                    var aimed = local.MorphCtrl.FindAimedProp(local.GetAimRay());
                    if (aimed != null && gsm.PropRegistry != null)
                    {
                        var def = gsm.PropRegistry.GetById(aimed.PropId);
                        _cachedAimPrompt = def != null ? $"TAP MORPH -> {def.displayName.ToUpperInvariant()}" : "TAP MORPH";
                        var ren = aimed.GetComponentInChildren<Renderer>();
                        if (ren != null)
                        {
                            Vector3 ringPos = new Vector3(ren.bounds.center.x, ren.bounds.min.y + 0.02f, ren.bounds.center.z);
                            float r = Mathf.Max(ren.bounds.extents.x, ren.bounds.extents.z) + 0.14f;
                            _morphHighlighter.UpdateTarget(ringPos, r);
                        }
                        else
                        {
                            _morphHighlighter.UpdateTarget(aimed.transform.position, def != null ? Mathf.Max(def.boundsSize.x, def.boundsSize.z) * 0.65f : 0.45f);
                        }
                    }
                    else
                    {
                        _cachedAimPrompt = "";
                        _morphHighlighter.UpdateTarget(null, 0f);
                    }
                }
                if (!local.IsAlive) _morphHighlighter.UpdateTarget(null, 0f);
                _aimPromptText.text = local.IsAlive ? _cachedAimPrompt : "ELIMINATED — USE < > TO SPECTATE";

                _lockButtonLabel.text = local.MorphCtrl.IsRotationLocked ? "LOCKED" : "LOCK YAW";
                float wCd = local.PropAbility.WhistleRemainingCooldown;
                _whistleCdText.text = wCd > 0.1f ? $"WHISTLE\n{wCd:0}s" : "WHISTLE";
                float dCd = local.PropAbility.DecoyRemainingCooldown;
                _decoyCdText.text = dCd > 0.1f ? $"DECOY\n{dCd:0}s" : "DECOY";
                if (_stunCdText != null)
                {
                    float sCd = local.PropAbility.StunRemainingCooldown;
                    _stunCdText.text = sCd > 0.1f ? $"STUN\n{sCd:0}s" : "STUN";
                }
            }
            else
            {
                _morphHighlighter.UpdateTarget(null, 0f);
                _rolePropText.text = local.IsAlive ? "TEAM: HUNTER (BLASTER)" : "SPECTATOR (KNOCKED OUT)";

                bool assistLocked = false;
                if (local.IsAlive && gsm.Phase == MatchPhase.HuntPhase && !local.IsStunned && local.HunterAction != null)
                {
                    assistLocked = local.HunterAction.FindAimAssistedProp(local.GetAimRay(), out _) != null;
                }
                if (_hookedHunter != local.HunterAction)
                {
                    if (_hookedHunter != null) _hookedHunter.OnStrikeHitFeedback -= HandleStrikeHitFeedback;
                    _hookedHunter = local.HunterAction;
                    if (_hookedHunter != null) _hookedHunter.OnStrikeHitFeedback += HandleStrikeHitFeedback;
                }
                if (_strikeCooldownFill != null && local.HunterAction != null)
                {
                    float cd = local.HunterAction.StrikeCooldownDuration;
                    _strikeCooldownFill.fillAmount = cd > 0.01f ? Mathf.Clamp01(local.HunterAction.StrikeRemainingCooldown / cd) : 0f;
                }
                if (_hitmarkerText != null)
                {
                    if (_hitmarkerTimer > 0f)
                    {
                        _hitmarkerTimer = Mathf.Max(0f, _hitmarkerTimer - Time.deltaTime);
                        _hitmarkerText.alpha = Mathf.Clamp01(_hitmarkerTimer / 0.18f);
                    }
                    else if (_hitmarkerText.alpha > 0f)
                    {
                        _hitmarkerText.alpha = 0f;
                    }
                }
                if (_reticleImg != null)
                    _reticleImg.color = assistLocked ? Palette.HunterAccent : new Color(Palette.TextLight.r, Palette.TextLight.g, Palette.TextLight.b, 0.78f);

                if (local.IsStunned)
                    _aimPromptText.text = $"STUNNED BY PROP! ({local.StunRemainingSeconds:0.0}s)";
                else if (local.IsAlive && gsm.Phase == MatchPhase.HuntPhase)
                    _aimPromptText.text = assistLocked ? "AIM ASSIST LOCKED — TAP SHOOT!" : "SHOOT SUSPECT OBJECTS (-10 HP ON MISS)";
                else
                    _aimPromptText.text = local.IsAlive ? "" : "ELIMINATED — USE < > TO SPECTATE";

                float rCd = local.HunterAction.RadarRemainingCooldown;
                _radarCdText.text = rCd > 0.1f ? $"RADAR\n{rCd:0}s" : "RADAR";

                if (gsm.Phase == MatchPhase.HidePhase)
                    _final45ToastShown = false;

                UpdateLateGameMovingPropPings(gsm, local);
            }
        }

        private void ClearAllNameplates()
        {
            for (int i = 0; i < _nameplatePool.Count; i++)
            {
                if (_nameplatePool[i].root != null && _nameplatePool[i].root.gameObject.activeSelf)
                    _nameplatePool[i].root.gameObject.SetActive(false);
            }
        }

        private void UpdateTeammateNameplates(PlayerParticipant local)
        {
            var cam = UnityEngine.Camera.main;
            if (local == null || cam == null || _nameplateContainer == null || _nameplatePool.Count == 0)
            {
                ClearAllNameplates();
                return;
            }

            int used = 0;
            for (int i = 0; i < PlayerParticipant.AllActive.Count && used < _nameplatePool.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                // Multiplayer Team Visibility Rule: Hunter sees fellow Hunters ONLY; Prop sees fellow Props ONLY!
                if (p == null || p == local || !p.IsAlive || p.Role != local.Role) continue;

                float headY = p.Role == TeamRole.Hunter ? 2.15f : (p.MorphCtrl != null && p.MorphCtrl.IsMorphed ? 1.32f : 1.98f);
                Vector3 wp = p.transform.position + new Vector3(0f, headY, 0f);
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0.2f || sp.z > 38f) continue;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_nameplateContainer, sp, null, out Vector2 lp))
                    continue;

                var slot = _nameplatePool[used++];
                slot.root.gameObject.SetActive(true);
                slot.root.anchoredPosition = lp;
                if (slot.text != null)
                {
                    slot.text.text = string.IsNullOrEmpty(p.DisplayName) ? "Teammate" : p.DisplayName;
                    slot.text.color = p.Role == TeamRole.Hunter ? Palette.AccentGold : Palette.PropAccent;
                }
                if (slot.bg != null)
                    slot.bg.color = p.Role == TeamRole.Hunter
                        ? new Color(Palette.HunterNavy.r, Palette.HunterNavy.g, Palette.HunterNavy.b, 0.84f)
                        : new Color(Palette.PanelDark.r, Palette.PanelDark.g, Palette.PanelDark.b, 0.80f);
            }

            for (int i = used; i < _nameplatePool.Count; i++)
            {
                if (_nameplatePool[i].root != null && _nameplatePool[i].root.gameObject.activeSelf)
                    _nameplatePool[i].root.gameObject.SetActive(false);
            }
        }

        private static Sprite LoadBrandLogoSprite()
        {
            var sp = Resources.Load<Sprite>("UI/ParishramHuntIcon");
            if (sp != null) return sp;
            var tex = Resources.Load<Texture2D>("UI/ParishramHuntIcon");
            if (tex != null)
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return null;
        }

        private void ClearAllMotionPings()
        {
            for (int i = 0; i < _motionPingPool.Count; i++)
            {
                var p = _motionPingPool[i];
                if (p.active)
                {
                    p.active = false;
                    _motionPingPool[i] = p;
                }
                if (p.ringImg != null && p.ringImg.gameObject.activeSelf)
                    p.ringImg.gameObject.SetActive(false);
            }
        }

        private void UpdateLateGameMovingPropPings(GameStateManager gsm, PlayerParticipant local)
        {
            bool enablePings = local.IsAlive && gsm.Phase == MatchPhase.HuntPhase && gsm.PhaseRemainingSeconds <= 45f;
            if (!enablePings)
            {
                ClearAllMotionPings();
                return;
            }

            if (!_final45ToastShown)
            {
                _final45ToastShown = true;
                gsm.ShowToast("FINAL 45s: Moving Props emit faint heat pings!", Palette.HunterAccent);
            }

            // Spawn a subtle 1.05s pulse every ~2.1s whenever a Prop is actively moving
            float now = Time.time;
            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var prop = PlayerParticipant.AllActive[i];
                if (prop == null || !prop.IsAlive || prop.Role != TeamRole.Prop) continue;
                if (now - prop.LastActiveMoveTime > 0.55f) continue;

                int key = prop.GetInstanceID();
                if (_lastPropPingTimes.TryGetValue(key, out float lastPing) && now - lastPing < 2.1f)
                    continue;

                _lastPropPingTimes[key] = now;
                for (int k = 0; k < _motionPingPool.Count; k++)
                {
                    if (!_motionPingPool[k].active)
                    {
                        var slot = _motionPingPool[k];
                        slot.active = true;
                        slot.startTime = now;
                        slot.worldPos = prop.transform.position + Vector3.up * 0.55f;
                        _motionPingPool[k] = slot;
                        break;
                    }
                }
            }

            var cam = UnityEngine.Camera.main;
            if (cam == null || _motionPingContainer == null) return;

            for (int i = 0; i < _motionPingPool.Count; i++)
            {
                var slot = _motionPingPool[i];
                if (!slot.active || slot.ringImg == null) continue;

                float age = now - slot.startTime;
                const float pingDur = 1.05f;
                if (age >= pingDur)
                {
                    slot.active = false;
                    slot.ringImg.gameObject.SetActive(false);
                    _motionPingPool[i] = slot;
                    continue;
                }

                Vector3 sp = cam.WorldToScreenPoint(slot.worldPos);
                if (sp.z <= 0.1f)
                {
                    slot.ringImg.gameObject.SetActive(false);
                    continue;
                }

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_motionPingContainer, sp, null, out Vector2 lp))
                {
                    slot.ringImg.gameObject.SetActive(true);
                    var r = slot.ringImg.rectTransform;
                    r.anchoredPosition = lp;
                    float u = age / pingDur;
                    float scale = Mathf.Lerp(0.65f, 1.45f, u);
                    r.localScale = new Vector3(scale, scale, 1f);
                    float alpha = (1f - u) * 0.44f; // Subtle through-wall hint, never oppressive
                    slot.ringImg.color = new Color(Palette.HunterAccent.r, Palette.HunterAccent.g, Palette.HunterAccent.b, alpha);
                    if (slot.distTxt != null)
                    {
                        float d = Vector3.Distance(local.transform.position, slot.worldPos);
                        slot.distTxt.text = $"{d:0}m";
                        slot.distTxt.color = new Color(Palette.AccentGold.r, Palette.AccentGold.g, Palette.AccentGold.b, alpha * 1.1f);
                    }
                }
            }
        }

        private void AdjustMatchSlots(int delta)
        {
            AudioSynthManager.Instance?.PlayClick();
            int current = GameStateManager.Instance != null ? GameStateManager.Instance.TargetTotalPlayers : 12;
            int next = Mathf.Clamp(current + delta, 4, 16);
            if (GameStateManager.Instance != null) GameStateManager.Instance.TargetTotalPlayers = next;
            if (_titleSlotsLabel != null) _titleSlotsLabel.text = $"PLAYERS: {next}";
            if (_botCountSlider != null) _botCountSlider.SetValueWithoutNotify(next);
            if (_botCountLabel != null) _botCountLabel.text = $"MATCH SLOTS (ONLINE + BOTS): {next}";
        }

        private void SelectGraphicsTier(GraphicsPresetTier tier)
        {
            AudioSynthManager.Instance?.PlayClick();
            GraphicsQualityController.Apply(tier, save: true);
            RefreshQualityButtons();
            ShowToast($"Graphics: {tier.ToString().ToUpperInvariant()}", Palette.PropAccent);
        }

        private void RefreshQualityButtons()
        {
            GraphicsPresetTier cur = GraphicsQualityController.CurrentTier;
            void Paint(Button b, bool active)
            {
                if (b == null) return;
                var img = b.GetComponent<Image>();
                if (img != null) img.color = active ? Palette.PropTeal : Palette.PanelMid;
            }
            Paint(_btnQualLow, cur == GraphicsPresetTier.Low);
            Paint(_btnQualMed, cur == GraphicsPresetTier.Medium);
            Paint(_btnQualHigh, cur == GraphicsPresetTier.High);
        }

        private void HandleStrikeHitFeedback(bool confirmedHit)
        {
            if (_hitmarkerText == null) return;
            _hitmarkerText.color = confirmedHit ? Palette.HunterAccent : Palette.AccentGold;
            _hitmarkerText.alpha = 1f;
            _hitmarkerTimer = 0.18f;
        }

        private void HideAllKillFeedSlots()
        {
            for (int i = 0; i < _killFeedPool.Count; i++)
            {
                var slot = _killFeedPool[i];
                if (slot == null) continue;
                if (slot.fadeRoutine != null)
                {
                    StopCoroutine(slot.fadeRoutine);
                    slot.fadeRoutine = null;
                }
                if (slot.root != null) slot.root.SetActive(false);
            }
        }

        private void HandleKillFeedEvent(string victimName, string attackerName, Vector3 worldPos)
        {
            if (_killFeedRoot == null) return;
            string room = CoachingRoomLocator.GetRoomLabel(worldPos);
            string killer = string.IsNullOrEmpty(attackerName) ? "Hunter" : attackerName;
            SpawnKillFeedEntry($"{killer} tagged {victimName} ({room})");
        }

        private void SpawnKillFeedEntry(string text)
        {
            if (_killFeedPool.Count == 0) return;
            var slot = _killFeedPool[_killFeedCursor % _killFeedPool.Count];
            _killFeedCursor = (_killFeedCursor + 1) % _killFeedPool.Count;

            if (slot.fadeRoutine != null)
            {
                StopCoroutine(slot.fadeRoutine);
                slot.fadeRoutine = null;
            }
            if (slot.root == null) return;
            slot.root.transform.SetAsLastSibling();
            slot.root.SetActive(true);
            if (slot.label != null) slot.label.text = text;
            if (slot.cg != null) slot.cg.alpha = 0.94f;
            slot.fadeRoutine = StartCoroutine(FadeOutKillFeedSlot(slot));
        }

        private static IEnumerator FadeOutKillFeedSlot(PooledKillFeedSlot slot)
        {
            yield return new WaitForSecondsRealtime(2.8f);
            float t = 0f;
            while (t < 0.65f && slot.cg != null)
            {
                t += Time.unscaledDeltaTime;
                slot.cg.alpha = Mathf.Lerp(0.94f, 0f, t / 0.65f);
                yield return null;
            }
            if (slot.root != null) slot.root.SetActive(false);
            slot.fadeRoutine = null;
        }

        private void SelectPreferredRole(TeamRole? role)
        {
            AudioSynthManager.Instance?.PlayClick();
            if (MatchCoordinator.Instance != null) MatchCoordinator.Instance.PreferredLocalRole = role;
            HighlightRoleButtons(role);
        }

        private void HighlightRoleButtons(TeamRole? role)
        {
            void SetSel(Button btn, bool active, Color baseCol)
            {
                if (btn == null) return;
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = active ? Palette.AccentGold : baseCol;
            }
            SetSel(_btnRoleRandom, !role.HasValue, Palette.PanelMid);
            SetSel(_btnRoleProp, role == TeamRole.Prop, Palette.PropTeal);
            SetSel(_btnRoleHunter, role == TeamRole.Hunter, Palette.HunterNavy);
        }

        private void SavePlayerName()
        {
            string chosen = _nameInput != null && !string.IsNullOrWhiteSpace(_nameInput.text)
                ? _nameInput.text
                : (_titleNameInput != null ? _titleNameInput.text : "");
            if (!string.IsNullOrWhiteSpace(chosen) && GameStateManager.Instance != null)
            {
                GameStateManager.Instance.SetPlayerName(chosen);
                string clean = GameStateManager.Instance.Progress.playerName;
                if (_nameInput != null) _nameInput.SetTextWithoutNotify(clean);
                if (_titleNameInput != null) _titleNameInput.SetTextWithoutNotify(clean);
            }
        }

        private void OnCreateOnlineClicked()
        {
            AudioSynthManager.Instance?.PlayClick();
            SavePlayerName();
            var mrm = MultiplayerRoomManager.Instance;
            if (mrm == null) return;
            int maxSlots = Mathf.RoundToInt(_botCountSlider.value);
            if (GameStateManager.Instance != null) GameStateManager.Instance.TargetTotalPlayers = maxSlots;
            _roomStatusText.text = "Creating room on Authoritative Server...";

            // Instant Authoritative Server Room Creation
            mrm.CreateRoomSocketIO(_roomNameInput.text, maxSlots, isPrivate: false);
            RefreshOnlineLobbyState();
        }

        private void OnJoinByCodeClicked()
        {
            AudioSynthManager.Instance?.PlayClick();
            SavePlayerName();
            var mrm = MultiplayerRoomManager.Instance;
            if (mrm == null) return;
            string code = _joinCodeInput.text.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                _roomStatusText.text = "Enter a valid room code!";
                return;
            }
            _roomStatusText.text = $"Joining room {code}...";

            // Instant Authoritative Server Room Join
            mrm.JoinRoomSocketIO(code);
            RefreshOnlineLobbyState();
        }

        private void OnHostStartLobbyMatchClicked()
        {
            AudioSynthManager.Instance?.PlayClick();
            var mrm = MultiplayerRoomManager.Instance;
            if (mrm != null)
            {
                mrm.StartMatchSocketIO();
            }

            int slots = _botCountSlider != null ? Mathf.RoundToInt(_botCountSlider.value) : 12;
            GameStateManager.Instance?.StartMatch(true, slots);
        }

        private void OnSendLobbyChatClicked()
        {
            if (_lobbyChatInput == null || string.IsNullOrWhiteSpace(_lobbyChatInput.text)) return;
            string msg = _lobbyChatInput.text.Trim();
            _lobbyChatInput.text = "";
            MultiplayerRoomManager.Instance?.SendChatSocketIO(msg);
        }

        public void AppendLobbyChat(string sender, string msg, string role)
        {
            _chatHistory.AppendLine($"[{role}] {sender}: {msg}");
            if (_lobbyChatLogText != null) _lobbyChatLogText.text = _chatHistory.ToString();
        }

        private void RefreshSameWifiButton()
        {
            var mrm = MultiplayerRoomManager.Instance;
            bool on = mrm != null && mrm.UseSameWifiDirect;
            if (_sameWifiLabel != null)
                _sameWifiLabel.text = on ? "SAME WI-FI / HOTSPOT FAST MODE: ON" : "SAME WI-FI / HOTSPOT FAST MODE: OFF";
            if (_sameWifiBtn != null)
            {
                var img = _sameWifiBtn.GetComponent<Image>();
                if (img != null) img.color = on ? Palette.PropTeal : Palette.PanelMid;
            }
        }

        private void RefreshOnlineLobbyState()
        {
            RefreshSameWifiButton();
            var mrm = MultiplayerRoomManager.Instance;
            bool inRoom = mrm != null && mrm.IsInRoom;
            if (_roomBrowserBody != null) _roomBrowserBody.SetActive(!inRoom);
            if (_roomLobbyOverlay != null) _roomLobbyOverlay.SetActive(inRoom);
            if (!inRoom)
            {
                if (_roomStatusText != null && mrm != null && !string.IsNullOrEmpty(mrm.ConnectionStage))
                    _roomStatusText.text = mrm.ConnectionStage;
                return;
            }

            if (_lobbyHeaderText != null) _lobbyHeaderText.text = $"LOBBY: {mrm.CurrentRoomName}";
            if (_lobbyCodeText != null) _lobbyCodeText.text = $"ROOM CODE: {mrm.CurrentJoinCode}";

            var sb = new System.Text.StringBuilder();
            if (Networking.SocketIO.SocketIOUnityClient.Instance?.LastRoomState?.players != null && Networking.SocketIO.SocketIOUnityClient.Instance.LastRoomState.players.Count > 0)
            {
                var sList = Networking.SocketIO.SocketIOUnityClient.Instance.LastRoomState.players;
                int maxSlots = Networking.SocketIO.SocketIOUnityClient.Instance.LastRoomState.max_players > 0 ? Networking.SocketIO.SocketIOUnityClient.Instance.LastRoomState.max_players : 8;
                sb.AppendLine($"CONNECTED FRIENDS ({sList.Count}/{maxSlots}):");
                for (int i = 0; i < sList.Count; i++)
                {
                    var sp = sList[i];
                    string role = string.IsNullOrEmpty(sp.assigned_role) ? sp.preferred_role : sp.assigned_role;
                    string tag = sp.is_host ? " 👑[HOST]" : (sp.is_ready ? " ✅[READY]" : " ⏳[WAITING]");
                    sb.AppendLine($"  {i + 1}. {sp.player_name} ({role}){tag}");
                }
            }
            else
            {
                var peers = NetworkPlayerState.AllPeers;
                int count = peers.Count > 0 ? peers.Count : mrm.ConnectedHumanCount;
                sb.AppendLine($"CONNECTED FRIENDS ({count} Human + Bots backfill):");
                if (peers.Count > 0)
                {
                    for (int i = 0; i < peers.Count; i++)
                    {
                        var p = peers[i];
                        if (p == null) continue;
                        string n = p.NetName.Value.ToString();
                        if (string.IsNullOrEmpty(n)) n = $"Player {i + 1}";
                        string tag = p.OwnerClientId == 0 ? " (Host)" : "";
                        sb.AppendLine($"  {i + 1}. {n}{tag}");
                    }
                }
                else
                {
                    var names = mrm.GetConnectedPlayerNames();
                    for (int i = 0; i < names.Count; i++)
                        sb.AppendLine($"  {i + 1}. {names[i]}");
                }
            }
            if (_lobbyPlayersText != null) _lobbyPlayersText.text = sb.ToString();

            bool isHost = mrm.IsRoomHost;
            if (_lobbyStartBtn != null) _lobbyStartBtn.gameObject.SetActive(isHost);
            if (_lobbyWaitText != null) _lobbyWaitText.gameObject.SetActive(!isHost);
        }

        private async void RefreshOpenRooms()
        {
            if (_roomListContent == null) return;
            foreach (Transform child in _roomListContent) Destroy(child.gameObject);
            _roomStatusText.text = "Loading open Coaching rooms...";
            var mrm = MultiplayerRoomManager.Instance;
            if (mrm == null) return;
            var rooms = await mrm.QueryOpenRoomsAsync();
            if (rooms.Count == 0)
            {
                CreateLabel("EmptyRooms", _roomListContent, "No open online rooms found yet.\nCreate your own room or Play with AI Bots!", 24, Palette.TextMuted, TextAlignmentOptions.Center, Vector2.zero, new Vector2(660f, 120f));
                _roomStatusText.text = string.IsNullOrEmpty(mrm.LastError) ? "No open rooms found. Ready to host or play Bots!" : mrm.LastError;
                return;
            }
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                CreateButton($"JoinRoom_{i}", _roomListContent, $"{r.roomName}   ({r.playerCount}/{r.maxPlayers})", Vector2.zero, new Vector2(660f, 62f), Palette.HunterNavy, async () =>
                {
                    _roomStatusText.text = $"Connecting to {r.roomName}...";
                    if (await mrm.JoinRoomByIdAsync(r.roomId))
                        RefreshOnlineLobbyState();
                    else
                        _roomStatusText.text = mrm.LastError;
                });
            }
            _roomStatusText.text = $"Found {rooms.Count} open room(s). Tap to join!";
        }

        private void ShowToast(string msg, Color color)
        {
            if (_toastGroup == null) return;
            _toastText.text = msg;
            _toastText.color = color;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastSequence());
        }

        private IEnumerator ToastSequence()
        {
            yield return Ease.Tween(0.18f, a => _toastGroup.alpha = a, Ease.OutCubic, true);
            yield return new WaitForSecondsRealtime(2.4f);
            yield return Ease.Tween(0.28f, a => _toastGroup.alpha = 1f - a, Ease.OutCubic, true);
        }

        private void TriggerFlash(Color color, float duration)
        {
            if (_flashOverlay == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(Ease.Tween(duration, t =>
            {
                var c = color;
                c.a = Mathf.Lerp(0.45f, 0f, t);
                _flashOverlay.color = c;
            }, Ease.OutCubic, true));
        }

        private void ShowMilestone(string title, int style)
        {
            if (_milestoneGroup == null) return;
            _milestoneText.text = title;
            _milestoneText.color = style == 1 ? Palette.AccentGold : Palette.PropAccent;
            if (_milestoneRoutine != null) StopCoroutine(_milestoneRoutine);
            _milestoneRoutine = StartCoroutine(MilestoneSequence());
        }

        private IEnumerator MilestoneSequence()
        {
            _milestoneGroup.alpha = 1f;
            yield return Ease.Tween(0.32f, t => _milestoneRect.localScale = Vector3.one * t, Ease.OutBack, true);
            yield return new WaitForSecondsRealtime(1.1f);
            yield return Ease.Tween(0.22f, t =>
            {
                _milestoneRect.localScale = Vector3.one * (1f - t);
                _milestoneGroup.alpha = 1f - t;
            }, Ease.OutCubic, true);
        }

        private readonly Dictionary<CanvasGroup, Coroutine> _fadeCoroutines = new Dictionary<CanvasGroup, Coroutine>();

        private void SetGroupVisible(CanvasGroup cg, bool visible, float fadeSeconds)
        {
            if (cg == null) return;
            if (_fadeCoroutines.TryGetValue(cg, out var running) && running != null)
                StopCoroutine(running);
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
            if (visible) cg.gameObject.SetActive(true);
            _fadeCoroutines[cg] = StartCoroutine(Ease.Tween(fadeSeconds, t =>
            {
                cg.alpha = visible ? t : (1f - t);
                if (!visible && t >= 0.99f) cg.gameObject.SetActive(false);
            }, Ease.OutCubic, true));
        }

        // --- UI Factory Helpers (zero untinted white boxes!) ---

        private static CanvasGroup CreateScreenGroup(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
            return cg;
        }

        private static RectTransform CreateCard(string name, Transform parent, Vector2 size, Color bgCol)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.sizeDelta = size;
            Palette.StylePanel(go.GetComponent<Image>(), bgCol);
            return r;
        }

        private static TextMeshProUGUI CreateLabel(string name, Transform parent, string text, int fontSize, Color col, TextAlignmentOptions align, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            Palette.StyleText(tmp, col, bold: true, spacing: 2.8f);
            return tmp;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color bgCol, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(ButtonBounceAnimator));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            Palette.StylePanel(go.GetComponent<Image>(), bgCol);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            CreateLabel("Lbl", go.transform, label, 22, Palette.TextLight, TextAlignmentOptions.Center, Vector2.zero, size - new Vector2(18f, 10f));
            return btn;
        }

        private static Button CreateButtonWithIcon(string name, Transform parent, string label, HudIconKind icon, Vector2 pos, Vector2 size, Color bgCol, Action onClick)
        {
            var btn = CreateButton(name, parent, "", pos, size, bgCol, onClick);
            float iconSize = Mathf.Min(36f, size.y * 0.56f);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(btn.transform, false);
            var iRect = iconGo.GetComponent<RectTransform>();
            iRect.anchorMin = new Vector2(0f, 0.5f);
            iRect.anchorMax = new Vector2(0f, 0.5f);
            iRect.anchoredPosition = new Vector2(18f + iconSize * 0.5f, 0f);
            iRect.sizeDelta = new Vector2(iconSize, iconSize);
            var img = iconGo.GetComponent<Image>();
            img.sprite = HudIcons.Get(icon);
            img.color = Palette.TextLight;
            img.raycastTarget = false;

            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.text = label;
                lbl.rectTransform.anchoredPosition = new Vector2(iconSize * 0.5f + 6f, 0f);
                lbl.rectTransform.sizeDelta = new Vector2(size.x - iconSize - 38f, size.y - 10f);
            }
            return btn;
        }

        private static Button CreateActionCircle(string name, Transform parent, string label, Vector2 pos, float diameter, Color bgCol, Action onClick, HudIconKind? icon = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(ButtonBounceAnimator));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(diameter, diameter);
            var img = go.GetComponent<Image>();
            img.sprite = Palette.GetCircleSprite();
            img.color = bgCol;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Crisp outer glow rim ring
            var rimGo = new GameObject("GlowRim", typeof(RectTransform), typeof(Image));
            rimGo.transform.SetParent(go.transform, false);
            var rimRect = rimGo.GetComponent<RectTransform>();
            rimRect.anchorMin = Vector2.zero;
            rimRect.anchorMax = Vector2.one;
            rimRect.offsetMin = new Vector2(-4f, -4f);
            rimRect.offsetMax = new Vector2(4f, 4f);
            var rimImg = rimGo.GetComponent<Image>();
            rimImg.sprite = Palette.GetRingSprite();
            rimImg.color = new Color(1f, 1f, 1f, 0.28f);
            rimImg.raycastTarget = false;

            if (icon.HasValue)
            {
                var iGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iGo.transform.SetParent(go.transform, false);
                var iRect = iGo.GetComponent<RectTransform>();
                bool iconOnly = string.IsNullOrEmpty(label);
                float iSz = iconOnly ? diameter * 0.50f : diameter * 0.36f;
                iRect.sizeDelta = new Vector2(iSz, iSz);
                iRect.anchoredPosition = iconOnly ? Vector2.zero : new Vector2(0f, diameter * 0.14f);
                var iImg = iGo.GetComponent<Image>();
                iImg.sprite = HudIcons.Get(icon.Value);
                iImg.color = Palette.TextLight;
                iImg.raycastTarget = false;

                if (!iconOnly)
                    CreateLabel("Lbl", go.transform, label, 13, Palette.TextLight, TextAlignmentOptions.Center, new Vector2(0f, -diameter * 0.23f), new Vector2(diameter - 10f, diameter * 0.34f));
            }
            else
            {
                CreateLabel("Lbl", go.transform, label, 16, Palette.TextLight, TextAlignmentOptions.Center, Vector2.zero, new Vector2(diameter - 10f, diameter - 10f));
            }
            return btn;
        }

        private static TMP_InputField CreateInputField(string name, Transform parent, string placeholder, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            Palette.StylePanel(go.GetComponent<Image>(), Palette.PanelMid);

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(go.transform, false);
            var taRect = textArea.GetComponent<RectTransform>();
            taRect.anchorMin = Vector2.zero;
            taRect.anchorMax = Vector2.one;
            taRect.offsetMin = new Vector2(14f, 6f);
            taRect.offsetMax = new Vector2(-14f, -6f);

            var phTmp = CreateLabel("Placeholder", textArea.transform, placeholder, 22, Palette.TextMuted, TextAlignmentOptions.MidlineLeft, Vector2.zero, size - new Vector2(28f, 12f));
            var txtTmp = CreateLabel("Text", textArea.transform, "", 24, Palette.TextLight, TextAlignmentOptions.MidlineLeft, Vector2.zero, size - new Vector2(28f, 12f));
            txtTmp.rectTransform.anchorMin = Vector2.zero;
            txtTmp.rectTransform.anchorMax = Vector2.one;
            txtTmp.rectTransform.offsetMin = Vector2.zero;
            txtTmp.rectTransform.offsetMax = Vector2.zero;

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = taRect;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            return input;
        }

        private static Slider CreateSlider(string name, Transform parent, Vector2 pos, Vector2 size, float min, float max, float val, bool whole, Action<float> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta = size;

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            Palette.StylePanel(bg, Palette.PanelMid);
            bg.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.75f);
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(go.transform, false);
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(fillArea, false);
            Palette.StylePanel(fill, Palette.PropTeal);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(go.transform, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = Vector2.zero;
            handleArea.offsetMax = Vector2.zero;

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            handle.transform.SetParent(handleArea, false);
            handle.sprite = Palette.GetCircleSprite();
            handle.color = Palette.AccentGold;
            handle.rectTransform.sizeDelta = new Vector2(size.y, size.y);

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.wholeNumbers = whole;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = val;
            slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            return slider;
        }
    }
}
