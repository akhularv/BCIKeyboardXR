using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class FlickerTile : MonoBehaviour
    {
        [SerializeField] private float flickerHz = 8f;
        [SerializeField] private Image targetImage;
        [SerializeField] private float minAlpha = 0.08f;
        [SerializeField] private float maxAlpha = 0.38f;
        [SerializeField] private Color lowColor = new Color(0.84f, 0.91f, 1f, 0.08f);
        [SerializeField] private Color highColor = new Color(1f, 1f, 1f, 0.38f);
        [SerializeField] private bool flickerEnabled = true;

        public float FlickerHz
        {
            get => flickerHz;
            set => flickerHz = Mathf.Max(0f, value);
        }

        public Image TargetImage
        {
            get => targetImage;
            set
            {
                targetImage = value;
                CacheBaseColor();
            }
        }

        public bool FlickerEnabled
        {
            get => flickerEnabled;
            set
            {
                flickerEnabled = value;
                if (!flickerEnabled && targetImage != null)
                {
                    targetImage.color = highColor;
                }
            }
        }

        public void SetAlphaRange(float min, float max)
        {
            minAlpha = Mathf.Clamp01(min);
            maxAlpha = Mathf.Clamp01(max);
            if (maxAlpha < minAlpha)
                (minAlpha, maxAlpha) = (maxAlpha, minAlpha);
        }

        public void SetColorRange(Color low, Color high)
        {
            lowColor = low;
            highColor = high;
            minAlpha = low.a;
            maxAlpha = high.a;
            CacheBaseColor();
        }

        private void Awake()
        {
            CacheBaseColor();
        }

        private void Update()
        {
            if (!flickerEnabled || targetImage == null)
                return;

            float t = (Mathf.Sin(Time.unscaledTime * flickerHz * Mathf.PI * 2f) + 1f) * 0.5f;
            Color color = Color.Lerp(lowColor, highColor, t);
            color.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            targetImage.color = color;
        }

        private void CacheBaseColor()
        {
            if (targetImage != null)
            {
                targetImage.enabled = true;
            }
        }
    }
}
