using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nekolpos.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Nekolpos.System
{
    [DefaultExecutionOrder(-31000)]
    public sealed class SettingsManager : MonoBehaviour
    {
        private static SettingsManager instance;
        private AudioMixer mixer;
        private readonly Dictionary<SettingsAudioCategory, AudioMixerGroup> groups = new Dictionary<SettingsAudioCategory, AudioMixerGroup>();
        private Coroutine displayRoutine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private bool shortcutHeld;
#endif
        private float nextAudioDiscovery;
        private int initialVSyncCount;
        public static SettingsManager Instance => instance;
        public GameSettings Values { get; private set; }
        public IReadOnlyList<Vector2Int> Resolutions { get; private set; }
        public bool IsDisplayChangePending { get; private set; }
        public event Action Changed;

        public static bool SupportsDisplaySettings
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() => EnsureInstance();

        public static SettingsManager EnsureInstance()
        {
            if (instance == null) new GameObject(nameof(SettingsManager)).AddComponent<SettingsManager>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            initialVSyncCount = QualitySettings.vSyncCount;
            Vector2Int current = CurrentResolution();
            Resolutions = GameSettings.ResolutionChoices(Screen.resolutions, current);
            Values = GameSettingsStore.Load(GameSettings.Defaults(current.x, current.y, initialVSyncCount > 0));
            Values.Validate(Resolutions, current);
            mixer = Resources.Load<AudioMixer>("Audio/GameSettings");
            if (mixer == null) Debug.LogError("[SettingsManager] Audio/GameSettings Mixerがありません。");
            else foreach (SettingsAudioCategory category in Enum.GetValues(typeof(SettingsAudioCategory)))
                groups[category] = mixer.FindMatchingGroups(GroupName(category)).FirstOrDefault();
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyDisplay();
            ApplyVSync();
        }

        // AudioMixer.SetFloat is applied in Start rather than Awake, after mixer initialization.
        private void Start() { ApplyAudio(); DiscoverAudioSources(); }

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            bool held = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter));
            if (ShouldToggleShortcut(held, shortcutHeld, IsTextInputFocused(), !string.IsNullOrEmpty(Input.compositionString)))
                ToggleDisplayMode();
            shortcutHeld = held;
#endif
            if (Time.unscaledTime >= nextAudioDiscovery)
            {
                DiscoverAudioSources();
                nextAudioDiscovery = Time.unscaledTime + 1f;
            }
        }

        public static bool ShouldToggleShortcut(bool held, bool wasHeld, bool textInputFocused, bool composing) =>
            held && !wasHeld && !textInputFocused && !composing;

        private static bool IsTextInputFocused()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && (selected.GetComponentInParent<InputField>()?.isFocused == true ||
                selected.GetComponentInParent<TMP_InputField>()?.isFocused == true);
        }

        public void SetDisplayMode(FullScreenMode mode)
        {
            if (!SupportsDisplaySettings || !GameSettings.IsDisplayMode(mode)) return;
            Values.displayMode = mode;
            if (GameSettings.IsFullscreenMode(mode)) Values.lastFullscreenMode = mode;
            ApplyDisplay();
            Commit();
        }

        public void ToggleDisplayMode()
        {
            if (!SupportsDisplaySettings || IsDisplayChangePending) return;
            Values.ToggleDisplayMode();
            ApplyDisplay();
            Commit();
        }

        public void SetResolution(Vector2Int size)
        {
            if (!SupportsDisplaySettings || !Resolutions.Contains(size)) return;
            Values.width = size.x; Values.height = size.y;
            ApplyDisplay();
            Commit();
        }

        public void SetVolumes(float master, float bgm, float se, float voice)
        {
            Values.masterVolume = GameSettings.FiniteRange(master, 0, 1, 1);
            Values.bgmVolume = GameSettings.FiniteRange(bgm, 0, 1, 1);
            Values.seVolume = GameSettings.FiniteRange(se, 0, 1, 1);
            Values.catVoiceVolume = GameSettings.FiniteRange(voice, 0, 1, 1);
            ApplyAudio(); Commit();
        }

        public void SetMouseSensitivity(float value)
        { Values.mouseSensitivity = GameSettings.FiniteRange(value, 0.1f, 5f, 1f); Commit(); }
        public void SetInvertY(bool value) { Values.invertY = value; Commit(); }
        public void SetVSync(bool value) { Values.vSync = value; ApplyVSync(); Commit(); }
        private void Commit() { GameSettingsStore.Save(Values); Changed?.Invoke(); }

        public static Vector2 LookInput(float x, float y, float authoredSensitivity)
        {
            float multiplier = instance != null ? instance.Values.mouseSensitivity : 1f;
            float ySign = instance != null && instance.Values.invertY ? -1f : 1f;
            return new Vector2(x, y * ySign) * authoredSensitivity * multiplier;
        }

        private static Vector2Int CurrentResolution()
        {
            Resolution desktop = Screen.currentResolution;
            return SupportsDisplaySettings && desktop.width > 0 && desktop.height > 0
                ? new Vector2Int(desktop.width, desktop.height) : new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        }

        private void ApplyDisplay()
        {
            if (!SupportsDisplaySettings) return;
            if (displayRoutine != null) StopCoroutine(displayRoutine);
            IsDisplayChangePending = true;
            displayRoutine = StartCoroutine(ApplyDisplayRoutine());
        }

        private IEnumerator ApplyDisplayRoutine()
        {
            FullScreenMode requested = Values.displayMode;
            Resolution[] matching = Screen.resolutions.Where(r => r.width == Values.width && r.height == Values.height).ToArray();
            if (matching.Length > 0)
            {
                double currentRate = Screen.currentResolution.refreshRateRatio.value;
                Resolution resolution = matching.OrderBy(r => Math.Abs(r.refreshRateRatio.value - currentRate)).First();
                Screen.SetResolution(Values.width, Values.height, requested, resolution.refreshRateRatio);
            }
            else Screen.SetResolution(Values.width, Values.height, requested);
            for (int frame = 0; frame < 120; frame++)
            {
                yield return null;
                if (Screen.fullScreenMode == requested && Screen.width == Values.width && Screen.height == Values.height)
                { IsDisplayChangePending = false; displayRoutine = null; yield break; }
            }
            // Driver/monitor rejection must not leave the UI or persisted value claiming success.
            Debug.LogWarning("[SettingsManager] 表示変更を確認できませんでした。現在の表示へ設定を戻します。");
            Values.displayMode = GameSettings.IsDisplayMode(Screen.fullScreenMode) ? Screen.fullScreenMode : FullScreenMode.FullScreenWindow;
            Values.width = Mathf.Max(1, Screen.width); Values.height = Mathf.Max(1, Screen.height);
            Resolutions = GameSettings.ResolutionChoices(Screen.resolutions, new Vector2Int(Values.width, Values.height));
            IsDisplayChangePending = false; displayRoutine = null;
            Commit();
        }

        private void ApplyVSync() => QualitySettings.vSyncCount = Values.vSync ? Mathf.Max(1, initialVSyncCount) : 0;

        private void ApplyAudio()
        {
            if (mixer == null) return;
            foreach (var pair in new[] {
                new KeyValuePair<string,float>("MasterVolume", Values.masterVolume),
                new KeyValuePair<string,float>("BgmVolume", Values.bgmVolume),
                new KeyValuePair<string,float>("SeVolume", Values.seVolume),
                new KeyValuePair<string,float>("CatVoiceVolume", Values.catVoiceVolume) })
                if (!mixer.SetFloat(pair.Key, GameSettings.VolumeToDecibels(pair.Value)))
                    Debug.LogError("[SettingsManager] Mixer parameterがありません: " + pair.Key);
        }

        private static string GroupName(SettingsAudioCategory category) =>
            category == SettingsAudioCategory.Bgm ? "BGM" : category == SettingsAudioCategory.CatVoice ? "CatVoice" : "SE";

        public static void RouteAudio(AudioSource source, SettingsAudioCategory category)
        {
            if (source == null || !Application.isPlaying) return;
            var manager = EnsureInstance();
            if (manager.groups.TryGetValue(category, out AudioMixerGroup group) && group != null)
                source.outputAudioMixerGroup = group;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => DiscoverAudioSources();

        private void DiscoverAudioSources()
        {
            foreach (AudioSource source in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (!source.gameObject.scene.IsValid() || source.outputAudioMixerGroup != null) continue;
                SettingsAudioCategory category = source.GetComponentInParent<BackgroundMusicController>(true) != null
                    ? SettingsAudioCategory.Bgm : source.GetComponentInParent<CatPurrAudioController>(true) != null
                    && source.name == "CatPurrAudioSource" ? SettingsAudioCategory.CatVoice : SettingsAudioCategory.Se;
                RouteAudio(source, category);
            }
            // Some legacy Timeline audio outputs have no source binding. Supply a routed SE source
            // before those directors play, without changing the clips, tracks, or authored bindings.
            foreach (PlayableDirector director in Resources.FindObjectsOfTypeAll<PlayableDirector>())
            {
                if (!director.gameObject.scene.IsValid() || director.playableAsset == null) continue;
                foreach (PlayableBinding output in director.playableAsset.outputs)
                {
                    if (output.outputTargetType != typeof(AudioSource) || director.GetGenericBinding(output.sourceObject) != null) continue;
                    var child = new GameObject("SettingsTimelineAudio");
                    child.transform.SetParent(director.transform, false);
                    AudioSource source = child.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    RouteAudio(source, SettingsAudioCategory.Se);
                    director.SetGenericBinding(output.sourceObject, source);
                }
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this) instance = null;
        }
    }
}
