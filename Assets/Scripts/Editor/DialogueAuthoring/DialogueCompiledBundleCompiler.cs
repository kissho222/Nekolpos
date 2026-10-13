using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Nekolpos.Data;
using Nekolpos.System;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    public static class DialogueCompiledBundleCompiler
    {
        public const string PreviewOutputPath = "Library/DialogueAuthoringShadow/DialoguePreviewRuntime.json";

        public static DialogueCompiledRuntimeBundle Compile(DialogueAuthoringProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (project.schemaVersion != 1) throw new ArgumentException("Unsupported authoring schema version.", nameof(project));

            DialogueShadowBundle shadow = DialogueShadowCompiler.Compile(project);
            string sourceName = Path.GetFileNameWithoutExtension(project.source);
            var bundle = new DialogueCompiledRuntimeBundle
            {
                source = project.source,
                sourceSha256 = project.sourceSha256,
                publishable = shadow.publishable
            };

            foreach (IGrouping<(DiagnosticSeverity severity, string code), AuthoringDiagnostic> group in
                     shadow.diagnostics.GroupBy(item => (item.severity, item.code)))
            {
                bundle.diagnostics.Add(new DialogueCompiledDiagnostic
                {
                    severity = group.Key.severity.ToString(),
                    code = group.Key.code,
                    count = group.Count()
                });
            }

            foreach (DialogueAuthoringRoot root in project.dialogues)
            {
                foreach (AuthoringMatcher matcher in root.matchers)
                {
                    LegacyRow raw = root.segments.SelectMany(segment => segment.lines)
                        .Select(line => line.legacy)
                        .First(row => row.sourceRow == matcher.firstSourceRow);
                    string labelRaw = root.normalizedMeaningKey;
                    bundle.patterns.Add(new DialogueCompiledPattern
                    {
                        stableRootId = root.stableId,
                        sourceRow = matcher.firstSourceRow,
                        sourceRegex = matcher.normalizedJapaneseRegex,
                        labelHash = root.legacyLabelHash,
                        priority = matcher.priority,
                        sensitivity = Default(DialogueLegacyImporter.Read(raw, "sensitivity_level", "sensitivity", "フィルタ"), "SAFE"),
                        teachingCategory = TeachingCategory.None,
                        teachingEntryId = ResolveTeachingEntryId(raw, sourceName, labelRaw),
                        teachingSeriesGroupId = labelRaw,
                        teachingDisplayName = DialogueLegacyImporter.Read(raw, "DisplayName", "display_name", "ItemName", "item_name", "input").Trim(),
                        teachingPromptDisabled = DialogueLegacyImporter.Bool(DialogueLegacyImporter.Read(raw, "teaching_prompt_disabled", "TeachingPromptDisabled", "disable_teaching_prompt", "DisableTeachingPrompt"))
                    });
                }

                foreach (AuthoringSegment segment in root.segments)
                foreach (AuthoringLine line in segment.lines)
                {
                    LegacyRow raw = line.legacy;
                    string sourceRegex = DialogueLegacyImporter.Read(raw, "regex_id", "RegexID", "__source_regex", "source_regex", "意味キー", "label", "meaning", "regex");
                    string labelRaw = root.normalizedMeaningKey;
                    bundle.reactions.Add(new DialogueCompiledReaction
                    {
                        stableRootId = root.stableId,
                        stableSegmentId = segment.stableId,
                        stableLineId = line.stableId,
                        sourceRow = raw.sourceRow,
                        labelHash = root.legacyLabelHash,
                        patternId = segment.legacyPattern,
                        diarySourceCsv = sourceName,
                        diarySourceEntryId = DialogueLegacyImporter.Read(raw, "id", "ID").Trim(),
                        diarySourceRegexId = sourceRegex.Trim().Replace("\\_", "_"),
                        speechControl = ToRuntimeSpeech(line.speech),
                        order = ReadIntWithFallback(raw, "Order", "state_seq"),
                        repeatCount = ReadIntWithFallback(raw, "RepeatCount", "終了カウント"),
                        randomRepeatLimit = ReadRandomRepeatLimit(raw),
                        targetPatternId = DialogueLegacyImporter.Read(raw, "target_pattern", "TargetPattern", "targetPattern").Trim(),
                        callOnly = ParseCallOnly(DialogueLegacyImporter.Read(raw, "call_only", "CallOnly", "pattern_access", "PatternAccess")),
                        choiceYesPatternId = line.legacyLineTerminal.choice?.yesTarget?.legacyKey,
                        choiceNoPatternId = line.legacyLineTerminal.choice?.noTarget?.legacyKey,
                        sequenceProgressHold = DialogueLegacyImporter.Bool(DialogueLegacyImporter.Read(raw, "sequence_progress_hold", "SequenceProgressHold", "progress_hold", "進行保持")),
                        intentId = ReadIfPresent(project, raw, "intent"),
                        intentColumnPresent = HasHeader(project, "intent"),
                        reactionType = ReadIfPresent(project, raw, "reaction_type"),
                        reactionTypeColumnPresent = HasHeader(project, "reaction_type"),
                        sourceRegexLabel = sourceRegex,
                        choiceQuestionJa = line.legacyLineTerminal.choice?.legacyRuntimeQuestionJa,
                        choiceYesLabel = line.legacyLineTerminal.choice?.legacyRuntimeYesLabel,
                        choiceNoLabel = line.legacyLineTerminal.choice?.legacyRuntimeNoLabel,
                        actionId = DialogueLegacyImporter.Read(raw, "action_id", "ActionID"),
                        timedEventKey = DialogueLegacyImporter.Read(raw, "timed_event_key", "TimedEventKey", "system_timed_event_key", "SystemTimedEventKey").Trim(),
                        nextAction = ReadIfPresent(project, raw, "NextAction", "反応リンク", "next_action"),
                        nextActionColumnPresent = HasHeader(project, "NextAction", "反応リンク", "next_action"),
                        textJp = line.legacyRuntimeJapaneseText,
                        responseType = line.legacyLineTerminal.legacyResponseType,
                        condition = line.condition.expression,
                        waitTime = line.presentation.waitTime,
                        hideUi = line.presentation.hideUi,
                        animation = line.presentation.animation,
                        reactionIcon = ParseReactionIcon(line.presentation.reactionIcon),
                        emotionChangeType = DialogueLegacyImporter.Read(raw, "emotion_change_type", "EmotionChangeType"),
                        emotionChangeValue = DialogueLegacyImporter.Int(DialogueLegacyImporter.Read(raw, "emotion_change_value", "EmotionChangeValue")),
                        teachingCategory = TeachingCategory.None.ToString(),
                        teachingEntryId = ResolveTeachingEntryId(raw, sourceName, labelRaw),
                        teachingSeriesGroupId = labelRaw,
                        teachingDisplayName = DialogueLegacyImporter.Read(raw, "DisplayName", "display_name", "ItemName", "item_name", "input").Trim(),
                        questionLinks = ReadQuestionLinks(raw)
                    });
                }
            }

            bundle.patterns = bundle.patterns.OrderBy(item => item.sourceRow).ToList();
            bundle.reactions = bundle.reactions.OrderBy(item => item.sourceRow).ToList();

            return bundle;
        }

        public static string CompileJson(DialogueAuthoringProject project = null)
        {
            return JsonUtility.ToJson(Compile(project ?? DialogueLegacyImporter.ImportFile()), false);
        }

        [MenuItem("Nekolpos/開発/会話Runtime/Compiled Bundleを生成")]
        public static void GeneratePreviewBundle()
        {
            string json = CompileJson();
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewOutputPath));
            TalkTopicMasterCsvUtility.AtomicWrite(PreviewOutputPath, global::System.Text.Encoding.UTF8.GetBytes(json));
            DialogueCompiledRuntimeBundle bundle = JsonUtility.FromJson<DialogueCompiledRuntimeBundle>(json);
            Debug.Log($"[DialogueCompiledBundle] {PreviewOutputPath} を生成しました。pattern={bundle.patterns.Count}, reaction={bundle.reactions.Count}, publishable={bundle.publishable}");
        }

        [MenuItem("Nekolpos/開発/会話Runtime/Compiled Bundleを使用")]
        public static void UseCompiledBundle()
        {
            SetMode(DialogueRuntimeSourceMode.CompiledBundle);
        }

        [MenuItem("Nekolpos/開発/会話Runtime/Legacy CSVへ戻す")]
        public static void UseLegacyCsv()
        {
            SetMode(DialogueRuntimeSourceMode.LegacyCsv);
        }

        [MenuItem("Nekolpos/開発/会話Runtime/Compiled Bundleを使用", true)]
        private static bool ValidateUseCompiledBundle()
        {
            Menu.SetChecked("Nekolpos/開発/会話Runtime/Compiled Bundleを使用", DialogueRuntimeSourceSelection.RequestedMode == DialogueRuntimeSourceMode.CompiledBundle);
            return true;
        }

        [MenuItem("Nekolpos/開発/会話Runtime/Legacy CSVへ戻す", true)]
        private static bool ValidateUseLegacyCsv()
        {
            Menu.SetChecked("Nekolpos/開発/会話Runtime/Legacy CSVへ戻す", DialogueRuntimeSourceSelection.RequestedMode == DialogueRuntimeSourceMode.LegacyCsv);
            return true;
        }

        private static void SetMode(DialogueRuntimeSourceMode mode)
        {
            DialogueRuntimeSourceSelection.SetDevelopmentMode(mode);
            DialogueEngine engine = UnityEngine.Object.FindFirstObjectByType<DialogueEngine>();
            if (engine != null) engine.RebuildRuntimeData();
            Debug.Log("[DialogueCompiledBundle] 会話Runtime読込を " + mode + " に切り替えました。");
        }

        private static SpeechControlType ToRuntimeSpeech(AuthoringSpeech speech)
        {
            return speech switch
            {
                AuthoringSpeech.Sequence => SpeechControlType.Sequence,
                AuthoringSpeech.RepeatEvent => SpeechControlType.RepeatEvent,
                AuthoringSpeech.Call => SpeechControlType.Call,
                AuthoringSpeech.Return => SpeechControlType.Return,
                _ => SpeechControlType.Random
            };
        }

        private static string ResolveTeachingEntryId(LegacyRow raw, string sourceName, string labelRaw)
        {
            string explicitId = DialogueLegacyImporter.Read(raw, "EntryID", "entry_id", "TeachingEntryID", "teaching_entry_id");
            if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId.Trim();
            string rowId = DialogueLegacyImporter.Read(raw, "id", "ID");
            return string.IsNullOrWhiteSpace(rowId)
                ? $"{sourceName}:{labelRaw}"
                : $"{sourceName}:{rowId.Trim()}:{labelRaw}";
        }

        private static int ReadIntWithFallback(LegacyRow raw, string primary, string fallback)
        {
            string value = DialogueLegacyImporter.Read(raw, primary);
            return DialogueLegacyImporter.Int(string.IsNullOrWhiteSpace(value) ? DialogueLegacyImporter.Read(raw, fallback) : value);
        }

        private static int ReadRandomRepeatLimit(LegacyRow raw)
        {
            string value = DialogueLegacyImporter.Read(raw, "random_repeat_limit", "RandomRepeatLimit", "random_limit", "RandomLimit");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? Math.Max(-1, parsed) : -1;
        }

        private static bool ParseCallOnly(string value)
        {
            return DialogueLegacyImporter.Bool(value) ||
                   (value ?? string.Empty).Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty)
                       .Equals("callonly", StringComparison.OrdinalIgnoreCase);
        }

        private static ReactionIcon ParseReactionIcon(string value)
        {
            return Enum.TryParse(value, true, out ReactionIcon parsed) && Enum.IsDefined(typeof(ReactionIcon), parsed)
                ? parsed
                : ReactionIcon.None;
        }

        private static List<DialogueLogQuestionLink> ReadQuestionLinks(LegacyRow raw)
        {
            string[] keywords = DialogueLegacyImporter.Read(raw, "question_keyword", "QuestionKeyword", "keyword").Split(new[] { "||" }, StringSplitOptions.None);
            string[] insertTexts = DialogueLegacyImporter.Read(raw, "question_insert_text", "QuestionInsertText", "insertText").Split(new[] { "||" }, StringSplitOptions.None);
            var result = new List<DialogueLogQuestionLink>();
            for (int i = 0; i < Math.Min(keywords.Length, insertTexts.Length); i++)
            {
                string keyword = keywords[i].Trim();
                string insertText = insertTexts[i].Trim();
                if (keyword.Length > 0 && insertText.Length > 0) result.Add(new DialogueLogQuestionLink(keyword, insertText));
            }
            return result;
        }

        private static string ReadIfPresent(DialogueAuthoringProject project, LegacyRow raw, params string[] aliases)
        {
            return HasHeader(project, aliases) ? DialogueLegacyImporter.Read(raw, aliases) : null;
        }

        private static bool HasHeader(DialogueAuthoringProject project, params string[] aliases) =>
            project.headers.Any(header => aliases.Any(alias => NormalizeHeader(header) == NormalizeHeader(alias)));

        private static string NormalizeHeader(string value) =>
            (value ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant().Trim('\uFEFF', '\u200B');

        private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
