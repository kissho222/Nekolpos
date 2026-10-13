using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Nekolpos.TimeSystem;
using Nekolpos.StatusSystem;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#endif

namespace Nekolpos.System
{
    [DefaultExecutionOrder(-32700), DisallowMultipleComponent]
    public sealed class NekolposDebugConsole : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private GameObject window;
        [SerializeField] private TMP_Text currentStateText;
        [SerializeField] private Button closeButton;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private GameObject stateView;
        [SerializeField] private NekolposDebugPanel operations;
        [SerializeField] private GameObject conversationView;
        [SerializeField] private TMP_Text conversationText;
        [SerializeField] private Button stateTab;
        [SerializeField] private Button operationsTab;
        [SerializeField] private Button conversationTab;
        [SerializeField] private Button openConversationPanel;
        [SerializeField] private Button legacyDebugButton;
        [SerializeField] private Button diagnosticsTab;
        [SerializeField] private GameObject diagnosticsControls;
        [SerializeField] private Button diagnosticKind;
        private int diagnosticKindIndex;
        private static readonly string[] DiagnosticKinds = { "全種別", "Input", "Dialogue", "Status", "TimeRequest", "Time", "Diary", "UI", "Session", "ConversationResult", "Scenario" };
        [SerializeField] private TMP_InputField diagnosticCorrelation;
        [SerializeField] private Button copySnapshotButton, copyTraceButton, exportTraceButton;
        private int selectedTab;
        private string diagnosticMessage;
        [SerializeField] private Button scenariosTab, previousScenario, nextScenario;
        [SerializeField] private GameObject scenarioControls;
        [SerializeField] private TMP_InputField scenarioSearch;
        [SerializeField] private Button executeScenario;
        [SerializeField] private Button returnInitial;
        private int scenarioIndex;
        private static NekolposDebugConsole instance;
        private DebugConsoleInput.Lease inputLease;
        private float refreshAt;
        public bool IsOpen => window != null && window.activeSelf;
        public static NekolposDebugConsole Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; SceneManager.sceneLoaded -= OnSceneLoaded; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!OpenBetaSceneNames.IsMainGameplayScene(scene.name) || instance != null) return;
            var prefab = Resources.Load<GameObject>("Debug/NekolposDebugConsole");
            if (prefab == null) { Debug.LogWarning("[DebugConsole] Authored prefab is missing."); return; }
            var root = Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            if (window != null) window.SetActive(false);
            closeButton?.onClick.AddListener(Close);
            stateTab?.onClick.AddListener(() => SelectTab(0));
            operationsTab?.onClick.AddListener(() => SelectTab(1));
            conversationTab?.onClick.AddListener(() => SelectTab(2));
            openConversationPanel?.onClick.AddListener(() =>
            {
                var existing = FindFirstObjectByType<Backgammon.Conversation.ConversationDebugPanel>(FindObjectsInactive.Include);
                if (existing == null || !existing.CanOpenExistingWindow || DebugConsoleGuard.GetBlockReason() != null) return;
                Close(); existing.TryOpenFromConsole();
            });
            legacyDebugButton?.onClick.AddListener(() => { if (Open()) SelectTab(1); });
            diagnosticsTab?.onClick.AddListener(() => SelectTab(3));
            scenariosTab?.onClick.AddListener(() => SelectTab(4));
            previousScenario?.onClick.AddListener(() => { scenarioIndex--; RefreshState(); });
            nextScenario?.onClick.AddListener(() => { scenarioIndex++; RefreshState(); });
            scenarioSearch?.onValueChanged.AddListener(_ => { scenarioIndex = 0; RefreshState(); });
            executeScenario?.onClick.AddListener(() => RunSelectedScenarioAsync().Forget());
            returnInitial?.onClick.AddListener(() => DebugScenarioExecutor.ReturnToInitialAsync(this.GetCancellationTokenOnDestroy()).Forget());
            diagnosticKind?.onClick.AddListener(() => { diagnosticKindIndex = (diagnosticKindIndex + 1) % DiagnosticKinds.Length; diagnosticKind.GetComponentInChildren<TMP_Text>().text = DiagnosticKinds[diagnosticKindIndex]; RefreshState(); });
            copySnapshotButton?.onClick.AddListener(() => GUIUtility.systemCopyBuffer = DebugConsoleSnapshot.Capture().ToJson());
            copyTraceButton?.onClick.AddListener(() => GUIUtility.systemCopyBuffer = DebugConsoleTrace.ToJson());
            exportTraceButton?.onClick.AddListener(() =>
            {
                try { DebugConsoleTrace.ExportLocal(); diagnosticMessage = "ローカル書出し完了"; }
                catch (global::System.Exception e) when (e is global::System.IO.IOException || e is global::System.UnauthorizedAccessException || e is global::System.NotSupportedException)
                { diagnosticMessage = "書出し不可: " + e.GetType().Name; }
            });
        }
        public static bool CanDisplay()
        {
            var bootstrap = FindFirstObjectByType<NekolposGameBootstrap>();
            return Application.isPlaying && bootstrap != null && bootstrap.HasEnteredGameplay &&
                OpenBetaSceneNames.IsMainGameplayScene(SceneManager.GetActiveScene().name);
        }
        private void Update()
        {
            if (legacyDebugButton != null) legacyDebugButton.gameObject.SetActive(CanDisplay() && NekolposGameBootstrap.IsDebugStart && !IsOpen);
            if (!CanDisplay()) { if (IsOpen) Close(); return; }
            if (IsOpen) inputLease?.Refresh();
            // Do not commit or discard the player's unfinished IME conversion.
            if (Input.GetKeyDown(toggleKey) && CanToggleDuringIme())
            {
                if (IsOpen) Close(); else Open();
            }
            if (IsOpen && Time.unscaledTime >= refreshAt)
            {
                RefreshState(); refreshAt = Time.unscaledTime + .25f;
            }
        }
        public bool Open()
        {
            if (IsOpen) return true;
            if (!CanDisplay() || window == null || !CanToggleDuringIme()) return false;
            inputLease = DebugConsoleInput.Acquire(transform);
            if (inputLease == null) return false;
            window.SetActive(true);
            SelectTab(0);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            RefreshState(); return true;
        }
        private void LateUpdate() { if (IsOpen) inputLease?.Refresh(); }
        public void Close()
        {
            // Scene destruction or an authored view being disabled must still release input ownership.
            if (window != null) window.SetActive(false);
            inputLease?.Dispose(); inputLease = null;
        }
        private void OnDisable() { Close(); }
        private static bool CanToggleDuringIme()
        {
            var chat = FindFirstObjectByType<ChatUIController>();
            return string.IsNullOrEmpty(Input.compositionString) && (chat == null || !chat.HasActiveImeComposition);
        }
        private void OnDestroy() { Close(); if (instance == this) instance = null; }
        public void RefreshState()
        {
            if (currentStateText != null) currentStateText.text = selectedTab == 3 ? ReadDiagnostics() : selectedTab == 4 ? ReadScenarios() : ReadCurrentState();
            if (conversationText != null) conversationText.text = "会話\nDialogueEngine: " + (DialogueEngine.Instance != null ? "接続済み" : "未接続") +
                "\nConversationDebugPanel: " + (FindFirstObjectByType<Backgammon.Conversation.ConversationDebugPanel>(FindObjectsInactive.Include) != null ? "接続済み" : "未接続（Managerを新設しません）") +
                "\n" + (DebugConsoleGuard.GetBlockReason() ?? "通常会話待機") + "\n既存会話デバッグ画面は配置されているSceneで利用できます。";
            if (openConversationPanel != null)
            {
                var panel = FindFirstObjectByType<Backgammon.Conversation.ConversationDebugPanel>(FindObjectsInactive.Include);
                openConversationPanel.interactable = panel != null && panel.CanOpenExistingWindow && DebugConsoleGuard.GetBlockReason() == null;
            }
        }
        private void SelectTab(int index)
        {
            selectedTab = index;
            if (stateView != null) stateView.SetActive(index == 0 || index == 3 || index == 4);
            if (operations != null) operations.ShowControls(index == 1);
            if (conversationView != null) conversationView.SetActive(index == 2);
            if (diagnosticsControls != null) diagnosticsControls.SetActive(index == 3);
            if (scenarioControls != null) scenarioControls.SetActive(index == 4);
            RefreshState();
        }
        private string ReadDiagnostics()
        {
            string kind = diagnosticKindIndex > 0 ? DiagnosticKinds[diagnosticKindIndex] : null;
            var events = DebugConsoleTrace.Read(kind, diagnosticCorrelation != null ? diagnosticCorrelation.text.Trim() : null);
            var text = new StringBuilder($"診断 ({DebugConsoleTrace.Count}/{DebugConsoleTrace.Capacity}) {diagnosticMessage}\n入力本文は記録しません。\n");
            foreach (var e in events) text.AppendLine($"#{e.sequence} {e.elapsed:F2}s D{e.day} {e.period} [{e.kind}] {e.source}: {e.before} → {e.after} ({e.result})\n  {e.correlationId}");
            return text.ToString();
        }
        private DebugScenarioDefinition SelectedScenario()
        {
            var found = new List<DebugScenarioDefinition>();
            string query = scenarioSearch != null ? scenarioSearch.text.Trim() : "";
            foreach (var entry in DebugScenarioCatalog.Entries)
                if (string.IsNullOrEmpty(query) || entry.Id.IndexOf(query, global::System.StringComparison.OrdinalIgnoreCase) >= 0 || entry.DisplayName.Contains(query)) found.Add(entry);
            if (found.Count == 0) return null;
            scenarioIndex = (scenarioIndex % found.Count + found.Count) % found.Count;
            return found[scenarioIndex];
        }
        private string ReadScenarios()
        {
            var text = new StringBuilder("シナリオ登録一覧\n");
            foreach (var e in DebugScenarioCatalog.Entries) text.AppendLine(e.Id + " — " + e.DisplayName);
            var selected = SelectedScenario();
            if (executeScenario != null) executeScenario.interactable = selected != null && !DebugScenarioExecutor.IsRunning && DebugScenarioCatalog.PrerequisiteReason(selected) == null && DebugConsoleGuard.GetBlockReason() == null;
            if (selected == null) return text.AppendLine("検索結果なし").ToString();
            text.AppendLine($"\n選択: {selected.Id}\n{selected.DisplayName}\n必要モード: {selected.Mode}\nFixture: {selected.Fixture}\n前提: {DebugScenarioCatalog.PrerequisiteReason(selected) ?? "隔離済み: 前提接続あり"}");
            text.AppendLine("副作用: 隔離したメモリ内進行のみを更新。通常セーブへ適用しません。\n隔離開始: Play停止 → Nekolpos > Debug > Start Isolated Play\n隔離終了: Play停止。通常プレイは新しいPlayから再開してください。");
            var last = DebugScenarioExecutor.LastResult;
            if (last != null) text.AppendLine($"\n前回結果: {last.scenarioId} / {last.status}\n{last.reason}");
            return text.ToString();
        }
        private async Cysharp.Threading.Tasks.UniTask RunSelectedScenarioAsync()
        {
            var entry = SelectedScenario();
            if (entry != null) await DebugScenarioExecutor.RunAsync(entry.Id, this.GetCancellationTokenOnDestroy());
        }
        public static string ReadCurrentState()
        {
            var time = FindFirstObjectByType<TimeManager>();
            if (time != null && !time.IsInitialized) time = null;
            var status = FindFirstObjectByType<StatusManager>();
            if (status != null && !status.IsInitialized) status = null;
            var area = FindFirstObjectByType<PlayerAreaTracker>();
            var presentation = FindFirstObjectByType<CatPresentationModeController>();
            var walk = FindFirstObjectByType<PlayerWalkModeController>();
            var bootstrap = FindFirstObjectByType<NekolposGameBootstrap>();
            var pause = FindFirstObjectByType<OpenBetaPauseMenuController>();
            var diary = FindFirstObjectByType<DiaryCalendarController>();
            var chat = FindFirstObjectByType<ChatUIController>();
            var text = new StringBuilder("現在状態\n");
            text.AppendLine(time != null ? $"{time.GetCalendarDate(time.CurrentDay):yyyy-MM-dd} / Day {time.CurrentDay} / {time.CurrentPeriod}\n天気: {time.Weather} / 翌日: {time.TomorrowWeather}" : "日時・天気: 未接続");
            text.AppendLine("エリア: " + (area != null ? area.CurrentAreaId : "未接続"));
            text.AppendLine("拠点: " + (presentation != null && presentation.PositionController != null ? presentation.PositionController.CurrentHomeLocation.ToString() : "未接続"));
            foreach (StatusType type in global::System.Enum.GetValues(typeof(StatusType)))
                text.AppendLine(type + ": " + (status != null ? status.GetValue(type).ToString() : "未接続"));
            text.AppendLine("代表心理: " + (status != null ? status.GetRepresentativeState().DisplayName : "未接続"));
            text.AppendLine("ゲーム状態: " + (GameManager.Instance?.CurrentState?.GetType().Name ?? "未接続"));
            text.AppendLine("散歩: " + (walk != null ? $"{walk.IsWalkMode} / 遷移: {walk.IsTransitioning} / 選択: {walk.IsInteractionMenuOpen}" : "未接続"));
            text.AppendLine("Tablet: " + (bootstrap != null ? $"{bootstrap.IsTabletCameraViewActive} / 遷移: {bootstrap.IsTabletCameraTransitioning} / 運搬: {bootstrap.IsTransporting}" : "未接続"));
            text.AppendLine("Pause: " + (pause != null ? pause.IsPaused.ToString() : "未接続") + $" / TimeScale: {Time.timeScale}");
            text.AppendLine("日記: " + (diary != null ? $"夜記入: {diary.IsNightDiaryActive} / Window: {diary.IsCalendarWindowOpen}" : "未接続"));
            text.AppendLine("会話: " + (chat != null ? $"入力: {chat.IsControlGuideInputActive} / 表示: {chat.IsInDialogueMode} / タイプ中: {chat.IsTyping}\n選択肢: {chat.HasOpenChoices} / ログ: {chat.IsLogWindowOpen}" : "未接続"));
            text.AppendLine("設定UI: " + SettingsPanel.IsOpen);
            return text.ToString();
        }
#else
        private void Awake() { gameObject.SetActive(false); }
#endif
    }
}
