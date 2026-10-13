using System;
using System.Linq;
using Backgammon.Conversation;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    using Authoring;
    public sealed class DialogueAuthoringPhaseTwoTests
    {
        private static DialogueAuthoringProject Fixture(params string[] rows) => DialogueLegacyImporter.ImportText("id,regex_id,regex_jp,pattern,order,output_ja,SpeechControl,response_type,condition,RepeatCount,call_only,sequence_progress_hold,random_repeat_limit,target_pattern,choice_yes_pattern,choice_no_pattern\n" + string.Join("\n", rows), "fixture.csv");
        [Test] public void FullSourceShadowSelectionAndIntentMatchLegacyKernels()
        {
            var report = DialogueShadowComparison.Run(DialogueLegacyImporter.ImportFile());
            Assert.That(report.roots, Is.EqualTo(139)); Assert.That(report.segments, Is.EqualTo(351)); Assert.That(report.lines, Is.EqualTo(750));
            Assert.That(report.selections, Is.EqualTo(10008)); Assert.That(report.differences, Is.Empty, string.Join("\n", report.differences.Take(20)));
            Assert.That(report.inputComparisons, Is.EqualTo(840));
            Assert.That(report.publishable, Is.False); Assert.That(report.diagnostics.Any(d => d.code == "MissingTimedEvent"), Is.False);
            Assert.That(report.diagnostics.Count(d => d.code == "UnresolvedAction"), Is.EqualTo(3));
        }
        [Test] public void CompilerResolvesDirectBranchesWithoutConditionRecheckAndDetachesSource()
        {
            var project = Fixture("1,x,x,1,0,,Sequence,Choice,,0,false,false,0,,2,3", "1,x,x,2,0,,Call,Normal,Affection>50,0,true,false,0,3,,", "1,x,x,3,0,,Return,Normal,,0,true,false,0,,,");
            var bundle = DialogueShadowCompiler.Compile(project); var root = bundle.roots.Single(); var line = root.segments[0].lines[0];
            Assert.That(line.yesIndex, Is.EqualTo(1)); Assert.That(line.noIndex, Is.EqualTo(2)); Assert.That(root.segments[1].lines[0].callIndex, Is.EqualTo(2));
            Assert.That(root.normalOrder, Is.EqualTo(new[] { 0 })); Assert.That(line.terminal.choice.reevaluateSelectionCondition, Is.False);
            project.dialogues[0].segments[0].lines[0].presentation.animation = "changed";
            Assert.That(line.presentation.animation, Is.Empty);
            var json = JsonUtility.ToJson(bundle); Assert.That(json, Does.Not.Contain("originalCsv")); Assert.That(json, Does.Not.Contain("cells"));
            var copy = JsonUtility.FromJson<DialogueShadowBundle>(json); Assert.That(copy.roots[0].segments[0].lines[0].yesIndex, Is.EqualTo(1));
        }
        [Test] public void InvalidVersionAndNonDeterministicOrderBlockPublication()
        {
            var project = Fixture("1,x,x,,0,,Random,Normal,,0,false,false,-1,,,", "1,x,x,1,0,,Sequence,Normal,,0,false,false,-1,,,", "1,x,x,1,0,,Sequence,Normal,,0,false,false,-1,,,");
            var bundle = DialogueShadowCompiler.Compile(project); Assert.That(bundle.publishable, Is.False);
            Assert.That(bundle.diagnostics.Any(d => d.code == "ShadowDuplicateOrder"), Is.True); Assert.That(bundle.diagnostics.Any(d => d.code == "ShadowEmptyPattern"), Is.True);
            project.schemaVersion = 99; Assert.Throws<ArgumentException>(() => DialogueShadowCompiler.Compile(project));
        }
        [Test] public void ConditionalCursorHoldStaleResetRepeatAndTopicSwitchMatchLegacy()
        {
            var project = Fixture("1,x,x,1,0,,Random,Normal,,0,false,false,2,,,", "1,x,x,2,0,,Random,Normal,,0,false,false,2,,,", "1,x,x,3,0,,Sequence,Normal,gate,0,false,false,2,,,", "1,x,x,4,0,,Sequence,Normal,,0,false,true,2,,,", "1,x,x,5,0,,Sequence,Action,,0,false,false,2,,,", "1,x,x,6,0,,RepeatEvent,Normal,,14,true,false,2,,,", "1,y,y,1,0,,Random,Normal,,0,false,false,2,,,");
            var bundle = DialogueShadowCompiler.Compile(project); var shadow = new DialogueShadowSelector(); var report = new ShadowComparisonReport();
            var saved = UnityEngine.Random.state;
            try
            {
                using var legacy = new DialogueLegacyShadowProbe(project.originalCsv); UnityEngine.Random.InitState(73);
                for (int step = 0; step < 20; step++) DialogueShadowComparison.CompareStep(report, legacy, shadow, bundle.roots[step == 8 ? 1 : 0], step < 10 ? 1 : step == 10 ? 3 : step == 11 ? 6 : 7, expression => step < 5);
                Assert.That(report.differences, Is.Empty); Assert.That(shadow.states[bundle.roots[0].legacyHash].spoken.Count, Is.EqualTo(2));
            }
            finally { UnityEngine.Random.state = saved; }
        }
        [Test] public void ExternalTailReplayUsesRepresentativeResponseRatherThanLastLine()
        {
            var project = Fixture("1,x,x,1,0,,Sequence,Normal,,0,false,false,0,,,", "1,x,x,1,1,,Sequence,Action,,0,false,false,0,,,", "1,x,x,2,0,,Sequence,Choice,,0,false,false,0,,,");
            var shadow = new DialogueShadowSelector(); var root = DialogueShadowCompiler.Compile(project).roots[0];
            Assert.That(shadow.Select(root, 1, _ => true, _ => 0).pattern, Is.EqualTo("1")); Assert.That(shadow.Select(root, 1, _ => true, _ => 0).pattern, Is.EqualTo("2"));
            Assert.That(shadow.Select(root, 1, _ => true, _ => 0).pattern, Is.EqualTo("1"));
        }
        [Test] public void SequenceTailReplaysExplicitHoldCheckpointBeforeGenericExternalFallback()
        {
            var project = Fixture(
                "1,x,x,1,0,,Sequence,Normal,,0,false,false,0,,,,",
                "1,x,x,2,0,,Sequence,Normal,,0,false,false,0,,,,",
                "1,x,x,3,0,,Sequence,Event,,0,false,false,0,,,,",
                "1,x,x,6,0,,Sequence,Choice,,0,false,true,0,,7,4");
            var root = DialogueShadowCompiler.Compile(project).roots[0];
            var shadow = new DialogueShadowSelector();
            string[] selected = Enumerable.Range(0, 5).Select(_ => shadow.Select(root, 1, __ => true, __ => 0).pattern).ToArray();
            Assert.That(selected, Is.EqualTo(new[] { "1", "2", "3", "6", "6" }));
        }
        [Test] public void NoCandidatesDoNotAdvanceProgressAndDefeatConditionWins()
        {
            var project = Fixture("1,x,x,1,0,,Random,Normal,enabled,0,false,false,-1,,,", "1,x,x,2,0,,Random,Normal,PlayerDefeatedByAnger,0,false,false,-1,,,");
            var root = DialogueShadowCompiler.Compile(project).roots[0]; var shadow = new DialogueShadowSelector();
            Assert.That(shadow.Select(root, 1, _ => false, _ => 0), Is.Null); Assert.That(shadow.states, Is.Empty); Assert.That(shadow.lastHash, Is.Zero);
            Assert.That(shadow.Select(root, 1, _ => true, _ => 0).pattern, Is.EqualTo("2"));
            Assert.That(shadow.Select(root, 20, _ => false, _ => 0), Is.Null); Assert.That(shadow.states[root.legacyHash].lastDay, Is.EqualTo(1));
        }
        [Test] public void ThreeDayResetRetainsRandomHistoryAndResumesHoldAnchor()
        {
            var project = Fixture("1,x,x,1,0,,Sequence,Normal,,0,false,false,0,,,", "1,x,x,2,0,,Sequence,Normal,,0,false,true,0,,,", "1,x,x,3,0,,Sequence,Normal,,0,false,false,0,,,");
            var root = DialogueShadowCompiler.Compile(project).roots[0]; var shadow = new DialogueShadowSelector();
            for (int i = 0; i < 3; i++) shadow.Select(root, 1, _ => true, _ => 0);
            var state = shadow.states[root.legacyHash]; state.spoken.Add("kept");
            // The existing explicit-hold fix resumes its checkpoint even at the sequence tail.
            Assert.That(shadow.Select(root, 3, _ => true, _ => 0).pattern, Is.EqualTo("2"));
            Assert.That(shadow.Select(root, 6, _ => true, _ => 0).pattern, Is.EqualTo("2")); Assert.That(state.repeat, Is.EqualTo(1)); Assert.That(state.spoken, Does.Contain("kept"));
        }
        [Test] public void OutOfRangeRandomDrawIsReported()
        {
            var root = DialogueShadowCompiler.Compile(Fixture("1,x,x,1,0,,Random,Normal,,0,false,false,-1,,,")).roots[0];
            Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueShadowSelector().Select(root, 1, _ => true, count => count));
        }
        [Test] public void ComparisonDetectsIntentMutationAndRestoresGlobalRandom()
        {
            var project = Fixture("1,x,x,1,0,,Random,Normal,,0,false,false,-1,,,"); var saved = UnityEngine.Random.state;
            project.dialogues[0].segments[0].lines[0].presentation.hideUi = true;
            var report = DialogueShadowComparison.Run(project); Assert.That(report.differences, Is.Not.Empty); Assert.That(report.publishable, Is.False); Assert.That(UnityEngine.Random.state.Equals(saved), Is.True);
        }
        [Test] public void MatcherPrioritizesLongestThenPriorityThenExplicitThenAppearance()
        {
            var project = DialogueLegacyImporter.ImportText("regex_id,regex_jp,pattern,SpeechControl,priority,sensitivity_level\na,ab,1,Random,0,SAFE\nb,b,1,Random,99,EXPLICIT\nc,ab,1,Random,0,EXPLICIT\nd,ab,1,Random,0,EXPLICIT", "fixture.csv");
            var bundle = DialogueShadowCompiler.Compile(project);
            Assert.That(DialogueShadowSelector.Match(bundle, "ab", _ => true).meaning, Is.EqualTo("c"));
            bundle.roots[0].matchers[0].priority = 1;
            Assert.That(DialogueShadowSelector.Match(bundle, "ab", _ => true).meaning, Is.EqualTo("a"));
            Assert.That(DialogueShadowSelector.Match(bundle, "", _ => true), Is.Null);
        }
        [Test] public void InvalidRegexIsPublicationErrorAndTeachingProbeIsRejectedBeforeRegistration()
        {
            var bundle = DialogueShadowCompiler.Compile(DialogueLegacyImporter.ImportText("regex_id,regex_jp,pattern\nx,(,1", "fixture.csv"));
            Assert.That(bundle.publishable, Is.False); Assert.That(bundle.diagnostics.Any(d => d.code == "ShadowInvalidRegex"), Is.True);
            Assert.Throws<ArgumentException>(() => new DialogueLegacyShadowProbe("regex_id,regex_jp,pattern,teaching_category\nx,x,1,CatExpression"));
        }
    }
}
