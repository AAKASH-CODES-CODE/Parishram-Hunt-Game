using System.Collections.Generic;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Props;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CoachingPropHunt.Networking
{
    /// <summary>
    /// NetworkBehaviour synchronizing authoritative round start/phase/clock/results,
    /// spatial whistle taunts, and Host-driven AI Bot snapshots across all connected clients.
    /// </summary>
    public sealed class NetworkMatchSync : NetworkBehaviour
    {
        public static NetworkMatchSync Instance { get; private set; }

        private readonly NetworkVariable<int> _netPhase = new NetworkVariable<int>(0);
        private readonly NetworkVariable<float> _netRemainingSeconds = new NetworkVariable<float>(60f);
        private readonly NetworkVariable<int> _netWinnerTeam = new NetworkVariable<int>(0);
        private readonly NetworkVariable<FixedString128Bytes> _netWinReason = new NetworkVariable<FixedString128Bytes>("");
        private readonly NetworkVariable<bool> _netMatchActive = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<int> _netMatchSeed = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> _netTargetTotal = new NetworkVariable<int>(12);
        private readonly NetworkVariable<int> _netRoundId = new NetworkVariable<int>(0);
        private readonly NetworkVariable<FixedString512Bytes> _netBotNames = new NetworkVariable<FixedString512Bytes>("");

        private readonly Dictionary<int, PlayerParticipant> _clientBotMirrors = new Dictionary<int, PlayerParticipant>();
        private string[] _cachedBotNames = System.Array.Empty<string>();
        private float _botBroadcastTimer;
        private int _localStartedRoundId = -1;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _netBotNames.OnValueChanged += (_, next) => ParseCachedBotNames(next.ToString());
            if (!string.IsNullOrEmpty(_netBotNames.Value.ToString()))
                ParseCachedBotNames(_netBotNames.Value.ToString());
        }

        private void ParseCachedBotNames(string packed)
        {
            _cachedBotNames = string.IsNullOrEmpty(packed) ? System.Array.Empty<string>() : packed.Split('|');
            foreach (var kv in _clientBotMirrors)
            {
                if (kv.Value != null && kv.Key >= 0 && kv.Key < _cachedBotNames.Length && !string.IsNullOrEmpty(_cachedBotNames[kv.Key]))
                    kv.Value.ApplyRemoteIdentity(_cachedBotNames[kv.Key], kv.Value.Role);
            }
        }

        public void PublishHostBotNames(IReadOnlyList<PlayerParticipant> bots)
        {
            if (!IsSpawned || !IsServer || bots == null) return;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bots.Count; i++)
            {
                if (i > 0) sb.Append('|');
                string n = bots[i] != null ? bots[i].DisplayName.Replace("|", "") : $"Student {i + 1}";
                if (sb.Length + n.Length >= 500) break;
                sb.Append(n);
            }
            _netBotNames.Value = sb.ToString();
        }

        public override void OnNetworkDespawn()
        {
            CleanupClientBotMirrors();
        }

        public void CleanupClientBotMirrors()
        {
            foreach (var kv in _clientBotMirrors)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _clientBotMirrors.Clear();
        }

        public void BroadcastWhistle(Vector3 worldPos, int variant)
        {
            if (IsSpawned) BroadcastWhistleRpc(worldPos, variant);
        }

        public void BroadcastBlasterShot(Vector3 start, Vector3 end, bool hitProp, Vector3 shooterPos)
        {
            if (IsSpawned) BroadcastBlasterShotRpc(start, end, hitProp, shooterPos);
        }

        public void BroadcastStunBurst(Vector3 pos)
        {
            if (IsSpawned) BroadcastStunBurstRpc(pos);
        }

        public void BroadcastSpawnDecoy(string propId, Vector3 pos, float yawDeg, int decoyNetId)
        {
            if (IsSpawned && !string.IsNullOrEmpty(propId))
                BroadcastSpawnDecoyRpc(propId, pos, yawDeg, decoyNetId);
        }

        public void BroadcastShatterDecoy(int decoyNetId, Vector3 pos)
        {
            if (IsSpawned) BroadcastShatterDecoyRpc(decoyNetId, pos);
        }

        public void BroadcastPropEliminated(string victimName, string attackerName, Vector3 worldPos)
        {
            if (IsSpawned) BroadcastPropEliminatedRpc(victimName ?? "Prop", attackerName ?? "Hunter", worldPos);
        }

        public void RequestHostStartRound(int matchSeed, int targetTotal)
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                _netRoundId.Value++;
                _netPhase.Value = (int)MatchPhase.HidePhase;
                _netRemainingSeconds.Value = GameStateManager.Instance != null ? GameStateManager.Instance.HideDuration : 60f;
                _netWinnerTeam.Value = 0;
                _netWinReason.Value = "";
                _netMatchSeed.Value = matchSeed;
                _netTargetTotal.Value = targetTotal;
                _netMatchActive.Value = true;
                _botBroadcastTimer = 0f;
                BroadcastStartOnlineRoundRpc(_netRoundId.Value, matchSeed, targetTotal);
            }
        }

        [Rpc(SendTo.Everyone)]
        public void BroadcastStartOnlineRoundRpc(int roundId, int matchSeed, int targetTotal)
        {
            _localStartedRoundId = roundId;
            CleanupClientBotMirrors();
            var coordinator = MatchCoordinator.Instance;
            if (coordinator != null) coordinator.OnlineMatchSeed = matchSeed;
            GameStateManager.Instance?.StartMatch(true, targetTotal);
        }

        [Rpc(SendTo.NotMe)]
        public void BroadcastWhistleRpc(Vector3 worldPos, int variant)
        {
            AudioSynthManager.Instance?.PlayWhistleTaunt(worldPos, variant);
            ExpandingWaveEffect.SpawnSoundRipple(worldPos + Vector3.up * 0.45f, Palette.PropAccent);
        }

        [Rpc(SendTo.NotMe)]
        public void BroadcastBlasterShotRpc(Vector3 start, Vector3 end, bool hitProp, Vector3 shooterPos)
        {
            BlasterTracerFx.Spawn(start, end, hitProp);
            AudioSynthManager.Instance?.PlayBlasterShotAt(shooterPos);
            if (hitProp) AudioSynthManager.Instance?.PlayHitSolid();
        }

        [Rpc(SendTo.NotMe)]
        public void BroadcastStunBurstRpc(Vector3 pos)
        {
            ExpandingWaveEffect.SpawnStunBurst(pos);
            AudioSynthManager.Instance?.PlayStunBashAt(pos);
        }

        [Rpc(SendTo.NotMe)]
        public void BroadcastSpawnDecoyRpc(FixedString64Bytes propId, Vector3 pos, float yawDeg, int decoyNetId)
        {
            var reg = GameStateManager.Instance != null ? GameStateManager.Instance.PropRegistry : null;
            var def = reg != null ? reg.GetById(propId.ToString()) : null;
            if (def == null || def.prefab == null) return;

            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            if (ObjectPoolManager.Instance != null)
            {
                ObjectPoolManager.Instance.AcquireDecoy(def, pos, rot, decoyNetId);
            }
            else
            {
                var decoy = Instantiate(def.prefab, pos, rot);
                decoy.name = $"Decoy_{def.propId}";
                var ip = decoy.GetComponent<InteractiveProp>();
                if (ip == null) ip = decoy.AddComponent<InteractiveProp>();
                ip.Configure(def.propId, "Decoy", decoy: true, ttlSeconds: 22f, netId: decoyNetId);
            }
            AudioSynthManager.Instance?.PlayMorphPop();
        }

        [Rpc(SendTo.Everyone)]
        public void BroadcastShatterDecoyRpc(int decoyNetId, Vector3 pos)
        {
            ExpandingWaveEffect.SpawnSoundRipple(pos + Vector3.up * 0.35f, Palette.AccentGold);
            var target = ObjectPoolManager.Instance != null ? ObjectPoolManager.Instance.FindActiveDecoyByNetId(decoyNetId) : null;
            if (target == null)
            {
                // Nearest active decoy clone fallback within 1.5m
                float bestSq = 2.25f;
                foreach (var ip in FindObjectsByType<InteractiveProp>(FindObjectsSortMode.None))
                {
                    if (!ip.IsDecoyClone) continue;
                    float dSq = (ip.transform.position - pos).sqrMagnitude;
                    if (dSq < bestSq)
                    {
                        bestSq = dSq;
                        target = ip.gameObject;
                    }
                }
            }
            if (target != null)
            {
                if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.ReleaseDecoy(target);
                else Destroy(target);
            }
        }

        [Rpc(SendTo.Everyone)]
        public void BroadcastPropEliminatedRpc(FixedString64Bytes victimName, FixedString64Bytes attackerName, Vector3 worldPos)
        {
            AudioSynthManager.Instance?.PlayEliminationSting();
            PlayerParticipant.EmitRemoteKillFeed(victimName.ToString(), attackerName.ToString(), worldPos);
        }

        [Rpc(SendTo.Server)]
        public void DamageBotOnServerRpc(int botIndex, int damage, FixedString64Bytes attackerName)
        {
            var bots = MatchCoordinator.Instance != null ? MatchCoordinator.Instance.HostBotParticipants : null;
            if (bots == null || botIndex < 0 || botIndex >= bots.Count) return;
            var bot = bots[botIndex];
            if (bot != null && bot.IsAlive)
            {
                string killer = attackerName.ToString();
                bot.TakeDamage(damage, string.IsNullOrEmpty(killer) ? "Hunter" : killer);
            }
        }

        [Rpc(SendTo.NotServer)]
        private void SyncBotsSnapshotRpc(Vector3[] positions, float[] yaws, int[] propIndices, int[] hps, byte[] packedFlags)
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive || positions == null) return;
            var reg = gsm.PropRegistry;

            for (int i = 0; i < positions.Length; i++)
            {
                byte f = packedFlags[i];
                TeamRole role = (f & 1) != 0 ? TeamRole.Hunter : TeamRole.Prop;
                bool alive = (f & 2) != 0;
                bool morphed = (f & 4) != 0;
                bool locked = (f & 8) != 0;

                if (!_clientBotMirrors.TryGetValue(i, out var mirror) || mirror == null)
                {
                    var go = new GameObject($"RemoteBot_{i}_{role}");
                    go.transform.position = positions[i];
                    mirror = go.AddComponent<PlayerParticipant>();
                    string botDisplay = (i < _cachedBotNames.Length && !string.IsNullOrEmpty(_cachedBotNames[i]))
                        ? _cachedBotNames[i]
                        : $"Student {i + 1} (Bot)";
                    mirror.Initialize(100 + i, botDisplay, role, localHuman: false, isBot: false, initialYawDeg: yaws[i], isRemotePuppet: true);
                    int capturedIdx = i;
                    mirror.ExternalDamageHandler = (dmg, attackerName) => DamageBotOnServerRpc(capturedIdx, dmg, attackerName);
                    _clientBotMirrors[i] = mirror;
                }

                string propId = "";
                if (morphed && reg != null && reg.Entries != null && propIndices[i] >= 0 && propIndices[i] < reg.Entries.Count)
                    propId = reg.Entries[propIndices[i]].propId;

                mirror.ApplyRemoteMirror(positions[i], yaws[i], locked, yaws[i], morphed, propId, hps[i], alive, Time.deltaTime * 2f);
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            if (IsServer)
            {
                _netPhase.Value = (int)gsm.Phase;
                _netRemainingSeconds.Value = gsm.PhaseRemainingSeconds;
                _netMatchActive.Value = gsm.IsMatchLive && gsm.Phase != MatchPhase.RoundOver;
                if (gsm.Phase == MatchPhase.RoundOver)
                {
                    _netWinnerTeam.Value = (int)gsm.WinningTeam;
                    _netWinReason.Value = string.IsNullOrEmpty(gsm.WinReason) ? "Round Complete" : gsm.WinReason;
                }

                // Sync Host AI bots to connected Clients at ~10 Hz when >1 humans are connected
                if (gsm.IsMatchLive && NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
                {
                    _botBroadcastTimer -= Time.deltaTime;
                    if (_botBroadcastTimer <= 0f)
                    {
                        _botBroadcastTimer = 0.10f;
                        PushBotSnapshotToClients(gsm.PropRegistry);
                    }
                }
            }
            else
            {
                // Auto-catchup for clients whose NetworkManager finished spawning slightly after host clicked Start!
                if (_netMatchActive.Value && _netRoundId.Value != _localStartedRoundId
                    && (gsm.ShellState == AppShellState.RoomLobby || gsm.ShellState == AppShellState.Title)
                    && MultiplayerRoomManager.Instance != null && MultiplayerRoomManager.Instance.IsInRoom)
                {
                    _localStartedRoundId = _netRoundId.Value;
                    var coordinator = MatchCoordinator.Instance;
                    if (coordinator != null) coordinator.OnlineMatchSeed = _netMatchSeed.Value;
                    gsm.StartMatch(true, _netTargetTotal.Value);
                    var remote = (MatchPhase)_netPhase.Value;
                    if (remote != MatchPhase.Waiting && remote != MatchPhase.RoundOver)
                        gsm.SyncPhaseFromNetwork(remote, _netRemainingSeconds.Value);
                    return;
                }

                var remotePhase = (MatchPhase)_netPhase.Value;
                if (remotePhase == MatchPhase.RoundOver && _netRoundId.Value == _localStartedRoundId
                    && gsm.Phase != MatchPhase.RoundOver && gsm.IsMatchLive)
                {
                    gsm.EndMatch((TeamRole)_netWinnerTeam.Value, _netWinReason.Value.ToString());
                }
                else if (gsm.IsMatchLive && remotePhase != MatchPhase.Waiting && remotePhase != MatchPhase.RoundOver)
                {
                    gsm.SyncPhaseFromNetwork(remotePhase, _netRemainingSeconds.Value);
                }
            }
        }

        private void PushBotSnapshotToClients(PropRegistrySO reg)
        {
            var bots = MatchCoordinator.Instance != null ? MatchCoordinator.Instance.HostBotParticipants : null;
            if (bots == null || bots.Count == 0) return;

            int n = bots.Count;
            var pos = new Vector3[n];
            var yaws = new float[n];
            var propIds = new int[n];
            var hps = new int[n];
            var flags = new byte[n];

            for (int i = 0; i < n; i++)
            {
                var b = bots[i];
                if (b == null) continue;
                pos[i] = b.transform.position;
                yaws[i] = b.transform.eulerAngles.y;
                hps[i] = b.Health;

                bool morphed = b.MorphCtrl != null && b.MorphCtrl.IsMorphed;
                propIds[i] = -1;
                if (morphed && reg != null && reg.Entries != null)
                {
                    string pid = b.MorphCtrl.CurrentPropId;
                    for (int e = 0; e < reg.Entries.Count; e++)
                    {
                        if (reg.Entries[e].propId == pid)
                        {
                            propIds[i] = e;
                            break;
                        }
                    }
                }

                byte f = 0;
                if (b.Role == TeamRole.Hunter) f |= 1;
                if (b.IsAlive) f |= 2;
                if (morphed) f |= 4;
                if (b.MorphCtrl != null && b.MorphCtrl.IsRotationLocked) f |= 8;
                flags[i] = f;
            }

            SyncBotsSnapshotRpc(pos, yaws, propIds, hps, flags);
        }
    }
}
