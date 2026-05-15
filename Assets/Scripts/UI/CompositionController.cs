using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;
using BCIKeyboardXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class CompositionController : MonoBehaviour
    {
        private enum CommitType
        {
            Character,
            Word,
            Phrase,
            Space
        }

        private struct CommitRecord
        {
            public CommitType Type;
            public int Length;
        }

        [SerializeField] private Image shadowImage;
        [SerializeField] private Image contactShadowImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image tintImage;
        [SerializeField] private Image highlightImage;
        [SerializeField] private Image edgeImage;
        [SerializeField] private TextMeshProUGUI textLabel;
        [SerializeField] private string committedText = string.Empty;
        [SerializeField] private string ghostText = string.Empty;

        private readonly Stack<CommitRecord> _commitStack = new Stack<CommitRecord>();
        private readonly StringBuilder _builder = new StringBuilder(256);
        private Coroutine _pulseRoutine;
        private Coroutine _fadeRoutine;
        private Coroutine _stretchRoutine;
        private Coroutine _resetFadeRoutine;
        private float _textAlpha = 1f;
        private readonly Dictionary<int, Coroutine> _activeCharacterAnimations = new Dictionary<int, Coroutine>();
        private readonly Dictionary<int, float> _characterWaveStartTimes = new Dictionary<int, float>();
        private readonly List<int> _waveScratch = new List<int>();

        public string CommittedText => committedText;
        public string GhostText => ghostText;

        private void Awake()
        {
            EnsureBuilt();
            Render();
        }

        private void OnEnable()
        {
            TtsService.OnCharacterSpoken += HandleCharacterSpoken;
        }

        private void OnDisable()
        {
            TtsService.OnCharacterSpoken -= HandleCharacterSpoken;
            ResetCharacterWaves();
        }

        private void Update()
        {
            Render();
        }

        public void AppendChar(char character)
        {
            ResetCharacterWaves();
            committedText += character;
            _commitStack.Push(new CommitRecord { Type = CommitType.Character, Length = 1 });
            Render();
        }

        public void AppendWord(string word)
        {
            string clean = (word ?? string.Empty).Trim();
            if (clean.Length == 0)
                return;

            ReplaceCurrentPartialWith(clean + " ", CommitType.Word);
        }

        public void AppendPhrase(string phrase)
        {
            string clean = (phrase ?? string.Empty).Trim();
            if (clean.Length == 0)
                return;

            ReplaceCurrentPartialWith(clean + " ", CommitType.Phrase);
        }

        public void AppendSpace()
        {
            if (committedText.Length > 0 && committedText[committedText.Length - 1] == ' ')
                return;

            ResetCharacterWaves();
            committedText += " ";
            _commitStack.Push(new CommitRecord { Type = CommitType.Space, Length = 1 });
            Render();
        }

        public void Backspace()
        {
            if (committedText.Length == 0)
                return;

            int removeLength = 1;
            if (_commitStack.Count > 0)
            {
                CommitRecord record = _commitStack.Pop();
                removeLength = record.Type == CommitType.Word || record.Type == CommitType.Phrase
                    ? record.Length
                    : 1;
            }

            removeLength = Mathf.Clamp(removeLength, 1, committedText.Length);
            ResetCharacterWaves();
            committedText = committedText.Substring(0, committedText.Length - removeLength);
            Render();
        }

        public void Reset()
        {
            ResetCharacterWaves();
            committedText = string.Empty;
            ghostText = string.Empty;
            _commitStack.Clear();
            Render();
        }

        public void SetGhostPreview(string previewText)
        {
            previewText ??= string.Empty;
            ghostText = previewText.StartsWith(committedText)
                ? previewText.Substring(committedText.Length)
                : previewText;
            Render();
        }

        public void ClearGhostPreview()
        {
            ghostText = string.Empty;
            Render();
        }

        public string GetCurrentPartialWord()
        {
            if (string.IsNullOrEmpty(committedText))
                return string.Empty;

            int index = committedText.Length - 1;
            while (index >= 0 && !char.IsWhiteSpace(committedText[index]))
                index--;

            return committedText.Substring(index + 1);
        }

        public void Pulse()
        {
            EnsureBuilt();
            if (_pulseRoutine != null)
                StopCoroutine(_pulseRoutine);

            _pulseRoutine = StartCoroutine(PulseRoutine());
            StretchAbsorb();
        }

        public void SetSpeakingPulseAlpha(float alpha)
        {
            EnsureBuilt();
            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }

            Color color = UiTheme.Glass;
            color.a = alpha;
            backgroundImage.color = color;
        }

        public void RestoreBackground()
        {
            EnsureBuilt();
            backgroundImage.color = UiTheme.Glass;
        }

        public void AnimateResetThen(Action onComplete)
        {
            EnsureBuilt();
            if (_resetFadeRoutine != null)
                StopCoroutine(_resetFadeRoutine);

            _resetFadeRoutine = StartCoroutine(ResetFadeRoutine(onComplete));
        }

        private void ReplaceCurrentPartialWith(string replacement, CommitType type)
        {
            ResetCharacterWaves();
            string partial = GetCurrentPartialWord();
            if (partial.Length > 0 && committedText.EndsWith(partial))
                committedText = committedText.Substring(0, committedText.Length - partial.Length);

            if (committedText.Length > 0 && !char.IsWhiteSpace(committedText[committedText.Length - 1]))
                committedText += " ";

            committedText += replacement;
            _commitStack.Push(new CommitRecord { Type = type, Length = replacement.Length });
            ghostText = string.Empty;
            Render();
            Pulse();
            StartFade();
        }

        private void StartFade()
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            _fadeRoutine = StartCoroutine(FadeRoutine());
        }

        private IEnumerator PulseRoutine()
        {
            yield return LerpBackground(UiTheme.Glass, UiTheme.PulseBlue, 0.15f);
            yield return LerpBackground(UiTheme.PulseBlue, UiTheme.Glass, 0.15f);
            _pulseRoutine = null;
        }

        private void StretchAbsorb()
        {
            if (_stretchRoutine != null)
                StopCoroutine(_stretchRoutine);

            _stretchRoutine = StartCoroutine(StretchRoutine());
        }

        private IEnumerator StretchRoutine()
        {
            RectTransform rect = backgroundImage != null ? backgroundImage.rectTransform : transform as RectTransform;
            if (rect == null)
                yield break;

            Vector3 start = rect.localScale;
            Vector3 wide = new Vector3(1.02f, 1f, 1f);

            float elapsed = 0f;
            while (elapsed < 0.10f)
            {
                elapsed += Time.unscaledDeltaTime;
                rect.localScale = Vector3.LerpUnclamped(start, wide, EaseOutCubic(Mathf.Clamp01(elapsed / 0.10f)));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < 0.10f)
            {
                elapsed += Time.unscaledDeltaTime;
                rect.localScale = Vector3.LerpUnclamped(wide, Vector3.one, EaseOutCubic(Mathf.Clamp01(elapsed / 0.10f)));
                yield return null;
            }

            rect.localScale = Vector3.one;
            _stretchRoutine = null;
        }

        private IEnumerator ResetFadeRoutine(Action onComplete)
        {
            EnsureBuilt();
            RectTransform labelRect = textLabel.rectTransform;
            Vector2 startMin = labelRect.offsetMin;
            Vector2 startMax = labelRect.offsetMax;
            Vector2 down = new Vector2(0f, -10f);

            float elapsed = 0f;
            while (elapsed < 0.25f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutCubic(Mathf.Clamp01(elapsed / 0.25f));
                _textAlpha = 1f - t;
                labelRect.offsetMin = Vector2.Lerp(startMin, startMin + down, t);
                labelRect.offsetMax = Vector2.Lerp(startMax, startMax + down, t);
                Render();
                yield return null;
            }

            onComplete?.Invoke();
            _textAlpha = 1f;
            labelRect.offsetMin = startMin;
            labelRect.offsetMax = startMax;
            Render();
            _resetFadeRoutine = null;
        }

        private IEnumerator LerpBackground(Color from, Color to, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                backgroundImage.color = Color.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }
        }

        private IEnumerator FadeRoutine()
        {
            float elapsed = 0f;
            while (elapsed < 0.25f)
            {
                elapsed += Time.unscaledDeltaTime;
                _textAlpha = Mathf.Clamp01(elapsed / 0.25f);
                Render();
                yield return null;
            }

            _textAlpha = 1f;
            Render();
            _fadeRoutine = null;
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private void Render()
        {
            EnsureBuilt();
            string escapedCommitted = EscapeRichText(committedText);
            string escapedGhost = EscapeRichText(ghostText);
            int alpha = Mathf.RoundToInt(_textAlpha * 255f);

            _builder.Clear();
            _builder.Append("<color=#1A2740");
            _builder.Append(alpha.ToString("X2"));
            _builder.Append(">");
            _builder.Append(escapedCommitted);
            _builder.Append("</color>");
            float cursorAlpha = Mathf.Lerp(0.4f, 1f, (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 1.2f) + 1f) * 0.5f);
            _builder.Append("<color=#4070C0");
            _builder.Append(Mathf.RoundToInt(cursorAlpha * 255f).ToString("X2"));
            _builder.Append("><size=38>|</size></color>");

            if (!string.IsNullOrEmpty(escapedGhost))
            {
                _builder.Append("<i><color=#1A274066>");
                _builder.Append(escapedGhost);
                _builder.Append("</color></i>");
            }

            textLabel.text = _builder.ToString();
            if (_activeCharacterAnimations.Count > 0)
                ApplyCharacterWaves();
        }

        private static string EscapeRichText(string value)
        {
            return (value ?? string.Empty)
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private void EnsureBuilt()
        {
            if (shadowImage == null)
                shadowImage = UiTheme.AddGlassShadow(gameObject);
            shadowImage.sprite = UiTheme.PhraseSprite;

            if (contactShadowImage == null)
                contactShadowImage = UiTheme.AddContactShadow(gameObject);
            contactShadowImage.sprite = UiTheme.PhraseSprite;

            if (backgroundImage == null)
            {
                backgroundImage = UiTheme.AddImage(gameObject, "Background", UiTheme.Glass);
                backgroundImage.color = UiTheme.Glass;
            }
            backgroundImage.sprite = UiTheme.PhraseSprite;

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

            if (textLabel == null)
            {
                textLabel = UiTheme.AddText(gameObject, "CompositionText", 36);
                textLabel.alignment = TextAlignmentOptions.Center;
                textLabel.textWrappingMode = TextWrappingModes.NoWrap;
                textLabel.overflowMode = TextOverflowModes.Ellipsis;
                UiTheme.Stretch(textLabel.rectTransform, 30f, 8f);
            }
            textLabel.fontSize = 36f;
            textLabel.characterSpacing = 0f;
            textLabel.fontStyle = FontStyles.Normal;
            if (UiTheme.RegularFont != null)
                textLabel.font = UiTheme.RegularFont;
        }

        private void HandleCharacterSpoken(int characterIndex)
        {
            if (characterIndex < 0)
            {
                ResetCharacterWaves();
                return;
            }

            if (characterIndex >= committedText.Length)
                return;

            if (_activeCharacterAnimations.TryGetValue(characterIndex, out Coroutine existing) && existing != null)
                StopCoroutine(existing);

            _characterWaveStartTimes[characterIndex] = Time.unscaledTime;
            _activeCharacterAnimations[characterIndex] = StartCoroutine(CharacterWaveRoutine(characterIndex));
            ApplyCharacterWaves();
        }

        private IEnumerator CharacterWaveRoutine(int characterIndex)
        {
            const float duration = 0.4f;
            while (Time.unscaledTime - _characterWaveStartTimes[characterIndex] < duration)
            {
                ApplyCharacterWaves();
                yield return null;
            }

            _activeCharacterAnimations.Remove(characterIndex);
            _characterWaveStartTimes.Remove(characterIndex);
            if (_characterWaveStartTimes.Count == 0)
                ForceBaseMeshUpdate();
            else
                ApplyCharacterWaves();
        }

        private void ResetCharacterWaves()
        {
            foreach (Coroutine routine in _activeCharacterAnimations.Values)
            {
                if (routine != null)
                    StopCoroutine(routine);
            }

            _activeCharacterAnimations.Clear();
            _characterWaveStartTimes.Clear();

            if (textLabel == null)
                return;

            ForceBaseMeshUpdate();
        }

        private void ApplyCharacterWaves()
        {
            if (textLabel == null || _characterWaveStartTimes.Count == 0)
                return;

            textLabel.ForceMeshUpdate();
            TMP_TextInfo textInfo = textLabel.textInfo;

            _waveScratch.Clear();
            foreach (int characterIndex in _characterWaveStartTimes.Keys)
                _waveScratch.Add(characterIndex);

            for (int i = 0; i < _waveScratch.Count; i++)
            {
                int characterIndex = _waveScratch[i];
                if (!_characterWaveStartTimes.TryGetValue(characterIndex, out float startTime))
                    continue;
                if (characterIndex < 0 || characterIndex >= textInfo.characterCount)
                    continue;

                TMP_CharacterInfo characterInfo = textInfo.characterInfo[characterIndex];
                if (!characterInfo.isVisible)
                    continue;

                float elapsed = Mathf.Clamp01((Time.unscaledTime - startTime) / 0.4f);
                float phase = elapsed <= 0.5f
                    ? EaseOutBack(elapsed / 0.5f, 0.5f)
                    : 1f - EaseInOutQuart((elapsed - 0.5f) / 0.5f);

                float yOffset = Mathf.Lerp(0f, 6f, phase);
                float scale = Mathf.Lerp(1f, 1.18f, phase);
                float brighten = Mathf.Lerp(0f, 0.35f, phase);

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;
                Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
                Color32[] colors = textInfo.meshInfo[materialIndex].colors32;

                Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 1] + vertices[vertexIndex + 2] + vertices[vertexIndex + 3]) * 0.25f;
                for (int v = 0; v < 4; v++)
                {
                    int index = vertexIndex + v;
                    Vector3 direction = vertices[index] - center;
                    vertices[index] = center + direction * scale + Vector3.up * yOffset;
                    colors[index] = Color32.Lerp(colors[index], Color.white, brighten);
                }
            }

            textLabel.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }

        private void ForceBaseMeshUpdate()
        {
            textLabel.ForceMeshUpdate();
            textLabel.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }

        private static float EaseOutBack(float t, float overshoot)
        {
            t -= 1f;
            return 1f + (overshoot + 1f) * t * t * t + overshoot * t * t;
        }

        private static float EaseInOutQuart(float t)
        {
            return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) * 0.5f;
        }
    }
}
