using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class DialogueDataLoader
    {
        [Serializable]
        public class DataSet
        {
            public string SourcePath;
            public string SourceLabel;
            public string SavePath;
            public string SaveLabel;
            public bool IsDerivedFromGroupedSource;
            public string CompanionSourcePath;
            public List<string> Headers = new List<string>();
            public List<DialogueEntry> Entries = new List<DialogueEntry>();
        }

        private static readonly string[] RegexHeaderAliases = { "regex", "Regex", "InputRegex", "MatchRegex", "TestRegex", "regex_jp", "RegexJP", "regex_ja", "RegexJA", "JA Regex", "Regular_ExpressionJA", "RegularExpressionJA", "正規表現" };
        private static readonly string[] RegexChineseHeaderAliases = { "regex_cn", "regex_zh", "regex_zh_cn", "RegexCN", "RegexZH", "RegexZhCn", "Regular_ExpressionCN", "Regular_ExpressionZH", "RegularExpressionCN", "RegularExpressionZH" };
        private static readonly string[] RegexEnglishHeaderAliases = { "regex_en", "RegexEN", "regex_english", "RegexEnglish", "Regular_ExpressionEN", "RegularExpressionEN" };
        private static readonly string[] RegexIdHeaderAliases = { "regex_id", "Regex ID", "regex ID", "regexid" };
        private static readonly string[] RegexMeaningKeyHeaderAliases = { "意味キー", "meaning_key", "meaningkey", "meaning", "label" };
        private static readonly string[] PatternHeaderAliases = { "pattern", "Pattern", "Pattern ID" };
        private static readonly string[] OrderHeaderAliases = { "order", "Order" };
        private static readonly string[] TextHeaderAliases = { "text", "text_jp", "text_jp 1", "text_jp1", "text_ja", "output_ja", "TextJA", "Line", "JP", "JA" };
        private static readonly string[] TextChineseHeaderAliases = { "text_cn", "text_zh", "text_zh_cn", "output_zh", "CN", "ZH" };
        private static readonly string[] TextEnglishHeaderAliases = { "text_en", "text_english", "output_en", "EN" };
        private static readonly string[] ResponseTypeHeaderAliases = { "response_type", "ResponseType" };
        private static readonly string[] ActionIdHeaderAliases = { "action_id", "ActionID", "NextAction", "next_action", "反応リンク" };
        private static readonly string[] TimedEventKeyHeaderAliases = { "timed_event_key", "TimedEventKey", "system_timed_event_key", "SystemTimedEventKey" };
        private static readonly string[] ConditionHeaderAliases = { "condition", "Condition", "条件", "tag", "tags" };
        private static readonly string[] EmotionChangeTypeHeaderAliases = { "emotion_change_type", "EmotionChangeType" };
        private static readonly string[] EmotionChangeValueHeaderAliases = { "emotion_change_value", "EmotionChangeValue" };
        private static readonly string[] WaitTimeHeaderAliases = { "WaitTime", "wait_time" };
        private static readonly string[] HideUiHeaderAliases = { "HideUI", "hide_ui" };
        private static readonly string[] AnimationHeaderAliases = { "Animation", "animation" };
        private static readonly string[] PriorityHeaderAliases = { "Priority", "priority" };
        private static readonly string[] InternalIdHeaderAliases = { "ID", "id" };
        private static readonly string[] IntentHeaderAliases = { "intent", "Intent" };
        private static readonly string[] ReactionTypeHeaderAliases = { "reaction_type", "ReactionType" };
        private static readonly string[] RepeatCountHeaderAliases = { "RepeatCount", "RepeatCount " };
        private static readonly string[] RandomRepeatLimitHeaderAliases = { "random_repeat_limit", "RandomRepeatLimit", "random_limit", "RandomLimit" };
        private static readonly string[] SpeechControlHeaderAliases = { "SpeechControl", "speech_control" };
        private static readonly string[] TargetPatternHeaderAliases = { "target_pattern", "TargetPattern", "targetPattern" };
        private static readonly string[] CallOnlyHeaderAliases = { "call_only", "CallOnly", "pattern_access", "PatternAccess" };
        private static readonly string[] ChoiceYesPatternHeaderAliases = { "choice_yes_pattern", "ChoiceYesPattern", "choice_yes", "yes_pattern" };
        private static readonly string[] ChoiceNoPatternHeaderAliases = { "choice_no_pattern", "ChoiceNoPattern", "choice_no", "no_pattern" };
        private static readonly string[] SequenceProgressHoldHeaderAliases = { "sequence_progress_hold", "SequenceProgressHold", "progress_hold", "進行保持" };

        private sealed class GroupedSourceDialogue
        {
            public string GroupId;
            public string Input;
            public readonly List<string> OutputLines = new List<string>();
        }

        private sealed class GroupedSourceData
        {
            public string SourcePath;
            public int OverlapCount;
            public readonly Dictionary<string, GroupedSourceDialogue> ByInput = new Dictionary<string, GroupedSourceDialogue>();
        }

        private sealed class RegexCompanionEntry
        {
            public readonly List<string> JapanesePatterns = new List<string>();
            public readonly List<string> ChinesePatterns = new List<string>();
            public readonly List<string> EnglishPatterns = new List<string>();
        }

        private sealed class RegexCompanionData
        {
            public string SourcePath;
            public readonly Dictionary<string, RegexCompanionEntry> ByLabel = new Dictionary<string, RegexCompanionEntry>();
            public readonly Dictionary<string, RegexCompanionEntry> ByLabelAndIntent = new Dictionary<string, RegexCompanionEntry>();
        }

        public static string GetTalkCsvDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sourceDirectory = Path.Combine(projectRoot, "TalkSource", "TalkCSV");
            if (Directory.Exists(sourceDirectory))
            {
                return sourceDirectory;
            }

            return Path.Combine(Application.dataPath, "Resources", "TalkCSV");
        }

        public static string GetBackupDirectory()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProjectBackups", "DialoguePreview");
        }

        public static List<string> GetResourceCsvPaths()
        {
            List<string> paths = new List<string>();
            string absoluteFolder = GetTalkCsvDirectory();

            if (!Directory.Exists(absoluteFolder))
            {
                return paths;
            }

            string[] files = Directory.GetFiles(absoluteFolder, "*.csv", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string absolutePath in files)
            {
                if (IsBackupPath(absolutePath))
                {
                    continue;
                }

                paths.Add(ToDisplayPath(absolutePath));
            }

            return paths;
        }

        private static string ToDisplayPath(string absolutePath)
        {
            string normalized = (absolutePath ?? string.Empty).Replace("\\", "/");
            string assetsRoot = Application.dataPath.Replace("\\", "/");
            if (normalized.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normalized.Substring(assetsRoot.Length);
            }

            return normalized;
        }

        public static string GetNewestResourceCsvPath()
        {
            string newestPath = null;
            DateTime newestWriteTime = DateTime.MinValue;
            List<string> paths = GetResourceCsvPaths();

            for (int i = 0; i < paths.Count; i++)
            {
                string fullPath = GetFullPath(paths[i]);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                DateTime writeTime = File.GetLastWriteTime(fullPath);
                if (writeTime > newestWriteTime)
                {
                    newestWriteTime = writeTime;
                    newestPath = paths[i];
                }
            }

            return newestPath;
        }

        public static DataSet LoadFromPath(string path)
        {
            string fullPath = GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"CSV file not found: {fullPath}");
            }

            string raw = File.ReadAllText(fullPath, Encoding.UTF8);
            List<string> lines = SplitCsvLines(raw);
            DataSet dataSet = new DataSet
            {
                SourcePath = path.Replace("\\", "/"),
                SourceLabel = Path.GetFileName(path)
            };

            if (lines.Count == 0)
            {
                SeedHeaders(dataSet.Headers);
                return dataSet;
            }

            string[] headers = ParseSimpleWrappedCsvLine(lines[0]);
            if (IsSimpleGroupedDialogueFormat(headers))
            {
                LoadSimpleGroupedDialogue(lines, dataSet, headers);
                EnsureHeaders(dataSet);
                return dataSet;
            }

            dataSet.Headers.AddRange(headers);

            int idxRegex = FindColumnIndex(headers, RegexHeaderAliases);
            int idxRegexChinese = FindColumnIndex(headers, RegexChineseHeaderAliases);
            int idxRegexEnglish = FindColumnIndex(headers, RegexEnglishHeaderAliases);
            int idxRegexId = FindColumnIndex(headers, RegexIdHeaderAliases);
            int idxPattern = FindColumnIndex(headers, PatternHeaderAliases);
            int idxOrder = FindColumnIndex(headers, OrderHeaderAliases);
            int idxText = FindColumnIndex(headers, TextHeaderAliases);
            int idxTextChinese = FindColumnIndex(headers, TextChineseHeaderAliases);
            int idxTextEnglish = FindColumnIndex(headers, TextEnglishHeaderAliases);
            int idxResponseType = FindColumnIndex(headers, ResponseTypeHeaderAliases);
            int idxActionId = FindColumnIndex(headers, ActionIdHeaderAliases);
            int idxTimedEventKey = FindColumnIndex(headers, TimedEventKeyHeaderAliases);
            int idxCondition = FindColumnIndex(headers, ConditionHeaderAliases);
            int idxEmotionChangeType = FindColumnIndex(headers, EmotionChangeTypeHeaderAliases);
            int idxEmotionChangeValue = FindColumnIndex(headers, EmotionChangeValueHeaderAliases);
            int idxWaitTime = FindColumnIndex(headers, WaitTimeHeaderAliases);
            int idxHideUi = FindColumnIndex(headers, HideUiHeaderAliases);
            int idxAnimation = FindColumnIndex(headers, AnimationHeaderAliases);
            int idxPriority = FindColumnIndex(headers, PriorityHeaderAliases);
            int idxInternalId = FindColumnIndex(headers, InternalIdHeaderAliases);
            int idxIntent = FindColumnIndex(headers, IntentHeaderAliases);
            int idxReactionType = FindColumnIndex(headers, ReactionTypeHeaderAliases);
            int idxRepeatCount = FindColumnIndex(headers, RepeatCountHeaderAliases);
            int idxRandomRepeatLimit = FindColumnIndex(headers, RandomRepeatLimitHeaderAliases);
            int idxSpeechControl = FindColumnIndex(headers, SpeechControlHeaderAliases);
            int idxTargetPattern = FindColumnIndex(headers, TargetPatternHeaderAliases);
            int idxCallOnly = FindColumnIndex(headers, CallOnlyHeaderAliases);
            int idxChoiceYesPattern = FindColumnIndex(headers, ChoiceYesPatternHeaderAliases);
            int idxChoiceNoPattern = FindColumnIndex(headers, ChoiceNoPatternHeaderAliases);
            int idxSequenceProgressHold = FindColumnIndex(headers, SequenceProgressHoldHeaderAliases);

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseCsvLine(lines[lineIndex]);
                if (IsEmptyRow(columns))
                {
                    continue;
                }

                DialogueEntry entry = new DialogueEntry
                {
                    SourceLineNumber = lineIndex + 1,
                    InternalId = GetColumn(columns, idxInternalId),
                    RegexPattern = GetColumn(columns, idxRegex),
                    RegexPatternChinese = GetColumn(columns, idxRegexChinese),
                    RegexPatternEnglish = GetColumn(columns, idxRegexEnglish),
                    RegexId = GetColumn(columns, idxRegexId),
                    Pattern = GetColumn(columns, idxPattern),
                    Order = ParseInt(GetColumn(columns, idxOrder)),
                    Text = GetColumn(columns, idxText),
                    TextChinese = GetColumn(columns, idxTextChinese),
                    TextEnglish = GetColumn(columns, idxTextEnglish),
                    ResponseType = GetColumn(columns, idxResponseType),
                    ActionId = GetColumn(columns, idxActionId),
                    TimedEventKey = GetColumn(columns, idxTimedEventKey),
                    Condition = GetColumn(columns, idxCondition),
                    EmotionChangeType = GetColumn(columns, idxEmotionChangeType),
                    EmotionChangeValue = ParseInt(GetColumn(columns, idxEmotionChangeValue)),
                    WaitTime = ParseFloat(GetColumn(columns, idxWaitTime), 0f),
                    HideUI = ParseBool(GetColumn(columns, idxHideUi)),
                    Animation = GetColumn(columns, idxAnimation),
                    Priority = ParsePriority(GetColumn(columns, idxPriority)),
                    Intent = GetColumn(columns, idxIntent),
                    ReactionType = GetColumn(columns, idxReactionType),
                    RepeatCount = ParseInt(GetColumn(columns, idxRepeatCount)),
                    RandomRepeatLimit = ParseOptionalNonNegativeInt(GetColumn(columns, idxRandomRepeatLimit)),
                    SpeechControl = GetColumn(columns, idxSpeechControl),
                    TargetPattern = GetColumn(columns, idxTargetPattern),
                    CallOnly = ParseCallOnly(GetColumn(columns, idxCallOnly)),
                    ChoiceYesPattern = GetColumn(columns, idxChoiceYesPattern),
                    ChoiceNoPattern = GetColumn(columns, idxChoiceNoPattern),
                    SequenceProgressHold = ParseBool(GetColumn(columns, idxSequenceProgressHold))
                };

                if (string.IsNullOrWhiteSpace(entry.ResponseType))
                {
                    entry.ResponseType = "Normal";
                }

                for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    if (IsKnownHeader(headers[columnIndex]))
                    {
                        continue;
                    }

                    entry.SetAdditionalValue(headers[columnIndex], GetColumn(columns, columnIndex));
                }

                dataSet.Entries.Add(entry);
            }

            TryApplyRegexDictionaryCompanionPatterns(dataSet, path);
            EnsureHeaders(dataSet);
            TryExpandLocalizedTranslationRows(dataSet, path);
            return dataSet;
        }

        public static string SaveToPath(DataSet dataSet, string path)
        {
            if (dataSet == null)
            {
                throw new ArgumentNullException(nameof(dataSet));
            }

            string fullPath = GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
            }

            Directory.CreateDirectory(directory);

            string backupPath = string.Empty;
            if (File.Exists(fullPath))
            {
                string baseName = Path.GetFileNameWithoutExtension(fullPath);
                string extension = Path.GetExtension(fullPath);
                string backupDirectory = GetBackupDirectory();
                Directory.CreateDirectory(backupDirectory);
                backupPath = GetUniqueBackupPath(backupDirectory, baseName, extension);
                File.Copy(fullPath, backupPath, true);
            }

            EnsureHeaders(dataSet);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < dataSet.Headers.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(EscapeCsvValue(dataSet.Headers[i]));
            }

            builder.AppendLine();

            for (int rowIndex = 0; rowIndex < dataSet.Entries.Count; rowIndex++)
            {
                DialogueEntry entry = dataSet.Entries[rowIndex];
                for (int headerIndex = 0; headerIndex < dataSet.Headers.Count; headerIndex++)
                {
                    if (headerIndex > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(EscapeCsvValue(entry.GetValue(dataSet.Headers[headerIndex])));
                }

                if (rowIndex < dataSet.Entries.Count - 1)
                {
                    builder.AppendLine();
                }
            }

            File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(true));
            dataSet.SavePath = path.Replace("\\", "/");
            dataSet.SaveLabel = Path.GetFileName(path);
            return backupPath.Replace("\\", "/");
        }

        private static string GetUniqueBackupPath(string directory, string baseName, string extension)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, $"{baseName}.backup_{timestamp}{extension}");
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(directory, $"{baseName}.backup_{timestamp}_{suffix}{extension}");
                suffix++;
            }

            return path;
        }

        private static bool IsBackupPath(string path)
        {
            string normalized = path ?? string.Empty;
            return normalized.IndexOf(".backup_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("/Backup/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("\\Backup\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static DataSet CollapseDerivedGroupedDataSet(DataSet dataSet)
        {
            if (dataSet == null)
            {
                throw new ArgumentNullException(nameof(dataSet));
            }

            if (!dataSet.IsDerivedFromGroupedSource || dataSet.Entries == null || dataSet.Entries.Count == 0)
            {
                return dataSet;
            }

            DataSet collapsed = new DataSet
            {
                SourcePath = dataSet.SourcePath,
                SourceLabel = dataSet.SourceLabel,
                SavePath = dataSet.SavePath,
                SaveLabel = dataSet.SaveLabel,
                IsDerivedFromGroupedSource = false,
                CompanionSourcePath = dataSet.CompanionSourcePath
            };
            collapsed.Headers.AddRange(dataSet.Headers);

            List<DialogueEntry> orderedEntries = new List<DialogueEntry>(dataSet.Entries);
            orderedEntries.Sort((a, b) =>
            {
                int lineCompare = a.SourceLineNumber.CompareTo(b.SourceLineNumber);
                if (lineCompare != 0)
                {
                    return lineCompare;
                }

                int orderCompare = a.Order.CompareTo(b.Order);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                return string.Compare(a.Pattern, b.Pattern, StringComparison.Ordinal);
            });

            int index = 0;
            while (index < orderedEntries.Count)
            {
                DialogueEntry first = orderedEntries[index];
                DialogueEntry collapsedEntry = CloneEntry(first);
                List<string> japaneseLines = new List<string>();
                List<string> chineseLines = new List<string>();
                List<string> englishLines = new List<string>();

                int sourceLineNumber = first.SourceLineNumber;
                while (index < orderedEntries.Count && orderedEntries[index].SourceLineNumber == sourceLineNumber)
                {
                    DialogueEntry current = orderedEntries[index];
                    japaneseLines.Add(current.Text ?? string.Empty);
                    chineseLines.Add(current.TextChinese ?? string.Empty);
                    englishLines.Add(current.TextEnglish ?? string.Empty);
                    index++;
                }

                collapsedEntry.Order = 1;
                collapsedEntry.Text = string.Join("\n", japaneseLines);
                collapsedEntry.TextChinese = string.Join("\n", chineseLines);
                collapsedEntry.TextEnglish = string.Join("\n", englishLines);
                collapsed.Entries.Add(collapsedEntry);
            }

            EnsureHeaders(collapsed);
            return collapsed;
        }

        public static string GetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = path.Substring("Assets/".Length);
                return Path.Combine(Application.dataPath, suffix);
            }

            return Path.GetFullPath(path);
        }

        private static void SeedHeaders(List<string> headers)
        {
            headers.Add("regex");
            headers.Add("regex_cn");
            headers.Add("regex_en");
            headers.Add("regex_id");
            headers.Add("pattern");
            headers.Add("order");
            headers.Add("text");
            headers.Add("text_cn");
            headers.Add("text_en");
            headers.Add("response_type");
            headers.Add("action_id");
            headers.Add("condition");
        }

        private static void LoadSimpleGroupedDialogue(List<string> lines, DataSet dataSet, string[] headers)
        {
            dataSet.Headers.Clear();
            SeedHeaders(dataSet.Headers);

            int idxType = FindColumnIndex(headers, "type");
            int idxGroup = FindColumnIndex(headers, "group", "regex_id", "regex id");
            int idxRegex = FindColumnIndex(headers, RegexHeaderAliases);
            int idxText = FindColumnIndex(headers, TextHeaderAliases);

            Dictionary<string, string> groupToRegex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> groupToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseSimpleWrappedCsvLine(lines[lineIndex]);
                if (IsEmptyRow(columns))
                {
                    continue;
                }

                string rowType = GetColumn(columns, idxType).Trim();
                string group = GetColumn(columns, idxGroup).Trim();
                string value = GetColumn(columns, idxText);
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = GetColumn(columns, idxRegex);
                }

                if (string.IsNullOrWhiteSpace(group))
                {
                    group = $"group_{lineIndex}";
                }

                if (string.Equals(rowType, "input", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        groupToRegex[group] = value;
                    }

                    continue;
                }

                if (!string.Equals(rowType, "output", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int nextOrder = 0;
                if (groupToOrder.TryGetValue(group, out int existingOrder))
                {
                    nextOrder = existingOrder + 1;
                }

                groupToOrder[group] = nextOrder;

                DialogueEntry entry = new DialogueEntry
                {
                    SourceLineNumber = lineIndex + 1,
                    RegexPattern = groupToRegex.TryGetValue(group, out string regexPattern) ? regexPattern : string.Empty,
                    RegexId = group,
                    Pattern = "1",
                    Order = nextOrder,
                    Text = value,
                    ResponseType = "Normal",
                    Priority = 10
                };

                dataSet.Entries.Add(entry);
            }
        }

        private static void EnsureHeaders(DataSet dataSet)
        {
            if (dataSet.Headers == null)
            {
                dataSet.Headers = new List<string>();
            }

            EnsureCanonicalHeader(dataSet.Headers, RegexHeaderAliases, "regex");
            EnsureCanonicalHeader(dataSet.Headers, RegexChineseHeaderAliases, "regex_cn");
            EnsureCanonicalHeader(dataSet.Headers, RegexEnglishHeaderAliases, "regex_en");
            EnsureCanonicalHeader(dataSet.Headers, RegexIdHeaderAliases, "regex_id");
            EnsureCanonicalHeader(dataSet.Headers, PatternHeaderAliases, "pattern");
            EnsureCanonicalHeader(dataSet.Headers, OrderHeaderAliases, "order");
            EnsureCanonicalHeader(dataSet.Headers, TextHeaderAliases, "text");
            EnsureCanonicalHeader(dataSet.Headers, TextChineseHeaderAliases, "text_cn");
            EnsureCanonicalHeader(dataSet.Headers, TextEnglishHeaderAliases, "text_en");
            EnsureCanonicalHeader(dataSet.Headers, ResponseTypeHeaderAliases, "response_type");
            EnsureCanonicalHeader(dataSet.Headers, ActionIdHeaderAliases, "action_id");
            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.TimedEventKey)))
            {
                EnsureCanonicalHeader(dataSet.Headers, TimedEventKeyHeaderAliases, "timed_event_key");
            }
            EnsureCanonicalHeader(dataSet.Headers, ConditionHeaderAliases, "condition");
            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.EmotionChangeType)))
            {
                EnsureCanonicalHeader(dataSet.Headers, EmotionChangeTypeHeaderAliases, "emotion_change_type");
                EnsureCanonicalHeader(dataSet.Headers, EmotionChangeValueHeaderAliases, "emotion_change_value");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.WaitTime > 0f))
            {
                EnsureCanonicalHeader(dataSet.Headers, WaitTimeHeaderAliases, "WaitTime");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.HideUI))
            {
                EnsureCanonicalHeader(dataSet.Headers, HideUiHeaderAliases, "HideUI");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.Animation)))
            {
                EnsureCanonicalHeader(dataSet.Headers, AnimationHeaderAliases, "Animation");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.Priority != 0))
            {
                EnsureCanonicalHeader(dataSet.Headers, PriorityHeaderAliases, "Priority");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.InternalId)))
            {
                EnsureCanonicalHeader(dataSet.Headers, InternalIdHeaderAliases, "id");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.Intent)))
            {
                EnsureCanonicalHeader(dataSet.Headers, IntentHeaderAliases, "intent");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.ReactionType)))
            {
                EnsureCanonicalHeader(dataSet.Headers, ReactionTypeHeaderAliases, "reaction_type");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.RepeatCount != 0))
            {
                EnsureCanonicalHeader(dataSet.Headers, RepeatCountHeaderAliases, "RepeatCount");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.RandomRepeatLimit >= 0))
            {
                EnsureCanonicalHeader(dataSet.Headers, RandomRepeatLimitHeaderAliases, "random_repeat_limit");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.SpeechControl)))
            {
                EnsureCanonicalHeader(dataSet.Headers, SpeechControlHeaderAliases, "SpeechControl");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.TargetPattern)))
            {
                EnsureCanonicalHeader(dataSet.Headers, TargetPatternHeaderAliases, "target_pattern");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.CallOnly))
            {
                EnsureCanonicalHeader(dataSet.Headers, CallOnlyHeaderAliases, "call_only");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.ChoiceYesPattern)))
            {
                EnsureCanonicalHeader(dataSet.Headers, ChoiceYesPatternHeaderAliases, "choice_yes_pattern");
            }

            if (HasAnyValue(dataSet.Entries, entry => !string.IsNullOrWhiteSpace(entry.ChoiceNoPattern)))
            {
                EnsureCanonicalHeader(dataSet.Headers, ChoiceNoPatternHeaderAliases, "choice_no_pattern");
            }

            if (HasAnyValue(dataSet.Entries, entry => entry.SequenceProgressHold))
            {
                EnsureCanonicalHeader(dataSet.Headers, SequenceProgressHoldHeaderAliases, "sequence_progress_hold");
            }

            for (int entryIndex = 0; entryIndex < dataSet.Entries.Count; entryIndex++)
            {
                List<DialogueExtraField> fields = dataSet.Entries[entryIndex].AdditionalFields;
                for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    AddHeaderIfMissing(dataSet.Headers, fields[fieldIndex].Key);
                }
            }
        }

        private static bool HasAnyValue(List<DialogueEntry> entries, Func<DialogueEntry, bool> predicate)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (predicate(entries[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureCanonicalHeader(List<string> headers, string[] aliases, string fallbackHeader)
        {
            if (FindColumnIndex(headers.ToArray(), aliases) >= 0)
            {
                return;
            }

            headers.Add(fallbackHeader);
        }

        private static void AddHeaderIfMissing(List<string> headers, string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return;
            }

            if (FindColumnIndex(headers.ToArray(), header) >= 0)
            {
                return;
            }

            headers.Add(header);
        }

        private static bool IsKnownHeader(string header)
        {
            string normalized = DialogueEntry.NormalizeHeader(header);
            switch (normalized)
            {
                case "id":
                case "regex":
                case "inputregex":
                case "matchregex":
                case "testregex":
                case "regexjp":
                case "regexja":
                case "regexcn":
                case "regexzh":
                case "regexzhcn":
                case "regexen":
                case "regexenglish":
                case "regexid":
                case "pattern":
                case "patternid":
                case "order":
                case "text":
                case "textjp1":
                case "line":
                case "jp":
                case "ja":
                case "textjp":
                case "textja":
                case "outputja":
                case "textcn":
                case "textzh":
                case "textzhcn":
                case "outputzh":
                case "texten":
                case "textenglish":
                case "outputen":
                case "cn":
                case "zh":
                case "en":
                case "responsetype":
                case "actionid":
                case "timedeventkey":
                case "systemtimedeventkey":
                case "condition":
                case "tag":
                case "tags":
                case "条件":
                case "emotionchangetype":
                case "emotionchangevalue":
                case "waittime":
                case "hideui":
                case "animation":
                case "priority":
                case "intent":
                case "reactiontype":
                case "repeatcount":
                case "randomrepeatlimit":
                case "randomlimit":
                case "speechcontrol":
                case "targetpattern":
                case "callonly":
                case "patternaccess":
                case "choiceyespattern":
                case "choiceyes":
                case "yespattern":
                case "choicenopattern":
                case "choiceno":
                case "nopattern":
                case "sequenceprogresshold":
                case "progresshold":
                case "進行保持":
                    return true;
                default:
                    return false;
            }
        }

        private static int FindColumnIndex(string[] headers, params string[] candidates)
        {
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string normalizedCandidate = DialogueEntry.NormalizeHeader(candidates[candidateIndex]);
                for (int headerIndex = 0; headerIndex < headers.Length; headerIndex++)
                {
                    if (DialogueEntry.NormalizeHeader(headers[headerIndex]) == normalizedCandidate)
                    {
                        return headerIndex;
                    }
                }
            }

            return -1;
        }

        private static bool IsSimpleGroupedDialogueFormat(string[] headers)
        {
            int idxType = FindColumnIndex(headers, "type");
            int idxGroup = FindColumnIndex(headers, "group");
            int idxText = FindColumnIndex(headers, TextHeaderAliases);

            if (idxType < 0 || idxGroup < 0 || idxText < 0)
            {
                return false;
            }

            int idxPattern = FindColumnIndex(headers, PatternHeaderAliases);
            int idxOrder = FindColumnIndex(headers, OrderHeaderAliases);
            int idxRegexId = FindColumnIndex(headers, RegexIdHeaderAliases);
            return idxPattern < 0 && idxOrder < 0 && idxRegexId < 0;
        }

        private static string[] ParseSimpleWrappedCsvLine(string line)
        {
            string[] columns = ParseCsvLine(line);
            if (columns.Length != 1)
            {
                return columns;
            }

            string wrapped = columns[0];
            if (string.IsNullOrEmpty(wrapped))
            {
                return columns;
            }

            int firstComma = wrapped.IndexOf(',');
            if (firstComma < 0)
            {
                return columns;
            }

            int secondComma = wrapped.IndexOf(',', firstComma + 1);
            if (secondComma < 0)
            {
                return columns;
            }

            return new[]
            {
                wrapped.Substring(0, firstComma),
                wrapped.Substring(firstComma + 1, secondComma - firstComma - 1),
                wrapped.Substring(secondComma + 1)
            };
        }

        private static string GetColumn(string[] columns, int index)
        {
            if (index < 0 || index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index];
        }

        private static bool IsEmptyRow(string[] columns)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(columns[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        private static int ParseOptionalNonNegativeInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? Math.Max(0, parsed)
                : -1;
        }

        private static int ParsePriority(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 10;
            }

            return ParseInt(value);
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "true" || normalized == "1" || normalized == "yes" || normalized == "on";
        }

        private static bool ParseCallOnly(string value)
        {
            if (ParseBool(value))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            return normalized == "callonly";
        }

        private static void TryApplyRegexDictionaryCompanionPatterns(DataSet dataSet, string currentPath)
        {
            if (dataSet == null || dataSet.Entries == null || dataSet.Entries.Count == 0)
            {
                return;
            }

            string companionPath = FindRegexDictionaryCompanionPath(currentPath);
            if (string.IsNullOrWhiteSpace(companionPath))
            {
                return;
            }

            RegexCompanionData companion = LoadRegexCompanionData(companionPath);
            if (companion == null)
            {
                return;
            }

            bool appliedAny = false;
            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                DialogueEntry entry = dataSet.Entries[i];
                string preservedLookupLabel = NormalizeLookupKey(entry.GetPreservedRegexSource());
                string lookupLabel = string.IsNullOrWhiteSpace(preservedLookupLabel)
                    ? NormalizeLookupKey(entry.RegexPattern)
                    : preservedLookupLabel;
                if (string.IsNullOrWhiteSpace(lookupLabel))
                {
                    continue;
                }

                if (!TryGetCompanionRegexPatterns(companion, lookupLabel, entry.Intent, out string japanese, out string chinese, out string english))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.GetPreservedRegexSource()) &&
                    string.IsNullOrWhiteSpace(entry.GetPreservedRegexChineseSource()) &&
                    string.IsNullOrWhiteSpace(entry.GetPreservedRegexEnglishSource()))
                {
                    entry.PreserveRegexSourceValues(entry.RegexPattern, entry.RegexPatternChinese, entry.RegexPatternEnglish);
                }

                if (ShouldApplyCompanionPattern(entry.RegexPattern, entry.GetPreservedRegexSource(), japanese))
                {
                    entry.RegexPattern = japanese;
                }

                if (ShouldApplyCompanionPattern(entry.RegexPatternChinese, entry.GetPreservedRegexChineseSource(), chinese))
                {
                    entry.RegexPatternChinese = chinese;
                }

                if (ShouldApplyCompanionPattern(entry.RegexPatternEnglish, entry.GetPreservedRegexEnglishSource(), english))
                {
                    entry.RegexPatternEnglish = english;
                }

                appliedAny = true;
            }

            if (appliedAny)
            {
                dataSet.CompanionSourcePath = companion.SourcePath;
            }
        }

        private static void TryExpandLocalizedTranslationRows(DataSet dataSet, string currentPath)
        {
            if (dataSet == null || dataSet.Entries == null || dataSet.Entries.Count == 0)
            {
                return;
            }

            if (FindColumnIndex(dataSet.Headers.ToArray(), "input") < 0)
            {
                return;
            }

            GroupedSourceData groupedSource = FindBestGroupedSource(dataSet, currentPath);
            if (groupedSource == null || groupedSource.ByInput.Count == 0)
            {
                return;
            }

            List<DialogueEntry> expandedEntries = new List<DialogueEntry>();
            bool expandedAny = false;

            for (int entryIndex = 0; entryIndex < dataSet.Entries.Count; entryIndex++)
            {
                DialogueEntry sourceEntry = dataSet.Entries[entryIndex];
                string inputKey = NormalizeLookupKey(sourceEntry.GetAdditionalValue("input"));
                if (string.IsNullOrEmpty(inputKey) || !groupedSource.ByInput.TryGetValue(inputKey, out GroupedSourceDialogue sourceDialogue))
                {
                    EnsureDerivedPatternDefaults(sourceEntry, null);
                    expandedEntries.Add(sourceEntry);
                    continue;
                }

                EnsureDerivedPatternDefaults(sourceEntry, sourceDialogue.GroupId);

                if (sourceDialogue.OutputLines.Count <= 1)
                {
                    sourceEntry.Order = sourceEntry.Order > 0 ? sourceEntry.Order : 1;
                    expandedEntries.Add(sourceEntry);
                    continue;
                }

                expandedAny = true;
                List<string> textSegments = SplitLocalizedTextIntoPreviewLines(sourceEntry.Text, sourceDialogue.OutputLines);
                List<string> chineseSegments = SplitLocalizedTextIntoPreviewLines(sourceEntry.TextChinese, sourceDialogue.OutputLines);
                List<string> englishSegments = SplitLocalizedTextIntoPreviewLines(sourceEntry.TextEnglish, sourceDialogue.OutputLines);

                for (int lineIndex = 0; lineIndex < sourceDialogue.OutputLines.Count; lineIndex++)
                {
                    DialogueEntry clone = CloneEntry(sourceEntry);
                    clone.Order = lineIndex + 1;
                    clone.Text = textSegments[lineIndex];
                    clone.TextChinese = chineseSegments[lineIndex];
                    clone.TextEnglish = englishSegments[lineIndex];
                    expandedEntries.Add(clone);
                }
            }

            if (!expandedAny)
            {
                return;
            }

            dataSet.Entries = expandedEntries;
            dataSet.IsDerivedFromGroupedSource = true;
            if (string.IsNullOrWhiteSpace(dataSet.CompanionSourcePath))
            {
                dataSet.CompanionSourcePath = groupedSource.SourcePath;
            }
            EnsureHeaders(dataSet);
        }

        private static void EnsureDerivedPatternDefaults(DialogueEntry entry, string fallbackRegexId)
        {
            if (entry == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.RegexId) && !string.IsNullOrWhiteSpace(fallbackRegexId))
            {
                entry.RegexId = fallbackRegexId;
            }

            if (string.IsNullOrWhiteSpace(entry.Pattern))
            {
                entry.Pattern = "1";
            }

            if (entry.Order <= 0)
            {
                entry.Order = 1;
            }
        }

        private static DialogueEntry CloneEntry(DialogueEntry source)
        {
            DialogueEntry clone = new DialogueEntry
            {
                SourceLineNumber = source.SourceLineNumber,
                InternalId = source.InternalId,
                RegexPattern = source.RegexPattern,
                RegexPatternChinese = source.RegexPatternChinese,
                RegexPatternEnglish = source.RegexPatternEnglish,
                RegexId = source.RegexId,
                Pattern = source.Pattern,
                Order = source.Order,
                Text = source.Text,
                TextChinese = source.TextChinese,
                TextEnglish = source.TextEnglish,
                ResponseType = source.ResponseType,
                ActionId = source.ActionId,
                TimedEventKey = source.TimedEventKey,
                Condition = source.Condition,
                EmotionChangeType = source.EmotionChangeType,
                EmotionChangeValue = source.EmotionChangeValue,
                WaitTime = source.WaitTime,
                HideUI = source.HideUI,
                Animation = source.Animation,
                Priority = source.Priority,
                Intent = source.Intent,
                ReactionType = source.ReactionType,
                RepeatCount = source.RepeatCount,
                RandomRepeatLimit = source.RandomRepeatLimit,
                SpeechControl = source.SpeechControl,
                TargetPattern = source.TargetPattern,
                CallOnly = source.CallOnly,
                ChoiceYesPattern = source.ChoiceYesPattern,
                ChoiceNoPattern = source.ChoiceNoPattern,
                SequenceProgressHold = source.SequenceProgressHold
            };

            for (int fieldIndex = 0; fieldIndex < source.AdditionalFields.Count; fieldIndex++)
            {
                DialogueExtraField field = source.AdditionalFields[fieldIndex];
                clone.AdditionalFields.Add(new DialogueExtraField(field.Key, field.Value));
            }

            return clone;
        }

        private static string FindRegexDictionaryCompanionPath(string currentPath)
        {
            string fullCurrentPath = GetFullPath(currentPath);
            if (string.IsNullOrWhiteSpace(fullCurrentPath))
            {
                return null;
            }

            string fileName = Path.GetFileName(fullCurrentPath);
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.IndexOf("RegexDict", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            string directory = Path.GetDirectoryName(fullCurrentPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            string bestPath = null;
            DateTime bestWriteTime = DateTime.MinValue;
            string[] candidates = Directory.GetFiles(directory, "*.csv", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidatePath = candidates[i];
                string candidateName = Path.GetFileName(candidatePath);
                if (candidateName.IndexOf(".backup_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidateName.IndexOf("Regular Expression", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                DateTime writeTime = File.GetLastWriteTime(candidatePath);
                if (bestPath == null || writeTime > bestWriteTime)
                {
                    bestPath = candidatePath;
                    bestWriteTime = writeTime;
                }
            }

            return bestPath;
        }

        private static RegexCompanionData LoadRegexCompanionData(string companionPath)
        {
            string raw = File.ReadAllText(companionPath, Encoding.UTF8);
            List<string> lines = SplitCsvLines(raw);
            if (lines.Count == 0)
            {
                return null;
            }

            string[] headers = ParseSimpleWrappedCsvLine(lines[0]);
            int idxMeaningKey = FindColumnIndex(headers, RegexMeaningKeyHeaderAliases);
            int idxIntent = FindColumnIndex(headers, IntentHeaderAliases);
            int idxRegexJapanese = FindColumnIndex(headers, RegexHeaderAliases);
            int idxRegexChinese = FindColumnIndex(headers, RegexChineseHeaderAliases);
            int idxRegexEnglish = FindColumnIndex(headers, RegexEnglishHeaderAliases);

            if (idxMeaningKey < 0 || (idxRegexJapanese < 0 && idxRegexChinese < 0 && idxRegexEnglish < 0))
            {
                return null;
            }

            RegexCompanionData data = new RegexCompanionData
            {
                SourcePath = companionPath.Replace("\\", "/")
            };

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseCsvLine(lines[lineIndex]);
                if (IsEmptyRow(columns))
                {
                    continue;
                }

                string label = NormalizeLookupKey(GetColumn(columns, idxMeaningKey));
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                string intent = NormalizeLookupKey(GetColumn(columns, idxIntent));
                string japanese = GetColumn(columns, idxRegexJapanese);
                string chinese = GetColumn(columns, idxRegexChinese);
                string english = GetColumn(columns, idxRegexEnglish);
                if (string.IsNullOrWhiteSpace(japanese) && string.IsNullOrWhiteSpace(chinese) && string.IsNullOrWhiteSpace(english))
                {
                    continue;
                }

                RegexCompanionEntry labelEntry = GetOrCreateRegexCompanionEntry(data.ByLabel, label);
                AppendUniquePattern(labelEntry.JapanesePatterns, japanese);
                AppendUniquePattern(labelEntry.ChinesePatterns, chinese);
                AppendUniquePattern(labelEntry.EnglishPatterns, english);

                if (!string.IsNullOrWhiteSpace(intent))
                {
                    string compositeKey = BuildRegexCompanionLookupKey(label, intent);
                    RegexCompanionEntry compositeEntry = GetOrCreateRegexCompanionEntry(data.ByLabelAndIntent, compositeKey);
                    AppendUniquePattern(compositeEntry.JapanesePatterns, japanese);
                    AppendUniquePattern(compositeEntry.ChinesePatterns, chinese);
                    AppendUniquePattern(compositeEntry.EnglishPatterns, english);
                }
            }

            return data;
        }

        private static bool TryGetCompanionRegexPatterns(
            RegexCompanionData companion,
            string lookupLabel,
            string intent,
            out string japanese,
            out string chinese,
            out string english)
        {
            japanese = string.Empty;
            chinese = string.Empty;
            english = string.Empty;

            if (companion == null || string.IsNullOrWhiteSpace(lookupLabel))
            {
                return false;
            }

            RegexCompanionEntry entry = null;
            string normalizedIntent = NormalizeLookupKey(intent);
            if (!string.IsNullOrWhiteSpace(normalizedIntent))
            {
                companion.ByLabelAndIntent.TryGetValue(BuildRegexCompanionLookupKey(lookupLabel, normalizedIntent), out entry);
            }

            if (entry == null)
            {
                companion.ByLabel.TryGetValue(lookupLabel, out entry);
            }

            if (entry == null)
            {
                return false;
            }

            japanese = CombineRegexPatterns(entry.JapanesePatterns);
            chinese = CombineRegexPatterns(entry.ChinesePatterns);
            english = CombineRegexPatterns(entry.EnglishPatterns);
            return !string.IsNullOrWhiteSpace(japanese) ||
                   !string.IsNullOrWhiteSpace(chinese) ||
                   !string.IsNullOrWhiteSpace(english);
        }

        private static bool ShouldApplyCompanionPattern(string currentValue, string preservedSourceValue, string companionValue)
        {
            if (string.IsNullOrWhiteSpace(companionValue))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(preservedSourceValue) &&
                   string.Equals(currentValue.Trim(), preservedSourceValue.Trim(), StringComparison.Ordinal);
        }

        private static GroupedSourceData FindBestGroupedSource(DataSet dataSet, string currentPath)
        {
            string fullCurrentPath = GetFullPath(currentPath);
            string directory = Path.GetDirectoryName(fullCurrentPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            HashSet<string> inputKeys = new HashSet<string>();
            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                string inputKey = NormalizeLookupKey(dataSet.Entries[i].GetAdditionalValue("input"));
                if (!string.IsNullOrWhiteSpace(inputKey))
                {
                    inputKeys.Add(inputKey);
                }
            }

            if (inputKeys.Count == 0)
            {
                return null;
            }

            GroupedSourceData best = null;
            string[] candidates = Directory.GetFiles(directory, "*.csv", SearchOption.TopDirectoryOnly);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidatePath = candidates[candidateIndex];
                if (string.Equals(candidatePath, fullCurrentPath, StringComparison.OrdinalIgnoreCase) ||
                    candidatePath.IndexOf(".backup_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                GroupedSourceData candidate = TryLoadGroupedSourceData(candidatePath, inputKeys);
                if (candidate == null || candidate.OverlapCount <= 0)
                {
                    continue;
                }

                if (best == null || candidate.OverlapCount > best.OverlapCount)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static GroupedSourceData TryLoadGroupedSourceData(string candidatePath, HashSet<string> targetInputs)
        {
            string raw = File.ReadAllText(candidatePath, Encoding.UTF8);
            List<string> lines = SplitCsvLines(raw);
            if (lines.Count == 0)
            {
                return null;
            }

            string[] headers = ParseSimpleWrappedCsvLine(lines[0]);
            if (!IsSimpleGroupedDialogueFormat(headers))
            {
                return null;
            }

            int idxType = FindColumnIndex(headers, "type");
            int idxGroup = FindColumnIndex(headers, "group", "regex_id", "regex id");
            int idxText = FindColumnIndex(headers, TextHeaderAliases);
            if (idxType < 0 || idxGroup < 0 || idxText < 0)
            {
                return null;
            }

            Dictionary<string, GroupedSourceDialogue> byGroup = new Dictionary<string, GroupedSourceDialogue>(StringComparer.OrdinalIgnoreCase);

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseSimpleWrappedCsvLine(lines[lineIndex]);
                if (IsEmptyRow(columns))
                {
                    continue;
                }

                string rowType = GetColumn(columns, idxType).Trim();
                string group = GetColumn(columns, idxGroup).Trim();
                string text = GetColumn(columns, idxText);
                if (string.IsNullOrWhiteSpace(group))
                {
                    continue;
                }

                if (!byGroup.TryGetValue(group, out GroupedSourceDialogue dialogue))
                {
                    dialogue = new GroupedSourceDialogue { GroupId = group };
                    byGroup.Add(group, dialogue);
                }

                if (string.Equals(rowType, "input", StringComparison.OrdinalIgnoreCase))
                {
                    dialogue.Input = text;
                }
                else if (string.Equals(rowType, "output", StringComparison.OrdinalIgnoreCase))
                {
                    dialogue.OutputLines.Add(text);
                }
            }

            GroupedSourceData result = new GroupedSourceData
            {
                SourcePath = candidatePath.Replace("\\", "/")
            };

            foreach (KeyValuePair<string, GroupedSourceDialogue> pair in byGroup)
            {
                GroupedSourceDialogue dialogue = pair.Value;
                string inputKey = NormalizeLookupKey(dialogue.Input);
                if (string.IsNullOrWhiteSpace(inputKey) || result.ByInput.ContainsKey(inputKey))
                {
                    continue;
                }

                result.ByInput.Add(inputKey, dialogue);
                if (targetInputs.Contains(inputKey))
                {
                    result.OverlapCount++;
                }
            }

            return result;
        }

        private static RegexCompanionEntry GetOrCreateRegexCompanionEntry(Dictionary<string, RegexCompanionEntry> dictionary, string key)
        {
            if (!dictionary.TryGetValue(key, out RegexCompanionEntry entry))
            {
                entry = new RegexCompanionEntry();
                dictionary.Add(key, entry);
            }

            return entry;
        }

        private static void AppendUniquePattern(List<string> patterns, string pattern)
        {
            string normalized = string.IsNullOrWhiteSpace(pattern) ? string.Empty : pattern.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            for (int i = 0; i < patterns.Count; i++)
            {
                if (string.Equals(patterns[i], normalized, StringComparison.Ordinal))
                {
                    return;
                }
            }

            patterns.Add(normalized);
        }

        private static string CombineRegexPatterns(List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0)
            {
                return string.Empty;
            }

            if (patterns.Count == 1)
            {
                return patterns[0];
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < patterns.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append("(?:");
                builder.Append(patterns[i]);
                builder.Append(')');
            }

            return builder.ToString();
        }

        private static string BuildRegexCompanionLookupKey(string label, string intent)
        {
            return NormalizeLookupKey(label) + "\u001f" + NormalizeLookupKey(intent);
        }

        private static string NormalizeLookupKey(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\_", "_")
                .Trim();
        }

        private static List<string> SplitLocalizedTextIntoPreviewLines(string text, List<string> sourceLines)
        {
            int targetCount = Math.Max(1, sourceLines != null ? sourceLines.Count : 0);
            List<string> result = new List<string>(targetCount);
            if (targetCount == 1)
            {
                result.Add(text ?? string.Empty);
                return result;
            }

            string safeText = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(safeText))
            {
                for (int i = 0; i < targetCount; i++)
                {
                    result.Add(string.Empty);
                }

                return result;
            }

            List<string> units = SplitIntoSentenceUnits(safeText);
            ExpandUnitsToCount(units, targetCount);
            return MergeUnitsIntoTargetCount(units, targetCount, sourceLines);
        }

        private static List<string> SplitIntoSentenceUnits(string text)
        {
            List<string> units = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return units;
            }

            int unitStart = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (!IsStrongSentenceBoundary(text[i]))
                {
                    continue;
                }

                int unitEnd = i + 1;
                while (unitEnd < text.Length && char.IsWhiteSpace(text[unitEnd]))
                {
                    unitEnd++;
                }

                units.Add(text.Substring(unitStart, unitEnd - unitStart));
                unitStart = unitEnd;
                i = unitEnd - 1;
            }

            if (unitStart < text.Length)
            {
                units.Add(text.Substring(unitStart));
            }

            if (units.Count == 0)
            {
                units.Add(text);
            }

            return units;
        }

        private static void ExpandUnitsToCount(List<string> units, int targetCount)
        {
            while (units.Count < targetCount)
            {
                int splitIndex = FindLongestSplittableUnit(units);
                if (splitIndex < 0)
                {
                    break;
                }

                if (!TrySplitUnit(units[splitIndex], out string left, out string right))
                {
                    break;
                }

                units[splitIndex] = left;
                units.Insert(splitIndex + 1, right);
            }

            while (units.Count < targetCount)
            {
                int splitIndex = FindLongestUnit(units);
                if (splitIndex < 0 || units[splitIndex].Length <= 1)
                {
                    break;
                }

                string unit = units[splitIndex];
                int midpoint = FindSafeSplitIndex(unit, unit.Length / 2);
                midpoint = Mathf.Clamp(midpoint, 1, unit.Length - 1);
                units[splitIndex] = unit.Substring(0, midpoint);
                units.Insert(splitIndex + 1, unit.Substring(midpoint));
            }
        }

        private static List<string> MergeUnitsIntoTargetCount(List<string> units, int targetCount, List<string> sourceLines)
        {
            List<string> merged = new List<string>(targetCount);
            if (units.Count == 0)
            {
                for (int i = 0; i < targetCount; i++)
                {
                    merged.Add(string.Empty);
                }

                return merged;
            }

            int[] sourceWeights = new int[targetCount];
            int totalWeight = 0;
            for (int i = 0; i < targetCount; i++)
            {
                int weight = 1;
                if (sourceLines != null && i < sourceLines.Count && !string.IsNullOrEmpty(sourceLines[i]))
                {
                    weight = Mathf.Max(1, sourceLines[i].Trim().Length);
                }

                sourceWeights[i] = weight;
                totalWeight += weight;
            }

            int totalTextLength = 0;
            for (int i = 0; i < units.Count; i++)
            {
                totalTextLength += Mathf.Max(1, units[i].Trim().Length);
            }

            int unitIndex = 0;
            for (int groupIndex = 0; groupIndex < targetCount; groupIndex++)
            {
                int remainingGroups = targetCount - groupIndex;
                int remainingUnits = units.Count - unitIndex;
                int maxTake = Mathf.Max(1, remainingUnits - (remainingGroups - 1));
                int bestTake = 1;
                int desiredLength = Mathf.Max(1, totalTextLength * sourceWeights[groupIndex] / Mathf.Max(1, totalWeight));
                int accumulated = Mathf.Max(1, units[unitIndex].Trim().Length);
                int bestDiff = Math.Abs(accumulated - desiredLength);

                for (int take = 2; take <= maxTake; take++)
                {
                    accumulated += Mathf.Max(1, units[unitIndex + take - 1].Trim().Length);
                    int diff = Math.Abs(accumulated - desiredLength);
                    if (diff <= bestDiff)
                    {
                        bestDiff = diff;
                        bestTake = take;
                    }
                }

                StringBuilder builder = new StringBuilder();
                for (int take = 0; take < bestTake && unitIndex < units.Count; take++, unitIndex++)
                {
                    builder.Append(units[unitIndex]);
                }

                merged.Add(builder.ToString().Trim());
            }

            while (merged.Count < targetCount)
            {
                merged.Add(string.Empty);
            }

            return merged;
        }

        private static bool TrySplitUnit(string unit, out string left, out string right)
        {
            left = unit;
            right = string.Empty;
            if (string.IsNullOrWhiteSpace(unit) || unit.Length <= 1)
            {
                return false;
            }

            List<int> splitCandidates = new List<int>();
            for (int i = 0; i < unit.Length; i++)
            {
                if (!IsWeakSplitBoundary(unit[i]))
                {
                    continue;
                }

                int candidate = i + 1;
                while (candidate < unit.Length && char.IsWhiteSpace(unit[candidate]))
                {
                    candidate++;
                }

                if (candidate > 0 && candidate < unit.Length)
                {
                    splitCandidates.Add(candidate);
                }
            }

            if (splitCandidates.Count == 0)
            {
                return false;
            }

            int midpoint = unit.Length / 2;
            int bestIndex = splitCandidates[0];
            int bestDistance = Math.Abs(bestIndex - midpoint);
            for (int i = 1; i < splitCandidates.Count; i++)
            {
                int distance = Math.Abs(splitCandidates[i] - midpoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = splitCandidates[i];
                }
            }

            bestIndex = FindSafeSplitIndex(unit, bestIndex);
            if (bestIndex <= 0 || bestIndex >= unit.Length)
            {
                return false;
            }

            left = unit.Substring(0, bestIndex);
            right = unit.Substring(bestIndex);
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right);
        }

        private static int FindLongestSplittableUnit(List<string> units)
        {
            int bestIndex = -1;
            int bestLength = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (!CanSplitUnit(units[i]))
                {
                    continue;
                }

                int length = units[i].Length;
                if (length > bestLength)
                {
                    bestLength = length;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int FindLongestUnit(List<string> units)
        {
            int bestIndex = -1;
            int bestLength = 0;
            for (int i = 0; i < units.Count; i++)
            {
                int length = units[i].Length;
                if (length > bestLength)
                {
                    bestLength = length;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool CanSplitUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit) || unit.Length <= 1)
            {
                return false;
            }

            for (int i = 0; i < unit.Length; i++)
            {
                if (IsWeakSplitBoundary(unit[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindSafeSplitIndex(string unit, int preferredIndex)
        {
            int clamped = Mathf.Clamp(preferredIndex, 1, unit.Length - 1);
            if (!IsInsidePlaceholder(unit, clamped))
            {
                return clamped;
            }

            for (int offset = 1; offset < unit.Length; offset++)
            {
                int left = clamped - offset;
                if (left > 0 && !IsInsidePlaceholder(unit, left))
                {
                    return left;
                }

                int right = clamped + offset;
                if (right < unit.Length && !IsInsidePlaceholder(unit, right))
                {
                    return right;
                }
            }

            return clamped;
        }

        private static bool IsInsidePlaceholder(string text, int index)
        {
            bool inside = false;
            for (int i = 0; i < text.Length - 1 && i < index; i++)
            {
                if (!inside && text[i] == '{' && text[i + 1] == '{')
                {
                    inside = true;
                    i++;
                    continue;
                }

                if (inside && text[i] == '}' && text[i + 1] == '}')
                {
                    inside = false;
                    i++;
                }
            }

            return inside;
        }

        private static bool IsStrongSentenceBoundary(char character)
        {
            switch (character)
            {
                case '。':
                case '！':
                case '？':
                case '!':
                case '?':
                case '…':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsWeakSplitBoundary(char character)
        {
            switch (character)
            {
                case '、':
                case '，':
                case ',':
                case ' ':
                case '　':
                case '：':
                case ':':
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> SplitCsvLines(string raw)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(raw))
            {
                return lines;
            }

            bool inQuotes = false;
            int startIndex = 0;

            for (int i = 0; i < raw.Length; i++)
            {
                char current = raw[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < raw.Length && raw[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if ((current == '\n' || current == '\r') && !inQuotes)
                {
                    lines.Add(raw.Substring(startIndex, i - startIndex));

                    if (current == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    startIndex = i + 1;
                }
            }

            if (startIndex <= raw.Length)
            {
                string last = raw.Substring(startIndex);
                if (!string.IsNullOrEmpty(last))
                {
                    lines.Add(last);
                }
            }

            return lines;
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char currentChar = line[i];
                if (currentChar == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (currentChar == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(currentChar);
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private static string EscapeCsvValue(string value)
        {
            string safe = value ?? string.Empty;
            bool needsQuotes = safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuotes)
            {
                return safe;
            }

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
    }
}
