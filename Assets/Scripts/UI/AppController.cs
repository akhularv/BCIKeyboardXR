using System.Collections.Generic;
using BCIKeyboardXR.Core;
using BCIKeyboardXR.LLM;
using UnityEngine.InputSystem.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class AppController : MonoBehaviour
    {
        [SerializeField] private PredictionService predictionService;
        [SerializeField] private PhraseRowController phraseRowController;
        [SerializeField] private WordRowController wordRowController;
        [SerializeField] private KeyboardController keyboardController;
        [SerializeField] private CompositionController compositionController;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button speakButton;
        [SerializeField] private RawImage backgroundImage;

        private Coroutine _backgroundBreathRoutine;
        private Coroutine _speakPunchRoutine;
        private Coroutine _speakingPulseRoutine;
        private Text _speakButtonLegacyText;
        private TMPro.TextMeshProUGUI _speakButtonText;
        private Image _speakButtonImage;
        private int _wordRequestVersion;
        private int _phraseRequestVersion;
        private string _lastCommittedAutocompleteWord = string.Empty;

        private void Awake()
        {
            EnsureSceneBuilt();
            predictionService = PredictionService.Instance ?? predictionService;
        }

        private void Start()
        {
            predictionService = PredictionService.Instance ?? predictionService;
            StartBackgroundBreathing();
            RequestPhrasePredictions();
        }

        private void OnEnable()
        {
            WireEvents();
        }

        private void OnDisable()
        {
            UnwireEvents();
        }

        private void OnDestroy()
        {
            if (_backgroundBreathRoutine != null)
                StopCoroutine(_backgroundBreathRoutine);
            if (_speakPunchRoutine != null)
                StopCoroutine(_speakPunchRoutine);
            if (_speakingPulseRoutine != null)
                StopCoroutine(_speakingPulseRoutine);
            TtsService.Stop();
        }

        private void OnApplicationQuit()
        {
            TtsService.Stop();
        }

        private void WireEvents()
        {
            EnsureSceneBuilt();

            keyboardController.OnKeySelected += HandleKeySelected;
            wordRowController.OnWordSelected += HandleWordSelected;
            wordRowController.OnHoverPreview += HandleWordHoverPreview;
            wordRowController.OnHoverExit += compositionController.ClearGhostPreview;
            phraseRowController.OnPhraseSelected += HandlePhraseSelected;
            phraseRowController.OnHoverPreview += HandlePhraseHoverPreview;
            phraseRowController.OnHoverExit += compositionController.ClearGhostPreview;
            resetButton.onClick.AddListener(HandleReset);
            speakButton.onClick.AddListener(HandleSpeak);
        }

        private void UnwireEvents()
        {
            if (keyboardController != null)
                keyboardController.OnKeySelected -= HandleKeySelected;
            if (wordRowController != null)
            {
                wordRowController.OnWordSelected -= HandleWordSelected;
                wordRowController.OnHoverPreview -= HandleWordHoverPreview;
                wordRowController.OnHoverExit -= compositionController.ClearGhostPreview;
            }
            if (phraseRowController != null)
            {
                phraseRowController.OnPhraseSelected -= HandlePhraseSelected;
                phraseRowController.OnHoverPreview -= HandlePhraseHoverPreview;
                phraseRowController.OnHoverExit -= compositionController.ClearGhostPreview;
            }
            if (resetButton != null)
                resetButton.onClick.RemoveListener(HandleReset);
            if (speakButton != null)
                speakButton.onClick.RemoveListener(HandleSpeak);
        }

        private void HandleKeySelected(string key)
        {
            switch (key)
            {
                case "SPACE":
                    compositionController.AppendSpace();
                    wordRowController.Clear();
                    RequestWordPredictions();
                    RequestPhrasePredictions();
                    break;
                case "BACKSPACE":
                    compositionController.Backspace();
                    _lastCommittedAutocompleteWord = null;
                    RequestWordPredictions();
                    break;
                case "ENTER":
                    EnsureSentenceEndingPunctuation();
                    HandleSpeak();
                    break;
                default:
                    if (!string.IsNullOrEmpty(key))
                    {
                        compositionController.AppendChar(key[0]);
                        RequestWordPredictions();
                    }
                    break;
            }
        }

        private void HandleWordSelected(string word)
        {
            string candidate = ToWordCandidate(
                StripPartialWord(compositionController.CommittedText),
                compositionController.GetCurrentPartialWord(),
                word);
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            compositionController.AppendWord(candidate);
            _lastCommittedAutocompleteWord = candidate;
            _wordRequestVersion++;
            wordRowController.Clear();
            compositionController.ClearGhostPreview();
            RequestWordPredictions();
            RequestPhrasePredictions();
        }

        private void HandlePhraseSelected(string phrase)
        {
            string continuation = ToCoherentPhraseContinuation(compositionController.CommittedText, phrase);
            if (string.IsNullOrWhiteSpace(continuation))
                return;

            compositionController.AppendPhrase(continuation);
            _wordRequestVersion++;
            wordRowController.Clear();
            compositionController.ClearGhostPreview();
            RequestPhrasePredictions();
        }

        private void HandleWordHoverPreview(string word)
        {
            string preview = GhostPreviewHelper.PreviewWithWord(
                compositionController.CommittedText,
                compositionController.GetCurrentPartialWord(),
                word);
            compositionController.SetGhostPreview(preview);
        }

        private void HandlePhraseHoverPreview(string phrase)
        {
            string continuation = ToCoherentPhraseContinuation(compositionController.CommittedText, phrase);
            string preview = GhostPreviewHelper.PreviewWithPhrase(
                compositionController.CommittedText,
                compositionController.GetCurrentPartialWord(),
                continuation);
            compositionController.SetGhostPreview(preview);
        }

        private void HandleReset()
        {
            compositionController.AnimateResetThen(() =>
            {
                compositionController.Reset();
                _wordRequestVersion++;
                _lastCommittedAutocompleteWord = null;
                wordRowController.Clear();
                phraseRowController.Clear();
                RequestPhrasePredictions();
            });
        }

        private void HandleSpeak()
        {
            if (TtsService.IsSpeaking)
            {
                TtsService.Stop();
                StopSpeakingVisuals();
                return;
            }

            string text = compositionController.CommittedText.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            TtsService.Speak(text);

            if (_speakPunchRoutine != null)
                StopCoroutine(_speakPunchRoutine);
            _speakPunchRoutine = StartCoroutine(ButtonPunchRoutine(speakButton != null ? speakButton.transform : null));
            compositionController.Pulse();

            if (TtsService.IsSpeaking)
                StartSpeakingVisuals();
        }

        private void StartBackgroundBreathing()
        {
            if (backgroundImage == null)
            {
                Canvas canvas = FindAnyObjectByType<Canvas>();
                Transform background = canvas != null ? canvas.transform.Find("Background") : null;
                if (background != null)
                    backgroundImage = background.GetComponent<RawImage>();
            }

            if (backgroundImage == null || _backgroundBreathRoutine != null)
                return;

            _backgroundBreathRoutine = StartCoroutine(BackgroundBreathRoutine());
        }

        private IEnumerator BackgroundBreathRoutine()
        {
            Color baseColor = Color.white;
            while (backgroundImage != null)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 8f) + 1f) * 0.5f;
                float value = Mathf.Lerp(0.97f, 1.03f, pulse);
                backgroundImage.color = baseColor * value;
                backgroundImage.color = new Color(backgroundImage.color.r, backgroundImage.color.g, backgroundImage.color.b, 1f);
                yield return null;
            }

            _backgroundBreathRoutine = null;
        }

        private IEnumerator ButtonPunchRoutine(Transform target)
        {
            if (target == null)
                yield break;

            Vector3 start = target.localScale;
            Vector3 pressed = Vector3.one * 0.95f;
            float elapsed = 0f;
            while (elapsed < 0.08f)
            {
                elapsed += Time.unscaledDeltaTime;
                target.localScale = Vector3.LerpUnclamped(start, pressed, EaseOutCubic(Mathf.Clamp01(elapsed / 0.08f)));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < 0.12f)
            {
                elapsed += Time.unscaledDeltaTime;
                target.localScale = Vector3.LerpUnclamped(pressed, Vector3.one, EaseOutCubic(Mathf.Clamp01(elapsed / 0.12f)));
                yield return null;
            }

            target.localScale = Vector3.one;
            _speakPunchRoutine = null;
        }

        private void StartSpeakingVisuals()
        {
            CacheSpeakButtonVisuals();
            SetSpeakButtonState(true);

            if (_speakingPulseRoutine != null)
                StopCoroutine(_speakingPulseRoutine);

            _speakingPulseRoutine = StartCoroutine(SpeakingPulseRoutine());
        }

        private void StopSpeakingVisuals()
        {
            if (_speakingPulseRoutine != null)
            {
                StopCoroutine(_speakingPulseRoutine);
                _speakingPulseRoutine = null;
            }

            compositionController.RestoreBackground();
            SetSpeakButtonState(false);
        }

        private IEnumerator SpeakingPulseRoutine()
        {
            var wait = new WaitForSecondsRealtime(0.1f);
            while (TtsService.IsSpeaking)
            {
                float t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 0.8f) + 1f) * 0.5f;
                compositionController.SetSpeakingPulseAlpha(Mathf.Lerp(0.6f, 0.9f, t));
                yield return wait;
            }

            StopSpeakingVisuals();
        }

        private void CacheSpeakButtonVisuals()
        {
            if (speakButton == null)
                return;

            if (_speakButtonImage == null)
                _speakButtonImage = speakButton.GetComponent<Image>();
            if (_speakButtonText == null)
                _speakButtonText = speakButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (_speakButtonLegacyText == null)
                _speakButtonLegacyText = speakButton.GetComponentInChildren<Text>(true);
        }

        private void SetSpeakButtonState(bool speaking)
        {
            CacheSpeakButtonVisuals();

            string label = speaking ? "STOP" : "SPEAK";
            if (_speakButtonText != null)
                _speakButtonText.text = label;
            if (_speakButtonLegacyText != null)
                _speakButtonLegacyText.text = label;

            if (_speakButtonImage != null)
                _speakButtonImage.color = speaking ? UiTheme.WarmGlassHover : UiTheme.Glass;
        }

        private void EnsureSentenceEndingPunctuation()
        {
            string text = compositionController.CommittedText.TrimEnd();
            if (text.Length == 0 || EndsWithSentencePunctuation(text))
                return;

            compositionController.AppendChar('.');
        }

        private static bool EndsWithSentencePunctuation(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            char c = text[text.Length - 1];
            return c == '.' || c == '?' || c == '!';
        }

        private void RequestWordPredictions()
        {
            if (predictionService == null)
                predictionService = PredictionService.Instance;

            if (predictionService == null)
            {
                Debug.LogWarning("[AppController] PredictionService missing; word predictions unavailable.");
                return;
            }

            SplitForWordPrediction(compositionController.CommittedText, out string committedBeforePartial, out string partial);
            if (string.IsNullOrWhiteSpace(partial) && !HasCompletedWordContext(compositionController.CommittedText))
            {
                wordRowController.Clear();
                return;
            }

            int requestVersion = ++_wordRequestVersion;
            wordRowController.Clear();
            predictionService.RequestWordPrediction(
                committedBeforePartial,
                partial,
                result =>
                {
                    if (requestVersion != _wordRequestVersion)
                        return;

                    if (result != null && result.Success)
                        wordRowController.UpdateWords(ToWordCandidates(
                            committedBeforePartial,
                            partial,
                            result.Completions,
                            _lastCommittedAutocompleteWord));
                    else
                        wordRowController.Clear();
                });
        }

        private void RequestPhrasePredictions()
        {
            if (predictionService == null)
                predictionService = PredictionService.Instance;

            if (predictionService == null)
            {
                Debug.LogWarning("[AppController] PredictionService missing; phrase predictions unavailable.");
                return;
            }

            string completedContext = StripPartialWord(compositionController.CommittedText);
            int requestVersion = ++_phraseRequestVersion;
            predictionService.RequestPhrasePrediction(
                completedContext,
                result =>
                {
                    if (requestVersion != _phraseRequestVersion)
                        return;

                    if (result != null && result.Success)
                        phraseRowController.UpdatePhrases(result.Phrases);
                    else
                        phraseRowController.Clear();
                });
        }

        private static string StripPartialWord(string text)
        {
            text ??= string.Empty;
            if (text.Length == 0)
                return string.Empty;

            if (char.IsWhiteSpace(text[text.Length - 1]))
                return text;

            int lastSpace = text.LastIndexOf(' ');
            if (lastSpace < 0)
                return string.Empty;

            return text.Substring(0, lastSpace + 1);
        }

        private static void SplitForWordPrediction(string composition, out string committedBeforePartial, out string partial)
        {
            composition ??= string.Empty;
            partial = string.Empty;

            if (composition.Length == 0)
            {
                committedBeforePartial = string.Empty;
                return;
            }

            if (char.IsWhiteSpace(composition[composition.Length - 1]))
            {
                committedBeforePartial = composition;
                return;
            }

            int lastSpace = composition.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                committedBeforePartial = string.Empty;
                partial = composition;
                return;
            }

            committedBeforePartial = composition.Substring(0, lastSpace + 1);
            partial = composition.Substring(lastSpace + 1);
        }

        private static string ToCoherentPhraseContinuation(string committedText, string phrase)
        {
            string completedContext = StripPartialWord(committedText);
            string continuation = ToPhraseContinuation(completedContext, phrase);
            continuation = CollapseRepeatedBoundaryWords(completedContext, continuation);
            return continuation.Trim();
        }

        private static bool HasCompletedWordContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return char.IsWhiteSpace(text[text.Length - 1]);
        }

        private static string ToPhraseContinuation(string completedContext, string phrase)
        {
            completedContext ??= string.Empty;
            phrase = (phrase ?? string.Empty).Trim();

            if (phrase.Length == 0 || completedContext.Length == 0)
                return phrase;

            string trimmedContext = completedContext.Trim();
            if (trimmedContext.Length == 0)
                return phrase;

            string normalizedContext = NormalizeForComparison(trimmedContext);
            string normalizedPhrase = NormalizeForComparison(phrase);
            if (normalizedContext.Length > 0 &&
                (normalizedPhrase == normalizedContext || normalizedPhrase.StartsWith(normalizedContext + " ")))
            {
                return RemoveLeadingWords(phrase, CountNormalizedWords(normalizedContext)).TrimStart();
            }

            if (phrase.Length >= trimmedContext.Length &&
                string.Compare(phrase, 0, trimmedContext, 0, trimmedContext.Length, true) == 0)
            {
                return phrase.Substring(trimmedContext.Length).TrimStart();
            }

            return phrase;
        }

        private static string CollapseRepeatedBoundaryWords(string completedContext, string phrase)
        {
            phrase = (phrase ?? string.Empty).Trim();
            if (phrase.Length == 0)
                return string.Empty;

            string lastContextWord = LastWord(completedContext);
            string firstPhraseWord = FirstToken(phrase);
            if (lastContextWord.Length > 0 && NormalizedWordsEqual(lastContextWord, firstPhraseWord))
                return phrase.Substring(firstPhraseWord.Length).TrimStart();

            return phrase;
        }

        private static string LastWord(string value)
        {
            value = (value ?? string.Empty).Trim();
            int lastSpace = value.LastIndexOf(' ');
            return lastSpace < 0 ? value : value.Substring(lastSpace + 1);
        }

        private static bool NormalizedWordsEqual(string left, string right)
        {
            return NormalizeForComparison(left) == NormalizeForComparison(right);
        }

        private static string NormalizeForComparison(string value)
        {
            value = (value ?? string.Empty).ToLowerInvariant();
            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                    chars.Add(c);
                else if (char.IsWhiteSpace(c) && chars.Count > 0 && chars[chars.Count - 1] != ' ')
                    chars.Add(' ');
            }

            return new string(chars.ToArray()).Trim();
        }

        private static List<string> ToWordCandidates(string completedContext, string partialWord, List<string> rawCandidates, string excludedWord)
        {
            var cleaned = new List<string>(6);
            var seen = new HashSet<string>();

            if (rawCandidates == null)
                return cleaned;

            for (int i = 0; i < rawCandidates.Count && cleaned.Count < 6; i++)
            {
                string candidate = ToWordCandidate(completedContext, partialWord, rawCandidates[i]);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (ShouldSuppressCommittedAutocompleteCandidate(completedContext, partialWord, candidate, excludedWord))
                    continue;

                string key = candidate.ToLowerInvariant();
                if (seen.Add(key))
                    cleaned.Add(candidate);
            }

            return cleaned;
        }

        private static string ToWordCandidate(string completedContext, string partialWord, string rawCandidate)
        {
            completedContext ??= string.Empty;
            partialWord ??= string.Empty;
            string candidate = (rawCandidate ?? string.Empty).Trim();
            if (candidate.Length == 0)
                return string.Empty;

            string trimmedContext = completedContext.Trim();
            if (trimmedContext.Length > 0 &&
                candidate.Length >= trimmedContext.Length &&
                string.Compare(candidate, 0, trimmedContext, 0, trimmedContext.Length, true) == 0)
            {
                candidate = candidate.Substring(trimmedContext.Length).TrimStart();
            }

            return FirstToken(candidate);
        }

        private static bool ShouldSuppressCommittedAutocompleteCandidate(string completedContext, string partialWord, string candidate, string excludedWord)
        {
            if (!string.IsNullOrWhiteSpace(partialWord))
                return false;

            string normalizedCandidate = NormalizeForComparison(candidate);
            if (normalizedCandidate.Length == 0)
                return true;

            if (!string.IsNullOrWhiteSpace(excludedWord) &&
                normalizedCandidate == NormalizeForComparison(excludedWord))
                return true;

            string lastCompletedWord = LastWord(completedContext);
            return lastCompletedWord.Length > 0 && normalizedCandidate == NormalizeForComparison(lastCompletedWord);
        }

        private static string FirstToken(string value)
        {
            value = (value ?? string.Empty).Trim();
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                    return value.Substring(0, i);
            }

            return value;
        }

        private static int CountNormalizedWords(string value)
        {
            value = NormalizeForComparison(value);
            if (value.Length == 0)
                return 0;

            return value.Split(' ').Length;
        }

        private static string RemoveLeadingWords(string value, int count)
        {
            value = (value ?? string.Empty).TrimStart();
            int index = 0;
            int removed = 0;

            while (index < value.Length && removed < count)
            {
                while (index < value.Length && !char.IsLetterOrDigit(value[index]))
                    index++;
                while (index < value.Length && char.IsLetterOrDigit(value[index]))
                    index++;
                removed++;
            }

            return index >= value.Length ? string.Empty : value.Substring(index).TrimStart();
        }

        private void EnsureSceneBuilt()
        {
            if (compositionController != null)
                return;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                eventSystem.transform.SetParent(transform, false);
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
                canvas = CreateCanvas();

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            CreateBackground(canvasRect);
            CreateTopBar(canvasRect);
            phraseRowController = CreatePhraseGrid(canvasRect);
            wordRowController = CreateWordRow(canvasRect);
            compositionController = CreateCompositionBar(canvasRect);
            keyboardController = CreateKeyboard(canvasRect);
            phraseRowController.FlyTarget = compositionController.GetComponent<RectTransform>();
            wordRowController.FlyTarget = compositionController.GetComponent<RectTransform>();
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreateBackground(RectTransform parent)
        {
            if (parent.Find("Background") != null)
                return;

            var go = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.texture = UiTheme.RadialBackgroundTexture;
            image.color = Color.white;
            image.raycastTarget = false;
            UiTheme.Stretch(go.GetComponent<RectTransform>());
            go.transform.SetAsFirstSibling();
        }

        private void CreateTopBar(RectTransform parent)
        {
            var topBar = CreatePanel(parent, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), Vector2.zero);
            resetButton = CreateButton(topBar, "ResetButton", "Reset", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(28f, 10f), new Vector2(168f, -10f));
            speakButton = CreateButton(topBar, "SpeakButton", "Speak", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-168f, 10f), new Vector2(-28f, -10f));
        }

        private PhraseRowController CreatePhraseGrid(RectTransform parent)
        {
            var panel = CreatePanel(parent, "PhraseGrid", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -320f), new Vector2(0f, -60f));
            var grid = panel.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(850f, 98f);
            grid.spacing = new Vector2(60f, 26f);
            grid.padding = new RectOffset(80, 80, 22, 22);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            return panel.gameObject.AddComponent<PhraseRowController>();
        }

        private WordRowController CreateWordRow(RectTransform parent)
        {
            var panel = CreatePanel(parent, "WordRow", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -420f), new Vector2(0f, -320f));
            var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 18f;
            layout.padding = new RectOffset(80, 80, 18, 18);
            return panel.gameObject.AddComponent<WordRowController>();
        }

        private CompositionController CreateCompositionBar(RectTransform parent)
        {
            var panel = CreatePanel(parent, "CompositionBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(80f, -500f), new Vector2(-80f, -420f));
            return panel.gameObject.AddComponent<CompositionController>();
        }

        private KeyboardController CreateKeyboard(RectTransform parent)
        {
            var panel = CreatePanel(parent, "KeyboardPanel", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(80f, 6f), new Vector2(-80f, 548f));
            return panel.gameObject.AddComponent<KeyboardController>();
        }

        private static RectTransform CreatePanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing.GetComponent<RectTransform>();

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            UiTheme.SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            return rect;
        }

        private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            UiTheme.SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);

            var image = go.GetComponent<Image>();
            image.sprite = UiTheme.SmallSprite;
            image.type = Image.Type.Sliced;
            image.color = UiTheme.Glass;

            var text = UiTheme.AddText(go, "Label", 14);
            text.text = (label ?? string.Empty).ToUpperInvariant();
            text.fontStyle = TMPro.FontStyles.Bold;
            text.characterSpacing = 1.0f;
            if (UiTheme.SemiboldFont != null)
                text.font = UiTheme.SemiboldFont;

            return go.GetComponent<Button>();
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }
    }
}
