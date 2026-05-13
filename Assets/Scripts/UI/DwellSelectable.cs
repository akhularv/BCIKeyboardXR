using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace BCIKeyboardXR.UI
{
    public class DwellSelectable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
    {
        public static readonly List<DwellSelectable> Registry = new List<DwellSelectable>();

        [SerializeField] private float dwellSeconds = 0.8f;
        [SerializeField] private Image progressImage;
        [SerializeField] private bool dwellEnabled = true;

        public UnityEvent onSelected = new UnityEvent();
        public UnityEvent onHoverEnter = new UnityEvent();
        public UnityEvent onHoverExit = new UnityEvent();

        private bool _isHovering;
        private bool _hasSelected;
        private bool _requiresExitAfterSelection;
        private float _hoverTime;

        public float DwellSeconds
        {
            get => dwellSeconds;
            set => dwellSeconds = Mathf.Max(0.05f, value);
        }

        public Image ProgressImage
        {
            get => progressImage;
            set
            {
                progressImage = value;
                ResetProgress();
            }
        }

        public bool DwellEnabled
        {
            get => dwellEnabled;
            set
            {
                dwellEnabled = value;
                if (!dwellEnabled)
                    ResetHover();
            }
        }

        public static void SetAllDwellEnabled(bool enabled)
        {
            for (int i = Registry.Count - 1; i >= 0; i--)
            {
                if (Registry[i] == null)
                {
                    Registry.RemoveAt(i);
                    continue;
                }

                Registry[i].DwellEnabled = enabled;
            }
        }

        private void Awake()
        {
            if (progressImage != null)
                ConfigureProgressImage(progressImage);
            ResetProgress();
        }

        private void OnEnable()
        {
            if (!Registry.Contains(this))
                Registry.Add(this);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
            _requiresExitAfterSelection = false;
            ResetHover();
        }

        public void ForceResetSelectionGate()
        {
            _requiresExitAfterSelection = false;
            ResetHover();
        }

        private void Update()
        {
            SyncMouseHoverFallback();

            if (!_isHovering || _hasSelected || !dwellEnabled)
                return;

            _hoverTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_hoverTime / dwellSeconds);
            if (progressImage != null)
            {
                progressImage.fillAmount = progress;
                float pulse = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f) + 1f) * 0.5f;
                Color color = Color.Lerp(UiTheme.HaloWarm, UiTheme.HaloComplete, progress);
                color.a = Mathf.Lerp(0.24f, 0.62f, progress) * Mathf.Lerp(0.85f, 1.08f, pulse);
                progressImage.color = color;
                progressImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.02f, 1.055f, pulse);
            }

            if (progress >= 1f)
            {
                _hasSelected = true;
                _requiresExitAfterSelection = true;
                onSelected.Invoke();
                onHoverExit.Invoke();
                ResetHover();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            BeginHover();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!_isHovering)
                BeginHover();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_isHovering && !_hasSelected)
                onHoverExit.Invoke();

            _requiresExitAfterSelection = false;
            ResetHover();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!dwellEnabled)
                return;

            _hasSelected = true;
            _requiresExitAfterSelection = true;
            onSelected.Invoke();
            onHoverExit.Invoke();
            ResetHover();
        }

        public void ResetHover()
        {
            _isHovering = false;
            _hasSelected = false;
            _hoverTime = 0f;
            ResetProgress();
        }

        private void ResetProgress()
        {
            if (progressImage != null)
            {
                progressImage.fillAmount = 0f;
                progressImage.rectTransform.localScale = Vector3.one;
                progressImage.color = new Color(UiTheme.HaloWarm.r, UiTheme.HaloWarm.g, UiTheme.HaloWarm.b, 0f);
            }
        }

        private void BeginHover()
        {
            if (!dwellEnabled || _requiresExitAfterSelection)
                return;

            _isHovering = true;
            _hasSelected = false;
            _hoverTime = 0f;
            ResetProgress();
            onHoverEnter.Invoke();
        }

        private void SyncMouseHoverFallback()
        {
            if (Mouse.current == null)
                return;

            var rect = transform as RectTransform;
            if (rect == null)
                return;

            bool containsMouse = RectTransformUtility.RectangleContainsScreenPoint(
                rect,
                Mouse.current.position.ReadValue(),
                null);

            if (containsMouse)
            {
                if (!_isHovering)
                    BeginHover();
            }
            else
            {
                if (_isHovering && !_hasSelected)
                    onHoverExit.Invoke();

                _requiresExitAfterSelection = false;
                ResetHover();
            }
        }

        public static void ConfigureProgressImage(Image image)
        {
            image.sprite = UiTheme.RoundedSprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = 2;
            image.fillClockwise = true;
            image.fillAmount = 0f;
            image.color = new Color(UiTheme.HaloWarm.r, UiTheme.HaloWarm.g, UiTheme.HaloWarm.b, 0f);
            image.raycastTarget = false;
        }
    }
}
