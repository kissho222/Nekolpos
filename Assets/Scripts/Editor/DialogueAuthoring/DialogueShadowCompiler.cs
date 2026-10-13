using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    [Serializable] public sealed class DialogueShadowBundle
    {
        public int formatVersion = 1, authoringVersion;
        public string source, sourceSha256;
        public bool publishable;
        public List<ShadowRoot> roots = new();
        public List<AuthoringDiagnostic> diagnostics = new();
    }
    [Serializable] public sealed class ShadowRoot
    {
        public string stableId, meaning;
        public int legacyHash;
        public List<ShadowMatcher> matchers = new();
        public List<ShadowSegment> segments = new();
        public List<int> normalOrder = new();
        public LegacyHybridPolicy policy;
    }
    [Serializable] public sealed class ShadowMatcher
    {
        public string regex, sensitivity;
        public int priority, sourceRow;
        public List<LocalizedValue> expressions = new();
    }
    [Serializable] public sealed class ShadowSegment
    {
        public string stableId, pattern, condition, representativeResponse, emotionType;
        public int repeatThreshold, randomLimit, emotionValue;
        public AuthoringSpeech speech;
        public bool hold, eligible;
        public List<string> editorIdentities = new();
        public List<AuthoringReference> externalRefs = new();
        public List<ShadowLine> lines = new();
    }
    [Serializable] public sealed class ShadowLine
    {
        public string stableId, japaneseText, condition;
        public string intent, reactionType, sourceRegex, diaryEntryId, diaryRegexId, nextAction, emotionType;
        public int emotionValue;
        public int order, sourceRow, sourceOrdinal;
        public AuthoringSpeech speech;
        public List<LocalizedValue> texts = new();
        public AuthoringPresentation presentation;
        public AuthoringTerminal terminal;
        // -1 means absent or unresolved; the typed reference retains the distinction.
        public int callIndex = -1, yesIndex = -1, noIndex = -1, continuationIndex = -1;
    }
    public static class DialogueShadowCompiler
    {
        private static T Copy<T>(T value) where T : class => value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        public static DialogueShadowBundle Compile(DialogueAuthoringProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (project.schemaVersion != 1) throw new ArgumentException("Unsupported authoring schema version.", nameof(project));
            var bundle = new DialogueShadowBundle { authoringVersion = project.schemaVersion, source = project.source, sourceSha256 = project.sourceSha256 };
            bundle.diagnostics.AddRange(project.diagnostics.Select(Copy));
            foreach (var root in project.dialogues)
            {
                var result = new ShadowRoot { stableId = root.stableId, meaning = root.normalizedMeaningKey, legacyHash = root.legacyLabelHash, policy = Copy(root.selectionPolicy) };
                var policy = result.policy;
                if (policy.kind != "LegacyHybridPolicy" || policy.defaultRandomRepeatLimit != 3 || policy.staleGameDays != 3 || !policy.repeatBeforeOrdinarySelection || !policy.repeatFirstInputExcluded || !policy.preferUnspokenRandom || !policy.avoidImmediateRandomRepeatAfterExhaustion || !policy.sequenceCursorUsesConditionFilteredCandidates || !policy.resetAnchorUpdatedOnlyWhenHoldSegmentSelected || policy.holdIsSavePersistence || policy.conversationProgressIsSaved)
                    Gate("UnsupportedPolicy", root.stableId, "Phase2は旧Hybrid互換ポリシーだけを検証します。");
                foreach (var matcher in root.matchers)
                {
                    var raw = root.segments.SelectMany(s => s.lines).First(l => l.legacy.sourceRow == matcher.firstSourceRow).legacy;
                    string sensitivity = DialogueLegacyImporter.Read(raw, "sensitivity_level", "sensitivity", "フィルタ");
                    var item = new ShadowMatcher { regex = matcher.normalizedJapaneseRegex, priority = matcher.priority, sourceRow = matcher.firstSourceRow, sensitivity = string.IsNullOrWhiteSpace(sensitivity) ? "SAFE" : sensitivity };
                    item.expressions.AddRange(matcher.expressions.Select(Copy)); result.matchers.Add(item);
                    try { _ = new global::System.Text.RegularExpressions.Regex(item.regex); }
                    catch (ArgumentException exception) { Gate("InvalidRegex", root.stableId, exception.Message); }
                }
                foreach (var segment in root.segments)
                {
                    var compiled = new ShadowSegment { stableId = segment.stableId, pattern = segment.legacyPattern, condition = segment.selectionCondition.expression,
                        representativeResponse = segment.lines[0].legacyLineTerminal.legacyResponseType, speech = segment.representativeSpeech, repeatThreshold = segment.repeatThreshold,
                        randomLimit = segment.randomRepeatLimit, hold = segment.progressPolicy == SequenceProgressPolicy.HoldResetAnchor, eligible = segment.eligibleForNormalSelection,
                        emotionType = segment.emotionChangeType, emotionValue = segment.emotionChangeValue };
                    compiled.editorIdentities.AddRange(segment.legacyEditorIdentityIds);
                    compiled.externalRefs.AddRange(segment.externalRefs.Select(Copy));
                    int Target(AuthoringReference reference)
                    {
                        if (reference == null || string.IsNullOrEmpty(reference.legacyKey)) return -1;
                        int index = root.segments.FindIndex(s => s.stableId == reference.stableTargetId && s.legacyPattern == reference.legacyKey);
                        if (index < 0) Gate("MissingSegmentIndex", segment.stableId, "未解決Segment: " + reference.legacyKey);
                        return index;
                    }
                    foreach (var line in segment.lines)
                    {
                        var item = new ShadowLine { stableId = line.stableId, order = line.order, sourceRow = line.legacy.sourceRow, sourceOrdinal = line.legacy.ordinal,
                            japaneseText = line.legacyRuntimeJapaneseText, condition = line.condition.expression, speech = line.speech, presentation = Copy(line.presentation), terminal = Copy(line.legacyLineTerminal),
                            callIndex = Target(line.callTarget), yesIndex = Target(line.legacyLineTerminal.choice?.yesTarget), noIndex = Target(line.legacyLineTerminal.choice?.noTarget), continuationIndex = Target(line.legacyLineTerminal.continuation) };
                        item.intent = DialogueLegacyImporter.Read(line.legacy, "intent"); item.reactionType = DialogueLegacyImporter.Read(line.legacy, "reaction_type");
                        item.sourceRegex = DialogueLegacyImporter.Read(line.legacy, "regex_id", "__source_regex", "source_regex", "意味キー", "label", "meaning", "regex");
                        item.diaryEntryId = DialogueLegacyImporter.Read(line.legacy, "id", "ID").Trim(); item.diaryRegexId = item.sourceRegex.Trim().Replace("\\_", "_");
                        item.nextAction = DialogueLegacyImporter.Read(line.legacy, "NextAction", "反応リンク", "next_action"); item.emotionType = DialogueLegacyImporter.Read(line.legacy, "emotion_change_type");
                        item.emotionValue = DialogueLegacyImporter.Int(DialogueLegacyImporter.Read(line.legacy, "emotion_change_value"));
                        item.texts.AddRange(line.texts.Select(Copy)); compiled.lines.Add(item);
                    }
                    result.segments.Add(compiled);
                    if (string.IsNullOrWhiteSpace(compiled.pattern)) Gate("EmptyPattern", compiled.stableId, "空Patternの旧GUID発行は決定的なshadow比較ができません。");
                    if (compiled.lines.GroupBy(l => l.order).Any(g => g.Count() > 1)) Gate("DuplicateOrder", compiled.stableId, "同Orderの旧List.Sort順を確定できません。");
                }
                // The legacy engine removes CallOnly before sorting its normal candidate list.
                var normal = result.segments.Select((s, i) => (s, i)).Where(x => x.s.eligible).OrderBy(x => x.s.lines.Min(l => l.sourceOrdinal)).ToList();
                var appearance = normal.Select((x, i) => (x.i, index: i)).ToDictionary(x => x.i, x => x.index);
                normal.Sort((a, b) => int.TryParse(a.s.pattern, out int an) && int.TryParse(b.s.pattern, out int bn) && an != bn ? an.CompareTo(bn) : appearance[a.i].CompareTo(appearance[b.i]));
                result.normalOrder.AddRange(normal.Select(x => x.i));
                bool numeric = result.segments.Any(s => int.TryParse(s.pattern, out _)), nonNumeric = result.segments.Any(s => !int.TryParse(s.pattern, out _));
                if (numeric && nonNumeric) Gate("MixedPatternOrder", root.stableId, "数値/非数値混在の旧比較関数は全順序ではないため公開を保留します。");
                bundle.roots.Add(result);
            }
            bundle.publishable = bundle.diagnostics.All(d => d.severity != DiagnosticSeverity.Error);
            return bundle;
            void Gate(string code, string id, string message) => bundle.diagnostics.Add(new AuthoringDiagnostic { severity = DiagnosticSeverity.Error, code = "Shadow" + code, stableId = id, message = message });
        }
    }
}
