using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nekolpos.System;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    internal static class DialogueDiaryAuthoring
    {
        public const string CandidatePath = "TalkSource/Diary/DiaryCandidates.csv";
        public const string LinkPath = "TalkSource/Diary/DialogueDiaryLinks.csv";
        public static readonly string[] CandidateHeaders = { "DiaryId", "kind", "ja", "en", "zh", "Enabled", "diary_variants", "diary_selection_mode" };
        public static readonly string[] LinkHeaders = { "DialogueId", "DiaryId", "condition", "Enabled" };

        public static DialogueDiaryCatalog.Document BuildDocument(string candidatePath = CandidatePath, string linkPath = LinkPath, string metadataPath = DialogueAuthoringIdentity.MetadataPath)
        {
            var candidates = TalkTopicMasterCsvUtility.ReadDocument(candidatePath);
            var links = TalkTopicMasterCsvUtility.ReadDocument(linkPath);
            var document = new DialogueDiaryCatalog.Document();
            foreach (var row in candidates.Rows)
                document.candidates.Add(new DialogueDiaryCatalog.Candidate
                {
                    variants = DiaryTextVariants.Read(Read(row, "ja"), Read(row, "en"), Read(row, "zh"), Read(row, "diary_variants")), selectionMode = Read(row, "diary_selection_mode"),
                    id = Read(row, "DiaryId").Trim(), kind = Read(row, "kind").Trim(), ja = Read(row, "ja"), en = Read(row, "en"), zh = Read(row, "zh"), enabled = Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(Read(row, "Enabled"))
                });
            var index = DialogueAuthoringIdentity.Load(metadataPath);
            var sourceData = new Dictionary<string, DialogueDataLoader.DataSet>();
            foreach (var row in links.Rows)
            {
                string id = Read(row, "DialogueId").Trim();
                var identity = index.records.SingleOrDefault(record => record.id == id);
                if (identity == null || !File.Exists(identity.source)) throw new InvalidDataException("DialogueIDの出典がありません: " + id);
                if (!sourceData.TryGetValue(identity.source, out var data)) { data = DialogueDataLoader.LoadFromPath(identity.source); sourceData.Add(identity.source, data); }
                var entry = data.Entries.Select((item, i) => new { item, key = DialogueAuthoringIdentity.GroupKey(item, i) }).FirstOrDefault(item => item.key == identity.key)?.item;
                if (entry == null) throw new InvalidDataException("会話キーが変更・削除されています: " + id);
                if (!data.Headers.Any(header => header.Equals("regex_jp", StringComparison.OrdinalIgnoreCase) || header.Equals("regex", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("独立日記のRuntime Adapter未対応形式: " + identity.source);
                document.links.Add(new DialogueDiaryCatalog.Link
                {
                    dialogueId = id, diaryId = Read(row, "DiaryId").Trim(), source = Path.GetFileNameWithoutExtension(identity.source),
                    entryId = entry.InternalId?.Trim() ?? "", regexId = entry.RegexId?.Trim() ?? "", pattern = entry.Pattern?.Trim() ?? "", condition = Read(row, "condition"), enabled = Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(Read(row, "Enabled"))
                });
            }
            DialogueDiaryCatalog.Validate(document); return document;
        }
        public static string CompileJson() => JsonUtility.ToJson(BuildDocument());
        private static string Read(Dictionary<string, string> row, string field) => TalkTopicMasterCsvUtility.ReadValue(row, field);
    }
}
