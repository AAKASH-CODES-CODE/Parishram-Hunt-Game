using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CoachingPropHunt.Networking.SocketIO
{
    /// <summary>
    /// High-performance, zero-dependency C# WebSocket & Socket.IO Engine.IO v4 client.
    /// Fully compatible with Unity 6 (6000.3.28f1), Android ARM64 IL2CPP, and Render Free Tier.
    /// </summary>
    public sealed class SocketIOUnityClient : MonoBehaviour
    {
        public static SocketIOUnityClient Instance { get; private set; }

        [Header("Backend Configuration")]
        [SerializeField] private string serverUrl = "https://parishram-hunt-server.onrender.com";
        [SerializeField] private bool autoConnect = true;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, List<Action<string>>> _eventHandlers = new Dictionary<string, List<Action<string>>>(StringComparer.Ordinal);

        public bool IsConnected { get; private set; }
        public string SocketId { get; private set; } = "";
        public string CurrentRoomCode { get; private set; } = "";
        public RoomStateData LastRoomState { get; private set; }

        // Strongly-typed Unity Events
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<RoomStateData> OnRoomStateUpdated;
        public event Action<MatchStartedData> OnMatchStarted;
        public event Action<ChatMessageData> OnNewChat;
        public event Action<List<OpenRoomSummaryData>> OnOpenRoomsReceived;
        public event Action<PlayerDamagedData> OnPlayerDamaged;
        public event Action<KillCamTriggerData> OnTriggerKillCam;
        public event Action<string> OnKicked;
        public event Action<string> OnErrorReceived;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance == null)
            {
                var go = new GameObject("SocketIOUnityClient");
                Instance = go.AddComponent<SocketIOUnityClient>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Register standard server event listeners
            RegisterInternalEvents();
        }

        private async void Start()
        {
            if (autoConnect)
            {
                await ConnectAsync();
            }
        }

        private void Update()
        {
            // Dispatch queued network events on Unity main thread safely
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SocketIO] Main thread dispatch error: {e}");
                }
            }
        }

        public async Task<bool> ConnectAsync()
        {
            if (IsConnected || (_ws != null && _ws.State == WebSocketState.Open))
                return true;

            try
            {
                DisconnectInternal();

                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();

                // Format WebSocket URL: convert https:// to wss:// and append Engine.IO v4 parameters
                string wsUrl = serverUrl.Trim().TrimEnd('/');
                if (wsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    wsUrl = "wss://" + wsUrl.Substring(8);
                else if (wsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    wsUrl = "ws://" + wsUrl.Substring(7);

                string fullUri = $"{wsUrl}/socket.io/?EIO=4&transport=websocket";
                Debug.Log($"[SocketIO] Connecting to: {fullUri}");

                var uri = new Uri(fullUri);
                await _ws.ConnectAsync(uri, _cts.Token);

                // Start background receive loop
                _ = ReceiveLoopAsync(_cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SocketIO] Connection failed: {ex.Message}");
                EnqueueMainThread(() => OnErrorReceived?.Invoke($"Connection failed: {ex.Message}"));
                return false;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var ms = new MemoryStream();

            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                            HandleDisconnect();
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string message = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessEngineIOPacket(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SocketIO] Receive loop terminated: {ex.Message}");
            }
            finally
            {
                HandleDisconnect();
            }
        }

        private void ProcessEngineIOPacket(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;

            // Engine.IO packet types:
            // 0: open (handshake)
            // 2: ping
            // 3: pong
            // 4: message (Socket.IO packet)
            char type = raw[0];
            string content = raw.Length > 1 ? raw.Substring(1) : "";

            switch (type)
            {
                case '0': // Handshake open packet
                    // Server returned handshake JSON, send Socket.IO CONNECT packet ("40")
                    SendRaw("40");
                    break;

                case '2': // Server PING -> reply with PONG ("3")
                    SendRaw("3");
                    break;

                case '3': // PONG response
                    break;

                case '4': // Socket.IO Packet
                    ProcessSocketIOPacket(content);
                    break;
            }
        }

        private void ProcessSocketIOPacket(string packet)
        {
            if (string.IsNullOrEmpty(packet)) return;

            char sioType = packet[0];
            string payload = packet.Length > 1 ? packet.Substring(1) : "";

            switch (sioType)
            {
                case '0': // CONNECT acknowledgment
                    IsConnected = true;
                    Debug.Log("[SocketIO] Successfully connected to Authoritative Server!");
                    EnqueueMainThread(() => OnConnected?.Invoke());
                    break;

                case '1': // DISCONNECT
                    HandleDisconnect();
                    break;

                case '2': // EVENT: 42["event_name", {payload}]
                    ParseAndDispatchEvent(payload);
                    break;

                case '4': // ERROR
                    Debug.LogWarning($"[SocketIO] Server Error: {payload}");
                    EnqueueMainThread(() => OnErrorReceived?.Invoke(payload));
                    break;
            }
        }

        private void ParseAndDispatchEvent(string jsonArray)
        {
            try
            {
                // Format: ["eventName", payloadObject] or ["eventName"]
                if (!jsonArray.StartsWith("[")) return;

                int firstQuote = jsonArray.IndexOf('"');
                if (firstQuote < 0) return;
                int secondQuote = jsonArray.IndexOf('"', firstQuote + 1);
                if (secondQuote < 0) return;

                string eventName = jsonArray.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

                // Find payload after comma
                int comma = jsonArray.IndexOf(',', secondQuote);
                string payloadJson = "";
                if (comma >= 0)
                {
                    int lastBracket = jsonArray.LastIndexOf(']');
                    if (lastBracket > comma)
                    {
                        payloadJson = jsonArray.Substring(comma + 1, lastBracket - comma - 1).Trim();
                    }
                }

                // Dispatch to registered callbacks
                if (_eventHandlers.TryGetValue(eventName, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var handler = list[i];
                        EnqueueMainThread(() => handler?.Invoke(payloadJson));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SocketIO] Failed to parse event packet: {e.Message}");
            }
        }

        private void RegisterInternalEvents()
        {
            On("room_state_update", json =>
            {
                try
                {
                    RoomStateData data = null;
                    var env = JsonUtility.FromJson<RoomStateEnvelope>(json);
                    if (env != null && env.room != null && !string.IsNullOrEmpty(env.room.room_code))
                    {
                        data = env.room;
                        if (!string.IsNullOrEmpty(env.notification))
                            data.notification = env.notification;
                    }
                    else
                    {
                        data = JsonUtility.FromJson<RoomStateData>(json);
                    }

                    if (data != null && !string.IsNullOrEmpty(data.room_code))
                    {
                        LastRoomState = data;
                        CurrentRoomCode = data.room_code;
                        Debug.Log($"[SocketIO] room_state_update received: Code={data.room_code}, Players={data.players.Count}");
                        OnRoomStateUpdated?.Invoke(data);
                    }
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing room_state_update: {e}"); }
            });

            On("room_created", json =>
            {
                try
                {
                    var res = JsonUtility.FromJson<RoomCreatedData>(json);
                    if (res != null && !string.IsNullOrEmpty(res.room_code))
                    {
                        CurrentRoomCode = res.room_code;
                        Debug.Log($"[SocketIO] room_created received: Code={res.room_code}");
                        if (LastRoomState == null)
                            LastRoomState = new RoomStateData { room_code = res.room_code };
                        else
                            LastRoomState.room_code = res.room_code;
                        OnRoomStateUpdated?.Invoke(LastRoomState);
                    }
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing room_created: {e}"); }
            });

            On("room_joined", json =>
            {
                try
                {
                    var res = JsonUtility.FromJson<RoomCreatedData>(json);
                    if (res != null && !string.IsNullOrEmpty(res.room_code))
                    {
                        CurrentRoomCode = res.room_code;
                        Debug.Log($"[SocketIO] room_joined received: Code={res.room_code}");
                        if (LastRoomState == null)
                            LastRoomState = new RoomStateData { room_code = res.room_code };
                        else
                            LastRoomState.room_code = res.room_code;
                        OnRoomStateUpdated?.Invoke(LastRoomState);
                    }
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing room_joined: {e}"); }
            });

            On("match_started", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<MatchStartedData>(json);
                    if (data != null) OnMatchStarted?.Invoke(data);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing match_started: {e}"); }
            });

            On("new_chat", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<ChatMessageData>(json);
                    if (data != null) OnNewChat?.Invoke(data);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing new_chat: {e}"); }
            });

            On("open_rooms_list", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<OpenRoomsListWrapper>(json);
                    if (data != null && data.rooms != null)
                    {
                        OnOpenRoomsReceived?.Invoke(data.rooms);
                    }
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing open_rooms_list: {e}"); }
            });

            On("player_damaged", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<PlayerDamagedData>(json);
                    if (data != null) OnPlayerDamaged?.Invoke(data);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing player_damaged: {e}"); }
            });

            On("trigger_killcam", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<KillCamTriggerData>(json);
                    if (data != null) OnTriggerKillCam?.Invoke(data);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing trigger_killcam: {e}"); }
            });

            On("kicked", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<KickedData>(json);
                    string reason = data != null && !string.IsNullOrEmpty(data.message) ? data.message : "You were kicked by the host.";
                    OnKicked?.Invoke(reason);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing kicked: {e}"); }
            });

            On("error_response", json =>
            {
                try
                {
                    var data = JsonUtility.FromJson<ErrorResponseData>(json);
                    string msg = data != null ? data.message : json;
                    OnErrorReceived?.Invoke(msg);
                }
                catch (Exception e) { Debug.LogError($"[SocketIO] Error parsing error_response: {e}"); }
            });
        }

        public void On(string eventName, Action<string> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null) return;
            if (!_eventHandlers.ContainsKey(eventName))
                _eventHandlers[eventName] = new List<Action<string>>();
            _eventHandlers[eventName].Add(callback);
        }

        public void Off(string eventName, Action<string> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null) return;
            if (_eventHandlers.TryGetValue(eventName, out var list))
            {
                list.Remove(callback);
            }
        }

        public void Emit(string eventName, string jsonPayload = "{}")
        {
            if (!IsConnected || _ws == null || _ws.State != WebSocketState.Open)
            {
                Debug.LogWarning($"[SocketIO] Cannot emit '{eventName}': WebSocket not connected.");
                return;
            }

            // Socket.IO event format: 42["eventName", payload]
            string packet = $"42[\"{eventName}\",{jsonPayload}]";
            SendRaw(packet);
        }

        private async void SendRaw(string raw)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(raw);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SocketIO] Send error: {ex.Message}");
            }
        }

        private void HandleDisconnect()
        {
            if (IsConnected)
            {
                IsConnected = false;
                Debug.Log("[SocketIO] Disconnected from server.");
                EnqueueMainThread(() => OnDisconnected?.Invoke());
            }
        }

        private void DisconnectInternal()
        {
            try
            {
                _cts?.Cancel();
                _ws?.Dispose();
            }
            catch { }
            finally
            {
                _ws = null;
                _cts = null;
                IsConnected = false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client leaving", CancellationToken.None);
                }
                catch { }
            }
            DisconnectInternal();
        }

        public void EnqueueMainThread(Action action)
        {
            if (action != null) _mainThreadQueue.Enqueue(action);
        }

        // ----------------------------------------------------------------------
        // High-level Game Actions
        // ----------------------------------------------------------------------

        [Serializable]
        private class CreateRoomRequest
        {
            public string player_name;
            public string room_name;
            public int max_players;
            public bool is_private;
        }

        public async void CreateRoom(string playerName, string roomName, int maxPlayers = 8, bool isPrivate = false)
        {
            if (!IsConnected)
            {
                Debug.Log("[SocketIO] CreateRoom called while not connected. Connecting first...");
                bool ok = await ConnectAsync();
                if (!ok)
                {
                    Debug.LogError("[SocketIO] Failed to connect to server for CreateRoom.");
                    EnqueueMainThread(() => OnErrorReceived?.Invoke("Could not connect to online server. Check internet connection!"));
                    return;
                }
                int waitAttempts = 0;
                while (!IsConnected && waitAttempts < 25)
                {
                    await Task.Delay(100);
                    waitAttempts++;
                }
            }

            var req = new CreateRoomRequest
            {
                player_name = playerName,
                room_name = string.IsNullOrEmpty(roomName) ? $"{playerName}'s Room" : roomName,
                max_players = maxPlayers,
                is_private = isPrivate
            };
            Emit("create_room", JsonUtility.ToJson(req));
        }

        [Serializable]
        private class JoinRoomRequest
        {
            public string player_name;
            public string room_code;
        }

        public async void JoinRoom(string playerName, string roomCode)
        {
            if (!IsConnected)
            {
                Debug.Log("[SocketIO] JoinRoom called while not connected. Connecting first...");
                bool ok = await ConnectAsync();
                if (!ok)
                {
                    Debug.LogError("[SocketIO] Failed to connect to server for JoinRoom.");
                    EnqueueMainThread(() => OnErrorReceived?.Invoke("Could not connect to online server. Check internet connection!"));
                    return;
                }
                int waitAttempts = 0;
                while (!IsConnected && waitAttempts < 25)
                {
                    await Task.Delay(100);
                    waitAttempts++;
                }
            }

            var req = new JoinRoomRequest
            {
                player_name = playerName,
                room_code = roomCode.Trim().ToUpperInvariant()
            };
            Emit("join_room", JsonUtility.ToJson(req));
        }

        public void LeaveRoom()
        {
            Emit("leave_room", "{}");
            CurrentRoomCode = "";
            LastRoomState = null;
        }

        [Serializable]
        private class ToggleReadyRequest
        {
            public bool is_ready;
        }

        public void ToggleReady(bool isReady)
        {
            var req = new ToggleReadyRequest { is_ready = isReady };
            Emit("toggle_ready", JsonUtility.ToJson(req));
        }

        [Serializable]
        private class SetRoleRequest
        {
            public string preferred_role;
        }

        public void SetRole(string role)
        {
            var req = new SetRoleRequest { preferred_role = role };
            Emit("set_role", JsonUtility.ToJson(req));
        }

        public void StartMatch()
        {
            Emit("start_match", "{}");
        }

        [Serializable]
        private class KickPlayerRequest
        {
            public string target_sid;
        }

        public void KickPlayer(string targetSid)
        {
            var req = new KickPlayerRequest { target_sid = targetSid };
            Emit("kick_player", JsonUtility.ToJson(req));
        }

        [Serializable]
        private class SendChatRequest
        {
            public string message;
        }

        public void SendChat(string message)
        {
            var req = new SendChatRequest { message = message };
            Emit("send_chat", JsonUtility.ToJson(req));
        }

        public void GetOpenRooms()
        {
            Emit("get_open_rooms", "{}");
        }

        [Serializable]
        private class PropHitRequest
        {
            public string target_sid;
            public int damage;
            public float[] muzzle_pos;
            public float[] impact_pos;
        }

        public void ReportPropHit(string targetSid, int damage, Vector3 muzzlePos, Vector3 impactPos)
        {
            var req = new PropHitRequest
            {
                target_sid = targetSid,
                damage = damage,
                muzzle_pos = new float[] { muzzlePos.x, muzzlePos.y, muzzlePos.z },
                impact_pos = new float[] { impactPos.x, impactPos.y, impactPos.z }
            };
            Emit("prop_hit", JsonUtility.ToJson(req));
        }

        private void OnDestroy()
        {
            DisconnectInternal();
        }
    }
}
