using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.System;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    public sealed class DialogueAuthoringPhaseThreeTests
    {
        [Test]
        public void FullSourceBundleRoundTripsAndMatchesEveryLegacyRuntimeField()
        {
            DialogueAuthoringProject project = DialogueLegacyImporter.ImportFile();
            DialogueCompiledRuntimeBundle bundle = DialogueCompiledBundleCompiler.Compile(project);
            string json = JsonUtility.ToJson(bundle);

            Assert.That(DialogueCompiledRuntimeLoader.TryParse(
                json, project.originalCsv, "DialoguePreview", out DialogueCompiledRuntimeBundle metadata,
                out List<RegexPatternData> compiledPatterns,
                out Dictionary<int, List<DialogueReactionData>> compiledReactions, out string error), Is.True, error);
            Assert.That(metadata.patterns.Count, Is.EqualTo(142));
            Assert.That(metadata.reactions.Count, Is.EqualTo(750));

            using LegacyRuntimeProbe legacy = new LegacyRuntimeProbe(project.originalCsv);
            AssertPublicFieldsEqual(legacy.Patterns, compiledPatterns, nameof(RegexPatternData));
            Assert.That(compiledReactions.Keys.OrderBy(value => value), Is.EqualTo(legacy.Reactions.Keys.OrderBy(value => value)));
            foreach (int key in legacy.Reactions.Keys)
                AssertPublicFieldsEqual(legacy.Reactions[key], compiledReactions[key], nameof(DialogueReactionData) + " label=" + key);
        }

        [Test]
        public void LoaderRejectsStaleMalformedAndDuplicateDataWithoutPartialResults()
        {
            DialogueAuthoringProject project = DialogueLegacyImporter.ImportFile();
            DialogueCompiledRuntimeBundle bundle = DialogueCompiledBundleCompiler.Compile(project);
            string json = JsonUtility.ToJson(bundle);

            Assert.That(DialogueCompiledRuntimeLoader.TryParse(json, project.originalCsv + "\n", "DialoguePreview", out _, out var stalePatterns, out var staleReactions, out string staleError), Is.False);
            Assert.That(staleError, Does.Contain("stale"));
            Assert.That(stalePatterns, Is.Null); Assert.That(staleReactions, Is.Null);

            bundle.formatVersion = 99;
            Assert.That(DialogueCompiledRuntimeLoader.TryParse(JsonUtility.ToJson(bundle), project.originalCsv, "DialoguePreview", out _, out _, out _, out string versionError), Is.False);
            Assert.That(versionError, Does.Contain("version"));

            bundle.formatVersion = DialogueCompiledRuntimeBundle.CurrentFormatVersion;
            bundle.reactions.Add(bundle.reactions[0]);
            Assert.That(DialogueCompiledRuntimeLoader.TryParse(JsonUtility.ToJson(bundle), project.originalCsv, "DialoguePreview", out _, out _, out _, out string duplicateError), Is.False);
            Assert.That(duplicateError, Does.Contain("duplicate line ID"));
        }

        [Test]
        public void DevelopmentFlagCanRollbackToLegacyImmediately()
        {
            int previous = PlayerPrefs.GetInt(DialogueRuntimeSourceSelection.PlayerPrefsKey, 0);
            try
            {
                DialogueRuntimeSourceSelection.SetDevelopmentMode(DialogueRuntimeSourceMode.CompiledBundle);
                Assert.That(DialogueRuntimeSourceSelection.RequestedMode, Is.EqualTo(DialogueRuntimeSourceMode.CompiledBundle));
                DialogueRuntimeSourceSelection.SetDevelopmentMode(DialogueRuntimeSourceMode.LegacyCsv);
                Assert.That(DialogueRuntimeSourceSelection.RequestedMode, Is.EqualTo(DialogueRuntimeSourceMode.LegacyCsv));
            }
            finally
            {
                PlayerPrefs.SetInt(DialogueRuntimeSourceSelection.PlayerPrefsKey, previous);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void CompiledLoaderPerformanceIsMeasuredOnFullSource()
        {
            DialogueAuthoringProject project = DialogueLegacyImporter.ImportFile();
            string json = DialogueCompiledBundleCompiler.CompileJson(project);
            var timer = Stopwatch.StartNew();
            const int iterations = 10;
            for (int i = 0; i < iterations; i++)
            {
                bool success = DialogueCompiledRuntimeLoader.TryParse(json, project.originalCsv, "DialoguePreview", out _, out _, out _, out string error);
                Assert.That(success, Is.True, error);
            }
            timer.Stop();
            double averageMilliseconds = timer.Elapsed.TotalMilliseconds / iterations;
            TestContext.Progress.WriteLine($"Dialogue compiled loader: {averageMilliseconds:F3} ms average, {json.Length} UTF-16 chars, {iterations} iterations");
            Assert.That(averageMilliseconds, Is.LessThan(1000), "起動時Loaderが1秒を超えています。");
        }

        [Test]
        public void CompilerOutputIsDeterministicAndContainsNoLegacyRowsOrDialogueTextOutsidePayload()
        {
            DialogueAuthoringProject project = DialogueLegacyImporter.ImportFile();
            string first = DialogueCompiledBundleCompiler.CompileJson(project);
            string second = DialogueCompiledBundleCompiler.CompileJson(project);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Not.Contain("originalCsv"));
            Assert.That(first, Does.Not.Contain("\"cells\""));
            Assert.That(first, Does.Contain(project.sourceSha256));
        }

        [Test]
        public void BiteConversationReplaysPatternSixHoldAfterSecondDefeatBranch()
        {
            DialogueAuthoringProject project = DialogueLegacyImporter.ImportFile();
            DialogueAuthoringRoot biteRoot = project.dialogues.Single(root => root.legacyCsvIds.Contains("00450"));
            string json = DialogueCompiledBundleCompiler.CompileJson(project);
            Assert.That(DialogueCompiledRuntimeLoader.TryParse(json, project.originalCsv, "DialoguePreview", out _, out var patterns, out var reactions, out string error), Is.True, error);

            using var compiled = new LegacyRuntimeProbe(patterns, reactions);
            using var legacy = new LegacyRuntimeProbe(project.originalCsv);
            string[] expected = { "1", "2", "3", "6", "6" };
            Assert.That(Enumerable.Range(0, expected.Length).Select(_ => compiled.SelectPattern(biteRoot.legacyLabelHash)), Is.EqualTo(expected));
            Assert.That(Enumerable.Range(0, expected.Length).Select(_ => legacy.SelectPattern(biteRoot.legacyLabelHash)), Is.EqualTo(expected));
        }

        private static void AssertPublicFieldsEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string context)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), context + " count");
            FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < expected.Count; index++)
            foreach (FieldInfo field in fields)
            {
                object left = field.GetValue(expected[index]);
                object right = field.GetValue(actual[index]);
                if (left is IList leftList && right is IList rightList)
                {
                    Assert.That(rightList.Count, Is.EqualTo(leftList.Count), $"{context}[{index}].{field.Name}.Count");
                    for (int item = 0; item < leftList.Count; item++)
                        Assert.That(JsonUtility.ToJson(rightList[item]), Is.EqualTo(JsonUtility.ToJson(leftList[item])), $"{context}[{index}].{field.Name}[{item}]");
                }
                else
                {
                    Assert.That(right, Is.EqualTo(left), $"{context}[{index}].{field.Name}");
                }
            }
        }

        private sealed class LegacyRuntimeProbe : IDisposable
        {
            private readonly GameObject host;
            private readonly DialogueEngine engine;
            public readonly List<RegexPatternData> Patterns;
            public readonly Dictionary<int, List<DialogueReactionData>> Reactions;

            public LegacyRuntimeProbe(string csv)
            {
                host = new GameObject("DialogueAuthoringPhaseThreeLegacyProbe");
                host.SetActive(false);
                engine = host.AddComponent<DialogueEngine>();
                typeof(DialogueEngine).GetMethod("LoadReactionStyleCsv", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(engine, new object[] { csv, "DialoguePreview", true });
                Patterns = (List<RegexPatternData>)typeof(DialogueEngine).GetField("regexPatterns", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine);
                Reactions = (Dictionary<int, List<DialogueReactionData>>)typeof(DialogueEngine).GetField("reactionDatabase", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine);
                Assert.That(Patterns, Is.Not.Null); Assert.That(Reactions, Is.Not.Null);
            }

            public LegacyRuntimeProbe(List<RegexPatternData> patterns, Dictionary<int, List<DialogueReactionData>> reactions)
            {
                host = new GameObject("DialogueAuthoringPhaseThreeCompiledProbe");
                host.SetActive(false);
                engine = host.AddComponent<DialogueEngine>();
                Patterns = patterns;
                Reactions = reactions;
                typeof(DialogueEngine).GetField("regexPatterns", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(engine, patterns);
                typeof(DialogueEngine).GetField("reactionDatabase", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(engine, reactions);
            }

            public string SelectPattern(int labelHash)
            {
                Assert.That(engine.TryDetermineReactionGroup(labelHash, out List<DialogueReactionData> group), Is.True);
                return group[0].PatternID;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
