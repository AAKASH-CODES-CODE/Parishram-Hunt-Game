using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoachingPropHunt.Core
{
    public enum GraphicsPresetTier
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Smoothly rotates Coaching Center ceiling fans in real time.
    /// </summary>
    public sealed class CoachingFanSpinner : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 220f;

        public void SetSpeed(float dps) => degreesPerSecond = dps;

        private void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }

    /// <summary>
    /// Procedural expanding 3D ring used for Hunter Radar scans and Prop Whistle sound ripples.
    /// Zero external shader or mesh dependencies.
    /// </summary>
    public sealed class ExpandingWaveEffect : MonoBehaviour
    {
        private static Material _sharedWaveMat;
        private LineRenderer _lr;
        private MaterialPropertyBlock _mpb;
        private float _elapsed;
        private float _duration;
        private float _startRadius;
        private float _endRadius;
        private float _riseSpeed;
        private Color _baseColor;
        private bool _isPooled;

        private static Material GetSharedWaveMaterial()
        {
            if (_sharedWaveMat != null) return _sharedWaveMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _sharedWaveMat = new Material(sh) { color = Color.white };
            return _sharedWaveMat;
        }

        public static void SpawnRadarWave(Vector3 center, float maxRadius, Color color)
        {
            Spawn(center, 0.35f, maxRadius, 0.95f, 0.05f, color, 0.14f);
            Spawn(center + Vector3.up * 0.12f, 0.20f, maxRadius * 0.65f, 0.72f, 0.08f, color, 0.09f);
        }

        public static void SpawnSoundRipple(Vector3 center, Color color)
        {
            Spawn(center, 0.18f, 2.4f, 0.85f, 0.65f, color, 0.07f);
            Spawn(center + Vector3.up * 0.16f, 0.12f, 1.55f, 0.62f, 0.85f, Palette.AccentGold, 0.05f);
        }

        public static void SpawnStunBurst(Vector3 center)
        {
            Spawn(center, 0.25f, 3.3f, 0.55f, 0.25f, Palette.AccentGold, 0.13f);
            Spawn(center + Vector3.up * 0.22f, 0.18f, 2.2f, 0.45f, 0.45f, Palette.PropAccent, 0.09f);
        }

        private static void Spawn(Vector3 center, float r0, float r1, float dur, float riseSpeed, Color col, float width)
        {
            var pool = ObjectPoolManager.Instance;
            ExpandingWaveEffect fx;
            bool pooled = false;
            if (pool != null)
            {
                fx = pool.AcquireWaveEffect();
                pooled = true;
            }
            else
            {
                var go = new GameObject("WaveRingFX");
                fx = go.AddComponent<ExpandingWaveEffect>();
            }
            fx.transform.position = center;
            fx.Init(r0, r1, dur, riseSpeed, col, width, pooled);
        }

        private void Init(float r0, float r1, float dur, float rise, Color col, float width, bool pooled)
        {
            _isPooled = pooled;
            _elapsed = 0f;
            _startRadius = r0;
            _endRadius = r1;
            _duration = Mathf.Max(0.15f, dur);
            _riseSpeed = rise;
            _baseColor = col;

            if (_lr == null)
            {
                _lr = GetComponent<LineRenderer>();
                if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();
                _lr.useWorldSpace = false;
                _lr.loop = true;
                _lr.positionCount = 40;
                _lr.sharedMaterial = GetSharedWaveMaterial();
                _lr.shadowCastingMode = ShadowCastingMode.Off;
                _lr.receiveShadows = false;
            }
            _lr.startWidth = width;
            _lr.endWidth = width;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            UpdateRing(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(_elapsed / _duration);
            if (_riseSpeed > 0.001f) transform.position += Vector3.up * (_riseSpeed * Time.deltaTime);
            UpdateRing(u);
            if (_elapsed >= _duration)
            {
                if (_isPooled && ObjectPoolManager.Instance != null)
                    ObjectPoolManager.Instance.ReturnWaveEffect(this);
                else
                    Destroy(gameObject);
            }
        }

        private void UpdateRing(float u)
        {
            if (_lr == null) return;
            float r = Mathf.Lerp(_startRadius, _endRadius, Ease.OutCubic(u));
            int count = _lr.positionCount;
            for (int i = 0; i < count; i++)
            {
                float a = (float)i / count * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            Color c = _baseColor;
            c.a = (1f - u) * 0.85f;
            _lr.startColor = c;
            _lr.endColor = c;
            if (_mpb != null)
            {
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _lr.SetPropertyBlock(_mpb);
            }
        }
    }

    /// <summary>
    /// Subtle glowing ground highlight ring shown under an aimed morphable prop.
    /// </summary>
    public sealed class MorphTargetHighlighter
    {
        private GameObject _ringGo;
        private LineRenderer _lr;

        public void UpdateTarget(Vector3? targetCenter, float radius)
        {
            if (!targetCenter.HasValue)
            {
                if (_ringGo != null && _ringGo.activeSelf) _ringGo.SetActive(false);
                return;
            }
            if (_ringGo == null) BuildRing();
            _ringGo.SetActive(true);
            _ringGo.transform.position = targetCenter.Value;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.06f;
            float r = Mathf.Clamp(radius, 0.24f, 1.05f) * pulse;
            int count = _lr.positionCount;
            for (int i = 0; i < count; i++)
            {
                float a = (float)i / count * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.02f, Mathf.Sin(a) * r));
            }
        }

        private void BuildRing()
        {
            _ringGo = new GameObject("MorphTargetRingFX");
            _lr = _ringGo.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.positionCount = 32;
            _lr.startWidth = 0.035f;
            _lr.endWidth = 0.035f;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh) { color = Palette.PropAccent };
            _lr.sharedMaterial = mat;
            _lr.startColor = Palette.PropAccent;
            _lr.endColor = Palette.PropAccent;
            _lr.shadowCastingMode = ShadowCastingMode.Off;
            _lr.receiveShadows = false;
        }
    }

    /// <summary>
    /// Celebratory confetti burst triggered when a match concludes.
    /// </summary>
    public static class VictoryConfettiFx
    {
        private static Material _sharedConfettiMat;

        private static Material GetSharedMaterial()
        {
            if (_sharedConfettiMat != null) return _sharedConfettiMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _sharedConfettiMat = new Material(sh) { color = Palette.AccentGold };
            return _sharedConfettiMat;
        }

        public static ParticleSystem CreatePooledInstance(Transform parent)
        {
            var go = new GameObject("VictoryConfettiBurst_Pooled");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 2.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.gravityModifier = 0.55f;
            main.maxParticles = 60;
            main.stopAction = ParticleSystemStopAction.None;
            main.startColor = new ParticleSystem.MinMaxGradient(Palette.AccentGold, Palette.PropAccent);

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });

            var shObj = ps.shape;
            shObj.shapeType = ParticleSystemShapeType.Cone;
            shObj.angle = 38f;
            shObj.radius = 0.35f;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = GetSharedMaterial();
            go.SetActive(false);
            return ps;
        }

        public static void SpawnAt(Vector3 worldPos)
        {
            if (ObjectPoolManager.Instance != null)
            {
                ObjectPoolManager.Instance.PlayVictoryConfetti(worldPos);
                return;
            }
            var ps = CreatePooledInstance(null);
            ps.transform.position = worldPos;
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ps.gameObject.SetActive(true);
            ps.Play();
        }
    }

    /// <summary>
    /// Persistent Low / Medium / High mobile graphics quality controller.
    /// Adjusts URP renderScale, HDR, MSAA, SSAO, Bloom, Mipmap Streaming, and frame rate target cleanly.
    /// </summary>
    public static class GraphicsQualityController
    {
        private const string PrefQualityKey = "CoachingPropHunt_QualityTier";

        public static GraphicsPresetTier CurrentTier { get; private set; } = GraphicsPresetTier.Medium;

        public static void LoadAndApply()
        {
            int saved = PlayerPrefs.GetInt(PrefQualityKey, (int)GraphicsPresetTier.Medium);
            Apply((GraphicsPresetTier)Mathf.Clamp(saved, 0, 2), save: false);
        }

        public static void Apply(GraphicsPresetTier tier, bool save = true)
        {
            CurrentTier = tier;
            if (save)
            {
                PlayerPrefs.SetInt(PrefQualityKey, (int)tier);
                PlayerPrefs.Save();
            }

            QualitySettings.vSyncCount = 0;
            QualitySettings.streamingMipmapsActive = true;
            QualitySettings.streamingMipmapsMemoryBudget = tier == GraphicsPresetTier.Low ? 96f : (tier == GraphicsPresetTier.Medium ? 128f : 160f);
            QualitySettings.lodBias = tier == GraphicsPresetTier.Low ? 0.70f : (tier == GraphicsPresetTier.Medium ? 0.85f : 1.0f);

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                // Thermal-optimized renderScale for Mobile: 0.85x looks crisp on modern DPI while cutting GPU workload ~30%
                urp.renderScale = tier == GraphicsPresetTier.Low ? 0.75f : (tier == GraphicsPresetTier.Medium ? 0.85f : 0.95f);
                urp.supportsHDR = false; // Disable heavy HDR FP16 buffers on mobile to eliminate heat
                urp.msaaSampleCount = 1; // 1 sample (MSAA disabled, uses fast post-FXAA)
                urp.shadowDistance = tier == GraphicsPresetTier.Low ? 22f : (tier == GraphicsPresetTier.Medium ? 30f : 40f);
                urp.maxAdditionalLightsCount = tier == GraphicsPresetTier.Low ? 1 : (tier == GraphicsPresetTier.Medium ? 2 : 3);
            }

            if (Application.isPlaying)
            {
                var ssaoFeatures = Resources.FindObjectsOfTypeAll<ScreenSpaceAmbientOcclusion>();
                for (int i = 0; i < ssaoFeatures.Length; i++)
                {
                    if (ssaoFeatures[i] != null)
                        ssaoFeatures[i].SetActive(tier == GraphicsPresetTier.High);
                }
            }

            var vol = Object.FindFirstObjectByType<Volume>();
            if (vol != null && vol.sharedProfile != null && vol.sharedProfile.TryGet(out Bloom bloom))
            {
                bloom.active = tier != GraphicsPresetTier.Low;
                bloom.intensity.value = tier == GraphicsPresetTier.High ? 0.34f : 0.20f;
            }
        }
    }

    /// <summary>
    /// Crisp short-lived Blaster tracer beam spawned from the Hunter's gun tip (pooled zero-GC).
    /// </summary>
    public sealed class BlasterTracerFx : MonoBehaviour
    {
        private static Material _sharedTracerMat;
        private LineRenderer _lr;
        private MaterialPropertyBlock _mpb;
        private Color _baseCol;
        private float _elapsed;
        private bool _isPooled;
        private const float Duration = 0.14f;

        private static Material GetSharedTracerMaterial()
        {
            if (_sharedTracerMat != null) return _sharedTracerMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _sharedTracerMat = new Material(sh) { color = Color.white };
            return _sharedTracerMat;
        }

        public static void Spawn(Vector3 startPos, Vector3 endPos, bool hitProp)
        {
            var pool = ObjectPoolManager.Instance;
            BlasterTracerFx fx;
            bool pooled = false;
            if (pool != null)
            {
                fx = pool.AcquireBlasterTracer();
                pooled = true;
            }
            else
            {
                var go = new GameObject("BlasterTracerFX");
                fx = go.AddComponent<BlasterTracerFx>();
            }
            fx.Init(startPos, endPos, hitProp ? Palette.SuccessEmerald : Palette.HunterAccent, pooled);
        }

        private void Init(Vector3 a, Vector3 b, Color col, bool pooled)
        {
            _isPooled = pooled;
            _elapsed = 0f;
            _baseCol = col;
            if (_lr == null)
            {
                _lr = GetComponent<LineRenderer>();
                if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();
                _lr.useWorldSpace = true;
                _lr.positionCount = 2;
                _lr.shadowCastingMode = ShadowCastingMode.Off;
                _lr.receiveShadows = false;
                _lr.sharedMaterial = GetSharedTracerMaterial();
            }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _lr.SetPosition(0, a);
            _lr.SetPosition(1, b);
            _lr.startWidth = 0.055f;
            _lr.endWidth = 0.028f;
            _lr.startColor = col;
            _lr.endColor = Palette.AccentGold;
            _mpb.SetColor("_BaseColor", col);
            _mpb.SetColor("_Color", col);
            _lr.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(_elapsed / Duration);
            Color c = _baseCol;
            c.a = 1f - u;
            if (_lr != null)
            {
                _lr.startColor = c;
                _lr.endColor = c;
                _lr.startWidth = Mathf.Lerp(0.055f, 0.01f, u);
                _lr.endWidth = Mathf.Lerp(0.028f, 0.005f, u);
                if (_mpb != null)
                {
                    _mpb.SetColor("_BaseColor", c);
                    _mpb.SetColor("_Color", c);
                    _lr.SetPropertyBlock(_mpb);
                }
            }
            if (_elapsed >= Duration)
            {
                if (_isPooled && ObjectPoolManager.Instance != null)
                    ObjectPoolManager.Instance.ReturnBlasterTracer(this);
                else
                    Destroy(gameObject);
            }
        }
    }
}
