#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nekolpos.TimeSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nekolpos.System
{
    public enum DebugScenarioStatus { Succeeded, Rejected, Cancelled, Failed, ManualVerificationRequired, EnvironmentUnavailable }
    [Serializable]
    public sealed class DebugScenarioResult
    {
        public string scenarioId, status, reason, correlationId;
        public float duration;
        public DebugConsoleSnapshot before, after;
    }
    /// <summary>F1 and Editor automation share this allowlisted executor and the same rejection/cleanup rules.</summary>
    public static class DebugScenarioExecutor
    {
        public static bool IsRunning { get; private set; }
        public static DebugScenarioResult LastResult { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { IsRunning = false; LastResult = null; }
        public static async UniTask<DebugScenarioResult> RunAsync(string id, CancellationToken cancellation = default)
        {
            var entry = DebugScenarioCatalog.Find(id);
            string reason = DebugScenarioCatalog.PrerequisiteReason(entry) ?? DebugConsoleGuard.GetBlockReason();
            if (IsRunning) reason = "別のシナリオを実行中です";
            if (reason != null) return Result(id, DebugScenarioStatus.Rejected, reason, null, 0);
            var bootstrap = Object.FindFirstObjectByType<NekolposGameBootstrap>();
            float started = Time.realtimeSinceStartup;
            DebugConsoleTrace.SetSession(DebugSaveSession.SessionId, id);
            var before = DebugConsoleSnapshot.Capture();
            IsRunning = true;
            NekolposDebugConsole.Instance?.Close();
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellation);
                if (!bootstrap.HasEnteredGameplay) throw new OperationCanceledException("タイトル復帰により中止");
                switch (entry.Kind)
                {
                    case DebugScenarioKind.NormalTable:
                        await MoveHomeAsync(bootstrap, CatHomeLocation.Table, cancellation); break;
                    case DebugScenarioKind.NormalDesk:
                        await MoveHomeAsync(bootstrap, CatHomeLocation.Desk, cancellation); break;
                    case DebugScenarioKind.Walk:
                        var walk = Object.FindFirstObjectByType<PlayerWalkModeController>();
                        if (walk == null || !walk.TryEnterWalkMode()) throw new InvalidOperationException("正式な散歩入口が要求を拒否しました");
                        await WaitAsync(() => walk != null && walk.IsWalkMode && !walk.IsTransitioning, bootstrap, cancellation); break;
                    case DebugScenarioKind.TabletDesk:
                        await MoveHomeAsync(bootstrap, CatHomeLocation.Desk, cancellation);
                        if (!bootstrap.EnterTabletCameraView()) throw new InvalidOperationException("正式なTablet入口が要求を拒否しました");
                        await WaitAsync(() => !bootstrap.IsTabletCameraTransitioning && bootstrap.IsTabletCameraViewActive && bootstrap.IsTabletDisplayPowered, bootstrap, cancellation); break;
                    case DebugScenarioKind.Transport:
                        await MoveHomeAsync(bootstrap, CatHomeLocation.Table, cancellation);
                        if (!bootstrap.TryTransport("MoveToDesk")) throw new InvalidOperationException("運搬入口が要求を拒否しました");
                        await WaitAsync(() => !bootstrap.IsTransporting && DebugConsoleGuard.GetBlockReason() == null, bootstrap, cancellation);
                        if (Object.FindFirstObjectByType<CatPresentationModeController>()?.PositionController?.CurrentHomeLocation != CatHomeLocation.Desk)
                            throw new InvalidOperationException("運搬後の拠点がDeskではありません");
                        break;
                    case DebugScenarioKind.DiaryNormal:
                    case DebugScenarioKind.DiarySpecial:
                        var clock = Object.FindFirstObjectByType<TimeManager>();
                        if (clock == null) throw new InvalidOperationException("TimeManagerが未接続です");
                        DebugScenarioFixtures.PrepareDiary(clock, entry.Kind == DebugScenarioKind.DiarySpecial);
                        await DialogueManager.Instance.RequestTimeAdvanceAsync(new TimeAdvanceRequest(clock.MinutesUntilNight, TimeAdvanceReason.Debug), cancellationToken: cancellation);
                        var diary = Object.FindFirstObjectByType<DiaryCalendarController>();
                        await WaitAsync(() => clock.CurrentPeriod == DayPeriod.Night && diary != null && diary.IsNightDiaryActive && !bootstrap.IsTabletCameraTransitioning, bootstrap, cancellation);
                        return Result(id, DebugScenarioStatus.ManualVerificationRequired, "夜日記の正式入口へ到達。既存UIから記入・カレンダー確定・電源終了を確認してください", before, started);
                    case DebugScenarioKind.Death:
                        var defeatClock = Object.FindFirstObjectByType<TimeManager>();
                        int dayBefore = defeatClock.CurrentDay;
                        DialogueManager.Instance.TriggerPlayerDefeat("press", timedEventKey: "action_paw_press_death_result");
                        await WaitAsync(() => !DialogueManager.Instance.IsPlayerDefeatRunning && DebugConsoleGuard.GetBlockReason() == null, bootstrap, cancellation, 60);
                        if (defeatClock.CurrentDay != dayBefore + 1 || defeatClock.CurrentPeriod != DayPeriod.Morning)
                            throw new InvalidOperationException("死亡復帰の日付が正確に翌朝になっていません");
                        break;
                    default: return Result(id, DebugScenarioStatus.Rejected, "この段階では未対応です", before, started);
                }
                return Result(id, DebugScenarioStatus.Succeeded, "正式経路で目的状態へ到達。カメラ・描画・IMEは要手動確認", before, started);
            }
            catch (OperationCanceledException) { RecoverToTitle(bootstrap); return Result(id, DebugScenarioStatus.Cancelled, "キャンセル・Scene退出により中止", before, started); }
            catch (Exception e)
            { RecoverToTitle(bootstrap); return Result(id, DebugScenarioStatus.Failed, e is TimeoutException ? "目的状態への待機が時間切れです" : e.GetType().Name, before, started); }
            finally { IsRunning = false; }
        }
        public static async UniTask<DebugScenarioResult> ReturnToInitialAsync(CancellationToken cancellation = default)
        {
            if (!DebugSaveSession.CanRunScenarios || IsRunning) return Result("return.initial", DebugScenarioStatus.Rejected, "隔離中・シナリオ停止後だけ復帰できます", null, 0);
            var b = Object.FindFirstObjectByType<NekolposGameBootstrap>();
            var d = DialogueManager.Instance;
            var diary = Object.FindFirstObjectByType<DiaryCalendarController>();
            if (b == null || !b.HasEnteredGameplay || b.IsTransporting || b.IsTabletCameraTransitioning || b.IsTabletPowerTransitioning ||
                (d != null && (d.IsTimeFlowRunning || d.IsPlayerDefeatRunning || d.IsNightSleepRunning)) ||
                (diary != null && (diary.IsNightDiaryActive || diary.IsWritingOrTransitioning)) || SettingsPanel.IsOpen || Time.timeScale <= 0)
                return Result("return.initial", DebugScenarioStatus.Rejected, "アトミックなイベント・Pause・日記が完了するまで復帰を拒否します", null, 0);
            float started = Time.realtimeSinceStartup; var before = DebugConsoleSnapshot.Capture(); IsRunning = true;
            NekolposDebugConsole.Instance?.Close();
            try
            {
                if (b.IsTabletCameraViewActive)
                {
                    if (!b.RequestIsolatedTabletPowerOff()) throw new InvalidOperationException("タブレット電源終了を開始できません");
                    await WaitAsync(() => !b.IsTabletCameraViewActive && !b.IsTabletPowerTransitioning && !b.IsTabletCameraTransitioning, b, cancellation);
                }
                var walk = Object.FindFirstObjectByType<PlayerWalkModeController>();
                if (walk != null && walk.IsWalkMode && !walk.TryExitWalkMode()) throw new InvalidOperationException("散歩終了を開始できません");
                await WaitAsync(() => DebugConsoleGuard.GetBlockReason() == null, b, cancellation);
                await MoveHomeAsync(b, CatHomeLocation.Table, cancellation);
                return Result("return.initial", DebugScenarioStatus.Succeeded, "正式な散歩終了・電源終了・運搬を通してTable通常入力へ復帰", before, started);
            }
            catch (Exception e) when (e is InvalidOperationException || e is TimeoutException || e is OperationCanceledException || e is MissingReferenceException)
            { RecoverToTitle(b); return Result("return.initial", e is OperationCanceledException ? DebugScenarioStatus.Cancelled : DebugScenarioStatus.Failed, e.GetType().Name, before, started); }
            finally { IsRunning = false; }
        }
        internal static async UniTask MoveHomeAsync(NekolposGameBootstrap b, CatHomeLocation home, CancellationToken token)
        {
            var presentation = Object.FindFirstObjectByType<CatPresentationModeController>();
            if (presentation == null || presentation.PositionController == null) throw new InvalidOperationException("猫の正式位置Controllerが未接続です");
            if (presentation.PositionController.CurrentHomeLocation != home && !b.TryPlayConversationTimeline(home == CatHomeLocation.Table ? "MoveToTable" : "MoveToDesk"))
                throw new InvalidOperationException("正式な運搬入口が要求を拒否しました");
            await WaitAsync(() => !b.IsTransporting && presentation.PositionController.CurrentHomeLocation == home && DebugConsoleGuard.GetBlockReason() == null, b, token);
        }
        internal static async UniTask WaitAsync(Func<bool> predicate, NekolposGameBootstrap owner, CancellationToken token, float timeout = 30)
        {
            token.ThrowIfCancellationRequested();
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate())
            {
                token.ThrowIfCancellationRequested();
                if (owner == null || !owner.isActiveAndEnabled || !owner.HasEnteredGameplay) throw new OperationCanceledException("Scene退出");
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        private static void RecoverToTitle(NekolposGameBootstrap owner)
        {
            if (!DebugSaveSession.IsIsolated) return;
            DebugSaveSession.Finish();
            if (owner == null || !owner.HasEnteredGameplay) return;
            NekolposDebugConsole.Instance?.Close();
            OpenBetaPauseMenuController.ResetOpenBetaState();
            SceneManager.LoadScene(OpenBetaSceneNames.Main);
        }
        private static DebugScenarioResult Result(string id, DebugScenarioStatus status, string reason, DebugConsoleSnapshot before, float started)
        {
            DebugConsoleTrace.ScenarioOutcome((int)status);
            LastResult = new DebugScenarioResult { scenarioId = id, status = status.ToString(), reason = reason,
                correlationId = DebugConsoleTrace.CorrelationId, duration = started == 0 ? 0 : Time.realtimeSinceStartup - started, before = before, after = DebugConsoleSnapshot.Capture() };
            return LastResult;
        }
    }
}
#endif
