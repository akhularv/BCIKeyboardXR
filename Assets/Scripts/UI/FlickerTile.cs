using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class FlickerTile : MonoBehaviour
    {
        [SerializeField] private float flickerHz = 8f;
        [SerializeField] private Image targetImage;
        [SerializeField] private float minAlpha = 0.85f;
        [SerializeField] private float maxAlpha = 1f;
        [SerializeField] private bool flickerEnabled = true;

        private Color _baseColor;

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
                    _baseColor.a = maxAlpha;
                    targetImage.color = _baseColor;
                }
            }
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
            _baseColor = targetImage.color;
            _baseColor.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            targetImage.color = _baseColor;
        }

        private void CacheBaseColor()
        {
            if (targetImage != null)
                _baseColor = targetImage.color;
        }
    }
}
