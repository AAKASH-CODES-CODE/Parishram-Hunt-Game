using System;
using System.Collections.Generic;
using CoachingPropHunt.AI;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Input;
using CoachingPropHunt.Props;
using UnityEngine;

namespace CoachingPropHunt.Networking
{
    /// <summary>
    /// Spawns and manages all 10–16 match participants (local human + online slots + AI Bot backfill),
    /// tracks alive team totals, assigns random coaching student/hunter roles, and cleans up cleanly.
    /// </summary>
    public sealed class MatchCoordinator : MonoBehaviour
    {
        public static MatchCoordinator Instance { get; private set; }

        private static readonly string[] BotNames =
        {
            "Aarav", "Priya", "Rohan", "Sneha", "Vikram", "Ananya", "Kabir", "Meera",
            "Aditya", "Ishita", "Karan", "Neha", "Siddharth", "Pooja", "Rahul", "Divya"
        };

        private readonly List<GameObject> _spawnedActors = new List<GameObject>();
        private readonly List<PlayerParticipant> _hostBotParticipants = new List<PlayerParticipant>();
        private TeamRole? _preferredLocalRole;
        private bool _roundRecorded;
        private float _recountTimer;

        public IReadOnlyList<PlayerParticipant> HostBotParticipants => _hostBotParticipants;
        public int OnlineMatchSeed { get; set; }

        public TeamRole? PreferredLocalRole
        {
            get => _preferredLocalRole;
            set
            {
                _preferredLocalRole = value;
                NetworkPlayerState.LocalPeer?.SetWantedRole(value);
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
        }

        private void Start()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.OnMatchRequestedStart += SpawnMatchParticipants;
                gsm.OnMatchCleanupRequested += CleanupParticipants;
                gsm.OnMatchPhaseChanged += HandlePhaseChanged;
            }
        }

        private void OnDestroy()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.OnMatchRequestedStart -= SpawnMatchParticipants;
                gsm.OnMatchCleanupRequested -= CleanupParticipants;
                gsm.OnMatchPhaseChanged -= HandlePhaseChanged;
            }
        }

        public void SpawnMatchParticipants()
        {
            CleanupParticipants();
            _roundRecorded = false;

            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            int total = Mathf.Clamp(gsm.TargetTotalPlayers, 4, 16);
            int hunterSlots = Mathf.Max(1, Mathf.RoundToInt(total * 0.25f));

            bool isNetOnline = gsm.IsOnlineSession
                && Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening;

            if (isNetOnline)
            {
                bool isHost = Unity.Netcode.NetworkManager.Singleton.IsServer;
                int localSlot = 0;
                ulong localCid = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
                var sortedIds = new List<ulong>();
                bool anyHumanWantsHunter = _preferredLocalRole == TeamRole.Hunter;
                foreach (var peer in NetworkPlayerState.AllPeers)
                {
                    if (peer == null) continue;
                    if (!sortedIds.Contains(peer.OwnerClientId)) sortedIds.Add(peer.OwnerClientId);
                    if (peer.NetWantedRole.Value == (int)TeamRole.Hunter) anyHumanWantsHunter = true;
                }
                sortedIds.Sort();
                int idxFound = sortedIds.IndexOf(localCid);
                if (idxFound >= 0) localSlot = idxFound;

                TeamRole localRole;
                if (sortedIds.Count >= 2 && !anyHumanWantsHunter)
                {
                    int designatedHunterSlot = Mathf.Abs(OnlineMatchSeed) % sortedIds.Count;
                    localRole = localSlot == designatedHunterSlot ? TeamRole.Hunter : TeamRole.Prop;
                    if (localRole == TeamRole.Hunter && _preferredLocalRole == TeamRole.Prop)
                        gsm.ShowToast("All picked Prop — you were assigned HUNTER!", Palette.HunterAccent);
                }
                else if (_preferredLocalRole.HasValue)
                {
                    localRole = _preferredLocalRole.Value;
                }
                else if (sortedIds.Count >= 2)
                {
                    int designatedHunterSlot = Mathf.Abs(OnlineMatchSeed) % sortedIds.Count;
                    localRole = localSlot == designatedHunterSlot ? TeamRole.Hunter : TeamRole.Prop;
                }
                else
                {
                    localRole = TeamRole.Prop;
                }

                if (NetworkPlayerState.LocalPeer != null)
                {
                    NetworkPlayerState.LocalPeer.SetWantedRole(_preferredLocalRole);
                    NetworkPlayerState.LocalPeer.SetAssignedRole(localRole);
                }

                string humanName = string.IsNullOrWhiteSpace(gsm.Progress.playerName) ? "You" : gsm.Progress.playerName;
                SpawnParticipant(localSlot, humanName, localRole, localHuman: true, isBot: false);
                NetworkPlayerState.LocalPeer?.ForceImmediateSync();

                if (isHost)
                {
                    int humans = Mathf.Max(1, sortedIds.Count);
                    int propIdx = humans;
                    int botsNeeded = Mathf.Max(0, total - humans);
                    // Online Multiplayer rule: AI Bots NEVER play Hunter (always hiding Props!)
                    for (int b = 0; b < botsNeeded; b++)
                    {
                        string botName = $"{BotNames[(b + humans) % BotNames.Length]} (Bot)";
                        int sp = propIdx++;
                        var botP = SpawnParticipant(sp, botName, TeamRole.Prop, localHuman: false, isBot: true);
                        _hostBotParticipants.Add(botP);
                    }
                    NetworkMatchSync.Instance?.PublishHostBotNames(_hostBotParticipants);
                }
                RecountAliveTeams();
                return;
            }

            // Offline Single-Player + AI Bots
            TeamRole offlineRole = _preferredLocalRole ?? (UnityEngine.Random.value < 0.28f ? TeamRole.Hunter : TeamRole.Prop);
            int botsToAssignHunter = offlineRole == TeamRole.Hunter ? hunterSlots - 1 : hunterSlots;

            string offlineName = string.IsNullOrWhiteSpace(gsm.Progress.playerName) ? "You" : gsm.Progress.playerName;
            SpawnParticipant(0, offlineName, offlineRole, localHuman: true, isBot: false);

            int oPropIdx = offlineRole == TeamRole.Prop ? 1 : 0;
            int oHunterIdx = offlineRole == TeamRole.Hunter ? 1 : 0;

            for (int i = 1; i < total; i++)
            {
                TeamRole r;
                if (botsToAssignHunter > 0 && (i % 4 == 1 || (total - i) <= botsToAssignHunter))
                {
                    r = TeamRole.Hunter;
                    botsToAssignHunter--;
                }
                else
                {
                    r = TeamRole.Prop;
                }

                string botName = $"{BotNames[i % BotNames.Length]} (Bot)";
                int spawnIdx = r == TeamRole.Hunter ? oHunterIdx++ : oPropIdx++;
                SpawnParticipant(spawnIdx, botName, r, localHuman: false, isBot: true);
            }

            RecountAliveTeams();
        }

        private void Update()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsOnlineSession || !gsm.IsMatchLive) return;
            for (int i = 0; i < NetworkPlayerState.AllPeers.Count; i++)
            {
                var peer = NetworkPlayerState.AllPeers[i];
                if (peer != null && !peer.IsOwner)
                    peer.EnsureRemotePuppet(200 + i);
            }
            _recountTimer -= Time.deltaTime;
            if (_recountTimer <= 0f)
            {
                _recountTimer = 0.35f;
                RecountAliveTeams();
            }
        }

        private PlayerParticipant SpawnParticipant(int spawnIndex, string playerName, TeamRole role, bool localHuman, bool isBot)
        {
            var gsm = GameStateManager.Instance;
            Transform sp = role == TeamRole.Hunter ? gsm.GetHunterSpawn(spawnIndex) : gsm.GetPropSpawn(spawnIndex);
            Vector3 pos = sp != null ? sp.position : (role == TeamRole.Hunter ? new Vector3(0.4f, 0.1f, 14.8f) : new Vector3(-1.5f, 0.1f, 6.0f));
            float yaw = sp != null ? sp.eulerAngles.y : 0f;

            var go = new GameObject(localHuman ? $"LocalPlayer_{role}" : $"Participant_{playerName}_{role}");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var participant = go.AddComponent<PlayerParticipant>();
            participant.Initialize(_spawnedActors.Count + 1, playerName, role, localHuman, isBot, yaw);
            participant.OnEliminated += _ => RecountAliveTeams();

            if (localHuman)
            {
                go.AddComponent<MobileAndDesktopInputDriver>();
            }
            else if (isBot)
            {
                var brain = go.AddComponent<BotBrainController>();
                brain.ActivateBot();
            }

            _spawnedActors.Add(go);
            return participant;
        }

        public void CleanupParticipants()
        {
            PropTauntAndAbility.CleanupAllDecoys();
            NetworkMatchSync.Instance?.CleanupClientBotMirrors();
            for (int i = 0; i < NetworkPlayerState.AllPeers.Count; i++)
                NetworkPlayerState.AllPeers[i]?.DestroyPuppet();
            for (int i = 0; i < _spawnedActors.Count; i++)
            {
                if (_spawnedActors[i] != null) Destroy(_spawnedActors[i]);
            }
            _spawnedActors.Clear();
            _hostBotParticipants.Clear();
        }

        private void RecountAliveTeams()
        {
            int props = 0;
            int hunters = 0;
            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive) continue;
                if (p.Role == TeamRole.Prop) props++;
                else hunters++;
            }
            GameStateManager.Instance?.UpdateAliveCounts(props, hunters);
        }

        private void HandlePhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.RoundOver && !_roundRecorded)
            {
                _roundRecorded = true;
                var local = PlayerParticipant.LocalPlayer;
                if (local != null && GameStateManager.Instance != null)
                {
                    bool propWin = GameStateManager.Instance.WinningTeam == TeamRole.Prop;
                    bool survived = local.Role == TeamRole.Prop && local.IsAlive && propWin;
                    GameStateManager.Instance.RecordRoundResult(local.Score, survived, local.Eliminations);
                }
            }
        }
    }
}
