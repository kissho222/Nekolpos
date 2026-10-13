using System;
using System.Collections.Generic;
using System.Linq;
using Nekolpos.TimeSystem;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// Keeps unfinished diary material separate from entries already written to the calendar.
    /// All event systems enqueue through this one store so skipped nights cannot lose special events.
    /// </summary>
    public static class DiaryJournalStore
    {
        private const string DraftPrefsKey = "Nekolpos.Diary.Drafts.v1";
        private const string PendingPrefsKey = "Nekolpos.Diary.SpecialPending.v1";
        private const string SpecialSortUnlockedPrefsKey = "Nekolpos.Diary.SpecialSortUnlocked.v1";

        [Serializable]
        public sealed class DiaryDraft
        {
            public string diaryId;
            public int occurredDay;
            public DayPeriod period;
            public int sequence;
            public string body;
        }

        [Serializable]
        private sealed class DiaryDraftCollection
        {
            public List<DiaryDraft> entries = new List<DiaryDraft>();
        }

        private static readonly List<DiaryDraft> drafts = new List<DiaryDraft>();
        private static readonly List<DiaryDraft> pendingSpecialEntries = new List<DiaryDraft>();
        private static bool loaded;
        private static int nextSequence;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { drafts.Clear(); pendingSpecialEntries.Clear(); loaded = false; nextSequence = 0; }

        private const string VariantSequencePrefsKey = "Nekolpos.Diary.VariantSequence.v1";
        [Serializable] private sealed class VariantPosition { public string id; public int next; }
        [Serializable] private sealed class VariantPositions { public List<VariantPosition> entries = new List<VariantPosition>(); }

        public static int GetNextVariantIndex(string diaryId, int count)
        {
            if (count <= 0) return 0;
            var state = JsonUtility.FromJson<VariantPositions>(ProgressSaveStore.GetString(VariantSequencePrefsKey, ""));
            int next = state?.entries?.FirstOrDefault(item => item.id == diaryId)?.next ?? 0;
            return ((next % count) + count) % count;
        }

        // Selection and progress commit belong to the recording boundary, never Resolve/preview.
        public static bool RecordVariants(string diaryId, int day, DayPeriod period, bool special,
            IReadOnlyList<DiaryTextVariants.Text> variants, string mode, string locale = "ja", Func<string, string> resolveText = null)
        {
            if (string.IsNullOrWhiteSpace(diaryId) || variants == null || variants.Count == 0) return false;
            bool sequence = DiaryTextVariants.IsSequence(mode);
            int index = sequence ? GetNextVariantIndex(diaryId, variants.Count) : variants.Count == 1 ? 0 : UnityEngine.Random.Range(0, variants.Count);
            string body = variants[index]?.Resolve(locale) ?? "";
            if (resolveText != null) body = resolveText(body);
            if (string.IsNullOrWhiteSpace(body)) return false;
            EnsureLoaded();
            var target = special ? pendingSpecialEntries : drafts;
            int previousSequence = nextSequence;
            string previousPositions = ProgressSaveStore.GetString(VariantSequencePrefsKey, "");
            string previousDrafts = ProgressSaveStore.GetString(DraftPrefsKey, "");
            string previousPending = ProgressSaveStore.GetString(PendingPrefsKey, "");
            int previousUnlock = ProgressSaveStore.GetInt(SpecialSortUnlockedPrefsKey, 0);
            try
            {
                if (!Add(target, diaryId, day, period, body)) return false;
                if (sequence)
                {
                    var positions = JsonUtility.FromJson<VariantPositions>(previousPositions) ?? new VariantPositions();
                    var position = positions.entries.FirstOrDefault(item => item.id == diaryId);
                    if (position == null) { position = new VariantPosition { id = diaryId }; positions.entries.Add(position); }
                    position.next = (index + 1) % variants.Count;
                    ProgressSaveStore.SetString(VariantSequencePrefsKey, JsonUtility.ToJson(positions));
                }
                if (special) ProgressSaveStore.SetInt(SpecialSortUnlockedPrefsKey, 1);
                Save();
                return true;
            }
            catch
            {
                target.RemoveAll(item => item.sequence >= previousSequence);
                nextSequence = previousSequence;
                ProgressSaveStore.SetString(VariantSequencePrefsKey, previousPositions);
                ProgressSaveStore.SetString(DraftPrefsKey, previousDrafts);
                ProgressSaveStore.SetString(PendingPrefsKey, previousPending);
                ProgressSaveStore.SetInt(SpecialSortUnlockedPrefsKey, previousUnlock);
                throw;
            }
        }

        public static void RecordNormalDraft(string diaryId, int day, DayPeriod period, string body)
        {
            Add(drafts, diaryId, day, period, body);
            Save();
        }

        public static void RecordSpecialPending(string diaryId, int day, DayPeriod period, string body)
        {
            if (Add(pendingSpecialEntries, diaryId, day, period, body))
            {
                UnlockSpecialDiarySort();
            }

            Save();
        }

        /// <summary>Marks the special-diary browser as discovered. This survives application restarts.</summary>
        public static void UnlockSpecialDiarySort()
        {
            if (ProgressSaveStore.GetInt(SpecialSortUnlockedPrefsKey, 0) != 0)
            {
                return;
            }

            ProgressSaveStore.SetInt(SpecialSortUnlockedPrefsKey, 1);
            ProgressSaveStore.Save();
        }

        public static bool IsSpecialDiarySortUnlocked
        {
            get
            {
                EnsureLoaded();
                return ProgressSaveStore.GetInt(SpecialSortUnlockedPrefsKey, 0) != 0;
            }
        }

        public static bool HasPendingSpecialEntries
        {
            get
            {
                EnsureLoaded();
                return pendingSpecialEntries.Count > 0;
            }
        }

        public static DiaryDraft GetLatestNormalDraft(int day, DayPeriod period)
        {
            EnsureLoaded();
            return drafts
                .Where(item => item.occurredDay == day && item.period == period)
                .OrderByDescending(item => item.sequence)
                .FirstOrDefault();
        }

        /// <summary>Removes a normal draft only after its displayed diary entry has been confirmed.</summary>
        public static void RemoveNormalDraft(DiaryDraft draft)
        {
            EnsureLoaded();
            if (draft == null)
            {
                return;
            }

            if (drafts.RemoveAll(item => item.sequence == draft.sequence) > 0)
            {
                Save();
            }
        }

        public static IReadOnlyList<DiaryDraft> GetPendingSpecialEntries()
        {
            EnsureLoaded();
            return pendingSpecialEntries
                .OrderBy(item => item.occurredDay)
                .ThenBy(item => item.period)
                .ThenBy(item => item.sequence)
                .ToList();
        }

        public static void RemovePendingSpecialEntries(IReadOnlyList<DiaryDraft> writtenEntries)
        {
            EnsureLoaded();
            if (writtenEntries == null || writtenEntries.Count == 0)
            {
                return;
            }

            HashSet<int> writtenSequences = new HashSet<int>(writtenEntries.Select(item => item.sequence));
            pendingSpecialEntries.RemoveAll(item => writtenSequences.Contains(item.sequence));
            Save();
        }

        public static bool HasNormalDraft(int day, DayPeriod period)
        {
            EnsureLoaded();
            return drafts.Any(item => item.occurredDay == day && item.period == period);
        }

        /// <summary>
        /// Discards the uncommitted morning, afternoon, and evening material for one game day.
        /// Special pending entries intentionally remain queued because they may be written on a later night.
        /// </summary>
        public static void DiscardNormalDraftsForDay(int day)
        {
            EnsureLoaded();
            if (drafts.RemoveAll(item => item.occurredDay == Mathf.Max(1, day)) > 0)
            {
                Save();
            }
        }

        /// <summary>Clears every uncommitted diary draft and the special-diary unlock for the debug reset tool.</summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static void ResetAllForDebug()
        {
            EnsureLoaded();
            drafts.Clear();
            pendingSpecialEntries.Clear();
            nextSequence = 0;
            ProgressSaveStore.DeleteKey(VariantSequencePrefsKey);
            ProgressSaveStore.DeleteKey(DraftPrefsKey);
            ProgressSaveStore.DeleteKey(PendingPrefsKey);
            ProgressSaveStore.DeleteKey(SpecialSortUnlockedPrefsKey);
            ProgressSaveStore.Save();
        }
#endif

        private static bool Add(List<DiaryDraft> target, string diaryId, int day, DayPeriod period, string body)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(diaryId) || string.IsNullOrWhiteSpace(body))
            {
                Debug.LogWarning("[DiaryJournal] 空のDiaryIdまたは本文は記録しません。");
                return false;
            }

            target.Add(new DiaryDraft
            {
                diaryId = diaryId.Trim(),
                occurredDay = Mathf.Max(1, day),
                period = period,
                sequence = nextSequence++,
                body = body.Trim()
            });
            return true;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            Load(DraftPrefsKey, drafts);
            Load(PendingPrefsKey, pendingSpecialEntries);
            if (pendingSpecialEntries.Count > 0)
            {
                UnlockSpecialDiarySort();
            }
            nextSequence = drafts.Concat(pendingSpecialEntries).Select(item => item.sequence).DefaultIfEmpty(-1).Max() + 1;
        }

        private static void Load(string key, List<DiaryDraft> target)
        {
            DiaryDraftCollection collection = JsonUtility.FromJson<DiaryDraftCollection>(ProgressSaveStore.GetString(key, string.Empty));
            if (collection?.entries == null)
            {
                return;
            }

            target.AddRange(collection.entries.Where(item => item != null && !string.IsNullOrWhiteSpace(item.diaryId)));
        }

        private static void Save()
        {
            EnsureLoaded();
            ProgressSaveStore.SetString(DraftPrefsKey, JsonUtility.ToJson(new DiaryDraftCollection { entries = drafts }));
            ProgressSaveStore.SetString(PendingPrefsKey, JsonUtility.ToJson(new DiaryDraftCollection { entries = pendingSpecialEntries }));
            ProgressSaveStore.Save();
        }
    }
}
