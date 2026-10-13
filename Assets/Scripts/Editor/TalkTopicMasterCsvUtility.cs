using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nekolpos.EditorTools
{
    internal static class TalkTopicMasterCsvUtility
    {
        public const string SourcePath = "TalkSource/Dialogue/TalkTopicMaster.csv";
        public const string RuntimePath = "Assets/Resources/Dialogue/TalkTopicMaster.csv";

        public static void Synchronize(string sourcePath, string runtimePath)
        {
            ReadDocument(sourcePath); // Reject malformed source before publishing it.
            byte[] bytes = File.ReadAllBytes(sourcePath);
            AtomicWrite(runtimePath, bytes);
        }

        public static void AtomicWrite(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            path = Path.GetFullPath(path);
            if (File.Exists(path) && RetryFileAccess(() => File.ReadAllBytes(path)).SequenceEqual(bytes)) return;
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("保存先が読み取り専用です: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                RetryFileAccess(() =>
                {
                    // Mono File.Replace can fail while merging/removing the old Windows file.
                    // Rename in the same directory; never delete the original before publishing.
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        if (!MoveFileEx(temporary, path, 0x1 | 0x8))
                        {
                            var error = new Win32Exception(Marshal.GetLastWin32Error());
                            throw new IOException("ファイルを保存できません: " + path + "\n他のアプリで開いている場合は閉じて再試行してください。\nWindows error " + error.NativeErrorCode + ": " + error.Message, error);
                        }
                    }
                    else if (File.Exists(path)) File.Replace(temporary, path, null, true);
                    else File.Move(temporary, path);
                    return true;
                });
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingPath, string newPath, uint flags);

        private static T RetryFileAccess<T>(Func<T> operation)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return operation(); }
                catch (IOException exception) when (attempt < 4 && IsTransientFileAccess(exception))
                {
                    Thread.Sleep(25 << attempt); // Bound UI blocking to 375ms per operation.
                }
            }
        }
        private static bool IsTransientFileAccess(IOException exception)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            int code = exception.InnerException is Win32Exception native ? native.NativeErrorCode : exception.HResult & 0xffff;
            return code == 5 || code == 32 || code == 33;
        }

        public static void SaveAndSynchronize(string sourcePath, string runtimePath, Action saveSource)
        {
            byte[] previous = File.Exists(sourcePath) ? File.ReadAllBytes(sourcePath) : null;
            try { saveSource(); Synchronize(sourcePath, runtimePath); }
            catch
            {
                if (previous != null) AtomicWrite(sourcePath, previous);
                else if (File.Exists(sourcePath)) File.Delete(sourcePath);
                throw;
            }
        }

        [UnityEditor.MenuItem("Nekolpos/TalkTopic正本をRuntimeへ同期")]
        public static void SynchronizeProjectMaster()
        {
            Synchronize(SourcePath, RuntimePath);
            UnityEditor.AssetDatabase.Refresh();
        }
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
            return ReadDocumentText(raw);
        }

        public static CsvDocument ReadDocumentText(string raw)
        {
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

            byte[] body = new UTF8Encoding(false).GetBytes(builder.ToString());
            AtomicWrite(path, body);
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

        public static string SerializeDocument(CsvDocument document)
        {
            var builder = new StringBuilder(); AppendRow(builder, document.Headers); builder.AppendLine();
            foreach (var row in document.Rows)
            {
                var values = new List<string>();
                foreach (string header in document.Headers) values.Add(ReadValue(row, header));
                AppendRow(builder, values); builder.AppendLine();
            }
            return builder.ToString();
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
