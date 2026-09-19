using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nekolpos.EditorTools
{
    internal static class TalkTopicMasterCsvUtility
    {
        public const string TalkID = "TalkID";
        public const string TalkTitle = "TalkTitle";
        public const string Source = "Source";
        public const string SourceKey = "SourceKey";
        public const string Genre = "Genre";
        public const string Idea = "Idea";
        public const string Import = "Import";
        public const string Keywords = "Keywords";
        public const string Enabled = "Enabled";

        public static readonly string[] MasterHeaders =
        {
            TalkID,
            TalkTitle,
            Source,
            SourceKey,
            Genre,
            Idea,
            Import,
            Keywords,
            Enabled
        };

        public sealed class CsvDocument
        {
            public readonly List<string> Headers = new List<string>();
            public readonly List<Dictionary<string, string>> Rows = new List<Dictionary<string, string>>();
        }

        public sealed class CsvRecord
        {
            public int LineNumber;
            public List<string> Fields = new List<string>();
        }

        public static CsvDocument ReadDocument(string path)
        {
            string raw = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            List<CsvRecord> records = Parse(raw);
            CsvDocument document = new CsvDocument();
            if (records.Count == 0)
            {
                return document;
            }

            for (int i = 0; i < records[0].Fields.Count; i++)
            {
                string header = NormalizeHeader(records[0].Fields[i], i == 0);
                document.Headers.Add(header);
            }

            for (int recordIndex = 1; recordIndex < records.Count; recordIndex++)
            {
                CsvRecord record = records[recordIndex];
                if (IsEmptyRecord(record.Fields))
                {
                    continue;
                }

                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int headerIndex = 0; headerIndex < document.Headers.Count; headerIndex++)
                {
                    string header = document.Headers[headerIndex];
                    if (string.IsNullOrWhiteSpace(header))
                    {
                        continue;
                    }

                    row[header] = headerIndex < record.Fields.Count ? record.Fields[headerIndex] ?? string.Empty : string.Empty;
                }

                document.Rows.Add(row);
            }

            return document;
        }

        public static List<CsvRecord> ReadRecords(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void WriteMaster(string path, IReadOnlyList<TalkTopicMasterRow> rows)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            StringBuilder builder = new StringBuilder();
            AppendRow(builder, MasterHeaders);
            builder.AppendLine();

            for (int i = 0; i < rows.Count; i++)
            {
                TalkTopicMasterRow row = rows[i];
                AppendRow(builder, new[]
                {
                    row.TalkID,
                    row.TalkTitle,
                    row.Source,
                    row.SourceKey,
                    row.Genre,
                    row.Idea,
                    row.Import,
                    row.Keywords,
                    NormalizeEnabledForSave(row.Enabled)
                });
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        }

        public static string ReadValue(Dictionary<string, string> row, params string[] names)
        {
            if (row == null || names == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (row.TryGetValue(name, out string value))
                {
                    return value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        public static string NormalizeHeader(string header, bool firstColumn)
        {
            string normalized = (header ?? string.Empty).Trim();
            if (firstColumn)
            {
                normalized = normalized.TrimStart('\uFEFF');
            }

            return string.Equals(normalized, "KeyWords", StringComparison.OrdinalIgnoreCase)
                ? Keywords
                : normalized;
        }

        public static bool IsEmptyRecord(List<string> fields)
        {
            if (fields == null)
            {
                return true;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(fields[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static List<CsvRecord> Parse(string csv)
        {
            List<CsvRecord> records = new List<CsvRecord>();
            List<string> currentFields = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;
            int lineNumber = 1;
            int rowLineNumber = 1;
            string safeCsv = csv ?? string.Empty;

            for (int index = 0; index < safeCsv.Length; index++)
            {
                char current = safeCsv[index];
                if (current == '"')
                {
                    if (inQuotes && index + 1 < safeCsv.Length && safeCsv[index + 1] == '"')
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
                    currentFields.Add(currentField.ToString());
                    currentField.Length = 0;
                    continue;
                }

                if (!inQuotes && (current == '\r' || current == '\n'))
                {
                    currentFields.Add(currentField.ToString());
                    currentField.Length = 0;
                    records.Add(new CsvRecord
                    {
                        LineNumber = rowLineNumber,
                        Fields = currentFields
                    });

                    currentFields = new List<string>();
                    if (current == '\r' && index + 1 < safeCsv.Length && safeCsv[index + 1] == '\n')
                    {
                        index++;
                    }

                    lineNumber++;
                    rowLineNumber = lineNumber;
                    continue;
                }

                currentField.Append(current);
                if (current == '\n')
                {
                    lineNumber++;
                }
            }

            if (inQuotes)
            {
                throw new FormatException("CSV contains an unterminated quoted field.");
            }

            currentFields.Add(currentField.ToString());
            if (currentFields.Count > 1 || currentFields[0].Length > 0 || records.Count == 0)
            {
                records.Add(new CsvRecord
                {
                    LineNumber = rowLineNumber,
                    Fields = currentFields
                });
            }

            return records;
        }

        private static void AppendRow(StringBuilder builder, IReadOnlyList<string> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Escape(values[i]));
            }
        }

        private static string Escape(string value)
        {
            string safe = value ?? string.Empty;
            bool needsQuotes = safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuotes)
            {
                return safe;
            }

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static string NormalizeEnabledForSave(string value)
        {
            return string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase) ? "FALSE" : "TRUE";
        }
    }
}
