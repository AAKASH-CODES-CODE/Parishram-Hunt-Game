using System;
using System.Collections;
using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoachingPropHunt.CameraRig
{
    /// <summary>
    /// Cinematic Last-Bullet Kill-Cam for Parishram Hunt:
    /// Triggers slow-motion bullet tracking when a Hunter fires the fatal 4th shot (25 DMG -> 0 HP).
    /// Camera cuts behind the Hunter's shoulder, smoothly follows the chalk bullet in slow motion,
    /// captures the high-impact shatter on the disguised Prop, and transitions to Spectator mode.
    /// </summary>
    public sealed class KillCamController : MonoBehaviour
    {
        public static KillCamController Instance { get; private set; }

        private UnityEngine.Camera _cam;
        private GameObject _bulletGo;
        private TrailRenderer _bulletTrail;
        private bool _isKillCamActive;

        public static event Action OnKillCamStarted;
        public static event Action OnKillCamFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance == null)
            {
                var go = new GameObject("KillCamController");
                Instance = go.AddComponent<KillCamController>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildCameraAndBullet();
        }

        private void BuildCameraAndBullet()
        {
            // Dedicated Kill-Cam Camera
            var camGo = new GameObject("KillCamCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<UnityEngine.Camera>();
            _cam.clearFlags = CameraClearFlags.Depth;
            _cam.depth = 20; // Renders on top of gameplay camera
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 120f;
            camGo.SetActive(false);

            // Procedural Chalk Bullet Projectile
            _bulletGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _bulletGo.name = "KillCamBullet";
            _bulletGo.transform.SetParent(transform, false);
            var col = _bulletGo.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _bulletGo.transform.localScale = new Vector3(0.06f, 0.22f, 0.06f);

            var mr = _bulletGo.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(sh) { color = new Color(1f, 0.95f, 0.85f, 1f) };
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            // Glowing Chalk Trail
            _bulletTrail = _bulletGo.AddComponent<TrailRenderer>();
            _bulletTrail.time = 0.35f;
            _bulletTrail.startWidth = 0.09f;
            _bulletTrail.endWidth = 0.01f;
            _bulletTrail.sharedMaterial = mat;

            _bulletGo.SetActive(false);
        }

        public void TriggerKillCam(Vector3 muzzlePos, Vector3 targetHitPos, PlayerParticipant victimProp, PlayerParticipant attackerHunter, Action onFinishedCallback)
        {
            if (_isKillCamActive) return;
            StartCoroutine(RoutineKillCam(muzzlePos, targetHitPos, victimProp, attackerHunter, onFinishedCallback));
        }

        private IEnumerator RoutineKillCam(Vector3 muzzlePos, Vector3 targetHitPos, PlayerParticipant victimProp, PlayerParticipant attackerHunter, Action onFinished)
        {
            _isKillCamActive = true;
            OnKillCamStarted?.Invoke();

            // 1. Enter Slow-Motion
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0.22f;
            Time.fixedDeltaTime = 0.02f * 0.22f;

            // 2. Position Bullet and Camera Behind Hunter Shoulder
            Vector3 flightDir = (targetHitPos - muzzlePos).normalized;
            if (flightDir.sqrMagnitude < 0.001f) flightDir = Vector3.forward;

            Vector3 camRight = Vector3.Cross(Vector3.up, flightDir).normalized;
            Vector3 camStartPos = muzzlePos - flightDir * 1.25f + Vector3.up * 0.35f + camRight * 0.38f;

            _bulletGo.transform.position = muzzlePos;
            _bulletGo.transform.rotation = Quaternion.LookRotation(flightDir) * Quaternion.Euler(90f, 0f, 0f);
            _bulletGo.SetActive(true);
            _bulletTrail.Clear();

            _cam.transform.position = camStartPos;
            _cam.transform.rotation = Quaternion.LookRotation(flightDir);
            _cam.gameObject.SetActive(true);

            // Audio & Screen Flash
            AudioSynthManager.Instance?.PlayBlasterShot();
            GameStateManager.Instance?.TriggerFlash(Palette.AccentGold, 0.30f);

            string hunterName = attackerHunter != null ? attackerHunter.DisplayName : "Hunter";
            string victimName = victimProp != null ? victimProp.DisplayName : "Prop";
            GameStateManager.Instance?.ShowToast($"FINAL SHOT! {hunterName} tagged {victimName}", Palette.AccentGold);

            // 3. Bullet Flight & Camera Tracking (0.9 real seconds of slow-mo flight)
            float flightRealDuration = 0.95f;
            float elapsedReal = 0f;

            while (elapsedReal < flightRealDuration)
            {
                elapsedReal += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsedReal / flightRealDuration);
                float curvedT = t * (2f - t); // OutQuad easing curve

                Vector3 currentBulletPos = Vector3.Lerp(muzzlePos, targetHitPos, curvedT);
                _bulletGo.transform.position = currentBulletPos;

                // Camera follows 0.65m behind bullet with dynamic look-ahead
                Vector3 camPos = currentBulletPos - flightDir * 0.85f + Vector3.up * 0.24f + camRight * 0.22f;
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, camPos, Time.unscaledDeltaTime * 12f);
                _cam.transform.LookAt(Vector3.Lerp(currentBulletPos, targetHitPos, 0.6f));

                yield return null;
            }

            // 4. Bullet Impact & Shatter VFX
            _bulletGo.transform.position = targetHitPos;
            _bulletGo.SetActive(false);

            ExpandingWaveEffect.SpawnStunBurst(targetHitPos);
            AudioSynthManager.Instance?.PlayWrongHitBuzz();
            GameStateManager.Instance?.TriggerFlash(Palette.DangerRed, 0.40f);

            // Victim prop dramatic collapse / elimination
            if (victimProp != null)
            {
                victimProp.TakeDamage(100, hunterName);
            }

            // 5. Cinematic Linger View (1.0 real second)
            float lingerDuration = 1.1f;
            float lingerElapsed = 0f;
            while (lingerElapsed < lingerDuration)
            {
                lingerElapsed += Time.unscaledDeltaTime;
                // Gentle slow cinematic drift around impact point
                _cam.transform.RotateAround(targetHitPos, Vector3.up, 18f * Time.unscaledDeltaTime);
                _cam.transform.LookAt(targetHitPos + Vector3.up * 0.4f);
                yield return null;
            }

            // 6. Restore Normal Time Scale & Cleanup
            Time.timeScale = previousTimeScale;
            Time.fixedDeltaTime = 0.02f;

            _cam.gameObject.SetActive(false);
            _isKillCamActive = false;
            OnKillCamFinished?.Invoke();

            onFinished?.Invoke();
        }
    }
}
