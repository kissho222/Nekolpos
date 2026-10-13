#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Nekolpos.ActionSystem;
using UnityEngine;
using UnityEngine.Playables;

namespace Nekolpos.System
{
    public static class DebugConsoleGuard
    {
        public static bool CanMutate(out string reason)
        {
            reason = GetBlockReason(); return reason == null;
        }
        public static string GetBlockReason()
        {
            if (!NekolposDebugConsole.CanDisplay()) return "ゲーム開始前・タイトル復帰中です";
            if (Time.timeScale <= 0f || SettingsPanel.IsOpen) return "Pause・設定画面を閉じてください";
            var pause = Object.FindFirstObjectByType<OpenBetaPauseMenuController>();
            if (pause != null && pause.IsPaused) return "Pause中です";
            var b = Object.FindFirstObjectByType<NekolposGameBootstrap>();
            if (b == null || b.IsTransporting || b.IsTabletCameraTransitioning || b.IsTabletPowerTransitioning) return "移動・タブレット遷移中です";
            if (b.IsTabletCameraViewActive) return "タブレット閲覧中です";
            var diary = Object.FindFirstObjectByType<DiaryCalendarController>();
            if (diary != null && (diary.IsNightDiaryActive || diary.IsWritingOrTransitioning)) return "日記記入・確定・画面遷移中です";
            var d = DialogueManager.Instance;
            if (d != null && (d.IsTimeFlowRunning || d.IsPlayerDefeatRunning || d.IsNightSleepRunning)) return "時間進行・就寝・死亡復帰中です";
            foreach (var fade in Object.FindObjectsByType<FadeController>(FindObjectsSortMode.None))
                if (!fade.IsTransparent) return "暗転・明転中です";
            foreach (var director in Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
                if (director.state == PlayState.Playing) return "Timeline再生中です";
            var walk = Object.FindFirstObjectByType<PlayerWalkModeController>();
            if (walk != null && (walk.IsWalkMode || walk.IsTransitioning || walk.IsInteractionMenuOpen)) return "通常会話待機へ戻ってください";
            var chat = Object.FindFirstObjectByType<ChatUIController>();
            if (chat == null || !chat.IsControlGuideInputActive) return "通常会話の入力待機中だけ変更できます";
            var learning = Object.FindFirstObjectByType<Nekolpos.Dialogue.UnknownWord.WordLearningController>();
            if (learning != null && learning.IsActive) return "言葉学習中です";
            return null;
        }
    }
}
#endif
