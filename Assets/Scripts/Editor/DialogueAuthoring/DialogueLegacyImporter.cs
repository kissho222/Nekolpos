using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Backgammon.Conversation;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    public static class DialogueLegacyImporter
    {
        public const string DefaultSource = "TalkSource/TalkCSV/DialoguePreview.csv";
        public static DialogueAuthoringProject ImportFile(string source = DefaultSource)
        {
            string identity = File.Exists(DialogueAuthoringIdentity.MetadataPath) ? File.ReadAllText(DialogueAuthoringIdentity.MetadataPath) : "";
            var project = ImportText(File.ReadAllText(source), DialogueAuthoringIdentity.NormalizeSource(source), identity);
            DialogueAuthoringValidator.ResolveSharedReferences(project); return project;
        }
        public static DialogueAuthoringProject ImportText(string csv, string source = DefaultSource, string identityJson = "")
        {
            var records = TalkTopicMasterCsvUtility.Parse(csv ?? "");
            var project = new DialogueAuthoringProject { source = source.Replace('\\', '/'), originalCsv = csv ?? "", sourceSha256 = Hash(csv ?? "") };
            if (records.Count == 0) { Add(project, DiagnosticSeverity.Error, "MissingHeader", "CSVヘッダーがありません。"); return project; }
            project.headers.AddRange(records[0].Fields.Select((h, i) => i == 0 ? h.TrimStart('\uFEFF', '\u200B') : h));
            var identities = string.IsNullOrWhiteSpace(identityJson) ? new DialogueAuthoringIdentity.Index() : JsonUtility.FromJson<DialogueAuthoringIdentity.Index>(identityJson);
            var roots = new Dictionary<string, DialogueAuthoringRoot>(StringComparer.Ordinal);
            var matcherKeys = new HashSet<string>(StringComparer.Ordinal);
            int ordinal = 0;
            foreach (var record in records.Skip(1))
            {
                if (record.Fields.All(string.IsNullOrWhiteSpace)) continue;
                var raw = new LegacyRow { ordinal = ordinal++, sourceRow = record.LineNumber };
                for (int i = 0; i < project.headers.Count; i++) raw.cells.Add(new LegacyCell { column = project.headers[i], value = i < record.Fields.Count ? record.Fields[i] : "" });
                if (record.Fields.Count != project.headers.Count) Add(project, DiagnosticSeverity.Error, "ColumnCount", "列数が一致しません。原文はoriginalCsvへ保持。", raw);
                string label = Read(raw, "regex_id", "__source_regex", "source_regex", "意味キー", "label", "meaning", "regex");
                string regex = Read(raw, "regex", "regex_jp", "正規表現", "pattern_text");
                if (string.IsNullOrWhiteSpace(label)) { label = regex; Add(project, DiagnosticSeverity.Info, "MeaningFallback", "regex_id空欄: Regexを意味キーとして使用。", raw, "regex_id"); }
                string normalized = NormalizeMeaning(label);
                if (string.IsNullOrWhiteSpace(normalized)) Add(project, DiagnosticSeverity.Error, "EmptyMeaning", "Runtimeでは意味キーのない行を取り込みません。", raw);
                if (!roots.TryGetValue(normalized, out var root))
                {
                    root = new DialogueAuthoringRoot { stableId = Id("root", project.source, normalized), legacyLabel = label, normalizedMeaningKey = normalized, legacyLabelHash = LabelHash(normalized) };
                    roots.Add(normalized, root); project.dialogues.Add(root);
                }
                string csvId = raw.Read("id", "ID").Trim(); if (!root.legacyCsvIds.Contains(csvId)) root.legacyCsvIds.Add(csvId);
                string normalizedRegex = JapaneseTextNormalizer.NormalizeToken(regex);
                if (!string.IsNullOrWhiteSpace(regex) && matcherKeys.Add(root.legacyLabelHash + "|" + normalizedRegex))
                    root.matchers.Add(new AuthoringMatcher { legacyRegex = regex, normalizedJapaneseRegex = normalizedRegex, priority = Int(Read(raw, "Priority", "優先度")), firstSourceRow = raw.sourceRow,
                        expressions = Locales(raw, "regex_jp", "regex_en", "regex_zh", "regex_cn") });
                string pattern = Read(raw, "Pattern", "pattern", "Pattern ID").Trim();
                var segment = root.segments.FirstOrDefault(s => s.legacyPattern == pattern && pattern.Length > 0);
                if (segment == null)
                {
                    segment = new AuthoringSegment { stableId = Id("segment", root.stableId, pattern.Length > 0 ? pattern : "__row_" + raw.ordinal), legacyPattern = pattern };
                    root.segments.Add(segment);
                    if (pattern.Length == 0) Add(project, DiagnosticSeverity.Warning, "EmptyPattern", "Runtimeは群化ごとにGUIDを発行します。Phase1は出典行で固定して保持。", raw);
                }
                string editorKey = (string.IsNullOrWhiteSpace(raw.Read("regex_id")) ? "(empty)" : raw.Read("regex_id").Trim()) + "|" + (csvId.Length == 0 ? "(empty-id)" : csvId) + "|" + (pattern.Length == 0 ? "__line_" + raw.ordinal : raw.Read("pattern").Trim());
                foreach (var identity in identities.records.Where(i => i.source == project.source && i.key == editorKey))
                    if (!segment.legacyEditorIdentityIds.Contains(identity.id)) segment.legacyEditorIdentityIds.Add(identity.id);
                var line = new AuthoringLine { stableId = Id("line", project.source, raw.ordinal.ToString(CultureInfo.InvariantCulture)), order = Int(OrderValue(raw)), legacy = raw, legacyRuntimeJapaneseText = Read(raw, "text_jp 1", "output_ja", "Line", "text_jp1", "text"),
                    texts = Locales(raw, "output_ja", "output_en", "output_zh", "text_cn"), condition = Condition(Read(raw, "condition", "条件", "tag", "tags"), ConditionScope.LegacyLine),
                    speech = Speech(project, raw), callTarget = Ref(ReferenceKind.Segment, Read(raw, "target_pattern", "TargetPattern")),
                    presentation = new AuthoringPresentation { waitTime = Float(Read(raw, "WaitTime", "wait_time")), hideUi = Bool(Read(raw, "HideUI", "hide_ui")), animation = Read(raw, "Animation"), reactionIcon = Read(raw, "ReactionIcon", "reaction_icon") },
                    legacyLineTerminal = Terminal(project, raw) };
                segment.lines.Add(line);
            }
            foreach (var root in project.dialogues)
            {
                var initial = root.segments.Select((s, i) => (s, i)).ToDictionary(x => x.s.stableId, x => x.i);
                root.segments.Sort((a, b) => int.TryParse(a.legacyPattern, out int an) && int.TryParse(b.legacyPattern, out int bn) && an != bn ? an.CompareTo(bn) : initial[a.stableId].CompareTo(initial[b.stableId]));
                foreach (var segment in root.segments)
                {
                    // Equal-order rows retain provenance, but Runtime List.Sort is not stable; validator marks the ambiguity.
                    segment.lines = segment.lines.OrderBy(l => l.order).ThenBy(l => l.legacy.ordinal).ToList();
                    var first = segment.lines[0]; var raw = first.legacy;
                    segment.representativeSpeech = first.speech; segment.selectionCondition = Condition(first.condition.expression, ConditionScope.RepresentativeSelection);
                    segment.repeatThreshold = Math.Max(2, Int(RepeatValue(raw)));
                    segment.randomRepeatLimit = int.TryParse(Read(raw, "random_repeat_limit", "random_limit"), out int limit) ? Math.Max(-1, limit) : -1;
                    segment.progressPolicy = Bool(Read(raw, "sequence_progress_hold", "progress_hold", "進行保持")) ? SequenceProgressPolicy.HoldResetAnchor : SequenceProgressPolicy.Resettable;
                    segment.anyLineCallOnly = segment.lines.Any(l => CallOnly(Read(l.legacy, "call_only", "pattern_access")));
                    segment.repeatCallOnlyException = segment.anyLineCallOnly && first.speech == AuthoringSpeech.RepeatEvent;
                    segment.eligibleForNormalSelection = !segment.anyLineCallOnly || segment.repeatCallOnlyException;
                    segment.emotionChangeType = Read(raw, "emotion_change_type"); segment.emotionChangeValue = Int(Read(raw, "emotion_change_value"));
                    segment.terminal = segment.lines.Last().legacyLineTerminal;
                    foreach (var line in segment.lines)
                    {
                        Resolve(root, line.callTarget); Resolve(root, line.legacyLineTerminal.continuation);
                        if (line.legacyLineTerminal.choice != null) { Resolve(root, line.legacyLineTerminal.choice.yesTarget); Resolve(root, line.legacyLineTerminal.choice.noTarget); }
                        foreach (var reference in new[] { line.legacyLineTerminal.action, line.legacyLineTerminal.timedEvent })
                            if (reference != null && !segment.externalRefs.Any(r => r.kind == reference.kind && r.legacyKey == reference.legacyKey)) segment.externalRefs.Add(reference);
                    }
                    root.selectionPolicy.patternOrder.Add(segment.stableId);
                    if (!segment.eligibleForNormalSelection) continue;
                    if (first.speech == AuthoringSpeech.RepeatEvent) root.selectionPolicy.repeatOverrides.Add(segment.stableId);
                    if (first.speech == AuthoringSpeech.Random) root.selectionPolicy.randomCandidates.Add(segment.stableId);
                    if (first.speech == AuthoringSpeech.Sequence) root.selectionPolicy.sequenceCandidates.Add(segment.stableId);
                }
            }
            DialogueAuthoringValidator.Validate(project);
            return project;
        }
        internal static void Resolve(DialogueAuthoringRoot root, AuthoringReference reference)
        {
            if (reference == null) return;
            var segment = root.segments.FirstOrDefault(s => s.legacyPattern == reference.legacyKey);
            reference.resolved = segment != null; reference.stableTargetId = segment?.stableId ?? "";
        }
        internal static AuthoringReference Ref(ReferenceKind kind, string key) => string.IsNullOrWhiteSpace(key) ? null : new AuthoringReference { kind = kind, legacyKey = key.Trim(), stableTargetId = "" };
        internal static AuthoringCondition Condition(string value, ConditionScope scope) => new() { expression = value, unconditional = string.IsNullOrWhiteSpace(value), scope = scope };
        internal static List<LocalizedValue> Locales(LegacyRow raw, string ja, string en, string zh, string zhAlias = "") => new()
        {
            new() { locale = "ja", value = raw.Read(ja) }, new() { locale = "en", value = raw.Read(en) }, new() { locale = "zh", value = raw.Read(zh, zhAlias) }
        };
        private static AuthoringTerminal Terminal(DialogueAuthoringProject project, LegacyRow raw)
        {
            string type = Read(raw, "response_type").Trim();
            var terminal = new AuthoringTerminal { legacyResponseType = type, action = Ref(ReferenceKind.Action, Read(raw, "action_id", "ActionID")), timedEvent = Ref(ReferenceKind.TimedEvent, Read(raw, "timed_event_key", "system_timed_event_key")), continuation = Ref(ReferenceKind.Segment, Read(raw, "target_pattern", "TargetPattern")) };
            switch (type.ToLowerInvariant())
            {
                case "": case "normal": case "reaction": terminal.kind = TerminalKind.End; break;
                case "action": terminal.kind = TerminalKind.Action; break;
                case "event": terminal.kind = TerminalKind.Event; break;
                case "choice": terminal.kind = TerminalKind.Choice; break;
                default: terminal.kind = TerminalKind.Unknown; break;
            }
            // Retain dormant Choice columns too: they are not an active Choice unless kind=Choice.
            terminal.choice = new AuthoringChoice { legacyRuntimeQuestionJa = Read(raw, "question_ja", "ChoiceQuestionJa", "question"), legacyRuntimeYesLabel = Read(raw, "choice_yes_ja", "ChoiceYesLabel", "choice_yes_label"), legacyRuntimeNoLabel = Read(raw, "choice_no_ja", "ChoiceNoLabel", "choice_no_label"), question = Locales(raw, "question_ja", "question_en", "question_cn"), yesLabel = Locales(raw, "choice_yes_ja", "choice_yes_en", "choice_yes_cn"), noLabel = Locales(raw, "choice_no_ja", "choice_no_en", "choice_no_cn") };
            terminal.choice.yesTarget = ChoiceTarget(project, raw, "choice_yes_pattern", "yes_next_pattern", out terminal.choice.yesOriginColumn);
            terminal.choice.noTarget = ChoiceTarget(project, raw, "choice_no_pattern", "no_next_pattern", out terminal.choice.noOriginColumn);
            return terminal;
        }
        private static AuthoringReference ChoiceTarget(DialogueAuthoringProject project, LegacyRow raw, string current, string fallback, out string origin)
        {
            string value = Read(raw, current); origin = current;
            if (string.IsNullOrWhiteSpace(value)) { value = Read(raw, fallback); origin = fallback; if (!string.IsNullOrWhiteSpace(value)) Add(project, DiagnosticSeverity.Info, "ChoiceFallback", fallback + "を実効分岐へ使用。", raw, fallback); }
            return Ref(ReferenceKind.Segment, value);
        }
        private static AuthoringSpeech Speech(DialogueAuthoringProject project, LegacyRow raw)
        {
            string value = Read(raw, "SpeechControl", "speech_control").Trim();
            if (Enum.GetNames(typeof(AuthoringSpeech)).Any(name => name.Equals(value, StringComparison.OrdinalIgnoreCase))) return (AuthoringSpeech)Enum.Parse(typeof(AuthoringSpeech), value, true);
            if (value.Length > 0) Add(project, DiagnosticSeverity.Warning, "UnknownSpeechControl", "未知SpeechControl: Runtimeと同じ推定を使用。", raw, "SpeechControl");
            Add(project, DiagnosticSeverity.Info, "SpeechFallback", "Order→RepeatCount→Randomの推定。", raw, "SpeechControl");
            if (int.TryParse(OrderValue(raw), out _)) return AuthoringSpeech.Sequence;
            if (int.TryParse(RepeatValue(raw), out _)) return AuthoringSpeech.RepeatEvent;
            return AuthoringSpeech.Random;
        }
        private static string OrderValue(LegacyRow raw) { string value = Read(raw, "Order"); return string.IsNullOrWhiteSpace(value) ? Read(raw, "state_seq") : value; }
        private static string RepeatValue(LegacyRow raw) { string value = Read(raw, "RepeatCount"); return string.IsNullOrWhiteSpace(value) ? Read(raw, "end_count") : value; }
        internal static string Read(LegacyRow raw, params string[] aliases)
        {
            foreach (var alias in aliases)
                foreach (var cell in raw.cells)
                    if (Header(cell.column) == Header(alias)) return (cell.value ?? "").Replace("\"", ""); // Legacy Engine removes quote characters while parsing.
            return "";
        }
        private static string Header(string s) => (s ?? "").Replace(" ", "").Replace("_", "").ToLowerInvariant().Trim('\uFEFF', '\u200B');
        internal static int Int(string s) => int.TryParse(s, out int n) ? n : 0;
        internal static float Float(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0;
        internal static bool Bool(string s) => new[] { "true", "1", "yes", "on" }.Contains((s ?? "").Trim().ToLowerInvariant());
        private static bool CallOnly(string s) => Bool(s) || s.Trim().Replace("_", "").Replace("-", "").Replace(" ", "").Equals("callonly", StringComparison.OrdinalIgnoreCase);
        public static string NormalizeMeaning(string value)
        {
            var chars = (value ?? "").ToCharArray(); for (int i = 0; i < chars.Length; i++) if (chars[i] >= 0x30A1 && chars[i] <= 0x30F6) chars[i] = (char)(chars[i] - 0x60);
            return new string(chars).Replace("\uFEFF", "").Replace("\u200B", "").Trim().Trim('　');
        }
        private static int LabelHash(string label) { if (label.Length == 0) return 0; using var md5 = MD5.Create(); return BitConverter.ToInt32(md5.ComputeHash(Encoding.UTF8.GetBytes(label)), 0); }
        internal static string Id(string kind, params string[] parts) => kind + "_" + Hash(string.Join("", parts.Select(p => p.Length + ":" + p)));
        internal static string Hash(string text) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2"))); }
        internal static void Add(DialogueAuthoringProject p, DiagnosticSeverity severity, string code, string message, LegacyRow row = null, string column = "", string id = "")
        {
            var segment = p.dialogues.SelectMany(r => r.segments).FirstOrDefault(s => s.lines.Any(l => ReferenceEquals(l.legacy, row)));
            string text = row?.Read("output_ja", "text", "text_jp", "ja") ?? "";
            p.diagnostics.Add(new AuthoringDiagnostic { severity = severity, code = code, message = message,
                sourceRow = row?.sourceRow ?? 0, column = column, stableId = id,
                csvId = row?.Read("id"), regexId = row?.Read("regex_id"), pattern = row?.Read("pattern"),
                order = row?.Read("order"), responseType = row?.Read("response_type"), currentValue = row?.Read(column),
                textPreview = text.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(40, text.Length)),
                dialogueId = segment == null ? "" : string.Join(", ", segment.legacyEditorIdentityIds), segmentId = segment?.stableId });
        }
    }
}
