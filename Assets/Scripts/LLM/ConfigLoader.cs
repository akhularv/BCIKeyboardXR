// Assets/Scripts/LLM/ConfigLoader.cs
// Namespace: BCIKeyboardXR.LLM
// Responsibility: Load and expose the Anthropic API key from config.json (Resources/).
//
// DEPENDENCY: UnityEngine (Resources, TextAsset, JsonUtility)
// NOTE: JsonUtility is used here (not Newtonsoft) because config.json is a simple flat object.
//       JsonUtility cannot deserialize top-level Dictionaries, so a [Serializable] class is used.
//
// config.json expected shape:
//   { "anthropic_api_key": "sk-ant-..." }
//
// ACTION 4 (Council): ConfigLoader uses Lazy<string> with LazyThreadSafetyMode.ExecutionAndPublication
//                     to eliminate the double-initialization race on simultaneous first access.

using System;
using System.Threading;
using UnityEngine;

namespace BCIKeyboardXR.LLM
{
    /// <summary>
    /// Static loader for runtime configuration values stored in Assets/Resources/config.json.
    /// Thread-safe via <see cref="Lazy{T}"/> with ExecutionAndPublication mode.
    /// Throws <see cref="InvalidOperationException"/> with a descriptive message on misconfiguration —
    /// the project should not start with a missing or empty API key.
    /// </summary>
    public static class ConfigLoader
    {
        // ---------------------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------------------

        // Resource path without extension — Unity Resources.Load convention.
        private const string RESOURCE_PATH = "config";

        // ---------------------------------------------------------------------------
        // ACTION 4 (Council): Lazy<string> with thread-safe initialization mode.
        // LazyThreadSafetyMode.ExecutionAndPublication ensures only one thread runs
        // LoadApiKey() even if multiple threads race to access AnthropicApiKey.
        // ---------------------------------------------------------------------------
        private static readonly Lazy<string> _apiKey = new Lazy<string>(
            LoadApiKey,
            LazyThreadSafetyMode.ExecutionAndPublication);

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Returns the Anthropic API key loaded from config.json (Resources/).
        /// Cached after first access — O(1) on subsequent calls.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if config.json is missing from Resources/, or if anthropic_api_key
        /// is null or empty. The exception message includes actionable remediation text.
        /// </exception>
        public static string AnthropicApiKey => _apiKey.Value;

        // ---------------------------------------------------------------------------
        // Private implementation
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Called exactly once by the Lazy initializer. Loads and validates config.json.
        /// Throws <see cref="InvalidOperationException"/> on any misconfiguration.
        /// </summary>
        private static string LoadApiKey()
        {
            // Resources.Load must be called from the Unity main thread.
            // PredictionService accesses AnthropicApiKey from LLMClient.PredictWords,
            // which is asserted to run on the main thread (ACTION 5 / BACKEND_PLAN §6).
            var asset = Resources.Load<TextAsset>(RESOURCE_PATH);

            if (asset == null)
            {
                throw new InvalidOperationException(
                    "[ConfigLoader] config.json not found in Assets/Resources/. " +
                    "Create the file with shape: { \"anthropic_api_key\": \"sk-ant-...\" }");
            }

            // JsonUtility requires a concrete [Serializable] class — cannot target Dictionary.
            ApiConfig cfg = JsonUtility.FromJson<ApiConfig>(asset.text);

            if (cfg == null)
            {
                throw new InvalidOperationException(
                    "[ConfigLoader] Failed to parse config.json — JsonUtility returned null. " +
                    "Verify the file is valid JSON with the correct field names.");
            }

            if (string.IsNullOrEmpty(cfg.anthropic_api_key))
            {
                throw new InvalidOperationException(
                    "[ConfigLoader] anthropic_api_key is null or empty in config.json. " +
                    "Populate it with a valid Anthropic API key (sk-ant-...). " +
                    "Never commit the key to source control.");
            }

            Debug.Log("[ConfigLoader] API key loaded successfully.");

            return cfg.anthropic_api_key;
        }

        // ---------------------------------------------------------------------------
        // Private serialization helper
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Mirrors the config.json schema for JsonUtility deserialization.
        /// Field names must match JSON keys exactly (snake_case).
        /// </summary>
        [Serializable]
        private class ApiConfig
        {
            // ReSharper disable InconsistentNaming — field name must match JSON key exactly.
            public string anthropic_api_key;
        }
    }
}
