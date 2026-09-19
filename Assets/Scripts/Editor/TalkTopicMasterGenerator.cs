using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class TalkTopicMasterGenerator
    {
        private const string TalkCsvDirectory = "TalkSource/TalkCSV";
        private const string OutputPath = "TalkSource/Dialogue/TalkTopicMaster.csv";
        private const string BackupDirectory = "ProjectBackups/TalkTopicMaster";
        private const int MaxBackupsToKeep = 10;

        private static readonly HashSet<string> ExcludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TalkTopicMaster.csv",
            "TalkTopicMasterWarnings.csv",
            "BasicSystemDialogue.csv",
            "CatCharacters.csv",
            "SystemTimedEvent.csv",
            "VulgarLanguage 2fd3350ea12d8001872cf0e28346f230.csv",
            "KnownWords.csv",
            "IgnorePatterns.csv"
        };

        private static readonly string[] ExplicitTitleColumns =
        {
            "TalkTitle",
            "title",
            "topic",
            "Topic",
            "話題",
            "表題",
            "notes",
            "comment",
            "comments"
        };

        private sealed class CandidateTopic
        {
            public string Source;
            public string SourceKey;
            public string TalkTitle;
            public Dictionary<string, string> FirstRow;
        }

        private sealed class GenerationStats
        {
            public int ScannedCsvFiles;
            public int ExistingTopics;
            public int AddedTopics;
            public int UpdatedTopics;
            public int PreservedManualFields;
            public int WarningCount;
            public int ErrorCount;
        }

        public static void Generate()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string inputDirectory = Path.Combine(projectRoot, TalkCsvDirectory);
            string outputPath = Path.Combine(projectRoot, OutputPath);
            string backupDirectory = Path.Combine(projectRoot, BackupDirectory);

            GenerationStats stats = new GenerationStats();
            List<string> warnings = new List<string>();
            List<string> errors = new List<string>();

            if (!Directory.Exists(inputDirectory))
            {
                Debug.LogError($"[TalkTopicMaster] Dialogue CSV directory not found: {inputDirectory}");
                return;
            }

            List<CandidateTopic> candidates = CollectCandidateTopics(inputDirectory, stats, warnings);
            TalkTopicMasterCsvUtility.CsvDocument existingDocument = File.Exists(outputPath)
                ? TalkTopicMasterCsvUtility.ReadDocument(outputPath)
                : new TalkTopicMasterCsvUtility.CsvDocument();

            List<TalkTopicMasterRow> rows = LoadExistingRows(existingDocument);
            stats.ExistingTopics = rows.Count;

            ValidateExistingRows(rows, candidates, warnings, errors);
            MergeCandidates(rows, candidates, stats, warnings);
            ValidateRows(rows, candidates, warnings, errors);

            stats.WarningCount = warnings.Count;
            stats.ErrorCount = errors.Count;

            if (errors.Count > 0)
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    Debug.LogError(errors[i]);
                }

                Debug.LogError($"[TalkTopicMaster] Generation aborted. Errors: {errors.Count}");
                return;
            }

            if (File.Exists(outputPath))
            {
                CreateBackup(outputPath, backupDirectory);
                PruneBackups(backupDirectory);
            }

            TalkTopicMasterCsvUtility.WriteMaster(outputPath, rows);
            AssetDatabase.Refresh();

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning(warnings[i]);
            }

            Debug.Log(
                "[TalkTopicMaster] Generation completed.\n" +
                $"Scanned CSV files: {stats.ScannedCsvFiles}\n" +
                $"Existing topics: {stats.ExistingTopics}\n" +
                $"Added topics: {stats.AddedTopics}\n" +
                $"Updated topics: {stats.UpdatedTopics}\n" +
                $"Preserved manual fields: {stats.PreservedManualFields}\n" +
                $"Warnings: {stats.WarningCount}\n" +
                $"Output: {OutputPath}");
        }

        private static List<CandidateTopic> CollectCandidateTopics(string inputDirectory, GenerationStats stats, List<string> warnings)
        {
            List<CandidateTopic> candidates = new List<CandidateTopic>();
            string[] files = Directory.GetFiles(inputDirectory, "*.csv", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                if (ShouldExclude(path))
                {
                    continue;
                }

                stats.ScannedCsvFiles++;
                try
                {
                    AddCandidatesFromCsv(inputDirectory, path, candidates, warnings);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
                {
                    warnings.Add($"[TalkTopicMaster] Failed to read CSV: {ToUnityPath(path)}\n{exception.Message}");
                }
            }

            return candidates;
        }

        private static void AddCandidatesFromCsv(string inputDirectory, string path, List<CandidateTopic> candidates, List<string> warnings)
        {
            List<TalkTopicMasterCsvUtility.CsvRecord> records = TalkTopicMasterCsvUtility.ReadRecords(path);
            if (records.Count == 0)
            {
                return;
            }

            List<string> headers = records[0].Fields
                .Select((header, index) => TalkTopicMasterCsvUtility.NormalizeHeader(header, index == 0))
                .ToList();

            if (!LooksLikeConversationCsv(headers))
            {
                warnings.Add($"[TalkTopicMaster] Skipped non-conversation CSV: {ToUnityPath(path)}");
                return;
            }

            string source = ToRelativeSource(inputDirectory, path);
            Dictionary<string, CandidateTopic> byKey = new Dictionary<string, CandidateTopic>(StringComparer.Ordinal);

            for (int recordIndex = 1; recordIndex < records.Count; recordIndex++)
            {
                TalkTopicMasterCsvUtility.CsvRecord record = records[recordIndex];
                if (TalkTopicMasterCsvUtility.IsEmptyRecord(record.Fields))
                {
                    continue;
                }

                Dictionary<string, string> row = ToRow(headers, record.Fields);
                string sourceKey = BuildSourceKey(row, recordIndex);
                if (string.IsNullOrWhiteSpace(sourceKey))
                {
                    continue;
                }

                if (byKey.ContainsKey(sourceKey))
                {
                    continue;
                }

                byKey[sourceKey] = new CandidateTopic
                {
                    Source = source,
                    SourceKey = sourceKey,
                    TalkTitle = CreateTalkTitle(row, source),
                    FirstRow = row
                };
            }

            foreach (CandidateTopic topic in byKey.Values.OrderBy(topic => topic.SourceKey, StringComparer.Ordinal))
            {
                candidates.Add(topic);
            }
        }

        private static List<TalkTopicMasterRow> LoadExistingRows(TalkTopicMasterCsvUtility.CsvDocument document)
        {
            List<TalkTopicMasterRow> rows = new List<TalkTopicMasterRow>();
            for (int i = 0; i < document.Rows.Count; i++)
            {
                Dictionary<string, string> row = document.Rows[i];
                rows.Add(new TalkTopicMasterRow
                {
                    TalkID = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.TalkID).Trim(),
                    TalkTitle = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.TalkTitle).Trim(),
                    Source = NormalizeSource(TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Source)),
                    SourceKey = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.SourceKey).Trim(),
                    Genre = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Genre),
                    Idea = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Idea),
                    Import = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Import),
                    Keywords = TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Keywords, "KeyWords"),
                    Enabled = NormalizeEnabled(TalkTopicMasterCsvUtility.ReadValue(row, TalkTopicMasterCsvUtility.Enabled), "TRUE")
                });
            }

            return rows;
        }

        private static void MergeCandidates(List<TalkTopicMasterRow> rows, List<CandidateTopic> candidates, GenerationStats stats, List<string> warnings)
        {
            Dictionary<string, TalkTopicMasterRow> existingBySourceAndKey = new Dictionary<string, TalkTopicMasterRow>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TalkTopicMasterRow> existingBySourceOnly = new Dictionary<string, TalkTopicMasterRow>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Count; i++)
            {
                TalkTopicMasterRow row = rows[i];
                if (!string.IsNullOrWhiteSpace(row.SourceKey))
                {
                    string key = BuildTopicKey(row.Source, row.SourceKey);
                    if (!existingBySourceAndKey.ContainsKey(key))
                    {
                        existingBySourceAndKey.Add(key, row);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(row.Source) && !existingBySourceOnly.ContainsKey(row.Source))
                {
                    existingBySourceOnly.Add(row.Source, row);
                }
            }

            int nextId = FindNextTalkIdNumber(rows);
            for (int i = 0; i < candidates.Count; i++)
            {
                CandidateTopic candidate = candidates[i];
                string topicKey = BuildTopicKey(candidate.Source, candidate.SourceKey);
                TalkTopicMasterRow row = null;

                if (!existingBySourceAndKey.TryGetValue(topicKey, out row)
                    && existingBySourceOnly.TryGetValue(candidate.Source, out TalkTopicMasterRow sourceOnlyRow))
                {
                    row = sourceOnlyRow;
                    row.SourceKey = candidate.SourceKey;
                    existingBySourceAndKey[topicKey] = row;
                    stats.UpdatedTopics++;
                }

                if (row == null)
                {
                    row = new TalkTopicMasterRow
                    {
                        TalkID = FormatTalkId(nextId++),
                        TalkTitle = candidate.TalkTitle,
                        Source = candidate.Source,
                        SourceKey = candidate.SourceKey,
                        Enabled = "TRUE"
                    };
                    rows.Add(row);
                    existingBySourceAndKey[topicKey] = row;
                    stats.AddedTopics++;
                    continue;
                }

                if (!string.Equals(row.Source, candidate.Source, StringComparison.Ordinal))
                {
                    row.Source = candidate.Source;
                    stats.UpdatedTopics++;
                }

                if (string.IsNullOrWhiteSpace(row.TalkTitle))
                {
                    row.TalkTitle = candidate.TalkTitle;
                    stats.UpdatedTopics++;
                }
                else if (ManualFieldsAreEmpty(row) && LooksLikeRegexFragment(row.TalkTitle))
                {
                    row.TalkTitle = candidate.TalkTitle;
                    stats.UpdatedTopics++;
                }
                else if (!string.Equals(row.TalkTitle, candidate.TalkTitle, StringComparison.Ordinal))
                {
                    warnings.Add(
                        "[TalkTopicMaster] TalkTitle differs from generated title; existing value was preserved.\n" +
                        $"TalkID={row.TalkID}\nSource={row.Source}\nSourceKey={row.SourceKey}\nExisting={row.TalkTitle}\nGenerated={candidate.TalkTitle}");
                }

                stats.PreservedManualFields++;
            }
        }

        private static bool ManualFieldsAreEmpty(TalkTopicMasterRow row)
        {
            return row != null &&
                   string.IsNullOrWhiteSpace(row.Genre) &&
                   string.IsNullOrWhiteSpace(row.Idea) &&
                   string.IsNullOrWhiteSpace(row.Import) &&
                   string.IsNullOrWhiteSpace(row.Keywords);
        }

        private static void ValidateExistingRows(List<TalkTopicMasterRow> rows, List<CandidateTopic> candidates, List<string> warnings, List<string> errors)
        {
            HashSet<string> candidateKeys = new HashSet<string>(
                candidates.Select(candidate => BuildTopicKey(candidate.Source, candidate.SourceKey)),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> candidateSources = new HashSet<string>(
                candidates.Select(candidate => candidate.Source),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Count; i++)
            {
                TalkTopicMasterRow row = rows[i];
                if (string.IsNullOrWhiteSpace(row.TalkID))
                {
                    errors.Add($"[TalkTopicMaster] Empty TalkID at master row {i + 2}.");
                }

                if (string.IsNullOrWhiteSpace(row.Source))
                {
                    warnings.Add($"[TalkTopicMaster] Empty Source: TalkID={row.TalkID}");
                }

                if (!string.IsNullOrWhiteSpace(row.Source))
                {
                    bool exists = !string.IsNullOrWhiteSpace(row.SourceKey)
                        ? candidateKeys.Contains(BuildTopicKey(row.Source, row.SourceKey))
                        : candidateSources.Contains(row.Source);
                    if (!exists)
                    {
                        warnings.Add(
                            "[TalkTopicMaster] Source not found:\n" +
                            $"TalkID={row.TalkID}\nSource={row.Source}\nSourceKey={row.SourceKey}");
                    }
                }

                if (!IsValidEnabled(row.Enabled))
                {
                    warnings.Add($"[TalkTopicMaster] Enabled must be TRUE or FALSE. TalkID={row.TalkID}, Enabled={row.Enabled}");
                }

                if (!string.IsNullOrWhiteSpace(row.Genre)
                    && !string.Equals(row.Genre, "雑談", StringComparison.Ordinal)
                    && !string.Equals(row.Genre, "ストーリー", StringComparison.Ordinal))
                {
                    warnings.Add($"[TalkTopicMaster] Unexpected Genre. TalkID={row.TalkID}, Genre={row.Genre}");
                }
            }
        }

        private static void ValidateRows(List<TalkTopicMasterRow> rows, List<CandidateTopic> candidates, List<string> warnings, List<string> errors)
        {
            CheckDuplicate(rows, row => row.TalkID, "TalkID", true, warnings, errors);
            CheckDuplicate(rows, row => BuildTopicKey(row.Source, row.SourceKey), "Source+SourceKey", true, warnings, errors);
            CheckDuplicate(rows, row => row.TalkTitle, "TalkTitle", false, warnings, errors);
            CheckDuplicate(rows, row => row.Import, "Import", false, warnings, errors);
        }

        private static void CheckDuplicate(
            List<TalkTopicMasterRow> rows,
            Func<TalkTopicMasterRow, string> selector,
            string label,
            bool error,
            List<string> warnings,
            List<string> errors)
        {
            Dictionary<string, List<string>> idsByValue = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                TalkTopicMasterRow row = rows[i];
                string value = selector(row);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!idsByValue.TryGetValue(value, out List<string> ids))
                {
                    ids = new List<string>();
                    idsByValue.Add(value, ids);
                }

                ids.Add(string.IsNullOrWhiteSpace(row.TalkID) ? $"row {i + 2}" : row.TalkID);
            }

            foreach (KeyValuePair<string, List<string>> pair in idsByValue)
            {
                if (pair.Value.Count <= 1)
                {
                    continue;
                }

                string message = $"[TalkTopicMaster] Duplicate {label}: {pair.Key} ({string.Join(", ", pair.Value)})";
                if (error)
                {
                    errors.Add(message);
                }
                else
                {
                    warnings.Add(message);
                }
            }
        }

        private static bool LooksLikeConversationCsv(List<string> headers)
        {
            HashSet<string> headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
            if (headerSet.Contains("regex_id") && (headerSet.Contains("input") || headerSet.Contains("regex_jp")))
            {
                return true;
            }

            return headerSet.Contains("id")
                && (headerSet.Contains("input") || headerSet.Contains("response") || headerSet.Contains("responses") || headerSet.Contains("output_ja"));
        }

        private static string BuildSourceKey(Dictionary<string, string> row, int fallbackIndex)
        {
            string id = Read(row, "id", "ID", "key");
            string regexId = DecodePreviewToken(Read(row, "regex_id", "regex ID", "regexid"));
            string intent = Read(row, "intent", "Intent");
            string pattern = Read(row, "pattern", "Pattern");

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(regexId))
            {
                return $"{SanitizeKey(id)}:{SanitizeKey(regexId)}";
            }

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(intent))
            {
                return $"{SanitizeKey(id)}:{SanitizeKey(intent)}";
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                return SanitizeKey(id);
            }

            if (!string.IsNullOrWhiteSpace(regexId))
            {
                return SanitizeKey(regexId);
            }

            if (!string.IsNullOrWhiteSpace(pattern))
            {
                return SanitizeKey(pattern);
            }

            return $"row_{fallbackIndex.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string CreateTalkTitle(Dictionary<string, string> row, string source)
        {
            for (int i = 0; i < ExplicitTitleColumns.Length; i++)
            {
                string value = CleanTitle(Read(row, ExplicitTitleColumns[i]));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            string input = CleanTitle(DecodePreviewToken(Read(row, "input")));
            if (!string.IsNullOrWhiteSpace(input) && !LooksLikeRegexFragment(input))
            {
                return input;
            }

            string regex = CleanTitle(DecodePreviewToken(Read(row, "regex_jp", "regex", "Regex")));
            if (!string.IsNullOrWhiteSpace(regex))
            {
                return regex;
            }

            string intent = CleanTitle(Read(row, "intent", "Intent", "category", "Category", "tag", "tags"));
            if (!string.IsNullOrWhiteSpace(intent))
            {
                return intent;
            }

            string regexId = CleanTitle(DecodePreviewToken(Read(row, "regex_id", "regex ID", "regexid")));
            if (!string.IsNullOrWhiteSpace(regexId))
            {
                return regexId;
            }

            string text = CleanTitle(DecodePreviewToken(Read(row, "output_ja", "text_jp", "text_ja", "ja", "response", "responses")));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return CleanTitle(Path.GetFileNameWithoutExtension(source));
        }

        private static string CleanTitle(string value)
        {
            string title = (value ?? string.Empty)
                .Replace("\\_", "_")
                .Replace("\\|", "|")
                .Replace("\\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            if (title.StartsWith("(") && title.EndsWith(")", StringComparison.Ordinal) && title.IndexOf('|') >= 0)
            {
                title = title.Substring(1, title.Length - 2);
            }

            title = SimplifyRegexTitle(title);

            if (title.IndexOf('|') >= 0)
            {
                title = title.Split('|').FirstOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? title;
            }

            title = title
                .Replace("{{CAT_NAME}}", string.Empty)
                .Replace("{{CAT_PRONOUN}}", string.Empty)
                .Replace("{{PLAYER_CALLING}}", string.Empty)
                .Replace("_", " ")
                .Trim(' ', '　', ',', '、', '.', '。', '!', '！', '?', '？', '"', '\'');

            while (title.StartsWith("RE_", StringComparison.OrdinalIgnoreCase) ||
                   title.StartsWith("SCENARIO_", StringComparison.OrdinalIgnoreCase))
            {
                int separatorIndex = title.IndexOf('_');
                if (separatorIndex < 0 || separatorIndex + 1 >= title.Length)
                {
                    break;
                }

                title = title.Substring(separatorIndex + 1);
            }

            title = title.Replace("についての会話", string.Empty).Trim();

            if (title.Length > 25)
            {
                int boundary = FindTitleBoundary(title, 25);
                title = title.Substring(0, boundary).Trim(' ', '　', ',', '、', '.', '。');
            }

            if (string.Equals(title, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(title, "no", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return title;
        }

        private static bool LooksLikeRegexFragment(string value)
        {
            string text = value ?? string.Empty;
            return text.IndexOfAny(new[] { '(', ')', '|', '[', ']', '^', '$', '*', '+', '?' }) >= 0;
        }

        private static string SimplifyRegexTitle(string value)
        {
            string text = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool inCharacterClass = false;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '[')
                {
                    inCharacterClass = true;
                    continue;
                }

                if (current == ']')
                {
                    inCharacterClass = false;
                    continue;
                }

                if (inCharacterClass)
                {
                    continue;
                }

                if (current == '\\')
                {
                    if (i + 1 < text.Length)
                    {
                        builder.Append(text[i + 1]);
                        i++;
                    }
                    continue;
                }

                if (current == '(' || current == ')' || current == '^' || current == '$' ||
                    current == '*' || current == '+' || current == '?')
                {
                    continue;
                }

                if (current == '|')
                {
                    break;
                }

                builder.Append(current);
            }

            return builder.ToString().Trim();
        }

        private static int FindTitleBoundary(string title, int maxLength)
        {
            int boundary = Math.Min(maxLength, title.Length);
            for (int i = boundary - 1; i >= 8; i--)
            {
                char current = title[i];
                if (current == '、' || current == '。' || current == ' ' || current == '　')
                {
                    return i;
                }
            }

            return boundary;
        }

        private static Dictionary<string, string> ToRow(List<string> headers, List<string> fields)
        {
            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                row[header] = i < fields.Count ? fields[i] ?? string.Empty : string.Empty;
            }

            return row;
        }

        private static string Read(Dictionary<string, string> row, params string[] names)
        {
            return TalkTopicMasterCsvUtility.ReadValue(row, names).Trim();
        }

        private static string DecodePreviewToken(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\_", "_")
                .Replace("\\|", "|")
                .Replace("\\n", "\n");
        }

        private static string BuildTopicKey(string source, string sourceKey)
        {
            return $"{NormalizeSource(source)}\u001f{(sourceKey ?? string.Empty).Trim()}";
        }

        private static string SanitizeKey(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\_", "_")
                .Trim();
        }

        private static string NormalizeSource(string source)
        {
            return (source ?? string.Empty).Replace("\\", "/").Trim();
        }

        private static string ToRelativeSource(string inputDirectory, string path)
        {
            Uri root = new Uri(AppendDirectorySeparator(inputDirectory));
            Uri file = new Uri(path);
            return Uri.UnescapeDataString(root.MakeRelativeUri(file).ToString()).Replace("\\", "/");
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string ToUnityPath(string path)
        {
            return (path ?? string.Empty).Replace("\\", "/");
        }

        private static bool ShouldExclude(string path)
        {
            string fileName = Path.GetFileName(path);
            if (ExcludedFileNames.Contains(fileName))
            {
                return true;
            }

            string normalized = ToUnityPath(path);
            return normalized.IndexOf(".backup_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("_Backup_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("/Backup/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("/Backups/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("/Temp/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("/Test/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindNextTalkIdNumber(List<TalkTopicMasterRow> rows)
        {
            int max = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                string talkId = rows[i].TalkID ?? string.Empty;
                if (!talkId.StartsWith("TALK_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(talkId.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    max = Math.Max(max, value);
                }
            }

            return max + 1;
        }

        private static string FormatTalkId(int value)
        {
            return "TALK_" + Math.Max(1, value).ToString("D4", CultureInfo.InvariantCulture);
        }

        private static string NormalizeEnabled(string value, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase) ? "FALSE" : "TRUE";
        }

        private static bool IsValidEnabled(string value)
        {
            return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateBackup(string outputPath, string backupDirectory)
        {
            Directory.CreateDirectory(backupDirectory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string backupPath = Path.Combine(backupDirectory, $"TalkTopicMaster_Backup_{timestamp}.csv");
            File.Copy(outputPath, backupPath, true);
        }

        private static void PruneBackups(string backupDirectory)
        {
            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            FileInfo[] backups = new DirectoryInfo(backupDirectory)
                .GetFiles("TalkTopicMaster_Backup_*.csv", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            for (int i = MaxBackupsToKeep; i < backups.Length; i++)
            {
                backups[i].Delete();
            }
        }

        /*
         * Future UI integration note:
         * Conversation hints and log keyword clicks should both resolve a TalkID,
         * then call one shared insertion path such as InsertTalkTopicToInput(talkId).
         * That method should read Import from TalkTopicData and pass it to the
         * existing conversation input insertion method.
         */
    }
}
