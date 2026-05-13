using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    internal static class UiTheme
    {
        public static readonly Color BackgroundEdge = Hex(0xE4, 0xE8, 0xEE);
        public static readonly Color BackgroundBase = Hex(0xEE, 0xF1, 0xF5);
        public static readonly Color BackgroundCenter = Hex(0xF4, 0xF6, 0xFA);
        public static readonly Color Charcoal = Hex(0x1F, 0x27, 0x35);
        public static readonly Color CursorBlue = Hex(0x40, 0x70, 0xC0);
        public static readonly Color Glass = new Color(1f, 1f, 1f, 0.40f);
        public static readonly Color GlassBright = new Color(1f, 1f, 1f, 0.46f);
        public static readonly Color GlassHover = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color WarmGlass = Hex(0xFF, 0xFA, 0xEE, 0.43f);
        public static readonly Color WarmGlassHover = Hex(0xFF, 0xFA, 0xEE, 0.57f);
        public static readonly Color InnerBlueTint = Hex(0xF4, 0xF8, 0xFC, 0.12f);
        public static readonly Color InnerHighlight = new Color(1f, 1f, 1f, 0.25f);
        public static readonly Color GlassEdge = new Color(1f, 1f, 1f, 0.25f);
        public static readonly Color AmbientShadow = Hex(0x7E, 0x90, 0xA8, 0.08f);
        public static readonly Color ContactShadow = Hex(0x4A, 0x58, 0x70, 0.12f);
        public static readonly Color PulseBlue = Hex(0xB8, 0xD8, 0xFF, 0.50f);
        public static readonly Color HaloWarm = Hex(0xFF, 0xF8, 0xE8, 0.40f);
        public static readonly Color HaloComplete = Hex(0xB8, 0xD8, 0xFF, 0.70f);
        public static readonly Color Ghost = Hex(0x1F, 0x27, 0x35, 0.35f);

        private static Sprite _roundedSprite;
        private static Sprite _phraseSprite;
        private static Sprite _wordSprite;
        private static Sprite _keySprite;
        private static Sprite _smallSprite;
        private static Sprite _softCircleSprite;
        private static Sprite _topHighlightSprite;
        private static Texture2D _radialTexture;
        private static TMP_FontAsset _defaultFont;

        public static Color Hex(byte r, byte g, byte b, float a = 1f)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite == null)
                    _roundedSprite = CreateRoundedSprite(64, 16, 18);
                return _roundedSprite;
            }
        }

        public static Sprite PhraseSprite => _phraseSprite ??= CreateRoundedSprite(96, 22, 24);
        public static Sprite WordSprite => _wordSprite ??= CreateRoundedSprite(96, 18, 20);
        public static Sprite KeySprite => _keySprite ??= CreateRoundedSprite(96, 14, 16);
        public static Sprite SmallSprite => _smallSprite ??= CreateRoundedSprite(64, 8, 10);

        public static Sprite SoftCircleSprite
        {
            get
            {
                if (_softCircleSprite == null)
                    _softCircleSprite = CreateRoundedSprite(64, 32, 0);
                return _softCircleSprite;
            }
        }

        public static Sprite TopHighlightSprite
        {
            get
            {
                if (_topHighlightSprite == null)
                    _topHighlightSprite = CreateTopHighlightSprite(32, 128);
                return _topHighlightSprite;
            }
        }

        public static Texture2D RadialBackgroundTexture
        {
            get
            {
                if (_radialTexture == null)
                    _radialTexture = CreateRadialTexture(512);
                return _radialTexture;
            }
        }

        public static TMP_FontAsset DefaultFont
        {
            get
            {
                if (_defaultFont == null)
                    _defaultFont = TMP_Settings.defaultFontAsset;

                return _defaultFont;
            }
        }

        public static TMP_FontAsset RegularFont => DefaultFont;
        public static TMP_FontAsset MediumFont => DefaultFont;
        public static TMP_FontAsset SemiboldFont => DefaultFont;

        public static Image AddImage(GameObject parent, string name, Color color)
        {
            return AddImage(parent, name, color, RoundedSprite);
        }

        public static Image AddImage(GameObject parent, string name, Color color, Sprite sprite)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(parent.transform, false);
            var image = child.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            Stretch(image.rectTransform);
            return image;
        }

        public static TextMeshProUGUI AddText(GameObject parent, string name, int size, FontStyles style = FontStyles.Normal)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            child.transform.SetParent(parent.transform, false);
            var text = child.GetComponent<TextMeshProUGUI>();
            if (DefaultFont != null)
                text.font = DefaultFont;
            text.color = Charcoal;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            Stretch(text.rectTransform, 16f, 8f);
            return text;
        }

        public static Image AddGlassShadow(GameObject parent, string name = "Shadow")
        {
            Image shadow = AddImage(parent, name, AmbientShadow);
            Stretch(shadow.rectTransform, -14f, -14f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            shadow.raycastTarget = false;
            shadow.transform.SetAsFirstSibling();
            return shadow;
        }

        public static Image AddContactShadow(GameObject parent, string name = "ContactShadow")
        {
            Image shadow = AddImage(parent, name, ContactShadow);
            Stretch(shadow.rectTransform, -3f, -3f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -1f);
            shadow.raycastTarget = false;
            shadow.transform.SetAsFirstSibling();
            return shadow;
        }

        public static Image AddGlassEdge(GameObject parent, string name = "GlassEdge")
        {
            Image edge = AddImage(parent, name, GlassEdge);
            Stretch(edge.rectTransform, -0.5f, -0.5f);
            edge.raycastTarget = false;
            return edge;
        }

        public static Image AddTopHighlight(GameObject parent, string name = "InnerHighlight")
        {
            Image highlight = AddImage(parent, name, Color.white, TopHighlightSprite);
            highlight.type = Image.Type.Simple;
            highlight.color = Color.white;
            highlight.raycastTarget = false;
            Stretch(highlight.rectTransform);
            return highlight;
        }

        public static void Stretch(RectTransform rect, float horizontalPadding = 0f, float verticalPadding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
        }

        public static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Sprite CreateRoundedSprite(int size, int radius, int border)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "BCIKeyboardXR_RoundedRect";
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(r - x, 0f, x - (size - 1 - r));
                    float dy = Mathf.Max(r - y, 0f, y - (size - 1 - r));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    byte alpha = distance <= r ? (byte)255 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        private static Texture2D CreateRadialTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "BCIKeyboardXR_RadialBlue";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.52f);
            float maxDistance = size * 0.72f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float t = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), center) / maxDistance);
                    Color color = Color.Lerp(BackgroundCenter, BackgroundEdge, t);
                    color = Color.Lerp(color, BackgroundBase, 0.18f);
                    float topShade = Mathf.Clamp01(1f - (float)y / (size * 0.48f)) * 0.05f;
                    color = Color.Lerp(color, Hex(0xDC, 0xE2, 0xEA), topShade);
                    float noise = (((x * 73 + y * 151) & 255) / 255f - 0.5f) * 0.01f;
                    color.r = Mathf.Clamp01(color.r + noise);
                    color.g = Mathf.Clamp01(color.g + noise);
                    color.b = Mathf.Clamp01(color.b + noise);
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Sprite CreateTopHighlightSprite(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "BCIKeyboardXR_TopHighlight";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float normalizedFromTop = 1f - (float)y / (height - 1);
                float alpha = normalizedFromTop <= 0.40f ? 0f : Mathf.InverseLerp(0.40f, 1f, normalizedFromTop) * 0.25f;
                var color = new Color(1f, 1f, 1f, alpha);
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

    }
}
