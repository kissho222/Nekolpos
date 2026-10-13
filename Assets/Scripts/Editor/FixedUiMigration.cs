using System;
using System.Collections.Generic;
using System.Linq;
using Nekolpos.System;
using Nekolpos.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools
{
    /// <summary>Adds only missing fixed UI; existing authored geometry is never reset.</summary>
    public static class FixedUiMigration
    {
        public const string SettingsPrefabPath = "Assets/Prefab/SettingsPanel.prefab";

        [MenuItem("Nekolpos/UI/Migrate Missing Fixed UI")]
        public static void MigrateOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("固定UIの移行はPlay停止中に実行してください。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var intro = SceneComponent<OpenBetaIntroPopupPanel>();
            var pause = SceneComponent<OpenBetaPauseMenuController>();
            if (intro == null || pause == null)
                throw new InvalidOperationException("タイトルとPauseを持つMainSceneを開いてください。");
            foreach (var item in new[] {
                new[] { "StartDemoButton", "startButton", "startButtonLabel" },
                new[] { "AboutButton", "aboutButton", "aboutButtonLabel" },
                new[] { "QuitButton", "quitButton", "quitButtonLabel" },
                new[] { "SteamButton", "steamButton", "steamButtonLabel" },
                new[] { "WorldviewButton", "worldviewButton", "worldviewButtonLabel" },
                new[] { "TrainingDataButton", "trainingDataButton", "trainingDataButtonLabel" },
                new[] { "CreditButton", "creditButton", "creditButtonLabel" } })
            {
                var button = Find<Button>(intro.transform, item[0]);
                if (button == null) continue;
                SetMissingReference(intro, item[1], button);
                SetMissingReference(intro, item[2], button.GetComponentInChildren<TMP_Text>(true));
            }
            SetMissingReference(intro, "precautionsText", Find<TMP_Text>(intro.transform, "PrecautionsText"));
            var startButton = Find<Button>(intro.transform, "StartDemoButton");
            var startButtonLabel = startButton.GetComponentInChildren<TMP_Text>(true);
            var settings = SceneComponent<SettingsPanel>();
            if (settings == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
                if (prefab == null)
                {
                    var root = new SettingsViewAuthoring(startButtonLabel.font).Build();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefabPath);
                    Object.DestroyImmediate(root);
                }
                settings = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<SettingsPanel>();
                Undo.RegisterCreatedObjectUndo(settings.gameObject, "Add authored settings UI");
            }
            SetReference(intro, "settingsPanel", settings);
            var titleSettings = Find<Button>(intro.transform, "SettingsButton");
            if (titleSettings == null)
            {
                titleSettings = CreateEntryButton("SettingsButton", startButton, new Vector2(0, -112));
                titleSettings.transform.SetSiblingIndex(startButton.transform.GetSiblingIndex() + 1);
            }
            SetReference(intro, "settingsButton", titleSettings);
            var debug = Find<Button>(intro.transform, "DebugButton");
            if (debug == null)
            {
                debug = Object.Instantiate(startButton, startButton.transform.parent);
                debug.name = "DebugButton";
                debug.onClick = new Button.ButtonClickedEvent();
                ((RectTransform)debug.transform).anchoredPosition = ((RectTransform)startButton.transform).anchoredPosition + new Vector2(0, -56);
                debug.GetComponentInChildren<TMP_Text>(true).text = "デバッグ開始";
            }
            SetReference(intro, "debugButton", debug);
            SetReference(intro, "debugButtonLabel", debug.GetComponentInChildren<TMP_Text>(true));
            var skip = Find<Toggle>(intro.transform, "SkipOpeningToggle");
            if (skip == null) skip = CreateSkipOption(startButton, startButtonLabel);
            SetReference(intro, "skipOpeningToggle", skip);

            var pauseObject = new SerializedObject(pause);
            var menuPanel = pauseObject.FindProperty("menuPanel").objectReferenceValue as GameObject;
            var goTitle = Find<Button>(menuPanel.transform, "GoTitleButton");
            var pauseSettings = Find<Button>(menuPanel.transform, "SettingsButton");
            if (pauseSettings == null)
            {
                pauseSettings = CreateEntryButton("SettingsButton", goTitle, new Vector2(0, -76));
                pauseSettings.transform.SetSiblingIndex(goTitle.transform.GetSiblingIndex() + 1);
            }
            var unstuck = Find<Button>(menuPanel.transform, "UnstuckButton");
            if (unstuck == null)
            {
                unstuck = CreateEntryButton("UnstuckButton", goTitle, new Vector2(0, -152));
                unstuck.GetComponentInChildren<TMP_Text>(true).text = "スタック脱出";
                unstuck.transform.SetSiblingIndex(goTitle.transform.GetSiblingIndex() + 2);
            }
            var close = Find<Button>(menuPanel.transform, "CloseMenuButton");
            if (close == null) close = CreateCloseButton(menuPanel.transform);
            SetReference(pause, "settingsPanel", settings);
            SetReference(pause, "settingsButton", pauseSettings);
            SetReference(pause, "unstuckButton", unstuck);
            SetReference(pause, "closeMenuButton", close);
            foreach (string name in new[] { "BGMButton", "SEButton" })
            {
                var old = Find<Button>(menuPanel.transform, name);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            // Preserve Scene visuals and stop automatic theme resets.
            foreach (var theme in SceneComponents<UIThemeApplier>())
            {
                var so = new SerializedObject(theme);
                var auto = so.FindProperty("applyOnAwake");
                if (!auto.boolValue) continue;
                auto.boolValue = false;
                so.ApplyModifiedProperties();
            }
            foreach (var setup in SceneComponents<OpenBetaCharacterSetupPanel>())
            {
                var so = new SerializedObject(setup);
                var setupBack = Find<Button>(setup.transform, "BackButton");
                if (setupBack == null)
                    setupBack = OpenBetaUiFactory.CreateButton("BackButton", setup.transform, out _, new Vector2(260, 48));
                SetMissingReference(setup, "backButton", setupBack);
                SetMissingReference(setup, "backButtonLabel", setupBack.GetComponentInChildren<TMP_Text>(true));
                var warning = Find<TMP_Text>(setup.transform, "CatNameKanaWarningText");
                if (warning == null && so.FindProperty("japaneseCatNameWarningEnabled").boolValue)
                {
                    var input = so.FindProperty("catNameInput").objectReferenceValue as InputField;
                    Transform row = input != null ? input.transform.parent : setup.transform;
                    Transform parent = row.parent != null ? row.parent : setup.transform;
                    warning = OpenBetaUiFactory.CreateTmpText("CatNameKanaWarningText", parent, 18, FontStyles.Bold);
                    warning.color = new Color(.86f, .08f, .08f, 1);
                    warning.text = "ひらがな・カタカナ・漢字・数字のみ、20文字までです。";
                    var element = warning.gameObject.AddComponent<LayoutElement>();
                    element.preferredWidth = 660; element.preferredHeight = element.minHeight = 24;
                    warning.transform.SetSiblingIndex(row.GetSiblingIndex());
                    warning.gameObject.SetActive(false);
                }
                SetMissingReference(setup, "catNameKanaWarningText", warning);
                so.Update();
                var authored = so.FindProperty("useAuthoredVisuals");
                if (!authored.boolValue)
                {
                    authored.boolValue = true;
                    so.ApplyModifiedProperties();
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[FixedUiMigration] 固定UIを追加・参照接続しました。既存UIとPrefabは再生成せず、Sceneは手動保存できます。");
        }

        private static IEnumerable<T> SceneComponents<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(component => component.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        private static T SceneComponent<T>() where T : Component => SceneComponents<T>().FirstOrDefault();
        private static T Find<T>(Transform root, string name) where T : Component =>
            root.GetComponentsInChildren<T>(true).FirstOrDefault(component => component.name == name);
        private static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
        private static void SetMissingReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            if (so.FindProperty(field).objectReferenceValue == null && value != null) SetReference(target, field, value);
        }
        private static void BindSettingsReferences(SettingsPanel view)
        {
            var root = view.transform;
            Transform content = root.Find("ModalShade/Window/Viewport/Content");
            SetReference(view, "displayMode", content.Find("DisplayMode/Dropdown").GetComponent<TMP_Dropdown>());
            SetReference(view, "resolution", content.Find("Resolution/Dropdown").GetComponent<TMP_Dropdown>());
            SetReference(view, "sensitivity", content.Find("MouseSensitivity/Slider").GetComponent<Slider>());
            SetReference(view, "sensitivityValue", content.Find("MouseSensitivity/Value").GetComponent<TMP_Text>());
            SetReference(view, "invertY", content.Find("InvertY/Toggle").GetComponent<Toggle>());
            SetReference(view, "vSync", content.Find("VSync/Toggle").GetComponent<Toggle>());
            SetReference(view, "backButton", root.Find("ModalShade/Window/Back").GetComponent<Button>());
            SetReference(view, "displayPlatformNotice", content.Find("表示モード・解像度はWindows版で変更できます").gameObject);
            var so = new SerializedObject(view);
            var sliders = so.FindProperty("volumeSliders");
            var labels = so.FindProperty("volumeLabels");
            sliders.arraySize = labels.arraySize = 4;
            var names = new[] { "Master", "BGM", "SE", "Cat Voice" };
            for (int i = 0; i < names.Length; i++)
            {
                sliders.GetArrayElementAtIndex(i).objectReferenceValue = content.Find(names[i] + "/Slider").GetComponent<Slider>();
                labels.GetArrayElementAtIndex(i).objectReferenceValue = content.Find(names[i] + "/Value").GetComponent<TMP_Text>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static Button CreateCloseButton(Transform parent)
        {
            var root = new GameObject("CloseMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-12, -12);
            rect.sizeDelta = new Vector2(64, 64);
            var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelRoot.transform.SetParent(root.transform, false);
            var label = labelRoot.GetComponent<TMP_Text>();
            label.font = parent.GetComponentInChildren<TMP_Text>(true).font;
            label.fontSize = 36; label.text = "×"; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
            var button = root.GetComponent<Button>(); button.targetGraphic = root.GetComponent<Image>();
            UIStyle.ApplyButton(button);
            return button;
        }
        private static Toggle CreateSkipOption(Button startButton, TMP_Text startButtonLabel)
        {
                GameObject row = new GameObject("SkipOpeningToggle", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
                row.transform.SetParent(startButton.transform.parent, false);
                RectTransform rect = (RectTransform)row.transform;
                RectTransform startRect = (RectTransform)startButton.transform;
                rect.anchorMin = startRect.anchorMin;
                rect.anchorMax = startRect.anchorMax;
                rect.pivot = startRect.pivot;
                rect.sizeDelta = new Vector2(Mathf.Max(360f, startRect.sizeDelta.x), 48f);
                rect.anchoredPosition = startRect.anchoredPosition + new Vector2(0f, 56f);
                row.transform.SetSiblingIndex(startButton.transform.GetSiblingIndex());
                row.GetComponent<Image>().color = Color.clear;
                LayoutElement layout = row.GetComponent<LayoutElement>();
                layout.preferredWidth = rect.sizeDelta.x;
                layout.preferredHeight = 48f;
                bool horizontalButtons = startButton.transform.parent.GetComponent<HorizontalLayoutGroup>() != null;
                if (horizontalButtons)
                {
                    // Keep the existing horizontal button group intact; use the free space beside Start.
                    layout.ignoreLayout = true;
                    rect.SetParent(startRect, false);
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(260f, 64f);
                    rect.anchoredPosition = new Vector2(142f, 0f);
                }

                Image box = CreateCheckboxImage("Box", row.transform, new Vector2(28f, 28f), Color.white);
                RectTransform boxRect = box.rectTransform;
                boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
                boxRect.anchoredPosition = new Vector2(18f, 0f);
                Image check = CreateCheckboxImage("Checkmark", box.transform, new Vector2(16f, 16f), new Color(0.15f, 0.25f, 0.35f, 1f));

                TMP_Text label = Object.Instantiate(startButtonLabel, row.transform);
                label.name = "Label";
                label.text = "オープニングをスキップする";
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = Mathf.Min(label.fontSize, horizontalButtons ? 18f : 22f);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(42f, 0f);
                label.rectTransform.offsetMax = Vector2.zero;
                var skipOpeningToggle = row.GetComponent<Toggle>();
                skipOpeningToggle.targetGraphic = box;
                skipOpeningToggle.graphic = check;
                skipOpeningToggle.SetIsOnWithoutNotify(false);

            return skipOpeningToggle;
        }
        private static Image CreateCheckboxImage(string objectName, Transform parent, Vector2 size, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.rectTransform.sizeDelta = size;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button CreateEntryButton(string name, Button template, Vector2 offset)
        {
            if (template == null) return null;
            RectTransform templateRect = (RectTransform)template.transform;
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(template.transform.parent, false);
            RectTransform rect = (RectTransform)root.transform;
            rect.anchorMin = templateRect.anchorMin; rect.anchorMax = templateRect.anchorMax; rect.pivot = templateRect.pivot;
            rect.sizeDelta = templateRect.sizeDelta; rect.anchoredPosition = templateRect.anchoredPosition + offset;
            var element = root.GetComponent<LayoutElement>();
            element.preferredWidth = rect.sizeDelta.x; element.preferredHeight = rect.sizeDelta.y;
            TMP_Text templateLabel = template.GetComponentInChildren<TMP_Text>(true);
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.font = templateLabel != null ? templateLabel.font : TMP_Settings.defaultFontAsset;
            label.fontSize = templateLabel != null ? templateLabel.fontSize : 24f;
            label.alignment = TextAlignmentOptions.Center;
            label.text = "設定"; label.raycastTarget = false;
            Stretch(label.rectTransform, new Vector2(8, 4), new Vector2(-8, -4));
            Button button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            UIStyle.ApplyButton(button);
            return button;
        }

        private sealed class SettingsViewAuthoring
        {
            private Transform transform;
            private TMP_FontAsset font;
            private RectTransform content;
            private TMP_Dropdown displayMode, resolution;
            private Slider sensitivity;
            private TMP_Text sensitivityValue;
            private Toggle invertY, vSync;
            public SettingsViewAuthoring(TMP_FontAsset font) { this.font = font; }
            public GameObject Build()
            {
                var root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                root.SetActive(false);
                transform = root.transform;
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10000;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                RectTransform shade = Rect("ModalShade", transform);
                Stretch(shade, Vector2.zero, Vector2.zero);
                shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);
                RectTransform panel = Rect("Window", shade);
                panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
                panel.offsetMin = new Vector2(24, 24); panel.offsetMax = new Vector2(-24, -24);
                var shadeLayout = shade.gameObject.AddComponent<VerticalLayoutGroup>();
                shadeLayout.childAlignment = TextAnchor.MiddleCenter;
                shadeLayout.padding = new RectOffset(24, 24, 24, 24);
                shadeLayout.childControlWidth = shadeLayout.childControlHeight = true;
                shadeLayout.childForceExpandWidth = shadeLayout.childForceExpandHeight = false;
                var windowLayout = panel.gameObject.AddComponent<LayoutElement>();
                windowLayout.preferredWidth = 1000; windowLayout.preferredHeight = 950;
                panel.gameObject.AddComponent<Image>();
                UIStyle.ApplyPanel(panel.gameObject);
                TMP_Text title = Text("Title", panel, "設定", 34);
                title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1);
                title.rectTransform.anchoredPosition = new Vector2(0, -46);
                title.rectTransform.sizeDelta = new Vector2(900, 55);
                title.alignment = TextAlignmentOptions.Center;

                RectTransform viewport = Rect("Viewport", panel);
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
                viewport.offsetMin = new Vector2(40, 110); viewport.offsetMax = new Vector2(-40, -95);
                viewport.gameObject.AddComponent<Image>().color = Color.clear;
                viewport.gameObject.AddComponent<RectMask2D>();
                ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
                content = Rect("Content", viewport);
                content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 1);
                content.sizeDelta = Vector2.zero;
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 10; layout.padding = new RectOffset(12, 12, 8, 8);
                layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewport; scroll.content = content;
                scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 28;
                RectTransform scrollbarRect = Rect("Scrollbar", panel);
                scrollbarRect.anchorMin = new Vector2(1, 0); scrollbarRect.anchorMax = Vector2.one;
                scrollbarRect.offsetMin = new Vector2(-30, 110); scrollbarRect.offsetMax = new Vector2(-16, -95);
                scrollbarRect.gameObject.AddComponent<Image>().color = UIStyle.NormalButton;
                RectTransform slidingArea = Rect("SlidingArea", scrollbarRect);
                Stretch(slidingArea, Vector2.zero, Vector2.zero);
                RectTransform scrollHandle = Rect("Handle", slidingArea);
                Stretch(scrollHandle, Vector2.zero, Vector2.zero);
                Image scrollHandleImage = scrollHandle.gameObject.AddComponent<Image>();
                scrollHandleImage.color = UIStyle.NormalPressed;
                Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
                scrollbar.handleRect = scrollHandle; scrollbar.targetGraphic = scrollHandleImage;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = scrollbar;

                Section("画面");
                displayMode = Dropdown("DisplayMode", "表示モード");
                displayMode.AddOptions(new List<string> { "ウィンドウ", "フルスクリーン", "ボーダーレスフルスクリーン" });
                resolution = Dropdown("Resolution", "解像度");
            
                Section("表示モード・解像度はWindows版で変更できます", 18);

                Section("音量");
                foreach (string name in new[] { "Master", "BGM", "SE", "Cat Voice" })
                {
                    SliderRow(name, name == "Cat Voice" ? "猫のボイス" : name == "Master" ? "全体音量" : name, 0, 1, out TMP_Text valueLabel);
                }
                Section("操作");
                sensitivity = SliderRow("MouseSensitivity", "マウス感度", 0.1f, 5f, out sensitivityValue);
                invertY = ToggleRow("InvertY", "視点の上下を反転");
                vSync = ToggleRow("VSync", "垂直同期（VSync）");
    #if UNITY_WEBGL && !UNITY_EDITOR
                vSync.interactable = false;
    #endif
                RectTransform back = Rect("Back", panel);
                back.anchorMin = back.anchorMax = new Vector2(0.5f, 0);
                back.anchoredPosition = new Vector2(0, 54); back.sizeDelta = new Vector2(270, 55);
                back.gameObject.AddComponent<Image>();
                Button backButton = back.gameObject.AddComponent<Button>();
                backButton.targetGraphic = back.GetComponent<Image>();
                TMP_Text backLabel = Text("Label", back, "戻る", 24);
                backLabel.alignment = TextAlignmentOptions.Center;
                Stretch(backLabel.rectTransform, Vector2.zero, Vector2.zero);
                UIStyle.ApplyButton(backButton);
                root.AddComponent<SettingsPanel>();
                BindSettingsReferences(root.GetComponent<SettingsPanel>());
                return root;
            }
            private void Section(string label, float fontSize = 25)
            {
                TMP_Text text = Text(label, content, label, fontSize);
                text.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
                text.fontStyle = FontStyles.Bold;
            }

            private RectTransform Row(string name, string caption)
            {
                RectTransform row = Rect(name, content);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                TMP_Text label = Text("Caption", row, caption, 23);
                label.rectTransform.anchorMin = new Vector2(0, 0); label.rectTransform.anchorMax = new Vector2(0.43f, 1);
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                return row;
            }

            private Slider SliderRow(string name, string caption, float min, float max, out TMP_Text valueLabel)
            {
                RectTransform row = Row(name, caption);
                RectTransform track = Rect("Slider", row);
                track.anchorMin = new Vector2(0.44f, 0.2f); track.anchorMax = new Vector2(0.86f, 0.8f);
                track.offsetMin = track.offsetMax = Vector2.zero;
                RectTransform background = Rect("Background", track);
                Stretch(background, new Vector2(0, 12), new Vector2(0, -12));
                background.gameObject.AddComponent<Image>().color = UIStyle.NormalButton;
                RectTransform fillArea = Rect("FillArea", track); Stretch(fillArea, new Vector2(10, 12), new Vector2(-10, -12));
                RectTransform fill = Rect("Fill", fillArea); Stretch(fill, Vector2.zero, Vector2.zero);
                fill.gameObject.AddComponent<Image>().color = UIStyle.NormalPressed;
                RectTransform handleArea = Rect("HandleArea", track); Stretch(handleArea, new Vector2(10, 0), new Vector2(-10, 0));
                RectTransform handle = Rect("Handle", handleArea);
                // Slider drives Y anchors across the handle area, so additional height must be zero.
                handle.sizeDelta = new Vector2(22, 0);
                Image handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = UIStyle.NormalInput;
                Slider slider = track.gameObject.AddComponent<Slider>();
                slider.targetGraphic = handleImage; slider.fillRect = fill; slider.handleRect = handle;
                slider.minValue = min; slider.maxValue = max;
                valueLabel = Text("Value", row, "", 22);
                valueLabel.rectTransform.anchorMin = new Vector2(0.88f, 0); valueLabel.rectTransform.anchorMax = Vector2.one;
                valueLabel.rectTransform.offsetMin = valueLabel.rectTransform.offsetMax = Vector2.zero;
                valueLabel.alignment = TextAlignmentOptions.MidlineRight;
                return slider;
            }

            private Toggle ToggleRow(string name, string caption)
            {
                RectTransform row = Row(name, caption);
                RectTransform box = Rect("Toggle", row);
                box.anchorMin = box.anchorMax = new Vector2(0.65f, 0.5f); box.sizeDelta = new Vector2(36, 36);
                Image image = box.gameObject.AddComponent<Image>(); image.color = UIStyle.NormalButton;
                RectTransform check = Rect("Check", box); Stretch(check, new Vector2(8, 8), new Vector2(-8, -8));
                Image checkImage = check.gameObject.AddComponent<Image>(); checkImage.color = UIStyle.NormalText;
                Toggle toggle = box.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = image; toggle.graphic = checkImage;
                return toggle;
            }

            private TMP_Dropdown Dropdown(string name, string caption)
            {
                RectTransform row = Row(name, caption);
                RectTransform root = Rect("Dropdown", row);
                root.anchorMin = new Vector2(0.44f, 0.08f); root.anchorMax = new Vector2(1, 0.92f);
                root.offsetMin = root.offsetMax = Vector2.zero;
                Image image = root.gameObject.AddComponent<Image>(); image.color = UIStyle.NormalInput;
                TMP_Text label = Text("Value", root, "", 21);
                Stretch(label.rectTransform, new Vector2(14, 0), new Vector2(-30, 0));
                TMP_Text arrow = Text("Arrow", root, "▼", 16);
                arrow.rectTransform.anchorMin = new Vector2(1, 0); arrow.rectTransform.anchorMax = Vector2.one;
                arrow.rectTransform.offsetMin = new Vector2(-28, 0); arrow.rectTransform.offsetMax = Vector2.zero;
                TMP_Dropdown dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
                dropdown.targetGraphic = image; dropdown.captionText = label;
                RectTransform template = Rect("Template", root);
                template.anchorMin = Vector2.zero; template.anchorMax = new Vector2(1, 0);
                template.pivot = new Vector2(0.5f, 1); template.sizeDelta = new Vector2(0, 250);
                template.gameObject.AddComponent<Image>().color = UIStyle.NormalInput;
                ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
                RectTransform viewport = Rect("Viewport", template); Stretch(viewport, Vector2.zero, Vector2.zero);
                viewport.gameObject.AddComponent<RectMask2D>();
                RectTransform list = Rect("Content", viewport);
                list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one; list.pivot = new Vector2(0.5f, 1);
                list.sizeDelta = new Vector2(0, 44);
                RectTransform item = Rect("Item", list);
                // TMP_Dropdown derives popup padding from this template item's bounds.
                // Match the content bounds so every option fits inside the viewport.
                item.anchorMin = new Vector2(0, 1); item.anchorMax = Vector2.one;
                item.pivot = new Vector2(0.5f, 1); item.sizeDelta = new Vector2(0, 44);
                Image itemImage = item.gameObject.AddComponent<Image>(); itemImage.color = UIStyle.NormalInput;
                Toggle toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemImage;
                RectTransform selected = Rect("Selected", item); Stretch(selected, Vector2.zero, Vector2.zero);
                Image check = selected.gameObject.AddComponent<Image>(); check.color = UIStyle.NormalHighlighted; check.raycastTarget = false;
                toggle.graphic = check;
                TMP_Text itemLabel = Text("ItemLabel", item, "", 21);
                Stretch(itemLabel.rectTransform, new Vector2(14, 0), new Vector2(-10, 0));
                scroll.viewport = viewport; scroll.content = list; scroll.horizontal = false; scroll.scrollSensitivity = 25;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                dropdown.template = template; dropdown.itemText = itemLabel;
                template.gameObject.SetActive(false);
                return dropdown;
            }

            private TMP_Text Text(string name, Transform parent, string value, float size)
            {
                var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                root.transform.SetParent(parent, false);
                TMP_Text text = root.GetComponent<TMP_Text>();
                text.font = font; text.fontSize = size; text.text = value; text.color = UIStyle.NormalText;
                text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
                return text;
            }

            private static RectTransform Rect(string name, Transform parent)
            {
                var root = new GameObject(name, typeof(RectTransform));
                root.transform.SetParent(parent, false);
                return (RectTransform)root.transform;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = min; rect.offsetMax = max;
        }
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = min; rect.offsetMax = max;
        }
    }
}
