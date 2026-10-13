using Backgammon.Conversation;
using Nekolpos.StatusSystem;
using NUnit.Framework;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using Nekolpos.TimeSystem;
using Nekolpos.Data;
using Object = UnityEngine.Object;

namespace Nekolpos.Tests.Editor
{
    public sealed class StatusManagerTests
    {
        private bool hadSave, hadDay;
        private string saved;
        private int savedDay;
        [SetUp] public void PreserveProgress()
        {
            hadSave = PlayerPrefs.HasKey(StatusManager.SaveKey); saved = PlayerPrefs.GetString(StatusManager.SaveKey);
            hadDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey); savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            PlayerPrefs.DeleteKey(StatusManager.SaveKey); PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, 1);
        }
        [TearDown] public void RestoreProgress()
        {
            if (hadSave) PlayerPrefs.SetString(StatusManager.SaveKey, saved); else PlayerPrefs.DeleteKey(StatusManager.SaveKey);
            if (hadDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay); else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
        }
        private static void Invoke(StatusManager manager, string method, params object[] args)
            => typeof(StatusManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, args);

        [Test] public void DefaultsAndBaselineComeFromConfigurableAsset()
        {
            var go = new GameObject("BaselineTest"); var asset = ScriptableObject.CreateInstance<CatDataSO>();
            try
            {
                var manager = go.AddComponent<StatusManager>();
                manager.ConfigureInitialData(asset, true);
                int[] expected = { 50, 30, 30, 0, 30, 0 };
                foreach (StatusType type in Enum.GetValues(typeof(StatusType)))
                { Assert.That(manager.GetValue(type), Is.EqualTo(expected[(int)type])); Assert.That(manager.GetBaseline(type), Is.EqualTo(expected[(int)type])); }
                asset.affectionLevel = 42; Assert.That(manager.GetBaseline(StatusType.Affection), Is.EqualTo(42));
                Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(50));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(asset); }
        }

        [Test] public void NaturalDecayHonorsGraceDaysAndDoesNotDoubleApplyAfterReload()
        {
            var go = new GameObject("DecayTest");
            try
            {
                var manager = go.AddComponent<StatusManager>(); manager.SetValue(StatusType.Affection, 90);
                manager.ProcessDateUpdate(2); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(90));
                manager.ProcessDateUpdate(3); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(80));
                manager.ProcessDateUpdate(3); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(80));
                Invoke(manager, "SaveState"); string json = PlayerPrefs.GetString(StatusManager.SaveKey);
                manager.SetValue(StatusType.Affection, 1);
                Invoke(manager, "RestoreState", json, 3);
                manager.ProcessDateUpdate(3); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(80));
                manager.ProcessDateUpdate(6); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(50));
                manager.ProcessDateUpdate(7); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(50));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void NaturalRecoveryDoesNotCountAsRiseAndDecreasesDoNotResetGrace()
        {
            var go = new GameObject("RecoveryTest");
            try
            {
                var manager = go.AddComponent<StatusManager>(); manager.SetValue(StatusType.Concern, 5);
                manager.ProcessDateUpdate(3); Assert.That(manager.GetValue(StatusType.Concern), Is.EqualTo(15));
                manager.ProcessDateUpdate(4); Assert.That(manager.GetValue(StatusType.Concern), Is.EqualTo(25));
                manager.SetValue(StatusType.Concern, 3);
                manager.ProcessDateUpdate(5); Assert.That(manager.GetValue(StatusType.Concern), Is.EqualTo(13));
                manager.ProcessDateUpdate(7); Assert.That(manager.GetValue(StatusType.Concern), Is.EqualTo(30));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void RiseResetsGraceButLoweringDoesNotChangeTiePriority()
        {
            var go = new GameObject("RiseTest");
            try
            {
                var manager = go.AddComponent<StatusManager>(); manager.SetValue(StatusType.Affection, 90); manager.SetValue(StatusType.Concern, 80);
                manager.SetValue(StatusType.Affection, 80);
                Assert.That(manager.RepresentativeState.Primary, Is.EqualTo(StatusType.Concern));
                PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, 3);
                manager.SetValue(StatusType.Affection, 85);
                manager.ProcessDateUpdate(4); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(85));
                manager.ProcessDateUpdate(5); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(75));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void ValuesOnlySaveMigratesWithoutOverwritingOrRetroactiveDecay()
        {
            var go = new GameObject("MigrationTest");
            try
            {
                var manager = go.AddComponent<StatusManager>();
                string json = "{\"version\":1,\"statuses\":[{\"type\":0,\"value\":93},{\"type\":1,\"value\":12},{\"type\":2,\"value\":14},{\"type\":3,\"value\":7},{\"type\":4,\"value\":11},{\"type\":5,\"value\":9}]}";
                manager.EnsureInitialized();
                Invoke(manager, "RestoreState", json, 20);
                manager.ProcessDateUpdate(20); manager.ProcessDateUpdate(21);
                Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(93));
                manager.ProcessDateUpdate(22); Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(83));
                Assert.That(() => Invoke(manager, "RestoreState", "{\"version\":99}", 20), Throws.TypeOf<TargetInvocationException>());
                Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(83));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void CalendarUpdateAppliesRecoveryBeforeTimeSubscribers()
        {
            var go = new GameObject("CalendarStatusTest");
            try
            {
                var time = go.AddComponent<TimeManager>();
                time.SetCurrentTime(1, DayPeriod.Morning);
                var manager = go.AddComponent<StatusManager>(); manager.SetValue(StatusType.Affection, 90);
                int observed = -1; time.OnTimeAdvanced += result => observed = manager.GetValue(StatusType.Affection);
                time.AdvanceToNextMorning(); Assert.That(observed, Is.EqualTo(90));
                time.AdvanceToNextMorning(); Assert.That(observed, Is.EqualTo(80));
                time.SetCurrentTime(3, DayPeriod.Morning); Assert.That(observed, Is.EqualTo(80));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void MissingStateDoesNotBecomeTrueThroughNegation()
        {
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(new ConversationGameState(), "!PsychologyState == \"AFFECTION_CONCERN\""), Is.False);
        }

        [TestCase("PsychologyState == \"NORMAL\"", "NORMAL", true)]
        [TestCase("!PsychologyState == \"AFFECTION_CONCERN\"", "CONCERN_AFFECTION", false)]
        [TestCase("!PsychologyState == \"AFFECTION_CONCERN\"", "HOSTILITY", true)]
        [TestCase("PsychologyState != \"AFFECTION_CONCERN\"", "AFFECTION_CONCERN", false)]
        [TestCase("PsychologyState == \"CONCERN_AFFECTION\"", "AFFECTION_CONCERN", true)]
        public void RepresentativeConditionsPreserveNegationAndIgnorePairOrder(string expression, string actual, bool expected)
        {
            var state = new ConversationGameState(); state.SetString("PsychologyState", actual);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, expression), Is.EqualTo(expected));
        }

        [Test]
        public void SetValue_ChangesOnlyRequestedStatus_AndProjectsToConversationState()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                ConversationGameStateManager conversationState = gameObject.AddComponent<ConversationGameStateManager>();
                StatusManager manager = gameObject.AddComponent<StatusManager>();

                int previousHostility = manager.GetValue(StatusType.Hostility);
                manager.SetValue(StatusType.Affection, 80, "Test", "single-status-change");

                Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(80));
                Assert.That(manager.GetValue(StatusType.Hostility), Is.EqualTo(previousHostility));
                Assert.That(conversationState.State.TryGetInt("Affection", out int projectedAffection), Is.True);
                Assert.That(projectedAffection, Is.EqualTo(80));
                Assert.That(conversationState.State.TryGetInt("affection", out int projectedAffectionLowercase), Is.True);
                Assert.That(projectedAffectionLowercase, Is.EqualTo(80));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RepresentativeState_UsesTopTwoValues_AndPrefersMostRecentlyChangedTie()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                ConversationGameStateManager conversationState = gameObject.AddComponent<ConversationGameStateManager>();
                StatusManager manager = gameObject.AddComponent<StatusManager>();
                manager.SetValue(StatusType.Affection, 80, "Test");
                manager.SetValue(StatusType.Hostility, 80, "Test");
                manager.SetValue(StatusType.Concern, 70, "Test");

                StatusRepresentativeState state = manager.GetRepresentativeState();

                Assert.That(state.Primary, Is.EqualTo(StatusType.Hostility));
                Assert.That(state.Secondary, Is.EqualTo(StatusType.Affection));
                Assert.That(state.DisplayName, Is.EqualTo("Hostility + Affection"));
                Assert.That(conversationState.State.TryGetString("PsychologyState", out string projectedState), Is.True);
                Assert.That(projectedState, Is.EqualTo("AFFECTION_HOSTILITY"));
                Assert.That(
                    ConversationGameStateConditionEvaluator.Evaluate(
                        conversationState.State,
                        "PsychologyState == \"AFFECTION_HOSTILITY\""),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetValue_ClampsAndRetainsChangeMetadata()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                StatusManager manager = gameObject.AddComponent<StatusManager>();
                StatusChangeResult result = manager.SetValue(StatusType.Instinct, 140, "Yarn", "TestNode");

                Assert.That(result.PreviousValue, Is.EqualTo(0));
                Assert.That(result.CurrentValue, Is.EqualTo(100));
                Assert.That(result.Delta, Is.EqualTo(100));
                Assert.That(result.Reason, Is.EqualTo("Yarn"));
                Assert.That(result.SourceId, Is.EqualTo("TestNode"));
                Assert.That(manager.ChangeHistory.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
