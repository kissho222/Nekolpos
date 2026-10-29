using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Backgammon.Conversation;
using Nekolpos.EditorTools.Authoring;
using Nekolpos.System;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    // Read saved sources only. Never bind identities, publish encrypted data, or evaluate live state.
    public static class ProductionDataChecks
    {
        public const string OutputDirectory = "Library/ProductionDataChecks";
        [Serializable] public sealed class Finding
        {
            public string severity, code, file, id, column, reason, suggestion;
            public int line;
        }
        [Serializable] public sealed class Input { public string file, sha256; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, status;
            public int exitCode, errors, warnings, information;
            public List<Input> inputs = new();
            public List<Finding> findings = new();
        }
        internal sealed class Table
        {
            public string path;
            public List<string> headers;
            public List<(int line, Dictionary<string, string> cells)> rows = new();
        }

        [MenuItem("Tools/Nekolpos/制作データ/静的品質チェック")]
        public static void RunMenu()
        {
            var report = Run();
            Debug.Log($"制作データ静的チェック: {report.status} / Error {report.errors}, Warning {report.warnings}, Info {report.information} / {OutputDirectory}/report.txt");
        }

        // Unity -batchmode -projectPath ... -executeMethod Nekolpos.EditorTools.ProductionDataChecks.RunBatch -logFile ...
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch requires batch mode. Use RunMenu in the Editor.");
            try { EditorApplication.Exit(Run().exitCode); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(2); }
        }

        public static Report Run()
        {
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion };
            bool environmentFailure = false;
            var tables = new Dictionary<string, Table>(StringComparer.Ordinal);
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed || !Directory.Exists("TalkSource/TalkCSV"))
            {
                Add(report, "Error", "Environment", "", 0, "", "", "非Play・コンパイル完了・プロジェクトルートが必要です。", "Editorの停止後に再実行。");
                environmentFailure = true;
            }
            else
            {
                var sources = Directory.GetFiles("TalkSource/TalkCSV", "*.csv")
                    .Concat(new[] { DialogueDiaryAuthoring.CandidatePath, DialogueDiaryAuthoring.LinkPath, TalkTopicMasterCsvUtility.SourcePath })
                    .OrderBy(p => p, StringComparer.Ordinal).ToArray();
                foreach (string path in sources)
                {
                    try
                    {
                        Snapshot(path);
                        var table = InspectCsv(report, path, File.ReadAllText(path, new UTF8Encoding(false, true)));
                        if (table != null) tables.Add(path.Replace('\\', '/'), table);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException)
                    {
                        Add(report, "Error", "InputUnavailable", path, 0, "", "", exception.GetType().Name + ": 入力を読めません。", "ファイルの存在・権限・UTF-8を確認。");
                        environmentFailure |= !(exception is DecoderFallbackException);
                    }
                }
                Snapshot(DialogueAuthoringIdentity.MetadataPath);
                if (Directory.Exists("Assets/Yarn"))
                    foreach (string path in Directory.GetFiles("Assets/Yarn", "*.yarn", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)) Snapshot(path);
                foreach (string guid in AssetDatabase.FindAssets("t:ActionData").OrderBy(g => g, StringComparer.Ordinal)) Snapshot(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var table in tables.Values)
                {
                    if (Path.GetFileName(table.path) == "InternalDialogue.csv") CheckInternal(report, table);
                    else environmentFailure |= !CheckDialogue(report, table, DialogueLegacyImporter.ImportFile);
                    if (Path.GetFileName(table.path) == "SystemTimedEvent.csv") CheckTimed(report, table);
                    if (Path.GetFileName(table.path) == "DiaryCandidates.csv")
                        CheckCandidates(report, table);
                    if (Path.GetFileName(table.path) == "KanjiReadingDictionary.csv") CheckDictionary(report, table);
                    if (Path.GetFileName(table.path) == "TalkTopicMaster.csv")
                    {
                        Require(report, table, "TalkID", "Source", "SourceKey", "Enabled");
                        Unique(report, table, StringComparer.OrdinalIgnoreCase, "TalkID");
                    }
                    CheckVariantJson(report, table);
                }
                CheckLinks(report, tables);
                foreach (var input in report.inputs)
                    if (!File.Exists(input.file) || Hash(File.ReadAllBytes(input.file)) != input.sha256)
                    { environmentFailure = true; Add(report, "Error", "InputChanged", input.file, 0, "", "", "検査中に入力が変更されました。", "編集終了後に再実行。"); }
            }
            report.findings = report.findings.OrderBy(f => f.file, StringComparer.Ordinal).ThenBy(f => f.line).ThenBy(f => f.code, StringComparer.Ordinal).ThenBy(f => f.column, StringComparer.Ordinal).ToList();
            report.errors = report.findings.Count(f => f.severity == "Error");
            report.warnings = report.findings.Count(f => f.severity == "Warning");
            report.information = report.findings.Count(f => f.severity == "Info");
            report.exitCode = environmentFailure ? 2 : report.errors > 0 ? 1 : 0;
            report.status = report.exitCode == 2 ? "environment_unavailable" : report.exitCode == 1 ? "inspection_failed" : "success";
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(OutputDirectory + "/report.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            string[] headings = { "severity", "code", "file", "line", "id", "column", "reason", "suggestion" };
            string Csv(IEnumerable<string> values) => string.Join(",", values.Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""));
            File.WriteAllLines(OutputDirectory + "/report.csv", new[] { Csv(headings) }.Concat(report.findings.Select(f => Csv(new[] { f.severity, f.code, f.file, f.line.ToString(CultureInfo.InvariantCulture), f.id, f.column, f.reason, f.suggestion }))), new UTF8Encoding(true));
            File.WriteAllLines(OutputDirectory + "/report.txt", new[] { $"{report.status} (exit {report.exitCode}) Error={report.errors} Warning={report.warnings} Info={report.information}", "Unity " + report.unityVersion + " / " + report.generatedUtc }.Concat(report.findings.Select(f => $"{f.file}:{f.line} [{f.severity}] {f.code} {f.id} {f.column}: {f.reason} / {f.suggestion}")), new UTF8Encoding(false));
            return report;

            void Snapshot(string path)
            {
                if (File.Exists(path)) report.inputs.Add(new Input { file = path.Replace('\\', '/'), sha256 = Hash(File.ReadAllBytes(path)) });
            }
        }

        internal static Table InspectCsv(Report report, string path, string csv)
        {
            List<TalkTopicMasterCsvUtility.CsvRecord> records;
            try { records = TalkTopicMasterCsvUtility.Parse(csv); }
            catch (FormatException)
            { Add(report, "Error", "CsvParse", path, Math.Max(1, csv.Count(c => c == '\n') + 1), "", "", "引用符が閉じていません（行は検出位置）。", "開始セルと終端の引用符を確認。"); return null; }
            if (records.Count == 0 || records[0].Fields.All(string.IsNullOrWhiteSpace))
            { Add(report, "Error", "MissingHeader", path, 1, "", "", "ヘッダーがありません。", "正本仕様のヘッダーを指定。"); return null; }
            var table = new Table { path = path.Replace('\\', '/'), headers = records[0].Fields.Select((h, i) => i == 0 ? h.TrimStart('\uFEFF') : h).ToList() };
            foreach (var group in table.headers.Where(h => h.Length > 0).GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Add(report, "Error", "DuplicateColumn", path, 1, "", group.Key, "重複ヘッダーです。", "列名を一意にする。");
            foreach (var record in records.Skip(1).Where(r => r.Fields.Any(v => !string.IsNullOrWhiteSpace(v))))
            {
                if (record.Fields.Count != table.headers.Count)
                    Add(report, "Error", "ColumnCount", path, record.LineNumber, "", "", $"列数 {record.Fields.Count} / ヘッダー {table.headers.Count}。", "カンマ・引用符と末尾空列を確認。");
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < table.headers.Count; i++) cells[table.headers[i]] = i < record.Fields.Count ? record.Fields[i] : "";
                table.rows.Add((record.LineNumber, cells));
            }
            return table;
        }

        internal static void CheckTimed(Report report, Table table)
        {
            Require(report, table, "key", "ja", "time_minutes", "display_seconds");
            Unique(report, table, StringComparer.OrdinalIgnoreCase, "key");
            foreach (var row in table.rows)
                foreach (string column in new[] { "time_minutes", "display_seconds" })
                {
                    string value = Read(row.cells, column);
                    if (string.IsNullOrWhiteSpace(value)) continue; // Runtime defaults are valid.
                    bool valid = column == "time_minutes" ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) && minutes >= 0 :
                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) && !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= 0;
                    if (!valid) Add(report, "Error", "TimedValue", table.path, row.line, Read(row.cells, "key"), column, "非負の整数（分）または有限数（秒）が必要です。", "例: time_minutes=30, display_seconds=2.5。空欄/0は既定値。");
                }
        }

        private static bool CheckDialogue(Report report, Table table, Func<string, DialogueAuthoringProject> import)
        {
            if (!table.headers.Contains("regex_jp", StringComparer.OrdinalIgnoreCase)) return true;
            Require(report, table, "id", "regex_jp", "regex_id", "pattern", "order", "response_type", "condition", "output_ja");
            try
            {
                var project = import(table.path);
                foreach (var d in project.diagnostics)
                    Add(report, d.severity.ToString(), "Dialogue." + d.code, table.path, d.sourceRow, d.stableId, d.column, d.message, "既存「作者確認・診断」で該当行を確認。");
                return true;
            }
            catch (Exception exception)
            {
                Add(report, "Error", "DialogueInspectionFailed", table.path, 0, "", "", exception.GetType().Name + ": 会話診断を完了できません。", "既存Validatorの例外・入力を確認。");
                return false;
            }
        }

        private static void CheckVariantJson(Report report, Table table)
        {
            foreach (var row in table.rows)
                foreach (string column in new[] { "display_variants", "diary_variants" }.Where(c => table.headers.Contains(c, StringComparer.OrdinalIgnoreCase)))
                    try { DiaryTextVariants.Read("", "", "", Read(row.cells, column)); }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
                    { Add(report, "Error", "VariantJson", table.path, row.line, Read(row.cells, "DiaryId", "key"), column, "本文パターンJSONが不正です。", "{\"items\":[{\"ja\":\"...\",\"en\":\"\",\"zh\":\"\"}]} の形式を確認。"); }
        }

        internal static void CheckDictionary(Report report, Table table)
        {
            Require(report, table, "Surface", "Reading", "Enabled");
            var readings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in table.rows.Where(r => ReadingDictionaryEntry.ParseBool(Read(r.cells, "Enabled"))))
            {
                string surface = JapaneseTextNormalizer.NormalizeSurfaceForDictionary(Read(row.cells, "Surface"));
                string reading = JapaneseTextNormalizer.NormalizeReadingForDictionary(Read(row.cells, "Reading"));
                if (string.IsNullOrWhiteSpace(surface) || string.IsNullOrWhiteSpace(reading))
                    Add(report, "Warning", "UnusableReading", table.path, row.line, surface, "Reading", "有効行の表記/読みが空でRuntimeに採用されません。", "値を補うかEnabledを無効化。");
                else if (readings.TryGetValue(surface, out string previous) && previous != reading)
                    Add(report, "Warning", "ReadingConflict", table.path, row.line, surface, "Reading", "正規化後の同じ表記に異なる読みがあります（要確認）。", "Runtimeのマージ順と意図した読みを確認。");
                else readings[surface] = reading;
            }
        }

        private static void CheckInternal(Report report, Table table)
        {
            // InternalDialogueCatalog keys by pattern/id, does not consume regex or timed_event_key.
            Require(report, table, "pattern", "output_ja");
            var patterns = new HashSet<string>(table.rows.Select(r => Read(r.cells, "pattern").Trim()), StringComparer.Ordinal);
            foreach (var row in table.rows)
            {
                string id = Read(row.cells, "id");
                if (string.IsNullOrWhiteSpace(Read(row.cells, "pattern")))
                    Add(report, "Error", "InternalPattern", table.path, row.line, id, "pattern", "内部会話のpatternが空欄です。", "InternalDialogueCatalog用のpatternを指定。");
                string target = Read(row.cells, "target_pattern").Trim();
                if (target.Length > 0 && !patterns.Contains(target))
                    Add(report, "Error", "InternalTarget", table.path, row.line, id, "target_pattern", "内部会話の遷移先patternがありません。", "同原稿のpatternを確認。");
                string unusedEvent = Read(row.cells, "timed_event_key");
                if (!string.IsNullOrWhiteSpace(unusedEvent))
                    Add(report, "Warning", "InternalUnusedTimedEvent", table.path, row.line, id, "timed_event_key", "内部会話Runtimeはこの列を使用しません（要確認）。", "列位置/制作意図を確認。speaker等への自動移動はしません。");
                ValidateCondition(report, table.path, row.line, id, Read(row.cells, "condition"));
            }
        }

        private static void ValidateCondition(Report report, string path, int line, string id, string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return;
            // Like the existing Dialogue validator, inspect each operand without short-circuiting.
            string normalized = global::System.Text.RegularExpressions.Regex.Replace(condition, @"\bAND\b", "&&", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            normalized = global::System.Text.RegularExpressions.Regex.Replace(normalized, @"\bOR\b", "||", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (string clause in global::System.Text.RegularExpressions.Regex.Split(normalized, @"&&|\|\|"))
            {
                if (string.IsNullOrWhiteSpace(clause))
                    Add(report, "Warning", "EmptyConditionOperand", path, line, id, "condition", "空の論理条件です。", "論理演算子の前後を確認。");
                else try { ConversationGameStateConditionEvaluator.ValidateSyntax(clause); }
                catch (FormatException e) { Add(report, "Error", "InvalidCondition", path, line, id, "condition", e.Message, "既存条件文法に修正。"); }
            }
        }

        private static void CheckLinks(Report report, Dictionary<string, Table> tables)
        {
            if (!tables.TryGetValue(DialogueDiaryAuthoring.LinkPath, out var links) || !tables.TryGetValue(DialogueDiaryAuthoring.CandidatePath, out var candidates)) return;
            CheckLinkReferences(report, links, candidates);
            DialogueAuthoringIdentity.Index identity = null;
            try { identity = DialogueAuthoringIdentity.Load(DialogueAuthoringIdentity.MetadataPath); }
            catch (Exception e) { Add(report, "Error", "IdentityUnavailable", DialogueAuthoringIdentity.MetadataPath, 0, "", "", e.GetType().Name + ": ID対応表を読めません。", "Editor ID対応表を確認。"); }
            foreach (var row in links.rows)
            {
                string dialogueId = Read(row.cells, "DialogueId").Trim();
                var records = identity?.records.Where(r => r.id == dialogueId).ToList();
                if (records != null && (records.Count != 1 || !File.Exists(records[0].source)))
                    Add(report, "Error", "MissingDialogueIdentity", links.path, row.line, dialogueId, "DialogueId", "会話IDが未解決・重複、または出典ファイルがありません。", "既存EditorのDialogueIDと出典を確認。");
            }
            try { DialogueDiaryAuthoring.BuildDocument(); }
            catch (Exception e)
            {
                Add(report, "Error", "DiaryPublicationValidation", links.path, 0, "", "DialogueId", e.Message, "既存日記公開Validatorで参照/会話出典を確認（行を特定できない集約診断）。");
            }
        }

        private static void CheckCandidates(Report report, Table table)
        {
            Require(report, table, "DiaryId", "kind", "ja", "en", "zh", "Enabled");
            Unique(report, table, StringComparer.Ordinal, "DiaryId");
            foreach (var row in table.rows)
                if (Read(row.cells, "kind").Trim() != "normal" && Read(row.cells, "kind").Trim() != "special")
                    Add(report, "Error", "DiaryKind", table.path, row.line, Read(row.cells, "DiaryId"), "kind", "独立日記のkindが不正です。", "normal または special を指定。");
        }

        private static void CheckLinkReferences(Report report, Table links, Table candidates)
        {
            Require(report, links, "DialogueId", "DiaryId", "condition", "Enabled");
            Unique(report, links, StringComparer.Ordinal, "DialogueId", "DiaryId");
            var ids = new HashSet<string>(candidates.rows.Select(r => Read(r.cells, "DiaryId").Trim()), StringComparer.Ordinal);
            foreach (var row in links.rows)
            {
                if (!ids.Contains(Read(row.cells, "DiaryId").Trim())) Add(report, "Error", "MissingDiary", links.path, row.line, Read(row.cells, "DialogueId"), "DiaryId", "日記候補への参照がありません。", "DiaryCandidates.csvのDiaryIdを指定。");
                string dialogueId = Read(row.cells, "DialogueId").Trim();
                ValidateCondition(report, links.path, row.line, dialogueId, Read(row.cells, "condition"));
            }
        }

        private static string Read(Dictionary<string, string> row, params string[] names)
        { foreach (string name in names) if (row.TryGetValue(name, out string value)) return value ?? ""; return ""; }
        private static void Require(Report report, Table table, params string[] names)
        {
            foreach (string name in names.Where(n => !table.headers.Contains(n, StringComparer.OrdinalIgnoreCase)))
                Add(report, "Error", "RequiredColumn", table.path, 1, "", name, "必要列がありません。", "現行仕様の列を復元。");
        }
        private static void Unique(Report report, Table table, StringComparer comparer, params string[] names)
        {
            var keys = new HashSet<string>(comparer);
            foreach (var row in table.rows)
            {
                var values = names.Select(n => Read(row.cells, n).Trim()).ToArray();
                if (values.Any(string.IsNullOrWhiteSpace) || !keys.Add(string.Join("\n", values)))
                    Add(report, "Error", "EmptyOrDuplicateKey", table.path, row.line, string.Join("/", values), string.Join("+", names), "空欄または重複したキーです。", "キーを一意にする。会話の複数行idには適用しない。");
            }
        }
        private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        // Pure in-memory fixtures; no TestRunner, save adapters, Scene, or live game state.
        public static int VerifyFixtures()
        {
            int checks = 0;
            void Expect(bool condition, string label) { if (!condition) throw new InvalidDataException("Static fixture failed: " + label); checks++; }
            var report = new Report();
            var normal = InspectCsv(report, "fixture.csv", "key,ja,time_minutes,display_seconds\nvalid,\"a,b\",30,2.5\n");
            CheckTimed(report, normal);
            Expect(report.findings.Count == 0 && normal.rows[0].line == 2, "normal and quoted comma");
            var multiline = InspectCsv(new Report(), "fixture.csv", "a,b\n\"x\ny\",z\nq,r\n");
            Expect(multiline.rows[1].line == 4, "physical multiline position");
            report = new Report(); InspectCsv(report, "fixture.csv", "a,a\nx,y\n");
            Expect(report.findings.Any(f => f.code == "DuplicateColumn" && f.line == 1), "duplicate header");
            report = new Report(); InspectCsv(report, "fixture.csv", "a,b\nx\n");
            Expect(report.findings.Any(f => f.code == "ColumnCount" && f.line == 2), "missing cell");
            report = new Report(); InspectCsv(report, "fixture.csv", "a,b\n\"open,b");
            Expect(report.findings.Any(f => f.code == "CsvParse"), "unclosed quote");
            report = new Report(); InspectCsv(report, "fixture.csv", "");
            Expect(report.findings.Any(f => f.code == "MissingHeader"), "missing header");
            foreach (string value in new[] { "NaN", "Infinity", "-1", "invalid" })
            {
                report = new Report(); CheckTimed(report, InspectCsv(report, "fixture.csv", "key,ja,time_minutes,display_seconds\na,,," + value));
                Expect(report.findings.Any(f => f.code == "TimedValue" && f.column == "display_seconds"), "invalid seconds " + value);
            }
            report = new Report(); CheckTimed(report, InspectCsv(report, "fixture.csv", "key,ja,time_minutes,display_seconds\na,,1.5,0\nA,,,\n"));
            Expect(report.findings.Any(f => f.code == "TimedValue" && f.column == "time_minutes"), "fractional minutes");
            Expect(report.findings.Any(f => f.code == "EmptyOrDuplicateKey" && f.line == 3), "case insensitive event collision");
            report = new Report(); CheckTimed(report, InspectCsv(report, "fixture.csv", "key,ja\na,\n"));
            Expect(report.findings.Count(f => f.code == "RequiredColumn") == 2, "required columns");
            report = new Report(); CheckDictionary(report, InspectCsv(report, "fixture.csv", "Surface,Reading,Enabled\n,,,\ncat,,true\ncat,a,true\ncat,b,true\n"));
            Expect(report.findings.Any(f => f.code == "UnusableReading" && f.line == 3), "enabled empty reading");
            Expect(report.findings.Any(f => f.code == "ReadingConflict" && f.line == 5), "conflicting reading");
            report = new Report(); CheckDictionary(report, InspectCsv(report, "fixture.csv", "Surface,Reading,Enabled\ncat,,false\ncat,a,true\ncat,a,true\n"));
            Expect(report.findings.Count == 0, "disabled and identical readings allowed");
            report = new Report(); CheckInternal(report, InspectCsv(report, "fixture.csv", "id,pattern,output_ja,condition,target_pattern,timed_event_key\na,A,,,,\n"));
            Expect(report.findings.Count == 0, "internal dialogue needs no regex");
            report = new Report(); CheckInternal(report, InspectCsv(report, "fixture.csv", "id,pattern,output_ja,condition,target_pattern,timed_event_key\na,A,,,a,Cat\n"));
            Expect(report.findings.Any(f => f.code == "InternalTarget"), "internal target case sensitivity");
            Expect(report.findings.Any(f => f.code == "InternalUnusedTimedEvent" && f.severity == "Warning"), "unused internal event is review only");
            report = new Report(); ValidateCondition(report, "fixture.csv", 2, "a", "Morning && deliberately_invalid_clause");
            Expect(report.findings.Any(f => f.code == "InvalidCondition" && f.line == 2), "condition validates later operands");
            bool rejected = false;
            try { DiaryTextVariants.Read("", "", "", "{broken"); } catch (InvalidDataException) { rejected = true; }
            Expect(rejected, "invalid variant JSON");
            report = new Report();
            var candidates = InspectCsv(report, "candidates.csv", "DiaryId,kind,ja,en,zh,Enabled\na,normal,,,,true\n");
            CheckCandidates(report, candidates);
            CheckLinkReferences(report, InspectCsv(report, "links.csv", "DialogueId,DiaryId,condition,Enabled\nd,a,,true\n"), candidates);
            Expect(report.findings.Count == 0, "valid diary links with empty localized text");
            report = new Report(); CheckCandidates(report, InspectCsv(report, "candidates.csv", "DiaryId,kind,ja,en,zh,Enabled\na,invalid,,,,true\na,special,,,,false\n"));
            Expect(report.findings.Any(f => f.code == "DiaryKind" && f.line == 2), "invalid diary kind");
            Expect(report.findings.Any(f => f.code == "EmptyOrDuplicateKey" && f.line == 3), "duplicate diary even disabled");
            report = new Report(); CheckLinkReferences(report, InspectCsv(report, "links.csv", "DialogueId,DiaryId,condition,Enabled\nd,missing,,true\nd,missing,,true\n"), candidates);
            Expect(report.findings.Count(f => f.code == "MissingDiary") == 2, "missing diary references");
            Expect(report.findings.Any(f => f.code == "EmptyOrDuplicateKey" && f.line == 3), "duplicate diary links");
            foreach (bool upperCase in new[] { false, true })
            {
                string header = "id,regex_jp,regex_id,pattern,order,response_type,condition,output_ja";
                string csv = (upperCase ? header.ToUpperInvariant() : header) + "\na,x,x,1,1,Normal,deliberately_invalid_clause,\n";
                report = new Report();
                CheckDialogue(report, InspectCsv(report, "fixture.csv", csv), path => DialogueLegacyImporter.ImportText(csv, path));
                Expect(report.findings.Any(f => f.code == "Dialogue.InvalidCondition" && f.line == 2) &&
                    !report.findings.Any(f => f.code == "RequiredColumn"), "dialogue header case " + upperCase);
                report = new Report();
                CheckVariantJson(report, InspectCsv(report, "fixture.csv", (upperCase ? "KEY,DISPLAY_VARIANTS,DIARY_VARIANTS" : "key,display_variants,diary_variants") + "\na,{broken,{broken\n"));
                Expect(report.findings.Count(f => f.code == "VariantJson" && f.line == 2) == 2, "variant header case " + upperCase);
            }
            report = new Report();
            CheckCandidates(report, InspectCsv(report, "candidates.csv", "DiaryId,kind,ja,en,zh,Enabled\na, normal ,,,,true\nb, special ,,,,true\n"));
            Expect(report.findings.Count == 0, "diary kind matches publication trimming");
            return checks;
        }
        private static void Add(Report report, string severity, string code, string file, int line, string id, string column, string reason, string suggestion)
        { report.findings.Add(new Finding { severity = severity, code = code, file = file.Replace('\\', '/'), line = line, id = id, column = column, reason = reason, suggestion = suggestion }); }
    }
}
