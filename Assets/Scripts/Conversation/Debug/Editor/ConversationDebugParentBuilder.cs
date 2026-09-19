using System.Collections.Generic;
using Backgammon.Conversation;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Backgammon.Conversation.Editor
{
    public static class ConversationDebugParentBuilder
    {
        private const string RootName = "DebugParent";
        private const float WindowWidth = 1280f;
        private const float WindowHeight = 860f;

        [MenuItem("GameObject/Conversation/Create DebugParent", false, 10)]
        public static void CreateDebugParent()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("DebugParent already exists.", existing);
                return;
            }

            EnsureEventSystem();

            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(ConversationDebugPanel));
            Undo.RegisterCreatedObjectUndo(root, "Create DebugParent");
            Selection.activeGameObject = root;

            var canvas = root.GetComponent<Canvas>();
            canvas.sortingOrder = 500;
            ConfigureCanvas(canvas);

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);
            rootRect.sizeDelta = scaler.referenceResolution;

            var overlay = CreatePanel(root.transform, "Overlay", new Color(0f, 0f, 0f, 0.2f));
            Stretch(overlay);

            var windowToggleButton = CreateButton(root.transform as RectTransform, "WindowToggleButton", "デバッグ非表示");
            var windowToggleRect = windowToggleButton.transform as RectTransform;
            windowToggleRect.anchorMin = new Vector2(0f, 1f);
            windowToggleRect.anchorMax = new Vector2(0f, 1f);
            windowToggleRect.pivot = new Vector2(0f, 1f);
            windowToggleRect.anchoredPosition = new Vector2(24f, -24f);

            var window = CreatePanel(overlay, "Window", new Color(0.08f, 0.1f, 0.14f, 0.96f));
            window.anchorMin = new Vector2(1f, 1f);
            window.anchorMax = new Vector2(1f, 1f);
            window.pivot = new Vector2(1f, 1f);
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            window.anchoredPosition = new Vector2(-24f, -24f);

            var windowLayout = window.gameObject.AddComponent<VerticalLayoutGroup>();
            windowLayout.padding = new RectOffset(16, 16, 16, 16);
            windowLayout.spacing = 12f;
            windowLayout.childControlHeight = true;
            windowLayout.childControlWidth = true;
            windowLayout.childForceExpandHeight = false;
            windowLayout.childForceExpandWidth = true;

            var header = CreateContainer(window, "Header", 72f);
            var headerTitle = CreateText(header, "Title", "会話デバッグ", 28, FontStyles.Bold);
            headerTitle.alignment = TextAlignmentOptions.MidlineLeft;
            var statusText = CreateText(header, "StatusText", "準備完了", 20, FontStyles.Normal);
            statusText.alignment = TextAlignmentOptions.MidlineLeft;

            var buttonRow = CreateHorizontalContainer(window, "ButtonRow", 40f, 40f);
            var buttonRowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childControlHeight = true;
            buttonRowLayout.childForceExpandWidth = false;
            buttonRowLayout.childForceExpandHeight = false;
            var runButton = CreateButton(buttonRow, "RunButton", "入力テスト");
            var forceButton = CreateButton(buttonRow, "ForceButton", "ルート再生");
            var effectButton = CreateButton(buttonRow, "EffectButton", "効果適用");
            var stateButton = CreateButton(buttonRow, "StateButton", "状態反映");
            var flagButton = CreateButton(buttonRow, "FlagButton", "フラグ反映");
            var copyButton = CreateButton(buttonRow, "CopyButton", "JSONコピー");
            var resetButton = CreateButton(buttonRow, "ResetButton", "状態リセット");
            var refreshButton = CreateButton(buttonRow, "RefreshButton", "再読込");
            var logToggleButton = CreateButton(buttonRow, "LogToggleButton", "デバッグ非表示");

            var contentRow = CreateHorizontalContainer(window, "ContentRow", -1f, 12f);
            var contentRowLayout = contentRow.GetComponent<HorizontalLayoutGroup>();
            contentRowLayout.childControlHeight = true;
            contentRowLayout.childControlWidth = true;
            contentRowLayout.childForceExpandHeight = true;
            contentRowLayout.childForceExpandWidth = true;
            var contentRowElement = contentRow.gameObject.AddComponent<LayoutElement>();
            contentRowElement.flexibleHeight = 1f;

            var leftPanel = CreatePanel(contentRow, "LeftPanel", new Color(0.12f, 0.14f, 0.18f, 1f));
            var leftLayoutElement = leftPanel.gameObject.AddComponent<LayoutElement>();
            leftLayoutElement.preferredWidth = 390f;
            leftLayoutElement.flexibleWidth = 0f;
            leftLayoutElement.flexibleHeight = 1f;
            var leftScroll = CreateScrollView(leftPanel, "LeftScrollView", out var leftContent);
            Stretch(leftScroll.GetComponent<RectTransform>());
            var leftContentLayout = leftContent.gameObject.AddComponent<VerticalLayoutGroup>();
            leftContentLayout.padding = new RectOffset(12, 12, 12, 12);
            leftContentLayout.spacing = 12f;
            leftContentLayout.childControlHeight = true;
            leftContentLayout.childControlWidth = true;
            leftContentLayout.childForceExpandHeight = false;
            leftContentLayout.childForceExpandWidth = true;
            leftContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var inputSection = CreateSection(leftContent, "InputSection", "デバッグ入力");
            var debugInputField = CreateInputField(inputSection, "DebugInputField", true, 90f, "任意の入力文");
            var tagsInputField = CreateInputField(inputSection, "TagsInputField", false, 34f, "タグ1,タグ2");
            var forceRouteField = CreateInputField(inputSection, "ForceDialogueIdInputField", false, 34f, "dlg_xxx");

            var effectSection = CreateSection(leftContent, "EffectSection", "効果JSON");
            var effectJsonField = CreateInputField(effectSection, "EffectJsonInputField", true, 90f, "{\"effects\":[]}");

            var stateSection = CreateSection(leftContent, "StateSection", "状態編集");
            var stateKeyField = CreateInputField(stateSection, "StateKeyInputField", false, 34f, "MealRefusalCount");
            var stateValueField = CreateInputField(stateSection, "StateValueInputField", false, 34f, "1");
            var stateTypeDropdown = CreateDropdown(stateSection, "StateValueTypeDropdown", new[] { "整数", "真偽値", "文字列", "文字列リスト" });
            var boolToggle = CreateToggle(stateSection, "StateBoolToggle", "真偽値");

            var personalitySection = CreateSection(leftContent, "PersonalitySection", "性格値");
            var personalityView = personalitySection.gameObject.AddComponent<ConversationPersonalityDebugView>();
            var radarRoot = CreatePanel(personalitySection, "RadarRoot", new Color(0.1f, 0.12f, 0.16f, 1f));
            var radarElement = radarRoot.gameObject.AddComponent<LayoutElement>();
            radarElement.preferredHeight = 280f;
            var radarChartRect = CreateRect(radarRoot, "RadarChart", typeof(CanvasRenderer));
            radarChartRect.anchorMin = new Vector2(0.5f, 0.5f);
            radarChartRect.anchorMax = new Vector2(0.5f, 0.5f);
            radarChartRect.pivot = new Vector2(0.5f, 0.5f);
            radarChartRect.sizeDelta = new Vector2(240f, 240f);
            radarChartRect.anchoredPosition = Vector2.zero;
            var radarGraphic = radarChartRect.gameObject.AddComponent<ConversationRadarChartGraphic>();

            var axisLabels = new TMP_Text[ConversationPersonalityDebugView.PersonalityKeys.Length];
            var axisPositions = new[]
            {
                new Vector2(0f, 118f),
                new Vector2(104f, 58f),
                new Vector2(104f, -58f),
                new Vector2(0f, -118f),
                new Vector2(-104f, -58f),
                new Vector2(-104f, 58f)
            };

            for (var i = 0; i < axisLabels.Length; i++)
            {
                var axisLabel = CreateText(radarRoot, $"AxisLabel{i}", ConversationPersonalityDebugView.GetDisplayName(ConversationPersonalityDebugView.PersonalityKeys[i]), 16, FontStyles.Bold);
                var axisRect = axisLabel.rectTransform;
                axisRect.anchorMin = new Vector2(0.5f, 0.5f);
                axisRect.anchorMax = new Vector2(0.5f, 0.5f);
                axisRect.pivot = new Vector2(0.5f, 0.5f);
                axisRect.sizeDelta = new Vector2(120f, 24f);
                axisRect.anchoredPosition = axisPositions[i];
                axisLabel.alignment = TextAlignmentOptions.Center;
                axisLabels[i] = axisLabel;
            }

            var sliderSection = CreateContainer(personalitySection, "SliderSection", -1f);
            var sliderLayout = sliderSection.gameObject.AddComponent<VerticalLayoutGroup>();
            sliderLayout.spacing = 6f;
            sliderLayout.childControlHeight = true;
            sliderLayout.childControlWidth = true;
            sliderLayout.childForceExpandHeight = false;
            sliderLayout.childForceExpandWidth = true;
            var sliders = new Slider[ConversationPersonalityDebugView.PersonalityKeys.Length];
            var valueLabels = new TMP_Text[ConversationPersonalityDebugView.PersonalityKeys.Length];
            for (var i = 0; i < sliders.Length; i++)
            {
                var row = CreateHorizontalContainer(sliderSection, $"SliderRow{i}", 28f, 8f);
                CreateText(row, $"SliderName{i}", ConversationPersonalityDebugView.GetDisplayName(ConversationPersonalityDebugView.PersonalityKeys[i]), 16, FontStyles.Normal).rectTransform.sizeDelta = new Vector2(90f, 24f);
                sliders[i] = CreateSlider(row, $"Slider{i}");
                var sliderElement = sliders[i].gameObject.AddComponent<LayoutElement>();
                sliderElement.flexibleWidth = 1f;
                valueLabels[i] = CreateText(row, $"ValueLabel{i}", "0", 16, FontStyles.Bold);
                valueLabels[i].rectTransform.sizeDelta = new Vector2(32f, 24f);
                valueLabels[i].alignment = TextAlignmentOptions.Center;
            }

            var rightPanel = CreatePanel(contentRow, "RightPanel", new Color(0.12f, 0.14f, 0.18f, 1f));
            var rightLayoutElement = rightPanel.gameObject.AddComponent<LayoutElement>();
            rightLayoutElement.flexibleWidth = 1f;
            rightLayoutElement.flexibleHeight = 1f;
            var rightLayout = rightPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rightLayout.padding = new RectOffset(12, 12, 12, 12);
            rightLayout.spacing = 12f;
            rightLayout.childControlHeight = true;
            rightLayout.childControlWidth = true;
            rightLayout.childForceExpandHeight = false;
            rightLayout.childForceExpandWidth = true;

            var outputScroll = CreateScrollView(rightPanel, "OutputScrollView", out var outputContent);
            var outputElement = outputScroll.gameObject.AddComponent<LayoutElement>();
            outputElement.flexibleHeight = 1f;
            var outputContentLayout = outputContent.gameObject.AddComponent<VerticalLayoutGroup>();
            outputContentLayout.spacing = 12f;
            outputContentLayout.childControlHeight = true;
            outputContentLayout.childControlWidth = true;
            outputContentLayout.childForceExpandHeight = false;
            outputContentLayout.childForceExpandWidth = true;
            outputContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var parseResultText = CreateOutputBlock(outputContent, "ParseResultText", "解析結果");
            var routeResultText = CreateOutputBlock(outputContent, "RouteResultText", "ルート結果");
            var stateResultText = CreateOutputBlock(outputContent, "StateResultText", "状態結果");
            var effectResultText = CreateOutputBlock(outputContent, "EffectResultText", "効果結果");
            var eventResultText = CreateOutputBlock(outputContent, "EventResultText", "イベント結果");

            var logPanel = CreatePanel(rightPanel, "LogPanel", new Color(0.09f, 0.11f, 0.15f, 1f));
            var logElement = logPanel.gameObject.AddComponent<LayoutElement>();
            logElement.preferredHeight = 240f;
            var logTitle = CreateText(logPanel, "LogTitle", "会話ログ", 22, FontStyles.Bold);
            logTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            logTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            logTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            logTitle.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            logTitle.rectTransform.sizeDelta = new Vector2(0f, 28f);
            var logViewRoot = CreateRect(logPanel, "LogViewRoot");
            logViewRoot.anchorMin = new Vector2(0f, 0f);
            logViewRoot.anchorMax = new Vector2(1f, 1f);
            logViewRoot.offsetMin = new Vector2(12f, 12f);
            logViewRoot.offsetMax = new Vector2(-12f, -40f);
            var logScroll = CreateScrollView(logViewRoot, "LogScrollView", out var logContent);
            Stretch(logScroll.GetComponent<RectTransform>());
            var logText = CreateText(logContent, "LogText", string.Empty, 18, FontStyles.Normal);
            logText.textWrappingMode = TextWrappingModes.Normal;
            logText.overflowMode = TextOverflowModes.Overflow;
            logText.alignment = TextAlignmentOptions.TopLeft;
            StretchHorizontallyTop(logText.rectTransform);
            var logView = logPanel.gameObject.AddComponent<ConversationLogView>();

            ConfigurePersonalityView(personalityView, radarGraphic, axisLabels, valueLabels, sliders);
            ConfigureLogView(logView, logText, logScroll);
            ConfigureDebugPanel(
                root.GetComponent<ConversationDebugPanel>(),
                root,
                statusText,
                debugInputField,
                tagsInputField,
                forceRouteField,
                effectJsonField,
                stateKeyField,
                stateValueField,
                stateTypeDropdown,
                boolToggle,
                parseResultText,
                routeResultText,
                stateResultText,
                effectResultText,
                eventResultText,
                logView,
                personalityView);

            WireButton(runButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.RunDebugInputTest));
            WireButton(forceButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.ForcePlayDialogue));
            WireButton(effectButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.ApplyEffectJson));
            WireButton(stateButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.ApplyStateEdit));
            WireButton(flagButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.ApplyFlagToggle));
            WireButton(copyButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.CopyStateJson));
            WireButton(resetButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.ResetState));
            WireButton(refreshButton, root.GetComponent<ConversationDebugPanel>(), nameof(ConversationDebugPanel.RefreshStateDisplay));

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("DebugParent created. Toggle its Active state to show/hide the debug UI.", root);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            var targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = Object.FindFirstObjectByType<Camera>();
            }

            if (targetCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = targetCamera;
                canvas.planeDistance = 100f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
        }

        private static void ConfigureDebugPanel(
            ConversationDebugPanel panel,
            GameObject debugRoot,
            TMP_Text statusText,
            TMP_InputField debugInputField,
            TMP_InputField tagsInputField,
            TMP_InputField forceDialogueIdInputField,
            TMP_InputField effectJsonInputField,
            TMP_InputField stateKeyInputField,
            TMP_InputField stateValueInputField,
            TMP_Dropdown stateValueTypeDropdown,
            Toggle stateBoolToggle,
            TMP_Text parseResultText,
            TMP_Text routeResultText,
            TMP_Text stateResultText,
            TMP_Text effectResultText,
            TMP_Text eventResultText,
            ConversationLogView logView,
            ConversationPersonalityDebugView personalityView)
        {
            var serializedObject = new SerializedObject(panel);
            SetObject(serializedObject, "inputParser", Object.FindFirstObjectByType<GiantCatConversationInputParser>());
            SetObject(serializedObject, "dataManager", Object.FindFirstObjectByType<ConversationDataManager>());
            SetObject(serializedObject, "gameStateManager", Object.FindFirstObjectByType<ConversationGameStateManager>());
            SetObject(serializedObject, "eventManager", Object.FindFirstObjectByType<ConversationEventManager>());
            SetObject(serializedObject, "personalityView", personalityView);
            SetObject(serializedObject, "debugRoot", debugRoot);
            SetObject(serializedObject, "debugInputField", debugInputField);
            SetObject(serializedObject, "tagsInputField", tagsInputField);
            SetObject(serializedObject, "forceDialogueIdInputField", forceDialogueIdInputField);
            SetObject(serializedObject, "effectJsonInputField", effectJsonInputField);
            SetObject(serializedObject, "stateKeyInputField", stateKeyInputField);
            SetObject(serializedObject, "stateValueInputField", stateValueInputField);
            SetObject(serializedObject, "stateValueTypeDropdown", stateValueTypeDropdown);
            SetObject(serializedObject, "stateBoolToggle", stateBoolToggle);
            SetObject(serializedObject, "statusText", statusText);
            SetObject(serializedObject, "parseResultText", parseResultText);
            SetObject(serializedObject, "routeResultText", routeResultText);
            SetObject(serializedObject, "stateResultText", stateResultText);
            SetObject(serializedObject, "effectResultText", effectResultText);
            SetObject(serializedObject, "eventResultText", eventResultText);
            SetObject(serializedObject, "conversationLogView", logView);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePersonalityView(
            ConversationPersonalityDebugView view,
            ConversationRadarChartGraphic radarGraphic,
            TMP_Text[] axisLabels,
            TMP_Text[] valueLabels,
            Slider[] sliders)
        {
            var serializedObject = new SerializedObject(view);
            SetObject(serializedObject, "radarChart", radarGraphic);
            SetArray(serializedObject, "axisLabels", axisLabels);
            SetArray(serializedObject, "valueLabels", valueLabels);
            SetArray(serializedObject, "sliders", sliders);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLogView(ConversationLogView logView, TMP_Text logText, ScrollRect scrollRect)
        {
            var serializedObject = new SerializedObject(logView);
            SetObject(serializedObject, "logText", logText);
            SetObject(serializedObject, "scrollRect", scrollRect);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireButton(Button button, ConversationDebugPanel target, string methodName)
        {
            if (button == null || target == null)
            {
                return;
            }

            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            var registered = true;
            switch (methodName)
            {
                case nameof(ConversationDebugPanel.RunDebugInputTest):
                    UnityEventTools.AddPersistentListener(button.onClick, target.RunDebugInputTest);
                    break;
                case nameof(ConversationDebugPanel.ForcePlayDialogue):
                    UnityEventTools.AddPersistentListener(button.onClick, target.ForcePlayDialogue);
                    break;
                case nameof(ConversationDebugPanel.ApplyEffectJson):
                    UnityEventTools.AddPersistentListener(button.onClick, target.ApplyEffectJson);
                    break;
                case nameof(ConversationDebugPanel.ApplyStateEdit):
                    UnityEventTools.AddPersistentListener(button.onClick, target.ApplyStateEdit);
                    break;
                case nameof(ConversationDebugPanel.ApplyFlagToggle):
                    UnityEventTools.AddPersistentListener(button.onClick, target.ApplyFlagToggle);
                    break;
                case nameof(ConversationDebugPanel.CopyStateJson):
                    UnityEventTools.AddPersistentListener(button.onClick, target.CopyStateJson);
                    break;
                case nameof(ConversationDebugPanel.ResetState):
                    UnityEventTools.AddPersistentListener(button.onClick, target.ResetState);
                    break;
                case nameof(ConversationDebugPanel.RefreshStateDisplay):
                    UnityEventTools.AddPersistentListener(button.onClick, target.RefreshStateDisplay);
                    break;
                default:
                    registered = false;
                    break;
            }

            if (registered)
            {
                EditorUtility.SetDirty(button);
            }
        }

        private static RectTransform CreateSection(Transform parent, string name, string title)
        {
            var panel = CreatePanel(parent, name, new Color(0.09f, 0.11f, 0.15f, 1f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            CreateText(panel, "Title", title, 20, FontStyles.Bold);
            return panel;
        }

        private static RectTransform CreateContainer(Transform parent, string name, float preferredHeight)
        {
            var rect = CreateRect(parent, name);
            if (preferredHeight > 0f)
            {
                var element = rect.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = preferredHeight;
            }
            return rect;
        }

        private static RectTransform CreateHorizontalContainer(Transform parent, string name, float preferredHeight, float spacing)
        {
            var rect = CreateContainer(parent, name, preferredHeight);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            return rect;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static RectTransform CreateRect(Transform parent, string name, params System.Type[] extraComponents)
        {
            var componentTypes = new List<System.Type> { typeof(RectTransform) };
            if (extraComponents != null)
            {
                for (var i = 0; i < extraComponents.Length; i++)
                {
                    if (extraComponents[i] != null)
                    {
                        componentTypes.Add(extraComponents[i]);
                    }
                }
            }

            var gameObject = new GameObject(name, componentTypes.ToArray());
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
            var rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            var rect = CreatePanel(parent, name, new Color(0.2f, 0.36f, 0.58f, 1f));
            rect.sizeDelta = new Vector2(126f, 36f);
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.36f, 0.58f, 1f);
            colors.highlightedColor = new Color(0.28f, 0.46f, 0.7f, 1f);
            colors.pressedColor = new Color(0.14f, 0.26f, 0.42f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 126f;
            element.preferredHeight = 36f;

            var text = CreateText(rect, "Label", label, 18, FontStyles.Bold);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, bool multiLine, float preferredHeight, string placeholder)
        {
            var root = CreatePanel(parent, name, new Color(0.18f, 0.2f, 0.25f, 1f));
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;

            var inputField = root.gameObject.AddComponent<TMP_InputField>();
            inputField.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            inputField.textViewport = CreateRect(root, "TextArea");
            Stretch(inputField.textViewport);
            inputField.textViewport.offsetMin = new Vector2(10f, 8f);
            inputField.textViewport.offsetMax = new Vector2(-10f, -8f);
            inputField.textViewport.gameObject.AddComponent<RectMask2D>();

            var text = CreateText(inputField.textViewport, "Text", string.Empty, 18, FontStyles.Normal);
            Stretch(text.rectTransform);
            text.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = multiLine ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            var placeholderText = CreateText(inputField.textViewport, "Placeholder", placeholder, 18, FontStyles.Italic);
            Stretch(placeholderText.rectTransform);
            placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderText.alignment = text.alignment;

            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            return inputField;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label)
        {
            var root = CreateRect(parent, name);
            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 28f;

            var toggle = root.gameObject.AddComponent<Toggle>();
            var background = CreatePanel(root, "Background", new Color(0.2f, 0.2f, 0.2f, 1f));
            background.sizeDelta = new Vector2(22f, 22f);
            var checkmark = CreatePanel(background, "Checkmark", new Color(0.95f, 0.45f, 0.25f, 1f));
            checkmark.anchorMin = new Vector2(0.2f, 0.2f);
            checkmark.anchorMax = new Vector2(0.8f, 0.8f);
            checkmark.offsetMin = Vector2.zero;
            checkmark.offsetMax = Vector2.zero;

            var labelText = CreateText(root, "Label", label, 18, FontStyles.Normal);
            labelText.alignment = TextAlignmentOptions.MidlineLeft;

            toggle.targetGraphic = background.GetComponent<Image>();
            toggle.graphic = checkmark.GetComponent<Image>();
            return toggle;
        }

        private static Slider CreateSlider(Transform parent, string name)
        {
            var root = CreateRect(parent, name);
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 20f;
            var slider = root.gameObject.AddComponent<Slider>();

            var background = CreatePanel(root, "Background", new Color(0.18f, 0.18f, 0.18f, 1f));
            Stretch(background);
            background.offsetMin = new Vector2(0f, 6f);
            background.offsetMax = new Vector2(0f, -6f);

            var fillArea = CreateRect(root, "Fill Area");
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(5f, 6f);
            fillArea.offsetMax = new Vector2(-5f, -6f);

            var fill = CreatePanel(fillArea, "Fill", new Color(0.95f, 0.45f, 0.25f, 1f));
            Stretch(fill);

            var handleSlideArea = CreateRect(root, "Handle Slide Area");
            Stretch(handleSlideArea);
            handleSlideArea.offsetMin = new Vector2(10f, 0f);
            handleSlideArea.offsetMax = new Vector2(-10f, 0f);

            var handle = CreatePanel(handleSlideArea, "Handle", Color.white);
            handle.sizeDelta = new Vector2(16f, 24f);
            var handleRect = handle.GetComponent<RectTransform>();

            slider.fillRect = fill;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private static TMP_Dropdown CreateDropdown(Transform parent, string name, IReadOnlyList<string> options)
        {
            var root = CreatePanel(parent, name, new Color(0.18f, 0.2f, 0.25f, 1f));
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 34f;
            var dropdown = root.gameObject.AddComponent<TMP_Dropdown>();

            var label = CreateText(root, "Label", options != null && options.Count > 0 ? options[0] : string.Empty, 18, FontStyles.Normal);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(10f, 4f);
            label.rectTransform.offsetMax = new Vector2(-28f, -4f);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            var arrow = CreateText(root, "Arrow", "▼", 18, FontStyles.Bold);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(24f, 24f);
            arrow.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
            arrow.alignment = TextAlignmentOptions.Center;

            var template = CreatePanel(root, "Template", new Color(0.14f, 0.16f, 0.2f, 1f));
            template.gameObject.SetActive(false);
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, 180f);
            template.anchoredPosition = new Vector2(0f, -2f);
            var templateScrollRect = template.gameObject.AddComponent<ScrollRect>();
            templateScrollRect.horizontal = false;

            var viewport = CreateRect(template, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 2f;
            contentLayout.childControlHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var item = CreateRect(content, "Item");
            item.gameObject.AddComponent<Image>().color = new Color(0.24f, 0.26f, 0.3f, 1f);
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            var itemToggle = item.gameObject.AddComponent<Toggle>();

            var itemBackground = CreatePanel(item, "Item Background", new Color(0f, 0f, 0f, 0f));
            Stretch(itemBackground);
            var itemCheckmark = CreatePanel(itemBackground, "Item Checkmark", new Color(0.95f, 0.45f, 0.25f, 1f));
            itemCheckmark.anchorMin = new Vector2(0f, 0.5f);
            itemCheckmark.anchorMax = new Vector2(0f, 0.5f);
            itemCheckmark.pivot = new Vector2(0f, 0.5f);
            itemCheckmark.sizeDelta = new Vector2(16f, 16f);
            itemCheckmark.anchoredPosition = new Vector2(6f, 0f);

            var itemLabel = CreateText(item, "Item Label", "Option", 18, FontStyles.Normal);
            itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            itemLabel.rectTransform.offsetMin = new Vector2(28f, 2f);
            itemLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;

            templateScrollRect.viewport = viewport;
            templateScrollRect.content = content;

            dropdown.template = template;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.options = new List<TMP_Dropdown.OptionData>();
            if (options != null)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    dropdown.options.Add(new TMP_Dropdown.OptionData(options[i]));
                }
            }
            dropdown.RefreshShownValue();

            itemToggle.targetGraphic = item.GetComponent<Image>();
            itemToggle.graphic = itemCheckmark.GetComponent<Image>();
            return dropdown;
        }

        private static ScrollRect CreateScrollView(Transform parent, string name, out RectTransform content)
        {
            var root = CreatePanel(parent, name, new Color(0.06f, 0.08f, 0.12f, 1f));
            var scrollRect = root.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateRect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return scrollRect;
        }

        private static TMP_Text CreateOutputBlock(Transform parent, string name, string title)
        {
            var section = CreateSection(parent, $"{name}Section", title);
            var text = CreateText(section, name, string.Empty, 18, FontStyles.Normal);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 220f;
            return text;
        }

        private static TMP_Text CreateText(Transform parent, string name, string value, float fontSize, FontStyles fontStyle)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void StretchHorizontallyTop(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 0f);
        }

        private static void SetObject(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetArray<T>(SerializedObject serializedObject, string propertyName, IReadOnlyList<T> values) where T : Object
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = values?.Count ?? 0;
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
