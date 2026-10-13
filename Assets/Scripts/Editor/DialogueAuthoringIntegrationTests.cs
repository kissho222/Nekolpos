using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Nekolpos.Data;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using UnityEngine;
using NUnit.Framework;
using Backgammon.Conversation;

namespace Nekolpos.EditorTools
{
    // Opt-in XML capture survives the Test Runner's Play Mode domain reload.
    [UnityEditor.InitializeOnLoad]
    internal static class DialogueAuthoringTestCapture
    {
        private const string Key = "Nekolpos.DialogueAuthoring.TestXmlPath";
        private static UnityEditor.TestTools.TestRunner.Api.TestRunnerApi api;
        private static Capture callback;
        static DialogueAuthoringTestCapture() { if (!string.IsNullOrEmpty(UnityEditor.SessionState.GetString(Key, ""))) Register(); }
        public static void Start(string path) { UnityEditor.SessionState.SetString(Key, path); Register(); }
        public static void Stop()
        {
            UnityEditor.SessionState.EraseString(Key);
            if (api != null && callback != null) api.UnregisterCallbacks(callback);
            callback = null; if (api != null) UnityEngine.Object.DestroyImmediate(api); api = null;
        }
        private static void Register()
        {
            if (callback != null) return;
            api = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>(); callback = new Capture(); api.RegisterCallbacks(callback);
        }
        private sealed class Capture : UnityEditor.TestTools.TestRunner.Api.ICallbacks
        {
            public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
            public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
            public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
            public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
            {
                string path = UnityEditor.SessionState.GetString(Key, "");
                if (!string.IsNullOrEmpty(path)) UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.SaveResultToFile(result, path);
            }
        }
    }

    public sealed class DialogueAuthoringIntegrationTests
    {
        private string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "DialogueAuthoring-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() { Directory.Delete(directory, true); }

        [Test] public void AtomicSaveRepeatedlyReplacesUnicodePathAndLeavesNoTemporaryFiles()
        {
            string path = Path.Combine(directory, "会話 ID.json");
            for (int i = 0; i < 100; i++)
            {
                byte[] content = Encoding.UTF8.GetBytes("{\"version\":" + i + "}");
                TalkTopicMasterCsvUtility.AtomicWrite(path, content);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(content));
            }
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
        [Test] public void AtomicSaveIdenticalContentDoesNotReplaceLockedFileOrChangeTimestamp()
        {
            string path = Path.Combine(directory, "unchanged.json"); byte[] content = Encoding.UTF8.GetBytes("original");
            File.WriteAllBytes(path, content); var timestamp = File.GetLastWriteTimeUtc(path);
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) TalkTopicMasterCsvUtility.AtomicWrite(path, content);
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
        [Test, Platform("Win")] public void AtomicSaveRetriesUntilTemporaryWindowsLockIsReleased()
        {
            string path = Path.Combine(directory, "temporary-lock.json"); File.WriteAllText(path, "original");
            var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = new global::System.Threading.Thread(() => { global::System.Threading.Thread.Sleep(60); reader.Dispose(); }) { IsBackground = true };
            release.Start();
            try { TalkTopicMasterCsvUtility.AtomicWrite(path, Encoding.UTF8.GetBytes("updated")); }
            finally { release.Join(); reader.Dispose(); }
            Assert.That(File.ReadAllText(path), Is.EqualTo("updated")); Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
        [Test, Platform("Win")] public void AtomicSavePersistentWindowsLockPreservesOriginalAndReportsPath()
        {
            string path = Path.Combine(directory, "persistent-lock.json"); File.WriteAllText(path, "original");
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Assert.Throws<IOException>(() => TalkTopicMasterCsvUtility.AtomicWrite(path, Encoding.UTF8.GetBytes("updated")));
                Assert.That(error.Message, Does.Contain(path)); Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
        }
        [Test] public void AtomicSaveReadOnlyDestinationPreservesOriginal()
        {
            string path = Path.Combine(directory, "readonly.json"); File.WriteAllText(path, "original"); var attributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                Assert.Throws<UnauthorizedAccessException>(() => TalkTopicMasterCsvUtility.AtomicWrite(path, Encoding.UTF8.GetBytes("updated")));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original")); Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
            finally { File.SetAttributes(path, attributes); }
        }
        [Test] public void AtomicSaveNullPayloadCannotModifyOriginal()
        {
            string path = Path.Combine(directory, "invalid.json"); File.WriteAllText(path, "original");
            Assert.Throws<ArgumentNullException>(() => TalkTopicMasterCsvUtility.AtomicWrite(path, null)); Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
        }

        [Test]
        public void TopicSynchronizationReflectsAddUpdateDisableAndRemoval()
        {
            string source = Path.Combine(directory, "source.csv"), runtime = Path.Combine(directory, "runtime.csv");
            const string header = "TalkID,Source,SourceKey,Idea,Import,Enabled\n";
            foreach (string row in new[] { "T1,source.csv,key,idea,input,TRUE\n", "T1,source.csv,key,updated,input,TRUE\n", "T1,source.csv,key,updated,input,FALSE\n", "" })
            {
                File.WriteAllText(source, header + row);
                TalkTopicMasterCsvUtility.Synchronize(source, runtime);
                Assert.That(File.ReadAllBytes(runtime), Is.EqualTo(File.ReadAllBytes(source)));
                var hints = TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(File.ReadAllText(runtime));
                Assert.That(hints.Count, Is.EqualTo(row.Contains("TRUE") ? 1 : 0));
                if (hints.Count > 0) { Assert.That(hints[0].Source, Is.EqualTo("source.csv")); Assert.That(hints[0].SourceKey, Is.EqualTo("key")); }
            }
        }

        [Test]
        public void FailedTopicPublicationRollsBackSourceAndDoesNotClaimSuccess()
        {
            string source = Path.Combine(directory, "source.csv"), blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(source, "TalkID\noriginal"); File.WriteAllText(blocker, "file");
            Assert.Throws<IOException>(() => TalkTopicMasterCsvUtility.SaveAndSynchronize(source, Path.Combine(blocker, "runtime.csv"), () => File.WriteAllText(source, "TalkID\nchanged")));
            Assert.That(File.ReadAllText(source), Is.EqualTo("TalkID\noriginal"));
        }

        [Test]
        public void ProjectTopicSourceAndRuntimeAreIdentical()
        {
            Assert.That(File.ReadAllBytes(TalkTopicMasterCsvUtility.RuntimePath), Is.EqualTo(File.ReadAllBytes(TalkTopicMasterCsvUtility.SourcePath)));
        }

        [Test]
        public void DialogueIdSurvivesReloadRenameAndLineAdditionButCopiesAreIndependent()
        {
            string metadata = Path.Combine(directory, "identity.json");
            var data = new DialogueDataLoader.DataSet { SourcePath = Path.Combine(directory, "dialogue.csv"), SavePath = Path.Combine(directory, "dialogue.csv") };
            var entry = new DialogueEntry { InternalId = "001", RegexId = "r", Pattern = "1" }; data.Entries.Add(entry);
            DialogueAuthoringIdentity.Bind(data, metadata); string original = entry.EditorDialogueId;
            entry.Pattern = "renamed"; DialogueAuthoringIdentity.Save(data, metadata);
            var reloaded = new DialogueDataLoader.DataSet { SourcePath = data.SourcePath, SavePath = data.SavePath };
            reloaded.Entries.Add(new DialogueEntry { InternalId = "001", RegexId = "r", Pattern = "renamed" });
            DialogueAuthoringIdentity.Bind(reloaded, metadata);
            Assert.That(reloaded.Entries[0].EditorDialogueId, Is.EqualTo(original));
            reloaded.Entries.Add(new DialogueEntry { InternalId = "001", RegexId = "r", Pattern = "renamed" });
            reloaded.Entries.Add(new DialogueEntry { InternalId = "001", RegexId = "r", Pattern = "copy", EditorDialogueId = original });
            DialogueAuthoringIdentity.Save(reloaded, metadata);
            Assert.That(reloaded.Entries[1].EditorDialogueId, Is.EqualTo(original));
            Assert.That(reloaded.Entries[2].EditorDialogueId, Is.Not.EqualTo(original));
            reloaded.SavePath = Path.Combine(directory, "copy.csv"); DialogueAuthoringIdentity.Save(reloaded, metadata);
            Assert.That(reloaded.Entries[0].EditorDialogueId, Is.Not.EqualTo(original));
        }

        [Test]
        public void ExplicitDialogueIdDeletionRemovesOnlyRequestedIdentity()
        {
            string metadata = Path.Combine(directory, "identity.json");
            var data = new DialogueDataLoader.DataSet { SourcePath = Path.Combine(directory, "dialogue.csv"), SavePath = Path.Combine(directory, "dialogue.csv") };
            data.Entries.Add(new DialogueEntry { InternalId = "001", RegexId = "one", Pattern = "1" });
            data.Entries.Add(new DialogueEntry { InternalId = "002", RegexId = "two", Pattern = "1" });
            DialogueAuthoringIdentity.Bind(data, metadata);
            string deletedId = data.Entries[0].EditorDialogueId;
            string retainedId = data.Entries[1].EditorDialogueId;

            DialogueAuthoringIdentity.Delete(new[] { deletedId }, metadata);

            var index = DialogueAuthoringIdentity.Load(metadata);
            Assert.That(index.records.Any(record => record.id == deletedId), Is.False);
            Assert.That(index.records.Any(record => record.id == retainedId), Is.True);
        }

        [Test]
        public void LinkedDiaryDeletionKeepsSharedCandidateAndDeletesOrphanCandidate()
        {
            string candidatesPath = Path.Combine(directory, "candidates.csv");
            string linksPath = Path.Combine(directory, "links.csv");
            File.WriteAllText(candidatesPath, "DiaryId,kind,ja,Enabled\nshared,normal,text,TRUE\norphan,normal,text,TRUE\n");
            File.WriteAllText(linksPath, "DialogueId,DiaryId,condition,Enabled\ndeleted,shared,,TRUE\nkept,shared,,TRUE\ndeleted,orphan,,TRUE\n");
            var candidates = new DialogueRelatedCsvRepository(candidatesPath);
            var links = new DialogueRelatedCsvRepository(linksPath);

            DialoguePreviewWindow.RemoveIndependentDiaryLinks("deleted", candidates, links);

            Assert.That(links.Find("DialogueId", "deleted"), Is.Empty);
            Assert.That(links.Find("DialogueId", "kept"), Has.Count.EqualTo(1));
            Assert.That(candidates.Find("DiaryId", "shared"), Has.Count.EqualTo(1));
            Assert.That(candidates.Find("DiaryId", "orphan"), Is.Empty);
        }

        [Test]
        public void LinkedTopicDeletionMatchesSourceAndSourceKeyTogether()
        {
            string path = Path.Combine(directory, "topics.csv");
            File.WriteAllText(path, "TalkID,Source,SourceKey,Enabled\none,folder/source.csv,001:r,TRUE\ntwo,folder/source.csv,002:r,TRUE\nthree,other.csv,001:r,TRUE\n");
            var topics = new DialogueRelatedCsvRepository(path);

            DialoguePreviewWindow.RemoveTalkTopicLink("folder\\source.csv", "001:r", topics);

            Assert.That(topics.Document.Rows.Select(row => TalkTopicMasterCsvUtility.ReadValue(row, "TalkID")),
                Is.EquivalentTo(new[] { "two", "three" }));
        }

        [Test]
        public void DialogueCsvRoundTripPreservesUnknownColumnsWithoutEditorIdentity()
        {
            string path = Path.Combine(directory, "dialogue.csv");
            File.WriteAllText(path, "id,regex_id,pattern,order,output_ja,custom\n001,r,1,0,existing,\"extra, value\"\n");
            var data = DialogueDataLoader.LoadFromPath(path); DialogueAuthoringIdentity.Bind(data, Path.Combine(directory, "identity.json"));
            DialogueDataLoader.SaveToPath(data, path);
            var reloaded = DialogueDataLoader.LoadFromPath(path);
            Assert.That(reloaded.Entries[0].GetValue("custom"), Is.EqualTo("extra, value"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain(data.Entries[0].EditorDialogueId));
        }

        [Test]
        public void RelatedCsvRejectsDuplicateKeysAndConcurrentEditsAndKeepsUnknownFields()
        {
            string path = Path.Combine(directory, "event.csv");
            File.WriteAllText(path, "key,ja,custom\none,existing,keep\n");
            var repository = new DialogueRelatedCsvRepository(path, "key", "ja");
            repository.Document.Rows[0]["ja"] = "updated"; repository.Save();
            Assert.That(TalkTopicMasterCsvUtility.ReadDocument(path).Rows[0]["custom"], Is.EqualTo("keep"));
            repository.Add()["key"] = "ONE"; Assert.Throws<InvalidDataException>(() => repository.RequireUnique("key"));
            File.AppendAllText(path, "two,external,extra\n"); Assert.Throws<IOException>(() => repository.Save());
        }

        [Test]
        public void TimedEventNewRecordEncryptsAndRuntimeKeepsTimeDisplayAndDiarySettings()
        {
            string input = Path.Combine(directory, "input"), output = Path.Combine(directory, "output"); Directory.CreateDirectory(input);
            var repository = new DialogueRelatedCsvRepository(Path.Combine(input, "SystemTimedEvent.csv"), "key", "ja", "en", "zh", "time_minutes", "display_seconds", "diary_id", "diary_kind");
            var row = repository.Add(); row["key"] = "authoring_test"; row["ja"] = "existing"; row["time_minutes"] = "15"; row["display_seconds"] = "3.5"; row["diary_id"] = "diary_test"; row["diary_kind"] = "special";
            DialoguePreviewWindow.ValidateTimedEvents(repository); repository.Save();
            string key = Guid.NewGuid().ToString("N"), iv = Guid.NewGuid().ToString("N").Substring(0, 16);
            Assert.That(CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, key, iv), Is.EqualTo(1));
            string csv;
            using (var aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key); aes.IV = Encoding.UTF8.GetBytes(iv);
                using (var stream = new CryptoStream(new MemoryStream(File.ReadAllBytes(Path.Combine(output, "SystemTimedEvent.bytes"))), aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) csv = reader.ReadToEnd();
            }
            var type = typeof(Nekolpos.System.SystemTimedEventCatalog);
            try
            {
                type.GetMethod("LoadCsv", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { csv });
                type.GetField("isLoaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
                var resolved = Nekolpos.System.SystemTimedEventCatalog.Resolve("authoring_test", "ja");
                Assert.That(resolved.TimeMinutes, Is.EqualTo(15)); Assert.That(resolved.DisplaySeconds, Is.EqualTo(3.5f));
                Assert.That(resolved.DiaryId, Is.EqualTo("diary_test")); Assert.That(resolved.IsSpecialDiary, Is.True);
            }
            finally { Nekolpos.System.SystemTimedEventCatalog.ResetCache(); }
        }

        [Test]
        public void TopicRegistrationUpdateDisableAndDuplicateValidationUseCanonicalRepository()
        {
            string path = Path.Combine(directory, "topics.csv"), runtime = Path.Combine(directory, "runtime.csv");
            var repository = new DialogueRelatedCsvRepository(path, TalkTopicMasterCsvUtility.MasterHeaders);
            var row = repository.Add(); row["TalkID"] = "T1"; row["Source"] = "source.csv"; row["SourceKey"] = "001:r"; row["Idea"] = "existing"; row["Import"] = "input"; row["Enabled"] = "TRUE";
            repository.RequireUnique("Source", "SourceKey"); repository.Save(() => TalkTopicMasterCsvUtility.Synchronize(path, runtime));
            Assert.That(TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(File.ReadAllText(runtime))[0].Import, Is.EqualTo("input"));
            row["Import"] = "updated"; repository.Save(() => TalkTopicMasterCsvUtility.Synchronize(path, runtime));
            Assert.That(TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(File.ReadAllText(runtime))[0].Import, Is.EqualTo("updated"));
            row["Enabled"] = "FALSE"; repository.Save(() => TalkTopicMasterCsvUtility.Synchronize(path, runtime));
            Assert.That(TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(File.ReadAllText(runtime)), Is.Empty);
            var duplicate = repository.Add(); duplicate["Source"] = "source.csv"; duplicate["SourceKey"] = "001:r";
            Assert.Throws<InvalidDataException>(() => repository.RequireUnique("Source", "SourceKey"));
            Assert.That(DialoguePreviewWindow.TopicSourceKey(new DialogueEntry { InternalId = "001", RegexId = "r", Pattern = "1" }, 0), Is.EqualTo("001:r"));
            Assert.That(DialoguePreviewWindow.ValidateTopicImport(new DialogueEntry { RegexPattern = "input" }, "input"), Does.Contain("到達可能"));
        }

        [Test]
        public void SharedReadingDiagnosticsDistinguishMissingDisabledConflictAndReview()
        {
            var document = new TalkTopicMasterCsvUtility.CsvDocument(); document.Headers.AddRange(new[] { "Surface", "Reading", "Enabled", "NeedsReview" });
            Assert.That(DialoguePreviewWindow.DiagnoseReadings(document, "肉球"), Does.Contain("未登録表記"));
            document.Rows.Add(new Dictionary<string, string> { ["Surface"] = "肉球", ["Reading"] = "にくきゅう", ["Enabled"] = "TRUE", ["NeedsReview"] = "FALSE" });
            Assert.That(DialoguePreviewWindow.DiagnoseReadings(document, "肉球"), Does.Not.Contain("未登録表記"));
            document.Rows[0]["Enabled"] = "FALSE"; document.Rows[0]["NeedsReview"] = "TRUE";
            Assert.That(DialoguePreviewWindow.DiagnoseReadings(document, "肉球"), Does.Contain("無効 / NeedsReview"));
            document.Rows.Add(new Dictionary<string, string> { ["Surface"] = "肉球", ["Reading"] = "other", ["Enabled"] = "TRUE" });
            Assert.That(DialoguePreviewWindow.DiagnoseReadings(document, "肉球"), Does.Contain("読み競合"));
            Assert.That(DialoguePreviewWindow.DiagnoseReadings(new TalkTopicMasterCsvUtility.CsvDocument(), "{{CAT_NAME}}"), Does.Not.Contain("未登録表記"));
        }

        [Test]
        public void ExistingDiaryRouteShowsNormalSpecialBodyAndKeyFallback()
        {
            var row = new Dictionary<string, string> { ["key"] = "event", ["ja"] = "{{CAT_NAME}}", ["diary_kind"] = "normal" };
            string normal = DialoguePreviewWindow.DescribeDiaryRoute(row);
            Assert.That(normal, Does.Contain("通常草稿").And.Contain("key fallback").And.Contain("{{CAT_NAME}}"));
            row["diary_kind"] = "special"; row["diary_id"] = "specific";
            Assert.That(DialoguePreviewWindow.DescribeDiaryRoute(row), Does.Contain("特別Pending").And.Contain("specific").And.Not.Contain("key fallback"));
        }

        private static void WithIsolatedDiary(Action action)
        {
            string[] keys = { "Nekolpos.Diary.Drafts.v1", "Nekolpos.Diary.SpecialPending.v1", "Nekolpos.Diary.SpecialSortUnlocked.v1" };
            bool[] had = keys.Select(PlayerPrefs.HasKey).ToArray();
            string[] values = keys.Select(key => PlayerPrefs.GetString(key, "")).ToArray();
            int unlock = PlayerPrefs.GetInt(keys[2], 0);
            Action reload = () =>
            {
                var flags = BindingFlags.Static | BindingFlags.NonPublic;
                typeof(DiaryJournalStore).GetField("loaded", flags).SetValue(null, false);
                foreach (string field in new[] { "drafts", "pendingSpecialEntries" })
                    ((global::System.Collections.IList)typeof(DiaryJournalStore).GetField(field, flags).GetValue(null)).Clear();
            };
            try { foreach (string key in keys) PlayerPrefs.DeleteKey(key); reload(); action(); }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    if (!had[i]) PlayerPrefs.DeleteKey(keys[i]); else if (i == 2) PlayerPrefs.SetInt(keys[i], unlock); else PlayerPrefs.SetString(keys[i], values[i]);
                PlayerPrefs.Save(); reload(); DialogueDiaryCatalog.ResetCache();
            }
        }

        private static DialogueDiaryCatalog.Document DiaryDocument()
        {
            return new DialogueDiaryCatalog.Document
            {
                candidates = new List<DialogueDiaryCatalog.Candidate>
                {
                    new DialogueDiaryCatalog.Candidate { id = "normal1", kind = "normal", ja = "first", enabled = true },
                    new DialogueDiaryCatalog.Candidate { id = "normal2", kind = "normal", ja = "second", enabled = true },
                    new DialogueDiaryCatalog.Candidate { id = "special", kind = "special", ja = "pending", enabled = true }
                },
                links = new List<DialogueDiaryCatalog.Link>
                {
                    new DialogueDiaryCatalog.Link { dialogueId = "d", diaryId = "normal1", source = "source", entryId = "001", regexId = "r", pattern = "1", enabled = true },
                    new DialogueDiaryCatalog.Link { dialogueId = "d", diaryId = "normal2", source = "source", entryId = "001", regexId = "r", pattern = "1", enabled = true },
                    new DialogueDiaryCatalog.Link { dialogueId = "d", diaryId = "special", source = "source", entryId = "001", regexId = "r", pattern = "1", condition = "allowed", enabled = true }
                }
            };
        }

        [Test]
        public void IndependentDiaryRecordsMultipleCandidatesWithoutTimeAdvanceAndReloadsForNight()
        {
            WithIsolatedDiary(() =>
            {
                var document = DiaryDocument(); DialogueDiaryCatalog.Validate(document);
                var reaction = new DialogueReactionData { DiarySourceCsv = "source", DiarySourceEntryId = "001", DiarySourceRegexId = "r", PatternID = "1" };
                Assert.That(DialogueDiaryCatalog.Record(reaction, 2, DayPeriod.Morning, _ => true, text => text + " resolved", document: document), Is.EqualTo(3));
                Assert.That(DiaryJournalStore.GetLatestNormalDraft(2, DayPeriod.Morning).body, Is.EqualTo("second resolved"));
                Assert.That(DiaryJournalStore.GetPendingSpecialEntries(), Has.Count.EqualTo(1));
                Assert.That(DialogueDiaryCatalog.Record(reaction, 2, DayPeriod.Afternoon, _ => false, text => text, document: document), Is.EqualTo(2));
                var flags = BindingFlags.Static | BindingFlags.NonPublic;
                typeof(DiaryJournalStore).GetField("loaded", flags).SetValue(null, false);
                foreach (string field in new[] { "drafts", "pendingSpecialEntries" }) ((global::System.Collections.IList)typeof(DiaryJournalStore).GetField(field, flags).GetValue(null)).Clear();
                Assert.That(DiaryJournalStore.GetLatestNormalDraft(2, DayPeriod.Morning).diaryId, Is.EqualTo("normal2"));
                Assert.That(DiaryJournalStore.GetLatestNormalDraft(2, DayPeriod.Afternoon).body, Is.EqualTo("second"));
                Assert.That(DiaryJournalStore.GetPendingSpecialEntries()[0].occurredDay, Is.EqualTo(2));
                reaction.DiarySourceEntryId = "other";
                Assert.That(DialogueDiaryCatalog.Record(reaction, 3, DayPeriod.Morning, _ => true, text => text, document: document), Is.Zero);
            });
        }

        [Test]
        public void IndependentDiaryAuthoringResolvesStableIdentityAndRejectsBrokenReferences()
        {
            string source = Path.Combine(directory, "source.csv"), metadata = Path.Combine(directory, "identity.json");
            File.WriteAllText(source, "id,regex_jp,regex_id,pattern,order,output_ja\n001,input,r,1,0,existing\n");
            var data = DialogueDataLoader.LoadFromPath(source); DialogueAuthoringIdentity.Bind(data, metadata);
            string candidates = Path.Combine(directory, "candidates.csv"), links = Path.Combine(directory, "links.csv");
            File.WriteAllText(candidates, "DiaryId,kind,ja,Enabled\nn,normal,existing,TRUE\n");
            File.WriteAllText(links, "DialogueId,DiaryId,condition,Enabled\n" + data.Entries[0].EditorDialogueId + ",n,,TRUE\n");
            var document = DialogueDiaryAuthoring.BuildDocument(candidates, links, metadata);
            Assert.That(document.links[0].entryId, Is.EqualTo("001")); Assert.That(document.links[0].regexId, Is.EqualTo("r")); Assert.That(document.links[0].pattern, Is.EqualTo("1"));
            File.WriteAllText(links, "DialogueId,DiaryId,Enabled\nunknown,n,TRUE\n");
            Assert.Throws<InvalidDataException>(() => DialogueDiaryAuthoring.BuildDocument(candidates, links, metadata));
        }

        [Test]
        public void ManagerIndependentDiaryMarksAllLinesForTimedEventSuppressionAndPreservesTime()
        {
            WithIsolatedDiary(() =>
            {
                var host = new GameObject("diary test"); host.SetActive(false);
                try
                {
                    var time = host.AddComponent<TimeManager>(); var manager = host.AddComponent<DialogueManager>();
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(TimeManager).GetField("initialized", flags).SetValue(time, true); typeof(TimeManager).GetField("currentDay", flags).SetValue(time, 2);
                    typeof(DialogueManager).GetField("timeManager", flags).SetValue(manager, time);
                    var document = DiaryDocument(); document.links[2].enabled = false;
                    typeof(DialogueDiaryCatalog).GetField("cached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, document);
                    var first = new DialogueReactionData { DiarySourceCsv = "source", DiarySourceEntryId = "001", DiarySourceRegexId = "r", PatternID = "1" };
                    var last = new DialogueReactionData { ActionId = "event", PatternID = "1" };
                    typeof(DialogueManager).GetMethod("RecordIndependentDiary", flags).Invoke(manager, new object[] { new List<DialogueReactionData> { first, last } });
                    var suppressed = (HashSet<DialogueReactionData>)typeof(DialogueManager).GetField("independentDiaryRecordedReactions", flags).GetValue(manager);
                    Assert.That(suppressed.Contains(last), Is.True); Assert.That(time.CurrentDay, Is.EqualTo(2)); Assert.That(time.CurrentPeriod, Is.EqualTo(DayPeriod.Morning));
                    Assert.That(DiaryJournalStore.GetLatestNormalDraft(2, DayPeriod.Morning).diaryId, Is.EqualTo("normal2"));
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            });
        }

        [Test]
        public void TopicImportUsesActualDialogueEngineAndPreservesDiarySourceKeys()
        {
            var host = new GameObject("topic input regression"); host.SetActive(false);
            var random = UnityEngine.Random.state;
            try
            {
                var engine = host.AddComponent<DialogueEngine>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                string csv = File.ReadAllText("TalkSource/TalkCSV/DialoguePreview.csv");
                Assert.That((bool)typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", flags).Invoke(engine, new object[] { csv, "DialoguePreview" }), Is.True);
                var topic = TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(File.ReadAllText(TalkTopicMasterCsvUtility.RuntimePath)).First(item => item.SourceKey == "00010:nail");
                List<DialogueReactionData> selected = null; engine.OnDialogueDetermined += group => selected = group;
                engine.ProcessInput(topic.Import);
                Assert.That(selected, Is.Not.Null.And.Not.Empty);
                Assert.That(selected[0].DiarySourceCsv, Is.EqualTo("DialoguePreview"));
                Assert.That(selected[0].DiarySourceEntryId, Is.EqualTo("00010"));
                Assert.That(selected[0].DiarySourceRegexId, Is.EqualTo("nail"));
            }
            finally { UnityEngine.Random.state = random; UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void RelatedDraftSurvivesEditorReloadAndStillDetectsExternalWrites()
        {
            string path = Path.Combine(directory, "related.csv"); File.WriteAllText(path, "key,ja,custom\nevent,existing,keep\n");
            var original = new DialogueRelatedCsvRepository(path); original.Document.Rows[0]["ja"] = "draft";
            string snapshot = JsonUtility.ToJson(original.Capture());
            var restored = DialogueRelatedCsvRepository.Restore(JsonUtility.FromJson<DialogueRelatedCsvRepository.Draft>(snapshot));
            Assert.That(restored.Document.Rows[0]["ja"], Is.EqualTo("draft")); Assert.That(restored.Document.Rows[0]["custom"], Is.EqualTo("keep"));
            File.AppendAllText(path, "external,new,extra\n"); Assert.Throws<IOException>(() => restored.Save());
        }

        [UnityEngine.TestTools.UnityTest]
        public global::System.Collections.IEnumerator ActualPlaybackRecordsOnceAcrossLinesAndRepeatAndFeedsCalendar()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/MainScene.unity", new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return new WaitForSecondsRealtime(0.2f);
            var manager = UnityEngine.Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
            var time = UnityEngine.Object.FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            var diary = UnityEngine.Object.FindFirstObjectByType<DiaryCalendarController>(FindObjectsInactive.Include);
            VerifyActualPlayback(manager, time, diary);
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        // Keep closures out of the iterator: Unity serializes it across EnterPlayMode.
        private static void VerifyActualPlayback(DialogueManager manager, TimeManager time, DiaryCalendarController diary)
        {
            bool hadEntries = PlayerPrefs.HasKey("Nekolpos.Diary.Entries.v1"); string savedEntries = PlayerPrefs.GetString("Nekolpos.Diary.Entries.v1", "");
            bool hadDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey); int savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            try
            {
                Assert.That(manager, Is.Not.Null); Assert.That(time, Is.Not.Null); Assert.That(diary, Is.Not.Null);
                WithIsolatedDiary(() =>
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var document = DiaryDocument(); document.links[2].condition = "";
                    typeof(DialogueDiaryCatalog).GetField("cached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, document);
                    var first = new DialogueReactionData { DiarySourceCsv = "source", DiarySourceEntryId = "001", DiarySourceRegexId = "r", PatternID = "1", TextJP = "test", Speaker = "System", ResponseType = "Normal" };
                    var last = new DialogueReactionData { PatternID = "1", TextJP = "test", Speaker = "System", ResponseType = "Normal" };
                    var group = new List<DialogueReactionData> { first, last }; int day = time.CurrentDay; DayPeriod period = time.CurrentPeriod;
                    var start = typeof(DialogueManager).GetMethod("StartPatternPlayback", flags);
                    start.Invoke(manager, new object[] { group, false, true, false });
                    Assert.That(DiaryJournalStore.GetPendingSpecialEntries(), Has.Count.EqualTo(1));
                    manager.ProgressNextLine();
                    Assert.That(DiaryJournalStore.GetPendingSpecialEntries(), Has.Count.EqualTo(1));
                    start.Invoke(manager, new object[] { group, true, true, false });
                    Assert.That(DiaryJournalStore.GetPendingSpecialEntries(), Has.Count.EqualTo(1));
                    Assert.That(time.CurrentDay, Is.EqualTo(day)); Assert.That(time.CurrentPeriod, Is.EqualTo(period));
                    var draft = DiaryJournalStore.GetLatestNormalDraft(day, period);
                    var date = time.GetCalendarDate(day); diary.AddEntryForDate(date, draft.body);
                    Assert.That(PlayerPrefs.GetString("Nekolpos.Diary.Entries.v1"), Does.Contain("second"));
                    diary.AddSpecialEntryForDate(date, DiaryJournalStore.GetPendingSpecialEntries()[0].body, "special");
                    Assert.That(PlayerPrefs.GetString("Nekolpos.Diary.Entries.v1"), Does.Contain("pending").And.Contain("special"));
                });
            }
            finally
            {
                manager?.ResetRuntimeStateForTitleRestart();
                if (hadEntries) PlayerPrefs.SetString("Nekolpos.Diary.Entries.v1", savedEntries); else PlayerPrefs.DeleteKey("Nekolpos.Diary.Entries.v1");
                if (hadDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay); else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                PlayerPrefs.Save(); DialogueDiaryCatalog.ResetCache();
            }
        }
    }
}
