using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CoachingPropHunt.UI
{
    public enum HudIconKind
    {
        Morph,
        Unmorph,
        Lock,
        Whistle,
        Decoy,
        Jump,
        Tag,
        Radar,
        Pause,
        Settings,
        Play,
        Online,
        RotateLeft,
        RotateRight,
        Stun,
        Run
    }

    /// <summary>
    /// Procedural crisp 64x64 monochrome icon generator + tactile button bounce helper
    /// so every HUD action circle renders crisp recognizable iconography without external textures.
    /// </summary>
    public static class HudIcons
    {
        private static readonly Dictionary<HudIconKind, Sprite> Cache = new Dictionary<HudIconKind, Sprite>();

        public static Sprite Get(HudIconKind kind)
        {
            if (Cache.TryGetValue(kind, out var existing) && existing != null) return existing;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[S * S];
            DrawIcon(kind, px, S);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            Cache[kind] = sp;
            return sp;
        }

        private static void DrawIcon(HudIconKind kind, Color32[] px, int S)
        {
            switch (kind)
            {
                case HudIconKind.Morph:
                    DrawRing(px, S, 32f, 32f, 20f, 15.5f);
                    DrawRect(px, S, 24, 24, 40, 40);
                    break;
                case HudIconKind.Unmorph:
                    DrawDisk(px, S, 32f, 45f, 8.5f);
                    DrawRect(px, S, 21, 16, 43, 34);
                    break;
                case HudIconKind.Lock:
                    DrawRing(px, S, 32f, 38f, 13f, 8.5f);
                    DrawRect(px, S, 18, 13, 46, 36);
                    break;
                case HudIconKind.Whistle:
                    DrawDisk(px, S, 22f, 32f, 9f);
                    DrawArcRight(px, S, 24f, 32f, 16f, 12f);
                    DrawArcRight(px, S, 24f, 32f, 24f, 20f);
                    break;
                case HudIconKind.Decoy:
                    DrawRect(px, S, 16, 22, 36, 48);
                    DrawOutlineRect(px, S, 28, 15, 48, 41, 3);
                    break;
                case HudIconKind.Jump:
                    DrawTriangleUp(px, S, 32f, 16f, 42f, 48f);
                    DrawRect(px, S, 26, 14, 38, 28);
                    break;
                case HudIconKind.Tag:
                    DrawRing(px, S, 32f, 32f, 22f, 17.5f);
                    DrawRect(px, S, 30, 8, 34, 56);
                    DrawRect(px, S, 8, 30, 56, 34);
                    break;
                case HudIconKind.Radar:
                    DrawRing(px, S, 32f, 32f, 23f, 19.5f);
                    DrawRing(px, S, 32f, 32f, 14f, 10.5f);
                    DrawDisk(px, S, 32f, 32f, 4.5f);
                    break;
                case HudIconKind.Pause:
                    DrawRect(px, S, 19, 14, 28, 50);
                    DrawRect(px, S, 36, 14, 45, 50);
                    break;
                case HudIconKind.Settings:
                    DrawRing(px, S, 32f, 32f, 20f, 12f);
                    DrawRect(px, S, 29, 8, 35, 56);
                    DrawRect(px, S, 8, 29, 56, 35);
                    break;
                case HudIconKind.Play:
                    DrawTriangleRight(px, S, 20f, 14f, 50f, 36f);
                    break;
                case HudIconKind.Online:
                    DrawRing(px, S, 32f, 32f, 22f, 17.5f);
                    DrawRect(px, S, 11, 30, 53, 34);
                    DrawRing(px, S, 32f, 32f, 11f, 7.5f);
                    break;
                case HudIconKind.RotateLeft:
                    DrawPartialArc(px, S, 32f, 32f, 21f, 14f, 40f, 305f);
                    DrawTriangleDownLeft(px, S, 13f, 35f, 15f);
                    break;
                case HudIconKind.RotateRight:
                    DrawPartialArc(px, S, 32f, 32f, 21f, 14f, -125f, 140f);
                    DrawTriangleDownRight(px, S, 51f, 35f, 15f);
                    break;
                case HudIconKind.Stun:
                    DrawRing(px, S, 32f, 32f, 24f, 19.5f);
                    DrawBolt(px, S);
                    break;
                case HudIconKind.Run:
                    DrawTriangleRight(px, S, 13f, 16f, 35f, 48f);
                    DrawTriangleRight(px, S, 31f, 16f, 53f, 48f);
                    break;
            }
        }

        private static void SetPx(Color32[] px, int S, int x, int y, float alpha = 1f)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
            int idx = y * S + x;
            if (a > px[idx].a) px[idx] = new Color32(255, 255, 255, a);
        }

        private static void DrawDisk(Color32[] px, int S, float cx, float cy, float r)
        {
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    if (d <= r + 1f) SetPx(px, S, x, y, r - d + 1f);
                }
        }

        private static void DrawRing(Color32[] px, int S, float cx, float cy, float rOut, float rIn)
        {
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(rOut - d + 1f) * Mathf.Clamp01(d - rIn + 1f);
                    if (a > 0f) SetPx(px, S, x, y, a);
                }
        }

        private static void DrawArcRight(Color32[] px, int S, float cx, float cy, float rOut, float rIn)
        {
            for (int y = 0; y < S; y++)
                for (int x = (int)cx; x < S; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (Mathf.Abs(dy) > dx * 1.15f) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOut - d + 1f) * Mathf.Clamp01(d - rIn + 1f);
                    if (a > 0f) SetPx(px, S, x, y, a);
                }
        }

        private static void DrawRect(Color32[] px, int S, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) SetPx(px, S, x, y, 1f);
        }

        private static void DrawOutlineRect(Color32[] px, int S, int x0, int y0, int x1, int y1, int t)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (x < x0 + t || x > x1 - t || y < y0 + t || y > y1 - t)
                        SetPx(px, S, x, y, 1f);
        }

        private static void DrawTriangleUp(Color32[] px, int S, float cx, float yBot, float width, float yTop)
        {
            float h = Mathf.Max(1f, yTop - yBot);
            for (int y = (int)yBot; y <= (int)yTop; y++)
            {
                float t = 1f - (y - yBot) / h;
                float halfW = width * 0.5f * t;
                for (int x = (int)(cx - halfW); x <= (int)(cx + halfW); x++)
                    SetPx(px, S, x, y, 1f);
            }
        }

        private static void DrawTriangleRight(Color32[] px, int S, float xLeft, float yBot, float xRight, float height)
        {
            float cy = yBot + height * 0.5f;
            float w = Mathf.Max(1f, xRight - xLeft);
            for (int x = (int)xLeft; x <= (int)xRight; x++)
            {
                float t = 1f - (x - xLeft) / w;
                float halfH = height * 0.5f * t;
                for (int y = (int)(cy - halfH); y <= (int)(cy + halfH); y++)
                    SetPx(px, S, x, y, 1f);
            }
        }

        private static void DrawPartialArc(Color32[] px, int S, float cx, float cy, float rOut, float rIn, float degMin, float degMax)
        {
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < rIn - 1f || d > rOut + 1f) continue;
                    float deg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (deg < degMin) deg += 360f;
                    if (deg > degMax) continue;
                    float a = Mathf.Clamp01(rOut - d + 1f) * Mathf.Clamp01(d - rIn + 1f);
                    if (a > 0f) SetPx(px, S, x, y, a);
                }
            }
        }

        private static void DrawTriangleDownLeft(Color32[] px, int S, float tipX, float tipY, float size)
        {
            for (int dy = 0; dy <= (int)size; dy++)
            {
                float half = (size - dy) * 0.45f;
                for (int dx = -(int)half; dx <= (int)half; dx++)
                    SetPx(px, S, Mathf.RoundToInt(tipX + dx + 3f), Mathf.RoundToInt(tipY - dy + size * 0.4f), 1f);
            }
        }

        private static void DrawTriangleDownRight(Color32[] px, int S, float tipX, float tipY, float size)
        {
            for (int dy = 0; dy <= (int)size; dy++)
            {
                float half = (size - dy) * 0.45f;
                for (int dx = -(int)half; dx <= (int)half; dx++)
                    SetPx(px, S, Mathf.RoundToInt(tipX + dx - 3f), Mathf.RoundToInt(tipY - dy + size * 0.4f), 1f);
            }
        }

        private static void DrawBolt(Color32[] px, int S)
        {
            for (int y = 14; y <= 50; y++)
            {
                float u = (y - 14f) / 36f;
                float cx = u < 0.52f ? Mathf.Lerp(26f, 38f, u / 0.52f) : Mathf.Lerp(24f, 37f, (u - 0.52f) / 0.48f);
                float hw = u < 0.52f ? 4.5f : 5.0f;
                for (int x = (int)(cx - hw); x <= (int)(cx + hw); x++)
                    SetPx(px, S, x, y, 1f);
            }
        }
    }

    /// <summary>
    /// Press-and-hold helper for continuous mobile rotation arrows (skips safely during SETTING HUD edit mode).
    /// </summary>
    public sealed class HoldButtonHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action OnPressedDown;
        public System.Action<bool> OnHoldStateChanged;

        public void OnPointerDown(PointerEventData eventData)
        {
            var custom = GetComponent<HudEditableHandle>();
            if (custom != null && custom.IsEditing) return;
            OnPressedDown?.Invoke();
            OnHoldStateChanged?.Invoke(true);
        }

        public void OnPointerUp(PointerEventData eventData) => OnHoldStateChanged?.Invoke(false);
        public void OnPointerExit(PointerEventData eventData) => OnHoldStateChanged?.Invoke(false);
        private void OnDisable() => OnHoldStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Tactile mobile spring-press animation on buttons (zero effect when HUD editor drag is active).
    /// </summary>
    public sealed class ButtonBounceAnimator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private bool _pressed;
        private Vector3 _baseScale = Vector3.one;
        private bool _hasBase;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _hasBase = true;
        }

        public void SyncBaseScale()
        {
            if (!_pressed) _baseScale = transform.localScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_hasBase && !_pressed) _baseScale = transform.localScale;
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
        public void OnPointerExit(PointerEventData eventData) => _pressed = false;

        private void OnDisable()
        {
            _pressed = false;
            if (_hasBase) transform.localScale = _baseScale;
        }

        private void Update()
        {
            if (!_hasBase) return;
            var custom = GetComponent<HudEditableHandle>();
            if (custom != null)
            {
                if (custom.IsEditing)
                {
                    _pressed = false;
                    return;
                }
                _baseScale = new Vector3(custom.TargetScale, custom.TargetScale, 1f);
            }
            Vector3 target = _pressed ? _baseScale * 0.91f : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, target, Time.unscaledDeltaTime * 18f);
        }
    }
}
