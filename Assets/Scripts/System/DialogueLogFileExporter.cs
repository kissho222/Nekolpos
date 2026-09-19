using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace Nekolpos.System
{
    public sealed class DialogueLogFileExporter : IDisposable
    {
        private const string LogsDirectoryName = "Logs";
        private const string ConversationLogsDirectoryName = "ConversationLogs";
        private const int DefaultRetentionMonths = 3;
        private const int MaxTextFieldLength = 512;

        private readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly object syncRoot = new object();
        private readonly string logsDirectoryPath;
        private readonly bool includeSensitiveFields;
        private readonly int retentionMonths;
        private StreamWriter writer;
        private string currentFilePath;
        private bool disposed;

        public DialogueLogFileExporter(
            string logsDirectoryPath = null,
            bool includeSensitiveFields = false,
            int retentionMonths = DefaultRetentionMonths)
        {
            this.logsDirectoryPath = ResolveLogsDirectoryPath(logsDirectoryPath);
            this.includeSensitiveFields = includeSensitiveFields;
            this.retentionMonths = Mathf.Max(1, retentionMonths);
        }

        public void AppendLog(DialogueLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                lock (syncRoot)
                {
                    DialogueLogEntry exportEntry = CreateExportEntry(entry, includeSensitiveFields);
                    EnsureWriter(exportEntry);

                    string json = JsonConvert.SerializeObject(exportEntry, serializerSettings);
                    writer.WriteLine(json);
                    writer.Flush();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueLogFileExporter] Failed to append log: {exception.Message}");
            }
        }

        public void Flush()
        {
            try
            {
                lock (syncRoot)
                {
                    writer?.Flush();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueLogFileExporter] Failed to flush log writer: {exception.Message}");
            }
        }

        public string GetCurrentFilePath()
        {
            lock (syncRoot)
            {
                if (string.IsNullOrWhiteSpace(currentFilePath))
                {
                    currentFilePath = BuildFilePathForTimestamp(DialogueLogEntry.CreateTimestamp());
                }

                return currentFilePath ?? string.Empty;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            lock (syncRoot)
            {
                try
                {
                    writer?.Flush();
                    writer?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DialogueLogFileExporter] Failed to dispose log writer: {exception.Message}");
                }
                finally
                {
                    writer = null;
                }
            }
        }

        public static string ResolveLogsDirectoryPath(string overrideLogsDirectoryPath = null)
        {
            return !string.IsNullOrWhiteSpace(overrideLogsDirectoryPath)
                ? overrideLogsDirectoryPath
                : Path.Combine(
                    Application.persistentDataPath,
                    LogsDirectoryName,
                    ConversationLogsDirectoryName);
        }

        private void EnsureWriter(DialogueLogEntry entry)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DialogueLogFileExporter));
            }

            string targetFilePath = BuildFilePathForTimestamp(entry != null ? entry.Timestamp : null);
            if (!string.Equals(currentFilePath, targetFilePath, StringComparison.Ordinal))
            {
                ReplaceWriter(targetFilePath);
            }

            if (writer != null)
            {
                return;
            }

            string directory = Path.GetDirectoryName(currentFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                PruneExpiredLogFiles(directory);
            }

            writer = new StreamWriter(
                new FileStream(currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
        }

        private void ReplaceWriter(string targetFilePath)
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }

            currentFilePath = targetFilePath;
        }

        private string BuildFilePathForTimestamp(string timestamp)
        {
            DateTime fileDate = ParseTimestampOrFallback(timestamp);
            string fileName = $"conversation_{fileDate:yyyy_MM}.jsonl";
            return Path.Combine(logsDirectoryPath, fileName);
        }

        internal static DialogueLogEntry CreateExportEntry(DialogueLogEntry entry, bool includeSensitiveFields = false)
        {
            DialogueLogEntry exportEntry = entry.Clone();

            exportEntry.logId = string.IsNullOrWhiteSpace(exportEntry.logId)
                ? DialogueLogEntry.CreateLogId()
                : exportEntry.logId.Trim();
            exportEntry.timestamp = string.IsNullOrWhiteSpace(exportEntry.timestamp)
                ? DialogueLogEntry.CreateTimestamp()
                : exportEntry.timestamp.Trim();
            exportEntry.language = NormalizeOptional(exportEntry.language) ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

            exportEntry.speaker = NormalizeRequired(exportEntry.speaker);
            exportEntry.speakerDisplayName = NormalizeOptional(exportEntry.speakerDisplayName);
            exportEntry.source = NormalizeRequired(exportEntry.source);
            exportEntry.text = Truncate(NormalizeRequired(exportEntry.text));
            exportEntry.intent = NormalizeOptional(exportEntry.intent);
            exportEntry.nodeId = NormalizeOptional(exportEntry.nodeId);
            exportEntry.rawInput = includeSensitiveFields ? Truncate(NormalizeOptional(exportEntry.rawInput)) : null;
            exportEntry.normalizedInput = includeSensitiveFields ? Truncate(NormalizeOptional(exportEntry.normalizedInput)) : null;
            exportEntry.matchedRegex = includeSensitiveFields ? Truncate(NormalizeOptional(exportEntry.matchedRegex)) : null;
            exportEntry.selectedIntent = NormalizeOptional(exportEntry.selectedIntent) ?? exportEntry.intent;
            exportEntry.selectedResponseId = NormalizeOptional(exportEntry.selectedResponseId) ?? exportEntry.nodeId;
            exportEntry.reactionResult = Truncate(NormalizeOptional(exportEntry.reactionResult));
            exportEntry.uploadStatus = NormalizeUploadStatus(exportEntry.uploadStatus);

            if (includeSensitiveFields && string.Equals(exportEntry.speaker, DialogueLogManager.SpeakerPlayer, StringComparison.Ordinal))
            {
                exportEntry.rawInput ??= NormalizeOptional(exportEntry.text);
                exportEntry.normalizedInput ??= exportEntry.rawInput;
            }
            else
            {
                exportEntry.reactionResult ??= NormalizeOptional(exportEntry.text);
            }

            exportEntry.intentCandidates = NormalizeList(exportEntry.intentCandidates);
            if (!string.IsNullOrWhiteSpace(exportEntry.selectedIntent) &&
                !exportEntry.intentCandidates.Contains(exportEntry.selectedIntent))
            {
                exportEntry.intentCandidates.Add(exportEntry.selectedIntent);
            }

            exportEntry.unknownWords = NormalizeList(exportEntry.unknownWords);
            exportEntry.gameStateSnapshot = includeSensitiveFields ? CloneSnapshot(exportEntry.gameStateSnapshot) : null;

            return exportEntry;
        }

        private void PruneExpiredLogFiles(string directory)
        {
            if (retentionMonths <= 0 || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            DateTime cutoff = DateTime.UtcNow.AddMonths(-retentionMonths);
            string[] filePaths = Directory.GetFiles(directory, "conversation_*.jsonl", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < filePaths.Length; i++)
            {
                string filePath = filePaths[i];
                try
                {
                    DateTime lastWriteTime = File.GetLastWriteTimeUtc(filePath);
                    if (lastWriteTime < cutoff)
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DialogueLogFileExporter] Failed to prune {Path.GetFileName(filePath)}: {exception.Message}");
                }
            }
        }

        private static string NormalizeUploadStatus(string value)
        {
            if (string.Equals(value, DialogueLogEntry.UploadStatusQueued, StringComparison.Ordinal))
            {
                return DialogueLogEntry.UploadStatusQueued;
            }

            if (string.Equals(value, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal))
            {
                return DialogueLogEntry.UploadStatusUploaded;
            }

            if (string.Equals(value, DialogueLogEntry.UploadStatusFailed, StringComparison.Ordinal))
            {
                return DialogueLogEntry.UploadStatusFailed;
            }

            if (string.Equals(value, DialogueLogEntry.UploadStatusExcluded, StringComparison.Ordinal))
            {
                return DialogueLogEntry.UploadStatusExcluded;
            }

            return DialogueLogEntry.UploadStatusReview;
        }

        private static string NormalizeRequired(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static List<string> NormalizeList(List<string> values)
        {
            List<string> normalized = new List<string>();
            if (values == null)
            {
                return normalized;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = NormalizeOptional(values[i]);
                if (!string.IsNullOrWhiteSpace(value) && !normalized.Contains(value))
                {
                    normalized.Add(Truncate(value));
                }
            }

            return normalized;
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxTextFieldLength)
            {
                return value;
            }

            return value.Substring(0, MaxTextFieldLength);
        }

        private static Dictionary<string, object> CloneSnapshot(Dictionary<string, object> snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            string json = JsonConvert.SerializeObject(snapshot, Formatting.None);
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        }

        private static DateTime ParseTimestampOrFallback(string timestamp)
        {
            if (DateTime.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
            {
                return parsed;
            }

            return DateTime.UtcNow;
        }
    }
}
