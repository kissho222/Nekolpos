using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>Scene-authored transport overlay. Location changes remain owned by the bootstrap.</summary>
    public sealed class TransportAnimationView : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            public string id;
            public Image image;
            [Tooltip("キャンバス位置を保持した1～6のSpriteです。")]
            public Sprite[] frames = new Sprite[6];
        }

        [Serializable]
        public sealed class Preset
        {
            public string id;
            [Tooltip("背面から前面へのレイヤーID。未使用レイヤーは非表示になります。")]
            public string[] layers = Array.Empty<string>();
            public float[] frameSeconds = { .16f, .17f, .17f, .16f, .17f, .17f };
            public bool loop = true;
        }

        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private Layer[] layers = Array.Empty<Layer>();
        [SerializeField] private Preset[] presets = Array.Empty<Preset>();
        [SerializeField] private AudioSource soundSource;
        [SerializeField] private AudioClip[] startSounds = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float soundVolume = .7f;
        [SerializeField] private string previewPreset = "Collar";

        [Header("Horizontal Travel")]
        [SerializeField] private bool enableHorizontalTravel = true;
        [SerializeField, Tooltip("全レイヤーを含む共通の移動親。レイアウトはPrefabで設定します。")]
        private RectTransform motionRoot;
        [SerializeField, Min(.01f)] private float travelSeconds = 2f;
        [SerializeField, Tooltip("親の横幅を1とした中央基準の開始位置。")]
        private float startHorizontalPosition = -1f;
        [SerializeField] private float endHorizontalPosition = 1f;
        [SerializeField, Tooltip("端は速く、中央は遅く。6コマの保持時間とは独立した位置カーブです。")]
        private AnimationCurve travelProgress = new AnimationCurve(
            new Keyframe(0f, 0f, 2.5f, 2.5f),
            new Keyframe(.5f, .5f, .25f, .25f),
            new Keyframe(1f, 1f, 2.5f, 2.5f));

        private Layer[] activeLayers;
        private Preset activePreset;
        private double elapsed;
        private Vector2 motionOrigin;
        private bool hasMoved;
        private float activeTravelSeconds;
        private bool activeTravelEnabled;
        public bool HasCompletedTravel => !IsPlaying || !activeTravelEnabled || elapsed + .000001 >= activeTravelSeconds;
        public bool IsPlaying => activePreset != null;
        public int CurrentFrame { get; private set; } = -1;
        public double ElapsedSeconds => elapsed;
        public float TravelDurationSeconds => activeTravelEnabled ? activeTravelSeconds : 0f;
        public AudioClip LastSound { get; private set; }
        public IReadOnlyList<Preset> Presets => presets;

        private void Awake() => StopPlayback();
        private void OnDisable() => StopPlayback();

        private void Update()
        {
            if (IsPlaying) Advance(Time.unscaledDeltaTime);
        }

        public bool TryPlay(string presetId)
        {
            if (IsPlaying) return false;
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[TransportAnimation] Componentが無効です。既存の移動を継続します。", this);
                return false;
            }
            if (!TryResolve(presetId, out Preset preset, out Layer[] selected, out string error))
            {
                Debug.LogWarning($"[TransportAnimation] 再生できません: {error ?? "Componentが無効です。"} 既存の移動を継続します。", this);
                return false;
            }
            StopPlayback();
            activePreset = preset;
            activeLayers = selected;
            activeTravelEnabled = enableHorizontalTravel;
            activeTravelSeconds = travelSeconds;
            if (activeTravelEnabled)
            {
                motionOrigin = motionRoot.anchoredPosition;
                hasMoved = true;
                ApplyTravel();
            }
            foreach (Layer layer in selected)
            {
                layer.image.transform.SetAsLastSibling();
                layer.image.enabled = true;
            }
            visibility.alpha = 1f;
            ApplyFrame(0);
            PlayStartSound();
            return true;
        }

        public bool ValidatePreset(string presetId, out string error) => TryResolve(presetId, out _, out _, out error);

        private bool TryResolve(string id, out Preset preset, out Layer[] selected, out string error)
        {
            preset = null;
            selected = null;
            error = null;
            if (visibility == null) { error = "表示CanvasGroupが未設定です。"; return false; }
            if (presets == null || layers == null) { error = "レイヤーまたはプリセット配列が未設定です。"; return false; }
            if (enableHorizontalTravel)
            {
                if (motionRoot == null || !(motionRoot.parent is RectTransform)
                    || !IsFinite(travelSeconds) || travelSeconds <= 0f
                    || !IsFinite(startHorizontalPosition) || !IsFinite(endHorizontalPosition)
                    || startHorizontalPosition >= endHorizontalPosition || travelProgress == null || travelProgress.length < 2)
                { error = "横移動の親・正の時間・左→右の位置・カーブを設定してください。"; return false; }
                for (int i = 0; i <= 32; i++)
                    if (!IsFinite(travelProgress.Evaluate(i / 32f)))
                    { error = "横移動カーブが有限値ではありません。"; return false; }
            }
            foreach (Preset candidate in presets)
            {
                if (candidate == null || candidate.id != id) continue;
                if (preset != null) { error = $"プリセットID重複: {id}"; return false; }
                preset = candidate;
            }
            if (preset == null) { error = $"プリセット未登録: {id}"; return false; }
            if (preset.frameSeconds == null || preset.frameSeconds.Length != 6)
            { error = $"{id}: 保持時間は6件必要です。"; return false; }
            foreach (float seconds in preset.frameSeconds)
                if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
                { error = $"{id}: 保持時間は有限の正数が必要です。"; return false; }
            if (preset.layers == null || preset.layers.Length == 0)
            { error = $"{id}: レイヤーが空です。"; return false; }
            var ids = new HashSet<string>();
            var images = new HashSet<Image>();
            var resolved = new List<Layer>();
            foreach (string layerId in preset.layers)
            {
                if (string.IsNullOrWhiteSpace(layerId) || !ids.Add(layerId))
                { error = $"{id}: レイヤーIDが空または重複: {layerId}"; return false; }
                Layer found = null;
                foreach (Layer candidate in layers)
                {
                    if (candidate == null || candidate.id != layerId) continue;
                    if (found != null) { error = $"レイヤー定義ID重複: {layerId}"; return false; }
                    found = candidate;
                }
                if (found == null || found.image == null || !images.Add(found.image) || found.frames == null || found.frames.Length != 6)
                { error = $"{layerId}: Image参照または6コマの配列が不正です。"; return false; }
                if (enableHorizontalTravel && !found.image.transform.IsChildOf(motionRoot))
                { error = $"{layerId}: 共通の横移動親の下へ配置してください。"; return false; }
                var sprites = new HashSet<Sprite>();
                for (int i = 0; i < 6; i++)
                    if (found.frames[i] == null || !sprites.Add(found.frames[i]))
                    { error = $"{layerId}: コマ{i + 1}が欠損または重複しています。"; return false; }
                resolved.Add(found);
            }
            selected = resolved.ToArray();
            return true;
        }

        public void Advance(float seconds)
        {
            if (!IsPlaying || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return;
            elapsed += seconds;
            if (activeTravelEnabled)
            {
                if (motionRoot == null || !(motionRoot.parent is RectTransform))
                {
                    Debug.LogWarning("[TransportAnimation] 横移動の親が失われました。表示を終了して移動を継続します。", this);
                    StopPlayback();
                    return;
                }
                ApplyTravel();
            }
            double cycle = 0;
            foreach (float hold in activePreset.frameSeconds) cycle += hold;
            double local = activePreset.loop ? elapsed % cycle : Math.Min(elapsed, cycle);
            if (activePreset.loop && cycle - local < 0.000001) local = 0;
            int frame = 5;
            for (int i = 0; i < 6; i++)
            {
                if (local + 0.0000001 < activePreset.frameSeconds[i]) { frame = i; break; }
                local -= activePreset.frameSeconds[i];
            }
            if (frame != CurrentFrame) ApplyFrame(frame);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void ApplyTravel()
        {
            float time = Mathf.Clamp01((float)(elapsed / activeTravelSeconds));
            float progress = time >= 1f ? 1f : time <= 0f ? 0f : Mathf.Clamp01(travelProgress.Evaluate(time));
            float width = ((RectTransform)motionRoot.parent).rect.width;
            motionRoot.anchoredPosition = motionOrigin + Vector2.right
                * (Mathf.Lerp(startHorizontalPosition, endHorizontalPosition, progress) * width);
        }

        private void ApplyFrame(int frame)
        {
            foreach (Layer layer in activeLayers)
            {
                if (layer.image == null || layer.frames[frame] == null)
                {
                    Debug.LogWarning("[TransportAnimation] 再生中に素材参照が失われました。表示を終了し、既存移動を継続します。", this);
                    StopPlayback();
                    return;
                }
                layer.image.sprite = layer.frames[frame];
            }
            CurrentFrame = frame;
        }

        private void PlayStartSound()
        {
            if (soundSource == null || startSounds == null) return;
            int count = 0;
            foreach (AudioClip clip in startSounds) if (clip != null) count++;
            if (count == 0) return;
            int index = UnityEngine.Random.Range(0, count);
            foreach (AudioClip clip in startSounds)
            {
                if (clip == null || index-- > 0) continue;
                LastSound = clip;
                SettingsManager.RouteAudio(soundSource, SettingsAudioCategory.Se);
                soundSource.clip = clip;
                soundSource.volume = soundVolume;
                soundSource.Play();
                break;
            }
        }

        public void StopPlayback()
        {
            activePreset = null;
            activeLayers = null;
            if (hasMoved && motionRoot != null) motionRoot.anchoredPosition = motionOrigin;
            hasMoved = false;
            activeTravelEnabled = false;
            elapsed = 0;
            CurrentFrame = -1;
            if (visibility != null) visibility.alpha = 0f;
            if (soundSource != null) { soundSource.Stop(); soundSource.clip = null; }
            if (layers == null) return;
            foreach (Layer layer in layers)
                if (layer?.image != null) { layer.image.enabled = false; layer.image.sprite = null; }
        }

        [ContextMenu("Preview Selected Preset (Play Mode)")]
        private void PreviewSelected()
        {
            if (!Application.isPlaying) return;
            StopPlayback();
            TryPlay(previewPreset);
        }
        [ContextMenu("Stop Preview")]
        private void StopPreview() => StopPlayback();
    }
}
