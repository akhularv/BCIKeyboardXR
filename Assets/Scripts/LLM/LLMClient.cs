// Assets/Scripts/LLM/LLMClient.cs
// Namespace: BCIKeyboardXR.LLM
// Responsibility: Single-responsibility HTTP client for the Anthropic Messages API.
//                 Constructs requests, sends them via UnityWebRequest, parses responses.
//                 Returns typed results — never throws on network/parse failures.
//
// HARD DEPENDENCY: com.unity.nuget.newtonsoft-json 3.2.1
//   Add to Packages/manifest.json under "dependencies":
//     "com.unity.nuget.newtonsoft-json": "3.2.1"
//   If this package is missing, this file will fail to compile with CS0246 on the
//   'using Newtonsoft.Json' line — that is the correct loud failure mode.
//   Do NOT add a conditional compilation guard; see BACKEND_PLAN.md §3 Decision 2.
//
// THREADING CONTRACT:
//   PredictWords and PredictPhrases MUST be called from the Unity main thread.
//   UnityWebRequest.SendWebRequest() must execute on the main thread.
//   See BACKEND_PLAN.md §6 (UnityWebRequest on background threads).
//   Enforced by Debug.Assert at method entry (ACTION 5, Council brief).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BCIKeyboardXR.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace BCIKeyboardXR.LLM
{
    /// <summary>
    /// Non-MonoBehaviour HTTP client for the Anthropic claude-haiku-4-5 Messages API.
    /// Instantiate once and cache in <see cref="PredictionService"/>.
    /// All public methods return typed results (never throw) except
    /// <see cref="OperationCanceledException"/> when the caller's token is cancelled.
    /// </summary>
    public class LLMClient
    {
        // ---------------------------------------------------------------------------
        // Constants — no magic numbers anywhere in this file
        // ---------------------------------------------------------------------------

        /// <summary>Anthropic Messages API endpoint.</summary>
        private const string ENDPOINT = "https://api.anthropic.com/v1/messages";

        /// <summary>Required anthropic-version header value.</summary>
        private const string API_VERSION = "2023-06-01";

        /// <summary>Model identifier for all requests.</summary>
        private const string MODEL_NAME = "claude-haiku-4-5";

        /// <summary>max_tokens for word completion requests (short JSON array).</summary>
        private const int WORD_MAX_TOKENS = 200;

        /// <summary>max_tokens for phrase suggestion requests (4 full sentences).</summary>
        private const int PHRASE_MAX_TOKENS = 400;

        /// <summary>Per-request HTTP timeout in seconds, enforced via linked CancellationToken.</summary>
        private const int TIMEOUT_SECONDS = 5;

        // Base system instruction prepended before the user profile section.
        private const string BASE_SYSTEM_INSTRUCTION =
            "You are a word-prediction and phrase-suggestion engine embedded in an AAC keyboard. " +
            "You output ONLY valid JSON — no prose, no markdown, no preamble. " +
            "Here is the profile of the user you are assisting:\n\n";

        private const string PHRASE_CONTINUATION_INSTRUCTION =
            "\n\nWhen generating phrase continuations, your output is appended verbatim to " +
            "Sarah's typed text with one space separator. Do not include leading " +
            "punctuation, leading spaces, or repeat any of Sarah's already-typed words.";

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Initializes a new <see cref="LLMClient"/> instance.
        /// Lightweight — no network calls, no file I/O.
        /// </summary>
        public LLMClient()
        {
            // Nothing to initialize. ConfigLoader and UserProfile are accessed lazily
            // at request time, ensuring they are loaded before first use.
        }

        /// <summary>
        /// Requests up to 6 word completions for <paramref name="currentPartialWord"/>
        /// in the context of <paramref name="sentenceSoFar"/>.
        /// Returns <see cref="WordPrediction"/> with <c>Success=false</c> on any failure.
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> is cancelled.
        /// </summary>
        /// <param name="sentenceSoFar">
        /// The sentence typed so far, already normalized (trimmed, lowercased) by PredictionService.
        /// </param>
        /// <param name="currentPartialWord">
        /// The partial word being typed, already normalized by PredictionService.
        /// </param>
        /// <param name="ct">
        /// Cancellation token. Pass <c>_wordCts.Token</c> (the struct), NOT the CTS object.
        /// See ACTION 6 (Council brief) and PredictionService for the ownership rationale.
        /// </param>
        /// <returns>
        /// <see cref="WordPrediction"/> with populated <c>Completions</c> list on success,
        /// or <c>Success=false</c> with empty list on failure.
        /// </returns>
        public async Task<WordPrediction> PredictWords(
            string sentenceSoFar,
            string currentPartialWord,
            CancellationToken ct)
        {
            // ACTION 5 (Council): Assert main-thread entry. This is the canary for the silent
            // failure mode where ConfigureAwait(false) somewhere in the call chain shifts
            // execution to a background thread before SendWebRequest() is called.
            Debug.Assert(
                !Thread.CurrentThread.IsBackground,
                "[LLMClient] PredictWords must be called from the Unity main thread. " +
                "Check for ConfigureAwait(false) leaking from upstream awaits in PredictionService.");

            string userMessage = string.IsNullOrWhiteSpace(currentPartialWord)
                ? $"Sarah has typed: '{sentenceSoFar}'. Suggest the 6 most likely next words " +
                  "to continue the sentence. Each must be a single word, no punctuation. " +
                  "Output ONLY JSON: " +
                  "{\"completions\": [\"word1\", \"word2\", \"word3\", \"word4\", \"word5\", \"word6\"]}"
                : $"Sarah is typing. Sentence so far: '{sentenceSoFar}'. " +
                  $"Partial word: '{currentPartialWord}'. Suggest 6 completions for the partial " +
                  "word that continue the sentence naturally. Output ONLY JSON: " +
                  "{\"completions\": [\"word1\", \"word2\", \"word3\", \"word4\", \"word5\", \"word6\"]}";

            string systemPrompt = BASE_SYSTEM_INSTRUCTION + UserProfile.GetSystemPromptSection() + PHRASE_CONTINUATION_INSTRUCTION;

            string rawResponse;
            long httpStatus;

            try
            {
                (rawResponse, httpStatus) = await PostToAnthropicAsync(
                    systemPrompt, userMessage, WORD_MAX_TOKENS, ct);
            }
            catch (OperationCanceledException)
            {
                // Propagate cancellation — PredictionService's debounce depends on this.
                throw;
            }

            if (rawResponse == null)
            {
                // PostToAnthropicAsync already logged the specific failure reason.
                return new WordPrediction { Success = false, HttpStatusCode = httpStatus };
            }

            return ParseWordPrediction(rawResponse, httpStatus);
        }

        /// <summary>
        /// Requests up to 4 complete phrase suggestions given <paramref name="sentenceSoFar"/>.
        /// Returns <see cref="PhrasePrediction"/> with <c>Success=false</c> on any failure.
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> is cancelled.
        /// </summary>
        /// <param name="sentenceSoFar">
        /// The sentence typed so far, already normalized (trimmed, lowercased) by PredictionService.
        /// </param>
        /// <param name="ct">
        /// Cancellation token. Pass the token struct, not the CancellationTokenSource object.
        /// </param>
        /// <returns>
        /// <see cref="PhrasePrediction"/> with populated <c>Phrases</c> list on success,
        /// or <c>Success=false</c> with empty list on failure.
        /// </returns>
        public async Task<PhrasePrediction> PredictPhrases(
            string sentenceSoFar,
            CancellationToken ct)
        {
            // ACTION 5 (Council): Same main-thread assertion as PredictWords.
            Debug.Assert(
                !Thread.CurrentThread.IsBackground,
                "[LLMClient] PredictPhrases must be called from the Unity main thread. " +
                "Check for ConfigureAwait(false) leaking from upstream awaits in PredictionService.");

            string userMessage = string.IsNullOrWhiteSpace(sentenceSoFar)
                ? "Sarah is starting a new message. Suggest 4 short phrases (3-6 words each) " +
                  "she might want to say, based on her profile. Output ONLY JSON, no " +
                  "preamble, no markdown fences: " +
                  "{\"phrases\": [\"phrase1\", \"phrase2\", \"phrase3\", \"phrase4\"]}"
                : $"Sarah has typed: '{sentenceSoFar}'. Suggest 4 short continuations (3-6 " +
                  "words each) that grammatically extend this sentence. Each continuation " +
                  "should read naturally when appended directly after what she has typed. " +
                  "Do NOT repeat any words from what she has already typed. Output ONLY " +
                  "JSON, no preamble, no markdown fences: " +
                  "{\"phrases\": [\"continuation1\", \"continuation2\", \"continuation3\", \"continuation4\"]}";

            string systemPrompt = BASE_SYSTEM_INSTRUCTION + UserProfile.GetSystemPromptSection() + PHRASE_CONTINUATION_INSTRUCTION;

            string rawResponse;
            long httpStatus;

            try
            {
                (rawResponse, httpStatus) = await PostToAnthropicAsync(
                    systemPrompt, userMessage, PHRASE_MAX_TOKENS, ct);
            }
            catch (OperationCanceledException)
            {
                // Propagate cancellation — callers in PredictionService handle this.
                throw;
            }

            if (rawResponse == null)
            {
                return new PhrasePrediction { Success = false, HttpStatusCode = httpStatus };
            }

            return ParsePhrasePrediction(rawResponse, httpStatus);
        }

        // ---------------------------------------------------------------------------
        // Core HTTP method
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Posts a request to the Anthropic Messages API and returns the text content
        /// of the first content block, or null on any failure.
        /// All error handling (network, HTTP, JSON) is contained here.
        /// </summary>
        /// <returns>
        /// (responseText, httpStatusCode) where responseText is null on failure.
        /// httpStatusCode is 0 if no HTTP response was received.
        /// </returns>
        private async Task<(string responseText, long httpStatus)> PostToAnthropicAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens,
            CancellationToken ct)
        {
            // Build the Anthropic Messages API request body.
            // Shape verified against API docs: model, max_tokens, system, messages[].
            var requestBody = new
            {
                model = MODEL_NAME,
                max_tokens = maxTokens,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            // -----------------------------------------------------------------------
            // BACKEND_PLAN §3 Decision 5 / ACTION 1 (Council):
            // UnityWebRequest async pattern using TaskCompletionSource.
            //
            // Create TCS with RunContinuationsAsynchronously to prevent the completed
            // callback (which fires on the Unity main thread) from running continuations
            // inline — that would be a reentrancy bug since we're already on the main thread.
            // -----------------------------------------------------------------------

            // Link the caller's token with a hard 5-second timeout token.
            // Using 'using' ensures both are disposed when this method exits.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            // Capture the linked token struct NOW — before any await.
            // The CancellationToken struct is a value type; it remains valid even if
            // linkedCts is disposed, and will correctly report IsCancellationRequested=true.
            CancellationToken linkedToken = linkedCts.Token;

            // Construct and configure the request synchronously on the main thread.
            // SendWebRequest() must be called before any await — see BACKEND_PLAN §6.
            using var request = new UnityWebRequest(ENDPOINT, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("x-api-key", ConfigLoader.AnthropicApiKey);
            request.SetRequestHeader("anthropic-version", API_VERSION);
            request.SetRequestHeader("content-type", "application/json");

            // ACTION 1 (Council): TaskCreationOptions.RunContinuationsAsynchronously
            // prevents the completed callback from running continuations synchronously
            // on the main thread, eliminating the reentrancy vector.
            var tcs = new TaskCompletionSource<UnityWebRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Register cancellation: abort the request and signal the TCS.
            // The lambda captures linkedToken (struct) — safe even if linkedCts is disposed.
            linkedToken.Register(() =>
            {
                request.Abort();
                tcs.TrySetCanceled();
            });

            // Send the request. This call is synchronous up to here — safe on main thread.
            var operation = request.SendWebRequest();

            // Wire the completion callback. This fires on the Unity main thread when the
            // response arrives (or when the request fails/is aborted).
            operation.completed += _ =>
            {
                // Guard against use-after-dispose: if cancellation already fired (TrySetCanceled
                // won the race and PostToAnthropicAsync exited its using scope, disposing request),
                // the TCS task is already completed — do not touch the disposed UnityWebRequest.
                if (tcs.Task.IsCompleted)
                    return;
                if (!linkedToken.IsCancellationRequested)
                    tcs.TrySetResult(request);
            };

            // Await the TCS. Execution suspends here; UnityWebRequest runs in Unity's
            // internal update loop. Continuation resumes on UnitySynchronizationContext
            // (main thread) because RunContinuationsAsynchronously schedules through it.
            UnityWebRequest completedRequest;
            try
            {
                completedRequest = await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                // Caller's token was cancelled (debounce reset or timeout).
                // Propagate so PredictionService can handle it correctly.
                throw;
            }

            long statusCode = completedRequest.responseCode;

            // -----------------------------------------------------------------------
            // ACTION 3 (Council): Explicit handling for each UnityWebRequest.Result
            // enum value. ProtocolError (HTTP 4xx/5xx) is distinct from ConnectionError.
            // -----------------------------------------------------------------------

            if (completedRequest.result == UnityWebRequest.Result.ConnectionError ||
                completedRequest.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Network-level failure — no HTTP response received.
                Debug.LogWarning(
                    $"[LLMClient] Network/data error: {completedRequest.error}");
                return (null, 0);
            }

            if (completedRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                // HTTP-level failure — server responded with 4xx/5xx.
                Debug.LogWarning(
                    $"[LLMClient] HTTP {statusCode}: {completedRequest.error}");

                // Surface operationally important status codes with specific messages
                // so developers can diagnose auth and rate-limit issues immediately.
                if (statusCode == 401)
                    Debug.LogError("[LLMClient] Auth failure — check AnthropicApiKey in config.json");
                if (statusCode == 429)
                    Debug.LogWarning("[LLMClient] Rate limited — backing off");

                return (null, statusCode);
            }

            // UnityWebRequest.Result.Success — parse the response body.
            string responseText = completedRequest.downloadHandler?.text;

            if (string.IsNullOrEmpty(responseText))
            {
                Debug.LogWarning("[LLMClient] Empty response body from Anthropic API.");
                return (null, statusCode);
            }

            // Extract content[0].text from the Anthropic Messages response envelope.
            // The full response shape is:
            //   { "content": [{ "type": "text", "text": "<LLM output>" }], ... }
            string contentText = ExtractContentText(responseText);

            if (contentText == null)
            {
                Debug.LogWarning(
                    $"[LLMClient] Could not extract content[0].text from response. " +
                    $"Raw (first 200): {Truncate(responseText, 200)}");
                return (null, statusCode);
            }

            return (contentText, statusCode);
        }

        // ---------------------------------------------------------------------------
        // Response parsing helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Extracts <c>content[0].text</c> from the Anthropic Messages API response envelope.
        /// Returns null if the structure is unexpected or parsing fails.
        /// </summary>
        private static string ExtractContentText(string rawResponse)
        {
            try
            {
                // Use a lightweight anonymous type for the outer envelope only.
                var envelope = JsonConvert.DeserializeAnonymousType(rawResponse,
                    new
                    {
                        content = new[]
                        {
                            new { type = "", text = "" }
                        }
                    });

                if (envelope?.content == null || envelope.content.Length == 0)
                    return null;

                return envelope.content[0].text;
            }
            catch (JsonException ex)
            {
                Debug.LogWarning(
                    $"[LLMClient] Failed to parse Anthropic envelope: {ex.Message}. " +
                    $"Raw (first 200): {Truncate(rawResponse, 200)}");
                return null;
            }
        }

        /// <summary>
        /// Parses a word-prediction JSON string (possibly wrapped in markdown fences)
        /// into a <see cref="WordPrediction"/>. Returns <c>Success=false</c> on any parse failure.
        /// </summary>
        private static WordPrediction ParseWordPrediction(string contentText, long httpStatus)
        {
            string json = StripMarkdownFences(contentText);

            try
            {
                // Expected shape: { "completions": ["word1", "word2", ...] }
                var parsed = JsonConvert.DeserializeAnonymousType(json,
                    new { completions = new List<string>() });

                if (parsed?.completions == null)
                {
                    Debug.LogWarning(
                        $"[LLMClient] Word prediction JSON missing 'completions' key. " +
                        $"Raw (first 200): {Truncate(json, 200)}");
                    return new WordPrediction { Success = false, HttpStatusCode = httpStatus };
                }

                return new WordPrediction
                {
                    Completions = parsed.completions,
                    Success = true,
                    HttpStatusCode = httpStatus
                };
            }
            catch (JsonException ex)
            {
                // Defensive: never throw from JSON parsing — return Success=false.
                Debug.LogWarning(
                    $"[LLMClient] Failed to parse word prediction JSON: {ex.Message}. " +
                    $"Raw (first 200): {Truncate(json, 200)}");
                return new WordPrediction { Success = false, HttpStatusCode = httpStatus };
            }
        }

        /// <summary>
        /// Parses a phrase-prediction JSON string (possibly wrapped in markdown fences)
        /// into a <see cref="PhrasePrediction"/>. Returns <c>Success=false</c> on any parse failure.
        /// </summary>
        private static PhrasePrediction ParsePhrasePrediction(string contentText, long httpStatus)
        {
            string json = StripMarkdownFences(contentText);

            try
            {
                // Expected shape: { "phrases": ["phrase1", "phrase2", ...] }
                var parsed = JsonConvert.DeserializeAnonymousType(json,
                    new { phrases = new List<string>() });

                if (parsed?.phrases == null)
                {
                    Debug.LogWarning(
                        $"[LLMClient] Phrase prediction JSON missing 'phrases' key. " +
                        $"Raw (first 200): {Truncate(json, 200)}");
                    return new PhrasePrediction { Success = false, HttpStatusCode = httpStatus };
                }

                return new PhrasePrediction
                {
                    Phrases = parsed.phrases,
                    Success = true,
                    HttpStatusCode = httpStatus
                };
            }
            catch (JsonException ex)
            {
                // Defensive: never throw from JSON parsing — return Success=false.
                Debug.LogWarning(
                    $"[LLMClient] Failed to parse phrase prediction JSON: {ex.Message}. " +
                    $"Raw (first 200): {Truncate(json, 200)}");
                return new PhrasePrediction { Success = false, HttpStatusCode = httpStatus };
            }
        }

        // ---------------------------------------------------------------------------
        // Utility helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Strips markdown code fences from a JSON string if present.
        /// claude-haiku-4-5 occasionally wraps JSON in triple-backtick blocks despite
        /// being instructed not to. This is the defensive parsing step from BACKEND_PLAN §6.
        /// </summary>
        /// <remarks>
        /// Strips fences in this order:
        /// 1. If the string contains ```, extract text between first ``` (with or without 'json' label)
        ///    and the last ```.
        /// 2. Trim whitespace.
        /// 3. If no fences, return trimmed original.
        /// </remarks>
        private static string StripMarkdownFences(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Check for presence of triple-backtick fence
            int firstFence = input.IndexOf("```", StringComparison.Ordinal);
            if (firstFence < 0)
                return input.Trim();

            // Find the end of the opening fence line (handles ```json\n or ```\n)
            int contentStart = input.IndexOf('\n', firstFence);
            if (contentStart < 0)
            {
                // Malformed: opening fence but no newline — try to extract anyway
                contentStart = firstFence + 3;
            }
            else
            {
                contentStart += 1; // skip the newline character itself
            }

            // Find the last occurrence of ``` as the closing fence
            int lastFence = input.LastIndexOf("```", StringComparison.Ordinal);

            // Ensure the last fence is different from the opening fence position
            if (lastFence <= firstFence)
            {
                // Degenerate case: only one fence marker — take everything after it
                return input.Substring(contentStart).Trim();
            }

            return input.Substring(contentStart, lastFence - contentStart).Trim();
        }

        /// <summary>
        /// Truncates a string to <paramref name="maxLength"/> characters for safe log output.
        /// Returns the original string if it is already within the limit.
        /// </summary>
        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength)
                return s;
            return s.Substring(0, maxLength) + "...";
        }
    }
}
