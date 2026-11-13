using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using CoachingPropHunt.Props;
using Unity.Cinemachine;
using UnityEngine;

namespace CoachingPropHunt.CameraRig
{
    /// <summary>
    /// Cinemachine 3 camera controller managing 3 seamless modes:
    /// 1) TitleOrbit: Scenic exterior/cutaway architectural sweep of the 2-floor Coaching Center.
    /// 2) FirstPersonHunter: Crisp eye-level FPS rig with micro-bob & impulse feedback.
    /// 3) ThirdPersonOrbitProp: Wall-safe interior 3rd-person orbit that ignores small props so it
    ///    never collapses inside tight desks or corners.
    /// </summary>
    public sealed class PlayerCameraController : MonoBehaviour
    {
        public static PlayerCameraController Instance { get; private set; }

        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private float propOrbitDistance = 3.1f;
        [SerializeField] private float propShoulderHeight = 0.85f;
        [SerializeField] private float hunterOrbitDistance = 2.25f;
        [SerializeField] private float hunterShoulderHeight = 1.44f;
        [SerializeField] private float hunterTpsFov = 60f;
        [SerializeField] private float propTpsFov = 58f;
        [SerializeField] private float titleFov = 54f;

        private Transform _cameraFollowTarget;
        private Vector3 _smoothTargetPos;
        private float _titleOrbitAngle;
        private int _spectatorIndex;
        private PlayerParticipant _spectatingTarget;
        private bool _hasTarget;
        private float _spectatorYaw;
        private float _spectatorPitch = 14f;
        private int _lastSpectatedId;

        public PlayerParticipant CurrentSpectatorTarget => _spectatingTarget;

        public void AddSpectatorOrbitDelta(Vector2 delta)
        {
            _spectatorYaw = Mathf.Repeat(_spectatorYaw + delta.x, 360f);
            _spectatorPitch = Mathf.Clamp(_spectatorPitch - delta.y, -28f, 62f);
        }

        public void ConfigureCameraCulling(UnityEngine.Camera cam)
        {
            if (cam == null) return;
            cam.useOcclusionCulling = true;
            cam.farClipPlane = 85f;

            var cull = new float[32];
            int smallLayer = LayerMask.NameToLayer("SmallProp");
            int medLayer = LayerMask.NameToLayer("MediumProp");
            if (smallLayer >= 0 && smallLayer < 32) cull[smallLayer] = 28f;
            if (medLayer >= 0 && medLayer < 32) cull[medLayer] = 46f;
            cam.layerCullDistances = cull;
            cam.layerCullSpherical = true;
        }

        public void CycleSpectatorTarget(int step = 1)
        {
            var aliveList = GetSpectateCandidates(PlayerParticipant.LocalPlayer);
            if (aliveList.Count == 0)
            {
                _spectatingTarget = null;
                return;
            }
            _spectatorIndex = ((_spectatorIndex + step) % aliveList.Count + aliveList.Count) % aliveList.Count;
            _spectatingTarget = aliveList[_spectatorIndex];
            _hasTarget = false;
        }

        private static System.Collections.Generic.List<PlayerParticipant> GetSpectateCandidates(PlayerParticipant local)
        {
            var props = new System.Collections.Generic.List<PlayerParticipant>();
            var others = new System.Collections.Generic.List<PlayerParticipant>();
            for (int i = 0; i < PlayerParticipant.AllActive.Count; i++)
            {
                var p = PlayerParticipant.AllActive[i];
                if (p == null || !p.IsAlive || p == local) continue;
                if (local == null || p.Role == local.Role) props.Add(p);
                else others.Add(p);
            }
            return props.Count > 0 ? props : others;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            var holder = new GameObject("CinemachineFollowProxy");
            holder.transform.SetParent(transform, false);
            _cameraFollowTarget = holder.transform;

            EnsureCameraRig();
        }

        private void EnsureCameraRig()
        {
            var mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                mainCam = camGo.AddComponent<UnityEngine.Camera>();
                camGo.AddComponent<AudioListener>();
            }
            if (mainCam.GetComponent<CinemachineBrain>() == null)
                mainCam.gameObject.AddComponent<CinemachineBrain>();
            ConfigureCameraCulling(mainCam);

            if (virtualCamera == null)
            {
                var vcamGo = new GameObject("CM_GameplayCamera");
                vcamGo.transform.SetParent(transform, false);
                virtualCamera = vcamGo.AddComponent<CinemachineCamera>();
                vcamGo.AddComponent<CinemachineImpulseListener>();
            }
            virtualCamera.Follow = _cameraFollowTarget;
            virtualCamera.LookAt = _cameraFollowTarget;
            var lens = virtualCamera.Lens;
            lens.FarClipPlane = 85f;
            virtualCamera.Lens = lens;
        }

        private void LateUpdate()
        {
            if (virtualCamera == null) return;
            var gsm = GameStateManager.Instance;
            var local = PlayerParticipant.LocalPlayer;

            if (gsm == null || gsm.ShellState == AppShellState.Title || gsm.ShellState == AppShellState.RoomLobby || local == null)
            {
                UpdateTitleOrSpectatorOrbit(null);
                return;
            }

            if (!local.IsAlive)
            {
                UpdateSpectatorCamera(local);
                return;
            }

            _spectatingTarget = null;
            if (local.Role == TeamRole.Hunter)
            {
                UpdateHunterThirdPerson(local);
            }
            else
            {
                UpdatePropThirdPerson(local);
            }
        }

        private void UpdateHunterThirdPerson(PlayerParticipant hunter)
        {
            virtualCamera.Lens.FieldOfView = Mathf.Lerp(virtualCamera.Lens.FieldOfView, hunterTpsFov, Time.deltaTime * 10f);
            Vector3 pivot = hunter.transform.position + new Vector3(0f, hunterShoulderHeight, 0f);
            _smoothTargetPos = !_hasTarget ? pivot : Vector3.Lerp(_smoothTargetPos, pivot, Time.deltaTime * 22f);
            _hasTarget = true;

            Quaternion orbitRot = Quaternion.Euler(hunter.ViewPitchDeg, hunter.ViewYawDeg, 0f);
            Vector3 desiredOffset = orbitRot * new Vector3(0.42f, 0.18f, -hunterOrbitDistance);
            Vector3 aimFocus = _smoothTargetPos + orbitRot * (Vector3.forward * 10.5f);
            PlaceWallSafeThirdPersonCamera(hunter, _smoothTargetPos, desiredOffset, aimFocus);
        }

        private void UpdatePropThirdPerson(PlayerParticipant prop)
        {
            UpdatePropThirdPersonWithOrbit(prop, prop.ViewYawDeg, prop.ViewPitchDeg);
        }

        private void PlaceWallSafeThirdPersonCamera(PlayerParticipant subject, Vector3 pivot, Vector3 desiredOffset, Vector3 lookTarget)
        {
            float maxDist = desiredOffset.magnitude;
            Vector3 dir = desiredOffset / Mathf.Max(0.001f, maxDist);
            var hits = Physics.SphereCastAll(pivot, 0.16f, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);
            float safeDist = maxDist;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(subject.transform)) continue;
                if (h.collider.GetComponentInParent<InteractiveProp>() != null) continue;
                if (h.collider.GetComponentInParent<PlayerParticipant>() != null) continue;
                if (h.distance < safeDist) safeDist = Mathf.Max(0.14f, h.distance * 0.85f);
            }

            Vector3 finalPos = pivot + dir * safeDist;
            Vector3 lookDir = lookTarget - finalPos;
            Quaternion lookAtRot = lookDir.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : Quaternion.Euler(subject.ViewPitchDeg, subject.ViewYawDeg, 0f);
            virtualCamera.transform.SetPositionAndRotation(finalPos, lookAtRot);
            _cameraFollowTarget.position = pivot;
        }

        public float SpectatorYaw => _spectatorYaw;

        private void UpdateSpectatorCamera(PlayerParticipant local)
        {
            if (_spectatingTarget == null || !_spectatingTarget.IsAlive)
            {
                var list = GetSpectateCandidates(local);
                if (list.Count > 0)
                {
                    _spectatorIndex = ((_spectatorIndex % list.Count) + list.Count) % list.Count;
                    _spectatingTarget = list[_spectatorIndex];
                }
                else
                {
                    _spectatingTarget = null;
                }
            }

            if (_spectatingTarget != null && _spectatingTarget.IsAlive)
            {
                int targetId = _spectatingTarget.GetInstanceID();
                if (targetId != _lastSpectatedId)
                {
                    _lastSpectatedId = targetId;
                    _spectatorYaw = _spectatingTarget.ViewYawDeg;
                    _spectatorPitch = 14f;
                }
                UpdatePropThirdPersonWithOrbit(_spectatingTarget, _spectatorYaw, _spectatorPitch);
            }
            else
            {
                UpdateTitleOrSpectatorOrbit(local);
            }
        }

        private void UpdatePropThirdPersonWithOrbit(PlayerParticipant prop, float yawDeg, float pitchDeg)
        {
            virtualCamera.Lens.FieldOfView = Mathf.Lerp(virtualCamera.Lens.FieldOfView, propTpsFov, Time.deltaTime * 8f);
            float pivotHeight = prop.MorphCtrl != null && prop.MorphCtrl.IsMorphed && prop.MorphCtrl.CurrentProp != null
                ? Mathf.Clamp(prop.MorphCtrl.CurrentProp.boundsSize.y * 0.65f + 0.35f, 0.55f, 1.35f)
                : propShoulderHeight;
            Vector3 pivot = prop.transform.position + new Vector3(0f, pivotHeight, 0f);
            _smoothTargetPos = !_hasTarget ? pivot : Vector3.Lerp(_smoothTargetPos, pivot, Time.deltaTime * 16f);
            _hasTarget = true;

            Quaternion orbitRot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            Vector3 desiredOffset = orbitRot * new Vector3(0.28f, 0.25f, -propOrbitDistance);
            PlaceWallSafeThirdPersonCamera(prop, _smoothTargetPos, desiredOffset, _smoothTargetPos + Vector3.up * 0.12f);
        }

        private struct DroneShot
        {
            public Vector3 posStart;
            public Vector3 posEnd;
            public Vector3 lookStart;
            public Vector3 lookEnd;
        }

        private static readonly DroneShot[] DroneMontageShots =
        {
            // 1. Parishram Gate & Front Cycle Entry
            new DroneShot { posStart = new Vector3(1.8f, 2.3f, 16.2f), posEnd = new Vector3(-0.3f, 2.0f, 10.6f), lookStart = new Vector3(0.4f, 1.9f, 5.0f), lookEnd = new Vector3(0.4f, 1.6f, 2.0f) },
            // 2. GF Cycle Stand & Hall Sweep
            new DroneShot { posStart = new Vector3(-6.2f, 2.6f, 8.2f), posEnd = new Vector3(-5.2f, 2.1f, -3.5f), lookStart = new Vector3(-6.8f, 1.0f, -1.0f), lookEnd = new Vector3(-6.0f, 1.0f, -7.5f) },
            // 3. 1F Big Classroom (Benches, Stage & Whiteboard)
            new DroneShot { posStart = new Vector3(1.2f, 6.4f, -5.0f), posEnd = new Vector3(6.6f, 6.1f, -11.8f), lookStart = new Vector3(4.3f, 4.7f, -9.2f), lookEnd = new Vector3(3.8f, 4.8f, -5.0f) },
            // 4. 1F Classroom 2 (Widened Desk Columns Aisle)
            new DroneShot { posStart = new Vector3(-1.8f, 5.8f, -11.35f), posEnd = new Vector3(-6.8f, 5.7f, -11.35f), lookStart = new Vector3(-4.8f, 4.7f, -11.35f), lookEnd = new Vector3(-7.6f, 4.7f, -11.35f) },
            // 5. 1F Doubt Class Stations
            new DroneShot { posStart = new Vector3(2.4f, 6.0f, 8.2f), posEnd = new Vector3(-5.8f, 5.8f, 10.2f), lookStart = new Vector3(-1.2f, 4.6f, 10.2f), lookEnd = new Vector3(-4.0f, 4.6f, 9.0f) },
            // 6. 1F Self-Study Room (Cozy Cubicles & Desk Lamps)
            new DroneShot { posStart = new Vector3(-4.2f, 6.0f, -4.3f), posEnd = new Vector3(-7.4f, 5.8f, -8.1f), lookStart = new Vector3(-5.6f, 4.8f, -6.4f), lookEnd = new Vector3(-4.4f, 4.8f, -7.8f) },
            // 7. 1F Kitchen & Corridor Counters
            new DroneShot { posStart = new Vector3(7.5f, 6.1f, 5.8f), posEnd = new Vector3(2.8f, 6.0f, 2.2f), lookStart = new Vector3(6.2f, 4.8f, 2.6f), lookEnd = new Vector3(0.5f, 4.8f, 2.8f) },
            // 8. 1F Corridor & Staircase Landing Sweep
            new DroneShot { posStart = new Vector3(1.6f, 6.2f, -2.5f), posEnd = new Vector3(5.8f, 4.4f, 1.0f), lookStart = new Vector3(4.8f, 3.2f, 0.2f), lookEnd = new Vector3(2.8f, 1.8f, -1.5f) },
        };

        private float _droneElapsed;

        private void UpdateTitleOrSpectatorOrbit(PlayerParticipant fallbackTarget)
        {
            _hasTarget = false;
            virtualCamera.Lens.FieldOfView = Mathf.Lerp(virtualCamera.Lens.FieldOfView, titleFov, Time.unscaledDeltaTime * 4f);

            if (fallbackTarget != null && !fallbackTarget.IsAlive)
            {
                _titleOrbitAngle += Time.unscaledDeltaTime * 0.45f;
                Vector3 focus = new Vector3(0f, 3.2f, 0f);
                float rad = (76f + Mathf.Sin(_titleOrbitAngle) * 24f) * Mathf.Deg2Rad;
                Vector3 camPos = focus + new Vector3(Mathf.Cos(rad) * 14.5f, 2.5f, Mathf.Sin(rad) * 15.8f);
                Quaternion lookQ = Quaternion.LookRotation((focus - camPos).normalized, Vector3.up);
                virtualCamera.transform.SetPositionAndRotation(camPos, lookQ);
                _cameraFollowTarget.position = focus;
                return;
            }

            // Looping 2.0-second interior & gate Drone Montage across all Coaching rooms!
            const float shotSeconds = 2.0f;
            _droneElapsed += Time.unscaledDeltaTime;
            float totalDur = DroneMontageShots.Length * shotSeconds;
            float looped = Mathf.Repeat(_droneElapsed, totalDur);
            int idx = Mathf.Clamp(Mathf.FloorToInt(looped / shotSeconds), 0, DroneMontageShots.Length - 1);
            float t = Mathf.Clamp01((looped - idx * shotSeconds) / shotSeconds);
            float smooth = t * t * (3f - 2f * t);

            DroneShot s = DroneMontageShots[idx];
            Vector3 pos = Vector3.Lerp(s.posStart, s.posEnd, smooth);
            Vector3 look = Vector3.Lerp(s.lookStart, s.lookEnd, smooth);
            Quaternion q = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            virtualCamera.transform.SetPositionAndRotation(pos, q);
            _cameraFollowTarget.position = look;
        }
    }
}
