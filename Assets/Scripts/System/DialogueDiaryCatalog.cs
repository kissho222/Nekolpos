using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Backgammon.Conversation;
using Nekolpos.TimeSystem;
using Nekolpos.Data;
using UnityEngine;

namespace Nekolpos.System
{
    public static class DialogueDiaryCatalog
    {
        public const string ResourcePath = "TalkData/DialogueDiary";
        [Serializable] public sealed class Candidate
        {
            public string id, kind, ja, en, zh, selectionMode;
            public List<DiaryTextVariants.Text> variants;
            public bool enabled;
        }
        [Serializable] public sealed class Link
        {
            public string dialogueId, diaryId, source, entryId, regexId, pattern, condition;
            public bool enabled;
        }
        [Serializable] public sealed class Document
        {
            public List<Candidate> candidates = new List<Candidate>();
            public List<Link> links = new List<Link>();
        }
        private static Document cached;
        public static void ResetCache() => cached = null;
        private static Document Load()
        {
            if (cached != null) return cached;
            if (!ConversationDataManager.TryLoadEncryptedCsvResource(ResourcePath, out string json, out string error))
            {
                cached = new Document();
                // Empty authoring configuration is valid; an unreadable installed asset is not.
                if (Resources.Load<TextAsset>(ResourcePath) != null) Debug.LogError("[DialogueDiary] 関連データの読込に失敗しました: " + error);
                return cached;
            }
            Document document = JsonUtility.FromJson<Document>(json);
            Validate(document); cached = document; return cached;
        }
        public static void Validate(Document document)
        {
            if (document?.candidates == null || document.links == null) throw new InvalidDataException("日記関連データが不正です。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in document.candidates)
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.id) || !ids.Add(candidate.id) || (candidate.kind != "normal" && candidate.kind != "special"))
                    throw new InvalidDataException("DiaryId重複・空欄または種別が不正です。");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in document.links)
                if (link == null || string.IsNullOrWhiteSpace(link.dialogueId) || !ids.Contains(link.diaryId) || string.IsNullOrWhiteSpace(link.source) ||
                    !keys.Add(link.dialogueId + "\n" + link.diaryId)) throw new InvalidDataException("日記関連の参照切れ・重複です。");
        }
        public static int Record(DialogueReactionData reaction, int day, DayPeriod period,
            Func<string, bool> evaluateCondition, Func<string, string> resolveText, string locale = "ja", Document document = null)
        {
            if (reaction == null || string.IsNullOrWhiteSpace(reaction.DiarySourceCsv)) return 0;
            document ??= Load();
            int recorded = 0;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in document.links)
            {
                if (!link.enabled || link.source != reaction.DiarySourceCsv || link.entryId != reaction.DiarySourceEntryId ||
                    link.regexId != reaction.DiarySourceRegexId || link.pattern != (reaction.PatternID ?? "").Trim()) continue;
                if (!string.IsNullOrWhiteSpace(link.condition))
                {
                    try { if (evaluateCondition == null || !evaluateCondition(link.condition)) continue; }
                    catch (FormatException exception) { Debug.LogWarning("[DialogueDiary] 条件が不正です: " + link.condition + " / " + exception.Message); continue; }
                }
                var candidate = document.candidates.FirstOrDefault(item => item.id == link.diaryId && item.enabled);
                if (candidate == null || !used.Add(candidate.id)) continue;
                var variants = candidate.variants ?? DiaryTextVariants.Read(candidate.ja, candidate.en, candidate.zh, "");
                if (DiaryJournalStore.RecordVariants(candidate.id, day, period, candidate.kind == "special", variants,
                    candidate.selectionMode, locale, resolveText)) recorded++;
            }
            return recorded;
        }
    }
}
