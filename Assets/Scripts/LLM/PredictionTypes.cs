// Assets/Scripts/LLM/PredictionTypes.cs
// Namespace: BCIKeyboardXR.LLM
// Responsibility: Plain data transfer objects for LLM prediction results.
//                 No MonoBehaviour. No Unity dependencies. No logic.
//
// These types flow from LLMClient → PredictionService → UI layer (future).
// They are also written to and read from PredictionService's cache.

using System.Collections.Generic;

namespace BCIKeyboardXR.LLM
{
    /// <summary>
    /// Result of a word-completion prediction request.
    /// Returned by <see cref="LLMClient.PredictWords"/> and surfaced through
    /// <see cref="PredictionService.RequestWordPrediction"/>.
    /// </summary>
    public class WordPrediction
    {
        /// <summary>
        /// Ordered list of word completions (up to 6) for the current partial word.
        /// Empty (not null) when <see cref="Success"/> is false.
        /// Consumers must guard against an empty list — do not index without checking Count.
        /// </summary>
        public List<string> Completions = new List<string>();

        /// <summary>
        /// True if the API call succeeded and completions were parsed.
        /// False on network error, timeout, JSON parse failure, or cancellation.
        /// When false, <see cref="Completions"/> is empty and should not be displayed.
        /// </summary>
        public bool Success;

        /// <summary>
        /// HTTP status code from the Anthropic API response, or 0 if no HTTP response
        /// was received (e.g., connection error, timeout, cancellation).
        /// Used for operational diagnostics (401 = auth failure, 429 = rate limit).
        /// Not intended for display to end users.
        /// </summary>
        public long HttpStatusCode;
    }

    /// <summary>
    /// Result of a phrase-suggestion prediction request.
    /// Returned by <see cref="LLMClient.PredictPhrases"/> and surfaced through
    /// <see cref="PredictionService.RequestPhrasePrediction"/>.
    /// </summary>
    public class PhrasePrediction
    {
        /// <summary>
        /// Ordered list of complete phrase suggestions (up to 4) for the current sentence context.
        /// Empty (not null) when <see cref="Success"/> is false.
        /// Consumers must guard against an empty list — do not index without checking Count.
        /// </summary>
        public List<string> Phrases = new List<string>();

        /// <summary>
        /// True if the API call succeeded and phrases were parsed.
        /// False on network error, timeout, JSON parse failure, or cancellation.
        /// When false, <see cref="Phrases"/> is empty and should not be displayed.
        /// </summary>
        public bool Success;

        /// <summary>
        /// HTTP status code from the Anthropic API response, or 0 if no HTTP response
        /// was received (e.g., connection error, timeout, cancellation).
        /// Used for operational diagnostics (401 = auth failure, 429 = rate limit).
        /// Not intended for display to end users.
        /// </summary>
        public long HttpStatusCode;
    }
}
