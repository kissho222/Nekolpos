using System;
using System.Collections;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Audio;
using Nekolpos.Data;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.System
{
    public sealed class OpenBetaCharacterSetupPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text playerNameLabel;
        [SerializeField] private TMP_Text catNameLabel;
        [SerializeField] private TMP_Text playerCallingLabel;
        [SerializeField] private TMP_Text catFirstPersonLabel;
        [SerializeField] private TMP_Text continueButtonLabel;
        [SerializeField] private TMP_Text backButtonLabel;
        [SerializeField] private TMP_Text noticeText;
        [SerializeField] private TMP_Text catNameKanaWarningText;
        [SerializeField] private InputField playerNameInput;
        [SerializeField] private InputField catNameInput;
        [SerializeField] private InputField playerCallingInput;
        [SerializeField] private InputField catFirstPersonInput;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private bool japaneseCatNameWarningEnabled = true;

        private const int CatNameMaxLength = 20;
        private const string CatNameKanaWarningMessage = "ひらがな・カタカナ・漢字・数字のみ、20文字までです。";
        private Action backRequested;
        private Action<string, string, string, string> submitted;
        private int debugSnapshotSequence;
        private bool loggedVisibleSnapshot;
        private bool loggedRaycastSnapshot;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int debugPeriodicSnapshotCount;
        private int nextDebugPeriodicSnapshotFrame;
#endif

        private void Awake()
        {
            LogPanelDebugSnapshot("Awake");
        }

        private void OnEnable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            debugPeriodicSnapshotCount = 0;
            nextDebugPeriodicSnapshotFrame = Time.frameCount + 30;
            LogPanelDebugSnapshot("OnEnable:before");
#endif
            EnsurePanelInteraction();
            HideOverlappingTitleInputSurfaces();
            BringPanelToFront();
            EnsurePanelVisuals();
            EnsureInputTextVisuals(refreshLabels: false);
            LogPanelDebugSnapshot("OnEnable:after");
            StartCoroutine(EnsureInputTextVisualsAfterActivation());
        }

        public void RefreshForVisiblePanel()
        {
            EnsurePanelInteraction();
            HideOverlappingTitleInputSurfaces();
            BringPanelToFront();
            EnsurePanelVisuals();
            EnsureInputTextVisuals();
            Canvas.ForceUpdateCanvases();
            LogPanelDebugSnapshot("RefreshForVisiblePanel");
        }

        public void Configure(
            string title,
            string playerName,
            string catName,
            string playerCalling,
            string catFirstPerson,
            string continueLabel,
            string backLabel,
            string requiredNotice,
            Action onBack,
            Action<string, string, string, string> onSubmitted)
        {
            ResolveBackButtonReferences();
            ResolveCatNameWarningText();
            backRequested = onBack;
            submitted = onSubmitted;
            if (titleText != null) titleText.text = title;
            if (playerNameLabel != null) playerNameLabel.text = playerName;
            if (catNameLabel != null) catNameLabel.text = catName;
            if (playerCallingLabel != null) playerCallingLabel.text = playerCalling;
            if (catFirstPersonLabel != null) catFirstPersonLabel.text = catFirstPerson;
            if (continueButtonLabel != null) continueButtonLabel.text = continueLabel;
            if (backButtonLabel != null) backButtonLabel.text = backLabel;
            if (noticeText != null)
            {
                noticeText.text = requiredNotice;
                noticeText.gameObject.SetActive(false);
            }

            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(Submit);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(Back);
            }

            if (catNameInput != null)
            {
                catNameInput.characterLimit = CatNameMaxLength;
                catNameInput.onValidateInput = ValidateCatNameCharacter;
                catNameInput.onValueChanged.RemoveListener(HandleCatNameChanged);
                catNameInput.onValueChanged.AddListener(HandleCatNameChanged);
            }

            EnsureWebGLImeInputBridge(playerNameInput);
            EnsureWebGLImeInputBridge(catNameInput);
            EnsureWebGLImeInputBridge(playerCallingInput);
            EnsureWebGLImeInputBridge(catFirstPersonInput);
            EnsurePanelVisuals();
            EnsureInputTextVisuals();
            UpdateCatNameKanaWarning();
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPanelDebugSnapshot("Configure:after");
            StartCoroutine(LogPanelDebugSnapshotAfterFrames("Configure:deferred", 2));
#endif
        }

        public void SetValues(string playerName, string catName, string playerCalling, string catFirstPerson)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPanelDebugSnapshot("SetValues:before");
#endif
            SetInputTextWithoutInvalidCaret(playerNameInput, playerName != null ? playerName.Trim() : string.Empty);
            SetInputTextWithoutInvalidCaret(catNameInput, SanitizeCatName(catName));
            SetInputTextWithoutInvalidCaret(playerCallingInput, playerCalling != null ? playerCalling.Trim() : string.Empty);
            SetInputTextWithoutInvalidCaret(catFirstPersonInput, string.IsNullOrWhiteSpace(catFirstPerson) ? "私" : catFirstPerson.Trim());
            EnsureInputTextVisuals();
            UpdateCatNameKanaWarning();
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPanelDebugSnapshot("SetValues:after");
            StartCoroutine(LogPanelDebugSnapshotAfterFrames("SetValues:deferred", 2));
#endif
        }

        private void Update()
        {
            if (!loggedVisibleSnapshot)
            {
                loggedVisibleSnapshot = true;
                LogPanelDebugSnapshot("Update:visible");
            }

            if (!loggedRaycastSnapshot)
            {
                loggedRaycastSnapshot = true;
                LogRaycastDebugSnapshot("Update:raycast");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshWebGLImeInputBridge(playerNameInput);
            RefreshWebGLImeInputBridge(catNameInput);
            RefreshWebGLImeInputBridge(playerCallingInput);
            RefreshWebGLImeInputBridge(catFirstPersonInput);
            LogPeriodicPanelDebugSnapshot();
#endif
        }

        private void Submit()
        {
            string playerName = playerNameInput != null ? playerNameInput.text.Trim() : string.Empty;
            string catName = catNameInput != null ? SanitizeCatName(catNameInput.text).Trim() : string.Empty;
            string playerCalling = playerCallingInput != null ? playerCallingInput.text.Trim() : string.Empty;
            string catFirstPerson = catFirstPersonInput != null ? catFirstPersonInput.text.Trim() : string.Empty;
            bool valid = !string.IsNullOrWhiteSpace(playerName) &&
                         !string.IsNullOrWhiteSpace(catName) &&
                         IsAllowedCatName(catName) &&
                         !string.IsNullOrWhiteSpace(playerCalling) &&
                         !string.IsNullOrWhiteSpace(catFirstPerson);
            if (noticeText != null)
            {
                noticeText.gameObject.SetActive(!valid);
            }

            if (valid)
            {
                HideWebGLImeInputBridges();
                submitted?.Invoke(playerName, catName, playerCalling, catFirstPerson);
            }
        }

        private void Back()
        {
            HideWebGLImeInputBridges();

            if (noticeText != null)
            {
                noticeText.gameObject.SetActive(false);
            }

            if (catNameKanaWarningText != null)
            {
                catNameKanaWarningText.gameObject.SetActive(false);
            }

            backRequested?.Invoke();
        }

        private void OnDisable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPanelDebugSnapshot("OnDisable:beforeHide");
#endif
            loggedVisibleSnapshot = false;
            loggedRaycastSnapshot = false;
            HideWebGLImeInputBridges();
        }

        private void HandleCatNameChanged(string _)
        {
            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                UpdateCatNameKanaWarning();
                return;
            }

            NormalizeCatNameInputText();
            UpdateCatNameKanaWarning();
        }

        private void NormalizeCatNameInputText()
        {
            if (catNameInput == null)
            {
                return;
            }

            string normalized = SanitizeCatName(catNameInput.text);
            if (string.Equals(catNameInput.text, normalized, StringComparison.Ordinal))
            {
                return;
            }

            int caret = Mathf.Min(catNameInput.caretPosition, normalized.Length);
            catNameInput.SetTextWithoutNotify(normalized);
            catNameInput.caretPosition = caret;
            catNameInput.selectionAnchorPosition = caret;
            catNameInput.selectionFocusPosition = caret;
        }

        private void UpdateCatNameKanaWarning()
        {
            if (catNameKanaWarningText == null)
            {
                return;
            }

            string catName = catNameInput != null ? catNameInput.text.Trim() : string.Empty;
            bool show = japaneseCatNameWarningEnabled &&
                        !string.IsNullOrWhiteSpace(catName) &&
                        !IsAllowedCatName(catName);
            catNameKanaWarningText.gameObject.SetActive(show);
        }

        private static char ValidateCatNameCharacter(string text, int charIndex, char addedChar)
        {
            char normalizedChar = NormalizeCatNameCharacter(addedChar);
            return IsAllowedCatNameCharacter(normalizedChar) ? normalizedChar : '\0';
        }

        private static string SanitizeCatName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            char[] buffer = new char[Mathf.Min(value.Length, CatNameMaxLength)];
            int count = 0;
            for (int i = 0; i < value.Length && count < CatNameMaxLength; i++)
            {
                char c = NormalizeCatNameCharacter(value[i]);
                if (IsAllowedCatNameCharacter(c))
                {
                    buffer[count] = c;
                    count++;
                }
            }

            return new string(buffer, 0, count);
        }

        private static bool IsAllowedCatName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (IsAllowedCatNameCharacter(NormalizeCatNameCharacter(value[i])))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static char NormalizeCatNameCharacter(char c)
        {
            switch (c)
            {
                case '~':
                case '～':
                case '〜':
                case '∼':
                case '∽':
                case '≋':
                case '〰':
                case '﹋':
                case '﹌':
                case '﹏':
                    return 'ー';
                default:
                    return c;
            }
        }

        private static bool IsAllowedCatNameCharacter(char c)
        {
            return (c >= '\u3040' && c <= '\u309F') ||
                   (c >= '\u30A0' && c <= '\u30FF') ||
                   (c >= '\u31F0' && c <= '\u31FF') ||
                   (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\uF900' && c <= '\uFAFF') ||
                   c == '々' ||
                   c == '〆' ||
                   c == 'ヶ' ||
                   (c >= '0' && c <= '9') ||
                   (c >= '\uFF10' && c <= '\uFF19');
        }

        private static void SetInputTextWithoutInvalidCaret(InputField input, string text)
        {
            if (input == null)
            {
                return;
            }

            string value = text ?? string.Empty;
            input.caretPosition = 0;
            input.selectionAnchorPosition = 0;
            input.selectionFocusPosition = 0;
            input.SetTextWithoutNotify(value);
            int caret = value.Length;
            input.caretPosition = caret;
            input.selectionAnchorPosition = caret;
            input.selectionFocusPosition = caret;
        }

        private void EnsureInputTextVisuals(bool refreshLabels = true)
        {
            EnsureInputTextVisual(playerNameInput, refreshLabels);
            EnsureInputTextVisual(catNameInput, refreshLabels);
            EnsureInputTextVisual(playerCallingInput, refreshLabels);
            EnsureInputTextVisual(catFirstPersonInput, refreshLabels);
        }

        private IEnumerator EnsureInputTextVisualsAfterActivation()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            EnsurePanelInteraction();
            BringPanelToFront();
            EnsurePanelVisuals();
            EnsureInputTextVisuals();
            yield return null;
            EnsurePanelInteraction();
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPanelDebugSnapshot("OnEnable:deferred1");
            yield return null;
            LogPanelDebugSnapshot("OnEnable:deferred2");
#endif
        }

        private void EnsurePanelVisuals()
        {
            UIStyle.ApplyPanel(gameObject);
            UIStyle.ApplyInputField(playerNameInput);
            UIStyle.ApplyInputField(catNameInput);
            UIStyle.ApplyInputField(playerCallingInput);
            UIStyle.ApplyInputField(catFirstPersonInput);
            UIStyle.ApplyButton(continueButton);
            UIStyle.ApplyButton(backButton);
        }

        private void EnsurePanelInteraction()
        {
            CanvasGroup panelGroup = GetComponent<CanvasGroup>();
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }

            EnsureInputInteraction(playerNameInput);
            EnsureInputInteraction(catNameInput);
            EnsureInputInteraction(playerCallingInput);
            EnsureInputInteraction(catFirstPersonInput);
        }

        private void HideOverlappingTitleInputSurfaces()
        {
            HideSiblingPanel("CallCatPanel");
        }

        private void BringPanelToFront()
        {
            if (transform.parent == null)
            {
                return;
            }

            transform.SetAsLastSibling();
        }

        private void HideSiblingPanel(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName) || transform.parent == null)
            {
                return;
            }

            Transform sibling = transform.parent.Find(objectName);
            if (sibling != null && sibling != transform)
            {
                sibling.gameObject.SetActive(false);
            }
        }

        private static void EnsureInputInteraction(InputField input)
        {
            if (input == null)
            {
                return;
            }

            if (!input.gameObject.activeSelf)
            {
                input.gameObject.SetActive(true);
            }

            input.enabled = true;
            input.interactable = true;
            if (input.targetGraphic != null)
            {
                input.targetGraphic.raycastTarget = true;
            }

            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = true;
            }

        }

        private static void EnsureInputTextVisual(InputField input, bool refreshLabel)
        {
            if (input == null)
            {
                return;
            }

            input.lineType = InputField.LineType.SingleLine;
            input.customCaretColor = true;
            input.caretColor = UIStyle.NormalText;
            input.selectionColor = new Color(UIStyle.NormalText.r, UIStyle.NormalText.g, UIStyle.NormalText.b, 0.28f);
            input.shouldHideMobileInput = false;

            if (input.isFocused)
            {
                Input.imeCompositionMode = IMECompositionMode.On;
            }

            Text text = input.textComponent;
            if (text == null)
            {
                return;
            }

            text.enabled = true;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;

            if (text.font == null)
            {
                text.font = OpenBetaUiFactory.ResolveLegacyJapaneseInputFont();
            }

            if (text.color.a <= 0.001f)
            {
                Color color = UIStyle.NormalText;
                color.a = 1f;
                text.color = color;
            }

            if (text.canvasRenderer.GetAlpha() <= 0.001f)
            {
                text.canvasRenderer.SetAlpha(1f);
            }

            ImeCompositionVisualController compositionController = input.GetComponent<ImeCompositionVisualController>();
            if (compositionController == null)
            {
                compositionController = input.gameObject.AddComponent<ImeCompositionVisualController>();
            }

            compositionController.Bind(input);
            EnsureVisibleInputTextMirror(input, text);

            if (!refreshLabel || input.isFocused || !string.IsNullOrEmpty(Input.compositionString))
            {
                return;
            }

            text.SetAllDirty();
            input.ForceLabelUpdate();
        }

        private static void EnsureVisibleInputTextMirror(InputField input, Text sourceText)
        {
            CharacterSetupInputTextMirror mirror = input.GetComponent<CharacterSetupInputTextMirror>();
            if (mirror == null)
            {
                mirror = input.gameObject.AddComponent<CharacterSetupInputTextMirror>();
            }

            mirror.Bind(input, sourceText);
            mirror.RefreshNow();
        }

        private static void EnsureWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            if (bridge == null)
            {
                bridge = input.gameObject.AddComponent<WebGLImeInputBridge>();
            }

            bridge.Bind(input);
#endif
        }

        private static void RefreshWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            bridge?.Refresh();
#endif
        }

        private void HideWebGLImeInputBridges()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            HideWebGLImeInputBridge(playerNameInput);
            HideWebGLImeInputBridge(catNameInput);
            HideWebGLImeInputBridge(playerCallingInput);
            HideWebGLImeInputBridge(catFirstPersonInput);

            if (EventSystem.current == null)
            {
                return;
            }

            GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
            if (selectedObject != null && selectedObject.transform != null && selectedObject.transform.IsChildOf(transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
#endif
        }

        private static void HideWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            if (input.isFocused)
            {
                input.DeactivateInputField();
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            bridge?.ForceHideDomInput();
#endif
        }

        private void ResolveBackButtonReferences()
        {
            if (backButton == null)
            {
                Transform buttonTransform = FindDescendant(transform, "BackButton");
                if (buttonTransform != null)
                {
                    backButton = buttonTransform.GetComponent<Button>();
                }
            }

            if (backButton == null)
            {
                backButton = OpenBetaUiFactory.CreateButton("BackButton", transform, out TextMeshProUGUI createdLabel, new Vector2(260f, 48f));
                backButtonLabel = createdLabel;
            }

            if (backButtonLabel == null && backButton != null)
            {
                backButtonLabel = backButton.GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void ResolveCatNameWarningText()
        {
            if (!japaneseCatNameWarningEnabled)
            {
                return;
            }

            if (catNameKanaWarningText == null)
            {
                Transform warningTransform = FindDescendant(transform, "CatNameKanaWarningText");
                if (warningTransform != null)
                {
                    catNameKanaWarningText = warningTransform.GetComponent<TMP_Text>();
                }
            }

            if (catNameKanaWarningText == null)
            {
                Transform row = catNameInput != null ? catNameInput.transform.parent : FindDescendant(transform, "CatNameRow");
                Transform parent = row != null && row.parent != null ? row.parent : transform;
                TextMeshProUGUI warning = OpenBetaUiFactory.CreateTmpText("CatNameKanaWarningText", parent, 18f, FontStyles.Bold);
                warning.color = new Color(0.86f, 0.08f, 0.08f, 1f);
                warning.text = CatNameKanaWarningMessage;
                LayoutElement layout = warning.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = 660f;
                layout.preferredHeight = 24f;
                layout.minHeight = 24f;

                if (row != null)
                {
                    warning.transform.SetSiblingIndex(row.GetSiblingIndex());
                }

                catNameKanaWarningText = warning;
            }

            catNameKanaWarningText.text = CatNameKanaWarningMessage;
            catNameKanaWarningText.gameObject.SetActive(false);
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform result = FindDescendant(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private IEnumerator LogPanelDebugSnapshotAfterFrames(string phase, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            LogPanelDebugSnapshot(phase);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void LogPeriodicPanelDebugSnapshot()
        {
            if (debugPeriodicSnapshotCount >= 10 || Time.frameCount < nextDebugPeriodicSnapshotFrame)
            {
                return;
            }

            debugPeriodicSnapshotCount++;
            nextDebugPeriodicSnapshotFrame = Time.frameCount + 120;
            LogPanelDebugSnapshot($"Update:periodic{debugPeriodicSnapshotCount}");
        }
#endif

        private void LogPanelDebugSnapshot(string phase)
        {
            debugSnapshotSequence++;
            Debug.Log($"[CharacterSetupDebug] phase={phase} seq={debugSnapshotSequence} frame={Time.frameCount} panelActiveSelf={gameObject.activeSelf} panelActiveInHierarchy={gameObject.activeInHierarchy} selected={FormatSelectedObject()} canvasGroups={FormatCanvasGroups(transform)}");
            LogInputDebug("playerName", playerNameInput);
            LogInputDebug("catName", catNameInput);
            LogInputDebug("playerCalling", playerCallingInput);
            LogInputDebug("catFirstPerson", catFirstPersonInput);
        }

        private void LogRaycastDebugSnapshot(string phase)
        {
            LogRaycastDebug(phase, "playerName", playerNameInput);
            LogRaycastDebug(phase, "catName", catNameInput);
            LogRaycastDebug(phase, "playerCalling", playerCallingInput);
            LogRaycastDebug(phase, "catFirstPerson", catFirstPersonInput);
        }

        private static void LogRaycastDebug(string phase, string label, InputField input)
        {
            if (input == null)
            {
                Debug.Log($"[CharacterSetupDebug] raycast phase={phase} input={label} missing");
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            RectTransform rect = input.transform as RectTransform;
            if (eventSystem == null || rect == null)
            {
                Debug.Log($"[CharacterSetupDebug] raycast phase={phase} input={label} unavailable eventSystem={(eventSystem != null)} rect={(rect != null)}");
                return;
            }

            Canvas canvas = input.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            PointerEventData pointer = new PointerEventData(eventSystem)
            {
                position = screenPoint
            };

            List<RaycastResult> results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, results);
            string top = results.Count > 0 ? FormatRaycastResult(results[0]) : "<none>";
            string all = FormatRaycastResults(results, 5);
            Debug.Log($"[CharacterSetupDebug] raycast phase={phase} input={label} point={FormatVector2(screenPoint)} count={results.Count} top={top} all={all}");
        }

        private static void LogInputDebug(string label, InputField input)
        {
            if (input == null)
            {
                Debug.Log($"[CharacterSetupDebug] input={label} missing");
                return;
            }

            Text text = input.textComponent;
            Graphic targetGraphic = input.targetGraphic;
            Image image = input.GetComponent<Image>();
            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            RectTransform rect = input.transform as RectTransform;
            RectTransform textRect = text != null ? text.transform as RectTransform : null;
            bool targetIsText = targetGraphic != null && text != null && targetGraphic == text;
            bool targetIsImage = targetGraphic != null && image != null && targetGraphic == image;
            string textValue = input.text ?? string.Empty;

            Debug.Log(
                $"[CharacterSetupDebug] input={label}" +
                $" activeSelf={input.gameObject.activeSelf}" +
                $" activeInHierarchy={input.gameObject.activeInHierarchy}" +
                $" interactable={input.interactable}" +
                $" enabled={input.enabled}" +
                $" focused={input.isFocused}" +
                $" selected={IsSelected(input.gameObject)}" +
                $" domVisible={(bridge != null && bridge.IsDomInputVisible)}" +
                $" textLen={textValue.Length}" +
                $" textPreview={FormatPreview(textValue)}" +
                $" inputRect={FormatRect(rect)}" +
                $" textRect={FormatRect(textRect)}" +
                $" image={FormatGraphic(image)}" +
                $" target={FormatGraphic(targetGraphic)}" +
                $" targetIsText={targetIsText}" +
                $" targetIsImage={targetIsImage}" +
                $" colors(normal={FormatColor(input.colors.normalColor)}, highlighted={FormatColor(input.colors.highlightedColor)}, selected={FormatColor(input.colors.selectedColor)}, multiplier={input.colors.colorMultiplier:0.###})" +
                $" textGraphic={FormatText(text)}" +
                $" textCanvas={FormatCanvasRenderer(text != null ? text.canvasRenderer : null)}" +
                $" mirror={FormatTmpMirror(input.transform)}" +
                $" parentCanvasGroups={FormatCanvasGroups(input.transform)}");
        }

        private static bool IsSelected(GameObject target)
        {
            return EventSystem.current != null && EventSystem.current.currentSelectedGameObject == target;
        }

        private static string FormatSelectedObject()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null ? selected.name : "<null>";
        }

        private static string FormatPreview(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            string preview = value.Length <= 8 ? value : value.Substring(0, 8) + "...";
            return $"'{preview}'";
        }

        private static string FormatInputFocusState(InputField input)
        {
            if (input == null)
            {
                return "<null>";
            }

            return $"{input.name}(active={input.gameObject.activeInHierarchy},selected={IsSelected(input.gameObject)},focused={input.isFocused},textLen={(input.text != null ? input.text.Length : 0)})";
        }

        private static string FormatGraphic(Graphic graphic)
        {
            if (graphic == null)
            {
                return "<null>";
            }

            return $"{graphic.GetType().Name}:{graphic.name} enabled={graphic.enabled} raycast={graphic.raycastTarget} color={FormatColor(graphic.color)}";
        }

        private static string FormatText(Text text)
        {
            if (text == null)
            {
                return "<null>";
            }

            return $"Text:{text.name} enabled={text.enabled} activeSelf={text.gameObject.activeSelf} activeInHierarchy={text.gameObject.activeInHierarchy} font={text.font?.name ?? "<null>"} fontSize={text.fontSize} align={text.alignment} color={FormatColor(text.color)}";
        }

        private static string FormatCanvasRenderer(CanvasRenderer renderer)
        {
            if (renderer == null)
            {
                return "<null>";
            }

            return $"alpha={renderer.GetAlpha():0.###} cull={renderer.cull}";
        }

        private static string FormatTmpMirror(Transform inputTransform)
        {
            if (inputTransform == null)
            {
                return "<null>";
            }

            Transform mirrorTransform = inputTransform.Find("CharacterSetupVisibleTmpText");
            TextMeshProUGUI mirror = mirrorTransform != null ? mirrorTransform.GetComponent<TextMeshProUGUI>() : null;
            if (mirror == null)
            {
                return "<missing>";
            }

            Canvas canvas = mirror.GetComponent<Canvas>();
            RectTransform rect = mirror.transform as RectTransform;
            return $"TMP:{mirror.name} activeSelf={mirror.gameObject.activeSelf} activeInHierarchy={mirror.gameObject.activeInHierarchy} enabled={mirror.enabled} font={mirror.font?.name ?? "<null>"} fontSize={mirror.fontSize:0.#} color={FormatColor(mirror.color)} rect={FormatRect(rect)} canvas={(canvas != null ? $"override={canvas.overrideSorting},order={canvas.sortingOrder}" : "<null>")} renderer={FormatCanvasRenderer(mirror.canvasRenderer)}";
        }

        private static string FormatRaycastResults(List<RaycastResult> results, int maxCount)
        {
            if (results == null || results.Count == 0)
            {
                return "<none>";
            }

            int count = Mathf.Min(results.Count, Mathf.Max(1, maxCount));
            List<string> values = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(FormatRaycastResult(results[i]));
            }

            return string.Join(" | ", values);
        }

        private static string FormatRaycastResult(RaycastResult result)
        {
            GameObject target = result.gameObject;
            if (target == null)
            {
                return "<null>";
            }

            Graphic graphic = target.GetComponent<Graphic>();
            return $"{target.name}(module={result.module?.GetType().Name ?? "<null>"},depth={result.depth},sorting={result.sortingLayer}/{result.sortingOrder},distance={result.distance:0.###},graphic={FormatGraphic(graphic)})";
        }

        private static string FormatRect(RectTransform rect)
        {
            if (rect == null)
            {
                return "<null>";
            }

            Rect r = rect.rect;
            return $"name={rect.name} pos={FormatVector2(rect.anchoredPosition)} size={FormatVector2(r.size)} anchors={FormatVector2(rect.anchorMin)}-{FormatVector2(rect.anchorMax)} offsets={FormatVector2(rect.offsetMin)}/{FormatVector2(rect.offsetMax)} scale={rect.lossyScale.x:0.###},{rect.lossyScale.y:0.###},{rect.lossyScale.z:0.###}";
        }

        private static string FormatCanvasGroups(Transform source)
        {
            if (source == null)
            {
                return "<null>";
            }

            List<string> values = new List<string>();
            CanvasGroup[] groups = source.GetComponentsInParent<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                values.Add($"{group.name}(alpha={group.alpha:0.###},interactable={group.interactable},blocks={group.blocksRaycasts},ignoreParent={group.ignoreParentGroups},active={group.gameObject.activeInHierarchy})");
            }

            return values.Count > 0 ? string.Join(">", values) : "<none>";
        }

        private static string FormatColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"({value.x:0.##},{value.y:0.##})";
        }

        [DisallowMultipleComponent]
        [RequireComponent(typeof(InputField))]
        private sealed class CharacterSetupInputTextMirror : MonoBehaviour
        {
            private const string LegacyMirrorObjectName = "CharacterSetupVisibleText";
            private const string MirrorObjectName = "CharacterSetupVisibleTmpText";
            private const string CaretObjectName = "CharacterSetupVisibleCaret";
            private InputField input;
            private Text sourceText;
            private TextMeshProUGUI mirrorText;
            private Image caretImage;
            private float nextCaretBlinkTime;
            private bool caretVisible = true;

            public void Bind(InputField targetInput, Text targetSource)
            {
                input = targetInput != null ? targetInput : GetComponent<InputField>();
                sourceText = targetSource != null ? targetSource : input?.textComponent;
                EnsureMirror();
                RefreshNow();
            }

            private void Awake()
            {
                input ??= GetComponent<InputField>();
                sourceText ??= input != null ? input.textComponent : null;
                EnsureMirror();
            }

            private void LateUpdate()
            {
                RefreshNow();
            }

            public void RefreshNow()
            {
                if (input == null)
                {
                    input = GetComponent<InputField>();
                }

                if (sourceText == null && input != null)
                {
                    sourceText = input.textComponent;
                }

                EnsureMirror();
                if (mirrorText == null || input == null)
                {
                    return;
                }

                if (input.isFocused)
                {
                    Input.imeCompositionMode = IMECompositionMode.On;
                }

                HideSourceInputText();

                bool domInputVisible = false;
#if UNITY_WEBGL && !UNITY_EDITOR
                WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
                domInputVisible = bridge != null && bridge.IsDomInputVisible;
#endif

                mirrorText.text = BuildVisibleText();
                if (sourceText != null)
                {
                    mirrorText.fontSize = sourceText.fontSize;
                    mirrorText.alignment = ToTmpAlignment(sourceText.alignment);
                    mirrorText.textWrappingMode = TextWrappingModes.NoWrap;
                    mirrorText.overflowMode = TextOverflowModes.Overflow;
                }
                else
                {
                    mirrorText.fontSize = 22;
                    mirrorText.alignment = TextAlignmentOptions.MidlineLeft;
                    mirrorText.textWrappingMode = TextWrappingModes.NoWrap;
                    mirrorText.overflowMode = TextOverflowModes.Overflow;
                }

                if (mirrorText.font == null)
                {
                    mirrorText.font = TMP_Settings.defaultFontAsset;
                }

                mirrorText.color = UIStyle.NormalText;
                mirrorText.enabled = input.gameObject.activeInHierarchy && !domInputVisible;
                mirrorText.maskable = false;
                mirrorText.raycastTarget = false;
                mirrorText.transform.SetAsLastSibling();

                Canvas mirrorCanvas = mirrorText.GetComponent<Canvas>();
                if (mirrorCanvas != null)
                {
                    mirrorCanvas.overrideSorting = true;
                    mirrorCanvas.sortingOrder = 1000;
                }

                CanvasRenderer renderer = mirrorText.canvasRenderer;
                if (renderer != null)
                {
                    renderer.cull = false;
                    if (renderer.GetAlpha() <= 0.001f)
                    {
                        renderer.SetAlpha(1f);
                    }
                }

                UpdateCaret(domInputVisible);
            }

            private string BuildVisibleText()
            {
                string text = input.text ?? string.Empty;
                int caret = Mathf.Clamp(input.caretPosition, 0, text.Length);
                string composition = input.isFocused ? Input.compositionString ?? string.Empty : string.Empty;

                if (!string.IsNullOrEmpty(composition))
                {
                    string escapedComposition = RichTextEscaper.Escape(composition);
                    return RichTextEscaper.Escape(text.Substring(0, caret)) +
                           $"<u>{escapedComposition}</u>" +
                           RichTextEscaper.Escape(text.Substring(caret));
                }

                return RichTextEscaper.Escape(text);
            }

            private bool ShouldShowCaret()
            {
                if (Time.unscaledTime >= nextCaretBlinkTime)
                {
                    caretVisible = !caretVisible;
                    nextCaretBlinkTime = Time.unscaledTime + 0.5f;
                }

                return caretVisible;
            }

            private void HideSourceInputText()
            {
                if (sourceText != null)
                {
                    Color hidden = sourceText.color;
                    hidden.a = 0f;
                    sourceText.color = hidden;
                    sourceText.raycastTarget = false;

                    CanvasRenderer sourceRenderer = sourceText.canvasRenderer;
                    if (sourceRenderer != null)
                    {
                        sourceRenderer.SetAlpha(0f);
                    }
                }

                if (input == null)
                {
                    return;
                }

                Color hiddenCaret = input.caretColor;
                hiddenCaret.a = 0f;
                Color hiddenSelection = input.selectionColor;
                hiddenSelection.a = 0f;
                input.customCaretColor = true;
                input.caretColor = hiddenCaret;
                input.selectionColor = hiddenSelection;
            }

            private void UpdateCaret(bool domInputVisible)
            {
                EnsureCaret();
                if (caretImage == null || input == null || mirrorText == null)
                {
                    return;
                }

                string composition = input.isFocused ? Input.compositionString ?? string.Empty : string.Empty;
                bool visible = !domInputVisible && input.isFocused && string.IsNullOrEmpty(composition) && ShouldShowCaret();
                caretImage.enabled = visible;
                if (!visible)
                {
                    return;
                }

                RectTransform caretRect = caretImage.transform as RectTransform;
                RectTransform mirrorRect = mirrorText.transform as RectTransform;
                if (caretRect == null || mirrorRect == null)
                {
                    return;
                }

                string text = input.text ?? string.Empty;
                int caret = Mathf.Clamp(input.caretPosition, 0, text.Length);
                float x = 0f;
                if (caret > 0)
                {
                    string prefix = RichTextEscaper.Escape(text.Substring(0, caret));
                    x += mirrorText.GetPreferredValues(prefix, 10000f, mirrorRect.rect.height).x;
                }

                x = Mathf.Clamp(x, 0f, Mathf.Max(0f, mirrorRect.rect.width - 2f));
                caretRect.anchoredPosition = new Vector2(x, 0f);
                caretRect.sizeDelta = new Vector2(2f, Mathf.Max(18f, mirrorText.fontSize * 1.2f));
            }

            private void EnsureMirror()
            {
                if (mirrorText != null)
                {
                    return;
                }

                Transform legacy = transform.Find(LegacyMirrorObjectName);
                if (legacy != null)
                {
                    legacy.gameObject.SetActive(false);
                }

                Transform existing = transform.Find(MirrorObjectName);
                GameObject mirrorObject = existing != null
                    ? existing.gameObject
                    : new GameObject(MirrorObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                if (existing == null)
                {
                    mirrorObject.transform.SetParent(transform, false);
                }

                mirrorObject.SetActive(true);

                mirrorText = mirrorObject.GetComponent<TextMeshProUGUI>();
                RectTransform mirrorRect = mirrorObject.transform as RectTransform;
                RectTransform sourceRect = sourceText != null ? sourceText.transform as RectTransform : null;
                if (mirrorRect != null)
                {
                    if (sourceRect != null)
                    {
                        mirrorRect.anchorMin = sourceRect.anchorMin;
                        mirrorRect.anchorMax = sourceRect.anchorMax;
                        mirrorRect.pivot = sourceRect.pivot;
                        mirrorRect.offsetMin = sourceRect.offsetMin;
                        mirrorRect.offsetMax = sourceRect.offsetMax;
                    }
                    else
                    {
                        mirrorRect.anchorMin = Vector2.zero;
                        mirrorRect.anchorMax = Vector2.one;
                        mirrorRect.offsetMin = new Vector2(12f, 6f);
                        mirrorRect.offsetMax = new Vector2(-12f, -6f);
                    }

                    mirrorRect.localScale = Vector3.one;
                }

                EnsureCaret();
            }

            private void EnsureCaret()
            {
                if (mirrorText == null)
                {
                    return;
                }

                if (caretImage != null)
                {
                    return;
                }

                Transform existing = mirrorText.transform.Find(CaretObjectName);
                GameObject caretObject = existing != null
                    ? existing.gameObject
                    : new GameObject(CaretObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                if (existing == null)
                {
                    caretObject.transform.SetParent(mirrorText.transform, false);
                }

                caretObject.SetActive(true);
                caretImage = caretObject.GetComponent<Image>();
                caretImage.color = UIStyle.NormalText;
                caretImage.raycastTarget = false;

                RectTransform caretRect = caretObject.transform as RectTransform;
                if (caretRect != null)
                {
                    caretRect.anchorMin = new Vector2(0f, 0.5f);
                    caretRect.anchorMax = new Vector2(0f, 0.5f);
                    caretRect.pivot = new Vector2(0.5f, 0.5f);
                    caretRect.localScale = Vector3.one;
                }
            }

            private static TextAlignmentOptions ToTmpAlignment(TextAnchor alignment)
            {
                switch (alignment)
                {
                    case TextAnchor.UpperLeft:
                        return TextAlignmentOptions.TopLeft;
                    case TextAnchor.UpperCenter:
                        return TextAlignmentOptions.Top;
                    case TextAnchor.UpperRight:
                        return TextAlignmentOptions.TopRight;
                    case TextAnchor.MiddleCenter:
                        return TextAlignmentOptions.Midline;
                    case TextAnchor.MiddleRight:
                        return TextAlignmentOptions.MidlineRight;
                    case TextAnchor.LowerLeft:
                        return TextAlignmentOptions.BottomLeft;
                    case TextAnchor.LowerCenter:
                        return TextAlignmentOptions.Bottom;
                    case TextAnchor.LowerRight:
                        return TextAlignmentOptions.BottomRight;
                    case TextAnchor.MiddleLeft:
                    default:
                        return TextAlignmentOptions.MidlineLeft;
                }
            }
        }

    }
}
