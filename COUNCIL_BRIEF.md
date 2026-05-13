# The Council — Deliberation Brief

**Date**: 2026-05-12
**Session**: Architecture review of BCIKeyboardXR backend plan (LLMClient, PredictionService, cache, ConfigLoader)
**Chaired by**: Claude Sonnet 4.6

---

## The Question

Six targeted risks in the BCIKeyboardXR backend plan need council review before Master Coder begins implementation: (1) main-thread continuation guarantee after `Task.Delay`, (2) CancellationToken lifecycle race with fast typing, (3) TaskCompletionSource continuation context with UnityWebRequest, (4) cache eviction strategy at 100-entry scale, (5) missing HTTP 4xx/5xx error handling, (6) ConfigLoader static property thread safety.

---

## Claude (Sonnet 4.6)

The plan is well-reasoned and the chosen abstractions are appropriate for a Unity 6 LTS interview deliverable. The main-thread continuation question is real but bounded: `Task.Delay` awaited on Unity's main thread WILL resume on `UnitySynchronizationContext` because Unity installs that context on the main thread and the `PredictionService` async chain begins on the main thread — the guarantee holds unless `ConfigureAwait(false)` leaks from a dependency, which it does not here. The `Debug.Assert` at `LLMClient.PredictWords` entry is a necessary guard and must stay. The CancellationToken race is not a missed-cancellation problem — the cancel propagates into the linked timeout CTS which fires `request.Abort()` via `ct.Register` — but it IS a stale-completion problem: if a cancelled request happens to complete before `TrySetCanceled` fires, `TrySetResult` wins the race and the stale result flows to `onComplete`. The fix is a request-generation counter, not a redesigned token structure. HTTP 4xx/5xx is a genuine gap: `UnityWebRequest.result == UnityWebRequest.Result.ProtocolError` is a separate enum value from `ConnectionError`, and 429 rate-limit or 401 auth failure currently returns `Success=false` with no distinguishable signal, making operational debugging impossible. `ConfigLoader` needs `Lazy<T>` — the current backing-field pattern is not thread-safe. Cache linear scan at 100 entries is not a concern.

---

## Codex (GPT-5.4 · High Reasoning)

The plan is directionally sound for Unity if all UnityWebRequest creation/sending and service state mutation stay on the main thread, but it relies on assumptions that should be made explicit. The biggest risks are not the cache design; they are async continuation context, cancellation ownership, and incomplete HTTP error classification.

**Key risks identified by Codex:**
- `Task.Delay(150, ct)` does not by itself guarantee resumption on `UnitySynchronizationContext`; after `await`, continuation capture depends on the current synchronization context at the await site. If it resumes off the Unity main thread, touching Unity APIs, singleton state, or starting `UnityWebRequest.SendWebRequest()` can fail or behave unpredictably.
- Reusing one `_wordCts` for debounce and HTTP request is acceptable only if the intended behavior is "new input cancels every older request." The race risk is stale completion: an older request may complete after a newer one unless responses are guarded with a request/version id before updating predictions.
- `UnityWebRequest.completed` should fire on the main thread when the request was started correctly, but `TaskCompletionSource` continuations may run inline unless `TaskCreationOptions.RunContinuationsAsynchronously` is used. Inline continuations can cause reentrancy bugs; off-context continuations can cause Unity API violations.
- A 100-entry cache with TTL eviction by linear scan is fine. O(1) LRU is unnecessary unless the cache grows materially or eviction happens on a hot path every frame.
- Error handling is underspecified. Unity distinguishes connection/network failures from protocol-level HTTP failures; 4xx/5xx need explicit handling, including rate limits, auth failures, malformed requests, and server errors.
- A static lazy `ConfigLoader` property without locking or `Lazy<T>` can race on simultaneous first access. If initialization is idempotent and read-only the impact may be low, but API key/config loading should use `Lazy<T>` or static initialization for correctness.

**Codex recommendation**: Keep the architecture, but tighten the async contract: force main-thread entry before Unity API calls, use request ids plus cancellation, create TCS with asynchronous continuations, and classify HTTP status errors explicitly. The cache can stay simple; fix lifecycle and error semantics first.

---

## Gemini

The architecture provides a functional foundation for BCI-driven AAC, but its reliance on legacy `System.Threading.Tasks` in a Unity environment introduces unnecessary overhead and potential deadlocks. The plan lacks robust thread-safety for static initializers and nuanced error handling required for the high-availability demands of assistive technology.

**Key risks identified by Gemini:**
- `TaskCompletionSource` defaults to synchronous continuations; if the `UnityWebRequest` callback triggers a heavy task continuation on the main thread, it can stall the engine or cause deadlocks.
- Reusing a single CTS for both debounce and HTTP logic risks `ObjectDisposedException` if the `LLMClient` attempts to access the token after the `PredictionService` has already cancelled and replaced it.
- The `ConfigLoader` static property lacks synchronization, which may lead to redundant file I/O or multiple initializations if UI and Services initialize simultaneously.
- Failing to distinguish between `ConnectionError` (offline) and `ProtocolError` (HTTP 429 / 401) prevents actionable feedback for users who rely on the interface for basic communication.

**Gemini recommendation**: Replace `Task.Delay` and `TaskCompletionSource` with Unity 6's native `Awaitable` or `UniTask` for zero-allocation, main-thread-safe async. Implement `System.Lazy<string>` for `ConfigLoader`. Transition cache to a proper LRU structure.

*Council chair note on Gemini's recommendation*: The push to `Awaitable`/`UniTask` is reasonable for production at scale but is over-scoped for this deliverable. `Awaitable` is a Unity 6 addition worth noting, but `TaskCompletionSource` is not "legacy" — it is the correct tool for wrapping event-based APIs. The UniTask suggestion is a third-party dependency that adds package management overhead with no correctness benefit at this scale. The LRU suggestion is rejected: linear scan at 100 entries is not a performance issue. These are deferred to a future architecture revision, not action items for the current plan.

---

## Synthesis

### Where All Three Agree

1. **HTTP 4xx/5xx error handling is a genuine gap.** All three reviewers independently flagged that the plan's "network error" path does not distinguish `UnityWebRequest.Result.ProtocolError` from `ConnectionError`. A 429 rate-limit or 401 auth failure currently produces an indistinguishable `Success=false` — this must be fixed.

2. **ConfigLoader thread safety needs `Lazy<T>`.** All three agree the static lazy property without synchronization is a correctness risk. The fix is unambiguous: `private static readonly Lazy<string> _apiKey = new Lazy<string>(LoadKey, LazyThreadSafetyMode.ExecutionAndPublication)`.

3. **`TaskCompletionSource` should use `RunContinuationsAsynchronously`.** All three raised the inline-continuation risk. Creating the TCS as `new TaskCompletionSource<UnityWebRequest>(TaskCreationOptions.RunContinuationsAsynchronously)` eliminates the reentrancy vector.

4. **Cache linear scan at 100 entries is acceptable.** All three explicitly or implicitly agreed this is not a performance concern at current scale.

### Where They Diverge

1. **Severity of the Task.Delay continuation guarantee.** Claude and Codex treat it as a real-but-bounded risk that the `Debug.Assert` guards adequately; Gemini treats it as a fundamental design flaw requiring a full migration to `Awaitable`/`UniTask`. The council sides with Claude/Codex: the risk is real, the guard is correct, and a platform migration is not warranted for this scope.

2. **CancellationToken ObjectDisposedException risk.** Gemini raised `ObjectDisposedException` as a risk if `LLMClient` accesses the token after `PredictionService` disposes the CTS. Claude and Codex did not flag this as a primary concern because the token is passed by value into `LLMClient` before disposal. However, the concern is valid if `LLMClient` holds a reference to the CTS rather than the token — the plan must be explicit that only the `CancellationToken` struct (not the `CancellationTokenSource`) is passed into `LLMClient`. This is worth a clarifying note, not a redesign.

3. **Whether to add a request-generation counter.** Claude and Codex recommend a version/generation counter to prevent stale completions from overwriting fresher results. Gemini did not raise this specifically. The council endorses the generation counter as a concrete action item.

---

## Council Verdict

**APPROVED WITH CHANGES**

The plan is well-structured, Unity 6 correct in its major choices, and appropriate in scope for the internship deliverable. The async pattern (TCS wrapping `UnityWebRequest.completed`), debounce mechanism, cache design, and main-thread marshaling via captured `SynchronizationContext` are all sound. Six targeted changes are required before implementation begins.

### Confidence Level

HIGH — The six action items below are concrete, bounded, and do not require architectural redesign. The core pattern holds.

---

## ACTION ITEMS

Master Coder must implement all six before writing production code. These are non-negotiable pre-conditions, not suggestions.

**ACTION 1 — TCS: use `RunContinuationsAsynchronously`**
Construct all `TaskCompletionSource` instances as:
```csharp
var tcs = new TaskCompletionSource<UnityWebRequest>(
    TaskCreationOptions.RunContinuationsAsynchronously);
```
This prevents inline continuation execution when `tcs.TrySetResult()` is called from the `completed` callback on the main thread, eliminating the reentrancy vector Codex and Gemini both identified.

**ACTION 2 — Add a request-generation counter to prevent stale completions**
In `PredictionService`, maintain `private int _wordRequestGeneration = 0`. Increment on each `RequestWordPrediction` call, capture the current value into `RunDebouncedWordRequest`. Before calling `_syncContext.Post(...)`, check that the captured generation still equals `_wordRequestGeneration`. If not, discard the result silently. This closes the race where a slow response from a cancelled request arrives after a newer request has completed.
```csharp
// In RequestWordPrediction:
int generation = ++_wordRequestGeneration;
_ = RunDebouncedWordRequest(sentence, partial, onComplete, _wordCts.Token, generation);

// In RunDebouncedWordRequest, before Post:
if (generation != _wordRequestGeneration) return;
_syncContext.Post(_ => onComplete(result), null);
```

**ACTION 3 — Handle HTTP ProtocolError explicitly**
In `LLMClient`, after `await tcs.Task`, check `completedRequest.result` against all relevant enum values:
```csharp
if (completedRequest.result == UnityWebRequest.Result.ConnectionError ||
    completedRequest.result == UnityWebRequest.Result.DataProcessingError)
{
    Debug.LogWarning($"[LLMClient] Network error: {completedRequest.error}");
    return new WordPrediction { Success = false };
}
if (completedRequest.result == UnityWebRequest.Result.ProtocolError)
{
    long statusCode = completedRequest.responseCode;
    Debug.LogWarning($"[LLMClient] HTTP {statusCode}: {completedRequest.error}");
    // Surface 401 and 429 distinctly for operational diagnosis:
    if (statusCode == 401) Debug.LogError("[LLMClient] Auth failure — check AnthropicApiKey in config.json");
    if (statusCode == 429) Debug.LogWarning("[LLMClient] Rate limited — backing off");
    return new WordPrediction { Success = false };
}
```
Also add `HttpStatusCode` (or the raw `responseCode`) to the `WordPrediction` / `PhrasePrediction` types as an optional diagnostic field, or log it in the structured prefix format already specified in the plan.

**ACTION 4 — ConfigLoader: replace backing field with `Lazy<T>`**
Replace any manual lazy-init backing field pattern with:
```csharp
private static readonly Lazy<string> _apiKey = new Lazy<string>(
    LoadApiKey,
    LazyThreadSafetyMode.ExecutionAndPublication);

public static string AnthropicApiKey => _apiKey.Value;

private static string LoadApiKey()
{
    var asset = Resources.Load<TextAsset>("config");
    if (asset == null)
        throw new InvalidOperationException("[ConfigLoader] config.json not found in Resources/");
    var cfg = JsonUtility.FromJson<ApiConfig>(asset.text);
    if (string.IsNullOrEmpty(cfg?.anthropic_api_key))
        throw new InvalidOperationException("[ConfigLoader] anthropic_api_key is null or empty in config.json");
    return cfg.anthropic_api_key;
}
```
This is thread-safe by construction and eliminates the double-initialization race.

**ACTION 5 — Assert main-thread entry in LLMClient.PredictWords and PredictPhrases**
The plan mentions this `Debug.Assert` but marks it as a verification step. Elevate it to a required implementation line — the first executable line of both methods:
```csharp
Debug.Assert(
    !System.Threading.Thread.CurrentThread.IsBackground,
    "[LLMClient] PredictWords must be called from the Unity main thread.");
```
This assert is the only guard against the silent failure mode where `SendWebRequest()` is called from a background thread after an unintended `ConfigureAwait(false)` in the call chain.

**ACTION 6 — Pass only `CancellationToken` (not `CancellationTokenSource`) into LLMClient**
Codex and Gemini both raised `ObjectDisposedException` as a risk. The plan's structure is safe IF the `CancellationToken` struct is passed (not the CTS object). Make this explicit in code with a comment:
```csharp
// Pass _wordCts.Token (the struct) — NOT _wordCts itself.
// _wordCts may be disposed and replaced by PredictionService at any time.
// The CancellationToken struct remains valid after its source is disposed;
// it will report IsCancellationRequested=true, which is the correct behavior.
_ = RunDebouncedWordRequest(sentence, partial, onComplete, _wordCts.Token, generation);
```
This is a documentation/comment requirement, not a code structure change — but it must be present to prevent future refactoring from introducing the bug.

---

*The Council is advisory. Final judgment belongs to the user.*
