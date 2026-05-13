# BCIKeyboardXR — Backend Layer

LLM word-completion and phrase-suggestion backend for the BCIKeyboardXR AAC interface.
Implements real Anthropic API calls (claude-haiku-4-5) personalized to the Sarah Martinez user profile.

---

## 1. Setup

### Prerequisites

**Newtonsoft.Json (required before compiling)**

The LLM layer (`LLMClient.cs`) depends on `Newtonsoft.Json` for robust JSON parsing with
markdown-fence stripping. Unity's built-in `JsonUtility` cannot handle the nested Anthropic
response structure.

Add the following line to `Packages/manifest.json` under `"dependencies"`:

```json
"com.unity.nuget.newtonsoft-json": "3.2.1"
```

This is already present in `Packages/manifest.json` in this repository. If you regenerated
the manifest, re-add this entry before opening Unity.

After adding (or confirming it is present), open Unity — the Package Manager resolves it
automatically from the Unity package registry. Do not use the standalone NuGet Newtonsoft
package; it conflicts with Unity's assembly resolution.

**API Key**

The Anthropic API key lives in `Assets/Resources/config.json`:

```json
{ "anthropic_api_key": "sk-ant-..." }
```

This file is already populated. Do not commit real API keys to source control.

---

## 2. Smoke Test

The smoke test verifies the full request path:
`PredictionService → LLMClient → Anthropic API → callback → Console`

**Steps:**

1. Open the Main scene (`Assets/Scenes/Main.unity`).
2. Create an empty GameObject. Name it `PredictionService`.
3. Attach the `PredictionService` script (`Assets/Scripts/LLM/PredictionService.cs`) to it.
4. Create a second empty GameObject. Name it `SmokeTest`.
5. Attach the `BackendSmokeTest` script (`Assets/Scripts/Core/BackendSmokeTest.cs`) to it.
6. Hit **Play**.
7. Watch the **Console** window (Window > General > Console).

Both GameObjects can be on the same object. `PredictionService` must exist in the scene
for `BackendSmokeTest` to find it via `PredictionService.Instance`.

---

## 3. Expected Output

Within 2–5 seconds of hitting Play, the Console should show lines similar to:

```
[UserProfile] Loaded profile (1234 chars). Prompt section cached.
[ConfigLoader] API key loaded successfully.
[PredictionService] Initialized.
[SmokeTest] Starting backend smoke test...
[SmokeTest] Word test  — sentence: 'I need some', partial: 'wat'
[SmokeTest] Phrase test — sentence: 'Please help me'
[SmokeTest] Requests dispatched. Awaiting callbacks (up to ~5s)...
[PredictionService] 14:32:01.234 | Word | input_len=15 | latency=1823ms | cache=MISS
[SmokeTest] WORD PREDICTION SUCCESS (6 completions):
[SmokeTest]   [1] water
[SmokeTest]   [2] watch
[SmokeTest]   [3] warm
[SmokeTest]   [4] wait
[SmokeTest]   [5] walk
[SmokeTest]   [6] way
[PredictionService] 14:32:02.891 | Phrase | input_len=14 | latency=2410ms | cache=MISS
[SmokeTest] PHRASE PREDICTION SUCCESS (4 phrases):
[SmokeTest]   [1] Please help me get some water
[SmokeTest]   [2] Please help me adjust my pillow
[SmokeTest]   [3] Please help me call Emma
[SmokeTest]   [4] Please help me turn on the lights
```

Exact completions and phrases will vary by API response. The important signals are:
- `WORD PREDICTION SUCCESS` with at least one completion
- `PHRASE PREDICTION SUCCESS` with at least one phrase
- No `[LLMClient]` error lines

If you see `WORD PREDICTION FAILED` or `PHRASE PREDICTION FAILED`, scroll up in the Console
for `[LLMClient]` warning lines that describe the specific failure (auth error, rate limit,
network error, JSON parse failure).

---

## 4. Known Limitations

**No retry logic.**
A failed request returns `Success=false` immediately. The UI layer (not yet implemented)
is responsible for retry policy. This is by design — see `BACKEND_PLAN.md §5 Non-Goals`.

**No streaming.**
Uses the standard `/v1/messages` non-streaming endpoint. Responses arrive as complete JSON
payloads after the full generation completes. Streaming would require a chunked response
reader and is deferred to a future architecture revision.

**No session persistence.**
Each `RequestWordPrediction` / `RequestPhrasePrediction` call is stateless from the backend's
perspective. The caller (future UI layer) is responsible for maintaining `sentenceSoFar`
across turns. In-memory cache survives within a single Unity session but is lost on restart.

**No BCI signal handling.**
`PredictionService` receives already-decoded text intent (`sentenceSoFar`, `currentPartialWord`).
SSVEP classification and BCI signal processing are out of scope for this backend layer.

**Hard Newtonsoft.Json dependency.**
If `com.unity.nuget.newtonsoft-json 3.2.1` is missing from `Packages/manifest.json`, the
project will fail to compile with `CS0246` on the `using Newtonsoft.Json` line in `LLMClient.cs`.
This is the correct loud failure mode — not a silent fallback.

**Smoke test is throwaway code.**
`BackendSmokeTest.cs` is marked `// THROWAWAY TEST CODE` throughout. It must be removed or
excluded from production builds before shipping.

---

## 5. Architecture Reference

For the full architecture rationale, interface contracts, and threading model, see:

- `BACKEND_PLAN.md` — authoritative architecture document, design decisions, interface contracts
- `COUNCIL_BRIEF.md` — council deliberation and the six required action items (all implemented)

Key design decisions summarized:

| Decision | Choice | Rationale |
|---|---|---|
| Main-thread marshaling | `SynchronizationContext.Post()` captured in `Awake()` | Zero external dependencies; safe for one-shot callbacks |
| Async HTTP | `TaskCompletionSource<UnityWebRequest>` wrapping `completed` event | Stable hook; `UnityWebRequest` does not natively support `await` without custom awaiter |
| Debounce | `Task.Delay(150ms, ct)` cancel-and-restart | Same token propagates into HTTP request, aborting in-flight calls on new keypress |
| JSON parsing | Newtonsoft.Json (hard dep) | `JsonUtility` cannot handle nested structures; conditional compilation creates two code paths |
| Cache | `Dictionary<string, CacheEntry>` with TTL eviction | Linear scan at 100 entries is not a performance concern |
| Thread safety | `Lazy<T>` with `ExecutionAndPublication` in `ConfigLoader` | Eliminates double-initialization race on simultaneous first access |
