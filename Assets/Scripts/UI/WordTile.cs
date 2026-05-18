using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(LayoutElement))]
    public class WordTile : MonoBehaviour
    {
        [SerializeField] private Image shadowImage;
        [SerializeField] private Image contactShadowImage;
        [SerializeField] private Image raycastImage;
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

        private string _word = string.Empty;

        public event Action<string> OnWordSelected;
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

        public void SetWord(string word)
        {
            _word = word ?? string.Empty;
            EnsureBuilt();
            ResetVisualState();
            label.text = _word;
        }

        public void ClearWord()
        {
            _word = string.Empty;
            EnsureBuilt();
            ResetVisualState();
            label.text = string.Empty;
        }

        public void ResetVisualState()
        {
            if (outlineImage != null)
                outlineImage.enabled = false;

            if (progressImage != null)
                progressImage.fillAmount = 0f;

            if (dwellSelectable != null)
                dwellSelectable.ForceResetSelectionGate();

            if (backgroundImage != null)
                backgroundImage.color = UiTheme.Glass;

            OnHoverExit?.Invoke();
        }

        public void SetFlickerHz(float hz)
        {
            EnsureBuilt();
            flickerTile.FlickerHz = hz;
        }

        private void HandleSelected()
        {
            if (!string.IsNullOrWhiteSpace(_word))
            {
                string selectedWord = _word;
                animator?.FlyLabelTo(flyTarget, selectedWord);
                ClearWord();
                gameObject.SetActive(false);
                OnWordSelected?.Invoke(selectedWord);
            }
        }

        private void HandleHoverEnter()
        {
            animator?.PlayHoverEnter();

            if (!string.IsNullOrWhiteSpace(_word))
                OnHoverPreview?.Invoke(_word);
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
                raycastImage.sprite = UiTheme.WordSprite;
                raycastImage.type = Image.Type.Sliced;
                raycastImage.color = new Color(1f, 1f, 1f, 0f);
                raycastImage.raycastTarget = true;
            }

            if (shadowImage == null)
                shadowImage = UiTheme.AddGlassShadow(gameObject);
            shadowImage.sprite = UiTheme.WordSprite;

            if (contactShadowImage == null)
                contactShadowImage = UiTheme.AddContactShadow(gameObject);
            contactShadowImage.sprite = UiTheme.WordSprite;

            if (flickerOverlayImage == null)
                flickerOverlayImage = UiTheme.AddImage(gameObject, "FlickerOverlay", UiTheme.FlickerLow, UiTheme.WordSprite);
            flickerOverlayImage.sprite = UiTheme.WordSprite;
            flickerOverlayImage.color = UiTheme.FlickerLow;
            flickerOverlayImage.raycastTarget = false;

            if (backgroundImage == null)
                backgroundImage = UiTheme.AddImage(gameObject, "Glass", UiTheme.Glass);
            backgroundImage.sprite = UiTheme.WordSprite;
            backgroundImage.color = UiTheme.Glass;
            backgroundImage.raycastTarget = false;
            flickerOverlayImage.transform.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex() + 1);

            if (tintImage == null)
            {
                tintImage = UiTheme.AddImage(gameObject, "InnerBlueTint", UiTheme.InnerBlueTint);
                tintImage.raycastTarget = false;
            }
            tintImage.sprite = UiTheme.WordSprite;

            if (highlightImage == null)
                highlightImage = UiTheme.AddTopHighlight(gameObject);

            if (edgeImage == null)
                edgeImage = UiTheme.AddGlassEdge(gameObject);
            edgeImage.sprite = UiTheme.WordSprite;

            if (outlineImage == null)
            {
                outlineImage = UiTheme.AddImage(gameObject, "HoverHalo", UiTheme.HaloWarm, UiTheme.WordSprite);
                UiTheme.Stretch(outlineImage.rectTransform, -10f, -10f);
                outlineImage.enabled = false;
            }
            outlineImage.sprite = UiTheme.WordSprite;
            outlineImage.raycastTarget = false;

            if (progressImage == null)
            {
                progressImage = UiTheme.AddImage(gameObject, "DwellHaloProgress", Color.white, UiTheme.WordSprite);
                UiTheme.Stretch(progressImage.rectTransform, -8f, -8f);
                DwellSelectable.ConfigureProgressImage(progressImage);
            }
            progressImage.sprite = UiTheme.WordSprite;
            DwellSelectable.ConfigureProgressImage(progressImage);
            progressImage.sprite = UiTheme.WordSprite;

            if (label == null)
                label = UiTheme.AddText(gameObject, "Label", 24);
            label.fontSize = 24f;
            label.characterSpacing = -0.3f;
            label.fontStyle = FontStyles.Normal;
            if (UiTheme.RegularFont != null)
                label.font = UiTheme.RegularFont;

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
