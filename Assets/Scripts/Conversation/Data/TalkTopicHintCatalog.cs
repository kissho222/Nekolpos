using System;
using System.Collections.Generic;
using System.Text;

namespace Backgammon.Conversation
{
    public static class TalkTopicHintCatalog
    {
        public static List<TalkTopicData> LoadEnabledHintsFromCsvText(string csvText, string sourceName = "TalkTopicMaster.csv")
        {
            var hints = new List<TalkTopicData>();
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return hints;
            }

            List<CsvRecord> records = CsvParser.Parse(csvText);
            if (records.Count <= 1)
            {
                return hints;
            }

            List<string> headers = records[0].Fields;
            for (int i = 1; i < records.Count; i++)
            {
                Dictionary<string, string> row = ToRow(headers, records[i].Fields);
                TalkTopicData hint = ParseRow(row);
                if (hint == null)
                {
                    continue;
                }

                hints.Add(hint);
            }

            return hints;
        }

        private static TalkTopicData ParseRow(Dictionary<string, string> row)
        {
            string idea = Read(row, "Idea");
            string import = Read(row, "Import");
            if (!ReadBool(row, "Enabled") ||
                string.IsNullOrWhiteSpace(idea) ||
                string.IsNullOrWhiteSpace(import))
            {
                return null;
            }

            return new TalkTopicData
            {
                TalkID = Read(row, "TalkID"),
                TalkTitle = Read(row, "TalkTitle"),
                Source = Read(row, "Source"),
                SourceKey = Read(row, "SourceKey"),
                Genre = Read(row, "Genre"),
                Idea = idea,
                Import = import,
                Keywords = Read(row, "Keywords"),
                Enabled = true
            };
        }

        private static Dictionary<string, string> ToRow(List<string> headers, List<string> fields)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int count = Math.Min(headers.Count, fields.Count);
            for (int i = 0; i < count; i++)
            {
                string header = NormalizeHeader(headers[i]);
                if (string.IsNullOrWhiteSpace(header) || row.ContainsKey(header))
                {
                    continue;
                }

                row.Add(header, fields[i] ?? string.Empty);
            }

            return row;
        }

        private static string Read(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out string value)
                ? value?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static bool ReadBool(Dictionary<string, string> row, string key)
        {
            string value = Read(row, key);
            return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeHeader(string header)
        {
            return (header ?? string.Empty).Trim().TrimStart('\uFEFF');
        }

        private sealed class CsvRecord
        {
            public readonly List<string> Fields = new List<string>();
        }

        private static class CsvParser
        {
            public static List<CsvRecord> Parse(string csv)
            {
                var records = new List<CsvRecord>();
                var currentRecord = new CsvRecord();
                var currentField = new StringBuilder();
                bool inQuotes = false;

                for (int index = 0; index < csv.Length; index++)
                {
                    char current = csv[index];
                    if (current == '"')
                    {
                        if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            currentField.Append('"');
                            index++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && current == ',')
                    {
                        currentRecord.Fields.Add(currentField.ToString());
                        currentField.Length = 0;
                        continue;
                    }

                    if (!inQuotes && (current == '\r' || current == '\n'))
                    {
                        currentRecord.Fields.Add(currentField.ToString());
                        currentField.Length = 0;
                        records.Add(currentRecord);
                        currentRecord = new CsvRecord();

                        if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        {
                            index++;
                        }

                        continue;
                    }

                    currentField.Append(current);
                }

                if (inQuotes)
                {
                    throw new FormatException("CSV contains an unterminated quoted field.");
                }

                currentRecord.Fields.Add(currentField.ToString());
                if (currentRecord.Fields.Count > 1 || currentRecord.Fields[0].Length > 0 || records.Count == 0)
                {
                    records.Add(currentRecord);
                }

                return records;
            }
        }
    }
}
