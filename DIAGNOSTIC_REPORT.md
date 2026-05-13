# BCIKeyboardXR Diagnostic Report

Date: 2026-05-13  
Scope: Read-only wiring/prompt/state/lifecycle audit. No code changes were made in this pass.

## Section 1: Event Wiring

### KeyTile.OnKeySelected -> AppController.HandleKeyPress

⚠️ Concern

- The actual destination method is `AppController.HandleKeySelected`, not `HandleKeyPress`.
- `KeyTile` subscribes its `DwellSelectable` events in `OnEnable` and unsubscribes in `OnDisable`.
- `KeyboardController` forwards each `KeyTile.OnKeySelected` to `KeyboardController.HandleKeySelected`, then raises `KeyboardController.OnKeySelected`.
- `AppController.WireEvents` subscribes `keyboardController.OnKeySelected += HandleKeySelected`; `UnwireEvents` removes the same delegate.
- `KeyboardController.CreateKey` and `SubscribeKeys` both use `tile.OnKeySelected -= HandleKeySelected` before `+=`, which prevents duplicate key-forwarder subscriptions.
- No try/catch swallows errors in this chain.

Concern: `AppController.WireEvents` has no `_wired` guard. Normal Unity `OnEnable`/`OnDisable` sequencing is fine, but a manual/internal second `WireEvents` call without `UnwireEvents` would double-subscribe all app-level handlers.

### WordTile.OnWordSelected -> WordRowController.OnWordSelected -> AppController.HandleWordSelected

✅ Correct

- `WordTile` adds `HandleSelected` to `dwellSelectable.onSelected` in `OnEnable` and removes it in `OnDisable`.
- `WordRowController.Subscribe` subscribes each tile to `HandleWordSelected`; `Unsubscribe` removes the same delegates.
- `AppController.WireEvents` subscribes `wordRowController.OnWordSelected += HandleWordSelected`; `UnwireEvents` removes it.
- No try/catch swallows errors.

⚠️ Concern

- `WordRowController.Subscribe` has no `_subscribed` guard. Under ordinary Unity lifecycle this is safe because `OnDisable` unsubscribes, but it is not idempotent if called manually.
- On dwell completion, `DwellSelectable` invokes `onSelected`, then `onHoverExit`, then resets. `WordTile.HandleSelected` also calls `ResetVisualState`, which invokes `OnHoverExit`. This can clear ghost preview twice. Harmless behaviorally, but noisy and worth cleaning later.

### PhraseTile.OnPhraseSelected -> PhraseRowController.OnPhraseSelected -> AppController.HandlePhraseSelected

✅ Correct

- `PhraseTile` subscribes to dwell events in `OnEnable` and unsubscribes in `OnDisable`.
- `PhraseRowController.Subscribe` forwards tile events; `Unsubscribe` removes the same delegates.
- `AppController.WireEvents` subscribes `phraseRowController.OnPhraseSelected += HandlePhraseSelected`; `UnwireEvents` removes it.
- No try/catch swallows errors.

⚠️ Concern

- `PhraseRowController.Subscribe` also lacks an idempotent `_subscribed` guard.
- `PhraseRowController.Clear` hides tiles but does not clear phrase strings or visual dwell state. Hidden tiles are inactive, so runtime behavior is likely fine, but stale labels can remain in inactive objects.

### WordTile Hover Events -> AppController Ghost Preview

✅ Correct

- `WordTile.HandleHoverEnter` raises `OnHoverPreview`.
- `WordRowController.HandleHoverPreview` forwards to `WordRowController.OnHoverPreview`.
- `AppController.HandleWordHoverPreview` computes preview through `GhostPreviewHelper.PreviewWithWord`.
- `wordRowController.OnHoverExit += compositionController.ClearGhostPreview` clears preview.

⚠️ Concern

- Hover exit can fire more than once after selection/reset because both `WordTile.ResetVisualState` and `DwellSelectable` selection completion trigger hover-exit paths.

### PhraseTile Hover Events -> AppController Ghost Preview

✅ Correct

- `PhraseTile.HandleHoverEnter` raises `OnHoverPreview`.
- `PhraseRowController.HandleHoverPreview` forwards to app.
- `AppController.HandlePhraseHoverPreview` normalizes phrase continuation with `ToCoherentPhraseContinuation`, then uses `GhostPreviewHelper.PreviewWithPhrase`.
- `phraseRowController.OnHoverExit += compositionController.ClearGhostPreview` clears preview.

### ResetButton.onClick -> AppController.HandleReset

✅ Correct

- `resetButton.onClick.AddListener(HandleReset)` in `WireEvents`.
- `RemoveListener(HandleReset)` in `UnwireEvents`.
- Reset is delayed through `CompositionController.AnimateResetThen`, then composition and rows are cleared and a fresh phrase prediction is requested.

⚠️ Concern

- `_lastCommittedAutocompleteWord` is not reset in `HandleReset`. It only matters when later requesting word predictions with an empty partial, but stale suppression state can technically survive reset.

### SpeakButton.onClick -> AppController.HandleSpeak

✅ Correct

- `speakButton.onClick.AddListener(HandleSpeak)` in `WireEvents`.
- `RemoveListener(HandleSpeak)` in `UnwireEvents`.
- Speak logs the current composition and plays the button/composition pulse.

### PredictionService callbacks -> AppController -> Row Controllers

✅ Correct

- Word callback path: `PredictionService.RequestWordPrediction` -> debounced async request -> main-thread callback -> `AppController` version guard -> `WordRowController.UpdateWords`.
- Phrase callback path: `PredictionService.RequestPhrasePrediction` -> async request -> main-thread callback -> `PhraseRowController.UpdatePhrases`.
- `PredictionService` posts callbacks through captured `SynchronizationContext`.
- Word requests have both `PredictionService` generation guards and an additional `_wordRequestVersion` guard in `AppController`.

⚠️ Concern

- Phrase callbacks do not have an AppController-level version guard. `PredictionService` cancels prior phrase CTS, so this is probably fine, but a cache-hit callback or cancellation race could still update phrase tiles after a newer app state unless verified in Play mode.

## Section 2: Prediction Prompt Audit

### PredictWords

✅ Correct

- `UserProfile.GetSystemPromptSection()` is injected into the system prompt.
- `currentPartialWord` is passed to the LLM.
- `sentenceSoFar` is passed to the LLM.
- JSON shape is clearly specified.
- `WORD_MAX_TOKENS = 200`, reasonable for six short completions.

⚠️ Concern

- `PredictionService` normalizes `sentenceSoFar` with `.Trim().ToLowerInvariant()`. For active partial words, `AppController` passes the full composition, so typing `h` produces normalized `sentenceSoFar = "h"` and `partialWord = "h"`. That duplicates the partial context in the prompt. The diagnostic prompt expected `sentenceSoFar="" partialWord="h"`.

Exact system prompt structure:

> You are a word-prediction and phrase-suggestion engine embedded in an AAC keyboard. You output ONLY valid JSON — no prose, no markdown, no preamble. Here is the profile of the user you are assisting:
>
> You are an AI assistant integrated into a BCI-driven AAC (augmentative and alternative communication) keyboard for the following user:
>
> # User Profile: Sarah Martinez
>
> ## Identity
> - Name: Sarah Martinez
> - Age: 58
> - Condition: ALS, diagnosed 2023; motor function preserved only in eyes
> - Communication mode: SSVEP-AAC via BCI headset
>
> ## Communication style
> - Direct, prefers short sentences
> - Uses "please" and "thank you" frequently
> - Comfortable with humor
> - Strong preference for being specific over polite hedging
>
> ## Common topics (ranked by frequency)
> 1. Physical needs (water, repositioning, temperature, bathroom)
> 2. Family communication (husband David, daughter Emma, son Marcus)
> 3. Medical (medication timing, pain, breathing)
> 4. Entertainment (music, audiobooks, news)
> 5. Daily activities (window view, sunlight, visitors)
>
> ## High-frequency phrases
> - "I need some water"
> - "Can you adjust my pillow"
> - "Please raise the bed"
> - "I'd like to listen to music"
> - "Call Emma"
> - "I'm in pain"
> - "Turn on the lights"
> - "Open the window"
>
> ## Important people
> - David (husband, primary caregiver)
> - Emma (daughter, lives nearby)
> - Marcus (son, lives out of state)
> - Dr. Chen (neurologist)
> - Linda (home aide, weekdays)
>
> ## Medical context
> - Medication times: 8am, 2pm, 8pm
> - Pain points: shoulders, lower back
> - Breathing: BiPAP at night
>
> Always respond in the voice and context of this user. Prefer short, direct completions. Never include explanatory text outside the JSON structure requested.

Exact user message structure:

> Complete the partial word '{currentPartialWord}' in the context of the sentence so far: '{sentenceSoFar}'. Output ONLY JSON, no preamble, no markdown fences: {"completions": ["word1", "word2", "word3", "word4", "word5", "word6"]}

### PredictPhrases

⚠️ Concern

- `currentPartialWord` is not part of `LLMClient.PredictPhrases`, which is correct.
- `AppController.RequestPhrasePredictions` computes `completedContext = StripPartialWord(compositionController.CommittedText)` before calling `PredictionService.RequestPhrasePrediction`, which is the intended Iteration 2 fix.
- However, `PredictionService` trims the sentence before sending it to `LLMClient`, so the LLM sees `"i need some"` rather than `"i need some "` even when the committed context ends at a space.
- The phrase prompt asks for "4 complete phrases Sarah might want to say next" and does not explicitly say "continue the current sentence fragment." This can encourage full-sentence suggestions such as "Can you get me some water please?" rather than continuations like "water please".
- `PHRASE_MAX_TOKENS = 400`, reasonable for four phrases.

Exact system prompt structure is identical to `PredictWords`.

Exact user message structure:

> Suggest 4 complete phrases that Sarah might want to say next, given: '{sentenceSoFar}'. Output ONLY JSON, no preamble, no markdown fences: {"phrases": ["phrase1", "phrase2", "phrase3", "phrase4"]}

❌ Broken relative to the stated requirement:

- The system/user prompt does not clearly instruct the LLM to continue the sentence. It says "complete phrases" and "might want to say next," which is ambiguous and can start a new sentence.

## Section 3: State Transitions

### (a) User types letter "h"

⚠️ Concern

- `KeyTile` dwell completion fires `OnKeySelected("h")`.
- `KeyboardController` forwards the key.
- `AppController.HandleKeySelected` calls `CompositionController.AppendChar('h')`.
- `AppController.RequestWordPredictions` fires.
- Phrase prediction does not fire.
- Word row updates when the response arrives.
- Phrase row stays as it was.

Concern: the actual word request uses `sentenceSoFar = compositionController.CommittedText`, so after appending `h`, the request carries `sentenceSoFar="h"` and `partialWord="h"`. The requested diagnostic expectation was `sentenceSoFar="" partialWord="h"`.

### (b) User types space

⚠️ Concern

- Space uses the special `"SPACE"` path, not `AppendChar(' ')`.
- `CompositionController.AppendSpace()` appends one space unless already at a space.
- `wordRowController.Clear()` runs immediately.
- `RequestWordPredictions()` runs even with empty partial if the composition ends in whitespace.
- `RequestPhrasePredictions()` runs with `StripPartialWord(committedText)`.

Concern: word prediction after space is intentional based on recent product iteration ("words after one word has been typed"), but it is an extra LLM call with `partialWord=""`. The current LLM prompt says "Complete the partial word ''", which is semantically weak for next-word prediction.

### (c) User dwell-selects a word

⚠️ Concern

- `WordTile` selection fires through `WordRowController` to `AppController.HandleWordSelected`.
- `ToWordCandidate` normalizes the selected candidate.
- `CompositionController.AppendWord(candidate)` replaces current partial word with `candidate + " "`.
- Word row is cleared.
- Ghost preview is cleared.
- Phrase prediction fires with new committed context.
- Word prediction also fires immediately to populate next-word suggestions after the committed word.

Concern: The diagnostic expectation says "Word row cleared until next keystroke." Current behavior deliberately requests next-word suggestions immediately after autocomplete. That matches the user's later preference, but not the text of this diagnostic checklist.

Concern: `_lastCommittedAutocompleteWord` suppresses repeated candidates but is not cleared on normal letter typing. Since suppression is bypassed when `partialWord` is non-empty, this is mostly safe.

### (d) User dwell-selects a phrase

✅ Correct

- `PhraseTile` selection fires through `PhraseRowController` to `AppController.HandlePhraseSelected`.
- App computes continuation using `ToCoherentPhraseContinuation`.
- `CompositionController.AppendPhrase` uses `ReplaceCurrentPartialWith`, which strips the current partial word before appending `phrase + " "`.
- Word row is cleared.
- Ghost preview is cleared.
- Fresh phrase prediction fires.

⚠️ Concern

- If `ToCoherentPhraseContinuation` returns empty, app logs and skips commit. It does not clear the hover/ghost state in that early return path.

### (e) User hits backspace

⚠️ Concern

- `BACKSPACE` calls `CompositionController.Backspace()`.
- Backspace pops `_commitStack`.
- Word and phrase commits remove the full committed unit length; character/space removes one char.
- `RequestWordPredictions()` fires after backspace.

Concern: phrase predictions do not re-fire after backspace. If backspace removes a word/phrase boundary, phrase row may be stale relative to the new composition.

Concern: `CompositionController.Backspace` has no phrase/word record repair if text is modified in ways not reflected in `_commitStack`, although current UI paths all push records.

### (f) User hits reset

✅ Correct

- Reset triggers `CompositionController.AnimateResetThen`.
- Callback clears composition.
- `_wordRequestVersion` increments, invalidating pending word callbacks.
- Word row clears.
- Phrase row clears.
- Fresh phrase prediction fires with empty context.

⚠️ Concern

- `_lastCommittedAutocompleteWord` is not reset.
- Pending phrase callbacks are not version-gated at AppController level.

## Section 4: Coherence Filter Audit

⚠️ Concern

Location:

- `Assets/Scripts/UI/AppController.cs`
- `ToCoherentPhraseContinuation`
- `ToPhraseContinuation`
- `CollapseRepeatedBoundaryWords`
- `NormalizeForComparison`

Rules:

- `ToCoherentPhraseContinuation(committedText, phrase)` computes fully committed context by stripping the current partial word.
- `ToPhraseContinuation(completedContext, phrase)` removes the completed context prefix if the phrase starts with it, case-insensitively.
- `CollapseRepeatedBoundaryWords(completedContext, phrase)` removes the first word of the phrase if it duplicates the last word of the completed context.
- `NormalizeForComparison` lowercases and removes punctuation except whitespace for equality checks.

Inputs:

- Phrase hover preview.
- Phrase dwell selection/commit.

Outputs:

- A cleaned continuation phrase string.
- Empty string if input phrase becomes empty; commit path skips empty continuation.

Examples that work:

- Committed: `im in`, phrase: `im in pain`  
  Completed context: `im `, continuation: `in pain`, append strips partial `in`, final composition becomes `im in pain`.
- Committed: `I need some wat`, phrase: `I need some water please`  
  Completed context: `I need some `, continuation: `water please`, append strips `wat`, final composition becomes `I need some water please`.

Edge cases not fully handled:

- Punctuation at phrase/context boundary can defeat `ToPhraseContinuation` because it uses raw substring comparison before normalized comparison.
- It does not do grammar validation, only duplicate-prefix/boundary cleanup.
- It does not enforce capitalization or terminal punctuation.
- It does not ask the LLM to re-rank or repair incoherent text.
- It may over-remove if the last completed word is intentionally repeated.
- Contractions are partially handled by normalized comparison, but raw prefix stripping may still miss variants like `I'm` vs `im`.

## Section 5: Memory and Lifecycle

### Singletons

⚠️ Concern

- `PredictionService` enforces a singleton in `Awake`; duplicates are destroyed.
- The primary instance calls `DontDestroyOnLoad`.
- `OnDestroy` cancels/disposes CTS instances.

Concern: `PredictionService.OnDestroy` does not set `Instance = null` when the primary instance is destroyed. In normal single-scene use this is fine; in tests or scene teardown/reload it can leave a stale static reference.

### DOTween / Animations

✅ Correct

- No code currently uses DOTween APIs or `DG.Tweening`.
- `PremiumTileAnimator.OnDestroy` calls `StopAllCoroutines`.
- `AppController.OnDestroy` stops background and speak punch coroutines.

⚠️ Concern

- The requirement said DOTween should be used, but current implementation uses coroutine fallbacks. This is documented elsewhere, but still a deviation.
- `CompositionController` has running coroutines but no `OnDestroy` cleanup. Unity stops coroutines when the object is destroyed, but explicit cleanup would be clearer.

### CancellationTokenSources

✅ Correct

- `PredictionService` cancels and disposes `_wordCts` before replacing it.
- `_phraseCts` is cancelled and disposed before replacement.
- Both are cancelled/disposed in `OnDestroy`.
- `LLMClient` uses `using` for timeout and linked CTS.

⚠️ Concern

- `linkedToken.Register(...)` result is not explicitly disposed. Disposing the linked CTS should release registrations, so this is likely fine, but explicit disposal would be tidier.

### Resource Loads

✅ Correct

- `UserProfile` caches `user_profile.md`.
- `ConfigLoader` caches API key through `Lazy<string>`.
- `UiTheme` caches sprites, textures, and font asset lookups.

⚠️ Concern

- `CompositionController.Update` calls `Render()` every frame, rebuilding rich-text strings and assigning `textLabel.text` continuously for cursor breathing. This can allocate and force TMP updates in long sessions.
- `FlickerTile.Update` mutates `Image.color` every frame for every tile. This is expected for flicker, but it can fight hover/selection alpha animations because both write the same image color.

## Section 6: Error Paths

### Anthropic API

✅ Correct

- Network/data errors log `[LLMClient] Network/data error`.
- HTTP errors log status and message.
- 401 logs an auth-specific error.
- 429 logs rate-limit context.
- Empty body and bad envelope are logged.
- JSON parse failures log parse error plus truncated raw text.
- Prediction failures return `Success=false`; UI clears affected row.

⚠️ Concern

- End user is not shown any error state; failures only appear in Console.
- Request body/user prompt is not logged, so prompt debugging requires code inspection or temporary logs.

### Resources.Load / User Profile

✅ Correct

- Missing `user_profile.md` logs a warning and falls back to a generic ALS AAC profile.
- Empty profile logs a warning and falls back.
- No crash.

### Resources.Load / Config

⚠️ Concern

- Missing or invalid `config.json` throws `InvalidOperationException` in `ConfigLoader`.
- This exception is caught by `PredictionService` worker `catch (Exception ex)` and returned as failed prediction, so the app does not crash.
- The user sees no UI failure message; only Console logs.

### Font Resources

⚠️ Concern

- Missing Montserrat TMP assets silently fall back to LiberationSans SDF.
- There is a TODO in `AppController` and instructions in `UI_NOTES.md`, but no runtime warning telling the user the font fallback is active.

### File IO

✅ Correct

- No direct runtime file IO beyond Unity `Resources.Load`.

## Section 7: Dead Code Inventory

### Script Inventory

| File | Status | Notes |
|---|---|---|
| `Assets/Scripts/Core/UserProfile.cs` | ACTIVE | Used by `PredictionService.Awake` and `LLMClient` prompts. |
| `Assets/Scripts/LLM/ConfigLoader.cs` | ACTIVE | Used by `LLMClient` for API key. |
| `Assets/Scripts/LLM/LLMClient.cs` | ACTIVE | Used by `PredictionService`. |
| `Assets/Scripts/LLM/PredictionService.cs` | ACTIVE | Scene singleton, used by `AppController`. |
| `Assets/Scripts/LLM/PredictionTypes.cs` | ACTIVE | DTOs used by LLM/prediction/UI. |
| `Assets/Scripts/UI/AppController.cs` | ACTIVE | Runtime scene orchestrator and UI builder. |
| `Assets/Scripts/UI/CompositionController.cs` | ACTIVE | Composition state and rendering. |
| `Assets/Scripts/UI/DwellSelectable.cs` | ACTIVE | Used by all tiles. |
| `Assets/Scripts/UI/FlickerTile.cs` | ACTIVE | Used by phrase/word/key tiles. |
| `Assets/Scripts/UI/GhostPreviewHelper.cs` | ACTIVE UTILITY | Used by `AppController` hover previews. |
| `Assets/Scripts/UI/KeyboardController.cs` | ACTIVE | Builds keyboard and forwards key events. |
| `Assets/Scripts/UI/KeyTile.cs` | ACTIVE | Runtime key component. |
| `Assets/Scripts/UI/PhraseRowController.cs` | ACTIVE | Builds/updates phrase tiles. |
| `Assets/Scripts/UI/PhraseTile.cs` | ACTIVE | Phrase suggestion tile. |
| `Assets/Scripts/UI/PremiumTileAnimator.cs` | ACTIVE | Hover/selection/flying label animations. |
| `Assets/Scripts/UI/UiTheme.cs` | ACTIVE UTILITY | Theme/sprite/font factory. |
| `Assets/Scripts/UI/WordRowController.cs` | ACTIVE | Builds/updates word tiles. |
| `Assets/Scripts/UI/WordTile.cs` | ACTIVE | Word suggestion tile. |

No clearly orphaned script files found under `Assets/Scripts`.

### Commented-Out Code Blocks

✅ Correct

- No large commented-out code blocks found.

### TODO / FIXME / HACK

⚠️ Concern

- `AppController.cs` contains a TODO for manual Montserrat TMP Font Asset generation. This is valid for now, but should be resolved before final demo packaging.
- No `FIXME` or `HACK` markers found.

### Diagnostic Logs To Remove Before Production

⚠️ Concern

The following diagnostic logs are still present and should be removed or gated before final/demo build:

- `[WordChain]` logs in `AppController`, `PredictionService`, `KeyboardController`, `KeyTile`, `WordRowController`.
- `[WordChain-A]`, `[WordChain-B]`, `[WordChain-C]`.
- `[WordTile-Hover]`.
- `[PhraseContext]`.
- `[PhraseCommit]`.
- Verbose debounce/cancellation logs in `PredictionService`.
- `[ConfigLoader] API key loaded successfully.` may be acceptable but reveals config path behavior in logs.
- `[UserProfile] Loaded profile...` is useful but verbose.

## Highest-Priority Findings

1. ❌ Phrase prompt does not explicitly instruct continuation. It asks for "complete phrases" and can generate full sentences that fight composition semantics.
2. ⚠️ Word prediction prompt receives duplicated partial context for active typing (`sentenceSoFar` includes the partial and `currentPartialWord` repeats it).
3. ⚠️ Word predictions intentionally fire after space/word commit with empty partial, but the LLM prompt is still framed as "Complete the partial word ''" instead of next-word prediction.
4. ⚠️ Phrase callbacks lack an AppController-level version guard.
5. ⚠️ Debug diagnostic logs from previous iterations are still scattered through runtime paths.
6. ⚠️ `CompositionController.Render()` updates TMP text every frame for cursor breathing, which is visually simple but allocation/performance-risky over long sessions.
