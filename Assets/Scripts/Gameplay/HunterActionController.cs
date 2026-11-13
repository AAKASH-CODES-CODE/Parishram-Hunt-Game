using System;
using CoachingPropHunt.Core;
using CoachingPropHunt.Props;
using Unity.Cinemachine;
using UnityEngine;

namespace CoachingPropHunt.Gameplay
{
    /// <summary>
    /// Handles Hunter strike/tag raycasts, Wrong-Hit (-10 HP) penalty, pooled hit spark VFX,
    /// Cinemachine impulse feedback, and the Hunter Radar Pulse team ability.
    /// </summary>
    public sealed class HunterActionController : MonoBehaviour
    {
        [SerializeField] private float strikeRange = 11.5f;
        [SerializeField] private float strikeCooldown = 0.48f;
        [SerializeField] private int hitDamage = 25; // Balanced: 100 HP max / 25 dmg = 4 shots to eliminate
        [SerializeField] private int wrongHitPenalty = 10;
        [SerializeField] private float radarCooldown = 20.0f;
        [SerializeField] private float radarRadius = 14.0f;

        private PlayerParticipant _owner;
        private CinemachineImpulseSource _impulse;
        private ParticleSystem _hitSparks;
        private float _nextStrikeTime;
        private float _nextRadarTime;

        public float StrikeRemainingCooldown => Mathf.Max(0f, _nextStrikeTime - Time.time);
        public float StrikeCooldownDuration => strikeCooldown;
        public float RadarRemainingCooldown => Mathf.Max(0f, _nextRadarTime - Time.time);
        public float MaxBlasterRange => strikeRange;

        public event Action<int> OnHunterScored;
        public event Action<string> OnRadarResult;
        public event Action<bool> OnStrikeHitFeedback;

        private void Awake()
        {
            _owner = GetComponent<PlayerParticipant>();
            _impulse = GetComponent<CinemachineImpulseSource>();
            if (_impulse == null) _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            BuildHitSparks();
        }

        public void ResetForMatch()
        {
            _nextStrikeTime = 0f;
            _nextRadarTime = 0f;
        }

        public PlayerParticipant FindAimAssistedProp(Ray aimRay, out Vector3 assistPoint)
        {
            assistPoint = Vector3.zero;
            if (_owner == null) return null;

            PlayerParticipant best = null;
            Vector3 bestPt = Vector3.zero;
            float bestPerp = float.MaxValue;

            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                if (p == null || p == _owner || !p.IsAlive || p.Role != TeamRole.Prop) continue;

                Vector3 targetPt = p.transform.position + Vector3.up * 0.72f;
                Vector3 toProp = targetPt - aimRay.origin;
                float dist = toProp.magnitude;
                if (dist > strikeRange || dist < 0.05f) continue;

                float fwdProj = Vector3.Dot(toProp, aimRay.direction);
                if (fwdProj <= 0f) continue;

                bool isMoving = p.IsActivelyMoving || (Time.time - p.LastActiveMoveTime < 0.60f);
                float maxPerp = isMoving ? 1.15f : 0.85f;
                float minDot = isMoving ? 0.945f : 0.962f;

                float dot = fwdProj / dist;
                float perp = Vector3.Distance(toProp, aimRay.direction * fwdProj);
                if (dot < minDot || perp > maxPerp) continue;
                if (!HasClearLineOfSightTo(aimRay.origin, targetPt, p)) continue;

                float weighted = perp - (isMoving ? 0.14f : 0f);
                if (weighted < bestPerp)
                {
                    bestPerp = weighted;
                    best = p;
                    bestPt = targetPt;
                }
            }

            assistPoint = bestPt;
            return best;
        }

        private bool HasClearLineOfSightTo(Vector3 origin, Vector3 targetPt, PlayerParticipant targetProp)
        {
            Vector3 dir = targetPt - origin;
            float dist = dir.magnitude;
            if (dist < 0.05f) return true;
            var hits = Physics.RaycastAll(origin, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (targetProp != null && c.transform.IsChildOf(targetProp.transform)) return true;
                if (c.GetComponentInParent<PlayerParticipant>() != null) continue;
                if (c.GetComponentInParent<InteractiveProp>() != null) continue;
                return false;
            }
            return true;
        }

        public bool TryStrike(Ray aimRay)
        {
            if (_owner == null || !_owner.IsAlive || _owner.Role != TeamRole.Hunter || _owner.IsStunned) return false;
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Phase != MatchPhase.HuntPhase) return false;
            if (Time.time < _nextStrikeTime) return false;

            _nextStrikeTime = Time.time + strikeCooldown;
            _owner.AvatarVisual?.TriggerHunterSwing();
            if (_owner.IsLocalHuman) AudioSynthManager.Instance?.PlayBlasterShot();
            else AudioSynthManager.Instance?.PlayBlasterShotAt(transform.position);

            Vector3 muzzleStart = _owner.AvatarVisual != null ? _owner.AvatarVisual.MuzzleWorldPos : (transform.position + Vector3.up * 1.25f);

            // Evaluate exact center hits vs forgiving 0.26m sphere hits sorted fairly up to medium blaster range (11.5m)
            var exactHits = Physics.RaycastAll(aimRay, strikeRange, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(exactHits, (a, b) => a.distance.CompareTo(b.distance));

            var sphereHits = Physics.SphereCastAll(aimRay, 0.26f, strikeRange, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(sphereHits, (a, b) => a.distance.CompareTo(b.distance));

            float wallDist = strikeRange;
            RaycastHit? exactPropHit = null;
            RaycastHit? exactStaticHit = null;
            for (int i = 0; i < exactHits.Length; i++)
            {
                var h = exactHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                var pp = h.collider.GetComponentInParent<PlayerParticipant>();
                if (pp != null && pp != _owner && pp.IsAlive && pp.Role == TeamRole.Prop)
                {
                    if (!exactPropHit.HasValue) exactPropHit = h;
                    break;
                }
                var sp = h.collider.GetComponentInParent<InteractiveProp>();
                if (sp != null && sp.enabled)
                {
                    if (!exactStaticHit.HasValue) exactStaticHit = h;
                    break;
                }
                if (pp == null)
                {
                    wallDist = h.distance;
                    break;
                }
            }

            // Moving Prop Smart Aim Assist for human shooters (activates when not already dead-center on a prop)
            if (!exactPropHit.HasValue && !_owner.IsBot)
            {
                var assistedProp = FindAimAssistedProp(aimRay, out Vector3 assistPt);
                if (assistedProp != null)
                {
                    float assistDist = Vector3.Distance(aimRay.origin, assistPt);
                    bool isMovingAssist = assistedProp.IsActivelyMoving || (Time.time - assistedProp.LastActiveMoveTime < 0.60f);
                    if (isMovingAssist || !exactStaticHit.HasValue || assistDist <= exactStaticHit.Value.distance + 0.35f)
                    {
                        ApplyPropPlayerHit(assistedProp, assistPt, muzzleStart, gsm);
                        return true;
                    }
                }
            }

            RaycastHit? spherePropHit = null;
            RaycastHit? sphereStaticHit = null;
            for (int i = 0; i < sphereHits.Length; i++)
            {
                var h = sphereHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance > wallDist + 0.08f) break;
                var pp = h.collider.GetComponentInParent<PlayerParticipant>();
                if (pp != null && pp != _owner && pp.IsAlive && pp.Role == TeamRole.Prop)
                {
                    if (!spherePropHit.HasValue) spherePropHit = h;
                    break;
                }
                var sp = h.collider.GetComponentInParent<InteractiveProp>();
                if (sp != null && sp.enabled && !sphereStaticHit.HasValue)
                    sphereStaticHit = h;
            }

            RaycastHit? chosenPropHit = exactPropHit ?? spherePropHit;
            if (chosenPropHit.HasValue && (!exactStaticHit.HasValue || chosenPropHit.Value.distance <= exactStaticHit.Value.distance + 0.18f))
            {
                var h = chosenPropHit.Value;
                var targetPropPlayer = h.collider.GetComponentInParent<PlayerParticipant>();
                if (targetPropPlayer != null && targetPropPlayer != _owner && targetPropPlayer.IsAlive && targetPropPlayer.Role == TeamRole.Prop)
                {
                    ApplyPropPlayerHit(targetPropPlayer, h.point, muzzleStart, gsm);
                    return true;
                }
            }

            RaycastHit? chosenStaticHit = exactStaticHit ?? sphereStaticHit;
            if (chosenStaticHit.HasValue && chosenStaticHit.Value.distance <= wallDist + 0.08f)
            {
                var staticProp = chosenStaticHit.Value.collider.GetComponentInParent<InteractiveProp>();
                if (staticProp != null && staticProp.enabled)
                {
                    BlasterTracerFx.Spawn(muzzleStart, chosenStaticHit.Value.point, hitProp: false);
                    Networking.NetworkMatchSync.Instance?.BroadcastBlasterShot(muzzleStart, chosenStaticHit.Value.point, false, transform.position);

                    if (staticProp.IsDecoyClone)
                    {
                        const int decoyBaitPenalty = 5;
                        _owner.TakeSelfPenalty(decoyBaitPenalty);
                        AudioSynthManager.Instance?.PlayMorphPop();

                        var sync = Networking.NetworkMatchSync.Instance;
                        if (sync != null && sync.IsSpawned)
                        {
                            sync.BroadcastShatterDecoy(staticProp.DecoyNetId, staticProp.transform.position);
                        }
                        else
                        {
                            ExpandingWaveEffect.SpawnSoundRipple(staticProp.transform.position + Vector3.up * 0.35f, Palette.AccentGold);
                            if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.ReleaseDecoy(staticProp.gameObject);
                            else Destroy(staticProp.gameObject);
                        }

                        if (_owner.IsLocalHuman)
                        {
                            _impulse?.GenerateImpulse(0.24f);
                            gsm.TriggerFlash(Palette.AccentGold, 0.16f);
                            gsm.ShowToast($"DECOY BUSTED! -{decoyBaitPenalty} HP", Palette.AccentGold);
                            OnStrikeHitFeedback?.Invoke(false);
                        }
                        return true;
                    }

                    _owner.TakeSelfPenalty(wrongHitPenalty);
                    AudioSynthManager.Instance?.PlayWrongHitBuzz();
                    if (_owner.IsLocalHuman)
                    {
                        _impulse?.GenerateImpulse(0.32f);
                        gsm.TriggerFlash(Palette.DangerRed, 0.20f);
                        gsm.ShowToast($"Wrong Prop! -{wrongHitPenalty} HP", Palette.DangerRed);
                        OnStrikeHitFeedback?.Invoke(false);
                    }
                    return true;
                }
            }

            Vector3 wallEnd = aimRay.GetPoint(wallDist);
            BlasterTracerFx.Spawn(muzzleStart, wallEnd, hitProp: false);
            Networking.NetworkMatchSync.Instance?.BroadcastBlasterShot(muzzleStart, wallEnd, false, transform.position);
            return true;
        }

        private void ApplyPropPlayerHit(PlayerParticipant targetPropPlayer, Vector3 hitPoint, Vector3 muzzleStart, GameStateManager gsm)
        {
            if (targetPropPlayer == null) return;

            // Report hit over authoritative Socket.IO connection
            Networking.SocketIO.SocketIOUnityClient.Instance?.ReportPropHit(targetPropPlayer.DisplayName, hitDamage, muzzleStart, hitPoint);

            // Check if this 25 DMG shot is fatal (Prop HP <= 25)
            bool willKill = targetPropPlayer.Health <= hitDamage;

            if (willKill && CameraRig.KillCamController.Instance != null)
            {
                // Trigger Cinematic Slow-Motion Bullet Kill Cam!
                CameraRig.KillCamController.Instance.TriggerKillCam(muzzleStart, hitPoint, targetPropPlayer, _owner, () =>
                {
                    OnHunterScored?.Invoke(150);
                    OnStrikeHitFeedback?.Invoke(true);
                });
                return;
            }

            bool killed = targetPropPlayer.TakeDamage(hitDamage, _owner.DisplayName);
            BlasterTracerFx.Spawn(muzzleStart, hitPoint, hitProp: true);
            Networking.NetworkMatchSync.Instance?.BroadcastBlasterShot(muzzleStart, hitPoint, true, transform.position);
            SpawnSparks(hitPoint);
            AudioSynthManager.Instance?.PlayHitSolid();
            if (_owner.IsLocalHuman)
            {
                _impulse?.GenerateImpulse(0.22f);
                gsm.TriggerFlash(Palette.SuccessEmerald, 0.14f);
                gsm.ShowToast(killed ? $"PROP CAUGHT! ({targetPropPlayer.DisplayName})" : $"Direct Hit! Prop HP: {targetPropPlayer.Health}", Palette.SuccessEmerald);
                OnStrikeHitFeedback?.Invoke(true);
            }
            if (killed) AudioSynthManager.Instance?.PlayEliminationSting();
            OnHunterScored?.Invoke(killed ? 150 : 50);
        }

        public bool TryRadarPulse()
        {
            if (_owner == null || !_owner.IsAlive || _owner.Role != TeamRole.Hunter) return false;
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Phase != MatchPhase.HuntPhase) return false;
            if (Time.time < _nextRadarTime) return false;

            _nextRadarTime = Time.time + radarCooldown;
            AudioSynthManager.Instance?.PlayRadarPing();
            ExpandingWaveEffect.SpawnRadarWave(transform.position + Vector3.up * 0.14f, radarRadius, Palette.HunterAccent);

            int nearbyCount = 0;
            float closestDist = float.MaxValue;
            Vector3 closestDir = Vector3.forward;

            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive || p.Role != TeamRole.Prop) continue;
                Vector3 delta = p.transform.position - transform.position;
                float d = delta.magnitude;
                if (d <= radarRadius)
                {
                    nearbyCount++;
                    if (d < closestDist)
                    {
                        closestDist = d;
                        closestDir = delta.normalized;
                    }
                }
            }

            string summary;
            if (nearbyCount == 0)
            {
                summary = $"RADAR: Clear within {radarRadius:0}m";
            }
            else
            {
                string band = closestDist < 4.5f ? "VERY CLOSE" : (closestDist < 9.0f ? "NEARBY" : "FAR EDGE");
                float localAngle = Vector3.SignedAngle(transform.forward, new Vector3(closestDir.x, 0f, closestDir.z), Vector3.up);
                string bearing = Mathf.Abs(localAngle) < 40f ? "AHEAD" : (Mathf.Abs(localAngle) > 135f ? "BEHIND" : (localAngle > 0f ? "RIGHT" : "LEFT"));
                float dy = closestDir.y * closestDist;
                string floorHint = Mathf.Abs(dy) < 2.2f ? "SAME FLOOR" : (dy > 0f ? "UPSTAIRS" : "DOWNSTAIRS");
                summary = $"RADAR: {nearbyCount} Prop(s) — Nearest {band} ({bearing} • {floorHint})";
            }

            OnRadarResult?.Invoke(summary);
            if (_owner.IsLocalHuman)
            {
                gsm.TriggerFlash(Palette.HunterAccent, 0.14f);
                gsm.ShowToast(summary, Palette.HunterAccent);
            }
            return true;
        }

        private void SpawnSparks(Vector3 pos)
        {
            if (_hitSparks == null) return;
            _hitSparks.transform.position = pos;
            _hitSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _hitSparks.Play();
        }

        private void BuildHitSparks()
        {
            var go = new GameObject("HunterHitSparks");
            go.transform.SetParent(transform, false);
            _hitSparks = go.AddComponent<ParticleSystem>();
            _hitSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _hitSparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.35f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 4.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = Palette.HunterAccent;
            main.gravityModifier = 0.6f;
            main.maxParticles = 18;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _hitSparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = _hitSparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.16f;

            var sizeOverLife = _hitSparks.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.color = Palette.HunterAccent;
            psr.sharedMaterial = mat;
        }
    }
}
