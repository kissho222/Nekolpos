using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Nekolpos.System
{
    public sealed class DialogueLogUploadHistory
    {
        private const string UploadHistoryFileName = "upload_history.json";

        private readonly object syncRoot = new object();
        private readonly string filePath;

        private HashSet<string> uploadedLogIds;
        private HashSet<string> failedLogIds;
        private HashSet<string> queuedLogIds;
        private HashSet<string> excludedLogIds;
        private bool loaded;

        public DialogueLogUploadHistory(string logsDirectoryPath = null)
        {
            filePath = Path.Combine(
                DialogueLogFileExporter.ResolveLogsDirectoryPath(logsDirectoryPath),
                UploadHistoryFileName);
        }

        public string FilePath => filePath;

        public string GetStatus(string logId)
        {
            if (string.IsNullOrWhiteSpace(logId))
            {
                return DialogueLogEntry.UploadStatusReview;
            }

            lock (syncRoot)
            {
                EnsureLoaded();
                if (uploadedLogIds.Contains(logId))
                {
                    return DialogueLogEntry.UploadStatusUploaded;
                }

                if (failedLogIds.Contains(logId))
                {
                    return DialogueLogEntry.UploadStatusFailed;
                }

                if (queuedLogIds.Contains(logId))
                {
                    return DialogueLogEntry.UploadStatusQueued;
                }

                if (excludedLogIds.Contains(logId))
                {
                    return DialogueLogEntry.UploadStatusExcluded;
                }

                return DialogueLogEntry.UploadStatusReview;
            }
        }

        public bool IsUploaded(string logId)
        {
            return string.Equals(GetStatus(logId), DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal);
        }

        public bool ShouldUpload(DialogueLogEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.LogId))
            {
                return false;
            }

            string status = GetStatus(entry.LogId);
            return string.Equals(status, DialogueLogEntry.UploadStatusQueued, StringComparison.Ordinal) ||
                   string.Equals(status, DialogueLogEntry.UploadStatusFailed, StringComparison.Ordinal);
        }

        public void MarkQueued(IEnumerable<string> logIds)
        {
            SetReviewDecision(logIds, queueForUpload: true);
        }

        public void MarkExcluded(IEnumerable<string> logIds)
        {
            SetReviewDecision(logIds, queueForUpload: false);
        }

        public void MarkUploaded(IEnumerable<string> logIds)
        {
            UpdateStatuses(logIds, markUploaded: true);
        }

        public void MarkFailed(IEnumerable<string> logIds)
        {
            UpdateStatuses(logIds, markUploaded: false);
        }

        private void UpdateStatuses(IEnumerable<string> logIds, bool markUploaded)
        {
            if (logIds == null)
            {
                return;
            }

            lock (syncRoot)
            {
                EnsureLoaded();

                bool changed = false;
                foreach (string logId in logIds)
                {
                    if (string.IsNullOrWhiteSpace(logId))
                    {
                        continue;
                    }

                    if (markUploaded)
                    {
                        failedLogIds.Remove(logId);
                        queuedLogIds.Remove(logId);
                        excludedLogIds.Remove(logId);
                        changed |= uploadedLogIds.Add(logId);
                    }
                    else
                    {
                        if (uploadedLogIds.Contains(logId))
                        {
                            continue;
                        }

                        queuedLogIds.Remove(logId);
                        excludedLogIds.Remove(logId);
                        changed |= failedLogIds.Add(logId);
                    }
                }

                if (changed)
                {
                    Save();
                }
            }
        }

        private void SetReviewDecision(IEnumerable<string> logIds, bool queueForUpload)
        {
            if (logIds == null)
            {
                return;
            }

            lock (syncRoot)
            {
                EnsureLoaded();
                bool changed = false;
                foreach (string logId in logIds)
                {
                    if (string.IsNullOrWhiteSpace(logId) || uploadedLogIds.Contains(logId))
                    {
                        continue;
                    }

                    failedLogIds.Remove(logId);
                    if (queueForUpload)
                    {
                        changed |= excludedLogIds.Remove(logId);
                        changed |= queuedLogIds.Add(logId);
                    }
                    else
                    {
                        changed |= queuedLogIds.Remove(logId);
                        changed |= excludedLogIds.Add(logId);
                    }
                }

                if (changed)
                {
                    Save();
                }
            }
        }

        private void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            uploadedLogIds = new HashSet<string>(StringComparer.Ordinal);
            failedLogIds = new HashSet<string>(StringComparer.Ordinal);
            queuedLogIds = new HashSet<string>(StringComparer.Ordinal);
            excludedLogIds = new HashSet<string>(StringComparer.Ordinal);
            loaded = true;

            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                UploadHistoryPayload payload = JsonConvert.DeserializeObject<UploadHistoryPayload>(json);
                if (payload?.uploaded_log_ids != null)
                {
                    for (int i = 0; i < payload.uploaded_log_ids.Count; i++)
                    {
                        string logId = payload.uploaded_log_ids[i];
                        if (!string.IsNullOrWhiteSpace(logId))
                        {
                            uploadedLogIds.Add(logId);
                        }
                    }
                }

                if (payload?.failed_log_ids != null)
                {
                    for (int i = 0; i < payload.failed_log_ids.Count; i++)
                    {
                        string logId = payload.failed_log_ids[i];
                        if (!string.IsNullOrWhiteSpace(logId) && !uploadedLogIds.Contains(logId))
                        {
                            failedLogIds.Add(logId);
                        }
                    }
                }


                AddIds(payload?.queued_log_ids, queuedLogIds);
                AddIds(payload?.excluded_log_ids, excludedLogIds);
                queuedLogIds.ExceptWith(uploadedLogIds);
                queuedLogIds.ExceptWith(failedLogIds);
                excludedLogIds.ExceptWith(uploadedLogIds);
                excludedLogIds.ExceptWith(failedLogIds);
                excludedLogIds.ExceptWith(queuedLogIds);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueLogUploadHistory] Failed to load upload history: {exception.Message}");
            }
        }

        private void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                UploadHistoryPayload payload = new UploadHistoryPayload
                {
                    uploaded_log_ids = new List<string>(uploadedLogIds),
                    failed_log_ids = new List<string>(failedLogIds),
                    queued_log_ids = new List<string>(queuedLogIds),
                    excluded_log_ids = new List<string>(excludedLogIds)
                };

                string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueLogUploadHistory] Failed to save upload history: {exception.Message}");
            }
        }

        private static void AddIds(IEnumerable<string> source, HashSet<string> destination)
        {
            if (source == null)
            {
                return;
            }

            foreach (string logId in source)
            {
                if (!string.IsNullOrWhiteSpace(logId))
                {
                    destination.Add(logId);
                }
            }
        }

        [Serializable]
        private sealed class UploadHistoryPayload
        {
            public List<string> uploaded_log_ids = new List<string>();
            public List<string> failed_log_ids = new List<string>();
            public List<string> queued_log_ids = new List<string>();
            public List<string> excluded_log_ids = new List<string>();
        }
    }
}
