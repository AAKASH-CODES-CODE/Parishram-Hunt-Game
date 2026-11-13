using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoachingPropHunt.Core
{
    /// <summary>
    /// Spatial chunking & dynamic visual streaming controller for Parishram Hunt.
    /// Divides GF, 1F, and expansion floors into adjacent spatial sectors.
    /// Disables GPU rendering, fill lights, and rotating fans in far/off-floor sectors while keeping
    /// Physics Colliders & NavMesh 100% intact for remote players and AI Bots.
    /// Also performs per-sector StaticBatchingUtility.Combine on static prop visuals at startup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampusSectorStreamer : MonoBehaviour
    {
        public static CampusSectorStreamer Instance { get; private set; }

        [Serializable]
        public sealed class CampusSector
        {
            public string sectorId;
            public int floorIndex;
            public Bounds bounds;
            public string[] neighborIds = Array.Empty<string>();
            public List<Renderer> renderers = new List<Renderer>();
            public List<Light> lights = new List<Light>();
            public List<CoachingFanSpinner> fans = new List<CoachingFanSpinner>();
            public List<GameObject> staticBatchRoots = new List<GameObject>();

            [NonSerialized] public bool IsActive = true;
            [NonSerialized] public bool Batched;
        }

        [SerializeField] private List<CampusSector> sectors = new List<CampusSector>();
        [SerializeField] private float evaluateInterval = 0.20f;
        [SerializeField] private float visibilityRadius = 15.5f;

        private readonly Dictionary<string, CampusSector> _byId = new Dictionary<string, CampusSector>(12);
        private readonly HashSet<string> _visibleSet = new HashSet<string>();
        private float _nextEvaluateTime;
        private string _lastCameraSectorId = "";
        private AppShellState _lastShellState = (AppShellState)(-1);
        private bool _forceAllVisible;

        public int SectorCount => sectors.Count;
        public int ActiveSectorCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < sectors.Count; i++)
                    if (sectors[i].IsActive) c++;
                return c;
            }
        }

        public bool ForceAllVisible
        {
            get => _forceAllVisible;
            set
            {
                _forceAllVisible = value;
                _nextEvaluateTime = 0f;
            }
        }

        public IReadOnlyList<CampusSector> Sectors => sectors;

        public static string ClassifySectorId(Vector3 unityPos)
        {
            if (unityPos.y >= 7.2f)
            {
                int extraFloor = Mathf.FloorToInt((unityPos.y - 3.2f) / 4.0f) + 1;
                return unityPos.z >= 0f ? $"F{extraFloor}_Front" : $"F{extraFloor}_Back";
            }

            if (unityPos.y < 3.2f)
            {
                if (unityPos.z > 5.0f) return "GF_GateAndYard";
                if (unityPos.z >= -4.0f) return "GF_HallAndCycle";
                return "GF_OfficeAndStairs";
            }

            if (unityPos.z > 4.0f) return "1F_DoubtAndFront";
            if (unityPos.z >= -4.0f) return "1F_CorridorStaffKitchen";
            return "1F_ClassroomsAndStudy";
        }

        public void SetSectorsFromEditor(List<CampusSector> builtSectors)
        {
            sectors = builtSectors ?? new List<CampusSector>();
            RebuildLookup();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            RebuildLookup();
        }

        private void Start()
        {
            CombineStaticBatches();
            EvaluateVisibilityNow();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void RebuildLookup()
        {
            _byId.Clear();
            for (int i = 0; i < sectors.Count; i++)
            {
                var s = sectors[i];
                if (s != null && !string.IsNullOrEmpty(s.sectorId))
                    _byId[s.sectorId] = s;
            }
        }

        private void CombineStaticBatches()
        {
            for (int i = 0; i < sectors.Count; i++)
            {
                var s = sectors[i];
                if (s == null || s.Batched) continue;
                s.Batched = true;
                for (int r = 0; r < s.staticBatchRoots.Count; r++)
                {
                    var root = s.staticBatchRoots[r];
                    if (root != null)
                        StaticBatchingUtility.Combine(root);
                }
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextEvaluateTime) return;
            _nextEvaluateTime = Time.unscaledTime + evaluateInterval;
            EvaluateVisibilityNow();
        }

        public void EvaluateVisibilityNow()
        {
            if (sectors.Count == 0) return;
            var cam = UnityEngine.Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            ForceEvaluateAt(camPos);
        }

        public void ForceEvaluateAt(Vector3 camPos)
        {
            if (sectors.Count == 0) return;
            if (_byId.Count != sectors.Count) RebuildLookup();

            var shell = GameStateManager.Instance != null ? GameStateManager.Instance.ShellState : AppShellState.Playing;
            string currentId = ClassifySectorId(camPos);
            _lastShellState = shell;
            _lastCameraSectorId = currentId;

            _visibleSet.Clear();
            if (_forceAllVisible)
            {
                for (int i = 0; i < sectors.Count; i++)
                    _visibleSet.Add(sectors[i].sectorId);
            }
            else
            {
                _visibleSet.Add(currentId);
                if (_byId.TryGetValue(currentId, out var curSector) && curSector.neighborIds != null)
                {
                    for (int n = 0; n < curSector.neighborIds.Length; n++)
                        _visibleSet.Add(curSector.neighborIds[n]);
                }

                // Also keep any sector within close doorway sightline radius active
                float rSq = visibilityRadius * visibilityRadius;
                for (int i = 0; i < sectors.Count; i++)
                {
                    var s = sectors[i];
                    if (s == null) continue;
                    float distSq = s.bounds.SqrDistance(camPos);
                    if (distSq <= rSq * 0.45f)
                        _visibleSet.Add(s.sectorId);
                }

                // Exterior gate stays visible during Title exterior sweeps
                if (shell == AppShellState.Title || shell == AppShellState.RoomLobby)
                {
                    _visibleSet.Add("GF_GateAndYard");
                }
            }

            for (int i = 0; i < sectors.Count; i++)
            {
                var s = sectors[i];
                if (s == null) continue;
                bool active = _visibleSet.Contains(s.sectorId);
                ApplySectorState(s, active);
            }
        }

        public static string[] GetDefaultNeighbors(string sectorId)
        {
            switch (sectorId)
            {
                case "GF_GateAndYard":
                    return new[] { "GF_HallAndCycle", "1F_DoubtAndFront" };
                case "GF_HallAndCycle":
                    return new[] { "GF_GateAndYard", "GF_OfficeAndStairs", "1F_CorridorStaffKitchen" };
                case "GF_OfficeAndStairs":
                    return new[] { "GF_HallAndCycle", "1F_CorridorStaffKitchen", "1F_ClassroomsAndStudy" };
                case "1F_DoubtAndFront":
                    return new[] { "1F_CorridorStaffKitchen", "GF_GateAndYard" };
                case "1F_CorridorStaffKitchen":
                    return new[] { "1F_DoubtAndFront", "1F_ClassroomsAndStudy", "GF_OfficeAndStairs", "GF_HallAndCycle" };
                case "1F_ClassroomsAndStudy":
                    return new[] { "1F_CorridorStaffKitchen", "GF_OfficeAndStairs" };
                default:
                    return new[] { "1F_CorridorStaffKitchen" };
            }
        }

        private static void ApplySectorState(CampusSector s, bool active)
        {
            if (s.IsActive == active) return;
            s.IsActive = active;

            for (int i = 0; i < s.renderers.Count; i++)
            {
                var r = s.renderers[i];
                if (r != null) r.enabled = active;
            }
            for (int i = 0; i < s.lights.Count; i++)
            {
                var l = s.lights[i];
                if (l != null) l.enabled = active;
            }
            for (int i = 0; i < s.fans.Count; i++)
            {
                var f = s.fans[i];
                if (f != null) f.enabled = active;
            }
        }

        public static void TrimMemoryOnMatchExit()
        {
            ObjectPoolManager.Instance?.ReleaseAllActiveDecoys();
            Resources.UnloadUnusedAssets();
        }
    }
}
