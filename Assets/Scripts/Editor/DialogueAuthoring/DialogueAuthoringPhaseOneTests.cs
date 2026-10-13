using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Nekolpos.Data;
using Nekolpos.System;
using Nekolpos.EditorTools.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public sealed class DialogueAuthoringPhaseOneTests
    {
        private static DialogueAuthoringProject Fixture(string extraHeaders = "", params string[] rows) => DialogueLegacyImporter.ImportText("id,regex_id,regex_jp,pattern,order,output_ja,SpeechControl,response_type,condition,RepeatCount,call_only" + extraHeaders + "\n" + string.Join("\n", rows), "fixture.csv");
        private static IEnumerable<AuthoringLine> Lines(DialogueAuthoringProject p) => p.dialogues.SelectMany(r => r.segments).SelectMany(s => s.lines);
        [Test] public void FullSourceHasExpectedStructureAndNoCellLoss()
        {
            var p = DialogueLegacyImporter.ImportFile();
            Assert.That(p.headers.Count, Is.EqualTo(41)); Assert.That(p.dialogues.Count, Is.EqualTo(139));
            Assert.That(p.dialogues.Sum(r => r.segments.Count), Is.EqualTo(351)); Assert.That(Lines(p).Count(), Is.EqualTo(750));
            Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Empty);
            Assert.That(p.diagnostics.Count(d => d.code == "MeaningFallback"), Is.EqualTo(184));
        }
        [Test] public void FullSourceEditorParityPreservesAllColumnsAndPatterns()
        {
            var p = DialogueLegacyImporter.ImportFile(); Assert.That(DialogueAuthoringComparison.EditorParity(p, DialogueLegacyImporter.DefaultSource), Is.Empty);
        }
        [Test] public void FullSourceRuntimeInterpretationMatchesExistingParser()
        {
            var p = DialogueLegacyImporter.ImportFile(); var host = new GameObject("PhaseOneReadonlyParserProbe"); host.SetActive(false);
            try
            {
                var engine = host.AddComponent<DialogueEngine>(); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DialogueEngine).GetMethod("LoadReactionStyleCsv", flags).Invoke(engine, new object[] { p.originalCsv, "DialoguePreview", true });
                var db = (Dictionary<int, List<DialogueReactionData>>)typeof(DialogueEngine).GetField("reactionDatabase", flags).GetValue(engine);
                Assert.That(db.Values.Sum(v => v.Count), Is.EqualTo(750)); Assert.That(db.Count, Is.EqualTo(p.dialogues.Count));
                foreach (var root in p.dialogues)
                {
                    var old = db[root.legacyLabelHash];
                    var args = new object[] { root.legacyLabelHash, true, null, null };
                    Assert.That((bool)typeof(DialogueEngine).GetMethod("TryBuildReactionGroups", flags).Invoke(engine, args), Is.True);
                    Assert.That((List<string>)args[3], Is.EqualTo(root.segments.Select(s => s.legacyPattern)));
                    var groups = (Dictionary<string, List<DialogueReactionData>>)args[2];
                    foreach (var segment in root.segments)
                    {
                        var first = groups[segment.legacyPattern][0];
                        Assert.That(first.SpeechControl.ToString(), Is.EqualTo(segment.representativeSpeech.ToString()));
                        Assert.That(Math.Max(2, first.RepeatCount), Is.EqualTo(segment.repeatThreshold));
                        Assert.That(first.RandomRepeatLimit, Is.EqualTo(segment.randomRepeatLimit));
                        Assert.That(first.SequenceProgressHold, Is.EqualTo(segment.progressPolicy == SequenceProgressPolicy.HoldResetAnchor));
                        Assert.That(first.Condition, Is.EqualTo(segment.selectionCondition.expression));
                        Assert.That(first.EmotionChangeType ?? "", Is.EqualTo(segment.emotionChangeType)); Assert.That(first.EmotionChangeValue, Is.EqualTo(segment.emotionChangeValue));
                    }
                    var expected = root.segments.SelectMany(s => s.lines).OrderBy(l => l.legacy.ordinal).ToList();
                    Assert.That(old.Count, Is.EqualTo(expected.Count));
                    for (int i = 0; i < old.Count; i++)
                    {
                        var a = old[i]; var n = expected[i];
                        Assert.That(a.Order, Is.EqualTo(n.order)); Assert.That(a.SpeechControl.ToString(), Is.EqualTo(n.speech.ToString()));
                        Assert.That(a.TextJP, Is.EqualTo(n.legacyRuntimeJapaneseText));
                        Assert.That(a.WaitTime, Is.EqualTo(n.presentation.waitTime)); Assert.That(a.HideUI, Is.EqualTo(n.presentation.hideUi)); Assert.That(a.Animation ?? "", Is.EqualTo(n.presentation.animation));
                        Assert.That(a.ChoiceQuestionJa ?? "", Is.EqualTo(n.legacyLineTerminal.choice.legacyRuntimeQuestionJa));
                        Assert.That(a.ChoiceYesLabel ?? "", Is.EqualTo(n.legacyLineTerminal.choice.legacyRuntimeYesLabel)); Assert.That(a.ChoiceNoLabel ?? "", Is.EqualTo(n.legacyLineTerminal.choice.legacyRuntimeNoLabel));
                        Assert.That(a.Condition, Is.EqualTo(n.condition.expression)); Assert.That(a.ActionId ?? "", Is.EqualTo(n.legacyLineTerminal.action?.legacyKey ?? ""));
                        Assert.That(a.TimedEventKey ?? "", Is.EqualTo(n.legacyLineTerminal.timedEvent?.legacyKey ?? ""));
                        Assert.That(a.ChoiceYesPatternID ?? "", Is.EqualTo(n.legacyLineTerminal.choice.yesTarget?.legacyKey ?? ""));
                        Assert.That(a.ChoiceNoPatternID ?? "", Is.EqualTo(n.legacyLineTerminal.choice.noTarget?.legacyKey ?? ""));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        [Test] public void StableIdsAreIdempotentAndSeparateFromEditorGuids()
        {
            var a = DialogueLegacyImporter.ImportFile(); var b = DialogueLegacyImporter.ImportFile();
            Assert.That(DialogueAuthoringJson.Preview(b), Is.EqualTo(DialogueAuthoringJson.Preview(a)));
            var ids = a.dialogues.Select(r => r.stableId).Concat(a.dialogues.SelectMany(r => r.segments).Select(s => s.stableId)).Concat(Lines(a).Select(l => l.stableId)).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
            Assert.That(a.dialogues.All(r => r.stableId.StartsWith("root_")), Is.True);
            Assert.That(a.dialogues.SelectMany(r => r.segments).All(s => s.stableId.StartsWith("segment_") && !s.legacyEditorIdentityIds.Contains(s.stableId)), Is.True);
            Assert.That(a.dialogues.SelectMany(r => r.segments).SelectMany(s => s.legacyEditorIdentityIds).Count(), Is.EqualTo(351));
        }
        [Test] public void JsonRoundTripStillExportsEveryOriginalCell()
        {
            var p = DialogueLegacyImporter.ImportFile(); var copy = JsonUtility.FromJson<DialogueAuthoringProject>(DialogueAuthoringJson.Preview(p));
            Assert.That(copy.schemaVersion, Is.EqualTo(1)); Assert.That(DialogueAuthoringComparison.RoundTripLoss(copy), Is.Empty);
        }
        [Test] public void LossCheckDetectsMutationRatherThanReturningOriginalCsv()
        {
            var p = Fixture("", "1,x,x,1,0,text,Random,Normal,,0,false"); Lines(p).Single().legacy.cells.First(c => c.column == "output_ja").value = "different";
            Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Not.Empty);
        }
        [Test] public void UnknownColumnsMultilingualNewlinesAndQuotesSurvive()
        {
            var p = Fixture(",output_en,output_zh,Future", "1,x,x,1,0,\"line\n\"\"quote\"\"\",Random,Normal,,0,false,English,中文,\"a,b\"");
            Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Empty); Assert.That(Lines(p).Single().texts[2].value, Is.EqualTo("中文"));
            Assert.That(Lines(p).Single().legacy.Read("Future"), Is.EqualTo("a,b")); Assert.That(p.diagnostics.Any(d => d.code == "LegacyQuoteRemoval"), Is.True);
        }
        [TestCase("Random", AuthoringSpeech.Random)] [TestCase("Sequence", AuthoringSpeech.Sequence)] [TestCase("RepeatEvent", AuthoringSpeech.RepeatEvent)]
        [TestCase("Call", AuthoringSpeech.Call)] [TestCase("Return", AuthoringSpeech.Return)] [TestCase("", AuthoringSpeech.Sequence)] [TestCase("unexpected", AuthoringSpeech.Sequence)] [TestCase("2", AuthoringSpeech.Sequence)]
        public void SpeechMappingHonorsExplicitNamesAndLegacyOrderInference(string speech, AuthoringSpeech expected)
        {
            var p = Fixture("", $"1,x,x,1,0,text,{speech},Normal,,4,false"); Assert.That(Lines(p).Single().speech, Is.EqualTo(expected));
        }
        [Test] public void HybridPoliciesSeparateLineOrderCandidateOrderAndRepeatException()
        {
            var p = Fixture(",sequence_progress_hold,random_repeat_limit", "1,x,x,10,2,b,Sequence,Normal,,0,false,true,2", "1,x,x,10,1,a,Sequence,Normal,,0,false,false,2", "1,x,x,2,0,r,Random,Normal,,0,false,false,2", "1,x,x,99,0,t,RepeatEvent,Normal,,1,true,false,");
            var root = p.dialogues.Single(); Assert.That(root.segments.Select(s => s.legacyPattern), Is.EqualTo(new[] { "2", "10", "99" }));
            Assert.That(root.segments[1].lines.Select(l => l.order), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(root.selectionPolicy.randomCandidates.Count, Is.EqualTo(1)); Assert.That(root.selectionPolicy.sequenceCandidates.Count, Is.EqualTo(1)); Assert.That(root.selectionPolicy.repeatOverrides.Count, Is.EqualTo(1));
            Assert.That(root.segments[2].repeatCallOnlyException, Is.True); Assert.That(root.segments[2].repeatThreshold, Is.EqualTo(2));
            Assert.That(root.segments[1].progressPolicy, Is.EqualTo(SequenceProgressPolicy.Resettable));
        }
        [TestCase("true", SequenceProgressPolicy.HoldResetAnchor)] [TestCase("false", SequenceProgressPolicy.Resettable)]
        public void HoldMeansMemoryResetAnchorNotPermanentSave(string hold, SequenceProgressPolicy expected)
        {
            var root = Fixture(",sequence_progress_hold", $"1,x,x,1,0,text,Sequence,Normal,,0,false,{hold}").dialogues.Single();
            Assert.That(root.segments.Single().progressPolicy, Is.EqualTo(expected)); Assert.That(root.selectionPolicy.staleGameDays, Is.EqualTo(3));
            Assert.That(root.selectionPolicy.conversationProgressIsSaved, Is.False); Assert.That(root.selectionPolicy.holdIsSavePersistence, Is.False); Assert.That(root.selectionPolicy.resetAnchorUpdatedOnlyWhenHoldSegmentSelected, Is.True);
        }
        [Test] public void NewChoiceColumnsWinAndFallbackValuesRemainInMetadata()
        {
            var p = Fixture(",choice_yes_pattern,yes_next_pattern,choice_no_pattern,no_next_pattern", "1,x,x,1,0,text,Random,Choice,,0,false,2,3,,3", "1,x,x,2,0,a,Random,Normal,,0,true,,,,", "1,x,x,3,0,b,Random,Normal,,0,true,,,,");
            var c = p.dialogues[0].segments[0].terminal.choice;
            Assert.That(c.yesTarget.legacyKey, Is.EqualTo("2")); Assert.That(c.noTarget.legacyKey, Is.EqualTo("3")); Assert.That(c.yesTarget.resolved, Is.True);
            Assert.That(c.noOriginColumn, Is.EqualTo("no_next_pattern")); Assert.That(p.diagnostics.Any(d => d.code == "ChoiceAliasConflict"), Is.True); Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Empty);
        }
        [TestCase("Choice", TerminalKind.Choice)] [TestCase("Action", TerminalKind.Action)] [TestCase("Event", TerminalKind.Event)] [TestCase("Normal", TerminalKind.End)] [TestCase("unknown", TerminalKind.Unknown)]
        public void TerminalTypeIsIndependentOfSelection(string type, TerminalKind expected)
        {
            var s = Fixture(",action_id,timed_event_key,target_pattern", $"1,x,x,1,0,text,Random,{type},,0,false,act,event,2", "1,x,x,2,0,a,Random,Normal,,0,true,,,").dialogues[0].segments[0];
            Assert.That(s.terminal.kind, Is.EqualTo(expected)); Assert.That(s.terminal.action.kind, Is.EqualTo(ReferenceKind.Action)); Assert.That(s.terminal.continuation.resolved, Is.True);
        }
        [Test] public void ConditionsAreRepresentativeAndDormantLineConditionsRemain()
        {
            var p = Fixture("", "1,x,x,1,1,text,Random,Normal,A:Winter,0,false", "1,x,x,1,2,next,Random,Normal,B:Rain,0,false"); var s = p.dialogues[0].segments[0];
            Assert.That(s.selectionCondition.expression, Is.EqualTo("A:Winter")); Assert.That(s.selectionCondition.scope, Is.EqualTo(ConditionScope.RepresentativeSelection)); Assert.That(s.lines[1].condition.expression, Is.EqualTo("B:Rain"));
            Assert.That(p.diagnostics.Any(d => d.code == "RepresentativeConflict" && d.column == "condition"), Is.True);
        }
        [TestCase(",choice_yes_pattern", "99", "MissingSegment")] [TestCase(",target_pattern", "99", "MissingSegment")] [TestCase(",choice_yes_pattern", "0", "AmbiguousZeroTarget")]
        public void BrokenAndAmbiguousReferencesAreReportedWithoutRepair(string headers, string target, string code)
        {
            var p = Fixture(headers, $"1,x,x,1,0,text,Random,Choice,,0,false,{target}"); Assert.That(p.diagnostics.Any(d => d.code == code), Is.True); Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Empty);
        }
        [Test] public void InvalidConditionAndDuplicateOrderAreDiagnosed()
        {
            var p = Fixture("", "1,x,x,1,0,text,Random,Normal,broken ?,0,false", "1,x,x,1,0,next,Random,Normal,,0,false");
            Assert.That(p.diagnostics.Any(d => d.code == "InvalidCondition" && d.severity == DiagnosticSeverity.Error), Is.True); Assert.That(p.diagnostics.Any(d => d.code == "DuplicateOrder"), Is.True);
        }
        [Test] public void ImportDoesNotChangeSourceIdentityRuntimeOrGeneratedAssets()
        {
            var paths = new[] { DialogueLegacyImporter.DefaultSource, DialogueAuthoringIdentity.MetadataPath, "Assets/Scripts/System/DialogueEngine.cs", "Assets/Scripts/System/DialogueManager.cs", "Assets/Scenes/MainScene.unity" }.Where(File.Exists).Concat(Directory.GetFiles("Assets/Resources/TalkData", "*.bytes")).ToArray();
            byte[][] before = paths.Select(File.ReadAllBytes).ToArray(); DialogueLegacyImporter.ImportFile();
            for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
        }
        [Test] public void RestoredJsonDoesNotCreatePhantomMissingReferences()
        {
            var p = DialogueLegacyImporter.ImportFile(); var copy = JsonUtility.FromJson<DialogueAuthoringProject>(DialogueAuthoringJson.Preview(p));
            int old = copy.diagnostics.Count(d => d.code == "MissingSegment"); DialogueAuthoringValidator.Validate(copy);
            Assert.That(copy.diagnostics.Count(d => d.code == "MissingSegment"), Is.EqualTo(old));
        }
        [Test] public void QuotesPreserveEditorTextAndExplicitLegacyRuntimeInterpretation()
        {
            var p = Fixture("", "1,x,x,1,0,\"say \"\"quoted\"\"\",Random,Normal,,0,false"); var line = Lines(p).Single();
            Assert.That(line.texts[0].value, Is.EqualTo("say \"quoted\"")); Assert.That(line.legacyRuntimeJapaneseText, Is.EqualTo("say quoted"));
            Assert.That(DialogueAuthoringComparison.RoundTripLoss(p), Is.Empty);
        }
        [Test] public void SharedReferencesReportMissingTimedEventWithoutEditingSource()
        {
            var p = Fixture(",action_id,timed_event_key", "1,x,x,1,0,text,Random,Event,,0,false,unregistered_action,missing_event");
            DialogueAuthoringValidator.ResolveSharedReferences(p);
            Assert.That(p.diagnostics.Any(d => d.code == "MissingTimedEvent" && d.severity == DiagnosticSeverity.Error), Is.True);
            Assert.That(p.diagnostics.Any(d => d.code == "UnresolvedAction" && d.severity == DiagnosticSeverity.Error), Is.True);
        }
        [Test] public void FullSourceKnownTimedReferencesAreResolvedOnEveryLine()
        {
            var p = DialogueLegacyImporter.ImportFile();
            var known = TalkTopicMasterCsvUtility.ReadDocument("TalkSource/TalkCSV/SystemTimedEvent.csv").Rows.Select(r => TalkTopicMasterCsvUtility.ReadValue(r, "key")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in Lines(p).Select(l => l.legacyLineTerminal.timedEvent).Where(r => r != null && known.Contains(r.legacyKey))) Assert.That(reference.resolved, Is.True);
        }
        [Test] public void SchemaIncludesVersionLegacyCellsAndTypedPolicies()
        {
            var json = DialogueAuthoringJson.Schema(); Assert.That(json, Does.Contain("schemaVersion")); Assert.That(json, Does.Contain("LegacyHybridPolicy")); Assert.That(json, Does.Contain("LegacyCell")); Assert.That(json, Does.Contain("SequenceProgressPolicy"));
        }
    }
}
