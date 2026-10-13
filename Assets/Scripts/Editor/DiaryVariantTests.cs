using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public sealed class DiaryVariantTests
    {
        private readonly string[] keys = { "Nekolpos.Diary.Drafts.v1", "Nekolpos.Diary.SpecialPending.v1", "Nekolpos.Diary.VariantSequence.v1" };
        private readonly Dictionary<string, string> previous = new();
        private readonly HashSet<string> existing = new();
        private int unlock;
        private string directory;
        private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        [SetUp] public void Setup()
        {
            foreach (string key in keys) { previous[key] = ProgressSaveStore.GetString(key, ""); if (ProgressSaveStore.HasKey(key)) existing.Add(key); }
            unlock = ProgressSaveStore.GetInt("Nekolpos.Diary.SpecialSortUnlocked.v1", 0);
            DiaryJournalStore.ResetAllForDebug();
            directory = Path.Combine(Path.GetTempPath(), "DiaryVariants-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        }
        [TearDown] public void Teardown()
        {
            foreach (string key in keys) { if (existing.Contains(key)) ProgressSaveStore.SetString(key, previous[key]); else ProgressSaveStore.DeleteKey(key); }
            ProgressSaveStore.SetInt("Nekolpos.Diary.SpecialSortUnlocked.v1", unlock); ProgressSaveStore.Save();
            ReloadStore(); SystemTimedEventCatalog.ResetCache(); existing.Clear(); previous.Clear(); Directory.Delete(directory, true);
        }
        private static void ReloadStore()
        {
            foreach (string field in new[] { "drafts", "pendingSpecialEntries" }) ((global::System.Collections.IList)typeof(DiaryJournalStore).GetField(field, Flags).GetValue(null)).Clear();
            typeof(DiaryJournalStore).GetField("loaded", Flags).SetValue(null, false);
        }
        private static List<DiaryTextVariants.Text> Texts(int count) => Enumerable.Range(1, count).Select(index => new DiaryTextVariants.Text { ja = index.ToString(), en = "en" + index, zh = "zh" + index }).ToList();
        [TestCase(1, 3)][TestCase(3, 1)][TestCase(3, 2)]
        public void IndependentCollectionsRoundTripAndPreserveIds(int displayCount, int diaryCount)
        {
            var row = new Dictionary<string, string> { ["key"] = "event", ["diary_id"] = "stable", ["notes"] = "preserved" };
            DialoguePreviewWindow.WriteVariants(row, false, Texts(displayCount)); DialoguePreviewWindow.WriteVariants(row, true, Texts(diaryCount));
            string path = Path.Combine(directory, "event.csv"); var repository = new DialogueRelatedCsvRepository(path, row.Keys.ToArray());
            repository.Document.Rows.Add(row); repository.Save();
            var restored = new DialogueRelatedCsvRepository(path).Document.Rows.Single();
            Assert.That(DialoguePreviewWindow.ReadVariants(restored, false).Count, Is.EqualTo(displayCount));
            Assert.That(DialoguePreviewWindow.ReadVariants(restored, true).Count, Is.EqualTo(diaryCount));
            var display = DialoguePreviewWindow.ReadVariants(restored, false); display.Add(new DiaryTextVariants.Text()); display.RemoveAt(0); DialoguePreviewWindow.WriteVariants(restored, false, display);
            Assert.That(DialoguePreviewWindow.ReadVariants(restored, true).Select(item => item.ja), Is.EqualTo(Texts(diaryCount).Select(item => item.ja)));
            Assert.That(restored["key"], Is.EqualTo("event")); Assert.That(restored["diary_id"], Is.EqualTo("stable")); Assert.That(restored["notes"], Is.EqualTo("preserved"));
        }
        [Test] public void LegacyTextCopiesWithoutChangingExistingDiaryIdentity()
        {
            var repository = new DialogueRelatedCsvRepository(Path.Combine(directory, "event.csv"), "key", "ja", "en", "zh", "diary_id");
            var row = repository.Add(); row["key"] = "old-event"; row["ja"] = "既存本文"; row["en"] = "old"; row["zh"] = "旧";
            DialoguePreviewWindow.MigrateTimedEventDiaries(repository);
            Assert.That(row["diary_ja"], Is.EqualTo(row["ja"])); Assert.That(row["diary_id"], Is.EqualTo("old-event"));
            row["ja"] = "changed"; DialoguePreviewWindow.MigrateTimedEventDiaries(repository); Assert.That(row["diary_ja"], Is.EqualTo("既存本文"));
            Assert.That(DialoguePreviewWindow.GenerateDiaryId("new action", repository), Is.EqualTo("new_action_diary"));
            row["diary_id"] = "new_action_diary"; Assert.That(DialoguePreviewWindow.GenerateDiaryId("new action", repository), Is.EqualTo("new_action_diary_2"));
        }
        [TestCase(false)][TestCase(true)] public void SequenceLoopsAndContinuesAfterStoreReload(bool special)
        {
            var variants = Texts(3);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(DiaryJournalStore.RecordVariants("sequence", 1, DayPeriod.Morning, special, variants, "Sequence"), Is.True);
                string body = special ? DiaryJournalStore.GetPendingSpecialEntries().Last().body : DiaryJournalStore.GetLatestNormalDraft(1, DayPeriod.Morning).body;
                Assert.That(body, Is.EqualTo((i % 3 + 1).ToString())); ReloadStore();
            }
            Assert.That(DiaryJournalStore.GetNextVariantIndex("sequence", 3), Is.EqualTo(2));
            Assert.That(DiaryJournalStore.GetNextVariantIndex("other", 3), Is.Zero);
        }
        [TestCase(0)][TestCase(1)][TestCase(3)] public void RandomRecordsOnlyRegisteredCandidates(int count)
        {
            var variants = Texts(count);
            for (int i = 0; i < 8; i++)
            {
                bool recorded = DiaryJournalStore.RecordVariants("random", 1, DayPeriod.Morning, false, variants, "Random");
                Assert.That(recorded, Is.EqualTo(count > 0));
                if (recorded) Assert.That(variants.Select(item => item.ja), Does.Contain(DiaryJournalStore.GetLatestNormalDraft(1, DayPeriod.Morning).body));
            }
            Assert.That(DiaryJournalStore.GetNextVariantIndex("random", 3), Is.Zero);
        }
        [Test] public void FailedResolutionAndPreviewDoNotAdvanceSequence()
        {
            var variants = Texts(3);
            Assert.Throws<InvalidOperationException>(() => DiaryJournalStore.RecordVariants("id", 1, DayPeriod.Morning, false, variants, "Sequence", "ja", value => throw new InvalidOperationException("failed")));
            Assert.That(DiaryJournalStore.RecordVariants("id", 1, DayPeriod.Morning, false, variants, "Sequence", "ja", value => ""), Is.False);
            DiaryTextVariants.RandomText(variants, "ja");
            Assert.That(DiaryJournalStore.GetNextVariantIndex("id", 3), Is.Zero); Assert.That(DiaryJournalStore.HasNormalDraft(1, DayPeriod.Morning), Is.False);
        }
        [Test] public void SequenceNormalizesAfterDeletionAndAddition()
        {
            var variants = Texts(3);
            DiaryJournalStore.RecordVariants("id", 1, DayPeriod.Morning, false, variants, "Sequence");
            DiaryJournalStore.RecordVariants("id", 1, DayPeriod.Morning, false, variants, "Sequence");
            variants.RemoveAt(2); Assert.That(DiaryJournalStore.GetNextVariantIndex("id", variants.Count), Is.Zero);
            DiaryJournalStore.RecordVariants("id", 1, DayPeriod.Morning, false, variants, "Sequence"); variants.Add(new DiaryTextVariants.Text { ja = "3" });
            Assert.That(DiaryJournalStore.GetNextVariantIndex("id", variants.Count), Is.EqualTo(1));
        }
        [Test] public void CatalogLoadsSingleAndAdditionalVariantsWithoutPairing()
        {
            var row = new Dictionary<string, string> { ["key"] = "fixture", ["diary_id"] = "stable", ["diary_selection_mode"] = "Sequence" };
            DialoguePreviewWindow.WriteVariants(row, false, Texts(1)); DialoguePreviewWindow.WriteVariants(row, true, Texts(3));
            var repository = new DialogueRelatedCsvRepository(Path.Combine(directory, "catalog.csv"), row.Keys.ToArray()); repository.Document.Rows.Add(row);
            typeof(SystemTimedEventCatalog).GetMethod("LoadCsv", Flags).Invoke(null, new object[] { TalkTopicMasterCsvUtility.SerializeDocument(repository.Document) });
            typeof(SystemTimedEventCatalog).GetField("isLoaded", Flags).SetValue(null, true);
            var resolved = SystemTimedEventCatalog.Resolve("fixture", "en");
            Assert.That(resolved.Text, Is.EqualTo("en1")); Assert.That(resolved.DiaryVariants.Count, Is.EqualTo(3)); Assert.That(resolved.DiarySelectionMode, Is.EqualTo("Sequence"));
            Assert.That(DiaryJournalStore.GetNextVariantIndex("stable", 3), Is.Zero);
        }
        [TestCase("normal")][TestCase("special")]
        public void IndependentCandidateUsesSamePersistentSequenceAndConditionsDoNotAdvance(string kind)
        {
            var reaction = new Nekolpos.Data.DialogueReactionData { DiarySourceCsv = "fixture", DiarySourceEntryId = "1", DiarySourceRegexId = "r", PatternID = "p" };
            var document = new DialogueDiaryCatalog.Document();
            document.candidates.Add(new DialogueDiaryCatalog.Candidate { id = "independent", kind = kind, enabled = true, variants = Texts(3), selectionMode = "Sequence" });
            document.links.Add(new DialogueDiaryCatalog.Link { dialogueId = "dialogue", diaryId = "independent", source = "fixture", entryId = "1", regexId = "r", pattern = "p", enabled = true, condition = "condition" });
            Assert.That(DialogueDiaryCatalog.Record(reaction, 1, DayPeriod.Morning, value => false, value => value, "ja", document), Is.Zero);
            Assert.That(DiaryJournalStore.GetNextVariantIndex("independent", 3), Is.Zero);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(DialogueDiaryCatalog.Record(reaction, 1, DayPeriod.Morning, value => true, value => value, "ja", document), Is.EqualTo(1));
                string body = kind == "special" ? DiaryJournalStore.GetPendingSpecialEntries().Last().body : DiaryJournalStore.GetLatestNormalDraft(1, DayPeriod.Morning).body;
                Assert.That(body, Is.EqualTo((i % 3 + 1).ToString())); ReloadStore();
            }
        }
        [Test] public void TimedEventRejectsEmptyDedicatedDiaryRatherThanSavingSilentMissingMaterial()
        {
            var repository = new DialogueRelatedCsvRepository(Path.Combine(directory, "empty.csv"), "key", "diary_ja");
            var row = repository.Add(); row["key"] = "empty"; row["diary_ja"] = "";
            Assert.Throws<InvalidDataException>(() => DialoguePreviewWindow.ValidateTimedEvents(repository));
            row["diary_ja"] = "既存の素材"; Assert.DoesNotThrow(() => DialoguePreviewWindow.ValidateTimedEvents(repository));
        }
        [Test] public void MalformedVariantJsonIsRejected() => Assert.Throws<InvalidDataException>(() => DiaryTextVariants.Read("", "", "", "invalid-json"));
        [Test] public void RepositorySelfPublishReloadUsesUpdatedBaselineButExternalChangesStillFail()
        {
            string path = Path.Combine(directory, "topic.csv"); File.WriteAllText(path, "key,ja\nitem,before\n");
            var repository = new DialogueRelatedCsvRepository(path); repository.Document.Rows[0]["ja"] = "own-save";
            DialogueRelatedCsvRepository restored = null; repository.Save(() => restored = DialogueRelatedCsvRepository.Restore(repository.Capture()));
            restored.Document.Rows[0]["ja"] = "second-save"; Assert.DoesNotThrow(() => restored.Save());
            File.AppendAllText(path, "external,change\n"); Assert.Throws<IOException>(() => restored.Save());
        }
        [Test] public void ProfileKeysAreStableAcrossPathSpellings()
        {
            Assert.That(DialoguePreviewWindow.ManuscriptStateKey("TalkSource/TalkCSV/DialoguePreview.csv"), Is.EqualTo(DialoguePreviewWindow.ManuscriptStateKey(Path.GetFullPath("TalkSource/TalkCSV/DialoguePreview.csv"))));
            Assert.That(DialogueAuthoringProfile.Known[0].Path, Is.EqualTo("TalkSource/TalkCSV/DialoguePreview.csv"));
            Assert.That(DialogueAuthoringProfile.Available().Any(profile => profile.FileName == "DialoguePreview_Integrated.csv"), Is.False);
        }
        [TestCase(-1, 3, false, 0)][TestCase(-1, 3, true, 2)][TestCase(2, 3, false, 0)][TestCase(0, 3, true, 2)]
        public void TabTraversalWrapsBothDirections(int current, int count, bool reverse, int expected) => Assert.That(DialoguePreviewWindow.NextFocusIndex(current, count, reverse), Is.EqualTo(expected));
    }
}
