// Assets/Scripts/Core/UserProfile.cs
// Namespace: BCIKeyboardXR.Core
// Responsibility: Load and expose user_profile.md as a static, cached string
//                 suitable for injection into LLM system prompts.
//
// DEPENDENCY: UnityEngine (Resources, TextAsset, Debug)
// Resources.Load path rule: pass "user_profile" (no extension), relative to any Resources/ folder.

using UnityEngine;

namespace BCIKeyboardXR.Core
{
    /// <summary>
    /// Static cache for the Sarah Martinez user profile loaded from user_profile.md.
    /// Call <see cref="Initialize"/> once at startup (e.g., from PredictionService.Awake).
    /// All subsequent calls to <see cref="GetSystemPromptSection"/> are O(1) — no disk I/O.
    /// </summary>
    public static class UserProfile
    {
        // ---------------------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------------------

        // Resource path without extension — Unity Resources.Load convention.
        private const string RESOURCE_PATH = "user_profile";

        // Fallback text returned when the profile file cannot be loaded.
        // Never null — callers can always embed this in a prompt safely.
        private const string FALLBACK_PROFILE =
            "User is an ALS patient communicating via BCI. " +
            "Prioritize short, direct phrases about physical needs, family, and medical topics.";

        // System prompt wrapper: tells the LLM how to interpret the profile block.
        private const string PROMPT_HEADER =
            "You are an AI assistant integrated into a BCI-driven AAC (augmentative and " +
            "alternative communication) keyboard for the following user:\n\n";

        private const string PROMPT_FOOTER =
            "\n\nAlways respond in the voice and context of this user. " +
            "Prefer short, direct completions. " +
            "Never include explanatory text outside the JSON structure requested.";

        // ---------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------

        // Cached value after Initialize(). null means not yet loaded.
        private static string _cachedPromptSection = null;

        // Tracks whether Initialize() has run (even if the file was missing).
        private static bool _initialized = false;

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Load user_profile.md from Resources and cache it.
        /// Safe to call multiple times — subsequent calls are no-ops.
        /// Must be called from the Unity main thread (Resources.Load requirement).
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;

            // Resources.Load returns null if the asset is missing; never throws.
            var asset = Resources.Load<TextAsset>(RESOURCE_PATH);

            if (asset == null)
            {
                // Log a warning but do not throw — the system degrades gracefully
                // to the fallback profile rather than crashing the BCI interface.
                Debug.LogWarning(
                    $"[UserProfile] '{RESOURCE_PATH}.md' not found in any Resources/ folder. " +
                    "Using fallback profile. AAC predictions will be generic.");

                _cachedPromptSection = PROMPT_HEADER + FALLBACK_PROFILE + PROMPT_FOOTER;
                return;
            }

            // asset.text contains the full markdown text — Unity 6 treats .md as TextAsset.
            string profileText = asset.text;

            if (string.IsNullOrWhiteSpace(profileText))
            {
                Debug.LogWarning(
                    "[UserProfile] user_profile.md loaded but is empty. Using fallback profile.");
                _cachedPromptSection = PROMPT_HEADER + FALLBACK_PROFILE + PROMPT_FOOTER;
                return;
            }

            _cachedPromptSection = PROMPT_HEADER + profileText + PROMPT_FOOTER;

            Debug.Log(
                $"[UserProfile] Loaded profile ({profileText.Length} chars). " +
                "Prompt section cached.");
        }

        /// <summary>
        /// Returns the full user profile formatted for inclusion in an LLM system prompt.
        /// Calls <see cref="Initialize"/> automatically if not yet initialized (lazy fallback).
        /// Never returns null. Never throws.
        /// </summary>
        /// <returns>
        /// A string combining a system-prompt header, the raw profile markdown, and a footer
        /// instructing the LLM how to apply the profile.
        /// </returns>
        public static string GetSystemPromptSection()
        {
            // Lazy initialization guard: safe because Initialize() is idempotent.
            // Preferred path: Initialize() is called explicitly in PredictionService.Awake().
            if (!_initialized)
            {
                Debug.LogWarning(
                    "[UserProfile] GetSystemPromptSection called before Initialize(). " +
                    "Initializing now — ensure this is on the Unity main thread.");
                Initialize();
            }

            // _cachedPromptSection is always set by Initialize() — never null at this point.
            return _cachedPromptSection;
        }

        /// <summary>
        /// Resets the cache (for testing only). Not intended for production use.
        /// </summary>
        internal static void ResetForTesting()
        {
            _initialized = false;
            _cachedPromptSection = null;
        }
    }
}
