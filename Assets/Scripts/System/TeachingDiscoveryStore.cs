using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nekolpos.System
{
    public enum TeachingCategory
    {
        None,
        FavoritePoint,
        CatExpression,
        Felidae,
        CatBreed,
        CatSeries,
        FavoriteWeather
    }

    public static class TeachingDiscoveryStore
    {
        private const string Prefix = "Nekolpos.TeachingDiscovery.";
        public static event Action<DiscoveryMarkResult> DiscoveryChanged;

        public static bool IsDiscovered(TeachingCategory category, string entryId)
        {
            if (category == TeachingCategory.None || string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            return PlayerPrefs.GetInt(BuildEntryKey(category, entryId), 0) != 0;
        }

        public static bool IsSeriesDiscovered(string seriesGroupId)
        {
            if (string.IsNullOrWhiteSpace(seriesGroupId))
            {
                return false;
            }

            return PlayerPrefs.GetInt(BuildSeriesKey(seriesGroupId), 0) != 0;
        }

        public static DiscoveryMarkResult MarkDiscovered(TeachingCategory category, string entryId, string seriesGroupId)
        {
            DiscoveryMarkResult result = new DiscoveryMarkResult
            {
                Category = category
            };
            if (category == TeachingCategory.None || string.IsNullOrWhiteSpace(entryId))
            {
                return result;
            }

            if (!IsDiscovered(category, entryId))
            {
                PlayerPrefs.SetInt(BuildEntryKey(category, entryId), 1);
                IncrementCount(BuildCategoryCountKey(category));
                result.EntryWasNew = true;
            }

            if (category == TeachingCategory.CatSeries && !string.IsNullOrWhiteSpace(seriesGroupId) && !IsSeriesDiscovered(seriesGroupId))
            {
                PlayerPrefs.SetInt(BuildSeriesKey(seriesGroupId), 1);
                IncrementCount(BuildSeriesCountKey());
                result.SeriesWasNew = true;
            }

            PlayerPrefs.Save();
            if (result.EntryWasNew || result.SeriesWasNew)
            {
                DiscoveryChanged?.Invoke(result);
            }

            return result;
        }

        public static int GetCategoryUniqueCount(TeachingCategory category)
        {
            return category == TeachingCategory.None ? 0 : PlayerPrefs.GetInt(BuildCategoryCountKey(category), 0);
        }

        public static int GetCatSeriesUniqueSeriesCount()
        {
            return PlayerPrefs.GetInt(BuildSeriesCountKey(), 0);
        }

        public static int GetCatExpressionRankingValue()
        {
            return GetCategoryUniqueCount(TeachingCategory.CatExpression);
        }

        public static int GetCatSeriesRankingValue()
        {
            return GetCatSeriesUniqueSeriesCount();
        }

        public static void ResetAll()
        {
            foreach (TeachingCategory category in Enum.GetValues(typeof(TeachingCategory)))
            {
                if (category == TeachingCategory.None)
                {
                    continue;
                }

                PlayerPrefs.DeleteKey(BuildCategoryCountKey(category));
                DeleteKnownKeys(BuildKnownEntryListKey(category), key => BuildEntryKey(category, key));
            }

            PlayerPrefs.DeleteKey(BuildSeriesCountKey());
            DeleteKnownKeys(BuildKnownSeriesListKey(), BuildSeriesKey);
            PlayerPrefs.Save();
            DiscoveryChanged?.Invoke(new DiscoveryMarkResult { WasReset = true });
        }

        public static void RememberEntryKey(TeachingCategory category, string entryId)
        {
            if (category == TeachingCategory.None || string.IsNullOrWhiteSpace(entryId))
            {
                return;
            }

            RememberKnownKey(BuildKnownEntryListKey(category), entryId.Trim());
        }

        public static void RememberSeriesKey(string seriesGroupId)
        {
            if (string.IsNullOrWhiteSpace(seriesGroupId))
            {
                return;
            }

            RememberKnownKey(BuildKnownSeriesListKey(), seriesGroupId.Trim());
        }

        private static void IncrementCount(string key)
        {
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        }

        private static string BuildEntryKey(TeachingCategory category, string entryId)
        {
            return Prefix + "Entry." + category + "." + entryId.Trim();
        }

        private static string BuildSeriesKey(string seriesGroupId)
        {
            return Prefix + "Series." + seriesGroupId.Trim();
        }

        private static string BuildCategoryCountKey(TeachingCategory category)
        {
            return Prefix + "Count." + category;
        }

        private static string BuildSeriesCountKey()
        {
            return Prefix + "Count.CatSeries.SeriesGroup";
        }

        private static string BuildKnownEntryListKey(TeachingCategory category)
        {
            return Prefix + "KnownEntries." + category;
        }

        private static string BuildKnownSeriesListKey()
        {
            return Prefix + "KnownSeries";
        }

        private static void RememberKnownKey(string listKey, string value)
        {
            string existing = PlayerPrefs.GetString(listKey, string.Empty);
            HashSet<string> values = ParseList(existing);
            if (!values.Add(value))
            {
                return;
            }

            PlayerPrefs.SetString(listKey, string.Join("\n", values));
        }

        private static void DeleteKnownKeys(string listKey, Func<string, string> buildSaveKey)
        {
            HashSet<string> values = ParseList(PlayerPrefs.GetString(listKey, string.Empty));
            foreach (string value in values)
            {
                PlayerPrefs.DeleteKey(buildSaveKey(value));
            }

            PlayerPrefs.DeleteKey(listKey);
        }

        private static HashSet<string> ParseList(string raw)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return values;
            }

            string[] parts = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values;
        }
    }

    public struct DiscoveryMarkResult
    {
        public TeachingCategory Category;
        public bool EntryWasNew;
        public bool SeriesWasNew;
        public bool WasReset;
    }
}
