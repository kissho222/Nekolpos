using System.Collections.Generic;
using System.Reflection;
using Nekolpos.StatusSystem;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.System.Editor
{
    public sealed class CommandToneInputTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase("待て")]
        [TestCase("来い")]
        [TestCase("行け")]
        [TestCase("黙れ")]
        [TestCase("やめろ")]
        [TestCase("しろ")]
        [TestCase("せよ")]
        [TestCase("やれ")]
        [TestCase("そこにいろ")]
        [TestCase("触るな")]
        [TestCase("動くな")]
        [TestCase("片付けなさい")]
        [TestCase("見ておけ")]
        [TestCase("持ってきてくれ")]
        [TestCase("命令を聞け")]
        [TestCase("  待て！\n来い。　")]
        [TestCase("待て、そして来い")]
        public void ExplicitCommands_AreDetected(string input) => Assert.That(CommandToneDetector.IsCommand(input), Is.True);

        [TestCase("命令を聞いて")]
        [TestCase("待ってください")]
        [TestCase("来てほしい")]
        [TestCase("手伝ってちょうだい")]
        [TestCase("やってごらん")]
        [TestCase("見て")]
        [TestCase("帰ろ")]
        [TestCase("面白いだろ")]
        [TestCase("彼は待てと言った")]
        [TestCase("「待て」")]
        [TestCase("『来い』と言われた")]
        [TestCase("\"行け\"という命令形")]
        [TestCase("待てという言葉")]
        [TestCase("お疲れ")]
        [TestCase("これは綺麗")]
        [TestCase("持ってきてくれる？")]
        [TestCase("動くなら教えて")]
        [TestCase("「待て")]
        [TestCase("")]
        [TestCase(null)]
        public void RequestsProseAndQuotes_DoNotAdd(string input) => Assert.That(CommandToneDetector.IsCommand(input), Is.False);

        [TestCase("命令を聞いて", 10)]
        [TestCase("命令を聞け", 20)]
        public void DedicatedEmotion_IsIndependentOfAutomaticChange(string input, int expected)
        {
            WithStatus(status =>
            {
                status.SetValue(StatusType.Obedience, 0, "Test");
                new CommandToneInput(input).TryApply(status);
                status.ApplyDelta(StatusType.Obedience, 10, "Dialogue", "DedicatedCommand");
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(expected));
            });
        }

        [Test]
        public void SubmissionIsConsumedOnce_NewIdenticalSubmissionsAlwaysApply_AndClamp()
        {
            WithStatus(status =>
            {
                status.SetValue(StatusType.Obedience, 0, "Test");
                var input = new CommandToneInput("待て、来い、動くな");
                Assert.That(input.TryApply(status), Is.True);
                Assert.That(input.TryApply(status), Is.False);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(10));
                var change = status.ChangeHistory[status.ChangeHistory.Count - 1];
                Assert.That(change.Reason, Is.EqualTo("CommandTone"));
                Assert.That(change.SourceId, Does.StartWith("FreeInput.CommandTone:"));
                for (int i = 0; i < 12; i++) new CommandToneInput("待て、来い、動くな").TryApply(status);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(100));
                Assert.That(new CommandToneInput("待て").TryApply(status), Is.False);
                foreach (var type in new[] { StatusType.Affection, StatusType.Hostility, StatusType.Concern, StatusType.Sadistic, StatusType.Instinct })
                    Assert.That(status.GetValue(type), Is.EqualTo(status.GetBaseline(type)));
            });
        }

        [Test]
        public void AutomaticChange_UsesNormalInterval_AndIgnoresRepeatedEvaluation()
        {
            WithStatus(status =>
            {
                var dialogue = status.gameObject.AddComponent<DialogueManager>();
                typeof(DialogueManager).GetField("statusManager", Private).SetValue(dialogue, status);
                var input = new CommandToneInput("待て");
                for (int i = 0; i < 5; i++)
                {
                    typeof(DialogueManager).GetField("pendingNormalConversation", Private).SetValue(dialogue, true);
                    typeof(DialogueManager).GetMethod("ApplySubmittedCommand", Private).Invoke(dialogue, new object[] { input });
                    var result = typeof(DialogueManager).GetMethod("CompleteNormalConversation", Private).Invoke(dialogue, null);
                    Assert.That(result, i < 4 ? Is.Null : Is.EqualTo("PRD_CONVERSATION_RESULT_OBEDIENCE"));
                }
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(40));
            });
        }

        [Test]
        public void CommandRise_PreservesRepresentativeState_AndNaturalDecay()
        {
            WithStatus(status =>
            {
                status.GetValue(StatusType.Obedience);
                typeof(StatusManager).GetField("lastDecayDay", Private).SetValue(status, 1);
                for (int i = 0; i < 4; i++) new CommandToneInput("待て").TryApply(status);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(70));
                Assert.That(status.RepresentativeState.Primary, Is.EqualTo(StatusType.Obedience));
                var days = (Dictionary<StatusType, int>)typeof(StatusManager).GetField("lastRiseDays", Private).GetValue(status);
                days[StatusType.Obedience] = 1;
                status.ProcessDateUpdate(2);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(70));
                status.ProcessDateUpdate(3);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(60));
                status.ProcessDateUpdate(6);
                Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(30));
            });
        }

        [TestCase("命令を聞いて", 10)]
        [TestCase("命令を聞け", 20)]
        public void AuthoredCommandMatching_StaysIdentical_AndReevaluationDoesNotCreateSubmission(string input, int expected)
        {
            WithStatus(status =>
            {
                status.SetValue(StatusType.Obedience, 0, "Test");
                var engine = status.gameObject.AddComponent<DialogueEngine>();
                var source = global::System.IO.File.ReadAllText("TalkSource/TalkCSV/DialoguePreview.csv");
                Assert.That(typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", Private).Invoke(engine, new object[] { source, "DialoguePreview" }), Is.True);
                string selected = null;
                int count = 0;
                engine.OnDialogueDetermined += group =>
                {
                    Assert.That(group, Is.Not.Empty);
                    Assert.That(group.Count, Is.EqualTo(3));
                    Assert.That(group[0].PatternID, Is.EqualTo(count == 0 ? "1" : "2"));
                    string key = group[0].SourceRegexLabel;
                    if (selected == null) selected = key;
                    else Assert.That(key, Is.EqualTo(selected));
                    if (count++ == 0)
                    {
                        Assert.That(engine.ActiveSubmittedCommand, Is.Not.Null);
                        engine.ActiveSubmittedCommand.TryApply(status);
                        Assert.That(group[0].EmotionChangeType.ToLowerInvariant(), Is.EqualTo("obedience").Or.EqualTo("submission"));
                        status.ApplyDelta(StatusType.Obedience, group[0].EmotionChangeValue * 10, "Dialogue", group[0].PatternID);
                    }
                    else Assert.That(engine.ActiveSubmittedCommand, Is.Null);
                };
                var random = UnityEngine.Random.state;
                try
                {
                    engine.ProcessSubmittedFreeInput(input);
                    Assert.That(count, Is.EqualTo(1));
                    Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(expected));
                    engine.ProcessInput(input);
                    Assert.That(count, Is.EqualTo(2));
                    Assert.That(status.GetValue(StatusType.Obedience), Is.EqualTo(expected));
                }
                finally { UnityEngine.Random.state = random; }
                Assert.That(engine.ActiveSubmittedCommand, Is.Null);
            });
        }

        private static void WithStatus(global::System.Action<StatusManager> test)
        {
            var host = new GameObject("Command tone test");
            host.SetActive(false);
            try { test(host.AddComponent<StatusManager>()); }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
