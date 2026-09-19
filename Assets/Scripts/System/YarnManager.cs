using System;
using System.Collections.Generic;
using System.Reflection;
using Backgammon.Conversation;
using Nekolpos.CameraSystem;
using Nekolpos.Data;
using Nekolpos.ActionSystem;
using Nekolpos.StatusSystem;
using Nekolpos.TimeSystem;
using UnityEngine;
using UnityEngine.Events;
using Yarn.Unity;
using Cysharp.Threading.Tasks;

namespace Nekolpos.System
{
    /// <summary>
    /// Yarn Spinner を既存会話システムへ差し込むための進行管理ハブ。
    /// </summary>
    public sealed class YarnManager : MonoBehaviour
    {
        [Serializable]
        public struct IntentNodeMapping
        {
            public string intentId;
            public string nodeName;
        }

        [Header("Core References")]
        [SerializeField] private DialogueRunner dialogueRunner;
        [SerializeField] private YarnProject yarnProject;
        [SerializeField] private ExistingDialogueUIBridge dialogueBridge;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private ActionManager actionManager;

        [Header("State Sources")]
        [SerializeField] private CatDataSO catData;
        [SerializeField] private ConversationGameStateManager conversationGameStateManager;
        [SerializeField] private ConversationDataManager conversationDataManager;
        [SerializeField] private GameFlagManager gameFlagManager;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private StatusManager statusManager;

        [Header("Behavior")]
        [SerializeField] private bool autoWireDialogueRunner = true;
        [SerializeField] private bool verboseLogging;
        [SerializeField] private string morningGreetingNodeName = "MorningGreeting";
        [SerializeField] private List<IntentNodeMapping> intentNodeMappings = new List<IntentNodeMapping>();

        [Header("Events")]
        [SerializeField] private UnityEvent onDialogueComplete;

        private readonly Dictionary<string, string> stateVariableMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private DialogueRunner registeredCommandRunner;
        private bool ownsRunActionCommand;
        private bool ownsFocusCommands;
        private string currentNodeName = string.Empty;
        private StatusManager subscribedStatusManager;

        public DialogueRunner DialogueRunner => dialogueRunner;
        public string CurrentNodeName => currentNodeName;

        public VariableStorageBehaviour VariableStorage => dialogueRunner != null ? dialogueRunner.VariableStorage : null;

        public void SetYarnProject(YarnProject project)
        {
            yarnProject = project;
            ApplyYarnProjectToRunner();
        }

        private void Awake()
        {
            ResolveReferences();
            BindStatusManager();
            EnsureRunnerConfigured();
            RegisterLocaleEvents();
            ApplyConversationLocaleToRunner();
            RegisterCommands();
            RegisterRunnerEvents();
        }

        private void OnDestroy()
        {
            UnbindStatusManager();
            UnregisterCommands();
            UnregisterRunnerEvents();
            UnregisterLocaleEvents();
        }

        public void StartNode(string nodeName)
        {
            StartNodeAsync(nodeName).Forget();
        }

        public async YarnTask StartNodeAsync(string nodeName)
        {
            ResolveReferences();
            EnsureRunnerConfigured();
            ApplyConversationLocaleToRunner();

            string trimmedNodeName = nodeName?.Trim();

            if (dialogueRunner == null || string.IsNullOrWhiteSpace(trimmedNodeName))
            {
                return;
            }

            if (dialogueRunner.IsDialogueRunning)
            {
                await dialogueRunner.Stop();
            }

            if (!EnsureProjectReady(trimmedNodeName))
            {
                return;
            }

            SyncVariablesFromGameStateToYarn();

            if (verboseLogging)
            {
                Debug.Log($"[{nameof(YarnManager)}] StartNode: {trimmedNodeName}", this);
            }

            try
            {
                currentNodeName = trimmedNodeName;
                await dialogueRunner.StartDialogue(trimmedNodeName);
            }
            catch (Exception exception)
            {
                currentNodeName = string.Empty;
                Debug.LogError(
                    $"[{nameof(YarnManager)}] Failed to start node '{trimmedNodeName}'. {DescribeProjectState(dialogueRunner.YarnProject)}\n{exception}",
                    this);
            }
        }

        public void StopDialogue()
        {
            if (dialogueRunner == null || !dialogueRunner.IsDialogueRunning)
            {
                return;
            }

            dialogueRunner.Stop().Forget();
        }

        public void SetVariable(string key, float value)
        {
            if (VariableStorage == null)
            {
                return;
            }

            VariableStorage.SetValue(NormalizeVariableName(key), value);
        }

        public float GetVariable(string key)
        {
            if (VariableStorage == null)
            {
                return 0f;
            }

            if (VariableStorage.TryGetValue<float>(NormalizeVariableName(key), out float value))
            {
                return value;
            }

            return 0f;
        }

        public bool TryStartMappedNode(string intentId)
        {
            if (string.IsNullOrWhiteSpace(intentId))
            {
                return false;
            }

            for (int i = 0; i < intentNodeMappings.Count; i++)
            {
                IntentNodeMapping mapping = intentNodeMappings[i];
                if (string.Equals(mapping.intentId, intentId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(mapping.nodeName))
                {
                    StartNode(mapping.nodeName);
                    return true;
                }
            }

            return false;
        }

        [ContextMenu("Start Morning Greeting")]
        private void StartMorningGreeting()
        {
            if (!string.IsNullOrWhiteSpace(morningGreetingNodeName))
            {
                StartNode(morningGreetingNodeName);
            }
        }

        public void SyncVariablesFromGameStateToYarn()
        {
            if (VariableStorage == null)
            {
                return;
            }

            stateVariableMap.Clear();

            if (catData != null)
            {
                SetStringInternal("$catName", SanitizeRenameValue(catData.catName));
                SetStringInternal("$catPronoun", SanitizeRenameValue(catData.catPronoun));
                SetStringInternal("$playerName", SanitizeRenameValue(catData.playerName));
                SetStringInternal("$playerCalling", SanitizeRenameValue(catData.playerCalling));
            }

            SyncPsychologicalVariablesToYarn();

            if (conversationGameStateManager != null)
            {
                foreach (KeyValuePair<string, ConversationGameStateValue> pair in conversationGameStateManager.State.Values)
                {
                    string variableName = RegisterStateVariable(pair.Key);
                    switch (pair.Value.kind)
                    {
                        case ConversationGameStateValueKind.Integer:
                            SetFloatInternal(variableName, pair.Value.intValue);
                            break;
                        case ConversationGameStateValueKind.Boolean:
                            SetBoolInternal(variableName, pair.Value.boolValue);
                            break;
                        case ConversationGameStateValueKind.String:
                            SetStringInternal(variableName, pair.Value.stringValue);
                            break;
                    }
                }
            }

            string resolvedPhase = ResolveCurrentPhase();
            if (!string.IsNullOrWhiteSpace(resolvedPhase))
            {
                SetStringInternal("$timePhase", resolvedPhase);
                SetStringInternal("$phase", resolvedPhase);
            }

            SyncWeatherVariablesFromTimeManager();

            if (gameFlagManager != null)
            {
                foreach (KeyValuePair<string, bool> pair in gameFlagManager.GetAllFlags())
                {
                    SetBoolInternal(BuildFlagVariableName(pair.Key), pair.Value);
                }
            }
        }

        public void SyncVariablesFromYarnToGameState()
        {
            if (VariableStorage == null)
            {
                return;
            }

            if (catData != null)
            {
                bool namingChanged = false;
                namingChanged |= TryReadString("$catName", catData.catName, out catData.catName);
                namingChanged |= TryReadString("$catPronoun", catData.catPronoun, out catData.catPronoun);
                namingChanged |= TryReadString("$playerName", catData.playerName, out catData.playerName);
                namingChanged |= TryReadString("$playerCalling", catData.playerCalling, out catData.playerCalling);
                if (namingChanged)
                {
                    RenameSystem.Save(catData);
                }
            }

            SyncPsychologicalVariablesFromYarn();

            if (conversationGameStateManager != null)
            {
                ConversationGameState state = conversationGameStateManager.State;
                foreach (KeyValuePair<string, string> pair in stateVariableMap)
                {
                    string variableName = pair.Key;
                    string stateKey = pair.Value;

                    if (TryResolveStatusTypeFromStateKey(stateKey, out _))
                    {
                        continue;
                    }

                    if (VariableStorage.TryGetValue<float>(variableName, out float floatValue))
                    {
                        state.SetInt(stateKey, Mathf.RoundToInt(floatValue));
                        continue;
                    }

                    if (VariableStorage.TryGetValue<bool>(variableName, out bool boolValue))
                    {
                        state.SetBool(stateKey, boolValue);
                        continue;
                    }

                    if (VariableStorage.TryGetValue<string>(variableName, out string stringValue))
                    {
                        state.SetString(stateKey, stringValue);
                    }
                }

                if (VariableStorage.TryGetValue<string>("$timePhase", out string phaseValue) && !string.IsNullOrWhiteSpace(phaseValue))
                {
                    state.SetString("Phase", phaseValue);
                }
            }

            if (gameFlagManager != null && VariableStorage is InMemoryVariableStorage inMemoryVariableStorage)
            {
                foreach (KeyValuePair<string, object> pair in inMemoryVariableStorage)
                {
                    if (!pair.Key.StartsWith("$flag_", StringComparison.Ordinal) || pair.Value is not bool flagValue)
                    {
                        continue;
                    }

                    string flagName = pair.Key.Substring("$flag_".Length);
                    if (!string.IsNullOrWhiteSpace(flagName))
                    {
                        gameFlagManager.SetFlag(flagName, flagValue);
                    }
                }
            }
        }

        private void ResolveReferences()
        {
            if (dialogueRunner == null)
            {
                dialogueRunner = FindFirstObjectByType<DialogueRunner>();
            }

            if (yarnProject == null && dialogueRunner != null)
            {
                yarnProject = dialogueRunner.YarnProject;
            }

            if (chatUI == null)
            {
                chatUI = FindFirstObjectByType<ChatUIController>();
            }

            if (actionManager == null)
            {
                actionManager = FindFirstObjectByType<ActionManager>();
            }

            if (dialogueBridge == null && dialogueRunner != null)
            {
                dialogueBridge = dialogueRunner.GetComponent<ExistingDialogueUIBridge>();
            }

            if (catData == null)
            {
                DialogueManager dialogueManager = FindFirstObjectByType<DialogueManager>();
                if (dialogueManager != null)
                {
                    catData = dialogueManager.catData;
                }
            }

            if (conversationGameStateManager == null)
            {
                conversationGameStateManager = FindFirstObjectByType<ConversationGameStateManager>();
            }

            if (conversationDataManager == null)
            {
                conversationDataManager = FindFirstObjectByType<ConversationDataManager>();
            }

            if (gameFlagManager == null)
            {
                gameFlagManager = FindFirstObjectByType<GameFlagManager>();
            }

            if (gameManager == null)
            {
                gameManager = GameManager.Instance != null
                    ? GameManager.Instance
                    : FindFirstObjectByType<GameManager>();
            }

            if (timeManager == null)
            {
                timeManager = FindFirstObjectByType<TimeManager>();
            }

            if (statusManager == null)
            {
                statusManager = FindFirstObjectByType<StatusManager>();
            }

            BindStatusManager();
        }

        private void BindStatusManager()
        {
            if (ReferenceEquals(subscribedStatusManager, statusManager))
            {
                return;
            }

            UnbindStatusManager();
            subscribedStatusManager = statusManager;
            if (subscribedStatusManager != null)
            {
                subscribedStatusManager.OnStatusChangeApplied += HandleStatusChangeApplied;
            }
        }

        private void UnbindStatusManager()
        {
            if (subscribedStatusManager != null)
            {
                subscribedStatusManager.OnStatusChangeApplied -= HandleStatusChangeApplied;
                subscribedStatusManager = null;
            }
        }

        private void HandleStatusChangeApplied(StatusChangeResult _)
        {
            SyncPsychologicalVariablesToYarn();
        }

        private void SyncPsychologicalVariablesToYarn()
        {
            if (statusManager == null || VariableStorage == null)
            {
                return;
            }

            SetFloatInternal("$love", statusManager.GetValue(StatusType.Affection));
            SetFloatInternal("$affection", statusManager.GetValue(StatusType.Affection));
            SetFloatInternal("$sadistic", statusManager.GetValue(StatusType.Sadistic));
            SetFloatInternal("$concern", statusManager.GetValue(StatusType.Concern));
            SetFloatInternal("$hostility", statusManager.GetValue(StatusType.Hostility));
            SetFloatInternal("$submission", statusManager.GetValue(StatusType.Obedience));
            SetFloatInternal("$obedience", statusManager.GetValue(StatusType.Obedience));
            SetFloatInternal("$instinct", statusManager.GetValue(StatusType.Instinct));
        }

        private void SyncPsychologicalVariablesFromYarn()
        {
            if (statusManager == null || VariableStorage == null)
            {
                return;
            }

            SyncStatusValueFromYarn(StatusType.Affection, "$love", "$affection");
            SyncStatusValueFromYarn(StatusType.Sadistic, "$sadistic");
            SyncStatusValueFromYarn(StatusType.Concern, "$concern");
            SyncStatusValueFromYarn(StatusType.Hostility, "$hostility");
            SyncStatusValueFromYarn(StatusType.Obedience, "$submission", "$obedience");
            SyncStatusValueFromYarn(StatusType.Instinct, "$instinct");
        }

        private void SyncStatusValueFromYarn(StatusType statusType, params string[] variableNames)
        {
            for (int i = 0; i < variableNames.Length; i++)
            {
                if (VariableStorage.TryGetValue<float>(variableNames[i], out float value))
                {
                    statusManager.SetValue(statusType, Mathf.RoundToInt(value), "Yarn", currentNodeName);
                    return;
                }
            }
        }

        private static bool TryResolveStatusTypeFromStateKey(string key, out StatusType statusType)
        {
            switch (key?.Trim().ToLowerInvariant())
            {
                case "affection": statusType = StatusType.Affection; return true;
                case "sadistic":
                case "sadism": statusType = StatusType.Sadistic; return true;
                case "concern":
                case "anxiety": statusType = StatusType.Concern; return true;
                case "hostility": statusType = StatusType.Hostility; return true;
                case "obedience":
                case "submission": statusType = StatusType.Obedience; return true;
                case "instinct": statusType = StatusType.Instinct; return true;
                default: statusType = default; return false;
            }
        }

        private void SyncWeatherVariablesFromTimeManager()
        {
            if (timeManager == null)
            {
                timeManager = FindFirstObjectByType<TimeManager>();
            }

            if (timeManager == null)
            {
                return;
            }

            SetStringInternal("$Weather", WeatherSystem.ToDisplayText(timeManager.Weather));
            SetStringInternal("$CurrentWeather", timeManager.Weather.ToString());
            SetStringInternal("$TomorrowWeather", WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            SetStringInternal("$CurrentWeatherForecast", timeManager.TomorrowWeather.ToString());
            SetStringInternal("$FORECAST_WEATHER", WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            SetStringInternal("$FORCAST_WEATHER", WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            SetStringInternal("$NextActualWeather", timeManager.NextActualWeather.ToString());
            SetBoolInternal("$WeatherForecastCorrect", timeManager.ForecastCorrect);
        }

        private void EnsureRunnerConfigured()
        {
            if (!autoWireDialogueRunner || dialogueRunner == null)
            {
                return;
            }

            ApplyYarnProjectToRunner();

            if (dialogueBridge == null)
            {
                dialogueBridge = dialogueRunner.GetComponent<ExistingDialogueUIBridge>();
                if (dialogueBridge == null)
                {
                    dialogueBridge = dialogueRunner.gameObject.AddComponent<ExistingDialogueUIBridge>();
                }
            }

            if (chatUI != null)
            {
                dialogueBridge.Bind(chatUI);
            }

            List<DialoguePresenterBase> presenters = new List<DialoguePresenterBase>();
            if (dialogueBridge != null)
            {
                presenters.Add(dialogueBridge);
            }

            dialogueRunner.DialoguePresenters = presenters;
            _ = dialogueRunner.VariableStorage;
        }

        private void ApplyYarnProjectToRunner()
        {
            if (dialogueRunner == null || yarnProject == null)
            {
                return;
            }

            if (dialogueRunner.YarnProject != yarnProject)
            {
                dialogueRunner.SetProject(yarnProject);
            }
        }

        private bool EnsureProjectReady(string nodeName)
        {
            ApplyYarnProjectToRunner();
            YarnProject project = dialogueRunner != null ? dialogueRunner.YarnProject : null;
            if (project == null)
            {
                project = yarnProject;
                ApplyYarnProjectToRunner();
            }

            if (project == null)
            {
                Debug.LogError($"[{nameof(YarnManager)}] DialogueRunner に YarnProject が設定されていません。", this);
                return false;
            }

            if (ProjectContainsNode(project, nodeName))
            {
                dialogueRunner.SetProject(project);
                return true;
            }

#if UNITY_EDITOR
            if (TryReloadProjectInEditor(project, out YarnProject reloadedProject) &&
                ProjectContainsNode(reloadedProject, nodeName))
            {
                dialogueRunner.SetProject(reloadedProject);
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[{nameof(YarnManager)}] Reimported YarnProject '{reloadedProject.name}' and recovered node '{nodeName}'.",
                        this);
                }

                return true;
            }
#endif

            Debug.LogError(
                $"[{nameof(YarnManager)}] YarnProject に node '{nodeName}' が見つかりません。{DescribeProjectState(project)}",
                this);
            return false;
        }

        private static bool ProjectContainsNode(YarnProject project, string nodeName)
        {
            if (project == null || string.IsNullOrWhiteSpace(nodeName))
            {
                return false;
            }

            try
            {
                string[] nodeNames = project.NodeNames ?? Array.Empty<string>();
                for (int i = 0; i < nodeNames.Length; i++)
                {
                    if (string.Equals(nodeNames[i], nodeName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static string DescribeProjectState(YarnProject project)
        {
            if (project == null)
            {
                return "YarnProject=null";
            }

            try
            {
                string[] nodeNames = project.NodeNames ?? Array.Empty<string>();
                int previewCount = Math.Min(nodeNames.Length, 8);
                string[] preview = new string[previewCount];
                for (int i = 0; i < previewCount; i++)
                {
                    preview[i] = nodeNames[i];
                }

                string previewText = previewCount > 0 ? string.Join(", ", preview) : "(none)";
                return $"YarnProject='{project.name}', nodeCount={nodeNames.Length}, nodes=[{previewText}]";
            }
            catch (Exception exception)
            {
                return $"YarnProject='{project.name}', node inspection failed: {exception.Message}";
            }
        }

#if UNITY_EDITOR
        private static bool TryReloadProjectInEditor(YarnProject project, out YarnProject reloadedProject)
        {
            reloadedProject = project;

            if (project == null)
            {
                return false;
            }

            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(project);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            try
            {
                UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                reloadedProject = UnityEditor.AssetDatabase.LoadAssetAtPath<YarnProject>(assetPath);
                return reloadedProject != null;
            }
            catch (Exception)
            {
                reloadedProject = project;
                return false;
            }
        }
#endif

        private void RegisterLocaleEvents()
        {
            if (conversationDataManager != null)
            {
                conversationDataManager.LocaleChanged -= HandleConversationLocaleChanged;
                conversationDataManager.LocaleChanged += HandleConversationLocaleChanged;
            }
        }

        private void UnregisterLocaleEvents()
        {
            if (conversationDataManager != null)
            {
                conversationDataManager.LocaleChanged -= HandleConversationLocaleChanged;
            }
        }

        private void HandleConversationLocaleChanged(string _)
        {
            ApplyConversationLocaleToRunner();
        }

        public void ApplyLocale(string localeCode)
        {
            if (dialogueRunner == null)
            {
                return;
            }

            string normalizedLocale = NormalizeYarnLocale(localeCode);
            if (dialogueRunner.LineProvider is BuiltinLocalisedLineProvider builtinLineProvider)
            {
                builtinLineProvider.LocaleCode = normalizedLocale;
                builtinLineProvider.AssetLocaleCode = normalizedLocale;
            }
        }

        private void ApplyConversationLocaleToRunner()
        {
            if (conversationDataManager != null)
            {
                ApplyLocale(conversationDataManager.Locale);
            }
        }

        private static string NormalizeYarnLocale(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
            {
                return "ja";
            }

            string normalized = localeCode.Trim();
            if (normalized.Equals("jp", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "ja";
            }

            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return normalized;
        }

        private void RegisterCommands()
        {
            if (dialogueRunner == null)
            {
                return;
            }

            if (registeredCommandRunner != null &&
                registeredCommandRunner != dialogueRunner)
            {
                UnregisterCommands();
            }

            registeredCommandRunner = dialogueRunner;

            if (ownsRunActionCommand || IsCommandRegistered(dialogueRunner, "run_action"))
            {
                RegisterFocusCommands();
                return;
            }

            dialogueRunner.AddCommandHandler<string>("run_action", RunActionCommandAsync);
            ownsRunActionCommand = IsCommandRegistered(dialogueRunner, "run_action");
            RegisterFocusCommands();
        }

        private void UnregisterCommands()
        {
            DialogueRunner runner = registeredCommandRunner != null ? registeredCommandRunner : dialogueRunner;
            UnregisterFocusCommands(runner);

            if (!ownsRunActionCommand || runner == null)
            {
                ownsRunActionCommand = false;
                registeredCommandRunner = null;
                return;
            }

            if (IsCommandRegistered(runner, "run_action"))
            {
                runner.RemoveCommandHandler("run_action");
            }

            ownsRunActionCommand = false;
            registeredCommandRunner = null;
        }

        private void RegisterFocusCommands()
        {
            if (dialogueRunner == null || ownsFocusCommands)
            {
                return;
            }

            bool anyAlreadyRegistered =
                IsCommandRegistered(dialogueRunner, "FocusCatEyes") ||
                IsCommandRegistered(dialogueRunner, "FocusCatPaw") ||
                IsCommandRegistered(dialogueRunner, "FocusCatNose") ||
                IsCommandRegistered(dialogueRunner, "FocusCatTail") ||
                IsCommandRegistered(dialogueRunner, "ClearFocus") ||
                IsCommandRegistered(dialogueRunner, "SetFocusTargetById") ||
                IsCommandRegistered(dialogueRunner, "SetFocusEnabled");

            if (anyAlreadyRegistered)
            {
                return;
            }

            dialogueRunner.AddCommandHandler("FocusCatEyes", FocusCatEyesCommand);
            dialogueRunner.AddCommandHandler("FocusCatPaw", FocusCatPawCommand);
            dialogueRunner.AddCommandHandler("FocusCatNose", FocusCatNoseCommand);
            dialogueRunner.AddCommandHandler("FocusCatTail", FocusCatTailCommand);
            dialogueRunner.AddCommandHandler("ClearFocus", ClearFocusCommand);
            dialogueRunner.AddCommandHandler<string>("SetFocusTargetById", SetFocusTargetByIdCommand);
            dialogueRunner.AddCommandHandler<bool>("SetFocusEnabled", SetFocusEnabledCommand);

            ownsFocusCommands =
                IsCommandRegistered(dialogueRunner, "FocusCatEyes") ||
                IsCommandRegistered(dialogueRunner, "SetFocusTargetById");
        }

        private void UnregisterFocusCommands(DialogueRunner runner)
        {
            if (!ownsFocusCommands || runner == null)
            {
                ownsFocusCommands = false;
                return;
            }

            RemoveCommandIfRegistered(runner, "FocusCatEyes");
            RemoveCommandIfRegistered(runner, "FocusCatPaw");
            RemoveCommandIfRegistered(runner, "FocusCatNose");
            RemoveCommandIfRegistered(runner, "FocusCatTail");
            RemoveCommandIfRegistered(runner, "ClearFocus");
            RemoveCommandIfRegistered(runner, "SetFocusTargetById");
            RemoveCommandIfRegistered(runner, "SetFocusEnabled");
            ownsFocusCommands = false;
        }

        private void RegisterRunnerEvents()
        {
            if (dialogueRunner == null)
            {
                return;
            }

            dialogueRunner.onDialogueComplete ??= new UnityEvent();
            dialogueRunner.onDialogueComplete.AddListener(HandleDialogueComplete);
        }

        private void UnregisterRunnerEvents()
        {
            if (dialogueRunner == null || dialogueRunner.onDialogueComplete == null)
            {
                return;
            }

            dialogueRunner.onDialogueComplete.RemoveListener(HandleDialogueComplete);
        }

        private void HandleDialogueComplete()
        {
            SyncVariablesFromYarnToGameState();
            currentNodeName = string.Empty;
            chatUI?.HideChoices();
            onDialogueComplete?.Invoke();
        }

        private async UniTask RunActionCommandAsync(string actionId)
        {
            ResolveReferences();

            if (actionManager == null)
            {
                Debug.LogWarning($"[{nameof(YarnManager)}] run_action {actionId} を処理できません。ActionManager が見つかりません。");
                return;
            }

            await actionManager.ExecuteActionByIdAsync(actionId, "Yarn");
            SyncVariablesFromGameStateToYarn();
        }

        private static void FocusCatEyesCommand()
        {
            ResolveFocusController()?.FocusCatEyes();
        }

        private static void FocusCatPawCommand()
        {
            ResolveFocusController()?.FocusCatPaw();
        }

        private static void FocusCatNoseCommand()
        {
            ResolveFocusController()?.FocusCatNose();
        }

        private static void FocusCatTailCommand()
        {
            ResolveFocusController()?.FocusCatTail();
        }

        private static void ClearFocusCommand()
        {
            ResolveFocusController()?.ClearFocus();
        }

        private static void SetFocusTargetByIdCommand(string id)
        {
            ResolveFocusController()?.SetFocusTargetById(id);
        }

        private static void SetFocusEnabledCommand(bool enabled)
        {
            ResolveFocusController()?.SetFocusEnabled(enabled);
        }

        private static FocusCameraController ResolveFocusController()
        {
            return FocusCameraController.Instance != null
                ? FocusCameraController.Instance
                : FindFirstObjectByType<FocusCameraController>();
        }

        private static void RemoveCommandIfRegistered(DialogueRunner runner, string commandName)
        {
            if (IsCommandRegistered(runner, commandName))
            {
                runner.RemoveCommandHandler(commandName);
            }
        }

        private static bool IsCommandRegistered(DialogueRunner runner, string commandName)
        {
            if (runner == null || string.IsNullOrWhiteSpace(commandName))
            {
                return false;
            }

            try
            {
                PropertyInfo dispatcherProperty = typeof(DialogueRunner).GetProperty(
                    "CommandDispatcher",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object dispatcher = dispatcherProperty?.GetValue(runner);
                if (dispatcher == null)
                {
                    return false;
                }

                PropertyInfo commandsProperty = dispatcher.GetType().GetProperty(
                    "Commands",
                    BindingFlags.Instance | BindingFlags.Public);
                if (commandsProperty?.GetValue(dispatcher) is not global::System.Collections.IEnumerable commands)
                {
                    return false;
                }

                foreach (object command in commands)
                {
                    if (command == null)
                    {
                        continue;
                    }

                    PropertyInfo nameProperty = command.GetType().GetProperty(
                        "Name",
                        BindingFlags.Instance | BindingFlags.Public);
                    string registeredName = nameProperty?.GetValue(command) as string;
                    if (string.Equals(registeredName, commandName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private int ReadRoundedFloat(string variableName, int fallbackValue)
        {
            if (VariableStorage != null && VariableStorage.TryGetValue<float>(variableName, out float value))
            {
                return Mathf.Clamp(Mathf.RoundToInt(value), 0, 100);
            }

            return fallbackValue;
        }

        private bool TryReadString(string variableName, string currentValue, out string resolvedValue)
        {
            resolvedValue = currentValue ?? string.Empty;
            if (VariableStorage == null ||
                !VariableStorage.TryGetValue<string>(variableName, out string value) ||
                string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (string.Equals(trimmed, currentValue ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            resolvedValue = trimmed;
            return true;
        }

        private string ResolveCurrentPhase()
        {
            if (conversationGameStateManager != null &&
                conversationGameStateManager.State.TryGetString("Phase", out string phaseFromState) &&
                !string.IsNullOrWhiteSpace(phaseFromState))
            {
                return phaseFromState;
            }

            if (gameManager == null)
            {
                return string.Empty;
            }

            if (gameManager.CurrentState is StateMorning)
            {
                return "Morning";
            }

            if (gameManager.CurrentState is StateDay)
            {
                return "Lunch";
            }

            if (gameManager.CurrentState is StateEvening)
            {
                return "Evening";
            }

            if (gameManager.CurrentState is StateNight)
            {
                return "Night";
            }

            return string.Empty;
        }

        private string RegisterStateVariable(string stateKey)
        {
            string variableName = BuildStateVariableName(stateKey);
            stateVariableMap[variableName] = stateKey;
            return variableName;
        }

        private static string BuildStateVariableName(string stateKey)
        {
            return NormalizeVariableName(stateKey);
        }

        private static string BuildFlagVariableName(string flagName)
        {
            string rawName = string.IsNullOrWhiteSpace(flagName) ? "unnamed" : flagName.Trim();
            return "$flag_" + SanitizeVariableToken(rawName);
        }

        private static string NormalizeVariableName(string key)
        {
            string trimmed = string.IsNullOrWhiteSpace(key) ? "unnamed" : key.Trim();
            if (trimmed.StartsWith("$", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            return "$" + SanitizeVariableToken(trimmed);
        }

        private static string SanitizeVariableToken(string key)
        {
            char[] buffer = key.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char current = buffer[i];
                if (char.IsLetterOrDigit(current) || current == '_')
                {
                    continue;
                }

                buffer[i] = '_';
            }

            string sanitized = new string(buffer).Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
        }

        private void SetFloatInternal(string variableName, int value)
        {
            SetFloatInternal(variableName, (float)value);
        }

        private void SetFloatInternal(string variableName, float value)
        {
            VariableStorage?.SetValue(variableName, value);
        }

        private void SetBoolInternal(string variableName, bool value)
        {
            VariableStorage?.SetValue(variableName, value);
        }

        private void SetStringInternal(string variableName, string value)
        {
            VariableStorage?.SetValue(variableName, value ?? string.Empty);
        }

        private static string SanitizeRenameValue(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}
