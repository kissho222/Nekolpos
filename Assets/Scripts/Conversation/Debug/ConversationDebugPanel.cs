using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nekolpos.StatusSystem;

namespace Backgammon.Conversation
{
    public sealed class ConversationDebugPanel : MonoBehaviour
    {
        private const string DebugLogPrefix = "[ConversationDebugPanel]";

        [Header("System References")]
        [SerializeField] private GiantCatConversationInputParser inputParser;
        [SerializeField] private ConversationDataManager dataManager;
        [SerializeField] private ConversationGameStateManager gameStateManager;
        [SerializeField] private ConversationEventManager eventManager;
        [SerializeField] private ConversationPersonalityDebugView personalityView;
        [SerializeField] private StatusManager statusManager;

        [Header("Debug UI Root")]
        [SerializeField] private GameObject debugRoot;
        [SerializeField] private bool hideWhenUnavailable = true;
        [SerializeField] private bool commitStateAfterTestRun = true;
        [SerializeField] private bool executeRouteEvents = true;

        [Header("Inputs")]
        [SerializeField] private TMP_InputField debugInputField;
        [SerializeField] private TMP_InputField tagsInputField;
        [SerializeField] private TMP_InputField forceDialogueIdInputField;
        [SerializeField] [TextArea(3, 8)] private TMP_InputField effectJsonInputField;
        [SerializeField] private TMP_InputField stateKeyInputField;
        [SerializeField] private TMP_InputField stateValueInputField;
        [SerializeField] private TMP_Dropdown stateValueTypeDropdown;
        [SerializeField] private Toggle stateBoolToggle;

        [Header("Outputs")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text parseResultText;
        [SerializeField] private TMP_Text routeResultText;
        [SerializeField] private TMP_Text stateResultText;
        [SerializeField] private TMP_Text effectResultText;
        [SerializeField] private TMP_Text eventResultText;
        [SerializeField] private ConversationLogView conversationLogView;

        private readonly StringBuilder builder = new();
        private readonly List<RaycastResult> raycastResults = new();
        private static readonly ConversationParseConfig FallbackParseConfig = GiantCatConversationDefaults.CreateConfig();
        private ConversationDebugRunResult lastRunResult;
        private CancellationTokenSource runCts;
        private Canvas debugCanvas;
        private GraphicRaycaster debugRaycaster;
        private Button fallbackPressedButton;
        private Vector2 lastRootScreenSize;
        private bool isLogPanelVisible = true;
        private bool isDebugWindowVisible = true;

        private void Awake()
        {
            if (!ConversationDebugAvailability.IsEnabled)
            {
                Debug.Log($"{DebugLogPrefix} Debug UI disabled by availability flag.", this);
                if (hideWhenUnavailable && debugRoot != null)
                {
                    debugRoot.SetActive(false);
                }

                enabled = false;
                return;
            }

            NormalizeRootCanvasRect();
            CacheDebugComponents();
            EnsureLogToggleButton();
            EnsureWindowToggleButton();
            ResolveSystemReferences();
            ApplyJapaneseLabels();
            SyncLogPanelState();
            SyncDebugWindowState();
            RepairRuntimeLayout();
            Debug.Log(
                $"{DebugLogPrefix} Awake completed. debugRoot={(debugRoot != null ? debugRoot.name : "<null>")}, " +
                $"raycaster={(debugRaycaster != null ? "ok" : "missing")}, eventSystem={(EventSystem.current != null ? EventSystem.current.name : "<null>")}",
                this);
            personalityView?.BindState(gameStateManager != null ? gameStateManager.State : null);
            personalityView?.BindStatusManager(statusManager);
            if (personalityView != null)
            {
                personalityView.PersonalityValueChanged += HandlePersonalityValueChanged;
            }

            RefreshStateDisplay();
        }

        private IEnumerator Start()
        {
            yield return null;
            RepairRuntimeLayout();
        }

        private void OnDestroy()
        {
            if (personalityView != null)
            {
                personalityView.PersonalityValueChanged -= HandlePersonalityValueChanged;
            }

            runCts?.Cancel();
            runCts?.Dispose();
        }

        private void Update()
        {
            SyncRootCanvasSize();
            EnsureInteractiveButtonLayout();
            HandleFallbackButtonInput();
        }

        public async void RunDebugInputTest()
        {
            await RunAsync(false);
        }

        public async void ForcePlayDialogue()
        {
            await RunAsync(true);
        }

        public void ApplyEffectJson()
        {
            Debug.Log($"{DebugLogPrefix} ApplyEffectJson invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null || effectJsonInputField == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} ApplyEffectJson aborted. Missing manager or input field.", this);
                return;
            }

            try
            {
                gameStateManager.ApplyEffectsFromJson(effectJsonInputField.text);
                SetStatus("効果を適用しました。");
                RefreshStateDisplay();
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
            }
        }

        public void ApplyStateEdit()
        {
            Debug.Log($"{DebugLogPrefix} ApplyStateEdit invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null || stateKeyInputField == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} ApplyStateEdit aborted. Missing manager or key field.", this);
                return;
            }

            var key = stateKeyInputField.text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                SetStatus("ステートキーを入力してください。");
                return;
            }

            var kind = (ConversationDebugStateValueKind)Mathf.Clamp(
                stateValueTypeDropdown != null ? stateValueTypeDropdown.value : 0,
                0,
                Enum.GetValues(typeof(ConversationDebugStateValueKind)).Length - 1);

            try
            {
                if (statusManager != null && System.Enum.TryParse(key, true, out StatusType statusType))
                {
                    if (!int.TryParse(stateValueInputField != null ? stateValueInputField.text : string.Empty, out int statusValue))
                    {
                        SetStatus("心理値には0から100の整数を入力してください。");
                        return;
                    }

                    statusManager.SetValue(statusType, statusValue, "Debug", "ConversationDebugPanel");
                    SetStatus($"心理値を更新しました: {statusType}");
                    RefreshStateDisplay();
                    return;
                }

                ApplyStateValue(gameStateManager.State, key, kind, stateValueInputField != null ? stateValueInputField.text : string.Empty);
                SetStatus($"ステートを更新しました: {key}");
                RefreshStateDisplay();
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
            }
        }

        public void ApplyFlagToggle()
        {
            Debug.Log($"{DebugLogPrefix} ApplyFlagToggle invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null || stateKeyInputField == null || stateBoolToggle == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} ApplyFlagToggle aborted. Missing dependencies.", this);
                return;
            }

            var key = stateKeyInputField.text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                SetStatus("フラグキーを入力してください。");
                return;
            }

            gameStateManager.State.SetBool(key, stateBoolToggle.isOn);
            SetStatus($"フラグを更新しました: {key}={stateBoolToggle.isOn}");
            RefreshStateDisplay();
        }

        public void CopyStateJson()
        {
            Debug.Log($"{DebugLogPrefix} CopyStateJson invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} CopyStateJson aborted. Missing gameStateManager.", this);
                return;
            }

            GUIUtility.systemCopyBuffer = gameStateManager.SaveToJson(true);
            SetStatus("状態JSONをコピーしました。");
        }

        public void ResetState()
        {
            Debug.Log($"{DebugLogPrefix} ResetState invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} ResetState aborted. Missing gameStateManager.", this);
                return;
            }

            gameStateManager.ResetStateFromInitialJson();
            conversationLogView?.Clear();
            SetStatus("状態をリセットしました。");
            RefreshStateDisplay();
        }

        public void RefreshStateDisplay()
        {
            Debug.Log($"{DebugLogPrefix} RefreshStateDisplay invoked.", this);
            ResolveSystemReferences();
            if (gameStateManager == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} RefreshStateDisplay aborted. Missing gameStateManager.", this);
                return;
            }

            personalityView?.RefreshFromState(gameStateManager.State);
            stateResultText.text = FormatState(gameStateManager.State, lastRunResult?.activeTags);
        }

        private void CacheDebugComponents()
        {
            if (debugRoot == null)
            {
                Debug.LogWarning($"{DebugLogPrefix} CacheDebugComponents: debugRoot is null.", this);
                return;
            }

            debugRaycaster = debugRoot.GetComponent<GraphicRaycaster>();
            debugCanvas = debugRoot.GetComponent<Canvas>();
            Debug.Log(
                $"{DebugLogPrefix} CacheDebugComponents: root={debugRoot.name}, raycaster={(debugRaycaster != null ? "found" : "missing")}.",
                this);
        }

        private void ResolveSystemReferences()
        {
            var resolvedAny = false;

            if (inputParser == null)
            {
                inputParser = FindFirstObjectByType<GiantCatConversationInputParser>();
                resolvedAny |= inputParser != null;
            }

            if (dataManager == null)
            {
                dataManager = FindFirstObjectByType<ConversationDataManager>();
                resolvedAny |= dataManager != null;
            }

            if (gameStateManager == null)
            {
                gameStateManager = FindFirstObjectByType<ConversationGameStateManager>();
                if (gameStateManager == null)
                {
                    gameStateManager = GetComponent<ConversationGameStateManager>();
                }

                if (gameStateManager == null)
                {
                    gameStateManager = gameObject.AddComponent<ConversationGameStateManager>();
                }

                resolvedAny = true;
            }

            if (eventManager == null)
            {
                eventManager = FindFirstObjectByType<ConversationEventManager>();
                if (eventManager == null)
                {
                    eventManager = GetComponent<ConversationEventManager>();
                }

                if (eventManager == null)
                {
                    eventManager = gameObject.AddComponent<ConversationEventManager>();
                }

                resolvedAny = true;
            }

            if (statusManager == null)
            {
                statusManager = StatusManager.FindOrCreate(gameObject);

                personalityView?.BindStatusManager(statusManager);
                resolvedAny = true;
            }

            if (resolvedAny)
            {
                Debug.Log(
                    $"{DebugLogPrefix} ResolveSystemReferences: " +
                    $"inputParser={(inputParser != null ? inputParser.name : "<null>")}, " +
                    $"dataManager={(dataManager != null ? dataManager.name : "<null>")}, " +
                    $"gameStateManager={(gameStateManager != null ? gameStateManager.name : "<null>")}, " +
                    $"eventManager={(eventManager != null ? eventManager.name : "<null>")}",
                    this);
            }
        }

        private void NormalizeRootCanvasRect()
        {
            if (debugRoot == null)
            {
                return;
            }

            var rootRect = debugRoot.transform as RectTransform;
            if (rootRect == null)
            {
                return;
            }

            var oldScale = rootRect.localScale;
            var oldAnchorMin = rootRect.anchorMin;
            var oldAnchorMax = rootRect.anchorMax;
            var oldPivot = rootRect.pivot;

            rootRect.localScale = Vector3.one;
            rootRect.localRotation = Quaternion.identity;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            SyncRootCanvasSize();

            Debug.Log(
                $"{DebugLogPrefix} NormalizeRootCanvasRect: scale {oldScale} -> {rootRect.localScale}, " +
                $"anchorMin {oldAnchorMin} -> {rootRect.anchorMin}, anchorMax {oldAnchorMax} -> {rootRect.anchorMax}, pivot {oldPivot} -> {rootRect.pivot}",
                this);
        }

        private void SyncRootCanvasSize()
        {
            if (debugRoot == null)
            {
                return;
            }

            var rootRect = debugRoot.transform as RectTransform;
            if (rootRect == null || rootRect.parent != null)
            {
                return;
            }

            var targetSize = new Vector2(Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
            if ((targetSize - lastRootScreenSize).sqrMagnitude < 0.01f && rootRect.rect.width > 0.5f && rootRect.rect.height > 0.5f)
            {
                return;
            }

            lastRootScreenSize = targetSize;
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);
        }

        private void ApplyJapaneseLabels()
        {
            SetChildText("Header", "Title", "会話デバッグ");
            SetChildText("Header", "StatusText", "準備完了");
            SetButtonLabel("RunButton", "入力テスト");
            SetButtonLabel("ForceButton", "ルート再生");
            SetButtonLabel("EffectButton", "効果適用");
            SetButtonLabel("StateButton", "状態反映");
            SetButtonLabel("FlagButton", "フラグ反映");
            SetButtonLabel("CopyButton", "JSONコピー");
            SetButtonLabel("ResetButton", "状態リセット");
            SetButtonLabel("RefreshButton", "再読込");
            SetButtonLabel("LogToggleButton", "デバッグ非表示");
            SetButtonLabel("WindowToggleButton", "デバッグ表示");
            SetChildText("InputSection", "Title", "デバッグ入力");
            SetChildText("EffectSection", "Title", "効果JSON");
            SetChildText("StateSection", "Title", "状態編集");
            SetChildText("PersonalitySection", "Title", "性格値");
            SetChildText("ParseResultTextSection", "Title", "解析結果");
            SetChildText("RouteResultTextSection", "Title", "ルート結果");
            SetChildText("StateResultTextSection", "Title", "状態結果");
            SetChildText("EffectResultTextSection", "Title", "効果結果");
            SetChildText("EventResultTextSection", "Title", "イベント結果");

            if (debugInputField?.placeholder is TMP_Text debugPlaceholder)
            {
                debugPlaceholder.text = "任意の入力文";
            }

            if (tagsInputField?.placeholder is TMP_Text tagsPlaceholder)
            {
                tagsPlaceholder.text = "タグ1,タグ2";
            }

            if (forceDialogueIdInputField?.placeholder is TMP_Text routePlaceholder)
            {
                routePlaceholder.text = "dlg_xxx";
            }

            if (stateValueTypeDropdown != null)
            {
                stateValueTypeDropdown.options.Clear();
                stateValueTypeDropdown.options.Add(new TMP_Dropdown.OptionData("整数"));
                stateValueTypeDropdown.options.Add(new TMP_Dropdown.OptionData("真偽値"));
                stateValueTypeDropdown.options.Add(new TMP_Dropdown.OptionData("文字列"));
                stateValueTypeDropdown.options.Add(new TMP_Dropdown.OptionData("文字列リスト"));
                stateValueTypeDropdown.RefreshShownValue();
            }

            var toggleLabel = FindDescendant("StateBoolToggle")?.Find("Label")?.GetComponent<TMP_Text>();
            if (toggleLabel != null)
            {
                toggleLabel.text = "真偽値";
            }

            var logTitle = FindDescendant("LogTitle")?.GetComponent<TMP_Text>();
            if (logTitle != null)
            {
                logTitle.text = "会話ログ";
            }
        }

        private void EnsureLogToggleButton()
        {
            if (FindDescendant("LogToggleButton") != null)
            {
                return;
            }

            var buttonRow = FindDescendant("ButtonRow");
            var templateButton = FindDescendant("RefreshButton")?.GetComponent<Button>();
            if (buttonRow == null || templateButton == null)
            {
                return;
            }

            var templateRect = templateButton.transform as RectTransform;
            var templateImage = templateButton.GetComponent<Image>();
            var templateLayout = templateButton.GetComponent<LayoutElement>();
            var templateLabel = templateButton.GetComponentInChildren<TMP_Text>(true);
            if (templateRect == null || templateImage == null || templateLabel == null)
            {
                return;
            }

            var buttonObject = new GameObject("LogToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(buttonRow, false);
            buttonRect.anchorMin = templateRect.anchorMin;
            buttonRect.anchorMax = templateRect.anchorMax;
            buttonRect.pivot = templateRect.pivot;
            buttonRect.sizeDelta = templateRect.sizeDelta;
            buttonRect.localScale = Vector3.one;
            buttonRect.SetSiblingIndex(templateRect.GetSiblingIndex() + 1);

            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.sprite = templateImage.sprite;
            buttonImage.type = templateImage.type;
            buttonImage.color = templateImage.color;
            buttonImage.material = templateImage.material;
            buttonImage.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.transition = templateButton.transition;
            button.colors = templateButton.colors;
            button.spriteState = templateButton.spriteState;
            button.animationTriggers = templateButton.animationTriggers;
            button.targetGraphic = buttonImage;

            var layout = buttonObject.GetComponent<LayoutElement>();
            if (templateLayout != null)
            {
                layout.preferredWidth = templateLayout.preferredWidth;
                layout.preferredHeight = templateLayout.preferredHeight;
                layout.minWidth = templateLayout.minWidth;
                layout.minHeight = templateLayout.minHeight;
                layout.flexibleWidth = templateLayout.flexibleWidth;
                layout.flexibleHeight = templateLayout.flexibleHeight;
            }

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = templateLabel.font;
            label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            label.fontSize = templateLabel.fontSize;
            label.fontStyle = templateLabel.fontStyle;
            label.color = templateLabel.color;
            label.alignment = templateLabel.alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
        }

        private void EnsureWindowToggleButton()
        {
            if (FindDescendant("WindowToggleButton") != null || debugRoot == null)
            {
                return;
            }

            var templateButton = FindDescendant("RefreshButton")?.GetComponent<Button>();
            if (templateButton == null)
            {
                return;
            }

            var templateRect = templateButton.transform as RectTransform;
            var templateImage = templateButton.GetComponent<Image>();
            var templateLayout = templateButton.GetComponent<LayoutElement>();
            var templateLabel = templateButton.GetComponentInChildren<TMP_Text>(true);
            if (templateRect == null || templateImage == null || templateLabel == null)
            {
                return;
            }

            var buttonObject = new GameObject("WindowToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(debugRoot.transform, false);
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(24f, -24f);
            buttonRect.localScale = Vector3.one;

            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.sprite = templateImage.sprite;
            buttonImage.type = templateImage.type;
            buttonImage.color = templateImage.color;
            buttonImage.material = templateImage.material;
            buttonImage.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.transition = templateButton.transition;
            button.colors = templateButton.colors;
            button.spriteState = templateButton.spriteState;
            button.animationTriggers = templateButton.animationTriggers;
            button.targetGraphic = buttonImage;

            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = templateLayout != null && templateLayout.preferredWidth > 0f ? templateLayout.preferredWidth : 160f;
            layout.preferredHeight = templateLayout != null && templateLayout.preferredHeight > 0f ? templateLayout.preferredHeight : 40f;
            buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, layout.preferredWidth);
            buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, layout.preferredHeight);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = templateLabel.font;
            label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            label.fontSize = templateLabel.fontSize;
            label.fontStyle = templateLabel.fontStyle;
            label.color = templateLabel.color;
            label.alignment = templateLabel.alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
        }

        private void SyncLogPanelState()
        {
            var logPanel = FindDescendant("LogPanel");
            if (logPanel == null)
            {
                return;
            }

            logPanel.gameObject.SetActive(isLogPanelVisible);
        }

        private void SyncDebugWindowState()
        {
            var overlay = FindDescendant("Overlay");
            if (overlay != null)
            {
                overlay.gameObject.SetActive(isDebugWindowVisible);
            }

            var windowToggle = FindDescendant("WindowToggleButton");
            if (windowToggle != null)
            {
                windowToggle.gameObject.SetActive(!isDebugWindowVisible);
            }

            SetButtonLabel("WindowToggleButton", "デバッグ表示");
            SetButtonLabel("LogToggleButton", "デバッグ非表示");
        }

        private void SetButtonLabel(string buttonName, string text)
        {
            SetChildText(buttonName, "Label", text);
        }

        private void SetChildText(string parentName, string childName, string text)
        {
            var parent = FindDescendant(parentName);
            if (parent == null)
            {
                return;
            }

            var child = parent.Find(childName);
            var label = child != null ? child.GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                label.text = text;
            }
        }

        private void RepairRuntimeLayout()
        {
            if (debugRoot == null)
            {
                return;
            }

            SyncRootCanvasSize();
            SetAllTextRaycastTargets(false);
            RelaxNonInteractivePanelRaycasts();
            FixWindowLayout();
            FixVerticalSection("InputSection");
            FixVerticalSection("EffectSection");
            FixVerticalSection("StateSection");
            FixVerticalSection("PersonalitySection");
            FixVerticalSection("SliderSection");
            FixButtonRowLayout();
            FixLeftScrollContent();
            FixOutputScrollContent();
            ForceRebuildDebugLayout();
        }

        private void FixWindowLayout()
        {
            var window = FindDescendant("Window");
            var layout = window != null ? window.GetComponent<VerticalLayoutGroup>() : null;
            if (layout != null)
            {
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
            }
        }

        private void SetAllTextRaycastTargets(bool enabled)
        {
            foreach (var text in debugRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                text.raycastTarget = enabled;
            }
        }

        private void RelaxNonInteractivePanelRaycasts()
        {
            foreach (var image in debugRoot.GetComponentsInChildren<Image>(true))
            {
                if (image == null)
                {
                    continue;
                }

                if (image.GetComponent<Selectable>() != null || image.GetComponent<Mask>() != null)
                {
                    continue;
                }

                image.raycastTarget = false;
            }
        }

        private void FixVerticalSection(string sectionName)
        {
            var section = FindDescendant(sectionName);
            if (section == null)
            {
                return;
            }

            var layout = section.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            var fitter = section.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = section.gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void FixButtonRowLayout()
        {
            var row = FindDescendant("ButtonRow");
            if (row == null)
            {
                return;
            }

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 40f;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            var element = row.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.preferredHeight = 40f;
            }

            foreach (var button in row.GetComponentsInChildren<Button>(true))
            {
                var buttonRect = button.transform as RectTransform;
                var buttonElement = button.GetComponent<LayoutElement>();
                if (buttonRect == null || buttonElement == null)
                {
                    continue;
                }

                if (buttonElement.preferredWidth > 0f)
                {
                    buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, buttonElement.preferredWidth);
                }

                if (buttonElement.preferredHeight > 0f)
                {
                    buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, buttonElement.preferredHeight);
                }
            }
        }

        private void FixLeftScrollContent()
        {
            var content = FindDescendant("LeftScrollView")?.Find("Viewport/Content");
            if (content == null)
            {
                return;
            }

            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            var scrollRect = content.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.scrollSensitivity = 30f;
            }
        }

        private void FixOutputScrollContent()
        {
            var content = FindDescendant("OutputScrollView")?.Find("Viewport/Content");
            if (content == null)
            {
                return;
            }

            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private void ForceRebuildDebugLayout()
        {
            Canvas.ForceUpdateCanvases();

            var rebuildTargets = new[]
            {
                "Window",
                "Header",
                "ButtonRow",
                "ContentRow",
                "LeftPanel",
                "LeftScrollView",
                "InputSection",
                "EffectSection",
                "StateSection",
                "PersonalitySection",
                "SliderSection",
                "RightPanel",
                "OutputScrollView",
                "LogPanel"
            };

            for (var i = 0; i < rebuildTargets.Length; i++)
            {
                var rect = FindDescendant(rebuildTargets[i]) as RectTransform;
                if (rect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                }
            }

            var rootRect = debugRoot.transform as RectTransform;
            if (rootRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }

            Canvas.ForceUpdateCanvases();
        }

        private void EnsureInteractiveButtonLayout()
        {
            foreach (var button in debugRoot.GetComponentsInChildren<Button>(true))
            {
                var rectTransform = button.transform as RectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                if (rectTransform.rect.width > 1f && rectTransform.rect.height > 1f)
                {
                    continue;
                }

                RepairRuntimeLayout();
                return;
            }
        }

        private Transform FindDescendant(string name)
        {
            foreach (var child in debugRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private void HandleFallbackButtonInput()
        {
            if (debugRaycaster == null || EventSystem.current == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                fallbackPressedButton = RaycastButton(Input.mousePosition);
            }

            if (Input.GetMouseButtonUp(0))
            {
                var releasedButton = RaycastButton(Input.mousePosition);
                if (fallbackPressedButton != null && releasedButton == fallbackPressedButton)
                {
                    InvokeButtonAction(releasedButton.name);
                }

                fallbackPressedButton = null;
            }
        }

        private Button RaycastButton(Vector2 screenPosition)
        {
            if (EventSystem.current != null && debugRaycaster != null)
            {
                var pointerData = new PointerEventData(EventSystem.current)
                {
                    position = screenPosition
                };

                raycastResults.Clear();
                debugRaycaster.Raycast(pointerData, raycastResults);
                for (var i = 0; i < raycastResults.Count; i++)
                {
                    var button = raycastResults[i].gameObject.GetComponentInParent<Button>();
                    if (button != null)
                    {
                        return button;
                    }
                }
            }

            var eventCamera = debugCanvas != null && debugCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? debugCanvas.worldCamera
                : null;

            foreach (var button in debugRoot.GetComponentsInChildren<Button>(true))
            {
                if (button == null || !button.IsActive() || !button.interactable)
                {
                    continue;
                }

                var rectTransform = button.transform as RectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, eventCamera))
                {
                    return button;
                }
            }

            return null;
        }

        private void InvokeButtonAction(string buttonName)
        {
            switch (buttonName)
            {
                case "RunButton":
                    RunDebugInputTest();
                    break;
                case "ForceButton":
                    ForcePlayDialogue();
                    break;
                case "EffectButton":
                    ApplyEffectJson();
                    break;
                case "StateButton":
                    ApplyStateEdit();
                    break;
                case "FlagButton":
                    ApplyFlagToggle();
                    break;
                case "CopyButton":
                    CopyStateJson();
                    break;
                case "ResetButton":
                    ResetState();
                    break;
                case "RefreshButton":
                    RefreshStateDisplay();
                    break;
                case "LogToggleButton":
                    ToggleDebugWindow();
                    break;
                case "WindowToggleButton":
                    ToggleDebugWindow();
                    break;
                default:
                    Debug.LogWarning($"{DebugLogPrefix} No mapped action for button: {buttonName}", this);
                    break;
            }
        }

        private void ToggleLogPanel()
        {
            isLogPanelVisible = !isLogPanelVisible;
            SyncLogPanelState();
            ForceRebuildDebugLayout();
            SetStatus(isLogPanelVisible ? "会話ログを表示しました。" : "会話ログを隠しました。");
        }

        private void ToggleDebugWindow()
        {
            isDebugWindowVisible = !isDebugWindowVisible;
            SyncDebugWindowState();
            if (isDebugWindowVisible)
            {
                RepairRuntimeLayout();
                SetStatus("会話デバッグを表示しました。");
                return;
            }

            SetStatus("会話デバッグを隠しました。");
        }

        private async Task RunAsync(bool forcedRoute)
        {
            Debug.Log($"{DebugLogPrefix} RunAsync start. forcedRoute={forcedRoute}", this);
            ResolveSystemReferences();
            if (gameStateManager == null)
            {
                SetStatus("ゲームステート管理が初期化できません。");
                Debug.LogWarning($"{DebugLogPrefix} RunAsync aborted. gameStateManager missing.", this);
                return;
            }

            runCts?.Cancel();
            runCts?.Dispose();
            runCts = new CancellationTokenSource();

            try
            {
                var routeCatalog = LoadRouteCatalog();
                var runner = new ConversationDebugRunner(
                    routeCatalog,
                    eventManager != null ? ConversationEventExecutor.ParseDefinitionsJson(eventManager.EventDefinitionsJson) : Array.Empty<ConversationEventDefinition>(),
                    eventManager?.Bridge);

                if (forcedRoute)
                {
                    var routeId = ResolveForcedRouteId(routeCatalog);
                    if (string.IsNullOrWhiteSpace(routeId))
                    {
                        SetStatus("強制再生できるルートが見つかりません。");
                        return;
                    }

                    if (forceDialogueIdInputField != null && !string.Equals(forceDialogueIdInputField.text, routeId, StringComparison.Ordinal))
                    {
                        forceDialogueIdInputField.text = routeId;
                    }

                    lastRunResult = await runner.ForceRouteAsync(routeId, gameStateManager.State, executeRouteEvents, runCts.Token);
                }
                else
                {
                    var rawInput = debugInputField != null ? debugInputField.text : string.Empty;
                    var parseResult = ParseInput(rawInput);

                    if (conversationLogView != null && !string.IsNullOrWhiteSpace(parseResult.input))
                    {
                        conversationLogView.AddEntry(ConversationLogEntryType.PlayerInput, string.Empty, parseResult.input);
                    }

                    lastRunResult = await runner.RunAsync(
                        parseResult,
                        gameStateManager.State,
                        ParseTags(tagsInputField != null ? tagsInputField.text : string.Empty),
                        executeRouteEvents,
                        runCts.Token);
                }

                if (commitStateAfterTestRun)
                {
                    gameStateManager.LoadFromJson(lastRunResult.stateAfterEventsJson);
                }

                UpdateOutputViews(lastRunResult);
                SetStatus(forcedRoute
                    ? $"強制ルートを実行しました: {lastRunResult.routeResult.conversationId}"
                    : $"選択ルート: {(lastRunResult.routeResult.matched ? lastRunResult.routeResult.conversationId : "<なし>")}");
                Debug.Log(
                    $"{DebugLogPrefix} RunAsync completed. matched={lastRunResult.routeResult.matched}, " +
                    $"conversationId={lastRunResult.routeResult.conversationId}",
                    this);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
                Debug.LogException(exception, this);
            }
        }

        private void UpdateOutputViews(ConversationDebugRunResult result)
        {
            parseResultText.text = FormatParseResult(result.parseResult);
            routeResultText.text = FormatRouteResult(result.routeResult);
            effectResultText.text = FormatEffects(result);
            eventResultText.text = FormatEvents(result.eventResult);
            RefreshStateDisplay();
            AppendConversationLog(result);
        }

        private void AppendConversationLog(ConversationDebugRunResult result)
        {
            if (conversationLogView == null || result?.routeResult == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.routeResult.response))
            {
                conversationLogView.AddEntry(ConversationLogEntryType.Message, result.routeResult.conversationId, result.routeResult.response);
            }

            if (result.eventResult?.eventLogs != null)
            {
                for (var i = 0; i < result.eventResult.eventLogs.Count; i++)
                {
                    conversationLogView.AddEntry(ConversationLogEntryType.Event, string.Empty, result.eventResult.eventLogs[i]);
                }
            }
        }

        private void HandlePersonalityValueChanged(string key, int value)
        {
            RefreshStateDisplay();
        }

        private ConversationRouteCatalog LoadRouteCatalog()
        {
            if (dataManager != null)
            {
                dataManager.Reload();
                return dataManager.LoadedCatalog ?? new ConversationRouteCatalog(null);
            }

            var fallbackAsset = Resources.Load<TextAsset>("TalkCSV/DialoguePreview_Integrated");
            if (fallbackAsset == null)
            {
                throw new InvalidOperationException("会話データCSVが見つかりません。Resources/TalkCSV/DialoguePreview_Integrated.csv を確認してください。");
            }

            var result = ConversationDataLoader.LoadFromCsvTextAsset(fallbackAsset, new ConversationDataLoadOptions
            {
                locale = "ja",
                logErrorsToConsole = true
            });

            if (result.errors != null && result.errors.Count > 0)
            {
                var summary = string.Join("\n", result.errors.Take(5).Select(error => error.ToString()));
                throw new InvalidOperationException($"会話データCSVの読み込みでエラーが発生しました。\n{summary}");
            }

            return result.catalog ?? new ConversationRouteCatalog(null);
        }

        private string ResolveForcedRouteId(ConversationRouteCatalog routeCatalog)
        {
            var requestedRouteId = forceDialogueIdInputField != null ? forceDialogueIdInputField.text?.Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(requestedRouteId))
            {
                return requestedRouteId;
            }

            var lastResultRouteId = lastRunResult?.routeResult?.conversationId;
            if (RouteExists(routeCatalog, lastResultRouteId))
            {
                SetStatus($"ルートID未入力のため直前ルート {lastResultRouteId} を使用します。");
                return lastResultRouteId;
            }

            if (gameStateManager?.State != null &&
                gameStateManager.State.TryGetString(ConversationRouter.LastRouteIdStateKey, out var lastStateRouteId) &&
                RouteExists(routeCatalog, lastStateRouteId))
            {
                SetStatus($"ルートID未入力のため履歴ルート {lastStateRouteId} を使用します。");
                return lastStateRouteId;
            }

            var firstRouteId = routeCatalog?.Routes != null
                ? routeCatalog.Routes.FirstOrDefault()?.id
                : null;
            if (RouteExists(routeCatalog, firstRouteId))
            {
                SetStatus($"ルートID未入力のため先頭ルート {firstRouteId} を使用します。");
                return firstRouteId;
            }

            return string.Empty;
        }

        private static bool RouteExists(ConversationRouteCatalog routeCatalog, string routeId)
        {
            return routeCatalog != null &&
                   !string.IsNullOrWhiteSpace(routeId) &&
                   routeCatalog.TryGetById(routeId, out _);
        }

        private ConversationParseResult ParseInput(string rawInput)
        {
            if (inputParser != null)
            {
                return inputParser.Parse(rawInput);
            }

            return new RegexInputParser(FallbackParseConfig).Parse(rawInput);
        }

        private void ApplyStateValue(
            ConversationGameState state,
            string key,
            ConversationDebugStateValueKind kind,
            string rawValue)
        {
            switch (kind)
            {
                case ConversationDebugStateValueKind.Integer:
                    if (!int.TryParse(rawValue, out var intValue))
                    {
                        throw new FormatException("整数値が不正です。");
                    }
                    state.SetInt(key, intValue);
                    return;
                case ConversationDebugStateValueKind.Boolean:
                    state.SetBool(key, stateBoolToggle != null ? stateBoolToggle.isOn : bool.Parse(rawValue));
                    return;
                case ConversationDebugStateValueKind.String:
                    state.SetString(key, rawValue ?? string.Empty);
                    return;
                case ConversationDebugStateValueKind.StringList:
                    state.SetStringList(key, ParseTags(rawValue));
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        private string FormatParseResult(ConversationParseResult parseResult)
        {
            builder.Length = 0;
            builder.AppendLine("入力文");
            builder.AppendLine(parseResult?.input ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("正規化後");
            builder.AppendLine(parseResult?.normalizedInput ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("一致した正規表現");
            AppendLines(builder, parseResult?.matchedRegexRules, match => $"{match.intent} [{match.priority}] {match.pattern}");
            builder.AppendLine();
            builder.AppendLine("抽出された意図");
            AppendLines(builder, parseResult?.intents, intent => $"{intent.intent} [{intent.priority}]");
            builder.AppendLine();
            builder.AppendLine("抽出されたカテゴリ");
            AppendLines(builder, parseResult?.categories, category => $"{category.type}={category.value}");
            return builder.ToString().TrimEnd();
        }

        private string FormatRouteResult(ConversationRouteResult routeResult)
        {
            builder.Length = 0;
            builder.AppendLine("選択された会話ID");
            builder.AppendLine(routeResult != null && routeResult.matched ? routeResult.conversationId : "<なし>");
            builder.AppendLine();
            builder.AppendLine("優先度");
            builder.AppendLine(routeResult != null && routeResult.matched ? routeResult.priority.ToString() : "0");
            builder.AppendLine();
            builder.AppendLine("候補会話ID");

            if (routeResult?.candidates == null || routeResult.candidates.Count == 0)
            {
                builder.AppendLine("<なし>");
                return builder.ToString().TrimEnd();
            }

            for (var i = 0; i < routeResult.candidates.Count; i++)
            {
                var candidate = routeResult.candidates[i];
                builder.AppendLine($"{candidate.conversationId} [{candidate.priority}] {(candidate.selected ? "採用" : "除外")}");
                for (var conditionIndex = 0; conditionIndex < candidate.conditions.Count; conditionIndex++)
                {
                    var condition = candidate.conditions[conditionIndex];
                    builder.AppendLine($"  - {condition.label}: {(condition.passed ? "OK" : "NG")} ({condition.reason})");
                }

                if (!candidate.selected)
                {
                    builder.AppendLine($"  除外理由: {candidate.rejectionReason}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private string FormatState(ConversationGameState state, IReadOnlyList<string> tags)
        {
            builder.Length = 0;
            builder.AppendLine("現在フェーズ");
            builder.AppendLine(ReadStateString(state, "Phase", "CurrentPhase"));
            builder.AppendLine();
            builder.AppendLine("現在タグ");
            if (tags == null || tags.Count == 0)
            {
                builder.AppendLine("<なし>");
            }
            else
            {
                builder.AppendLine(string.Join(", ", tags));
            }

            builder.AppendLine();
            builder.AppendLine("現在フラグ");
            AppendFlagLines(builder, state);
            builder.AppendLine();
            builder.AppendLine("性格値");
            for (var i = 0; i < ConversationPersonalityDebugView.PersonalityKeys.Length; i++)
            {
                var key = ConversationPersonalityDebugView.PersonalityKeys[i];
                builder.AppendLine($"{ConversationPersonalityDebugView.GetDisplayName(key)}: {ReadStateInt(state, key)}");
            }

            builder.AppendLine();
            builder.AppendLine("状態JSON");
            builder.AppendLine(state != null ? state.SaveToJson(true) : "{}");
            return builder.ToString().TrimEnd();
        }

        private string FormatEffects(ConversationDebugRunResult result)
        {
            builder.Length = 0;
            builder.AppendLine("適用効果");
            if (result?.routeResult?.effects != null)
            {
                for (var i = 0; i < result.routeResult.effects.Count; i++)
                {
                    var effect = result.routeResult.effects[i];
                    builder.AppendLine($"{effect.type}:{effect.key}");
                }
            }

            if (result?.eventResult?.appliedEffects != null)
            {
                for (var i = 0; i < result.eventResult.appliedEffects.Count; i++)
                {
                    var effect = result.eventResult.appliedEffects[i];
                    builder.AppendLine($"{effect.type}:{effect.key} {effect.beforeValue} -> {effect.afterValue}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private string FormatEvents(ConversationEventExecutionResult eventResult)
        {
            builder.Length = 0;
            builder.AppendLine("実行イベント");
            if (eventResult?.eventLogs == null || eventResult.eventLogs.Count == 0)
            {
                builder.AppendLine("<なし>");
                return builder.ToString().TrimEnd();
            }

            for (var i = 0; i < eventResult.eventLogs.Count; i++)
            {
                builder.AppendLine(eventResult.eventLogs[i]);
            }

            return builder.ToString().TrimEnd();
        }

        private static List<string> ParseTags(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? new List<string>()
                : new List<string>(rawValue.Split(new[] { ',', '\n', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static void AppendLines<T>(StringBuilder target, IReadOnlyList<T> items, Func<T, string> formatter)
        {
            if (items == null || items.Count == 0)
            {
                target.AppendLine("<なし>");
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                target.AppendLine(formatter(items[i]));
            }
        }

        private static void AppendFlagLines(StringBuilder target, ConversationGameState state)
        {
            var hasFlags = false;
            if (state != null)
            {
                foreach (var pair in state.Values)
                {
                    if (pair.Value.kind != ConversationGameStateValueKind.Boolean)
                    {
                        continue;
                    }

                    target.AppendLine($"{pair.Key}: {(pair.Value.boolValue ? "オン" : "オフ")}");
                    hasFlags = true;
                }
            }

            if (!hasFlags)
            {
                target.AppendLine("<なし>");
            }
        }

        private static string ReadStateString(ConversationGameState state, params string[] keys)
        {
            if (state == null || keys == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                if (state.TryGetString(keys[i], out var value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static int ReadStateInt(ConversationGameState state, string key)
        {
            return state != null && state.TryGetInt(key, out var value) ? value : 0;
        }
    }
}
