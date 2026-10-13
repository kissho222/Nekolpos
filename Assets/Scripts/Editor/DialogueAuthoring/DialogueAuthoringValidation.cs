using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    public static class DialogueAuthoringValidator
    {
        public static void Validate(DialogueAuthoringProject project)
        {
            foreach (var headers in project.headers.GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                DialogueLegacyImporter.Add(project, DiagnosticSeverity.Error, "DuplicateColumn", "重複列: " + headers.Key);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var collision in project.dialogues.GroupBy(r => r.legacyLabelHash).Where(g => g.Select(r => r.normalizedMeaningKey).Distinct().Count() > 1))
                DialogueLegacyImporter.Add(project, DiagnosticSeverity.Error, "LabelHashCollision", "異なる意味キーが同じRuntime hashを共有。" );
            foreach (var idCollision in project.dialogues.SelectMany(r => r.legacyCsvIds.Select(id => (id, r))).GroupBy(x => x.id).Where(g => g.Select(x => x.r.stableId).Distinct().Count() > 1))
                DialogueLegacyImporter.Add(project, DiagnosticSeverity.Warning, "LegacyIdCollision", "同じCSV idを異なる意味キーが共有: " + idCollision.Key);
            foreach (var collision in project.dialogues.SelectMany(r => r.segments).SelectMany(s => s.legacyEditorIdentityIds.Select(id => (id, s.stableId))).GroupBy(x => x.id).Where(g => g.Select(x => x.stableId).Distinct().Count() > 1))
                DialogueLegacyImporter.Add(project, DiagnosticSeverity.Error, "LegacyEditorIdentityCollision", "異なるSegmentが同じEditor GUIDを共有: " + collision.Key);
            foreach (var root in project.dialogues)
            {
                Unique(root.stableId);
                foreach (var segment in root.segments)
                {
                    Unique(segment.stableId);
                    foreach (var group in segment.lines.GroupBy(l => l.order).Where(g => g.Count() > 1))
                        Diagnostic(DiagnosticSeverity.Warning, "DuplicateOrder", "同Order: Runtime Sortの代表/終端順は保証できません。", group.First(), "order");
                    foreach (string column in new[] { "condition", "SpeechControl", "RepeatCount", "random_repeat_limit", "sequence_progress_hold", "Priority", "emotion_change_type", "emotion_change_value" })
                        if (segment.lines.Select(l => l.legacy.Read(column)).Distinct(StringComparer.Ordinal).Count() > 1)
                            Diagnostic(DiagnosticSeverity.Warning, "RepresentativeConflict", "代表属性の行間差異。実効代表はOrder最小行: " + column, segment.lines[0], column);
                    if (segment.legacyEditorIdentityIds.Count > 1) Diagnostic(DiagnosticSeverity.Warning, "EditorIdentitySplit", "1Runtime Segmentに複数Editor GUID。", segment.lines[0]);
                    foreach (var line in segment.lines)
                    {
                        Unique(line.stableId);
                        if (line.legacyLineTerminal.kind == TerminalKind.Unknown) Diagnostic(DiagnosticSeverity.Warning, "UnknownResponseType", "未知response_typeを保存。Runtimeは入力へ復帰。", line, "response_type");
                        foreach (var reference in new[] { line.callTarget, line.legacyLineTerminal.choice.yesTarget, line.legacyLineTerminal.choice.noTarget })
                            if (reference != null && !string.IsNullOrWhiteSpace(reference.legacyKey))
                            {
                                if (reference.legacyKey == "0" && !reference.resolved) Diagnostic(DiagnosticSeverity.Warning, "AmbiguousZeroTarget", "分岐先「0」が見つかりません。" + (line.legacyLineTerminal.legacyResponseType == "Normal" ? "現在この行はNormalなので、古いChoice設定が残っている可能性があります。" : "分岐先を確認してください。") + "元値は保持しています。", line,
                                    ReferenceEquals(reference, line.legacyLineTerminal.choice.yesTarget) ? line.legacyLineTerminal.choice.yesOriginColumn : ReferenceEquals(reference, line.legacyLineTerminal.choice.noTarget) ? line.legacyLineTerminal.choice.noOriginColumn : "target_pattern");
                                else if (!reference.resolved) Diagnostic(DiagnosticSeverity.Error, "MissingSegment", "参照Patternなし: " + reference.legacyKey, line);
                            }
                        foreach (var pair in new[] { ("choice_yes_pattern", "yes_next_pattern"), ("choice_no_pattern", "no_next_pattern") })
                            if (!string.IsNullOrWhiteSpace(line.legacy.Read(pair.Item1)) && !string.IsNullOrWhiteSpace(line.legacy.Read(pair.Item2)) && line.legacy.Read(pair.Item1) != line.legacy.Read(pair.Item2))
                                Diagnostic(DiagnosticSeverity.Warning, "ChoiceAliasConflict", "新旧値不一致: 新列優先、旧値も保持。", line, pair.Item1);
                        ValidateCondition(line);
                        foreach (string column in new[] { "order", "RepeatCount", "random_repeat_limit", "Priority", "emotion_change_value" })
                            if (!string.IsNullOrWhiteSpace(line.legacy.Read(column)) && !int.TryParse(line.legacy.Read(column), out _)) Diagnostic(DiagnosticSeverity.Warning, "InvalidInteger", "整数として解釈できません。", line, column);
                        if (line.legacy.cells.Any(c => c.value.Contains("\""))) Diagnostic(DiagnosticSeverity.Info, "LegacyQuoteRemoval", "元セルの引用符は保存。Runtime parserは文字を除去。", line);
                    }
                }
            }
            DialogueLegacyImporter.Add(project, DiagnosticSeverity.Info, "EmptyConditionIsUnconditional", "空Conditionは通常候補では無条件。旧値を保持。", column: "condition");
            void Unique(string id) { if (!ids.Add(id)) DialogueLegacyImporter.Add(project, DiagnosticSeverity.Error, "StableIdCollision", "重複Stable ID", id: id); }
            void Diagnostic(DiagnosticSeverity severity, string code, string message, AuthoringLine line, string column = "") => DialogueLegacyImporter.Add(project, severity, code, message, line.legacy, column, line.stableId);
            void ValidateCondition(AuthoringLine line)
            {
                string expression = line.condition.expression;
                if (string.IsNullOrWhiteSpace(expression)) return;
                // Check every logical clause, avoiding the evaluator's short-circuit masking invalid later clauses.
                string normalized = Regex.Replace(expression, @"\bAND\b", "&&", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bOR\b", "||", RegexOptions.IgnoreCase);
                foreach (string clause in Regex.Split(normalized, @"&&|\|\|"))
                {
                    if (string.IsNullOrWhiteSpace(clause)) { Diagnostic(DiagnosticSeverity.Warning, "EmptyConditionOperand", "空の論理条件。", line, "condition"); continue; }
                    try { ConversationGameStateConditionEvaluator.Evaluate(new ConversationGameState(), clause); }
                    catch (FormatException exception) { Diagnostic(DiagnosticSeverity.Error, "InvalidCondition", exception.Message, line, "condition"); }
                }
            }
        }
        public static void ResolveSharedReferences(DialogueAuthoringProject project)
        {
            project.diagnostics.RemoveAll(d => d.code == "MissingTimedEvent" || d.code == "UnresolvedAction" || d.code == "ActionRequiresRuntimeResolver" || d.code == "ResolvedAction");
            project.actionResolutions.Clear();
            var timed = TalkTopicMasterCsvUtility.ReadDocument("TalkSource/TalkCSV/SystemTimedEvent.csv");
            var events = new HashSet<string>(timed.Rows.Select(r => TalkTopicMasterCsvUtility.ReadValue(r, "key")), StringComparer.OrdinalIgnoreCase);
            var actions = new HashSet<string>(events, StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:ActionData"))
            {
                var action = AssetDatabase.LoadAssetAtPath<Nekolpos.ActionSystem.ActionData>(AssetDatabase.GUIDToAssetPath(guid));
                if (action != null) actions.Add(action.ActionId);
            }
            foreach (var segment in project.dialogues.SelectMany(r => r.segments))
            {
                foreach (var reference in segment.lines.SelectMany(l => new[] { l.legacyLineTerminal.action, l.legacyLineTerminal.timedEvent }).Where(r => r != null && !string.IsNullOrWhiteSpace(r.legacyKey)))
                {
                    reference.resolved = reference.kind == ReferenceKind.TimedEvent ? events.Contains(reference.legacyKey) : DialogueAuthoringActionResolver.Classify(reference.legacyKey, actions).kind != ActionResolutionKind.Unresolved;
                    if (!reference.resolved && reference.kind == ReferenceKind.TimedEvent) DialogueLegacyImporter.Add(project, DiagnosticSeverity.Error,
                        "MissingTimedEvent", "時間経過イベントが見つかりません: " + reference.legacyKey, segment.lines.First(l => ReferenceEquals(l.legacyLineTerminal.timedEvent, reference)).legacy, "timed_event_key", segment.stableId);
                    if (events.Contains(reference.legacyKey))
                    {
                        var row = timed.Rows.First(r => string.Equals(TalkTopicMasterCsvUtility.ReadValue(r, "key"), reference.legacyKey, StringComparison.OrdinalIgnoreCase));
                        string diaryId = TalkTopicMasterCsvUtility.ReadValue(row, "diary_id");
                        if (string.IsNullOrWhiteSpace(diaryId)) diaryId = reference.legacyKey;
                        var diary = DialogueLegacyImporter.Ref(ReferenceKind.Diary, diaryId); diary.resolved = true;
                        if (!segment.externalRefs.Any(r => r.kind == diary.kind && r.legacyKey == diary.legacyKey)) segment.externalRefs.Add(diary);
                    }
                }
            }
            foreach (var group in project.dialogues.SelectMany(root => root.segments.SelectMany(s => s.lines.Select(line => (root, line))))
                .Where(x => !string.IsNullOrWhiteSpace(x.line.legacyLineTerminal.action?.legacyKey))
                .GroupBy(x => x.line.legacyLineTerminal.action.legacyKey.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var resolution = DialogueAuthoringActionResolver.Classify(group.Key, actions);
                resolution.rowCount = group.Count(); resolution.dialogueCount = group.Select(x => x.root.stableId).Distinct().Count();
                resolution.sourceRows.AddRange(group.Select(x => x.line.legacy.sourceRow)); project.actionResolutions.Add(resolution);
                DialogueLegacyImporter.Add(project, resolution.kind == ActionResolutionKind.Unresolved ? DiagnosticSeverity.Error : DiagnosticSeverity.Info,
                    resolution.kind == ActionResolutionKind.Unresolved ? "UnresolvedAction" : "ResolvedAction",
                    $"Action ID: {group.Key} / 出現: {resolution.rowCount}行 / 参照会話: {resolution.dialogueCount}件 / 解決方式: {resolution.resolver} / 状態: {(resolution.kind == ActionResolutionKind.Unresolved ? "参照切れ・要確認" : "正常")}", group.First().line.legacy, "action_id", group.First().line.stableId);
            }
            var topics = TalkTopicMasterCsvUtility.ReadDocument(TalkTopicMasterCsvUtility.SourcePath);
            var links = TalkTopicMasterCsvUtility.ReadDocument(DialogueDiaryAuthoring.LinkPath);
            foreach (var segment in project.dialogues.SelectMany(r => r.segments))
            {
                foreach (var line in segment.lines)
                {
                    var row = line.legacy.cells.GroupBy(c => c.column, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().value, StringComparer.OrdinalIgnoreCase);
                    string key = TalkTopicMasterGenerator.BuildSourceKey(row, line.legacy.ordinal);
                    foreach (var topic in topics.Rows.Where(t => TalkTopicMasterCsvUtility.ReadValue(t, "Source") == Path.GetFileName(project.source) && TalkTopicMasterCsvUtility.ReadValue(t, "SourceKey") == key))
                        AddRef(ReferenceKind.Topic, TalkTopicMasterCsvUtility.ReadValue(topic, "TalkID"));
                }
                foreach (var link in links.Rows.Where(l => segment.legacyEditorIdentityIds.Contains(TalkTopicMasterCsvUtility.ReadValue(l, "DialogueId"))))
                    AddRef(ReferenceKind.Diary, TalkTopicMasterCsvUtility.ReadValue(link, "DiaryId"));
                void AddRef(ReferenceKind kind, string key) { if (!string.IsNullOrWhiteSpace(key) && !segment.externalRefs.Any(r => r.kind == kind && r.legacyKey == key)) segment.externalRefs.Add(new AuthoringReference { kind = kind, legacyKey = key, resolved = true }); }
            }
        }
    }
    public static class DialogueAuthoringComparison
    {
        public static string ExportLegacySimulation(DialogueAuthoringProject project)
        {
            var rows = project.dialogues.SelectMany(r => r.segments).SelectMany(s => s.lines).Select(l => l.legacy).OrderBy(r => r.ordinal).ToList();
            string Escape(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
            return string.Join(",", project.headers.Select(Escape)) + "\n" + string.Join("\n", rows.Select(row => string.Join(",", row.cells.Select(c => Escape(c.value)))));
        }
        public static List<string> RoundTripLoss(DialogueAuthoringProject project)
        {
            var before = TalkTopicMasterCsvUtility.Parse(project.originalCsv).Where((r, i) => i == 0 || r.Fields.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
            var after = TalkTopicMasterCsvUtility.Parse(ExportLegacySimulation(project)); var loss = new List<string>();
            if (before.Count != after.Count) loss.Add("RowCount");
            for (int i = 0; i < Math.Min(before.Count, after.Count); i++) if (!before[i].Fields.SequenceEqual(after[i].Fields)) loss.Add("Row:" + i);
            return loss;
        }
        public static List<string> EditorParity(DialogueAuthoringProject project, string path)
        {
            var data = DialogueDataLoader.LoadFromPath(path); var loss = new List<string>();
            var lines = project.dialogues.SelectMany(r => r.segments).SelectMany(s => s.lines).OrderBy(l => l.legacy.ordinal).ToList();
            if (lines.Count != data.Entries.Count) loss.Add("LineCount");
            if (data.Entries.Select((e, i) => DialogueAuthoringIdentity.GroupKey(e, i)).Distinct().Count() != project.dialogues.Sum(r => r.segments.Count)) loss.Add("PatternCount");
            for (int i = 0; i < Math.Min(lines.Count, data.Entries.Count); i++)
            {
                var e = data.Entries[i]; var l = lines[i];
                if (e.Text != l.texts[0].value || e.TextEnglish != l.texts[1].value || e.TextChinese != l.texts[2].value || e.Order != l.order || e.Condition != l.condition.expression || e.ActionId != l.legacyLineTerminal.action?.legacyKey && !string.IsNullOrEmpty(e.ActionId)) loss.Add("Editor:" + i);
                foreach (string column in project.headers) if (e.GetValue(column) != l.legacy.Read(column)) loss.Add("Cell:" + i + ":" + column);
            }
            return loss;
        }
    }
}
