using System;
using System.Collections.Generic;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CoachingPropHunt.Networking
{
    /// <summary>
    /// NetworkBehaviour attached to NetworkPlayer.prefab (auto-spawned per connected client).
    /// Synchronizes player lobby presence, position, yaw, Prop morph state, HP, and damage RPCs
    /// across devices with zero physics/CharacterController ownership fighting.
    /// </summary>
    public sealed class NetworkPlayerState : NetworkBehaviour
    {
        public static readonly List<NetworkPlayerState> AllPeers = new List<NetworkPlayerState>();
        public static NetworkPlayerState LocalPeer { get; private set; }
        public static event Action OnPeersChanged;

        public readonly NetworkVariable<FixedString64Bytes> NetName = new NetworkVariable<FixedString64Bytes>(
            "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> NetRole = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<Vector3> NetPos = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> NetYawDeg = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> NetIsMorphed = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString64Bytes> NetPropId = new NetworkVariable<FixedString64Bytes>(
            "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> NetIsLocked = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> NetLockedYaw = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> NetHealth = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> NetIsAlive = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> NetWantedRole = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerParticipant _puppet;
        private float _syncTimer;

        public override void OnNetworkSpawn()
        {
            if (!AllPeers.Contains(this)) AllPeers.Add(this);
            if (IsOwner)
            {
                LocalPeer = this;
                string name = GameStateManager.Instance != null ? GameStateManager.Instance.Progress.playerName : "Student";
                NetName.Value = string.IsNullOrWhiteSpace(name) ? "Student" : name.Trim();
                var pref = MatchCoordinator.Instance != null ? MatchCoordinator.Instance.PreferredLocalRole : null;
                NetWantedRole.Value = pref.HasValue ? (int)pref.Value : -1;
                if (PlayerParticipant.LocalPlayer != null)
                {
                    NetRole.Value = (int)PlayerParticipant.LocalPlayer.Role;
                    NetPos.Value = PlayerParticipant.LocalPlayer.transform.position;
                    NetYawDeg.Value = PlayerParticipant.LocalPlayer.transform.eulerAngles.y;
                }
            }
            NetName.OnValueChanged += (_, next) =>
            {
                if (!IsOwner && _puppet != null)
                    _puppet.ApplyRemoteIdentity(next.ToString(), (TeamRole)Mathf.Clamp(NetRole.Value, 0, 1));
                OnPeersChanged?.Invoke();
            };
            NetRole.OnValueChanged += (_, nextRole) =>
            {
                if (!IsOwner && _puppet != null)
                    _puppet.ApplyRemoteIdentity(NetName.Value.ToString(), (TeamRole)Mathf.Clamp(nextRole, 0, 1));
                OnPeersChanged?.Invoke();
            };
            OnPeersChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            AllPeers.Remove(this);
            if (LocalPeer == this) LocalPeer = null;
            DestroyPuppet();
            OnPeersChanged?.Invoke();
        }

        public void SetWantedRole(TeamRole? wanted)
        {
            if (IsOwner && IsSpawned)
                NetWantedRole.Value = wanted.HasValue ? (int)wanted.Value : -1;
        }

        public void SetAssignedRole(TeamRole role)
        {
            if (IsOwner)
            {
                NetRole.Value = (int)role;
                _syncTimer = 0f;
            }
        }

        public void UpdateLocalName(string newName)
        {
            if (IsOwner && IsSpawned && !string.IsNullOrWhiteSpace(newName))
                NetName.Value = newName.Trim();
        }

        public void ForceImmediateSync() => _syncTimer = 0f;

        public void DestroyPuppet()
        {
            if (_puppet != null)
            {
                Destroy(_puppet.gameObject);
                _puppet = null;
            }
        }

        public PlayerParticipant EnsureRemotePuppet(int participantId)
        {
            if (IsOwner) return null;
            TeamRole role = (TeamRole)Mathf.Clamp(NetRole.Value, 0, 1);
            if (_puppet == null)
            {
                string display = NetName.Value.ToString();
                if (string.IsNullOrEmpty(display)) display = $"Player {OwnerClientId + 1}";
                var go = new GameObject($"RemotePeer_{display}_{role}");
                go.transform.position = NetPos.Value;
                _puppet = go.AddComponent<PlayerParticipant>();
                _puppet.Initialize(participantId, display, role, localHuman: false, isBot: false, initialYawDeg: NetYawDeg.Value, isRemotePuppet: true);
                _puppet.ExternalDamageHandler = (dmg, attackerName) => ApplyDamageFromRemoteRpc(dmg, attackerName);
                _puppet.ExternalStunHandler = (dur, knockDir) => ApplyStunFromRemoteRpc(dur, knockDir);
            }
            return _puppet;
        }

        [Rpc(SendTo.Owner)]
        public void ApplyDamageFromRemoteRpc(int damage, FixedString64Bytes attackerName)
        {
            var local = PlayerParticipant.LocalPlayer;
            if (local != null && local.IsAlive)
            {
                string killer = attackerName.ToString();
                local.TakeDamage(damage, string.IsNullOrEmpty(killer) ? "Hunter" : killer);
                GameStateManager.Instance?.TriggerFlash(Palette.DangerRed, 0.16f);
            }
        }

        [Rpc(SendTo.Owner)]
        public void ApplyStunFromRemoteRpc(float duration, Vector3 knockDir)
        {
            var local = PlayerParticipant.LocalPlayer;
            if (local != null && local.IsAlive)
            {
                local.ApplyStun(duration, knockDir);
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive) return;

            if (IsOwner)
            {
                var local = PlayerParticipant.LocalPlayer;
                if (local == null) return;
                _syncTimer -= Time.deltaTime;
                if (_syncTimer > 0f) return;
                _syncTimer = 0.045f; // ~22 Hz smooth network updates

                NetRole.Value = (int)local.Role;
                NetPos.Value = local.transform.position;
                NetYawDeg.Value = local.transform.eulerAngles.y;
                NetIsMorphed.Value = local.MorphCtrl != null && local.MorphCtrl.IsMorphed;
                NetPropId.Value = local.MorphCtrl != null ? local.MorphCtrl.CurrentPropId : "";
                NetIsLocked.Value = local.MorphCtrl != null && local.MorphCtrl.IsRotationLocked;
                NetLockedYaw.Value = local.MorphCtrl != null ? local.MorphCtrl.LockedYawDeg : 0f;
                NetHealth.Value = local.Health;
                NetIsAlive.Value = local.IsAlive;
            }
            else if (_puppet != null)
            {
                _puppet.ApplyRemoteMirror(
                    NetPos.Value,
                    NetYawDeg.Value,
                    NetIsLocked.Value,
                    NetLockedYaw.Value,
                    NetIsMorphed.Value,
                    NetPropId.Value.ToString(),
                    NetHealth.Value,
                    NetIsAlive.Value,
                    Time.deltaTime);
            }
        }
    }
}
