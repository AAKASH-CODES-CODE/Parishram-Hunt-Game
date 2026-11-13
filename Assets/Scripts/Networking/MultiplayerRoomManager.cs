using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CoachingPropHunt.Core;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using Unity.Services.Qos;
using UnityEngine;

namespace CoachingPropHunt.Networking
{
    [Serializable]
    public struct RoomSummaryItem
    {
        public string roomId;
        public string roomName;
        public int playerCount;
        public int maxPlayers;
    }

    /// <summary>
    /// Serializes match round state & remaining clock cleanly during Host Migration.
    /// </summary>
    public sealed class CoachingMigrationDataHandler : IMigrationDataHandler
    {
        public byte[] Generate()
        {
            var gsm = GameStateManager.Instance;
            int phase = gsm != null ? (int)gsm.Phase : 0;
            float rem = gsm != null ? gsm.PhaseRemainingSeconds : 0f;
            int target = gsm != null ? gsm.TargetTotalPlayers : 12;
            string payload = $"{phase}|{rem:F2}|{target}";
            return Encoding.UTF8.GetBytes(payload);
        }

        public void Apply(byte[] migrationData)
        {
            if (migrationData == null || migrationData.Length == 0) return;
            string payload = Encoding.UTF8.GetString(migrationData);
            string[] parts = payload.Split('|');
            if (parts.Length < 3) return;
            if (int.TryParse(parts[0], out int phase) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rem) &&
                int.TryParse(parts[2], out int target))
            {
                if (GameStateManager.Instance != null)
                {
                    GameStateManager.Instance.TargetTotalPlayers = target;
                    GameStateManager.Instance.SyncPhaseFromNetwork((MatchPhase)phase, rem);
                }
            }
        }
    }

    /// <summary>
    /// Online room creation, fast cached-QoS join-by-code, optional Same-WiFi Direct mode,
    /// open-room browser, host migration, and clean disconnect recovery.
    /// </summary>
    public sealed class MultiplayerRoomManager : MonoBehaviour
    {
        public static MultiplayerRoomManager Instance { get; private set; }

        private const string PrefRegionKey = "ParishramHunt_RelayRegion";
        private const string PrefRegionTsKey = "ParishramHunt_RelayRegionTs";
        private const string PrefSameWifiKey = "ParishramHunt_SameWifiDirect";

        private ISession _activeSession;
        private readonly CoachingMigrationDataHandler _migrationHandler = new CoachingMigrationDataHandler();
        private Action _onSessionChanged;
        private Action<string> _onPlayerJoined;
        private Action<string> _onPlayerLeft;
        private Action _onRemovedFromSession;
        private string _lastSyncedPlayerName = "";
        private string _cachedRelayRegion = "";
        private Task _prewarmTask;
        private Task _pendingLeaveTask;

        public bool IsInRoom => (_activeSession != null && _activeSession.State == SessionState.Connected) ||
                                (SocketIO.SocketIOUnityClient.Instance != null && !string.IsNullOrEmpty(SocketIO.SocketIOUnityClient.Instance.CurrentRoomCode));

        public string CurrentJoinCode => !string.IsNullOrEmpty(SocketIO.SocketIOUnityClient.Instance?.CurrentRoomCode)
                                            ? SocketIO.SocketIOUnityClient.Instance.CurrentRoomCode
                                            : (_activeSession != null ? _activeSession.Code : "");

        public string CurrentRoomName => SocketIO.SocketIOUnityClient.Instance?.LastRoomState != null
                                            ? SocketIO.SocketIOUnityClient.Instance.LastRoomState.room_name
                                            : (_activeSession != null ? _activeSession.Name : "");

        public int ConnectedHumanCount => SocketIO.SocketIOUnityClient.Instance?.LastRoomState?.players != null
                                            ? SocketIO.SocketIOUnityClient.Instance.LastRoomState.players.Count
                                            : (_activeSession != null && _activeSession.Players != null ? _activeSession.Players.Count : 1);

        public bool IsRoomHost
        {
            get
            {
                if (SocketIO.SocketIOUnityClient.Instance?.LastRoomState != null)
                {
                    string mySid = SocketIO.SocketIOUnityClient.Instance.SocketId;
                    var me = SocketIO.SocketIOUnityClient.Instance.LastRoomState.players.Find(p => p.sid == mySid || p.is_host);
                    return me != null && me.is_host;
                }
                return _activeSession == null || _activeSession.IsHost;
            }
        }

        public string LastError { get; private set; } = "";
        public string ConnectionStage { get; private set; } = "";
        public string CachedRelayRegion => _cachedRelayRegion;

        public bool UseSameWifiDirect
        {
            get => PlayerPrefs.GetInt(PrefSameWifiKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefSameWifiKey, value ? 1 : 0);
                PlayerPrefs.Save();
                OnRoomStateChanged?.Invoke();
            }
        }

        public IReadOnlyList<string> GetConnectedPlayerNames()
        {
            var names = new List<string>();
            if (SocketIO.SocketIOUnityClient.Instance?.LastRoomState?.players != null && SocketIO.SocketIOUnityClient.Instance.LastRoomState.players.Count > 0)
            {
                foreach (var p in SocketIO.SocketIOUnityClient.Instance.LastRoomState.players)
                {
                    string tag = p.is_host ? " (Host)" : (p.is_ready ? " [Ready]" : "");
                    names.Add($"{p.player_name}{tag}");
                }
                return names;
            }

            if (_activeSession != null && _activeSession.Players != null)
            {
                for (int i = 0; i < _activeSession.Players.Count; i++)
                {
                    var p = _activeSession.Players[i];
                    if (p == null) continue;
                    string pName = null;
                    if (p.Properties != null && p.Properties.TryGetValue("playerName", out var prop))
                        pName = prop?.Value;
                    names.Add(!string.IsNullOrEmpty(pName) ? pName : $"Player {i + 1}");
                }
            }
            return names;
        }

        public void CreateRoomSocketIO(string roomName, int maxPlayers = 8, bool isPrivate = false)
        {
            HookSocketIOClient();
            string pname = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
            SocketIO.SocketIOUnityClient.Instance?.CreateRoom(pname, roomName, maxPlayers, isPrivate);
        }

        public void JoinRoomSocketIO(string roomCode)
        {
            HookSocketIOClient();
            string pname = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
            SocketIO.SocketIOUnityClient.Instance?.JoinRoom(pname, roomCode);
        }

        public void KickPlayerSocketIO(string targetSid)
        {
            SocketIO.SocketIOUnityClient.Instance?.KickPlayer(targetSid);
        }

        public void SendChatSocketIO(string message)
        {
            SocketIO.SocketIOUnityClient.Instance?.SendChat(message);
        }

        public void ToggleReadySocketIO(bool isReady)
        {
            SocketIO.SocketIOUnityClient.Instance?.ToggleReady(isReady);
        }

        public void SetRoleSocketIO(string role)
        {
            SocketIO.SocketIOUnityClient.Instance?.SetRole(role);
        }

        public void StartMatchSocketIO()
        {
            SocketIO.SocketIOUnityClient.Instance?.StartMatch();
        }

        public event Action OnRoomStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            LoadCachedRegionFromPrefs();
            PlayerAuthentication.OnSignedInCompleted += HandleSignedInPrewarm;

            HookSocketIOClient();
        }

        private void Start()
        {
            HookSocketIOClient();
        }

        public void HookSocketIOClient()
        {
            var sio = SocketIO.SocketIOUnityClient.Instance;
            if (sio != null)
            {
                sio.OnRoomStateUpdated -= HandleSocketIORoomUpdated;
                sio.OnRoomStateUpdated += HandleSocketIORoomUpdated;
                sio.OnMatchStarted -= HandleSocketIOMatchStarted;
                sio.OnMatchStarted += HandleSocketIOMatchStarted;
                sio.OnKicked -= HandleSocketIOKicked;
                sio.OnKicked += HandleSocketIOKicked;
            }
        }

        private void HandleSocketIORoomUpdated(SocketIO.RoomStateData data)
        {
            Debug.Log($"[MRM] HandleSocketIORoomUpdated: Code={data?.room_code}, Players={data?.players?.Count}");
            OnRoomStateChanged?.Invoke();
        }

        private void HandleSocketIOMatchStarted(SocketIO.MatchStartedData data)
        {
            Debug.Log($"[MRM] HandleSocketIOMatchStarted: Starting match for room {data?.room_code}");
            if (GameStateManager.Instance != null && !GameStateManager.Instance.IsMatchLive)
            {
                int totalSlots = (data != null && data.hunters_count + data.props_count > 0) ? (data.hunters_count + data.props_count) : 8;
                GameStateManager.Instance.StartMatch(true, totalSlots);
            }
        }

        private void HandleSocketIOKicked(string reason)
        {
            LastError = reason;
            GameStateManager.Instance?.ShowToast(reason, Palette.DangerRed);
            GameStateManager.Instance?.ReturnToTitle();
            OnRoomStateChanged?.Invoke();
        }

        private void LoadCachedRegionFromPrefs()
        {
            string saved = PlayerPrefs.GetString(PrefRegionKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (long.TryParse(PlayerPrefs.GetString(PrefRegionTsKey, "0"), out long unixTs))
            {
                long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixTs;
                if (age >= 0 && age < 6 * 3600)
                    _cachedRelayRegion = saved;
            }
        }

        private void HandleSignedInPrewarm()
        {
            PrewarmMultiplayerAsync();
        }

        public void PrewarmMultiplayerAsync()
        {
            if (_prewarmTask != null && !_prewarmTask.IsCompleted) return;
            _prewarmTask = PrewarmInternalAsync();
        }

        private async Task PrewarmInternalAsync()
        {
            try
            {
                bool signedIn = await PlayerAuthentication.EnsureSignedInAsync();
                if (!signedIn) return;
                LiveOps.StartBackgroundFetch();

                string pname = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
                _ = SyncPlayerNameBackgroundAsync(pname);

                if (QosService.Instance != null)
                {
                    var qosResults = await QosService.Instance.GetSortedRelayQosResultsAsync(null);
                    if (qosResults != null && qosResults.Count > 0 && !string.IsNullOrEmpty(qosResults[0].Region))
                    {
                        _cachedRelayRegion = qosResults[0].Region;
                        PlayerPrefs.SetString(PrefRegionKey, _cachedRelayRegion);
                        PlayerPrefs.SetString(PrefRegionTsKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                        PlayerPrefs.Save();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Multiplayer pre-warm skipped: {e.Message}");
            }
        }

        private async Task SyncPlayerNameBackgroundAsync(string pname)
        {
            if (string.IsNullOrWhiteSpace(pname) || string.Equals(pname, _lastSyncedPlayerName, StringComparison.Ordinal))
                return;
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(pname.Trim());
                _lastSyncedPlayerName = pname.Trim();
            }
            catch { /* Optional Auth profile display update */ }
        }

        private void SetStage(string stage)
        {
            ConnectionStage = stage;
            OnRoomStateChanged?.Invoke();
        }

        private async Task<bool> EnsureOnlineServicesReadyAsync()
        {
            LastError = "";
            SetStage("Signing in...");
            bool signedIn = await PlayerAuthentication.EnsureSignedInAsync();
            if (!signedIn)
            {
                string detail = string.IsNullOrEmpty(PlayerAuthentication.LastError) ? "" : $" ({PlayerAuthentication.LastError})";
                LastError = $"Online sign-in unavailable{detail}. Check connection or play Bots.";
                SetStage("");
                return false;
            }
            bool liveEnabled = await LiveOps.IsEnabledAsync();
            if (!liveEnabled)
            {
                LastError = "Online matchmaking is temporarily paused for maintenance. Offline Bots mode is ready!";
                SetStage("");
                return false;
            }
            string pname = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
            _ = SyncPlayerNameBackgroundAsync(pname);
            PrewarmMultiplayerAsync();
            return true;
        }

        private async Task EnsureCleanSessionBeforeConnectAsync()
        {
            if (_pendingLeaveTask != null && !_pendingLeaveTask.IsCompleted)
                await _pendingLeaveTask;
            if (_activeSession != null || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
                await LeaveRoomAsync();
        }

        public async Task<bool> CreateRoomAsync(string roomName, int maxPlayers = 16, bool isPrivate = false)
        {
            if (!await EnsureOnlineServicesReadyAsync()) return false;
            try
            {
                await EnsureCleanSessionBeforeConnectAsync();
                SetStage(UseSameWifiDirect ? "Starting Wi-Fi Direct Room..." : "Creating Room...");

                int clampedMax = Mathf.Clamp(maxPlayers, 2, 16);
                var opts = new SessionOptions
                {
                    Name = string.IsNullOrWhiteSpace(roomName) ? $"CoachingRoom_{UnityEngine.Random.Range(100, 999)}" : roomName.Trim(),
                    MaxPlayers = clampedMax,
                    IsPrivate = isPrivate
                }
                .WithPlayerName();

                if (UseSameWifiDirect)
                {
                    opts.WithDirectNetwork();
                }
                else if (!string.IsNullOrEmpty(_cachedRelayRegion))
                {
                    opts.WithRelayNetwork(_cachedRelayRegion).WithHostMigration(_migrationHandler);
                }
                else
                {
                    opts.WithRelayNetwork().WithHostMigration(_migrationHandler);
                }

                _activeSession = await MultiplayerService.Instance.CreateSessionAsync(opts);
                HookSessionEvents(_activeSession);
                SetStage("");
                return true;
            }
            catch (Exception e)
            {
                LastError = $"Could not create room: {e.Message}";
                Debug.LogWarning(LastError);
                SetStage("");
                return false;
            }
        }

        public async Task<bool> JoinRoomByCodeAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                LastError = "Enter a valid room code first.";
                return false;
            }
            if (!await EnsureOnlineServicesReadyAsync()) return false;
            try
            {
                await EnsureCleanSessionBeforeConnectAsync();
                SetStage("Joining room by code...");
                var opts = new JoinSessionOptions()
                    .WithPlayerName()
                    .WithHostMigration(_migrationHandler);
                _activeSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim().ToUpperInvariant(), opts);
                HookSessionEvents(_activeSession);
                SetStage("");
                return true;
            }
            catch (Exception e)
            {
                LastError = $"Could not join room code '{joinCode}': {e.Message}";
                Debug.LogWarning(LastError);
                SetStage("");
                return false;
            }
        }

        public async Task<bool> JoinRoomByIdAsync(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return false;
            if (!await EnsureOnlineServicesReadyAsync()) return false;
            try
            {
                await EnsureCleanSessionBeforeConnectAsync();
                SetStage("Connecting to room...");
                var opts = new JoinSessionOptions()
                    .WithPlayerName()
                    .WithHostMigration(_migrationHandler);
                _activeSession = await MultiplayerService.Instance.JoinSessionByIdAsync(roomId, opts);
                HookSessionEvents(_activeSession);
                SetStage("");
                return true;
            }
            catch (Exception e)
            {
                LastError = $"Could not join room: {e.Message}";
                Debug.LogWarning(LastError);
                SetStage("");
                return false;
            }
        }

        public async Task<List<RoomSummaryItem>> QueryOpenRoomsAsync()
        {
            var list = new List<RoomSummaryItem>();
            if (!await EnsureOnlineServicesReadyAsync()) return list;
            try
            {
                SetStage("Finding open rooms...");
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 16 });
                if (results != null && results.Sessions != null)
                {
                    foreach (var s in results.Sessions)
                    {
                        if (s.IsLocked || s.AvailableSlots <= 0) continue;
                        list.Add(new RoomSummaryItem
                        {
                            roomId = s.Id,
                            roomName = string.IsNullOrEmpty(s.Name) ? "Coaching Match" : s.Name,
                            playerCount = Mathf.Max(1, s.MaxPlayers - s.AvailableSlots),
                            maxPlayers = s.MaxPlayers
                        });
                    }
                }
            }
            catch (Exception e)
            {
                LastError = $"Room list refresh failed: {e.Message}";
                Debug.LogWarning(LastError);
            }
            SetStage("");
            return list;
        }

        public Task LeaveRoomAsync()
        {
            if (_pendingLeaveTask != null && !_pendingLeaveTask.IsCompleted)
                return _pendingLeaveTask;
            _pendingLeaveTask = LeaveRoomInternalAsync();
            return _pendingLeaveTask;
        }

        private async Task LeaveRoomInternalAsync()
        {
            SocketIO.SocketIOUnityClient.Instance?.LeaveRoom();
            OnRoomStateChanged?.Invoke();
            UnhookSessionEvents();
            if (_activeSession != null)
            {
                try { await _activeSession.LeaveAsync(); }
                catch { /* Safe ignore on cleanup */ }
                _activeSession = null;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            SetStage("");
        }

        private void HookSessionEvents(ISession session)
        {
            UnhookSessionEvents();
            if (session == null) return;
            _onSessionChanged = () => OnRoomStateChanged?.Invoke();
            _onPlayerJoined = _ => OnRoomStateChanged?.Invoke();
            _onPlayerLeft = _ => OnRoomStateChanged?.Invoke();
            _onRemovedFromSession = () =>
            {
                UnhookSessionEvents();
                _activeSession = null;
                LastError = "Disconnected from room (Host closed or network dropped).";
                GameStateManager.Instance?.ShowToast("Host disconnected — returning to lobby.", Palette.DangerRed);
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    NetworkManager.Singleton.Shutdown();
                if (GameStateManager.Instance != null && GameStateManager.Instance.ShellState != AppShellState.Title)
                    GameStateManager.Instance.ReturnToTitle();
                OnRoomStateChanged?.Invoke();
            };
            session.Changed += _onSessionChanged;
            session.PlayerJoined += _onPlayerJoined;
            session.PlayerHasLeft += _onPlayerLeft;
            session.RemovedFromSession += _onRemovedFromSession;
        }

        private void UnhookSessionEvents()
        {
            if (_activeSession == null) return;
            if (_onSessionChanged != null) _activeSession.Changed -= _onSessionChanged;
            if (_onPlayerJoined != null) _activeSession.PlayerJoined -= _onPlayerJoined;
            if (_onPlayerLeft != null) _activeSession.PlayerHasLeft -= _onPlayerLeft;
            if (_onRemovedFromSession != null) _activeSession.RemovedFromSession -= _onRemovedFromSession;
            _onSessionChanged = null;
            _onPlayerJoined = null;
            _onPlayerLeft = null;
            _onRemovedFromSession = null;
        }

        private async void OnDestroy()
        {
            PlayerAuthentication.OnSignedInCompleted -= HandleSignedInPrewarm;
            UnhookSessionEvents();
            if (_activeSession != null)
            {
                try { await _activeSession.LeaveAsync(); }
                catch { }
            }
        }
    }
}
