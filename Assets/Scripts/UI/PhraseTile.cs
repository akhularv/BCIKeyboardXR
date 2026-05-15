using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class PhraseTile : MonoBehaviour
    {
        [SerializeField] private Image raycastImage;
        [SerializeField] private Image shadowImage;
        [SerializeField] private Image contactShadowImage;
        [SerializeField] private Image flickerOverlayImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image tintImage;
        [SerializeField] private Image highlightImage;
        [SerializeField] private Image edgeImage;
        [SerializeField] private Image outlineImage;
        [SerializeField] private Image progressImage;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private DwellSelectable dwellSelectable;
        [SerializeField] private FlickerTile flickerTile;
        [SerializeField] private PremiumTileAnimator animator;
        [SerializeField] private RectTransform flyTarget;

        private string _phrase = string.Empty;

        public event Action<string> OnPhraseSelected;
        public event Action<string> OnHoverPreview;
        public event Action OnHoverExit;

        public FlickerTile Flicker => flickerTile;
        public RectTransform FlyTarget { get => flyTarget; set => flyTarget = value; }

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            dwellSelectable.onSelected.AddListener(HandleSelected);
            dwellSelectable.onHoverEnter.AddListener(HandleHoverEnter);
            dwellSelectable.onHoverExit.AddListener(HandleHoverExit);
        }

        private void OnDisable()
        {
            if (dwellSelectable == null)
                return;

            dwellSelectable.onSelected.RemoveListener(HandleSelected);
            dwellSelectable.onHoverEnter.RemoveListener(HandleHoverEnter);
            dwellSelectable.onHoverExit.RemoveListener(HandleHoverExit);
        }

        public void SetPhrase(string phrase)
        {
            _phrase = phrase ?? string.Empty;
            EnsureBuilt();
            label.text = _phrase;
        }

        public void SetFlickerHz(float hz)
        {
            EnsureBuilt();
            flickerTile.FlickerHz = hz;
        }

        private void HandleSelected()
        {
            if (!string.IsNullOrWhiteSpace(_phrase))
            {
                animator?.PlaySelection();
                animator?.FlyLabelTo(flyTarget, _phrase);
                OnPhraseSelected?.Invoke(_phrase);
            }
        }

        private void HandleHoverEnter()
        {
            animator?.PlayHoverEnter();

            if (!string.IsNullOrWhiteSpace(_phrase))
                OnHoverPreview?.Invoke(_phrase);
        }

        private void HandleHoverExit()
        {
            animator?.PlayHoverExit();

            OnHoverExit?.Invoke();
        }

        private void EnsureBuilt()
        {
            if (raycastImage == null)
            {
                raycastImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
                raycastImage.sprite = UiTheme.PhraseSprite;
                raycastImage.type = Image.Type.Sliced;
                raycastImage.color = new Color(1f, 1f, 1f, 0f);
                raycastImage.raycastTarget = true;
            }

            if (shadowImage == null)
                shadowImage = UiTheme.AddGlassShadow(gameObject);
            shadowImage.sprite = UiTheme.PhraseSprite;

            if (contactShadowImage == null)
                contactShadowImage = UiTheme.AddContactShadow(gameObject);
            contactShadowImage.sprite = UiTheme.PhraseSprite;

            if (flickerOverlayImage == null)
                flickerOverlayImage = UiTheme.AddImage(gameObject, "FlickerOverlay", UiTheme.FlickerLow, UiTheme.PhraseSprite);
            flickerOverlayImage.sprite = UiTheme.PhraseSprite;
            flickerOverlayImage.color = UiTheme.FlickerLow;
            flickerOverlayImage.raycastTarget = false;

            if (backgroundImage == null)
                backgroundImage = UiTheme.AddImage(gameObject, "Glass", UiTheme.Glass);
            backgroundImage.sprite = UiTheme.PhraseSprite;
            backgroundImage.color = UiTheme.Glass;
            backgroundImage.raycastTarget = false;
            flickerOverlayImage.transform.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex() + 1);

            if (tintImage == null)
            {
                tintImage = UiTheme.AddImage(gameObject, "InnerBlueTint", UiTheme.InnerBlueTint);
                tintImage.raycastTarget = false;
            }
            tintImage.sprite = UiTheme.PhraseSprite;

            if (highlightImage == null)
                highlightImage = UiTheme.AddTopHighlight(gameObject);

            if (edgeImage == null)
                edgeImage = UiTheme.AddGlassEdge(gameObject);
            edgeImage.sprite = UiTheme.PhraseSprite;

            if (outlineImage == null)
            {
                outlineImage = UiTheme.AddImage(gameObject, "HoverHalo", UiTheme.HaloWarm, UiTheme.PhraseSprite);
                UiTheme.Stretch(outlineImage.rectTransform, -12f, -12f);
                outlineImage.enabled = false;
            }
            outlineImage.sprite = UiTheme.PhraseSprite;
            outlineImage.raycastTarget = false;

            if (progressImage == null)
            {
                progressImage = UiTheme.AddImage(gameObject, "DwellHaloProgress", Color.white, UiTheme.PhraseSprite);
                UiTheme.Stretch(progressImage.rectTransform, -10f, -10f);
                DwellSelectable.ConfigureProgressImage(progressImage);
            }
            progressImage.sprite = UiTheme.PhraseSprite;
            DwellSelectable.ConfigureProgressImage(progressImage);
            progressImage.sprite = UiTheme.PhraseSprite;

            if (label == null)
                label = UiTheme.AddText(gameObject, "Label", 28);
            label.fontSize = 28f;
            label.characterSpacing = -0.5f;
            label.fontStyle = FontStyles.Bold;
            if (UiTheme.MediumFont != null)
                label.font = UiTheme.MediumFont;

            if (dwellSelectable == null)
                dwellSelectable = gameObject.GetComponent<DwellSelectable>() ?? gameObject.AddComponent<DwellSelectable>();

            dwellSelectable.DwellSeconds = 0.7f;
            dwellSelectable.ProgressImage = progressImage;

            if (flickerTile == null)
                flickerTile = gameObject.GetComponent<FlickerTile>() ?? gameObject.AddComponent<FlickerTile>();

            flickerTile.TargetImage = flickerOverlayImage;
            flickerTile.SetColorRange(UiTheme.FlickerLow, UiTheme.FlickerHigh);

            if (animator == null)
                animator = gameObject.GetComponent<PremiumTileAnimator>() ?? gameObject.AddComponent<PremiumTileAnimator>();

            animator.Configure(backgroundImage, outlineImage, shadowImage);
        }
    }
}
