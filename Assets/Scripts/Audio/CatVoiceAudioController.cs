using System;
using Nekolpos.System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Timeline;

namespace Nekolpos.Audio
{
    public enum CatVoiceType
    {
        [InspectorName("呼ぶ")] Call,
        [InspectorName("怒り")] Angry,
        [InspectorName("嬉しい")] Happy,
        [InspectorName("不満")] Displeased
    }

    public enum CatVoiceOverlapPolicy
    {
        [InspectorName("再生中の要求を無視")] IgnoreWhilePlaying,
        [InspectorName("新しい鳴き声へ差し替え")] Replace
    }

    [Serializable]
    public sealed class CatVoiceCategory
    {
        [InspectorName("鳴き声候補")]
        [Tooltip("このカテゴリでランダム再生するAudioClipです。空欄なら安全に無音です。")]
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [InspectorName("カテゴリ音量")]
        [Tooltip("このカテゴリだけに適用する音量です。")]
        [SerializeField, Min(0f)] private float volume = 1f;

        public float Volume => Mathf.Max(0f, volume);
        public AudioClip Select(AudioClip previous) => AudioClipSelection.SelectAvoidingImmediateRepeat(clips, previous);
    }

    [DisallowMultipleComponent]
    public sealed class CatVoiceAudioController : MonoBehaviour
    {
        [Header("猫の鳴き声")]
        [InspectorName("呼ぶ")]
        [Tooltip("呼びかけ用途の鳴き声候補とカテゴリ音量です。候補が空なら無音です。")]
        [SerializeField] private CatVoiceCategory call = new CatVoiceCategory();
        [InspectorName("怒り")]
        [Tooltip("怒り用途の鳴き声候補とカテゴリ音量です。候補が空なら無音です。")]
        [SerializeField] private CatVoiceCategory angry = new CatVoiceCategory();
        [InspectorName("嬉しい")]
        [Tooltip("嬉しい用途の鳴き声候補とカテゴリ音量です。候補が空なら無音です。")]
        [SerializeField] private CatVoiceCategory happy = new CatVoiceCategory();
        [InspectorName("不満")]
        [Tooltip("不満用途の鳴き声候補とカテゴリ音量です。候補が空なら無音です。")]
        [SerializeField] private CatVoiceCategory displeased = new CatVoiceCategory();

        [Header("再生方法")]
        [InspectorName("猫ボイスの音源")]
        [Tooltip("未指定なら実行時に子オブジェクトへ作成し、既存のCatVoice Mixer経路へ接続します。")]
        [SerializeField] private AudioSource audioSource;
        [InspectorName("重複時の動作")]
        [Tooltip("鳴き声の多重再生を避ける方針です。")]
        [SerializeField] private CatVoiceOverlapPolicy overlapPolicy = CatVoiceOverlapPolicy.IgnoreWhilePlaying;
        [InspectorName("基本音量")]
        [Tooltip("全カテゴリへ共通で掛ける音量です。0なら無音です。")]
        [SerializeField, Min(0f)] private float baseVolume = 1f;

        private AudioClip previousClip;
        private bool pausedByGamePause;

        public bool PlayCatVoice(CatVoiceType type)
        {
            if (!isActiveAndEnabled) return false;
            EnsureAudioSource();
            SyncPauseState();
            if (IsGamePaused()) return false;
            if (audioSource.isPlaying && overlapPolicy == CatVoiceOverlapPolicy.IgnoreWhilePlaying) return false;
            CatVoiceCategory category = GetCategory(type);
            AudioClip clip = category?.Select(previousClip);
            if (clip == null) return false;
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.clip = clip;
            audioSource.volume = baseVolume * category.Volume;
            audioSource.Play();
            previousClip = clip;
            return true;
        }

        public bool TryGetPreview(CatVoiceType type, out AudioClip clip, out float volume)
        {
            CatVoiceCategory category = GetCategory(type);
            clip = category?.Select(previousClip);
            volume = category == null ? 0f : baseVolume * category.Volume;
            return clip != null;
        }

        private CatVoiceCategory GetCategory(CatVoiceType type)
        {
            return type switch
            {
                CatVoiceType.Call => call,
                CatVoiceType.Angry => angry,
                CatVoiceType.Happy => happy,
                CatVoiceType.Displeased => displeased,
                _ => null
            };
        }

        // Parameterless void methods are selectable from Timeline SignalReceiver UnityEvents.
        public void PlayCall() => PlayCatVoice(CatVoiceType.Call);
        public void PlayAngry() => PlayCatVoice(CatVoiceType.Angry);
        public void PlayHappy() => PlayCatVoice(CatVoiceType.Happy);
        public void PlayDispleased() => PlayCatVoice(CatVoiceType.Displeased);

        private void Awake()
        {
            EnsureAudioSource();
            RegisterSignal("CatVoice_Call", PlayCall);
            RegisterSignal("CatVoice_Angry", PlayAngry);
            RegisterSignal("CatVoice_Happy", PlayHappy);
            RegisterSignal("CatVoice_Displeased", PlayDispleased);
        }

        private void RegisterSignal(string name, UnityAction play)
        {
            var signal = Resources.Load<SignalAsset>("Audio/Signals/" + name);
            if (signal == null) return;
            var receiver = GetComponent<SignalReceiver>();
            if (receiver == null) receiver = gameObject.AddComponent<SignalReceiver>();
            if (receiver.GetReaction(signal) != null) return;
            var reaction = new UnityEvent();
            reaction.AddListener(play);
            receiver.AddReaction(signal, reaction);
        }
        private void Update() => SyncPauseState();

        private void OnDisable()
        {
            if (audioSource != null) audioSource.Stop();
            pausedByGamePause = false;
        }

        private void OnValidate() => baseVolume = Mathf.Max(0f, baseVolume);

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                Transform child = transform.Find("CatVoiceAudioSource");
                if (child == null)
                {
                    child = new GameObject("CatVoiceAudioSource").transform;
                    child.SetParent(transform, false);
                }
                audioSource = child.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = child.gameObject.AddComponent<AudioSource>();
                }
            }
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            SettingsManager.RouteAudio(audioSource, SettingsAudioCategory.CatVoice);
        }

        private void SyncPauseState()
        {
            if (audioSource == null) return;
            bool shouldPause = IsGamePaused();
            if (shouldPause && !pausedByGamePause)
            {
                audioSource.Pause();
                pausedByGamePause = true;
            }
            else if (!shouldPause && pausedByGamePause)
            {
                audioSource.UnPause();
                pausedByGamePause = false;
            }
        }

        private static bool IsGamePaused() => Time.timeScale <= 0f;
    }
}
