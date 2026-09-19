using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine.Networking;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueLogUploadTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "Nekolpos_DialogueLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void AppendLog_WritesMonthlyJsonlFilesAndDefaultsUploadStatus()
        {
            var exporter = new DialogueLogFileExporter(tempDirectory);

            exporter.AppendLog(new DialogueLogEntry
            {
                Timestamp = "2026-05-26T15:30:12.000Z",
                Speaker = DialogueLogManager.SpeakerPlayer,
                Source = DialogueLogManager.SourceFreeInput,
                Text = "たいやきってなに？",
                RawInput = "たいやきってなに？"
            });

            exporter.AppendLog(new DialogueLogEntry
            {
                Timestamp = "2026-06-01T00:00:00.000Z",
                Speaker = DialogueLogManager.SpeakerCat,
                Source = DialogueLogManager.SourceRegexReaction,
                Text = "たいやきはおいしいよ"
            });

            exporter.Dispose();

            string mayFile = Path.Combine(tempDirectory, "conversation_2026_05.jsonl");
            string juneFile = Path.Combine(tempDirectory, "conversation_2026_06.jsonl");

            Assert.That(File.Exists(mayFile), Is.True);
            Assert.That(File.Exists(juneFile), Is.True);

            DialogueLogEntry mayEntry = JsonConvert.DeserializeObject<DialogueLogEntry>(File.ReadAllLines(mayFile)[0]);
            DialogueLogEntry juneEntry = JsonConvert.DeserializeObject<DialogueLogEntry>(File.ReadAllLines(juneFile)[0]);

            Assert.That(mayEntry.LogId, Is.Not.Null.And.Not.Empty);
            Assert.That(mayEntry.UploadStatus, Is.EqualTo(DialogueLogEntry.UploadStatusReview));
            Assert.That(juneEntry.ReactionResult, Is.EqualTo("たいやきはおいしいよ"));
        }

        [Test]
        public void AppendLog_PreservesUnknownWordsWhenSensitiveFieldsAreDisabled()
        {
            var exporter = new DialogueLogFileExporter(tempDirectory, includeSensitiveFields: false);

            exporter.AppendLog(new DialogueLogEntry
            {
                Timestamp = "2026-06-24T00:00:00.000Z",
                Speaker = DialogueLogManager.SpeakerPlayer,
                Source = DialogueLogManager.SourceUnknownWord,
                Text = "ハタハタなべってなに？",
                RawInput = "ハタハタなべ",
                UnknownWords = new List<string> { "ハタハタなべ", "ハタハタなべ" }
            });

            exporter.Dispose();

            string filePath = Path.Combine(tempDirectory, "conversation_2026_06.jsonl");
            DialogueLogEntry entry = JsonConvert.DeserializeObject<DialogueLogEntry>(File.ReadAllLines(filePath)[0]);

            Assert.That(entry.UnknownWords, Is.EquivalentTo(new[] { "ハタハタなべ" }));
            Assert.That(entry.RawInput, Is.Null);
        }

        [Test]
        public void LoadPendingLogs_SkipsUploadedAndKeepsFailedForRetry()
        {
            string filePath = Path.Combine(tempDirectory, "conversation_2026_05.jsonl");
            var seedEntries = new List<DialogueLogEntry>
            {
                new DialogueLogEntry { LogId = "log_uploaded", Timestamp = "2026-05-26T00:00:00.000Z", Speaker = "Player", Source = "FreeInput", Text = "A", UploadStatus = DialogueLogEntry.UploadStatusPending },
                new DialogueLogEntry { LogId = "log_failed", Timestamp = "2026-05-26T00:00:01.000Z", Speaker = "Player", Source = "FreeInput", Text = "B", UploadStatus = DialogueLogEntry.UploadStatusPending },
                new DialogueLogEntry { LogId = "log_pending", Timestamp = "2026-05-26T00:00:02.000Z", Speaker = "Player", Source = "FreeInput", Text = "C", UploadStatus = DialogueLogEntry.UploadStatusPending }
            };

            File.WriteAllLines(filePath, new[]
            {
                JsonConvert.SerializeObject(seedEntries[0]),
                JsonConvert.SerializeObject(seedEntries[1]),
                JsonConvert.SerializeObject(seedEntries[2]),
                JsonConvert.SerializeObject(seedEntries[2])
            });

            var history = new DialogueLogUploadHistory(tempDirectory);
            history.MarkUploaded(new[] { "log_uploaded" });
            history.MarkQueued(new[] { "log_pending" });
            history.MarkFailed(new[] { "log_failed" });

            var service = new DialogueLogUploadService(tempDirectory, history);
            List<DialogueLogEntry> pendingLogs = service.LoadPendingLogs();

            Assert.That(pendingLogs.Count, Is.EqualTo(2));
            Assert.That(pendingLogs.Exists(entry => entry.LogId == "log_uploaded"), Is.False);
            Assert.That(pendingLogs.Exists(entry => entry.LogId == "log_failed" && entry.UploadStatus == DialogueLogEntry.UploadStatusFailed), Is.True);
            Assert.That(pendingLogs.Exists(entry => entry.LogId == "log_pending" && entry.UploadStatus == DialogueLogEntry.UploadStatusQueued), Is.True);
        }

        [Test]
        public void LegacyLog_RequiresReviewBeforeItBecomesUploadable()
        {
            string filePath = Path.Combine(tempDirectory, "conversation_2026_05.jsonl");
            File.WriteAllText(filePath, "{\"timestamp\":\"2026-05-26T00:00:00.000Z\",\"speaker\":\"Player\",\"source\":\"FreeInput\",\"text\":\"legacy\"}" + Environment.NewLine);

            var service = new DialogueLogUploadService(tempDirectory);
            List<DialogueLogEntry> historyLogs = service.LoadConversationLogs();
            List<DialogueLogEntry> pendingLogs = service.LoadPendingLogs();

            Assert.That(historyLogs.Count, Is.EqualTo(1));
            Assert.That(historyLogs[0].LogId, Does.StartWith("legacy_"));
            Assert.That(historyLogs[0].UploadStatus, Is.EqualTo(DialogueLogEntry.UploadStatusReview));
            Assert.That(pendingLogs, Is.Empty);

            var history = new DialogueLogUploadHistory(tempDirectory);
            history.MarkQueued(new[] { historyLogs[0].LogId });
            pendingLogs = new DialogueLogUploadService(tempDirectory, history).LoadPendingLogs();
            Assert.That(pendingLogs.Count, Is.EqualTo(1));
            Assert.That(pendingLogs[0].UploadStatus, Is.EqualTo(DialogueLogEntry.UploadStatusQueued));
        }

        [Test]
        public void ExcludedLog_IsPersistedButNeverLoadedForUpload()
        {
            string filePath = Path.Combine(tempDirectory, "conversation_2026_05.jsonl");
            var entry = new DialogueLogEntry
            {
                LogId = "private_log",
                Timestamp = "2026-05-26T00:00:00.000Z",
                Speaker = "Player",
                Source = "FreeInput",
                Text = "private",
                UploadStatus = DialogueLogEntry.UploadStatusReview
            };
            File.WriteAllText(filePath, JsonConvert.SerializeObject(entry) + Environment.NewLine);

            var history = new DialogueLogUploadHistory(tempDirectory);
            history.MarkExcluded(new[] { entry.LogId });
            var service = new DialogueLogUploadService(tempDirectory, history);

            Assert.That(service.LoadPendingLogs(), Is.Empty);
            Assert.That(service.LoadConversationLogs()[0].UploadStatus, Is.EqualTo(DialogueLogEntry.UploadStatusExcluded));
        }

        [Test]
        public void ApplyUploadAuthHeader_AddsHeaderOnlyWhenKeyIsConfigured()
        {
            var service = new DialogueLogUploadService(tempDirectory, uploadAuthKey: " upload-key ");
            using (UnityWebRequest request = new UnityWebRequest("https://example.invalid/api/dialogue-logs", UnityWebRequest.kHttpVerbPOST))
            {
                service.ApplyUploadAuthHeader(request);
                Assert.That(request.GetRequestHeader("X-Nekolpos-Upload-Key"), Is.EqualTo("upload-key"));
            }

            service.UploadAuthKey = string.Empty;
            using (UnityWebRequest request = new UnityWebRequest("https://example.invalid/api/dialogue-logs", UnityWebRequest.kHttpVerbPOST))
            {
                service.ApplyUploadAuthHeader(request);
                Assert.That(request.GetRequestHeader("X-Nekolpos-Upload-Key"), Is.Null);
            }
        }

        [Test]
        public void LoadConversationLogs_ReadsFileWhileExporterKeepsWriterOpen()
        {
            string filePath = Path.Combine(tempDirectory, "conversation_2026_06.jsonl");
            var entry = new DialogueLogEntry
            {
                LogId = "shared_log",
                Timestamp = "2026-06-24T00:00:00.000Z",
                Speaker = "Player",
                Source = "FreeInput",
                Text = "shared"
            };

            using (FileStream stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.WriteLine(JsonConvert.SerializeObject(entry));
                writer.Flush();

                var service = new DialogueLogUploadService(tempDirectory);
                List<DialogueLogEntry> logs = service.LoadConversationLogs();

                Assert.That(logs.Count, Is.EqualTo(1));
                Assert.That(logs[0].LogId, Is.EqualTo("shared_log"));
            }
        }

        [Test]
        public void DeleteLogsNotSelectedForUpload_RemovesOnlyUnselectedPendingLogs()
        {
            string filePath = Path.Combine(tempDirectory, "conversation_2026_05.jsonl");
            var seedEntries = new List<DialogueLogEntry>
            {
                new DialogueLogEntry { LogId = "log_uploaded", Timestamp = "2026-05-26T00:00:00.000Z", Speaker = "Player", Source = "FreeInput", Text = "uploaded", UploadStatus = DialogueLogEntry.UploadStatusPending },
                new DialogueLogEntry { LogId = "log_selected", Timestamp = "2026-05-26T00:00:01.000Z", Speaker = "Player", Source = "FreeInput", Text = "selected", UploadStatus = DialogueLogEntry.UploadStatusPending },
                new DialogueLogEntry { LogId = "log_unselected", Timestamp = "2026-05-26T00:00:02.000Z", Speaker = "Player", Source = "FreeInput", Text = "unselected", UploadStatus = DialogueLogEntry.UploadStatusPending }
            };

            File.WriteAllLines(filePath, new[]
            {
                JsonConvert.SerializeObject(seedEntries[0]),
                JsonConvert.SerializeObject(seedEntries[1]),
                JsonConvert.SerializeObject(seedEntries[2])
            });

            var history = new DialogueLogUploadHistory(tempDirectory);
            history.MarkUploaded(new[] { "log_uploaded" });

            var service = new DialogueLogUploadService(tempDirectory, history);
            int deletedCount = service.DeleteLogsNotSelectedForUpload(new[] { "log_selected" });
            List<DialogueLogEntry> remainingLogs = service.LoadConversationLogs();

            Assert.That(deletedCount, Is.EqualTo(1));
            Assert.That(remainingLogs.Exists(entry => entry.LogId == "log_uploaded"), Is.True);
            Assert.That(remainingLogs.Exists(entry => entry.LogId == "log_selected"), Is.True);
            Assert.That(remainingLogs.Exists(entry => entry.LogId == "log_unselected"), Is.False);
        }
    }
}
