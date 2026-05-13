# BCIKeyboardXR — Live Handoff File

> **Purpose**: Persistent context for cross-session continuity. If context is cleared, start by reading this file.
> **Last updated**: 2026-05-12
> **Active stage**: UI layer complete

---

## Project Identity

- **Project**: BCIKeyboardXR — BCI-driven AAC interface (Cognixion internship take-home)
- **Unity version**: Unity 6 LTS, macOS, 2D URP template
- **This prompt**: Prompt 2 of 2 — UI layer complete
- **Git branch**: main

---

## Environment Facts (verified)

| Fact | Value |
|------|-------|
| Newtonsoft.Json installed? | **YES** — `com.unity.nuget.newtonsoft-json` present in manifest.json |
| UnityWebRequest module | Present (`com.unity.modules.unitywebrequest`) |
| config.json location | `Assets/Resources/config.json` — gitignored, do not touch |
| Script folders | `Assets/Scripts/Core/`, `Assets/Scripts/LLM/`, `Assets/Scripts/UI/` |
| Input system | `com.unity.inputsystem 1.19.0` |

**Newtonsoft action**: Complete. `Packages/manifest.json` includes `com.unity.nuget.newtonsoft-json`.

---

## Deliverables Checklist

| File | Status | Notes |
|------|--------|-------|
| `BACKEND_PLAN.md` | ✅ Done | Architect agent |
| `COUNCIL_BRIEF.md` | ✅ Done | APPROVED WITH CHANGES |
| `BACKEND_README.md` | ✅ Done | |
| `Assets/Scripts/Core/UserProfile.cs` | ✅ Done | |
| `Assets/Resources/user_profile.md` | ✅ Done | |
| `Assets/Scripts/LLM/ConfigLoader.cs` | ✅ Done | Lazy<string> with ExecutionAndPublication |
| `Assets/Scripts/LLM/LLMClient.cs` | ✅ Done | All 6 council items + reviewer critical fix |
| `Assets/Scripts/LLM/PredictionTypes.cs` | ✅ Done | Includes HttpStatusCode diagnostic field |
| `Assets/Scripts/LLM/PredictionService.cs` | ✅ Done | All 6 council items + reviewer fixes |
| `Assets/Scripts/Core/BackendSmokeTest.cs` | ✅ Removed | UI scene now exercises backend |
| `Packages/manifest.json` | ✅ Done | com.unity.nuget.newtonsoft-json: 3.2.1 added |
| `Assets/Scripts/UI/DwellSelectable.cs` | ✅ Done | Hover-dwell with radial progress and registry |
| `Assets/Scripts/UI/FlickerTile.cs` | ✅ Done | SSVEP-style alpha modulation |
| `Assets/Scripts/UI/PhraseTile.cs` | ✅ Done | Runtime-built glass phrase tile |
| `Assets/Scripts/UI/WordTile.cs` | ✅ Done | Runtime-built glass word tile |
| `Assets/Scripts/UI/KeyTile.cs` | ✅ Done | Standard/action keyboard key tile |
| `Assets/Scripts/UI/PhraseRowController.cs` | ✅ Done | 4 phrase tiles, 2x2 grid |
| `Assets/Scripts/UI/WordRowController.cs` | ✅ Done | 6 word tiles |
| `Assets/Scripts/UI/KeyboardController.cs` | ✅ Done | QWERTY + punctuation + action keys |
| `Assets/Scripts/UI/CompositionController.cs` | ✅ Done | Smart commits, ghost preview, pulse/fade |
| `Assets/Scripts/UI/AppController.cs` | ✅ Done | Orchestrates UI and PredictionService |
| `Assets/Scripts/UI/GhostPreviewHelper.cs` | ✅ Done | Preview helpers |
| `Assets/Scripts/UI/UiTheme.cs` | ✅ Done | Runtime UI theme sprites/textures |
| `Assets/Prefabs/PhraseTile.prefab` | ✅ Done | Script-root prefab; visuals build in Awake |
| `Assets/Prefabs/WordTile.prefab` | ✅ Done | Script-root prefab; visuals build in Awake |
| `Assets/Prefabs/KeyTile.prefab` | ✅ Done | Script-root prefab; visuals build in Awake |
| `Assets/Prefabs/ActionKey.prefab` | ✅ Done | Gold action-key variant |
| `Assets/Scenes/Main.unity` | ✅ Done | Contains Main Camera, PredictionService, AppController |
| `UI_NOTES.md` | ✅ Done | Run notes, limitations, demo suggestions |

---

## Anthropic API Shape (claude-haiku-4-5)

- **Endpoint**: `https://api.anthropic.com/v1/messages`
- **Method**: POST
- **Headers**:
  - `x-api-key: <key>`
  - `anthropic-version: 2023-06-01`
  - `content-type: application/json`
- **Model string**: `claude-haiku-4-5`
- **Request body**:
  ```json
  {
    "model": "claude-haiku-4-5",
    "max_tokens": 200,
    "system": "<system prompt>",
    "messages": [{"role": "user", "content": "<user message>"}]
  }
  ```
- **Response**: `content[0].text` contains the model output

---

## Key Design Decisions (resolved)

| Decision | Resolved Choice |
|----------|----------------|
| Main-thread marshaling | `SynchronizationContext.Current` captured in `Awake()`, `.Post()` for callbacks |
| JSON library | Newtonsoft hard dep, no `#if` guard — missing package = compile error (intentional) |
| Cache key (words) | `"{sentence.Trim().ToLowerInvariant()}|{partial.Trim().ToLowerInvariant()}"` |
| Cache key (phrases) | `"{sentence.Trim().ToLowerInvariant()}|__phrase__"` (sentinel prevents collision) |
| Debounce impl | `Task.Delay(150, ct)` + cancel-restart; same CT propagates to HTTP abort |
| Singleton pattern | MonoBehaviour `Awake()` guard + `DontDestroyOnLoad` |
| UnityWebRequest async | `TaskCompletionSource<UnityWebRequest>` wrapping `completed` event |
| Timeout | Linked `CancellationTokenSource(5s)` — NOT `UnityWebRequest.timeout` int property |
| ConfigLoader JSON | `JsonUtility` with private `[Serializable] class ApiConfig { public string anthropic_api_key; }` |

---

## Council Verdict: APPROVED WITH CHANGES (2026-05-12)

6 mandatory action items for Master Coder:

1. **TCS: `new TaskCompletionSource<UnityWebRequest>(TaskCreationOptions.RunContinuationsAsynchronously)`** — prevents inline continuation from completed callback
2. **Request-generation counter** — `_wordRequestGeneration` int; check generation == current before `_syncContext.Post`; discard stale results silently
3. **Handle `ProtocolError` explicitly** — check `completedRequest.result` enum; log 401 as `LogError`, 429 as `LogWarning`; include `responseCode` in log
4. **ConfigLoader: `Lazy<string>` with `LazyThreadSafetyMode.ExecutionAndPublication`** — replaces manual backing field
5. **Assert main-thread entry** — first executable line of `LLMClient.PredictWords` and `PredictPhrases`: `Debug.Assert(!Thread.CurrentThread.IsBackground, ...)`
6. **Comment that `CancellationToken` struct (not `CancellationTokenSource`) is passed to LLMClient** — documentation requirement, not code change

---

## Reviewer Post-Fix Issues (all resolved)

| Issue | Severity | Fix Applied |
|-------|----------|-------------|
| Use-after-dispose in `completed` callback | CRITICAL | Added `if (tcs.Task.IsCompleted) return;` guard in lambda |
| No main-thread assert at public API entry | WARNING | Added `Debug.Assert(!Thread.CurrentThread.IsBackground)` to both `RequestWordPrediction` and `RequestPhrasePrediction` |
| Silent callback drop when `_syncContext` null | WARNING | All 4 `Post()` calls replaced with null-check + direct invocation fallback |

---

## Agent Pipeline Status

| Stage | Agent | Status | Output |
|-------|-------|--------|--------|
| 1 | Systems Architect | ✅ Done | `BACKEND_PLAN.md` |
| 2 | Council Review | ✅ APPROVED WITH CHANGES | `COUNCIL_BRIEF.md` |
| 3 | Master Coder | ✅ Done | all .cs + .md files |
| 4 | Reviewer | ✅ PASS (issues fixed) | inline fixes applied |
| **5** | **Prompt 2 UI layer** | ✅ Done | UI scripts, prefabs, Main scene, notes |

---

## UI Layer Status (2026-05-12)

`Assets/Scenes/Main.unity` is runnable. It contains a `PredictionService` GameObject and an `AppController` GameObject. `AppController` builds the full Screen Space Overlay Canvas at runtime:

- top bar with Reset and Speak
- 2x2 phrase grid
- 6-tile word row
- composition bar with committed text plus ghost preview
- QWERTY keyboard with punctuation, Space, Backspace, and Enter

The runtime-built UI uses generated rounded sprites and a pastel blue radial texture. Tile visuals, dwell progress images, hover rims, and labels are created by the tile/controller scripts if references are not assigned in the Inspector. Current polish includes hover lift/glow, dwell-ring pulse, selection flash/punch, flying commit labels, composition-bar pulse/stretch, reset fade-down, Speak button punch, and a very subtle breathing background.

The UI integrates with `PredictionService.Instance`:

- letters append characters and request word prediction
- word commit appends a whole word, clears word tiles, and requests phrase prediction
- phrase commit appends a whole phrase and requests fresh phrase prediction
- Backspace removes the most recent committed word/phrase as a unit when applicable
- Reset clears UI state and requests empty-context phrases
- Speak logs the current sentence and pulses the composition bar

Mouse hover is the gaze stand-in. Dwell selection uses Unity UI pointer enter/exit through an EventSystem with `InputSystemUIInputModule`.

---

## Backend Integration Surface

The UI layer wires up to `PredictionService` via:
```csharp
PredictionService.Instance.RequestWordPrediction(sentenceSoFar, partial, result => {
    // update word suggestion tiles
});
PredictionService.Instance.RequestPhrasePrediction(sentenceSoFar, result => {
    // update phrase suggestion tiles
});
```
Both callbacks are guaranteed on the Unity main thread.
`PredictionService` is attached to a GameObject in `Assets/Scenes/Main.unity`.

---

## Blocking Issues / Warnings

No known project blockers. `dotnet build Assembly-CSharp.csproj` passes from Codex with one existing `ConfigLoader.ApiConfig.anthropic_api_key` warning. DOTween is declared in `Packages/manifest.json`, but Unity still needs the editor-only `Tools > Demigiant > DOTween Utility Panel > Setup DOTween` step if you want to convert the coroutine tweens to DOTween-backed sequences.

---

## Namespace Conventions

```
BCIKeyboardXR.Core   → Assets/Scripts/Core/
BCIKeyboardXR.LLM    → Assets/Scripts/LLM/
BCIKeyboardXR.UI     → Assets/Scripts/UI/
```

---

## If You Are Reading This After a Context Clear

1. Read this file top-to-bottom first.
2. Check the Deliverables Checklist — any ✅ files are done, any ⏳ need work.
3. Check Agent Pipeline Status for current stage.
4. The Anthropic API shape above is verified — do not re-research.
5. Do NOT modify `Assets/Resources/config.json`.
6. Newtonsoft.Json is already declared in `Packages/manifest.json`.
7. Continue with Unity Editor import/play-mode verification.
