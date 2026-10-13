#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Nekolpos.TimeSystem;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>Fixtures reference existing authored CSV text. No dialogue or diary prose is generated.</summary>
    public static class DebugScenarioFixtures
    {
        public const string NormalDiaryKey = "PRD_CONVERSATION_RESULT_NORMAL";
        public const string SpecialDiaryKey = "TEMP_SPECIAL_DIARY_001";
        public static string CheckDiary(bool special)
        {
            if (UnityEngine.Object.FindFirstObjectByType<DiaryCalendarController>() == null || DialogueManager.Instance == null) return "日記・TimeFlow正本が未接続です";
            var normal = SystemTimedEventCatalog.Resolve(NormalDiaryKey);
            if (normal == null || normal.IsSpecialDiary || normal.DiaryVariants == null || normal.DiaryVariants.Count == 0) return "既存通常日記Fixtureがありません";
            if (special)
            {
                var pending = SystemTimedEventCatalog.Resolve(SpecialDiaryKey);
                if (pending == null || !pending.IsSpecialDiary || pending.DiaryVariants == null || pending.DiaryVariants.Count == 0) return "既存特別日記Fixtureがありません";
            }
            return null;
        }
        public static void PrepareDiary(TimeManager time, bool special)
        {
            if (!DebugSaveSession.CanRunScenarios) throw new InvalidOperationException("隔離必須");
            var normal = SystemTimedEventCatalog.Resolve(NormalDiaryKey);
            foreach (DayPeriod period in new[] { DayPeriod.Morning, DayPeriod.Afternoon, DayPeriod.Evening })
                if (!DiaryJournalStore.RecordVariants(normal.DiaryId, time.CurrentDay, period, false, normal.DiaryVariants,
                    normal.DiarySelectionMode, normal.Locale, value => DialogueManager.ResolveRuntimeText(value))) throw new InvalidOperationException("通常日記Fixtureを用意できません");
            if (special)
            {
                var pending = SystemTimedEventCatalog.Resolve(SpecialDiaryKey);
                if (!DiaryJournalStore.RecordVariants(pending.DiaryId, time.CurrentDay, DayPeriod.Evening, true, pending.DiaryVariants,
                    pending.DiarySelectionMode, pending.Locale, value => DialogueManager.ResolveRuntimeText(value))) throw new InvalidOperationException("特別日記Fixtureを用意できません");
            }
            // Absolute time is fixture setup only; entering Night always goes through TimeFlow.
            time.SetCurrentTime(time.CurrentDay, DayPeriod.Evening);
        }
    }
}
#endif
