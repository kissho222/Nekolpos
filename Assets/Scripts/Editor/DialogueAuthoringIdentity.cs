using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    // Separate metadata keeps authoring identities out of shipped Dialogue CSVs.
    internal static class DialogueAuthoringIdentity
    {
        public const string MetadataPath = "TalkSource/Editor/DialogueIdentity.json";
        [Serializable] internal sealed class Record
        {
            public string id;
            public string source;
            public string key;
        }
        [Serializable] internal sealed class Index { public List<Record> records = new List<Record>(); }

        public static string NormalizeSource(string path)
        {
            string full = DialogueDataLoader.GetFullPath(path).Replace('\\', '/');
            string root = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        public static string GroupKey(DialogueEntry entry, int row)
        {
            return (string.IsNullOrWhiteSpace(entry.RegexId) ? "(empty)" : entry.RegexId.Trim()) + "|" +
                (string.IsNullOrWhiteSpace(entry.InternalId) ? "(empty-id)" : entry.InternalId.Trim()) + "|" +
                (string.IsNullOrWhiteSpace(entry.Pattern) ? "__line_" + row : entry.Pattern.Trim());
        }

        public static void Bind(DialogueDataLoader.DataSet data, string metadataPath = MetadataPath)
        {
            if (string.IsNullOrWhiteSpace(data.SourcePath)) return;
            Index index = Load(metadataPath);
            string source = NormalizeSource(data.SourcePath);
            var groups = data.Entries.Select((entry, row) => new { entry, key = GroupKey(entry, row) }).GroupBy(item => item.key);
            bool changed = false;
            foreach (var group in groups)
            {
                string existingId = group.Select(item => item.entry.EditorDialogueId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrEmpty(existingId))
                {
                    foreach (var item in group) if (string.IsNullOrEmpty(item.entry.EditorDialogueId)) item.entry.EditorDialogueId = existingId;
                    continue;
                }
                Record record = index.records.SingleOrDefault(item => item.source == source && item.key == group.Key);
                if (record == null)
                {
                    record = new Record { id = Guid.NewGuid().ToString("N"), source = source, key = group.Key };
                    index.records.Add(record); changed = true;
                }
                foreach (var item in group) item.entry.EditorDialogueId = record.id;
            }
            if (changed) Write(index, metadataPath);
        }

        public static void Save(DialogueDataLoader.DataSet data, string metadataPath = MetadataPath)
        {
            Index index = Load(metadataPath);
            string source = NormalizeSource(string.IsNullOrWhiteSpace(data.SavePath) ? data.SourcePath : data.SavePath);
            var groups = data.Entries.Select((entry, row) => new { entry, key = GroupKey(entry, row) }).GroupBy(item => item.key);
            var used = new HashSet<string>();
            foreach (var group in groups)
            {
                string id = group.Select(item => item.entry.EditorDialogueId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (string.IsNullOrEmpty(id) || !used.Add(id)) { id = Guid.NewGuid().ToString("N"); used.Add(id); }
                // Save As is a copy: source ownership cannot silently migrate an existing identity.
                Record owner = index.records.FirstOrDefault(item => item.id == id);
                if (owner != null && owner.source != source) id = Guid.NewGuid().ToString("N");
                index.records.RemoveAll(item => item.source == source && (item.key == group.Key || item.id == id));
                index.records.Add(new Record { id = id, source = source, key = group.Key });
                foreach (var item in group) item.entry.EditorDialogueId = id;
            }
            Write(index, metadataPath);
        }

        public static void Delete(IEnumerable<string> ids, string metadataPath = MetadataPath)
        {
            var deleted = new HashSet<string>((ids ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            if (deleted.Count == 0) return;
            Index index = Load(metadataPath);
            if (index.records.RemoveAll(record => deleted.Contains(record.id)) > 0) Write(index, metadataPath);
        }

        internal static Index Load(string path)
        {
            Index index = File.Exists(path) ? JsonUtility.FromJson<Index>(File.ReadAllText(path)) : new Index();
            if (index?.records == null || index.records.Any(item => string.IsNullOrWhiteSpace(item.id)) ||
                index.records.GroupBy(item => item.id).Any(group => group.Count() > 1) ||
                index.records.GroupBy(item => item.source + "\n" + item.key).Any(group => group.Count() > 1))
                throw new InvalidDataException("DialogueID対応表が破損または重複しています。");
            return index;
        }
        private static void Write(Index index, string path)
        {
            TalkTopicMasterCsvUtility.AtomicWrite(path, Encoding.UTF8.GetBytes(JsonUtility.ToJson(index, true)));
        }
    }
}
