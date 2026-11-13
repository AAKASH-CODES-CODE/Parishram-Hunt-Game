using CoachingPropHunt.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoachingPropHunt.Gameplay
{
    /// <summary>
    /// Stylized articulated Blender character rig (or primitive fallback) with smooth damped
    /// 2-joint stride (hip + knee flex), airborne jump pose, landing squash, Hunter blaster aim + recoil,
    /// and dizzy stun reaction.
    /// </summary>
    public sealed class PlayerAvatarVisual : MonoBehaviour
    {
        private Transform _rigRoot;
        private Transform _bodyPivot;
        private Transform _headPivot;
        private Transform _armL;
        private Transform _armR;
        private Transform _legL;
        private Transform _shinL;
        private Transform _legR;
        private Transform _shinR;
        private Transform _gunAnchor;
        private Transform _muzzleTip;
        private Renderer[] _allRenderers;

        private TeamRole _role;
        private float _baseBodyY = 0.78f;
        private float _smoothedSpeedN;
        private float _gaitPhase;
        private float _gaitWeight;
        private float _idleTime;
        private float _squashTimer;
        private float _swingTimer;
        private float _airBlend;
        private float _stunTime;
        private Vector3 _prevVelocity;
        private bool _built;

        public bool IsVisible { get; private set; } = true;
        public bool UsesBlenderRig { get; private set; }
        public TeamRole BuiltRole => _role;
        public bool HasBlasterVisual => _role == TeamRole.Hunter && _gunAnchor != null && _gunAnchor.childCount > 0;
        public Vector3 MuzzleWorldPos => _muzzleTip != null
            ? _muzzleTip.position
            : transform.position + Vector3.up * 1.22f + transform.forward * 0.45f;
        public Vector3 MuzzleWorldForward => _muzzleTip != null ? _muzzleTip.forward : transform.forward;

        public void BuildVisual(TeamRole role)
        {
            if (_built)
            {
                if (_role == role && _rigRoot != null) return;
                if (_rigRoot != null) Destroy(_rigRoot.gameObject);
                _rigRoot = null;
                _bodyPivot = null;
                _headPivot = null;
                _armL = null;
                _armR = null;
                _legL = null;
                _shinL = null;
                _legR = null;
                _shinR = null;
                _gunAnchor = null;
                _muzzleTip = null;
                _allRenderers = null;
                UsesBlenderRig = false;
            }
            _built = true;
            _role = role;

            string charRes = role == TeamRole.Hunter ? "Characters/HunterCharacter" : "Characters/PropStudentCharacter";
            var prefab = Resources.Load<GameObject>(charRes);
            if (prefab != null)
            {
                var inst = Instantiate(prefab, transform);
                inst.name = "CharacterRig";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                _rigRoot = inst.transform;

                _bodyPivot = FindChild(_rigRoot, "BodyPivot");
                _headPivot = FindChild(_rigRoot, "HeadPivot");
                _armL = FindChild(_rigRoot, "ArmL_Pivot");
                _armR = FindChild(_rigRoot, "ArmR_Pivot");
                _legL = FindChild(_rigRoot, "LegL_Pivot");
                _shinL = FindChild(_rigRoot, "ShinL_Pivot");
                _legR = FindChild(_rigRoot, "LegR_Pivot");
                _shinR = FindChild(_rigRoot, "ShinR_Pivot");
                _gunAnchor = FindChild(_rigRoot, "GunAnchor");

                if (_bodyPivot != null)
                {
                    UsesBlenderRig = true;
                    _baseBodyY = _bodyPivot.localPosition.y;
                    if (role == TeamRole.Hunter) AttachBlasterToAnchor();
                }
                else
                {
                    Destroy(inst);
                    BuildFallbackRig(role);
                }
            }
            else
            {
                BuildFallbackRig(role);
            }

            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (_rigRoot != null && c.transform.IsChildOf(_rigRoot)) Destroy(c);
            }
            _allRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                if (_allRenderers[i] == null) continue;
                _allRenderers[i].lightProbeUsage = LightProbeUsage.BlendProbes;
                _allRenderers[i].shadowCastingMode = ShadowCastingMode.On;
                _allRenderers[i].receiveShadows = true;
            }
        }

        private void AttachBlasterToAnchor()
        {
            if (_gunAnchor == null)
            {
                var ga = new GameObject("GunAnchor").transform;
                ga.SetParent(_armR != null ? _armR : _bodyPivot, false);
                ga.localPosition = new Vector3(0f, -0.44f, 0.055f);
                _gunAnchor = ga;
            }
            // Grip angle matches -76° ready-aim arm raise so barrel aims horizontally ahead (+Z)
            _gunAnchor.localRotation = Quaternion.Euler(76f, 0f, 0f);

            var gunPrefab = Resources.Load<GameObject>("Characters/HunterBlaster");
            if (gunPrefab != null)
            {
                var gun = Instantiate(gunPrefab, _gunAnchor);
                gun.name = "HunterBlasterMesh";
                gun.transform.localPosition = Vector3.zero;
                gun.transform.localRotation = Quaternion.identity;
                gun.transform.localScale = Vector3.one;
                _muzzleTip = FindChild(gun.transform, "MuzzleTip");
            }
            else
            {
                var matGun = Palette.CreateUrpLitMaterial(Palette.HunterAccent, 0.40f);
                MakePart("BlasterBody", _gunAnchor, PrimitiveType.Cube, new Vector3(0f, 0.03f, 0.14f), new Vector3(0.06f, 0.08f, 0.34f), matGun);
                var tip = new GameObject("MuzzleTip").transform;
                tip.SetParent(_gunAnchor, false);
                tip.localPosition = new Vector3(0f, 0.03f, 0.35f);
                _muzzleTip = tip;
            }
        }

        private void BuildFallbackRig(TeamRole role)
        {
            Color jacketColor = role == TeamRole.Hunter ? Palette.HunterNavy : Palette.PropTeal;
            Color accentColor = role == TeamRole.Hunter ? Palette.HunterAccent : Palette.AccentGold;

            var matJacket = Palette.CreateUrpLitMaterial(jacketColor, 0.32f);
            var matAccent = Palette.CreateUrpLitMaterial(accentColor, 0.45f);
            var matDenim = Palette.CreateUrpLitMaterial(Palette.DenimSlate, 0.25f);
            var matSkin = Palette.CreateUrpLitMaterial(Palette.SkinWarm, 0.30f);
            var matHair = Palette.CreateUrpLitMaterial(Palette.HairCharcoal, 0.22f);
            var matShoe = Palette.CreateUrpLitMaterial(Palette.ShoeDark, 0.20f);

            var root = new GameObject("CharacterRig").transform;
            root.SetParent(transform, false);
            _rigRoot = root;

            _bodyPivot = new GameObject("BodyPivot").transform;
            _bodyPivot.SetParent(root, false);
            _bodyPivot.localPosition = new Vector3(0f, 0.78f, 0f);
            _baseBodyY = 0.78f;

            _legL = new GameObject("LegL_Pivot").transform;
            _legL.SetParent(root, false);
            _legL.localPosition = new Vector3(-0.11f, 0.76f, 0f);
            MakePart("ThighL", _legL, PrimitiveType.Cube, new Vector3(0f, -0.18f, 0f), new Vector3(0.14f, 0.36f, 0.14f), matDenim);
            _shinL = new GameObject("ShinL_Pivot").transform;
            _shinL.SetParent(_legL, false);
            _shinL.localPosition = new Vector3(0f, -0.37f, 0f);
            MakePart("CalfL", _shinL, PrimitiveType.Cube, new Vector3(0f, -0.16f, 0f), new Vector3(0.12f, 0.32f, 0.12f), matDenim);
            MakePart("ShoeL", _shinL, PrimitiveType.Cube, new Vector3(0f, -0.35f, 0.04f), new Vector3(0.15f, 0.08f, 0.22f), matShoe);

            _legR = new GameObject("LegR_Pivot").transform;
            _legR.SetParent(root, false);
            _legR.localPosition = new Vector3(0.11f, 0.76f, 0f);
            MakePart("ThighR", _legR, PrimitiveType.Cube, new Vector3(0f, -0.18f, 0f), new Vector3(0.14f, 0.36f, 0.14f), matDenim);
            _shinR = new GameObject("ShinR_Pivot").transform;
            _shinR.SetParent(_legR, false);
            _shinR.localPosition = new Vector3(0f, -0.37f, 0f);
            MakePart("CalfR", _shinR, PrimitiveType.Cube, new Vector3(0f, -0.16f, 0f), new Vector3(0.12f, 0.32f, 0.12f), matDenim);
            MakePart("ShoeR", _shinR, PrimitiveType.Cube, new Vector3(0f, -0.35f, 0.04f), new Vector3(0.15f, 0.08f, 0.22f), matShoe);

            MakePart("Torso", _bodyPivot, PrimitiveType.Cube, new Vector3(0f, 0.24f, 0f), new Vector3(0.40f, 0.50f, 0.22f), matJacket);
            if (role == TeamRole.Prop)
                MakePart("StudentBag", _bodyPivot, PrimitiveType.Cube, new Vector3(0f, 0.25f, -0.16f), new Vector3(0.28f, 0.34f, 0.13f), matAccent);
            else
                MakePart("UtilityBelt", _bodyPivot, PrimitiveType.Cube, new Vector3(0f, 0.03f, 0f), new Vector3(0.42f, 0.07f, 0.24f), matAccent);

            _armL = new GameObject("ArmL_Pivot").transform;
            _armL.SetParent(_bodyPivot, false);
            _armL.localPosition = new Vector3(-0.26f, 0.44f, 0f);
            MakePart("ArmL_Mesh", _armL, PrimitiveType.Cube, new Vector3(0f, -0.20f, 0f), new Vector3(0.10f, 0.44f, 0.10f), matJacket);

            _armR = new GameObject("ArmR_Pivot").transform;
            _armR.SetParent(_bodyPivot, false);
            _armR.localPosition = new Vector3(0.26f, 0.44f, 0f);
            MakePart("ArmR_Mesh", _armR, PrimitiveType.Cube, new Vector3(0f, -0.20f, 0f), new Vector3(0.10f, 0.44f, 0.10f), matJacket);
            if (role == TeamRole.Hunter) AttachBlasterToAnchor();

            _headPivot = new GameObject("HeadPivot").transform;
            _headPivot.SetParent(_bodyPivot, false);
            _headPivot.localPosition = new Vector3(0f, 0.54f, 0f);
            MakePart("Head", _headPivot, PrimitiveType.Sphere, new Vector3(0f, 0.16f, 0f), new Vector3(0.26f, 0.28f, 0.26f), matSkin);
            MakePart("Hair", _headPivot, PrimitiveType.Sphere, new Vector3(0f, 0.22f, -0.02f), new Vector3(0.27f, 0.20f, 0.27f), matHair);
        }

        public void SetVisible(bool visible, bool firstPersonHideBody = false)
        {
            IsVisible = visible;
            if (_allRenderers == null) return;
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                var r = _allRenderers[i];
                if (r == null) continue;
                r.enabled = visible && !firstPersonHideBody;
            }
        }

        public void TriggerLandingSquash() => _squashTimer = 0.14f;
        public void TriggerHunterSwing() => _swingTimer = 0.22f;

        public void UpdateMotion(Vector3 worldVelocity, float maxSpeed, float angularYawRate, float dt,
            bool isGrounded = true, float aimPitchDeg = 0f, float stunRemaining = 0f)
        {
            if (!_built || !IsVisible || _bodyPivot == null) return;

            float horizSpeed = new Vector2(worldVelocity.x, worldVelocity.z).magnitude;
            float rawSpeedN = maxSpeed > 0.05f ? Mathf.Clamp01(horizSpeed / maxSpeed) : 0f;
            float damp = 1f - Mathf.Exp(-12f * dt);
            _smoothedSpeedN = Mathf.Lerp(_smoothedSpeedN, rawSpeedN, damp);

            float targetW = _smoothedSpeedN > 0.04f ? 1f : 0f;
            _gaitWeight = Mathf.Lerp(_gaitWeight, targetW, 1f - Mathf.Exp(-9.5f * dt));
            _airBlend = Mathf.Lerp(_airBlend, isGrounded ? 0f : 1f, 1f - Mathf.Exp(-14f * dt));

            const float stepFreq = 1.95f; // Calibrated smoothly to ~3.25 m/s stride cadence
            _gaitPhase += dt * stepFreq * Mathf.PI * 2f * Mathf.Max(0.32f, _smoothedSpeedN);
            _idleTime += dt;

            bool stunned = stunRemaining > 0.02f;
            if (stunned) _stunTime += dt * 11f;
            else _stunTime = 0f;

            float idleBreathe = Mathf.Sin(_idleTime * 2.4f) * 0.014f;
            float walkBob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.036f * _smoothedSpeedN * _gaitWeight * (1f - _airBlend);
            float roll = Mathf.Sin(_gaitPhase * 0.5f) * 2.1f * _gaitWeight * (1f - _airBlend);

            Vector3 accel = (worldVelocity - _prevVelocity) / Mathf.Max(0.008f, dt);
            _prevVelocity = worldVelocity;
            float fwdAccel = Vector3.Dot(transform.forward, accel);
            float pitchLean = Mathf.Clamp(fwdAccel * 0.35f + _smoothedSpeedN * 5.0f, -6f, 7.5f) * _gaitWeight;
            if (stunned) pitchLean = -14f;
            float bank = Mathf.Clamp(-angularYawRate * 0.035f, -5f, 5f) * _gaitWeight;

            float stunBob = stunned ? Mathf.Abs(Mathf.Sin(_stunTime)) * 0.025f : 0f;
            Vector3 targetBodyPos = new Vector3(0f, _baseBodyY + walkBob + idleBreathe + stunBob, 0f);
            _bodyPivot.localPosition = Vector3.Lerp(_bodyPivot.localPosition, targetBodyPos, 1f - Mathf.Exp(-16f * dt));
            _bodyPivot.localRotation = Quaternion.Slerp(
                _bodyPivot.localRotation,
                Quaternion.Euler(pitchLean, stunned ? Mathf.Sin(_stunTime * 0.8f) * 14f : 0f, roll + bank),
                1f - Mathf.Exp(-12f * dt));

            if (_squashTimer > 0f)
            {
                _squashTimer = Mathf.Max(0f, _squashTimer - dt);
                float u = Mathf.Sin((_squashTimer / 0.14f) * Mathf.PI);
                _bodyPivot.localScale = new Vector3(1f + 0.09f * u, 1f - 0.13f * u, 1f + 0.09f * u);
            }
            else
            {
                _bodyPivot.localScale = Vector3.Lerp(_bodyPivot.localScale, Vector3.one, 1f - Mathf.Exp(-14f * dt));
            }

            // Smooth 2-joint legs (Hip + Knee bend + Air jump pose)
            float swingSin = Mathf.Sin(_gaitPhase);
            float hipAngleL = Mathf.Lerp(swingSin * 33f * _smoothedSpeedN * _gaitWeight, -22f, _airBlend);
            float hipAngleR = Mathf.Lerp(-swingSin * 33f * _smoothedSpeedN * _gaitWeight, 14f, _airBlend);
            float kneeFlexL = Mathf.Lerp(Mathf.Max(0f, -Mathf.Sin(_gaitPhase + 0.40f)) * 46f * _smoothedSpeedN * _gaitWeight, 34f, _airBlend);
            float kneeFlexR = Mathf.Lerp(Mathf.Max(0f, Mathf.Sin(_gaitPhase + 0.40f)) * 46f * _smoothedSpeedN * _gaitWeight, 24f, _airBlend);

            float jSlerp = 1f - Mathf.Exp(-18f * dt);
            if (_legL != null) _legL.localRotation = Quaternion.Slerp(_legL.localRotation, Quaternion.Euler(hipAngleL, 0f, 0f), jSlerp);
            if (_legR != null) _legR.localRotation = Quaternion.Slerp(_legR.localRotation, Quaternion.Euler(hipAngleR, 0f, 0f), jSlerp);
            if (_shinL != null) _shinL.localRotation = Quaternion.Slerp(_shinL.localRotation, Quaternion.Euler(kneeFlexL, 0f, 0f), jSlerp);
            if (_shinR != null) _shinR.localRotation = Quaternion.Slerp(_shinR.localRotation, Quaternion.Euler(kneeFlexR, 0f, 0f), jSlerp);

            // Smooth arm animations (Hunter Ready-Aim Blaster Pose + Recoil vs Prop Student Swing)
            if (_swingTimer > 0f) _swingTimer = Mathf.Max(0f, _swingTimer - dt);
            float recoilKick = _swingTimer > 0f ? Mathf.Sin((_swingTimer / 0.22f) * Mathf.PI) * 18f : 0f;

            if (_role == TeamRole.Hunter && !stunned)
            {
                float aimClamped = Mathf.Clamp(aimPitchDeg, -38f, 45f);
                float bobArm = Mathf.Sin(_gaitPhase) * 4.5f * _smoothedSpeedN * _gaitWeight;
                Quaternion targetArmR = Quaternion.Euler(-76f + aimClamped * 0.65f - recoilKick + bobArm * 0.5f, -8f, -5f);
                Quaternion targetArmL = Quaternion.Euler(-32f + swingSin * 14f * _smoothedSpeedN * _gaitWeight, 16f, Mathf.Lerp(5f, 18f, _airBlend));
                if (_armR != null) _armR.localRotation = Quaternion.Slerp(_armR.localRotation, targetArmR, 1f - Mathf.Exp(-20f * dt));
                if (_armL != null) _armL.localRotation = Quaternion.Slerp(_armL.localRotation, targetArmL, 1f - Mathf.Exp(-14f * dt));
            }
            else if (stunned)
            {
                float flail = Mathf.Sin(_stunTime) * 16f;
                if (_armL != null) _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(-15f + flail, 0f, 24f), jSlerp);
                if (_armR != null) _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(-15f - flail, 0f, -24f), jSlerp);
            }
            else
            {
                float armSwing = swingSin * 28f * _smoothedSpeedN * _gaitWeight;
                float airOut = Mathf.Lerp(4f, 22f, _airBlend);
                if (_armL != null) _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(-armSwing, 0f, airOut), jSlerp);
                if (_armR != null) _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(armSwing, 0f, -airOut), jSlerp);
            }

            if (_headPivot != null)
            {
                float headYaw = stunned ? Mathf.Sin(_stunTime) * 26f : Mathf.Clamp(-angularYawRate * 0.14f, -32f, 32f);
                float headNod = stunned ? 14f : (Mathf.Sin(_idleTime * 1.8f) * 2.0f * (1f - _gaitWeight) + Mathf.Clamp(aimPitchDeg * 0.25f, -15f, 15f));
                _headPivot.localRotation = Quaternion.Slerp(_headPivot.localRotation, Quaternion.Euler(headNod, headYaw, 0f), 1f - Mathf.Exp(-10f * dt));
            }
        }

        private static Transform FindChild(Transform parent, string targetName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == targetName) return c;
                var nested = FindChild(c, targetName);
                if (nested != null) return nested;
            }
            return null;
        }

        private static void MakePart(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
