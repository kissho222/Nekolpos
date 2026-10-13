using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nekolpos.System;
using Nekolpos.ActionSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nekolpos.TimeSystem.Editor
{
    public sealed class TimeFlowCoordinatorTests
    {
        [TestCase(TimeAdvanceReason.SystemEvent)]
        [TestCase(TimeAdvanceReason.UnknownWord)]
        [TestCase(TimeAdvanceReason.Food)]
        [TestCase(TimeAdvanceReason.Teaching)]
        [TestCase(TimeAdvanceReason.Action)]
        [TestCase(TimeAdvanceReason.Debug)]
        public void EveryReasonStopsAtNightAndUpdatesClockOnce(TimeAdvanceReason reason)
        {
            var root = new GameObject("TimeFlow clock");
            try
            {
                var clock = root.AddComponent<TimeManager>();
                clock.SetCurrentTime(clock.CurrentDay, DayPeriod.Evening);
                int day = clock.CurrentDay;
                int updates = 0;
                clock.OnTimeAdvanced += _ => updates++;
                var flow = new TimeFlowCoordinator();
                Assert.That(flow.TryBegin(), Is.True);
                Assert.That(flow.TryBegin(), Is.False);
                var result = flow.Advance(clock, new TimeAdvanceRequest(int.MaxValue, reason));
                Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Night));
                Assert.That(clock.CurrentDay, Is.EqualTo(day));
                Assert.That(result.AdvancedMinutes, Is.EqualTo(120));
                Assert.That(updates, Is.EqualTo(1));
                flow.End();
                Assert.That(flow.TryBegin(), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(DayPeriod.Afternoon)]
        [TestCase(DayPeriod.Evening)]
        public void DayTransitionsGreetBeforeFollowUpAndReturnOnce(DayPeriod period)
        {
            var flow = new TimeFlowCoordinator();
            flow.TryBegin();
            var order = new List<string>();
            flow.CompleteAsync(new TimeAdvanceResult((DayPeriod)((int)period - 1), period, 1, 1, 120),
                () => { order.Add("diary"); return UniTask.CompletedTask; },
                () => { order.Add("greeting"); return UniTask.CompletedTask; },
                () => { order.Add("followup"); return false; }, () => order.Add("input")).GetAwaiter().GetResult();
            Assert.That(order, Is.EqualTo(new[] { "greeting", "followup", "input" }));
        }

        [Test]
        public void NightNeverGreetsOrStartsFollowUpOrInputAndClaimsOnlyOncePerDay()
        {
            var flow = new TimeFlowCoordinator(); flow.TryBegin();
            var order = new List<string>();
            for (int i = 0; i < 2; i++)
                flow.CompleteAsync(new TimeAdvanceResult(DayPeriod.Evening, DayPeriod.Night, 3, 3, 120),
                    () => { order.Add("diary"); return UniTask.CompletedTask; },
                    () => { order.Add("greeting"); return UniTask.CompletedTask; },
                    () => { order.Add("followup"); return true; }, () => order.Add("input")).GetAwaiter().GetResult();
            Assert.That(order, Is.EqualTo(new[] { "diary" }));
            Assert.That(flow.TryClaimNightDiary(new TimeAdvanceResult(DayPeriod.Evening, DayPeriod.Night, 4, 4, 120)), Is.True);
        }

        [Test]
        public void FailedNightStartupAllowsRetryWithoutOpeningInput()
        {
            var flow = new TimeFlowCoordinator(); flow.TryBegin();
            var result = new TimeAdvanceResult(DayPeriod.Evening, DayPeriod.Night, 2, 2, 120);
            bool inputOpened = false;
            Assert.Throws<InvalidOperationException>(() => flow.CompleteAsync(result,
                () => throw new InvalidOperationException("Missing diary view"),
                () => UniTask.CompletedTask, null, () => inputOpened = true).GetAwaiter().GetResult());
            Assert.That(inputOpened, Is.False);
            Assert.That(flow.HasClaimedNightDiary(2), Is.False);
            Assert.That(flow.TryClaimNightDiary(result), Is.True);
        }

        [Test]
        public void InvalidRequestAndUnownedAdvanceFailEarly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeAdvanceRequest(-1, TimeAdvanceReason.Action));
            Assert.Throws<InvalidOperationException>(() => new TimeFlowCoordinator().Advance(null, new TimeAdvanceRequest(1, TimeAdvanceReason.Action)));
        }

        [UnityTest]
        public IEnumerator ActualEventEntrancesAllForceNightDiaryAndRejectRepeatedRequests()
        {
            DebugSaveSession.ArmNextPlay();
            yield return new EnterPlayMode();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string[] keys = { "Nekolpos.Diary.Drafts.v1", "Nekolpos.Diary.SpecialPending.v1", TimeManager.CurrentDayPrefsKey };
            bool[] present = Array.ConvertAll(keys, PlayerPrefs.HasKey);
            string[] saved = Array.ConvertAll(keys, key => PlayerPrefs.GetString(key));
            int savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            string[] deathKeys = { "Nekolpos.PlayerDefeat.LastReason", "Nekolpos.PlayerDefeat.By.Nekomata",
                "Nekolpos.PlayerDefeat.By.Predation", "Nekolpos.PlayerDefeat.By.Anger", "Nekolpos.PlayerDefeat.By.Accident" };
            bool[] deathPresent = Array.ConvertAll(deathKeys, PlayerPrefs.HasKey);
            int[] deathValues = Array.ConvertAll(deathKeys, key => PlayerPrefs.GetInt(key));
            string deathReason = PlayerPrefs.GetString(deathKeys[0]);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/MainScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity"));
            yield return new WaitForSecondsRealtime(.2f);
            var dm = Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
            var clock = Object.FindFirstObjectByType<TimeManager>();
            var diary = DiaryCalendarController.Instance;
            try
            {
                var bootstrap = Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
                bootstrap.enabled = false;
                dm.chatUI.SetTitleInputRouter(null);
                var timed = SystemTimedEventCatalog.Resolve("PRD_CONVERSATION_RESULT_NORMAL");
                Assert.That(timed, Is.Not.Null);
                for (int entry = 0; entry < 5; entry++)
                {
                    dm.ResetRuntimeStateForTitleRestart();
                    clock.SetCurrentTime(10 + entry, DayPeriod.Evening);
                    typeof(DiaryCalendarController).GetField("isNightDiaryActive", flags).SetValue(diary, false);
                    if (entry == 0) typeof(DialogueManager).GetMethod("RunSystemTimedEventAsync", flags).Invoke(dm,
                        new object[] { timed, null, null, false, default(global::System.Threading.CancellationToken) });
                    if (entry == 1) typeof(DialogueManager).GetMethod("RunFoodHungryCompletionAsync", flags).Invoke(dm, new object[] { null });
                    if (entry == 2) dm.RunTeachingTimePassage(string.Empty);
                    if (entry == 3) dm.DebugAdvanceToNextPeriod();
                    ActionData action = null;
                    if (entry == 4)
                    {
                        action = ScriptableObject.CreateInstance<ActionData>();
                        action.Initialize("flow-test", string.Empty, string.Empty, 120, Array.Empty<Nekolpos.StatusSystem.StatEffect>());
                        var actions = dm.GetComponent<ActionManager>();
                        typeof(ActionManager).GetMethod("EnsureInitialized", flags).Invoke(actions, null);
                        typeof(ActionManager).GetMethod("ExecuteActionDataAsync", flags).Invoke(actions,
                            new object[] { action, "flow-test", string.Empty, false, null });
                    }
                    float deadline = Time.realtimeSinceStartup + 20;
                    while (dm.IsTimeFlowRunning && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(dm.IsTimeFlowRunning, Is.False, "TimeFlow timed out for entrance " + entry);
                    Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Night));
                    Assert.That(clock.CurrentDay, Is.EqualTo(10 + entry));
                    if (entry == 0)
                        Assert.That(DiaryJournalStore.GetLatestNormalDraft(10, DayPeriod.Evening).diaryId, Is.EqualTo("PRD_CONVERSATION_RESULT_NORMAL"));
                    Assert.That((bool)typeof(DiaryCalendarController).GetField("isNightDiaryActive", flags).GetValue(diary), Is.True, "entrance " + entry);
                    int updates = 0; Action<TimeAdvanceResult> onUpdate = _ => updates++;
                    clock.OnTimeAdvanced += onUpdate;
                    dm.DebugAdvanceToNextPeriod();
                    yield return null;
                    clock.OnTimeAdvanced -= onUpdate;
                    Assert.That(updates, Is.Zero, "Already claimed night must not repeat clock/UI updates.");
                    if (action != null) Object.Destroy(action);
                }
                dm.ResetRuntimeStateForTitleRestart();
                clock.SetCurrentTime(20, DayPeriod.Evening);
                typeof(DiaryCalendarController).GetField("isNightDiaryActive", flags).SetValue(diary, false);
                dm.TriggerPlayerDefeat("Punch", timedEventKey: "action_angry_punch");
                dm.TriggerPlayerDefeat("Punch", timedEventKey: "action_angry_punch");
                Assert.That(dm.IsPlayerDefeatRunning, Is.True);
                float deathDeadline = Time.realtimeSinceStartup + 15;
                while (clock.CurrentDay == 20 && Time.realtimeSinceStartup < deathDeadline) yield return null;
                Assert.That(clock.CurrentDay, Is.EqualTo(21));
                Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Morning));
                Assert.That((bool)typeof(DiaryCalendarController).GetField("isNightDiaryActive", flags).GetValue(diary), Is.False);
                Assert.That(DiaryJournalStore.GetPendingSpecialEntries(), Has.Some.Matches<DiaryJournalStore.DiaryDraft>(draft => draft.diaryId == "action_angry_punch" && draft.occurredDay == 20));
                var fade = Object.FindFirstObjectByType<Nekolpos.ActionSystem.FadeController>();
                Assert.That(fade.IsOpaque, Is.True, "Day changes while fully covered, before sunrise/fade-in.");
                while (dm.IsPlayerDefeatRunning && Time.realtimeSinceStartup < deathDeadline) yield return null;
                Assert.That(dm.IsPlayerDefeatRunning, Is.False);
                Assert.That(fade.IsTransparent, Is.True);
                Assert.That(clock.CurrentDay, Is.EqualTo(21), "Repeated defeat must not advance two days.");
                dm.TriggerPlayerDefeat("Punch");
                yield return new WaitForSecondsRealtime(.1f);
                dm.ResetRuntimeStateForTitleRestart();
                Assert.That(dm.IsPlayerDefeatRunning, Is.False);
                Assert.That(fade.IsTransparent, Is.True);
                yield return new WaitForSecondsRealtime(.6f);
                Assert.That(clock.CurrentDay, Is.EqualTo(21), "Cancelled defeat must not update tomorrow later.");
                clock.SetCurrentTime(21, DayPeriod.Evening);
                dm.DebugAdvanceToNextPeriod();
                deathDeadline = Time.realtimeSinceStartup + 10;
                while (dm.IsTimeFlowRunning && Time.realtimeSinceStartup < deathDeadline) yield return null;
                Assert.That((bool)typeof(DiaryCalendarController).GetField("isNightDiaryActive", flags).GetValue(diary), Is.True);
                Assert.That((bool)typeof(DiaryCalendarController).GetField("confirmingSpecialEntries", flags).GetValue(diary), Is.True);
                Assert.That(DiaryJournalStore.HasPendingSpecialEntries, Is.True, "Pending survives until confirmation.");
            }
            finally
            {
                dm.ResetRuntimeStateForTitleRestart();
                for (int i = 0; i < keys.Length; i++)
                    if (present[i]) { if (i == 2) PlayerPrefs.SetInt(keys[i], savedDay); else PlayerPrefs.SetString(keys[i], saved[i]); }
                    else PlayerPrefs.DeleteKey(keys[i]);
                PlayerPrefs.Save();
                for (int i = 0; i < deathKeys.Length; i++)
                    if (!deathPresent[i]) PlayerPrefs.DeleteKey(deathKeys[i]);
                    else if (i == 0) PlayerPrefs.SetString(deathKeys[i], deathReason);
                    else PlayerPrefs.SetInt(deathKeys[i], deathValues[i]);
                PlayerPrefs.Save();
            }
            yield return new ExitPlayMode();
        }
    }
}
