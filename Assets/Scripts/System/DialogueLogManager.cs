using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Backgammon.Conversation;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Nekolpos.System
{
    public sealed class DialogueLogManager : MonoBehaviour
    {
        public const string SpeakerPlayer = "Player";
        public const string SpeakerCat = "Cat";
        public const string SpeakerSystem = "System";

        public const string SourceFreeInput = "FreeInput";
        public const string SourceRegexReaction = "RegexReaction";
        public const string SourceYarn = "Yarn";
        public const string SourceChoice = "Choice";
        public const string SourceSystem = "System";
        public const string SourceAction = "Action";
        public const string SourceUnmatchedInput = "UnmatchedInput";
        public const string SourceUnknownWord = "UnknownWord";

        private const int DefaultMaxLogCount = 1000;
        private const int DefaultHistoryDisplayCount = 200;
        private const string DefaultUploadEndpoint = "https://nekolpos-log-ingest.nekolpos.workers.dev/api/dialogue-logs";
        private const string ClientInstallIdPlayerPrefsKey = "nekolpos.client_install_id";
        private const string UploadSchemaVersion = "nekolpos-dialogue-log/v1";

        private static DialogueLogManager instance;
        private static bool isQuitting;

        [SerializeField] private int maxLogCount = DefaultMaxLogCount;
        [SerializeField] private bool exportJsonl = true;
        [SerializeField] private bool includeSensitiveExportFields;
        [SerializeField] private int logRetentionMonths = 3;
        [Header("Open Beta Log Upload")]
        [SerializeField] private string uploadEndpoint = DefaultUploadEndpoint;
        [SerializeField] private string uploadAuthKey = string.Empty;
        [SerializeField] private string uploadHost = string.Empty;
        [SerializeField] private string uploadConsentVersion = string.Empty;
        [SerializeField] private bool autoUploadQueuedLogs = true;
        [SerializeField, Min(1)] private int uploadBatchSize = 100;
        // Runtime表示用のキャッシュ。Unityにシリアライズさせると、Play中に件数が増えるほど
        // Inspector/Editor側のシリアライズ負荷も増えるため、永続化対象にはしない。
        private readonly List<DialogueLogEntry> logs = new List<DialogueLogEntry>();
        private readonly List<DialogueLogEntry> historyCache = new List<DialogueLogEntry>();
        private bool historyCacheLoaded;

        private DialogueLogFileExporter fileExporter;
        private DialogueLogUploadHistory uploadHistory;
        private DialogueLogUploadService uploadService;
        private ConversationGameStateManager conversationGameStateManager;
        private Coroutine uploadCoroutine;
        private string clientInstallId;
        private string clientSessionId;

        public static DialogueLogManager Instance
        {
            get
            {
                if (isQuitting)
                {
                    return null;
                }

                EnsureInstance();
                return instance;
            }
        }

        public event Action LogsChanged;
        public event Action<bool> UploadCompleted;

        public int RuntimeLogCount => logs.Count;
        public int RuntimeHistoryCacheCount => historyCache.Count;
        public bool RuntimeHistoryCacheLoaded => historyCacheLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            isQuitting = false;
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            instance = FindFirstObjectByType<DialogueLogManager>();
            if (instance != null)
            {
                return;
            }

            GameObject managerObject = new GameObject(nameof(DialogueLogManager));
            instance = managerObject.AddComponent<DialogueLogManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            clientInstallId = ResolveClientInstallId();
            clientSessionId = Guid.NewGuid().ToString("N");
            fileExporter = CreateFileExporter();
            uploadHistory = new DialogueLogUploadHistory();
            uploadService = CreateUploadService();
            if (IsDedicatedManagerObject())
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private bool IsDedicatedManagerObject()
        {
            Component[] components = GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null ||
                    component is Transform ||
                    component is DialogueLogManager)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void Start()
        {
            TryStartQueuedUpload();
        }

        private void OnApplicationQuit()
        {
            if (instance == this)
            {
                fileExporter?.Flush();
                isQuitting = true;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                fileExporter?.Dispose();
                fileExporter = null;
            }
        }

        public void AddLog(DialogueLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            DialogueLogEntry normalizedEntry = NormalizeEntry(entry);

            logs.Add(normalizedEntry);

            if (historyCacheLoaded)
            {
                historyCache.Add(normalizedEntry.Clone());
                TrimHistoryCache(DefaultHistoryDisplayCount);
            }

            TrimLogs();
            WriteLogToFile(normalizedEntry);

            LogsChanged?.Invoke();
        }

        public void ExportOnlyLog(DialogueLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            DialogueLogEntry normalizedEntry = NormalizeEntry(entry);
            WriteLogToFile(normalizedEntry);
        }

        public List<DialogueLogEntry> GetLogs()
        {
            return new List<DialogueLogEntry>(logs);
        }

        public List<DialogueLogEntry> GetConversationHistoryLogs(int maxCount = 0)
        {
            DialogueLogUploadService service = GetUploadService();
            int displayCount = maxCount > 0 ? maxCount : DefaultHistoryDisplayCount;
            if (historyCacheLoaded)
            {
                return CloneMostRecent(historyCache, displayCount);
            }

            List<DialogueLogEntry> historyLogs = service.LoadConversationLogs(displayCount);
            HashSet<string> seenLogIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < historyLogs.Count; i++)
            {
                DialogueLogEntry entry = historyLogs[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.LogId))
                {
                    seenLogIds.Add(entry.LogId);
                }
            }

            for (int i = 0; i < logs.Count; i++)
            {
                DialogueLogEntry entry = logs[i];
                if (entry == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.LogId) && seenLogIds.Contains(entry.LogId))
                {
                    continue;
                }

                historyLogs.Add(entry.Clone());
                if (!string.IsNullOrWhiteSpace(entry.LogId))
                {
                    seenLogIds.Add(entry.LogId);
                }

            }

            historyLogs.Sort(CompareLogTimestamp);
            historyCache.Clear();
            historyCache.AddRange(historyLogs);
            TrimHistoryCache(displayCount);
            historyCacheLoaded = true;
            return CloneMostRecent(historyCache, displayCount);
        }

        public void ClearLogs()
        {
            if (logs.Count <= 0)
            {
                return;
            }

            logs.Clear();
            LogsChanged?.Invoke();
        }

        public void Flush()
        {
            fileExporter?.Flush();
        }

        public string GetCurrentFilePath()
        {
            if (!exportJsonl)
            {
                return string.Empty;
            }

            fileExporter ??= CreateFileExporter();
            return fileExporter.GetCurrentFilePath();
        }

        public string GetUploadHistoryFilePath()
        {
            uploadHistory ??= new DialogueLogUploadHistory();
            return uploadHistory.FilePath;
        }

        public List<DialogueLogEntry> GetPendingUploadLogs(int maxCount = 0)
        {
            return GetUploadService().LoadPendingLogs(maxCount);
        }

        public bool RetryQueuedUpload()
        {
            return TryStartQueuedUpload();
        }

        public void MarkLogsUploaded(IEnumerable<string> logIds)
        {
            GetUploadService().MarkUploaded(logIds);
            InvalidateHistoryCacheAndNotify();
        }

        public void MarkLogsFailed(IEnumerable<string> logIds)
        {
            GetUploadService().MarkFailed(logIds);
            InvalidateHistoryCacheAndNotify();
        }

        public bool SaveUploadReview(IEnumerable<string> queuedLogIds, IEnumerable<string> excludedLogIds)
        {
            DialogueLogUploadService service = GetUploadService();
            service.MarkQueued(queuedLogIds);
            service.MarkExcluded(excludedLogIds);
            InvalidateHistoryCacheAndNotify();
            return TryStartQueuedUpload();
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

            fileExporter?.Flush();
            fileExporter?.Dispose();
            fileExporter = null;

            uploadHistory ??= new DialogueLogUploadHistory();
            DialogueLogUploadService service = GetUploadService();
            int deletedFileLogCount = service.DeleteLogsNotSelectedForUpload(selectedIds);
            int deletedMemoryLogCount = RemoveUnselectedMemoryLogs(selectedIds);
            historyCache.Clear();
            historyCacheLoaded = false;

            if (exportJsonl)
            {
                fileExporter = CreateFileExporter();
            }

            if (deletedFileLogCount > 0 || deletedMemoryLogCount > 0)
            {
                LogsChanged?.Invoke();
            }

            return deletedFileLogCount + deletedMemoryLogCount;
        }

        private DialogueLogEntry NormalizeEntry(DialogueLogEntry entry)
        {
            // ゲーム状態は機微情報の出力を明示的に有効にした場合だけ収集する。
            // 通常設定ではExporterが破棄するデータなので、毎ログのJSON生成と
            // Dictionary保持は無駄なCPU・メモリ負荷になる。
            Dictionary<string, object> gameStateSnapshot = includeSensitiveExportFields
                ? entry.GameStateSnapshot ?? TryCaptureGameStateSnapshot()
                : null;

            return new DialogueLogEntry
            {
                LogId = string.IsNullOrWhiteSpace(entry.LogId) ? DialogueLogEntry.CreateLogId() : entry.LogId,
                Timestamp = string.IsNullOrWhiteSpace(entry.Timestamp) ? DialogueLogEntry.CreateTimestamp() : entry.Timestamp,
                Language = string.IsNullOrWhiteSpace(entry.Language) ? Application.systemLanguage.ToString() : entry.Language,
                Speaker = entry.Speaker ?? string.Empty,
                SpeakerDisplayName = string.IsNullOrWhiteSpace(entry.SpeakerDisplayName) ? null : entry.SpeakerDisplayName,
                Text = entry.Text ?? string.Empty,
                Source = entry.Source ?? string.Empty,
                Intent = string.IsNullOrWhiteSpace(entry.Intent) ? null : entry.Intent,
                NodeId = string.IsNullOrWhiteSpace(entry.NodeId) ? null : entry.NodeId,
                RawInput = string.IsNullOrWhiteSpace(entry.RawInput) ? null : entry.RawInput,
                NormalizedInput = string.IsNullOrWhiteSpace(entry.NormalizedInput) ? null : entry.NormalizedInput,
                MatchedRegex = string.IsNullOrWhiteSpace(entry.MatchedRegex) ? null : entry.MatchedRegex,
                IntentCandidates = entry.IntentCandidates != null ? new List<string>(entry.IntentCandidates) : new List<string>(),
                SelectedIntent = string.IsNullOrWhiteSpace(entry.SelectedIntent) ? entry.Intent : entry.SelectedIntent,
                SelectedResponseId = string.IsNullOrWhiteSpace(entry.SelectedResponseId) ? entry.NodeId : entry.SelectedResponseId,
                UnknownWords = entry.UnknownWords != null ? new List<string>(entry.UnknownWords) : new List<string>(),
                ReactionResult = string.IsNullOrWhiteSpace(entry.ReactionResult) ? null : entry.ReactionResult,
                TriggerType = string.IsNullOrWhiteSpace(entry.TriggerType) ? null : entry.TriggerType,
                MatchedPattern = string.IsNullOrWhiteSpace(entry.MatchedPattern) ? null : entry.MatchedPattern,
                FoodName = string.IsNullOrWhiteSpace(entry.FoodName) ? null : entry.FoodName,
                PlayerChoice = string.IsNullOrWhiteSpace(entry.PlayerChoice) ? null : entry.PlayerChoice,
                TalkTopicHintUsed = entry.TalkTopicHintUsed,
                GameStateSnapshot = gameStateSnapshot,
                UploadStatus = string.IsNullOrWhiteSpace(entry.UploadStatus) ? DialogueLogEntry.UploadStatusReview : entry.UploadStatus,
                QuestionLinks = entry.QuestionLinks
            };
        }

        private void WriteLogToFile(DialogueLogEntry entry)
        {
            if (!exportJsonl || entry == null)
            {
                return;
            }

            fileExporter ??= CreateFileExporter();
            fileExporter.AppendLog(entry);
        }

        private DialogueLogFileExporter CreateFileExporter()
        {
            return new DialogueLogFileExporter(
                includeSensitiveFields: includeSensitiveExportFields,
                retentionMonths: logRetentionMonths);
        }

        private DialogueLogUploadService CreateUploadService()
        {
            return new DialogueLogUploadService(
                uploadHistory: uploadHistory ??= new DialogueLogUploadHistory(),
                uploadAuthKey: uploadAuthKey);
        }

        private DialogueLogUploadService GetUploadService()
        {
            uploadService ??= CreateUploadService();
            uploadService.UploadAuthKey = uploadAuthKey;
            return uploadService;
        }

        private void TrimLogs()
        {
            int limit = maxLogCount > 0 ? maxLogCount : DefaultMaxLogCount;
            int overflow = logs.Count - limit;
            if (overflow > 0)
            {
                logs.RemoveRange(0, overflow);
            }
        }

        private Dictionary<string, object> TryCaptureGameStateSnapshot()
        {
            try
            {
                conversationGameStateManager ??= FindFirstObjectByType<ConversationGameStateManager>();
                if (conversationGameStateManager == null)
                {
                    return null;
                }

                string json = conversationGameStateManager.SaveToJson(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueLogManager] Failed to capture game state snapshot: {exception.Message}");
                return null;
            }
        }

        private int RemoveUnselectedMemoryLogs(HashSet<string> selectedIds)
        {
            int removedCount = 0;
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                DialogueLogEntry entry = logs[i];
                if (entry == null)
                {
                    logs.RemoveAt(i);
                    removedCount++;
                    continue;
                }

                if (string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.LogId) && selectedIds.Contains(entry.LogId))
                {
                    continue;
                }

                logs.RemoveAt(i);
                removedCount++;
            }

            return removedCount;
        }

        private static int CompareLogTimestamp(DialogueLogEntry left, DialogueLogEntry right)
        {
            string leftTimestamp = left != null ? left.Timestamp : null;
            string rightTimestamp = right != null ? right.Timestamp : null;
            return string.CompareOrdinal(leftTimestamp, rightTimestamp);
        }

        private void InvalidateHistoryCacheAndNotify()
        {
            historyCache.Clear();
            historyCacheLoaded = false;
            LogsChanged?.Invoke();
        }

        private bool TryStartQueuedUpload()
        {
            if (uploadCoroutine != null)
            {
                return true;
            }

            string endpoint = ResolveUploadEndpoint();
            if (!autoUploadQueuedLogs || string.IsNullOrWhiteSpace(endpoint) || !isActiveAndEnabled)
            {
                return false;
            }

            uploadCoroutine = StartCoroutine(UploadQueuedLogs());
            return true;
        }

        private IEnumerator UploadQueuedLogs()
        {
            int batchSize = Mathf.Max(1, uploadBatchSize);
            bool uploadedAny = false;
            bool failed = false;
            while (true)
            {
                List<DialogueLogEntry> batch = GetPendingUploadLogs(batchSize);
                if (batch.Count <= 0)
                {
                    break;
                }

                string json = JsonConvert.SerializeObject(CreateUploadPayload(batch));
                byte[] body = Encoding.UTF8.GetBytes(json);
                using (UnityWebRequest request = new UnityWebRequest(ResolveUploadEndpoint(), UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                    bool authHeaderSet = GetUploadService().ApplyUploadAuthHeader(request);
                    yield return request.SendWebRequest();

                    List<string> logIds = CollectLogIds(batch);
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        uploadedAny = uploadedAny || logIds.Count > 0;
                        MarkLogsUploaded(logIds);
                    }
                    else
                    {
                        failed = true;
                        MarkLogsFailed(logIds);
                        Debug.LogWarning(
                            $"[DialogueLogManager] Log upload failed ({request.responseCode}): {request.error} " +
                            $"endpoint={ResolveUploadEndpoint()} authHeaderSet={authHeaderSet}");
                        break;
                    }
                }
            }

            uploadCoroutine = null;
            if (uploadedAny || failed)
            {
                UploadCompleted?.Invoke(uploadedAny && !failed);
            }
        }

        private static List<string> CollectLogIds(IEnumerable<DialogueLogEntry> entries)
        {
            List<string> ids = new List<string>();
            if (entries == null)
            {
                return ids;
            }

            foreach (DialogueLogEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.LogId))
                {
                    ids.Add(entry.LogId);
                }
            }

            return ids;
        }

        private string ResolveUploadEndpoint()
        {
            return string.IsNullOrWhiteSpace(uploadEndpoint)
                ? DefaultUploadEndpoint
                : uploadEndpoint.Trim();
        }

        private DialogueLogUploadPayload CreateUploadPayload(List<DialogueLogEntry> batch)
        {
            if (string.IsNullOrWhiteSpace(clientInstallId))
            {
                clientInstallId = ResolveClientInstallId();
            }

            if (string.IsNullOrWhiteSpace(clientSessionId))
            {
                clientSessionId = Guid.NewGuid().ToString("N");
            }

            return new DialogueLogUploadPayload
            {
                SchemaVersion = UploadSchemaVersion,
                ClientInstallId = clientInstallId,
                ClientSessionId = clientSessionId,
                UploadBatchId = Guid.NewGuid().ToString("N"),
                AppVersion = Application.version ?? string.Empty,
                BuildId = Application.buildGUID ?? string.Empty,
                Platform = Application.platform.ToString(),
                Host = string.IsNullOrWhiteSpace(uploadHost) ? string.Empty : uploadHost.Trim(),
                ConsentVersion = string.IsNullOrWhiteSpace(uploadConsentVersion) ? string.Empty : uploadConsentVersion.Trim(),
                Logs = batch ?? new List<DialogueLogEntry>()
            };
        }

        private static string ResolveClientInstallId()
        {
            string existingId = PlayerPrefs.GetString(ClientInstallIdPlayerPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(existingId))
            {
                return existingId.Trim();
            }

            string generatedId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(ClientInstallIdPlayerPrefsKey, generatedId);
            PlayerPrefs.Save();
            return generatedId;
        }

        [Serializable]
        private sealed class DialogueLogUploadPayload
        {
            [JsonProperty("schema_version")]
            public string SchemaVersion = UploadSchemaVersion;

            [JsonProperty("client_install_id")]
            public string ClientInstallId = string.Empty;

            [JsonProperty("client_session_id")]
            public string ClientSessionId = string.Empty;

            [JsonProperty("upload_batch_id")]
            public string UploadBatchId = string.Empty;

            [JsonProperty("app_version")]
            public string AppVersion = string.Empty;

            [JsonProperty("build_id")]
            public string BuildId = string.Empty;

            [JsonProperty("platform")]
            public string Platform = string.Empty;

            [JsonProperty("host")]
            public string Host = string.Empty;

            [JsonProperty("consent_version")]
            public string ConsentVersion = string.Empty;

            [JsonProperty("logs")]
            public List<DialogueLogEntry> Logs = new List<DialogueLogEntry>();
        }

        private void TrimHistoryCache(int limit)
        {
            int overflow = historyCache.Count - Mathf.Max(1, limit);
            if (overflow > 0)
            {
                historyCache.RemoveRange(0, overflow);
            }
        }

        private static List<DialogueLogEntry> CloneMostRecent(List<DialogueLogEntry> source, int maxCount)
        {
            int count = Mathf.Min(source.Count, Mathf.Max(1, maxCount));
            int start = source.Count - count;
            List<DialogueLogEntry> result = new List<DialogueLogEntry>(count);
            for (int i = start; i < source.Count; i++)
            {
                result.Add(source[i]?.Clone());
            }

            return result;
        }
    }
}
