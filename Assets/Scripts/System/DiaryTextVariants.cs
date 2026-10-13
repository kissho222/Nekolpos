using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Nekolpos.System
{
    // Variant 1 stays in the readable locale columns; JSON contains only subsequent variants.
    public static class DiaryTextVariants
    {
        [Serializable] public sealed class Text
        {
            public string ja, en, zh;
            public string Resolve(string locale)
            {
                string value = (locale ?? "ja").StartsWith("en", StringComparison.OrdinalIgnoreCase) ? en :
                    (locale ?? "ja").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : ja;
                return !string.IsNullOrWhiteSpace(value) ? value : ja ?? "";
            }
        }
        [Serializable] public sealed class Collection { public List<Text> items = new List<Text>(); }
        public static List<Text> Read(string ja, string en, string zh, string additional)
        {
            var result = new List<Text> { new Text { ja = ja ?? "", en = en ?? "", zh = zh ?? "" } };
            if (string.IsNullOrWhiteSpace(additional)) return result;
            try
            {
                var collection = JsonUtility.FromJson<Collection>(additional);
                if (collection?.items == null || collection.items.Any(item => item == null))
                    throw new InvalidDataException("本文パターンの形式が不正です。");
                result.AddRange(collection.items);
            }
            catch (ArgumentException exception) { throw new InvalidDataException("本文パターンのJSONが不正です。", exception); }
            return result;
        }
        public static string WriteAdditional(IReadOnlyList<Text> items) => items.Count <= 1 ? "" :
            JsonUtility.ToJson(new Collection { items = items.Skip(1).ToList() });
        public static bool IsSequence(string mode) => string.Equals(mode, "Sequence", StringComparison.OrdinalIgnoreCase);
        public static string RandomText(IReadOnlyList<Text> items, string locale) => items == null || items.Count == 0 ? "" :
            items[items.Count == 1 ? 0 : UnityEngine.Random.Range(0, items.Count)].Resolve(locale);
    }
}
