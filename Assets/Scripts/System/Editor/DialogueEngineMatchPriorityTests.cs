using System.Collections.Generic;
using System.Reflection;
using Nekolpos.Data;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueEngineMatchPriorityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void VulgarPatternSix_StartsOnFifthConsecutiveInput(bool encrypted)
        {
            GameObject host = new GameObject("Issue42 fifth input regression");
            host.SetActive(false);
            var randomState = UnityEngine.Random.state;
            try
            {
                var engine = host.AddComponent<DialogueEngine>();
                string csv = global::System.IO.File.ReadAllText("TalkSource/TalkCSV/DialoguePreview.csv");
                var preview = Nekolpos.EditorTools.DialogueDataLoader.LoadFromPath("TalkSource/TalkCSV/DialoguePreview.csv");
                var previewPatternSix = preview.Entries.FindAll(r => r.RegexId == "ひわい" && r.Pattern == "6");
                Assert.That(previewPatternSix.Count, Is.EqualTo(2));
                Assert.That(previewPatternSix.TrueForAll(r => r.SpeechControl == "RepeatEvent" && r.RepeatCount == 5 && r.RandomRepeatLimit == -1), Is.True);
                if (encrypted)
                {
                    var manager = host.AddComponent<Backgammon.Conversation.ConversationDataManager>();
                    csv = manager.GetActiveCsvSources().Find(s => s.Name == "DialoguePreview").Text;
                }
                int hash = LoadReactionCsv(engine, csv, "ひわい");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var database = (Dictionary<int, List<DialogueReactionData>>)typeof(DialogueEngine).GetField("reactionDatabase", flags).GetValue(engine);
                var patternSix = database[hash].FindAll(r => r.PatternID == "6");
                Assert.That(patternSix.Count, Is.EqualTo(2));
                Assert.That(patternSix.TrueForAll(r => r.SpeechControl == SpeechControlType.RepeatEvent && r.RepeatCount == 5), Is.True);

                // Seed 8 reproduced Pattern 6 on the second input before the data correction.
                for (int seed = 0; seed < 32; seed++)
                {
                    engine.ResetRuntimeConversationState();
                    UnityEngine.Random.InitState(seed);
                    for (int input = 1; input <= 6; input++)
                    {
                        Assert.That(engine.TryDetermineReactionGroup(hash, out var group), Is.True);
                        Assert.That(group[0].PatternID, input < 5 ? Is.Not.EqualTo("6") : Is.EqualTo("6"), $"seed={seed}, input={input}");
                        Assert.That(group.Count, Is.EqualTo(2));
                        Assert.That(group[0].Order, Is.EqualTo(0));
                        Assert.That(group[1].Order, Is.EqualTo(1));
                    }
                }

                int otherHash = (int)typeof(DialogueEngine).GetMethod("GetDeterministicHash", flags).Invoke(engine, new object[] { "nail" });
                Assert.That(engine.TryDetermineReactionGroup(otherHash, out _), Is.True);
                Assert.That(engine.TryDetermineReactionGroup(hash, out var restarted), Is.True);
                Assert.That(restarted[0].PatternID, Is.Not.EqualTo("6"), "Changing the topic resets the consecutive input count.");
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                Object.DestroyImmediate(host);
            }
        }

        [TestCase("", -1, 3)]
        [TestCase("-1", -1, 3)]
        [TestCase("invalid", -1, 3)]
        [TestCase("-5", -1, 3)]
        [TestCase("0", 0, 0)]
        [TestCase("1", 1, 1)]
        [TestCase("4", 4, 4)]
        [TestCase("5", 5, 5)]
        public void RandomRepeatLimit_PreservesEditorRuntimeParityAndSequenceBoundary(string value, int loaded, int randomInputs)
        {
            string csv = "regex_id,regex_jp,output_ja,pattern,order,SpeechControl,random_repeat_limit\n" +
                         $"limit,limit,,0,0,Random,{value}\n" +
                         "limit,limit,,1,0,Sequence,\n";
            string path = global::System.IO.Path.GetTempFileName();
            GameObject host = new GameObject("Issue42 limit boundary regression");
            host.SetActive(false);
            var randomState = UnityEngine.Random.state;
            try
            {
                global::System.IO.File.WriteAllText(path, csv);
                var preview = Nekolpos.EditorTools.DialogueDataLoader.LoadFromPath(path);
                Assert.That(preview.Entries[0].RandomRepeatLimit, Is.EqualTo(loaded));
                var engine = host.AddComponent<DialogueEngine>();
                int hash = LoadReactionCsv(engine, csv, "limit");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var database = (Dictionary<int, List<DialogueReactionData>>)typeof(DialogueEngine).GetField("reactionDatabase", flags).GetValue(engine);
                Assert.That(database[hash][0].RandomRepeatLimit, Is.EqualTo(loaded));
                for (int input = 1; input <= randomInputs + 2; input++)
                {
                    Assert.That(engine.TryDetermineReactionGroup(hash, out var group), Is.True);
                    Assert.That(group[0].PatternID, Is.EqualTo(input <= randomInputs ? "0" : "1"));
                }
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                Object.DestroyImmediate(host);
                global::System.IO.File.Delete(path);
            }
        }

        private static int LoadReactionCsv(DialogueEngine engine, string csv, string label)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.That((bool)typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", flags).Invoke(engine, new object[] { csv, "DialoguePreview" }), Is.True);
            return (int)typeof(DialogueEngine).GetMethod("GetDeterministicHash", flags).Invoke(engine, new object[] { label });
        }

        [Test]
        public void VulgarDestination_HasReactionInActualEncryptedDialogue()
        {
            GameObject host = new GameObject("Vulgar actual data regression");
            host.SetActive(false);
            try
            {
                var engine = host.AddComponent<DialogueEngine>();
                var manager = host.AddComponent<Backgammon.Conversation.ConversationDataManager>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo loader = typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", flags);
                var source = manager.GetActiveCsvSources().Find(s => s.Name == "DialoguePreview");
                Assert.That(source, Is.Not.Null);
                Assert.That((bool)loader.Invoke(engine, new object[] { source.Text, source.Name }), Is.True);
                int hash = (int)typeof(DialogueEngine).GetMethod("GetDeterministicHash", flags).Invoke(engine, new object[] { "ひわい" });
                var database = (Dictionary<int, List<DialogueReactionData>>)typeof(DialogueEngine).GetField("reactionDatabase", flags).GetValue(engine);
                Assert.That(database.ContainsKey(hash), Is.True);
                Assert.That(database[hash].Count, Is.EqualTo(14));
                Assert.That(engine.TryDetermineReactionGroup(hash, out var group), Is.True);
                Assert.That(group, Is.Not.Empty);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void IsBetterMatchCandidate_PrefersLongerMatchOverHigherPriorityShortMatch()
        {
            RegexPatternData longerQuestionPattern = new RegexPatternData
            {
                SourceRegex = "(は|って)(なんで|どうして|なぜ)?かしこいの？",
                Priority = 0,
                Sensitivity = "SAFE"
            };
            RegexPatternData shorterSleepPattern = new RegexPatternData
            {
                SourceRegex = "ねる",
                Priority = 999,
                Sensitivity = "SAFE"
            };

            MethodInfo method = typeof(DialogueEngine).GetMethod(
                "IsBetterMatchCandidate",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            bool selectsLongerQuestion = (bool)method.Invoke(
                null,
                new object[]
                {
                    longerQuestionPattern,
                    11,
                    shorterSleepPattern,
                    2
                });

            Assert.That(selectsLongerQuestion, Is.True);
        }

        [Test]
        public void LoadVulgarStyleCsv_UsesTheSecondColumnAsTheDestinationLabel()
        {
            GameObject host = new GameObject("DialogueEngineVulgarCsvTest");
            host.SetActive(false);
            try
            {
                DialogueEngine engine = host.AddComponent<DialogueEngine>();
                MethodInfo loadMethod = typeof(DialogueEngine).GetMethod(
                    "LoadVulgarStyleCsv",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo hashMethod = typeof(DialogueEngine).GetMethod(
                    "GetDeterministicHash",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo labelsField = typeof(DialogueEngine).GetField(
                    "vulgarLabelHashesByWord",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(loadMethod, Is.Not.Null);
                Assert.That(hashMethod, Is.Not.Null);
                Assert.That(labelsField, Is.Not.Null);

                loadMethod.Invoke(engine, new object[] { "ちんちん,ひわい\nまんこ,ひわい", "test" });
                var labelHashes = (Dictionary<string, int>)labelsField.GetValue(engine);
                int hiwaiHash = (int)hashMethod.Invoke(engine, new object[] { "ひわい" });

                Assert.That(labelHashes["ちんちん"], Is.EqualTo(hiwaiHash));
                Assert.That(labelHashes["まんこ"], Is.EqualTo(hiwaiHash));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
