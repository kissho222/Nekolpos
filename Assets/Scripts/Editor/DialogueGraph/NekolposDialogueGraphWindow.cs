using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools.DialogueGraph
{
    internal sealed class NekolposDialogueGraphWindow : EditorWindow
    {
        private const float ToolbarHeight = 70f;
        private const float ScrollbarSize = 16f;
        private const float DetailWidth = 330f;
        private const float NodeWidth = 220f;
        private const float NodeHeight = 112f;
        private DialogueGraphData data;
        private readonly Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        private Vector2 pan;
        private Vector2 detailScroll;
        private string dialogueId = string.Empty;
        private string patternId = string.Empty;
        private string sourceFilter = "All";
        private bool timelineOnly;
        private int depthIndex = 2;
        private DialogueGraphNode selected;
        private string draggingNodeId;
        private Vector2 dragOffset;
        private string selectedDialogueSourcePath = string.Empty;
        private DialoguePickerOption[] dialoguePickerOptions = Array.Empty<DialoguePickerOption>();
        private static readonly int[] DepthValues = { 1, 2, 3, 5, int.MaxValue };
        private static readonly string[] DepthLabels = { "1", "2", "3", "5", "All" };

        [MenuItem("Nekolpos/Dialogue Graph")]
        private static void Open() => GetWindow<NekolposDialogueGraphWindow>("Nekolpos Dialogue Graph");

        private void OnEnable() { Refresh(); }
        private void Refresh() { data = DialogueGraphDataProvider.Load(); BuildDialogueIdOptions(); selected = null; RebuildVisibleGraph(); Repaint(); }

        private void OnGUI()
        {
            HandleDialogueNavigationShortcut();
            DrawToolbar();
            if (data == null) { EditorGUILayout.HelpBox("Loading dialogue graph…", MessageType.Info); return; }
            Rect graphRect = new Rect(0, ToolbarHeight, position.width - DetailWidth, position.height - ToolbarHeight);
            Rect detailRect = new Rect(graphRect.xMax, ToolbarHeight, DetailWidth, position.height - ToolbarHeight);
            EditorGUI.DrawRect(graphRect, EditorGUIUtility.isProSkin ? new Color(.12f, .12f, .12f) : new Color(.76f, .76f, .76f));
            DrawGraph(graphRect);
            DrawDetails(detailRect);
            HandleInput(graphRect);
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(0, 0, position.width, ToolbarHeight), EditorStyles.toolbar);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dialogue ID", GUILayout.Width(72));
            string previousDialogueId = dialogueId;
            dialogueId = GUILayout.TextField(dialogueId, EditorStyles.toolbarTextField, GUILayout.Width(130));
            GUILayout.Label("会話を選択", GUILayout.Width(58));
            if (GUILayout.Button(GetSelectedDialogueLabel(), EditorStyles.toolbarDropDown, GUILayout.Width(250)))
            {
                PopupWindow.Show(
                    GUILayoutUtility.GetLastRect(),
                    new DialoguePicker(dialoguePickerOptions, GetSourceNames(), sourceFilter, selectedDialogueSourcePath, SelectDialogue));
            }
            if (GUILayout.Button(new GUIContent("◀", "前のDialogue IDへ移動 (Shift+↑)"), EditorStyles.toolbarButton, GUILayout.Width(24)))
                SelectAdjacentDialogue(-1);
            if (GUILayout.Button(new GUIContent("▶", "次のDialogue IDへ移動 (Shift+↓)"), EditorStyles.toolbarButton, GUILayout.Width(24)))
                SelectAdjacentDialogue(1);
            if (!string.Equals(previousDialogueId, dialogueId, StringComparison.Ordinal))
            {
                selectedDialogueSourcePath = string.Empty;
                RebuildVisibleGraph();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) Refresh();
            if (GUILayout.Button("表示位置を合わせる", EditorStyles.toolbarButton, GUILayout.Width(105))) FocusVisibleGraph();
            GUILayout.Label($"{data?.Entries.Count ?? 0} dialogue rows", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pattern ID", GUILayout.Width(66));
            string previousPatternId = patternId;
            patternId = GUILayout.TextField(patternId, EditorStyles.toolbarTextField, GUILayout.Width(180));
            if (!string.Equals(previousPatternId, patternId, StringComparison.Ordinal))
            {
                selectedDialogueSourcePath = string.Empty;
                RebuildVisibleGraph();
            }
            GUILayout.Label("Source", GUILayout.Width(45));
            string[] sources = GetSourceNames();
            int sourceIndex = Mathf.Max(0, Array.FindIndex(sources, value => string.Equals(value, sourceFilter, StringComparison.OrdinalIgnoreCase)));
            string previousSource = sourceFilter;
            sourceFilter = sources[EditorGUILayout.Popup(sourceIndex, sources, EditorStyles.toolbarPopup, GUILayout.Width(170))];
            bool previousTimelineOnly = timelineOnly;
            timelineOnly = GUILayout.Toggle(timelineOnly, "Timelineあり", EditorStyles.toolbarButton, GUILayout.Width(82));
            GUILayout.Label("Depth", GUILayout.Width(40));
            int previousDepthIndex = depthIndex;
            depthIndex = EditorGUILayout.Popup(depthIndex, DepthLabels, EditorStyles.toolbarPopup, GUILayout.Width(52));
            if (!string.Equals(previousSource, sourceFilter, StringComparison.Ordinal) || previousTimelineOnly != timelineOnly || previousDepthIndex != depthIndex)
            {
                if (!string.Equals(previousSource, sourceFilter, StringComparison.Ordinal)) selectedDialogueSourcePath = string.Empty;
                RebuildVisibleGraph();
            }
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }

        private void DrawGraph(Rect graphRect)
        {
            GUI.BeginGroup(graphRect);
            Rect localGraphRect = new Rect(0, 0, graphRect.width, graphRect.height);
            HashSet<string> visible = GetVisibleNodes();
            Handles.BeginGUI();
            foreach (DialogueGraphEdge edge in data.Edges)
            {
                if (!visible.Contains(edge.From) || !visible.Contains(edge.To) || !nodeRects.TryGetValue(edge.From, out Rect from) || !nodeRects.TryGetValue(edge.To, out Rect to)) continue;
                Vector3 start = new Vector3(from.xMax, from.center.y) + (Vector3)pan;
                Vector3 end = new Vector3(to.xMin, to.center.y) + (Vector3)pan;
                Color color = edge.IsWarning ? new Color(1f, .45f, .2f) : new Color(.72f, .72f, .72f);
                Handles.DrawBezier(start, end, start + Vector3.right * 45f, end + Vector3.left * 45f, color, null, 2f);
                if (!string.IsNullOrWhiteSpace(edge.Label)) GUI.Label(new Rect((start.x + end.x) * .5f - 28, (start.y + end.y) * .5f - 10, 56, 20), edge.Label, EditorStyles.miniLabel);
            }
            Handles.EndGUI();
            foreach (string id in visible)
            {
                if (!nodeRects.TryGetValue(id, out Rect rect) || !data.Nodes.TryGetValue(id, out DialogueGraphNode node)) continue;
                rect.position += pan;
                if (!localGraphRect.Overlaps(rect)) continue;
                DrawNode(rect, node);
            }
            DrawScrollbars(localGraphRect, visible);
            if (visible.Count == 0)
            {
                string message = data.Entries.Count == 0
                    ? "会話CSVを読み込めませんでした。右側の Read warnings を確認してください。"
                    : "一致するDialogue ID / Pattern IDがありません。Source・Timelineフィルタも確認してください。";
                GUI.Label(new Rect(16, 16, localGraphRect.width - 32, 40), message, EditorStyles.helpBox);
            }
            GUI.EndGroup();
        }

        private void DrawNode(Rect rect, DialogueGraphNode node)
        {
            Color color = NodeColor(node.Kind);
            EditorGUI.DrawRect(rect, color);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect content = new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12);
            GUI.Label(new Rect(content.x, content.y, content.width, 20), node.Title, EditorStyles.boldLabel);
            GUI.Label(new Rect(content.x, content.y + 23, content.width, content.height - 23), node.Body, EditorStyles.wordWrappedMiniLabel);
            if (selected == node) GUI.Box(rect, GUIContent.none, "SelectionRect");
        }

        private void DrawDetails(Rect rect)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(.18f, .18f, .18f) : new Color(.86f, .86f, .86f));
            GUILayout.BeginArea(rect); detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            GUILayout.Label("Details", EditorStyles.boldLabel);
            if (selected == null) EditorGUILayout.HelpBox("ノードを選択すると、CSVの読み取り専用詳細を表示します。", MessageType.Info);
            else
            {
                EditorGUILayout.LabelField("Type", selected.Kind.ToString());
                EditorGUILayout.TextArea(selected.Detail ?? string.Empty, GUILayout.MinHeight(180));
                if (selected.Entry != null) EditorGUILayout.LabelField("CSV", $"{selected.Entry.SourcePath}:{selected.Entry.Line}", EditorStyles.wordWrappedMiniLabel);
                if (selected.Asset != null && GUILayout.Button("Select Timeline Asset")) { Selection.activeObject = selected.Asset; EditorGUIUtility.PingObject(selected.Asset); }
            }
            if (data.Warnings.Count > 0) { GUILayout.Space(12); GUILayout.Label("Read warnings", EditorStyles.boldLabel); foreach (string warning in data.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning); }
            EditorGUILayout.EndScrollView(); GUILayout.EndArea();
        }

        private void HandleInput(Rect graphRect)
        {
            Event evt = Event.current;
            if (!graphRect.Contains(evt.mousePosition)) return;
            Vector2 localMousePosition = evt.mousePosition - graphRect.position;
            if (evt.type == EventType.ScrollWheel)
            {
                if (evt.shift) pan.x -= evt.delta.y * 32f;
                else pan.y -= evt.delta.y * 32f;
                evt.Use(); Repaint(); return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 2) { GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); evt.Use(); return; }
            if (evt.type == EventType.MouseDrag && evt.button == 2) { pan += evt.delta; evt.Use(); Repaint(); return; }
            if (evt.type == EventType.MouseUp && evt.button == 2) { GUIUtility.hotControl = 0; evt.Use(); return; }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                foreach (KeyValuePair<string, Rect> pair in nodeRects)
                {
                    Rect rect = pair.Value; rect.position += pan;
                    if (!rect.Contains(localMousePosition) || !GetVisibleNodes().Contains(pair.Key)) continue;
                    selected = data.Nodes[pair.Key]; draggingNodeId = pair.Key; dragOffset = localMousePosition - rect.position;
                    if (evt.clickCount == 2 && selected.Asset != null) { Selection.activeObject = selected.Asset; EditorGUIUtility.PingObject(selected.Asset); }
                    evt.Use(); Repaint(); return;
                }
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && !string.IsNullOrEmpty(draggingNodeId))
            {
                Rect rect = nodeRects[draggingNodeId]; rect.position = localMousePosition - dragOffset - pan; nodeRects[draggingNodeId] = rect; DialogueGraphLayoutStore.instance.Set(draggingNodeId, rect.position); evt.Use(); Repaint(); return;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0) { draggingNodeId = null; evt.Use(); }
        }

        private HashSet<string> GetVisibleNodes()
        {
            var matches = data.Nodes.Values.Where(node => node.Entry != null && Matches(node.Entry)).Select(node => node.Id).ToList();
            if (matches.Count == 0) return new HashSet<string>();
            int maxDepth = DepthValues[depthIndex]; var result = new HashSet<string>(matches); var frontier = new Queue<string>(matches.Select(id => id + "|0"));
            var neighbors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (DialogueGraphEdge edge in data.Edges) { if (!neighbors.TryGetValue(edge.From, out List<string> forward)) neighbors[edge.From] = forward = new List<string>(); if (!neighbors.TryGetValue(edge.To, out List<string> backward)) neighbors[edge.To] = backward = new List<string>(); forward.Add(edge.To); backward.Add(edge.From); }
            while (frontier.Count > 0) { string[] item = frontier.Dequeue().Split('|'); int distance = int.Parse(item[1]); if (distance >= maxDepth || !neighbors.TryGetValue(item[0], out List<string> next)) continue; foreach (string id in next) if (result.Add(id)) frontier.Enqueue(id + "|" + (distance + 1)); }
            return result;
        }

        private bool Matches(DialogueGraphEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(selectedDialogueSourcePath))
            {
                return string.Equals(entry.SourcePath, selectedDialogueSourcePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Id, dialogueId, StringComparison.OrdinalIgnoreCase)
                    && (!timelineOnly || !string.IsNullOrWhiteSpace(entry.Animation) || !string.IsNullOrWhiteSpace(entry.TimedEventKey));
            }
            if (!string.Equals(sourceFilter, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.SourceName, sourceFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (timelineOnly && string.IsNullOrWhiteSpace(entry.Animation) && string.IsNullOrWhiteSpace(entry.TimedEventKey)) return false;
            if (string.IsNullOrWhiteSpace(dialogueId) && string.IsNullOrWhiteSpace(patternId)) return false;
            return (!string.IsNullOrWhiteSpace(dialogueId) && entry.Id.IndexOf(dialogueId, StringComparison.OrdinalIgnoreCase) >= 0) || (!string.IsNullOrWhiteSpace(patternId) && entry.Pattern.IndexOf(patternId, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RebuildVisibleGraph()
        {
            if (data == null) return;
            nodeRects.Clear();
            HashSet<string> visible = GetVisibleNodes();
            if (visible.Count == 0) return;
            var ranks = visible.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            ApplyPatternOrderRanks(visible, ranks);
            // A bounded relaxation gives readable left-to-right flow without treating cyclic dialogue routes as an error.
            for (int pass = 0; pass < 8; pass++)
                foreach (DialogueGraphEdge edge in data.Edges)
                    if (visible.Contains(edge.From) && visible.Contains(edge.To)) ranks[edge.To] = Mathf.Max(ranks[edge.To], ranks[edge.From] + 1);
            var lanes = new Dictionary<int, int>();
            foreach (string nodeId in visible.OrderBy(id => ranks[id]).ThenBy(id => id, StringComparer.Ordinal))
            {
                int rank = ranks[nodeId];
                int lane = lanes.TryGetValue(rank, out int currentLane) ? currentLane : 0;
                lanes[rank] = lane + 1;
                Vector2 fallback = new Vector2(30 + rank * 270, 30 + lane * 145);
                Vector2 position = DialogueGraphLayoutStore.instance.TryGet(nodeId, out Vector2 saved) ? saved : fallback;
                nodeRects[nodeId] = new Rect(position, new Vector2(NodeWidth, NodeHeight));
            }
            FocusVisibleGraph();
        }

        private void ApplyPatternOrderRanks(HashSet<string> visible, Dictionary<string, int> ranks)
        {
            IEnumerable<DialogueGraphNode> dialogueNodes = visible
                .Where(id => data.Nodes.TryGetValue(id, out DialogueGraphNode node)
                    && node.Kind == DialogueGraphNodeKind.Dialogue
                    && node.Entry != null
                    && !string.IsNullOrWhiteSpace(node.Entry.Pattern))
                .Select(id => data.Nodes[id]);

            foreach (IGrouping<string, DialogueGraphNode> pattern in dialogueNodes.GroupBy(
                node => node.Entry.SourcePath + "\u001f" + node.Entry.Pattern,
                StringComparer.OrdinalIgnoreCase))
            {
                int rank = 0;
                foreach (DialogueGraphNode node in pattern.OrderBy(node => node.Entry.Order).ThenBy(node => node.Entry.Line))
                {
                    ranks[node.Id] = rank;
                    rank++;
                }
            }
        }

        private void BuildDialogueIdOptions()
        {
            if (data == null)
            {
                dialoguePickerOptions = Array.Empty<DialoguePickerOption>();
                return;
            }

            dialoguePickerOptions = data.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .GroupBy(entry => entry.SourcePath + "\u001f" + entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => ParseDialogueIdSortKey(entry.Id))
                .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new DialoguePickerOption(entry.Id, entry.SourceName, entry.SourcePath, BuildDialogueLabel(entry)))
                .ToArray();
        }

        private string[] GetSourceNames() => new[] { "All" }
            .Concat(data?.Sources ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static long ParseDialogueIdSortKey(string id)
        {
            return long.TryParse(id, out long numericId) ? numericId : long.MaxValue;
        }

        private static string BuildDialogueLabel(DialogueGraphEntry entry)
        {
            string input = string.IsNullOrWhiteSpace(entry.Input) ? entry.Pattern : entry.Input;
            input = (input ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (input.Length > 42) input = input.Substring(0, 42) + "…";
            return string.IsNullOrWhiteSpace(input) ? entry.Id : $"{entry.Id} | {input}";
        }

        private string GetSelectedDialogueLabel()
        {
            DialoguePickerOption option = dialoguePickerOptions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, dialogueId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.SourcePath, selectedDialogueSourcePath, StringComparison.OrdinalIgnoreCase));
            return option != null ? option.Label : "(会話IDを選択)";
        }

        private void SelectDialogue(DialoguePickerOption option)
        {
            if (option == null) return;
            dialogueId = option.Id;
            sourceFilter = option.SourceName;
            selectedDialogueSourcePath = option.SourcePath;
            patternId = string.Empty;
            RebuildVisibleGraph();
        }

        private void HandleDialogueNavigationShortcut()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown || !evt.shift || EditorGUIUtility.editingTextField) return;
            if (evt.keyCode == KeyCode.UpArrow) SelectAdjacentDialogue(-1);
            else if (evt.keyCode == KeyCode.DownArrow) SelectAdjacentDialogue(1);
            else return;
            evt.Use();
        }

        private void SelectAdjacentDialogue(int direction)
        {
            DialoguePickerOption[] sourceOptions = dialoguePickerOptions
                .Where(option => string.Equals(option.SourceName, sourceFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (sourceOptions.Length == 0) return;
            int currentIndex = Array.FindIndex(sourceOptions, option =>
                string.Equals(option.Id, dialogueId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(option.SourcePath, selectedDialogueSourcePath, StringComparison.OrdinalIgnoreCase));
            int targetIndex;
            if (currentIndex < 0) targetIndex = direction > 0 ? 0 : sourceOptions.Length - 1;
            else targetIndex = Mathf.Clamp(currentIndex + direction, 0, sourceOptions.Length - 1);
            SelectDialogue(sourceOptions[targetIndex]);
        }

        private void FocusVisibleGraph()
        {
            HashSet<string> visible = GetVisibleNodes();
            if (visible.Count == 0 || nodeRects.Count == 0) return;
            bool hasBounds = false;
            Rect bounds = default;
            foreach (string id in visible)
            {
                if (!nodeRects.TryGetValue(id, out Rect rect)) continue;
                if (!hasBounds) { bounds = rect; hasBounds = true; }
                else bounds = Encompass(bounds, rect);
            }
            if (hasBounds) pan = new Vector2(28f - bounds.xMin, 28f - bounds.yMin);
            Repaint();
        }

        private void DrawScrollbars(Rect graphRect, HashSet<string> visible)
        {
            if (visible.Count == 0 || nodeRects.Count == 0) return;
            bool hasBounds = false;
            Rect bounds = default;
            foreach (string id in visible)
            {
                if (!nodeRects.TryGetValue(id, out Rect rect)) continue;
                if (!hasBounds) { bounds = rect; hasBounds = true; }
                else bounds = Encompass(bounds, rect);
            }
            if (!hasBounds) return;
            float minX = bounds.xMin - 40f;
            float maxX = Mathf.Max(minX + graphRect.width, bounds.xMax + 40f);
            float minY = bounds.yMin - 40f;
            float maxY = Mathf.Max(minY + graphRect.height, bounds.yMax + 40f);
            float viewX = GUI.HorizontalScrollbar(new Rect(graphRect.x, graphRect.yMax - ScrollbarSize, graphRect.width - ScrollbarSize, ScrollbarSize), -pan.x, Mathf.Min(graphRect.width, maxX - minX), minX, maxX);
            float viewY = GUI.VerticalScrollbar(new Rect(graphRect.xMax - ScrollbarSize, graphRect.y, ScrollbarSize, graphRect.height - ScrollbarSize), -pan.y, Mathf.Min(graphRect.height, maxY - minY), minY, maxY);
            pan = new Vector2(-viewX, -viewY);
        }

        private static Rect Encompass(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private sealed class DialoguePicker : PopupWindowContent
        {
            private readonly DialoguePickerOption[] options;
            private readonly string[] sources;
            private readonly string selectedSourcePath;
            private readonly Action<DialoguePickerOption> onSelected;
            private Vector2 scrollPosition;
            private string search = string.Empty;
            private string source;

            public DialoguePicker(
                DialoguePickerOption[] options,
                string[] sources,
                string selectedSource,
                string selectedSourcePath,
                Action<DialoguePickerOption> onSelected)
            {
                this.options = options ?? Array.Empty<DialoguePickerOption>();
                this.sources = sources ?? new[] { "All" };
                source = selectedSource ?? "All";
                this.selectedSourcePath = selectedSourcePath ?? string.Empty;
                this.onSelected = onSelected;
            }

            public override Vector2 GetWindowSize() => new Vector2(440f, 430f);

            public override void OnGUI(Rect rect)
            {
                GUILayout.Label("会話を選択", EditorStyles.boldLabel);
                GUILayout.Label("Sourceを選び、IDまたはinputで絞り込みます。ホイールまたは右のスクロールバーで移動します。", EditorStyles.miniLabel);
                int sourceIndex = Mathf.Max(0, Array.FindIndex(sources, candidate => string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase)));
                source = sources[EditorGUILayout.Popup(sourceIndex, sources, EditorStyles.toolbarPopup)];
                if (string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
                {
                    EditorGUILayout.HelpBox("候補はSourceごとに表示します。先にSourceを選択してください。", MessageType.Info);
                    return;
                }
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                string query = (search ?? string.Empty).Trim();
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, true, true);
                bool found = false;
                foreach (DialoguePickerOption option in options)
                {
                    if (!string.Equals(option.SourceName, source, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(query) &&
                        option.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    found = true;
                    GUIStyle style = new GUIStyle(string.Equals(option.SourcePath, selectedSourcePath, StringComparison.OrdinalIgnoreCase)
                        ? EditorStyles.toolbarButton
                        : EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleLeft
                    };
                    if (GUILayout.Button(option.Label, style, GUILayout.Height(24f)))
                    {
                        onSelected?.Invoke(option);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                }
                if (!found) EditorGUILayout.HelpBox("一致する会話がありません。", MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }

        private sealed class DialoguePickerOption
        {
            public readonly string Id;
            public readonly string SourceName;
            public readonly string SourcePath;
            public readonly string Label;

            public DialoguePickerOption(string id, string sourceName, string sourcePath, string label)
            {
                Id = id;
                SourceName = sourceName;
                SourcePath = sourcePath;
                Label = label;
            }
        }

        private static Color NodeColor(DialogueGraphNodeKind kind) => kind switch
        {
            DialogueGraphNodeKind.Dialogue => new Color(.22f, .42f, .68f), DialogueGraphNodeKind.Condition => new Color(.58f, .42f, .18f), DialogueGraphNodeKind.Choice => new Color(.38f, .56f, .2f), DialogueGraphNodeKind.Timeline => new Color(.48f, .28f, .64f), DialogueGraphNodeKind.StateChange => new Color(.16f, .56f, .55f), DialogueGraphNodeKind.TimedEvent => new Color(.62f, .3f, .42f), DialogueGraphNodeKind.End => new Color(.36f, .36f, .36f), _ => new Color(.3f, .3f, .3f)
        };
    }

    [FilePath("Library/NekolposDialogueGraphLayout.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DialogueGraphLayoutStore : ScriptableSingleton<DialogueGraphLayoutStore>
    {
        [Serializable] private struct Entry { public string id; public Vector2 position; }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public bool TryGet(string id, out Vector2 position) { int index = entries.FindIndex(entry => entry.id == id); position = index >= 0 ? entries[index].position : default; return index >= 0; }
        public void Set(string id, Vector2 position) { int index = entries.FindIndex(entry => entry.id == id); if (index >= 0) entries[index] = new Entry { id = id, position = position }; else entries.Add(new Entry { id = id, position = position }); Save(true); }
    }
}
