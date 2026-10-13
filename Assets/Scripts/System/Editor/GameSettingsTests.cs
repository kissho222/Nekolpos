using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Nekolpos.System.Editor.Tests
{
    public sealed class GameSettingsTests
    {
        private string saved;
        private bool hadSaved;
        private int oldBgm;
        private int oldSe;
        private bool hadBgm;
        private bool hadSe;

        [SetUp]
        public void SetUp()
        {
            hadSaved = PlayerPrefs.HasKey(GameSettingsStore.Key);
            saved = PlayerPrefs.GetString(GameSettingsStore.Key);
            hadBgm = PlayerPrefs.HasKey(GameSettingsStore.LegacyBgmKey);
            hadSe = PlayerPrefs.HasKey(GameSettingsStore.LegacySeKey);
            oldBgm = PlayerPrefs.GetInt(GameSettingsStore.LegacyBgmKey);
            oldSe = PlayerPrefs.GetInt(GameSettingsStore.LegacySeKey);
            PlayerPrefs.DeleteKey(GameSettingsStore.Key);
            PlayerPrefs.DeleteKey(GameSettingsStore.LegacyBgmKey);
            PlayerPrefs.DeleteKey(GameSettingsStore.LegacySeKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (hadSaved) PlayerPrefs.SetString(GameSettingsStore.Key, saved); else PlayerPrefs.DeleteKey(GameSettingsStore.Key);
            if (hadBgm) PlayerPrefs.SetInt(GameSettingsStore.LegacyBgmKey, oldBgm); else PlayerPrefs.DeleteKey(GameSettingsStore.LegacyBgmKey);
            if (hadSe) PlayerPrefs.SetInt(GameSettingsStore.LegacySeKey, oldSe); else PlayerPrefs.DeleteKey(GameSettingsStore.LegacySeKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void NewProfilePreservesAuthoredControlsAndDesktopDefaults()
        {
            var values = GameSettingsStore.Load(GameSettings.Defaults(2560, 1440, true));
            Assert.That(values.displayMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(values.width, Is.EqualTo(2560));
            Assert.That(values.height, Is.EqualTo(1440));
            Assert.That(values.vSync, Is.True);
            Assert.That(values.mouseSensitivity, Is.EqualTo(1));
            Assert.That(values.invertY, Is.False);
            Assert.That(new[] { values.masterVolume, values.bgmVolume, values.seVolume, values.catVoiceVolume }, Is.All.EqualTo(1));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void LegacyMuteMigratesOnceAndNewVolumesTakePrecedence(int bgm, int se)
        {
            PlayerPrefs.SetInt(GameSettingsStore.LegacyBgmKey, bgm);
            PlayerPrefs.SetInt(GameSettingsStore.LegacySeKey, se);
            var values = GameSettingsStore.Load(GameSettings.Defaults(1920, 1080, false));
            Assert.That(values.bgmVolume, Is.EqualTo(bgm == 1 ? 0 : 1));
            Assert.That(values.seVolume, Is.EqualTo(se == 1 ? 0 : 1));
            Assert.That(values.catVoiceVolume, Is.EqualTo(values.seVolume));
            values.bgmVolume = .35f;
            GameSettingsStore.Save(values);
            Assert.That(PlayerPrefs.HasKey(GameSettingsStore.LegacyBgmKey), Is.False);
            PlayerPrefs.SetInt(GameSettingsStore.LegacyBgmKey, 1);
            Assert.That(GameSettingsStore.Load(GameSettings.Defaults(1920, 1080, false)).bgmVolume, Is.EqualTo(.35f));
        }

        [Test]
        public void EverySettingSurvivesSerializationAndNewLoad()
        {
            var values = GameSettings.Defaults(1280, 720, true);
            values.displayMode = FullScreenMode.Windowed; values.lastFullscreenMode = FullScreenMode.ExclusiveFullScreen;
            values.masterVolume = .4f; values.bgmVolume = .2f; values.seVolume = .6f; values.catVoiceVolume = .8f;
            values.mouseSensitivity = 2.5f; values.invertY = true;
            GameSettingsStore.Save(values);
            Assert.That(JsonUtility.ToJson(GameSettingsStore.Load(GameSettings.Defaults(1, 1, false))), Is.EqualTo(JsonUtility.ToJson(values)));
        }

        [TestCase("broken")]
        [TestCase("{\"version\":2,\"width\":0,\"masterVolume\":0}")]
        public void InvalidOrFutureSaveReportsAndRecoversPristineDefaults(string json)
        {
            PlayerPrefs.SetString(GameSettingsStore.Key, json);
            LogAssert.Expect(LogType.Warning, new Regex("\\[GameSettings\\].*"));
            var values = GameSettingsStore.Load(GameSettings.Defaults(1920, 1080, true));
            Assert.That(values.width, Is.EqualTo(1920)); Assert.That(values.masterVolume, Is.EqualTo(1));
            Assert.That(values.vSync, Is.True);
        }

        [Test]
        public void UnsupportedDimensionsAndNonFiniteValuesRecoverSafely()
        {
            var values = new GameSettings { width = 99999, height = -1, mouseSensitivity = float.NaN,
                masterVolume = float.PositiveInfinity, seVolume = -3, bgmVolume = 5,
                displayMode = (FullScreenMode)99, lastFullscreenMode = FullScreenMode.Windowed };
            values.Validate(new[] { new Vector2Int(1280, 720) }, new Vector2Int(1280, 720));
            Assert.That(new Vector2Int(values.width, values.height), Is.EqualTo(new Vector2Int(1280, 720)));
            Assert.That(values.masterVolume, Is.EqualTo(1)); Assert.That(values.bgmVolume, Is.EqualTo(1));
            Assert.That(values.seVolume, Is.EqualTo(0)); Assert.That(values.mouseSensitivity, Is.EqualTo(1));
            Assert.That(values.displayMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(values.lastFullscreenMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
        }

        [Test]
        public void ResolutionsDeduplicateRefreshRatesAndRetainSafeCurrentValue()
        {
            var choices = GameSettings.ResolutionChoices(new[] {
                new Resolution { width = 1920, height = 1080 }, new Resolution { width = 1920, height = 1080 },
                new Resolution { width = 1280, height = 720 }, new Resolution { width = 0, height = 0 } }, new Vector2Int(1600, 900));
            Assert.That(choices, Is.EqualTo(new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) }));
        }

        [TestCase(FullScreenMode.FullScreenWindow)]
        [TestCase(FullScreenMode.ExclusiveFullScreen)]
        public void DisplayToggleReturnsToLastFullscreenMode(FullScreenMode mode)
        {
            var values = new GameSettings { displayMode = mode };
            values.ToggleDisplayMode(); Assert.That(values.displayMode, Is.EqualTo(FullScreenMode.Windowed));
            values.ToggleDisplayMode(); Assert.That(values.displayMode, Is.EqualTo(mode));
        }

        [Test]
        public void ShortcutIsOneShotAndRejectsTextOrImeComposition()
        {
            Assert.That(SettingsManager.ShouldToggleShortcut(true, false, false, false), Is.True);
            Assert.That(SettingsManager.ShouldToggleShortcut(true, true, false, false), Is.False);
            Assert.That(SettingsManager.ShouldToggleShortcut(true, false, true, false), Is.False);
            Assert.That(SettingsManager.ShouldToggleShortcut(true, false, false, true), Is.False);
            Assert.That(SettingsManager.ShouldToggleShortcut(false, true, false, false), Is.False);
        }

        [TestCase(0, -80)]
        [TestCase(1, 0)]
        [TestCase(.1f, -20)]
        public void VolumeUsesLogarithmicMixerGain(float volume, float expected) =>
            Assert.That(GameSettings.VolumeToDecibels(volume), Is.EqualTo(expected).Within(.001));

        [Test]
        public void SettingsPrefabHasAllAuthoredReferences()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Nekolpos.EditorTools.FixedUiMigration.SettingsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent<SettingsPanel>();
            Assert.That(view.HasValidReferences, Is.True);
            Assert.That(prefab.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(5));
            Assert.That(prefab.GetComponentsInChildren<TMP_Dropdown>(true).Length, Is.EqualTo(2));
            Assert.That(prefab.GetComponentsInChildren<Button>(true).Any(button => button.name == "Back"), Is.True);
        }

        [Test]
        public void IncompleteSettingsViewReportsErrorWithoutCreatingUi()
        {
            var root = new GameObject("Incomplete settings", typeof(RectTransform));
            root.SetActive(false);
            try
            {
                var view = root.AddComponent<SettingsPanel>();
                LogAssert.Expect(LogType.Error, new Regex("SerializeField参照が不足"));
                view.Show(view);
                Assert.That(root.activeSelf, Is.False);
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator VolumePreviewStopsOnReleaseAndCloseAndUsesIndependentSources()
        {
            yield return new EnterPlayMode();
            var manager = SettingsManager.EnsureInstance();
            string saved = JsonUtility.ToJson(manager.Values);
            float timeScale = Time.timeScale;
            var root = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Nekolpos.EditorTools.FixedUiMigration.SettingsPrefabPath));
            var owner = new GameObject("Preview owner");
            var eventRoot = new GameObject("Preview events", typeof(UnityEngine.EventSystems.EventSystem));
            var gameplay = new GameObject("Independent gameplay purr", typeof(AudioSource));
            try
            {
                Time.timeScale = 0;
                var panel = root.GetComponent<SettingsPanel>();
                panel.Show(owner.transform);
                var pointers = root.GetComponentsInChildren<SettingsVolumePreviewPointer>(true);
                Assert.That(pointers.Length, Is.EqualTo(2));
                var sources = panel.GetComponents<AudioSource>();
                var se = sources.Single(source => !source.loop);
                var voice = sources.Single(source => source.loop);
                Assert.That(se.outputAudioMixerGroup.name, Is.EqualTo("SE"));
                Assert.That(voice.outputAudioMixerGroup.name, Is.EqualTo("CatVoice"));
                Assert.That(voice.ignoreListenerPause, Is.True);
                var original = gameplay.GetComponent<AudioSource>();
                original.clip = voice.clip; original.loop = true; original.Play();
                var data = new UnityEngine.EventSystems.PointerEventData(eventRoot.GetComponent<UnityEngine.EventSystems.EventSystem>()) { pointerId = 17 };
                pointers[1].OnPointerDown(data);
                Assert.That(voice.isPlaying, Is.True);
                pointers[1].GetComponent<Slider>().value = .27f;
                yield return null;
                Assert.That(voice.isPlaying, Is.True);
                Assert.That(voice.outputAudioMixerGroup.audioMixer.GetFloat("CatVoiceVolume", out float db), Is.True);
                Assert.That(db, Is.EqualTo(GameSettings.VolumeToDecibels(.27f)).Within(.001f));
                pointers[1].OnPointerUp(new UnityEngine.EventSystems.PointerEventData(eventRoot.GetComponent<UnityEngine.EventSystems.EventSystem>()) { pointerId = 18 });
                Assert.That(voice.isPlaying, Is.True, "Another touch must not stop the active touch.");
                pointers[1].OnPointerUp(data);
                Assert.That(voice.isPlaying, Is.False);
                Assert.That(original.isPlaying, Is.True);
                pointers[0].OnPointerDown(data);
                float next = (float)typeof(SettingsPanel).GetField("nextSePreviewTime", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance).GetValue(panel);
                for (int i = 1; i <= 20; i++) pointers[0].GetComponent<Slider>().value = i / 25f;
                Assert.That((float)typeof(SettingsPanel).GetField("nextSePreviewTime", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance).GetValue(panel), Is.EqualTo(next));
                pointers[0].OnPointerUp(data);
                Assert.That(se.isPlaying, Is.False);
                pointers[1].OnPointerDown(data);
                SettingsPanel.Close();
                Assert.That(voice.isPlaying, Is.False);
                Assert.That(original.isPlaying, Is.True);
                panel.Show(owner.transform);
                Assert.That(voice.isPlaying, Is.False);
                Assert.That(panel.GetComponents<AudioSource>().Length, Is.EqualTo(2));
            }
            finally
            {
                SettingsPanel.Close();
                var values = JsonUtility.FromJson<GameSettings>(saved);
                manager.SetVolumes(values.masterVolume, values.bgmVolume, values.seVolume, values.catVoiceVolume);
                Time.timeScale = timeScale;
                Object.DestroyImmediate(root); Object.DestroyImmediate(owner);
                Object.DestroyImmediate(eventRoot); Object.DestroyImmediate(gameplay);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator AuthoredTitleAndPauseShareSettingsAndPreserveGeometry()
        {
            yield return new EnterPlayMode();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/MainScene.unity", new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity"));
            var bootstrap = Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance;
            if (!bootstrap.enabled)
            {
                bootstrap.enabled = true;
                typeof(NekolposGameBootstrap).GetMethod("Awake", flags).Invoke(bootstrap, null);
            }
            yield return new WaitForSecondsRealtime(.2f);
            var intro = Object.FindFirstObjectByType<OpenBetaIntroPopupPanel>(FindObjectsInactive.Include);
            var pause = Object.FindFirstObjectByType<OpenBetaPauseMenuController>(FindObjectsInactive.Include);
            if (!pause.enabled) pause.enabled = true;
            var settings = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
            var titleButton = (Button)typeof(OpenBetaIntroPopupPanel).GetField("settingsButton", flags).GetValue(intro);
            var pauseButton = (Button)typeof(OpenBetaPauseMenuController).GetField("settingsButton", flags).GetValue(pause);
            var menuButton = (Button)typeof(OpenBetaPauseMenuController).GetField("menuButton", flags).GetValue(pause);
            var rect = (RectTransform)menuButton.transform;
            Vector2 originalPosition = rect.anchoredPosition;
            Vector2 originalSize = rect.sizeDelta;
            var layout = settings.transform.Find("ModalShade/Window").GetComponent<LayoutElement>();
            float originalWidth = layout.preferredWidth;
            float originalScale = Time.timeScale;
            try
            {
                rect.anchoredPosition += new Vector2(9, 11);
                rect.sizeDelta += new Vector2(13, 17);
                var editedPosition = rect.anchoredPosition;
                var editedSize = rect.sizeDelta;
                layout.preferredWidth = 847;
                titleButton.onClick.Invoke();
                Assert.That(SettingsPanel.Current, Is.SameAs(settings));
                Assert.That(SettingsPanel.IsOpen, Is.True);
                settings.GetComponentsInChildren<Button>(true).Single(button => button.name == "Back").onClick.Invoke();
                Assert.That(SettingsPanel.IsOpen, Is.False);
                pause.OpenMenu();
                pauseButton.onClick.Invoke();
                Assert.That(SettingsPanel.Current, Is.SameAs(settings));
                Assert.That(SettingsPanel.IsOpen, Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                SettingsPanel.Close();
                Assert.That(pause.IsPaused, Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                yield return new WaitForSecondsRealtime(1.1f);
                Assert.That(rect.anchoredPosition, Is.EqualTo(editedPosition));
                Assert.That(rect.sizeDelta, Is.EqualTo(editedSize));
                Assert.That(layout.preferredWidth, Is.EqualTo(847));
                var close = (Button)typeof(OpenBetaPauseMenuController).GetField("closeMenuButton", flags).GetValue(pause);
                close.onClick.Invoke();
                Assert.That(pause.IsPaused, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(originalScale));
                Assert.That(pause.GetComponentsInChildren<Button>(true).Any(button => button.name == "BGMButton" || button.name == "SEButton"), Is.False);
            }
            finally
            {
                SettingsPanel.Close(); pause.CloseMenu();
                rect.anchoredPosition = originalPosition; rect.sizeDelta = originalSize;
                layout.preferredWidth = originalWidth; Time.timeScale = originalScale;
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SharedPanelAppliesMixerAndLookSettingsWithoutUnpausing()
        {
            int originalVSync = QualitySettings.vSyncCount;
            yield return new EnterPlayMode();
            var manager = SettingsManager.EnsureInstance();
            string originalValues = JsonUtility.ToJson(manager.Values);
            GameObject owner = new GameObject("Settings test owner");
            GameObject audio = new GameObject("Settings test audio");
            var authoredView = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Nekolpos.EditorTools.FixedUiMigration.SettingsPrefabPath));
            float originalTime = Time.timeScale;
            try
            {
                AudioSource source = audio.AddComponent<AudioSource>(); source.volume = .32f;
                SettingsManager.RouteAudio(source, SettingsAudioCategory.CatVoice);
                Assert.That(source.outputAudioMixerGroup.name, Is.EqualTo("CatVoice"));
                manager.SetVolumes(.5f, .2f, .8f, 0);
                AudioMixer mixer = source.outputAudioMixerGroup.audioMixer;
                Assert.That(mixer.GetFloat("CatVoiceVolume", out float voice), Is.True);
                Assert.That(voice, Is.EqualTo(-80));
                Assert.That(mixer.GetFloat("SeVolume", out float se), Is.True);
                Assert.That(se, Is.EqualTo(GameSettings.VolumeToDecibels(.8f)).Within(.001));
                Assert.That(source.volume, Is.EqualTo(.32f), "Mixer gain must preserve authored source fades.");
                manager.SetMouseSensitivity(2); manager.SetInvertY(true);
                Assert.That(SettingsManager.LookInput(1, 1, 3), Is.EqualTo(new Vector2(6, -6)));
                manager.SetVSync(false); Assert.That(QualitySettings.vSyncCount, Is.Zero);
                Time.timeScale = 0;
                SettingsPanel.Open(owner.transform, null);
                var first = SettingsPanel.Current;
                Assert.That(first, Is.Not.Null);
                var window = first.transform.Find("ModalShade/Window").GetComponent<LayoutElement>();
                float authoredWidth = window.preferredWidth;
                window.preferredWidth = 867;
                int authoredControls = first.GetComponentsInChildren<Selectable>(true).Length;
                yield return new WaitForSecondsRealtime(.1f);
                Canvas.ForceUpdateCanvases();
                foreach (Slider slider in first.GetComponentsInChildren<Slider>())
                    Assert.That(slider.handleRect.rect.height, Is.LessThanOrEqualTo(((RectTransform)slider.transform).rect.height));
                foreach (TMP_Dropdown dropdown in first.GetComponentsInChildren<TMP_Dropdown>())
                {
                    bool enabled = dropdown.interactable;
                    dropdown.interactable = true;
                    dropdown.Show();
                    Assert.That(dropdown.IsExpanded, Is.True);
                    Assert.That(dropdown.options.Count, Is.GreaterThan(0));
                    Canvas.ForceUpdateCanvases();
                    RectTransform viewport = (RectTransform)dropdown.transform.Find("Dropdown List/Viewport");
                    var popup = (RectTransform)viewport.parent;
                    var controlCorners = new Vector3[4];
                    ((RectTransform)dropdown.transform).GetWorldCorners(controlCorners);
                    var popupCorners = new Vector3[4];
                    popup.GetWorldCorners(popupCorners);
                    Assert.That(popupCorners[0].x, Is.EqualTo(controlCorners[0].x).Within(.01f));
                    Assert.That(popupCorners[2].x, Is.EqualTo(controlCorners[2].x).Within(.01f));
                    Assert.That(popupCorners[1].y, Is.EqualTo(controlCorners[0].y).Within(.01f));
                    var scroll = popup.GetComponent<ScrollRect>();
                    scroll.verticalNormalizedPosition = 0;
                    Canvas.ForceUpdateCanvases();
                    foreach (Toggle option in viewport.GetComponentsInChildren<Toggle>().TakeLast(1))
                    {
                        var corners = new Vector3[4];
                        ((RectTransform)option.transform).GetWorldCorners(corners);
                        foreach (Vector3 corner in corners)
                        {
                            float y = viewport.InverseTransformPoint(corner).y;
                            Assert.That(y, Is.InRange(viewport.rect.yMin - .01f, viewport.rect.yMax + .01f));
                        }
                    }
                    dropdown.Hide();
                    dropdown.interactable = enabled;
                    yield return new WaitForSecondsRealtime(.2f);
                }
                SettingsPanel.Close();
                Assert.That(Time.timeScale, Is.Zero);
                SettingsPanel.Open(owner.transform, null);
                Assert.That(SettingsPanel.Current, Is.SameAs(first));
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(window.preferredWidth, Is.EqualTo(867), "Opening and updates must preserve authored layout.");
                Assert.That(first.GetComponentsInChildren<Selectable>(true).Length, Is.EqualTo(authoredControls));
                window.preferredWidth = authoredWidth;
                Assert.That(first.GetComponentsInChildren<Slider>().Length, Is.EqualTo(5));
                Slider master = first.GetComponentsInChildren<Slider>().First(s => s.transform.parent.name == "Master");
                master.value = 0;
                mixer.GetFloat("MasterVolume", out float gain);
                Assert.That(gain, Is.EqualTo(-80));
                SettingsPanel.Close();
                Assert.That(Time.timeScale, Is.Zero);
            }
            finally
            {
                SettingsPanel.Close();
                if (SettingsPanel.Current != null) Object.DestroyImmediate(SettingsPanel.Current.gameObject);
                JsonUtility.FromJsonOverwrite(originalValues, manager.Values);
                manager.SetVolumes(manager.Values.masterVolume, manager.Values.bgmVolume, manager.Values.seVolume, manager.Values.catVoiceVolume);
                Time.timeScale = originalTime;
                QualitySettings.vSyncCount = originalVSync;
                Object.DestroyImmediate(owner); Object.DestroyImmediate(audio);
                if (authoredView != null) Object.DestroyImmediate(authoredView);
            }
            yield return new ExitPlayMode();
        }
    }
}
