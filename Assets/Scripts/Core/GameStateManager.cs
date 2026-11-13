using System;
using System.Collections;
using System.Collections.Generic;
using CoachingPropHunt.Props;
using UnityEngine;

namespace CoachingPropHunt.Core
{
    public enum AppShellState
    {
        Title = 0,
        RoomLobby = 1,
        Playing = 2,
        Paused = 3,
        Ended = 4
    }

    public enum MatchPhase
    {
        Waiting = 0,
        HidePhase = 1,
        HuntPhase = 2,
        RoundOver = 3
    }

    public enum TeamRole
    {
        Prop = 0,
        Hunter = 1
    }

    [Serializable]
    public sealed class PlayerProgressData
    {
        public string playerName = "Student";
        public int matchesPlayed;
        public int propsEliminated;
        public int roundsSurvived;
        public int highScore;
    }

    /// <summary>
    /// Authoritative local game loop & UI shell state machine (Title -> RoomLobby -> Playing -> Paused -> Ended)
    /// plus Prop Hunt round timer, team counters, toast notifications, and player stats persistence.
    /// </summary>
    public sealed class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        [Header("Core References")]
        [SerializeField] private PropRegistrySO propRegistry;
        [SerializeField] private Transform[] hunterSpawns;
        [SerializeField] private Transform[] propSpawns;

        [Header("Match Rules")]
        [SerializeField] private float hidePhaseSeconds = 60f;
        [SerializeField] private float huntPhaseSeconds = 300f;
        [SerializeField] private int targetTotalPlayers = 12;

        public AppShellState ShellState { get; private set; } = AppShellState.Title;
        public MatchPhase Phase { get; private set; } = MatchPhase.Waiting;
        public float PhaseRemainingSeconds { get; private set; }
        public int AlivePropsCount { get; private set; }
        public int AliveHuntersCount { get; private set; }
        public TeamRole WinningTeam { get; private set; } = TeamRole.Prop;
        public string WinReason { get; private set; } = "";
        public string LastToastMessage { get; private set; } = "";
        public float ToastExpireTime { get; private set; }
        public bool IsOnlineSession { get; private set; }
        public bool IsMatchLive => ShellState == AppShellState.Playing || (IsOnlineSession && ShellState == AppShellState.Paused);

        public PropRegistrySO PropRegistry => propRegistry;
        public float HideDuration => hidePhaseSeconds;
        public float HuntDuration => huntPhaseSeconds;
        public int TargetTotalPlayers
        {
            get => targetTotalPlayers;
            set => targetTotalPlayers = Mathf.Clamp(value, 4, 16);
        }

        public readonly PlayerProgressData Progress = new PlayerProgressData();

        public event Action<AppShellState> OnShellStateChanged;
        public event Action<MatchPhase> OnMatchPhaseChanged;
        public event Action<string, Color> OnToast;
        public event Action<Color, float> OnScreenFlash;
        public event Action<string, int> OnMilestoneBanner;
        public event Action OnMatchRequestedStart;
        public event Action OnMatchCleanupRequested;

        private Coroutine _transitionRoutine;
        private bool _hadHuntersThisRound;
        private bool _hadPropsThisRound;
        private float _roundElapsedSeconds;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            GraphicsQualityController.LoadAndApply();
            LoadLocalProgress();
        }

        public void SetPlayerName(string rawName)
        {
            string cleaned = string.IsNullOrWhiteSpace(rawName) ? "Student" : rawName.Trim();
            if (cleaned.Length > 18) cleaned = cleaned.Substring(0, 18);
            Progress.playerName = cleaned;
            PlayerPrefs.SetString("CoachingPropHunt_PlayerName", cleaned);
            PlayerPrefs.Save();
            if (Networking.NetworkPlayerState.LocalPeer != null)
                Networking.NetworkPlayerState.LocalPeer.UpdateLocalName(cleaned);
        }

        private void LoadLocalProgress()
        {
            if (PlayerPrefs.HasKey("CoachingPropHunt_PlayerName"))
                Progress.playerName = PlayerPrefs.GetString("CoachingPropHunt_PlayerName", "Student");
            Progress.matchesPlayed = PlayerPrefs.GetInt("CoachingPropHunt_MatchesPlayed", 0);
            Progress.propsEliminated = PlayerPrefs.GetInt("CoachingPropHunt_PropsEliminated", 0);
            Progress.roundsSurvived = PlayerPrefs.GetInt("CoachingPropHunt_RoundsSurvived", 0);
            Progress.highScore = PlayerPrefs.GetInt("CoachingPropHunt_HighScore", 0);
        }

        private void SaveLocalProgress()
        {
            PlayerPrefs.SetString("CoachingPropHunt_PlayerName", Progress.playerName);
            PlayerPrefs.SetInt("CoachingPropHunt_MatchesPlayed", Progress.matchesPlayed);
            PlayerPrefs.SetInt("CoachingPropHunt_PropsEliminated", Progress.propsEliminated);
            PlayerPrefs.SetInt("CoachingPropHunt_RoundsSurvived", Progress.roundsSurvived);
            PlayerPrefs.SetInt("CoachingPropHunt_HighScore", Progress.highScore);
            PlayerPrefs.Save();
        }

        private async void Start()
        {
            try
            {
                var loaded = await PlayerSave.LoadAsync<PlayerProgressData>(null);
                if (loaded != null)
                {
                    if (!string.IsNullOrEmpty(loaded.playerName) && !PlayerPrefs.HasKey("CoachingPropHunt_PlayerName"))
                        Progress.playerName = loaded.playerName;
                    Progress.matchesPlayed = Mathf.Max(Progress.matchesPlayed, loaded.matchesPlayed);
                    Progress.propsEliminated = Mathf.Max(Progress.propsEliminated, loaded.propsEliminated);
                    Progress.roundsSurvived = Mathf.Max(Progress.roundsSurvived, loaded.roundsSurvived);
                    Progress.highScore = Mathf.Max(Progress.highScore, loaded.highScore);
                    SaveLocalProgress();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Progress load skipped: {e.Message}");
            }
            if (ShellState == AppShellState.Title)
                SetShellState(AppShellState.Title);
        }

        public void ConfigureMapData(PropRegistrySO registry, Transform[] hunters, Transform[] props)
        {
            propRegistry = registry;
            hunterSpawns = hunters ?? Array.Empty<Transform>();
            propSpawns = props ?? Array.Empty<Transform>();
        }

        public Transform GetHunterSpawn(int index)
        {
            if (hunterSpawns == null || hunterSpawns.Length == 0) return null;
            return hunterSpawns[((index % hunterSpawns.Length) + hunterSpawns.Length) % hunterSpawns.Length];
        }

        public Transform GetPropSpawn(int index)
        {
            if (propSpawns == null || propSpawns.Length == 0) return null;
            return propSpawns[((index % propSpawns.Length) + propSpawns.Length) % propSpawns.Length];
        }

        public void SetShellState(AppShellState next)
        {
            ShellState = next;
            Time.timeScale = (next == AppShellState.Paused && !IsOnlineSession) ? 0f : 1f;

            // Thermal & Battery Optimization:
            // 30 FPS in title/browser/lobby to let GPU and phone cool down
            // 60 FPS in active match for peak responsive gameplay
            if (next == AppShellState.Title || next == AppShellState.RoomLobby)
            {
                Application.targetFrameRate = 30;
            }
            else
            {
                Application.targetFrameRate = 60;
            }

            OnShellStateChanged?.Invoke(next);
        }

        public void OpenRoomBrowser()
        {
            AudioSynthManager.Instance?.PlayClick();
            SetShellState(AppShellState.RoomLobby);
        }

        public void ReturnToTitle()
        {
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }
            AudioSynthManager.Instance?.PlayClick();
            IsOnlineSession = false;
            Time.timeScale = 1f;
            OnMatchCleanupRequested?.Invoke();
            CampusSectorStreamer.TrimMemoryOnMatchExit();
            Phase = MatchPhase.Waiting;
            SetShellState(AppShellState.Title);
        }

        public void StartMatch(bool onlineSession, int totalSlots = 12)
        {
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }
            ObjectPoolManager.EnsureExists()?.PrewarmDefaultPools();
            IsOnlineSession = onlineSession;
            TargetTotalPlayers = totalSlots;
            _hadHuntersThisRound = false;
            _hadPropsThisRound = false;
            _roundElapsedSeconds = 0f;
            AlivePropsCount = 0;
            AliveHuntersCount = 0;
            Phase = MatchPhase.HidePhase;
            PhaseRemainingSeconds = hidePhaseSeconds;
            WinningTeam = TeamRole.Prop;
            WinReason = "";
            Time.timeScale = 1f;
            SetShellState(AppShellState.Playing);
            OnMatchRequestedStart?.Invoke();
            OnMatchPhaseChanged?.Invoke(Phase);
            TriggerMilestone("HIDE PHASE", 0);
            int hideSec = Mathf.RoundToInt(hidePhaseSeconds);
            ShowToast($"Props: Hide inside the Coaching Center! Hunters locked for {hideSec}s.", Palette.PropAccent);
        }

        public void TogglePause()
        {
            if (ShellState == AppShellState.Playing) SetShellState(AppShellState.Paused);
            else if (ShellState == AppShellState.Paused) SetShellState(AppShellState.Playing);
        }

        public void UpdateAliveCounts(int aliveProps, int aliveHunters)
        {
            AlivePropsCount = aliveProps;
            AliveHuntersCount = aliveHunters;
            if (aliveProps > 0) _hadPropsThisRound = true;
            if (aliveHunters > 0) _hadHuntersThisRound = true;
            if (!IsMatchLive) return;

            // Online authority rule: ONLY the Host/Server ever decides round win/loss!
            if (IsOnlineSession && Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening
                && !Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                return;
            }

            // User rule: never end a match during Hide Phase; and wait >= 5s spawn grace in online Hunt Phase
            if (Phase != MatchPhase.HuntPhase) return;
            if (IsOnlineSession && (_roundElapsedSeconds < 5.0f || !_hadPropsThisRound)) return;

            if (aliveProps <= 0)
            {
                EndMatch(TeamRole.Hunter, "All Props Found & Eliminated!");
            }
            else if (_hadHuntersThisRound && aliveHunters <= 0)
            {
                EndMatch(TeamRole.Prop, "All Hunters Knocked Out by Penalties!");
            }
        }

        public void SyncPhaseFromNetwork(MatchPhase netPhase, float netRemaining)
        {
            bool changed = Phase != netPhase;
            Phase = netPhase;
            PhaseRemainingSeconds = Mathf.Max(0f, netRemaining);
            if (changed) OnMatchPhaseChanged?.Invoke(Phase);
        }

        public void ShowToast(string message, Color color)
        {
            LastToastMessage = message;
            ToastExpireTime = Time.unscaledTime + 3.2f;
            OnToast?.Invoke(message, color);
        }

        public void TriggerFlash(Color color, float duration = 0.16f)
        {
            OnScreenFlash?.Invoke(color, duration);
        }

        public void TriggerMilestone(string title, int subValue = 0)
        {
            OnMilestoneBanner?.Invoke(title, subValue);
        }

        public void RecordRoundResult(int localScore, bool localSurvivedAsProp, int eliminations)
        {
            Progress.matchesPlayed++;
            Progress.propsEliminated += eliminations;
            if (localSurvivedAsProp) Progress.roundsSurvived++;
            if (localScore > Progress.highScore) Progress.highScore = localScore;
            SaveLocalProgress();
            _ = PlayerSave.SaveAsync(Progress);
        }

        private void Update()
        {
            if (!IsMatchLive) return;
            if (Phase == MatchPhase.HidePhase || Phase == MatchPhase.HuntPhase)
                _roundElapsedSeconds += Time.deltaTime;

            bool isNetClient = IsOnlineSession
                && Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening
                && !Unity.Netcode.NetworkManager.Singleton.IsServer;
            if (isNetClient) return;

            if (Phase == MatchPhase.HidePhase)
            {
                PhaseRemainingSeconds -= Time.deltaTime;
                if (PhaseRemainingSeconds <= 0f)
                {
                    Phase = MatchPhase.HuntPhase;
                    PhaseRemainingSeconds = huntPhaseSeconds;
                    OnMatchPhaseChanged?.Invoke(Phase);
                    AudioSynthManager.Instance?.PlayRadarPing();
                    TriggerFlash(Palette.HunterAccent, 0.25f);
                    TriggerMilestone("HUNT BEGINS!", 0);
                    ShowToast("Hunters Released! Check every desk, cycle, bottle & kitchen item.", Palette.HunterAccent);
                }
            }
            else if (Phase == MatchPhase.HuntPhase)
            {
                PhaseRemainingSeconds -= Time.deltaTime;
                if (PhaseRemainingSeconds <= 0f)
                {
                    EndMatch(TeamRole.Prop, "Time Up! Hidden Props Survived the Coaching Bell!");
                }
            }
        }

        public void EndMatch(TeamRole winner, string reason)
        {
            if (Phase == MatchPhase.RoundOver) return;
            Phase = MatchPhase.RoundOver;
            WinningTeam = winner;
            WinReason = reason;
            OnMatchPhaseChanged?.Invoke(Phase);
            AudioSynthManager.Instance?.PlayVictorySting();
            TriggerMilestone(winner == TeamRole.Hunter ? "HUNTERS WIN!" : "PROPS WIN!", 1);
            Vector3 fxPos = Gameplay.PlayerParticipant.LocalPlayer != null
                ? Gameplay.PlayerParticipant.LocalPlayer.transform.position + Vector3.up * 0.6f
                : (UnityEngine.Camera.main != null
                    ? UnityEngine.Camera.main.transform.position + UnityEngine.Camera.main.transform.forward * 4f
                    : new Vector3(0f, 2f, 4f));
            VictoryConfettiFx.SpawnAt(fxPos);
            if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
            _transitionRoutine = StartCoroutine(DelayedEndedOverlay());
        }

        private IEnumerator DelayedEndedOverlay()
        {
            yield return new WaitForSecondsRealtime(1.6f);
            if (ShellState == AppShellState.Playing || ShellState == AppShellState.Paused)
            {
                SetShellState(AppShellState.Ended);
            }
        }
    }
}
