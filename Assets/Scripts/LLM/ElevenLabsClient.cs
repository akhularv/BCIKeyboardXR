using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace BCIKeyboardXR.LLM
{
    public struct CharTiming
    {
        public char Ch;
        public float StartSec;
        public float EndSec;
    }

    public class TtsResult
    {
        public byte[] AudioBytes;
        public List<CharTiming> CharTimings = new List<CharTiming>();
        public bool Success;
        public string ErrorMessage;
    }

    public static class ElevenLabsClient
    {
        private const string VoiceId = "21m00Tcm4TlvDq8ikWAM";
        private const string EndpointBase = "https://api.elevenlabs.io/v1/text-to-speech/";
        private const string ModelId = "eleven_turbo_v2_5";
        private const int TimeoutSeconds = 10;

        public static async Task<TtsResult> Synthesize(string text, CancellationToken ct)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
                return Failure("Text is empty.");

            string apiKey = ConfigLoader.ElevenLabsApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return Failure("ElevenLabs API key missing.");

            Debug.Log("[ElevenLabs] Request start.");

            var body = new
            {
                text,
                model_id = ModelId,
                voice_settings = new
                {
                    stability = 0.5f,
                    similarity_boost = 0.75f
                }
            };

            string jsonBody = JsonConvert.SerializeObject(body);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            string endpoint = EndpointBase + VoiceId + "/with-timestamps";

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            CancellationToken linkedToken = linkedCts.Token;

            using var request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("xi-api-key", apiKey);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            var tcs = new TaskCompletionSource<UnityWebRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using CancellationTokenRegistration registration = linkedToken.Register(() =>
            {
                request.Abort();
                if (ct.IsCancellationRequested)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(request);
            });

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                if (tcs.Task.IsCompleted)
                    return;

                if (!linkedToken.IsCancellationRequested)
                    tcs.TrySetResult(request);
            };

            UnityWebRequest completedRequest;
            try
            {
                completedRequest = await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                Debug.LogWarning("[ElevenLabs] Failure: request timed out.");
                return Failure("Request timed out.");
            }

            if (completedRequest.result != UnityWebRequest.Result.Success)
            {
                string responseBody = completedRequest.downloadHandler?.text;
                string error = $"HTTP {completedRequest.responseCode}: {completedRequest.error}";
                if (!string.IsNullOrWhiteSpace(responseBody))
                    error += " | " + Truncate(responseBody, 240);
                Debug.LogWarning("[ElevenLabs] Failure: " + error);
                return Failure(error);
            }

            string responseText = completedRequest.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                Debug.LogWarning("[ElevenLabs] Failure: empty response body.");
                return Failure("Empty response body.");
            }

            try
            {
                ElevenLabsResponse response = JsonConvert.DeserializeObject<ElevenLabsResponse>(responseText);
                if (response == null || string.IsNullOrEmpty(response.audio_base64))
                    return LogAndFail("Response did not contain audio_base64.");

                byte[] audioBytes = Convert.FromBase64String(response.audio_base64);
                List<CharTiming> timings = ParseTimings(response.alignment);
                if (audioBytes.Length == 0)
                    return LogAndFail("Decoded audio was empty.");
                if (timings.Count == 0)
                    return LogAndFail("Response did not contain valid character timings.");

                Debug.Log($"[ElevenLabs] Success: chars={timings.Count}, audio_bytes={audioBytes.Length}");
                return new TtsResult
                {
                    Success = true,
                    AudioBytes = audioBytes,
                    CharTimings = timings
                };
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentException)
            {
                return LogAndFail("Parse/decode failure: " + ex.Message);
            }
        }

        private static List<CharTiming> ParseTimings(Alignment alignment)
        {
            var timings = new List<CharTiming>();
            if (alignment?.characters == null ||
                alignment.character_start_times_seconds == null ||
                alignment.character_end_times_seconds == null)
            {
                return timings;
            }

            int count = Math.Min(
                alignment.characters.Count,
                Math.Min(alignment.character_start_times_seconds.Count, alignment.character_end_times_seconds.Count));

            for (int i = 0; i < count; i++)
            {
                string character = alignment.characters[i] ?? string.Empty;
                if (character.Length == 0)
                    continue;

                timings.Add(new CharTiming
                {
                    Ch = character[0],
                    StartSec = alignment.character_start_times_seconds[i],
                    EndSec = alignment.character_end_times_seconds[i]
                });
            }

            return timings;
        }

        private static TtsResult LogAndFail(string message)
        {
            Debug.LogWarning("[ElevenLabs] Failure: " + message);
            return Failure(message);
        }

        private static TtsResult Failure(string message)
        {
            return new TtsResult
            {
                Success = false,
                ErrorMessage = message
            };
        }

        private static string Truncate(string value, int maxLength)
        {
            value ??= string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private class ElevenLabsResponse
        {
            public string audio_base64 = null;
            public Alignment alignment = null;
        }

        private class Alignment
        {
            public List<string> characters = null;
            public List<float> character_start_times_seconds = null;
            public List<float> character_end_times_seconds = null;
        }
    }
}
