using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nekolpos.UI;

namespace Nekolpos.System
{
    public sealed class WebGLImeInputBridge : MonoBehaviour, IPointerDownHandler, ISelectHandler
    {
        [SerializeField] private ChatUIController chatUI;

        private InputField inputField;
        private RectTransform inputRect;
        private Canvas inputCanvas;
        private string registeredObjectName;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool hideUnityInputVisualsWhileDomInputVisible = true;
        private bool isVisible;
        private bool savedTextColor;
        private Color originalTextColor;
        private bool savedCaretVisuals;
        private bool originalCustomCaretColor;
        private Color originalCaretColor;
        private Color originalSelectionColor;
        private bool loggedTextHidden;
        private bool isDomFocused;
        private int forceShowUntilFrame = -1;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_Register(string objectName, int debugLogging);

        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_Show(
            string objectName,
            string text,
            int x,
            int y,
            int width,
            int height,
            int fontSize,
            string color);

        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_Hide(string objectName);

        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_HideAll();

        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_Deactivate(string objectName);

        [DllImport("__Internal")]
        private static extern void NekolposWebGLIme_SetText(string text);
#endif

        public void Bind(ChatUIController controller)
        {
            chatUI = controller;
#if UNITY_WEBGL && !UNITY_EDITOR
            hideUnityInputVisualsWhileDomInputVisible = true;
#endif
            BindInputField(chatUI != null ? chatUI.chatInputField : null, true);
        }

        public void Bind(InputField field)
        {
            chatUI = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            hideUnityInputVisualsWhileDomInputVisible = false;
#endif
            BindInputField(field, false);
        }

        private bool showWheneverActive;
        private bool isSuspended;

        public void SetShowWheneverActive(bool value)
        {
            showWheneverActive = value;
        }

        private void BindInputField(InputField field, bool showWheneverInputIsActive)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Hide();
#endif
            inputField = field;
            inputRect = inputField != null ? inputField.transform as RectTransform : null;
            inputCanvas = inputField != null ? inputField.GetComponentInParent<Canvas>() : null;
            showWheneverActive = showWheneverInputIsActive;
#if UNITY_WEBGL && !UNITY_EDITOR
            isVisible = false;
            savedTextColor = false;
            savedCaretVisuals = false;
            loggedTextHidden = false;
            isDomFocused = false;
            forceShowUntilFrame = -1;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            SetUnityKeyboardCapture(true);
            registeredObjectName = gameObject.name;
            NekolposWebGLIme_Register(registeredObjectName, Debug.isDebugBuild ? 1 : 0);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void OnEnable()
        {
            if (chatUI == null)
            {
                Refresh();
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            Hide();
        }

        private void Update()
        {
            if (chatUI != null)
            {
                return;
            }

            Refresh();
        }
#endif

        public void OnPointerDown(PointerEventData eventData)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ForceShowForSeveralFrames();
            Refresh();
#endif
        }

        public void OnSelect(BaseEventData eventData)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ForceShowForSeveralFrames();
            Refresh();
#endif
        }

        public void Refresh()
        {
            if (isSuspended)
            {
                Hide();
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (inputField == null)
            {
                Hide();
                return;
            }

            if (inputRect == null)
            {
                Hide();
                return;
            }

            if (!inputField.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

            if (!inputField.interactable)
            {
                Hide();
                return;
            }

            bool selectedByEventSystem = EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == inputField.gameObject;
            bool forceShow = Time.frameCount <= forceShowUntilFrame;
            if (!showWheneverActive && !inputField.isFocused && !selectedByEventSystem && !forceShow && !isDomFocused)
            {
                if (!Application.isFocused && isVisible)
                {
                    return;
                }

                DeactivateDomInput();
                return;
            }

            Rect screenRect = GetScreenRect(inputRect, inputCanvas);
            int width = Mathf.Max(1, Mathf.RoundToInt(screenRect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(screenRect.height));
            int fontSize = ResolveFontSize(inputField);
            string color = ColorUtility.ToHtmlStringRGBA(ResolveTextColor());

            SetUnityKeyboardCapture(false);
            NekolposWebGLIme_Show(
                registeredObjectName,
                inputField.text ?? string.Empty,
                Mathf.RoundToInt(screenRect.xMin),
                Mathf.RoundToInt(screenRect.yMin),
                width,
                height,
                fontSize,
                "#" + color);
            if (hideUnityInputVisualsWhileDomInputVisible)
            {
                HideUnityInputVisualsWhileDomInputIsVisible();
            }
            else
            {
                RestoreUnityText();
            }

            isVisible = true;
#endif
        }

        public void Hide()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            NekolposWebGLIme_Hide(registeredObjectName ?? gameObject.name);
            isVisible = false;
            isDomFocused = false;
            RestoreUnityText();
            SetUnityKeyboardCapture(true);
#endif
        }

        public void ForceHideDomInput()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            NekolposWebGLIme_HideAll();
            isVisible = false;
            isDomFocused = false;
            RestoreUnityText();
            SetUnityKeyboardCapture(true);
#endif
        }

        public void SetSuspended(bool suspended)
        {
            if (isSuspended == suspended)
            {
                return;
            }

            isSuspended = suspended;
            if (isSuspended)
            {
                ForceHideDomInput();
            }
        }

        public void SyncText(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            NekolposWebGLIme_SetText(text ?? string.Empty);
#endif
        }

        public bool IsExternalImeComposing { get; private set; }

        public bool IsDomInputVisible
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return isVisible;
#else
                return false;
#endif
            }
        }

        public void SuppressUnityTextForDomInput()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (isVisible && hideUnityInputVisualsWhileDomInputVisible)
            {
                HideUnityInputVisualsWhileDomInputIsVisible();
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static void SetUnityKeyboardCapture(bool capture)
        {
            WebGLInput.captureAllKeyboardInput = capture;
        }

        private void DeactivateDomInput()
        {
            if (!isVisible)
            {
                return;
            }

            NekolposWebGLIme_Deactivate(registeredObjectName ?? gameObject.name);
            isVisible = false;
            isDomFocused = false;
            RestoreUnityText();
            SetUnityKeyboardCapture(true);
        }

        private void HideUnityInputVisualsWhileDomInputIsVisible()
        {
            Text text = inputField != null ? inputField.textComponent : null;
            if (text == null)
            {
                return;
            }

            if (!savedTextColor)
            {
                originalTextColor = text.color;
                savedTextColor = true;
            }

            Color hidden = text.color;
            hidden.a = 0f;
            text.color = hidden;

            if (inputField != null)
            {
                if (!savedCaretVisuals)
                {
                    originalCustomCaretColor = inputField.customCaretColor;
                    originalCaretColor = inputField.caretColor;
                    originalSelectionColor = inputField.selectionColor;
                    savedCaretVisuals = true;
                }

                Color hiddenCaret = inputField.caretColor;
                hiddenCaret.a = 0f;
                Color hiddenSelection = inputField.selectionColor;
                hiddenSelection.a = 0f;
                inputField.customCaretColor = true;
                inputField.caretColor = hiddenCaret;
                inputField.selectionColor = hiddenSelection;
            }

            if (!loggedTextHidden)
            {
                Debug.Log($"[WebGLImeInputBridge] Unity text hidden while DOM input is visible input={inputField.name} frame={Time.frameCount}.");
                loggedTextHidden = true;
            }
        }

        private void RestoreUnityText()
        {
            Text text = inputField != null ? inputField.textComponent : null;
            if (text == null || !savedTextColor)
            {
                if (text != null)
                {
                    Color current = text.color;
                    if (current.a <= 0.001f)
                    {
                        current.a = 1f;
                        text.color = current;
                    }

                    text.enabled = true;
                }

                RestoreUnityCaretVisuals();
                return;
            }

            text.color = originalTextColor;
            text.enabled = true;
            savedTextColor = false;
            RestoreUnityCaretVisuals();
            if (loggedTextHidden)
            {
                Debug.Log($"[WebGLImeInputBridge] Unity text restored input={(inputField != null ? inputField.name : "<null>")} frame={Time.frameCount}.");
                loggedTextHidden = false;
            }
        }

        private void RestoreUnityCaretVisuals()
        {
            if (inputField == null || !savedCaretVisuals)
            {
                return;
            }

            inputField.customCaretColor = originalCustomCaretColor;
            inputField.caretColor = originalCaretColor;
            inputField.selectionColor = originalSelectionColor;
            savedCaretVisuals = false;
        }

        private void ForceShowForSeveralFrames()
        {
            forceShowUntilFrame = Time.frameCount + 8;
        }
#endif

        public void OnWebGLImeTextChanged(string value)
        {
            if (chatUI != null)
            {
                chatUI.SetExternalImeInputText(value);
                return;
            }

            SetBoundInputFieldText(value);
        }

        public void OnWebGLImeSubmit(string value)
        {
            if (chatUI != null)
            {
                Debug.Log($"[WebGLImeInputBridge] DOM submit received input={inputField?.name ?? "<null>"} length={(value != null ? value.Length : 0)} frame={Time.frameCount}.");
                bool submitted = chatUI.SubmitExternalImeInput(value);
                chatUI.SuppressNextEndEditSubmit();
                Debug.Log($"[WebGLImeInputBridge] DOM submit completed submitted={submitted} frame={Time.frameCount}.");
                Hide();
                return;
            }

            SetBoundInputFieldText(value);
            inputField?.DeactivateInputField();
            Hide();
        }

        public void OnWebGLImeHistoryPrevious(string value)
        {
            chatUI?.TryNavigateExternalInputHistory(-1, value);
        }

        public void OnWebGLImeHistoryNext(string value)
        {
            chatUI?.TryNavigateExternalInputHistory(1, value);
        }

        public void OnWebGLImeEscape(string value)
        {
            if (chatUI != null)
            {
                chatUI.SetExternalImeInputText(value);
            }

            OpenBetaPauseMenuController pauseMenu =
                FindFirstObjectByType<OpenBetaPauseMenuController>(FindObjectsInactive.Include);
            pauseMenu?.TryToggleMenuFromExternalInput();
        }

        public void OnWebGLImeBlur(string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            isDomFocused = false;
            SetUnityKeyboardCapture(true);
#endif
            if (chatUI != null)
            {
                chatUI.SetExternalImeInputText(value);
            }
            else
            {
                SetBoundInputFieldText(value);
            }
        }

        public void OnWebGLImeFocus(string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            isDomFocused = true;
            SetUnityKeyboardCapture(false);
            ForceShowForSeveralFrames();
#endif
            if (chatUI != null)
            {
                chatUI.SetExternalImeInputText(value);
            }
            else
            {
                SetBoundInputFieldText(value);
            }
        }

        public void OnWebGLImeExternalPointerDown(string value)
        {
            if (chatUI != null)
            {
                chatUI.SetExternalImeInputText(value);
                inputField?.DeactivateInputField();
            }
            else
            {
                SetBoundInputFieldText(value);
                inputField?.DeactivateInputField();
            }

            if (EventSystem.current != null &&
                inputField != null &&
                EventSystem.current.currentSelectedGameObject == inputField.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            Hide();
        }

        public void OnWebGLImeCompositionStart(string _)
        {
            IsExternalImeComposing = true;
            chatUI?.SetExternalImeCompositionActive(true);
        }

        public void OnWebGLImeCompositionEnd(string value)
        {
            IsExternalImeComposing = false;
            chatUI?.SetExternalImeCompositionActive(false);
            OnWebGLImeTextChanged(value);
        }

        private void SetBoundInputFieldText(string value)
        {
            if (inputField == null)
            {
                return;
            }

            string text = value ?? string.Empty;
            inputField.text = text;
            int caret = Mathf.Clamp(text.Length, 0, text.Length);
            inputField.caretPosition = caret;
            inputField.selectionAnchorPosition = caret;
            inputField.selectionFocusPosition = caret;
        }

        private static Rect GetScreenRect(RectTransform rectTransform, Canvas canvas)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
        }

        private static int ResolveFontSize(InputField field)
        {
            Text text = field != null ? field.textComponent : null;
            if (text == null)
            {
                return 20;
            }

            RectTransform textRect = text.transform as RectTransform;
            float scale = textRect != null ? Mathf.Abs(textRect.lossyScale.y) : 1f;
            if (scale <= 0.001f)
            {
                scale = 1f;
            }

            return Mathf.Max(12, Mathf.RoundToInt(text.fontSize * scale));
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private Color ResolveTextColor()
        {
            if (savedTextColor)
            {
                return originalTextColor;
            }

            Text text = inputField != null ? inputField.textComponent : null;
            if (text == null)
            {
                return Color.white;
            }

            Color color = text.color;
            if (color.a <= 0.001f)
            {
                color = UIStyle.NormalText;
            }

            return color;
        }
#endif
    }
}
