using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using BCIKeyboardXR.LLM;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace BCIKeyboardXR.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class TtsService : MonoBehaviour
    {
        private static TtsService _instance;

        [SerializeField] private AudioSource audioSource;

        private CancellationTokenSource _requestCts;
        private Coroutine _playbackRoutine;
        private Coroutine _decodeRoutine;
        private Coroutine _sayPollRoutine;
        private UnityWebRequest _decodeRequest;
        private AudioClip _currentClip;
        private Process _sayProcess;
        private int _generation;
        private bool _isSpeaking;
        private string _tempAudioPath;

        public static event Action<int> OnCharacterSpoken;

        public static TtsService Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var existing = FindAnyObjectByType<TtsService>();
                if (existing != null)
                {
                    _instance = existing;
                    return _instance;
                }

                var go = new GameObject("TtsService", typeof(AudioSource));
                _instance = go.AddComponent<TtsService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        public static bool IsSpeaking => _instance != null && _instance._isSpeaking;

        public static void Speak(string text)
        {
            Instance.SpeakInternal(text);
        }

        public static void Stop()
        {
            if (_instance != null)
                _instance.StopInternal();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureAudioSource();

            audioSource.playOnAwake = false;
        }

        private void OnDestroy()
        {
            StopInternal();
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            StopInternal();
        }

        private async void SpeakInternal(string text)
        {
            string clean = Sanitize(text);
            if (string.IsNullOrEmpty(clean))
                return;

            StopInternal();
            int generation = ++_generation;
            _isSpeaking = true;

            if (string.IsNullOrEmpty(ConfigLoader.ElevenLabsApiKey))
            {
                Debug.Log("[TTS] ElevenLabs key missing, falling back to /usr/bin/say");
                SpeakViaSay(clean, generation);
                return;
            }

            _requestCts = new CancellationTokenSource();
            TtsResult result;
            try
            {
                result = await ElevenLabsClient.Synthesize(clean, _requestCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (generation == _generation)
                    FinishSpeech();
                return;
            }
            catch (Exception ex)
            {
                result = new TtsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }

            if (generation != _generation)
                return;

            if (result != null && result.Success)
            {
                _decodeRoutine = StartCoroutine(PlaySynthesizedAudio(result, clean, generation));
                return;
            }

            Debug.LogWarning("[TTS] ElevenLabs failed: " + (result?.ErrorMessage ?? "unknown error") + ", falling back to say");
            SpeakViaSay(clean, generation);
        }

        private IEnumerator PlaySynthesizedAudio(TtsResult result, string fallbackText, int generation)
        {
            string path = Path.Combine(Application.temporaryCachePath, "elevenlabs-" + Guid.NewGuid().ToString("N") + ".mp3");
            _tempAudioPath = path;

            byte[] audioBytes = result.AudioBytes ?? Array.Empty<byte>();
            try
            {
                File.WriteAllBytes(path, audioBytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TTS] Failed to write temp MP3: " + ex.Message + ", falling back to say");
                SpeakViaSay(fallbackText, generation);
                yield break;
            }

            string uri = new Uri(path).AbsoluteUri;
            _decodeRequest = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
            UnityWebRequestAsyncOperation operation = _decodeRequest.SendWebRequest();
            while (!operation.isDone)
            {
                if (generation != _generation)
                    yield break;
                yield return null;
            }

            if (generation != _generation)
                yield break;

            if (_decodeRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[TTS] MP3 decode failed: " + _decodeRequest.error + ", falling back to say");
                DisposeDecodeRequest();
                DeleteTempAudioFile();
                SpeakViaSay(fallbackText, generation);
                yield break;
            }

            _currentClip = DownloadHandlerAudioClip.GetContent(_decodeRequest);
            DisposeDecodeRequest();
            if (_currentClip == null)
            {
                Debug.LogWarning("[TTS] MP3 decode returned no AudioClip, falling back to say");
                DeleteTempAudioFile();
                SpeakViaSay(fallbackText, generation);
                yield break;
            }

            audioSource.clip = _currentClip;
            audioSource.time = 0f;
            audioSource.Play();
            _playbackRoutine = StartCoroutine(PollPlaybackPosition(result.CharTimings, generation));
            _decodeRoutine = null;
        }

        private IEnumerator PollPlaybackPosition(List<CharTiming> timings, int generation)
        {
            int lastFiredIndex = -2;

            while (generation == _generation && audioSource != null && audioSource.isPlaying)
            {
                int currentIndex = FindCharIndexAtTime(audioSource.time, timings);
                if (currentIndex != lastFiredIndex)
                {
                    OnCharacterSpoken?.Invoke(currentIndex);
                    lastFiredIndex = currentIndex;
                }

                yield return null;
            }

            if (generation == _generation)
                FinishSpeech();
        }

        private static int FindCharIndexAtTime(float time, List<CharTiming> timings)
        {
            if (timings == null || timings.Count == 0)
                return -1;

            if (time < timings[0].StartSec)
                return -1;

            for (int i = 0; i < timings.Count; i++)
            {
                CharTiming timing = timings[i];
                if (time >= timing.StartSec && time < timing.EndSec)
                    return i;
            }

            return time > timings[timings.Count - 1].EndSec ? timings.Count - 1 : -1;
        }

        private void SpeakViaSay(string text, int generation)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            try
            {
                _sayProcess = new Process
                {
                    EnableRaisingEvents = true,
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/say",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _sayProcess.StartInfo.ArgumentList.Add("-v");
                _sayProcess.StartInfo.ArgumentList.Add("Samantha");
                _sayProcess.StartInfo.ArgumentList.Add(text);

                _sayProcess.Start();
                _sayPollRoutine = StartCoroutine(PollSayProcess(generation));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TTS] Failed to start macOS say: " + ex.Message);
                FinishSpeech();
            }
#else
            Debug.LogWarning("[TTS] Text-to-speech fallback is only available on macOS.");
            FinishSpeech();
#endif
        }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private IEnumerator PollSayProcess(int generation)
        {
            var wait = new WaitForSecondsRealtime(0.1f);
            while (generation == _generation && _sayProcess != null)
            {
                bool exited;
                try
                {
                    exited = _sayProcess.HasExited;
                }
                catch (InvalidOperationException)
                {
                    exited = true;
                }

                if (exited)
                    break;

                yield return wait;
            }

            if (generation == _generation)
                FinishSpeech();
        }
#endif

        private void StopInternal()
        {
            _generation++;
            CancelRequest();

            if (_playbackRoutine != null)
            {
                StopCoroutine(_playbackRoutine);
                _playbackRoutine = null;
            }

            if (_decodeRoutine != null)
            {
                StopCoroutine(_decodeRoutine);
                _decodeRoutine = null;
            }

            if (_decodeRequest != null)
            {
                _decodeRequest.Abort();
                DisposeDecodeRequest();
            }

            if (_sayPollRoutine != null)
            {
                StopCoroutine(_sayPollRoutine);
                _sayPollRoutine = null;
            }

            if (audioSource != null)
                audioSource.Stop();

            StopSayProcess();
            CleanupAudioClip();
            DeleteTempAudioFile();

            if (_isSpeaking)
            {
                _isSpeaking = false;
                OnCharacterSpoken?.Invoke(-1);
            }
        }

        private void FinishSpeech()
        {
            CancelRequest();

            if (_playbackRoutine != null)
            {
                StopCoroutine(_playbackRoutine);
                _playbackRoutine = null;
            }

            if (_decodeRoutine != null)
            {
                StopCoroutine(_decodeRoutine);
                _decodeRoutine = null;
            }

            DisposeDecodeRequest();

            if (_sayPollRoutine != null)
            {
                StopCoroutine(_sayPollRoutine);
                _sayPollRoutine = null;
            }

            StopSayProcess();
            CleanupAudioClip();
            DeleteTempAudioFile();

            _isSpeaking = false;
            OnCharacterSpoken?.Invoke(-1);
        }

        private void CancelRequest()
        {
            if (_requestCts == null)
                return;

            _requestCts.Cancel();
            _requestCts.Dispose();
            _requestCts = null;
        }

        private void StopSayProcess()
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (_sayProcess == null)
                return;

            try
            {
                if (!_sayProcess.HasExited)
                    _sayProcess.Kill();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TTS] Failed to stop macOS say: " + ex.Message);
            }
            finally
            {
                _sayProcess.Dispose();
                _sayProcess = null;
            }
#endif
        }

        private void CleanupAudioClip()
        {
            if (audioSource != null)
                audioSource.clip = null;

            if (_currentClip == null)
                return;

            Destroy(_currentClip);
            _currentClip = null;
        }

        private void EnsureAudioSource()
        {
            if (audioSource != null && audioSource.gameObject == gameObject)
                return;

            if (!TryGetComponent(out audioSource))
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        private void DisposeDecodeRequest()
        {
            if (_decodeRequest == null)
                return;

            _decodeRequest.Dispose();
            _decodeRequest = null;
        }

        private void DeleteTempAudioFile()
        {
            if (string.IsNullOrEmpty(_tempAudioPath))
                return;

            try
            {
                if (File.Exists(_tempAudioPath))
                    File.Delete(_tempAudioPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TTS] Failed to delete temp audio file: " + ex.Message);
            }
            finally
            {
                _tempAudioPath = null;
            }
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsControl(c))
                    continue;

                builder.Append(c == '"' ? "\\\"" : c.ToString());
            }

            return builder.ToString().Trim();
        }
    }
}
