using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Nekolpos.EditorTools
{
    public partial class DialoguePreviewWindow
    {
        [SerializeField] private bool showOtherLanguages, showBehaviour, showBranches, showDevelopment, showConditionExpression, showTeaching;
        [SerializeField] private string activeRelatedTab = "時間";
        [SerializeField] private bool showTimeDiary, showTopics, showReadings;
        private bool drawingRelatedSection;
        [SerializeField] private Vector2 relatedScroll;
        private readonly List<string> inputFocusNames = new List<string>();
        private string requestedInputFocus;
        internal string lastFocusedInput;
        private int inputFocusOrdinal;
        private T FocusInput<T>(T value, string explicitName = null)
        {
            string name = explicitName ?? "DialogueInput_" + inputFocusOrdinal;
            inputFocusOrdinal++;
            GUI.SetNextControlName(name);
            if (GUI.enabled) inputFocusNames.Add(name);
            return value;
        }
        internal static int NextFocusIndex(int current, int count, bool reverse) => count <= 0 ? -1 :
            current < 0 ? reverse ? count - 1 : 0 : (current + (reverse ? count - 1 : 1)) % count;
        private void BeginInputFocusFrame()
        {
            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Tab)
            {
                int next = NextFocusIndex(inputFocusNames.IndexOf(GUI.GetNameOfFocusedControl()), inputFocusNames.Count, current.shift);
                requestedInputFocus = next >= 0 ? inputFocusNames[next] : null;
                current.Use();
            }
            inputFocusNames.Clear(); inputFocusOrdinal = 0;
        }
        private void EndInputFocusFrame()
        {
            if (lastFocusedInput != GUI.GetNameOfFocusedControl()) ValidateAuthoringRegexes();
            if (requestedInputFocus == null) { lastFocusedInput = GUI.GetNameOfFocusedControl(); return; }
            if (Event.current.type != EventType.Repaint) { Repaint(); return; }
            if (inputFocusNames.Contains(requestedInputFocus))
            {
                GUI.FocusControl(requestedInputFocus);
                EditorGUIUtility.editingTextField = true;
            }
            lastFocusedInput = GUI.GetNameOfFocusedControl();
            requestedInputFocus = null;
        }
        private const string LanguagesKey = "Nekolpos.DialogueAuthoring.OtherLanguages";
        private bool regexAuthoringValidationDirty = true;
        private readonly List<string> regexAuthoringErrors = new List<string>();
        private readonly List<string> visibleRegexAuthoringErrors = new List<string>();
        private readonly Dictionary<string, DialogueEntry> regexAuthoringTargets = new Dictionary<string, DialogueEntry>();
        private readonly Dictionary<string, DialogueEntry> visibleRegexAuthoringTargets = new Dictionary<string, DialogueEntry>();
        private readonly Dictionary<string, string> regexSyntaxCache = new Dictionary<string, string>();

        private void OnLostFocus()
        {
            ValidateAuthoringRegexes();
            Repaint();
        }

        private static string GetRegexSyntaxError(string pattern, bool japanese)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                _ = new global::System.Text.RegularExpressions.Regex(pattern, global::System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
                if (japanese)
                    _ = new global::System.Text.RegularExpressions.Regex(Backgammon.Conversation.JapaneseTextNormalizer.NormalizeToken(pattern), global::System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
                return null;
            }
            catch (ArgumentException exception) { return exception.Message; }
        }

        private void ValidateAuthoringRegexes()
        {
            if (!regexAuthoringValidationDirty) return;
            regexAuthoringErrors.Clear();
            regexAuthoringTargets.Clear();
            var seen = new HashSet<string>();
            foreach (var entry in dataSet?.Entries ?? new List<DialogueEntry>())
            {
                if (entry == null) continue;
                string[] patterns = { entry.RegexPattern, entry.RegexPatternChinese, entry.RegexPatternEnglish };
                string[] languages = { "JP", "中国語", "英語" };
                for (int i = 0; i < patterns.Length; i++)
                {
                    string key = languages[i] + "\u001f" + patterns[i];
                    if (!regexSyntaxCache.TryGetValue(key, out string error))
                    {
                        error = GetRegexSyntaxError(patterns[i], i == 0);
                        regexSyntaxCache[key] = error;
                    }
                    if (string.IsNullOrEmpty(error)) continue;
                    string message = $"ID {entry.InternalId} | {entry.RegexId} | {languages[i]}: {patterns[i]}\n{error}";
                    if (seen.Add(message))
                    {
                        regexAuthoringErrors.Add(message);
                        regexAuthoringTargets[message] = entry;
                    }
                }
            }
            regexAuthoringValidationDirty = false;
        }

        private void UpdateRegexAuthoringSnapshot()
        {
            if (Event.current.type != EventType.Layout) return;
            if (!EditorGUIUtility.editingTextField) ValidateAuthoringRegexes();
            visibleRegexAuthoringErrors.Clear();
            visibleRegexAuthoringErrors.AddRange(regexAuthoringErrors);
            visibleRegexAuthoringTargets.Clear();
            foreach (var target in regexAuthoringTargets) visibleRegexAuthoringTargets[target.Key] = target.Value;
        }

        private bool OpenRegexAuthoringDiagnostic(DialogueEntry entry)
        {
            for (int i = 0; i < patternGroups.Count; i++)
            {
                int line = patternGroups[i].Entries.IndexOf(entry);
                if (line < 0) continue;
                SelectCurrentCsvItem(i);
                currentSequenceIndex = line;
                selectedTab = 0;
                showDetails = true;
                SyncStateFromCurrentSelectionIfNeeded(true);
                Repaint();
                return true;
            }
            return false;
        }

        internal Rect footerRect;
        internal float primaryContentBottom;
        internal Rect branchesRect, behaviourRect, developmentRect;
        internal readonly Dictionary<string, Rect> relatedHeaderRects = new Dictionary<string, Rect>();
        private string lastAuthoringViewJson;
        private Authoring.DialogueAuthoringProject authorDiagnostics;
        private string authorDiagnosticsSource;
        private bool showAuthorDiagnostics;
        internal void ReloadAuthorDiagnostics()
        {
            authorDiagnostics = Authoring.DialogueLegacyImporter.ImportFile(dataSet.SourcePath);
            authorDiagnosticsSource = dataSet.SourcePath;
        }
        private void DrawAuthorDiagnostics()
        {
            bool normalManuscript = string.Equals(Path.GetFileName(dataSet?.SourcePath), "DialoguePreview.csv", StringComparison.OrdinalIgnoreCase);
            string label = "作者確認・診断";
            if (visibleRegexAuthoringErrors.Count > 0) label += $"（正規表現エラー {visibleRegexAuthoringErrors.Count}件）";
            showAuthorDiagnostics = EditorGUILayout.Foldout(showAuthorDiagnostics, label, true);
            if (!showAuthorDiagnostics) return;
            foreach (string error in visibleRegexAuthoringErrors)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                    if (GUILayout.Button("この会話を開く") && visibleRegexAuthoringTargets.TryGetValue(error, out var entry))
                    {
                        if (OpenRegexAuthoringDiagnostic(entry)) GUIUtility.ExitGUI();
                    }
                }
            }
            if (!normalManuscript)
            {
                if (visibleRegexAuthoringErrors.Count == 0) EditorGUILayout.LabelField("正規表現の構文エラーはありません。");
                return;
            }
            if (authorDiagnostics == null || authorDiagnosticsSource != dataSet.SourcePath) ReloadAuthorDiagnostics();
            EditorGUILayout.HelpBox("以下は保存済み正本の診断です。正規表現エラーには未保存の編集も含みます。", MessageType.Info);
            if (GUILayout.Button("診断を再読込")) ReloadAuthorDiagnostics();
            foreach (var diagnostic in authorDiagnostics.diagnostics.Where(d => d.severity != Authoring.DiagnosticSeverity.Info))
                Authoring.DialogueAuthoringPreviewWindow.DrawDiagnostic(authorDiagnostics.source, diagnostic);
        }
        internal static bool OpenDiagnostic(string source, int sourceRow)
        {
            var window = GetWindow<DialoguePreviewWindow>("Dialogue Preview");
            if (!window.ConfirmDiscardChanges()) return false;
            window.ApplyLoadedDataSet(DialogueDataLoader.LoadFromPath(source));
            window.RebuildPatternCache();
            var entry = window.dataSet.Entries.FirstOrDefault(e => e.SourceLineNumber == sourceRow);
            if (entry == null) return false;
            window.OpenRegexMatchResult(new RegexMatchDisplayItem { RegexId = entry.RegexId, Pattern = entry.Pattern, SourceLineNumber = sourceRow });
            window.Show(); window.Focus(); return true;
        }
        private List<DialogueAuthoringProfile> manuscriptProfiles;
        private readonly HashSet<string> expandedNotices = new HashSet<string>();
        private DialogueAuthoringProfile CurrentProfile => DialogueAuthoringProfile.Resolve(dataSet?.SourcePath) ?? manuscriptProfiles?.FirstOrDefault(p => p.FileName == Path.GetFileName(dataSet?.SourcePath));
        internal static readonly string[] AuthoringCategoryKeys = { "", "A", "B", "C", "D", "E", "F", "G", "H", "I" };
        internal static readonly string[] AuthoringCategoryLabels = { "なし", "A: 季節", "B: 天気", "C: 空腹", "D: 時間帯", "E: 性別", "F: 発情", "G: 敗北", "H: 現在位置", "I: 心理" };
        internal static string GetAuthoringConditionCategory(string option)
        {
            if (string.IsNullOrEmpty(option)) return "";
            if (option.StartsWith("PsychologyState ")) return "I";
            if (option.StartsWith("H: ")) return "H";
            if (EmotionChangeTypeOptions.Any(metric => metric.Length > 0 && option.StartsWith(metric + " "))) return "I";
            return GetConditionCategoryLabel(option);
        }
        internal static string[] ConditionValuesForCategory(string category) => SharedConditionOptions.Where(value => GetAuthoringConditionCategory(value) == category).ToArray();
        internal static string[] NewPsychologyNumericConditions() => EmotionChangeTypeOptions.Where(value => value.Length > 0)
            .SelectMany(value => new[] { value + " >= 70", value + " < 70", value + " == 100" }).ToArray();

        internal static string[] NewPsychologyStateConditions()
        {
            var states = EmotionChangeTypeOptions.Where(value => value.Length > 0).Select(value => value.ToUpperInvariant()).ToArray();
            var result = new List<string>();
            foreach (string value in states) result.Add("PsychologyState == \"" + value + "\"");
            for (int a = 0; a < states.Length; a++)
                for (int b = a + 1; b < states.Length; b++)
                {
                    var pair = new[] { states[a], states[b] }; Array.Sort(pair, StringComparer.Ordinal);
                    result.Add("PsychologyState == \"" + string.Join("_", pair) + "\"");
                }
            return result.ToArray();
        }

        private readonly int[] psychologyConditionKinds = new int[4];

        private bool DrawPsychologyConditionSelection(int slot, ref string selection, ref bool negated)
        {
            string current = string.IsNullOrEmpty(selection) ? "" : BuildConditionClause(selection, negated);
            bool isState = string.IsNullOrEmpty(selection) ? psychologyConditionKinds[slot] == 1 : selection.StartsWith("PsychologyState ", StringComparison.Ordinal);
            int kind = EditorGUILayout.Popup(FocusInput(isState ? 1 : 0), new[] { "心理値", "代表心理" }, GUILayout.Width(90));
            psychologyConditionKinds[slot] = kind;
            string[] candidates = kind == 0 ? NewPsychologyNumericConditions() : NewPsychologyStateConditions();
            var values = new List<string> { "" }; values.AddRange(candidates);
            var labels = new List<string> { "なし" };
            labels.AddRange(candidates.Select(value => value.StartsWith("PsychologyState ") ? ConditionValueLabel(value, false) :
                PsychologyConditionLabel(value.Split(' ')[0]) + (value.EndsWith("< 70") ? " 70未満" : value.EndsWith("== 100") ? " 100" : " 70以上")));
            // Keep a removed legacy choice visible only for the row that already uses it.
            if (kind == (isState ? 1 : 0) && current.Length > 0 && !values.Contains(current))
            { values.Add(current); labels.Add("旧条件: " + ConditionValueLabel(selection, negated)); }
            int index = Mathf.Max(0, values.IndexOf(current));
            int next = EditorGUILayout.Popup(FocusInput(index), labels.ToArray());
            if (kind == (isState ? 1 : 0) && next == index) return false;
            if (next == 0) { selection = null; negated = false; }
            else if (TryNormalizeConditionOption(values[next], out string option, out bool inverse))
            { selection = option; negated = inverse; }
            return true;
        }

        private static string PsychologyConditionLabel(string key)
        {
            if (string.Equals(key, "NORMAL", StringComparison.OrdinalIgnoreCase)) return "通常";
            if (string.Equals(key, "MATING", StringComparison.OrdinalIgnoreCase)) return "発情";
            int index = Array.FindIndex(EmotionChangeTypeOptions, value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? EmotionChangeTypeLabels[index] : key;
        }

        internal static string ConditionValueLabel(string value, bool negated)
        {
            if (value.StartsWith("PsychologyState ")) return string.Join("・", value.Substring("PsychologyState == ".Length).Trim('"').Split('_').Select(PsychologyConditionLabel)) + (negated ? "以外" : "");
            int metric = Array.FindIndex(EmotionChangeTypeOptions, key => key.Length > 0 && value.StartsWith(key + " "));
            if (metric >= 0)
            {
                string clause = BuildConditionClause(value, negated);
                string[] tokens = clause.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string suffix = tokens[1] switch { ">=" => "以上", "<" => "未満", "==" => "", "!=" => "以外", _ => "" };
                return EmotionChangeTypeLabels[metric] + " " + tokens[2] + suffix;
            }
            var labels = new Dictionary<string, string> {
                ["Winter"]="冬", ["Fall"]="秋", ["Summer"]="夏", ["Spring"]="春", ["Snow"]="雪", ["Sunny"]="晴れ", ["Rain"]="雨",
                ["CatHungry"]="猫が空腹", ["PlayerHungry"]="プレイヤーが空腹", ["Night"]="夜", ["Evening"]="夕方", ["Day"]="昼", ["Morning"]="朝",
                ["Male"]="男性", ["Female"]="女性", ["During estrus"]="発情中", ["PlayerDefeatedByNekomata"]="猫による敗北", ["PlayerDefeatedByPredation"]="捕食による敗北",
                ["PlayerDefeatedByAnger"]="怒りによる敗北", ["PlayerDefeatedByAccident"]="事故による敗北", ["H: Table"]="ちゃぶ台", ["H: Desk"]="作業台", ["H: Bathroom"]="浴室", ["H: Bed"]="ベッド" };
            string label = labels.TryGetValue(value, out string found) ? found : value;
            return label + (negated ? "以外" : "");
        }
        private void DrawManuscriptToolbar()
        {
            manuscriptProfiles ??= DialogueAuthoringProfile.Available();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("原稿", GUILayout.Width(34));
                int current = manuscriptProfiles.FindIndex(p => p.FileName == Path.GetFileName(dataSet.SourcePath));
                string[] names = new[] { "原稿を選択" }.Concat(manuscriptProfiles.Select(p => p.Name)).ToArray();
                int next = EditorGUILayout.Popup(FocusInput(current + 1), names, GUILayout.Width(240));
                if (next > 0 && next - 1 != current) { LoadCsv(manuscriptProfiles[next - 1].Path); GUIUtility.ExitGUI(); }
                DrawCurrentCsvItemSelector();
                bool languages = GUILayout.Toggle(FocusInput(showOtherLanguages), "英語・中国語を表示", GUILayout.Width(160));
                if (languages != showOtherLanguages) { showOtherLanguages = languages; EditorPrefs.SetBool(LanguagesKey, languages); }
            }
        }
        private List<int> GetCurrentCsvItemIndices()
        {
            var indices = new List<int>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < patternGroups.Count; i++)
            {
                var entry = GetRepresentativeEntry(patternGroups[i]);
                if (entry != null && keys.Add((entry.InternalId ?? "") + "\u001f" + (entry.RegexId ?? ""))) indices.Add(i);
            }
            return indices;
        }

        private static string BuildCsvItemLabel(DialogueEntry entry)
        {
            string input = (entry.GetAdditionalValue("input") ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return $"{entry.InternalId} | {entry.RegexId} | {input}";
        }

        private void SelectCurrentCsvItem(int patternIndex)
        {
            if (patternIndex < 0 || patternIndex >= patternGroups.Count) return;
            ClearActiveTextFieldFocus();
            CacheStateForCurrentSelection();
            currentPatternIndex = patternIndex;
            currentSequenceIndex = 0;
            sequenceSelectorScroll = Vector2.zero;
            SyncStateFromCurrentSelectionIfNeeded(true);
            Repaint();
        }

        private void DrawCurrentCsvItemSelector()
        {
            var indices = GetCurrentCsvItemIndices();
            var selected = GetRepresentativeEntry(GetCurrentGroup());
            int current = indices.FindIndex(index =>
            {
                var entry = GetRepresentativeEntry(patternGroups[index]);
                return selected != null && entry.InternalId == selected.InternalId && entry.RegexId == selected.RegexId;
            });
            using (new EditorGUI.DisabledScope(indices.Count == 0))
            {
                string[] labels = indices.Select(index => BuildCsvItemLabel(GetRepresentativeEntry(patternGroups[index]))).ToArray();
                if (labels.Length == 0) labels = new[] { "項目なし" };
                Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(labels[Mathf.Max(0, current)]), EditorStyles.popup, GUILayout.MinWidth(180), GUILayout.ExpandWidth(true));
                if (GUI.Button(buttonRect, labels[Mathf.Max(0, current)], EditorStyles.popup))
                {
                    ClearActiveTextFieldFocus();
                    var source = dataSet;
                    PopupWindow.Show(buttonRect, new CsvItemListPopup(labels, current, buttonRect.width, index =>
                    {
                        if (dataSet == source && index >= 0 && index < indices.Count)
                            SelectCurrentCsvItem(indices[index]);
                    }));
                }
            }
        }

        internal sealed class CsvItemListPopup : PopupWindowContent
        {
            private const float RowHeight = 24f;
            private readonly string[] labels;
            private readonly int selected;
            private readonly float width;
            private readonly Action<int> select;
            private Vector2 scroll;

            internal CsvItemListPopup(string[] labels, int selected, float width, Action<int> select)
            {
                this.labels = labels;
                this.selected = selected;
                this.width = Mathf.Max(360f, width);
                this.select = select;
            }

            public override Vector2 GetWindowSize() => new Vector2(width, Mathf.Min(360f, labels.Length * RowHeight + 8f));

            internal static float InitialScroll(int selected, int count, float viewportHeight)
                => Mathf.Clamp((selected + 0.5f) * RowHeight - viewportHeight * 0.5f, 0f, Mathf.Max(0f, count * RowHeight - viewportHeight));

            public override void OnOpen()
            {
                scroll.y = InitialScroll(selected, labels.Length, GetWindowSize().y - 8f);
            }

            public override void OnGUI(Rect rect)
            {
                Rect viewport = new Rect(4f, 4f, rect.width - 8f, rect.height - 8f);
                Rect content = new Rect(0f, 0f, viewport.width - 18f, labels.Length * RowHeight);
                // A real scroll view supports wheel scrolling, scrollbar dragging and track clicks.
                scroll = GUI.BeginScrollView(viewport, scroll, content);
                int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight));
                int last = Mathf.Min(labels.Length, Mathf.CeilToInt((scroll.y + viewport.height) / RowHeight));
                for (int i = first; i < last; i++)
                {
                    Rect row = new Rect(0f, i * RowHeight, content.width, RowHeight);
                    if (i == selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.72f, 0.35f));
                    if (GUI.Button(row, new GUIContent((i == selected ? "✓ " : "   ") + labels[i], labels[i]), EditorStyles.label))
                    {
                        select(i);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.EndScrollView();
            }
        }

        internal static readonly string[] PsychologyTimingValues = { "MORNING", "DAY", "EVENING", "NIGHT", "BEFORE_SLEEP" };
        private static readonly string[] PsychologyTimingLabels = { "朝開始時", "昼開始時", "夕開始時", "夜開始時", "就寝前" };

        internal static string PsychologyContextLabel(string context)
        {
            switch (context)
            {
                case "MORNING": return "朝開始時";
                case "DAY": return "昼開始時";
                case "EVENING": return "夕開始時";
                case "NIGHT": return "夜開始時";
                case "BEFORE_SLEEP": return "就寝前";
                case "FEELING": return "現在の気持ち";
                case "DIARY_REACTION": return "日記への反応";
                case "MAX_EVENT": return "心理値最大時";
                case "COMPOSITE": return "複合心理（タイミング未分類）";
                case "ESTRUS": return "発情期";
                case "SPECIAL_PATTERN": return "特殊反応";
                case "": return "未設定";
                default: return "その他（" + context + "）";
            }
        }

        private void DrawPsychologyTimingSelector(DialoguePatternGroup group)
        {
            if (CurrentProfile?.FileName != "PsychologyReactionDialogue.csv") return;
            string current = GetRepresentativeEntry(group)?.GetAdditionalValue("context") ?? "";
            var values = new List<string> { current };
            var labels = new List<string> { PsychologyContextLabel(current) };
            for (int i = 0; i < PsychologyTimingValues.Length; i++)
                if (PsychologyTimingValues[i] != current) { values.Add(PsychologyTimingValues[i]); labels.Add(PsychologyTimingLabels[i]); }
            foreach (string existing in dataSet.Entries.Select(e => e.GetAdditionalValue("context") ?? "").Distinct())
                if (!values.Contains(existing)) { values.Add(existing); labels.Add(PsychologyContextLabel(existing)); }
            int currentTiming = Array.IndexOf(PsychologyTimingValues, current);
            if (currentTiming >= 0) labels[0] = PsychologyTimingLabels[currentTiming];
            int next = EditorGUILayout.Popup(FocusInput("反応タイミング"), 0, labels.ToArray());
            if (next > 0) ApplyPsychologyTiming(group, values[next]);
        }

        private void ApplyPsychologyTiming(DialoguePatternGroup group, string context)
        {
            if (CurrentProfile?.FileName != "PsychologyReactionDialogue.csv" || group == null) return;
            foreach (var entry in group.Entries) entry.SetAdditionalValue("context", context);
            hasPendingChanges = true;
        }

        private void DrawDevelopmentInfo(DialoguePatternGroup group)
        {
            showDevelopment = EditorGUILayout.Foldout(showDevelopment, "開発情報", true);
            if (Event.current.type == EventType.Repaint) developmentRect = GUILayoutUtility.GetLastRect();
            if (!showDevelopment) return;
            var entry = GetCurrentEntry(group);
            EditorGUILayout.SelectableLabel("原稿: " + dataSet.SourcePath, GUILayout.Height(20));
            EditorGUILayout.SelectableLabel("保存先: " + dataSet.SavePath, GUILayout.Height(20));
            EditorGUILayout.SelectableLabel("Editor DialogueID: " + entry.EditorDialogueId, GUILayout.Height(20));
            EditorGUILayout.LabelField("既存グループキー", DialogueAuthoringIdentity.GroupKey(entry, dataSet.Entries.IndexOf(entry)));
            EditorGUILayout.SelectableLabel("SourceKey: " + TopicSourceKey(entry, dataSet.Entries.IndexOf(entry)), GUILayout.Height(20));
            EditorGUILayout.SelectableLabel("内部ID: " + entry.InternalId, GUILayout.Height(20));
            EditorGUILayout.LabelField("Runtime参照", "Action: " + entry.ActionId + " / TimedEvent: " + entry.TimedEventKey);
            if (timedEvents != null)
                foreach (string key in new[] { entry.ActionId, entry.TimedEventKey }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                    foreach (var row in timedEvents.Find("key", key)) EditorGUILayout.SelectableLabel("日記ID: " + TalkTopicMasterCsvUtility.ReadValue(row, "diary_id"), GUILayout.Height(20));
            if (!string.IsNullOrEmpty(lastBackupPath)) EditorGUILayout.SelectableLabel("バックアップ: " + lastBackupPath, GUILayout.Height(20));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("その他のCSVを開く")) { string path = EditorUtility.OpenFilePanel("原稿CSV", DialogueDataLoader.GetTalkCsvDirectory(), "csv"); if (!string.IsNullOrEmpty(path)) LoadCsv(path); }
                if (GUILayout.Button("Resources一覧")) ShowResourceCsvMenu();
            }
            if (global::System.IO.Path.GetFileName(dataSet.SourcePath).Equals("DialoguePreview.csv", global::System.StringComparison.OrdinalIgnoreCase) && GUILayout.Button("Authoring Phase1 読取比較"))
                Authoring.DialogueAuthoringPreviewWindow.Open(dataSet.SourcePath, dataSet.Entries.IndexOf(entry));
            DrawFooterDetails(); DrawNavigationShortcutHints();
        }
        private void DrawRelatedSections(DialoguePatternGroup group)
        {
            var profile = CurrentProfile;
            using (var area = new EditorGUILayout.ScrollViewScope(relatedScroll, GUILayout.ExpandHeight(true)))
            {
                relatedScroll = area.scrollPosition;
                var entry = GetCurrentEntry(group);
                try
                {
                    if (profile != null)
                    {
                        if (profile.TimedEvent || profile.Diary)
                            DrawRelatedSection("時間経過・日記", ref showTimeDiary, entry, () =>
                            {
                                if (profile.TimedEvent) DrawTimedEvents(entry);
                                if (profile.Diary) { DrawExistingDiaryRoute(entry); DrawIndependentDiary(entry); }
                            });
                        if (profile.TalkTopic) DrawRelatedSection("話題候補", ref showTopics, entry, () => DrawTalkTopic(entry));
                        if (profile.Readings) DrawRelatedSection("漢字読み", ref showReadings, entry, () => DrawReadingDictionary(entry));
                        if (profile.Teaching) DrawRelatedSection("教示設定", ref showTeaching, entry, () => DrawTeachingSettings(entry));
                    }
                    else EditorGUILayout.LabelField("この原稿の関連設定は未定義です。");
                    DrawDevelopmentInfo(group);
                }
                catch (ExitGUIException) { throw; }
                catch (Exception exception) { relatedDataError = exception.Message; }
                if (!string.IsNullOrEmpty(relatedDataError)) EditorGUILayout.HelpBox(relatedDataError, MessageType.Error);
            }
        }
        private void DrawRelatedSection(string title, ref bool expanded, DialogueEntry entry, Action draw)
        {
            var notices = GetRelatedNotices(title, entry);
            using (new EditorGUILayout.HorizontalScope())
            {
                expanded = EditorGUILayout.Foldout(expanded, title, true);
                foreach (var type in new[] { MessageType.Error, MessageType.Warning, MessageType.Info })
                {
                    var matching = notices.Where(item => item.type == type).ToList();
                    if (matching.Count == 0) continue;
                    string label = type == MessageType.Error ? "エラー" : type == MessageType.Warning ? "警告" : "情報";
                    if (GUILayout.Button(new GUIContent(label + " " + matching.Count, string.Join("\n", matching.Select(item => item.message))), EditorStyles.miniButton, GUILayout.Width(62))) expanded = true;
                }
            }
            if (Event.current.type == EventType.Repaint) relatedHeaderRects[title] = GUILayoutUtility.GetLastRect();
            if (!expanded) return;
            drawingRelatedSection = true;
            try { draw(); }
            finally { drawingRelatedSection = false; }
        }
        private void DrawTeachingSettings(DialogueEntry entry)
        {
            string[] fields = { "input", "EntryID", "SeriesGroupID", "TeachingCategory", "DisplayName", "teaching_prompt_disabled" };
            bool any = false;
            foreach (string field in fields.Where(field => dataSet.Headers.Any(header => header.Equals(field, StringComparison.OrdinalIgnoreCase))))
            {
                any = true; string value = entry.GetAdditionalValue(field) ?? "";
                string next = EditorGUILayout.TextField(FocusInput(field == "input" ? "入力語" : field), value);
                if (next != value) { entry.SetAdditionalValue(field, next); hasPendingChanges = true; }
            }
            if (!any) DrawCompactNotice("この原稿は既存の入力・Regex・出力から教示内容を登録します。専用メタデータ列はありません。", MessageType.Info);
            DrawCompactNotice("教示本文は左側、入力照合式は詳細設定から編集できます。", MessageType.Info);
        }
        // Read-only diagnostics keep collapsed headers informative without drawing controls or changing CSV data.
        private List<(MessageType type, string message)> GetRelatedNotices(string section, DialogueEntry entry)
        {
            var result = new List<(MessageType type, string message)>();
            void Add(MessageType type, string message) => result.Add((type, message));
            if (section == "時間経過・日記")
            {
                timedEvents ??= new DialogueRelatedCsvRepository("TalkSource/TalkCSV/SystemTimedEvent.csv", TimedEventHeaders);
                Add(MessageType.Info, "通常経路の0分以下は120分補完。専用経路・共有keyの扱いは展開して確認できます。");
                foreach (var link in new[] { (entry.ActionId, false), (entry.TimedEventKey, true) })
                {
                    var matches = timedEvents.Find("key", link.Item1);
                    if (matches.Count == 0 && !string.IsNullOrWhiteSpace(link.Item1)) Add(MessageType.Info, "TimedEvent未登録（他のActionの場合は正常）");
                    if (matches.Count > 1) Add(MessageType.Error, "重複TimedEvent key");
                    if (matches.Count != 1) continue;
                    Add(MessageType.Warning, "共有Event: " + link.Item1 + "。編集は全参照元へ反映されます。");
                    if (link.Item2) Add(MessageType.Info, "専用時間経過はAction側フローに依存します。");
                }
                Add(MessageType.Info, "TimedEventに付随する日記の記録経路・死亡例外");
                Add(MessageType.Info, "独立日記候補は時間経過不要。会話開始時に記録します。");
                diaryCandidates ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.CandidatePath, DialogueDiaryAuthoring.CandidateHeaders);
                diaryLinks ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.LinkPath, DialogueDiaryAuthoring.LinkHeaders);
                foreach (var link in diaryLinks.Find("DialogueId", entry.EditorDialogueId))
                {
                    string id = TalkTopicMasterCsvUtility.ReadValue(link, "DiaryId");
                    if (diaryCandidates.Find("DiaryId", id).Count != 1) Add(MessageType.Error, "日記候補の欠落・重複: " + id);
                    else if (diaryLinks.Find("DiaryId", id).Count > 1) Add(MessageType.Warning, "共有日記候補: " + id);
                }
            }
            else if (section == "話題候補")
            {
                string source = DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath);
                if (!source.StartsWith("TalkSource/TalkCSV/", StringComparison.OrdinalIgnoreCase)) Add(MessageType.Info, "TalkTopic登録は制作元で行ってください。");
                else
                {
                    topics ??= new DialogueRelatedCsvRepository(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.MasterHeaders);
                    string key = TopicSourceKey(entry, dataSet.Entries.IndexOf(entry));
                    int count = topics.Document.Rows.Count(row => TalkTopicMasterCsvUtility.ReadValue(row, "Source").Replace('\\', '/') == source.Substring("TalkSource/TalkCSV/".Length) && TalkTopicMasterCsvUtility.ReadValue(row, "SourceKey") == key);
                    if (count > 1) Add(MessageType.Error, "Source + SourceKey重複");
                    else if (count == 1) Add(MessageType.Info, "Importは通常の入力判定を通ります。登録OFFはEnabledをOFFにします。");
                }
            }
            else if (section == "漢字読み")
            {
                string diagnostics = GetReadingDiagnostics(entry);
                Add(ReadingNoticeType(diagnostics),
                    "input / regex_jp / Importを検査。\n" + diagnostics);
            }
            else if (section == "教示設定")
            {
                if (!new[] { "input", "EntryID", "SeriesGroupID", "TeachingCategory", "DisplayName", "teaching_prompt_disabled" }.Any(field => dataSet.Headers.Any(header => header.Equals(field, StringComparison.OrdinalIgnoreCase))))
                    Add(MessageType.Info, "専用メタデータ列なし。既存の入力・Regex・出力を使用します。");
                Add(MessageType.Info, "教示本文は左側、入力照合式は詳細設定から編集できます。");
            }
            return result;
        }
        private static MessageType ReadingNoticeType(string diagnostics) => diagnostics.Contains("未登録表記:") || diagnostics.Contains("読み競合") || diagnostics.Contains("無効辞書項目") ? MessageType.Warning : MessageType.Info;
        private void DrawCompactNotice(string message, MessageType type)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (type == MessageType.Error) { EditorGUILayout.HelpBox(message, type); return; }
            if (drawingRelatedSection) { EditorGUILayout.LabelField(message, EditorStyles.wordWrappedMiniLabel); return; }
            string key = message;
            bool expanded = expandedNotices.Contains(key);
            string badge = type == MessageType.Error ? "⛔ エラー" : type == MessageType.Warning ? "⚠ 警告" : "ℹ 情報";
            if (GUILayout.Button(new GUIContent(badge + " 1件 " + (expanded ? "▾" : "▸"), message), EditorStyles.miniButton, GUILayout.Width(105)))
            { if (expanded) expandedNotices.Remove(key); else expandedNotices.Add(key); expanded = !expanded; }
            if (expanded) EditorGUILayout.HelpBox(message, type);
        }
        internal static string ManuscriptStateKey(string path)
        {
            string source = DialogueAuthoringIdentity.NormalizeSource(path);
            string id = source.StartsWith("TalkSource/TalkCSV/", StringComparison.OrdinalIgnoreCase) ? DialogueAuthoringProfile.Resolve(path)?.Id : null;
            return SessionStateEditorPrefsKey + ".Profile." + (id ?? source);
        }
        private DialoguePreviewSessionState ReadManuscriptState()
        {
            if (string.IsNullOrWhiteSpace(dataSet.SourcePath)) return null;
            string json = EditorPrefs.GetString(ManuscriptStateKey(dataSet.SourcePath), "");
            if (string.IsNullOrEmpty(json)) json = EditorPrefs.GetString(SessionStateEditorPrefsKey + ".Manuscript." + DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath), "");
            if (string.IsNullOrEmpty(json) && CurrentProfile?.Id == "normal-dialogue")
                json = EditorPrefs.GetString(SessionStateEditorPrefsKey + ".Manuscript.TalkSource/TalkCSV/DialoguePreview_Integrated.csv", "");
            try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<DialoguePreviewSessionState>(json); }
            catch (ArgumentException) { return null; }
        }
        private void RestoreAuthoringView(DialoguePreviewSessionState view)
        {
            activeRelatedTab = view.RelatedTab ?? "時間"; showDetails = view.Details; showBehaviour = view.Behaviour;
            showBranches = view.Branches; showDevelopment = view.Development; showConditionExpression = view.ConditionExpression; showTeaching = view.Teaching;
            showTimeDiary = view.RelatedLayoutVersion > 0 ? view.TimeDiary : activeRelatedTab == "時間" || activeRelatedTab == "日記";
            showTopics = view.RelatedLayoutVersion > 0 ? view.Topics : activeRelatedTab == "話題";
            showReadings = view.RelatedLayoutVersion > 0 ? view.Readings : activeRelatedTab == "漢字";
            if (view.RelatedLayoutVersion == 0 && activeRelatedTab == "教示") showTeaching = true;
            regexInput = view.RegexInput ?? ""; scrollPosition = relatedScroll = Vector2.zero;
        }
        private void PersistAuthoringViewIfChanged()
        {
            string json = JsonUtility.ToJson(BuildSessionState());
            if (json == lastAuthoringViewJson) return;
            lastAuthoringViewJson = json; TryPersistSessionState();
        }
    }
}
