using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Nekolpos.System
{
    public sealed class DialogueLogUploadService
    {
        private const string UploadAuthHeaderName = "X-Nekolpos-Upload-Key";

        private readonly string logsDirectoryPath;
        private readonly DialogueLogUploadHistory uploadHistory;
        private string uploadAuthKey;

        public DialogueLogUploadService(string logsDirectoryPath = null, DialogueLogUploadHistory uploadHistory = null, string uploadAuthKey = "")
        {
            this.logsDirectoryPath = DialogueLogFileExporter.ResolveLogsDirectoryPath(logsDirectoryPath);
            this.uploadHistory = uploadHistory ?? new DialogueLogUploadHistory(this.logsDirectoryPath);
            this.uploadAuthKey = uploadAuthKey ?? string.Empty;
        }

        public string LogsDirectoryPath => logsDirectoryPath;
        public string UploadHistoryFilePath => uploadHistory.FilePath;
        public bool HasUploadAuthKey => !string.IsNullOrWhiteSpace(uploadAuthKey);
        public string UploadAuthKey
        {
            get => uploadAuthKey;
            set => uploadAuthKey = value ?? string.Empty;
        }

        public bool ApplyUploadAuthHeader(UnityWebRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(uploadAuthKey))
            {
                return false;
            }

            request.SetRequestHeader(UploadAuthHeaderName, uploadAuthKey.Trim());
            return true;
        }

        public List<DialogueLogEntry> LoadConversationLogs(int maxCount = 0)
        {
            return LoadLogs(
                maxCount,
                includeUploaded: true,
                onlyUploadable: false,
                keepMostRecent: maxCount > 0);
        }

        public List<DialogueLogEntry> LoadPendingLogs(int maxCount = 0)
        {
            return LoadLogs(
                maxCount,
                includeUploaded: false,
                onlyUploadable: true,
                keepMostRecent: false);
        }

        public void MarkQueued(IEnumerable<string> logIds)
        {
            uploadHistory.MarkQueued(logIds);
        }

        public void MarkExcluded(IEnumerable<string> logIds)
        {
            uploadHistory.MarkExcluded(logIds);
        }

        public int DeleteLogsNotSelectedForUpload(IEnumerable<string> selectedLogIds)
        {
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            if (selectedLogIds != null)
            {
                foreach (string logId in selectedLogIds)
                {
                    if (!string.IsNullOrWhiteSpace(logId))
                    {
                        selectedIds.Add(logId);
                    }
                }
            }

            if (!Directory.Exists(logsDirectoryPath))
            {
                return 0;
            }

            int deletedCount = 0;
            string[] filePaths = Directory.GetFiles(logsDirectoryPath, "conversation_*.jsonl", SearchOption.TopDirectoryOnly);
            Array.Sort(filePaths, StringComparer.Ordinal);

            for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++)
            {
                string filePath = filePaths[fileIndex];
                List<string> retainedLines = new List<string>();
                bool changed = false;
                int lineNumber = 0;

                try
                {
                    foreach (string line in ReadLinesShared(filePath))
                    {
                        lineNumber++;
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        DialogueLogEntry entry = JsonConvert.DeserializeObject<DialogueLogEntry>(line);
                        if (entry == null)
                        {
                            retainedLines.Add(line);
                            continue;
                        }

                        EnsureLogId(entry, filePath, lineNumber, line);
                        string status = uploadHistory.GetStatus(entry.LogId);
                        bool keep = string.Equals(status, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal) ||
                                    selectedIds.Contains(entry.LogId);
                        if (keep)
                        {
                            entry.UploadStatus = status;
                            retainedLines.Add(JsonConvert.SerializeObject(entry));
                            continue;
                        }

                        changed = true;
                        deletedCount++;
                    }

                    if (!changed)
                    {
                        continue;
                    }

                    if (retainedLines.Count <= 0)
                    {
                        File.Delete(filePath);
                    }
                    else
                    {
                        File.WriteAllLines(filePath, retainedLines, new UTF8Encoding(false));
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DialogueLogUploadService] Failed to delete unselected logs from {Path.GetFileName(filePath)}: {exception.Message}");
                }
            }

            return deletedCount;
        }

        private List<DialogueLogEntry> LoadLogs(int maxCount, bool includeUploaded, bool onlyUploadable, bool keepMostRecent)
        {
            List<DialogueLogEntry> pendingLogs = new List<DialogueLogEntry>();
            Queue<DialogueLogEntry> recentLogs = keepMostRecent && maxCount > 0
                ? new Queue<DialogueLogEntry>(maxCount)
                : null;
            HashSet<string> seenLogIds = new HashSet<string>(StringComparer.Ordinal);

            if (!Directory.Exists(logsDirectoryPath))
            {
                return pendingLogs;
            }

            string[] filePaths = Directory.GetFiles(logsDirectoryPath, "conversation_*.jsonl", SearchOption.TopDirectoryOnly);
            Array.Sort(filePaths, StringComparer.Ordinal);

            for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++)
            {
                string filePath = filePaths[fileIndex];
                int lineNumber = 0;

                try
                {
                    foreach (string line in ReadLinesShared(filePath))
                    {
                        lineNumber++;
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        DialogueLogEntry entry = JsonConvert.DeserializeObject<DialogueLogEntry>(line);
                        if (entry == null)
                        {
                            continue;
                        }

                        EnsureLogId(entry, filePath, lineNumber, line);

                        if (!seenLogIds.Add(entry.LogId))
                        {
                            continue;
                        }

                        entry.UploadStatus = uploadHistory.GetStatus(entry.LogId);
                        if (onlyUploadable && !uploadHistory.ShouldUpload(entry))
                        {
                            continue;
                        }

                        if (!includeUploaded && string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (recentLogs != null)
                        {
                            recentLogs.Enqueue(entry);
                            if (recentLogs.Count > maxCount)
                            {
                                recentLogs.Dequeue();
                            }

                            continue;
                        }

                        pendingLogs.Add(entry);
                        if (maxCount > 0 && pendingLogs.Count >= maxCount)
                        {
                            return pendingLogs;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DialogueLogUploadService] Failed to read {Path.GetFileName(filePath)}: {exception.Message}");
                }
            }

            if (recentLogs != null)
            {
                pendingLogs.AddRange(recentLogs);
            }

            return pendingLogs;
        }

        public void MarkUploaded(IEnumerable<DialogueLogEntry> entries)
        {
            uploadHistory.MarkUploaded(CollectLogIds(entries));
        }

        public void MarkUploaded(IEnumerable<string> logIds)
        {
            uploadHistory.MarkUploaded(logIds);
        }

        public void MarkFailed(IEnumerable<DialogueLogEntry> entries)
        {
            uploadHistory.MarkFailed(CollectLogIds(entries));
        }

        public void MarkFailed(IEnumerable<string> logIds)
        {
            uploadHistory.MarkFailed(logIds);
        }

        private static IEnumerable<string> ReadLinesShared(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }

        private static IEnumerable<string> CollectLogIds(IEnumerable<DialogueLogEntry> entries)
        {
            List<string> logIds = new List<string>();
            if (entries == null)
            {
                return logIds;
            }

            foreach (DialogueLogEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.LogId))
                {
                    logIds.Add(entry.LogId);
                }
            }

            return logIds;
        }

        private static void EnsureLogId(DialogueLogEntry entry, string filePath, int lineNumber, string rawJsonLine)
        {
            if (entry == null || !string.IsNullOrWhiteSpace(entry.LogId))
            {
                return;
            }

            entry.LogId = CreateLegacyLogId(filePath, lineNumber, rawJsonLine);
        }

        private static string CreateLegacyLogId(string filePath, int lineNumber, string rawJsonLine)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                string seed = $"{Path.GetFileName(filePath)}:{lineNumber}:{rawJsonLine}";
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed));
                StringBuilder builder = new StringBuilder(16);
                for (int i = 0; i < 8 && i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return $"legacy_{lineNumber}_{builder}";
            }
        }
    }
}
