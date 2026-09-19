using System;
using System.Collections.Generic;
using System.Globalization;
#if UNITY_EDITOR
using System.IO;
#endif
using System.Text;
using Backgammon.Conversation;
using UnityEngine;

namespace Nekolpos.System
{
    public static class SystemTimedEventCatalog
    {
        public const string UnknownWordTeachEventKey = "unknown_word_teach_event";
        public const string LegacyStartEventIntroKey = "legacy_start_event_intro";
        public const string LegacyStartEventResultKey = "legacy_start_event_result";
        public const string ActionBallPlayResultKey = "action_ball_play_result";
        public const string ActionNapTogetherResultKey = "action_nap_together_result";
        public const string ActionBrushingResultKey = "action_brushing_result";
        public const string ActionStepBackResultKey = "action_step_back_result";
        public const string ActionApologizeResultKey = "action_apologize_result";
        public const string ActionGentlePettingResultKey = "action_gentle_petting_result";
        public const string ActionNapAfterPettingResultKey = "action_nap_after_petting_result";
        private const string ResourcePath = "Dialogue/SystemTimedEvent";
        private const string EncryptedResourcePath = "TalkData/SystemTimedEvent";

        private static readonly Dictionary<string, TimedEventRow> RowsByKey =
            new Dictionary<string, TimedEventRow>(StringComparer.OrdinalIgnoreCase);

        private static bool isLoaded;

        public sealed class ResolvedTimedEvent
        {
            public string Key;
            public string Text;
            public int TimeMinutes;
            public float DisplaySeconds;
        }

        private sealed class TimedEventRow
        {
            public string Key;
            public int TimeMinutes;
            public float DisplaySeconds;
            public readonly Dictionary<string, string> TextByLocale =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static ResolvedTimedEvent Resolve(string key, string locale = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            EnsureLoaded();
            if (!RowsByKey.TryGetValue(key.Trim(), out TimedEventRow row) || row == null)
            {
                return null;
            }

            string resolvedLocale = NormalizeLocale(string.IsNullOrWhiteSpace(locale) ? ResolveCurrentLocale() : locale);
            if (!TryResolveLocalizedValue(row.TextByLocale, resolvedLocale, out string text))
            {
                text = string.Empty;
            }

            return new ResolvedTimedEvent
            {
                Key = row.Key,
                Text = text,
                TimeMinutes = row.TimeMinutes,
                DisplaySeconds = row.DisplaySeconds
            };
        }

        public static string Get(string key, string fallback = "", string locale = null)
        {
            ResolvedTimedEvent resolved = Resolve(key, locale);
            return resolved != null && !string.IsNullOrWhiteSpace(resolved.Text)
                ? resolved.Text
                : fallback ?? string.Empty;
        }

        public static string Format(
            string key,
            IReadOnlyDictionary<string, string> placeholders,
            string fallback = "",
            string locale = null)
        {
            return FormatTemplate(Get(key, fallback, locale), placeholders);
        }

        public static int GetTimeMinutes(string key, int fallback = 0)
        {
            ResolvedTimedEvent resolved = Resolve(key);
            return resolved != null && resolved.TimeMinutes > 0
                ? resolved.TimeMinutes
                : fallback;
        }

        public static float GetDisplaySeconds(string key, float fallback = 0f)
        {
            ResolvedTimedEvent resolved = Resolve(key);
            return resolved != null && resolved.DisplaySeconds > 0f
                ? resolved.DisplaySeconds
                : fallback;
        }

        private static void EnsureLoaded()
        {
            if (isLoaded)
            {
                return;
            }

            isLoaded = true;
            RowsByKey.Clear();

            string csvText = string.Empty;
            if (!ConversationDataManager.TryLoadEncryptedCsvResource(EncryptedResourcePath, out csvText, out string decryptError))
            {
                TextAsset csvAsset = Resources.Load<TextAsset>(ResourcePath);
                csvText = csvAsset != null ? csvAsset.text : string.Empty;
#if UNITY_EDITOR
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    const string editorSourcePath = "TalkSource/TalkCSV/SystemTimedEvent.csv";
                    if (File.Exists(editorSourcePath))
                    {
                        csvText = File.ReadAllText(editorSourcePath, Encoding.UTF8);
                    }
                }
#endif
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    Debug.LogWarning($"[SystemTimedEventCatalog] CSV が見つかりません: Resources/{EncryptedResourcePath}.bytes / Resources/{ResourcePath}.csv ({decryptError})");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(csvText))
            {
                return;
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
            int keyIndex = FindColumnIndex(headers, "key");
            int jaIndex = FindColumnIndex(headers, "ja");
            int enIndex = FindColumnIndex(headers, "en");
            int zhIndex = FindColumnIndex(headers, "zh", "zh_cn", "cn");
            int timeMinutesIndex = FindColumnIndex(headers, "time_minutes", "timeminutes");
            int displaySecondsIndex = FindColumnIndex(headers, "display_seconds", "displayseconds");
            if (keyIndex < 0)
            {
                Debug.LogWarning("[SystemTimedEventCatalog] key 列が見つかりません。");
                return;
            }

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseCsvLine(lines[lineIndex]);
                string key = GetColumn(columns, keyIndex).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                TimedEventRow row = new TimedEventRow
                {
                    Key = key,
                    TimeMinutes = ParseInt(GetColumn(columns, timeMinutesIndex), 0),
                    DisplaySeconds = ParseFloat(GetColumn(columns, displaySecondsIndex), 0f)
                };

                AddLocalizedValue(row.TextByLocale, "ja", GetColumn(columns, jaIndex));
                AddLocalizedValue(row.TextByLocale, "en", GetColumn(columns, enIndex));
                AddLocalizedValue(row.TextByLocale, "zh-Hans", GetColumn(columns, zhIndex));
                RowsByKey[key] = row;
            }
        }

        private static void AddLocalizedValue(Dictionary<string, string> localizedValues, string locale, string value)
        {
            if (localizedValues == null || string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            localizedValues[locale] = value;
        }

        private static bool TryResolveLocalizedValue(
            Dictionary<string, string> localizedValues,
            string locale,
            out string text)
        {
            if (localizedValues == null)
            {
                text = string.Empty;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(locale) &&
                localizedValues.TryGetValue(locale, out text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (localizedValues.TryGetValue("ja", out text) && !string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            foreach (KeyValuePair<string, string> pair in localizedValues)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    text = pair.Value;
                    return true;
                }
            }

            text = string.Empty;
            return false;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static string ResolveCurrentLocale()
        {
            ConversationDataManager manager = UnityEngine.Object.FindFirstObjectByType<ConversationDataManager>();
            if (manager == null)
            {
                return "ja";
            }

            return NormalizeLocale(manager.Locale);
        }

        private static string NormalizeLocale(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            if (normalized.Equals("jp", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "ja";
            }

            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return normalized;
        }

        private static string FormatTemplate(string template, IReadOnlyDictionary<string, string> placeholders)
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

                formatted = formatted.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
            }

            return formatted;
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
    }
}
