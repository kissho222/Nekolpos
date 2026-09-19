using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nekolpos.Audio;
using Nekolpos.StatusSystem;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using TMPro;
using UnityEngine;

namespace Nekolpos.ActionSystem
{
    public readonly struct ActionRequest
    {
        public ActionRequest(string triggerId, string sourceDialogue, bool suppressTimeTransitionGreeting = false)
        {
            TriggerId = triggerId;
            SourceDialogue = sourceDialogue;
            SuppressTimeTransitionGreeting = suppressTimeTransitionGreeting;
        }

        public string TriggerId { get; }

        public string SourceDialogue { get; }

        public bool SuppressTimeTransitionGreeting { get; }
    }

    [Serializable]
    public sealed class ActionChoiceSet
    {
        public string triggerId;
        public string prompt = "どう過ごす？";
        public List<ActionData> actions = new List<ActionData>();
        public string continueConversationLabel = "雑談を続ける";
    }

    [Serializable]
    public sealed class ActionChoiceYarnNode
    {
        public string triggerId;
        public string nodeName;
    }

    public enum ActionFlowResult
    {
        Skipped,
        Executed
    }

    public class ActionManager : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private DialogueManager dialogueManager;
        [SerializeField] private YarnManager yarnManager;
        [SerializeField] private ActionChoiceUI choiceUI;
        [SerializeField] private ActionExecutor actionExecutor;
        [SerializeField] private ResultTextUI resultTextUI;
        [SerializeField] private FadeController fadeController;
        [SerializeField] private TimeManager timeManager;

        [Header("Configuration")]
        [SerializeField] private List<ActionChoiceSet> choiceSets = new List<ActionChoiceSet>();
        [SerializeField] private bool preferYarnChoices;
        [SerializeField] private List<ActionChoiceYarnNode> yarnChoiceNodes = new List<ActionChoiceYarnNode>();
        [SerializeField] [Min(0.5f)] private float resultDisplaySeconds = 2.8f;
        [SerializeField] private string defaultPrompt = "どう過ごす？";
        [SerializeField] private string defaultContinueLabel = "雑談を続ける";

        private readonly Dictionary<string, ActionChoiceSet> choiceSetLookup = new Dictionary<string, ActionChoiceSet>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> yarnChoiceNodeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ActionData> actionLookup = new Dictionary<string, ActionData>(StringComparer.OrdinalIgnoreCase);
        private ActionChoiceSet fallbackChoiceSet;
        private bool isInitialized;
        private bool isRunning;
        private bool hasLastTimeResult;
        private TimeAdvanceResult lastTimeResult;

        public bool TryGetLastTimeResult(out TimeAdvanceResult timeResult)
        {
            timeResult = lastTimeResult;
            return hasLastTimeResult;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public async UniTask<ActionFlowResult> RunActionFlowAsync(ActionRequest request)
        {
            EnsureInitialized();
            chatUI?.HideChoices();

            if (preferYarnChoices && TryResolveYarnChoiceNode(request.TriggerId, out string yarnNodeName))
            {
                await yarnManager.StartNodeAsync(yarnNodeName);
                return ActionFlowResult.Executed;
            }

            if (isRunning)
            {
                return ActionFlowResult.Skipped;
            }

            ActionChoiceSet choiceSet = ResolveChoiceSet(request.TriggerId);
            if (choiceSet == null || choiceSet.actions == null || choiceSet.actions.Count == 0)
            {
                return ActionFlowResult.Skipped;
            }

            EnsureChoiceUiInitialized();
            isRunning = true;
            try
            {
                ActionChoiceResult choice = await choiceUI.ShowChoicesAsync(
                    string.IsNullOrWhiteSpace(choiceSet.prompt) ? defaultPrompt : choiceSet.prompt,
                    choiceSet.actions,
                    string.IsNullOrWhiteSpace(choiceSet.continueConversationLabel) ? defaultContinueLabel : choiceSet.continueConversationLabel);

                if (choice.ContinueConversation || choice.ActionData == null)
                {
                    return ActionFlowResult.Skipped;
                }

                await ExecuteActionDataAsync(
                    choice.ActionData,
                    request.TriggerId,
                    request.SourceDialogue,
                    request.SuppressTimeTransitionGreeting);
                return ActionFlowResult.Executed;
            }
            finally
            {
                choiceUI.HideInstant();
                isRunning = false;
            }
        }

        public async UniTask<bool> ExecuteActionByIdAsync(string actionId, string sourceDialogue)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            if (!actionLookup.TryGetValue(actionId.Trim(), out ActionData actionData) || actionData == null)
            {
                Debug.LogWarning($"[ActionManager] action_id {actionId} に対応する ActionData が見つかりません。");
                return false;
            }

            await ExecuteActionDataAsync(actionData, actionId.Trim(), sourceDialogue, false);
            return true;
        }

        private void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            chatUI = chatUI != null ? chatUI : GetComponent<ChatUIController>();
            dialogueManager = dialogueManager != null ? dialogueManager : GetComponent<DialogueManager>();
            yarnManager = yarnManager != null ? yarnManager : GetComponent<YarnManager>() ?? FindFirstObjectByType<YarnManager>();
            choiceUI = choiceUI != null ? choiceUI : GetComponent<ActionChoiceUI>();
            actionExecutor = actionExecutor != null ? actionExecutor : GetComponent<ActionExecutor>() ?? gameObject.AddComponent<ActionExecutor>();
            resultTextUI = resultTextUI != null ? resultTextUI : GetComponent<ResultTextUI>() ?? gameObject.AddComponent<ResultTextUI>();
            fadeController = fadeController != null ? fadeController : GetComponent<FadeController>() ?? gameObject.AddComponent<FadeController>();
            timeManager = timeManager != null ? timeManager : GetComponent<TimeManager>() ?? gameObject.AddComponent<TimeManager>();

            Canvas runtimeCanvas = chatUI != null && chatUI.messageText != null ? chatUI.messageText.canvas : null;
            TMP_FontAsset runtimeFont = chatUI != null && chatUI.messageText != null ? chatUI.messageText.font : TMP_Settings.defaultFontAsset;

            resultTextUI.Initialize(runtimeCanvas, runtimeFont);
            fadeController.Initialize(runtimeCanvas);

            if (choiceSets == null || choiceSets.Count == 0)
            {
                choiceSets = CreateDefaultChoiceSets();
            }

            if (yarnChoiceNodes == null || yarnChoiceNodes.Count == 0)
            {
                yarnChoiceNodes = CreateDefaultYarnChoiceNodes();
            }

            RebuildLookup();
            isInitialized = true;
        }

        private void EnsureChoiceUiInitialized()
        {
            if (choiceUI == null)
            {
                choiceUI = GetComponent<ActionChoiceUI>() ?? gameObject.AddComponent<ActionChoiceUI>();
            }

            Canvas runtimeCanvas = chatUI != null && chatUI.messageText != null ? chatUI.messageText.canvas : null;
            TMP_FontAsset runtimeFont = chatUI != null && chatUI.messageText != null ? chatUI.messageText.font : TMP_Settings.defaultFontAsset;
            choiceUI.Initialize(runtimeCanvas, runtimeFont);
        }

        private async UniTask PlayTimeTransitionGreetingAsync(TimeAdvanceResult timeResult)
        {
            if (dialogueManager == null || timeManager == null)
            {
                return;
            }

            if (!timeManager.TryGetTransitionGreeting(timeResult, out string greeting))
            {
                return;
            }

            await dialogueManager.PlayIsolatedGreetingAsync(greeting);
        }

        private async UniTask ExecuteActionDataAsync(
            ActionData actionData,
            string triggerId,
            string sourceDialogue,
            bool suppressTimeTransitionGreeting)
        {
            bool interactionPurrEnabled = IsTouchInteractionAction(actionData, triggerId);
            hasLastTimeResult = false;
            chatUI?.SetUiSuppressed(true);
            chatUI?.ClearDialogueDisplay();
            chatUI?.HideChoices();
            try
            {
                SetPurrInteractionActive(interactionPurrEnabled);
                await fadeController.FadeOutAsync();

                ActionExecutionResult result = await actionExecutor.ExecuteAsync(
                    actionData,
                    new ActionExecutionContext(triggerId, sourceDialogue));
                lastTimeResult = result.TimeResult;
                hasLastTimeResult = true;

                fadeController.BringToFront();
                resultTextUI.BringToFront();
                string resultMessage = dialogueManager != null
                    ? dialogueManager.FormatRuntimeText(result.Message)
                    : result.Message;
                await resultTextUI.ShowResultAsync(resultMessage, resultDisplaySeconds);
                await fadeController.FadeInAsync();
                chatUI?.SetUiSuppressed(false);
                if (!suppressTimeTransitionGreeting)
                {
                    await PlayTimeTransitionGreetingAsync(result.TimeResult);
                }
            }
            finally
            {
                SetPurrInteractionActive(false);
                chatUI?.SetUiSuppressed(false);
            }
        }

        private static bool IsTouchInteractionAction(ActionData actionData, string triggerId)
        {
            if (string.Equals(triggerId?.Trim(), "Petting", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string actionId = actionData != null ? actionData.ActionId?.Trim() : string.Empty;
            return string.Equals(actionId, "gentle_petting", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(actionId, "brush_together", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(actionId, "brushing", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(actionId, "nap_after_petting", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetPurrInteractionActive(bool active)
        {
            CatPurrAudioController[] controllers =
                FindObjectsByType<CatPurrAudioController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].SetInteractionActive(active);
            }
        }

        private void RebuildLookup()
        {
            choiceSetLookup.Clear();
            yarnChoiceNodeLookup.Clear();
            actionLookup.Clear();
            fallbackChoiceSet = null;

            if (choiceSets == null)
            {
                choiceSets = new List<ActionChoiceSet>();
            }

            for (int i = 0; i < choiceSets.Count; i++)
            {
                ActionChoiceSet choiceSet = choiceSets[i];
                if (choiceSet == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(choiceSet.triggerId))
                {
                    fallbackChoiceSet = choiceSet;
                    continue;
                }

                choiceSetLookup[choiceSet.triggerId.Trim()] = choiceSet;

                if (choiceSet.actions == null)
                {
                    continue;
                }

                for (int j = 0; j < choiceSet.actions.Count; j++)
                {
                    ActionData actionData = choiceSet.actions[j];
                    if (actionData == null || string.IsNullOrWhiteSpace(actionData.ActionId))
                    {
                        continue;
                    }

                    actionLookup[actionData.ActionId.Trim()] = actionData;
                }
            }

            if (yarnChoiceNodes == null)
            {
                return;
            }

            for (int i = 0; i < yarnChoiceNodes.Count; i++)
            {
                ActionChoiceYarnNode mapping = yarnChoiceNodes[i];
                if (mapping == null ||
                    string.IsNullOrWhiteSpace(mapping.triggerId) ||
                    string.IsNullOrWhiteSpace(mapping.nodeName))
                {
                    continue;
                }

                yarnChoiceNodeLookup[mapping.triggerId.Trim()] = mapping.nodeName.Trim();
            }
        }

        private ActionChoiceSet ResolveChoiceSet(string triggerId)
        {
            if (!string.IsNullOrWhiteSpace(triggerId) &&
                choiceSetLookup.TryGetValue(triggerId.Trim(), out ActionChoiceSet choiceSet))
            {
                return choiceSet;
            }

            return fallbackChoiceSet;
        }

        private bool TryResolveYarnChoiceNode(string triggerId, out string nodeName)
        {
            nodeName = string.Empty;
            if (yarnManager == null || string.IsNullOrWhiteSpace(triggerId) || IsBuiltInActionChoiceTrigger(triggerId))
            {
                return false;
            }

            return yarnChoiceNodeLookup.TryGetValue(triggerId.Trim(), out nodeName) &&
                   !string.IsNullOrWhiteSpace(nodeName);
        }

        private static bool IsBuiltInActionChoiceTrigger(string triggerId)
        {
            string normalized = triggerId?.Trim();
            return string.Equals(normalized, "Play_Game", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Petting", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Punch", StringComparison.OrdinalIgnoreCase);
        }

        private List<ActionChoiceSet> CreateDefaultChoiceSets()
        {
            List<ActionChoiceSet> sets = new List<ActionChoiceSet>();

            ActionChoiceSet playGame = new ActionChoiceSet
            {
                triggerId = "Play_Game",
                prompt = "猫又と何をして過ごす？",
                continueConversationLabel = "雑談を続ける",
                actions = new List<ActionData>
                {
                    CreateRuntimeAction(
                        "ball_play",
                        "ボール遊びする",
                        SystemTimedEventCatalog.ActionBallPlayResultKey,
                        "猫又とボール遊びをして過ごした。\n完成版では、実際に遊ぶことができます！",
                        120,
                        new StatEffect(StatusType.Affection, 1)),
                    CreateRuntimeAction(
                        "nap_together",
                        "一緒に昼寝する",
                        SystemTimedEventCatalog.ActionNapTogetherResultKey,
                        "猫又と一緒に昼寝して過ごした。\nとても暖かかった。",
                        120,
                        new StatEffect(StatusType.Affection, 1),
                        new StatEffect(StatusType.Concern, -1)),
                    CreateRuntimeAction(
                        "brushing",
                        "ブラッシングする",
                        SystemTimedEventCatalog.ActionBrushingResultKey,
                        "毛並みを整えながら、ゆっくり触れ合って過ごした。\n猫又は気持ちよさそうに目を細めていた。",
                        120,
                        new StatEffect(StatusType.Affection, 1))
                }
            };

            ActionChoiceSet coolDown = new ActionChoiceSet
            {
                triggerId = "Punch",
                prompt = "空気が張りつめた。どうする？",
                continueConversationLabel = "少し黙る",
                actions = new List<ActionData>
                {
                    CreateRuntimeAction(
                        "step_back",
                        "距離を取る",
                        SystemTimedEventCatalog.ActionStepBackResultKey,
                        "少し距離を置いて、気まずい空気が落ち着くのを待った。",
                        120,
                        new StatEffect(StatusType.Hostility, -1)),
                    CreateRuntimeAction(
                        "apologize",
                        "素直に謝る",
                        SystemTimedEventCatalog.ActionApologizeResultKey,
                        "ぎこちなく謝ると、猫又はすぐには笑わなかったが、話は聞いてくれた。",
                        120,
                        new StatEffect(StatusType.Hostility, -1),
                        new StatEffect(StatusType.Concern, 1))
                }
            };

            ActionChoiceSet petting = new ActionChoiceSet
            {
                triggerId = "Petting",
                prompt = "そっと触れ合う時間にする？",
                continueConversationLabel = "そのまま話す",
                actions = new List<ActionData>
                {
                    CreateRuntimeAction(
                        "gentle_petting",
                        "ゆっくり撫でる",
                        SystemTimedEventCatalog.ActionGentlePettingResultKey,
                        "猫又をゆっくり撫でながら、静かな時間を一緒に過ごした。\n少しずつ肩の力が抜けていった。",
                        120,
                        new StatEffect(StatusType.Affection, 1),
                        new StatEffect(StatusType.Concern, -1)),
                    CreateRuntimeAction(
                        "brush_together",
                        "ブラッシングする",
                        SystemTimedEventCatalog.ActionBrushingResultKey,
                        "毛並みを整えながら、ゆっくり触れ合って過ごした。\n猫又は気持ちよさそうに目を細めていた。",
                        120,
                        new StatEffect(StatusType.Affection, 1)),
                    CreateRuntimeAction(
                        "nap_after_petting",
                        "そのまま一緒に昼寝する",
                        SystemTimedEventCatalog.ActionNapAfterPettingResultKey,
                        "撫でているうちに眠くなって、猫又と寄り添って少し休んだ。",
                        120,
                        new StatEffect(StatusType.Affection, 1),
                        new StatEffect(StatusType.Concern, -1))
                }
            };

            ActionChoiceSet fallback = new ActionChoiceSet
            {
                triggerId = string.Empty,
                prompt = defaultPrompt,
                continueConversationLabel = defaultContinueLabel,
                actions = new List<ActionData>
                {
                    playGame.actions[0],
                    playGame.actions[1]
                }
            };

            sets.Add(playGame);
            sets.Add(petting);
            sets.Add(coolDown);
            sets.Add(fallback);
            return sets;
        }

        private List<ActionChoiceYarnNode> CreateDefaultYarnChoiceNodes()
        {
            return new List<ActionChoiceYarnNode>
            {
                new ActionChoiceYarnNode { triggerId = "Petting", nodeName = "ActionChoice_Petting" },
                new ActionChoiceYarnNode { triggerId = "Punch", nodeName = "ActionChoice_Punch" },
            };
        }

        private static ActionData CreateRuntimeAction(
            string actionId,
            string displayName,
            string timedEventKey,
            string fallbackResultText,
            int timeCost,
            params StatEffect[] effects)
        {
            ActionData actionData = ScriptableObject.CreateInstance<ActionData>();
            actionData.hideFlags = HideFlags.DontSave;
            actionData.Initialize(actionId, displayName, fallbackResultText, timeCost, effects, timedEventKey);
            return actionData;
        }
    }
}
