using System;
using System.Collections.Generic;
using System.Globalization;
#if UNITY_EDITOR
using System.IO;
#endif
using System.Text;
using Backgammon.Conversation;
using Nekolpos.Data;
using UnityEngine;

namespace Nekolpos.System
{
    public static class InternalDialogueCatalog
    {
        public const string BranchResolverId = "InternalDialogue";

        private const string ResourcePath = "Dialogue/InternalDialogue";
        private const string EncryptedResourcePath = "TalkData/InternalDialogue";

        private static readonly Dictionary<string, List<DialogueReactionData>> GroupsByPattern =
            new Dictionary<string, List<DialogueReactionData>>(StringComparer.Ordinal);

        private static bool isLoaded;

        public static bool TryCreateReactionGroupByPatternId(
            string patternId,
            IReadOnlyDictionary<string, string> placeholders,
            out List<DialogueReactionData> reactions)
        {
            reactions = null;
            if (string.IsNullOrWhiteSpace(patternId))
            {
                return false;
            }

            EnsureLoaded();
            if (!GroupsByPattern.TryGetValue(patternId.Trim(), out List<DialogueReactionData> group) ||
                group == null ||
                group.Count == 0)
            {
                return false;
            }

            reactions = new List<DialogueReactionData>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                DialogueReactionData clone = CloneReaction(group[i], placeholders);
                if (clone != null)
                {
                    reactions.Add(clone);
                }
            }

            return reactions.Count > 0;
        }

        private static void EnsureLoaded()
        {
            if (isLoaded)
            {
                return;
            }

            isLoaded = true;
            GroupsByPattern.Clear();

            string csvText = string.Empty;
            if (!ConversationDataManager.TryLoadEncryptedCsvResource(EncryptedResourcePath, out csvText, out string decryptError))
            {
                TextAsset csvAsset = Resources.Load<TextAsset>(ResourcePath);
                csvText = csvAsset != null ? csvAsset.text : string.Empty;
#if UNITY_EDITOR
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    const string editorSourcePath = "TalkSource/TalkCSV/InternalDialogue.csv";
                    if (File.Exists(editorSourcePath))
                    {
                        csvText = File.ReadAllText(editorSourcePath, Encoding.UTF8);
                    }
                }
#endif
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    Debug.LogWarning($"[InternalDialogueCatalog] CSV が見つかりません: Resources/{EncryptedResourcePath}.bytes / Resources/{ResourcePath}.csv ({decryptError})");
                    return;
                }
            }

            LoadCsv(csvText);
        }

        private static void LoadCsv(string csvText)
        {
            List<string> lines = SplitCsvLines(csvText);
            if (lines.Count <= 0)
            {
                return;
            }

            string[] headers = ParseCsvLine(lines[0]);
            int idxId = FindColumnIndex(headers, "id");
            int idxTextJa = FindColumnIndex(headers, "output_ja", "text_jp", "ja", "Line", "text");
            int idxTextEn = FindColumnIndex(headers, "output_en", "en");
            int idxTextZh = FindColumnIndex(headers, "output_zh", "zh", "zh_cn", "cn");
            int idxPattern = FindColumnIndex(headers, "pattern", "PatternID", "pattern_id");
            int idxOrder = FindColumnIndex(headers, "order");
            int idxResponseType = FindColumnIndex(headers, "response_type", "ResponseType");
            int idxActionId = FindColumnIndex(headers, "action_id", "ActionId");
            int idxCondition = FindColumnIndex(headers, "condition");
            int idxSpeechControl = FindColumnIndex(headers, "SpeechControl", "speech_control");
            int idxWaitTime = FindColumnIndex(headers, "WaitTime", "wait_time");
            int idxHideUi = FindColumnIndex(headers, "HideUI", "hide_ui");
            int idxAnimation = FindColumnIndex(headers, "Animation", "animation");
            int idxChoiceQuestionJa = FindColumnIndex(headers, "question_ja", "ChoiceQuestionJa", "question");
            int idxChoiceYesJa = FindColumnIndex(headers, "choice_yes_ja", "ChoiceYesLabel", "choice_yes_label");
            int idxChoiceNoJa = FindColumnIndex(headers, "choice_no_ja", "ChoiceNoLabel", "choice_no_label");
            int idxTargetPattern = FindColumnIndex(headers, "target_pattern", "TargetPattern");
            int idxSpeaker = FindColumnIndex(headers, "speaker", "Speaker");

            if (idxPattern < 0 || idxTextJa < 0)
            {
                Debug.LogWarning("[InternalDialogueCatalog] pattern/output_ja 列が見つからないため読み込みをスキップします。");
                return;
            }

            string locale = ResolveCurrentLocale();
            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseCsvLine(lines[lineIndex]);
                string patternId = GetColumn(columns, idxPattern).Trim();
                if (string.IsNullOrWhiteSpace(patternId))
                {
                    continue;
                }

                DialogueReactionData reaction = new DialogueReactionData
                {
                    PatternID = patternId,
                    TextJP = ResolveLocalizedText(columns, locale, idxTextJa, idxTextEn, idxTextZh),
                    Order = ParseInt(GetColumn(columns, idxOrder), 0),
                    ResponseType = GetColumn(columns, idxResponseType),
                    ActionId = GetColumn(columns, idxActionId),
                    Speaker = GetColumn(columns, idxSpeaker),
                    Condition = GetColumn(columns, idxCondition),
                    ChoiceQuestionJa = GetColumn(columns, idxChoiceQuestionJa),
                    ChoiceYesLabel = GetColumn(columns, idxChoiceYesJa),
                    ChoiceNoLabel = GetColumn(columns, idxChoiceNoJa),
                    TargetPatternID = GetColumn(columns, idxTargetPattern),
                    BranchResolver = BranchResolverId,
                    SourceRegexLabel = BranchResolverId,
                    IntentID = "INTERNAL_DIALOGUE",
                    ReactionType = "InternalDialogue"
                };

                string id = GetColumn(columns, idxId);
                reaction.LabelHash = GetDeterministicHash(string.IsNullOrWhiteSpace(id) ? patternId : id);

                string speechControl = GetColumn(columns, idxSpeechControl);
                reaction.SpeechControl = ParseSpeechControl(speechControl);
                if (TryParseFloat(GetColumn(columns, idxWaitTime), out float waitTime))
                {
                    reaction.WaitTime = waitTime;
                }

                reaction.HideUI = ParseBool(GetColumn(columns, idxHideUi));
                reaction.Animation = GetColumn(columns, idxAnimation);

                if (!GroupsByPattern.TryGetValue(patternId, out List<DialogueReactionData> group))
                {
                    group = new List<DialogueReactionData>();
                    GroupsByPattern.Add(patternId, group);
                }

                group.Add(reaction);
            }

            foreach (List<DialogueReactionData> group in GroupsByPattern.Values)
            {
                group.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }

        private static DialogueReactionData CloneReaction(
            DialogueReactionData source,
            IReadOnlyDictionary<string, string> placeholders)
        {
            if (source == null)
            {
                return null;
            }

            Dictionary<string, string> runtimePlaceholders = ClonePlaceholders(placeholders);
            return new DialogueReactionData
            {
                LabelHash = source.LabelHash,
                PatternID = source.PatternID,
                SpeechControl = source.SpeechControl,
                Order = source.Order,
                TargetPatternID = source.TargetPatternID,
                ChoiceQuestionJa = FormatRuntimeText(source.ChoiceQuestionJa, runtimePlaceholders),
                ChoiceYesLabel = FormatRuntimeText(source.ChoiceYesLabel, runtimePlaceholders),
                ChoiceNoLabel = FormatRuntimeText(source.ChoiceNoLabel, runtimePlaceholders),
                IntentID = source.IntentID,
                ReactionType = source.ReactionType,
                SourceRegexLabel = source.SourceRegexLabel,
                ActionId = source.ActionId,
                Speaker = source.Speaker,
                ResponseType = source.ResponseType,
                Condition = source.Condition,
                WaitTime = source.WaitTime,
                HideUI = source.HideUI,
                Animation = source.Animation,
                TextJP = FormatRuntimeText(source.TextJP, runtimePlaceholders),
                BranchResolver = source.BranchResolver,
                RuntimePlaceholders = runtimePlaceholders
            };
        }

        private static string ResolveLocalizedText(string[] columns, string locale, int ja, int en, int zh)
        {
            if (locale == "en")
            {
                string value = GetColumn(columns, en);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            if (locale == "zh-Hans")
            {
                string value = GetColumn(columns, zh);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return GetColumn(columns, ja);
        }

        private static string ResolveCurrentLocale()
        {
            ConversationDataManager manager = UnityEngine.Object.FindFirstObjectByType<ConversationDataManager>();
            if (manager == null)
            {
                return "ja";
            }

            string locale = manager.Locale;
            if (string.IsNullOrWhiteSpace(locale) || locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "ja";
            }

            if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || locale.Equals("cn", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return locale;
        }

        private static SpeechControlType ParseSpeechControl(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sequence":
                    return SpeechControlType.Sequence;
                case "repeatevent":
                    return SpeechControlType.RepeatEvent;
                case "call":
                    return SpeechControlType.Call;
                case "return":
                    return SpeechControlType.Return;
                default:
                    return SpeechControlType.Random;
            }
        }

        private static Dictionary<string, string> ClonePlaceholders(IReadOnlyDictionary<string, string> placeholders)
        {
            Dictionary<string, string> clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (placeholders == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    clone[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            return clone;
        }

        private static string FormatRuntimeText(string template, IReadOnlyDictionary<string, string> placeholders)
        {
            if (string.IsNullOrEmpty(template) || placeholders == null || placeholders.Count == 0)
            {
                return template ?? string.Empty;
            }

            string formatted = template;
            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                formatted = formatted.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
                formatted = formatted.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
            }

            return formatted;
        }

        private static int GetDeterministicHash(string value)
        {
            unchecked
            {
                int hash = 23;
                string source = value ?? string.Empty;
                for (int i = 0; i < source.Length; i++)
                {
                    hash = hash * 31 + source[i];
                }

                return hash;
            }
        }

        private static bool ParseBool(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "true" || normalized == "1" || normalized == "yes" || normalized == "on";
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static int FindColumnIndex(string[] headers, params string[] candidates)
        {
            if (headers == null || candidates == null)
            {
                return -1;
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidate = NormalizeHeader(candidates[candidateIndex]);
                for (int headerIndex = 0; headerIndex < headers.Length; headerIndex++)
                {
                    if (NormalizeHeader(headers[headerIndex]) == candidate)
                    {
                        return headerIndex;
                    }
                }
            }

            return -1;
        }

        private static string NormalizeHeader(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
        }

        private static string GetColumn(string[] columns, int index)
        {
            if (columns == null || index < 0 || index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index] ?? string.Empty;
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

            if (startIndex < raw.Length)
            {
                lines.Add(raw.Substring(startIndex));
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
                char c = line[i];
                if (c == '"')
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

                if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
