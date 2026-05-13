# BCIKeyboardXR — Backend Plan
Version: 1  |  Date: 2026-05-12  |  Changed: Initial authoring

---

## 1. File Map

| File | Namespace | Class Type | Hard Dependencies |
|------|-----------|------------|-------------------|
| `Assets/Scripts/Core/UserProfile.cs` | `BCIKeyboardXR.Core` | Static class | `UnityEngine` (Resources, TextAsset, Debug) |
| `Assets/Resources/user_profile.md` | — | Markdown resource | None |
| `Assets/Scripts/LLM/ConfigLoader.cs` | `BCIKeyboardXR.LLM` | Static class | `UnityEngine` (Resources, TextAsset, JsonUtility) |
| `Assets/Scripts/LLM/PredictionTypes.cs` | `BCIKeyboardXR.LLM` | POCOs (plain classes) | None |
| `Assets/Scripts/LLM/LLMClient.cs` | `BCIKeyboardXR.LLM` | Non-MonoBehaviour class | `UnityEngine.Networking`, `System.Threading.Tasks`, `Newtonsoft.Json` (hard dep) |
| `Assets/Scripts/LLM/PredictionService.cs` | `BCIKeyboardXR.LLM` | MonoBehaviour singleton | `BCIKeyboardXR.LLM.LLMClient`, `BCIKeyboardXR.LLM.PredictionTypes`, `BCIKeyboardXR.Core.UserProfile`, `System.Threading` |
| `Assets/Scripts/Core/BackendSmokeTest.cs` | `BCIKeyboardXR.Core` | MonoBehaviour (throwaway) | `BCIKeyboardXR.LLM.PredictionService` |

**Package prerequisites (user must install before compiling):**
- `com.unity.nuget.newtonsoft-json` — add to `Packages/manifest.json` under dependencies.
  Recommended version: `"com.unity.nuget.newtonsoft-json": "3.2.1"`.
  After install Unity auto-defines the scripting symbol `UNITY_NEWTONSOFT_JSON` in the project (Unity 6 behavior confirmed).

---

## 2. Data Flow

```
[User presses key / partial word changes]
        |
        v
PredictionService.RequestWordPrediction(sentenceSoFar, partialWord, onComplete)
        |
        |-- Check cache: key = "{sentence}|{partial}" (normalized)
        |   HIT  --> invoke onComplete via SynchronizationContext.Post (main thread)
        |           DONE
        |
        |-- MISS: cancel any in-flight CancellationTokenSource (debounce reset)
              |
              v
        Task.Delay(150ms, newCancellationToken)   [debounce window]
              |
              | (if not cancelled within 150ms)
              v
        LLMClient.PredictWords(sentence, partial, ct)
              |
              v
        ConfigLoader.AnthropicApiKey  +  UserProfile.GetSystemPromptSection()
              |
              v
        UnityWebRequest POST  -->  https://api.anthropic.com/v1/messages
              |                    model: claude-haiku-4-5
              |                    max_tokens: 200
              |                    timeout: 5s
              |
              v
        Raw JSON response string
              |
              v
        Newtonsoft.Json.JsonConvert.DeserializeObject<...>
        (strip markdown fences, partial recovery on malformed JSON)
              |
              v
        WordPrediction { List<string> Completions, bool Success }
              |
              v
        Write to cache (key above, timestamped)
              |
              v
        _syncContext.Post(...)  -->  onComplete(prediction)  [main thread]
              |
              v
        [UI layer — future] receives WordPrediction and renders suggestions

[Phrase prediction follows identical flow except:]
  - cache key = "{sentence}|<phrase>" (no partial word component)
  - max_tokens: 400
  - no debounce (phrase requests are triggered at sentence boundary, not per-keystroke)
```

---

## 3. Design Decisions

### Decision 1: Main-thread marshaling

**Choice: Capture `SynchronizationContext` in `PredictionService.Awake()`, use `.Post()` for all callbacks.**

Rationale: Unity's `UnitySynchronizationContext` is set on the main thread. Capturing it in `Awake()` (which Unity guarantees runs on the main thread) is the minimal, zero-dependency approach. A `UnityMainThreadDispatcher` queue is heavier — it requires an always-running MonoBehaviour with an `Update()` loop and a concurrent queue. Since every callback in this system is a one-shot completion (not a stream), `.Post()` on the captured context is sufficient and adds no overhead. The coder must not use `.Send()` (blocks the calling thread, deadlock risk from async context).

```
// In Awake():
_syncContext = SynchronizationContext.Current;

// In async completion:
_syncContext.Post(_ => onComplete(prediction), null);
```

### Decision 2: Newtonsoft conditional compilation

**Choice: Use Newtonsoft.Json as a hard dependency with no conditional compilation guard.**

Rationale: The preprocessor symbol `UNITY_NEWTONSOFT_JSON` is NOT automatically defined by the `com.unity.nuget.newtonsoft-json` package in Unity 6 LTS. That symbol is defined only when using the older Unity-distributed Newtonsoft package through certain UPM workflows, and even then it is unreliable across Unity versions. A `#if UNITY_NEWTONSOFT_JSON / #else JsonUtility #endif` fallback creates two code paths that must both be maintained and tested.

The correct approach: treat Newtonsoft as a hard compile-time dependency. `LLMClient.cs` will have `using Newtonsoft.Json;` unconditionally. If the package is missing, the project will fail to compile with a clear `CS0246` error pointing directly at the missing `using`, which is the correct failure mode — loud, not silent. The Master Coder must document in a comment above the `using` that `com.unity.nuget.newtonsoft-json 3.2.1` must be installed first.

`JsonUtility` is used only in `ConfigLoader.cs` for the simple flat JSON structure of `config.json`. That file requires no Newtonsoft.

### Decision 3: Debounce mechanics

**Choice: `Task.Delay(150, ct)` pattern — cancel-and-restart on each incoming call.**

Exact flow:

```
RequestWordPrediction called:
  1. _wordCts?.Cancel()           // cancel previous debounce + any in-flight request
  2. _wordCts?.Dispose()
  3. _wordCts = new CancellationTokenSource()
  4. _ = RunDebouncedWordRequest(sentence, partial, onComplete, _wordCts.Token)

async Task RunDebouncedWordRequest(..., CancellationToken ct):
  1. await Task.Delay(150, ct)    // throws OperationCanceledException if new call arrives
  2. // 150ms elapsed without cancellation — proceed
  3. result = await _client.PredictWords(sentence, partial, ct)
  4. WriteToCache(key, result)
  5. _syncContext.Post(_ => onComplete(result), null)
```

The `OperationCanceledException` from `Task.Delay` must be caught at the `RunDebouncedWordRequest` call site (or within that method), logged at `Debug.Log` level (not `LogError`), and silently discarded — it is normal control flow, not an error.

This is preferable to a `System.Timers.Timer` or `Coroutine` approach because the same `CancellationToken` propagates into the HTTP request, ensuring an in-flight network call is also aborted when the user types another key.

### Decision 4: Cache key format

**Confirmed key format:**

Word prediction: `$"{sentenceSoFar.Trim().ToLowerInvariant()}|{partialWord.Trim().ToLowerInvariant()}"`

Phrase prediction: `$"{sentenceSoFar.Trim().ToLowerInvariant()}|__phrase__"`

The `__phrase__` literal sentinel ensures word and phrase cache entries cannot collide even when `partialWord` is empty. Both normalizations (Trim + ToLowerInvariant) must be applied before key construction and before passing to `LLMClient` to avoid near-duplicate API calls from trailing spaces or case differences.

Cache eviction policy:
- Max 100 entries (Dictionary<string, CacheEntry> where CacheEntry holds the result + `DateTime timestamp`).
- TTL: 60 seconds from insertion time.
- On insert: if `Count >= 100`, iterate and remove all entries older than 60 seconds first. If still >= 100 after TTL removal, remove the oldest entry by timestamp (linear scan — acceptable at 100-entry scale).
- On read: check timestamp; if expired, treat as miss, do not return stale data.

### Decision 5: UnityWebRequest async pattern

**Choice: `TaskCompletionSource<UnityWebRequest>` wrapping the `completed` callback.**

Unity 6 LTS does not natively `await` a `UnityWebRequestAsyncOperation` in a non-coroutine context without a custom awaiter. The `completed` event is the stable hook. The pattern:

```csharp
var tcs = new TaskCompletionSource<UnityWebRequest>();
var operation = request.SendWebRequest();

// Register cancellation
ct.Register(() =>
{
    request.Abort();
    tcs.TrySetCanceled();
});

operation.completed += _ =>
{
    if (!ct.IsCancellationRequested)
        tcs.TrySetResult(request);
};

// 5-second timeout enforced via CancellationTokenSource.CancelAfter(5000)
// on a linked token (link the caller ct + a 5s timeout ct)
UnityWebRequest completedRequest = await tcs.Task;
```

The 5-second timeout must be implemented via a secondary `CancellationTokenSource` linked to the caller's token:

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
// pass linkedCts.Token into the TCS pattern above
```

Do NOT use `UnityWebRequest.timeout` (the integer property). It only cancels the connection phase, not the full request duration, and its behavior is inconsistent in Unity 6.

The `completed` callback fires on the Unity main thread (it is driven by Unity's internal update loop). The `tcs.Task` continuation will run on whatever context awaited it — `LLMClient` is called from `PredictionService`'s async chain, which was dispatched off the main thread via `Task.Run` (see Implementation Notes). This is safe.

---

## 4. Interface Contracts

### `BCIKeyboardXR.Core.UserProfile` (static class)

```csharp
// Call once at application startup (e.g., from PredictionService.Awake)
// Subsequent calls return cached value — no repeated disk I/O.
public static void Initialize();

// Returns the full text of user_profile.md formatted for inclusion in an LLM system prompt.
// Returns a fallback string (never null, never throws) if file is missing.
public static string GetSystemPromptSection();
```

### `BCIKeyboardXR.LLM.ConfigLoader` (static class)

```csharp
// Throws InvalidOperationException with descriptive message if key is null, empty, or file missing.
// Caches after first load; subsequent accesses are O(1).
public static string AnthropicApiKey { get; }
```

### `BCIKeyboardXR.LLM.PredictionTypes` (plain classes, no MonoBehaviour)

```csharp
public class WordPrediction
{
    public List<string> Completions;  // 0–5 word completions; empty list if Success == false
    public bool Success;              // false on network error, timeout, or JSON parse failure
}

public class PhrasePrediction
{
    public List<string> Phrases;      // 0–3 full phrase suggestions; empty list if Success == false
    public bool Success;
}
```

### `BCIKeyboardXR.LLM.LLMClient` (non-MonoBehaviour, instantiated class)

```csharp
// Constructor — call once and cache the instance in PredictionService.
public LLMClient();

// Returns WordPrediction with Success=false (not throws) on timeout, network error, or parse failure.
// Throws OperationCanceledException if ct is cancelled.
public Task<WordPrediction> PredictWords(
    string sentenceSoFar,
    string currentPartialWord,
    CancellationToken ct);

// Returns PhrasePrediction with Success=false on failure.
// Throws OperationCanceledException if ct is cancelled.
public Task<PhrasePrediction> PredictPhrases(
    string sentenceSoFar,
    CancellationToken ct);
```

### `BCIKeyboardXR.LLM.PredictionService` (MonoBehaviour singleton)

```csharp
// Singleton accessor — returns null if not yet initialized.
public static PredictionService Instance { get; }

// Fire-and-forget. Applies 150ms debounce. Cancels previous in-flight word request.
// onComplete is always called on the Unity main thread.
// onComplete receives Success=false if request fails or is superseded.
public void RequestWordPrediction(
    string sentenceSoFar,
    string currentPartialWord,
    Action<WordPrediction> onComplete);

// Fire-and-forget. No debounce. Does NOT cancel in-flight word requests.
// onComplete is always called on the Unity main thread.
public void RequestPhrasePrediction(
    string sentenceSoFar,
    Action<PhrasePrediction> onComplete);
```

### `BCIKeyboardXR.Core.BackendSmokeTest` (MonoBehaviour, throwaway)

```csharp
// Called automatically on scene Start. Fires both prediction types.
// WARNING: THROWAWAY TEST CODE — remove before production.
private void Start();

// Public hook for inspector button trigger.
// WARNING: THROWAWAY TEST CODE — remove before production.
public void RunTest();
```

---

## 5. Non-Goals

This backend layer does NOT:

- Render any UI (no canvas, no text mesh updates, no button state management).
- Handle BCI signal decoding or classification — it receives already-decoded text intent.
- Manage Unity Input System events — it does not listen for keypresses.
- Implement TTS (text-to-speech) or any audio output.
- Persist conversation history across sessions — each `RequestWordPrediction` / `RequestPhrasePrediction` call is stateless from the perspective of the backend (the caller constructs `sentenceSoFar`).
- Handle authentication token refresh — the API key is static.
- Implement retry logic — a failed request returns `Success=false` immediately; retry policy is the responsibility of the caller (future UI layer).
- Stream API responses — uses the standard `/v1/messages` non-streaming endpoint.
- Validate the content of `user_profile.md` — it is passed through as-is.

---

## 6. Implementation Notes (Unity 6 Specific)

### Newtonsoft installation
Add to `Packages/manifest.json` under `"dependencies"`:
```
"com.unity.nuget.newtonsoft-json": "3.2.1"
```
Unity Package Manager will resolve it from the Unity package registry. Do not use the standalone Newtonsoft NuGet package — it will conflict with Unity's assembly resolution.

### `Resources.Load` path rules
The path passed to `Resources.Load<TextAsset>()` must NOT include the file extension and must be relative to any `Resources/` folder in the project. Correct usages:
- `Resources.Load<TextAsset>("config")` for `Assets/Resources/config.json`
- `Resources.Load<TextAsset>("user_profile")` for `Assets/Resources/user_profile.md`

`user_profile.md` loaded as `TextAsset` will have its full markdown text in `.text`. Unity treats `.md` as a text asset by default in Unity 6.

### `JsonUtility` limitations (ConfigLoader)
`JsonUtility` cannot deserialize a top-level JSON object into a `Dictionary`. Use a `[Serializable]` private class with explicit fields matching the JSON keys. Example config.json structure expected:
```json
{ "anthropic_api_key": "sk-ant-..." }
```
The deserializing class must be:
```csharp
[Serializable]
private class ApiConfig { public string anthropic_api_key; }
```

### UnityWebRequest on background threads
`UnityWebRequest.SendWebRequest()` must be called from the Unity main thread. `LLMClient.PredictWords` and `PredictPhrases` are `async Task` methods. `PredictionService` must dispatch the `LLMClient` call such that `SendWebRequest()` executes on the main thread. The safest pattern: do all `UnityWebRequest` construction and `.SendWebRequest()` calls synchronously on the main thread before any `await`, then `await` the `TaskCompletionSource.Task`. Since `PredictionService.RequestWordPrediction` is called from the main thread, and `RunDebouncedWordRequest` begins on the main thread before its first `await Task.Delay`, `SendWebRequest()` must be called before any `await` in `LLMClient`, or the entire `LLMClient` call must be wrapped in a `UnityMainThreadDispatcher.Enqueue` if called from a background thread. The chosen design keeps the `LLMClient` call on the main thread: `await Task.Delay` suspends and resumes on the `UnitySynchronizationContext` (Unity 6 default), so execution after `Task.Delay` is still on the main thread. The coder must verify this assumption with a `Debug.Assert(Thread.CurrentThread.IsBackground == false)` in `LLMClient.PredictWords` at entry.

### CancellationToken and `UnityWebRequest` disposal
After `await tcs.Task`, the `UnityWebRequest` must be disposed via `using` or explicit `.Dispose()` before returning. Failure to dispose causes native memory leaks.

### Singleton pattern for PredictionService
Use the `Awake()` instance check pattern, not a static initializer. `DontDestroyOnLoad` must be called in `Awake()` if the service must persist across scene loads (recommend: yes, since AAC use requires persistence).

### Debug.Log prefix convention
Every `Debug.Log` in `PredictionService` must use the prefix `[PredictionService]`. Format:
```
[PredictionService] {timestamp} | {type:Word|Phrase} | input_len={n} | latency={ms}ms | cache={HIT|MISS}
```
`timestamp` = `System.DateTime.UtcNow.ToString("HH:mm:ss.fff")`

### Markdown fence stripping
Anthropic's claude-haiku-4-5 occasionally wraps JSON responses in triple-backtick fences. The defensive parsing sequence in `LLMClient`:
1. If response contains `` ``` ``, extract text between first `` ``` `` (with or without `json` label) and last `` ``` ``.
2. Trim whitespace.
3. Attempt `JsonConvert.DeserializeObject`.
4. On `JsonException`: log `Debug.LogWarning` with raw response (first 200 chars), return `Success=false`.
Never throw from JSON parsing — always return a typed result with `Success=false`.

### Assembly Definition Files (asmdef)
If the project grows to require asmdef isolation, `BCIKeyboardXR.Core` and `BCIKeyboardXR.LLM` should be separate asmdefs. `BCIKeyboardXR.LLM` must reference `BCIKeyboardXR.Core` (one-way dependency). Do not add asmdefs now — they are not required for the current scope and add setup friction. Note this as the correct future direction.
