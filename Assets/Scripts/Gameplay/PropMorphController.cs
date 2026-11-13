using System;
using CoachingPropHunt.Core;
using CoachingPropHunt.Props;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoachingPropHunt.Gameplay
{
    /// <summary>
    /// Handles scanning morphable world props, morphing the player into the exact same prefab
    /// (identical PBR materials, scale, light probes, zero outlines), snapping flush onto any
    /// floor/desk/stage surface beneath, and locking world yaw.
    /// </summary>
    public sealed class PropMorphController : MonoBehaviour
    {
        [SerializeField] private float morphRange = 4.6f;
        [SerializeField] private Transform propVisualRoot;

        private GameObject _currentVisual;
        private PropDefinition _currentDef;
        private ParticleSystem _morphPuff;
        private CharacterController _cc;
        private PlayerAvatarVisual _avatarVisual;
        private readonly Collider[] _overlapBuf = new Collider[24];

        public PropDefinition CurrentProp => _currentDef;
        public string CurrentPropId => _currentDef != null ? _currentDef.propId : "";
        public bool IsMorphed => _currentDef != null;
        public bool IsRotationLocked { get; private set; }
        public float LockedYawDeg { get; private set; }

        public event Action<PropDefinition> OnMorphed;
        public event Action<bool> OnRotationLockChanged;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _avatarVisual = GetComponentInChildren<PlayerAvatarVisual>(true);
            if (propVisualRoot == null)
            {
                var holder = new GameObject("PropVisualRoot");
                holder.transform.SetParent(transform, false);
                propVisualRoot = holder.transform;
            }
            BuildPooledMorphPuff();
        }

        public InteractiveProp FindAimedProp(Ray viewRay)
        {
            Vector3 playerChest = transform.position + Vector3.up * 0.45f;
            float raySpan = morphRange + 3.6f;

            // 1. Prioritize exact center-screen ray so small tabletop props (mugs, bottles, books) win over underlying desks
            var exactHits = Physics.RaycastAll(viewRay, raySpan, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(exactHits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < exactHits.Length; i++)
            {
                var h = exactHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                Vector3 toHit = h.point - playerChest;
                if (Vector3.Dot(toHit, viewRay.direction) < -0.20f) continue;

                var p = h.collider.GetComponentInParent<InteractiveProp>();
                if (p != null && !p.transform.IsChildOf(transform))
                {
                    if (toHit.magnitude <= morphRange) return p;
                    continue;
                }
                if (h.collider.GetComponentInParent<PlayerParticipant>() == null)
                    break;
            }

            // 2. Forgiving sphere-cast fallback for larger / angled props
            var hits = Physics.SphereCastAll(viewRay, 0.28f, raySpan, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                Vector3 toHit = h.point - playerChest;
                if (Vector3.Dot(toHit, viewRay.direction) < -0.20f) continue;

                var p = h.collider.GetComponentInParent<InteractiveProp>();
                if (p != null && !p.transform.IsChildOf(transform))
                {
                    if (toHit.magnitude <= morphRange) return p;
                    continue;
                }
                if (h.collider.GetComponentInParent<PlayerParticipant>() == null)
                    break;
            }

            // Non-alloc fallback favoring props ahead of the crosshair
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.55f, 2.2f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            InteractiveProp best = null;
            float bestScore = float.MaxValue;
            Vector3 flatForward = new Vector3(viewRay.direction.x, 0f, viewRay.direction.z);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = transform.forward;
            else flatForward.Normalize();

            for (int i = 0; i < count; i++)
            {
                var col = _overlapBuf[i];
                if (col == null) continue;
                var p = col.GetComponentInParent<InteractiveProp>();
                if (p == null || p.transform.IsChildOf(transform)) continue;
                Vector3 toProp = p.transform.position - transform.position;
                Vector3 flatTo = new Vector3(toProp.x, 0f, toProp.z);
                float dot = flatTo.sqrMagnitude > 0.01f ? Vector3.Dot(flatTo.normalized, flatForward) : 1f;
                if (dot < -0.25f) continue;
                float score = toProp.magnitude - dot * 0.85f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        public bool TryMorphTarget(Ray viewRay, out string morphedDisplayName)
        {
            morphedDisplayName = "";
            var target = FindAimedProp(viewRay);
            if (target == null) return false;
            return ApplyPropById(target.PropId, playFx: true, out morphedDisplayName);
        }

        public bool ApplyPropById(string propId, bool playFx, out string displayName)
        {
            displayName = "";
            var reg = GameStateManager.Instance != null ? GameStateManager.Instance.PropRegistry : null;
            var def = reg != null ? reg.GetById(propId) : null;
            if (def == null || def.prefab == null) return false;

            if (_currentVisual != null)
            {
                var pool = ObjectPoolManager.Instance;
                if (pool != null && _currentDef != null) pool.ReleasePropVisual(_currentDef.propId, _currentVisual);
                else Destroy(_currentVisual);
                _currentVisual = null;
            }

            _currentDef = def;
            if (ObjectPoolManager.Instance != null)
            {
                _currentVisual = ObjectPoolManager.Instance.AcquirePropVisual(def, propVisualRoot);
            }
            else
            {
                _currentVisual = Instantiate(def.prefab, propVisualRoot);
                _currentVisual.name = $"PlayerProp_{def.propId}";
                _currentVisual.transform.localPosition = Vector3.zero;
                _currentVisual.transform.localRotation = Quaternion.identity;
                ObjectPoolManager.StripForVisualOnly(_currentVisual);
            }

            if (_avatarVisual != null) _avatarVisual.SetVisible(false);

            ResizeCharacterController(def.boundsSize);
            SnapToSurfaceBelow();
            GetComponent<PlayerParticipant>()?.EnsureCharacterClearance();

            if (playFx)
            {
                AudioSynthManager.Instance?.PlayMorphPop();
                if (_morphPuff != null)
                {
                    _morphPuff.transform.position = transform.position + Vector3.up * Mathf.Max(0.25f, def.boundsSize.y * 0.45f);
                    _morphPuff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _morphPuff.Play();
                }
            }

            displayName = def.displayName;
            OnMorphed?.Invoke(def);
            return true;
        }

        public void ResetToAvatar()
        {
            if (_currentVisual != null)
            {
                var pool = ObjectPoolManager.Instance;
                if (pool != null && _currentDef != null) pool.ReleasePropVisual(_currentDef.propId, _currentVisual);
                else Destroy(_currentVisual);
                _currentVisual = null;
            }
            _currentDef = null;
            IsRotationLocked = false;
            if (_cc != null)
            {
                _cc.stepOffset = 0.05f;
                _cc.skinWidth = 0.03f;
                _cc.radius = 0.18f;
                _cc.height = 1.66f;
                _cc.center = new Vector3(0f, 0.83f, 0f);
                _cc.stepOffset = 0.35f;
            }
            GetComponent<PlayerParticipant>()?.EnsureCharacterClearance();
            if (_avatarVisual != null) _avatarVisual.SetVisible(true);
            OnRotationLockChanged?.Invoke(false);
        }

        public void ToggleRotationLock()
        {
            IsRotationLocked = !IsRotationLocked;
            if (IsRotationLocked)
            {
                LockedYawDeg = transform.eulerAngles.y;
                SnapToSurfaceBelow();
            }
            AudioSynthManager.Instance?.PlayClick();
            OnRotationLockChanged?.Invoke(IsRotationLocked);
        }

        public void RotateMorphYaw(float deltaDeg)
        {
            if (!IsMorphed) return;
            float nextYaw = Mathf.Repeat(transform.eulerAngles.y + deltaDeg, 360f);
            transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            if (IsRotationLocked)
                LockedYawDeg = nextYaw;
        }

        public void SnapToSurfaceBelow()
        {
            Vector3 origin = transform.position + Vector3.up * 0.12f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 3.0f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
                bool wasEnabled = _cc != null && _cc.enabled;
                if (_cc != null) _cc.enabled = false;
                Vector3 pos = transform.position;
                pos.y = hits[i].point.y + 0.005f;
                transform.position = pos;
                if (_cc != null) _cc.enabled = wasEnabled;
                return;
            }
        }

        private void ResizeCharacterController(Vector3 boundsSize)
        {
            if (_cc == null) return;
            float radius = Mathf.Clamp(Mathf.Min(boundsSize.x, boundsSize.z) * 0.36f, 0.12f, 0.24f);
            float height = Mathf.Clamp(boundsSize.y, radius * 2.04f, 1.85f);
            _cc.stepOffset = 0.05f;
            _cc.skinWidth = 0.03f;
            _cc.radius = radius;
            _cc.height = height;
            _cc.center = new Vector3(0f, height * 0.5f, 0f);
            _cc.stepOffset = Mathf.Clamp(height * 0.48f, 0.05f, 0.32f);
        }

        private void BuildPooledMorphPuff()
        {
            var go = new GameObject("MorphBurstPuff");
            go.transform.SetParent(transform, false);
            _morphPuff = go.AddComponent<ParticleSystem>();
            _morphPuff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _morphPuff.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.52f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = Palette.MorphBurstTeal;
            main.gravityModifier = 0.15f;
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _morphPuff.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = _morphPuff.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var sizeOverLife = _morphPuff.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var spriteMat = new Material(sh);
            spriteMat.color = Palette.MorphBurstTeal;
            psr.sharedMaterial = spriteMat;
        }
    }
}
