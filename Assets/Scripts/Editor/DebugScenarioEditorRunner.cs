using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools
{
    [InitializeOnLoad]
    public static class DebugScenarioEditorRunner
    {
        private const string PendingKey = "Nekolpos.DebugRunner.Pending";
        private const string LastKey = "Nekolpos.DebugRunner.LastReport";
        private const string BatchKey = "Nekolpos.DebugRunner.Batch";
        public const string ReportDirectory = "Library/DebugConsole/Reports";
        public const string NUnitXmlPath = "Library/DebugConsole/Regression.xml";
        private static CancellationTokenSource cancellation;
        private static Report active;
        [Serializable] public sealed class Check { public string name; public bool passed; }
        [Serializable] public sealed class Report
        {
            public int version = 1;
            public string runId, scenarioId, unityVersion, buildTarget, environment = "Unity Editor Play (isolated)",
                status, automatedStatus, reason, startedUtc, finishedUtc, correlationId, phase, reportPath;
            public bool sceneWasDirty, normalSavesPreserved, scenePreserved;
            public float duration;
            public DebugScenarioResult scenario, returned;
            public ScenarioAwaitDiagnostics scenarioAwait;
            public DebugConsoleSnapshot afterCleanup;
            public DebugConsoleTrace.Entry[] trace;
            public List<Check> checks = new();
            public List<string> failureLogs = new();
            public string[] manualChecks = { "F1/Esc/F10・日本語IMEの実キー入力", "カメラ・視線・運搬/朝日・日記の描画", "Windows/WebGL/モバイル実Player" };
            public string nunitXml, nunitAssociation = "Separate regression run; not executed by this scenario invocation";
            public string reproduction;
        }
        static DebugScenarioEditorRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        public static string LastReportPath => SessionState.GetString(LastKey, "");
        public static string ReadLastReport() => File.Exists(LastReportPath) ? File.ReadAllText(LastReportPath) : "{}";
        public static string Request(string id)
        {
            var entry = DebugScenarioCatalog.Find(id);
            var report = NewReport(entry?.Id ?? "<unregistered>");
            if (entry == null) return Reject(report, "Rejected", "許可リストにないIDです");
            if (EditorApplication.isPlayingOrWillChangePlaymode || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                return Reject(report, "EnvironmentUnavailable", "Playを停止し、進行中のRunnerが終了してから実行してください");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !OpenBetaSceneNames.IsMainGameplayScene(SceneManager.GetActiveScene().name))
                return Reject(report, "EnvironmentUnavailable", "MainSceneを開き、コンパイル・Importの終了を待ってください");
            report.sceneWasDirty = SceneManager.GetActiveScene().isDirty;
            report.phase = "Starting"; report.status = "Running";
            try
            {
                var baseline = DebugNormalSaveAudit.Capture();
                Save(report);
                File.WriteAllText(report.reportPath + ".baseline", Newtonsoft.Json.JsonConvert.SerializeObject(baseline));
                SessionState.SetString(PendingKey, report.reportPath);
                SessionState.SetString(LastKey, report.reportPath);
                DebugSessionMenu.StartIsolatedPlay();
                return report.reportPath;
            }
            catch (Exception e) { SessionState.EraseString(PendingKey); return Reject(report, "EnvironmentUnavailable", e.GetType().Name); }
        }
        public static void RequestBatch(string[] ids)
        {
            if (ids == null || ids.Length == 0 || ids.Any(id => DebugScenarioCatalog.Find(id) == null)) throw new ArgumentException("許可済みIDの配列を指定してください");
            if (EditorApplication.isPlayingOrWillChangePlaymode || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) throw new InvalidOperationException("Play/Runnerを停止してください");
            SessionState.SetString(BatchKey, string.Join("\n", ids.Skip(1)));
            StartScenarioBatchRequest(() => Request(ids[0]), () => SessionState.EraseString(BatchKey));
        }
        [MenuItem("Nekolpos/Debug/Run Scenario normal.table")]
        private static void RunDefault() => Request("normal.table");
        private static Report NewReport(string id)
        {
            string run = Guid.NewGuid().ToString("N");
            return new Report { runId = run, scenarioId = id, unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(), startedUtc = DateTime.UtcNow.ToString("O"),
                reportPath = Path.Combine(ReportDirectory, run + ".json"), nunitXml = File.Exists(NUnitXmlPath) ? NUnitXmlPath : null,
                reproduction = "MainSceneを開いてPlay停止 → Nekolpos.EditorTools.DebugScenarioEditorRunner.Request(\"" + id + "\")。同じIDはF1シナリオ一覧でも使用可能。" };
        }
        private static string Reject(Report report, string status, string reason)
        { SessionState.EraseString(BatchKey); report.status = status; report.automatedStatus = status; report.reason = reason; report.phase = "Finished"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Save(report); SessionState.SetString(LastKey, report.reportPath); return report.reportPath; }
        private static void Save(Report report)
        {
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(report.reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        }
        private static Report LoadPending()
        {
            string path = SessionState.GetString(PendingKey, "");
            return File.Exists(path) ? Newtonsoft.Json.JsonConvert.DeserializeObject<Report>(File.ReadAllText(path)) : null;
        }
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                active = LoadPending(); if (active == null) return;
                cancellation = new CancellationTokenSource();
                Application.logMessageReceived += CaptureFailure;
                RunCycleAsync(active, cancellation.Token).Forget();
            }
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                if (active != null && active.phase != "AwaitingShutdown") { active.status = "Cancelled"; active.automatedStatus = "Cancelled"; active.reason = "Play停止により試験を中止しました"; Save(active); }
                cancellation?.Cancel(); Application.logMessageReceived -= CaptureFailure;
            }
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                var report = LoadPending(); if (report == null) { EditorApplication.delayCall += DebugSaveSession.ReleaseAfterPlayStopped; return; }
                if (report.phase != "AwaitingShutdown")
                {
                    report.status = report.automatedStatus = "Cancelled";
                    report.reason = "試験の終了処理前にPlayが終了しました。reload後の継続は許可しません";
                }
                try
                {
                    var baseline = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(report.reportPath + ".baseline"));
                    var after = DebugNormalSaveAudit.Capture();
                    File.WriteAllText(report.reportPath + ".after", Newtonsoft.Json.JsonConvert.SerializeObject(after));
                    report.normalSavesPreserved = DebugNormalSaveAudit.Matches(baseline, after);
                    report.scenePreserved = OpenBetaSceneNames.IsMainGameplayScene(SceneManager.GetActiveScene().name) && SceneManager.GetActiveScene().isDirty == report.sceneWasDirty;
                    report.checks.Add(new Check { name = "Normal saves/Prefs/CatData/Assets/EditorPrefs unchanged after shutdown", passed = report.normalSavesPreserved });
                    report.checks.Add(new Check { name = "Unsaved scene retained", passed = report.scenePreserved });
                    if (!report.normalSavesPreserved || !report.scenePreserved) { report.status = report.automatedStatus = "TestFailed"; report.reason = "終了後の保存またはScene保全監査が失敗しました"; }
                    report.phase = "Finished"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Save(report);
                }
                catch (Exception e) { report.status = report.automatedStatus = "EnvironmentUnavailable"; report.phase = "Finished"; report.finishedUtc = DateTime.UtcNow.ToString("O"); report.reason = "終了監査を実施できません: " + e.GetType().Name; Save(report); }
                finally
                {
                    // Clear a failed queue even when report persistence throws in the audit catch.
                    if (!ShouldContinueScenarioBatch(report.phase, report.automatedStatus, report.normalSavesPreserved, report.scenePreserved))
                        SessionState.EraseString(BatchKey);
                    SessionState.EraseString(PendingKey); active = null; cancellation?.Dispose(); cancellation = null;
                    EditorApplication.delayCall += DebugSaveSession.ReleaseAfterPlayStopped;
                }
                // Stop a batch on a failed preservation check rather than compounding damage.
                if (!ShouldContinueScenarioBatch(report.phase, report.automatedStatus, report.normalSavesPreserved, report.scenePreserved)) return;
                string scheduledQueue = SessionState.GetString(BatchKey, "");
                if (!string.IsNullOrEmpty(scheduledQueue))
                    EditorApplication.delayCall += () => RunScheduledScenarioBatchRequest(scheduledQueue,
                        () => SessionState.GetString(BatchKey, ""), value => SessionState.SetString(BatchKey, value),
                        id => Request(id), () => SessionState.EraseString(BatchKey));
            }
        }
        private static void CaptureFailure(string text, string stack, LogType type)
        {
            if (active == null || active.failureLogs.Count >= 64 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            // Raw engine logs may include utterances, local paths or credentials.
            active.failureLogs.Add(type + ": <masked>");
        }
        private static async UniTask RunCycleAsync(Report report, CancellationToken token)
        {
            float began = Time.realtimeSinceStartup;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                if (!DebugSaveSession.CanRunScenarios) throw new NotSupportedException("Scene Awake前の隔離が確認できません");
                var b = Object.FindFirstObjectByType<NekolposGameBootstrap>();
                if (b == null || !b.BeginIsolatedGameplay()) throw new NotSupportedException("隔離ゲーム開始を要求できません");
                var chat = Object.FindFirstObjectByType<ChatUIController>();
                await DriveAsync(() => DebugConsoleGuard.GetBlockReason() == null, () => chat.AdvanceIsolatedDebugMessage(), token, 30);
                report.phase = "Executing"; Save(report);
                report.scenario = await RunScenarioWithDiaryGreetingAsync(report, b, chat, token);
                report.correlationId = report.scenario.correlationId;
                if (report.scenario.status == DebugScenarioStatus.Rejected.ToString()) throw new NotSupportedException(report.scenario.reason);
                VerifyConsoleLifecycle(report);
                var kind = DebugScenarioCatalog.Find(report.scenarioId).Kind;
                if (report.scenario.status == DebugScenarioStatus.ManualVerificationRequired.ToString() && (kind == DebugScenarioKind.DiaryNormal || kind == DebugScenarioKind.DiarySpecial))
                {
                    var diary = Object.FindFirstObjectByType<DiaryCalendarController>();
                    int day = Object.FindFirstObjectByType<TimeManager>().CurrentDay;
                    await DriveAsync(() => !diary.IsNightDiaryActive && !diary.IsWritingOrTransitioning, () => diary.TryAdvanceIsolatedNightDiary(), token, 60);
                    var entries = Newtonsoft.Json.Linq.JObject.Parse(ProgressSaveStore.GetString("Nekolpos.Diary.Entries.v1", "{}"))["entries"] as Newtonsoft.Json.Linq.JArray;
                    CheckOrFail(report, "Calendar saved through authored confirmation UI", entries != null && entries.Count >= (kind == DebugScenarioKind.DiarySpecial ? 2 : 1));
                    CheckOrFail(report, "Pending consumed", !DiaryJournalStore.HasPendingSpecialEntries);
                    if (!b.RequestIsolatedTabletPowerOff()) throw new InvalidOperationException("日記後の電源終了を要求できません");
                    await DriveAsync(() => Object.FindFirstObjectByType<TimeManager>().CurrentDay == day + 1 && DebugConsoleGuard.GetBlockReason() == null, () => chat.AdvanceIsolatedDebugMessage(), token, 60);
                    CheckOrFail(report, "Night sleep advanced exactly one day", Object.FindFirstObjectByType<TimeManager>().CurrentDay == day + 1);
                }
                else CheckOrFail(report, "Allowlisted target reached", report.scenario.status == DebugScenarioStatus.Succeeded.ToString());
                CheckOrFail(report, "Single Console", Object.FindObjectsByType<NekolposDebugConsole>(FindObjectsSortMode.None).Length == 1);
                CheckOrFail(report, "TimeScale retained", Mathf.Approximately(Time.timeScale, report.scenario.before.timeScale));
                report.phase = "Returning"; Save(report);
                report.returned = await DebugScenarioExecutor.ReturnToInitialAsync(token);
                CheckOrFail(report, "Returned through official APIs", report.returned.status == DebugScenarioStatus.Succeeded.ToString());
                report.afterCleanup = DebugConsoleSnapshot.Capture();
                CheckOrFail(report, "Normal Table input restored", report.afterCleanup.home == "Table" && report.afterCleanup.chatInput == true && report.afterCleanup.walk == false && report.afterCleanup.tablet == false);
                CheckOrFail(report, "No Error/Exception logs", report.failureLogs.Count == 0);
                report.automatedStatus = "Passed"; report.status = "ManualVerificationRequired";
                report.reason = "自動状態検証成功。描画・IME・実PlayerはmanualChecksを確認してください";
            }
            catch (OperationCanceledException) { report.status = report.automatedStatus = "Cancelled"; report.reason = "試験をキャンセルしました"; }
            catch (NotSupportedException e) { report.status = report.automatedStatus = "EnvironmentUnavailable"; report.reason = e.Message; }
            catch (Exception e) { report.status = report.automatedStatus = "TestFailed"; report.reason = e.GetType().Name; }
            finally
            {
                try
                {
                    report.duration = Time.realtimeSinceStartup - began; report.trace = DebugConsoleTrace.Read();
                    report.phase = "AwaitingShutdown"; Save(report);
                }
                finally
                {
                    // Report IO/trace failure must not leave an unattended Play session alive.
                    DebugSaveSession.Finish();
                    EditorApplication.isPlaying = false;
                }
            }
        }

        // BEGIN MEMORY-ONLY SCENARIO AWAIT
        internal static bool ShouldContinueScenarioBatch(string phase, string automatedStatus,
            bool normalSavesPreserved, bool scenePreserved) =>
            string.Equals(phase, "Finished", StringComparison.Ordinal) &&
            string.Equals(automatedStatus, "Passed", StringComparison.Ordinal) &&
            normalSavesPreserved && scenePreserved;

        internal static void StartScenarioBatchRequest(Action request, Action clearQueue)
        {
            try { request(); }
            catch { clearQueue(); throw; }
        }

        internal static void RunScheduledScenarioBatchRequest(string scheduledQueue,
            Func<string> readQueue, Action<string> writeQueue, Action<string> request, Action clearQueue)
        {
            if (string.IsNullOrEmpty(scheduledQueue) ||
                !string.Equals(readQueue(), scheduledQueue, StringComparison.Ordinal)) return;
            string[] queue = scheduledQueue.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (queue.Length == 0) { clearQueue(); return; }
            writeQueue(string.Join("\n", queue.Skip(1)));
            StartScenarioBatchRequest(() => request(queue[0]), clearQueue);
        }

        [Serializable] public sealed class ScenarioAwaitDiagnostics
        {
            public string observedOperationFailureType;
            public string cancellationFailureType;
        }

        internal static async global::System.Threading.Tasks.Task<T> AwaitScenarioTaskAsync<T>(
            Func<CancellationToken, global::System.Threading.Tasks.Task<T>> start,
            Func<bool> remainsValid, Func<bool> mayAdvance, Action advance,
            Func<double> now, Func<CancellationToken, global::System.Threading.Tasks.Task> nextFrame,
            CancellationToken token, double timeoutSeconds, ScenarioAwaitDiagnostics diagnostics)
        {
            if (start == null || remainsValid == null || mayAdvance == null || advance == null ||
                now == null || nextFrame == null || diagnostics == null) throw new ArgumentNullException();
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            token.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            global::System.Threading.Tasks.Task<T> operation = null;
            try
            {
                if (!remainsValid()) throw new OperationCanceledException("Scenario lifetime ended.", token);
                double deadline = now() + timeoutSeconds, nextAdvance = double.NegativeInfinity;
                operation = start(linked.Token) ?? throw new InvalidOperationException("Scenario task is missing.");
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (!remainsValid()) throw new OperationCanceledException("Scenario lifetime ended.", token);
                    if (operation.IsCompleted) return await operation;
                    double current = now();
                    if (current >= deadline) throw new TimeoutException("Scenario execution exceeded its deadline.");
                    if (current >= nextAdvance && mayAdvance())
                    {
                        // Gate evaluation may synchronously finish the operation. Never advance afterward.
                        token.ThrowIfCancellationRequested();
                        if (operation.IsCompleted) return await operation;
                        if (!remainsValid()) throw new OperationCanceledException("Scenario lifetime ended.", token);
                        advance();
                        nextAdvance = current + .2;
                    }
                    token.ThrowIfCancellationRequested();
                    if (!remainsValid()) throw new OperationCanceledException("Scenario lifetime ended.", token);
                    if (operation.IsCompleted) return await operation;
                    await nextFrame(linked.Token);
                }
            }
            catch
            {
                // Cancel the real operation before unwinding to Finish/Play stop. Cleanup never awaits
                // an uncooperative task; a fault-only observer consumes any eventual exception.
                try { linked.Cancel(); }
                catch (Exception error) { diagnostics.cancellationFailureType = error.GetType().Name; }
                if (operation != null)
                    _ = operation.ContinueWith(failed =>
                    {
                        var error = failed.Exception;
                        Interlocked.Exchange(ref diagnostics.observedOperationFailureType,
                            error?.InnerException?.GetType().Name ?? nameof(AggregateException));
                    }, CancellationToken.None,
                        global::System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted |
                        global::System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                        global::System.Threading.Tasks.TaskScheduler.Default);
                throw;
            }
        }
        // END MEMORY-ONLY SCENARIO AWAIT

        private static async UniTask<DebugScenarioResult> RunScenarioWithDiaryGreetingAsync(
            Report report, NekolposGameBootstrap bootstrap, ChatUIController chat, CancellationToken token)
        {
            var kind = DebugScenarioCatalog.Find(report.scenarioId).Kind;
            if (kind != DebugScenarioKind.DiaryNormal && kind != DebugScenarioKind.DiarySpecial)
                return await DebugScenarioExecutor.RunAsync(report.scenarioId, token);
            var clock = Object.FindFirstObjectByType<TimeManager>();
            var dialogue = DialogueManager.Instance;
            var diary = Object.FindFirstObjectByType<DiaryCalendarController>();
            if (chat == null || clock == null || dialogue == null || diary == null)
                throw new NotSupportedException("日記の正式な入力・時計・会話・日記が未接続です");
            report.scenarioAwait = new ScenarioAwaitDiagnostics();
            return await AwaitScenarioTaskAsync(
                ct => DebugScenarioExecutor.RunAsync(report.scenarioId, ct).AsTask(),
                () => DebugSaveSession.CanRunScenarios && bootstrap != null && bootstrap.isActiveAndEnabled &&
                    bootstrap.HasEnteredGameplay && chat != null && clock != null && dialogue != null && diary != null,
                () => clock.CurrentPeriod == DayPeriod.Night && dialogue.IsTimeFlowRunning && !diary.IsNightDiaryActive,
                () => chat.AdvanceIsolatedScenarioMessage(),
                () => Time.realtimeSinceStartupAsDouble,
                ct => UniTask.Yield(PlayerLoopTiming.Update, ct, cancelImmediately: true).AsTask(), token, 60, report.scenarioAwait);
        }

        private static void CheckOrFail(Report report, string name, bool passed)
        { report.checks.Add(new Check { name = name, passed = passed }); if (!passed) throw new InvalidOperationException(name); }
        private static void VerifyConsoleLifecycle(Report report)
        {
            var console = NekolposDebugConsole.Instance;
            var before = DebugConsoleSnapshot.Capture();
            var selection = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            // Walk/Tablet may still retain a pointer to the now-hidden chat input. Never reactivate that UI.
            var expectedSelection = selection != null && selection.activeInHierarchy ? selection : null;
            var chat = Object.FindFirstObjectByType<ChatUIController>();
            if (chat != null)
            {
                chat.SetExternalImeCompositionActive(true);
                try { CheckOrFail(report, "External IME composition refuses Console toggle", !console.Open() && !DebugConsoleInput.ConsoleOwnsInput); }
                finally { chat.SetExternalImeCompositionActive(false); }
            }
            CheckOrFail(report, "Console opens in target mode", console != null && console.Open());
            CheckOrFail(report, "Console owns input", DebugConsoleInput.ConsoleOwnsInput);
            console.Close();
            var after = DebugConsoleSnapshot.Capture();
            CheckOrFail(report, "Console preserves cursor/Pause/TimeScale", before.cursorLock == after.cursorLock && before.cursorVisible == after.cursorVisible && before.paused == after.paused && before.timeScale == after.timeScale);
            CheckOrFail(report, "Console restores valid selected UI", UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject == expectedSelection);
        }
        private static async UniTask DriveAsync(Func<bool> done, Func<bool> advance, CancellationToken token, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout, nextAdvance = 0;
            while (!done())
            {
                token.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException();
                if (Time.realtimeSinceStartup >= nextAdvance) { advance(); nextAdvance = Time.realtimeSinceStartup + .2f; }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
    }
}
