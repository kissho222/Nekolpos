using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.TimeSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

namespace Nekolpos.System
{
    /// <summary>
    /// 【ノベルゲーム風UIコントローラー】
    /// ログ履歴、タイプライター表示、スキップ入力、変数置換（名前・代名詞等）を管理します。
    /// 旧 DialogueUIController に代わる、よりリッチなチャットUIシステムです。
    /// </summary>
    public class ChatUIController : MonoBehaviour
    {
        public enum InputLanguageMode
        {
            Auto,
            JapaneseKana,
            English,
            Unrestricted
        }

        [Header("UI References (Assign in Editor)")]
        [Tooltip("ウィンドウ全体（フェード用・CanvasGroupが付いている前提）")]
        public CanvasGroup chatWindowGroup;    
        public TextMeshProUGUI speakerNameText; // 発言者名テキスト
        public TextMeshProUGUI messageText;     // メッセージ本文テキスト
        public InputField chatInputField;      // プレイヤーの入力欄（表示/非表示制御用）
        public GameObject choicePanel;         // 選択肢用パネル
        [SerializeField] private TMP_Text choiceQuestionText;
        
        [Tooltip("「はじめる」ボタン")]
        public Button choiceAcceptButton;
        [Tooltip("「ちがうよ！」ボタン")]
        public Button choiceDeclineButton;

        [Header("Log Window References")]
        public GameObject logWindowPanel;      // ログ画面全体
        public Transform logContentTransform;  // ログプレハブを並べる親要素 (ScrollRectのContent)
        public GameObject logLinePrefab;       // 1行分のログプレハブ
        public Button logBackButton;           // ログ画面を閉じるボタン
        [SerializeField] private LogWindowPanel logWindowController;

        [Header("Extra Control Buttons")]
        [Tooltip("ログ画面を開閉するボタン")]
        public Button logButton;
        [Tooltip("オート進行を切り替えるボタン（現在仮実装）")]
        public Button autoButton;
        [Tooltip("ウィンドウの手動非表示を切り替えるボタン")]
        public Button hideWindowButton;
        [Tooltip("前の画面へ戻るボタン")]
        public Button backButton;
        [Tooltip("自由入力の話題ヒントを表示するシャボン玉ボタン")]
        [SerializeField] private TalkTopicHintBubble talkTopicHintBubble;
        [Tooltip("会話ヒントの表示を許可するか。今後の表示タイミング制御用。")]
        [SerializeField] private bool talkTopicHintsAllowed = true;

        [Header("Typing Settings")]
        public float typingSpeed = 0.05f;      // 1文字あたりの表示時間
        public float fadeDuration = 0.5f;      // ウィンドウのフェードイン/アウトにかかる時間

        [Header("Input Settings")]
        [SerializeField] private InputLanguageMode inputLanguageMode = InputLanguageMode.Auto;
        [SerializeField] [Min(1)] private int inputFocusDelayFrames = 2;
        [SerializeField] [Min(1)] private int inputImeStabilizeFrames = 8;
        [SerializeField] [Min(0.1f)] private float inputWarningDuration = 2f;
        [SerializeField] private Color inputFieldActiveBackgroundColor = new Color(0.20f, 0.16f, 0.10f, 0.96f);
        [SerializeField] private Color inputFieldActiveTextColor = new Color(1.00f, 0.98f, 0.88f, 1f);
        [SerializeField] private Color inputFieldActiveOutlineColor = new Color(1.00f, 0.84f, 0.38f, 0.88f);
        [SerializeField] private Color inputFieldActiveCaretColor = new Color(1.00f, 0.92f, 0.46f, 1f);
        [SerializeField] private Color inputFieldActiveSelectionColor = new Color(1.00f, 0.74f, 0.18f, 0.62f);
        [SerializeField] [Min(0.1f)] private float inputFieldPulseSpeed = 2.4f;
        
        // --- 状態管理フラグ ---
        private bool isTyping = false;         // 現在タイプライター表示中か
        private bool skipTyping = false;       // スキップが入力されたか
        private bool isWindowVisible = true;   // ウィンドウが表示されているか
        private bool isUiSuppressed = false;
        private bool menuInputBlocked;
        private InputField menuBlockedInput;
        private bool inputWasInteractableBeforeMenu;
        private string currentFullMessage;     // 全文（スキップ時や置換後の文字列）
        
        private Coroutine fadeCoroutine;       // 現在実行中のフェードコルーチン
        private Coroutine typingCoroutine;
        private Coroutine inputModeCoroutine;  // Window/Input の排他的切り替え用
        private Coroutine inputFocusCoroutine;
        private Coroutine inputWarningCoroutine;
        private Coroutine delayedDialogueProcessCoroutine;
        private bool suppressClickAdvanceUntilRelease = false; // UIボタン押下直後の誤文字送り防止
        private ConversationDataManager conversationDataManager;
        private CatAnimationRuntime catAnimationRuntime;
        private CatPresentationModeController catPresentationMode;
        private OpenBetaTitleBootstrap titleInputRouter;
        private readonly List<Button> runtimeChoiceButtons = new List<Button>();
        private readonly List<string> currentChoiceDisplayLabels = new List<string>();
        private Canvas rootCanvas;
        private Image chatInputFieldBackground;
        private Graphic chatInputFieldTextGraphic;
        private Graphic chatInputFieldPlaceholderGraphic;
        private Outline chatInputFieldOutline;
        private CanvasGroup inputActiveBadgeGroup;
        private RectTransform inputActiveBadgeRect;
        private TextMeshProUGUI inputActiveBadgeText;
        private CanvasGroup inputWarningGroup;
        private RectTransform inputWarningRect;
        private TextMeshProUGUI inputWarningText;
        private RectTransform choicePanelRect;
        private float choiceButtonTopY;
        private float choiceButtonStep;
        private float choiceButtonHeight;
        private float choiceButtonPosX;
        private bool choiceLayoutInitialized;
        private int selectedChoiceIndex = -1;
        private float choiceSpaceSubmitLockedUntil;
        private Color inputFieldBaseBackgroundColor = Color.white;
        private Color inputFieldBaseTextColor = Color.black;
        private Color inputFieldBasePlaceholderColor = Color.white;
        private Color inputFieldBaseOutlineColor = Color.clear;
        private Color inputFieldBaseCaretColor = new Color(0.196f, 0.196f, 0.196f, 1f);
        private Color inputFieldBaseSelectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
        private Vector2 inputFieldBaseOutlineDistance = Vector2.zero;
        private bool inputFieldBaseOutlineEnabled;
        private bool inputFieldBaseOutlineUseGraphicAlpha;
        private bool inputFieldHadExistingOutline;
        private bool inputFieldBaseCustomCaretColor;
        private bool inputFieldVisualsInitialized;
        private bool isApplyingInputFilter;
        private int suppressEndEditSubmitUntilFrame = -1;
        private int lastSubmittedInputFrame = -1000;
        private string lastSubmittedInputText = string.Empty;
        private int lastImeCompositionFrame = -1000;
        private bool pendingTalkTopicHintInput;
        private bool logButtonAllowedDuringConversation;
        private int forceImeCompositionOnUntilFrame = -1;
        private bool externalImeCompositionActive;
        private InputLanguageMode lastResolvedInputLanguageMode = InputLanguageMode.Auto;
        private readonly DialogueInputHistory inputHistory = new DialogueInputHistory();
        private ImeCompositionVisualController imeCompositionVisualController;
        private WebGLImeInputBridge webGLImeInputBridge;

        private static readonly Regex JapaneseTextFilterRegex =
            new Regex(@"[^\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}\p{IsCJKCompatibilityIdeographs}々〆ヶー、。！？?「」『』（）。・0-9０-９\s]", RegexOptions.Compiled);

        private static readonly Regex EnglishFilterRegex =
            new Regex(@"[^A-Za-z0-9\s\.\,\!\?'"":;\-\(\)]", RegexOptions.Compiled);

        private const string KanaInputModeWarningMessage = "かな入力に切り替えてね";
        private const string InvalidJapaneseInputWarningMessage = "日本語・数字・一部記号のみ入力可能です";
        private const string EnglishInputWarningMessage = "英数字と一部記号のみ入力可能です";
        private const string SelectedChoiceCursor = "▶";
        private const string ChoiceCursorObjectName = "ChoiceCursor";
        private const float ChoiceCursorWidth = 28f;
        private const float ChoiceLabelLeftMargin = 34f;
        private const float ChoiceSpaceSubmitCooldownSeconds = 1f;
        private const float ChoicePanelHeightWithoutQuestion = 195f;
        private const float ChoicePanelHeightWithQuestion = 275f;
        private const int DuplicateSubmitSuppressionFrames = 8;
        private const string CatRenameWordColor = "#D9480F";
        private const string PlayerRenameWordColor = "#0066CC";
        private const string PendingRenameWordColor = "#8A5A00";

        [global::System.Diagnostics.Conditional("NEKOLPOS_VERBOSE_LOGS")]
        private static void VerboseLog(string message)
        {
            Debug.Log(message);
        }

        // --- コールバック ---
        // メッセージが全て表示し終わった等、次に進んで良いタイミングで呼ばれる
        public event Action OnTypingCompleted;
        public event Action OnWaitInputCompleted; // 表示後、さらに「次へ」ボタン・キーが押された時
        public event Action<string> OnPlayerInputSubmitted;
        public event Action OnBackRequested;

        public bool IsTyping => isTyping;
        public bool IsMenuInputBlocked => menuInputBlocked;

        public void SetMenuInputBlocked(bool blocked)
        {
            if (menuInputBlocked == blocked) return;
            menuInputBlocked = blocked;
            // Closing a modal must consume the same press instead of advancing the dialogue behind it.
            suppressClickAdvanceUntilRelease = true;
            SuppressNextEndEditSubmit();
            if (blocked && chatInputField != null)
            {
                menuBlockedInput = chatInputField;
                inputWasInteractableBeforeMenu = menuBlockedInput.interactable;
                menuBlockedInput.DeactivateInputField();
                menuBlockedInput.interactable = false;
            }
            else if (!blocked && menuBlockedInput != null)
            {
                menuBlockedInput.interactable = inputWasInteractableBeforeMenu;
                menuBlockedInput = null;
            }
        }
        public bool IsInDialogueMode { get; private set; }
        public bool TalkTopicHintsAllowed => talkTopicHintsAllowed && !isUiSuppressed;

        public string CurrentMessage => currentFullMessage ?? string.Empty;

        public bool LastSubmittedInputUsedTalkTopicHint { get; private set; }

        public bool RouteSubmittedInputToDialogueEngine { get; set; } = true;

        public void SetTitleInputRoutingHandlers(Action<string> inputSubmittedHandler, Action backRequestedHandler)
        {
            OnPlayerInputSubmitted = null;
            OnBackRequested = null;

            if (inputSubmittedHandler != null)
            {
                OnPlayerInputSubmitted += inputSubmittedHandler;
            }

            if (backRequestedHandler != null)
            {
                OnBackRequested += backRequestedHandler;
            }
        }

        public void RemoveTitleInputRoutingHandlers(Action<string> inputSubmittedHandler, Action backRequestedHandler)
        {
            if (inputSubmittedHandler != null)
            {
                OnPlayerInputSubmitted -= inputSubmittedHandler;
            }

            if (backRequestedHandler != null)
            {
                OnBackRequested -= backRequestedHandler;
            }
        }

        public void SetTitleInputRouter(OpenBetaTitleBootstrap router)
        {
            if (ReferenceEquals(titleInputRouter, router))
            {
                return;
            }

            titleInputRouter = router;
            VerboseLog($"[ChatUI][Submit] SetTitleInputRouter router={FormatTitleRouter(router)} frame={Time.frameCount}");
        }

        public void ClearTitleInputRouterIfMatches(OpenBetaTitleBootstrap router)
        {
            VerboseLog($"[ChatUI][Submit] ClearTitleInputRouterIfMatches current={FormatTitleRouter(titleInputRouter)} target={FormatTitleRouter(router)} referenceEquals={ReferenceEquals(titleInputRouter, router)} frame={Time.frameCount}");
            if (!ReferenceEquals(titleInputRouter, router))
            {
                return;
            }

            SetTitleInputRouter(null);
        }

        private static string FormatTitleRouter(OpenBetaTitleBootstrap router)
        {
            return router != null
                ? $"{router.name}#{router.GetInstanceID()} session={router.TitleSessionId} current={router.IsCurrentTitleSession}"
                : "<null>";
        }

        public void SetInputLanguageMode(InputLanguageMode mode)
        {
            inputLanguageMode = mode;
            ApplyCurrentInputFilter();
        }

        public void SetUiSuppressed(bool suppressed)
        {
            isUiSuppressed = suppressed;
            if (suppressed)
            {
                IsInDialogueMode = false;
                HideAllRuntimeUiInstant();
            }
        }

        public void SetTalkTopicHintsAllowed(bool allowed)
        {
            talkTopicHintsAllowed = allowed;
            talkTopicHintBubble?.RefreshVisibility();
        }

        public void ClearDialogueDisplay()
        {
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            currentFullMessage = string.Empty;
            isTyping = false;
            skipTyping = false;
            SetSpeakerNameDisplay(string.Empty);

            if (messageText != null)
            {
                messageText.text = string.Empty;
                messageText.maxVisibleCharacters = int.MaxValue;
            }
        }

        public void ResetRuntimeStateForTitleRestart(bool suppressUi = true)
        {
            VerboseLog($"[ChatUI][Reset] ResetRuntimeStateForTitleRestart suppress={suppressUi} typing={isTyping} dialogueMode={IsInDialogueMode} route={RouteSubmittedInputToDialogueEngine} uiSuppressed={isUiSuppressed} delayed={delayedDialogueProcessCoroutine != null} inputActive={(chatInputField != null && chatInputField.gameObject.activeInHierarchy)} titleRouter={FormatTitleRouter(titleInputRouter)} hasSubmitHandlers={(OnPlayerInputSubmitted != null)} hasBackHandlers={(OnBackRequested != null)} hasTypingHandlers={(OnTypingCompleted != null)} hasWaitHandlers={(OnWaitInputCompleted != null)}");
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            if (delayedDialogueProcessCoroutine != null)
            {
                StopCoroutine(delayedDialogueProcessCoroutine);
                delayedDialogueProcessCoroutine = null;
            }

            if (inputWarningCoroutine != null)
            {
                StopCoroutine(inputWarningCoroutine);
                inputWarningCoroutine = null;
            }

            if (externalLogOpenCoroutine != null)
            {
                StopCoroutine(externalLogOpenCoroutine);
                externalLogOpenCoroutine = null;
            }

            currentFullMessage = string.Empty;
            isTyping = false;
            skipTyping = false;
            IsInDialogueMode = false;
            RouteSubmittedInputToDialogueEngine = true;
            OnTypingCompleted = null;
            OnWaitInputCompleted = null;
            OnPlayerInputSubmitted = null;
            OnBackRequested = null;
            titleInputRouter = null;
            pendingTalkTopicHintInput = false;
            LastSubmittedInputUsedTalkTopicHint = false;
            talkTopicHintsAllowed = false;
            logButtonAllowedDuringConversation = false;
            catAnimationRuntime = null;
            catPresentationMode = null;
            inputHistory.ResetNavigation();
            currentChoiceDisplayLabels.Clear();
            ClearChoiceSelectionFocus();
            SetSpeakerNameDisplay(string.Empty);

            if (messageText != null)
            {
                messageText.text = string.Empty;
                messageText.maxVisibleCharacters = int.MaxValue;
            }

            if (chatInputField != null)
            {
                SetChatInputTextWithoutInvalidCaret(string.Empty, 0);
                if (chatInputField.isFocused)
                {
                    chatInputField.DeactivateInputField();
                }
            }

            isUiSuppressed = suppressUi;
            HideAllRuntimeUiInstant();
            RouteSubmittedInputToDialogueEngine = true;
        }

        // 文字列置換用辞書。本来はセーブデータから引くべきですが、一旦固定値
        private Dictionary<string, string> variables = new Dictionary<string, string>()
        {
            { "{{CAT_NAME}}", "猫又" },
            { "{{CAT_PRONOUN}}", "私" },
            { "{{CAT_GENDER}}", "メス" },
            { "{{PARENT}}", "親" },
            { "{{PLAYER_CALLING}}", "ご主人" },
            { "{{PLAYER_NAME}}", "八雲" }
        };

        private static readonly Regex PlaceholderRegex = new Regex(@"\{\{\s*([A-Za-z0-9_?]+)\s*\}\}", RegexOptions.Compiled);

        public void SetRuntimeRenameValues(string playerName, string catName, string playerCalling, string catPronoun)
        {
            variables["{{PLAYER_NAME}}"] = SanitizeDisplayVariable(playerName);
            variables["{{CAT_NAME}}"] = SanitizeDisplayVariable(catName);
            variables["{{PLAYER_CALLING}}"] = SanitizeDisplayVariable(playerCalling);
            variables["{{CAT_PRONOUN}}"] = string.IsNullOrWhiteSpace(catPronoun) ? "私" : SanitizeDisplayVariable(catPronoun);
        }

        private static string SanitizeDisplayVariable(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private void Awake()
        {
            ResolveChatInputFieldReference();
            ResolveBackButtonReference();
            EnsureLogWindowPanel();
            EnsureImeCompositionVisualController();
            EnsureWebGLImeInputBridge();
            EnsureInputFieldActiveVisuals();
            EnsureInputWarningPopup();
            ResolveConversationDataManager();
            EnsureTalkTopicHintBubble();
            UpdateLogButtonVisibility();
        }

        private void Start()
        {
            // 初期状態は選択肢パネル・ログ画面を非表示
            if (choicePanel != null) choicePanel.SetActive(false);
            if (logWindowController != null) logWindowController.Close();
            else if (logWindowPanel != null) logWindowPanel.SetActive(false);

            // インプットフィールドのEnterイベント
            if (chatInputField != null)
            {
                chatInputField.onEndEdit.AddListener(OnEndEditSubmit);
                chatInputField.onValueChanged.AddListener(HandleInputValueChanged);
                chatInputField.lineType = InputField.LineType.SingleLine;
                chatInputField.shouldHideMobileInput = false;
            }

            UpdateInputFieldActiveVisualState(true);

            // 追加ボタン（ログ・オート・非表示）のイベント登録
            if (logButton != null)
            {
                logButton.onClick.RemoveListener(OpenLogWindow);
                logButton.onClick.AddListener(OpenLogWindow);
            }
            UpdateLogButtonVisibility();

            if (logBackButton != null)
            {
                logBackButton.onClick.RemoveListener(CloseLogWindow);
                logBackButton.onClick.AddListener(CloseLogWindow);
            }
            if (hideWindowButton != null) hideWindowButton.onClick.AddListener(ToggleWindowVisibility);
            if (autoButton != null) autoButton.onClick.AddListener(ToggleAutoMode);
            if (backButton != null)
            {
                backButton.onClick.AddListener(HandleBackButtonClicked);
                backButton.gameObject.SetActive(false);
            }

            SubscribeLocaleChanged();
            lastResolvedInputLanguageMode = ResolveInputLanguageMode();
        }

        private void ResolveChatInputFieldReference()
        {
            if (IsUsableChatInputField(chatInputField))
            {
                return;
            }

            InputField[] candidates = Resources.FindObjectsOfTypeAll<InputField>();
            InputField fallback = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                InputField candidate = candidates[i];
                if (!IsUsableChatInputField(candidate) || candidate.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                if (string.Equals(candidate.gameObject.name, "ChatInputField", StringComparison.Ordinal))
                {
                    fallback = candidate;
                    break;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }
            }

            if (fallback != null)
            {
                if (chatInputField != null && chatInputField != fallback)
                {
                    Debug.LogWarning("[ChatUI] Invalid chatInputField reference detected. Rebinding to a valid scene InputField.");
                }

                chatInputField = fallback;
            }
            else if (chatInputField != null)
            {
                Debug.LogError("[ChatUI] chatInputField reference is invalid and no usable InputField was found in this scene.");
            }
        }

        private void ResolveBackButtonReference()
        {
            if (backButton != null)
            {
                return;
            }

            // Do not bind generic "BackButton" objects automatically. This scene contains
            // several panel back buttons, including the log close button, and mixing those
            // with the input-mode back button changes unrelated title/setup panels.
        }

        private static bool IsUsableChatInputField(InputField inputField)
        {
            if (inputField == null || !inputField.gameObject.scene.IsValid())
            {
                return false;
            }

            if (!IsOwnedGraphic(inputField, inputField.textComponent))
            {
                return false;
            }

            if (inputField.placeholder != null && !IsOwnedGraphic(inputField, inputField.placeholder))
            {
                return false;
            }

            return true;
        }

        private static bool IsOwnedGraphic(InputField inputField, Graphic graphic)
        {
            return graphic != null &&
                   graphic.transform != null &&
                   graphic.transform.IsChildOf(inputField.transform);
        }

        private void OnDestroy()
        {
            if (chatInputField != null)
            {
                chatInputField.onEndEdit.RemoveListener(OnEndEditSubmit);
                chatInputField.onValueChanged.RemoveListener(HandleInputValueChanged);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(HandleBackButtonClicked);
            }

            if (logButton != null)
            {
                logButton.onClick.RemoveListener(OpenLogWindow);
            }

            if (logBackButton != null)
            {
                logBackButton.onClick.RemoveListener(CloseLogWindow);
            }

            if (inputWarningCoroutine != null)
            {
                StopCoroutine(inputWarningCoroutine);
                inputWarningCoroutine = null;
            }

            UnsubscribeLocaleChanged();
            webGLImeInputBridge?.Hide();
        }

        private void EnsureImeCompositionVisualController()
        {
            if (chatInputField == null)
            {
                return;
            }

            imeCompositionVisualController = chatInputField.GetComponent<ImeCompositionVisualController>();
            if (imeCompositionVisualController == null)
            {
                imeCompositionVisualController = chatInputField.gameObject.AddComponent<ImeCompositionVisualController>();
            }

            imeCompositionVisualController.Bind(chatInputField);
        }

        private void EnsureWebGLImeInputBridge()
        {
            if (chatInputField == null)
            {
                return;
            }

            webGLImeInputBridge = GetComponent<WebGLImeInputBridge>();
            if (webGLImeInputBridge == null)
            {
                webGLImeInputBridge = gameObject.AddComponent<WebGLImeInputBridge>();
            }

            webGLImeInputBridge.Bind(this);
            webGLImeInputBridge.SetShowWheneverActive(false);
        }

        private void EnsureLogWindowPanel()
        {
            if (logWindowPanel == null)
            {
                return;
            }

            if (logWindowController == null)
            {
                logWindowController = logWindowPanel.GetComponent<LogWindowPanel>();
                if (logWindowController == null)
                {
                    logWindowController = logWindowPanel.AddComponent<LogWindowPanel>();
                }
            }

            logWindowController.Configure(logContentTransform, logLinePrefab, this);
        }

        private void EnsureTalkTopicHintBubble()
        {
            if (chatInputField == null)
            {
                return;
            }

            if (talkTopicHintBubble == null)
            {
                talkTopicHintBubble = GetComponent<TalkTopicHintBubble>();
            }

            if (talkTopicHintBubble == null)
            {
                talkTopicHintBubble = GetComponentInChildren<TalkTopicHintBubble>(true);
            }

            if (talkTopicHintBubble == null)
            {
                talkTopicHintBubble = gameObject.AddComponent<TalkTopicHintBubble>();
            }

            talkTopicHintBubble.Configure(this);
        }

        /// <summary>
        /// オートモードの切り替え（仮実装）
        /// </summary>
        public void ToggleAutoMode()
        {
            VerboseLog("[ChatUI] オート進行ボタンが押されました（機能は別途実装予定）");
        }

        private void Update()
        {
            if (menuInputBlocked) return;
            MaintainInputImeCompositionMode();
            TrackImeComposition();
            imeCompositionVisualController?.Refresh();
            UpdateInputFieldActiveVisualState();
            webGLImeInputBridge?.Refresh();
            UpdateLogButtonVisibility();
            talkTopicHintBubble?.RefreshVisibility();
            HandleInputHistoryNavigation();

            // ログ表示中は、背後の会話UIに画面クリックを処理させない。
            // タイトル画面では会話UIが抑制状態のため、この判定が遅いと
            // 任意のクリックがHideAllRuntimeUiInstantを経由してログまで閉じてしまう。
            if (logWindowPanel != null && logWindowPanel.activeSelf) return;

            // UI非表示中は、画面クリックで再表示する
            if (!isWindowVisible || (chatWindowGroup != null && chatWindowGroup.alpha <= 0))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    // 入力待ちモードへの切り替え途中は、クリック連打でウィンドウを復帰させない
                    if (inputModeCoroutine != null)
                    {
                        return;
                    }

                    // 入力待ち状態（InputField表示中）では、クリックでChatWindowを復帰させない
                    if (chatInputField != null && chatInputField.gameObject.activeInHierarchy)
                    {
                        return;
                    }

                    EnterDialogueMode();
                    suppressClickAdvanceUntilRelease = true; // 再表示クリックで即文字送りしない
                }
                return;
            }

            // ウィンドウの表示・非表示切り替え（例としてTabキー。お好みで変更可能）
            // （※今回は自動制御がメインですが、手動でのトグル機能も残しています）
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleWindowVisibility();
            }

            if (choicePanel != null && choicePanel.activeSelf)
            {
                HandleChoiceKeyboardInput();
                return;
            }

            // マウスクリック、スペースキー、Enterキーでメッセージ送りを進行する
            // プレイヤー自身がテキスト入力中（InputFieldにフォーカスがある）の場合は無視する
            if (chatInputField != null && chatInputField.isFocused) return;

            // UIボタン押下直後の「同クリックによる文字送り」を抑止
            if (suppressClickAdvanceUntilRelease)
            {
                if (!Input.GetMouseButton(0))
                {
                    suppressClickAdvanceUntilRelease = false;
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOverInteractiveUi())
                {
                    suppressClickAdvanceUntilRelease = true;
                    return;
                }

                HandleNextInput();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                HandleNextInput();
            }
        }

        private void HandleInputHistoryNavigation()
        {
            if (chatInputField == null ||
                !chatInputField.gameObject.activeInHierarchy ||
                !chatInputField.isFocused ||
                !string.IsNullOrEmpty(Input.compositionString))
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                TryNavigateInputHistory(-1, true);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                TryNavigateInputHistory(1, true);
            }
        }

        public bool TryNavigateExternalInputHistory(int direction, string currentInput)
        {
            if (chatInputField == null ||
                !chatInputField.gameObject.activeInHierarchy ||
                externalImeCompositionActive)
            {
                return false;
            }

            SetExternalImeInputText(currentInput);
            return TryNavigateInputHistory(direction, false);
        }

        private bool TryNavigateInputHistory(int direction, bool requireUnityFocus)
        {
            if (chatInputField == null ||
                !chatInputField.gameObject.activeInHierarchy ||
                (requireUnityFocus && !chatInputField.isFocused) ||
                !string.IsNullOrEmpty(Input.compositionString))
            {
                return false;
            }

            string recalledText;
            bool changed = direction < 0
                ? inputHistory.TryMovePrevious(chatInputField.text, out recalledText)
                : inputHistory.TryMoveNext(out recalledText);
            if (!changed)
            {
                return false;
            }

            int end = recalledText != null ? recalledText.Length : 0;
            SetChatInputTextWithoutInvalidCaret(recalledText, end);
            return true;
        }

        private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();

        private bool IsPointerOverInteractiveUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            pointerRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, pointerRaycastResults);
            for (int i = 0; i < pointerRaycastResults.Count; i++)
            {
                GameObject target = pointerRaycastResults[i].gameObject;
                if (target == null || !target.activeInHierarchy)
                {
                    continue;
                }

                Selectable selectable = target.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.IsInteractable() && selectable.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }

        private void ToggleWindowVisibility()
        {
            isWindowVisible = !isWindowVisible;
            if (isWindowVisible)
            {
                FadeInWindow();
            }
            else
            {
                FadeOutWindow();
            }
        }

        /// <summary>
        /// ウィンドウをフェードインさせます
        /// </summary>
        public void FadeInWindow()
        {
            if (chatWindowGroup == null) return;
            isWindowVisible = true;
            
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeCanvasGroup(chatWindowGroup, chatWindowGroup.alpha, 1f, fadeDuration));
            UpdateLogButtonVisibility();
        }

        /// <summary>
        /// ウィンドウをフェードアウトさせます
        /// </summary>
        public void FadeOutWindow()
        {
            if (chatWindowGroup == null) return;
            isWindowVisible = false;

            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeCanvasGroup(chatWindowGroup, chatWindowGroup.alpha, 0f, fadeDuration));
            UpdateLogButtonVisibility();
        }

        /// <summary>
        /// 会話表示モードへ切り替える。メッセージウィンドウのみを表示し、入力欄は必ず閉じる。
        /// </summary>
        public void EnterDialogueMode()
        {
            if (isUiSuppressed)
            {
                IsInDialogueMode = false;
                HideAllRuntimeUiInstant();
                return;
            }

            IsInDialogueMode = true;
            CancelInputModeTransition();
            SetInputFieldVisible(false, false);
            FadeInWindow();
            UpdateLogButtonVisibility();
        }

        public void EnterDialogueModeInstant()
        {
            isUiSuppressed = false;
            IsInDialogueMode = true;
            CancelInputModeTransition();

            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            SetInputFieldVisible(false, false);
            isWindowVisible = true;

            if (chatWindowGroup != null)
            {
                chatWindowGroup.alpha = 1f;
                chatWindowGroup.interactable = true;
                chatWindowGroup.blocksRaycasts = true;
            }
            UpdateLogButtonVisibility();
        }

        /// <summary>
        /// 入力待ちモードへ切り替える。メッセージウィンドウを閉じたあと、入力欄のみを表示する。
        /// </summary>
        public void EnterPlayerInputMode()
        {
            if (isUiSuppressed)
            {
                IsInDialogueMode = false;
                HideAllRuntimeUiInstant();
                return;
            }

            IsInDialogueMode = false;
            CancelInputModeTransition();
            SetInputFieldVisible(false, false);

            if (chatWindowGroup == null)
            {
                SetInputFieldVisible(true, true);
                return;
            }

            FadeOutWindow();
            SetInputFieldVisible(true, true);
            UpdateLogButtonVisibility();
        }

        public void EnterTeachingInputMode()
        {
            if (isUiSuppressed)
            {
                IsInDialogueMode = false;
                HideAllRuntimeUiInstant();
                return;
            }

            IsInDialogueMode = true;
            CancelInputModeTransition();
            SetTalkTopicHintsAllowed(false);
            FadeInWindow();
            SetInputFieldVisible(true, true);
            UpdateLogButtonVisibility();
        }

        public void RestoreNormalConversationInputMode()
        {
            SetTalkTopicHintsAllowed(true);
            EnterPlayerInputMode();
        }

        private void CancelInputModeTransition()
        {
            if (inputModeCoroutine != null)
            {
                StopCoroutine(inputModeCoroutine);
                inputModeCoroutine = null;
            }

            if (inputFocusCoroutine != null)
            {
                StopCoroutine(inputFocusCoroutine);
                inputFocusCoroutine = null;
            }
        }

        private void SetInputFieldVisible(bool enable, bool focusInput)
        {
            if (chatInputField == null) return;

            if (!enable)
            {
                if (chatInputField.isFocused)
                {
                    chatInputField.DeactivateInputField();
                }

                if (EventSystem.current != null &&
                    EventSystem.current.currentSelectedGameObject == chatInputField.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }

                HideInputWarningInstant();
            }

            if (enable)
            {
                SetChatInputTextWithoutInvalidCaret(string.Empty, 0);
            }

            chatInputField.gameObject.SetActive(enable);
            if (backButton != null)
            {
                backButton.gameObject.SetActive(enable);
            }

            if (enable)
            {
                Input.imeCompositionMode = IMECompositionMode.On;
                forceImeCompositionOnUntilFrame = Time.frameCount + Mathf.Max(1, inputImeStabilizeFrames);
                chatInputField.shouldHideMobileInput = false;

                if (focusInput)
                {
                    inputFocusCoroutine = StartCoroutine(FocusInputFieldDeferred());
                }
            }

            UpdateInputFieldActiveVisualState(true);
            UpdateLogButtonVisibility();
        }

        private void HideAllRuntimeUiInstant()
        {
            CancelInputModeTransition();

            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            isWindowVisible = false;

            if (chatWindowGroup != null)
            {
                chatWindowGroup.alpha = 0f;
                chatWindowGroup.interactable = false;
                chatWindowGroup.blocksRaycasts = false;
            }

            if (chatInputField != null)
            {
                if (chatInputField.isFocused)
                {
                    chatInputField.DeactivateInputField();
                }

                chatInputField.gameObject.SetActive(false);
            }

            if (backButton != null)
            {
                backButton.gameObject.SetActive(false);
            }

            if (choicePanel != null)
            {
                choicePanel.SetActive(false);
            }

            if (logWindowController != null)
            {
                logWindowController.Close();
            }
            else if (logWindowPanel != null)
            {
                logWindowPanel.SetActive(false);
            }

            HideInputWarningInstant();
            UpdateLogButtonVisibility();
        }

        public void SetLogButtonAllowedDuringConversation(bool allowed)
        {
            logButtonAllowedDuringConversation = allowed;
            UpdateLogButtonVisibility();
        }

        private void UpdateLogButtonVisibility()
        {
            if (logButton == null)
            {
                return;
            }

            bool chatWindowVisible = chatWindowGroup != null &&
                chatWindowGroup.gameObject.activeInHierarchy &&
                chatWindowGroup.alpha > 0.01f;
            bool inputFieldVisible = chatInputField != null &&
                chatInputField.gameObject.activeInHierarchy;
            bool shouldShow = logButtonAllowedDuringConversation &&
                !isUiSuppressed &&
                (chatWindowVisible || inputFieldVisible);

            if (logButton.gameObject.activeSelf != shouldShow)
            {
                logButton.gameObject.SetActive(shouldShow);
            }
        }

        private IEnumerator FocusInputFieldDeferred()
        {
            Input.imeCompositionMode = IMECompositionMode.On;
            forceImeCompositionOnUntilFrame = Time.frameCount + Mathf.Max(1, inputImeStabilizeFrames);

            while (Input.GetKey(KeyCode.Return) ||
                   Input.GetKey(KeyCode.KeypadEnter) ||
                   Input.GetKey(KeyCode.Space))
            {
                yield return null;
            }

            int delayFrames = Mathf.Max(1, inputFocusDelayFrames);
            for (int i = 0; i < delayFrames; i++)
            {
                yield return null;
            }

            if (chatInputField == null || !chatInputField.gameObject.activeInHierarchy)
            {
                inputFocusCoroutine = null;
                yield break;
            }

            ResetInputFieldCaretSelection();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                yield return null;
                EventSystem.current.SetSelectedGameObject(chatInputField.gameObject);
            }

            chatInputField.Select();
            chatInputField.ActivateInputField();
            Input.imeCompositionMode = IMECompositionMode.On;
            forceImeCompositionOnUntilFrame = Time.frameCount + Mathf.Max(1, inputImeStabilizeFrames);
            chatInputField.ForceLabelUpdate();
            chatInputField.MoveTextEnd(false);
            UpdateInputFieldActiveVisualState(true);
            inputFocusCoroutine = null;
        }

        private void MaintainInputImeCompositionMode()
        {
            if (chatInputField == null || !chatInputField.gameObject.activeInHierarchy)
            {
                return;
            }

            InputLanguageMode resolvedMode = ResolveInputLanguageMode();
            bool allowImeComposition =
                resolvedMode == InputLanguageMode.JapaneseKana ||
                resolvedMode == InputLanguageMode.Unrestricted ||
                resolvedMode == InputLanguageMode.Auto;

            if (!allowImeComposition)
            {
                return;
            }

            if (chatInputField.isFocused ||
                inputFocusCoroutine != null ||
                Time.frameCount <= forceImeCompositionOnUntilFrame)
            {
                Input.imeCompositionMode = IMECompositionMode.On;
            }
        }

        private void EnsureInputFieldActiveVisuals()
        {
            if (inputFieldVisualsInitialized || chatInputField == null)
            {
                return;
            }

            chatInputFieldBackground = chatInputField.GetComponent<Image>();
            chatInputFieldTextGraphic = chatInputField.textComponent;
            chatInputFieldPlaceholderGraphic = chatInputField.placeholder;
            chatInputFieldOutline = chatInputField.GetComponent<Outline>();
            inputFieldHadExistingOutline = chatInputFieldOutline != null;

            if (chatInputFieldBackground != null)
            {
                inputFieldBaseBackgroundColor = chatInputFieldBackground.color;
            }

            if (chatInputFieldPlaceholderGraphic != null)
            {
                inputFieldBasePlaceholderColor = chatInputFieldPlaceholderGraphic.color;
            }

            if (chatInputFieldTextGraphic != null)
            {
                inputFieldBaseTextColor = chatInputFieldTextGraphic.color;
            }

            inputFieldBaseCaretColor = chatInputField.caretColor;
            inputFieldBaseCustomCaretColor = chatInputField.customCaretColor;
            inputFieldBaseSelectionColor = chatInputField.selectionColor;

            if (chatInputFieldOutline == null)
            {
                chatInputFieldOutline = chatInputField.gameObject.AddComponent<Outline>();
            }

            if (chatInputFieldOutline != null)
            {
                inputFieldBaseOutlineEnabled = chatInputFieldOutline.enabled;
                inputFieldBaseOutlineColor = chatInputFieldOutline.effectColor;
                inputFieldBaseOutlineDistance = chatInputFieldOutline.effectDistance;
                inputFieldBaseOutlineUseGraphicAlpha = chatInputFieldOutline.useGraphicAlpha;
            }

            ResolveInputActiveBadge();
            inputFieldVisualsInitialized = true;
        }

        private void ResolveInputActiveBadge()
        {
            if (chatInputField == null)
            {
                return;
            }

            Transform badgeTransform = chatInputField.transform.Find("ChatInputActiveBadge");
            inputActiveBadgeRect = badgeTransform as RectTransform;
            inputActiveBadgeGroup = badgeTransform != null ? badgeTransform.GetComponent<CanvasGroup>() : null;
            inputActiveBadgeText = badgeTransform != null ? badgeTransform.GetComponent<TextMeshProUGUI>() : null;
        }

        private void UpdateInputFieldActiveVisualState(bool force = false)
        {
            EnsureInputFieldActiveVisuals();
            if (!inputFieldVisualsInitialized || chatInputField == null)
            {
                return;
            }

            bool isActive = chatInputField.gameObject.activeInHierarchy;
            bool isFocused = isActive && chatInputField.isFocused;
            float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * inputFieldPulseSpeed * Mathf.PI * 2f));
            float emphasis = isFocused ? Mathf.Lerp(0.70f, 1.00f, pulse) : 0.42f;

            if (!force && !isActive && inputActiveBadgeGroup != null && !inputActiveBadgeGroup.gameObject.activeSelf)
            {
                return;
            }

            if (chatInputFieldBackground != null)
            {
                chatInputFieldBackground.color = isActive
                    ? Color.Lerp(inputFieldBaseBackgroundColor, inputFieldActiveBackgroundColor, isFocused ? 0.92f : 0.72f)
                    : inputFieldBaseBackgroundColor;
            }

            if (chatInputFieldPlaceholderGraphic != null)
            {
                Color highlightColor = Color.Lerp(inputFieldBasePlaceholderColor, new Color(1.00f, 0.95f, 0.78f, inputFieldBasePlaceholderColor.a), isFocused ? 0.75f : 0.45f);
                chatInputFieldPlaceholderGraphic.color = isActive ? highlightColor : inputFieldBasePlaceholderColor;
            }

            if (chatInputFieldTextGraphic != null)
            {
                chatInputFieldTextGraphic.color = isActive ? inputFieldActiveTextColor : inputFieldBaseTextColor;
            }

            chatInputField.customCaretColor = isActive || isFocused
                ? true
                : inputFieldBaseCustomCaretColor;
            chatInputField.caretColor = isActive || isFocused
                ? inputFieldActiveCaretColor
                : inputFieldBaseCaretColor;
            chatInputField.selectionColor = isActive || isFocused
                ? inputFieldActiveSelectionColor
                : inputFieldBaseSelectionColor;

            if (chatInputFieldOutline != null)
            {
                if (isActive)
                {
                    chatInputFieldOutline.enabled = true;
                    chatInputFieldOutline.useGraphicAlpha = false;
                    chatInputFieldOutline.effectColor = Color.Lerp(
                        new Color(inputFieldActiveOutlineColor.r, inputFieldActiveOutlineColor.g, inputFieldActiveOutlineColor.b, 0.18f),
                        inputFieldActiveOutlineColor,
                        emphasis);

                    float distance = isFocused ? Mathf.Lerp(3.5f, 5.5f, pulse) : 3.5f;
                    chatInputFieldOutline.effectDistance = new Vector2(distance, -distance);
                }
                else
                {
                    chatInputFieldOutline.enabled = inputFieldHadExistingOutline ? inputFieldBaseOutlineEnabled : false;
                    chatInputFieldOutline.useGraphicAlpha = inputFieldBaseOutlineUseGraphicAlpha;
                    chatInputFieldOutline.effectColor = inputFieldBaseOutlineColor;
                    chatInputFieldOutline.effectDistance = inputFieldBaseOutlineDistance;
                }
            }

            if (inputActiveBadgeGroup != null)
            {
                inputActiveBadgeGroup.gameObject.SetActive(isActive);
                if (isActive)
                {
                    inputActiveBadgeGroup.alpha = isFocused ? Mathf.Lerp(0.84f, 1.00f, pulse) : 0.86f;

                    if (inputActiveBadgeText != null)
                    {
                        inputActiveBadgeText.text = isFocused ? "入力受付中" : "入力待機中";
                    }
                }
            }

            webGLImeInputBridge?.SuppressUnityTextForDomInput();
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float startAlpha, float targetAlpha, float duration)
        {
            float time = 0;
            
            // フェードイン開始時にブロックや入力を許可
            if (targetAlpha > 0)
            {
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }

            while (time < duration)
            {
                time += Time.deltaTime;
                cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / duration);
                yield return null;
            }

            cg.alpha = targetAlpha;

            // フェードアウト完了時に当たり判定を消す
            if (targetAlpha <= 0)
            {
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            
            fadeCoroutine = null;
        }

        private void HandleNextInput()
        {
            if (menuInputBlocked || delayedDialogueProcessCoroutine != null)
            {
                return;
            }

            // タイプライター表示中ならスキップする
            if (isTyping)
            {
                skipTyping = true;
            }
            else
            {
                // すでに表示完了していて、選択肢待機中でなければ次へ
                if (choicePanel != null && !choicePanel.activeSelf)
                {
                    OnWaitInputCompleted?.Invoke();
                }
            }
        }

        // ============================================
        // メッセージ表示関連
        // ============================================

        /// <summary>
        /// 発言者名とテキストをセットし、タイプライター表示を開始する
        /// </summary>
        public void ShowMessage(string speakerName, string rawMessage, DialogueLogEntry logEntry = null)
        {
            // 発言が呼ばれたらウィンドウを強制的にフェードイン
            if (!isWindowVisible || inputModeCoroutine != null || (chatInputField != null && chatInputField.gameObject.activeInHierarchy))
            {
                EnterDialogueMode();
            }

            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            string resolvedSpeakerName = ReplaceVariables(speakerName);
            string resolvedMessageForLog = ReplaceVariables(rawMessage);
            string resolvedMessage = ReplaceVariables(rawMessage, ShouldHighlightRenameWords(rawMessage));
            AddDialogueLogEntry(logEntry, speakerName, resolvedSpeakerName, resolvedMessageForLog);

            // パネル名に置換処理（例：{{CAT_NAME}} -> 猫又）
            SetSpeakerNameDisplay(resolvedSpeakerName);

            // 本文も同様に置換処理
            currentFullMessage = resolvedMessage;

            // コルーチン開始前にテキストとフラグをリセット
            if (messageText != null)
            {
                messageText.text = "";
                messageText.maxVisibleCharacters = int.MaxValue;
            }
            isTyping = true;
            skipTyping = false;

            typingCoroutine = StartCoroutine(TypewriterRoutine());
        }

        public void ShowMessageInstant(string speakerName, string rawMessage, DialogueLogEntry logEntry = null)
        {
            EnterDialogueModeInstant();

            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            string resolvedSpeakerName = ReplaceVariables(speakerName);
            string resolvedMessageForLog = ReplaceVariables(rawMessage);
            string resolvedMessage = ReplaceVariables(rawMessage, ShouldHighlightRenameWords(rawMessage));
            AddDialogueLogEntry(logEntry, speakerName, resolvedSpeakerName, resolvedMessageForLog);

            SetSpeakerNameDisplay(resolvedSpeakerName);

            currentFullMessage = resolvedMessage;
            if (messageText != null)
            {
                messageText.text = currentFullMessage;
                messageText.maxVisibleCharacters = int.MaxValue;
            }

            isTyping = false;
            skipTyping = false;
            OnTypingCompleted?.Invoke();
        }

        private IEnumerator TypewriterRoutine()
        {
            if (messageText == null)
            {
                isTyping = false;
                skipTyping = false;
                typingCoroutine = null;
                yield break;
            }

            // 直前のエンターキーやクリックの入力判定が残って即スキップされるのを防ぐため、
            // ほんの一瞬だけスキップ判定を無効化（待機）します。
            yield return new WaitForEndOfFrame();
            skipTyping = false;

            messageText.text = currentFullMessage;
            messageText.maxVisibleCharacters = 0;
            messageText.ForceMeshUpdate();
            int visibleCharacterCount = messageText.textInfo.characterCount;

            for (int i = 0; i < visibleCharacterCount; i++)
            {
                // スキップが入力されたらループを抜けて全文を即表示
                if (skipTyping)
                {
                    break;
                }

                messageText.maxVisibleCharacters = i + 1;
                yield return new WaitForSeconds(typingSpeed);
            }

            // スキップされた場合や正常終了した場合の最終表示
            messageText.maxVisibleCharacters = int.MaxValue;
            isTyping = false;
            skipTyping = false;
            typingCoroutine = null;

            // 表示完了イベントを発火
            OnTypingCompleted?.Invoke();
        }

        /// <summary>
        /// 文章中の {{}} で囲まれた変数を辞書と照らし合わせて置換する
        /// </summary>
        private string ReplaceVariables(string text, bool highlightRenameWords = false)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // CSV上で \{\{KEY\}\} のようにエスケープされた記述も受け入れる
            string processedText = text
                .Replace(@"\{\{", "{{")
                .Replace(@"\}\}", "}}")
                .Replace(@"\_", "_");

            // {{ CAT_GENDER }} のような空白入りトークンも含め、1回の走査で置換する
            processedText = PlaceholderRegex.Replace(processedText, match =>
            {
                string canonicalKey = "{{" + match.Groups[1].Value.ToUpperInvariant() + "}}";
                if (TryResolveVariableValue(canonicalKey, out string value))
                {
                    return highlightRenameWords && IsRenameWordPlaceholder(canonicalKey)
                        ? ColorizeRenameWord(canonicalKey, value)
                        : value;
                }
                return match.Value;
            });

            return processedText;
        }

        private static bool ShouldHighlightRenameWords(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            return rawText.IndexOf("{{PENDING_VALUE}}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawText.IndexOf("{{CAT_NAME}}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawText.IndexOf("{{PLAYER_CALLING}}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawText.IndexOf("{{CAT_PRONOUN}}", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRenameWordPlaceholder(string canonicalKey)
        {
            return string.Equals(canonicalKey, "{{PENDING_VALUE}}", StringComparison.Ordinal) ||
                   string.Equals(canonicalKey, "{{CAT_NAME}}", StringComparison.Ordinal) ||
                   string.Equals(canonicalKey, "{{PLAYER_CALLING}}", StringComparison.Ordinal) ||
                   string.Equals(canonicalKey, "{{CAT_PRONOUN}}", StringComparison.Ordinal);
        }

        private static string ColorizeRenameWord(string canonicalKey, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            string safeValue = value.Replace("<", "‹").Replace(">", "›");
            return $"<color={GetRenameWordColor(canonicalKey)}>{safeValue}</color>";
        }

        private static string GetRenameWordColor(string canonicalKey)
        {
            if (string.Equals(canonicalKey, "{{CAT_NAME}}", StringComparison.Ordinal) ||
                string.Equals(canonicalKey, "{{CAT_PRONOUN}}", StringComparison.Ordinal))
            {
                return CatRenameWordColor;
            }

            if (string.Equals(canonicalKey, "{{PLAYER_NAME}}", StringComparison.Ordinal) ||
                string.Equals(canonicalKey, "{{PLAYER_CALLING}}", StringComparison.Ordinal))
            {
                return PlayerRenameWordColor;
            }

            return PendingRenameWordColor;
        }

        private bool TryResolveVariableValue(string key, out string value)
        {
            if (RenameSystem.TryResolvePlaceholder(key, out value))
            {
                value = SanitizeDisplayVariable(value);
                return true;
            }

            if (variables.TryGetValue(key, out value))
            {
                value = SanitizeDisplayVariable(value);
                return true;
            }

            if (TryResolveWeatherPlaceholder(key, out value))
            {
                return true;
            }

            GameManager gameManager = GameManager.Instance;
            if (gameManager == null)
            {
                value = null;
                return false;
            }

            switch (key)
            {
                case "{{TIME_OF_DAY}}":
                    value = gameManager.CurrentTimeOfDayLabel;
                    return !string.IsNullOrEmpty(value);

                case "{{NEXT_TIME_OF_DAY}}":
                    value = gameManager.NextTimeOfDayLabel;
                    return !string.IsNullOrEmpty(value);

                default:
                    value = null;
                    return false;
            }
        }

        private static bool TryResolveWeatherPlaceholder(string key, out string value)
        {
            value = null;
            string stateKey = ResolveWeatherStateKey(key);
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            TimeManager timeManager = Object.FindFirstObjectByType<TimeManager>();
            if (timeManager != null)
            {
                switch (stateKey)
                {
                    case WeatherSystem.WeatherKey:
                        value = WeatherSystem.ToDisplayText(timeManager.Weather);
                        return true;
                    case WeatherSystem.CurrentWeatherKey:
                        value = timeManager.Weather.ToString();
                        return true;
                    case WeatherSystem.TomorrowWeatherKey:
                    case WeatherSystem.ForecastWeatherKey:
                    case WeatherSystem.ForecastWeatherMisspelledKey:
                    case WeatherSystem.ForecastWeatherQuestionKey:
                        value = WeatherSystem.ToDisplayText(timeManager.TomorrowWeather);
                        return true;
                    case WeatherSystem.CurrentWeatherForecastKey:
                        value = timeManager.TomorrowWeather.ToString();
                        return true;
                    case WeatherSystem.NextActualWeatherKey:
                        value = timeManager.NextActualWeather.ToString();
                        return true;
                    case WeatherSystem.ForecastCorrectKey:
                        value = timeManager.ForecastCorrect ? "true" : "false";
                        return true;
                }
            }

            ConversationGameStateManager stateManager = Object.FindFirstObjectByType<ConversationGameStateManager>();
            ConversationGameState state = stateManager != null ? stateManager.State : null;
            if (state == null)
            {
                return false;
            }

            if (state.TryGetString(stateKey, out value))
            {
                return true;
            }

            if (state.TryGetBool(stateKey, out bool boolValue))
            {
                value = boolValue ? "true" : "false";
                return true;
            }

            return false;
        }

        private static string ResolveWeatherStateKey(string key)
        {
            switch ((key ?? string.Empty).Trim())
            {
                case "{{WEATHER}}":
                    return WeatherSystem.WeatherKey;
                case "{{CURRENTWEATHER}}":
                    return WeatherSystem.CurrentWeatherKey;
                case "{{TOMORROWWEATHER}}":
                    return WeatherSystem.TomorrowWeatherKey;
                case "{{FORECAST_WEATHER}}":
                    return WeatherSystem.ForecastWeatherKey;
                case "{{FORCAST_WEATHER}}":
                    return WeatherSystem.ForecastWeatherMisspelledKey;
                case "{{FORCAST?WEATHER}}":
                    return WeatherSystem.ForecastWeatherQuestionKey;
                case "{{CURRENTWEATHERFORECAST}}":
                    return WeatherSystem.CurrentWeatherForecastKey;
                case "{{NEXTACTUALWEATHER}}":
                    return WeatherSystem.NextActualWeatherKey;
                case "{{WEATHERFORECASTCORRECT}}":
                    return WeatherSystem.ForecastCorrectKey;
                default:
                    return string.Empty;
            }
        }

        // ============================================
        // プレイヤー入力・選択肢関連
        // ============================================

        /// <summary>
        /// InputFieldをアクティブにして、プレイヤーからの入力を受け付ける（AskAgainなどで呼ぶ）
        /// </summary>
        public void EnablePlayerInput(bool enable)
        {
            if (enable)
            {
                EnterPlayerInputMode();
                return;
            }

            CancelInputModeTransition();
            SetInputFieldVisible(false, false);
        }

        private void HandleInputValueChanged(string value)
        {
            if (isApplyingInputFilter)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                pendingTalkTopicHintInput = false;
            }

            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                return;
            }

            if (externalImeCompositionActive)
            {
                return;
            }

            ShowInputWarningForValue(value);
        }

        private void ApplyCurrentInputFilter()
        {
            if (chatInputField == null || !chatInputField.gameObject.activeInHierarchy)
            {
                return;
            }

            // IME変換中は未確定文字列を触らない。
            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                return;
            }

            if (externalImeCompositionActive)
            {
                return;
            }

            ShowInputWarningForValue(chatInputField.text);
        }

        private void ShowInputWarningForValue(string value)
        {
            if (chatInputField == null)
            {
                return;
            }

            InputLanguageMode resolvedMode = ResolveInputLanguageMode();
            if (lastResolvedInputLanguageMode != InputLanguageMode.Auto &&
                lastResolvedInputLanguageMode != resolvedMode &&
                !string.IsNullOrEmpty(value))
            {
                ShowInputWarning(ResolveLanguageModeChangedWarning(resolvedMode));
            }

            lastResolvedInputLanguageMode = resolvedMode;

            string warningMessage = ResolveInputWarningMessage(value, resolvedMode);
            if (Application.platform == RuntimePlatform.WebGLPlayer &&
                string.Equals(warningMessage, KanaInputModeWarningMessage, StringComparison.Ordinal))
            {
                HideInputWarningInstant();
                return;
            }

            if (!string.IsNullOrEmpty(warningMessage))
            {
                ShowInputWarning(warningMessage);
                return;
            }

            HideInputWarningInstant();
        }

        private void ApplyInputFilter(string value)
        {
            if (chatInputField == null || isApplyingInputFilter)
            {
                return;
            }

            if (externalImeCompositionActive)
            {
                return;
            }

            InputLanguageMode resolvedMode = ResolveInputLanguageMode();
            ShowInputWarningForValue(value);

            string sanitized = SanitizeInputForEditing(value, resolvedMode);
            if (string.Equals(value, sanitized, StringComparison.Ordinal))
            {
                return;
            }

            int caretPosition = Mathf.Clamp(chatInputField.caretPosition - (value.Length - sanitized.Length), 0, sanitized.Length);
            isApplyingInputFilter = true;
            SetChatInputTextWithoutInvalidCaret(sanitized, caretPosition);
            isApplyingInputFilter = false;
        }

        private void OnEndEditSubmit(string text)
        {
            VerboseLog($"[ChatUI][Submit] OnEndEdit frame={Time.frameCount} suppressedUntil={suppressEndEditSubmitUntilFrame} focused={(chatInputField != null && chatInputField.isFocused)} active={(chatInputField != null && chatInputField.gameObject.activeInHierarchy)} route={RouteSubmittedInputToDialogueEngine} raw={FormatDebugInput(text)}");
            TrySubmitInputText(text, true);
        }

        public void SetExternalImeInputText(string text)
        {
            if (chatInputField == null)
            {
                return;
            }

            string value = text ?? string.Empty;
            isApplyingInputFilter = true;
            SetChatInputTextWithoutInvalidCaret(value, value.Length);
            isApplyingInputFilter = false;
            if (externalImeCompositionActive)
            {
                return;
            }

            ShowInputWarningForValue(value);
        }

        public bool SubmitExternalImeInput(string text)
        {
            if (menuInputBlocked) return false;
            string value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) &&
                chatInputField != null &&
                !string.IsNullOrWhiteSpace(chatInputField.text))
            {
                value = chatInputField.text;
            }

            SetExternalImeInputText(value);
            bool submitted = TrySubmitInputText(value, false);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[ChatUI][SubmitDebug] External IME submit result={submitted} length={value.Length} frame={Time.frameCount}.");
#endif
            return submitted;
        }

        public void SetExternalImeCompositionActive(bool active)
        {
            externalImeCompositionActive = active;
            if (active)
            {
                HideInputWarningInstant();
            }
        }

        private bool TrySubmitInputText(string text, bool requireSubmitKey)
        {
            if (menuInputBlocked) return false;
            if (IsImeCompositionActiveOrJustCommitted())
            {
                VerboseLog("[ChatUI][Submit] ignored: IME composition is active or was just committed");
                ReactivateInputAfterImeCommit();
                return false;
            }

            if (Time.frameCount <= suppressEndEditSubmitUntilFrame)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                Debug.Log($"[ChatUI][SubmitDebug] Suppressed Unity submit requireSubmitKey={requireSubmitKey} frame={Time.frameCount} suppressedUntil={suppressEndEditSubmitUntilFrame}.");
#else
                VerboseLog("[ChatUI][Submit] ignored: suppressEndEditSubmitUntilFrame");
#endif
                return false;
            }

            if (requireSubmitKey && !IsSubmitKeyPressed())
            {
                VerboseLog("[ChatUI][Submit] ignored: end edit without submit key");
                ResetInputFieldCaretSelection();
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                VerboseLog("[ChatUI][Submit] blank input submitted with submit key");
            }

            bool isBlankInput = string.IsNullOrWhiteSpace(text);
            string sanitized = isBlankInput ? string.Empty : SanitizeInput(text);
            if (!isBlankInput && string.IsNullOrWhiteSpace(sanitized))
            {
                VerboseLog($"[ChatUI][Submit] ignored: sanitized input is blank. raw={FormatDebugInput(text)}");
                pendingTalkTopicHintInput = false;
                LastSubmittedInputUsedTalkTopicHint = false;
                if (chatInputField != null)
                {
                    SetChatInputTextWithoutInvalidCaret(string.Empty, 0);
                }
                return false;
            }

            if (Time.frameCount - lastSubmittedInputFrame <= DuplicateSubmitSuppressionFrames &&
                string.Equals(lastSubmittedInputText, sanitized, StringComparison.Ordinal))
            {
                VerboseLog($"[ChatUI][Submit] ignored: duplicate submit input={FormatDebugInput(sanitized)}");
                return false;
            }

            lastSubmittedInputFrame = Time.frameCount;
            lastSubmittedInputText = sanitized;

            bool talkTopicHintUsed = pendingTalkTopicHintInput;
            pendingTalkTopicHintInput = false;
            LastSubmittedInputUsedTalkTopicHint = talkTopicHintUsed;
            
            inputHistory.AddHistory(sanitized);

            // 入力を確定したら入力欄からはフォーカスを外し隠す
            EnablePlayerInput(false);

            Backgammon.Conversation.PlayerInputContext inputContext = new Backgammon.Conversation.PlayerInputContext(sanitized);
            DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
            {
                Speaker = DialogueLogManager.SpeakerPlayer,
                SpeakerDisplayName = ResolveSpeakerDisplayName(DialogueLogManager.SpeakerPlayer),
                Text = inputContext.OriginalInput,
                Source = DialogueLogManager.SourceFreeInput,
                RawInput = inputContext.OriginalInput,
                NormalizedInput = inputContext.NormalizedInput,
                TalkTopicHintUsed = talkTopicHintUsed
            });
            VerboseLog($"[ChatUI][Submit] invoking OnPlayerInputSubmitted listeners={(OnPlayerInputSubmitted != null)} route={RouteSubmittedInputToDialogueEngine} input={FormatDebugInput(inputContext.OriginalInput)}");
            if (!RouteSubmittedInputToDialogueEngine)
            {
                bool routedToTitle = titleInputRouter != null &&
                                     titleInputRouter.TryRoutePlayerInputFromChatUIInstance(this, inputContext.OriginalInput);
                if (!routedToTitle)
                {
                    routedToTitle = InvokePlayerInputSubmittedSafely(inputContext.OriginalInput);
                }

                if (!routedToTitle)
                {
                    routedToTitle = OpenBetaTitleBootstrap.TryRoutePlayerInputFromChatUI(this, inputContext.OriginalInput);
                }

                VerboseLog($"[ChatUI][Submit] direct title route result={routedToTitle} input={FormatDebugInput(inputContext.OriginalInput)}");
                ProcessSubmittedInput(inputContext.OriginalInput);
                return true;
            }

            OnPlayerInputSubmitted?.Invoke(inputContext.OriginalInput);

            // 入力された文字を動的にフォントアトラスへ追加（文字欠け防止）
            FontPreloader preloader = FindFirstObjectByType<FontPreloader>();
            if (preloader != null)
            {
                preloader.AddCharacters(inputContext.OriginalInput);
            }

            // DialogueEngineへ投げて次のフローへ
            ProcessSubmittedInput(inputContext.OriginalInput);
            return true;
        }

        /// <summary>
        /// Replaces the current draft with a log suggestion. This never submits it.
        /// </summary>
        public void SetInputTextFromSuggestion(string text)
        {
            SetInputTextFromSuggestion(text, false);
        }

        public void SetInputTextFromTalkTopicHint(string text)
        {
            SetInputTextFromSuggestion(text, true);
        }

        private void SetInputTextFromSuggestion(string text, bool markTalkTopicHint)
        {
            if (chatInputField == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            inputHistory.ResetNavigation();
            CloseLogWindow();
            IsInDialogueMode = false;
            CancelInputModeTransition();
            FadeOutWindow();
            SetInputFieldVisible(true, true);

            string sanitized = SanitizeInput(text);
            pendingTalkTopicHintInput = markTalkTopicHint && !string.IsNullOrWhiteSpace(sanitized);
            int end = sanitized.Length;
            SetChatInputTextWithoutInvalidCaret(sanitized, end);
            webGLImeInputBridge?.SyncText(sanitized);
            UpdateInputFieldActiveVisualState(true);
        }

        private void SetChatInputTextWithoutInvalidCaret(string text, int caretPosition)
        {
            if (chatInputField == null)
            {
                return;
            }

            string value = text ?? string.Empty;
            chatInputField.caretPosition = 0;
            chatInputField.selectionAnchorPosition = 0;
            chatInputField.selectionFocusPosition = 0;
            chatInputField.SetTextWithoutNotify(value);
            webGLImeInputBridge?.SyncText(value);

            int caret = Mathf.Clamp(caretPosition, 0, value.Length);
            chatInputField.caretPosition = caret;
            chatInputField.selectionAnchorPosition = caret;
            chatInputField.selectionFocusPosition = caret;
        }

        private void ResetInputFieldCaretSelection()
        {
            if (chatInputField == null)
            {
                return;
            }

            int textLength = chatInputField.text != null ? chatInputField.text.Length : 0;
            int caret = Mathf.Clamp(chatInputField.caretPosition, 0, textLength);
            chatInputField.caretPosition = caret;
            chatInputField.selectionAnchorPosition = caret;
            chatInputField.selectionFocusPosition = caret;
        }

        public void SuppressNextEndEditSubmit()
        {
            suppressEndEditSubmitUntilFrame = Time.frameCount + 1;
        }

        private void ProcessSubmittedInput(string input)
        {
            if (menuInputBlocked) return;
            if (!RouteSubmittedInputToDialogueEngine || DialogueEngine.Instance == null)
            {
                VerboseLog($"[ChatUI][Submit] ProcessSubmittedInput skipped route={RouteSubmittedInputToDialogueEngine} engine={(DialogueEngine.Instance != null)} input={FormatDebugInput(input)}");
                return;
            }

            CatAnimationRuntime runtime = ResolveCatAnimationRuntime();
            CatPresentationModeController presentationMode = ResolveCatPresentationMode();
            VerboseLog($"[ChatUI][Submit] ProcessSubmittedInput route=true runtimeDelay={(runtime != null && runtime.IsDelayingDialogueResponse)} presentationDelay={(presentationMode != null && presentationMode.IsDelayingDialogueResponse)} input={FormatDebugInput(input)}");
            if ((runtime != null && runtime.IsDelayingDialogueResponse) ||
                (presentationMode != null && presentationMode.IsDelayingDialogueResponse))
            {
                if (delayedDialogueProcessCoroutine != null)
                {
                    StopCoroutine(delayedDialogueProcessCoroutine);
                }

                delayedDialogueProcessCoroutine = StartCoroutine(ProcessSubmittedInputAfterCatDelay(input, runtime, presentationMode));
                return;
            }

            DialogueEngine.Instance.ProcessInput(input);
        }

        private IEnumerator ProcessSubmittedInputAfterCatDelay(
            string input,
            CatAnimationRuntime runtime,
            CatPresentationModeController presentationMode)
        {
            while ((runtime != null && runtime.IsDelayingDialogueResponse) ||
                   (presentationMode != null && presentationMode.IsDelayingDialogueResponse))
            {
                yield return null;
            }

            delayedDialogueProcessCoroutine = null;
            VerboseLog($"[ChatUI][Submit] delayed ProcessSubmittedInput resumed route={RouteSubmittedInputToDialogueEngine} engine={(DialogueEngine.Instance != null)} input={FormatDebugInput(input)}");
            if (RouteSubmittedInputToDialogueEngine && DialogueEngine.Instance != null)
            {
                DialogueEngine.Instance.ProcessInput(input);
            }
        }

        private static string FormatDebugInput(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            string preview = value.Length <= 4 ? value : value.Substring(0, 4) + "...";
            return $"len={value.Length}, preview='{preview}'";
        }

        private bool InvokePlayerInputSubmittedSafely(string input)
        {
            if (OnPlayerInputSubmitted == null)
            {
                return false;
            }

            bool invokedAny = false;
            Delegate[] listeners = OnPlayerInputSubmitted.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] is not Action<string> listener)
                {
                    continue;
                }

                UnityEngine.Object targetObject = listener.Target as UnityEngine.Object;
                if (listener.Target != null && targetObject == null)
                {
                    OnPlayerInputSubmitted -= listener;
                    VerboseLog("[ChatUI][Submit] removed destroyed OnPlayerInputSubmitted listener.");
                    continue;
                }

                try
                {
                    listener.Invoke(input);
                    invokedAny = true;
                }
                catch (MissingReferenceException exception)
                {
                    OnPlayerInputSubmitted -= listener;
                    Debug.LogWarning($"[ChatUI][Submit] removed listener after MissingReferenceException: {exception.Message}");
                }
            }

            return invokedAny;
        }

        private CatAnimationRuntime ResolveCatAnimationRuntime()
        {
            if (catAnimationRuntime != null)
            {
                return catAnimationRuntime;
            }

            catAnimationRuntime = FindFirstObjectByType<CatAnimationRuntime>(FindObjectsInactive.Include);
            return catAnimationRuntime;
        }

        private CatPresentationModeController ResolveCatPresentationMode()
        {
            if (catPresentationMode != null)
            {
                return catPresentationMode;
            }

            catPresentationMode = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
            return catPresentationMode;
        }

        private void HandleBackButtonClicked()
        {
            OnBackRequested?.Invoke();
        }

        private string SanitizeInput(string value)
        {
            return SanitizeInput(value, ResolveInputLanguageMode());
        }

        private string SanitizeInput(string value, InputLanguageMode resolvedMode)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            InputFilterRule rule = ResolveInputFilterRule(resolvedMode);
            if (rule.FilterRegex == null)
            {
                return value.Trim();
            }

            return rule.FilterRegex.Replace(value, string.Empty).Trim();
        }

        private string SanitizeInputForEditing(string value, InputLanguageMode resolvedMode)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            InputFilterRule rule = ResolveInputFilterRule(resolvedMode);
            if (rule.FilterRegex == null)
            {
                return value;
            }

            return rule.FilterRegex.Replace(value, string.Empty);
        }

        private static bool IsSubmitKeyPressed()
        {
            return Input.GetKeyDown(KeyCode.Return) ||
                   Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        private void TrackImeComposition()
        {
            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                lastImeCompositionFrame = Time.frameCount;
            }
        }

        private bool IsImeCompositionActiveOrJustCommitted()
        {
            return externalImeCompositionActive ||
                   !string.IsNullOrEmpty(Input.compositionString) ||
                   Time.frameCount <= lastImeCompositionFrame + 1;
        }

        private void ReactivateInputAfterImeCommit()
        {
            if (chatInputField == null || !chatInputField.gameObject.activeInHierarchy)
            {
                return;
            }

            chatInputField.ActivateInputField();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(chatInputField.gameObject);
            }
        }

        private InputLanguageMode ResolveInputLanguageMode()
        {
            if (inputLanguageMode != InputLanguageMode.Auto)
            {
                return inputLanguageMode;
            }

            string locale = ResolveLocale();
            if (locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageMode.JapaneseKana;
            }

            if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageMode.English;
            }

            return InputLanguageMode.Unrestricted;
        }

        private static InputFilterRule ResolveInputFilterRule(InputLanguageMode resolvedMode)
        {
            switch (resolvedMode)
            {
                case InputLanguageMode.JapaneseKana:
                    return new InputFilterRule(JapaneseTextFilterRegex, InvalidJapaneseInputWarningMessage);
                case InputLanguageMode.English:
                    return new InputFilterRule(EnglishFilterRegex, EnglishInputWarningMessage);
                case InputLanguageMode.Unrestricted:
                case InputLanguageMode.Auto:
                default:
                    return new InputFilterRule(null, null);
            }
        }

        private string ResolveLocale()
        {
            ResolveConversationDataManager();

            return conversationDataManager != null ? conversationDataManager.Locale : "ja";
        }

        private void ResolveConversationDataManager()
        {
            if (conversationDataManager != null)
            {
                return;
            }

            conversationDataManager = FindFirstObjectByType<ConversationDataManager>();
        }

        private void SubscribeLocaleChanged()
        {
            ResolveConversationDataManager();
            if (conversationDataManager == null)
            {
                return;
            }

            conversationDataManager.LocaleChanged -= HandleLocaleChanged;
            conversationDataManager.LocaleChanged += HandleLocaleChanged;
        }

        private void UnsubscribeLocaleChanged()
        {
            if (conversationDataManager == null)
            {
                return;
            }

            conversationDataManager.LocaleChanged -= HandleLocaleChanged;
        }

        private void HandleLocaleChanged(string _)
        {
            ApplyCurrentInputFilter();
        }

        private static string ResolveLanguageModeChangedWarning(InputLanguageMode resolvedMode)
        {
            InputFilterRule rule = ResolveInputFilterRule(resolvedMode);
            return rule.InvalidInputWarning;
        }

        private string ResolveInputWarningMessage(string value, InputLanguageMode resolvedMode)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            switch (resolvedMode)
            {
                case InputLanguageMode.JapaneseKana:
                    return ResolveJapaneseTextInputWarningMessage(value);
                case InputLanguageMode.English:
                    return EnglishFilterRegex.IsMatch(value) ? EnglishInputWarningMessage : null;
                case InputLanguageMode.Unrestricted:
                case InputLanguageMode.Auto:
                default:
                    return null;
            }
        }

        private static string ResolveJapaneseTextInputWarningMessage(string value)
        {
            bool containsAsciiDirectInput = false;
            bool containsDisallowedJapaneseCharacter = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (IsAllowedJapaneseTextInputChar(c))
                {
                    continue;
                }

                if (IsAsciiDirectInputChar(c))
                {
                    containsAsciiDirectInput = true;
                    continue;
                }

                containsDisallowedJapaneseCharacter = true;
            }

            if (containsDisallowedJapaneseCharacter)
            {
                return InvalidJapaneseInputWarningMessage;
            }

            return containsAsciiDirectInput ? KanaInputModeWarningMessage : null;
        }

        private readonly struct InputFilterRule
        {
            public InputFilterRule(Regex filterRegex, string invalidInputWarning)
            {
                FilterRegex = filterRegex;
                InvalidInputWarning = invalidInputWarning;
            }

            public Regex FilterRegex { get; }
            public string InvalidInputWarning { get; }
        }

        private static bool IsAllowedJapaneseTextInputChar(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }

            if (IsArabicNumeralChar(c))
            {
                return true;
            }

            if ((c >= 'ぁ' && c <= 'ゖ') ||
                (c >= 'ァ' && c <= 'ヺ') ||
                (c >= '\u4E00' && c <= '\u9FFF') ||
                (c >= '\uF900' && c <= '\uFAFF'))
            {
                return true;
            }

            switch (c)
            {
                case 'ー':
                case '、':
                case '。':
                case '！':
                case '？':
                case '?':
                case '「':
                case '」':
                case '『':
                case '』':
                case '（':
                case '）':
                case '・':
                case '々':
                case '〆':
                case 'ヶ':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAsciiDirectInputChar(char c)
        {
            return c >= 0x21 && c <= 0x7E;
        }

        private static bool IsArabicNumeralChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= '０' && c <= '９');
        }

        private void EnsureInputWarningPopup()
        {
            if (inputWarningGroup != null)
            {
                return;
            }

            rootCanvas = ResolveRootCanvas();
            if (rootCanvas == null)
            {
                return;
            }

            GameObject root = new GameObject("ChatInputWarningPopup", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.transform.SetParent(rootCanvas.transform, false);
            root.transform.SetAsLastSibling();

            inputWarningRect = root.GetComponent<RectTransform>();
            inputWarningRect.anchorMin = new Vector2(0.5f, 0.5f);
            inputWarningRect.anchorMax = new Vector2(0.5f, 0.5f);
            inputWarningRect.pivot = new Vector2(0.5f, 0.5f);
            inputWarningRect.sizeDelta = new Vector2(560f, 46f);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.82f);
            background.raycastTarget = false;

            inputWarningGroup = root.GetComponent<CanvasGroup>();
            inputWarningGroup.alpha = 0f;
            inputWarningGroup.interactable = false;
            inputWarningGroup.blocksRaycasts = false;

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);

            inputWarningText = textObject.GetComponent<TextMeshProUGUI>();
            RectTransform textRect = inputWarningText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 8f);
            textRect.offsetMax = new Vector2(-16f, -8f);

            inputWarningText.font = messageText != null && messageText.font != null
                ? messageText.font
                : TMP_Settings.defaultFontAsset;
            inputWarningText.fontSize = 19f;
            inputWarningText.alignment = TextAlignmentOptions.Center;
            inputWarningText.color = Color.white;
            inputWarningText.textWrappingMode = TextWrappingModes.Normal;

            root.SetActive(false);
        }

        private Canvas ResolveRootCanvas()
        {
            if (messageText != null && messageText.canvas != null)
            {
                return messageText.canvas.rootCanvas;
            }

            if (chatInputField != null)
            {
                if (chatInputField.textComponent != null && chatInputField.textComponent.canvas != null)
                {
                    return chatInputField.textComponent.canvas.rootCanvas;
                }

                Canvas inputCanvas = chatInputField.GetComponentInParent<Canvas>();
                if (inputCanvas != null)
                {
                    return inputCanvas.rootCanvas;
                }
            }

            if (chatWindowGroup != null)
            {
                Canvas windowCanvas = chatWindowGroup.GetComponentInParent<Canvas>();
                if (windowCanvas != null)
                {
                    return windowCanvas.rootCanvas;
                }
            }

            return GetComponentInParent<Canvas>();
        }

        private void ShowInputWarning(string message)
        {
            EnsureInputWarningPopup();
            if (inputWarningGroup == null || inputWarningRect == null || inputWarningText == null)
            {
                return;
            }

            inputWarningText.text = message;
            PositionInputWarningPopup();
            inputWarningGroup.gameObject.SetActive(true);
            inputWarningGroup.transform.SetAsLastSibling();
            inputWarningGroup.alpha = 1f;

            if (inputWarningCoroutine != null)
            {
                StopCoroutine(inputWarningCoroutine);
            }

            inputWarningCoroutine = StartCoroutine(HideInputWarningAfterDelay());
        }

        private IEnumerator HideInputWarningAfterDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, inputWarningDuration));
            HideInputWarningInstant();
            inputWarningCoroutine = null;
        }

        private void HideInputWarningInstant()
        {
            if (inputWarningGroup == null)
            {
                return;
            }

            inputWarningGroup.alpha = 0f;
            inputWarningGroup.gameObject.SetActive(false);
        }

        private void PositionInputWarningPopup()
        {
            if (inputWarningRect == null || rootCanvas == null)
            {
                return;
            }

            RectTransform canvasRect = rootCanvas.transform as RectTransform;
            RectTransform anchorRect = GetInputWarningAnchorRect();
            if (canvasRect == null || anchorRect == null)
            {
                return;
            }

            Camera eventCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : rootCanvas.worldCamera;
            Vector3 worldAnchor = anchorRect.TransformPoint(new Vector3(0f, anchorRect.rect.yMax, 0f));
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldAnchor);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, eventCamera, out Vector2 localPoint))
            {
                inputWarningRect.anchoredPosition = new Vector2(localPoint.x, -90f);
            }
        }

        private RectTransform GetInputWarningAnchorRect()
        {
            if (chatWindowGroup != null)
            {
                return chatWindowGroup.transform as RectTransform;
            }

            return chatInputField != null ? chatInputField.transform as RectTransform : null;
        }

        /// <summary>
        /// 選択肢パネルを表示し、入力を待つ
        /// </summary>
        public void ShowChoices(Action onAccept, Action onDecline)
        {
            ShowChoices(string.Empty, onAccept, onDecline);
        }

        public void ShowChoices(string question, Action onAccept, Action onDecline)
        {
            ShowChoiceOptions(
                question,
                new[] { "はい", "いいえ" },
                selectedIndex =>
                {
                    if (selectedIndex == 0)
                    {
                        onAccept?.Invoke();
                        return;
                    }

                    onDecline?.Invoke();
                });
        }

        public void ShowChoiceOptions(IReadOnlyList<string> optionLabels, Action<int> onSelected)
        {
            ShowChoiceOptions(string.Empty, optionLabels, onSelected);
        }

        public void ShowChoiceOptions(
            string question,
            IReadOnlyList<string> optionLabels,
            Action<int> onSelected)
        {
            if (choicePanel == null || optionLabels == null || optionLabels.Count <= 0)
            {
                return;
            }

            HideChoices();
            EnsureChoiceLayoutInitialized();
            choicePanel.SetActive(true);
            SetChoiceQuestionText(question);
            choiceSpaceSubmitLockedUntil = Time.unscaledTime + ChoiceSpaceSubmitCooldownSeconds;
            currentChoiceDisplayLabels.Clear();

            IReadOnlyList<Button> buttons = EnsureChoiceButtonCount(optionLabels.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                int selectedIndex = i;
                string displayLabel = BuildChoiceDisplayLabel(selectedIndex, optionLabels[i]);
                currentChoiceDisplayLabels.Add(displayLabel);
                ConfigureChoiceButton(buttons[i], true, displayLabel, () =>
                {
                    string resolvedLabel = ResolveDisplayText(optionLabels[selectedIndex]);
                    DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
                    {
                        Speaker = DialogueLogManager.SpeakerPlayer,
                        SpeakerDisplayName = ResolveSpeakerDisplayName(DialogueLogManager.SpeakerPlayer),
                        Text = resolvedLabel,
                        Source = DialogueLogManager.SourceChoice,
                        RawInput = resolvedLabel
                    });

                    suppressClickAdvanceUntilRelease = true;
                    ClearChoiceSelectionFocus();
                    choicePanel.SetActive(false);
                    onSelected?.Invoke(selectedIndex);
                });
            }

            UpdateChoicePanelLayout(buttons.Count);
            SelectChoiceButton(0);
        }

        public void HideChoices()
        {
            if (choicePanel != null)
            {
                choicePanel.SetActive(false);
            }

            suppressClickAdvanceUntilRelease = true;
            ClearChoiceSelectionFocus();
            currentChoiceDisplayLabels.Clear();
            selectedChoiceIndex = -1;
            choiceSpaceSubmitLockedUntil = 0f;
            SetChoiceQuestionText(string.Empty);

            if (!choiceLayoutInitialized)
            {
                return;
            }

            for (int i = 0; i < runtimeChoiceButtons.Count; i++)
            {
                if (runtimeChoiceButtons[i] != null)
                {
                    runtimeChoiceButtons[i].onClick.RemoveAllListeners();
                    runtimeChoiceButtons[i].gameObject.SetActive(false);
                }
            }
        }

        public void SkipCurrentTyping()
        {
            if (isTyping && !menuInputBlocked)
            {
                skipTyping = true;
            }
        }

        public string ResolveDisplayText(string text)
        {
            return ReplaceVariables(text);
        }

        private void AddDialogueLogEntry(DialogueLogEntry template, string rawSpeakerName, string resolvedSpeakerName, string resolvedMessage)
        {
            DialogueLogManager dialogueLogManager = DialogueLogManager.Instance;
            if (dialogueLogManager == null)
            {
                return;
            }

            DialogueLogEntry logEntry = template != null ? template.Clone() : new DialogueLogEntry();
            logEntry.Timestamp = template != null ? template.Timestamp : default;
            logEntry.Speaker = template != null && !string.IsNullOrWhiteSpace(template.Speaker)
                ? template.Speaker
                : InferSpeakerId(rawSpeakerName, resolvedSpeakerName);
            logEntry.SpeakerDisplayName = ResolveSpeakerDisplayName(logEntry.Speaker, resolvedSpeakerName);
            logEntry.Text = resolvedMessage ?? string.Empty;
            logEntry.Source = template != null && !string.IsNullOrWhiteSpace(template.Source)
                ? template.Source
                : DialogueLogManager.SourceSystem;
            logEntry.Intent = template != null ? template.Intent : string.Empty;
            logEntry.NodeId = template != null ? template.NodeId : string.Empty;

            dialogueLogManager.AddLog(logEntry);
        }

        private string InferSpeakerId(string rawSpeakerName, string resolvedSpeakerName)
        {
            string playerName = ReplaceVariables("{{PLAYER_NAME}}");
            if (rawSpeakerName == "Player" || resolvedSpeakerName == playerName)
            {
                return DialogueLogManager.SpeakerPlayer;
            }

            string catName = ReplaceVariables("{{CAT_NAME}}");
            if (rawSpeakerName == "Cat" ||
                rawSpeakerName == "{{CAT_NAME}}" ||
                resolvedSpeakerName == catName)
            {
                return DialogueLogManager.SpeakerCat;
            }

            if (string.Equals(rawSpeakerName, "System", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawSpeakerName, "システム", StringComparison.Ordinal))
            {
                return DialogueLogManager.SpeakerSystem;
            }

            return string.IsNullOrWhiteSpace(resolvedSpeakerName)
                ? DialogueLogManager.SpeakerSystem
                : resolvedSpeakerName;
        }

        private string ResolveSpeakerDisplayName(string speakerId, string resolvedSpeakerName = null)
        {
            if (!string.IsNullOrWhiteSpace(resolvedSpeakerName) &&
                !string.Equals(resolvedSpeakerName, speakerId, StringComparison.Ordinal))
            {
                return resolvedSpeakerName;
            }

            switch (speakerId)
            {
                case DialogueLogManager.SpeakerPlayer:
                    return ReplaceVariables("{{PLAYER_NAME}}");
                case DialogueLogManager.SpeakerCat:
                    return ReplaceVariables("{{CAT_NAME}}");
                case DialogueLogManager.SpeakerSystem:
                    return "システム";
                default:
                    return speakerId ?? string.Empty;
            }
        }

        // ============================================
        // ログ画面関連
        // ============================================

        /// <summary>
        /// ログ画面の開閉を切り替えます。
        /// UIの「LogButton」などから呼び出してください。
        /// </summary>
        public void ToggleLogWindow()
        {
            EnsureLogWindowPanel();
            if (logWindowController == null) return;

            bool willOpen = !logWindowController.gameObject.activeSelf;
            suppressClickAdvanceUntilRelease = true;

            if (willOpen)
            {
                logWindowController.Open();
            }
            else
            {
                logWindowController.Close();
            }
        }

        public void OpenLogWindow()
        {
            EnsureLogWindowPanel();
            if (logWindowController == null) return;

            suppressClickAdvanceUntilRelease = true;
            webGLImeInputBridge?.ForceHideDomInput();
            webGLImeInputBridge?.SetSuspended(true);
            logWindowPanel?.transform.SetAsLastSibling();
            logWindowController.Open();
        }

        private bool deactivateUiRootAfterLogClose;
        private Coroutine externalLogOpenCoroutine;

        public void OpenLogWindowFromExternalContext()
        {
            if (!gameObject.activeSelf)
            {
                deactivateUiRootAfterLogClose = true;
                gameObject.SetActive(true);
                if (externalLogOpenCoroutine != null)
                {
                    StopCoroutine(externalLogOpenCoroutine);
                }
                externalLogOpenCoroutine = StartCoroutine(OpenLogWindowAfterUiInitialization());
                return;
            }

            OpenLogWindow();
        }

        private IEnumerator OpenLogWindowAfterUiInitialization()
        {
            // DialogueUIを初めてActive化したフレームではStart()がログを初期状態へ戻すため、
            // Start完了後の次フレームに開く。
            yield return null;
            externalLogOpenCoroutine = null;
            OpenLogWindow();
        }

        public void SetLogButtonLabel(string label)
        {
            if (logButton == null) return;
            TMP_Text text = logButton.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
        }

        private void SetSpeakerNameDisplay(string speakerName)
        {
            if (speakerNameText == null)
            {
                return;
            }

            bool hasSpeakerName = !string.IsNullOrWhiteSpace(speakerName);
            speakerNameText.text = hasSpeakerName ? speakerName : string.Empty;
            speakerNameText.gameObject.SetActive(hasSpeakerName);
        }

        public void CloseLogWindow()
        {
            EnsureLogWindowPanel();
            if (logWindowController == null) return;

            suppressClickAdvanceUntilRelease = true;
            logWindowController.Close();
            webGLImeInputBridge?.SetSuspended(false);

            if (deactivateUiRootAfterLogClose)
            {
                deactivateUiRootAfterLogClose = false;
                gameObject.SetActive(false);
            }
        }

        private void ConfigureChoiceButton(Button button, bool isVisible, string label, Action onClick)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(isVisible);
            if (!isVisible)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (!menuInputBlocked) onClick?.Invoke();
            });
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            TMP_Text labelText = GetChoiceLabelText(button);
            if (labelText != null)
            {
                labelText.alignment = TextAlignmentOptions.MidlineLeft;
                EnsureChoiceButtonCursor(button, labelText);
                labelText.text = ResolveDisplayText(label);
            }
        }

        private void ClearChoiceSelectionFocus()
        {
            if (choicePanel == null || EventSystem.current == null)
            {
                return;
            }

            GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
            if (selectedObject == null)
            {
                return;
            }

            Transform selectedTransform = selectedObject.transform;
            if (selectedTransform != null && selectedTransform.IsChildOf(choicePanel.transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void EnsureChoiceLayoutInitialized()
        {
            if (choiceLayoutInitialized || choicePanel == null || choiceAcceptButton == null)
            {
                return;
            }

            choicePanelRect = choicePanel.GetComponent<RectTransform>();
            if (choicePanelRect == null)
            {
                return;
            }

            RectTransform acceptRect = choiceAcceptButton.GetComponent<RectTransform>();
            RectTransform declineRect = choiceDeclineButton != null ? choiceDeclineButton.GetComponent<RectTransform>() : null;
            if (acceptRect == null)
            {
                return;
            }

            choiceButtonTopY = acceptRect.anchoredPosition.y;
            choiceButtonPosX = acceptRect.anchoredPosition.x;
            choiceButtonHeight = Mathf.Max(acceptRect.sizeDelta.y, acceptRect.rect.height);
            if (choiceButtonHeight <= 0f)
            {
                choiceButtonHeight = 30f;
            }

            if (declineRect != null)
            {
                choiceButtonStep = Mathf.Abs(acceptRect.anchoredPosition.y - declineRect.anchoredPosition.y);
            }

            if (choiceButtonStep <= 0f)
            {
                choiceButtonStep = choiceButtonHeight + 8f;
            }

            ResolveChoiceQuestionText();
            runtimeChoiceButtons.Clear();
            runtimeChoiceButtons.Add(choiceAcceptButton);
            if (choiceDeclineButton != null && choiceDeclineButton != choiceAcceptButton)
            {
                runtimeChoiceButtons.Add(choiceDeclineButton);
            }

            choiceLayoutInitialized = true;
        }

        private void ResolveChoiceQuestionText()
        {
            if (choiceQuestionText != null || choicePanel == null)
            {
                return;
            }

            TMP_Text[] texts = choicePanel.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text candidate = texts[i];
                if (candidate == null || candidate.GetComponentInParent<Button>() != null)
                {
                    continue;
                }

                choiceQuestionText = candidate;
                return;
            }
        }

        private void SetChoiceQuestionText(string question)
        {
            ResolveChoiceQuestionText();
            if (choiceQuestionText != null)
            {
                bool hasQuestion = !string.IsNullOrWhiteSpace(question);
                choiceQuestionText.text = !hasQuestion
                    ? string.Empty
                    : ResolveDisplayText(question);
                // The question is part of the VerticalLayoutGroup. Deactivating it
                // removes its layout slot completely when no question is supplied.
                choiceQuestionText.gameObject.SetActive(hasQuestion);
            }
        }

        private IReadOnlyList<Button> EnsureChoiceButtonCount(int count)
        {
            if (!choiceLayoutInitialized)
            {
                return Array.Empty<Button>();
            }

            while (runtimeChoiceButtons.Count < count)
            {
                Button templateButton = choiceAcceptButton != null ? choiceAcceptButton : choiceDeclineButton;
                if (templateButton == null)
                {
                    break;
                }

                Button clone = CreateRuntimeChoiceButton(templateButton, runtimeChoiceButtons.Count);
                if (clone == null)
                {
                    break;
                }

                runtimeChoiceButtons.Add(clone);
            }

            for (int i = 0; i < runtimeChoiceButtons.Count; i++)
            {
                Button button = runtimeChoiceButtons[i];
                if (button != null)
                {
                    button.gameObject.SetActive(i < count);
                }
            }

            return runtimeChoiceButtons.GetRange(0, Mathf.Min(count, runtimeChoiceButtons.Count));
        }

        private Button CreateRuntimeChoiceButton(Button templateButton, int index)
        {
            if (templateButton == null || choicePanel == null)
            {
                return null;
            }

            GameObject cloneObject = Object.Instantiate(templateButton.gameObject, choicePanel.transform, false);
            cloneObject.name = $"ChoiceButton_{index + 1}";
            Button cloneButton = cloneObject.GetComponent<Button>();
            if (cloneButton != null)
            {
                cloneButton.onClick.RemoveAllListeners();
            }

            return cloneButton;
        }

        private void UpdateChoicePanelLayout(int optionCount)
        {
            if (!choiceLayoutInitialized || choicePanelRect == null || optionCount <= 0)
            {
                return;
            }

            for (int i = 0; i < optionCount && i < runtimeChoiceButtons.Count; i++)
            {
                Button button = runtimeChoiceButtons[i];
                if (button == null)
                {
                    continue;
                }

                RectTransform rect = button.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                rect.anchoredPosition = new Vector2(choiceButtonPosX, choiceButtonTopY - (choiceButtonStep * i));
            }

            Vector2 sizeDelta = choicePanelRect.sizeDelta;
            bool hasQuestion = choiceQuestionText != null && choiceQuestionText.gameObject.activeSelf;
            float baseHeight = hasQuestion
                ? ChoicePanelHeightWithQuestion
                : ChoicePanelHeightWithoutQuestion;
            sizeDelta.y = baseHeight + Mathf.Max(0, optionCount - 2) * choiceButtonStep;
            choicePanelRect.sizeDelta = sizeDelta;
        }

        private void HandleChoiceKeyboardInput()
        {
            int activeChoiceCount = GetActiveChoiceButtonCount();
            if (activeChoiceCount <= 0)
            {
                return;
            }

            EnsureChoiceSelection();

            if (TryGetChoiceNumberKeyIndex(activeChoiceCount, out int numberIndex))
            {
                ActivateChoiceButton(numberIndex);
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                MoveChoiceSelection(-1, activeChoiceCount);
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                MoveChoiceSelection(1, activeChoiceCount);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ActivateSelectedChoiceButton();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) && Time.unscaledTime >= choiceSpaceSubmitLockedUntil)
            {
                ActivateSelectedChoiceButton();
            }
        }

        private static string BuildChoiceDisplayLabel(int index, string label)
        {
            return $"{ToFullWidthNumber(index + 1)}. {label ?? string.Empty}";
        }

        private static string ToFullWidthNumber(int value)
        {
            string source = Mathf.Max(0, value).ToString();
            char[] chars = source.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '0' && chars[i] <= '9')
                {
                    chars[i] = (char)('０' + (chars[i] - '0'));
                }
            }

            return new string(chars);
        }

        private int GetActiveChoiceButtonCount()
        {
            int count = 0;
            for (int i = 0; i < runtimeChoiceButtons.Count; i++)
            {
                Button button = runtimeChoiceButtons[i];
                if (button != null && button.gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        private void EnsureChoiceSelection()
        {
            if (selectedChoiceIndex >= 0 && selectedChoiceIndex < GetActiveChoiceButtonCount())
            {
                RefreshChoiceButtonSelectionVisuals();
                return;
            }

            SelectChoiceButton(0);
        }

        private void MoveChoiceSelection(int direction, int activeChoiceCount)
        {
            if (activeChoiceCount <= 0)
            {
                return;
            }

            int currentIndex = GetSelectedChoiceButtonIndex();
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            int nextIndex = (currentIndex + direction + activeChoiceCount) % activeChoiceCount;
            SelectChoiceButton(nextIndex);
        }

        private int GetSelectedChoiceButtonIndex()
        {
            if (selectedChoiceIndex >= 0 && selectedChoiceIndex < GetActiveChoiceButtonCount())
            {
                return selectedChoiceIndex;
            }

            if (EventSystem.current == null)
            {
                return -1;
            }

            GameObject current = EventSystem.current.currentSelectedGameObject;
            if (current == null)
            {
                return -1;
            }

            for (int i = 0; i < runtimeChoiceButtons.Count; i++)
            {
                Button button = runtimeChoiceButtons[i];
                if (button != null && button.gameObject.activeSelf && button.gameObject == current)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SelectChoiceButton(int index)
        {
            if (index < 0 || index >= runtimeChoiceButtons.Count)
            {
                return;
            }

            Button button = runtimeChoiceButtons[index];
            if (button == null || !button.gameObject.activeSelf)
            {
                return;
            }

            selectedChoiceIndex = index;
            ClearChoiceSelectionFocus();
            RefreshChoiceButtonSelectionVisuals();
        }

        private void ActivateSelectedChoiceButton()
        {
            int selectedIndex = GetSelectedChoiceButtonIndex();
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            ActivateChoiceButton(selectedIndex);
        }

        private void ActivateChoiceButton(int index)
        {
            if (index < 0 || index >= runtimeChoiceButtons.Count)
            {
                return;
            }

            Button button = runtimeChoiceButtons[index];
            if (button == null || !button.gameObject.activeSelf)
            {
                return;
            }

            button.onClick.Invoke();
        }

        private void RefreshChoiceButtonSelectionVisuals()
        {
            int labelCount = Mathf.Min(runtimeChoiceButtons.Count, currentChoiceDisplayLabels.Count);
            for (int i = 0; i < labelCount; i++)
            {
                Button button = runtimeChoiceButtons[i];
                if (button == null || !button.gameObject.activeSelf)
                {
                    continue;
                }

                TMP_Text labelText = GetChoiceLabelText(button);
                if (labelText == null)
                {
                    continue;
                }

                labelText.alignment = TextAlignmentOptions.MidlineLeft;
                labelText.text = ResolveDisplayText(currentChoiceDisplayLabels[i]);

                TMP_Text cursorText = EnsureChoiceButtonCursor(button, labelText);
                if (cursorText != null)
                {
                    cursorText.text = i == selectedChoiceIndex ? SelectedChoiceCursor : string.Empty;
                }
            }
        }

        private TMP_Text GetChoiceLabelText(Button button)
        {
            if (button == null)
            {
                return null;
            }

            TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text != null && !string.Equals(text.gameObject.name, ChoiceCursorObjectName, StringComparison.Ordinal))
                {
                    return text;
                }
            }

            return null;
        }

        private TMP_Text EnsureChoiceButtonCursor(Button button, TMP_Text labelText)
        {
            if (button == null || labelText == null)
            {
                return null;
            }

            RectTransform labelRect = labelText.rectTransform;
            if (labelRect != null)
            {
                Vector4 margin = labelText.margin;
                if (margin.x < ChoiceLabelLeftMargin)
                {
                    margin.x = ChoiceLabelLeftMargin;
                    labelText.margin = margin;
                }
            }

            Transform existingCursor = button.transform.Find(ChoiceCursorObjectName);
            if (existingCursor != null)
            {
                return existingCursor.GetComponent<TMP_Text>();
            }

            GameObject cursorObject = new GameObject(ChoiceCursorObjectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            cursorObject.transform.SetParent(button.transform, false);

            TMP_Text cursorText = cursorObject.GetComponent<TextMeshProUGUI>();
            RectTransform cursorRect = cursorText.rectTransform;
            cursorRect.anchorMin = new Vector2(0f, 0f);
            cursorRect.anchorMax = new Vector2(0f, 1f);
            cursorRect.pivot = new Vector2(0f, 0.5f);
            cursorRect.anchoredPosition = new Vector2(10f, 0f);
            cursorRect.sizeDelta = new Vector2(ChoiceCursorWidth, 0f);

            cursorText.font = labelText.font;
            cursorText.fontSharedMaterial = labelText.fontSharedMaterial;
            cursorText.fontSize = labelText.fontSize;
            cursorText.color = labelText.color;
            cursorText.alignment = TextAlignmentOptions.MidlineLeft;
            cursorText.textWrappingMode = TextWrappingModes.NoWrap;
            cursorText.raycastTarget = false;
            cursorText.text = string.Empty;

            return cursorText;
        }

        private bool TryGetChoiceNumberKeyIndex(int activeChoiceCount, out int index)
        {
            int maxCount = Mathf.Min(activeChoiceCount, 9);
            for (int i = 0; i < maxCount; i++)
            {
                KeyCode alphaKey = KeyCode.Alpha1 + i;
                KeyCode keypadKey = KeyCode.Keypad1 + i;
                if (Input.GetKeyDown(alphaKey) || Input.GetKeyDown(keypadKey))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }
    }
}
