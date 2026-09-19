using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Backgammon.Conversation
{
    public sealed class ConversationUIController : MonoBehaviour
    {
        [Header("Required References")]
        [SerializeField] private RectTransform messageWindow;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform choiceContainer;
        [SerializeField] private ConversationLogView logWindow;
        [SerializeField] private TMP_Text nameLabel;

        [Header("Optional References")]
        [SerializeField] private ScrollRect messageScrollRect;
        [SerializeField] private ScrollRect logScrollRect;
        [SerializeField] private ConversationChoiceButton choiceButtonPrefab;
        [SerializeField] private CanvasGroup inputCanvasGroup;
        [SerializeField] private CanvasGroup choiceCanvasGroup;

        [Header("Behavior")]
        [SerializeField] private float charactersPerSecond = 30f;
        [SerializeField] private bool showChoiceNumberShortcuts = true;
        [SerializeField] private bool submitOnEnter = true;
        [SerializeField] private bool focusInputOnEnable = true;
        [SerializeField] private bool autoClearInputOnSubmit = true;

        private readonly List<ConversationChoiceButton> activeChoiceButtons = new();

        private Coroutine typingCoroutine;
        private TaskCompletionSource<bool> typingCompletionSource;
        private TaskCompletionSource<string> inputCompletionSource;
        private TaskCompletionSource<int> choiceCompletionSource;
        private bool skipRequested;
        private bool inputLocked;
        private bool freeInputEnabled = true;

        public event Action AdvanceRequested;
        public event Action<string> InputSubmitted;
        public event Action<int, string> ChoiceSelected;

        public bool IsTyping => typingCoroutine != null;
        public bool InputLocked => inputLocked;

        public void SetInputLock(bool locked)
        {
            inputLocked = locked;
            RefreshInputState();
            RefreshChoiceState();
        }

        public void SetFreeInputEnabled(bool enabled)
        {
            freeInputEnabled = enabled;
            if (inputField != null)
            {
                inputField.gameObject.SetActive(enabled);
            }

            RefreshInputState();
        }

        public void SetSpeakerName(string speakerNameTemplate, ConversationGameState state = null)
        {
            if (nameLabel == null)
            {
                return;
            }

            nameLabel.text = ConversationVariableResolver.Resolve(speakerNameTemplate, state);
        }

        public async Task ShowMessageAsync(
            string speakerNameTemplate,
            string messageTemplate,
            ConversationGameState state = null,
            bool useTyping = true,
            bool addToLog = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetSpeakerName(speakerNameTemplate, state);

            var resolvedMessage = ConversationVariableResolver.Resolve(messageTemplate, state);
            if (addToLog)
            {
                logWindow?.AddEntry(ConversationLogEntryType.Message, nameLabel != null ? nameLabel.text : string.Empty, resolvedMessage);
            }

            if (messageText == null)
            {
                return;
            }

            CompleteTypingImmediately();
            messageText.text = resolvedMessage;
            messageText.maxVisibleCharacters = 0;
            ResetMessageScroll();

            if (!useTyping || charactersPerSecond <= 0f || string.IsNullOrEmpty(resolvedMessage))
            {
                messageText.maxVisibleCharacters = int.MaxValue;
                return;
            }

            typingCompletionSource = new TaskCompletionSource<bool>();
            typingCoroutine = StartCoroutine(TypeMessageRoutine(resolvedMessage, cancellationToken));
            await typingCompletionSource.Task;
        }

        public void SkipTyping()
        {
            if (!IsTyping)
            {
                return;
            }

            skipRequested = true;
        }

        public async Task<string> WaitForInputAsync(string initialValue = "", CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetFreeInputEnabled(true);
            RefreshInputState();
            ClearChoices();

            if (inputField == null)
            {
                return string.Empty;
            }

            inputField.text = initialValue ?? string.Empty;
            if (focusInputOnEnable)
            {
                ActivateInputField();
            }

            inputCompletionSource = new TaskCompletionSource<string>();
            using (cancellationToken.Register(() => inputCompletionSource.TrySetCanceled(cancellationToken)))
            {
                return await inputCompletionSource.Task;
            }
        }

        public async Task<int> ShowChoicesAsync(
            IReadOnlyList<string> choiceTemplates,
            ConversationGameState state = null,
            bool addSelectedChoiceToLog = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderChoices(choiceTemplates, state, addSelectedChoiceToLog);
            if (activeChoiceButtons.Count == 0)
            {
                return -1;
            }

            choiceCompletionSource = new TaskCompletionSource<int>();
            using (cancellationToken.Register(() => choiceCompletionSource.TrySetCanceled(cancellationToken)))
            {
                return await choiceCompletionSource.Task;
            }
        }

        public void ShowChoices(IReadOnlyList<string> choiceTemplates, ConversationGameState state = null, bool addSelectedChoiceToLog = true)
        {
            RenderChoices(choiceTemplates, state, addSelectedChoiceToLog);
        }

        public void AppendEventLog(string eventTextTemplate, ConversationGameState state = null)
        {
            var resolvedText = ConversationVariableResolver.Resolve(eventTextTemplate, state);
            logWindow?.AddEntry(ConversationLogEntryType.Event, string.Empty, resolvedText);
        }

        public void ClearChoices()
        {
            for (var i = activeChoiceButtons.Count - 1; i >= 0; i--)
            {
                if (activeChoiceButtons[i] != null)
                {
                    Destroy(activeChoiceButtons[i].gameObject);
                }
            }

            activeChoiceButtons.Clear();
            RefreshChoiceState();
        }

        public void ClearLog()
        {
            logWindow?.Clear();
        }

        public void SubmitInput()
        {
            if (inputLocked || inputField == null)
            {
                return;
            }

            var submittedText = inputField.text ?? string.Empty;
            if (autoClearInputOnSubmit)
            {
                inputField.text = string.Empty;
            }

            logWindow?.AddEntry(ConversationLogEntryType.PlayerInput, string.Empty, submittedText);
            InputSubmitted?.Invoke(submittedText);
            inputCompletionSource?.TrySetResult(submittedText);
            inputCompletionSource = null;

            if (focusInputOnEnable && freeInputEnabled)
            {
                ActivateInputField();
            }
        }

        public void RequestAdvance()
        {
            if (IsTyping)
            {
                SkipTyping();
                return;
            }

            if (!inputLocked)
            {
                AdvanceRequested?.Invoke();
            }
        }

        private void Reset()
        {
            messageWindow = FindChildComponent<RectTransform>("MessageWindow", "ChatWindowPanel");
            messageText = FindTextForContainer(messageWindow, "MessageText", "BodyText", "Text");
            inputField = FindChildComponent<TMP_InputField>("InputField", "ChatInputField");
            choiceContainer = FindChildComponent<RectTransform>("ChoiceContainer", "ChoicesPanel", "ChoicePanel");
            logWindow = FindChildComponent<ConversationLogView>("LogWindow", "ChatLogWindow");
            nameLabel = FindChildComponent<TMP_Text>("NameLabel", "SpeakerNameLabel", "Name");
            if (messageWindow != null && messageScrollRect == null)
            {
                messageScrollRect = messageWindow.GetComponentInChildren<ScrollRect>(true);
            }
        }

        private void Awake()
        {
            RefreshInputState();
            RefreshChoiceState();
            if (messageText != null)
            {
                messageText.textWrappingMode = TextWrappingModes.Normal;
                messageText.overflowMode = TextOverflowModes.Overflow;
            }
        }

        private void Update()
        {
            if (IsTyping && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
            {
                SkipTyping();
                return;
            }

            if (submitOnEnter
                && freeInputEnabled
                && !inputLocked
                && inputField != null
                && inputField.isFocused
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                SubmitInput();
                return;
            }

            if (!inputLocked && activeChoiceButtons.Count > 0)
            {
                for (var i = 0; i < activeChoiceButtons.Count && i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    {
                        SelectChoice(i, true);
                        return;
                    }
                }
            }
        }

        private void RenderChoices(IReadOnlyList<string> choiceTemplates, ConversationGameState state, bool addSelectedChoiceToLog)
        {
            ClearChoices();
            if (choiceContainer == null || choiceTemplates == null)
            {
                return;
            }

            for (var i = 0; i < choiceTemplates.Count; i++)
            {
                var resolvedText = ConversationVariableResolver.Resolve(choiceTemplates[i], state);
                var choiceButton = CreateChoiceButton();
                choiceButton.Bind(
                    i,
                    resolvedText,
                    showChoiceNumberShortcuts,
                    (index, text) => OnChoiceSelected(index, text, addSelectedChoiceToLog));
                activeChoiceButtons.Add(choiceButton);
            }

            RefreshChoiceState();
        }

        private void OnChoiceSelected(int index, string text, bool addSelectedChoiceToLog)
        {
            SelectChoice(index, addSelectedChoiceToLog, text);
        }

        private void SelectChoice(int index, bool addSelectedChoiceToLog, string explicitText = null)
        {
            if (index < 0 || index >= activeChoiceButtons.Count || inputLocked)
            {
                return;
            }

            var selectedText = explicitText ?? activeChoiceButtons[index].ChoiceText;
            if (addSelectedChoiceToLog)
            {
                logWindow?.AddEntry(ConversationLogEntryType.Choice, string.Empty, selectedText);
            }

            ChoiceSelected?.Invoke(index, selectedText);
            choiceCompletionSource?.TrySetResult(index);
            choiceCompletionSource = null;
        }

        private IEnumerator TypeMessageRoutine(string resolvedMessage, CancellationToken cancellationToken)
        {
            skipRequested = false;
            messageText.maxVisibleCharacters = 0;
            messageText.ForceMeshUpdate();
            var totalCharacters = messageText.textInfo.characterCount;
            var visibleCharacters = 0f;

            while (visibleCharacters < totalCharacters && !skipRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                visibleCharacters += charactersPerSecond * Time.unscaledDeltaTime;
                messageText.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(visibleCharacters), 0, totalCharacters);
                ScrollMessageToBottom();
                yield return null;
            }

            messageText.maxVisibleCharacters = int.MaxValue;
            ScrollMessageToBottom();
            typingCoroutine = null;
            typingCompletionSource?.TrySetResult(true);
            typingCompletionSource = null;
        }

        private void CompleteTypingImmediately()
        {
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            skipRequested = false;
            if (messageText != null)
            {
                messageText.maxVisibleCharacters = int.MaxValue;
            }

            typingCompletionSource?.TrySetResult(true);
            typingCompletionSource = null;
        }

        private void RefreshInputState()
        {
            if (inputField == null)
            {
                return;
            }

            inputField.readOnly = inputLocked;
            inputField.interactable = freeInputEnabled && !inputLocked;
            if (inputCanvasGroup != null)
            {
                inputCanvasGroup.alpha = freeInputEnabled ? 1f : 0.5f;
                inputCanvasGroup.interactable = freeInputEnabled && !inputLocked;
                inputCanvasGroup.blocksRaycasts = freeInputEnabled && !inputLocked;
            }
        }

        private void RefreshChoiceState()
        {
            var interactable = !inputLocked;
            for (var i = 0; i < activeChoiceButtons.Count; i++)
            {
                activeChoiceButtons[i].SetInteractable(interactable);
            }

            if (choiceCanvasGroup != null)
            {
                choiceCanvasGroup.alpha = interactable ? 1f : 0.5f;
                choiceCanvasGroup.interactable = interactable;
                choiceCanvasGroup.blocksRaycasts = interactable;
            }
        }

        private void ActivateInputField()
        {
            inputField.ActivateInputField();
            inputField.Select();
            EventSystem.current?.SetSelectedGameObject(inputField.gameObject);
        }

        private void ResetMessageScroll()
        {
            if (messageScrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            messageScrollRect.verticalNormalizedPosition = 1f;
        }

        private void ScrollMessageToBottom()
        {
            if (messageScrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            messageScrollRect.verticalNormalizedPosition = 0f;
        }

        private ConversationChoiceButton CreateChoiceButton()
        {
            if (choiceButtonPrefab != null)
            {
                return Instantiate(choiceButtonPrefab, choiceContainer);
            }

            var buttonObject = new GameObject("ChoiceButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(ConversationChoiceButton));
            buttonObject.transform.SetParent(choiceContainer, false);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 8f);
            labelRect.offsetMax = new Vector2(-16f, -8f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;

            var choiceButton = buttonObject.GetComponent<ConversationChoiceButton>();
            return choiceButton;
        }

        private TMP_Text FindTextForContainer(Component container, params string[] preferredNames)
        {
            if (container == null)
            {
                return null;
            }

            var rootTransform = container.transform;
            for (var i = 0; i < preferredNames.Length; i++)
            {
                var namedChild = FindNamedTransform(rootTransform, preferredNames[i]);
                if (namedChild != null)
                {
                    var text = namedChild.GetComponent<TMP_Text>();
                    if (text != null)
                    {
                        return text;
                    }
                }
            }

            return rootTransform.GetComponentInChildren<TMP_Text>(true);
        }

        private T FindChildComponent<T>(params string[] objectNames) where T : Component
        {
            if (objectNames == null)
            {
                return null;
            }

            for (var nameIndex = 0; nameIndex < objectNames.Length; nameIndex++)
            {
                var targetTransform = FindNamedTransform(transform, objectNames[nameIndex]);
                if (targetTransform == null)
                {
                    continue;
                }

                if (targetTransform.TryGetComponent<T>(out var directComponent))
                {
                    return directComponent;
                }

                var childComponent = targetTransform.GetComponentInChildren<T>(true);
                if (childComponent != null)
                {
                    return childComponent;
                }
            }

            return null;
        }

        private Transform FindNamedTransform(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, objectName, StringComparison.Ordinal))
                {
                    return transforms[i];
                }
            }

            return null;
        }
    }
}
