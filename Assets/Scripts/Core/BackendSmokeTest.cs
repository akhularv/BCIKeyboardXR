// Assets/Scripts/Core/BackendSmokeTest.cs
// Namespace: BCIKeyboardXR.Core
//
// THROWAWAY TEST CODE — remove before production.
// Purpose: Verify end-to-end connectivity: PredictionService → LLMClient → Anthropic API.
//
// HOW TO USE:
//   1. Ensure PredictionService is attached to a persistent GameObject (or already in the scene).
//   2. Attach this script to any empty GameObject in the Main scene.
//   3. Hit Play.
//   4. Observe the Console for "[SmokeTest]" prefixed log lines.
//   5. Expected: two sets of predictions (word + phrase) logged within ~5 seconds.
//
// WARNING: This script makes real network calls to the Anthropic API using the key in config.json.
//          Each test run consumes API credits.
//
// THROWAWAY TEST CODE — remove before production.

using System.Collections;
using BCIKeyboardXR.LLM;
using UnityEngine;

namespace BCIKeyboardXR.Core
{
    /// <summary>
    /// Throwaway smoke test for the BCIKeyboardXR backend layer.
    /// Fires one word prediction and one phrase prediction on scene Start.
    /// Remove this MonoBehaviour before shipping to production.
    /// </summary>
    // THROWAWAY TEST CODE — remove before production.
    public class BackendSmokeTest : MonoBehaviour
    {
        // ---------------------------------------------------------------------------
        // Test inputs — representative of Sarah Martinez's AAC use case
        // ---------------------------------------------------------------------------

        private const string TEST_SENTENCE_WORD = "I need some";
        private const string TEST_PARTIAL_WORD = "wat";
        private const string TEST_SENTENCE_PHRASE = "Please help me";

        // ---------------------------------------------------------------------------
        // MonoBehaviour lifecycle
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Called automatically by Unity on scene Start.
        /// Waits one frame to ensure PredictionService.Awake() has completed,
        /// then fires both prediction types.
        /// </summary>
        // THROWAWAY TEST CODE — remove before production.
        private IEnumerator Start()
        {
            // Wait one frame — guarantees PredictionService.Awake() has run.
            yield return null;

            RunTest();
        }

        // ---------------------------------------------------------------------------
        // Public test hook (Inspector button / external test runner)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fires both word and phrase prediction requests and logs results to the Console.
        /// Can be called from the Inspector or an external test runner.
        /// Requires PredictionService.Instance to be non-null (i.e., Awake() has run).
        /// </summary>
        // THROWAWAY TEST CODE — remove before production.
        public void RunTest()
        {
            // Verify PredictionService is available before calling into it.
            if (PredictionService.Instance == null)
            {
                Debug.LogError(
                    "[SmokeTest] PredictionService.Instance is null. " +
                    "Ensure a PredictionService MonoBehaviour exists in the scene before BackendSmokeTest.");
                return;
            }

            Debug.Log("[SmokeTest] Starting backend smoke test...");
            Debug.Log($"[SmokeTest] Word test  — sentence: '{TEST_SENTENCE_WORD}', partial: '{TEST_PARTIAL_WORD}'");
            Debug.Log($"[SmokeTest] Phrase test — sentence: '{TEST_SENTENCE_PHRASE}'");

            // ---------------------------------------------------------------------------
            // Test 1: Word completion
            // ---------------------------------------------------------------------------
            PredictionService.Instance.RequestWordPrediction(
                sentenceSoFar:       TEST_SENTENCE_WORD,
                currentPartialWord:  TEST_PARTIAL_WORD,
                onComplete: result =>
                {
                    // This callback is guaranteed to run on the Unity main thread.
                    if (result.Success)
                    {
                        Debug.Log(
                            $"[SmokeTest] WORD PREDICTION SUCCESS ({result.Completions.Count} completions):");
                        for (int i = 0; i < result.Completions.Count; i++)
                            Debug.Log($"[SmokeTest]   [{i + 1}] {result.Completions[i]}");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[SmokeTest] WORD PREDICTION FAILED. " +
                            $"HTTP status: {result.HttpStatusCode}. " +
                            "Check Console for [LLMClient] error lines above.");
                    }
                });

            // ---------------------------------------------------------------------------
            // Test 2: Phrase suggestion
            // ---------------------------------------------------------------------------
            PredictionService.Instance.RequestPhrasePrediction(
                sentenceSoFar: TEST_SENTENCE_PHRASE,
                onComplete: result =>
                {
                    // This callback is guaranteed to run on the Unity main thread.
                    if (result.Success)
                    {
                        Debug.Log(
                            $"[SmokeTest] PHRASE PREDICTION SUCCESS ({result.Phrases.Count} phrases):");
                        for (int i = 0; i < result.Phrases.Count; i++)
                            Debug.Log($"[SmokeTest]   [{i + 1}] {result.Phrases[i]}");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[SmokeTest] PHRASE PREDICTION FAILED. " +
                            $"HTTP status: {result.HttpStatusCode}. " +
                            "Check Console for [LLMClient] error lines above.");
                    }
                });

            Debug.Log("[SmokeTest] Requests dispatched. Awaiting callbacks (up to ~5s)...");
        }
    }
}
