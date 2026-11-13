using System;
using System.Collections.Generic;
using CoachingPropHunt.Props;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoachingPropHunt.Core
{
    /// <summary>
    /// Centralized zero-GC object pool for runtime FX (blaster tracers, expanding wave rings,
    /// victory confetti), pre-stripped morph prop visuals, and decoy prop clones.
    /// Eliminates Instantiate / Destroy and Material allocation spikes on Android.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectPoolManager : MonoBehaviour
    {
        public static ObjectPoolManager Instance { get; private set; }

        private Transform _poolRoot;
        private readonly Stack<BlasterTracerFx> _tracerPool = new Stack<BlasterTracerFx>(24);
        private readonly Stack<ExpandingWaveEffect> _wavePool = new Stack<ExpandingWaveEffect>(20);
        private readonly Stack<ParticleSystem> _confettiPool = new Stack<ParticleSystem>(4);

        private readonly Dictionary<string, Stack<GameObject>> _morphVisualPools = new Dictionary<string, Stack<GameObject>>(32);
        private readonly Dictionary<string, Stack<GameObject>> _decoyPools = new Dictionary<string, Stack<GameObject>>(24);
        private readonly Dictionary<int, GameObject> _activeDecoysByNetId = new Dictionary<int, GameObject>(16);
        private readonly List<GameObject> _allActiveDecoys = new List<GameObject>(16);

        private int _nextLocalDecoyId = 10000;

        public int PooledTracerCount => _tracerPool.Count;
        public int PooledWaveCount => _wavePool.Count;
        public int ActiveDecoyCount => _allActiveDecoys.Count;

        public static ObjectPoolManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<ObjectPoolManager>();
            if (existing != null)
            {
                Instance = existing;
                existing.EnsureRoot();
                return existing;
            }
            var go = new GameObject("ObjectPoolManager");
            return go.AddComponent<ObjectPoolManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EnsureRoot();
            PrewarmDefaultPools();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureRoot()
        {
            if (_poolRoot != null) return;
            var holder = new GameObject("_PooledInactive");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);
            _poolRoot = holder.transform;
        }

        public void PrewarmDefaultPools()
        {
            EnsureRoot();
            while (_tracerPool.Count < 16)
            {
                var go = new GameObject("BlasterTracerFX_Pooled");
                go.transform.SetParent(_poolRoot, false);
                go.SetActive(false);
                go.AddComponent<LineRenderer>();
                var fx = go.AddComponent<BlasterTracerFx>();
                _tracerPool.Push(fx);
            }

            while (_wavePool.Count < 12)
            {
                var go = new GameObject("WaveRingFX_Pooled");
                go.transform.SetParent(_poolRoot, false);
                go.SetActive(false);
                go.AddComponent<LineRenderer>();
                var fx = go.AddComponent<ExpandingWaveEffect>();
                _wavePool.Push(fx);
            }

            while (_confettiPool.Count < 2)
            {
                var ps = VictoryConfettiFx.CreatePooledInstance(_poolRoot);
                _confettiPool.Push(ps);
            }
        }

        public BlasterTracerFx AcquireBlasterTracer()
        {
            EnsureRoot();
            BlasterTracerFx fx = null;
            while (_tracerPool.Count > 0 && fx == null)
                fx = _tracerPool.Pop();

            if (fx == null)
            {
                var go = new GameObject("BlasterTracerFX_Pooled");
                fx = go.AddComponent<BlasterTracerFx>();
            }
            fx.transform.SetParent(null, false);
            fx.gameObject.SetActive(true);
            return fx;
        }

        public void ReturnBlasterTracer(BlasterTracerFx fx)
        {
            if (fx == null) return;
            EnsureRoot();
            fx.gameObject.SetActive(false);
            fx.transform.SetParent(_poolRoot, false);
            _tracerPool.Push(fx);
        }

        public ExpandingWaveEffect AcquireWaveEffect()
        {
            EnsureRoot();
            ExpandingWaveEffect fx = null;
            while (_wavePool.Count > 0 && fx == null)
                fx = _wavePool.Pop();

            if (fx == null)
            {
                var go = new GameObject("WaveRingFX_Pooled");
                fx = go.AddComponent<ExpandingWaveEffect>();
            }
            fx.transform.SetParent(null, false);
            fx.gameObject.SetActive(true);
            return fx;
        }

        public void ReturnWaveEffect(ExpandingWaveEffect fx)
        {
            if (fx == null) return;
            EnsureRoot();
            fx.gameObject.SetActive(false);
            fx.transform.SetParent(_poolRoot, false);
            _wavePool.Push(fx);
        }

        public void PlayVictoryConfetti(Vector3 worldPos)
        {
            EnsureRoot();
            ParticleSystem ps = null;
            while (_confettiPool.Count > 0 && ps == null)
            {
                var candidate = _confettiPool.Pop();
                if (candidate != null && !candidate.isPlaying)
                    ps = candidate;
            }
            if (ps == null) ps = VictoryConfettiFx.CreatePooledInstance(_poolRoot);
            ps.transform.SetParent(null, false);
            ps.transform.position = worldPos;
            ps.gameObject.SetActive(true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
            StartCoroutine(ReclaimConfettiAfterDelay(ps, 3.0f));
        }

        private System.Collections.IEnumerator ReclaimConfettiAfterDelay(ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ps == null || this == null) yield break;
            EnsureRoot();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            ps.transform.SetParent(_poolRoot, false);
            _confettiPool.Push(ps);
        }

        public GameObject AcquirePropVisual(PropDefinition def, Transform parent)
        {
            if (def == null || def.prefab == null) return null;
            EnsureRoot();

            if (_morphVisualPools.TryGetValue(def.propId, out var stack))
            {
                while (stack.Count > 0)
                {
                    var cached = stack.Pop();
                    if (cached == null) continue;
                    cached.transform.SetParent(parent, false);
                    cached.transform.localPosition = Vector3.zero;
                    cached.transform.localRotation = Quaternion.identity;
                    cached.transform.localScale = Vector3.one;
                    cached.SetActive(true);
                    return cached;
                }
            }

            var visual = Instantiate(def.prefab, parent);
            visual.name = $"PlayerProp_{def.propId}";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            StripForVisualOnly(visual);
            return visual;
        }

        public void ReleasePropVisual(string propId, GameObject visual)
        {
            if (visual == null) return;
            EnsureRoot();
            if (string.IsNullOrEmpty(propId))
            {
                Destroy(visual);
                return;
            }
            if (!_morphVisualPools.TryGetValue(propId, out var stack))
            {
                stack = new Stack<GameObject>(4);
                _morphVisualPools[propId] = stack;
            }
            visual.SetActive(false);
            visual.transform.SetParent(_poolRoot, false);
            stack.Push(visual);
        }

        public int AllocateDecoyNetId()
        {
            _nextLocalDecoyId++;
            if (_nextLocalDecoyId > 2000000) _nextLocalDecoyId = 10000;
            return _nextLocalDecoyId;
        }

        public GameObject AcquireDecoy(PropDefinition def, Vector3 pos, Quaternion rot, int decoyNetId = 0)
        {
            if (def == null || def.prefab == null) return null;
            EnsureRoot();

            GameObject decoy = null;
            if (_decoyPools.TryGetValue(def.propId, out var stack))
            {
                while (stack.Count > 0 && decoy == null)
                    decoy = stack.Pop();
            }

            if (decoy == null)
            {
                decoy = Instantiate(def.prefab, pos, rot);
                decoy.name = $"Decoy_{def.propId}";
                foreach (var mr in decoy.GetComponentsInChildren<Renderer>(true))
                {
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }
            }
            else
            {
                decoy.transform.SetParent(null, false);
                decoy.transform.SetPositionAndRotation(pos, rot);
                decoy.transform.localScale = Vector3.one;
            }

            if (decoyNetId == 0) decoyNetId = AllocateDecoyNetId();

            var ip = decoy.GetComponent<InteractiveProp>();
            if (ip == null) ip = decoy.AddComponent<InteractiveProp>();
            ip.Configure(def.propId, "Decoy", decoy: true, ttlSeconds: 22f, netId: decoyNetId);
            decoy.SetActive(true);

            if (!_allActiveDecoys.Contains(decoy)) _allActiveDecoys.Add(decoy);
            _activeDecoysByNetId[decoyNetId] = decoy;
            return decoy;
        }

        public GameObject FindActiveDecoyByNetId(int decoyNetId)
        {
            if (decoyNetId == 0) return null;
            if (_activeDecoysByNetId.TryGetValue(decoyNetId, out var go) && go != null && go.activeInHierarchy)
                return go;
            return null;
        }

        public void ReleaseDecoy(GameObject decoy)
        {
            if (decoy == null) return;
            EnsureRoot();
            var ip = decoy.GetComponent<InteractiveProp>();
            if (ip != null && ip.DecoyNetId != 0)
                _activeDecoysByNetId.Remove(ip.DecoyNetId);

            _allActiveDecoys.Remove(decoy);
            string id = ip != null ? ip.PropId : "";
            if (string.IsNullOrEmpty(id))
            {
                Destroy(decoy);
                return;
            }

            if (!_decoyPools.TryGetValue(id, out var stack))
            {
                stack = new Stack<GameObject>(4);
                _decoyPools[id] = stack;
            }
            ip.Configure(id, "Decoy", decoy: false, ttlSeconds: 0f, netId: 0);
            decoy.SetActive(false);
            decoy.transform.SetParent(_poolRoot, false);
            stack.Push(decoy);
        }

        public void ReleaseAllActiveDecoys()
        {
            for (int i = _allActiveDecoys.Count - 1; i >= 0; i--)
            {
                var d = _allActiveDecoys[i];
                if (d != null) ReleaseDecoy(d);
            }
            _allActiveDecoys.Clear();
            _activeDecoysByNetId.Clear();
        }

        public static void StripForVisualOnly(GameObject visual)
        {
            if (visual == null) return;
            foreach (var ip in visual.GetComponentsInChildren<InteractiveProp>(true))
            {
                ip.enabled = false;
                Destroy(ip);
            }
            foreach (var col in visual.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
                Destroy(col);
            }
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 0; // Default layer so player morphed visual is never short-culled
            }
            foreach (var mr in visual.GetComponentsInChildren<Renderer>(true))
            {
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }
    }
}
