using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools.DialogueGraph
{
    internal enum DialogueGraphNodeKind { Dialogue, Condition, Choice, Timeline, StateChange, TimedEvent, End, Dynamic }

    internal sealed class DialogueGraphNode
    {
        public string Id;
        public DialogueGraphNodeKind Kind;
        public string Title;
        public string Body;
        public DialogueGraphEntry Entry;
        public string Detail;
        public UnityEngine.Object Asset;
    }

    internal sealed class DialogueGraphEdge
    {
        public string From;
        public string To;
        public string Label;
        public bool IsWarning;
    }

    internal sealed class DialogueGraphEntry
    {
        public string Key;
        public string SourcePath;
        public string SourceName;
        public int Line;
        public Dictionary<string, string> Values;
        public string Id => Get("id");
        public string Input => Get("input");
        public string Pattern => Get("pattern", "patternid", "pattern_id");
        public int Order => int.TryParse(Get("order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        public string Text => Get("output_ja", "text_jp", "text", "ja", "line");
        public string ResponseType => Get("response_type", "responsetype");
        public string Condition => Get("condition");
        public string ActionId => Get("action_id", "actionid", "next_action", "nextaction");
        public string TimedEventKey => Get("timed_event_key", "timedeventkey", "system_timed_event_key");
        public string TargetPattern => Get("target_pattern", "targetpattern");
        public string YesPattern => FirstNonEmpty(Get("choice_yes_pattern", "choiceyespattern"), Get("yes_next_pattern", "yesnextpattern"));
        public string NoPattern => FirstNonEmpty(Get("choice_no_pattern", "choicenopattern"), Get("no_next_pattern", "nonextpattern"));
        public string Animation => Get("animation");
        public string EmotionChange => Get("emotion_change_type", "emotionchangetype");
        public string EmotionValue => Get("emotion_change_value", "emotionchangevalue");
        public string SpeechControl => Get("speechcontrol", "speech_control");
        public string Get(params string[] names)
        {
            foreach (string name in names)
                if (Values.TryGetValue(NormalizeHeader(name), out string value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return string.Empty;
        }
        private static string FirstNonEmpty(string first, string second) => !string.IsNullOrWhiteSpace(first) ? first : second;
        internal static string NormalizeHeader(string value) => (value ?? string.Empty).Trim('\uFEFF', '\u200B').Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed class DialogueGraphData
    {
        public readonly Dictionary<string, DialogueGraphNode> Nodes = new Dictionary<string, DialogueGraphNode>(StringComparer.Ordinal);
        public readonly List<DialogueGraphEdge> Edges = new List<DialogueGraphEdge>();
        public readonly List<DialogueGraphEntry> Entries = new List<DialogueGraphEntry>();
        public readonly List<string> Sources = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Read-only projection of the current dialogue CSV files for the editor graph.</summary>
    internal static class DialogueGraphDataProvider
    {
        private const string SourceRoot = "TalkSource/TalkCSV";

        public static DialogueGraphData Load()
        {
            var data = new DialogueGraphData();
            var timedEvents = new Dictionary<string, DialogueGraphEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in DiscoverCsvPaths())
            {
                List<DialogueGraphEntry> parsed = ParseCsv(path, data.Warnings);
                if (parsed.Count == 0) continue;
                bool isTimedEvent = Path.GetFileNameWithoutExtension(path).Equals("SystemTimedEvent", StringComparison.OrdinalIgnoreCase);
                if (isTimedEvent)
                {
                    foreach (DialogueGraphEntry entry in parsed)
                        if (!string.IsNullOrWhiteSpace(entry.Id)) timedEvents[entry.Id] = entry;
                    continue;
                }
                if (!parsed.Any(entry => !string.IsNullOrWhiteSpace(entry.Pattern) && !string.IsNullOrWhiteSpace(entry.Text))) continue;
                data.Entries.AddRange(parsed);
                data.Sources.Add(Path.GetFileName(path));
            }

            data.Sources.Sort(StringComparer.OrdinalIgnoreCase);
            if (data.Entries.Count == 0)
                data.Warnings.Add("会話CSVを検出できませんでした。TalkSource/TalkCSV または Assets/Resources 内のCSV配置を確認してください。");
            BuildGraph(data, timedEvents);
            return data;
        }

        private static IEnumerable<string> DiscoverCsvPaths()
        {
            // Unity Editor's current directory is not guaranteed to be the project root.
            // Resolve from Application.dataPath so the development CSV source is found reliably.
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            string sourceRoot = Path.Combine(projectRoot, SourceRoot);
            if (Directory.Exists(sourceRoot))
                return Directory.GetFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            return AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Resources" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.IndexOf("/TalkCSV/", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith("/Dialogue/InternalDialogue.csv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static List<DialogueGraphEntry> ParseCsv(string path, List<string> warnings)
        {
            var result = new List<DialogueGraphEntry>();
            try
            {
                List<string> lines = SplitLines(File.ReadAllText(path, Encoding.UTF8));
                if (lines.Count == 0) return result;
                string[] headers = ParseLine(lines[0]);
                for (int row = 1; row < lines.Count; row++)
                {
                    string[] columns = ParseLine(lines[row]);
                    if (columns.All(string.IsNullOrWhiteSpace)) continue;
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int column = 0; column < headers.Length; column++) values[DialogueGraphEntry.NormalizeHeader(headers[column])] = column < columns.Length ? columns[column] ?? string.Empty : string.Empty;
                    var entry = new DialogueGraphEntry { SourcePath = path.Replace('\\', '/'), SourceName = Path.GetFileName(path), Line = row + 1, Values = values };
                    entry.Key = $"entry:{entry.SourcePath}:{entry.Line}";
                    result.Add(entry);
                }
            }
            catch (Exception exception) { warnings.Add($"{path}: CSVを読み取れません ({exception.Message})"); }
            return result;
        }

        private static void BuildGraph(DialogueGraphData data, Dictionary<string, DialogueGraphEntry> timedEvents)
        {
            var sequenceGroups = data.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Pattern))
                .GroupBy(BuildSequenceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.Order).ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (DialogueGraphEntry entry in data.Entries)
            {
                AddNode(data, entry.Key, DialogueGraphNodeKind.Dialogue, $"Dialogue  {entry.Id}", $"Pattern: {entry.Pattern}\n{Preview(entry.Text)}\n{entry.SourceName}:{entry.Line}", entry, BuildDetail(entry));
                string tail = entry.Key;
                if (!string.IsNullOrWhiteSpace(entry.Condition)) tail = AddStep(data, entry, tail, DialogueGraphNodeKind.Condition, "Condition", entry.Condition);
                if (!string.IsNullOrWhiteSpace(entry.EmotionChange)) tail = AddStep(data, entry, tail, DialogueGraphNodeKind.StateChange, "State change", $"{entry.EmotionChange} {entry.EmotionValue}".Trim());
                UnityEngine.Object timeline = FindTimelineAsset(entry);
                if (timeline != null)
                {
                    tail = AddStep(data, entry, tail, DialogueGraphNodeKind.Timeline, "Timeline", timeline.name);
                    data.Nodes[tail].Asset = timeline;
                    data.Nodes[tail].Detail = $"Timeline Asset: {AssetDatabase.GetAssetPath(timeline)}\n\nCSV: {entry.SourcePath}:{entry.Line}";
                }
                if (IsChoice(entry))
                {
                    string choice = AddStep(data, entry, tail, DialogueGraphNodeKind.Choice, "Choice", BuildChoiceText(entry));
                    ConnectPattern(data, sequenceGroups, entry, choice, entry.YesPattern, "YES");
                    ConnectPattern(data, sequenceGroups, entry, choice, entry.NoPattern, "NO");
                    if (string.IsNullOrWhiteSpace(entry.YesPattern) || string.IsNullOrWhiteSpace(entry.NoPattern))
                        AddDynamic(data, entry, choice, "一部の選択肢は実行時処理で解決されます。");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(entry.TimedEventKey) || timedEvents.ContainsKey(entry.ActionId))
                {
                    string eventId = !string.IsNullOrWhiteSpace(entry.TimedEventKey) ? entry.TimedEventKey : entry.ActionId;
                    if (timedEvents.TryGetValue(eventId, out DialogueGraphEntry timed))
                        tail = AddStep(data, entry, tail, DialogueGraphNodeKind.TimedEvent, "Timed event", $"{eventId}\n{Preview(timed.Text)}\nTime: {timed.Get("time_minutes")} min");
                    else
                    {
                        string missing = AddStep(data, entry, tail, DialogueGraphNodeKind.TimedEvent, "Timed event (missing)", eventId);
                        data.Edges.Add(new DialogueGraphEdge { From = missing, To = AddWarningEnd(data, entry, $"SystemTimedEvent が見つかりません: {eventId}"), IsWarning = true });
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(entry.TargetPattern)) ConnectPattern(data, sequenceGroups, entry, tail, entry.TargetPattern, "Call");
                else if (IsReturn(entry)) AddEnd(data, entry, tail, "Return", "Returns to the calling dialogue.");
                else if (sequenceGroups.TryGetValue(BuildSequenceKey(entry), out List<DialogueGraphEntry> group))
                {
                    int index = group.IndexOf(entry);
                    if (index >= 0 && index + 1 < group.Count) data.Edges.Add(new DialogueGraphEdge { From = tail, To = group[index + 1].Key, Label = "Next" });
                    else AddEnd(data, entry, tail, "Input", "Dialogue group complete / runtime returns to dialogue input.");
                }
                else if (string.IsNullOrWhiteSpace(entry.ActionId)) AddEnd(data, entry, tail, "Input", "Runtime returns to dialogue input.");
                else AddDynamic(data, entry, tail, $"Action: {entry.ActionId}\n実行時コードで解決されます。");
            }
        }

        private static void ConnectPattern(
            DialogueGraphData data,
            Dictionary<string, List<DialogueGraphEntry>> patternsBySource,
            DialogueGraphEntry source,
            string from,
            string pattern,
            string label)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return;
            string key = BuildSequenceKey(source.SourcePath, source.Id, pattern);
            if (!patternsBySource.TryGetValue(key, out List<DialogueGraphEntry> targets) || targets.Count == 0)
            {
                string warning = AddWarningEnd(data, null, $"遷移先 Pattern が存在しません: {pattern}");
                data.Edges.Add(new DialogueGraphEdge { From = from, To = warning, Label = label, IsWarning = true });
                return;
            }
            DialogueGraphEntry target = targets.FirstOrDefault(candidate => !candidate.CallOnly()) ?? targets[0];
            data.Edges.Add(new DialogueGraphEdge { From = from, To = target.Key, Label = label });
        }

        private static string BuildSourcePatternKey(string sourcePath, string pattern)
        {
            return (sourcePath ?? string.Empty) + "\u001f" + (pattern ?? string.Empty).Trim();
        }

        // Runtime routing identifies a sequence by the source label (CSV id) and PatternID.
        // Pattern values alone are reused across independent CSV rows and are never sufficient.
        private static string BuildSequenceKey(DialogueGraphEntry entry)
        {
            return BuildSequenceKey(entry.SourcePath, entry.Id, entry.Pattern);
        }

        private static string BuildSequenceKey(string sourcePath, string id, string pattern)
        {
            return BuildSourcePatternKey(sourcePath, pattern) + "\u001f" + (id ?? string.Empty).Trim();
        }

        private static bool CallOnly(this DialogueGraphEntry entry) => string.Equals(entry.Get("call_only", "callonly"), "true", StringComparison.OrdinalIgnoreCase);
        private static bool IsChoice(DialogueGraphEntry entry) => string.Equals(entry.ResponseType, "Choice", StringComparison.OrdinalIgnoreCase);
        private static bool IsReturn(DialogueGraphEntry entry) => string.Equals(entry.SpeechControl, "Return", StringComparison.OrdinalIgnoreCase);
        private static UnityEngine.Object FindTimelineAsset(DialogueGraphEntry entry)
        {
            string[] candidates = { entry.ActionId, entry.Animation };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                foreach (string guid in AssetDatabase.FindAssets($"{candidate} t:TimelineAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.Equals(Path.GetFileNameWithoutExtension(path), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                        return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                }
            }
            return null;
        }
        private static string AddStep(DialogueGraphData data, DialogueGraphEntry entry, string from, DialogueGraphNodeKind kind, string title, string body) { string id = entry.Key + ":" + kind + ":" + data.Nodes.Count; AddNode(data, id, kind, title, body, entry, body); data.Edges.Add(new DialogueGraphEdge { From = from, To = id }); return id; }
        private static void AddEnd(DialogueGraphData data, DialogueGraphEntry entry, string from, string title, string body) { string id = entry.Key + ":end"; AddNode(data, id, DialogueGraphNodeKind.End, title, body, entry, body); data.Edges.Add(new DialogueGraphEdge { From = from, To = id }); }
        private static void AddDynamic(DialogueGraphData data, DialogueGraphEntry entry, string from, string body) { string id = entry.Key + ":dynamic"; AddNode(data, id, DialogueGraphNodeKind.Dynamic, "Runtime resolved", body, entry, body); data.Edges.Add(new DialogueGraphEdge { From = from, To = id }); }
        private static string AddWarningEnd(DialogueGraphData data, DialogueGraphEntry entry, string body) { string id = "warning:" + body; AddNode(data, id, DialogueGraphNodeKind.End, "Missing reference", body, entry, body); return id; }
        private static void AddNode(DialogueGraphData data, string id, DialogueGraphNodeKind kind, string title, string body, DialogueGraphEntry entry, string detail) { if (!data.Nodes.ContainsKey(id)) data.Nodes.Add(id, new DialogueGraphNode { Id = id, Kind = kind, Title = title, Body = body, Entry = entry, Detail = detail }); }
        private static string BuildChoiceText(DialogueGraphEntry entry) => $"{Preview(entry.Get("question_ja", "question"))}\nYES: {entry.Get("choice_yes_ja", "choiceyeslabel")}\nNO: {entry.Get("choice_no_ja", "choicenolabel")}";
        private static string BuildDetail(DialogueGraphEntry entry) => $"Dialogue ID: {entry.Id}\nPattern ID: {entry.Pattern}\nSource: {entry.SourcePath}:{entry.Line}\n\nText\n{entry.Text}\n\nCondition: {entry.Condition}\nResponse: {entry.ResponseType}\nAction: {entry.ActionId}\nTarget pattern: {entry.TargetPattern}\nTimeline/Animation: {entry.Animation}\nTimed event: {entry.TimedEventKey}";
        private static string Preview(string value) { value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim(); return value.Length <= 72 ? value : value.Substring(0, 72) + "…"; }
        private static List<string> SplitLines(string text) { var result = new List<string>(); bool quoted = false; int start = 0; for (int i = 0; i < text.Length; i++) { if (text[i] == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; } quoted = !quoted; } else if (!quoted && (text[i] == '\n' || text[i] == '\r')) { result.Add(text.Substring(start, i - start)); if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; start = i + 1; } } if (start < text.Length) result.Add(text.Substring(start)); return result; }
        private static string[] ParseLine(string line) { var result = new List<string>(); var current = new StringBuilder(); bool quoted = false; for (int i = 0; i < line.Length; i++) { char c = line[i]; if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append(c); i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); } else current.Append(c); } result.Add(current.ToString()); return result.ToArray(); }
    }
}
