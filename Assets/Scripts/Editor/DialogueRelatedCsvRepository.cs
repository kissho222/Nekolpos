using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nekolpos.EditorTools
{
    // Form-specific panels use this repository without changing Dialogue's DataSet.
    internal sealed class DialogueRelatedCsvRepository
    {
        public readonly string Path;
        public readonly TalkTopicMasterCsvUtility.CsvDocument Document;
        private byte[] baseline;
        public bool HasChanges => baseline == null ? Document.Rows.Count > 0 : !TalkTopicMasterCsvUtility.SerializeDocument(Document).Equals(Encoding.UTF8.GetString(baseline).TrimStart('\uFEFF'), StringComparison.Ordinal);
        [Serializable] public sealed class Draft { public string path, csv, baselineBase64; public bool existed; }
        public Draft Capture() => new Draft { path = Path, csv = TalkTopicMasterCsvUtility.SerializeDocument(Document), existed = baseline != null, baselineBase64 = baseline == null ? "" : Convert.ToBase64String(baseline) };
        public static DialogueRelatedCsvRepository Restore(Draft draft)
        {
            var repository = new DialogueRelatedCsvRepository(draft.path);
            var document = TalkTopicMasterCsvUtility.ReadDocumentText(draft.csv);
            repository.Document.Headers.Clear(); repository.Document.Headers.AddRange(document.Headers);
            repository.Document.Rows.Clear(); repository.Document.Rows.AddRange(document.Rows);
            repository.baseline = draft.existed ? Convert.FromBase64String(draft.baselineBase64) : null;
            return repository;
        }
        public DialogueRelatedCsvRepository(string path, params string[] requiredHeaders)
        {
            Path = path; baseline = File.Exists(path) ? File.ReadAllBytes(path) : null;
            Document = TalkTopicMasterCsvUtility.ReadDocument(path);
            foreach (string header in requiredHeaders)
                if (!Document.Headers.Contains(header, StringComparer.OrdinalIgnoreCase)) Document.Headers.Add(header);
        }
        public List<Dictionary<string, string>> Find(string column, string value)
        {
            return Document.Rows.Where(row => string.Equals(TalkTopicMasterCsvUtility.ReadValue(row, column).Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        public Dictionary<string, string> Add()
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); Document.Rows.Add(row); return row;
        }
        public void RequireUnique(params string[] columns)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Document.Rows)
            {
                var values = columns.Select(column => TalkTopicMasterCsvUtility.ReadValue(row, column).Trim()).ToArray();
                if (values.Any(string.IsNullOrEmpty) || !keys.Add(string.Join("\n", values)))
                    throw new InvalidDataException("空または重複したキー: " + string.Join(" + ", columns) + " / " + string.Join(" / ", values));
            }
        }
        public void Save(Action publish = null)
        {
            byte[] current = File.Exists(Path) ? File.ReadAllBytes(Path) : null;
            if ((baseline == null) != (current == null) || (baseline != null && !baseline.SequenceEqual(current)))
                throw new IOException("外部でCSVが更新されています。再読込して編集をやり直してください: " + Path);
            if (current != null)
            {
                string backup = global::System.IO.Path.Combine(DialogueDataLoader.GetBackupDirectory(), global::System.IO.Path.GetFileName(Path) + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + ".bak");
                Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(backup)); File.WriteAllBytes(backup, current);
            }
            try
            {
                TalkTopicMasterCsvUtility.AtomicWrite(Path, Encoding.UTF8.GetBytes(TalkTopicMasterCsvUtility.SerializeDocument(Document)));
                baseline = File.ReadAllBytes(Path); // Refresh/reload callbacks must capture the post-save baseline.
                publish?.Invoke();
                baseline = File.ReadAllBytes(Path);
            }
            catch
            {
                baseline = current;
                if (current != null) TalkTopicMasterCsvUtility.AtomicWrite(Path, current);
                else if (File.Exists(Path)) File.Delete(Path);
                throw;
            }
        }
    }
}
