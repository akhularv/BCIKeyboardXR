# BCIKeyboardXR Backend — Live Handoff File

> **Purpose**: Persistent context for cross-session continuity. If context is cleared, start by reading this file.
> **Last updated**: 2026-05-12
> **Active stage**: ARCHITECT → COUNCIL REVIEW (in progress)

---

## Project Identity

- **Project**: BCIKeyboardXR — BCI-driven AAC interface (Cognixion internship take-home)
- **Unity version**: Unity 6 LTS, macOS, 2D URP template
- **This prompt**: Prompt 1 of 2 — Backend only (no UI, no scene wiring)
- **Git branch**: main

---

## Environment Facts (verified)

| Fact | Value |
|------|-------|
| Newtonsoft.Json installed? | **NO** — `com.unity.nuget.newtonsoft-json` absent from manifest.json |
| UnityWebRequest module | Present (`com.unity.modules.unitywebrequest`) |
| config.json location | `Assets/Resources/config.json` — gitignored, do not touch |
| Script folders (empty) | `Assets/Scripts/Core/`, `Assets/Scripts/LLM/`, `Assets/Scripts/UI/` |
| Input system | `com.unity.inputsystem 1.19.0` |

**Newtonsoft action required**: User must add `com.unity.nuget.newtonsoft-json` via Package Manager → Add package by name before entering Play mode. This must be documented in BACKEND_README.md.

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
| `Assets/Scripts/Core/BackendSmokeTest.cs` | ✅ Done | IEnumerator Start, throwaway markers |
| `Packages/manifest.json` | ✅ Done | com.unity.nuget.newtonsoft-json: 3.2.1 added |

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

## Key Design Decisions (RESOLVED by Architect — pending Council review)

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
| **5** | **NEXT: Prompt 2 UI layer** | ⏳ Not started | — |

---

## What Prompt 2 Needs from This Backend

The UI layer (Prompt 2) will wire up to `PredictionService` via:
```csharp
PredictionService.Instance.RequestWordPrediction(sentenceSoFar, partial, result => {
    // update word suggestion tiles
});
PredictionService.Instance.RequestPhrasePrediction(sentenceSoFar, result => {
    // update phrase suggestion tiles
});
```
Both callbacks are guaranteed on the Unity main thread.
`PredictionService` must be attached to a persistent GameObject in Main scene before UI scripts run.

---

## Blocking Issues / Warnings

1. **Newtonsoft not installed** — code will compile with `#if NEWTONSOFT_JSON` guard or use conditional compilation. Must instruct user to install.
2. **No UI work** — PredictionService callbacks must marshal to main thread; UI scripts will wire up in Prompt 2.

---

## Namespace Conventions

```
BCIKeyboardXR.Core   → Assets/Scripts/Core/
BCIKeyboardXR.LLM    → Assets/Scripts/LLM/
```

---

## If You Are Reading This After a Context Clear

1. Read this file top-to-bottom first.
2. Check the Deliverables Checklist — any ✅ files are done, any ⏳ need work.
3. Check Agent Pipeline Status for current stage.
4. The Anthropic API shape above is verified — do not re-research.
5. Do NOT modify `Assets/Resources/config.json`.
6. Newtonsoft.Json must be used but requires a manual install step from the user.
7. Continue from the last incomplete stage in the pipeline.
