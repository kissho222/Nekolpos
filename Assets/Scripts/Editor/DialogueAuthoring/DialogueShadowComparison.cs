using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.System;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    [Serializable] public sealed class ShadowComparisonReport
    {
        public string scope = "Editor isolated legacy selection kernels / command intent; no playback or external effects";
        public int roots, segments, lines, selections, lineComparisons, inputComparisons, referenceComparisons, postActionComparisons;
        public bool publishable;
        public List<string> differences = new();
        public List<AuthoringDiagnostic> diagnostics = new();
    }
    // The probe never enables its host or calls Awake, DialogueManager, time advancement or Save.
    public sealed class DialogueLegacyShadowProbe : IDisposable
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private readonly GameObject host;
        private readonly DialogueEngine engine;
        public readonly Dictionary<int, ConversationState> states = new();
        public DialogueLegacyShadowProbe(string csv)
        {
            var headers = TalkTopicMasterCsvUtility.Parse(csv)[0].Fields;
            if (headers.Any(h => new[] { "teachingcategory", "categoryid" }.Contains(h.Replace("_", "").ToLowerInvariant())))
                throw new ArgumentException("Teaching source parsing may register persistent discovery keys; this readonly probe supports normal dialogue only.", nameof(csv));
            host = new GameObject("DialogueShadowReadonlyProbe"); host.SetActive(false);
            try { engine = host.AddComponent<DialogueEngine>(); Invoke("LoadReactionStyleCsv", csv, "DialoguePreview", true); }
            catch { UnityEngine.Object.DestroyImmediate(host); throw; }
        }
        private object Invoke(string method, params object[] args) => typeof(DialogueEngine).GetMethod(method, Flags).Invoke(engine, args);
        public Dictionary<string, List<DialogueReactionData>> Groups(int hash, bool includeCallOnly, out List<string> order)
        {
            var args = new object[] { hash, includeCallOnly, null, null }; Invoke("TryBuildReactionGroups", args);
            order = (List<string>)args[3]; return (Dictionary<string, List<DialogueReactionData>>)args[2];
        }
        public List<DialogueReactionData> Select(int hash, int day, Func<string, bool> condition)
        {
            var groups = Groups(hash, false, out var order);
            var reps = order.Select(p => groups[p][0]).Where(r => string.IsNullOrWhiteSpace(r.Condition) || condition(r.Condition)).ToList();
            reps = (List<DialogueReactionData>)Invoke("PreferPlayerDefeatConditionRepresentatives", reps);
            if (reps.Count == 0) return null;
            if (!states.TryGetValue(hash, out var state)) states.Add(hash, state = new ConversationState { patternHash = hash });
            Invoke("ResetConversationStateIfStale", hash, reps, state, Math.Max(1, day));
            int lastHash = (int)typeof(DialogueEngine).GetField("lastPatternHash", Flags).GetValue(engine);
            state.repeatCount = lastHash == hash ? state.repeatCount + 1 : 1;
            typeof(DialogueEngine).GetField("lastPatternHash", Flags).SetValue(engine, hash);
            var selected = (DialogueReactionData)Invoke("FindRepeatEvent", reps, state.repeatCount);
            selected ??= (DialogueReactionData)Invoke("GetHybridRandomSequenceReaction", reps.Where(r => r.SpeechControl == SpeechControlType.Random || r.SpeechControl == SpeechControlType.Sequence).ToList(), state);
            if (selected == null) return null;
            state.lastSpokenDay = Math.Max(1, day);
            return groups.Values.First(g => g.Contains(selected));
        }
        public int LastHash => (int)typeof(DialogueEngine).GetField("lastPatternHash", Flags).GetValue(engine);
        public int Match(string input, Func<string, bool> condition)
        {
            var patterns = (List<RegexPatternData>)typeof(DialogueEngine).GetField("regexPatterns", Flags).GetValue(engine);
            RegexPatternData best = null; int length = -1;
            foreach (var pattern in patterns)
            {
                var groups = Groups(pattern.LabelHash, false, out var order);
                if (!order.Any(p => string.IsNullOrWhiteSpace(groups[p][0].Condition) || condition(groups[p][0].Condition))) continue;
                var match = (global::System.Text.RegularExpressions.Match)Invoke("GetLongestRegexMatch", input, pattern.SourceRegex);
                if (match.Success && (bool)Invoke("IsBetterMatchCandidate", pattern, match.Length, best, length)) { best = pattern; length = match.Length; }
            }
            return best?.LabelHash ?? 0;
        }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(host);
    }
    public static class DialogueShadowComparison
    {
        // Length prefixes distinguish empty values, separators and embedded newlines without escaping ambiguity.
        private static string Pack(params object[] values) => string.Concat(values.Select(v => { string s = Convert.ToString(v, global::System.Globalization.CultureInfo.InvariantCulture) ?? ""; return s.Length + ":" + s; }));
        public static string Intent(DialogueReactionData line) => Pack(line.Order, line.TextJP, line.Condition, line.SpeechControl, line.TargetPatternID,
            line.ResponseType, line.ActionId, line.TimedEventKey, line.ChoiceYesPatternID, line.ChoiceNoPatternID, line.ChoiceQuestionJa, line.ChoiceYesLabel, line.ChoiceNoLabel,
            line.WaitTime, line.HideUI, line.Animation, line.ReactionIcon, line.IntentID, line.ReactionType, line.SourceRegexLabel, line.DiarySourceEntryId, line.DiarySourceRegexId, line.NextAction, line.EmotionChangeType, line.EmotionChangeValue);
        public static string Intent(ShadowRoot root, ShadowLine line) => Pack(line.order, line.japaneseText, line.condition, line.speech,
            line.terminal.continuation?.legacyKey, line.terminal.legacyResponseType, line.terminal.action?.legacyKey, line.terminal.timedEvent?.legacyKey,
            line.terminal.choice?.yesTarget?.legacyKey, line.terminal.choice?.noTarget?.legacyKey, line.terminal.choice?.legacyRuntimeQuestionJa,
            line.terminal.choice?.legacyRuntimeYesLabel, line.terminal.choice?.legacyRuntimeNoLabel,
            line.presentation.waitTime, line.presentation.hideUi, line.presentation.animation, ReactionIconUtility.Parse(line.presentation.reactionIcon), line.intent, line.reactionType, line.sourceRegex, line.diaryEntryId, line.diaryRegexId, line.nextAction, line.emotionType, line.emotionValue);
        public static void CompareStep(ShadowComparisonReport report, DialogueLegacyShadowProbe legacy, DialogueShadowSelector shadow, ShadowRoot root, int day, Func<string, bool> condition)
        {
            var before = UnityEngine.Random.state;
            var expected = legacy.Select(root.legacyHash, day, condition); var after = UnityEngine.Random.state;
            ShadowSegment actual;
            bool rngMatches;
            UnityEngine.Random.state = before;
            try { actual = shadow.Select(root, day, condition, count => UnityEngine.Random.Range(0, count)); rngMatches = UnityEngine.Random.state.Equals(after); }
            finally { UnityEngine.Random.state = after; }
            report.selections++;
            void Difference(string message) => report.differences.Add(root.meaning + " day=" + day + " step=" + report.selections + ": " + message);
            if (!rngMatches) Difference("RNG consumption differs");
            if ((expected?.First().PatternID?.Trim() ?? "") != (actual?.pattern ?? "")) Difference("selected Pattern differs");
            if (legacy.LastHash != shadow.lastHash) Difference("last topic differs");
            bool hasOld = legacy.states.TryGetValue(root.legacyHash, out var old), hasNew = shadow.states.TryGetValue(root.legacyHash, out var next);
            if (hasOld != hasNew) Difference("state existence differs");
            else if (hasOld && (old.repeatCount != next.repeat || old.sequenceIndex != next.cursor || old.sequenceResetAnchorIndex != next.anchor || old.lastSpokenDay != next.lastDay || old.lastRandomPatternId != next.lastRandom || !old.spokenRandomPatternIds.SetEquals(next.spoken))) Difference("progress / random history differs");
            if (expected == null || actual == null) return;
            if (expected.Count != actual.lines.Count) Difference("line count differs");
            for (int i = 0; i < Math.Min(expected.Count, actual.lines.Count); i++)
            {
                report.lineComparisons++;
                if (Intent(expected[i]) != Intent(root, actual.lines[i])) Difference("line intent differs at " + i);
            }
        }
        public static ShadowComparisonReport Run(DialogueAuthoringProject project)
        {
            var bundle = DialogueShadowCompiler.Compile(project);
            var report = new ShadowComparisonReport { roots = bundle.roots.Count, segments = bundle.roots.Sum(r => r.segments.Count), lines = bundle.roots.Sum(r => r.segments.Sum(s => s.lines.Count)), publishable = bundle.publishable };
            report.diagnostics.AddRange(bundle.diagnostics);
            var savedRandom = UnityEngine.Random.state;
            try
            {
                using var probe = new DialogueLegacyShadowProbe(project.originalCsv);
                foreach (var root in bundle.roots)
                {
                    var oldGroups = probe.Groups(root.legacyHash, true, out var oldOrder);
                    if (!oldOrder.SequenceEqual(root.segments.Select(s => s.pattern))) report.differences.Add(root.meaning + ": direct Pattern order differs");
                    foreach (var segment in root.segments)
                    {
                        if (!oldGroups.TryGetValue(segment.pattern, out var group)) { report.differences.Add(root.meaning + ": direct Pattern missing"); continue; }
                        if (group.Count != segment.lines.Count) report.differences.Add(root.meaning + ": direct line count differs");
                        for (int i = 0; i < Math.Min(group.Count, segment.lines.Count); i++)
                        {
                            report.lineComparisons++; if (Intent(group[i]) != Intent(root, segment.lines[i])) report.differences.Add(root.meaning + ": direct line intent differs at " + segment.pattern + "/" + i);
                            var oldTargets = new[] { group[i].TargetPatternID, group[i].ChoiceYesPatternID, group[i].ChoiceNoPatternID };
                            var indices = new[] { segment.lines[i].callIndex, segment.lines[i].yesIndex, segment.lines[i].noIndex };
                            for (int j = 0; j < oldTargets.Length; j++)
                            {
                                report.referenceComparisons++;
                                string expected = !string.IsNullOrWhiteSpace(oldTargets[j]) && oldGroups.ContainsKey(oldTargets[j].Trim()) ? oldTargets[j].Trim() : "";
                                string actual = indices[j] >= 0 && indices[j] < root.segments.Count ? root.segments[indices[j]].pattern : "";
                                if (expected != actual) report.differences.Add(root.meaning + ": direct reference differs at " + segment.pattern + "/" + i + "/" + j);
                            }
                        }
                        foreach (var state in Scenarios())
                        {
                            // Action continuations filter individual lines, while direct Choice/Call retain the whole group.
                            bool Condition(string expression) => string.IsNullOrWhiteSpace(expression) || ConversationGameStateConditionEvaluator.Evaluate(state, expression);
                            var expected = group.Where(l => Condition(l.Condition)).Select(Intent);
                            var actual = segment.lines.Where(l => Condition(l.condition)).Select(l => Intent(root, l));
                            report.postActionComparisons++;
                            if (!expected.SequenceEqual(actual)) report.differences.Add(root.meaning + ": post-Action line plan differs at " + segment.pattern);
                        }
                    }
                }
                foreach (int seed in new[] { 0, 7, 42 })
                foreach (var gameState in Scenarios())
                {
                    using var legacy = new DialogueLegacyShadowProbe(project.originalCsv);
                    var shadow = new DialogueShadowSelector(); UnityEngine.Random.InitState(seed);
                    foreach (string input in bundle.roots.Select(r => r.meaning).Append(""))
                    {
                        string normalized = JapaneseTextNormalizer.NormalizeToken(input);
                        bool Condition(string expression) => ConversationGameStateConditionEvaluator.Evaluate(gameState, expression);
                        int oldMatch = legacy.Match(normalized, Condition), newMatch = DialogueShadowSelector.Match(bundle, normalized, Condition)?.legacyHash ?? 0;
                        report.inputComparisons++; if (oldMatch != newMatch) report.differences.Add("input matcher differs: " + input);
                    }
                    foreach (var root in bundle.roots)
                    for (int step = 0; step < 12; step++)
                    {
                        int day = step < 8 ? 1 : step == 8 ? 3 : step == 9 ? 6 : 7;
                        gameState.SetInt("CurrentDay", day);
                        CompareStep(report, legacy, shadow, root, day, expression => ConversationGameStateConditionEvaluator.Evaluate(gameState, expression));
                    }
                }
            }
            finally { UnityEngine.Random.state = savedRandom; }
            report.publishable &= report.differences.Count == 0;
            return report;
        }
        private static IEnumerable<ConversationGameState> Scenarios()
        {
            foreach (var phase in new[] { "Morning", "Night" })
            {
                var state = new ConversationGameState(); state.SetString("Phase", phase); state.SetString("CurrentPhase", phase); state.SetString("Weather", "Sunny");
                foreach (string key in new[] { "Affection", "Sadistic", "Concern", "Hostility", "Obedience", "Instinct" }) { state.SetInt(key, phase == "Morning" ? 0 : 100); state.SetInt(key.ToLowerInvariant(), phase == "Morning" ? 0 : 100); }
                foreach (string reason in new[] { "Nekomata", "Predation", "Anger", "Accident" }) state.SetBool("PlayerDefeatedBy" + reason, phase == "Night");
                yield return state;
            }
        }
        public static ShadowComparisonReport Export(DialogueAuthoringProject project)
        {
            var report = Run(project); var bundle = DialogueShadowCompiler.Compile(project);
            string directory = Path.GetFullPath("Library/DialogueAuthoringShadow"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Bundle.json"), JsonUtility.ToJson(bundle, true));
            File.WriteAllText(Path.Combine(directory, "Comparison.json"), JsonUtility.ToJson(report, true));
            return report;
        }
    }
}
