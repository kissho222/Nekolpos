using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Nekolpos.System;

namespace Nekolpos.TimeSystem
{
    public enum TimeAdvanceReason { SystemEvent, UnknownWord, Food, Teaching, Action, Debug }

    public readonly struct TimeAdvanceRequest
    {
        public TimeAdvanceRequest(int minutes, TimeAdvanceReason reason)
        {
            if (minutes < 0) throw new ArgumentOutOfRangeException(nameof(minutes));
            Minutes = minutes;
            Reason = reason;
        }
        public int Minutes { get; }
        public TimeAdvanceReason Reason { get; }
    }

    /// <summary>One session's advance ownership and end-of-day gate. Death/sleep use the explicit morning route.</summary>
    public sealed class TimeFlowCoordinator
    {
        public bool IsRunning { get; private set; }
        private int nightDiaryDay;
        public bool HasClaimedNightDiary(int day) => nightDiaryDay == day;

        public bool TryBegin()
        {
            if (IsRunning) return false;
            IsRunning = true;
            return true;
        }

        public void End() => IsRunning = false;
        public void Reset() { IsRunning = false; nightDiaryDay = 0; }

        public TimeAdvanceResult Advance(TimeManager clock, TimeAdvanceRequest request)
        {
            if (!IsRunning) throw new InvalidOperationException("Time advance requires flow ownership.");
            // The remainder is deliberately discarded: night ends the current day, regardless of event length.
            int minutes = Mathf.Min(request.Minutes, clock.MinutesUntilNight);
            return clock.AdvanceTime(minutes);
        }

        public bool TryClaimNightDiary(TimeAdvanceResult result)
        {
            if (result.CurrentPeriod != DayPeriod.Night || nightDiaryDay == result.CurrentDay) return false;
            nightDiaryDay = result.CurrentDay;
            return true;
        }

        public static bool TryGetTransitionGreeting(TimeAdvanceResult result, out string greeting)
        {
            if (!result.PeriodChanged)
            {
                greeting = string.Empty;
                return false;
            }

            switch (result.CurrentPeriod)
            {
                case DayPeriod.Morning:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingMorningKey,
                        "おはよう！朝だね。今日はなにしようか。");
                    return true;
                case DayPeriod.Afternoon:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingDayKey,
                        "昼だね。お腹空いてない？");
                    return true;
                case DayPeriod.Evening:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingEveningKey,
                        "夕方だね。やり忘れたことはない？");
                    return true;
                default:
                    greeting = string.Empty;
                    return false;
            }
        }

        public async UniTask CompleteAsync(TimeAdvanceResult result, Func<UniTask> beginNight,
            Func<UniTask> greeting, Func<bool> followUp, Action returnToInput)
        {
            if (result.CurrentPeriod == DayPeriod.Night)
            {
                if (TryClaimNightDiary(result))
                {
                    try { await beginNight(); }
                    catch { nightDiaryDay = 0; throw; }
                }
                return;
            }
            if (result.PeriodChanged) await greeting();
            // Release before synchronous follow-up: it may legitimately request the next action.
            End();
            if (followUp == null || !followUp()) returnToInput();
        }
    }
}
