using System;
using System.Collections.Generic;
using CoachingPropHunt.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoachingPropHunt.Gameplay
{
    /// <summary>
    /// Unified participant actor supporting Local Human Players and AI Bots.
    /// Drives CharacterController physics (slopeLimit=50 for coaching stairs), gravity/jump,
    /// look pitch/yaw, health/score/elimination, and keeps a kinematic Rigidbody + Trigger child
    /// intact even when steered by a NavMeshAgent.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerParticipant : MonoBehaviour
    {
        public static readonly List<PlayerParticipant> AllActive = new List<PlayerParticipant>();
        public static PlayerParticipant LocalPlayer { get; private set; }

        [Header("Tuning")]
        [SerializeField] private float baseMoveSpeed = 3.25f;
        [SerializeField] private float hunterSpeedBonus = 1.08f;
        [SerializeField] private float jumpSpeed = 5.4f;
        [SerializeField] private float gravity = -19.5f;

        public int ParticipantId { get; private set; }
        public string DisplayName { get; private set; } = "Player";
        public TeamRole Role { get; private set; }
        public bool IsLocalHuman { get; private set; }
        public bool IsBot { get; private set; }
        public bool IsRemotePuppet { get; private set; }
        public int Health { get; private set; } = 100;
        public int MaxHealth { get; private set; } = 100;
        public int Score { get; private set; }
        public int Eliminations { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public Action<int, string> ExternalDamageHandler { get; set; }
        public Action<float, Vector3> ExternalStunHandler { get; set; }
        public float ViewYawDeg { get; private set; }
        public float ViewPitchDeg { get; private set; }
        public float LastFootstepTime { get; private set; }
        public float StunRemainingSeconds { get; private set; }
        public bool IsStunned => StunRemainingSeconds > 0f;
        public bool IsActivelyMoving { get; private set; }
        public float LastActiveMoveTime { get; private set; } = -10f;
        public bool IsRunning { get; private set; }

        public CharacterController CharacterCtrl { get; private set; }
        public BoxCollider CombatHitbox { get; private set; }
        public PlayerAvatarVisual AvatarVisual { get; private set; }
        public PropMorphController MorphCtrl { get; private set; }
        public PropTauntAndAbility PropAbility { get; private set; }
        public HunterActionController HunterAction { get; private set; }

        private Vector2 _moveInput;
        private Vector3 _stunKnockVel;
        private float _verticalVelocity;
        private bool _wasGrounded = true;
        private float _survivalScoreTimer;

        public event Action<PlayerParticipant> OnStatsChanged;
        public event Action<PlayerParticipant> OnEliminated;
        public static event Action<PlayerParticipant, string> OnAnyPropEliminated;
        public static event Action<string, string, Vector3> OnAnyKillFeed;
        public string LastAttackerName { get; private set; } = "Hunter";

        public static void EmitRemoteKillFeed(string victimName, string attackerName, Vector3 worldPos)
        {
            OnAnyKillFeed?.Invoke(victimName, attackerName, worldPos);
        }

        private void Awake()
        {
            CharacterCtrl = GetComponent<CharacterController>();
            CharacterCtrl.slopeLimit = 52f;
            CharacterCtrl.stepOffset = 0.35f;
            CharacterCtrl.minMoveDistance = 0f;
            CharacterCtrl.skinWidth = 0.03f;
            CharacterCtrl.radius = 0.18f;
            CharacterCtrl.height = 1.66f;
            CharacterCtrl.center = new Vector3(0f, 0.83f, 0f);

            // Keep kinematic Rigidbody + Trigger SphereCollider so trigger events work even under NavMeshAgent
            var trigGo = new GameObject("InteractionTrigger");
            trigGo.transform.SetParent(transform, false);
            trigGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var rb = trigGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var trig = trigGo.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.45f;

            var hitGo = new GameObject("CombatHitbox");
            hitGo.transform.SetParent(transform, false);
            CombatHitbox = hitGo.AddComponent<BoxCollider>();
            CombatHitbox.size = new Vector3(0.52f, 1.66f, 0.44f);
            CombatHitbox.center = new Vector3(0f, 0.83f, 0f);
            CombatHitbox.enabled = false;

            AvatarVisual = gameObject.AddComponent<PlayerAvatarVisual>();
            MorphCtrl = gameObject.AddComponent<PropMorphController>();
            PropAbility = gameObject.AddComponent<PropTauntAndAbility>();
            HunterAction = gameObject.AddComponent<HunterActionController>();

            MorphCtrl.OnMorphed += HandleMorphed;
            PropAbility.OnManualTauntScored += pts => AddScore(pts);
            HunterAction.OnHunterScored += pts =>
            {
                AddScore(pts);
                if (pts >= 150) Eliminations++;
                OnStatsChanged?.Invoke(this);
            };
        }

        private void OnEnable()
        {
            if (!AllActive.Contains(this)) AllActive.Add(this);
        }

        private void OnDisable()
        {
            AllActive.Remove(this);
            if (LocalPlayer == this) LocalPlayer = null;
        }

        public void Initialize(int id, string playerName, TeamRole role, bool localHuman, bool isBot, float initialYawDeg, bool isRemotePuppet = false)
        {
            ParticipantId = id;
            DisplayName = playerName;
            Role = role;
            IsLocalHuman = localHuman;
            IsBot = isBot;
            IsRemotePuppet = isRemotePuppet;
            ExternalDamageHandler = null;
            ExternalStunHandler = null;
            LastAttackerName = "Hunter";
            StunRemainingSeconds = 0f;
            _stunKnockVel = Vector3.zero;
            IsActivelyMoving = false;
            LastActiveMoveTime = -10f;
            IsAlive = true;
            Score = 0;
            Eliminations = 0;
            MaxHealth = role == TeamRole.Hunter ? 100 : 80;
            Health = MaxHealth;
            ViewYawDeg = initialYawDeg;
            ViewPitchDeg = 10f;
            transform.rotation = Quaternion.Euler(0f, initialYawDeg, 0f);
            if (CharacterCtrl != null) CharacterCtrl.enabled = !isRemotePuppet;

            if (localHuman) LocalPlayer = this;

            AvatarVisual.BuildVisual(role);
            MorphCtrl.ResetToAvatar();
            if (CombatHitbox != null)
            {
                CombatHitbox.size = new Vector3(0.52f, 1.66f, 0.44f);
                CombatHitbox.center = new Vector3(0f, 0.83f, 0f);
                if (CharacterCtrl != null) Physics.IgnoreCollision(CharacterCtrl, CombatHitbox, true);
                CombatHitbox.enabled = true;
            }
            AvatarVisual.SetVisible(true, firstPersonHideBody: false);

            PropAbility.ResetForMatch();
            HunterAction.ResetForMatch();
            OnStatsChanged?.Invoke(this);
        }

        public void SetMoveInput(Vector2 move) => _moveInput = Vector2.ClampMagnitude(move, 1f);
        public void SetRunning(bool running) => IsRunning = running;

        public void ApplyRemoteIdentity(string displayName, TeamRole role)
        {
            if (!IsRemotePuppet) return;
            if (!string.IsNullOrWhiteSpace(displayName))
                DisplayName = displayName;
            if (Role != role)
            {
                Role = role;
                MaxHealth = role == TeamRole.Hunter ? 100 : 80;
                Health = Mathf.Clamp(Health, 0, MaxHealth);
                if (MorphCtrl != null && MorphCtrl.IsMorphed && role == TeamRole.Hunter)
                    MorphCtrl.ResetToAvatar();
                AvatarVisual?.BuildVisual(role);
                AvatarVisual?.SetVisible(IsAlive, firstPersonHideBody: false);
                gameObject.name = $"RemotePeer_{DisplayName}_{role}";
                OnStatsChanged?.Invoke(this);
            }
        }

        public void AddLookDelta(Vector2 delta)
        {
            ViewYawDeg = Mathf.Repeat(ViewYawDeg + delta.x, 360f);
            ViewPitchDeg = Mathf.Clamp(ViewPitchDeg - delta.y, -55f, 68f);
        }

        public void ApplyStun(float duration, Vector3 knockDir)
        {
            if (!IsAlive) return;
            if (IsRemotePuppet && ExternalStunHandler != null)
            {
                ExternalStunHandler.Invoke(duration, knockDir);
                return;
            }
            StunRemainingSeconds = Mathf.Max(StunRemainingSeconds, duration);
            Vector3 flat = new Vector3(knockDir.x, 0f, knockDir.z);
            if (flat.sqrMagnitude > 0.001f) _stunKnockVel = flat.normalized * 4.2f;
            if (IsLocalHuman)
            {
                AudioSynthManager.Instance?.PlayWrongHitBuzz();
                GameStateManager.Instance?.TriggerFlash(Palette.AccentGold, 0.25f);
                GameStateManager.Instance?.ShowToast($"STUNNED BY PROP! ({duration:0.0}s)", Palette.DangerRed);
            }
        }

        public void TriggerJump()
        {
            if (!IsAlive) return;
            if (IsMovementFrozen()) return;
            if (CharacterCtrl != null && CharacterCtrl.enabled && CharacterCtrl.isGrounded)
            {
                _verticalVelocity = jumpSpeed;
                AudioSynthManager.Instance?.PlayClick();
            }
        }

        public Ray GetAimRay()
        {
            if (IsLocalHuman && UnityEngine.Camera.main != null)
            {
                var cam = UnityEngine.Camera.main;
                Vector3 planePt = transform.position + Vector3.up * (Role == TeamRole.Hunter ? 1.42f : 0.72f);
                float along = Mathf.Max(0f, Vector3.Dot(planePt - cam.transform.position, cam.transform.forward) - 0.22f);
                return new Ray(cam.transform.position + cam.transform.forward * along, cam.transform.forward);
            }
            Vector3 eye = transform.position + Vector3.up * (Role == TeamRole.Hunter ? 1.45f : 0.75f);
            Quaternion q = Quaternion.Euler(ViewPitchDeg, ViewYawDeg, 0f);
            return new Ray(eye, q * Vector3.forward);
        }

        public bool TakeDamage(int amount, int attackerId)
        {
            string attackerName = "Hunter";
            for (int i = 0; i < AllActive.Count; i++)
            {
                if (AllActive[i] != null && AllActive[i].ParticipantId == attackerId)
                {
                    attackerName = AllActive[i].DisplayName;
                    break;
                }
            }
            return TakeDamage(amount, attackerName);
        }

        public bool TakeDamage(int amount, string attackerName)
        {
            if (!IsAlive) return false;
            string safeAttacker = string.IsNullOrWhiteSpace(attackerName) ? "Hunter" : attackerName.Trim();
            if (IsRemotePuppet && ExternalDamageHandler != null)
            {
                ExternalDamageHandler.Invoke(amount, safeAttacker);
                return (Health - Mathf.Max(0, amount)) <= 0;
            }
            LastAttackerName = safeAttacker;
            Health = Mathf.Max(0, Health - Mathf.Max(0, amount));

            // Visual & Audio Damage Feedback on Local Player
            if (IsLocalHuman)
            {
                GameStateManager.Instance?.TriggerFlash(Palette.DangerRed, 0.28f);
                AudioSynthManager.Instance?.PlayWrongHitBuzz();
                GameStateManager.Instance?.ShowToast($"HIT! -{amount} HP ({Health}/100)", Palette.DangerRed);
                ExpandingWaveEffect.SpawnSoundRipple(transform.position + Vector3.up * 0.4f, Palette.DangerRed);
            }

            OnStatsChanged?.Invoke(this);
            if (Health <= 0)
            {
                Die();
                return true;
            }
            return false;
        }

        public void EnsureCharacterClearance()
        {
            if (CharacterCtrl == null || !CharacterCtrl.enabled || IsRemotePuppet) return;
            float r = Mathf.Max(0.14f, CharacterCtrl.radius);
            float h = Mathf.Max(r * 2f + 0.05f, CharacterCtrl.height);
            Vector3 center = transform.position + CharacterCtrl.center;
            Vector3 p0 = center + Vector3.up * (h * 0.5f - r);
            Vector3 p1 = center - Vector3.up * (h * 0.5f - r);

            var hits = Physics.OverlapCapsule(p0, p1, r + 0.02f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null || c == CharacterCtrl || c.transform.IsChildOf(transform)) continue;
                if (Physics.ComputePenetration(
                    CharacterCtrl, transform.position, transform.rotation,
                    c, c.transform.position, c.transform.rotation,
                    out Vector3 pushDir, out float pushDist))
                {
                    // Ignore upward-only push on flat floor; slide horizontally out of walls/furniture
                    pushDir.y = Mathf.Max(0f, pushDir.y * 0.25f);
                    if (pushDir.sqrMagnitude > 0.0001f && pushDist > 0.001f)
                        transform.position += pushDir.normalized * Mathf.Min(pushDist + 0.02f, 1.2f);
                }
            }
            Physics.SyncTransforms();
        }

        public void ApplyRemoteMirror(Vector3 targetPos, float yawDeg, bool isLocked, float lockedYaw, bool isMorphed, string propId, int hp, bool alive, float dt)
        {
            if (!IsRemotePuppet) return;
            if (!alive)
            {
                if (IsAlive)
                {
                    Health = 0;
                    Die();
                }
                return;
            }

            Vector3 prev = transform.position;
            float distSq = (targetPos - prev).sqrMagnitude;
            Vector3 next = distSq > 56.25f ? targetPos : Vector3.Lerp(prev, targetPos, 1f - Mathf.Exp(-Mathf.Max(0.001f, dt) * 16f));
            Vector3 vel = (next - prev) / Mathf.Max(0.004f, dt);
            transform.position = next;

            float targetYaw = isLocked ? lockedYaw : yawDeg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), 1f - Mathf.Exp(-Mathf.Max(0.001f, dt) * 16f));
            ViewYawDeg = targetYaw;

            if (isMorphed && !string.IsNullOrEmpty(propId))
            {
                if (MorphCtrl != null && MorphCtrl.CurrentPropId != propId)
                    MorphCtrl.ApplyPropById(propId, playFx: true, out _);
            }
            else if (MorphCtrl != null && MorphCtrl.IsMorphed)
            {
                MorphCtrl.ResetToAvatar();
            }

            if (Health != hp)
            {
                Health = Mathf.Clamp(hp, 0, MaxHealth);
                OnStatsChanged?.Invoke(this);
            }
            float planarSpd = new Vector2(vel.x, vel.z).magnitude;
            IsActivelyMoving = planarSpd > 0.35f;
            if (IsActivelyMoving) LastActiveMoveTime = Time.time;
            AvatarVisual?.UpdateMotion(new Vector3(vel.x, 0f, vel.z), baseMoveSpeed, 0f, dt, isGrounded: true, aimPitchDeg: 0f, stunRemaining: StunRemainingSeconds);
        }

        public void TakeSelfPenalty(int penalty)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(0, Health - Mathf.Max(0, penalty));
            OnStatsChanged?.Invoke(this);
            if (Health <= 0)
            {
                Die();
            }
        }

        public void AddScore(int pts)
        {
            if (pts == 0) return;
            Score += pts;
            OnStatsChanged?.Invoke(this);
        }

        private void Die()
        {
            IsAlive = false;
            if (CharacterCtrl != null) CharacterCtrl.enabled = false;
            if (CombatHitbox != null) CombatHitbox.enabled = false;
            var nma = GetComponent<NavMeshAgent>();
            if (nma != null && nma.enabled) nma.enabled = false;
            AvatarVisual.SetVisible(false);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            OnEliminated?.Invoke(this);
            if (Role == TeamRole.Prop)
            {
                string killer = string.IsNullOrEmpty(LastAttackerName) ? "Hunter" : LastAttackerName;
                var gsm = GameStateManager.Instance;
                var sync = Networking.NetworkMatchSync.Instance;
                if (gsm != null && gsm.IsOnlineSession && sync != null && sync.IsSpawned)
                {
                    // Only the authoritative side (Owner for local human, Host for host bots) broadcasts global elimination feed
                    if (!IsRemotePuppet)
                        sync.BroadcastPropEliminated(DisplayName, killer, transform.position);
                }
                else
                {
                    OnAnyPropEliminated?.Invoke(this, killer);
                    OnAnyKillFeed?.Invoke(DisplayName, killer, transform.position);
                }
            }
            OnStatsChanged?.Invoke(this);
        }

        private void HandleMorphed(Props.PropDefinition def)
        {
            if (def == null) return;
            if (CombatHitbox != null)
            {
                Vector3 sz = def.boundsSize;
                CombatHitbox.size = new Vector3(Mathf.Max(0.18f, sz.x), Mathf.Max(0.18f, sz.y), Mathf.Max(0.18f, sz.z));
                CombatHitbox.center = new Vector3(0f, CombatHitbox.size.y * 0.5f, 0f);
            }
            float ratio = MaxHealth > 0 ? (float)Health / MaxHealth : 1f;
            MaxHealth = Mathf.Max(25, def.maxHealth);
            Health = Mathf.Clamp(Mathf.CeilToInt(ratio * MaxHealth), 1, MaxHealth);
            OnStatsChanged?.Invoke(this);
        }

        private bool IsMovementFrozen()
        {
            if (IsStunned) return true;
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive) return true;
            if (Role == TeamRole.Hunter && gsm.Phase == MatchPhase.HidePhase) return true;
            if (gsm.Phase == MatchPhase.RoundOver) return true;
            return false;
        }

        private void Update()
        {
            if (!IsAlive) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || !gsm.IsMatchLive) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (StunRemainingSeconds > 0f) StunRemainingSeconds = Mathf.Max(0f, StunRemainingSeconds - dt);
            _stunKnockVel = Vector3.MoveTowards(_stunKnockVel, Vector3.zero, dt * 14f);

            // Periodic passive stealth score & auto-whistle for alive Props during Hunt Phase
            if (Role == TeamRole.Prop && gsm.Phase == MatchPhase.HuntPhase)
            {
                _survivalScoreTimer += dt;
                if (_survivalScoreTimer >= 5f)
                {
                    _survivalScoreTimer = 0f;
                    AddScore(10);
                }
                PropAbility.TickAutoWhistle(true);
            }

            // When AI Bot uses NavMeshAgent, drive only procedural AvatarVisual motion from agent.velocity
            var agent = GetComponent<NavMeshAgent>();
            if (IsBot && agent != null && agent.enabled)
            {
                float maxSpd = baseMoveSpeed * (Role == TeamRole.Hunter ? hunterSpeedBonus : (MorphCtrl.CurrentProp != null ? MorphCtrl.CurrentProp.moveSpeedMultiplier : 1f));
                float botSpd = new Vector2(agent.velocity.x, agent.velocity.z).magnitude;
                IsActivelyMoving = botSpd > 0.35f;
                if (IsActivelyMoving) LastActiveMoveTime = Time.time;
                AvatarVisual.UpdateMotion(agent.velocity, maxSpd, 0f, dt, isGrounded: true, aimPitchDeg: 0f, stunRemaining: StunRemainingSeconds);
                if (!MorphCtrl.IsMorphed && !MorphCtrl.IsRotationLocked && agent.velocity.sqrMagnitude > 0.04f)
                {
                    Quaternion targetQ = Quaternion.LookRotation(new Vector3(agent.velocity.x, 0f, agent.velocity.z));
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetQ, dt * 10f);
                }
                return;
            }

            if (CharacterCtrl == null || !CharacterCtrl.enabled || !CharacterCtrl.gameObject.activeInHierarchy) return;

            float prevYaw = transform.eulerAngles.y;
            bool frozen = IsMovementFrozen();
            Vector2 move = frozen ? Vector2.zero : _moveInput;

            Quaternion yawRot = Quaternion.Euler(0f, ViewYawDeg, 0f);
            Vector3 moveDir = yawRot * new Vector3(move.x, 0f, move.y);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            float speedMult = Role == TeamRole.Hunter ? hunterSpeedBonus : (MorphCtrl.CurrentProp != null ? MorphCtrl.CurrentProp.moveSpeedMultiplier : 1f);
            float runFactor = IsRunning ? 1.0f : 0.50f;
            float moveSpeed = baseMoveSpeed * speedMult * runFactor;
            Vector3 horizVel = moveDir * moveSpeed;

            if (CharacterCtrl.isGrounded)
            {
                if (!_wasGrounded)
                {
                    AvatarVisual.TriggerLandingSquash();
                    AudioSynthManager.Instance?.PlayFootstep(transform.position, isLanding: true);
                }
                if (_verticalVelocity < 0f) _verticalVelocity = -2.0f;
            }
            else
            {
                _verticalVelocity += gravity * dt;
            }
            _wasGrounded = CharacterCtrl.isGrounded;

            Vector3 velocity = new Vector3(horizVel.x + _stunKnockVel.x, _verticalVelocity, horizVel.z + _stunKnockVel.z);
            CharacterCtrl.Move(velocity * dt);

            float hMag = new Vector2(horizVel.x, horizVel.z).magnitude;
            IsActivelyMoving = hMag > 0.25f;
            if (IsActivelyMoving) LastActiveMoveTime = Time.time;

            if (Role == TeamRole.Hunter)
            {
                transform.rotation = Quaternion.Euler(0f, ViewYawDeg, 0f);
            }
            else if (MorphCtrl.IsRotationLocked)
            {
                transform.rotation = Quaternion.Euler(0f, MorphCtrl.LockedYawDeg, 0f);
            }
            else if (!MorphCtrl.IsMorphed && horizVel.sqrMagnitude > 0.04f)
            {
                Quaternion targetFace = Quaternion.LookRotation(new Vector3(horizVel.x, 0f, horizVel.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetFace, dt * 11f);
            }

            float yawDelta = Mathf.DeltaAngle(prevYaw, transform.eulerAngles.y) / Mathf.Max(0.001f, dt);
            AvatarVisual.UpdateMotion(horizVel, baseMoveSpeed * speedMult, yawDelta, dt, CharacterCtrl.isGrounded, ViewPitchDeg, StunRemainingSeconds);
            float stepInterval = IsRunning ? 0.36f : 0.56f;
            if (horizVel.sqrMagnitude > 0.12f && CharacterCtrl.isGrounded && Time.time - LastFootstepTime > stepInterval)
            {
                LastFootstepTime = Time.time;
                if (Role == TeamRole.Hunter || !MorphCtrl.IsMorphed)
                    AudioSynthManager.Instance?.PlayFootstep(transform.position, isLanding: false);
            }
        }
    }
}
