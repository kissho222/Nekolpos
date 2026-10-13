using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>State and value binding for the shared, Editor-authored settings view.</summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Header("Authored View")]
        [SerializeField] private TMP_Dropdown displayMode;
        [SerializeField] private TMP_Dropdown resolution;
        [SerializeField] private List<Slider> volumeSliders = new List<Slider>();
        [SerializeField] private List<TMP_Text> volumeLabels = new List<TMP_Text>();
        [SerializeField] private Slider sensitivity;
        [SerializeField] private TMP_Text sensitivityValue;
        [SerializeField] private Toggle invertY;
        [SerializeField] private Toggle vSync;
        [SerializeField] private Button backButton;
        [SerializeField] private GameObject displayPlatformNotice;
        [Header("Audio Preview")]
        [SerializeField] private AudioClip sePreviewClip;
        [SerializeField] private AudioClip catVoicePreviewClip;
        [SerializeField, Range(0.2f, 0.4f)] private float sePreviewCooldown = 0.3f;
        private AudioSource sePreviewSource;
        private AudioSource catVoicePreviewSource;
        private float nextSePreviewTime;
        private bool sePreviewHeld;
        private static SettingsPanel instance;
        private static int closedFrame = -1;
        private SettingsManager manager;
        private Component owner;
        private Action onBack;
        private bool initialized;
        private static readonly FullScreenMode[] DisplayModes = {
            FullScreenMode.Windowed, FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow };
        public static bool IsOpen => instance != null && instance.gameObject.activeInHierarchy;
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        public static SettingsPanel Current => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { instance = null; closedFrame = -1; }

        public bool HasValidReferences => displayMode != null && resolution != null &&
            volumeSliders.Count == 4 && volumeLabels.Count == 4 &&
            volumeSliders.All(slider => slider != null) && volumeLabels.All(label => label != null) &&
            sensitivity != null && sensitivityValue != null && invertY != null && vSync != null && backButton != null;

        // Legacy callers may use this overload; it only locates an already authored scene view.
        public static void Open(Component owner, TMP_Text textTemplate, Action onBack = null)
        {
            SettingsPanel panel = instance;
            if (panel == null || (owner != null && panel.gameObject.scene != owner.gameObject.scene))
                panel = FindObjectsByType<SettingsPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(view => owner == null || view.gameObject.scene == owner.gameObject.scene);
            if (panel == null)
            {
                Debug.LogError("[SettingsPanel] Sceneに設定画面がありません。Editorの固定UI移行を実行してください。", owner);
                return;
            }
            panel.Show(owner, onBack);
        }

        public void Show(Component target, Action back = null)
        {
            if (!HasValidReferences)
            {
                Debug.LogError("[SettingsPanel] 設定UIのSerializeField参照が不足しています。Prefabを確認してください。", this);
                return;
            }
            if (instance != null && instance != this && IsOpen) Close();
            instance = this;
            owner = target;
            onBack = back;
            Initialize();
            gameObject.SetActive(true);
            Refresh();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Initialize()
        {
            if (initialized) return;
            manager = SettingsManager.EnsureInstance();
            displayMode.onValueChanged.AddListener(SetDisplayMode);
            resolution.onValueChanged.AddListener(SetResolution);
            foreach (Slider slider in volumeSliders) slider.onValueChanged.AddListener(SetVolumes);
            InitializeAudioPreview();
            volumeSliders[2].onValueChanged.AddListener(PreviewSe);
            sensitivity.onValueChanged.AddListener(manager.SetMouseSensitivity);
            invertY.onValueChanged.AddListener(manager.SetInvertY);
            vSync.onValueChanged.AddListener(manager.SetVSync);
            backButton.onClick.AddListener(Close);
            displayMode.interactable = resolution.interactable = SettingsManager.SupportsDisplaySettings;
            if (displayPlatformNotice != null) displayPlatformNotice.SetActive(!SettingsManager.SupportsDisplaySettings);
#if UNITY_WEBGL && !UNITY_EDITOR
            vSync.interactable = false;
#endif
            initialized = true;
            if (isActiveAndEnabled) manager.Changed += Refresh;
        }

        private void SetDisplayMode(int value)
        {
            if (value >= 0 && value < DisplayModes.Length) manager.SetDisplayMode(DisplayModes[value]);
        }
        private void SetResolution(int value)
        {
            if (value >= 0 && value < manager.Resolutions.Count) manager.SetResolution(manager.Resolutions[value]);
        }
        private void SetVolumes(float value) => manager.SetVolumes(
            volumeSliders[0].value, volumeSliders[1].value, volumeSliders[2].value, volumeSliders[3].value);

        private void InitializeAudioPreview()
        {
            if (sePreviewClip == null) sePreviewClip = Resources.Load<AudioClip>("SE/step");
            if (catVoicePreviewClip == null) catVoicePreviewClip = Resources.Load<AudioClip>("SE/goro");
            sePreviewSource = CreatePreviewSource(sePreviewClip, SettingsAudioCategory.Se, false);
            catVoicePreviewSource = CreatePreviewSource(catVoicePreviewClip, SettingsAudioCategory.CatVoice, true);
            BindPreviewPointer(volumeSliders[2], false);
            BindPreviewPointer(volumeSliders[3], true);
        }

        private AudioSource CreatePreviewSource(AudioClip clip, SettingsAudioCategory category, bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            SettingsManager.RouteAudio(source, category);
            return source;
        }

        private void BindPreviewPointer(Slider slider, bool catVoice)
        {
            var pointer = slider.GetComponent<SettingsVolumePreviewPointer>();
            if (pointer == null) pointer = slider.gameObject.AddComponent<SettingsVolumePreviewPointer>();
            pointer.Bind(this, slider, catVoice);
        }

        internal void BeginAudioPreview(bool catVoice)
        {
            if (!isActiveAndEnabled) return;
            if (catVoice)
            {
                if (catVoicePreviewSource != null && catVoicePreviewSource.clip != null && !catVoicePreviewSource.isPlaying)
                    catVoicePreviewSource.Play();
            }
            else
            {
                sePreviewHeld = true;
                PreviewSe(0f);
            }
        }

        internal void EndAudioPreview(bool catVoice)
        {
            if (catVoice) { if (catVoicePreviewSource != null) catVoicePreviewSource.Stop(); }
            else { sePreviewHeld = false; if (sePreviewSource != null) sePreviewSource.Stop(); }
        }

        private void PreviewSe(float value)
        {
            if (!sePreviewHeld || sePreviewSource == null || sePreviewSource.clip == null ||
                Time.unscaledTime < nextSePreviewTime) return;
            nextSePreviewTime = Time.unscaledTime + Mathf.Clamp(sePreviewCooldown, 0.2f, 0.4f);
            sePreviewSource.Play();
        }

        private void StopAudioPreviews()
        {
            EndAudioPreview(false);
            EndAudioPreview(true);
            nextSePreviewTime = 0f;
        }

        public static void Close()
        {
            if (!IsOpen) return;
            Action callback = instance.onBack;
            instance.onBack = null;
            instance.owner = null;
            instance.gameObject.SetActive(false);
            closedFrame = Time.frameCount;
            callback?.Invoke();
        }
        public static void CloseForOwner(Component target)
        {
            if (instance != null && instance.owner == target) Close();
        }
        private void Update()
        {
            if (owner == null || !owner.gameObject.activeInHierarchy || (!DebugConsoleInput.IsBlocked && Input.GetKeyDown(KeyCode.Escape))) Close();
        }
        private void OnEnable() { if (initialized) manager.Changed += Refresh; }
        private void OnDisable() { StopAudioPreviews(); if (manager != null) manager.Changed -= Refresh; }
        private void OnApplicationFocus(bool focused) { if (!focused) StopAudioPreviews(); }
        private void OnApplicationPause(bool paused) { if (paused) StopAudioPreviews(); }
        private void OnDestroy() { if (instance == this) instance = null; }
        private void Refresh()
        {
            if (manager == null || displayMode == null) return;
            GameSettings values = manager.Values;
            displayMode.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(DisplayModes, values.displayMode)));
            resolution.ClearOptions();
            resolution.AddOptions(manager.Resolutions.Select(size => size.x + " × " + size.y).ToList());
            int index = manager.Resolutions.ToList().IndexOf(new Vector2Int(values.width, values.height));
            resolution.SetValueWithoutNotify(Mathf.Max(0, index));
            float[] volumes = { values.masterVolume, values.bgmVolume, values.seVolume, values.catVoiceVolume };
            for (int i = 0; i < volumes.Length; i++)
            {
                volumeSliders[i].SetValueWithoutNotify(volumes[i]);
                volumeLabels[i].text = Mathf.RoundToInt(volumes[i] * 100) + "%";
            }
            sensitivity.SetValueWithoutNotify(values.mouseSensitivity);
            sensitivityValue.text = values.mouseSensitivity.ToString("0.00") + "×";
            invertY.SetIsOnWithoutNotify(values.invertY);
            vSync.SetIsOnWithoutNotify(values.vSync);
        }

    }
}
