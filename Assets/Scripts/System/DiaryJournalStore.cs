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
            if (PlayerPrefs.GetInt(SpecialSortUnlockedPrefsKey, 0) != 0)
            {
                return;
            }

            PlayerPrefs.SetInt(SpecialSortUnlockedPrefsKey, 1);
            PlayerPrefs.Save();
        }

        public static bool IsSpecialDiarySortUnlocked
        {
            get
            {
                EnsureLoaded();
                return PlayerPrefs.GetInt(SpecialSortUnlockedPrefsKey, 0) != 0;
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
            DiaryDraftCollection collection = JsonUtility.FromJson<DiaryDraftCollection>(PlayerPrefs.GetString(key, string.Empty));
            if (collection?.entries == null)
            {
                return;
            }

            target.AddRange(collection.entries.Where(item => item != null && !string.IsNullOrWhiteSpace(item.diaryId)));
        }

        private static void Save()
        {
            EnsureLoaded();
            PlayerPrefs.SetString(DraftPrefsKey, JsonUtility.ToJson(new DiaryDraftCollection { entries = drafts }));
            PlayerPrefs.SetString(PendingPrefsKey, JsonUtility.ToJson(new DiaryDraftCollection { entries = pendingSpecialEntries }));
            PlayerPrefs.Save();
        }
    }
}
