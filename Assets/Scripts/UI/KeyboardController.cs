using System;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class KeyboardController : MonoBehaviour
    {
        [SerializeField] private KeyTile keyTilePrefab;
        [SerializeField] private KeyTile actionKeyPrefab;
        [SerializeField] private int builtLayoutVersion;

        public event Action<string> OnKeySelected;

        private const float KeyboardWidth = 1760f;
        private const float KeyHeight = 86f;
        private const float RowGap = 15f;
        private const float KeyGap = 10f;
        private const float BackspaceWidth = 240f;
        private const float LetterBackspaceGap = 20f;
        private const float ActionHeight = 94f;
        private const int CurrentLayoutVersion = 5;

        private bool _built;
        private bool _subscribed;
        private float _nextFrequency = 11.0f;

        private void Awake()
        {
            BuildKeyboard();
        }

        private void OnEnable()
        {
            if (!_built || builtLayoutVersion != CurrentLayoutVersion)
            {
                _built = false;
                BuildKeyboard();
            }

            SubscribeKeys();
        }

        private void OnDisable()
        {
            UnsubscribeKeys();
        }

        public void BuildKeyboard()
        {
            if (_built)
                return;

            _built = true;
            builtLayoutVersion = CurrentLayoutVersion;
            ClearExistingChildren();
            ClearLayoutGroups();

            float lettersWidth = KeyboardWidth - BackspaceWidth - LetterBackspaceGap;
            float regularKeyWidth = (lettersWidth - (10 - 1) * KeyGap) / 10f;
            float y = 0f;

            AddLetterRow("Row1", "QWERTYUIOP", 0f, y, regularKeyWidth);
            y += KeyHeight + RowGap;
            AddLetterRow("Row2", "ASDFGHJKL", regularKeyWidth * 0.5f, y, regularKeyWidth);
            y += KeyHeight + RowGap;
            AddLetterRow("Row3", "ZXCVBNM", regularKeyWidth * 1.5f, y, regularKeyWidth);

            AddAbsoluteKey("Backspace", "BACK", "BACKSPACE", KeyVariant.Action, lettersWidth + LetterBackspaceGap, 0f, BackspaceWidth, KeyHeight * 3f + RowGap * 2f);

            y += KeyHeight + RowGap + 4f;
            AddPunctuationRow(y, regularKeyWidth);

            y += KeyHeight + RowGap + 4f;
            AddActionRow(y);
        }

        private void AddLetterRow(string rowName, string letters, float startX, float y, float keyWidth)
        {
            RectTransform row = CreateAbsoluteContainer(rowName, 0f, y, KeyboardWidth, KeyHeight);

            for (int i = 0; i < letters.Length; i++)
            {
                char key = letters[i];
                KeyTile tile = CreateKey(row, KeyVariant.Standard);
                tile.name = key.ToString().ToUpperInvariant();
                tile.SetKey(key);
                tile.SetFlickerHz(NextHz());
                SetAbsoluteRect(tile.GetComponent<RectTransform>(), startX + i * (keyWidth + KeyGap), 0f, keyWidth, KeyHeight);
            }
        }

        private void AddPunctuationRow(float y, float keyWidth)
        {
            string[] punctuation = { ".", ",", "?", "'", "!", "&" };
            float rowWidth = punctuation.Length * keyWidth + (punctuation.Length - 1) * KeyGap;
            float startX = (KeyboardWidth - rowWidth) * 0.5f;
            RectTransform row = CreateAbsoluteContainer("PunctuationRow", 0f, y, KeyboardWidth, KeyHeight);

            for (int i = 0; i < punctuation.Length; i++)
            {
                KeyTile tile = CreateKey(row, KeyVariant.Standard);
                tile.name = "Punctuation_" + punctuation[i];
                tile.SetLabel(punctuation[i], punctuation[i], KeyVariant.Standard);
                tile.SetFlickerHz(NextHz());
                SetAbsoluteRect(tile.GetComponent<RectTransform>(), startX + i * (keyWidth + KeyGap), 0f, keyWidth, KeyHeight);
            }
        }

        private void AddActionRow(float y)
        {
            float enterWidth = 180f;
            float gap = 18f;
            float spaceWidth = KeyboardWidth * 0.60f;
            float totalWidth = spaceWidth + gap + enterWidth;
            float startX = (KeyboardWidth - totalWidth) * 0.5f;
            RectTransform row = CreateAbsoluteContainer("ActionRow", 0f, y, KeyboardWidth, ActionHeight);

            AddAbsoluteKey(row, "Space", "SPACE", "SPACE", KeyVariant.Action, startX, 0f, spaceWidth, ActionHeight);
            AddAbsoluteKey(row, "Enter", "ENTER", "ENTER", KeyVariant.Action, startX + spaceWidth + gap, 0f, enterWidth, ActionHeight);
        }

        private void AddAbsoluteKey(string name, string label, string output, KeyVariant variant, float x, float y, float width, float height)
        {
            AddAbsoluteKey(transform, name, label, output, variant, x, y, width, height);
        }

        private void AddAbsoluteKey(Transform parent, string name, string label, string output, KeyVariant variant, float x, float y, float width, float height)
        {
            KeyTile tile = CreateKey(parent, variant);
            tile.name = name;
            tile.SetLabel(label, output, variant);
            tile.SetFlickerHz(NextHz());
            SetAbsoluteRect(tile.GetComponent<RectTransform>(), x, y, width, height);
        }

        private RectTransform CreateAbsoluteContainer(string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            SetAbsoluteRect(rect, x, y, width, height);
            return rect;
        }

        private KeyTile CreateKey(Transform parent, KeyVariant variant)
        {
            KeyTile source = variant == KeyVariant.Action ? actionKeyPrefab : keyTilePrefab;
            KeyTile tile = source != null
                ? Instantiate(source, parent)
                : CreateRuntimeKey(parent);

            tile.OnKeySelected -= HandleKeySelected;
            tile.OnKeySelected += HandleKeySelected;
            return tile;
        }

        private static KeyTile CreateRuntimeKey(Transform parent)
        {
            var go = new GameObject("KeyTile", typeof(RectTransform), typeof(LayoutElement), typeof(KeyTile));
            go.transform.SetParent(parent, false);
            return go.GetComponent<KeyTile>();
        }

        private static void SetAbsoluteRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);

            if (rect.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredWidth = width;
                layoutElement.preferredHeight = height;
            }
        }

        private void ClearLayoutGroups()
        {
            foreach (var layoutGroup in GetComponents<LayoutGroup>())
                DestroyComponent(layoutGroup);
        }

        private void ClearExistingChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyUnityObject(transform.GetChild(i).gameObject);
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null)
                return;

            if (Application.isPlaying)
                Destroy(component);
            else
                DestroyImmediate(component);
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private float NextHz()
        {
            float hz = _nextFrequency;
            _nextFrequency += 0.1f;
            return hz;
        }

        private void SubscribeKeys()
        {
            if (_subscribed)
                return;

            foreach (var tile in GetComponentsInChildren<KeyTile>(true))
            {
                tile.OnKeySelected -= HandleKeySelected;
                tile.OnKeySelected += HandleKeySelected;
            }

            _subscribed = true;
        }

        private void UnsubscribeKeys()
        {
            if (!_subscribed)
                return;

            foreach (var tile in GetComponentsInChildren<KeyTile>(true))
                tile.OnKeySelected -= HandleKeySelected;

            _subscribed = false;
        }

        private void HandleKeySelected(string key)
        {
            OnKeySelected?.Invoke(key);
        }
    }
}
