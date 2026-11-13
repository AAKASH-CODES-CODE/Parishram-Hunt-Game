using System;
using CoachingPropHunt.Core;
using CoachingPropHunt.Props;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoachingPropHunt.Gameplay
{
    /// <summary>
    /// Prop-team active gameplay tools:
    /// - Spatial 3D Whistle Taunt (periodic auto-whistle every ~20s in Hunt Phase + manual bonus whistle)
    /// - Decoy Clone ability (spawns a real-looking duplicate prop with identical materials for 22s)
    /// </summary>
    public sealed class PropTauntAndAbility : MonoBehaviour
    {
        [SerializeField] private float whistleCooldown = 8.0f;
        [SerializeField] private float decoyCooldown = 22.0f;
        [SerializeField] private float stunCooldown = 14.0f;
        [SerializeField] private float stunRange = 3.25f;
        [SerializeField] private float stunDuration = 2.4f;
        [SerializeField] private float autoWhistleIntervalMin = 17f;
        [SerializeField] private float autoWhistleIntervalMax = 24f;

        private static readonly System.Collections.Generic.List<GameObject> ActiveDecoys = new System.Collections.Generic.List<GameObject>();

        private float _nextManualWhistleTime;
        private float _nextDecoyTime;
        private float _nextStunTime;
        private float _nextAutoWhistleTime;
        private int _whistleVariant;

        public static void CleanupAllDecoys()
        {
            ObjectPoolManager.Instance?.ReleaseAllActiveDecoys();
            for (int i = ActiveDecoys.Count - 1; i >= 0; i--)
            {
                if (ActiveDecoys[i] != null)
                {
                    if (ObjectPoolManager.Instance != null)
                        ObjectPoolManager.Instance.ReleaseDecoy(ActiveDecoys[i]);
                    else
                        Destroy(ActiveDecoys[i]);
                }
            }
            ActiveDecoys.Clear();
        }

        public float WhistleRemainingCooldown => Mathf.Max(0f, _nextManualWhistleTime - Time.time);
        public float DecoyRemainingCooldown => Mathf.Max(0f, _nextDecoyTime - Time.time);
        public float StunRemainingCooldown => Mathf.Max(0f, _nextStunTime - Time.time);

        public event Action<int> OnManualTauntScored;

        public void ResetForMatch()
        {
            _nextManualWhistleTime = 0f;
            _nextDecoyTime = 0f;
            _nextStunTime = 0f;
            _nextAutoWhistleTime = Time.time + UnityEngine.Random.Range(autoWhistleIntervalMin, autoWhistleIntervalMax);
        }

        public void TickAutoWhistle(bool isLocalOrBotAliveProp)
        {
            if (!isLocalOrBotAliveProp) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Phase != MatchPhase.HuntPhase) return;
            if (Time.time < _nextAutoWhistleTime) return;

            _nextAutoWhistleTime = Time.time + UnityEngine.Random.Range(autoWhistleIntervalMin, autoWhistleIntervalMax);
            EmitWhistleAtPosition(transform.position);
        }

        public bool TryManualWhistle(bool showLocalHint)
        {
            if (Time.time < _nextManualWhistleTime) return false;
            _nextManualWhistleTime = Time.time + whistleCooldown;
            _nextAutoWhistleTime = Time.time + UnityEngine.Random.Range(autoWhistleIntervalMin, autoWhistleIntervalMax);
            EmitWhistleAtPosition(transform.position);
            OnManualTauntScored?.Invoke(15);
            if (showLocalHint)
                GameStateManager.Instance?.ShowToast("Whistle Taunt! (+15 Stealth Points)", Palette.PropAccent);
            return true;
        }

        public bool TryStunHunter(bool showLocalHint)
        {
            if (Time.time < _nextStunTime) return false;
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Phase != MatchPhase.HuntPhase)
            {
                if (showLocalHint) gsm?.ShowToast("Wait until Hunt Phase to stun Hunters!", Palette.TextMuted);
                return false;
            }

            PlayerParticipant closest = null;
            float bestDist = stunRange;
            Vector3 myEye = transform.position + Vector3.up * 0.55f;

            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive || p.Role != TeamRole.Hunter) continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d > bestDist) continue;
                Vector3 targetPt = p.transform.position + Vector3.up * 1.1f;
                if (Physics.Linecast(myEye, targetPt, out RaycastHit wallHit, ~0, QueryTriggerInteraction.Ignore))
                {
                    var hitPlayer = wallHit.collider.GetComponentInParent<PlayerParticipant>();
                    if (hitPlayer != p) continue;
                }
                bestDist = d;
                closest = p;
            }

            if (closest == null)
            {
                if (showLocalHint) gsm.ShowToast($"Move closer to a Hunter (< {stunRange:0.0}m) to Stun!", Palette.TextMuted);
                return false;
            }

            _nextStunTime = Time.time + stunCooldown;
            Vector3 pushDir = (closest.transform.position - transform.position);
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.001f) pushDir = transform.forward;
            closest.ApplyStun(stunDuration, pushDir.normalized);

            Vector3 burstPos = transform.position + Vector3.up * 0.65f;
            ExpandingWaveEffect.SpawnStunBurst(burstPos);
            AudioSynthManager.Instance?.PlayStunBashAt(transform.position);
            Networking.NetworkMatchSync.Instance?.BroadcastStunBurst(burstPos);
            OnManualTauntScored?.Invoke(35);
            if (showLocalHint)
                gsm.ShowToast($"STUNNED {closest.DisplayName}! Run away! (+35)", Palette.AccentGold);
            return true;
        }

        public bool TrySpawnDecoy(string currentPropId, bool showLocalHint)
        {
            if (Time.time < _nextDecoyTime) return false;
            var gsm = GameStateManager.Instance;
            var reg = gsm != null ? gsm.PropRegistry : null;
            if (reg == null) return false;

            var def = !string.IsNullOrEmpty(currentPropId) ? reg.GetById(currentPropId) : reg.GetRandomNonLarge();
            if (def == null || def.prefab == null) return false;

            _nextDecoyTime = Time.time + decoyCooldown;
            Vector3 startChest = transform.position + Vector3.up * 0.55f;
            Vector3 desiredOffset = transform.right * 0.72f + transform.forward * 0.35f;
            Vector3 targetXZ = startChest + desiredOffset;
            if (Physics.Linecast(startChest, targetXZ, out RaycastHit wallHit, ~0, QueryTriggerInteraction.Ignore))
                targetXZ = wallHit.point - desiredOffset.normalized * 0.30f;

            Vector3 placePos = new Vector3(targetXZ.x, transform.position.y, targetXZ.z);
            if (Physics.Raycast(new Vector3(targetXZ.x, startChest.y, targetXZ.z), Vector3.down, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                placePos = hit.point;

            float decoyYaw = transform.eulerAngles.y + UnityEngine.Random.Range(-18f, 18f);
            Quaternion decoyRot = Quaternion.Euler(0f, decoyYaw, 0f);
            int decoyNetId = ObjectPoolManager.Instance != null
                ? ObjectPoolManager.Instance.AllocateDecoyNetId()
                : UnityEngine.Random.Range(10000, 999999);

            GameObject decoy;
            if (ObjectPoolManager.Instance != null)
            {
                decoy = ObjectPoolManager.Instance.AcquireDecoy(def, placePos, decoyRot, decoyNetId);
            }
            else
            {
                decoy = Instantiate(def.prefab, placePos, decoyRot);
                decoy.name = $"Decoy_{def.propId}";
                ActiveDecoys.Add(decoy);
                foreach (var mr in decoy.GetComponentsInChildren<Renderer>(true))
                {
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                }
                var ip = decoy.GetComponent<InteractiveProp>();
                if (ip == null) ip = decoy.AddComponent<InteractiveProp>();
                ip.Configure(def.propId, "Decoy", decoy: true, ttlSeconds: 22f, netId: decoyNetId);
            }

            Networking.NetworkMatchSync.Instance?.BroadcastSpawnDecoy(def.propId, placePos, decoyYaw, decoyNetId);
            AudioSynthManager.Instance?.PlayMorphPop();
            if (showLocalHint)
                gsm?.ShowToast($"Decoy '{def.displayName}' placed!", Palette.PropAccent);
            return true;
        }

        private void EmitWhistleAtPosition(Vector3 worldPos)
        {
            _whistleVariant++;
            AudioSynthManager.Instance?.PlayWhistleTaunt(worldPos, _whistleVariant);
            ExpandingWaveEffect.SpawnSoundRipple(worldPos + Vector3.up * 0.45f, Palette.PropAccent);
            AI.BotBrainController.NotifyNearestHunterOfWhistle(worldPos);
            Networking.NetworkMatchSync.Instance?.BroadcastWhistle(worldPos, _whistleVariant);
        }
    }
}
