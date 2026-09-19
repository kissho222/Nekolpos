using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class KanjiReadingDictionary
    {
        public const string ResourcePath = "TalkData/KanjiReadingDictionary";
        private const string SourceCsvPath = "TalkSource/TalkCSV/KanjiReadingDictionary.csv";
        private const string EncryptedAssetPath = "Assets/Resources/TalkData/KanjiReadingDictionary.bytes";

        private static KanjiReadingDictionary cachedDefault;
#if UNITY_EDITOR
        private static string cachedDefaultFingerprint;
#endif

        private readonly List<ReadingDictionaryEntry> entries;

        public KanjiReadingDictionary(IEnumerable<ReadingDictionaryEntry> entries)
        {
            this.entries = MergeEntries(entries)
                .Where(entry => entry.IsUsable)
                .OrderByDescending(entry => entry.Surface.Length)
                .ThenBy(entry => entry.Surface, StringComparer.Ordinal)
                .ToList();
        }

        public IReadOnlyList<ReadingDictionaryEntry> Entries => entries;

        public static KanjiReadingDictionary Default
        {
            get
            {
#if UNITY_EDITOR
                string fingerprint = GetDefaultFingerprint();
                if (cachedDefault == null || !string.Equals(cachedDefaultFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    cachedDefault = LoadDefault();
                    cachedDefaultFingerprint = fingerprint;
                }

                return cachedDefault;
#else
                return cachedDefault ??= LoadDefault();
#endif
            }
        }

        public static void ResetDefaultForTests()
        {
            cachedDefault = null;
#if UNITY_EDITOR
            cachedDefaultFingerprint = null;
#endif
        }

        public static KanjiReadingDictionary LoadDefault()
        {
#if UNITY_EDITOR
            if (ShouldLoadEditorSourceCsv())
            {
                return FromCsv(File.ReadAllText(SourceCsvPath, Encoding.UTF8), SourceCsvPath);
            }
#endif

            if (ConversationDataManager.TryLoadEncryptedCsvResource(ResourcePath, out string encryptedCsvText, out string encryptedError))
            {
                return FromCsv(encryptedCsvText, ResourcePath);
            }

#if UNITY_EDITOR
            if (File.Exists(SourceCsvPath))
            {
                return FromCsv(File.ReadAllText(SourceCsvPath, Encoding.UTF8), SourceCsvPath);
            }
#endif

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null)
            {
                return FromCsv(asset.text, ResourcePath);
            }

            Debug.LogWarning($"[KanjiReadingDictionary] {ResourcePath}.bytes was not found or could not be decrypted ({encryptedError}). Kanji dictionary conversion is disabled.");
            return new KanjiReadingDictionary(Array.Empty<ReadingDictionaryEntry>());
        }

#if UNITY_EDITOR
        private static bool ShouldLoadEditorSourceCsv()
        {
            if (!File.Exists(SourceCsvPath))
            {
                return false;
            }

            if (!File.Exists(EncryptedAssetPath))
            {
                return true;
            }

            return File.GetLastWriteTimeUtc(SourceCsvPath) > File.GetLastWriteTimeUtc(EncryptedAssetPath);
        }

        private static string GetDefaultFingerprint()
        {
            DateTime sourceTime = File.Exists(SourceCsvPath) ? File.GetLastWriteTimeUtc(SourceCsvPath) : DateTime.MinValue;
            DateTime encryptedTime = File.Exists(EncryptedAssetPath) ? File.GetLastWriteTimeUtc(EncryptedAssetPath) : DateTime.MinValue;
            return sourceTime.Ticks.ToString("D") + "|" + encryptedTime.Ticks.ToString("D");
        }
#endif

        public static KanjiReadingDictionary FromCsv(string csvText, string sourceName = "KanjiReadingDictionary.csv")
        {
            List<ReadingDictionaryEntry> parsedEntries = new List<ReadingDictionaryEntry>();
            try
            {
                List<CsvRecord> records = CsvParser.Parse(csvText ?? string.Empty);
                if (records.Count <= 1)
                {
                    return new KanjiReadingDictionary(parsedEntries);
                }

                Dictionary<string, int> headers = BuildHeaderMap(records[0].Fields);
                for (int i = 1; i < records.Count; i++)
                {
                    CsvRecord record = records[i];
                    if (record == null || IsEmpty(record.Fields))
                    {
                        continue;
                    }

                    try
                    {
                        ReadingDictionaryEntry entry = ParseEntry(record.Fields, headers);
                        if (!string.IsNullOrWhiteSpace(entry.Surface) && !string.IsNullOrWhiteSpace(entry.Reading))
                        {
                            parsedEntries.Add(entry);
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[KanjiReadingDictionary] Skipped invalid row {sourceName}:{record.LineNumber}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[KanjiReadingDictionary] Failed to load {sourceName}: {exception.Message}");
                return new KanjiReadingDictionary(Array.Empty<ReadingDictionaryEntry>());
            }

            return new KanjiReadingDictionary(parsedEntries);
        }

        private static List<ReadingDictionaryEntry> MergeEntries(IEnumerable<ReadingDictionaryEntry> source)
        {
            Dictionary<string, ReadingDictionaryEntry> merged = new Dictionary<string, ReadingDictionaryEntry>(StringComparer.Ordinal);
            if (source == null)
            {
                return new List<ReadingDictionaryEntry>();
            }

            foreach (ReadingDictionaryEntry rawEntry in source)
            {
                if (rawEntry == null || string.IsNullOrWhiteSpace(rawEntry.Surface))
                {
                    continue;
                }

                ReadingDictionaryEntry entry = rawEntry.Clone();
                entry.Surface = JapaneseTextNormalizer.NormalizeSurfaceForDictionary(entry.Surface);
                entry.Reading = JapaneseTextNormalizer.NormalizeReadingForDictionary(entry.Reading);
                if (string.IsNullOrWhiteSpace(entry.Surface) || string.IsNullOrWhiteSpace(entry.Reading))
                {
                    continue;
                }

                if (!merged.TryGetValue(entry.Surface, out ReadingDictionaryEntry existing))
                {
                    merged.Add(entry.Surface, entry);
                    continue;
                }

                if (string.Equals(existing.Reading, entry.Reading, StringComparison.Ordinal))
                {
                    existing.Enabled = existing.Enabled || entry.Enabled;
                    existing.NeedsReview = existing.NeedsReview && entry.NeedsReview;
                    existing.SourceFiles = MergePipeList(existing.SourceFiles, entry.SourceFiles);
                    existing.Note = MergePipeList(existing.Note, entry.Note);
                    continue;
                }

                existing.Enabled = false;
                existing.NeedsReview = true;
                existing.Note = MergePipeList(existing.Note, $"Reading conflict: {existing.Reading}|{entry.Reading}");
            }

            return merged.Values.ToList();
        }

        private static ReadingDictionaryEntry ParseEntry(List<string> fields, Dictionary<string, int> headers)
        {
            return new ReadingDictionaryEntry
            {
                Surface = Read(fields, headers, "Surface"),
                Reading = Read(fields, headers, "Reading"),
                Enabled = ReadingDictionaryEntry.ParseBool(Read(fields, headers, "Enabled")),
                NeedsReview = ReadingDictionaryEntry.ParseBool(Read(fields, headers, "NeedsReview")),
                Category = Read(fields, headers, "Category"),
                SourceFiles = Read(fields, headers, "SourceFiles"),
                Note = Read(fields, headers, "Note")
            };
        }

        private static Dictionary<string, int> BuildHeaderMap(List<string> headers)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (headers == null)
            {
                return result;
            }

            for (int i = 0; i < headers.Count; i++)
            {
                string header = (headers[i] ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (!string.IsNullOrWhiteSpace(header) && !result.ContainsKey(header))
                {
                    result.Add(header, i);
                }
            }

            return result;
        }

        private static string Read(List<string> fields, Dictionary<string, int> headers, string key)
        {
            if (fields == null || headers == null || !headers.TryGetValue(key, out int index) || index < 0 || index >= fields.Count)
            {
                return string.Empty;
            }

            return fields[index]?.Trim() ?? string.Empty;
        }

        private static bool IsEmpty(List<string> fields)
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

        private static string MergePipeList(string left, string right)
        {
            SortedSet<string> values = new SortedSet<string>(StringComparer.Ordinal);
            AddPipeValues(values, left);
            AddPipeValues(values, right);
            return string.Join("|", values);
        }

        private static void AddPipeValues(SortedSet<string> values, string raw)
        {
            if (values == null || string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        private sealed class CsvRecord
        {
            public int LineNumber;
            public List<string> Fields = new List<string>();
        }

        private static class CsvParser
        {
            public static List<CsvRecord> Parse(string csv)
            {
                List<CsvRecord> records = new List<CsvRecord>();
                List<string> fields = new List<string>();
                System.Text.StringBuilder field = new System.Text.StringBuilder();
                bool inQuotes = false;
                int lineNumber = 1;
                int rowLineNumber = 1;

                for (int index = 0; index < csv.Length; index++)
                {
                    char current = csv[index];
                    if (current == '"')
                    {
                        if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && current == ',')
                    {
                        fields.Add(field.ToString());
                        field.Length = 0;
                        continue;
                    }

                    if (!inQuotes && (current == '\r' || current == '\n'))
                    {
                        fields.Add(field.ToString());
                        field.Length = 0;
                        records.Add(new CsvRecord { LineNumber = rowLineNumber, Fields = fields });
                        fields = new List<string>();
                        if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        {
                            index++;
                        }

                        lineNumber++;
                        rowLineNumber = lineNumber;
                        continue;
                    }

                    field.Append(current);
                    if (current == '\n')
                    {
                        lineNumber++;
                    }
                }

                if (inQuotes)
                {
                    throw new FormatException("CSV contains an unterminated quoted field.");
                }

                fields.Add(field.ToString());
                if (fields.Count > 1 || fields[0].Length > 0 || records.Count == 0)
                {
                    records.Add(new CsvRecord { LineNumber = rowLineNumber, Fields = fields });
                }

                return records;
            }
        }
    }
}
