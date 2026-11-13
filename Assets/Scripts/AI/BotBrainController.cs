using System.Collections.Generic;
using CoachingPropHunt.Core;
using CoachingPropHunt.Props;
using UnityEngine;
using UnityEngine.AI;

namespace CoachingPropHunt.AI
{
    /// <summary>
    /// AI Bot autopilot for backfilling 10–16 slot matches cleanly across Ground Floor,
    /// U-turn Stairs, and First Floor Coaching rooms. Gracefully falls back to CharacterController
    /// steering whenever a spawn briefly leaves baked NavMesh bounds.
    /// </summary>
    [RequireComponent(typeof(Gameplay.PlayerParticipant))]
    public sealed class BotBrainController : MonoBehaviour
    {
        private static readonly List<BotBrainController> ActiveBrains = new List<BotBrainController>();

        private Gameplay.PlayerParticipant _participant;
        private NavMeshAgent _agent;
        private Vector3 _targetDest;
        private float _repathTimer;
        private float _actionTimer;
        private bool _hasMorphed;
        private bool _hasLockedSpot;
        private float _morphScheduleDelay;
        private float _hunterSpotTimer;
        private float _hunterStrikeCooldown;
        private Gameplay.PlayerParticipant _spottedTarget;
        private float _stuckTimer;
        private Vector3 _lastStuckPos;
        private float _investigateTimer;
        private Vector3 _investigatePos;

        public static void NotifyNearestHunterOfWhistle(Vector3 whistlePos, float hearRadius = 14.0f)
        {
            BotBrainController nearest = null;
            float bestSq = hearRadius * hearRadius;
            for (int i = 0; i < ActiveBrains.Count; i++)
            {
                var b = ActiveBrains[i];
                if (b == null || b._participant == null || !b._participant.IsAlive || b._participant.Role != TeamRole.Hunter) continue;
                if (b._spottedTarget != null) continue;
                float dSq = (b.transform.position - whistlePos).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    nearest = b;
                }
            }
            if (nearest != null)
            {
                nearest._investigatePos = whistlePos;
                nearest._investigateTimer = 5.5f;
                nearest.MoveTo(whistlePos);
            }
        }

        private void Awake()
        {
            _participant = GetComponent<Gameplay.PlayerParticipant>();
        }

        private void OnEnable()
        {
            if (!ActiveBrains.Contains(this)) ActiveBrains.Add(this);
        }

        private void OnDisable()
        {
            ActiveBrains.Remove(this);
        }

        public void ActivateBot()
        {
            _hasMorphed = false;
            _hasLockedSpot = false;
            _hunterSpotTimer = 0f;
            _hunterStrikeCooldown = 0f;
            _spottedTarget = null;
            _morphScheduleDelay = UnityEngine.Random.Range(2.5f, 8.5f);
            _repathTimer = UnityEngine.Random.Range(0.2f, 1.5f);
            _actionTimer = UnityEngine.Random.Range(3f, 8f);
            _stuckTimer = 0f;
            _lastStuckPos = transform.position;
            _investigateTimer = 0f;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                _agent = gameObject.GetComponent<NavMeshAgent>();
                if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
                _agent.radius = 0.18f;
                _agent.height = 1.65f;
                _agent.speed = _participant.Role == TeamRole.Hunter ? 2.80f : 2.90f;
                _agent.angularSpeed = 320f;
                _agent.acceleration = 16f;
                _agent.stoppingDistance = 0.65f;
                // Disable CharacterController while NavMeshAgent steers cleanly, keeping InteractionTrigger active
                if (_participant.CharacterCtrl != null) _participant.CharacterCtrl.enabled = false;
            }
            PickRandomWaypoint();
        }

        private void Update()
        {
            if (_participant == null || !_participant.IsBot || !_participant.IsAlive) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive || _participant.IsStunned)
            {
                StopMoving();
                return;
            }

            if (_participant.Role == TeamRole.Hunter) UpdateHunterBot(gsm);
            else UpdatePropBot(gsm);

            TickAntiStuckRecovery();
        }

        private void TickAntiStuckRecovery()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh || _agent.isStopped || !_agent.hasPath)
            {
                _stuckTimer = 0f;
                _lastStuckPos = transform.position;
                return;
            }
            if (_agent.remainingDistance <= _agent.stoppingDistance + 0.35f)
            {
                _stuckTimer = 0f;
                _lastStuckPos = transform.position;
                return;
            }
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer < 1.4f) return;

            float movedSq = (new Vector2(transform.position.x - _lastStuckPos.x, transform.position.z - _lastStuckPos.z)).sqrMagnitude;
            _stuckTimer = 0f;
            _lastStuckPos = transform.position;
            if (movedSq < 0.05f)
            {
                PickRandomWaypoint();
            }
        }

        private void UpdatePropBot(GameStateManager gsm)
        {
            // During Hide Phase: roam into a hiding spot & morph into an authentic coaching prop
            if (!_hasMorphed)
            {
                _morphScheduleDelay -= Time.deltaTime;
                if (_morphScheduleDelay <= 0f)
                {
                    _hasMorphed = true;
                    var ray = new Ray(transform.position + Vector3.up * 0.7f, transform.forward);
                    if (!_participant.MorphCtrl.TryMorphTarget(ray, out _))
                    {
                        var reg = gsm.PropRegistry;
                        var def = reg != null ? reg.GetRandomNonLarge() : null;
                        if (def != null) _participant.MorphCtrl.ApplyPropById(def.propId, playFx: false, out _);
                    }
                    if (_agent != null && _agent.enabled && _participant.MorphCtrl.CurrentProp != null)
                        _agent.speed = 2.95f * _participant.MorphCtrl.CurrentProp.moveSpeedMultiplier;
                }
            }

            if (_agent != null && _agent.enabled)
                _agent.updateRotation = !_participant.MorphCtrl.IsRotationLocked;

            if (gsm.Phase == MatchPhase.HidePhase)
            {
                _repathTimer -= Time.deltaTime;
                if (_repathTimer <= 0f || ReachedDestination())
                {
                    _repathTimer = UnityEngine.Random.Range(2.5f, 5.5f);
                    PickRandomWaypoint();
                }
                return;
            }

            // Hunt Phase: freeze in spot unless a Hunter gets dangerously close
            bool hunterNear = IsEnemyNear(4.6f, out Vector3 hunterPos);
            if (hunterNear)
            {
                if (Vector3.Distance(transform.position, hunterPos) <= 2.85f)
                    _participant.PropAbility.TryStunHunter(false);

                if (_participant.MorphCtrl.IsRotationLocked) _participant.MorphCtrl.ToggleRotationLock();
                _hasLockedSpot = false;
                if (_repathTimer <= 0f)
                {
                    _repathTimer = 1.6f;
                    PickFleeWaypoint(hunterPos);
                }
            }
            else if (ReachedDestination())
            {
                StopMoving();
                if (!_hasLockedSpot && _hasMorphed)
                {
                    _hasLockedSpot = true;
                    if (!_participant.MorphCtrl.IsRotationLocked) _participant.MorphCtrl.ToggleRotationLock();
                }
            }
            _repathTimer -= Time.deltaTime;

            _actionTimer -= Time.deltaTime;
            if (_actionTimer <= 0f)
            {
                _actionTimer = UnityEngine.Random.Range(10f, 18f);
                if (UnityEngine.Random.value < 0.35f)
                    _participant.PropAbility.TrySpawnDecoy(_participant.MorphCtrl.CurrentPropId, false);
                else if (UnityEngine.Random.value < 0.35f)
                    _participant.PropAbility.TryManualWhistle(false);
            }
        }

        private void UpdateHunterBot(GameStateManager gsm)
        {
            if (gsm.Phase != MatchPhase.HuntPhase)
            {
                StopMoving();
                return;
            }

            if (_hunterStrikeCooldown > 0f)
                _hunterStrikeCooldown -= Time.deltaTime;

            var targetProp = FindVisibleEnemyProp(6.8f);
            if (targetProp != null)
            {
                Vector3 toTarget = targetProp.transform.position - transform.position;
                if (targetProp != _spottedTarget)
                {
                    _spottedTarget = targetProp;
                    _hunterSpotTimer = 0.65f;
                }

                if (_hunterSpotTimer > 0f)
                {
                    _hunterSpotTimer -= Time.deltaTime;
                    StopMoving();
                    Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
                    if (flat.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 6f);
                    return;
                }

                if (toTarget.magnitude <= 4.4f)
                {
                    StopMoving();
                    Vector3 flatAim = new Vector3(toTarget.x, 0f, toTarget.z);
                    if (flatAim.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatAim), Time.deltaTime * 8f);
                    if (_hunterStrikeCooldown <= 0f)
                    {
                        _hunterStrikeCooldown = 1.35f;
                        Vector3 aimPt = targetProp.CombatHitbox != null
                            ? targetProp.CombatHitbox.bounds.center
                            : targetProp.transform.position + Vector3.up * 0.35f;
                        bool isSmallProp = targetProp.CombatHitbox != null && targetProp.CombatHitbox.size.magnitude < 0.85f;
                        if (isSmallProp)
                        {
                            aimPt += UnityEngine.Random.insideUnitSphere * 0.22f;
                        }
                        Vector3 origin = transform.position + Vector3.up * 1.35f;
                        _participant.HunterAction.TryStrike(new Ray(origin, (aimPt - origin).normalized));
                    }
                }
                else
                {
                    MoveTo(targetProp.transform.position);
                }
                return;
            }

            _spottedTarget = null;
            _hunterSpotTimer = 0f;

            if (_investigateTimer > 0f)
            {
                _investigateTimer -= Time.deltaTime;
                if (ReachedDestination()) _investigateTimer = 0f;
                return;
            }

            _repathTimer -= Time.deltaTime;
            if (_repathTimer <= 0f || ReachedDestination())
            {
                _repathTimer = UnityEngine.Random.Range(4.0f, 7.5f);
                PickRandomWaypoint();
            }

            // Occasionally probe suspicious clutter props like real players (can incur -10 HP penalty!)
            _actionTimer -= Time.deltaTime;
            if (_actionTimer <= 0f)
            {
                _actionTimer = UnityEngine.Random.Range(9f, 16f);
                if (UnityEngine.Random.value < 0.30f)
                {
                    _participant.HunterAction.TryRadarPulse();
                }
                else if (UnityEngine.Random.value < 0.32f)
                {
                    Vector3 origin = transform.position + Vector3.up * 1.25f;
                    Vector3 dir = (transform.forward + Vector3.down * 0.28f).normalized;
                    _participant.HunterAction.TryStrike(new Ray(origin, dir));
                }
            }
        }

        private void PickRandomWaypoint()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;
            Transform sp = UnityEngine.Random.value < 0.72f
                ? gsm.GetPropSpawn(UnityEngine.Random.Range(0, 32))
                : gsm.GetHunterSpawn(UnityEngine.Random.Range(0, 12));
            if (sp != null)
            {
                Vector3 jitter = new Vector3(UnityEngine.Random.Range(-1.8f, 1.8f), 0f, UnityEngine.Random.Range(-1.8f, 1.8f));
                MoveTo(sp.position + jitter);
            }
        }

        private void PickFleeWaypoint(Vector3 threatPos)
        {
            Vector3 away = (transform.position - threatPos).normalized * 6.5f;
            if (NavMesh.SamplePosition(transform.position + away, out NavMeshHit hit, 5.5f, NavMesh.AllAreas))
            {
                MoveTo(hit.position);
                return;
            }
            PickRandomWaypoint();
        }

        private void MoveTo(Vector3 worldPos)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    _targetDest = hit.position;
                    _agent.isStopped = false;
                    _agent.SetDestination(_targetDest);
                }
            }
            else if (_participant.CharacterCtrl != null && _participant.CharacterCtrl.enabled)
            {
                Vector3 flat = worldPos - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.1f)
                {
                    Vector3 localDir = Quaternion.Inverse(Quaternion.Euler(0f, _participant.ViewYawDeg, 0f)) * flat.normalized;
                    _participant.SetMoveInput(new Vector2(localDir.x, localDir.z));
                }
            }
        }

        private void StopMoving()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
            _participant.SetMoveInput(Vector2.zero);
        }

        private bool ReachedDestination()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return true;
            return !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.25f;
        }

        private bool IsEnemyNear(float radius, out Vector3 enemyPos)
        {
            enemyPos = Vector3.zero;
            float r2 = radius * radius;
            for (int i = 0; i < Gameplay.PlayerParticipant.AllActive.Count; i++)
            {
                var p = Gameplay.PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive || p.Role != TeamRole.Hunter) continue;
                float d2 = (p.transform.position - transform.position).sqrMagnitude;
                if (d2 <= r2)
                {
                    enemyPos = p.transform.position;
                    return true;
                }
            }
            return false;
        }

        private Gameplay.PlayerParticipant FindVisibleEnemyProp(float maxDist)
        {
            Gameplay.PlayerParticipant best = null;
            float bestDist = maxDist;
            for (int i = 0; i < Gameplay.PlayerParticipant.AllActive.Count; i++)
            {
                var p = Gameplay.PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive || p.Role != TeamRole.Prop) continue;

                // Disguised & motionless Props blend in unless inspected up close (< 1.45m)
                bool disguisedStill = p.MorphCtrl.IsMorphed && (p.MorphCtrl.IsRotationLocked || Time.time - p.LastFootstepTime > 1.2f);
                float effectiveRange = disguisedStill ? 1.45f : maxDist;
                Vector3 delta = p.transform.position - transform.position;
                float d = delta.magnitude;
                if (d >= bestDist || d > effectiveRange) continue;

                // Realistic 115-degree forward vision cone unless virtually bumping into the prop (< 1.15m)
                Vector3 flatDir = new Vector3(delta.x, 0f, delta.z);
                if (d >= 1.15f && flatDir.sqrMagnitude > 0.01f && Vector3.Dot(transform.forward, flatDir.normalized) < 0.52f)
                    continue;

                Vector3 eye = transform.position + Vector3.up * 1.4f;
                Vector3 targetPt = p.CombatHitbox != null ? p.CombatHitbox.bounds.center : p.transform.position + Vector3.up * 0.35f;
                if (!Physics.Linecast(eye, targetPt, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider.transform.IsChildOf(p.transform))
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
