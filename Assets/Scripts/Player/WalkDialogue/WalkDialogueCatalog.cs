using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>散歩専用CSVを通常会話データと分離して読み込むカタログ。</summary>
public sealed class WalkDialogueCatalog
{
    public const string ResourcePath = "Dialogue/WalkDialogue";

    public readonly struct Entry
    {
        public Entry(string triggerType, string triggerId, string speaker, string psychologyState, string text, int order)
        {
            TriggerType = triggerType;
            TriggerId = triggerId;
            Speaker = speaker;
            PsychologyState = psychologyState;
            Text = text;
            Order = order;
        }

        public string TriggerType { get; }
        public string TriggerId { get; }
        public string Speaker { get; }
        public string PsychologyState { get; }
        public string Text { get; }
        public int Order { get; }
    }

    private readonly List<Entry> entries = new List<Entry>();

    public IReadOnlyList<Entry> Entries => entries;

    public static WalkDialogueCatalog LoadDefault()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"[{nameof(WalkDialogueCatalog)}] Resources/{ResourcePath}.csv が見つかりません。");
            return new WalkDialogueCatalog();
        }

        return Parse(asset.text, asset.name);
    }

    public static WalkDialogueCatalog Parse(string csvText, string sourceName = "WalkDialogue.csv")
    {
        WalkDialogueCatalog catalog = new WalkDialogueCatalog();
        List<List<string>> rows = ReadRows(csvText);
        if (rows.Count == 0)
        {
            return catalog;
        }

        Dictionary<string, int> headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < rows[0].Count; index++)
        {
            headers[rows[0][index].Trim()] = index;
        }

        string[] required = { "TriggerType", "TriggerId", "Speaker", "PsychologyState", "Text", "Order" };
        foreach (string header in required)
        {
            if (!headers.ContainsKey(header))
            {
                Debug.LogWarning($"[{nameof(WalkDialogueCatalog)}] {sourceName} に必須列 {header} がありません。");
                return catalog;
            }
        }

        for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            string Get(string header) => headers[header] < row.Count ? row[headers[header]].Trim() : string.Empty;
            string triggerType = Get("TriggerType");
            string triggerId = Get("TriggerId");
            string speaker = Get("Speaker");
            string psychologyState = Get("PsychologyState");
            string text = Get("Text");
            if (!int.TryParse(Get("Order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) ||
                string.IsNullOrWhiteSpace(triggerType) || string.IsNullOrWhiteSpace(triggerId) ||
                string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning($"[{nameof(WalkDialogueCatalog)}] {sourceName} の {rowIndex + 1} 行目を読み飛ばしました。");
                continue;
            }

            catalog.entries.Add(new Entry(triggerType, triggerId, speaker, psychologyState, text, order));
        }

        catalog.entries.Sort((left, right) => left.Order.CompareTo(right.Order));
        return catalog;
    }

    public List<Entry> Find(string triggerType, string triggerId, string psychologyState = "NORMAL")
    {
        List<Entry> exact = FindInternal(triggerType, triggerId, psychologyState);
        return exact.Count > 0 || string.Equals(psychologyState, "NORMAL", StringComparison.OrdinalIgnoreCase)
            ? exact
            : FindInternal(triggerType, triggerId, "NORMAL");
    }

    private List<Entry> FindInternal(string triggerType, string triggerId, string psychologyState)
    {
        List<Entry> found = new List<Entry>();
        foreach (Entry entry in entries)
        {
            if (string.Equals(entry.TriggerType, triggerType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TriggerId, triggerId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(entry.PsychologyState) || string.Equals(entry.PsychologyState, psychologyState, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(entry);
            }
        }

        return found;
    }

    private static List<List<string>> ReadRows(string text)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> row = new List<string>();
        StringBuilder field = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < (text ?? string.Empty).Length; index++)
        {
            char character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append(character); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((character == '\n' || character == '\r') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0])) rows.Add(row);
                row = new List<string>();
            }
            else field.Append(character);
        }

        row.Add(field.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0])) rows.Add(row);
        return rows;
    }
}
