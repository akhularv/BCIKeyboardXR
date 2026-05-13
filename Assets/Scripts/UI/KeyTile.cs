using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public enum KeyVariant
    {
        Standard,
        Action
    }

    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(LayoutElement))]
    public class KeyTile : MonoBehaviour
    {
        [SerializeField] private Image raycastImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image shadowImage;
        [SerializeField] private Image contactShadowImage;
        [SerializeField] private Image tintImage;
        [SerializeField] private Image highlightImage;
        [SerializeField] private Image edgeImage;
        [SerializeField] private Image outlineImage;
        [SerializeField] private Image progressImage;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private DwellSelectable dwellSelectable;
        [SerializeField] private FlickerTile flickerTile;
        [SerializeField] private PremiumTileAnimator animator;
        [SerializeField] private KeyVariant variant = KeyVariant.Standard;

        private string _output = string.Empty;

        public event Action<string> OnKeySelected;
        public FlickerTile Flicker => flickerTile;

        private void Awake()
        {
            EnsureBuilt();
            ApplyVariant();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            dwellSelectable.onSelected.AddListener(HandleSelected);
            dwellSelectable.onHoverEnter.AddListener(ShowHover);
            dwellSelectable.onHoverExit.AddListener(HideHover);
        }

        private void OnDisable()
        {
            if (dwellSelectable == null)
                return;

            dwellSelectable.onSelected.RemoveListener(HandleSelected);
            dwellSelectable.onHoverEnter.RemoveListener(ShowHover);
            dwellSelectable.onHoverExit.RemoveListener(HideHover);
        }

        public void SetKey(char key)
        {
            SetLabel(key.ToString().ToUpperInvariant(), key.ToString().ToLowerInvariant(), KeyVariant.Standard);
        }

        public void SetLabel(string displayLabel, string output, KeyVariant keyVariant = KeyVariant.Standard)
        {
            variant = keyVariant;
            _output = output ?? displayLabel ?? string.Empty;
            EnsureBuilt();
            label.text = displayLabel ?? string.Empty;
            ApplyVariant();
        }

        public void SetFlickerHz(float hz)
        {
            EnsureBuilt();
            flickerTile.FlickerHz = hz;
        }

        private void HandleSelected()
        {
            if (!string.IsNullOrEmpty(_output))
            {
                animator?.PlaySelection();
                OnKeySelected?.Invoke(_output);
            }
        }

        private void ShowHover()
        {
            animator?.PlayHoverEnter();
        }

        private void HideHover()
        {
            animator?.PlayHoverExit();
        }

        private void ApplyVariant()
        {
            if (backgroundImage == null)
                return;

            backgroundImage.color = variant == KeyVariant.Action ? UiTheme.WarmGlass : UiTheme.GlassBright;
            if (label != null)
            {
                label.fontSize = variant == KeyVariant.Action ? 16f : 28f;
                label.characterSpacing = variant == KeyVariant.Action ? 1.0f : 0f;
                label.fontStyle = FontStyles.Bold;
                label.text = variant == KeyVariant.Action ? label.text.ToUpperInvariant() : label.text;
                label.font = variant == KeyVariant.Action && UiTheme.SemiboldFont != null
                    ? UiTheme.SemiboldFont
                    : UiTheme.MediumFont != null ? UiTheme.MediumFont : label.font;
            }

            if (animator != null)
                animator.Configure(backgroundImage, outlineImage, shadowImage);
        }

        private void EnsureBuilt()
        {
            if (raycastImage == null)
            {
                raycastImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
                raycastImage.sprite = UiTheme.KeySprite;
                raycastImage.type = Image.Type.Sliced;
                raycastImage.color = new Color(1f, 1f, 1f, 0f);
                raycastImage.raycastTarget = true;
            }

            if (shadowImage == null)
                shadowImage = UiTheme.AddGlassShadow(gameObject);
            shadowImage.sprite = UiTheme.KeySprite;

            if (contactShadowImage == null)
                contactShadowImage = UiTheme.AddContactShadow(gameObject);
            contactShadowImage.sprite = UiTheme.KeySprite;

            if (backgroundImage == null)
                backgroundImage = UiTheme.AddImage(gameObject, "Glass", UiTheme.GlassBright);
            backgroundImage.sprite = UiTheme.KeySprite;
            backgroundImage.raycastTarget = false;

            if (tintImage == null)
            {
                tintImage = UiTheme.AddImage(gameObject, "InnerBlueTint", UiTheme.InnerBlueTint);
                tintImage.raycastTarget = false;
            }
            tintImage.sprite = UiTheme.KeySprite;

            if (highlightImage == null)
                highlightImage = UiTheme.AddTopHighlight(gameObject);

            if (edgeImage == null)
                edgeImage = UiTheme.AddGlassEdge(gameObject);
            edgeImage.sprite = UiTheme.KeySprite;

            if (outlineImage == null)
            {
                outlineImage = UiTheme.AddImage(gameObject, "HoverHalo", UiTheme.HaloWarm, UiTheme.KeySprite);
                UiTheme.Stretch(outlineImage.rectTransform, -8f, -8f);
                outlineImage.enabled = false;
            }
            outlineImage.sprite = UiTheme.KeySprite;
            outlineImage.raycastTarget = false;

            if (progressImage == null)
            {
                progressImage = UiTheme.AddImage(gameObject, "DwellHaloProgress", Color.white, UiTheme.KeySprite);
                UiTheme.Stretch(progressImage.rectTransform, -7f, -7f);
                DwellSelectable.ConfigureProgressImage(progressImage);
            }
            progressImage.sprite = UiTheme.KeySprite;
            DwellSelectable.ConfigureProgressImage(progressImage);
            progressImage.sprite = UiTheme.KeySprite;

            if (label == null)
            {
                label = UiTheme.AddText(gameObject, "Label", 28);
                label.fontStyle = FontStyles.Normal;
            }

            if (dwellSelectable == null)
                dwellSelectable = gameObject.GetComponent<DwellSelectable>() ?? gameObject.AddComponent<DwellSelectable>();

            dwellSelectable.DwellSeconds = 0.8f;
            dwellSelectable.ProgressImage = progressImage;

            if (flickerTile == null)
                flickerTile = gameObject.GetComponent<FlickerTile>() ?? gameObject.AddComponent<FlickerTile>();

            flickerTile.TargetImage = backgroundImage;

            if (animator == null)
                animator = gameObject.GetComponent<PremiumTileAnimator>() ?? gameObject.AddComponent<PremiumTileAnimator>();

            animator.Configure(backgroundImage, outlineImage, shadowImage);
        }
    }
}
