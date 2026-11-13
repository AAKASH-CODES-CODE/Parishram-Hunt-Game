using CoachingPropHunt.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoachingPropHunt.UI
{
    /// <summary>
    /// Responsive multi-touch virtual joystick supporting tap-anywhere floating spawn inside LeftJoystickZone,
    /// smooth clamping across custom scales, and stuck-touch resets on pause/minimize.
    /// </summary>
    public sealed class VirtualThumbstick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform knobRect;
        [SerializeField] private float maxRadius = 64f;

        private RectTransform _bgRect;
        private RectTransform _parentRect;
        private Image _ringImage;
        private Vector2 _restAnchoredPos;
        private bool _hasRestPos;
        private int _activePointerId = int.MinValue;
        private Vector2 _currentValue;

        public Vector2 Value => _currentValue;
        public bool IsEditingHud { get; set; }

        public void Configure(RectTransform knob, float radius = 64f)
        {
            knobRect = knob;
            maxRadius = radius;
            CacheRestPosition();
        }

        public void CacheRestPosition()
        {
            if (_bgRect == null) _bgRect = GetComponent<RectTransform>();
            if (_bgRect != null)
            {
                _parentRect = _bgRect.parent as RectTransform;
                _restAnchoredPos = _bgRect.anchoredPosition;
                _hasRestPos = true;
            }
        }

        private void Awake()
        {
            _bgRect = GetComponent<RectTransform>();
            _ringImage = GetComponent<Image>();
            _parentRect = _bgRect != null ? _bgRect.parent as RectTransform : null;
        }

        private void OnDisable() => ResetStick();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetStick();
        }

        public void BeginFloatingAtScreenPos(PointerEventData eventData)
        {
            if (IsEditingHud || _activePointerId != int.MinValue) return;
            if (_bgRect == null) _bgRect = GetComponent<RectTransform>();
            if (_parentRect == null) _parentRect = _bgRect.parent as RectTransform;
            if (!_hasRestPos) CacheRestPosition();

            if (_parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localInParent))
            {
                // Convert center-relative parent point to anchor(0,0) coordinates
                Vector2 sizeP = _parentRect.rect.size;
                Vector2 bottomLeftPos = localInParent + new Vector2(sizeP.x * _parentRect.pivot.x, sizeP.y * _parentRect.pivot.y);
                float halfW = _bgRect.rect.width * _bgRect.localScale.x * 0.52f;
                float halfH = _bgRect.rect.height * _bgRect.localScale.y * 0.52f;
                bottomLeftPos.x = Mathf.Clamp(bottomLeftPos.x, halfW + 10f, Mathf.Max(halfW + 10f, sizeP.x * 0.50f - halfW));
                bottomLeftPos.y = Mathf.Clamp(bottomLeftPos.y, halfH + 10f, Mathf.Max(halfH + 10f, sizeP.y * 0.82f - halfH));
                _bgRect.anchoredPosition = bottomLeftPos;
            }

            if (_ringImage != null)
            {
                var c = _ringImage.color;
                c.a = 0.92f;
                _ringImage.color = c;
            }
            _activePointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsEditingHud) return;
            if (_activePointerId != int.MinValue) return;
            _activePointerId = eventData.pointerId;
            if (_ringImage != null)
            {
                var c = _ringImage.color;
                c.a = 0.92f;
                _ringImage.color = c;
            }
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (IsEditingHud) return;
            if (eventData.pointerId != _activePointerId || _bgRect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_bgRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;
            Vector2 clamped = Vector2.ClampMagnitude(local, maxRadius);
            if (knobRect != null) knobRect.anchoredPosition = clamped;
            _currentValue = clamped / Mathf.Max(1f, maxRadius);
            MobileAndDesktopInputDriver.Active?.SetVirtualMove(_currentValue);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            ResetStick();
        }

        public void ResetStick()
        {
            _activePointerId = int.MinValue;
            _currentValue = Vector2.zero;
            if (knobRect != null) knobRect.anchoredPosition = Vector2.zero;
            if (_hasRestPos && _bgRect != null) _bgRect.anchoredPosition = _restAnchoredPos;
            if (_ringImage != null)
            {
                var c = _ringImage.color;
                c.a = 0.42f;
                _ringImage.color = c;
            }
            MobileAndDesktopInputDriver.Active?.SetVirtualMove(Vector2.zero);
        }
    }

    /// <summary>
    /// Full left-side touch zone that spawns the floating joystick ring directly under the player's finger.
    /// </summary>
    public sealed class LeftJoystickZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private VirtualThumbstick _stick;
        private int _pointerId = int.MinValue;

        public void Bind(VirtualThumbstick stick) => _stick = stick;

        private void OnDisable() => _pointerId = int.MinValue;
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) _pointerId = int.MinValue;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_stick == null || _stick.IsEditingHud || _pointerId != int.MinValue) return;
            _pointerId = eventData.pointerId;
            _stick.BeginFloatingAtScreenPos(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_stick != null && eventData.pointerId == _pointerId)
                _stick.OnDrag(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = int.MinValue;
            _stick?.OnPointerUp(eventData);
        }
    }

    /// <summary>
    /// Multi-touch camera look drag region for the right side of mobile touchscreens.
    /// </summary>
    public sealed class TouchLookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int _activePointerId = int.MinValue;
        private Vector2 _lastPos;

        public bool IsEditingHud { get; set; }

        private void OnDisable() => _activePointerId = int.MinValue;
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) _activePointerId = int.MinValue;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsEditingHud || _activePointerId != int.MinValue) return;
            _activePointerId = eventData.pointerId;
            _lastPos = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (IsEditingHud || eventData.pointerId != _activePointerId) return;
            Vector2 delta = eventData.position - _lastPos;
            _lastPos = eventData.position;
            MobileAndDesktopInputDriver.Active?.AddTouchLook(delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            _activePointerId = int.MinValue;
        }
    }
}
