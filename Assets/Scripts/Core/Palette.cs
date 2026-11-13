using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoachingPropHunt.Core
{
    /// <summary>
    /// Single source of truth for the Coaching Prop Hunt Art Bible: cohesive colors,
    /// procedural crisp UI sprites, TMP typography styling, and unscaled easing helpers.
    /// Zero hardcoded ad-hoc colors or untinted white defaults anywhere else in scripts.
    /// </summary>
    public static class Palette
    {
        // Primary & Team Identity
        public static readonly Color HunterNavy = new Color32(28, 49, 84, 255);
        public static readonly Color HunterAccent = new Color32(242, 108, 63, 255);
        public static readonly Color PropTeal = new Color32(26, 152, 138, 255);
        public static readonly Color PropAccent = new Color32(104, 214, 189, 255);
        public static readonly Color AccentGold = new Color32(245, 186, 62, 255);

        // UI & Typography
        public static readonly Color PanelDark = new Color32(18, 25, 38, 232);
        public static readonly Color PanelMid = new Color32(31, 42, 62, 224);
        public static readonly Color PanelGlass = new Color32(24, 34, 52, 188);
        public static readonly Color TextLight = new Color32(244, 246, 250, 255);
        public static readonly Color TextDarkOutline = new Color32(12, 16, 26, 255);
        public static readonly Color TextMuted = new Color32(172, 186, 210, 255);
        public static readonly Color DangerRed = new Color32(226, 74, 74, 255);
        public static readonly Color SuccessEmerald = new Color32(54, 198, 128, 255);

        // Atmosphere, Sky & FX
        public static readonly Color SkyZenith = new Color32(94, 150, 216, 255);
        public static readonly Color SkyHorizon = new Color32(246, 214, 164, 255);
        public static readonly Color CampusGround = new Color32(58, 68, 78, 255);
        public static readonly Color SunWarm = new Color32(255, 238, 206, 255);
        public static readonly Color FillCool = new Color32(138, 178, 224, 255);
        public static readonly Color DustMoteCream = new Color32(255, 245, 216, 145);
        public static readonly Color MorphBurstTeal = new Color32(82, 222, 198, 255);

        // Player Avatar Surface Tones
        public static readonly Color SkinWarm = new Color32(204, 152, 114, 255);
        public static readonly Color HairCharcoal = new Color32(34, 36, 44, 255);
        public static readonly Color DenimSlate = new Color32(48, 66, 98, 255);
        public static readonly Color ShoeDark = new Color32(38, 42, 50, 255);

        private static Sprite _roundedRectSprite;
        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        private static Material _urpLitShared;

        public static Sprite GetRoundedRectSprite()
        {
            if (_roundedRectSprite != null) return _roundedRectSprite;
            const int size = 64;
            const int radius = 14;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Max(0, Mathf.Abs(x - (size - 1) * 0.5f) > (size * 0.5f - radius)
                        ? Mathf.RoundToInt(Mathf.Abs(x - (size - 1) * 0.5f) - (size * 0.5f - radius)) : 0);
                    int dy = Mathf.Max(0, Mathf.Abs(y - (size - 1) * 0.5f) > (size * 0.5f - radius)
                        ? Mathf.RoundToInt(Mathf.Abs(y - (size - 1) * 0.5f) - (size * 0.5f - radius)) : 0);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - dist + 0.8f);
                    // Subtle top-down sheen so panels never look dead-flat
                    float shade = Mathf.Lerp(0.92f, 1.0f, (float)y / size);
                    byte v = (byte)Mathf.RoundToInt(shade * 255f);
                    px[y * size + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return _roundedRectSprite;
        }

        public static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
                    float a = Mathf.Clamp01(half - 1.5f - d + 1.0f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        public static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            float outer = half - 2f;
            float inner = outer - 5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
                    float a = Mathf.Clamp01(outer - d + 1f) * Mathf.Clamp01(d - inner + 1f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }

        public static Material CreateUrpLitMaterial(Color baseColor, float smoothness = 0.35f, float metallic = 0.0f)
        {
            if (_urpLitShared == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                _urpLitShared = new Material(shader);
            }
            var mat = new Material(_urpLitShared);
            mat.SetColor("_BaseColor", baseColor);
            mat.color = baseColor;
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        public static void StyleText(TextMeshProUGUI tmp, Color color, bool bold = true, float spacing = 3.2f)
        {
            if (tmp == null) return;
            tmp.color = color;
            tmp.characterSpacing = spacing;
            if (bold) tmp.fontStyle |= FontStyles.Bold;
            if (tmp.fontSharedMaterial != null)
            {
                tmp.outlineWidth = 0.07f;
                tmp.outlineColor = TextDarkOutline;
            }
        }

        public static Image StylePanel(Image img, Color tint)
        {
            if (img == null) return null;
            img.sprite = GetRoundedRectSprite();
            img.type = Image.Type.Sliced;
            img.color = tint;
            return img;
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        public static IEnumerator Tween(float duration, Action<float> apply, Func<float, float> ease, bool unscaled = true)
        {
            if (duration <= 0f)
            {
                apply?.Invoke(1f);
                yield break;
            }
            float t = 0f;
            while (t < 1f)
            {
                float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                t += dt / duration;
                apply?.Invoke(ease(Mathf.Clamp01(t)));
                yield return null;
            }
            apply?.Invoke(1f);
        }
    }
}
