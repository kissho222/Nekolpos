using System.Collections;
using System;
using System.Collections.Generic;
using Backgammon.Conversation;
using Cysharp.Threading.Tasks;
using Nekolpos.ActionSystem;
using UnityEngine;
using Nekolpos.Data;
using Nekolpos.StatusSystem;
using Nekolpos.TimeSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    /// <summary>
    /// 【ノベルゲーム進行マネージャー】
    /// DialogueEngine が決定した「会話リスト」を受け取り、1行ずつ ChatUIController に渡して再生させます。
    /// SpeechControl は会話内部制御、response_type は会話終了後処理として扱います。
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

        public ChatUIController chatUI;
        public CatDataSO catData;
        private const string UnknownWordTeachEventActionId = "unknown_word_teach_event";
        private const int UnknownWordTeachEventMinutesFallback = 120;
        private const int FoodHungryEventMinutesFallback = 120;
        private const float DefaultTimedEventDisplaySecondsFallback = 2.8f;
        private const int EmotionChangeUnit = 10;
        private const float DefaultDefeatMotionSeconds = 1.5f;
        internal const string PlayerDefeatLastReasonPrefsKey = "Nekolpos.PlayerDefeat.LastReason";
        internal const string PlayerDefeatReasonPrefsPrefix = "Nekolpos.PlayerDefeat.By.";

        // 現在再生中の会話リスト
        private List<DialogueReactionData> currentReactions = new List<DialogueReactionData>();
        private int currentLineIndex = 0;
        private const int MaxCallDepth = 5;
        private readonly Stack<DialogueCallFrame> callStack = new Stack<DialogueCallFrame>();

        // 状態管理
        private bool isProcessingDialog = false;

        private ActionManager actionManager;
        private DialogueEngine subscribedEngine;
        private ChatUIController subscribedChatUI;
        private FadeController fadeController;
        private ResultTextUI resultTextUi;
        private TimeManager timeManager;
        private YarnManager yarnManager;
        private StatusManager statusManager;
        private readonly LastPatternState lastPattern = new LastPatternState();
        private readonly LastPatternState lastIsolatedPrompt = new LastPatternState();
        private RepeatLastPatternAction repeatLastPatternAction;
        private bool isRepeatPlayback;
        private int currentPatternLabelHash;
        private string currentPatternId = string.Empty;
        private bool currentPatternTracksHistory;
        private DialogueLogEntry activeInputLogContext;
        private Coroutine eventTransitionCoroutine;
        private bool isEventTransitionRunning;

        // 【新機能】直近の「猫又の純粋な発言（メタ会話を除く）」を保持
        private string lastNekomataLine = "おはよう。今日はなにしよっか？"; // デフォルト発言
        private bool pendingEmotionChangeApplied;

        private sealed class LastPatternState
        {
            public int LabelHash;
            public string PatternId;
            private List<DialogueReactionData> reactions = new List<DialogueReactionData>();

            public bool HasValue => reactions != null && reactions.Count > 0;

            public void Set(int labelHash, string patternId, IReadOnlyList<DialogueReactionData> sourceReactions)
            {
                LabelHash = labelHash;
                PatternId = patternId != null ? patternId.Trim() : string.Empty;
                reactions = CloneReactionGroup(sourceReactions);
            }

            public bool TryGetReactions(out List<DialogueReactionData> clonedReactions)
            {
                clonedReactions = CloneReactionGroup(reactions);
                return clonedReactions.Count > 0;
            }

            public void Clear()
            {
                LabelHash = 0;
                PatternId = string.Empty;
                reactions.Clear();
            }
        }

        private sealed class RepeatLastPatternAction
        {
            private readonly DialogueManager dialogueManager;

            public RepeatLastPatternAction(DialogueManager dialogueManager)
            {
                this.dialogueManager = dialogueManager;
            }

            public bool Execute()
            {
                return dialogueManager != null && dialogueManager.ReplayLastPattern();
            }
        }

        private sealed class DialogueCallFrame
        {
            public List<DialogueReactionData> Reactions;
            public int NextLineIndex;
            public int LabelHash;
            public string PatternId;
            public int Order;
            public bool TrackAsLastPattern;
        }

        private void Awake()
        {
            if (Instance == null || Instance == this)
            {
                Instance = this;
            }
            else
            {
                Debug.LogWarning("[DialogueManager] 複数の DialogueManager が見つかりました。後から生成されたインスタンスを破棄します。");
                Destroy(this);
                return;
            }
        }

        private void Start()
        {
            RebuildRuntimeReferences(chatUI, catData, ShouldEnterInputModeOnStart());
        }

        private void Update()
        {
            EnsureDialogueEngineSubscription();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (subscribedEngine != null)
            {
                subscribedEngine.OnDialogueDetermined -= HandleDialogue;
                subscribedEngine = null;
            }

            if (subscribedChatUI != null)
            {
                subscribedChatUI.OnWaitInputCompleted -= ProgressNextLine;
                subscribedChatUI = null;
            }
        }

        public void RebuildRuntimeReferences(ChatUIController resolvedChatUI = null, CatDataSO resolvedCatData = null, bool enterInputMode = false)
        {
            if (resolvedChatUI != null)
            {
                chatUI = resolvedChatUI;
            }
            else if (chatUI == null)
            {
                chatUI = FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
            }

            if (resolvedCatData != null)
            {
                catData = resolvedCatData;
            }

            EnsureRuntimeDependencies();
            EnsureDialogueEngineSubscription();
            EnsureChatUiSubscription();

            if (enterInputMode)
            {
                chatUI?.EnterPlayerInputMode();
            }
        }

        public void ResetRuntimeStateForTitleRestart()
        {
            Debug.Log($"[DialogueManager][Reset] ResetRuntimeStateForTitleRestart processing={isProcessingDialog} reactions={(currentReactions != null ? currentReactions.Count : -1)} line={currentLineIndex} callStack={callStack.Count} eventTransition={eventTransitionCoroutine != null} subscribedEngine={(subscribedEngine != null)} subscribedChatUI={(subscribedChatUI != null)}");
            CancelPendingUiTransitions();
            currentReactions = new List<DialogueReactionData>();
            currentLineIndex = 0;
            callStack.Clear();
            isProcessingDialog = false;
            pendingEmotionChangeApplied = false;
            isRepeatPlayback = false;
            currentPatternLabelHash = 0;
            currentPatternId = string.Empty;
            currentPatternTracksHistory = false;
            activeInputLogContext = null;
            lastPattern.Clear();
            lastIsolatedPrompt.Clear();
            lastNekomataLine = "おはよう。今日はなにしよっか？";
            isEventTransitionRunning = false;
        }

        private void EnsureRuntimeDependencies()
        {
            actionManager ??= GetComponent<ActionManager>();
            if (actionManager == null)
            {
                actionManager = gameObject.AddComponent<ActionManager>();
            }

            statusManager ??= GetComponent<StatusManager>();
            if (statusManager == null)
            {
                statusManager = FindFirstObjectByType<StatusManager>() ?? gameObject.AddComponent<StatusManager>();
            }

            statusManager.ConfigureInitialData(catData);

            repeatLastPatternAction ??= new RepeatLastPatternAction(this);
            RenameSystem.Load(catData);
        }

        private void EnsureDialogueEngineSubscription()
        {
            DialogueEngine engine = DialogueEngine.Instance;
            if (subscribedEngine == engine)
            {
                return;
            }

            if (subscribedEngine != null)
            {
                subscribedEngine.OnDialogueDetermined -= HandleDialogue;
            }

            subscribedEngine = engine;
            if (subscribedEngine != null)
            {
                subscribedEngine.OnDialogueDetermined -= HandleDialogue;
                subscribedEngine.OnDialogueDetermined += HandleDialogue;
            }
        }

        private void EnsureChatUiSubscription()
        {
            if (subscribedChatUI != null && subscribedChatUI != chatUI)
            {
                subscribedChatUI.OnWaitInputCompleted -= ProgressNextLine;
                subscribedChatUI = null;
            }

            if (chatUI == null)
            {
                return;
            }

            chatUI.OnWaitInputCompleted -= ProgressNextLine;
            chatUI.OnWaitInputCompleted += ProgressNextLine;
            subscribedChatUI = chatUI;
        }

        private static bool ShouldEnterInputModeOnStart()
        {
            return !OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// DialogueEngineから確定したセリフグループが送られてきた時
        /// </summary>
        private void HandleDialogue(List<DialogueReactionData> reactions)
        {
            if (chatUI == null) return;
            if (reactions == null || reactions.Count == 0) return;

            activeInputLogContext = DialogueEngine.Instance != null ? DialogueEngine.Instance.LastInputLogContext : null;
            StartPatternPlayback(reactions, isRepeat: false);
        }

        /// <summary>
        /// 朝・昼・夕などの時間経過時に、CSVを介さず直接挨拶を流すための機能
        /// </summary>
        public void PlayIsolatedGreeting(string message)
        {
            if (chatUI == null) return;

            message = FormatRuntimeText(message);
            if (string.IsNullOrWhiteSpace(message))
            {
                ReturnToInputState();
                return;
            }

            CancelPendingUiTransitions();
            chatUI.SetLogButtonAllowedDuringConversation(true);
            chatUI.EnterDialogueMode();

            // 擬似的な1行だけの会話データを作る
            // これにより、ProgressNextLine() を通った際に通常フローの ReturnToInputState が呼ばれる
            List<DialogueReactionData> dummyReactions = new List<DialogueReactionData>();
            
            // ScriptableObject ではなく、最低限のデータを詰めたインスタンスを生成（データの作り方に依存しますが）
            // 一旦手動でフィールドをコピーするか、簡略化のため専用のフローに流します
            
            // ※ 今回は簡単な単発処理として、既存の currentReactions を上書きして流します
            DialogueReactionData dummy = new DialogueReactionData();
            dummy.TextJP = message;
            dummy.NextAction = ""; // 単発なので終わったら入力を待つ
            dummy.IntentID = "GREETING";
            
            dummyReactions.Add(dummy);
            lastIsolatedPrompt.Set(0, "GREETING", dummyReactions);

            currentReactions = dummyReactions;
            currentLineIndex = 0;
            isProcessingDialog = true;
            pendingEmotionChangeApplied = false;
            isRepeatPlayback = false;
            currentPatternLabelHash = 0;
            currentPatternId = string.Empty;
            currentPatternTracksHistory = false;

            // Nekomataの最後のセリフとしても記憶
            lastNekomataLine = message;

            // タイプライターに流す
            chatUI.ShowMessage(
                "{{CAT_NAME}}",
                message,
                CreateReactionLogTemplate(dummy, DialogueLogManager.SpeakerCat, DialogueLogManager.SourceRegexReaction, message));
        }

        public async UniTask PlayIsolatedGreetingAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            PlayIsolatedGreeting(message);
            await UniTask.WaitUntil(() => !isProcessingDialog);
        }

        /// <summary>
        /// 現在の行（単一のセリフ）をUIに投げてタイプ開始
        /// </summary>
        private void PlayCurrentLine()
        {
            if (currentLineIndex >= currentReactions.Count)
            {
                // 全てのページを読み終わった
                FinishDialogue();
                return;
            }

            DialogueReactionData currentData = currentReactions[currentLineIndex];

            string speakerId = ResolveReactionSpeakerId(currentData);
            string displayText = FormatRuntimeText(
                currentData != null ? currentData.TextJP : string.Empty,
                currentData);

            // 空テキストのAction行は会話ページではなく制御専用行である。
            // 表示してから次クリックを待つと、空の会話ウィンドウが残るため即時実行する。
            if (ShouldExecuteActionWithoutDisplay(currentData, displayText))
            {
                ExecuteActionResponse(currentData);
                return;
            }

            // メタ発言（INTENT_META等）でなければ、最後の猫発話として記憶
            if (currentData.IntentID != "INTENT_META" &&
                string.Equals(speakerId, DialogueLogManager.SpeakerCat, StringComparison.Ordinal))
            {
                lastNekomataLine = displayText;
            }

            PrepareLineDirectives(currentData);

            // ChatUI側に表示を命令
            chatUI.ShowMessage(
                ResolveReactionSpeakerDisplayName(speakerId),
                displayText,
                CreateReactionLogTemplate(currentData, speakerId, null, displayText));

            // この行独自の処理（選択肢や進行制御など）は、タイプ終了後 or 入力待ちのタイミングで評価する
        }

        private static bool ShouldExecuteActionWithoutDisplay(DialogueReactionData responseEntry, string displayText)
        {
            return responseEntry != null &&
                   string.Equals(NormalizeResponseType(responseEntry.ResponseType), "Action", StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrWhiteSpace(displayText);
        }

        /// <summary>
        /// プレイヤーが画面クリック/Space等で「次へ」進めようとした時
        /// </summary>
        public void ProgressNextLine()
        {
            if (!isProcessingDialog) return;

            DialogueReactionData currentData = currentReactions[currentLineIndex];

            if (TryExecuteReturn(currentData))
            {
                return;
            }

            if (TryExecuteCall(currentData))
            {
                return;
            }

            if (currentLineIndex == currentReactions.Count - 1)
            {
                RememberCurrentPatternIfNeeded();

                if (TryReturnFromCallStack())
                {
                    return;
                }

                HandleResponseType(currentData);
                return;
            }

            currentLineIndex++;
            PlayCurrentLine();
        }

        private void PrepareLineDirectives(DialogueReactionData currentData)
        {
            if (currentData == null)
            {
                return;
            }

            // Timeline sequences start/end when this CSV line begins; other
            // presentation directives retain their existing handling.
            _ = currentData.ResponseType;
            _ = currentData.Condition;
            _ = currentData.WaitTime;
            _ = currentData.HideUI;
            if (!string.IsNullOrWhiteSpace(currentData.Animation) &&
                currentData.Animation.Trim().StartsWith(DialogueTimelineSequenceController.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var sequenceController = GetComponent<DialogueTimelineSequenceController>();
                if (sequenceController != null)
                    sequenceController.Execute(currentData.Animation);
                else
                    Debug.LogError("[DialogueManager] Timeline sequence controller is not configured.", this);
            }
        }

        /// <summary>
        /// Choice 用の選択肢を表示する
        /// </summary>
        private void ShowChoiceResponse(DialogueReactionData responseEntry)
        {
            chatUI.EnterDialogueMode();
            string question = ResolveChoiceQuestion(responseEntry);
            string actionId = ResolveActionTriggerId(responseEntry);

            if (IsTeachingKnownWordChoiceAction(actionId))
            {
                chatUI.ShowChoiceOptions(
                    question,
                    new[]
                    {
                        "他の言葉を教える",
                        "もう一度教える",
                        "もう終わる"
                    },
                    selectedIndex =>
                    {
                        FinishDialogue();
                        DialogueEngine.Instance?.TryHandleTeachingAction($"{actionId}:{selectedIndex}", true);
                    });
                return;
            }

            Action onAccept = () =>
            {
                HandleChoiceAccepted(responseEntry);
            };
            Action onDecline = () =>
            {
                HandleChoiceDeclined(responseEntry);
            };

            string yesLabel = responseEntry != null ? responseEntry.ChoiceYesLabel : string.Empty;
            string noLabel = responseEntry != null ? responseEntry.ChoiceNoLabel : string.Empty;
            if (!string.IsNullOrWhiteSpace(yesLabel) || !string.IsNullOrWhiteSpace(noLabel))
            {
                chatUI.ShowChoiceOptions(
                    question,
                    new[]
                    {
                        string.IsNullOrWhiteSpace(yesLabel) ? "はい" : yesLabel,
                        string.IsNullOrWhiteSpace(noLabel) ? "いいえ" : noLabel
                    },
                    selectedIndex =>
                    {
                        if (selectedIndex == 0)
                        {
                            onAccept();
                            return;
                        }

                        onDecline();
                    });
                return;
            }

            chatUI.ShowChoices(question, onAccept, onDecline);
        }

        private void HandleChoiceAccepted(DialogueReactionData responseEntry)
        {
            string targetPatternId = responseEntry != null ? responseEntry.ChoiceYesPatternID : string.Empty;
            MarkActiveFoodChoice("yes");
            if (string.IsNullOrWhiteSpace(targetPatternId))
            {
                if (TryExecuteChoiceImmediateAction(responseEntry, "Yes"))
                {
                    return;
                }

                ReturnToInputState();
                return;
            }

            if (string.Equals(targetPatternId.Trim(), FoodHungryInputMatcher.YesDialogueKey, StringComparison.Ordinal))
            {
                FinishDialogue();
                RunFoodHungryCompletionAsync(responseEntry).Forget();
                return;
            }

            if (TryStartChoiceBranch(responseEntry, targetPatternId, "Yes"))
            {
                return;
            }

            ReturnToInputState();
        }

        private void HandleChoiceDeclined(DialogueReactionData responseEntry)
        {
            string targetPatternId = responseEntry != null ? responseEntry.ChoiceNoPatternID : string.Empty;
            MarkActiveFoodChoice("no");
            if (string.IsNullOrWhiteSpace(targetPatternId))
            {
                if (TryExecuteChoiceImmediateAction(responseEntry, "No"))
                {
                    return;
                }

                chatUI.ShowMessage(
                    "{{CAT_NAME}}",
                    "わかった。別の話にしようか。",
                    CreateLogTemplate(DialogueLogManager.SpeakerCat, DialogueLogManager.SourceChoice, "わかった。別の話にしようか。"));
                RememberSingleLineIfNeeded("CHOICE_NO_FALLBACK", "わかった。別の話にしようか。", DialogueLogManager.SourceChoice);
                chatUI.OnTypingCompleted += ReturnToInputStateOnce;
                return;
            }

            if (TryStartChoiceBranch(responseEntry, targetPatternId, "No"))
            {
                return;
            }

            chatUI.ShowMessage(
                "{{CAT_NAME}}",
                "わかった。別の話にしようか。",
                CreateLogTemplate(DialogueLogManager.SpeakerCat, DialogueLogManager.SourceChoice, "わかった。別の話にしようか。"));
            RememberSingleLineIfNeeded("CHOICE_NO_FALLBACK", "わかった。別の話にしようか。", DialogueLogManager.SourceChoice);
            chatUI.OnTypingCompleted += ReturnToInputStateOnce;
        }

        private void MarkActiveFoodChoice(string playerChoice)
        {
            if (activeInputLogContext == null ||
                !string.Equals(activeInputLogContext.TriggerType, FoodHungryInputMatcher.TriggerType, StringComparison.Ordinal))
            {
                return;
            }

            activeInputLogContext.PlayerChoice = playerChoice ?? string.Empty;
        }

        private bool TryExecuteChoiceImmediateAction(DialogueReactionData responseEntry, string branchName)
        {
            string actionId = ResolveActionTriggerId(responseEntry);
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            if (isRepeatPlayback)
            {
                Debug.Log($"[DialogueManager] Repeat 再生中のため Choice {branchName} の action_id {actionId} は実行しません。");
                ReturnToInputState();
                return true;
            }

            if (DialogueEngine.Instance != null && IsTeachingAction(actionId))
            {
                FinishDialogue();
                return DialogueEngine.Instance.TryHandleTeachingAction(
                    actionId,
                    string.Equals(branchName, "Yes", StringComparison.OrdinalIgnoreCase));
            }

            if (TryStartPlayerDefeatAction(actionId, responseEntry))
            {
                return true;
            }

            if (TryExecuteSpecialAction(actionId))
            {
                return true;
            }

            Debug.Log($"[DialogueManager] Choice {branchName} 即時実行: {actionId}");

            if (TryStartYarnNodeFromActionId(actionId))
            {
                return true;
            }

            if (TryStartSystemTimedEvent(actionId, responseEntry))
            {
                return true;
            }

            if (string.Equals(actionId, "StartEvent", StringComparison.OrdinalIgnoreCase))
            {
                TryStartEventTransition();
                return true;
            }

            FinishDialogue();
            RunActionFlowAsync(actionId, responseEntry).Forget();
            return true;
        }

        private bool TryStartChoiceBranch(DialogueReactionData responseEntry, string targetPatternId, string branchName)
        {
            if (responseEntry == null || string.IsNullOrWhiteSpace(targetPatternId))
            {
                return false;
            }

            RenameSystem.TryHandleChoiceBranch(targetPatternId.Trim(), string.Equals(branchName, "Yes", StringComparison.OrdinalIgnoreCase));

            if (!TryResolveChoiceBranchGroup(responseEntry, targetPatternId.Trim(), out List<DialogueReactionData> branchGroup) ||
                branchGroup == null ||
                branchGroup.Count == 0)
            {
                Debug.LogWarning($"[DialogueManager] Choice {branchName} 分岐先 Pattern {targetPatternId} が見つかりません。");
                return false;
            }

            CancelPendingUiTransitions();
            StartPatternPlayback(branchGroup, isRepeatPlayback);
            return true;
        }

        private bool TryResolveChoiceBranchGroup(
            DialogueReactionData responseEntry,
            string targetPatternId,
            out List<DialogueReactionData> branchGroup)
        {
            branchGroup = null;
            if (responseEntry == null || string.IsNullOrWhiteSpace(targetPatternId))
            {
                return false;
            }

            return TryResolveBranchPatternGroup(responseEntry, targetPatternId, out branchGroup);
        }

        private bool TryResolveBranchPatternGroup(
            DialogueReactionData responseEntry,
            string targetPatternId,
            out List<DialogueReactionData> branchGroup)
        {
            branchGroup = null;
            if (responseEntry == null || string.IsNullOrWhiteSpace(targetPatternId))
            {
                return false;
            }

            string resolver = responseEntry.BranchResolver;
            string trimmedTargetPatternId = targetPatternId.Trim();
            if (string.Equals(resolver, BasicSystemDialogueCatalog.BranchResolverId, StringComparison.OrdinalIgnoreCase))
            {
                return BasicSystemDialogueCatalog.TryCreateReactionGroup(
                    trimmedTargetPatternId,
                    responseEntry.RuntimePlaceholders,
                    out branchGroup);
            }

            if (string.Equals(resolver, InternalDialogueCatalog.BranchResolverId, StringComparison.OrdinalIgnoreCase))
            {
                return InternalDialogueCatalog.TryCreateReactionGroupByPatternId(
                    trimmedTargetPatternId,
                    responseEntry.RuntimePlaceholders,
                    out branchGroup);
            }

            DialogueEngine engine = DialogueEngine.Instance;
            if (engine == null)
            {
                Debug.LogWarning($"[DialogueManager] 分岐先 {trimmedTargetPatternId} を解決できません。DialogueEngine が未初期化です。");
                return false;
            }

            return engine.TryGetPatternGroup(responseEntry.LabelHash, trimmedTargetPatternId, out branchGroup);
        }

        private void ReturnToInputStateOnce()
        {
            chatUI.OnTypingCompleted -= ReturnToInputStateOnce;
            ReturnToInputState();
        }

        private void ReturnToInputStateOnWaitInputOnce()
        {
            chatUI.OnWaitInputCompleted -= ReturnToInputStateOnWaitInputOnce;
            ReturnToInputState();
        }

        private static string ResolveChoiceQuestion(DialogueReactionData responseEntry)
        {
            if (responseEntry == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(responseEntry.ChoiceQuestionJa)
                ? string.Empty
                : responseEntry.ChoiceQuestionJa;
        }

        private IEnumerator EventTransitionRoutine()
        {
            isEventTransitionRunning = true;

            // 暗転 -> 2秒待機 -> 時間経過
            chatUI.ShowMessage(
                "システム",
                SystemTimedEventCatalog.Get(
                    SystemTimedEventCatalog.LegacyStartEventIntroKey,
                    "（イベントが開始されました…）"),
                CreateLogTemplate(
                    DialogueLogManager.SpeakerSystem,
                    DialogueLogManager.SourceSystem,
                    SystemTimedEventCatalog.Get(
                        SystemTimedEventCatalog.LegacyStartEventIntroKey,
                        "（イベントが開始されました…）")));
            yield return new WaitForSeconds(2.0f);
            
            chatUI.ShowMessage(
                "システム",
                SystemTimedEventCatalog.Get(
                    SystemTimedEventCatalog.LegacyStartEventResultKey,
                    "そのまま雑談して過ごした…。\n（現在イベント未実装のため、時間経過処理のみ）"),
                CreateLogTemplate(
                    DialogueLogManager.SpeakerSystem,
                    DialogueLogManager.SourceSystem,
                    SystemTimedEventCatalog.Get(
                        SystemTimedEventCatalog.LegacyStartEventResultKey,
                        "そのまま雑談して過ごした…。\n（現在イベント未実装のため、時間経過処理のみ）")));
            
            // 処理が完了したら入力待ちに戻る
            yield return new WaitForSeconds(3.0f);
            eventTransitionCoroutine = null;
            isEventTransitionRunning = false;
            ReturnToInputState();
        }

        private bool TryStartEventTransition()
        {
            if (isEventTransitionRunning || eventTransitionCoroutine != null)
            {
                Debug.Log("[DialogueManager] EventTransitionRoutine is already running; duplicate StartEvent was ignored.");
                return true;
            }

            FinishDialogue();
            CancelPendingUiTransitions();
            eventTransitionCoroutine = StartCoroutine(EventTransitionRoutine());
            return true;
        }

        /// <summary>
        /// response_type に基づく会話終了後処理
        /// </summary>
        private void HandleResponseType(DialogueReactionData responseEntry)
        {
            string responseType = NormalizeResponseType(responseEntry != null ? responseEntry.ResponseType : string.Empty);
            switch (responseType)
            {
                case "Choice":
                    ShowChoiceResponse(responseEntry);
                    return;
                case "Action":
                    if (isRepeatPlayback)
                    {
                        ReturnToInputState();
                        return;
                    }

                    ExecuteActionResponse(responseEntry);
                    return;
                case "Event":
                    if (isRepeatPlayback)
                    {
                        ReturnToInputState();
                        return;
                    }

                    ExecuteEventResponse(responseEntry);
                    return;
                case "Reaction":
                    ReturnToInputState();
                    return;
                case "Normal":
                    HandleLegacyNextAction(responseEntry != null ? responseEntry.NextAction : string.Empty);
                    return;
                default:
                    ReturnToInputState();
                    return;
            }
        }

        private bool TryHandleChoiceWithActionFlow(DialogueReactionData responseEntry)
        {
            string actionId = responseEntry != null ? responseEntry.ActionId?.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            if (isRepeatPlayback)
            {
                Debug.Log($"[DialogueManager] Repeat 再生中のため Choice action_id {actionId} は実行しません。");
                ReturnToInputState();
                return true;
            }

            if (DialogueEngine.Instance != null && IsTeachingAction(actionId))
            {
                FinishDialogue();
                return DialogueEngine.Instance.TryHandleTeachingAction(actionId, true);
            }

            if (TryStartPlayerDefeatAction(actionId, responseEntry))
            {
                return true;
            }

            if (TryExecuteSpecialAction(actionId))
            {
                return true;
            }

            Debug.Log($"[DialogueManager] Choice を Action/Yarn フローへ委譲: {actionId}");
            FinishDialogue();
            RunActionFlowAsync(actionId, responseEntry).Forget();
            return true;
        }

        private void ExecuteActionResponse(DialogueReactionData responseEntry)
        {
            string actionId = ResolveActionTriggerId(responseEntry);
            if (string.IsNullOrWhiteSpace(actionId))
            {
                ReturnToInputState();
                return;
            }

            if (DialogueEngine.Instance != null && IsNoTalkTeachingStartAction(actionId, responseEntry))
            {
                ShowNoTalkTeachingStartChoice(actionId);
                return;
            }

            if (DialogueEngine.Instance != null && IsTeachingAction(actionId))
            {
                FinishDialogue();
                if (DialogueEngine.Instance.TryHandleTeachingAction(actionId, true))
                {
                    return;
                }
            }

            if (TryStartPlayerDefeatAction(actionId, responseEntry))
            {
                return;
            }

            if (TryExecuteSpecialAction(actionId))
            {
                return;
            }

            if (TryStartYarnNodeFromActionId(actionId))
            {
                return;
            }

            if (TryStartSystemTimedEvent(actionId, responseEntry))
            {
                return;
            }

            Debug.Log($"[DialogueManager] Action 実行: {actionId}");
            RunActionFlowAsync(actionId, responseEntry).Forget();
        }

        private void ShowNoTalkTeachingStartChoice(string actionId)
        {
            chatUI.EnterDialogueMode();
            chatUI.ShowChoiceOptions(
                string.Empty,
                new[] { "教える", "また今度" },
                selectedIndex =>
                {
                    FinishDialogue();
                    if (selectedIndex == 0)
                    {
                        DialogueEngine.Instance?.TryHandleTeachingAction(actionId, true);
                        return;
                    }

                    PlayIsolatedGreeting("じゃあ、何か他にやりたいことはある？");
                });
        }

        private void ExecuteEventResponse(DialogueReactionData responseEntry)
        {
            string actionId = responseEntry != null ? responseEntry.ActionId?.Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(actionId))
            {
                Debug.Log($"[DialogueManager] Event 進行: {actionId}");
            }

            if (TryStartYarnNodeFromActionId(actionId))
            {
                return;
            }

            if (TryStartPlayerDefeatAction(actionId, responseEntry))
            {
                return;
            }

            if (string.Equals(actionId, UnknownWordTeachEventActionId, StringComparison.OrdinalIgnoreCase))
            {
                FinishDialogue();
                RunUnknownWordTeachEventAsync().Forget();
                return;
            }

            if (TryStartSystemTimedEvent(actionId, responseEntry))
            {
                return;
            }

            if (string.Equals(actionId, "StartEvent", StringComparison.OrdinalIgnoreCase))
            {
                TryStartEventTransition();
                return;
            }

            if (string.IsNullOrWhiteSpace(actionId))
            {
                Debug.LogWarning("[DialogueManager] response_type=Event に action_id がないため、旧イベントフォールバックを実行せず入力待ちに戻します。");
            }
            else
            {
                Debug.LogWarning($"[DialogueManager] response_type=Event の action_id を解決できませんでした: {actionId}");
            }

            ReturnToInputState();
        }

        private bool TryStartSystemTimedEvent(string actionId, DialogueReactionData responseEntry)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            SystemTimedEventCatalog.ResolvedTimedEvent timedEvent = SystemTimedEventCatalog.Resolve(actionId.Trim());
            if (timedEvent == null)
            {
                return false;
            }

            FinishDialogue();
            RunSystemTimedEventAsync(timedEvent, responseEntry).Forget();
            return true;
        }

        private bool TryStartPlayerDefeatAction(string actionId, DialogueReactionData responseEntry)
        {
            if (!TryParsePlayerDefeatReason(actionId, out string reason))
            {
                return false;
            }

            FinishDialogue();
            RunPlayerDefeatFlowAsync(reason, responseEntry).Forget();
            return true;
        }

        public void TriggerPlayerDefeat(string reason, string presentationId = null, string postPatternId = null, string timedEventKey = null)
        {
            DialogueReactionData request = new DialogueReactionData
            {
                Animation = presentationId ?? string.Empty,
                TargetPatternID = postPatternId ?? string.Empty,
                TimedEventKey = timedEventKey ?? string.Empty
            };

            RunPlayerDefeatFlowAsync(reason, request).Forget();
        }

        public void RunTeachingTimePassage(string message)
        {
            RunTeachingTimePassageAsync(message, null, null, true).Forget();
        }

        public void RunTeachingTimePassage(
            string message,
            IReadOnlyDictionary<string, string> placeholders,
            List<DialogueReactionData> followUpReactions)
        {
            RunTeachingTimePassageAsync(message, placeholders, followUpReactions, true).Forget();
        }

        public void RunTeachingResult(
            string message,
            IReadOnlyDictionary<string, string> placeholders,
            List<DialogueReactionData> followUpReactions)
        {
            RunTeachingTimePassageAsync(message, placeholders, followUpReactions, false).Forget();
        }

        private async UniTaskVoid RunTeachingTimePassageAsync(
            string message,
            IReadOnlyDictionary<string, string> placeholders,
            List<DialogueReactionData> followUpReactions,
            bool advanceTime)
        {
            try
            {
                EnsureTimedEventPresentation();
                chatUI?.SetUiSuppressed(true);
                chatUI?.ClearDialogueDisplay();
                chatUI?.HideChoices();
                await fadeController.FadeOutAsync();

                TimeAdvanceResult result = default;
                bool hasTimeResult = false;
                if (advanceTime)
                {
                    result = timeManager.AdvanceTime(120);
                    hasTimeResult = true;
                }

                DialogueReactionData placeholderSource = placeholders != null
                    ? new DialogueReactionData { RuntimePlaceholders = new Dictionary<string, string>(placeholders, StringComparer.OrdinalIgnoreCase) }
                    : null;
                string resultMessage = FormatRuntimeText(message, placeholderSource);
                if (string.IsNullOrWhiteSpace(resultMessage))
                {
                    resultMessage = advanceTime
                        ? FormatRuntimeText(SystemTimedEventCatalog.Get("TEACH_TIME_PASSAGE", string.Empty))
                        : string.Empty;
                }

                fadeController.BringToFront();
                resultTextUi.BringToFront();
                await resultTextUi.ShowResultAsync(resultMessage, DefaultTimedEventDisplaySecondsFallback);
                await fadeController.FadeInAsync();
                chatUI?.SetUiSuppressed(false);

                if (followUpReactions != null && followUpReactions.Count > 0)
                {
                    StartPatternPlayback(followUpReactions, false);
                    return;
                }

                if (hasTimeResult && timeManager.TryGetTransitionGreeting(result, out string greeting))
                {
                    await PlayIsolatedGreetingAsync(greeting);
                    return;
                }

                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                chatUI?.SetUiSuppressed(false);
                ReturnToInputState();
            }
        }

        private async UniTaskVoid RunPlayerDefeatFlowAsync(string reason, DialogueReactionData responseEntry)
        {
            try
            {
                EnsureTimedEventPresentation();
                chatUI?.EnterDialogueMode();

                string normalizedReason = NormalizePlayerDefeatReason(reason);
                SetPlayerDefeatFlags(normalizedReason);
                RecordPlayerDefeatDiaryPending(normalizedReason, responseEntry);

                await PlayDefeatPresentationAsync(responseEntry);
                await fadeController.FadeOutAsync();
                await ShowDefeatTimedEventTextAsync(responseEntry);

                timeManager.AdvanceToNextMorning();
                await UniTask.Delay(TimeSpan.FromSeconds(0.25f), DelayType.UnscaledDeltaTime);
                await fadeController.FadeInAsync();

                if (TryStartPostActionPattern(responseEntry))
                {
                    return;
                }

                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReturnToInputState();
            }
        }

        private void RecordPlayerDefeatDiaryPending(string normalizedReason, DialogueReactionData responseEntry)
        {
            if (timeManager == null)
            {
                return;
            }

            string diaryId = responseEntry?.TimedEventKey;
            if (string.IsNullOrWhiteSpace(diaryId) &&
                responseEntry != null &&
                SystemTimedEventCatalog.Resolve(responseEntry.ActionId) != null)
            {
                diaryId = responseEntry.ActionId;
            }

            if (string.IsNullOrWhiteSpace(diaryId))
            {
                diaryId = $"death:{normalizedReason}";
            }

            string body = SystemTimedEventCatalog.Get(diaryId, string.Empty);
            if (string.IsNullOrWhiteSpace(body))
            {
                body = FormatRuntimeText(responseEntry?.TextJP, responseEntry);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                Debug.LogWarning($"[DialogueManager] 死亡日記 '{diaryId}' のCSV本文が見つかりません。Pendingは追加しません。");
                return;
            }

            DiaryJournalStore.RecordSpecialPending(diaryId, timeManager.CurrentDay, timeManager.CurrentPeriod, body);
        }

        private async UniTask PlayDefeatPresentationAsync(DialogueReactionData responseEntry)
        {
            string presentationId = responseEntry != null ? responseEntry.Animation : string.Empty;
            if (string.IsNullOrWhiteSpace(presentationId))
            {
                return;
            }

            if (TryPlayDefeatTimeline(presentationId, out PlayableDirector director))
            {
                await WaitForDirectorAsync(director);
                return;
            }

            if (TryPlayDefeatMotion(presentationId))
            {
                float waitSeconds = responseEntry != null && responseEntry.WaitTime > 0f
                    ? responseEntry.WaitTime
                    : DefaultDefeatMotionSeconds;
                await UniTask.Delay(TimeSpan.FromSeconds(waitSeconds), DelayType.UnscaledDeltaTime);
            }
        }

        private async UniTask ShowDefeatTimedEventTextAsync(DialogueReactionData responseEntry)
        {
            string timedEventKey = responseEntry != null ? responseEntry.TimedEventKey : string.Empty;
            if (string.IsNullOrWhiteSpace(timedEventKey) &&
                responseEntry != null &&
                SystemTimedEventCatalog.Resolve(responseEntry.ActionId) != null)
            {
                timedEventKey = responseEntry.ActionId;
            }

            if (string.IsNullOrWhiteSpace(timedEventKey))
            {
                return;
            }

            SystemTimedEventCatalog.ResolvedTimedEvent timedEvent = SystemTimedEventCatalog.Resolve(timedEventKey.Trim());
            if (timedEvent == null)
            {
                Debug.LogWarning($"[DialogueManager] 死亡フロー用 SystemTimedEvent key {timedEventKey} が見つかりません。");
                return;
            }

            string resultMessage = FormatTimedEventText(timedEvent.Text, responseEntry);
            if (string.IsNullOrWhiteSpace(resultMessage))
            {
                return;
            }

            DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
            {
                Speaker = DialogueLogManager.SpeakerSystem,
                Text = resultMessage,
                Source = DialogueLogManager.SourceAction
            });

            float displaySeconds = timedEvent.DisplaySeconds > 0f
                ? timedEvent.DisplaySeconds
                : DefaultTimedEventDisplaySecondsFallback;
            await resultTextUi.ShowResultAsync(resultMessage, displaySeconds);
        }

        private bool TryPlayDefeatTimeline(string presentationId, out PlayableDirector director)
        {
            director = null;
            string directorName = StripPresentationPrefix(presentationId, "timeline:");
            if (string.Equals(directorName, presentationId, StringComparison.Ordinal))
            {
                return false;
            }

            director = FindPlayableDirector(directorName);
            if (director == null || director.playableAsset == null)
            {
                Debug.LogWarning($"[DialogueManager] death timeline が見つかりません: {directorName}");
                return false;
            }

            director.enabled = true;
            director.time = 0d;
            director.Play();
            return true;
        }

        private bool TryPlayDefeatMotion(string presentationId)
        {
            string motionId = StripPresentationPrefix(presentationId, "motion:");
            motionId = StripPresentationPrefix(motionId, "anim:");
            if (string.IsNullOrWhiteSpace(motionId))
            {
                return false;
            }

            CatPresentationModeController presentationMode = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
            if (presentationMode != null)
            {
                presentationMode.PlayMotion(motionId);
                return true;
            }

            CatMotionController motionController = FindFirstObjectByType<CatMotionController>(FindObjectsInactive.Include);
            if (motionController != null)
            {
                motionController.PlayMotion(motionId);
                return true;
            }

            CatAnimationRuntime animationRuntime = FindFirstObjectByType<CatAnimationRuntime>(FindObjectsInactive.Include);
            if (animationRuntime != null)
            {
                animationRuntime.PlayMotionId(motionId);
                return true;
            }

            CatController catController = FindFirstObjectByType<CatController>(FindObjectsInactive.Include);
            if (catController != null)
            {
                catController.PlayAnimation(motionId);
                return true;
            }

            Debug.LogWarning($"[DialogueManager] death motion を再生できません: {motionId}");
            return false;
        }

        private static async UniTask WaitForDirectorAsync(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update);
            while (director != null && director.state == PlayState.Playing)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        private static PlayableDirector FindPlayableDirector(string directorName)
        {
            if (string.IsNullOrWhiteSpace(directorName))
            {
                return null;
            }

            PlayableDirector[] directors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
            {
                PlayableDirector director = directors[i];
                if (director == null)
                {
                    continue;
                }

                if (string.Equals(director.name, directorName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(director.gameObject.name, directorName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(director.playableAsset != null ? director.playableAsset.name : string.Empty, directorName, StringComparison.OrdinalIgnoreCase))
                {
                    return director;
                }
            }

            return null;
        }

        private static string StripPresentationPrefix(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(prefix))
            {
                return value ?? string.Empty;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(prefix.Length).Trim()
                : trimmed;
        }

        private static bool TryParsePlayerDefeatReason(string actionId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            string trimmed = actionId.Trim();
            if (trimmed.StartsWith("death:", StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring("death:".Length);
                return !string.IsNullOrWhiteSpace(reason);
            }

            if (trimmed.StartsWith("defeat:", StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring("defeat:".Length);
                return !string.IsNullOrWhiteSpace(reason);
            }

            const string prefix = "PlayerDefeatedBy";
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring(prefix.Length);
                return !string.IsNullOrWhiteSpace(reason);
            }

            if (string.Equals(trimmed, "action_angry_punch", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Anger";
                return true;
            }

            return false;
        }

        private void SetPlayerDefeatFlags(string reason)
        {
            string normalizedReason = NormalizePlayerDefeatReason(reason);
            string flagName = "PlayerDefeatedBy" + normalizedReason;

            Debug.Log($"[DialogueManager] Player defeat flags set: reason={reason}, normalized={normalizedReason}, flag={flagName}");

            GameFlagManager.Instance?.SetFlag("IsDeadToday", true);
            ClearPlayerDefeatFlags(flagName);
            GameFlagManager.Instance?.SetFlag(flagName, true);

            ConversationGameStateManager stateManager = FindFirstObjectByType<ConversationGameStateManager>();
            if (stateManager != null && stateManager.State != null)
            {
                stateManager.State.SetBool("IsDeadToday", true);
                ClearPlayerDefeatStateFlags(stateManager);
                stateManager.State.SetBool(flagName, true);
                stateManager.State.SetString("LastPlayerDefeatReason", normalizedReason);
            }

            StorePersistentPlayerDefeatFlags(normalizedReason, flagName);
        }

        private static void ClearPlayerDefeatFlags(string exceptFlagName)
        {
            string[] defeatFlags =
            {
                "PlayerDefeatedByNekomata",
                "PlayerDefeatedByPredation",
                "PlayerDefeatedByAnger",
                "PlayerDefeatedByAccident"
            };

            for (int i = 0; i < defeatFlags.Length; i++)
            {
                if (string.Equals(defeatFlags[i], exceptFlagName, StringComparison.Ordinal))
                {
                    continue;
                }

                GameFlagManager.Instance?.SetFlag(defeatFlags[i], false);
            }
        }

        private static void ClearPlayerDefeatStateFlags(ConversationGameStateManager stateManager)
        {
            if (stateManager == null || stateManager.State == null)
            {
                return;
            }

            stateManager.State.SetBool("PlayerDefeatedByNekomata", false);
            stateManager.State.SetBool("PlayerDefeatedByPredation", false);
            stateManager.State.SetBool("PlayerDefeatedByAnger", false);
            stateManager.State.SetBool("PlayerDefeatedByAccident", false);
        }

        private static void StorePersistentPlayerDefeatFlags(string normalizedReason, string currentFlagName)
        {
            PlayerPrefs.SetString(PlayerDefeatLastReasonPrefsKey, normalizedReason ?? string.Empty);

            string[] defeatReasons =
            {
                "Nekomata",
                "Predation",
                "Anger",
                "Accident"
            };

            for (int i = 0; i < defeatReasons.Length; i++)
            {
                string flagName = "PlayerDefeatedBy" + defeatReasons[i];
                PlayerPrefs.SetInt(
                    PlayerDefeatReasonPrefsPrefix + defeatReasons[i],
                    string.Equals(flagName, currentFlagName, StringComparison.Ordinal) ? 1 : 0);
            }

            PlayerPrefs.Save();
        }

        private static string NormalizePlayerDefeatReason(string reason)
        {
            string trimmed = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason.Trim();
            char[] buffer = new char[trimmed.Length];
            int count = 0;
            bool capitalizeNext = true;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (!char.IsLetterOrDigit(c))
                {
                    capitalizeNext = true;
                    continue;
                }

                buffer[count++] = capitalizeNext ? char.ToUpperInvariant(c) : c;
                capitalizeNext = false;
            }

            string normalized = count > 0 ? new string(buffer, 0, count) : "Unknown";
            switch (normalized.ToLowerInvariant())
            {
                case "vore":
                case "predation":
                case "bite":
                case "biting":
                case "eat":
                case "eaten":
                    return "Predation";
                case "anger":
                case "punch":
                case "claw":
                case "attack":
                    return "Anger";
                case "accident":
                    return "Accident";
                case "nekomata":
                    return "Nekomata";
                default:
                    return normalized;
            }
        }

        private async UniTaskVoid RunSystemTimedEventAsync(
            SystemTimedEventCatalog.ResolvedTimedEvent timedEvent,
            DialogueReactionData responseEntry)
        {
            try
            {
                EnsureTimedEventPresentation();

                await fadeController.FadeOutAsync();
                int diaryDay = timeManager.CurrentDay;
                DayPeriod diaryPeriod = timeManager.CurrentPeriod;
                int timeMinutes = timedEvent != null && timedEvent.TimeMinutes > 0
                    ? timedEvent.TimeMinutes
                    : UnknownWordTeachEventMinutesFallback;
                TimeAdvanceResult timeResult = timeManager.AdvanceTime(timeMinutes);

                string resultMessage = FormatTimedEventText(
                    timedEvent != null ? timedEvent.Text : string.Empty,
                    responseEntry);
                if (string.IsNullOrWhiteSpace(resultMessage))
                {
                    resultMessage = "少し時間が過ぎた。";
                }

                if (timedEvent != null)
                {
                    if (timedEvent.IsSpecialDiary)
                    {
                        DiaryJournalStore.RecordSpecialPending(timedEvent.DiaryId, diaryDay, diaryPeriod, resultMessage);
                    }
                    else
                    {
                        DiaryJournalStore.RecordNormalDraft(timedEvent.DiaryId, diaryDay, diaryPeriod, resultMessage);
                    }
                }

                DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
                {
                    Speaker = DialogueLogManager.SpeakerSystem,
                    Text = resultMessage,
                    Source = DialogueLogManager.SourceAction
                });

                float displaySeconds = timedEvent != null && timedEvent.DisplaySeconds > 0f
                    ? timedEvent.DisplaySeconds
                    : DefaultTimedEventDisplaySecondsFallback;
                await resultTextUi.ShowResultAsync(resultMessage, displaySeconds);
                await fadeController.FadeInAsync();

                if (TryStartPostActionPattern(responseEntry))
                {
                    return;
                }

                if (timeManager.TryGetTransitionGreeting(timeResult, out string greeting))
                {
                    await PlayIsolatedGreetingAsync(greeting);
                    StartNightDiaryIfNeeded(timeResult);
                    return;
                }

                StartNightDiaryIfNeeded(timeResult);
                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReturnToInputState();
            }
        }

        private void StartNightDiaryIfNeeded(TimeAdvanceResult timeResult)
        {
            if (timeResult.CurrentPeriod == DayPeriod.Night && timeResult.PreviousPeriod != DayPeriod.Night)
            {
                DiaryCalendarController.Instance?.BeginNightDiary();
            }
        }

        private async UniTaskVoid RunUnknownWordTeachEventAsync()
        {
            try
            {
                EnsureTimedEventPresentation();
                SystemTimedEventCatalog.ResolvedTimedEvent timedEvent =
                    SystemTimedEventCatalog.Resolve(SystemTimedEventCatalog.UnknownWordTeachEventKey);

                await fadeController.FadeOutAsync();
                int timeMinutes = timedEvent != null && timedEvent.TimeMinutes > 0
                    ? timedEvent.TimeMinutes
                    : UnknownWordTeachEventMinutesFallback;
                TimeAdvanceResult timeResult = timeManager.AdvanceTime(timeMinutes);

                string resultMessage = FormatRuntimeText(
                    timedEvent != null && !string.IsNullOrWhiteSpace(timedEvent.Text)
                        ? timedEvent.Text
                        : "猫又に新しい言葉を教えた");
                DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
                {
                    Speaker = DialogueLogManager.SpeakerSystem,
                    Text = resultMessage,
                    Source = DialogueLogManager.SourceAction
                });

                float displaySeconds = timedEvent != null && timedEvent.DisplaySeconds > 0f
                    ? timedEvent.DisplaySeconds
                    : DefaultTimedEventDisplaySecondsFallback;
                await resultTextUi.ShowResultAsync(resultMessage, displaySeconds);
                await fadeController.FadeInAsync();

                if (timeManager.TryGetTransitionGreeting(timeResult, out string greeting))
                {
                    await PlayIsolatedGreetingAsync(greeting);
                    return;
                }

                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReturnToInputState();
            }
        }

        private async UniTaskVoid RunFoodHungryCompletionAsync(DialogueReactionData responseEntry)
        {
            try
            {
                EnsureTimedEventPresentation();
                SystemTimedEventCatalog.ResolvedTimedEvent timedEvent =
                    SystemTimedEventCatalog.Resolve(FoodHungryInputMatcher.CompleteActionId);

                await fadeController.FadeOutAsync();
                int timeMinutes = timedEvent != null && timedEvent.TimeMinutes > 0
                    ? timedEvent.TimeMinutes
                    : FoodHungryEventMinutesFallback;
                TimeAdvanceResult timeResult = timeManager.AdvanceTime(timeMinutes);
                await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.UnscaledDeltaTime);

                string resultText = FormatTimedEventText(
                    timedEvent != null ? timedEvent.Text : string.Empty,
                    responseEntry);
                if (string.IsNullOrWhiteSpace(resultText))
                {
                    resultText = "少し時間が過ぎた。";
                }

                DialogueLogManager.Instance?.AddLog(new DialogueLogEntry
                {
                    Speaker = DialogueLogManager.SpeakerSystem,
                    Text = resultText,
                    Source = DialogueLogManager.SourceAction
                });

                float displaySeconds = timedEvent != null && timedEvent.DisplaySeconds > 0f
                    ? timedEvent.DisplaySeconds
                    : DefaultTimedEventDisplaySecondsFallback;
                await resultTextUi.ShowResultAsync(resultText, displaySeconds);
                await fadeController.FadeInAsync();

                if (TryStartPostActionPattern(responseEntry))
                {
                    return;
                }

                if (timeManager.TryGetTransitionGreeting(timeResult, out string greeting))
                {
                    await PlayIsolatedGreetingAsync(greeting);
                    return;
                }

                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReturnToInputState();
            }
        }

        private UniTask WaitForChatAdvanceAsync()
        {
            if (chatUI == null)
            {
                return UniTask.CompletedTask;
            }

            UniTaskCompletionSource completionSource = new UniTaskCompletionSource();
            Action handler = null;
            handler = () =>
            {
                chatUI.OnWaitInputCompleted -= handler;
                completionSource.TrySetResult();
            };

            chatUI.OnWaitInputCompleted += handler;
            return completionSource.Task;
        }

        private string FormatTimedEventText(string template, DialogueReactionData responseEntry)
        {
            return FormatRuntimeText(template, responseEntry);
        }

        public string FormatRuntimeText(string template, DialogueReactionData responseEntry = null)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            string formatted = template;
            if (responseEntry != null && responseEntry.RuntimePlaceholders != null)
            {
                foreach (KeyValuePair<string, string> pair in responseEntry.RuntimePlaceholders)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    formatted = formatted.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
                    formatted = formatted.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
                }
            }

            if (catData != null)
            {
                formatted = ReplaceRuntimePlaceholder(formatted, "CAT_NAME", catData.catName);
                formatted = ReplaceRuntimePlaceholder(formatted, "CAT_PRONOUN", catData.catPronoun);
                formatted = ReplaceRuntimePlaceholder(formatted, "PLAYER_CALLING", catData.playerCalling);
                formatted = ReplaceRuntimePlaceholder(formatted, "PLAYER_NAME", catData.playerName);
            }
            else
            {
                formatted = ReplaceRuntimePlaceholderFromRenameSystem(formatted, "CAT_NAME");
                formatted = ReplaceRuntimePlaceholderFromRenameSystem(formatted, "CAT_PRONOUN");
                formatted = ReplaceRuntimePlaceholderFromRenameSystem(formatted, "PLAYER_CALLING");
                formatted = ReplaceRuntimePlaceholderFromRenameSystem(formatted, "PLAYER_NAME");
            }

            return formatted;
        }

        private static string ReplaceRuntimePlaceholderFromRenameSystem(string text, string key)
        {
            return RenameSystem.TryResolvePlaceholder(key, out string value)
                ? ReplaceRuntimePlaceholder(text, key, value)
                : text;
        }

        private static string ReplaceRuntimePlaceholder(string text, string key, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(key))
            {
                return text;
            }

            string replacement = value != null ? value.Trim() : string.Empty;
            return text
                .Replace("{{" + key + "}}", replacement)
                .Replace("{{ " + key + " }}", replacement)
                .Replace("{" + key + "}", replacement);
        }

        private void EnsureTimedEventPresentation()
        {
            fadeController = fadeController != null ? fadeController : GetComponent<FadeController>() ?? gameObject.AddComponent<FadeController>();
            resultTextUi = resultTextUi != null ? resultTextUi : GetComponent<ResultTextUI>() ?? gameObject.AddComponent<ResultTextUI>();
            timeManager = timeManager != null ? timeManager : GetComponent<TimeManager>() ?? gameObject.AddComponent<TimeManager>();

            chatUI?.ClearDialogueDisplay();
            Canvas runtimeCanvas = chatUI != null && chatUI.messageText != null ? chatUI.messageText.canvas : null;
            fadeController.Initialize(runtimeCanvas);
            resultTextUi.Initialize(runtimeCanvas, chatUI != null && chatUI.messageText != null ? chatUI.messageText.font : null);
            timeManager.EnsureInitialized();
        }

        private IEnumerator PlayActionStubRoutine(string actionId)
        {
            chatUI.EnterDialogueMode();
            chatUI.ShowMessage(
                "システム",
                $"Action 実行: {actionId}",
                CreateLogTemplate(DialogueLogManager.SpeakerSystem, DialogueLogManager.SourceAction, $"Action 実行: {actionId}"));
            yield return new WaitForSeconds(1.5f);
            ReturnToInputState();
        }

        private async UniTaskVoid RunActionFlowAsync(string actionId, DialogueReactionData responseEntry = null)
        {
            try
            {
                if (actionManager == null)
                {
                    actionManager = GetComponent<ActionManager>();
                }

                if (actionManager == null)
                {
                    StartCoroutine(PlayActionStubRoutine(actionId));
                    return;
                }

                bool hasPostActionPattern = HasPostActionPattern(responseEntry);
                ActionFlowResult result = await actionManager.RunActionFlowAsync(
                    new ActionRequest(actionId, lastNekomataLine, hasPostActionPattern));

                if (result == ActionFlowResult.Skipped)
                {
                    ReturnToInputState();
                    return;
                }

                if (TryStartPostActionPattern(responseEntry))
                {
                    return;
                }

                if (hasPostActionPattern &&
                    actionManager.TryGetLastTimeResult(out TimeAdvanceResult timeResult) &&
                    await TryPlayTimeTransitionGreetingAsync(timeResult))
                {
                    return;
                }

                ReturnToInputState();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReturnToInputState();
            }
        }

        private static bool HasPostActionPattern(DialogueReactionData responseEntry)
        {
            return !string.IsNullOrWhiteSpace(responseEntry != null ? responseEntry.TargetPatternID : string.Empty);
        }

        private bool TryStartPostActionPattern(DialogueReactionData responseEntry)
        {
            if (!HasPostActionPattern(responseEntry))
            {
                return false;
            }

            string targetPatternId = responseEntry.TargetPatternID.Trim();
            if (!TryResolvePostActionPatternGroup(responseEntry, targetPatternId, out List<DialogueReactionData> postActionGroup) ||
                postActionGroup == null ||
                postActionGroup.Count == 0)
            {
                int labelHash = responseEntry != null ? responseEntry.LabelHash : 0;
                Debug.LogWarning($"[DialogueManager] Action 後 Pattern {targetPatternId} が見つかりません。LabelHash={labelHash}");
                return false;
            }

            postActionGroup = FilterReactionsByCondition(postActionGroup);
            if (postActionGroup.Count == 0)
            {
                Debug.LogWarning($"[DialogueManager] Action 後 Pattern {targetPatternId} は condition に一致する行がありません。");
                return false;
            }

            FinishDialogue();
            StartPatternPlayback(postActionGroup, isRepeat: false);
            return true;
        }

        private bool TryResolvePostActionPatternGroup(
            DialogueReactionData responseEntry,
            string targetPatternId,
            out List<DialogueReactionData> postActionGroup)
        {
            postActionGroup = null;
            if (responseEntry == null || string.IsNullOrWhiteSpace(targetPatternId))
            {
                return false;
            }

            if (TryResolveBranchPatternGroup(responseEntry, targetPatternId, out postActionGroup) &&
                postActionGroup != null &&
                postActionGroup.Count > 0)
            {
                return true;
            }

            if (DialogueEngine.Instance != null &&
                DialogueEngine.Instance.TryFindPatternGroupByPatternId(targetPatternId, out postActionGroup) &&
                postActionGroup != null &&
                postActionGroup.Count > 0)
            {
                Debug.Log($"[DialogueManager] Action 後 Pattern {targetPatternId} を全ラベル検索で解決しました。");
                return true;
            }

            return false;
        }

        private List<DialogueReactionData> FilterReactionsByCondition(List<DialogueReactionData> reactions)
        {
            if (reactions == null || reactions.Count == 0)
            {
                return new List<DialogueReactionData>();
            }

            ConversationGameState state = ResolveConversationGameState();
            List<DialogueReactionData> filtered = new List<DialogueReactionData>();
            for (int i = 0; i < reactions.Count; i++)
            {
                DialogueReactionData reaction = reactions[i];
                if (reaction == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(reaction.Condition))
                {
                    filtered.Add(reaction);
                    continue;
                }

                if (state == null)
                {
                    continue;
                }

                try
                {
                    if (ConversationGameStateConditionEvaluator.Evaluate(state, reaction.Condition))
                    {
                        filtered.Add(reaction);
                    }
                }
                catch (FormatException exception)
                {
                    Debug.LogWarning($"[DialogueManager] condition 解釈失敗: {reaction.Condition} / {exception.Message}");
                }
            }

            return filtered;
        }

        private static ConversationGameState ResolveConversationGameState()
        {
            ConversationGameStateManager manager = FindFirstObjectByType<ConversationGameStateManager>();
            return manager != null ? manager.State : null;
        }

        private async UniTask<bool> TryPlayTimeTransitionGreetingAsync(TimeAdvanceResult timeResult)
        {
            if (timeManager == null)
            {
                timeManager = GetComponent<TimeManager>() ?? FindFirstObjectByType<TimeManager>();
            }

            if (timeManager == null || !timeManager.TryGetTransitionGreeting(timeResult, out string greeting))
            {
                return false;
            }

            await PlayIsolatedGreetingAsync(greeting);
            return true;
        }

        private string ResolveActionTriggerId(DialogueReactionData responseEntry)
        {
            if (responseEntry != null && !string.IsNullOrWhiteSpace(responseEntry.ActionId))
            {
                return responseEntry.ActionId.Trim();
            }

            string nextAction = responseEntry != null ? responseEntry.NextAction : string.Empty;
            if (!string.IsNullOrWhiteSpace(nextAction))
            {
            if (ContainsAny(nextAction, "Play_Game", "遊び", "ボール", "ゲーム"))
            {
                return "Play_Game";
            }

                if (ContainsAny(nextAction, "Petting", "撫で", "なで", "ブラシ"))
                {
                    return "Petting";
                }

            if (ContainsAny(nextAction, "Punch"))
            {
                return "Punch";
            }
        }

            string sourceRegexLabel = responseEntry != null ? responseEntry.SourceRegexLabel : string.Empty;
            if (ContainsAny(sourceRegexLabel, "遊", "あそ", "play", "ball", "ボール"))
            {
                return "Play_Game";
            }

            if (ContainsAny(sourceRegexLabel, "撫で", "なで", "pet", "pat", "stroke"))
            {
                return "Petting";
            }

            if (ContainsAny(sourceRegexLabel, "殴", "パンチ", "punch"))
            {
                return "Punch";
            }

            string choiceQuestion = responseEntry != null ? responseEntry.ChoiceQuestionJa : string.Empty;
            if (ContainsAny(choiceQuestion, "遊ぶ", "あそぶ", "ボール", "追いかけ"))
            {
                return "Play_Game";
            }

            if (ContainsAny(choiceQuestion, "撫で", "なで"))
            {
                return "Petting";
            }

            string hint = responseEntry != null ? responseEntry.TextJP : string.Empty;
            if (ContainsAny(hint, "ボール", "ゲーム", "遊ぶ"))
            {
                return "Play_Game";
            }

            if (ContainsAny(hint, "撫で", "なで"))
            {
                return "Petting";
            }

            return string.Empty;
        }

        private static bool ContainsAny(string source, params string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(source) || patterns == null)
            {
                return false;
            }

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (!string.IsNullOrWhiteSpace(pattern) &&
                    source.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 旧 NextAction 列の互換処理
        /// </summary>
        private void HandleLegacyNextAction(string nextAction)
        {
            if (isRepeatPlayback)
            {
                ReturnToInputState();
                return;
            }

            if (string.IsNullOrWhiteSpace(nextAction))
            {
                ReturnToInputState();
                return;
            }

            switch (nextAction)
            {
                case "AskAgain":
                    ReturnToInputState();
                    return;
                case "Repeat":
                    chatUI.ShowMessage(
                        "{{CAT_NAME}}",
                        lastNekomataLine,
                        CreateLogTemplate(DialogueLogManager.SpeakerCat, DialogueLogManager.SourceRegexReaction, lastNekomataLine));
                    Invoke(nameof(ReturnToInputState), 2.0f);
                    return;
                case "StartEvent":
                    TryStartEventTransition();
                    return;
                default:
                    ReturnToInputState();
                    return;
            }
        }

        private static string NormalizeResponseType(string responseType)
        {
            if (string.IsNullOrWhiteSpace(responseType))
            {
                return "Normal";
            }

            if (string.Equals(responseType, "Choice", StringComparison.OrdinalIgnoreCase))
            {
                return "Choice";
            }

            if (string.Equals(responseType, "Action", StringComparison.OrdinalIgnoreCase))
            {
                return "Action";
            }

            if (string.Equals(responseType, "Event", StringComparison.OrdinalIgnoreCase))
            {
                return "Event";
            }

            if (string.Equals(responseType, "Reaction", StringComparison.OrdinalIgnoreCase))
            {
                return "Reaction";
            }

            return "Normal";
        }

        private static bool IsTeachingAction(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   actionId.Trim().StartsWith("TeachingMode:", StringComparison.Ordinal);
        }

        private static bool IsTeachingKnownWordChoiceAction(string actionId)
        {
            return string.Equals(
                actionId?.Trim(),
                "TeachingMode:KnownWordChoice",
                StringComparison.Ordinal);
        }

        private static bool IsNoTalkTeachingStartAction(string actionId, DialogueReactionData responseEntry)
        {
            if (responseEntry == null ||
                string.IsNullOrWhiteSpace(actionId) ||
                !actionId.Trim().StartsWith("TeachingMode:Start:", StringComparison.Ordinal))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(responseEntry.PatternID) &&
                   responseEntry.PatternID.Trim().StartsWith("No_talk", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryStartYarnNodeFromActionId(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !actionId.StartsWith("yarn:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (yarnManager == null)
            {
                yarnManager = FindFirstObjectByType<YarnManager>();
            }

            if (yarnManager == null)
            {
                Debug.LogWarning($"[DialogueManager] YarnManager が見つからないため action_id {actionId} を実行できません。");
                ReturnToInputState();
                return true;
            }

            string nodeName = actionId.Substring("yarn:".Length).Trim();
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                Debug.LogWarning($"[DialogueManager] action_id {actionId} の node 名が空です。");
                ReturnToInputState();
                return true;
            }

            FinishDialogue();
            yarnManager.StartNode(nodeName);
            return true;
        }

        private void FinishDialogue()
        {
            callStack.Clear();
            isProcessingDialog = false;
            currentPatternLabelHash = 0;
            currentPatternId = string.Empty;
            currentPatternTracksHistory = false;
            isRepeatPlayback = false;
            activeInputLogContext = null;
        }

        /// <summary>
        /// 再質問ループ（プレイヤーの入力待ち状態）に戻る
        /// </summary>
        private void ReturnToInputState()
        {
            if (!isRepeatPlayback &&
                DialogueEngine.Instance != null &&
                DialogueEngine.Instance.TryHandleTeachingReactionCompleted(currentReactions, out List<DialogueReactionData> teachingFollowUp) &&
                teachingFollowUp != null &&
                teachingFollowUp.Count > 0)
            {
                StartPatternPlayback(teachingFollowUp, false);
                return;
            }

            ApplyPendingEmotionChange();
            CancelPendingUiTransitions();
            callStack.Clear();
            isProcessingDialog = false;
            currentPatternLabelHash = 0;
            currentPatternId = string.Empty;
            currentPatternTracksHistory = false;
            isRepeatPlayback = false;
            activeInputLogContext = null;
            if (DialogueEngine.Instance != null && DialogueEngine.Instance.IsTeachingModeActive)
            {
                chatUI.EnterTeachingInputMode();
            }
            else
            {
                chatUI.RestoreNormalConversationInputMode();
            }
        }

        private void ApplyPendingEmotionChange()
        {
            if (isRepeatPlayback || pendingEmotionChangeApplied || currentReactions == null || currentReactions.Count == 0)
            {
                return;
            }

            pendingEmotionChangeApplied = true;
            EnsureRuntimeDependencies();
            if (statusManager == null)
            {
                return;
            }

            DialogueReactionData firstReaction = currentReactions[0];
            if (firstReaction != null && firstReaction.EmotionChangeValue != 0)
            {
                int amount = Mathf.Clamp(firstReaction.EmotionChangeValue, -3, 3) * EmotionChangeUnit;
                ApplyEmotionDelta(firstReaction.EmotionChangeType, amount, firstReaction.PatternID);
            }
        }

        private void ApplyEmotionDelta(string emotionType, int amount, string sourceId)
        {
            if (statusManager == null || !TryResolveStatusType(emotionType, out StatusType statusType))
            {
                return;
            }

            statusManager.ApplyDelta(statusType, amount, "Dialogue", sourceId);
        }

        private static bool TryResolveStatusType(string emotionType, out StatusType statusType)
        {
            switch (emotionType?.Trim().ToLowerInvariant())
            {
                case "affection":
                    statusType = StatusType.Affection;
                    return true;
                case "sadistic":
                case "sadism":
                    statusType = StatusType.Sadistic;
                    return true;
                case "concern":
                case "anxiety":
                    statusType = StatusType.Concern;
                    return true;
                case "hostility":
                    statusType = StatusType.Hostility;
                    return true;
                case "obedience":
                    statusType = StatusType.Obedience;
                    return true;
                case "instinct":
                    statusType = StatusType.Instinct;
                    return true;
                default:
                    statusType = default;
                    return false;
            }
        }

        private DialogueLogEntry CreateLogTemplate(string speaker, string source, string text)
        {
            DialogueLogEntry entry = activeInputLogContext != null ? activeInputLogContext.Clone() : new DialogueLogEntry();
            entry.Speaker = speaker;
            entry.Source = source;
            entry.Text = text ?? string.Empty;
            if (!string.Equals(speaker, DialogueLogManager.SpeakerPlayer, StringComparison.Ordinal))
            {
                entry.ReactionResult = text ?? string.Empty;
            }

            return entry;
        }

        private DialogueLogEntry CreateReactionLogTemplate(DialogueReactionData reaction, string speaker, string sourceOverride, string textOverride)
        {
            DialogueLogEntry entry = CreateLogTemplate(
                speaker,
                !string.IsNullOrWhiteSpace(sourceOverride) ? sourceOverride : ResolveDialogueLogSource(reaction),
                !string.IsNullOrWhiteSpace(textOverride) ? textOverride : (reaction != null ? reaction.TextJP : string.Empty));

            if (reaction == null)
            {
                return entry;
            }

            entry.Intent = reaction.IntentID;
            entry.SelectedIntent = reaction.IntentID;
            entry.QuestionLinks = reaction.QuestionLinks;

            string responseId = !string.IsNullOrWhiteSpace(reaction.PatternID)
                ? reaction.PatternID
                : reaction.ActionId;
            if (!string.IsNullOrWhiteSpace(responseId))
            {
                entry.SelectedResponseId = responseId;
                entry.NodeId = responseId;
            }

            return entry;
        }

        private static string ResolveDialogueLogSource(DialogueReactionData reaction)
        {
            if (reaction == null)
            {
                return DialogueLogManager.SourceRegexReaction;
            }

            if (string.Equals(reaction.IntentID, "UNKNOWN_WORD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reaction.ReactionType, "UnknownWord", StringComparison.OrdinalIgnoreCase))
            {
                return DialogueLogManager.SourceUnknownWord;
            }

            return DialogueLogManager.SourceRegexReaction;
        }

        private static string ResolveReactionSpeakerId(DialogueReactionData reaction)
        {
            string speaker = reaction != null ? reaction.Speaker : string.Empty;
            if (string.IsNullOrWhiteSpace(speaker))
            {
                return DialogueLogManager.SpeakerCat;
            }

            switch (speaker.Trim().ToLowerInvariant())
            {
                case "system":
                    return DialogueLogManager.SpeakerSystem;
                case "player":
                    return DialogueLogManager.SpeakerPlayer;
                case "cat":
                case "nekolpos":
                case "neruko":
                case "nekomata":
                    return DialogueLogManager.SpeakerCat;
                default:
                    return speaker.Trim();
            }
        }

        private static string ResolveReactionSpeakerDisplayName(string speakerId)
        {
            switch (speakerId)
            {
                case DialogueLogManager.SpeakerSystem:
                    return "システム";
                case DialogueLogManager.SpeakerPlayer:
                    return "あなた";
                case DialogueLogManager.SpeakerCat:
                    return "{{CAT_NAME}}";
                default:
                    return string.IsNullOrWhiteSpace(speakerId) ? "{{CAT_NAME}}" : speakerId;
            }
        }

        // ============================================
        // ヘルパー
        // ============================================

        private void CancelPendingUiTransitions()
        {
            CancelInvoke();
            if (eventTransitionCoroutine != null)
            {
                StopCoroutine(eventTransitionCoroutine);
                eventTransitionCoroutine = null;
                isEventTransitionRunning = false;
            }

            if (chatUI != null)
            {
                chatUI.OnTypingCompleted -= ReturnToInputStateOnce;
            }
        }

        private bool TryExecuteCall(DialogueReactionData currentData)
        {
            if (currentData == null || currentData.SpeechControl != SpeechControlType.Call)
            {
                return false;
            }

            if (callStack.Count >= MaxCallDepth)
            {
                Debug.LogWarning($"[DialogueManager] Call 深度が上限 {MaxCallDepth} に達したため、target_pattern:{currentData.TargetPatternID} を無視します。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentData.TargetPatternID))
            {
                Debug.LogWarning("[DialogueManager] Call が設定されていますが target_pattern が空です。");
                return false;
            }

            if (!TryResolveBranchPatternGroup(currentData, currentData.TargetPatternID, out List<DialogueReactionData> targetReactions) ||
                targetReactions == null ||
                targetReactions.Count == 0)
            {
                Debug.LogWarning($"[DialogueManager] Call 先 pattern:{currentData.TargetPatternID} が見つかりません。");
                return false;
            }

            DialogueReactionData targetFirstLine = targetReactions[0];
            if (!string.IsNullOrWhiteSpace(currentData.ResponseType) &&
                !string.IsNullOrWhiteSpace(targetFirstLine.ResponseType) &&
                !string.Equals(currentData.ResponseType, targetFirstLine.ResponseType, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[DialogueManager] Call 元/先で response_type が異なります。from:{currentData.ResponseType} to:{targetFirstLine.ResponseType}");
            }

            callStack.Push(new DialogueCallFrame
            {
                Reactions = currentReactions,
                NextLineIndex = currentLineIndex + 1,
                LabelHash = currentPatternLabelHash,
                PatternId = currentPatternId,
                Order = currentData.Order,
                TrackAsLastPattern = currentPatternTracksHistory
            });

            StartPatternPlayback(targetReactions, isRepeatPlayback, clearCallStack: false);
            return true;
        }

        private bool TryExecuteReturn(DialogueReactionData currentData)
        {
            if (currentData == null || currentData.SpeechControl != SpeechControlType.Return)
            {
                return false;
            }

            if (TryReturnFromCallStack())
            {
                return true;
            }

            Debug.LogWarning("[DialogueManager] Return が設定されていますが、Call スタックが空のため復帰できません。");
            return false;
        }

        private bool TryReturnFromCallStack()
        {
            if (callStack.Count <= 0)
            {
                return false;
            }

            DialogueCallFrame frame = callStack.Pop();
            currentReactions = frame.Reactions ?? new List<DialogueReactionData>();
            currentLineIndex = frame.NextLineIndex;
            currentPatternLabelHash = frame.LabelHash;
            currentPatternId = frame.PatternId ?? string.Empty;
            currentPatternTracksHistory = frame.TrackAsLastPattern;

            if (currentReactions.Count == 0)
            {
                ReturnToInputState();
                return true;
            }

            if (currentLineIndex >= currentReactions.Count)
            {
                if (TryReturnFromCallStack())
                {
                    return true;
                }

                FinishDialogue();
                HandleResponseType(currentReactions[currentReactions.Count - 1]);
                return true;
            }

            PlayCurrentLine();
            return true;
        }

        public bool ReplayLastPattern()
        {
            if (lastPattern.TryGetReactions(out List<DialogueReactionData> reactions))
            {
                StartPatternPlayback(reactions, isRepeat: true);
                return true;
            }

            if (lastIsolatedPrompt.TryGetReactions(out List<DialogueReactionData> isolatedPrompt))
            {
                StartPatternPlayback(isolatedPrompt, isRepeat: true);
                return true;
            }

            Debug.Log("[DialogueManager] 聞き直し対象の発言がまだ保存されていません。");
            return false;
        }

        public bool PlayPattern(int labelHash, string patternId, bool isRepeat = false)
        {
            if (DialogueEngine.Instance == null)
            {
                Debug.LogWarning("[DialogueManager] DialogueEngine が未初期化のため Pattern を再生できません。");
                return false;
            }

            if (labelHash == 0 || string.IsNullOrWhiteSpace(patternId))
            {
                Debug.LogWarning("[DialogueManager] Pattern 再生に必要な情報が不足しています。");
                return false;
            }

            if (!DialogueEngine.Instance.TryGetPatternGroup(labelHash, patternId.Trim(), out List<DialogueReactionData> reactions) ||
                reactions == null ||
                reactions.Count == 0)
            {
                Debug.LogWarning($"[DialogueManager] Pattern {patternId} を再生できません。");
                return false;
            }

            StartPatternPlayback(reactions, isRepeat);
            return true;
        }

        private void StartPatternPlayback(List<DialogueReactionData> reactions, bool isRepeat, bool clearCallStack = true)
        {
            if (chatUI == null || reactions == null || reactions.Count == 0)
            {
                return;
            }

            CancelPendingUiTransitions();
            chatUI.EnterDialogueMode();

            currentReactions = reactions;
            currentLineIndex = 0;
            if (clearCallStack)
            {
                callStack.Clear();
            }

            isProcessingDialog = true;
            pendingEmotionChangeApplied = false;
            isRepeatPlayback = isRepeat;

            DialogueReactionData firstReaction = reactions[0];
            currentPatternLabelHash = firstReaction != null ? firstReaction.LabelHash : 0;
            currentPatternId = firstReaction != null && !string.IsNullOrWhiteSpace(firstReaction.PatternID)
                ? firstReaction.PatternID.Trim()
                : string.Empty;
            currentPatternTracksHistory = !isRepeat && ShouldTrackPatternHistory(reactions);

            PlayCurrentLine();
        }

        private bool ShouldTrackPatternHistory(List<DialogueReactionData> reactions)
        {
            if (reactions == null || reactions.Count == 0)
            {
                return false;
            }

            DialogueReactionData firstReaction = reactions[0];
            DialogueReactionData lastReaction = reactions[reactions.Count - 1];
            if (firstReaction == null || string.IsNullOrWhiteSpace(firstReaction.PatternID))
            {
                return false;
            }

            if (string.Equals(firstReaction.IntentID, "GREETING", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !IsRepeatLastPatternAction(lastReaction != null ? lastReaction.ActionId : string.Empty);
        }

        private void RememberCurrentPatternIfNeeded()
        {
            if (isRepeatPlayback || !currentPatternTracksHistory || string.IsNullOrWhiteSpace(currentPatternId))
            {
                return;
            }

            lastPattern.Set(currentPatternLabelHash, currentPatternId, currentReactions);
        }

        private void RememberSingleLineIfNeeded(string patternId, string text, string source)
        {
            if (isRepeatPlayback || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lastPattern.Set(0, patternId, new[]
            {
                new DialogueReactionData
                {
                    PatternID = patternId,
                    IntentID = "CHOICE",
                    SourceRegexLabel = source ?? string.Empty,
                    TextJP = text,
                    ResponseType = "Reaction",
                    Speaker = DialogueLogManager.SpeakerCat
                }
            });
        }

        private bool TryExecuteSpecialAction(string actionId)
        {
            if (TryExecuteConversationTimelineAction(actionId))
            {
                return true;
            }

            if (TryExecuteHomeLocationAction(actionId))
            {
                return true;
            }

            if (!IsRepeatLastPatternAction(actionId))
            {
                return false;
            }

            FinishDialogue();
            if (!repeatLastPatternAction.Execute())
            {
                ReturnToInputState();
            }

            return true;
        }

        private bool TryExecuteConversationTimelineAction(string actionId)
        {
            const string prefix = "play_conversation_timeline:";
            if (string.IsNullOrWhiteSpace(actionId) ||
                !actionId.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string timelineId = actionId.Trim().Substring(prefix.Length).Trim();
            OpenBetaTitleBootstrap bootstrap = FindFirstObjectByType<OpenBetaTitleBootstrap>(FindObjectsInactive.Include);
            if (bootstrap == null)
            {
                Debug.LogWarning($"[DialogueManager] 会話Timeline '{timelineId}' を再生できません。OpenBetaTitleBootstrapが見つかりません。", this);
                ReturnToInputState();
                return true;
            }

            if (!bootstrap.TryPlayConversationTimeline(timelineId))
            {
                ReturnToInputState();
                return true;
            }

            FinishDialogue();
            chatUI?.ClearDialogueDisplay();
            chatUI?.HideChoices();
            chatUI?.RestoreNormalConversationInputMode();
            return true;
        }

        private bool TryExecuteHomeLocationAction(string actionId)
        {
            const string prefix = "move_home:";
            if (string.IsNullOrWhiteSpace(actionId) ||
                !actionId.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string locationId = actionId.Trim().Substring(prefix.Length).Trim();
            if (!Enum.TryParse(locationId, false, out CatHomeLocation location) ||
                !Enum.IsDefined(typeof(CatHomeLocation), location))
            {
                Debug.LogWarning($"[DialogueManager] 未定義の基本会話地点ID '{locationId}' が action_id に指定されています。", this);
                ReturnToInputState();
                return true;
            }

            OpenBetaTitleBootstrap bootstrap = FindFirstObjectByType<OpenBetaTitleBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null)
            {
                bootstrap.SetHomeLocation(location);
            }
            else
            {
                CatPresentationModeController presentation = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
                if (presentation == null)
                {
                    Debug.LogWarning($"[DialogueManager] 基本会話地点 '{location}' への移動先が見つかりません。", this);
                    ReturnToInputState();
                    return true;
                }

                presentation.SetHomeLocation(location);
            }

            FinishDialogue();
            return true;
        }

        private static bool IsRepeatLastPatternAction(string actionId)
        {
            return string.Equals(actionId?.Trim(), "RepeatLastPattern", StringComparison.OrdinalIgnoreCase);
        }

        private static List<DialogueReactionData> CloneReactionGroup(IReadOnlyList<DialogueReactionData> source)
        {
            List<DialogueReactionData> clone = new List<DialogueReactionData>(source != null ? source.Count : 0);
            if (source == null)
            {
                return clone;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DialogueReactionData reaction = CloneReaction(source[i]);
                if (reaction != null)
                {
                    clone.Add(reaction);
                }
            }

            return clone;
        }

        private static DialogueReactionData CloneReaction(DialogueReactionData source)
        {
            if (source == null)
            {
                return null;
            }

            return new DialogueReactionData
            {
                LabelHash = source.LabelHash,
                PatternID = source.PatternID,
                SpeechControl = source.SpeechControl,
                Order = source.Order,
                RepeatCount = source.RepeatCount,
                RandomRepeatLimit = source.RandomRepeatLimit,
                TargetPatternID = source.TargetPatternID,
                CallOnly = source.CallOnly,
                ChoiceYesPatternID = source.ChoiceYesPatternID,
                ChoiceNoPatternID = source.ChoiceNoPatternID,
                SequenceProgressHold = source.SequenceProgressHold,
                IntentID = source.IntentID,
                ReactionType = source.ReactionType,
                Topic = source.Topic,
                SourceRegexLabel = source.SourceRegexLabel,
                ChoiceQuestionJa = source.ChoiceQuestionJa,
                ChoiceYesLabel = source.ChoiceYesLabel,
                ChoiceNoLabel = source.ChoiceNoLabel,
                ActionId = source.ActionId,
                TimedEventKey = source.TimedEventKey,
                Speaker = source.Speaker,
                NextAction = source.NextAction,
                TextJP = source.TextJP,
                ResponseType = source.ResponseType,
                Condition = source.Condition,
                WaitTime = source.WaitTime,
                HideUI = source.HideUI,
                Animation = source.Animation,
                EmotionChangeType = source.EmotionChangeType,
                EmotionChangeValue = source.EmotionChangeValue,
                BranchResolver = source.BranchResolver,
                RuntimePlaceholders = CloneRuntimePlaceholders(source.RuntimePlaceholders),
                TeachingCategory = source.TeachingCategory,
                TeachingEntryId = source.TeachingEntryId,
                TeachingSeriesGroupId = source.TeachingSeriesGroupId,
                TeachingDisplayName = source.TeachingDisplayName,
                QuestionLinks = CloneQuestionLinks(source.QuestionLinks)
            };
        }

        private static Dictionary<string, string> CloneRuntimePlaceholders(IReadOnlyDictionary<string, string> source)
        {
            Dictionary<string, string> clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, string> pair in source)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    clone[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            return clone;
        }

        private static List<DialogueLogQuestionLink> CloneQuestionLinks(IReadOnlyList<DialogueLogQuestionLink> source)
        {
            List<DialogueLogQuestionLink> clone = new List<DialogueLogQuestionLink>(source != null ? source.Count : 0);
            if (source == null)
            {
                return clone;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DialogueLogQuestionLink link = source[i];
                if (link != null)
                {
                    clone.Add(new DialogueLogQuestionLink(link.keyword, link.insertText));
                }
            }

            return clone;
        }
    }
}
