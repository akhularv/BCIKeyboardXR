using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class PremiumTileAnimator : MonoBehaviour
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image outlineImage;
        [SerializeField] private Image shadowImage;

        private Coroutine _hoverRoutine;
        private Coroutine _selectionRoutine;
        private Color _backgroundBase;
        private Color _backgroundHover;
        private Color _outlineBase;
        private Color _shadowBase;

        public void Configure(Image background, Image outline, Image shadow)
        {
            backgroundImage = background;
            outlineImage = outline;
            shadowImage = shadow;

            if (backgroundImage != null)
            {
                _backgroundBase = backgroundImage.color;
                _backgroundHover = new Color(backgroundImage.color.r, backgroundImage.color.g, backgroundImage.color.b, Mathf.Max(backgroundImage.color.a, 0.55f));
            }
            if (outlineImage != null)
            {
                _outlineBase = outlineImage.color;
                SetImageAlpha(outlineImage, 0f);
                outlineImage.enabled = false;
            }
            if (shadowImage != null)
                _shadowBase = shadowImage.color;
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        public void PlayHoverEnter()
        {
            if (_hoverRoutine != null)
                StopCoroutine(_hoverRoutine);

            _hoverRoutine = StartCoroutine(HoverRoutine(1.03f, 1f, 0.30f, true));
        }

        public void PlayHoverExit()
        {
            if (_hoverRoutine != null)
                StopCoroutine(_hoverRoutine);

            _hoverRoutine = StartCoroutine(HoverRoutine(1f, 0f, 0.25f, false));
        }

        public void PlaySelection()
        {
            if (_selectionRoutine != null)
                StopCoroutine(_selectionRoutine);

            _selectionRoutine = StartCoroutine(SelectionRoutine());
        }

        public void FlyLabelTo(RectTransform destination, string text)
        {
            if (destination == null || string.IsNullOrWhiteSpace(text))
                return;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            var go = new GameObject("CommitFlyLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            var label = go.GetComponent<TextMeshProUGUI>();
            var group = go.GetComponent<CanvasGroup>();

            label.text = text.Trim();
            label.color = UiTheme.Charcoal;
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            if (UiTheme.MediumFont != null)
                label.font = UiTheme.MediumFont;

            Vector3 startWorld = transform.position;
            Vector3 endWorld = destination.TransformPoint(destination.rect.center);
            rect.position = startWorld;
            rect.sizeDelta = new Vector2(420f, 60f);
            StartCoroutine(FlyRoutine(rect, group, startWorld, endWorld, 0.40f));
        }

        private IEnumerator HoverRoutine(float targetScale, float targetOutlineAlpha, float seconds, bool keepOutlineEnabled)
        {
            Vector3 startScale = transform.localScale;
            Vector3 endScale = Vector3.one * targetScale;
            float startOutlineAlpha = outlineImage != null ? outlineImage.color.a : 0f;
            float startShadowAlpha = shadowImage != null ? shadowImage.color.a : 0f;
            float endShadowAlpha = keepOutlineEnabled ? Mathf.Min(_shadowBase.a + 0.08f, 0.35f) : _shadowBase.a;
            Vector3 startShadowScale = shadowImage != null ? shadowImage.rectTransform.localScale : Vector3.one;
            Vector3 endShadowScale = Vector3.one * (keepOutlineEnabled ? 1.04f : 1f);
            Color startBackground = backgroundImage != null ? backgroundImage.color : Color.white;
            Color endBackground = keepOutlineEnabled ? _backgroundHover : _backgroundBase;

            if (outlineImage != null)
                outlineImage.enabled = true;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float linear = Mathf.Clamp01(elapsed / seconds);
                float t = keepOutlineEnabled ? EaseOutBack(linear, 0.5f) : EaseInOutQuart(linear);
                float fadeT = EaseOutQuart(linear);
                transform.localScale = Vector3.LerpUnclamped(startScale, endScale, t);

                if (outlineImage != null)
                    SetImageAlpha(outlineImage, Mathf.Lerp(startOutlineAlpha, targetOutlineAlpha * _outlineBase.a, fadeT));

                if (shadowImage != null)
                {
                    SetImageAlpha(shadowImage, Mathf.Lerp(startShadowAlpha, endShadowAlpha, fadeT));
                    shadowImage.rectTransform.localScale = Vector3.LerpUnclamped(startShadowScale, endShadowScale, fadeT);
                }

                if (backgroundImage != null)
                    backgroundImage.color = Color.Lerp(startBackground, endBackground, fadeT);

                yield return null;
            }

            transform.localScale = endScale;
            if (outlineImage != null)
            {
                SetImageAlpha(outlineImage, targetOutlineAlpha * _outlineBase.a);
                outlineImage.enabled = keepOutlineEnabled;
            }
            if (backgroundImage != null)
                backgroundImage.color = endBackground;

            _hoverRoutine = null;
        }

        private IEnumerator SelectionRoutine()
        {
            Vector3 startScale = transform.localScale;
            Color startBackground = backgroundImage != null ? backgroundImage.color : Color.white;

            yield return ScaleAndFlash(startScale, Vector3.one * 0.96f, startBackground, Color.white, 0.12f);
            yield return ScaleAndFlash(Vector3.one * 0.96f, Vector3.one, Color.white, _backgroundBase, 0.23f);

            _selectionRoutine = null;
        }

        private IEnumerator ScaleAndFlash(Vector3 fromScale, Vector3 toScale, Color fromColor, Color toColor, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutBack(Mathf.Clamp01(elapsed / seconds), 1.2f);
                transform.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
                if (backgroundImage != null)
                    backgroundImage.color = Color.Lerp(fromColor, toColor, t);
                yield return null;
            }
        }

        private static IEnumerator FlyRoutine(RectTransform rect, CanvasGroup group, Vector3 start, Vector3 end, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutQuart(Mathf.Clamp01(elapsed / seconds));
                rect.position = Vector3.LerpUnclamped(start, end, t);
                group.alpha = 1f - Mathf.Clamp01(elapsed / seconds);
                yield return null;
            }

            Destroy(rect.gameObject);
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private static float EaseOutQuart(float t)
        {
            return 1f - Mathf.Pow(1f - t, 4f);
        }

        private static float EaseInOutQuart(float t)
        {
            return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) * 0.5f;
        }

        private static float EaseOutBack(float t, float overshoot)
        {
            float c1 = overshoot;
            float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
