using System.Collections;
using UnityEngine;

namespace Nekolpos.Audio
{
    public sealed class BackgroundMusicController : MonoBehaviour
    {
        private const string AudioSourceObjectName = "BgmAudioSource";
        private const string DefaultTitleBgmResourcePath = "BGM/YUYAKE";
        private const string DefaultGameBgmResourcePath = "BGM/TROIKA";

        [Header("Clips")]
        [Tooltip("起動時から再生するタイトルBGMです。未指定なら Resources/BGM/YUYAKE を使います。")]
        [SerializeField] private AudioClip titleBgm;
        [Tooltip("ゲーム中BGMです。複数指定すると切り替え時と曲終了時にランダム再生します。未指定なら Resources/BGM/TROIKA を使います。")]
        [SerializeField] private AudioClip[] gameBgms;

        [Header("Playback")]
        [SerializeField] [Range(0f, 1f)] private float volume = 0.55f;
        [Tooltip("BGM切り替え時の片道フェード時間です。3なら、3秒フェードアウト後に3秒フェードインします。")]
        [SerializeField] [Min(0f)] private float crossFadeSeconds = 3f;
        [SerializeField] private bool playTitleOnAwake = true;
        [SerializeField] private bool avoidImmediateRepeat = true;

        [Header("Debug")]
        [SerializeField] private bool logMusicChanges;

        private AudioSource audioSource;
        private Coroutine fadeCoroutine;
        private MusicMode currentMode = MusicMode.None;
        private int lastGameClipIndex = -1;

        private enum MusicMode
        {
            None,
            Title,
            Game
        }

        private void Awake()
        {
            EnsureAudioSource();
            ConfigureAudioSource();
            LoadDefaultClipsIfNeeded();

            if (playTitleOnAwake)
            {
                PlayTitleMusic(true);
            }
        }

        private void Update()
        {
            if (currentMode != MusicMode.Game || audioSource == null || audioSource.clip == null || fadeCoroutine != null)
            {
                return;
            }

            if (audioSource.isPlaying)
            {
                float remainingSeconds = audioSource.clip.length - audioSource.time;
                if (remainingSeconds <= crossFadeSeconds)
                {
                    PlayGameMusic(true);
                }

                return;
            }

            if (!audioSource.isPlaying)
            {
                PlayGameMusic(true);
            }
        }

        private void OnValidate()
        {
            volume = Mathf.Clamp01(volume);
            crossFadeSeconds = Mathf.Max(0f, crossFadeSeconds);

            if (audioSource != null)
            {
                ConfigureAudioSource();
                audioSource.volume = Mathf.Min(audioSource.volume, volume);
            }
        }

        public void PlayTitleMusic(bool instant = false)
        {
            LoadDefaultClipsIfNeeded();
            PlayClip(titleBgm, MusicMode.Title, instant);
        }

        public void PlayGameMusic(bool fade = true)
        {
            LoadDefaultClipsIfNeeded();
            AudioClip clip = SelectRandomGameClip();
            PlayClip(clip, MusicMode.Game, !fade);
        }

        public void StopMusic(bool instant = false)
        {
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            currentMode = MusicMode.None;
            if (audioSource == null)
            {
                return;
            }

            if (instant || crossFadeSeconds <= 0f)
            {
                audioSource.Stop();
                audioSource.clip = null;
                audioSource.volume = 0f;
                return;
            }

            fadeCoroutine = StartCoroutine(FadeOutAndStopRoutine());
        }

        private void PlayClip(AudioClip clip, MusicMode mode, bool instant)
        {
            if (clip == null)
            {
                if (logMusicChanges)
                {
                    Debug.Log($"[BackgroundMusicController] {mode} BGM is not assigned.", this);
                }

                return;
            }

            EnsureAudioSource();
            ConfigureAudioSource();
            if (audioSource != null)
            {
                audioSource.enabled = true;
                if (!audioSource.gameObject.activeSelf)
                {
                    audioSource.gameObject.SetActive(true);
                }
            }

            if (audioSource.clip == clip && audioSource.isPlaying)
            {
                currentMode = mode;
                bool shouldRestartGameClip =
                    mode == MusicMode.Game
                    && !instant
                    && audioSource.clip.length - audioSource.time <= crossFadeSeconds;

                if (!shouldRestartGameClip)
                {
                    return;
                }
            }

            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            currentMode = mode;
            if (instant || crossFadeSeconds <= 0f)
            {
                audioSource.clip = clip;
                audioSource.loop = mode == MusicMode.Title;
                audioSource.volume = volume;
                audioSource.Play();
                LogClipChange(mode, clip);
                return;
            }

            if (!audioSource.isPlaying)
            {
                fadeCoroutine = StartCoroutine(FadeInRoutine(clip, mode));
                return;
            }

            fadeCoroutine = StartCoroutine(CrossFadeRoutine(clip, mode));
        }

        private IEnumerator CrossFadeRoutine(AudioClip nextClip, MusicMode nextMode)
        {
            float duration = Mathf.Max(0.01f, crossFadeSeconds);
            float startVolume = audioSource.volume;

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
                yield return null;
            }

            audioSource.volume = 0f;
            audioSource.Stop();
            audioSource.clip = nextClip;
            audioSource.loop = nextMode == MusicMode.Title;
            audioSource.Play();
            LogClipChange(nextMode, nextClip);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                audioSource.volume = Mathf.Lerp(0f, volume, elapsed / duration);
                yield return null;
            }

            audioSource.volume = volume;
            fadeCoroutine = null;
        }

        private IEnumerator FadeInRoutine(AudioClip nextClip, MusicMode nextMode)
        {
            float duration = Mathf.Max(0.01f, crossFadeSeconds);

            audioSource.Stop();
            audioSource.clip = nextClip;
            audioSource.loop = nextMode == MusicMode.Title;
            audioSource.volume = 0f;
            audioSource.Play();
            LogClipChange(nextMode, nextClip);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                audioSource.volume = Mathf.Lerp(0f, volume, elapsed / duration);
                yield return null;
            }

            audioSource.volume = volume;
            fadeCoroutine = null;
        }

        private IEnumerator FadeOutAndStopRoutine()
        {
            float duration = Mathf.Max(0.01f, crossFadeSeconds);
            float startVolume = audioSource.volume;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
                yield return null;
            }

            audioSource.Stop();
            audioSource.clip = null;
            audioSource.volume = 0f;
            fadeCoroutine = null;
        }

        private void EnsureAudioSource()
        {
            if (audioSource != null)
            {
                return;
            }

            Transform child = transform.Find(AudioSourceObjectName);
            if (child == null)
            {
                GameObject audioSourceObject = new GameObject(AudioSourceObjectName);
                child = audioSourceObject.transform;
                child.SetParent(transform, false);
            }

            audioSource = child.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = child.gameObject.AddComponent<AudioSource>();
            }
        }

        private void ConfigureAudioSource()
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.playOnAwake = false;
            Nekolpos.System.OpenBetaPauseMenuController.ApplySavedAudioState(audioSource, true);
            audioSource.loop = currentMode != MusicMode.Game;
            audioSource.spatialBlend = 0f;
            audioSource.volume = Mathf.Clamp(audioSource.volume, 0f, volume);
        }

        private AudioClip SelectRandomGameClip()
        {
            if (gameBgms == null || gameBgms.Length == 0)
            {
                return null;
            }

            int validCount = 0;
            for (int i = 0; i < gameBgms.Length; i++)
            {
                if (gameBgms[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            int selectedValidIndex = Random.Range(0, validCount);
            int selectedClipIndex = -1;
            for (int i = 0; i < gameBgms.Length; i++)
            {
                if (gameBgms[i] == null)
                {
                    continue;
                }

                if (selectedValidIndex == 0)
                {
                    selectedClipIndex = i;
                    break;
                }

                selectedValidIndex--;
            }

            if (avoidImmediateRepeat && validCount > 1 && selectedClipIndex == lastGameClipIndex)
            {
                return SelectNextDifferentGameClip();
            }

            lastGameClipIndex = selectedClipIndex;
            return selectedClipIndex >= 0 ? gameBgms[selectedClipIndex] : null;
        }

        private AudioClip SelectNextDifferentGameClip()
        {
            int startIndex = Random.Range(0, gameBgms.Length);
            for (int offset = 0; offset < gameBgms.Length; offset++)
            {
                int index = (startIndex + offset) % gameBgms.Length;
                AudioClip clip = gameBgms[index];
                if (clip != null && index != lastGameClipIndex)
                {
                    lastGameClipIndex = index;
                    return clip;
                }
            }

            return gameBgms[lastGameClipIndex];
        }

        private void LoadDefaultClipsIfNeeded()
        {
            if (titleBgm == null)
            {
                titleBgm = Resources.Load<AudioClip>(DefaultTitleBgmResourcePath);
            }

            if (!HasAnyGameClip())
            {
                AudioClip defaultGameBgm = Resources.Load<AudioClip>(DefaultGameBgmResourcePath);
                if (defaultGameBgm != null)
                {
                    gameBgms = new[] { defaultGameBgm };
                }
            }
        }

        private bool HasAnyGameClip()
        {
            if (gameBgms == null)
            {
                return false;
            }

            for (int i = 0; i < gameBgms.Length; i++)
            {
                if (gameBgms[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void LogClipChange(MusicMode mode, AudioClip clip)
        {
            if (logMusicChanges && clip != null)
            {
                Debug.Log($"[BackgroundMusicController] Playing {mode} BGM: {clip.name}", this);
            }
        }
    }
}
