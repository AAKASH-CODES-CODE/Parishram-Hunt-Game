using CoachingPropHunt.Core;
using CoachingPropHunt.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoachingPropHunt.Input
{
    /// <summary>
    /// Combines Mobile UI thumbstick/touch-drag look with New Input System keyboard controls
    /// (Editor & PC testing) without ever calling legacy Input APIs.
    /// </summary>
    public sealed class MobileAndDesktopInputDriver : MonoBehaviour
    {
        private const string PrefCamSensKey = "CoachingPropHunt_CamSens";
        private const string PrefPropSensKey = "CoachingPropHunt_PropSens";

        public static MobileAndDesktopInputDriver Active { get; private set; }
        public static float CameraSensitivity { get; private set; } = 0.85f;
        public static float PropMorphSensitivity { get; private set; } = 0.65f;

        [SerializeField] private float touchLookSensitivity = 0.22f;
        [SerializeField] private float keyLookSpeed = 125f;

        private PlayerParticipant _participant;
        private Vector2 _virtualMove;
        private Vector2 _pendingTouchLook;
        private int _rotateHoldDir;
        public bool IsRunToggled { get; private set; }
        public bool IsSprintActive
        {
            get
            {
                var kb = Keyboard.current;
                return IsRunToggled || (kb != null && kb.leftShiftKey.isPressed);
            }
        }

        static MobileAndDesktopInputDriver()
        {
            LoadSensitivityPrefs();
        }

        public static void LoadSensitivityPrefs()
        {
            CameraSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(PrefCamSensKey, 0.85f), 0.25f, 2.00f);
            PropMorphSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(PrefPropSensKey, 0.65f), 0.25f, 1.50f);
        }

        public static void SetSensitivities(float camMult, float propMult, bool save = true)
        {
            CameraSensitivity = Mathf.Clamp(camMult, 0.25f, 2.00f);
            PropMorphSensitivity = Mathf.Clamp(propMult, 0.25f, 1.50f);
            if (save)
            {
                PlayerPrefs.SetFloat(PrefCamSensKey, CameraSensitivity);
                PlayerPrefs.SetFloat(PrefPropSensKey, PropMorphSensitivity);
                PlayerPrefs.Save();
            }
        }

        private void Awake()
        {
            _participant = GetComponent<PlayerParticipant>();
            LoadSensitivityPrefs();
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        public void ToggleRun() => IsRunToggled = !IsRunToggled;
        public void SetRunToggled(bool run) => IsRunToggled = run;

        public void SetVirtualMove(Vector2 move) => _virtualMove = Vector2.ClampMagnitude(move, 1f);

        public void AddTouchLook(Vector2 pixelDelta)
        {
            float dpiScale = 1080f / Mathf.Max(480f, Screen.height);
            // Camera Sensitivity drives camera rotation everywhere (even when morphed); Prop Rotation Sensitivity drives ROT L / ROT R arrows!
            _pendingTouchLook += pixelDelta * (touchLookSensitivity * CameraSensitivity * dpiScale);
        }

        public void TriggerUnmorph()
        {
            if (_participant == null || !_participant.IsAlive || _participant.Role != TeamRole.Prop) return;
            if (_participant.MorphCtrl != null && _participant.MorphCtrl.IsMorphed)
            {
                _participant.MorphCtrl.ResetToAvatar();
                AudioSynthManager.Instance?.PlayClick();
                GameStateManager.Instance?.ShowToast("Reverted to Student!", Palette.TextLight);
            }
        }

        public void TriggerPrimaryAction()
        {
            if (_participant == null || !_participant.IsAlive) return;
            if (_participant.Role == TeamRole.Hunter)
                _participant.HunterAction.TryStrike(_participant.GetAimRay());
            else if (_participant.MorphCtrl.TryMorphTarget(_participant.GetAimRay(), out string disp))
                GameStateManager.Instance?.ShowToast($"Disguised as {disp}!", Palette.PropAccent);
            else
                GameStateManager.Instance?.ShowToast("Look closer at any object to Morph!", Palette.TextMuted);
        }

        public void TriggerSecondaryAction()
        {
            if (_participant == null || !_participant.IsAlive) return;
            if (_participant.Role == TeamRole.Hunter)
                _participant.HunterAction.TryRadarPulse();
            else
                _participant.MorphCtrl.ToggleRotationLock();
        }

        public void TriggerWhistle()
        {
            if (_participant == null || !_participant.IsAlive || _participant.Role != TeamRole.Prop) return;
            _participant.PropAbility.TryManualWhistle(true);
        }

        public void TriggerDecoy()
        {
            if (_participant == null || !_participant.IsAlive || _participant.Role != TeamRole.Prop) return;
            _participant.PropAbility.TrySpawnDecoy(_participant.MorphCtrl.CurrentPropId, true);
        }

        public void TriggerStun()
        {
            if (_participant == null || !_participant.IsAlive || _participant.Role != TeamRole.Prop) return;
            _participant.PropAbility.TryStunHunter(true);
        }

        public void SetHoldRotate(int direction) => _rotateHoldDir = Mathf.Clamp(direction, -1, 1);

        public void TapRotateStep(int direction)
        {
            if (_participant == null || !_participant.IsAlive || _participant.Role != TeamRole.Prop) return;
            _participant.MorphCtrl?.RotateMorphYaw(Mathf.Clamp(direction, -1, 1) * 18f * PropMorphSensitivity);
        }

        public void TriggerJump() => _participant?.TriggerJump();

        private void Update()
        {
            if (_participant == null) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                gsm.TogglePause();
                return;
            }

            if (gsm.ShellState != AppShellState.Playing)
            {
                _participant.SetMoveInput(Vector2.zero);
                _participant.SetRunning(false);
                _pendingTouchLook = Vector2.zero;
                return;
            }

            if (!_participant.IsAlive)
            {
                Vector2 specLook = _pendingTouchLook;
                _pendingTouchLook = Vector2.zero;
                if (kb != null)
                {
                    float lx = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                    float ly = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                    specLook += new Vector2(lx, ly) * (keyLookSpeed * Time.deltaTime);
                }
                if (specLook.sqrMagnitude > 0.0001f)
                    CoachingPropHunt.CameraRig.PlayerCameraController.Instance?.AddSpectatorOrbitDelta(specLook);
                _participant.SetMoveInput(Vector2.zero);
                _participant.SetRunning(false);
                return;
            }

            Vector2 move = _virtualMove;
            Vector2 look = _pendingTouchLook;
            _pendingTouchLook = Vector2.zero;

            if (kb != null)
            {
                float kx = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float ky = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                if (Mathf.Abs(kx) > 0.01f || Mathf.Abs(ky) > 0.01f)
                    move = Vector2.ClampMagnitude(new Vector2(kx, ky), 1f);

                float lx = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                float ly = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                look += new Vector2(lx, ly) * (keyLookSpeed * Time.deltaTime);

                if (kb.spaceKey.wasPressedThisFrame) TriggerJump();
                if (kb.eKey.wasPressedThisFrame) TriggerPrimaryAction();
                if (kb.qKey.wasPressedThisFrame) TriggerSecondaryAction();
                if (kb.fKey.wasPressedThisFrame) TriggerWhistle();
                if (kb.gKey.wasPressedThisFrame) TriggerDecoy();
                if (kb.rKey.wasPressedThisFrame) TriggerStun();

                int kRot = (kb.cKey.isPressed ? 1 : 0) - (kb.zKey.isPressed ? 1 : 0);
                if (kRot != 0 && _participant.Role == TeamRole.Prop && _participant.MorphCtrl.IsMorphed)
                    _participant.MorphCtrl.RotateMorphYaw(kRot * 135f * PropMorphSensitivity * Time.deltaTime);
            }

            if (_rotateHoldDir != 0 && _participant.Role == TeamRole.Prop && _participant.MorphCtrl.IsMorphed)
            {
                _participant.MorphCtrl.RotateMorphYaw(_rotateHoldDir * 135f * PropMorphSensitivity * Time.deltaTime);
            }

            _participant.SetRunning(IsSprintActive);
            _participant.SetMoveInput(move);
            if (look.sqrMagnitude > 0.0001f) _participant.AddLookDelta(look);
        }
    }
}
