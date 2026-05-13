// Assets/Scripts/LLM/PredictionService.cs
// Namespace: BCIKeyboardXR.LLM
// Responsibility: MonoBehaviour singleton that exposes word and phrase prediction to Unity callers.
//                 Owns: debounce, cache, main-thread marshaling, structured logging.
//                 Does NOT own: HTTP logic (LLMClient), API key (ConfigLoader), user profile (UserProfile).
//
// THREADING MODEL:
//   All public methods (RequestWordPrediction, RequestPhrasePrediction) must be called from
//   the Unity main thread. SynchronizationContext captured in Awake() ensures all callbacks
//   are delivered back to the main thread via _syncContext.Post().
//
// DEBOUNCE:
//   Word requests: 150ms debounce via Task.Delay + CancellationToken cancel-and-restart.
//   Phrase requests: no debounce — triggered at sentence boundary, not per-keystroke.
//
// CACHE:
//   Up to MAX_CACHE_ENTRIES entries with CACHE_TTL_SECONDS TTL.
//   Eviction on insert: remove expired entries first; if still full, remove oldest.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BCIKeyboardXR.Core;
using UnityEngine;

namespace BCIKeyboardXR.LLM
{
    /// <summary>
    /// MonoBehaviour singleton. Exposes word-completion and phrase-suggestion prediction
    /// to the rest of the application. All callbacks delivered on the Unity main thread.
    /// Attach to a persistent GameObject in the Main scene (DontDestroyOnLoad applies).
    /// </summary>
    public class PredictionService : MonoBehaviour
    {
        // ---------------------------------------------------------------------------
        // Constants — no magic numbers anywhere in this file
        // ---------------------------------------------------------------------------

        /// <summary>Debounce window in milliseconds for word prediction requests.</summary>
        private const int DEBOUNCE_MS = 150;

        /// <summary>Cache TTL in seconds — entries older than this are treated as misses.</summary>
        private const float CACHE_TTL_SECONDS = 60f;

        /// <summary>Maximum number of cache entries before eviction runs.</summary>
        private const int MAX_CACHE_ENTRIES = 100;

        // Sentinel string that differentiates phrase cache keys from word cache keys.
        // Prevents collision when sentenceSoFar is identical and partialWord is empty.
        private const string PHRASE_KEY_SENTINEL = "__phrase__";

        // ---------------------------------------------------------------------------
        // Singleton
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Singleton accessor. Returns null if the service has not yet been initialized.
        /// Do not cache this reference across frames — use Instance each time.
        /// </summary>
        public static PredictionService Instance { get; private set; }

        // ---------------------------------------------------------------------------
        // Dependencies
        // ---------------------------------------------------------------------------

        // LLMClient is instantiated once in Awake and reused for all requests.
        private LLMClient _client;

        // SynchronizationContext captured in Awake() — guaranteed to be Unity's main-thread
        // context because Unity calls Awake() on the main thread.
        private SynchronizationContext _syncContext;

        // ---------------------------------------------------------------------------
        // Word prediction debounce state (ACTION 2, Council)
        // ---------------------------------------------------------------------------

        // CancellationTokenSource for the current in-flight (or debouncing) word request.
        // Cancelled and replaced on each new RequestWordPrediction call.
        private CancellationTokenSource _wordCts;

        // ACTION 2 (Council): Generation counter to discard stale completions.
        // Incremented on each RequestWordPrediction call.
        // RunDebouncedWordRequest captures the generation at entry and checks it before Post().
        private int _wordRequestGeneration = 0;

        // ---------------------------------------------------------------------------
        // Phrase prediction cancellation state
        // ---------------------------------------------------------------------------

        // CancellationTokenSource for the current in-flight phrase request.
        // Phrase requests are not debounced but are still cancellable.
        private CancellationTokenSource _phraseCts;

        // ---------------------------------------------------------------------------
        // Cache
        // ---------------------------------------------------------------------------

        // Cache key → CacheEntry (result object + insertion timestamp).
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        /// <summary>
        /// A single cache entry holding a prediction result and its insertion time.
        /// Both WordPrediction and PhrasePrediction are stored as <c>object</c> to share one cache.
        /// </summary>
        private struct CacheEntry
        {
            /// <summary>The cached prediction result (WordPrediction or PhrasePrediction).</summary>
            public object Result;

            /// <summary>UTC timestamp when this entry was inserted.</summary>
            public DateTime Timestamp;
        }

        // ---------------------------------------------------------------------------
        // MonoBehaviour lifecycle
        // ---------------------------------------------------------------------------

        private void Awake()
        {
            // Singleton enforcement — destroy duplicates, not the first instance.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[PredictionService] Duplicate instance detected — destroying new one. " +
                    "Ensure only one PredictionService exists in the scene.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persist across scene loads — AAC use requires continuous availability.
            DontDestroyOnLoad(gameObject);

            // Capture the Unity SynchronizationContext while guaranteed on the main thread.
            // _syncContext.Post() is used for all onComplete callbacks.
            // NOTE: Do NOT use _syncContext.Send() — it blocks the calling thread and can deadlock.
            _syncContext = SynchronizationContext.Current;

            if (_syncContext == null)
            {
                // This should never happen if Awake() runs on the main thread, but log defensively.
                Debug.LogError(
                    "[PredictionService] SynchronizationContext.Current is null in Awake(). " +
                    "Callbacks will not be marshaled to the main thread.");
            }

            // Initialize dependencies.
            UserProfile.Initialize();
            _client = new LLMClient();

            Debug.Log("[PredictionService] Initialized.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            // Cancel and dispose any in-flight requests to prevent callbacks
            // firing after this MonoBehaviour has been destroyed.
            _wordCts?.Cancel();
            _wordCts?.Dispose();
            _wordCts = null;

            _phraseCts?.Cancel();
            _phraseCts?.Dispose();
            _phraseCts = null;
        }

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fire-and-forget word completion request with 150ms debounce.
        /// Cancels any previous in-flight word request.
        /// <paramref name="onComplete"/> is always called on the Unity main thread.
        /// <paramref name="onComplete"/> receives <c>Success=false</c> if the request fails or is superseded.
        /// </summary>
        /// <param name="sentenceSoFar">The sentence typed so far. May be empty or null (treated as empty).</param>
        /// <param name="currentPartialWord">The partial word being completed. May be empty or null.</param>
        /// <param name="onComplete">Callback invoked on the main thread with the prediction result.</param>
        public void RequestWordPrediction(
            string sentenceSoFar,
            string currentPartialWord,
            Action<WordPrediction> onComplete)
        {
            // Catch background-thread callers at the earliest possible point.
            // All Unity API usage in this call chain requires the main thread.
            Debug.Assert(
                !System.Threading.Thread.CurrentThread.IsBackground,
                "[PredictionService] RequestWordPrediction must be called from the Unity main thread.");

            if (onComplete == null)
            {
                Debug.LogWarning("[PredictionService] RequestWordPrediction called with null onComplete — ignoring.");
                return;
            }

            // Normalize inputs before key construction and API calls.
            // Prevents near-duplicate requests from trailing spaces or case differences.
            string normalizedSentence = (sentenceSoFar ?? string.Empty).Trim().ToLowerInvariant();
            string normalizedPartial = (currentPartialWord ?? string.Empty).Trim().ToLowerInvariant();

            // Check cache before debouncing — a cache HIT bypasses all async work.
            string cacheKey = MakeWordKey(normalizedSentence, normalizedPartial);
            if (TryGetCachedWord(cacheKey, out WordPrediction cached))
            {
                string ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                Debug.Log(
                    $"[PredictionService] {ts} | Word | " +
                    $"input_len={(normalizedSentence + normalizedPartial).Length} | " +
                    "latency=0ms | cache=HIT");

                // Deliver cached result on main thread via Post (not Send — see Awake() note).
                // If _syncContext is null (LogError fires in Awake), invoke directly on current thread
                // rather than silently dropping the callback and leaving the caller hanging.
                if (_syncContext != null)
                    _syncContext.Post(_ => onComplete(cached), null);
                else
                    onComplete(cached);
                return;
            }

            // Cancel and replace the previous word CTS.
            _wordCts?.Cancel();
            _wordCts?.Dispose();
            _wordCts = new CancellationTokenSource();

            // ACTION 2 (Council): Increment generation counter and capture current value.
            // This generation is checked in RunDebouncedWordRequest before calling Post().
            int generation = ++_wordRequestGeneration;

            // ACTION 6 (Council): Pass _wordCts.Token (the struct) — NOT _wordCts itself.
            // _wordCts may be disposed and replaced by PredictionService at any time.
            // The CancellationToken struct remains valid after its source is disposed.
            _ = RunDebouncedWordRequest(
                normalizedSentence, normalizedPartial, cacheKey, onComplete,
                _wordCts.Token, generation);
        }

        /// <summary>
        /// Fire-and-forget phrase suggestion request. No debounce.
        /// Does NOT cancel in-flight word requests.
        /// <paramref name="onComplete"/> is always called on the Unity main thread.
        /// <paramref name="onComplete"/> receives <c>Success=false</c> if the request fails.
        /// </summary>
        /// <param name="sentenceSoFar">The sentence typed so far. May be empty or null (treated as empty).</param>
        /// <param name="onComplete">Callback invoked on the main thread with the prediction result.</param>
        public void RequestPhrasePrediction(
            string sentenceSoFar,
            Action<PhrasePrediction> onComplete)
        {
            Debug.Assert(
                !System.Threading.Thread.CurrentThread.IsBackground,
                "[PredictionService] RequestPhrasePrediction must be called from the Unity main thread.");

            if (onComplete == null)
            {
                Debug.LogWarning("[PredictionService] RequestPhrasePrediction called with null onComplete — ignoring.");
                return;
            }

            string normalizedSentence = (sentenceSoFar ?? string.Empty).Trim().ToLowerInvariant();

            // Check cache.
            string cacheKey = MakePhraseKey(normalizedSentence);
            if (TryGetCachedPhrase(cacheKey, out PhrasePrediction cached))
            {
                string ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                Debug.Log(
                    $"[PredictionService] {ts} | Phrase | " +
                    $"input_len={normalizedSentence.Length} | " +
                    "latency=0ms | cache=HIT");

                if (_syncContext != null)
                    _syncContext.Post(_ => onComplete(cached), null);
                else
                    onComplete(cached);
                return;
            }

            // Cancel any previous in-flight phrase request (no debounce, but cancel old one).
            _phraseCts?.Cancel();
            _phraseCts?.Dispose();
            _phraseCts = new CancellationTokenSource();

            // ACTION 6 (Council): Pass the token struct, not the CTS object.
            _ = RunPhraseRequest(normalizedSentence, cacheKey, onComplete, _phraseCts.Token);
        }

        // ---------------------------------------------------------------------------
        // Async workers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Async worker for word prediction. Applies debounce delay then calls LLMClient.
        /// OperationCanceledException from Task.Delay is caught here and logged at Debug level —
        /// it is normal control flow (user typed another key), not an error.
        /// </summary>
        private async Task RunDebouncedWordRequest(
            string sentenceSoFar,
            string partialWord,
            string cacheKey,
            Action<WordPrediction> onComplete,
            CancellationToken ct,
            int generation)
        {
            var requestStart = DateTime.UtcNow;

            try
            {
                // Debounce: wait 150ms. If the user types another key, ct is cancelled,
                // Task.Delay throws OperationCanceledException, and this task exits.
                await Task.Delay(DEBOUNCE_MS, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal debounce cancellation — a newer request superseded this one.
                // Log at Debug level (not LogError) — this is expected control flow.
                Debug.Log(
                    $"[PredictionService] Word request debounce cancelled (generation {generation}).");
                return;
            }

            // After the debounce delay, check if a newer request has already started.
            // ACTION 2 (Council): generation guard — discard stale completions.
            if (generation != _wordRequestGeneration)
            {
                Debug.Log(
                    $"[PredictionService] Word request generation {generation} superseded " +
                    $"by {_wordRequestGeneration} — discarding.");
                return;
            }

            // Call LLMClient. This must execute on the Unity main thread (asserted inside LLMClient).
            // Task.Delay awaited on the main thread resumes on UnitySynchronizationContext,
            // so we are still on the main thread here — the Debug.Assert in LLMClient will pass.
            WordPrediction result;
            try
            {
                result = await _client.PredictWords(sentenceSoFar, partialWord, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancellation during the HTTP request — silently discard.
                Debug.Log(
                    $"[PredictionService] Word HTTP request cancelled (generation {generation}).");
                return;
            }
            catch (Exception ex)
            {
                // Unexpected exception from LLMClient — log and return failure.
                // PredictionService must never crash Unity, even on unexpected errors.
                Debug.LogError(
                    $"[PredictionService] Unexpected error in word prediction: {ex.Message}");
                result = new WordPrediction { Success = false };
            }

            // Write successful results to cache (do not cache failures).
            if (result.Success)
                WriteToCache(cacheKey, result);

            // ACTION 2 (Council): Final generation check before dispatching callback.
            // Closes the race where a slow cancelled request completes after a newer one.
            if (generation != _wordRequestGeneration)
            {
                Debug.Log(
                    $"[PredictionService] Word result for generation {generation} discarded " +
                    $"(current generation: {_wordRequestGeneration}).");
                return;
            }

            long latencyMs = (long)(DateTime.UtcNow - requestStart).TotalMilliseconds;
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            Debug.Log(
                $"[PredictionService] {timestamp} | Word | " +
                $"input_len={(sentenceSoFar + partialWord).Length} | " +
                $"latency={latencyMs}ms | cache=MISS");

            // Dispatch to main thread. _syncContext was captured in Awake() on the main thread.
            if (_syncContext != null)
                _syncContext.Post(_ => onComplete(result), null);
            else
                onComplete(result);
        }

        /// <summary>
        /// Async worker for phrase prediction. No debounce.
        /// OperationCanceledException is caught and silently discarded.
        /// </summary>
        private async Task RunPhraseRequest(
            string sentenceSoFar,
            string cacheKey,
            Action<PhrasePrediction> onComplete,
            CancellationToken ct)
        {
            var requestStart = DateTime.UtcNow;

            PhrasePrediction result;
            try
            {
                result = await _client.PredictPhrases(sentenceSoFar, ct);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[PredictionService] Phrase HTTP request cancelled.");
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[PredictionService] Unexpected error in phrase prediction: {ex.Message}");
                result = new PhrasePrediction { Success = false };
            }

            if (result.Success)
                WriteToCache(cacheKey, result);

            long latencyMs = (long)(DateTime.UtcNow - requestStart).TotalMilliseconds;
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            Debug.Log(
                $"[PredictionService] {timestamp} | Phrase | " +
                $"input_len={sentenceSoFar.Length} | " +
                $"latency={latencyMs}ms | cache=MISS");

            if (_syncContext != null)
                _syncContext.Post(_ => onComplete(result), null);
            else
                onComplete(result);
        }

        // ---------------------------------------------------------------------------
        // Cache helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Constructs the cache key for a word prediction request.
        /// Both inputs must already be normalized (trimmed, lowercased) by the caller.
        /// </summary>
        private static string MakeWordKey(string sentence, string partial) =>
            $"{sentence}|{partial}";

        /// <summary>
        /// Constructs the cache key for a phrase prediction request.
        /// The PHRASE_KEY_SENTINEL prevents collisions with word keys when partial is empty.
        /// </summary>
        private static string MakePhraseKey(string sentence) =>
            $"{sentence}|{PHRASE_KEY_SENTINEL}";

        /// <summary>
        /// Attempts to retrieve a valid (non-expired) <see cref="WordPrediction"/> from cache.
        /// Returns false if the key is absent or the entry has expired (TTL exceeded).
        /// </summary>
        private bool TryGetCachedWord(string key, out WordPrediction result)
        {
            result = null;

            if (!_cache.TryGetValue(key, out CacheEntry entry))
                return false;

            // Treat expired entries as cache misses — do not serve stale predictions.
            if ((DateTime.UtcNow - entry.Timestamp).TotalSeconds > CACHE_TTL_SECONDS)
            {
                _cache.Remove(key);
                return false;
            }

            result = entry.Result as WordPrediction;
            return result != null;
        }

        /// <summary>
        /// Attempts to retrieve a valid (non-expired) <see cref="PhrasePrediction"/> from cache.
        /// Returns false if the key is absent or the entry has expired.
        /// </summary>
        private bool TryGetCachedPhrase(string key, out PhrasePrediction result)
        {
            result = null;

            if (!_cache.TryGetValue(key, out CacheEntry entry))
                return false;

            if ((DateTime.UtcNow - entry.Timestamp).TotalSeconds > CACHE_TTL_SECONDS)
            {
                _cache.Remove(key);
                return false;
            }

            result = entry.Result as PhrasePrediction;
            return result != null;
        }

        /// <summary>
        /// Writes a prediction result to the cache with the current timestamp.
        /// Runs eviction if the cache is at or above <see cref="MAX_CACHE_ENTRIES"/>.
        /// </summary>
        private void WriteToCache(string key, object result)
        {
            if (_cache.Count >= MAX_CACHE_ENTRIES)
            {
                // Eviction step 1: remove all entries older than CACHE_TTL_SECONDS.
                // Collect keys first to avoid modifying the dictionary during enumeration.
                var now = DateTime.UtcNow;
                var expiredKeys = new List<string>(_cache.Count);

                foreach (var kv in _cache)
                {
                    if ((now - kv.Value.Timestamp).TotalSeconds > CACHE_TTL_SECONDS)
                        expiredKeys.Add(kv.Key);
                }

                foreach (var k in expiredKeys)
                    _cache.Remove(k);

                // Eviction step 2: if still at or above capacity, remove the oldest entry.
                // Linear scan at 100-entry scale is acceptable (BACKEND_PLAN §3 Decision 4).
                if (_cache.Count >= MAX_CACHE_ENTRIES)
                {
                    string oldestKey = null;
                    DateTime oldestTime = DateTime.MaxValue;

                    foreach (var kv in _cache)
                    {
                        if (kv.Value.Timestamp < oldestTime)
                        {
                            oldestTime = kv.Value.Timestamp;
                            oldestKey = kv.Key;
                        }
                    }

                    if (oldestKey != null)
                        _cache.Remove(oldestKey);
                }
            }

            _cache[key] = new CacheEntry
            {
                Result = result,
                Timestamp = DateTime.UtcNow
            };
        }
    }
}
