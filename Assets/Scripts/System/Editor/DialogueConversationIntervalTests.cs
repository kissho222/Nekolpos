using System.Collections.Generic;
using System.Reflection;
using Nekolpos.Data;
using Nekolpos.StatusSystem;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueConversationIntervalTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject host;
        private DialogueManager dialogue;
        private StatusManager status;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Conversation interval tests");
            host.SetActive(false);
            dialogue = host.AddComponent<DialogueManager>();
            status = host.AddComponent<StatusManager>();
            typeof(DialogueManager).GetField("statusManager", PrivateInstance).SetValue(dialogue, status);
            foreach (StatusType type in global::System.Enum.GetValues(typeof(StatusType)))
                status.SetValue(type, 0, "Test");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(host);

        [TestCase("", "NORMAL")]
        [TestCase("Affection", "AFFECTION")]
        [TestCase("Concern", "CONCERN")]
        [TestCase("Hostility", "HOSTILITY")]
        [TestCase("Instinct", "INSTINCT")]
        [TestCase("Sadistic", "SADISTIC")]
        [TestCase("Obedience", "OBEDIENCE")]
        public void FiveCompletedConversations_SelectActualChangedStatus(string emotion, string expected)
        {
            for (int i = 1; i <= 5; i++)
            {
                SetPending();
                if (i == 2 && emotion.Length > 0)
                    Invoke("ApplyEmotionDelta", emotion, 10, "Test");
                string result = (string)Invoke("CompleteNormalConversation");
                Assert.That(result, i < 5 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_" + expected));
                Assert.That(Invoke("CompleteNormalConversation"), Is.Null, "Repeated completion notifications must not count twice.");
            }
            Assert.That(SystemTimedEventCatalog.Resolve("PRD_CONVERSATION_RESULT_" + expected), Is.Not.Null);
        }

        [Test]
        public void HighUnchangedAndClampedValues_DoNotSelectEmotionResult()
        {
            status.SetValue(StatusType.Hostility, 100, "Test");
            for (int i = 1; i <= 5; i++)
            {
                SetPending();
                Invoke("ApplyEmotionDelta", "Hostility", 10, "Test");
                Assert.That(Invoke("CompleteNormalConversation"), i < 5 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_NORMAL"));
            }
        }

        [Test]
        public void MultipleChanges_UseExistingValueAndMostRecentTiePriority()
        {
            status.SetValue(StatusType.Hostility, 100, "Test");
            SetPending();
            Invoke("ApplyEmotionDelta", "Affection", 20, "Test");
            Invoke("ApplyEmotionDelta", "Concern", 10, "Test");
            Assert.That(status.GetHighestPriorityStatus(new[] { StatusType.Affection, StatusType.Concern }), Is.EqualTo(StatusType.Affection));
            Invoke("ApplyEmotionDelta", "Concern", 10, "Test");
            for (int i = 1; i <= 5; i++)
            {
                SetPending();
                Assert.That(Invoke("CompleteNormalConversation"), i < 5 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_CONCERN"));
            }
        }

        [Test]
        public void NegativeChange_AndOpposingChangesStillCountAsActualChanges()
        {
            status.SetValue(StatusType.Affection, 50, "Test");
            SetPending();
            Invoke("ApplyEmotionDelta", "Affection", -10, "Test");
            Invoke("ApplyEmotionDelta", "Affection", 10, "Test");
            for (int i = 1; i <= 5; i++)
            {
                SetPending();
                Assert.That(Invoke("CompleteNormalConversation"), i < 5 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_AFFECTION"));
            }
        }

        [Test]
        public void Reset_StartsFreshIntervalAndClearsEarlierChanges()
        {
            SetPending();
            Invoke("ApplyEmotionDelta", "Affection", 10, "Test");
            Invoke("CompleteNormalConversation");
            Invoke("ResetNormalConversationInterval");
            for (int i = 1; i <= 5; i++)
            {
                SetPending();
                Assert.That(Invoke("CompleteNormalConversation"), i < 5 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_NORMAL"));
            }
        }

        [TestCase("Action")]
        [TestCase("Event")]
        [TestCase("Choice")]
        public void SpecialResponse_IsNotNormalConversation(string response)
        {
            Assert.That(IsNormal(new DialogueReactionData { LabelHash = 1, ResponseType = response }), Is.False);
        }

        [Test]
        public void GreetingTeachingTimelineAndResult_AreExcluded()
        {
            Assert.That(IsNormal(new DialogueReactionData { LabelHash = 1, IntentID = "GREETING" }), Is.False);
            Assert.That(IsNormal(new DialogueReactionData { LabelHash = 1, TeachingCategory = "Body" }), Is.False);
            Assert.That(IsNormal(new DialogueReactionData { LabelHash = 1, Animation = "timeline_sequence:MawStart" }), Is.False);
            Assert.That(IsNormal(new DialogueReactionData { LabelHash = 1, TimedEventKey = "PRD_CONVERSATION_RESULT_NORMAL" }), Is.False);
        }

        [Test]
        public void ActualIntegratedConversation_IsEligibleDespiteGeneratedTeachingEntryId()
        {
            var engine = host.AddComponent<DialogueEngine>();
            var data = host.AddComponent<Backgammon.Conversation.ConversationDataManager>();
            var source = data.GetActiveCsvSources().Find(s => s.Name == "DialoguePreview");
            typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", PrivateInstance).Invoke(engine, new object[] { source.Text, source.Name });
            int hash = (int)typeof(DialogueEngine).GetMethod("GetDeterministicHash", PrivateInstance).Invoke(engine, new object[] { "nail" });
            Assert.That(engine.TryGetPatternGroup(hash, "0", out var group), Is.True);
            Assert.That(group.Count, Is.GreaterThan(0));
            Assert.That((bool)typeof(DialogueManager).GetMethod("IsNormalConversation", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { group }), Is.True);
        }

        private void SetPending() => typeof(DialogueManager).GetField("pendingNormalConversation", PrivateInstance).SetValue(dialogue, true);
        private object Invoke(string name, params object[] arguments) => typeof(DialogueManager).GetMethod(name, PrivateInstance).Invoke(dialogue, arguments);
        private static bool IsNormal(DialogueReactionData reaction) => (bool)typeof(DialogueManager)
            .GetMethod("IsNormalConversation", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { new List<DialogueReactionData> { reaction } });
    }
}

namespace Nekolpos.System.Editor
{
    public sealed class DialogueConversationIntervalSceneTests
    {
        [UnityEngine.TestTools.UnityTest]
        public global::System.Collections.IEnumerator FiveActualGroups_ShowResultBeforeTimeAdvance_AndTitleCancelsNextResult()
        {
            DebugSaveSession.ArmNextPlay();
            yield return new UnityEngine.TestTools.EnterPlayMode();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            const string draftsKey = "Nekolpos.Diary.Drafts.v1";
            bool hadDrafts = PlayerPrefs.HasKey(draftsKey);
            string drafts = PlayerPrefs.GetString(draftsKey);
            bool hadDay = PlayerPrefs.HasKey(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey);
            int savedDay = PlayerPrefs.GetInt(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey);
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/MainScene.unity", new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return new WaitForSecondsRealtime(0.2f);
            var manager = Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
            try
            {
                var bootstrap = Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
                bootstrap.enabled = false;
                var chat = manager.chatUI;
                chat.SetTitleInputRouter(null);
                manager.ResetRuntimeStateForTitleRestart();
                var engine = Object.FindFirstObjectByType<DialogueEngine>(FindObjectsInactive.Include);
                int hash = (int)typeof(DialogueEngine).GetMethod("GetDeterministicHash", flags).Invoke(engine, new object[] { "nail" });
                Assert.That(engine.TryGetPatternGroup(hash, "0", out var group), Is.True);
                var time = Object.FindFirstObjectByType<Nekolpos.TimeSystem.TimeManager>();
                time.SetCurrentTime(2, Nekolpos.TimeSystem.DayPeriod.Morning);
                var handle = typeof(DialogueManager).GetMethod("HandleDialogue", flags);
                var count = typeof(DialogueManager).GetField("completedNormalConversations", flags);
                var pending = typeof(DialogueManager).GetField("pendingNormalConversation", flags);
                var cancellation = typeof(DialogueManager).GetField("conversationResultCancellation", flags);
                for (int input = 1; input <= 5; input++)
                {
                    handle.Invoke(manager, new object[] { group });
                    Assert.That((int)count.GetValue(manager), Is.EqualTo(input - 1), "Starting a group must not count it.");
                    for (int page = 0; page < group.Count; page++)
                    {
                        chat.SkipCurrentTyping();
                        yield return null;
                        manager.ProgressNextLine();
                        if (page < group.Count - 1)
                            Assert.That((int)count.GetValue(manager), Is.EqualTo(input - 1), "A page must not count as a conversation.");
                    }
                    Assert.That((int)count.GetValue(manager), Is.EqualTo(input));
                    Assert.That(time.CurrentPeriod, Is.EqualTo(Nekolpos.TimeSystem.DayPeriod.Morning));
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                CanvasGroup resultGroup = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var result = Object.FindFirstObjectByType<Nekolpos.ActionSystem.ResultTextUI>();
                    resultGroup = result != null ? (CanvasGroup)typeof(Nekolpos.ActionSystem.ResultTextUI).GetField("rootGroup", flags).GetValue(result) : null;
                    if (resultGroup != null && resultGroup.alpha > 0.99f && resultGroup.gameObject.activeInHierarchy) break;
                    yield return null;
                }
                Assert.That(resultGroup, Is.Not.Null);
                Assert.That(resultGroup.alpha, Is.GreaterThan(0.99f));
                Assert.That(time.CurrentPeriod, Is.EqualTo(Nekolpos.TimeSystem.DayPeriod.Morning), "The result must appear before time advances.");
                Assert.That(DiaryJournalStore.GetLatestNormalDraft(2, Nekolpos.TimeSystem.DayPeriod.Morning).diaryId, Is.EqualTo("PRD_CONVERSATION_RESULT_NORMAL"));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(time.CurrentPeriod, Is.EqualTo(Nekolpos.TimeSystem.DayPeriod.Morning));

                deadline = Time.realtimeSinceStartup + 12f;
                while (cancellation.GetValue(manager) != null && Time.realtimeSinceStartup < deadline)
                {
                    if (!(bool)pending.GetValue(manager))
                    {
                        chat.SkipCurrentTyping();
                        manager.ProgressNextLine();
                    }
                    yield return null;
                }
                Assert.That(cancellation.GetValue(manager), Is.Null);
                Assert.That(time.CurrentPeriod, Is.EqualTo(Nekolpos.TimeSystem.DayPeriod.Afternoon));
                Assert.That((int)count.GetValue(manager), Is.Zero);

                for (int input = 1; input <= 5; input++)
                {
                    handle.Invoke(manager, new object[] { group });
                    for (int page = 0; page < group.Count; page++) manager.ProgressNextLine();
                }
                Assert.That(cancellation.GetValue(manager), Is.Not.Null);
                manager.ResetRuntimeStateForTitleRestart();
                chat.ResetRuntimeStateForTitleRestart(true);
                yield return new WaitForSecondsRealtime(4f);
                Assert.That(time.CurrentPeriod, Is.EqualTo(Nekolpos.TimeSystem.DayPeriod.Afternoon), "A cancelled result must not advance time later.");
                Assert.That((int)count.GetValue(manager), Is.Zero);
            }
            finally
            {
                manager?.ResetRuntimeStateForTitleRestart();
                if (hadDrafts) PlayerPrefs.SetString(draftsKey, drafts); else PlayerPrefs.DeleteKey(draftsKey);
                if (hadDay) PlayerPrefs.SetInt(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey, savedDay); else PlayerPrefs.DeleteKey(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey);
                PlayerPrefs.Save();
                typeof(DiaryJournalStore).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
                ((global::System.Collections.IList)typeof(DiaryJournalStore).GetField("drafts", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
                ((global::System.Collections.IList)typeof(DiaryJournalStore).GetField("pendingSpecialEntries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
            }
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }
    }
}
