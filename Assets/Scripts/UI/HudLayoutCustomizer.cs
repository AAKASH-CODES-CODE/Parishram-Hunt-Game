using System;
using System.Collections.Generic;
using CoachingPropHunt.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoachingPropHunt.UI
{
    /// <summary>
    /// Attached to each customizable gameplay HUD control. During SETTING HUD edit mode,
    /// renders a crisp selection border and supports drag-to-reposition clamped inside SafeArea.
    /// </summary>
    public sealed class HudEditableHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public string ControlId { get; private set; }
        public string DisplayTitle { get; private set; }
        public string GroupTag { get; private set; } = "shared";
        public Vector2 DefaultAnchoredPos { get; private set; }
        public float DefaultScale { get; private set; } = 1f;
        public float TargetScale { get; private set; } = 1f;
        public bool IsEditing { get; private set; }

        private RectTransform _rect;
        private Button _linkedButton;
        private Image _selectionRing;
        private HudLayoutCustomizer _owner;
        private Canvas _canvas;
        private RectTransform _safeBoundsRect;

        public RectTransform Rect => _rect;
        public float CurrentScale => TargetScale;

        public void Configure(HudLayoutCustomizer owner, string id, string title, string groupTag, Canvas canvas, RectTransform safeBoundsRect)
        {
            _owner = owner;
            ControlId = id;
            DisplayTitle = title;
            GroupTag = string.IsNullOrEmpty(groupTag) ? "shared" : groupTag;
            _canvas = canvas;
            _safeBoundsRect = safeBoundsRect;
            _rect = GetComponent<RectTransform>();
            _linkedButton = GetComponent<Button>();
            if (_rect != null)
            {
                DefaultAnchoredPos = _rect.anchoredPosition;
                DefaultScale = _rect.localScale.x;
                TargetScale = DefaultScale;
            }
            BuildOutline();
            SetEditMode(false, selected: false);
        }

        private void BuildOutline()
        {
            if (_selectionRing != null) return;
            var go = new GameObject("EditSelectionRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(-8f, -8f);
            r.offsetMax = new Vector2(8f, 8f);
            _selectionRing = go.GetComponent<Image>();
            _selectionRing.sprite = Palette.GetRingSprite();
            _selectionRing.color = new Color(Palette.PropAccent.r, Palette.PropAccent.g, Palette.PropAccent.b, 0.35f);
            _selectionRing.raycastTarget = false;
            go.SetActive(false);
        }

        public void SetEditMode(bool editing, bool selected)
        {
            IsEditing = editing;
            if (_linkedButton != null) _linkedButton.enabled = !editing;
            if (editing && _rect != null)
                _rect.localScale = new Vector3(TargetScale, TargetScale, 1f);
            if (_selectionRing != null)
            {
                _selectionRing.gameObject.SetActive(editing);
                _selectionRing.color = selected
                    ? new Color(Palette.AccentGold.r, Palette.AccentGold.g, Palette.AccentGold.b, 0.96f)
                    : new Color(Palette.PropAccent.r, Palette.PropAccent.g, Palette.PropAccent.b, 0.45f);
            }
        }

        public void SetScale(float scale)
        {
            float s = Mathf.Clamp(scale, 0.65f, 1.65f);
            TargetScale = s;
            if (_rect != null) _rect.localScale = new Vector3(s, s, 1f);
            ClampInsideSafeBounds();
        }

        public void ResetToDefault()
        {
            TargetScale = DefaultScale;
            if (_rect != null)
            {
                _rect.anchoredPosition = DefaultAnchoredPos;
                _rect.localScale = new Vector3(DefaultScale, DefaultScale, 1f);
            }
        }

        public void ClampInsideSafeBounds()
        {
            if (_rect == null || _safeBoundsRect == null) return;
            Vector3[] safeCorners = new Vector3[4];
            Vector3[] selfCorners = new Vector3[4];
            _safeBoundsRect.GetWorldCorners(safeCorners);
            _rect.GetWorldCorners(selfCorners);

            float minX = safeCorners[0].x, maxX = safeCorners[2].x;
            float minY = safeCorners[0].y, maxY = safeCorners[2].y;
            Vector3 deltaWorld = Vector3.zero;
            if (selfCorners[0].x < minX) deltaWorld.x += minX - selfCorners[0].x;
            if (selfCorners[2].x > maxX) deltaWorld.x -= selfCorners[2].x - maxX;
            if (selfCorners[0].y < minY) deltaWorld.y += minY - selfCorners[0].y;
            if (selfCorners[2].y > maxY) deltaWorld.y -= selfCorners[2].y - maxY;

            if (deltaWorld.sqrMagnitude > 0.0001f)
                _rect.position += deltaWorld;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_owner == null || !_owner.IsEditing) return;
            _owner.SelectHandle(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_owner == null || !_owner.IsEditing || _rect == null) return;
            float sf = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
            _rect.anchoredPosition += eventData.delta / sf;
            ClampInsideSafeBounds();
            _owner.SelectHandle(this);
        }
    }

    /// <summary>
    /// Manages registration, selection highlight, live drag/scale editing, snapshot cancel,
    /// factory reset, and PlayerPrefs persistence for the custom mobile HUD ("SETTING HUD").
    /// </summary>
    public sealed class HudLayoutCustomizer
    {
        private const string PrefPrefix = "CoachingPropHunt_HudLayout_v1_";

        private struct LayoutSnapshot
        {
            public Vector2 pos;
            public float scale;
        }

        private readonly List<HudEditableHandle> _handles = new List<HudEditableHandle>();
        private readonly Dictionary<string, LayoutSnapshot> _snapshot = new Dictionary<string, LayoutSnapshot>();
        private HudEditableHandle _selected;

        public bool IsEditing { get; private set; }
        public string ActiveRoleTab { get; private set; } = "prop";
        public HudEditableHandle Selected => _selected;
        public event Action<HudEditableHandle> OnSelectionChanged;

        public HudEditableHandle Register(GameObject targetGo, string id, string title, string groupTag, Canvas canvas, RectTransform safeBounds)
        {
            if (targetGo == null) return null;
            var handle = targetGo.GetComponent<HudEditableHandle>();
            if (handle == null) handle = targetGo.AddComponent<HudEditableHandle>();
            handle.Configure(this, id, title, groupTag, canvas, safeBounds);
            if (!_handles.Contains(handle)) _handles.Add(handle);
            LoadSingle(handle);
            return handle;
        }

        public void BeginEditSession(string activeRoleTab = "prop", string defaultSelectionId = "")
        {
            IsEditing = true;
            ActiveRoleTab = activeRoleTab == "hunter" ? "hunter" : "prop";
            if (string.IsNullOrEmpty(defaultSelectionId))
                defaultSelectionId = ActiveRoleTab == "hunter" ? "hunter_attack" : "morph";
            _snapshot.Clear();
            _selected = null;
            HudEditableHandle fallback = null;
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h == null || h.Rect == null) continue;
                _snapshot[h.ControlId] = new LayoutSnapshot
                {
                    pos = h.Rect.anchoredPosition,
                    scale = h.CurrentScale
                };
                if (!MatchesActiveTab(h)) continue;
                if (h.ControlId == defaultSelectionId) _selected = h;
                if (fallback == null) fallback = h;
            }
            if (_selected == null) _selected = fallback;
            RefreshOutlineVisuals();
            OnSelectionChanged?.Invoke(_selected);
        }

        public bool MatchesActiveTab(HudEditableHandle h)
        {
            if (h == null) return false;
            return h.GroupTag == "shared" || h.GroupTag == ActiveRoleTab;
        }

        public void SetActiveRoleTab(string roleTab)
        {
            ActiveRoleTab = roleTab == "hunter" ? "hunter" : "prop";
            if (_selected == null || !MatchesActiveTab(_selected))
            {
                HudEditableHandle firstRoleMatch = null;
                HudEditableHandle fallback = null;
                for (int i = 0; i < _handles.Count; i++)
                {
                    var h = _handles[i];
                    if (h == null || !MatchesActiveTab(h)) continue;
                    if (h.GroupTag == ActiveRoleTab && firstRoleMatch == null) firstRoleMatch = h;
                    if (fallback == null) fallback = h;
                }
                _selected = firstRoleMatch ?? fallback;
            }
            RefreshOutlineVisuals();
            OnSelectionChanged?.Invoke(_selected);
        }

        public void ResetTabDefaults(string roleTab)
        {
            string targetGroup = roleTab == "hunter" ? "hunter" : "prop";
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h != null && (h.GroupTag == targetGroup || h.GroupTag == "shared"))
                    h.ResetToDefault();
            }
            RefreshOutlineVisuals();
            OnSelectionChanged?.Invoke(_selected);
        }

        public void CancelEditSession()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h == null || h.Rect == null) continue;
                if (_snapshot.TryGetValue(h.ControlId, out var snap))
                {
                    h.Rect.anchoredPosition = snap.pos;
                    h.SetScale(snap.scale);
                }
            }
            EndEditSession();
        }

        public void SaveAndCloseEditSession()
        {
            SaveAll();
            EndEditSession();
        }

        public void EndEditSession()
        {
            IsEditing = false;
            _selected = null;
            RefreshOutlineVisuals();
        }

        public void SelectHandle(HudEditableHandle handle)
        {
            _selected = handle;
            RefreshOutlineVisuals();
            OnSelectionChanged?.Invoke(_selected);
        }

        public void SetSelectedScale(float scale)
        {
            if (_selected == null) return;
            _selected.SetScale(scale);
            OnSelectionChanged?.Invoke(_selected);
        }

        public void ResetAllToDefaults()
        {
            for (int i = 0; i < _handles.Count; i++)
                _handles[i]?.ResetToDefault();
            RefreshOutlineVisuals();
            OnSelectionChanged?.Invoke(_selected);
        }

        public void SaveAll()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h == null || h.Rect == null) continue;
                string key = PrefPrefix + h.ControlId;
                PlayerPrefs.SetFloat(key + "_X", h.Rect.anchoredPosition.x);
                PlayerPrefs.SetFloat(key + "_Y", h.Rect.anchoredPosition.y);
                PlayerPrefs.SetFloat(key + "_S", h.CurrentScale);
            }
            PlayerPrefs.Save();
        }

        public void LoadAll()
        {
            for (int i = 0; i < _handles.Count; i++)
                LoadSingle(_handles[i]);
        }

        private static void LoadSingle(HudEditableHandle h)
        {
            if (h == null || h.Rect == null) return;
            string key = PrefPrefix + h.ControlId;
            if (!PlayerPrefs.HasKey(key + "_X")) return;
            float x = PlayerPrefs.GetFloat(key + "_X", h.DefaultAnchoredPos.x);
            float y = PlayerPrefs.GetFloat(key + "_Y", h.DefaultAnchoredPos.y);
            float s = PlayerPrefs.GetFloat(key + "_S", h.DefaultScale);
            h.Rect.anchoredPosition = new Vector2(x, y);
            h.SetScale(s);
        }

        private void RefreshOutlineVisuals()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h != null) h.SetEditMode(IsEditing, h == _selected);
            }
        }
    }
}
