using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.Dialogue.UnknownWord;
using Nekolpos.System;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public partial class DialoguePreviewWindow : EditorWindow
    {
        private const string WindowTitle = "会話制作";
        private const float FooterHeight = 60f;
        private const string SessionStateEditorPrefsKey = "Nekolpos.DialoguePreviewWindow.SessionState";
        private const int ConditionDropdownCount = 4;

        [SerializeField] private DialogueDataLoader.DataSet dataSet = new DialogueDataLoader.DataSet();
        [SerializeField] private DialogueState state = new DialogueState();
        [SerializeField] private int selectedTab;
        [SerializeField] private int currentPatternIndex;
        [SerializeField] private int currentSequenceIndex;
        [SerializeField] private string regexInput = string.Empty;
        [SerializeField] private string regexInputChinese = string.Empty;
        [SerializeField] private string regexInputEnglish = string.Empty;
        [SerializeField] private string regexStatus = "未実行";
        [SerializeField] private List<string> matchedRegexIds = new List<string>();
        [SerializeField] private List<int> matchedPatternIndices = new List<int>();
        [SerializeField] private string unknownWordStatus = "未実行";
        [SerializeField] private string unknownWordNormalizedInput = string.Empty;
        [SerializeField] private string unknownWord = string.Empty;
        [SerializeField] private string unknownWordEstimatedCategory = string.Empty;
        [SerializeField] private List<string> unknownWordRemovedPatterns = new List<string>();
        [SerializeField] private List<string> unknownWordMatchedKnownWords = new List<string>();
        [SerializeField] private string lastBackupPath = string.Empty;
        [SerializeField] private bool hasPendingChanges;
        [SerializeField] private bool showDetails;
        [SerializeField] private bool deleteLinkedIdsWithLastLine = true;
        [SerializeField] private List<string> pendingDeletedDialogueIds = new List<string>();
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private string jumpIdValue = "00010";
        [SerializeField] private string jumpPatternStatus = string.Empty;

        private readonly List<DialoguePatternGroup> patternGroups = new List<DialoguePatternGroup>();
        private readonly List<RegexMatchDisplayItem> currentRegexMatchResults = new List<RegexMatchDisplayItem>();
        private readonly List<RegexMatchDisplaySection> otherCsvRegexMatchSections = new List<RegexMatchDisplaySection>();
        private readonly Dictionary<string, DialogueState> cachedPreviewStatesBySelectionKey = new Dictionary<string, DialogueState>();
        private readonly Dictionary<DialogueEntry, string> transientPatternGroupKeys = new Dictionary<DialogueEntry, string>();
        private string lastPreviewSyncKey = string.Empty;
        private string lastStateSyncSelectionKey = string.Empty;
        private GUIStyle compactInfoStyle;
        private bool isPersistingSessionState;

        private static readonly string[] ConditionDropdownLabels = { "condition_A", "condition_B", "condition_C", "condition_D" };
        private static readonly int[] PriorityOptionValues = { 0, 10, 20, 40, 60, 80, 100 };
        private static readonly string[] PriorityOptionLabels =
        {
            "0:フォールバック",
            "10:デフォルト",
            "20:通常会話",
            "40:軽い条件",
            "60:通常の条件分岐",
            "80:条件付き強分岐",
            "100:特殊イベント・強制"
        };
        private static readonly string[] SpeechControlOptionValues = { string.Empty, "RepeatEvent", "Sequence", "Random", "Call", "Return" };
        private static readonly string[] SpeechControlOptionLabels =
        {
            "未設定: OrderがあればSequence、RepeatCountがあればRepeatEvent、それ以外はRandomです",
            "RepeatEvent: 同じ入力が指定回数続いた時にこのPatternで返事します",
            "Sequence: 連続で呼ばれた時Patternの若い順に返事します",
            "Random: 呼ばれる度に別のPatternで返事します",
            "Call: 選択肢を出さずに、会話の途中で強制的に別Patternへ遷移します",
            "Return: Call元の次の行へ戻ります"
        };
        private static readonly string[] ResponseTypeOptionValues = { "Normal", "Choice", "Action", "Event", "Reaction" };
        private static readonly string[] ResponseTypeOptionLabels =
        {
            "Normal",
            "Choice",
            "Action",
            "Event",
            "Reaction"
        };
        private static readonly string[] DeathPresentationModeValues = { string.Empty, "motion", "timeline" };
        private static readonly string[] DeathPresentationModeLabels = { "なし", "Motion/Animation", "Timeline" };
        private static readonly string[] EmotionChangeTypeOptions = { string.Empty, "affection", "sadistic", "concern", "hostility", "obedience", "instinct" };
        private static readonly string[] EmotionChangeTypeLabels = { "なし", "愛情", "ドS", "心配", "敵対", "従順", "本能" };
        private static readonly string[] SharedConditionOptions = BuildSharedConditionOptions();
        private static readonly string[] ConditionDropdownOptions = BuildConditionDropdownOptions();

        private static readonly int[] EmotionChangeValueOptions = { 0, -1, -2, -3, 1, 2, 3 };
        private static readonly string[] EmotionChangeValueLabels = { "なし", "-1 (-10)", "-2 (-20)", "-3 (-30)", "+1 (+10)", "+2 (+20)", "+3 (+30)" };
        private const string ChoiceQuestionJaKey = "question_ja";
        private const string ChoiceQuestionEnKey = "question_en";
        private const string ChoiceQuestionCnKey = "question_cn";
        private const string ChoiceYesJaKey = "choice_yes_ja";
        private const string ChoiceYesEnKey = "choice_yes_en";
        private const string ChoiceYesCnKey = "choice_yes_cn";
        private const string ChoiceNoJaKey = "choice_no_ja";
        private const string ChoiceNoEnKey = "choice_no_en";
        private const string ChoiceNoCnKey = "choice_no_cn";
        private const string ChoiceYesNextPatternKey = "yes_next_pattern";
        private const string ChoiceNoNextPatternKey = "no_next_pattern";
        private const string RegexInputJaControlName = "DialoguePreview.RegexInput.JA";
        private const string RegexInputCnControlName = "DialoguePreview.RegexInput.CN";
        private const string RegexInputEnControlName = "DialoguePreview.RegexInput.EN";
        private static readonly Regex AdditionalChoiceKeyRegex = new Regex(
            @"^choice_(\d+)_(ja|en|cn|pattern)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StateConditionClauseRegex = new Regex(
            @"^\s*(?<key>[\p{L}\p{N}_]+)\s*(>=|<=|==|!=|=|>|<)\s*(?<value>.+?)\s*$",
            RegexOptions.Compiled);
        private static readonly Regex LocationConditionOptionRegex = new Regex(
            @"^\s*H\s*:\s*(?<negated>!)?\s*(?<location>[A-Za-z][A-Za-z0-9_]*)\s*$",
            RegexOptions.Compiled);

        [Serializable]
        private sealed class DialoguePreviewSessionState
        {
            public string SourcePath;
            public string SavePath;
            public string RegexId;
            public string DialogueId;
            public string Pattern;
            public int PatternIndex;
            public int SequenceIndex;
            public int EntryOrder;
            public int SelectedTab;
            public string RelatedTab;
            public int RelatedLayoutVersion;
            public bool TimeDiary, Topics, Readings;
            public bool Details, Behaviour, Branches, Development, ConditionExpression, Teaching;
            public string RegexInput;
        }

        private sealed class ParsedConditionDropdownState
        {
            public readonly string[] SelectedOptions = new string[ConditionDropdownCount];
            public readonly bool[] IsNegatedOptions = new bool[ConditionDropdownCount];
            public readonly string[] OriginalClauses = new string[ConditionDropdownCount];
            public readonly List<string> PreservedClauses = new List<string>();
            public bool HasOpaqueCondition;
        }

        private sealed class RegexMatchDisplayItem
        {
            public string CsvPath;
            public string CsvLabel;
            public string RegexId;
            public string Pattern;
            public string Preview;
            public string RegexSummary;
            public int Priority;
            public int MatchLength;
            public bool IsTopCandidate;
            public int PatternIndex = -1;
            public int SourceLineNumber;
            public int RegistrationOrder = int.MaxValue;
        }

        private sealed class RegexMatchDisplaySection
        {
            public string CsvPath;
            public string CsvLabel;
            public string ErrorMessage;
            public readonly List<RegexMatchDisplayItem> Items = new List<RegexMatchDisplayItem>();
        }

        [MenuItem("Nekolpos/会話プレビュー＆編集ツール")]
        public static void OpenWindow()
        {
            DialoguePreviewWindow window = GetWindow<DialoguePreviewWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1100f, 760f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            showOtherLanguages = EditorPrefs.GetBool(LanguagesKey, false);
            TextEditSceneUtility.EnsurePreviewStateAsset();
            RestoreSessionState();
            RestoreRelatedDrafts();
            RebuildPatternCache();
            ApplySessionSelection();
            PushCurrentPreviewToTextEditScene(true);
        }

        private void OnDisable()
        {
            CaptureRelatedDrafts();
            TryPersistSessionState();
        }

        private void OnGUI()
        {
            BeginInputFocusFrame();
            EnsureStyles(); HandleKeyboardNavigation(); SyncStateFromCurrentSelectionIfNeeded(false);
            DrawManuscriptToolbar();
            selectedTab = GUILayout.Toolbar(selectedTab, new[] { "会話", "入力照合" });
            UpdateRegexAuthoringSnapshot();
            if (selectedTab == 0 && patternGroups.Count > 0)
            {
                var group = GetCurrentGroup();
                DrawSequenceSelector(group);
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(450, position.width * 0.55f))))
                    using (var area = new EditorGUILayout.ScrollViewScope(scrollPosition))
                    {
                        scrollPosition = area.scrollPosition;
                        DrawMinimalEditor(group);
                        DrawAuthorDiagnostics();
                        DrawDetailsFoldout(group, DialogueStateSimulator.Evaluate(group, state));
                        if (Event.current.type == EventType.Repaint) primaryContentBottom = GUILayoutUtility.GetLastRect().yMax;
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        DrawRelatedSections(group);
                    }
                }
            }
            else
            {
                using (var area = new EditorGUILayout.ScrollViewScope(scrollPosition, GUILayout.ExpandHeight(true)))
                {
                    scrollPosition = area.scrollPosition;
                    if (patternGroups.Count > 0) { DrawAuthorDiagnostics(); DrawRegexTab(); }
                    else EditorGUILayout.LabelField("原稿を選択してください。");
                }
            }
            DrawFooter();
            if (Event.current.type == EventType.Repaint) footerRect = GUILayoutUtility.GetLastRect();
            PersistAuthoringViewIfChanged();
            PushCurrentPreviewToTextEditScene(false);
            EndInputFocusFrame();
        }

        private void DrawTopSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect csvRow = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                float resourcesWidth = 90f;
                float latestWidth = 58f;
                float openWidth = 58f;
                float spacing = 4f;
                float labelWidth = 28f;
                float pathWidth = Mathf.Max(
                    80f,
                    csvRow.width - labelWidth - resourcesWidth - latestWidth - openWidth - spacing * 3f);

                Rect labelRect = new Rect(csvRow.x, csvRow.y, labelWidth, csvRow.height);
                Rect pathRect = new Rect(labelRect.xMax, csvRow.y, pathWidth, csvRow.height);
                Rect resourcesRect = new Rect(pathRect.xMax + spacing, csvRow.y, resourcesWidth, csvRow.height);
                Rect latestRect = new Rect(resourcesRect.xMax + spacing, csvRow.y, latestWidth, csvRow.height);
                Rect openRect = new Rect(latestRect.xMax + spacing, csvRow.y, openWidth, csvRow.height);

                EditorGUI.LabelField(labelRect, "CSV");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.TextField(FocusInput(pathRect), string.IsNullOrEmpty(dataSet.SourcePath) ? "未選択" : dataSet.SourcePath);
                }

                if (GUI.Button(resourcesRect, "Resources"))
                {
                    ShowResourceCsvMenu();
                }

                if (GUI.Button(latestRect, "最新"))
                {
                    string newestPath = DialogueDataLoader.GetNewestResourceCsvPath();
                    if (!string.IsNullOrEmpty(newestPath))
                    {
                        LoadCsv(newestPath);
                    }
                }

                if (GUI.Button(openRect, "開く"))
                {
                    string selectedPath = EditorUtility.OpenFilePanel(
                        "Load Dialogue CSV",
                        DialogueDataLoader.GetTalkCsvDirectory(),
                        "csv");
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        LoadCsv(selectedPath);
                    }
                }

                Rect saveRow = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                float saveButtonWidth = 58f;
                float saveLabelWidth = 40f;
                float savePathWidth = Mathf.Max(80f, saveRow.width - saveLabelWidth - saveButtonWidth - spacing);
                Rect saveLabelRect = new Rect(saveRow.x, saveRow.y, saveLabelWidth, saveRow.height);
                Rect savePathRect = new Rect(saveLabelRect.xMax, saveRow.y, savePathWidth, saveRow.height);
                Rect saveButtonRect = new Rect(savePathRect.xMax + spacing, saveRow.y, saveButtonWidth, saveRow.height);

                EditorGUI.LabelField(saveLabelRect, "保存先");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.TextField(FocusInput(savePathRect), string.IsNullOrEmpty(dataSet.SavePath) ? "未選択" : dataSet.SavePath);
                }

                if (GUI.Button(saveButtonRect, "設定"))
                {
                    string initialDirectory = DialogueDataLoader.GetTalkCsvDirectory();
                    string initialName = "DialoguePreview.csv";
                    if (!string.IsNullOrWhiteSpace(dataSet.SavePath))
                    {
                        string existingFullPath = DialogueDataLoader.GetFullPath(dataSet.SavePath);
                        string existingDirectory = Path.GetDirectoryName(existingFullPath);
                        if (!string.IsNullOrWhiteSpace(existingDirectory))
                        {
                            initialDirectory = existingDirectory;
                        }

                        initialName = Path.GetFileName(existingFullPath);
                    }
                    else if (!string.IsNullOrWhiteSpace(dataSet.SourcePath))
                    {
                        string sourceFullPath = DialogueDataLoader.GetFullPath(dataSet.SourcePath);
                        string sourceDirectory = Path.GetDirectoryName(sourceFullPath);
                        if (!string.IsNullOrWhiteSpace(sourceDirectory))
                        {
                            initialDirectory = sourceDirectory;
                        }

                        initialName = Path.GetFileName(sourceFullPath);
                    }

                    string selectedSavePath = EditorUtility.SaveFilePanel("Save Dialogue CSV", initialDirectory, initialName, "csv");
                    if (!string.IsNullOrWhiteSpace(selectedSavePath))
                    {
                        dataSet.SavePath = selectedSavePath.Replace("\\", "/");
                        dataSet.SaveLabel = Path.GetFileName(selectedSavePath);
                    }
                }

                EditorGUILayout.Space(4f);
                DialogueState beforeState = CloneDialogueState(state);
                Rect stateRow1 = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow1, 0), "愛情", ref state.affectionLevel);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow1, 1), "ドS", ref state.sadisticLevel);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow1, 2), "心配", ref state.concernLevel);

                Rect stateRow2 = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow2, 0), "敵対", ref state.hostilityLevel);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow2, 1), "従順", ref state.obedienceLevel);
                DrawStateLevelToolbar(GetTripleRowSegment(stateRow2, 2), "本能", ref state.instinctLevel);

                if (!AreDialogueStatesEqual(beforeState, state))
                {
                    CacheStateForCurrentSelection();
                    ApplyStateToolbarConditionChange();
                }

                if (patternGroups.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField(
                        $"Patterns {patternGroups.Count} / Current {Mathf.Clamp(currentPatternIndex + 1, 0, patternGroups.Count)}",
                        compactInfoStyle);
                }

            }
        }

        private Rect GetTripleRowSegment(Rect rowRect, int index)
        {
            const float spacing = 8f;
            float segmentWidth = (rowRect.width - spacing * 2f) / 3f;
            float x = rowRect.x + (segmentWidth + spacing) * index;
            return new Rect(x, rowRect.y, segmentWidth, rowRect.height);
        }

        private void DrawDialogueTab()
        {
            DialoguePatternGroup group = GetCurrentGroup();
            if (group == null)
            {
                EditorGUILayout.HelpBox("Pattern が見つかりません。", MessageType.Warning);
                return;
            }

            DialogueStateSimulator.EvaluationResult groupState = DialogueStateSimulator.Evaluate(group, state);
            DrawPatternHeader(group, groupState);
            DrawSequenceSelector(group);
            DrawMinimalEditor(group);
            DrawDetailsFoldout(group, groupState);
            DrawRelatedData(group);
        }

        private void DrawPatternHeader(DialoguePatternGroup group, DialogueStateSimulator.EvaluationResult groupState)
        {
            if (groupState.IsMatch)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(groupState.Message, MessageType.Warning);
            }
            EditorGUILayout.Space(6f);
        }

        private void DrawSequenceSelector(DialoguePatternGroup group)
        {
            if (group == null || group.Entries.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"会話 {currentPatternIndex + 1}/{patternGroups.Count}　Pattern {group.Pattern}", GUILayout.Width(240));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("新項目追加", EditorStyles.toolbarButton, GUILayout.Width(82))) AddNewItem(group);
                if (GUILayout.Button("新パターン追加", EditorStyles.toolbarButton, GUILayout.Width(104))) AddNewPattern(group);
                if (GUILayout.Button("行追加", EditorStyles.toolbarButton, GUILayout.Width(62))) AddLineToCurrentPattern(group);
                if (group.Entries.Count == 1)
                    deleteLinkedIdsWithLastLine = GUILayout.Toggle(deleteLinkedIdsWithLastLine, "連携IDも削除", EditorStyles.toolbarButton, GUILayout.Width(104));
                if (GUILayout.Button("行削除", EditorStyles.toolbarButton, GUILayout.Width(62))) DeleteCurrentLine(group);
                GUILayout.Label(hasPendingChanges ? "未保存" : "保存済み", GUILayout.Width(64));
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(sequenceSelectorScroll, GUILayout.Height(36)))
            {
                sequenceSelectorScroll = scroll.scrollPosition;
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < group.Entries.Count; i++)
                    {
                        var label = new GUIContent((i + 1).ToString(), BuildLineDisplayLabel(group.Entries[i]));
                        if (GUILayout.Toggle(i == currentSequenceIndex, label, EditorStyles.toolbarButton, GUILayout.MinWidth(32), GUILayout.ExpandWidth(true)) && i != currentSequenceIndex)
                        {
                            ClearActiveTextFieldFocus();
                            currentSequenceIndex = i;
                            SyncStateFromCurrentSelectionIfNeeded(true);
                        }
                    }
                }
            }
        }

        private Vector2 sequenceSelectorScroll;

        private void AddNewItem(DialoguePatternGroup group)
        {
            DialogueEntry source = GetCurrentEntry(group);
            if (source == null || dataSet?.Entries == null) return;
            string currentId = source.InternalId?.Trim() ?? string.Empty;
            if (!long.TryParse(currentId, global::System.Globalization.NumberStyles.None, global::System.Globalization.CultureInfo.InvariantCulture, out long candidate))
            {
                ShowNotification(new GUIContent("新項目を追加できません：現在のIDを数値にしてください。"));
                return;
            }
            var usedIds = new HashSet<long>();
            foreach (DialogueEntry entry in dataSet.Entries)
                if (long.TryParse(entry.InternalId?.Trim(), out long numericId)) usedIds.Add(numericId);
            do
            {
                if (candidate > long.MaxValue - 10)
                {
                    ShowNotification(new GUIContent("新項目を追加できません：IDが数値の上限に達しています。"));
                    return;
                }
                candidate += 10;
            } while (usedIds.Contains(candidate));

            var newEntry = new DialogueEntry
            {
                InternalId = candidate.ToString("D" + currentId.Length, global::System.Globalization.CultureInfo.InvariantCulture),
                RegexId = GetNewItemRegexPrefix(),
                RegexPattern = string.Empty,
                RegexPatternChinese = string.Empty,
                RegexPatternEnglish = string.Empty,
                Pattern = "1",
                Order = 1,
                SourceLineNumber = GetNextSourceLineNumber(),
                Text = string.Empty,
                TextChinese = string.Empty,
                TextEnglish = string.Empty,
                ResponseType = "Normal",
                SpeechControl = "Random",
                Priority = 10
            };
            dataSet.Entries.Add(newEntry);
            ClearActiveTextFieldFocus();
            hasPendingChanges = true;
            sequenceSelectorScroll = Vector2.zero;
            RebuildPatternCache(newEntry);
            Repaint();
        }

        private string GetNewItemRegexPrefix()
        {
            string configuredPrefix = DialogueAuthoringProfile.Resolve(dataSet.SourcePath)?.NewItemRegexPrefix;
            if (!string.IsNullOrEmpty(configuredPrefix)) return configuredPrefix;
            var regexIds = dataSet.Entries.Select(entry => entry.RegexId?.Trim())
                .Where(id => !string.IsNullOrEmpty(id)).ToArray();
            if (regexIds.Length == 0) return string.Empty;
            int separator = regexIds[0].IndexOf('_');
            if (separator < 0) return string.Empty;
            string prefix = regexIds[0].Substring(0, separator + 1);
            return regexIds.All(id => id.StartsWith(prefix, StringComparison.Ordinal)) ? prefix : string.Empty;
        }

        private void AddNewPattern(DialoguePatternGroup group)
        {
            DialogueEntry source = GetRepresentativeEntry(group);
            if (source == null || dataSet?.Entries == null) return;

            var usedPatterns = new HashSet<string>(dataSet.Entries
                .Where(entry => string.Equals(entry.InternalId?.Trim(), source.InternalId?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.RegexId?.Trim(), source.RegexId?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(entry => ParsePatternNumber(entry.Pattern).ToString()));
            int pattern = 1;
            while (usedPatterns.Contains(pattern.ToString())) pattern++;

            var newEntry = new DialogueEntry
            {
                InternalId = source.InternalId,
                RegexId = source.RegexId,
                RegexPattern = source.RegexPattern,
                RegexPatternChinese = source.RegexPatternChinese,
                RegexPatternEnglish = source.RegexPatternEnglish,
                Pattern = pattern.ToString(),
                Order = 1,
                SourceLineNumber = GetNextSourceLineNumber(),
                Text = string.Empty,
                TextChinese = string.Empty,
                TextEnglish = string.Empty,
                ResponseType = "Normal",
                SpeechControl = source.SpeechControl,
                Priority = source.Priority,
                Condition = source.Condition
            };
            // 入力の照合情報だけを引き継ぎ、本文・分岐・演出を新パターンへ混入させない。
            newEntry.PreserveRegexSourceValues(source.GetPreservedRegexSource(), source.GetPreservedRegexChineseSource(), source.GetPreservedRegexEnglishSource());
            newEntry.SetAdditionalValue("input", source.GetAdditionalValue("input"));
            dataSet.Entries.Add(newEntry);
            ClearActiveTextFieldFocus();
            hasPendingChanges = true;
            sequenceSelectorScroll = Vector2.zero;
            RebuildPatternCache(newEntry);
            Repaint();
        }

        private void DrawMinimalEditor(DialoguePatternGroup group)
        {
            DialogueEntry currentEntry = GetCurrentEntry(group);
            DialogueEntry representative = GetRepresentativeEntry(group);
            if (currentEntry == null || representative == null)
            {
                return;
            }

            int pattern;
            int priority;
            bool callOnly;
            bool sequenceProgressHold;
            int randomRepeatLimit;
            string targetPattern;
            string speechControl;
            int responseTypeIndex;
            int nextResponseTypeIndex;
            string actionId;
            string timedEventKey;
            string animation;
            string nextEmotionChangeType;
            int nextEmotionChangeValue;
            string condition;
            int order;
            string textChinese;
            string textEnglish;
            string text;
            bool choiceSettingsChanged = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("会話本文", EditorStyles.boldLabel);
                DrawPsychologyTimingSelector(group);
                pattern = EditorGUILayout.IntField(FocusInput("Pattern"), ParsePatternNumber(currentEntry.Pattern));
                responseTypeIndex = GetResponseTypeOptionIndex(currentEntry.ResponseType);
                nextResponseTypeIndex = EditorGUILayout.Popup(FocusInput("応答"), responseTypeIndex, ResponseTypeOptionLabels);
                nextEmotionChangeType = representative.EmotionChangeType;
                nextEmotionChangeValue = representative.EmotionChangeValue;
                condition = representative.Condition ?? string.Empty;
                if (CurrentProfile?.Condition != false)
                    condition = DrawConditionDropdownEditors(condition, nextEmotionChangeType, nextEmotionChangeValue, out nextEmotionChangeType, out nextEmotionChangeValue);
                EditorGUILayout.LabelField("日本語");
                var dialogueTextHeight = EditorStyles.textArea.lineHeight * 2f + EditorStyles.textArea.padding.vertical;
                text = EditorGUILayout.TextArea(FocusInput(currentEntry.Text ?? string.Empty), GUILayout.Height(dialogueTextHeight));
                textEnglish = currentEntry.TextEnglish ?? string.Empty;
                textChinese = currentEntry.TextChinese ?? string.Empty;
                if (showOtherLanguages)
                {
                    EditorGUILayout.LabelField("英語"); textEnglish = EditorGUILayout.TextArea(FocusInput(textEnglish), GUILayout.Height(dialogueTextHeight));
                    EditorGUILayout.LabelField("中国語"); textChinese = EditorGUILayout.TextArea(FocusInput(textChinese), GUILayout.Height(dialogueTextHeight));
                }
                priority = representative.Priority; callOnly = representative.CallOnly;
                sequenceProgressHold = representative.SequenceProgressHold; randomRepeatLimit = representative.RandomRepeatLimit;
                targetPattern = currentEntry.TargetPattern ?? string.Empty; speechControl = currentEntry.SpeechControl;
                animation = currentEntry.Animation ?? string.Empty; actionId = currentEntry.ActionId ?? string.Empty;
                timedEventKey = currentEntry.TimedEventKey ?? string.Empty; order = currentEntry.Order;
                if (ResponseTypeOptionValues[nextResponseTypeIndex] == "Choice")
                {
                    if (NormalizeResponseType(currentEntry.ResponseType) != "Choice") showBranches = true;
                    showBranches = EditorGUILayout.Foldout(showBranches, "分岐・選択肢", true);
                    if (Event.current.type == EventType.Repaint) branchesRect = GUILayoutUtility.GetLastRect();
                    if (showBranches) choiceSettingsChanged = DrawChoiceSettings(currentEntry);
                }
                showBehaviour = EditorGUILayout.Foldout(showBehaviour, "再生・遷移設定", true);
                if (Event.current.type == EventType.Repaint) behaviourRect = GUILayoutUtility.GetLastRect();
                if (showBehaviour)
                {
                    priority = DrawPriorityPopup("優先度", priority);
                    callOnly = EditorGUILayout.Toggle(FocusInput("Call専用"), callOnly);
                    sequenceProgressHold = EditorGUILayout.Toggle(FocusInput("進行保持"), sequenceProgressHold);
                    randomRepeatLimit = EditorGUILayout.IntField(FocusInput("Random継続回数"), randomRepeatLimit);
                    speechControl = DrawSpeechControlPopup("再生方式", speechControl);
                    targetPattern = EditorGUILayout.TextField(FocusInput("遷移先Pattern"), targetPattern);
                    DrawActionIdAndDeathFlowRow(ref nextResponseTypeIndex, ref actionId, ref targetPattern, ref animation);
                    timedEventKey = EditorGUILayout.TextField(FocusInput("専用時間経過キー"), timedEventKey);
                    order = EditorGUILayout.IntField(FocusInput("行順"), order);
                    DrawCompactNotice(BuildResponseRoutingGuidance(currentEntry, ResponseTypeOptionValues[nextResponseTypeIndex], actionId, out MessageType guidanceType), guidanceType);
                }
            }

            string normalizedPattern = pattern.ToString();
            string normalizedSpeechControl = NormalizeSpeechControl(currentEntry.SpeechControl);
            string normalizedTargetPattern = NormalizeTargetPatternValue(targetPattern);

            bool changedPattern = !string.Equals(normalizedPattern, currentEntry.Pattern, StringComparison.Ordinal);
            bool changedSharedSettings = priority != representative.Priority ||
                                         callOnly != representative.CallOnly ||
                                         sequenceProgressHold != representative.SequenceProgressHold ||
                                         randomRepeatLimit != representative.RandomRepeatLimit;

            bool changedEntry = order != currentEntry.Order ||
                               !string.Equals(speechControl, normalizedSpeechControl, StringComparison.Ordinal) ||
                               !string.Equals(normalizedTargetPattern, currentEntry.TargetPattern ?? string.Empty, StringComparison.Ordinal) ||
                               !string.Equals(ResponseTypeOptionValues[nextResponseTypeIndex], NormalizeResponseType(currentEntry.ResponseType), StringComparison.Ordinal) ||
                               !string.Equals(actionId, currentEntry.ActionId, StringComparison.Ordinal) ||
                               !string.Equals(timedEventKey, currentEntry.TimedEventKey ?? string.Empty, StringComparison.Ordinal) ||
                               !string.Equals(animation, currentEntry.Animation ?? string.Empty, StringComparison.Ordinal) ||
                               !string.Equals(condition, representative.Condition, StringComparison.Ordinal);

            bool changedPatternEmotion = !string.Equals(nextEmotionChangeType, NormalizeEmotionChangeType(representative.EmotionChangeType), StringComparison.Ordinal) ||
                                         nextEmotionChangeValue != (string.IsNullOrEmpty(NormalizeEmotionChangeType(representative.EmotionChangeType)) ? 0 : NormalizeEmotionChangeValue(representative.EmotionChangeValue));

            changedEntry = changedEntry ||
                               changedPatternEmotion ||
                               !string.Equals(text, currentEntry.Text, StringComparison.Ordinal) ||
                               !string.Equals(textChinese, currentEntry.TextChinese, StringComparison.Ordinal) ||
                               !string.Equals(textEnglish, currentEntry.TextEnglish, StringComparison.Ordinal);

            if (changedPattern || changedSharedSettings || changedEntry || choiceSettingsChanged)
            {
                if (changedPattern)
                {
                    currentEntry.Pattern = normalizedPattern;
                }

                if (changedSharedSettings)
                {
                    for (int i = 0; i < group.Entries.Count; i++)
                    {
                        group.Entries[i].Priority = priority;
                        group.Entries[i].CallOnly = callOnly;
                        group.Entries[i].SequenceProgressHold = sequenceProgressHold;
                        group.Entries[i].RandomRepeatLimit = randomRepeatLimit;
                    }
                }

                if (changedEntry)
                {
                    ApplySpeechControlChange(group, currentEntry, speechControl, normalizedTargetPattern);
                    currentEntry.Order = order;
                    currentEntry.ResponseType = ResponseTypeOptionValues[nextResponseTypeIndex];
                    currentEntry.ActionId = actionId;
                    currentEntry.TimedEventKey = timedEventKey;
                    currentEntry.Animation = animation;
                    ApplyConditionChange(group, condition);
                    ApplyEmotionChange(group, nextEmotionChangeType, nextEmotionChangeValue);
                    currentEntry.Text = text;
                    currentEntry.TextChinese = textChinese;
                    currentEntry.TextEnglish = textEnglish;
                }

                SortPatternEntries(group);
                hasPendingChanges = true;
                RebuildPatternCache(currentEntry);
            }

            EditorGUILayout.Space(6f);
        }

        private bool DrawChoiceSettings(DialogueEntry currentEntry)
        {
            if (currentEntry == null)
            {
                return false;
            }

            EnsureChoiceAdditionalFields(currentEntry);
            bool changed = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Choice設定", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("他Patternに複製する", GUILayout.Width(150f), GUILayout.Height(22f)))
                    {
                        DuplicateChoiceSettingsToOtherPatterns(currentEntry);
                        GUI.FocusControl(null);
                    }
                }

                changed |= DrawChoiceLocalizedRow(currentEntry, "質問文", ChoiceQuestionJaKey, ChoiceQuestionEnKey, ChoiceQuestionCnKey);
                changed |= DrawChoiceChoiceRow(
                    currentEntry,
                    "Yes",
                    ChoiceYesJaKey,
                    ChoiceYesEnKey,
                    ChoiceYesCnKey,
                    ChoiceYesNextPatternKey,
                    entry => entry.ChoiceYesPattern,
                    (entry, value) => entry.ChoiceYesPattern = value);
                changed |= DrawChoiceChoiceRow(
                    currentEntry,
                    "No",
                    ChoiceNoJaKey,
                    ChoiceNoEnKey,
                    ChoiceNoCnKey,
                    ChoiceNoNextPatternKey,
                    entry => entry.ChoiceNoPattern,
                    (entry, value) => entry.ChoiceNoPattern = value);

                List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(currentEntry);
                for (int i = 0; i < additionalChoiceNumbers.Count; i++)
                {
                    int choiceNumber = additionalChoiceNumbers[i];
                    changed |= DrawChoiceChoiceRow(
                        currentEntry,
                        choiceNumber.ToString(),
                        GetAdditionalChoiceLanguageKey(choiceNumber, "ja"),
                        GetAdditionalChoiceLanguageKey(choiceNumber, "en"),
                        GetAdditionalChoiceLanguageKey(choiceNumber, "cn"),
                        GetAdditionalChoicePatternKey(choiceNumber),
                        entry => entry.GetAdditionalValue(GetAdditionalChoicePatternKey(choiceNumber)),
                        (entry, value) => entry.SetAdditionalValue(GetAdditionalChoicePatternKey(choiceNumber), value));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("選択肢追加", GUILayout.Width(92f), GUILayout.Height(22f)))
                    {
                        AddAdditionalChoiceOption(currentEntry);
                        changed = true;
                        GUI.FocusControl(null);
                    }

                    using (new EditorGUI.DisabledScope(additionalChoiceNumbers.Count == 0))
                    {
                        if (GUILayout.Button("選択肢削除", GUILayout.Width(92f), GUILayout.Height(22f)))
                        {
                            RemoveLastAdditionalChoiceOption(currentEntry, additionalChoiceNumbers);
                            changed = true;
                            GUI.FocusControl(null);
                        }
                    }
                }
            }

            return changed;
        }

        private void DrawActionIdAndDeathFlowRow(
            ref int responseTypeIndex,
            ref string actionId,
            ref string targetPattern,
            ref string animation)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            const float spacing = 4f;
            const float labelWidth = 58f;
            const float deathWidth = 54f;
            const float reasonWidth = 76f;
            const float modeWidth = 74f;
            const float animationWidth = 118f;
            const float postWidth = 46f;

            float actionWidth = Mathf.Max(
                70f,
                row.width - labelWidth - deathWidth - reasonWidth - modeWidth - animationWidth - postWidth - spacing * 6f);

            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect actionRect = new Rect(labelRect.xMax + spacing, row.y, actionWidth, row.height);
            Rect deathRect = new Rect(actionRect.xMax + spacing, row.y, deathWidth, row.height);
            Rect reasonRect = new Rect(deathRect.xMax + spacing, row.y, reasonWidth, row.height);
            Rect modeRect = new Rect(reasonRect.xMax + spacing, row.y, modeWidth, row.height);
            Rect animationRect = new Rect(modeRect.xMax + spacing, row.y, animationWidth, row.height);
            Rect postRect = new Rect(animationRect.xMax + spacing, row.y, postWidth, row.height);

            EditorGUI.LabelField(labelRect, "action_id");
            actionId = EditorGUI.TextField(FocusInput(actionRect), actionId ?? string.Empty);

            bool wasDeath = TryExtractDeathReason(actionId, out string reason);
            bool death = EditorGUI.ToggleLeft(FocusInput(deathRect), "死亡", wasDeath);
            if (!death)
            {
                if (wasDeath)
                {
                    actionId = string.Empty;
                }

                return;
            }

            if (!wasDeath || string.IsNullOrWhiteSpace(reason))
            {
                reason = "Claw";
            }

            responseTypeIndex = Mathf.Max(0, Array.IndexOf(ResponseTypeOptionValues, "Event"));
            reason = EditorGUI.TextField(FocusInput(reasonRect), reason);
            actionId = "death:" + NormalizeDeathReasonInput(reason);

            string presentationMode = ResolveDeathPresentationMode(animation);
            int modeIndex = Mathf.Max(0, Array.IndexOf(DeathPresentationModeValues, presentationMode));
            modeIndex = EditorGUI.Popup(FocusInput(modeRect), modeIndex, DeathPresentationModeLabels);
            string nextMode = DeathPresentationModeValues[Mathf.Clamp(modeIndex, 0, DeathPresentationModeValues.Length - 1)];

            string presentationValue = StripDeathPresentationPrefix(animation);
            if (string.IsNullOrEmpty(nextMode))
            {
                animation = string.Empty;
            }
            else
            {
                presentationValue = EditorGUI.TextField(FocusInput(animationRect), presentationValue ?? string.Empty);
                animation = nextMode + ":" + (presentationValue ?? string.Empty).Trim();
            }

            targetPattern = EditorGUI.TextField(FocusInput(postRect), targetPattern ?? string.Empty);
        }

        private static bool TryExtractDeathReason(string actionId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            string trimmed = actionId.Trim();
            if (trimmed.StartsWith("death:", StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring("death:".Length).Trim();
                return true;
            }

            if (trimmed.StartsWith("defeat:", StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring("defeat:".Length).Trim();
                return true;
            }

            const string prefix = "PlayerDefeatedBy";
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = trimmed.Substring(prefix.Length).Trim();
                return true;
            }

            return false;
        }

        private static string NormalizeDeathReasonInput(string reason)
        {
            string trimmed = string.IsNullOrWhiteSpace(reason) ? "Claw" : reason.Trim();
            return trimmed.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        }

        private static string ResolveDeathPresentationMode(string animation)
        {
            if (string.IsNullOrWhiteSpace(animation))
            {
                return string.Empty;
            }

            string trimmed = animation.Trim();
            if (trimmed.StartsWith("timeline:", StringComparison.OrdinalIgnoreCase))
            {
                return "timeline";
            }

            if (trimmed.StartsWith("motion:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("anim:", StringComparison.OrdinalIgnoreCase))
            {
                return "motion";
            }

            return "motion";
        }

        private static string StripDeathPresentationPrefix(string animation)
        {
            if (string.IsNullOrWhiteSpace(animation))
            {
                return string.Empty;
            }

            string trimmed = animation.Trim();
            if (trimmed.StartsWith("timeline:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("timeline:".Length).Trim();
            }

            if (trimmed.StartsWith("motion:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("motion:".Length).Trim();
            }

            if (trimmed.StartsWith("anim:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("anim:".Length).Trim();
            }

            return trimmed;
        }

        private void DrawDetailsFoldout(DialoguePatternGroup group, DialogueStateSimulator.EvaluationResult groupState)
        {
            showDetails = EditorGUILayout.Foldout(showDetails, "詳細設定", true);
            if (!showDetails)
            {
                return;
            }

            DialogueEntry currentEntry = GetCurrentEntry(group);
            DialogueEntry representative = GetRepresentativeEntry(group);
            if (currentEntry == null || representative == null)
            {
                return;
            }

            float waitTime;
            bool hideUi;
            string animation;
            string regexId;
            string regexChinese;
            string regexEnglish;
            string regex;
            string internalId;
            int repeatCount;
            string intent;
            string reactionType;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                waitTime = EditorGUILayout.FloatField(FocusInput("WaitTime"), currentEntry.WaitTime);
                hideUi = EditorGUILayout.Toggle(FocusInput("HideUI"), currentEntry.HideUI);
                animation = EditorGUILayout.TextField(FocusInput("Animation"), currentEntry.Animation ?? string.Empty);
                regexId = EditorGUILayout.TextField(FocusInput("regex_id"), currentEntry.RegexId ?? string.Empty);
                regexChinese = showOtherLanguages ? EditorGUILayout.TextField(FocusInput("照合式（中国語）"), currentEntry.RegexPatternChinese ?? string.Empty) : currentEntry.RegexPatternChinese;
                regexEnglish = showOtherLanguages ? EditorGUILayout.TextField(FocusInput("照合式（英語）"), currentEntry.RegexPatternEnglish ?? string.Empty) : currentEntry.RegexPatternEnglish;
                regex = EditorGUILayout.TextField(FocusInput("regex (JP)"), currentEntry.RegexPattern ?? string.Empty);
                internalId = EditorGUILayout.TextField(FocusInput("id"), currentEntry.InternalId ?? string.Empty);
                repeatCount = EditorGUILayout.IntField(FocusInput(new GUIContent("RepeatCount", "Speech Control=RepeatEventの発火回数。同じ話題の連続入力を1回目から数えます。Randomの抽選待ち回数ではありません。")), currentEntry.RepeatCount);
                intent = EditorGUILayout.TextField(FocusInput("intent"), currentEntry.Intent ?? string.Empty);
                reactionType = EditorGUILayout.TextField(FocusInput("reaction_type"), currentEntry.ReactionType ?? string.Empty);

                bool changedRegexGroup =
                    !string.Equals(regexId, currentEntry.RegexId, StringComparison.Ordinal) ||
                    !string.Equals(regex, currentEntry.RegexPattern, StringComparison.Ordinal) ||
                    !string.Equals(regexChinese, currentEntry.RegexPatternChinese, StringComparison.Ordinal) ||
                    !string.Equals(regexEnglish, currentEntry.RegexPatternEnglish, StringComparison.Ordinal);

                bool changedInternalId = !string.Equals(internalId, currentEntry.InternalId, StringComparison.Ordinal);
                bool changedRegexPatterns = !string.Equals(regex, currentEntry.RegexPattern, StringComparison.Ordinal)
                    || !string.Equals(regexChinese, currentEntry.RegexPatternChinese, StringComparison.Ordinal)
                    || !string.Equals(regexEnglish, currentEntry.RegexPatternEnglish, StringComparison.Ordinal);

                if (!Mathf.Approximately(waitTime, currentEntry.WaitTime) ||
                    hideUi != currentEntry.HideUI ||
                    !string.Equals(animation, currentEntry.Animation, StringComparison.Ordinal) ||
                    changedRegexGroup ||
                    changedInternalId ||
                    repeatCount != currentEntry.RepeatCount ||
                    !string.Equals(intent, currentEntry.Intent, StringComparison.Ordinal) ||
                    !string.Equals(reactionType, currentEntry.ReactionType, StringComparison.Ordinal))
                {
                    currentEntry.WaitTime = Mathf.Max(0f, waitTime);
                    currentEntry.HideUI = hideUi;
                    currentEntry.Animation = animation;
                    if (changedRegexPatterns) ApplySharedRegexPatterns(currentEntry, regex, regexChinese, regexEnglish);
                    currentEntry.InternalId = internalId;
                    currentEntry.RepeatCount = repeatCount;
                    currentEntry.Intent = intent;
                    currentEntry.ReactionType = reactionType;

                    if (changedRegexGroup)
                    {
                        regexAuthoringValidationDirty = true;
                        for (int i = 0; i < group.Entries.Count; i++)
                        {
                            group.Entries[i].RegexId = regexId;
                        }

                        ResetTransientPatternGroupKeys();
                    }
                    else if (changedInternalId)
                    {
                        ResetTransientPatternGroupKeys();
                    }

                    hasPendingChanges = true;
                    RebuildPatternCache(currentEntry);
                }
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField($"State Eval: {groupState.Message}", compactInfoStyle);
            }
            EditorGUILayout.Space(6f);
        }

        private void ApplySharedRegexPatterns(DialogueEntry source, string japanese, string chinese, string english)
        {
            if (source == null || dataSet?.Entries == null) return;
            string id = source.InternalId?.Trim();
            foreach (var entry in dataSet.Entries)
            {
                if (entry == null || (!ReferenceEquals(entry, source) &&
                    (string.IsNullOrEmpty(id) || !string.Equals(entry.InternalId?.Trim(), id, StringComparison.Ordinal)))) continue;
                entry.RegexPattern = japanese;
                entry.RegexPatternChinese = chinese;
                entry.RegexPatternEnglish = english;
            }
            regexAuthoringValidationDirty = true;
            hasPendingChanges = true;
        }

        private void DrawRegexTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Regex Test", EditorStyles.boldLabel);
                GUI.SetNextControlName(RegexInputJaControlName);
                regexInput = EditorGUILayout.TextField(FocusInput("入力 JP", RegexInputJaControlName), regexInput ?? string.Empty);
                if (showOtherLanguages) { GUI.SetNextControlName(RegexInputCnControlName);
                regexInputChinese = EditorGUILayout.TextField(FocusInput("入力 简中", RegexInputCnControlName), regexInputChinese ?? string.Empty);
                GUI.SetNextControlName(RegexInputEnControlName);
                regexInputEnglish = EditorGUILayout.TextField(FocusInput("入力 EN", RegexInputEnControlName), regexInputEnglish ?? string.Empty); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("regexテスト", GUILayout.Height(24f)))
                    {
                        RunRegexTest();
                    }

                    if (GUILayout.Button("結果クリア", GUILayout.Height(24f)))
                    {
                        regexStatus = "未実行";
                        regexTestWarnings.Clear();
                        matchedRegexIds.Clear();
                        matchedPatternIndices.Clear();
                        currentRegexMatchResults.Clear();
                        otherCsvRegexMatchSections.Clear();
                        ResetUnknownWordPreview();
                    }
                }

                EditorGUILayout.HelpBox(regexStatus ?? string.Empty, MessageType.Info);
                foreach (string warning in regexTestWarnings.Distinct()) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                DrawUnknownWordPreview();
            }
            EditorGUILayout.Space(6f);

            if (currentRegexMatchResults.Count == 0)
            {
                EditorGUILayout.HelpBox("一致した regex_id はまだありません。", MessageType.None);
            }
            else
            {
                DrawCurrentRegexMatchResults();
            }

            EditorGUILayout.Space(8f);
            DrawOtherCsvRegexMatchResults();
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(FooterHeight)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!CanMoveLine(-1)))
                    {
                        if (GUILayout.Button("← 前Line", GUILayout.Height(26f)))
                        {
                            MoveLineSelection(-1);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!CanMoveLine(1)))
                    {
                        if (GUILayout.Button("→ 次Line", GUILayout.Height(26f)))
                        {
                            MoveLineSelection(1);
                        }
                    }

                    using (new EditorGUI.DisabledScope(currentPatternIndex <= 0 || patternGroups.Count == 0))
                    {
                        if (GUILayout.Button("← 前Pattern", GUILayout.Height(26f)))
                        {
                            MovePatternSelection(-1);
                        }
                    }

                    using (new EditorGUI.DisabledScope(patternGroups.Count == 0 || currentPatternIndex >= patternGroups.Count - 1))
                    {
                        if (GUILayout.Button("→ 次Pattern", GUILayout.Height(26f)))
                        {
                            MovePatternSelection(1);
                        }
                    }

                    if (GUILayout.Button("保存", GUILayout.Height(26f)))
                    {
                        SaveCurrentCsv();
                    }

                    if (GUILayout.Button("regexテスト", GUILayout.Height(26f)))
                    {
                        if (selectedTab != 1)
                        {
                            selectedTab = 1;
                        }

                        RunRegexTest();
                    }
                }

                DrawPatternJumpControls();



            }
        }

        private void DrawPatternJumpControls()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("ID", GUILayout.Width(72f));
                using (new EditorGUI.DisabledScope(patternGroups.Count == 0))
                {
                    jumpIdValue = EditorGUILayout.DelayedTextField(FocusInput(jumpIdValue ?? string.Empty), GUILayout.Width(96f));

                    if (GUILayout.Button("ジャンプ", GUILayout.Width(76f), GUILayout.Height(22f)))
                    {
                        JumpToInternalIdValue(jumpIdValue);
                    }
                }

                if (!string.IsNullOrEmpty(jumpPatternStatus))
                {
                    EditorGUILayout.LabelField(jumpPatternStatus, compactInfoStyle);
                }
            }
        }

        private void DrawFooterDetails()
        {
            if (selectedTab != 0 || patternGroups.Count == 0)
            {
                return;
            }

            DialoguePatternGroup group = GetCurrentGroup();
            DialogueEntry entry = group != null ? GetCurrentEntry(group) : null;
            if (group == null || entry == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"flow: {BuildDialogueFlowSummary(entry)} / response_type: {NormalizeResponseType(entry.ResponseType)} / choice: {BuildChoiceSummary(entry)} / action_id: {entry.ActionId ?? string.Empty} / condition: {entry.Condition ?? string.Empty} / speech_control: {BuildSpeechControlSummary(entry)} / call_only: {(entry.CallOnly ? "true" : "false")}",
                compactInfoStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("TextEdit Sceneを開く", GUILayout.Height(22f)))
                {
                    OpenTextEditScene();
                }

                if (GUILayout.Button("Sceneへ反映", GUILayout.Height(22f)))
                {
                    PushCurrentPreviewToTextEditScene(true);
                    ShowNotification(new GUIContent("TextEdit Scene updated"));
                }

                EditorGUILayout.LabelField($"Scene: {TextEditSceneUtility.TextEditScenePath}", compactInfoStyle);
            }
        }

        private void AddLineToCurrentPattern(DialoguePatternGroup group)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0 || dataSet == null || dataSet.Entries == null)
            {
                return;
            }

            DialogueEntry source = GetCurrentEntry(group) ?? GetRepresentativeEntry(group);
            if (source == null)
            {
                return;
            }

            DialogueEntry newEntry = CloneEntry(source);
            int insertSequenceIndex = Mathf.Clamp(currentSequenceIndex + 1, 0, group.Entries.Count);
            newEntry.Order = Mathf.Clamp(source.Order + 1, 1, group.Entries.Count + 1);
            newEntry.SourceLineNumber = GetNextSourceLineNumber();
            newEntry.Text = string.Empty;
            newEntry.TextChinese = string.Empty;
            newEntry.TextEnglish = string.Empty;

            int sourceDataIndex = dataSet.Entries.IndexOf(source);
            if (sourceDataIndex >= 0)
            {
                dataSet.Entries.Insert(sourceDataIndex + 1, newEntry);
            }
            else
            {
                dataSet.Entries.Add(newEntry);
            }

            group.Entries.Insert(insertSequenceIndex, newEntry);
            NormalizeGroupEntryOrders(group);
            hasPendingChanges = true;
            RebuildPatternCache(newEntry);
            Repaint();
        }

        private void DeleteCurrentLine(DialoguePatternGroup group)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0 || dataSet == null || dataSet.Entries == null)
            {
                return;
            }

            int removeIndex = Mathf.Clamp(currentSequenceIndex, 0, group.Entries.Count - 1);
            DialogueEntry entryToRemove = group.Entries[removeIndex];
            if (entryToRemove == null)
            {
                return;
            }

            bool deletesPattern = group.Entries.Count == 1;
            bool deletesId = deletesPattern && dataSet.Entries.Count(entry =>
                string.Equals(entry.InternalId?.Trim(), entryToRemove.InternalId?.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;
            string warning = deletesId
                ? $"ID {entryToRemove.InternalId} の最後の1行です。削除すると、このID自体が会話CSVから消えます。"
                : deletesPattern
                    ? $"Pattern {group.Pattern} の最後の1行です。削除すると、このPattern自体が消えます。"
                    : $"Line {removeIndex + 1} を削除します。";
            if (deletesPattern)
                warning += deleteLinkedIdsWithLastLine
                    ? "\n\nチェック済みのため、TalkTopic・独立日記リンク・共有されていない日記候補・Editor識別IDも保存時に削除します。"
                    : "\n\n連携IDは残します。削除した会話を指す関連データも残るため、必要なら後で整理してください。";
            bool confirmed = EditorUtility.DisplayDialog(
                "会話行の削除警告",
                warning + "\n\nこの操作を続けますか？",
                "削除する",
                "キャンセル");
            if (!confirmed)
            {
                return;
            }

            DialogueEntry focusEntry = removeIndex > 0
                ? group.Entries[removeIndex - 1]
                : (group.Entries.Count > 1 ? group.Entries[1] : FindAdjacentEntry(entryToRemove));

            if (deletesPattern && deleteLinkedIdsWithLastLine)
            {
                RemoveLinkedRecordsForDeletedDialogue(entryToRemove, dataSet.Entries.IndexOf(entryToRemove));
                pendingDeletedDialogueIds ??= new List<string>();
                if (!string.IsNullOrWhiteSpace(entryToRemove.EditorDialogueId) &&
                    !pendingDeletedDialogueIds.Contains(entryToRemove.EditorDialogueId))
                    pendingDeletedDialogueIds.Add(entryToRemove.EditorDialogueId);
            }

            dataSet.Entries.Remove(entryToRemove);
            group.Entries.RemoveAt(removeIndex);
            NormalizeGroupEntryOrders(group);
            hasPendingChanges = true;
            RebuildPatternCache(focusEntry);
            Repaint();
        }

        private DialogueEntry FindAdjacentEntry(DialogueEntry entry)
        {
            int index = dataSet.Entries.IndexOf(entry);
            if (index > 0) return dataSet.Entries[index - 1];
            return index >= 0 && index + 1 < dataSet.Entries.Count ? dataSet.Entries[index + 1] : null;
        }

        private void DrawStateLevelToolbar(Rect rect, string label, ref StateLevel level)
        {
            Rect labelRect = new Rect(rect.x, rect.y, 32f, rect.height);
            Rect toolbarRect = new Rect(rect.x + 34f, rect.y, Mathf.Max(60f, rect.width - 34f), rect.height);
            EditorGUI.LabelField(labelRect, label);
            string[] labels = { "Low", "High", "MAX" };
            int index = (int)level;
            int nextIndex = GUI.Toolbar(toolbarRect, index, labels);
            if (nextIndex != index)
            {
                level = (StateLevel)nextIndex;
                Repaint();
            }
        }

        private string DrawConditionDropdownEditors(string currentCondition, string currentEmotionChangeType, int currentEmotionChangeValue,
            out string nextEmotionChangeType, out int nextEmotionChangeValue)
        {
            var parsed = ParseConditionDropdownState(currentCondition);
            var selections = (string[])parsed.SelectedOptions.Clone();
            var negated = (bool[])parsed.IsNegatedOptions.Clone();
            bool changed = false;
            for (int i = 0; i < ConditionDropdownCount; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("条件" + (char)('A' + i), GUILayout.Width(45));
                    string category = GetAuthoringConditionCategory(selections[i]);
                    int categoryIndex = Array.IndexOf(AuthoringCategoryKeys, category);
                    int nextCategory = EditorGUILayout.Popup(FocusInput(Mathf.Max(0, categoryIndex)), AuthoringCategoryLabels, GUILayout.Width(145));
                    if (nextCategory != categoryIndex) { selections[i] = AuthoringCategoryKeys[nextCategory] == "I" ? NewPsychologyNumericConditions()[0] : ConditionValuesForCategory(AuthoringCategoryKeys[nextCategory]).FirstOrDefault(); negated[i] = false; changed = true; }
                    if (AuthoringCategoryKeys[nextCategory] == "I")
                    {
                        changed |= DrawPsychologyConditionSelection(i, ref selections[i], ref negated[i]);
                        continue;
                    }
                    string[] values = ConditionValuesForCategory(AuthoringCategoryKeys[nextCategory]);
                    string[] labels = new[] { "なし" }.Concat(values.SelectMany(value => new[] { ConditionValueLabel(value, false), ConditionValueLabel(value, true) })).ToArray();
                    int valueIndex = Array.IndexOf(values, selections[i]);
                    int index = valueIndex < 0 ? 0 : 1 + valueIndex * 2 + (negated[i] ? 1 : 0);
                    int next = EditorGUILayout.Popup(FocusInput(index), labels);
                    if (next != index) { selections[i] = next == 0 ? null : values[(next - 1) / 2]; negated[i] = next > 0 && (next - 1) % 2 == 1; changed = true; }
                }
            }
            string result = changed ? BuildConditionFromSelections(selections, negated, parsed) : currentCondition ?? "";
            DrawCompactNotice(parsed.HasOpaqueCondition ? "複雑なOR条件を保持中。二段選択を変更すると置き換えます。" : "同じ分類の短縮条件はOR、異なる分類はAND。表示外の条件は保持します。", parsed.HasOpaqueCondition ? MessageType.Warning : MessageType.Info);
            showConditionExpression = EditorGUILayout.Foldout(showConditionExpression, "条件式・会話後の心理変化", true);
            nextEmotionChangeType = currentEmotionChangeType; nextEmotionChangeValue = currentEmotionChangeValue;
            if (showConditionExpression)
            {
                result = EditorGUILayout.TextField(FocusInput("条件式"), result);
                int metric = Mathf.Max(0, Array.IndexOf(EmotionChangeTypeOptions, NormalizeEmotionChangeType(currentEmotionChangeType)));
                metric = EditorGUILayout.Popup(FocusInput("会話後の心理値"), metric, EmotionChangeTypeLabels);
                nextEmotionChangeType = EmotionChangeTypeOptions[metric];
                int value = EditorGUILayout.Popup(FocusInput("変化量"), GetEmotionChangeValuePopupIndex(currentEmotionChangeValue), EmotionChangeValueLabels);
                nextEmotionChangeValue = metric == 0 ? 0 : EmotionChangeValueOptions[value];
            }
            return result;
        }

        private static ParsedConditionDropdownState ParseConditionDropdownState(string condition)
        {
            ParsedConditionDropdownState parsed = new ParsedConditionDropdownState();
            if (string.IsNullOrWhiteSpace(condition))
            {
                return parsed;
            }

            string normalized = condition
                .Replace(" AND ", " && ")
                .Replace(" and ", " && ")
                .Replace("かつ", "&&")
                .Replace(" または ", " || ")
                .Replace(" OR ", " || ")
                .Replace(" or ", " || ")
                ;
            normalized = Regex.Replace(normalized, @"\bAND\b", "&&", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bOR\b", "||", RegexOptions.IgnoreCase);

            if (normalized.Contains("||"))
            {
                parsed.HasOpaqueCondition = true;
                return parsed;
            }

            string[] clauses = normalized.Split(new[] { "&&" }, StringSplitOptions.None);
            int selectionIndex = 0;
            for (int i = 0; i < clauses.Length; i++)
            {
                string clause = clauses[i].Trim();
                if (string.IsNullOrEmpty(clause))
                {
                    continue;
                }

                bool isNegated = clause.StartsWith("!", StringComparison.Ordinal);
                string selectableClause = isNegated ? clause.Substring(1).TrimStart() : clause;
                if (TryNormalizeConditionOption(selectableClause, out string option, out bool isLocationNegated))
                {
                    if (selectionIndex < ConditionDropdownCount)
                    {
                        parsed.SelectedOptions[selectionIndex] = option;
                        parsed.IsNegatedOptions[selectionIndex] = isNegated || isLocationNegated;
                        parsed.OriginalClauses[selectionIndex] = clause;
                        selectionIndex++;
                    }
                    else
                    {
                        parsed.PreservedClauses.Add(clause);
                    }
                }
                else
                {
                    parsed.PreservedClauses.Add(clause);
                }
            }

            return parsed;
        }

        private static bool TryNormalizeConditionOption(string clause, out string option, out bool isLocationNegated)
        {
            option = null;
            isLocationNegated = false;
            string normalized = TrimConditionValue(clause);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            Match inverse = Regex.Match(normalized, @"^(affection|sadistic|concern|hostility|obedience|instinct)\s*<\s*(60|70|100)$", RegexOptions.IgnoreCase);
            if (inverse.Success) { option = inverse.Groups[1].Value.ToLowerInvariant() + " >= " + inverse.Groups[2].Value; isLocationNegated = true; return true; }
            normalized = Regex.Replace(normalized, @"\s*(>=|==|!=)\s*", " $1 ");
            Match unequal = Regex.Match(normalized, @"^(affection|sadistic|concern|hostility|obedience|instinct) != 100$", RegexOptions.IgnoreCase);
            if (unequal.Success) { option = unequal.Groups[1].Value.ToLowerInvariant() + " == 100"; isLocationNegated = true; return true; }
            if (normalized.StartsWith("PsychologyState != ", StringComparison.OrdinalIgnoreCase))
            { normalized = "PsychologyState == " + normalized.Substring("PsychologyState != ".Length); isLocationNegated = true; }
            Match locationMatch = LocationConditionOptionRegex.Match(normalized);
            if (locationMatch.Success)
            {
                string locationOption = $"H: {locationMatch.Groups["location"].Value}";
                if (Array.IndexOf(SharedConditionOptions, locationOption) >= 0)
                {
                    option = locationOption;
                    isLocationNegated = locationMatch.Groups["negated"].Success;
                    return true;
                }

                return false;
            }

            for (int i = 0; i < SharedConditionOptions.Length; i++)
            {
                if (string.Equals(SharedConditionOptions[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    option = SharedConditionOptions[i];
                    return true;
                }
            }

            return false;
        }

        private static string BuildConditionFromSelections(
            IList<string> selections,
            IList<bool> negatedSelections,
            ParsedConditionDropdownState parsed)
        {
            List<string> clauses = new List<string>();
            HashSet<string> selectedClauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (selections != null)
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    string selection = selections[i];
                    if (string.IsNullOrWhiteSpace(selection))
                    {
                        continue;
                    }

                    bool isNegated = negatedSelections != null && i < negatedSelections.Count && negatedSelections[i];
                    string clause = parsed != null && i < parsed.SelectedOptions.Length &&
                        selection == parsed.SelectedOptions[i] && isNegated == parsed.IsNegatedOptions[i] && !string.IsNullOrEmpty(parsed.OriginalClauses[i])
                        ? parsed.OriginalClauses[i] : BuildConditionClause(selection, isNegated);
                    clauses.Add(clause);
                    selectedClauses.Add(clause);
                }
            }

            if (parsed != null && !parsed.HasOpaqueCondition)
            {
                for (int i = 0; i < parsed.PreservedClauses.Count; i++)
                {
                    string clause = parsed.PreservedClauses[i];
                    if (selectedClauses.Contains(clause))
                    {
                        continue;
                    }

                    clauses.Add(clause);
                }
            }

            return string.Join(" && ", clauses);
        }

        private static string BuildConditionClause(string option, bool isNegated)
        {
            if (isNegated && GetAuthoringConditionCategory(option) == "I") return option.Contains("==") ? option.Replace("==", "!=") : option.Replace(">=", "<");
            if (option != null && option.StartsWith("H: ", StringComparison.Ordinal))
            {
                return $"H: {(isNegated ? "!" : string.Empty)}{option.Substring(3)}";
            }

            return isNegated ? "!" + option : option;
        }

        private void ApplyStateToolbarConditionChange()
        {
            DialoguePatternGroup group = GetCurrentGroup();
            DialogueEntry representative = GetRepresentativeEntry(group);
            if (group == null || representative == null)
            {
                return;
            }

            string currentCondition = representative.Condition ?? string.Empty;
            string nextCondition = BuildConditionWithStateLevels(currentCondition, state);
            if (string.Equals(currentCondition, nextCondition, StringComparison.Ordinal))
            {
                return;
            }

            ApplyConditionChange(group, nextCondition);
            hasPendingChanges = true;
            RebuildPatternCache(representative);
            Repaint();
        }

        private static string BuildConditionWithStateLevels(string currentCondition, DialogueState sourceState)
        {
            List<string> clauses = GetNonStateConditionClauses(currentCondition);
            AppendStateConditionClause(clauses, "affection", sourceState != null ? sourceState.affectionLevel : StateLevel.Low);
            AppendStateConditionClause(clauses, "sadistic", sourceState != null ? sourceState.sadisticLevel : StateLevel.Low);
            AppendStateConditionClause(clauses, "concern", sourceState != null ? sourceState.concernLevel : StateLevel.Low);
            AppendStateConditionClause(clauses, "hostility", sourceState != null ? sourceState.hostilityLevel : StateLevel.Low);
            AppendStateConditionClause(clauses, "obedience", sourceState != null ? sourceState.obedienceLevel : StateLevel.Low);
            AppendStateConditionClause(clauses, "instinct", sourceState != null ? sourceState.instinctLevel : StateLevel.Low);
            return string.Join(" && ", clauses);
        }

        private static List<string> GetNonStateConditionClauses(string condition)
        {
            List<string> clauses = new List<string>();
            string normalized = (condition ?? string.Empty)
                .Replace(" AND ", " && ")
                .Replace(" and ", " && ")
                .Replace("かつ", "&&");
            string[] splitClauses = normalized.Split(new[] { "&&" }, StringSplitOptions.None);
            for (int i = 0; i < splitClauses.Length; i++)
            {
                string clause = splitClauses[i].Trim();
                if (string.IsNullOrWhiteSpace(clause) || IsStateConditionClause(clause))
                {
                    continue;
                }

                clauses.Add(clause);
            }

            return clauses;
        }

        private static bool IsStateConditionClause(string clause)
        {
            Match match = StateConditionClauseRegex.Match(clause ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            return !string.IsNullOrEmpty(NormalizeStateMetricKey(match.Groups["key"].Value));
        }

        private static void AppendStateConditionClause(List<string> clauses, string key, StateLevel level)
        {
            if (clauses == null || level <= StateLevel.Low)
            {
                return;
            }

            clauses.Add($"{key} >= {ToStateNumericValue(level)}");
        }

        private static string BuildLineDisplayLabel(DialogueEntry entry)
        {
            if (entry == null)
            {
                return "?-0";
            }

            string pattern = string.IsNullOrWhiteSpace(entry.Pattern) ? "?" : entry.Pattern.Trim();
            return $"{pattern}-{entry.Order}";
        }

        private static int ParsePatternNumber(string pattern)
        {
            if (int.TryParse(pattern, out int parsed))
            {
                return parsed;
            }

            return 1;
        }

        private int DrawPriorityPopup(string label, int currentValue)
        {
            int currentIndex = GetPriorityOptionIndex(currentValue);
            int nextIndex = EditorGUILayout.Popup(FocusInput(label), currentIndex, PriorityOptionLabels);
            return PriorityOptionValues[Mathf.Clamp(nextIndex, 0, PriorityOptionValues.Length - 1)];
        }

        private static int GetPriorityOptionIndex(int value)
        {
            int index = Array.IndexOf(PriorityOptionValues, value);
            if (index >= 0)
            {
                return index;
            }

            int nearestIndex = 0;
            int nearestDistance = Mathf.Abs(PriorityOptionValues[0] - value);
            for (int i = 1; i < PriorityOptionValues.Length; i++)
            {
                int distance = Mathf.Abs(PriorityOptionValues[i] - value);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private string DrawSpeechControlPopup(string label, string currentValue)
        {
            string normalized = NormalizeSpeechControl(currentValue);
            int currentIndex = GetSpeechControlOptionIndex(normalized);
            int nextIndex = EditorGUILayout.Popup(FocusInput(label), currentIndex, SpeechControlOptionLabels);
            return SpeechControlOptionValues[Mathf.Clamp(nextIndex, 0, SpeechControlOptionValues.Length - 1)];
        }

        private static int GetSpeechControlOptionIndex(string value)
        {
            int index = Array.IndexOf(SpeechControlOptionValues, NormalizeSpeechControl(value));
            if (index >= 0)
            {
                return index;
            }

            return 0;
        }

        private static int GetResponseTypeOptionIndex(string value)
        {
            int index = Array.IndexOf(ResponseTypeOptionValues, NormalizeResponseTypeStatic(value));
            if (index >= 0)
            {
                return index;
            }

            return 0;
        }

        private SerializedProperty FindEntryProperty(SerializedObject serializedWindow, DialogueEntry entry)
        {
            if (serializedWindow == null || entry == null)
            {
                return null;
            }

            SerializedProperty dataSetProperty = serializedWindow.FindProperty("dataSet");
            SerializedProperty entriesProperty = dataSetProperty != null ? dataSetProperty.FindPropertyRelative("Entries") : null;
            if (entriesProperty == null)
            {
                return null;
            }

            int entryIndex = dataSet != null && dataSet.Entries != null ? dataSet.Entries.IndexOf(entry) : -1;
            if (entryIndex < 0 || entryIndex >= entriesProperty.arraySize)
            {
                return null;
            }

            return entriesProperty.GetArrayElementAtIndex(entryIndex);
        }

        private static SerializedProperty FindAdditionalFieldValueProperty(SerializedProperty additionalFieldsProperty, string key)
        {
            if (additionalFieldsProperty == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            for (int i = 0; i < additionalFieldsProperty.arraySize; i++)
            {
                SerializedProperty elementProperty = additionalFieldsProperty.GetArrayElementAtIndex(i);
                SerializedProperty keyProperty = elementProperty.FindPropertyRelative("Key");
                if (keyProperty != null && string.Equals(keyProperty.stringValue, key, StringComparison.OrdinalIgnoreCase))
                {
                    return elementProperty.FindPropertyRelative("Value");
                }
            }

            return null;
        }

        private static string NormalizeSpeechControl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (string.Equals(value, "RepeatEvent", StringComparison.OrdinalIgnoreCase))
            {
                return "RepeatEvent";
            }

            if (string.Equals(value, "Sequence", StringComparison.OrdinalIgnoreCase))
            {
                return "Sequence";
            }

            if (string.Equals(value, "Random", StringComparison.OrdinalIgnoreCase))
            {
                return "Random";
            }

            if (string.Equals(value, "Call", StringComparison.OrdinalIgnoreCase))
            {
                return "Call";
            }

            if (string.Equals(value, "Return", StringComparison.OrdinalIgnoreCase))
            {
                return "Return";
            }

            return string.Empty;
        }

        private static string NormalizeResponseTypeStatic(string responseType)
        {
            if (string.IsNullOrWhiteSpace(responseType))
            {
                return "Normal";
            }

            if (string.Equals(responseType, "Choice", StringComparison.OrdinalIgnoreCase))
            {
                return "Choice";
            }

            if (string.Equals(responseType, "Action", StringComparison.OrdinalIgnoreCase))
            {
                return "Action";
            }

            if (string.Equals(responseType, "Event", StringComparison.OrdinalIgnoreCase))
            {
                return "Event";
            }

            if (string.Equals(responseType, "Reaction", StringComparison.OrdinalIgnoreCase))
            {
                return "Reaction";
            }

            return "Normal";
        }

        private void EnsureChoiceAdditionalFields(DialogueEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            EnsureAdditionalField(entry, ChoiceQuestionJaKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceQuestionEnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceQuestionCnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceYesJaKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceYesEnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceYesCnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceNoJaKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceNoEnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceNoCnKey, string.Empty);
            EnsureAdditionalField(entry, ChoiceYesNextPatternKey, entry.ChoiceYesPattern ?? string.Empty);
            EnsureAdditionalField(entry, ChoiceNoNextPatternKey, entry.ChoiceNoPattern ?? string.Empty);
        }

        private void DuplicateChoiceSettingsToOtherPatterns(DialogueEntry sourceEntry)
        {
            DialoguePatternGroup sourceGroup = GetCurrentGroup();
            if (sourceEntry == null || sourceGroup == null || patternGroups.Count == 0)
            {
                return;
            }

            EnsureChoiceAdditionalFields(sourceEntry);

            string sourceInternalId = sourceEntry.InternalId ?? string.Empty;
            string sourcePattern = sourceEntry.Pattern ?? string.Empty;
            int copiedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < patternGroups.Count; i++)
            {
                DialoguePatternGroup targetGroup = patternGroups[i];
                DialogueEntry representative = targetGroup != null ? targetGroup.GetRepresentativeEntry() : null;
                if (targetGroup == null || representative == null || targetGroup.Entries == null || targetGroup.Entries.Count == 0)
                {
                    continue;
                }

                if (!string.Equals(representative.InternalId ?? string.Empty, sourceInternalId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(targetGroup.Pattern ?? string.Empty, sourcePattern, StringComparison.Ordinal))
                {
                    continue;
                }

                DialogueEntry targetEntry = GetLastOrderEntry(targetGroup);
                if (targetEntry == null)
                {
                    continue;
                }

                EnsureChoiceAdditionalFields(targetEntry);
                if (HasDifferentChoiceText(sourceEntry, targetEntry))
                {
                    string targetLabel = BuildPatternOrderLabel(targetEntry);
                    bool overwrite = EditorUtility.DisplayDialog(
                        "Choice設定の上書き",
                        $"{targetLabel}には設定済みです。上書きしますか？",
                        "上書き",
                        "スキップ");
                    if (!overwrite)
                    {
                        skippedCount++;
                        continue;
                    }
                }

                CopyChoiceSettings(sourceEntry, targetEntry);
                targetEntry.ResponseType = "Choice";
                copiedCount++;
            }

            if (copiedCount > 0)
            {
                hasPendingChanges = true;
                RebuildPatternCache(sourceEntry);
                Repaint();
            }

            ShowNotification(new GUIContent($"Choice複製: {copiedCount}件 / スキップ: {skippedCount}件"));
        }

        private DialogueEntry GetLastOrderEntry(DialoguePatternGroup group)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return null;
            }

            DialogueEntry last = null;
            for (int i = 0; i < group.Entries.Count; i++)
            {
                DialogueEntry entry = group.Entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (last == null || entry.Order > last.Order)
                {
                    last = entry;
                }
            }

            return last;
        }

        private bool HasDifferentChoiceText(DialogueEntry sourceEntry, DialogueEntry targetEntry)
        {
            if (!HasAnyChoiceText(targetEntry))
            {
                return false;
            }

            List<string> sourceKeys = GetChoiceTextKeys(sourceEntry);
            List<string> targetKeys = GetChoiceTextKeys(targetEntry);
            HashSet<string> allKeys = new HashSet<string>(sourceKeys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(targetKeys);

            foreach (string key in allKeys)
            {
                string sourceValue = sourceEntry.GetAdditionalValue(key) ?? string.Empty;
                string targetValue = targetEntry.GetAdditionalValue(key) ?? string.Empty;
                if (!string.Equals(sourceValue, targetValue, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyChoiceText(DialogueEntry entry)
        {
            List<string> keys = GetChoiceTextKeys(entry);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(entry.GetAdditionalValue(keys[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> GetChoiceTextKeys(DialogueEntry entry)
        {
            List<string> keys = new List<string>
            {
                ChoiceQuestionJaKey,
                ChoiceQuestionEnKey,
                ChoiceQuestionCnKey,
                ChoiceYesJaKey,
                ChoiceYesEnKey,
                ChoiceYesCnKey,
                ChoiceNoJaKey,
                ChoiceNoEnKey,
                ChoiceNoCnKey
            };

            List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(entry);
            for (int i = 0; i < additionalChoiceNumbers.Count; i++)
            {
                int choiceNumber = additionalChoiceNumbers[i];
                keys.Add(GetAdditionalChoiceLanguageKey(choiceNumber, "ja"));
                keys.Add(GetAdditionalChoiceLanguageKey(choiceNumber, "en"));
                keys.Add(GetAdditionalChoiceLanguageKey(choiceNumber, "cn"));
            }

            return keys;
        }

        private void CopyChoiceSettings(DialogueEntry sourceEntry, DialogueEntry targetEntry)
        {
            ClearChoiceAdditionalFields(targetEntry);
            EnsureChoiceAdditionalFields(targetEntry);

            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceQuestionJaKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceQuestionEnKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceQuestionCnKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceYesJaKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceYesEnKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceYesCnKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceNoJaKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceNoEnKey);
            CopyAdditionalValue(sourceEntry, targetEntry, ChoiceNoCnKey);

            targetEntry.ChoiceYesPattern = sourceEntry.ChoiceYesPattern ?? string.Empty;
            targetEntry.ChoiceNoPattern = sourceEntry.ChoiceNoPattern ?? string.Empty;
            targetEntry.ActionId = sourceEntry.ActionId ?? string.Empty;
            targetEntry.TimedEventKey = sourceEntry.TimedEventKey ?? string.Empty;
            targetEntry.SetAdditionalValue(ChoiceYesNextPatternKey, targetEntry.ChoiceYesPattern);
            targetEntry.SetAdditionalValue(ChoiceNoNextPatternKey, targetEntry.ChoiceNoPattern);

            List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(sourceEntry);
            for (int i = 0; i < additionalChoiceNumbers.Count; i++)
            {
                int choiceNumber = additionalChoiceNumbers[i];
                CopyAdditionalValue(sourceEntry, targetEntry, GetAdditionalChoiceLanguageKey(choiceNumber, "ja"));
                CopyAdditionalValue(sourceEntry, targetEntry, GetAdditionalChoiceLanguageKey(choiceNumber, "en"));
                CopyAdditionalValue(sourceEntry, targetEntry, GetAdditionalChoiceLanguageKey(choiceNumber, "cn"));
                CopyAdditionalValue(sourceEntry, targetEntry, GetAdditionalChoicePatternKey(choiceNumber));
            }
        }

        private static void CopyAdditionalValue(DialogueEntry sourceEntry, DialogueEntry targetEntry, string key)
        {
            targetEntry.SetAdditionalValue(key, sourceEntry.GetAdditionalValue(key) ?? string.Empty);
        }

        private void ClearChoiceAdditionalFields(DialogueEntry entry)
        {
            RemoveAdditionalField(entry, ChoiceQuestionJaKey);
            RemoveAdditionalField(entry, ChoiceQuestionEnKey);
            RemoveAdditionalField(entry, ChoiceQuestionCnKey);
            RemoveAdditionalField(entry, ChoiceYesJaKey);
            RemoveAdditionalField(entry, ChoiceYesEnKey);
            RemoveAdditionalField(entry, ChoiceYesCnKey);
            RemoveAdditionalField(entry, ChoiceNoJaKey);
            RemoveAdditionalField(entry, ChoiceNoEnKey);
            RemoveAdditionalField(entry, ChoiceNoCnKey);
            RemoveAdditionalField(entry, ChoiceYesNextPatternKey);
            RemoveAdditionalField(entry, ChoiceNoNextPatternKey);

            List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(entry);
            for (int i = 0; i < additionalChoiceNumbers.Count; i++)
            {
                int choiceNumber = additionalChoiceNumbers[i];
                RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(choiceNumber, "ja"));
                RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(choiceNumber, "en"));
                RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(choiceNumber, "cn"));
                RemoveAdditionalField(entry, GetAdditionalChoicePatternKey(choiceNumber));
            }
        }

        private static string BuildPatternOrderLabel(DialogueEntry entry)
        {
            string pattern = string.IsNullOrWhiteSpace(entry?.Pattern) ? "?" : entry.Pattern.Trim();
            int order = entry != null ? entry.Order : 0;
            return $"{pattern}-{order}";
        }

        private static void EnsureAdditionalField(DialogueEntry entry, string key, string defaultValue)
        {
            if (entry == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string existing = entry.GetAdditionalValue(key);
            if (!string.IsNullOrEmpty(existing))
            {
                return;
            }

            entry.SetAdditionalValue(key, defaultValue ?? string.Empty);
        }

        private bool DrawChoiceLocalizedRow(
            DialogueEntry entry,
            string label,
            string japaneseKey,
            string englishKey,
            string chineseKey)
        {
            string currentJapanese = entry.GetAdditionalValue(japaneseKey);
            string currentEnglish = entry.GetAdditionalValue(englishKey);
            string currentChinese = entry.GetAdditionalValue(chineseKey);

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string nextJapanese = DrawChoiceLanguageField("JP", currentJapanese, 1.4f);
                string nextEnglish = showOtherLanguages ? DrawChoiceLanguageField("EN", currentEnglish, 1f) : currentEnglish;
                string nextChinese = showOtherLanguages ? DrawChoiceLanguageField("CN", currentChinese, 1f) : currentChinese;

                bool changed =
                    !string.Equals(nextJapanese, currentJapanese, StringComparison.Ordinal) ||
                    !string.Equals(nextEnglish, currentEnglish, StringComparison.Ordinal) ||
                    !string.Equals(nextChinese, currentChinese, StringComparison.Ordinal);

                if (changed)
                {
                    entry.SetAdditionalValue(japaneseKey, nextJapanese);
                    entry.SetAdditionalValue(englishKey, nextEnglish);
                    entry.SetAdditionalValue(chineseKey, nextChinese);
                }

                return changed;
            }
        }

        private bool DrawChoiceChoiceRow(
            DialogueEntry entry,
            string choiceLabel,
            string japaneseKey,
            string englishKey,
            string chineseKey,
            string mirroredPatternKey,
            Func<DialogueEntry, string> getPattern,
            Action<DialogueEntry, string> setPattern)
        {
            bool changed = DrawChoiceLocalizedRow(entry, $"{choiceLabel}選択肢", japaneseKey, englishKey, chineseKey);
            string currentPattern = getPattern != null ? getPattern(entry) : string.Empty;
            string mirroredPattern = entry.GetAdditionalValue(mirroredPatternKey);
            string preferredPattern = !string.IsNullOrWhiteSpace(mirroredPattern) ? mirroredPattern : currentPattern;
            string nextPattern = DrawChoicePatternField(
                $"{choiceLabel}遷移先 Pattern番号（空欄で即Action）",
                preferredPattern);

            if (!string.Equals(nextPattern, currentPattern ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(nextPattern, mirroredPattern ?? string.Empty, StringComparison.Ordinal))
            {
                setPattern?.Invoke(entry, nextPattern);
                entry.SetAdditionalValue(mirroredPatternKey, nextPattern);
                changed = true;
            }

            return changed;
        }

        private string DrawChoiceLanguageField(string label, string value, float widthWeight)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(24f));
                return EditorGUILayout.TextField(FocusInput(value ?? string.Empty), GUILayout.MinWidth(120f * widthWeight));
            }
        }

        private string DrawChoicePatternField(string label, string currentValue)
        {
            string nextValue = EditorGUILayout.TextField(FocusInput(label), currentValue ?? string.Empty);
            return string.IsNullOrWhiteSpace(nextValue)
                ? string.Empty
                : new string(nextValue.Where(char.IsDigit).ToArray());
        }

        private List<int> GetAdditionalChoiceNumbers(DialogueEntry entry)
        {
            List<int> numbers = new List<int>();
            if (entry == null || entry.AdditionalFields == null)
            {
                return numbers;
            }

            HashSet<int> uniqueNumbers = new HashSet<int>();
            for (int i = 0; i < entry.AdditionalFields.Count; i++)
            {
                DialogueExtraField field = entry.AdditionalFields[i];
                if (field == null || string.IsNullOrWhiteSpace(field.Key))
                {
                    continue;
                }

                Match match = AdditionalChoiceKeyRegex.Match(field.Key);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int choiceNumber) || choiceNumber < 3)
                {
                    continue;
                }

                uniqueNumbers.Add(choiceNumber);
            }

            numbers.AddRange(uniqueNumbers);
            numbers.Sort();
            return numbers;
        }

        private void AddAdditionalChoiceOption(DialogueEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            List<int> numbers = GetAdditionalChoiceNumbers(entry);
            int nextNumber = numbers.Count > 0 ? numbers[numbers.Count - 1] + 1 : 3;
            EnsureAdditionalField(entry, GetAdditionalChoiceLanguageKey(nextNumber, "ja"), string.Empty);
            EnsureAdditionalField(entry, GetAdditionalChoiceLanguageKey(nextNumber, "en"), string.Empty);
            EnsureAdditionalField(entry, GetAdditionalChoiceLanguageKey(nextNumber, "cn"), string.Empty);
            EnsureAdditionalField(entry, GetAdditionalChoicePatternKey(nextNumber), string.Empty);
        }

        private void RemoveLastAdditionalChoiceOption(DialogueEntry entry, List<int> existingNumbers)
        {
            if (entry == null || existingNumbers == null || existingNumbers.Count == 0)
            {
                return;
            }

            int removeNumber = existingNumbers[existingNumbers.Count - 1];
            RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(removeNumber, "ja"));
            RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(removeNumber, "en"));
            RemoveAdditionalField(entry, GetAdditionalChoiceLanguageKey(removeNumber, "cn"));
            RemoveAdditionalField(entry, GetAdditionalChoicePatternKey(removeNumber));
        }

        private static void RemoveAdditionalField(DialogueEntry entry, string key)
        {
            if (entry == null || entry.AdditionalFields == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            for (int i = entry.AdditionalFields.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entry.AdditionalFields[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    entry.AdditionalFields.RemoveAt(i);
                }
            }
        }

        private static string GetAdditionalChoiceLanguageKey(int choiceNumber, string languageCode)
        {
            return $"choice_{choiceNumber}_{languageCode}";
        }

        private static string GetAdditionalChoicePatternKey(int choiceNumber)
        {
            return $"choice_{choiceNumber}_pattern";
        }

        private static string NormalizeTargetPatternValue(string targetPattern)
        {
            return string.IsNullOrWhiteSpace(targetPattern) ? string.Empty : targetPattern.Trim();
        }

        private static bool IsPerEntrySpeechControl(string speechControl)
        {
            string normalized = NormalizeSpeechControl(speechControl);
            return string.Equals(normalized, "Call", StringComparison.Ordinal) ||
                   string.Equals(normalized, "Return", StringComparison.Ordinal);
        }

        private static void ApplySpeechControlChange(DialoguePatternGroup group, DialogueEntry currentEntry, string speechControl, string normalizedTargetPattern)
        {
            if (group == null || currentEntry == null)
            {
                return;
            }

            if (IsPerEntrySpeechControl(speechControl))
            {
                currentEntry.SpeechControl = speechControl;
                currentEntry.TargetPattern = normalizedTargetPattern;
                return;
            }

            for (int i = 0; i < group.Entries.Count; i++)
            {
                group.Entries[i].SpeechControl = speechControl;
            }

            currentEntry.TargetPattern = normalizedTargetPattern;
        }

        private static void ApplyEmotionChange(DialoguePatternGroup group, string emotionChangeType, int emotionChangeValue)
        {
            if (group == null)
            {
                return;
            }

            string normalizedEmotionChangeType = NormalizeEmotionChangeType(emotionChangeType);
            int normalizedEmotionChangeValue = string.IsNullOrEmpty(normalizedEmotionChangeType)
                ? 0
                : NormalizeEmotionChangeValue(emotionChangeValue);

            for (int i = 0; i < group.Entries.Count; i++)
            {
                group.Entries[i].EmotionChangeType = normalizedEmotionChangeType;
                group.Entries[i].EmotionChangeValue = normalizedEmotionChangeValue;
            }
        }

        private static void ApplyConditionChange(DialoguePatternGroup group, string condition)
        {
            if (group == null)
            {
                return;
            }

            string normalizedCondition = condition ?? string.Empty;
            for (int i = 0; i < group.Entries.Count; i++)
            {
                group.Entries[i].Condition = normalizedCondition;
            }
        }

        private static string BuildSpeechControlBadge(DialogueEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string normalized = NormalizeSpeechControl(entry.SpeechControl);
            if (string.Equals(normalized, "Call", StringComparison.Ordinal))
            {
                string targetPattern = string.IsNullOrWhiteSpace(entry.TargetPattern) ? "?" : entry.TargetPattern.Trim();
                return $"[CALL:{targetPattern}]";
            }

            if (string.Equals(normalized, "Return", StringComparison.Ordinal))
            {
                return "[RETURN]";
            }

            return string.Empty;
        }

        private static string BuildSpeechControlSummary(DialogueEntry entry)
        {
            if (entry == null)
            {
                return "未設定";
            }

            string normalized = NormalizeSpeechControl(entry.SpeechControl);
            if (string.IsNullOrEmpty(normalized))
            {
                return "未設定";
            }

            if (string.Equals(normalized, "Call", StringComparison.Ordinal))
            {
                string targetPattern = string.IsNullOrWhiteSpace(entry.TargetPattern) ? "?" : entry.TargetPattern.Trim();
                return $"Call->{targetPattern}";
            }

            return normalized;
        }

        private static string BuildEntryPreviewText(DialogueEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string badge = BuildSpeechControlBadge(entry);
            if (string.IsNullOrEmpty(badge))
            {
                return entry.Text ?? string.Empty;
            }

            return $"{badge} {entry.Text ?? string.Empty}";
        }

        private static string TrimConditionValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            if ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static int ConditionOptionToPopupIndex(string option)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                return 0;
            }

            int optionIndex = Array.IndexOf(SharedConditionOptions, option);
            return optionIndex < 0 ? 0 : optionIndex + 1;
        }

        private static string PopupIndexToConditionOption(int index)
        {
            if (index <= 0 || index > SharedConditionOptions.Length)
            {
                return null;
            }

            return SharedConditionOptions[index - 1];
        }

        private static string[] BuildSharedConditionOptions()
        {
            List<string> options = new List<string>
            {
                "Winter",
                "Fall",
                "Summer",
                "Spring",
                "Snow",
                "Sunny",
                "Rain",
                "CatHungry",
                "PlayerHungry",
                "Night",
                "Evening",
                "Day",
                "Morning",
                "Male",
                "Female",
                "During estrus",
                "PlayerDefeatedByNekomata",
                "PlayerDefeatedByPredation",
                "PlayerDefeatedByAnger",
                "PlayerDefeatedByAccident"
            };

            string[] locationIds = Enum.GetNames(typeof(CatHomeLocation));
            for (int i = 0; i < locationIds.Length; i++)
            {
                options.Add($"H: {locationIds[i]}");
            }

            foreach (string metric in EmotionChangeTypeOptions.Where(value => !string.IsNullOrEmpty(value)))
            { options.Add(metric + " >= 60"); options.Add(metric + " >= 100"); }
            foreach (string metric in EmotionChangeTypeOptions.Where(value => !string.IsNullOrEmpty(value))) options.Add(metric + " == 100");
            options.Add("instinct >= 70");
            string[] psychologyStates = { "NORMAL", "HOSTILITY", "SADISTIC", "CONCERN", "AFFECTION", "OBEDIENCE", "INSTINCT", "MATING", "AFFECTION_OBEDIENCE", "INSTINCT_OBEDIENCE", "HOSTILITY_SADISTIC", "CONCERN_HOSTILITY", "AFFECTION_HOSTILITY", "HOSTILITY_INSTINCT", "AFFECTION_CONCERN", "AFFECTION_SADISTIC", "AFFECTION_INSTINCT", "CONCERN_SADISTIC", "CONCERN_OBEDIENCE", "CONCERN_INSTINCT", "INSTINCT_SADISTIC", "HOSTILITY_OBEDIENCE", "OBEDIENCE_SADISTIC" };
            foreach (string psychologyState in psychologyStates) options.Add("PsychologyState == \"" + psychologyState + "\"");
            foreach (string condition in NewPsychologyNumericConditions().Concat(NewPsychologyStateConditions()))
                if (!options.Contains(condition)) options.Add(condition);
            return options.ToArray();
        }

        private static string[] BuildConditionDropdownOptions()
        {
            string[] options = new string[SharedConditionOptions.Length + 1];
            options[0] = "なし";
            for (int i = 0; i < SharedConditionOptions.Length; i++)
            {
                string option = SharedConditionOptions[i];
                options[i + 1] = option.StartsWith("H: ", StringComparison.Ordinal)
                    ? option
                    : $"{GetConditionCategoryLabel(option)}: {option}";
            }

            return options;
        }

        private static string GetConditionCategoryLabel(string option)
        {
            switch (option)
            {
                case "Winter":
                case "Fall":
                case "Summer":
                case "Spring":
                    return "A";
                case "Snow":
                case "Sunny":
                case "Rain":
                    return "B";
                case "CatHungry":
                case "PlayerHungry":
                    return "C";
                case "Night":
                case "Evening":
                case "Day":
                case "Morning":
                    return "D";
                case "Male":
                case "Female":
                    return "E";
                case "During estrus":
                    return "F";
                case "PlayerDefeatedByNekomata":
                case "PlayerDefeatedByPredation":
                case "PlayerDefeatedByAnger":
                case "PlayerDefeatedByAccident":
                    return "G";
                default:
                    return "-";
            }
        }

        private void HandleKeyboardNavigation()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            bool alt = (currentEvent.modifiers & EventModifiers.Alt) != 0;
            bool shift = (currentEvent.modifiers & EventModifiers.Shift) != 0;

            if (alt && currentEvent.keyCode == KeyCode.PageUp)
            {
                if (shift)
                {
                    MovePatternSelection(-1);
                }
                else
                {
                    MoveLineSelection(-1);
                }

                currentEvent.Use();
                return;
            }

            if (alt && currentEvent.keyCode == KeyCode.PageDown)
            {
                if (shift)
                {
                    MovePatternSelection(1);
                }
                else
                {
                    MoveLineSelection(1);
                }

                currentEvent.Use();
                return;
            }

            if (selectedTab == 1 && IsSubmitKey(currentEvent) && IsRegexInputFocused())
            {
                RunRegexTest();
                currentEvent.Use();
                Repaint();
                return;
            }

            if (EditorGUIUtility.editingTextField)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.LeftArrow)
            {
                if (shift)
                {
                    MovePatternSelection(-1);
                }
                else
                {
                    MoveLineSelection(-1);
                }

                currentEvent.Use();
            }
            else if (currentEvent.keyCode == KeyCode.RightArrow)
            {
                if (shift)
                {
                    MovePatternSelection(1);
                }
                else
                {
                    MoveLineSelection(1);
                }

                currentEvent.Use();
            }
        }

        private static bool IsSubmitKey(Event currentEvent)
        {
            return currentEvent != null &&
                   (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);
        }

        private static bool IsRegexInputFocused()
        {
            string focusedControlName = GUI.GetNameOfFocusedControl();
            return string.Equals(focusedControlName, RegexInputJaControlName, StringComparison.Ordinal) ||
                   string.Equals(focusedControlName, RegexInputCnControlName, StringComparison.Ordinal) ||
                   string.Equals(focusedControlName, RegexInputEnControlName, StringComparison.Ordinal);
        }

        private void MovePatternSelection(int direction)
        {
            if (patternGroups.Count == 0)
            {
                return;
            }

            ClearActiveTextFieldFocus();
            currentPatternIndex = Mathf.Clamp(currentPatternIndex + direction, 0, patternGroups.Count - 1);
            DialoguePatternGroup group = GetCurrentGroup();
            if (group != null && group.Entries != null && group.Entries.Count > 0)
            {
                currentSequenceIndex = Mathf.Clamp(currentSequenceIndex, 0, group.Entries.Count - 1);
            }
            else
            {
                currentSequenceIndex = 0;
            }

            SyncStateFromCurrentSelectionIfNeeded(true);
            Repaint();
        }

        private void JumpToInternalIdValue(string idValue)
        {
            if (patternGroups.Count == 0)
            {
                jumpPatternStatus = "ID がありません。";
                return;
            }

            int targetIndex = FindPatternIndexByInternalIdValue(idValue);
            if (targetIndex < 0)
            {
                jumpPatternStatus = $"ID {idValue} は見つかりません。";
                Repaint();
                return;
            }

            ClearActiveTextFieldFocus();
            currentPatternIndex = targetIndex;

            DialoguePatternGroup group = GetCurrentGroup();
            currentSequenceIndex = group != null && group.Entries != null && group.Entries.Count > 0 ? 0 : 0;
            jumpPatternStatus = $"ID {idValue} へ移動";

            SyncStateFromCurrentSelectionIfNeeded(true);
            Repaint();
        }

        private int FindPatternIndexByInternalIdValue(string idValue)
        {
            string normalizedId = string.IsNullOrWhiteSpace(idValue) ? string.Empty : idValue.Trim();
            int numericFallbackIndex = -1;
            bool hasNumericTarget = long.TryParse(normalizedId, out long numericTargetId);
            for (int i = 0; i < patternGroups.Count; i++)
            {
                DialoguePatternGroup group = patternGroups[i];
                if (group == null)
                {
                    continue;
                }

                DialogueEntry representative = group.GetRepresentativeEntry();
                string groupId = representative != null ? (representative.InternalId ?? string.Empty).Trim() : string.Empty;
                if (string.Equals(groupId, normalizedId, StringComparison.Ordinal))
                {
                    return i;
                }

                if (numericFallbackIndex < 0 &&
                    hasNumericTarget &&
                    long.TryParse(groupId, out long numericGroupId) &&
                    numericGroupId == numericTargetId)
                {
                    numericFallbackIndex = i;
                }
            }

            return numericFallbackIndex;
        }

        private bool CanMoveLine(int direction)
        {
            if (patternGroups.Count == 0 || direction == 0)
            {
                return false;
            }

            DialoguePatternGroup group = GetCurrentGroup();
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return false;
            }

            if (direction > 0)
            {
                return currentSequenceIndex < group.Entries.Count - 1 || currentPatternIndex < patternGroups.Count - 1;
            }

            return currentSequenceIndex > 0 || currentPatternIndex > 0;
        }

        private void MoveLineSelection(int direction)
        {
            if (patternGroups.Count == 0 || direction == 0)
            {
                return;
            }

            DialoguePatternGroup group = GetCurrentGroup();
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return;
            }

            ClearActiveTextFieldFocus();

            int nextSequenceIndex = currentSequenceIndex + direction;
            if (nextSequenceIndex >= 0 && nextSequenceIndex < group.Entries.Count)
            {
                currentSequenceIndex = nextSequenceIndex;
            }
            else if (direction > 0 && currentPatternIndex < patternGroups.Count - 1)
            {
                currentPatternIndex++;
                DialoguePatternGroup nextGroup = GetCurrentGroup();
                currentSequenceIndex = nextGroup != null && nextGroup.Entries != null && nextGroup.Entries.Count > 0
                    ? 0
                    : 0;
            }
            else if (direction < 0 && currentPatternIndex > 0)
            {
                currentPatternIndex--;
                DialoguePatternGroup previousGroup = GetCurrentGroup();
                currentSequenceIndex = previousGroup != null && previousGroup.Entries != null && previousGroup.Entries.Count > 0
                    ? previousGroup.Entries.Count - 1
                    : 0;
            }
            else
            {
                return;
            }

            SyncStateFromCurrentSelectionIfNeeded(true);
            Repaint();
        }

        private void DrawNavigationShortcutHints()
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(
                "Shortcut: ←/→ = 前後Line, Shift+←/→ = 前後Pattern, Alt+PageUp/PageDown = 入力中でも前後Line, Alt+Shift+PageUp/PageDown = 入力中でも前後Pattern",
                compactInfoStyle);
        }

        private void SyncStateFromCurrentSelectionIfNeeded(bool force)
        {
            DialoguePatternGroup group = GetCurrentGroup();
            DialogueEntry entry = group != null ? GetCurrentEntry(group) : null;
            string selectionKey = BuildStateSyncSelectionKey(group, entry);
            if (!force && string.Equals(selectionKey, lastStateSyncSelectionKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryApplyCachedState(selectionKey))
            {
                ApplyStateFromEntryCondition(entry);
            }

            lastStateSyncSelectionKey = selectionKey;
        }

        private bool TryApplyCachedState(string selectionKey)
        {
            if (string.IsNullOrEmpty(selectionKey) ||
                !cachedPreviewStatesBySelectionKey.TryGetValue(selectionKey, out DialogueState cachedState) ||
                cachedState == null)
            {
                return false;
            }

            ApplyDialogueState(cachedState);
            return true;
        }

        private string BuildStateSyncSelectionKey(DialoguePatternGroup group, DialogueEntry entry)
        {
            return string.Join(
                "\n",
                dataSet != null ? dataSet.SourcePath ?? string.Empty : string.Empty,
                group != null ? group.RegexId ?? string.Empty : string.Empty,
                group != null ? group.Pattern ?? string.Empty : string.Empty,
                entry != null ? entry.Order.ToString() : "0",
                currentSequenceIndex.ToString());
        }

        private void ApplyStateFromEntryCondition(DialogueEntry entry)
        {
            DialogueState nextState = new DialogueState
            {
                timeZone = state.timeZone
            };

            if (entry != null && !string.IsNullOrWhiteSpace(entry.Condition))
            {
                ApplyStateLevelsFromCondition(entry.Condition, ref nextState);
            }

            state.affectionLevel = nextState.affectionLevel;
            state.sadisticLevel = nextState.sadisticLevel;
            state.concernLevel = nextState.concernLevel;
            state.hostilityLevel = nextState.hostilityLevel;
            state.obedienceLevel = nextState.obedienceLevel;
            state.instinctLevel = nextState.instinctLevel;
        }

        private void CacheStateForCurrentSelection()
        {
            DialoguePatternGroup group = GetCurrentGroup();
            DialogueEntry entry = group != null ? GetCurrentEntry(group) : null;
            string selectionKey = BuildStateSyncSelectionKey(group, entry);
            if (string.IsNullOrEmpty(selectionKey))
            {
                return;
            }

            cachedPreviewStatesBySelectionKey[selectionKey] = CloneDialogueState(state);
        }

        private static DialogueState CloneDialogueState(DialogueState source)
        {
            return new DialogueState
            {
                affectionLevel = source != null ? source.affectionLevel : StateLevel.Low,
                sadisticLevel = source != null ? source.sadisticLevel : StateLevel.Low,
                concernLevel = source != null ? source.concernLevel : StateLevel.Low,
                hostilityLevel = source != null ? source.hostilityLevel : StateLevel.Low,
                obedienceLevel = source != null ? source.obedienceLevel : StateLevel.Low,
                instinctLevel = source != null ? source.instinctLevel : StateLevel.Low,
                timeZone = source != null ? source.timeZone : DialoguePreviewTimeZone.Day,
                playerDefeatedByNekomata = source != null && source.playerDefeatedByNekomata,
                playerDefeatedByPredation = source != null && source.playerDefeatedByPredation,
                playerDefeatedByAnger = source != null && source.playerDefeatedByAnger,
                playerDefeatedByAccident = source != null && source.playerDefeatedByAccident,
                lastPlayerDefeatReason = source != null ? source.lastPlayerDefeatReason ?? string.Empty : string.Empty
            };
        }

        private void ApplyDialogueState(DialogueState source)
        {
            if (source == null)
            {
                return;
            }

            state.affectionLevel = source.affectionLevel;
            state.sadisticLevel = source.sadisticLevel;
            state.concernLevel = source.concernLevel;
            state.hostilityLevel = source.hostilityLevel;
            state.obedienceLevel = source.obedienceLevel;
            state.instinctLevel = source.instinctLevel;
            state.timeZone = source.timeZone;
            state.playerDefeatedByNekomata = source.playerDefeatedByNekomata;
            state.playerDefeatedByPredation = source.playerDefeatedByPredation;
            state.playerDefeatedByAnger = source.playerDefeatedByAnger;
            state.playerDefeatedByAccident = source.playerDefeatedByAccident;
            state.lastPlayerDefeatReason = source.lastPlayerDefeatReason ?? string.Empty;
        }

        private static bool AreDialogueStatesEqual(DialogueState left, DialogueState right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return left.affectionLevel == right.affectionLevel &&
                   left.sadisticLevel == right.sadisticLevel &&
                   left.concernLevel == right.concernLevel &&
                   left.hostilityLevel == right.hostilityLevel &&
                   left.obedienceLevel == right.obedienceLevel &&
                   left.instinctLevel == right.instinctLevel &&
                   left.timeZone == right.timeZone &&
                   left.playerDefeatedByNekomata == right.playerDefeatedByNekomata &&
                   left.playerDefeatedByPredation == right.playerDefeatedByPredation &&
                   left.playerDefeatedByAnger == right.playerDefeatedByAnger &&
                   left.playerDefeatedByAccident == right.playerDefeatedByAccident &&
                   string.Equals(left.lastPlayerDefeatReason ?? string.Empty, right.lastPlayerDefeatReason ?? string.Empty, StringComparison.Ordinal);
        }

        private void ApplyStateLevelsFromCondition(string condition, ref DialogueState targetState)
        {
            string normalized = (condition ?? string.Empty)
                .Replace(" AND ", " && ")
                .Replace(" and ", " && ")
                .Replace("かつ", "&&")
                .Replace(" または ", " || ")
                .Replace(" OR ", " || ")
                .Replace(" or ", " || ")
                ;
            normalized = Regex.Replace(normalized, @"\bAND\b", "&&", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bOR\b", "||", RegexOptions.IgnoreCase);

            string firstGroup = normalized.Split(new[] { "||" }, StringSplitOptions.None)[0];
            string[] clauses = firstGroup.Split(new[] { "&&" }, StringSplitOptions.None);
            for (int i = 0; i < clauses.Length; i++)
            {
                ApplyStateLevelFromClause(clauses[i], ref targetState);
                ApplyStateFlagFromClause(clauses[i], ref targetState);
            }
        }

        private void ApplyStateFlagFromClause(string clause, ref DialogueState targetState)
        {
            string trimmedValue = TrimConditionValue(clause);
            string normalized = DialogueEntry.NormalizeHeader(trimmedValue);
            switch (normalized)
            {
                case "playerdefeatedbynekomata":
                    targetState.playerDefeatedByNekomata = true;
                    targetState.lastPlayerDefeatReason = "Nekomata";
                    break;
                case "playerdefeatedbypredation":
                    targetState.playerDefeatedByPredation = true;
                    targetState.lastPlayerDefeatReason = "Predation";
                    break;
                case "playerdefeatedbyanger":
                    targetState.playerDefeatedByAnger = true;
                    targetState.lastPlayerDefeatReason = "Anger";
                    break;
                case "playerdefeatedbyaccident":
                    targetState.playerDefeatedByAccident = true;
                    targetState.lastPlayerDefeatReason = "Accident";
                    break;
                default:
                    if (normalized.StartsWith("playerdefeatedby", StringComparison.Ordinal))
                    {
                        targetState.lastPlayerDefeatReason = trimmedValue.Substring("PlayerDefeatedBy".Length).Trim();
                    }
                    break;
            }
        }

        private void ApplyStateLevelFromClause(string clause, ref DialogueState targetState)
        {
            Match match = Regex.Match(
                clause ?? string.Empty,
                @"^\s*(?<key>[\p{L}\p{N}_]+)\s*(?<op>>=|<=|==|!=|=|>|<)\s*(?<value>.+?)\s*$");
            if (!match.Success)
            {
                return;
            }

            string metricKey = NormalizeStateMetricKey(match.Groups["key"].Value);
            if (string.IsNullOrEmpty(metricKey))
            {
                return;
            }

            string rawValue = match.Groups["value"].Value.Trim().Trim('"');
            if (!TryInferStateLevel(match.Groups["op"].Value, rawValue, out StateLevel inferredLevel))
            {
                return;
            }

            switch (metricKey)
            {
                case "affection":
                    targetState.affectionLevel = MaxStateLevel(targetState.affectionLevel, inferredLevel);
                    break;
                case "sadistic":
                    targetState.sadisticLevel = MaxStateLevel(targetState.sadisticLevel, inferredLevel);
                    break;
                case "concern":
                    targetState.concernLevel = MaxStateLevel(targetState.concernLevel, inferredLevel);
                    break;
                case "hostility":
                    targetState.hostilityLevel = MaxStateLevel(targetState.hostilityLevel, inferredLevel);
                    break;
                case "obedience":
                    targetState.obedienceLevel = MaxStateLevel(targetState.obedienceLevel, inferredLevel);
                    break;
                case "instinct":
                    targetState.instinctLevel = MaxStateLevel(targetState.instinctLevel, inferredLevel);
                    break;
            }
        }

        private static string NormalizeStateMetricKey(string key)
        {
            switch (DialogueEntry.NormalizeHeader(key))
            {
                case "love":
                case "aff":
                case "affection":
                case "愛情":
                    return "affection";
                case "sadistic":
                case "sadism":
                case "ドs":
                case "どs":
                    return "sadistic";
                case "concern":
                case "care":
                case "worry":
                case "心配":
                    return "concern";
                case "enemy":
                case "hostile":
                case "hostility":
                case "敵対":
                    return "hostility";
                case "obedience":
                case "submissive":
                case "従順":
                    return "obedience";
                case "instinct":
                case "本能":
                    return "instinct";
                default:
                    return string.Empty;
            }
        }

        private static bool TryInferStateLevel(string op, string rawValue, out StateLevel inferredLevel)
        {
            inferredLevel = StateLevel.Low;
            if (!int.TryParse(rawValue, out int expectedValue))
            {
                return false;
            }

            StateLevel[] candidates = { StateLevel.Low, StateLevel.High, StateLevel.Max };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (CompareStateValue(ToStateNumericValue(candidates[i]), expectedValue, op))
                {
                    inferredLevel = candidates[i];
                    return true;
                }
            }

            return false;
        }

        private static bool CompareStateValue(int actualValue, int expectedValue, string op)
        {
            return op switch
            {
                "=" => actualValue == expectedValue,
                "==" => actualValue == expectedValue,
                "!=" => actualValue != expectedValue,
                ">" => actualValue > expectedValue,
                "<" => actualValue < expectedValue,
                ">=" => actualValue >= expectedValue,
                "<=" => actualValue <= expectedValue,
                _ => false
            };
        }

        private static int ToStateNumericValue(StateLevel level)
        {
            switch (level)
            {
                case StateLevel.Max:
                    return 100;
                case StateLevel.High:
                    return 60;
                default:
                    return 0;
            }
        }

        private static StateLevel MaxStateLevel(StateLevel currentLevel, StateLevel nextLevel)
        {
            return (StateLevel)Mathf.Max((int)currentLevel, (int)nextLevel);
        }

        private void ClearActiveTextFieldFocus()
        {
            GUI.FocusControl(string.Empty);
            EditorGUIUtility.editingTextField = false;
            GUIUtility.keyboardControl = 0;
        }

        private DialoguePatternGroup GetCurrentGroup()
        {
            if (patternGroups.Count == 0)
            {
                return null;
            }

            currentPatternIndex = Mathf.Clamp(currentPatternIndex, 0, patternGroups.Count - 1);
            return patternGroups[currentPatternIndex];
        }

        private DialogueEntry GetCurrentEntry(DialoguePatternGroup group)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return null;
            }

            currentSequenceIndex = Mathf.Clamp(currentSequenceIndex, 0, group.Entries.Count - 1);
            return group.Entries[currentSequenceIndex];
        }

        private DialogueEntry GetRepresentativeEntry(DialoguePatternGroup group)
        {
            return group != null && group.Entries.Count > 0 ? group.Entries[0] : null;
        }

        private void ShowResourceCsvMenu()
        {
            GenericMenu menu = new GenericMenu();
            List<string> paths = DialogueDataLoader.GetResourceCsvPaths();
            if (paths.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("TalkCSV is empty"));
            }
            else
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    menu.AddItem(new GUIContent(path), false, () => LoadCsv(path));
                }
            }

            menu.ShowAsContext();
        }

        private void LoadCsv(string path)
        {
            if (!ConfirmDiscardChanges())
            {
                return;
            }

            try
            {
                TryPersistSessionState();
                ApplyLoadedDataSet(DialogueDataLoader.LoadFromPath(path));
                authorDiagnostics = null;
                showDetails = showBehaviour = showBranches = showDevelopment = showConditionExpression = showTeaching = false;
                showTimeDiary = showTopics = showReadings = false;
                scrollPosition = relatedScroll = Vector2.zero;
                RebuildPatternCache();
                ApplySessionSelection();
                TryPersistSessionState();
                ShowNotification(new GUIContent($"Loaded: {dataSet.SourceLabel}"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Dialogue Preview", exception.Message, "OK");
            }
        }

        private void SaveCurrentCsv()
        {
            if (dataSet == null)
            {
                return;
            }

            string targetPath = dataSet.SavePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = EditorUtility.SaveFilePanel(
                    "Save Dialogue CSV",
                    DialogueDataLoader.GetTalkCsvDirectory(),
                    "DialoguePreview.csv",
                    "csv");
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }
            }

            try
            {
                TryPersistSessionState();
                DialogueEntry focusEntry = GetCurrentEntry(GetCurrentGroup());
                lastBackupPath = DialogueDataLoader.SaveToPath(dataSet, targetPath);
                DialogueAuthoringIdentity.Save(dataSet);
                dataSet.SourcePath = targetPath.Replace("\\", "/");
                dataSet.SourceLabel = Path.GetFileName(targetPath);
                dataSet.SavePath = targetPath.Replace("\\", "/");
                dataSet.SaveLabel = Path.GetFileName(targetPath);
                dataSet.IsDerivedFromGroupedSource = false;
                dataSet.CompanionSourcePath = string.Empty;
                bool compiledTalkData = false;
                SaveRelatedDrafts(() => compiledTalkData = CompileTalkDataIfSourceCsv(targetPath));
                DialogueAuthoringIdentity.Delete(pendingDeletedDialogueIds);
                pendingDeletedDialogueIds?.Clear();
                hasPendingChanges = false;

                if ((dataSet.SavePath ?? string.Empty).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    AssetDatabase.Refresh();
                }

                ResetTransientPatternGroupKeys();
                RebuildPatternCache(focusEntry);
                TryPersistSessionState();
                ShowNotification(new GUIContent(compiledTalkData ? "CSV saved / TalkData updated" : "CSV saved"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Dialogue Preview", exception.Message, "OK");
            }
        }

        private static bool CompileTalkDataIfSourceCsv(string savedPath)
        {
            string sourcePath = DialogueDataLoader.GetFullPath(savedPath);
            string sourceDirectory = Path.GetDirectoryName(sourcePath);
            string talkCsvDirectory = Path.GetFullPath(DialogueDataLoader.GetTalkCsvDirectory());
            if (!string.Equals(sourceDirectory, talkCsvDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            CSVCryptoCompiler.CompileForBuildOrThrow();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(
                projectRoot,
                "Assets",
                "Resources",
                "TalkData",
                Path.GetFileNameWithoutExtension(sourcePath) + ".bytes");
            return File.Exists(outputPath) &&
                   File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(sourcePath);
        }

        private readonly List<string> regexTestWarnings = new List<string>();

        private void RunRegexTest()
        {
            matchedRegexIds.Clear();
            matchedPatternIndices.Clear();
            currentRegexMatchResults.Clear();
            otherCsvRegexMatchSections.Clear();
            regexTestWarnings.Clear();
            ResetUnknownWordPreview();
            if (dataSet?.Entries == null || dataSet.Entries.Count == 0)
            {
                regexStatus = "CSV未ロード";
                return;
            }
            if (string.IsNullOrWhiteSpace(regexInput) && string.IsNullOrWhiteSpace(regexInputChinese) && string.IsNullOrWhiteSpace(regexInputEnglish))
            {
                regexStatus = "入力文字列が空です";
                return;
            }
            CollectCurrentRegexMatchResults(out string error);
            CollectOtherCsvRegexMatchResults();
            MarkTopRegexMatchItems();
            foreach (var item in currentRegexMatchResults)
            {
                if (!matchedRegexIds.Contains(item.RegexId)) matchedRegexIds.Add(item.RegexId);
                if (item.PatternIndex >= 0) matchedPatternIndices.Add(item.PatternIndex);
            }
            matchedRegexIds.Sort(CompareRegexIdOrder);
            matchedPatternIndices.Sort();
            int otherCount = otherCsvRegexMatchSections.Sum(section => section.Items.Count);
            regexStatus = $"一致 Pattern:{currentRegexMatchResults.Count}件 / 他CSV:{otherCount}件";
            if (regexTestWarnings.Count > 0) regexStatus += $" / 不正な照合式:{regexTestWarnings.Count}件";
            if (currentRegexMatchResults.Count > 0 || otherCount > 0)
                unknownWordStatus = "通常Regex一致のため未実行";
            else RunUnknownWordPreview();
        }

        private void DrawUnknownWordPreview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Unknown Word", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("status", string.IsNullOrWhiteSpace(unknownWordStatus) ? "未実行" : unknownWordStatus);
                EditorGUILayout.LabelField("normalizedInput", string.IsNullOrWhiteSpace(unknownWordNormalizedInput) ? "-" : unknownWordNormalizedInput);
                EditorGUILayout.LabelField("unknownWord", string.IsNullOrWhiteSpace(unknownWord) ? "-" : unknownWord);
                EditorGUILayout.LabelField("estimatedCategory", string.IsNullOrWhiteSpace(unknownWordEstimatedCategory) ? "null" : unknownWordEstimatedCategory);
                EditorGUILayout.LabelField(
                    "removedPatterns",
                    unknownWordRemovedPatterns.Count > 0 ? string.Join(", ", unknownWordRemovedPatterns) : "-");
                EditorGUILayout.LabelField(
                    "matchedKnownWords",
                    unknownWordMatchedKnownWords.Count > 0 ? string.Join(", ", unknownWordMatchedKnownWords) : "-");
            }
        }

        private void RunUnknownWordPreview()
        {
            if (string.IsNullOrWhiteSpace(regexInput))
            {
                unknownWordStatus = "JP入力なし";
                return;
            }

            try
            {
                RegexInputParser parser = new RegexInputParser(GiantCatConversationDefaults.CreateConfig());
                PlayerInputContext inputContext = new PlayerInputContext(regexInput ?? string.Empty);
                ConversationParseResult parseResult = parser.Parse(inputContext);
                DialogueIntentResult intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
                UnknownWordExtractor extractor = new UnknownWordExtractor();
                UnknownWordResult result = extractor.Extract(inputContext, intentResult);

                unknownWordNormalizedInput = result != null ? result.NormalizedInput ?? string.Empty : string.Empty;
                unknownWord = result != null ? result.UnknownWord ?? string.Empty : string.Empty;
                unknownWordEstimatedCategory = result != null ? result.EstimatedCategory ?? string.Empty : string.Empty;
                unknownWordRemovedPatterns = result != null && result.RemovedPatterns != null
                    ? new List<string>(result.RemovedPatterns)
                    : new List<string>();
                unknownWordMatchedKnownWords = result != null && result.MatchedKnownWords != null
                    ? new List<string>(result.MatchedKnownWords)
                    : new List<string>();
                unknownWordStatus = result != null && result.HasUnknownWord ? "未知語あり" : "未知語なし";
            }
            catch (Exception exception)
            {
                unknownWordStatus = "未知語抽出エラー: " + exception.Message;
            }
        }

        private void ResetUnknownWordPreview()
        {
            unknownWordStatus = "未実行";
            unknownWordNormalizedInput = string.Empty;
            unknownWord = string.Empty;
            unknownWordEstimatedCategory = string.Empty;
            unknownWordRemovedPatterns.Clear();
            unknownWordMatchedKnownWords.Clear();
        }

        private void DrawCurrentRegexMatchResults()
        {
            for (int regexIndex = 0; regexIndex < matchedRegexIds.Count; regexIndex++)
            {
                string regexId = matchedRegexIds[regexIndex];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"regex_id: {regexId}", EditorStyles.boldLabel);

                    for (int i = 0; i < currentRegexMatchResults.Count; i++)
                    {
                        RegexMatchDisplayItem item = currentRegexMatchResults[i];
                        string itemRegexId = string.IsNullOrWhiteSpace(item.RegexId) ? "(empty)" : item.RegexId;
                        if (!string.Equals(itemRegexId, regexId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (GUILayout.Button(BuildRegexMatchResultLabel(item), GUILayout.Height(42f)))
                        {
                            OpenRegexMatchResult(item);
                        }
                    }
                }

                EditorGUILayout.Space(4f);
            }
        }

        private void DrawOtherCsvRegexMatchResults()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("他のcsvの検索結果", EditorStyles.boldLabel);

                if (otherCsvRegexMatchSections.Count == 0)
                {
                    EditorGUILayout.HelpBox("他のcsvで一致した Pattern はありません。", MessageType.None);
                    return;
                }

                for (int sectionIndex = 0; sectionIndex < otherCsvRegexMatchSections.Count; sectionIndex++)
                {
                    RegexMatchDisplaySection section = otherCsvRegexMatchSections[sectionIndex];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(section.CsvLabel, EditorStyles.boldLabel);

                        if (!string.IsNullOrEmpty(section.ErrorMessage))
                        {
                            EditorGUILayout.HelpBox(section.ErrorMessage, MessageType.Warning);
                        }

                        List<string> regexIds = section.Items
                            .Select(item => string.IsNullOrWhiteSpace(item.RegexId) ? "(empty)" : item.RegexId)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        regexIds.Sort(CompareRegexIdOrder);

                        for (int regexIndex = 0; regexIndex < regexIds.Count; regexIndex++)
                        {
                            string regexId = regexIds[regexIndex];
                            EditorGUILayout.LabelField($"regex_id: {regexId}", compactInfoStyle);

                            for (int i = 0; i < section.Items.Count; i++)
                            {
                                RegexMatchDisplayItem item = section.Items[i];
                                string itemRegexId = string.IsNullOrWhiteSpace(item.RegexId) ? "(empty)" : item.RegexId;
                                if (!string.Equals(itemRegexId, regexId, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                if (GUILayout.Button(BuildRegexMatchResultLabel(item), GUILayout.Height(42f)))
                                {
                                    LoadCsvAndSelectPattern(item);
                                }
                            }
                        }
                    }

                    EditorGUILayout.Space(4f);
                }
            }
        }

        private string BuildRegexMatchResultLabel(RegexMatchDisplayItem item)
        {
            string star = item.IsTopCandidate ? "★ " : string.Empty;
            string displayName = $"regex:{(string.IsNullOrWhiteSpace(item.RegexId) ? "?" : item.RegexId)} / pattern:{(string.IsNullOrWhiteSpace(item.Pattern) ? "?" : item.Pattern)}";
            string metrics = $"Priority:{item.Priority} / len:{item.MatchLength}";
            string regex = string.IsNullOrWhiteSpace(item.RegexSummary) ? "regex: (none)" : item.RegexSummary;
            string preview = TruncateForDisplay(item.Preview, 72);
            return $"{star}{displayName} / {metrics}\n{regex} / {preview}";
        }

        private void CollectCurrentRegexMatchResults(out string error)
        {
            error = null;
            currentRegexMatchResults.Clear();
            Dictionary<string, int> registrationOrderLookup = BuildRegistrationOrderLookup(GetConversationDataManagerRegisteredCsvPaths(out _));

            for (int patternIndex = 0; patternIndex < patternGroups.Count; patternIndex++)
            {
                DialoguePatternGroup group = patternGroups[patternIndex];
                DialogueEntry representative = GetRepresentativeEntry(group);
                if (representative != null && representative.CallOnly)
                {
                    continue;
                }

                if (!TryBuildRegexMatchDisplayItem(
                        dataSet != null ? dataSet.SourcePath : string.Empty,
                        dataSet != null ? dataSet.SourceLabel : string.Empty,
                        group != null ? group.RegexId : string.Empty,
                        group != null ? group.Pattern : string.Empty,
                        patternIndex,
                        group != null ? group.Entries : null,
                        registrationOrderLookup,
                        out RegexMatchDisplayItem item,
                        out error))
                {
                    if (!string.IsNullOrEmpty(error)) regexTestWarnings.Add($"{group.RegexId} / Pattern {group.Pattern}: {error}");
                    continue;
                }
                if (!string.IsNullOrEmpty(error)) regexTestWarnings.Add($"{group.RegexId} / Pattern {group.Pattern}: {error}");
                currentRegexMatchResults.Add(item);
            }

            SortRegexMatchDisplayItems(currentRegexMatchResults);
        }

        private void CollectOtherCsvRegexMatchResults()
        {
            otherCsvRegexMatchSections.Clear();

            List<string> registeredPaths = GetConversationDataManagerRegisteredCsvPaths(out string sourceError);
            if (!string.IsNullOrEmpty(sourceError))
            {
                otherCsvRegexMatchSections.Add(new RegexMatchDisplaySection
                {
                    CsvLabel = "他のcsvの検索結果",
                    ErrorMessage = sourceError
                });
                return;
            }

            string currentSourcePath = dataSet != null ? NormalizeAssetPath(dataSet.SourcePath) : string.Empty;
            Dictionary<string, int> registrationOrderLookup = BuildRegistrationOrderLookup(registeredPaths);

            for (int i = 0; i < registeredPaths.Count; i++)
            {
                string resourcePath = NormalizeAssetPath(registeredPaths[i]);
                if (string.Equals(resourcePath, currentSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RegexMatchDisplaySection section = new RegexMatchDisplaySection
                {
                    CsvPath = resourcePath,
                    CsvLabel = Path.GetFileName(resourcePath)
                };

                try
                {
                    DialogueDataLoader.DataSet resourceDataSet = DialogueDataLoader.LoadFromPath(resourcePath);
                    List<DialoguePatternGroup> groups = BuildPatternGroupsForDataSet(resourceDataSet);
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        DialoguePatternGroup group = groups[groupIndex];
                        DialogueEntry representative = group.GetRepresentativeEntry();
                        if (representative != null && representative.CallOnly)
                        {
                            continue;
                        }

                        if (!TryBuildRegexMatchDisplayItem(
                                resourcePath,
                                resourceDataSet.SourceLabel,
                                group.RegexId,
                                group.Pattern,
                                -1,
                                group.Entries,
                                registrationOrderLookup,
                                out RegexMatchDisplayItem item,
                                out string error))
                        {
                            if (!string.IsNullOrEmpty(error))
                            {
                                section.ErrorMessage = string.IsNullOrEmpty(section.ErrorMessage) ? error : section.ErrorMessage + "\n" + error;
                            }

                            continue;
                        }

                        if (!string.IsNullOrEmpty(error))
                            section.ErrorMessage = string.IsNullOrEmpty(section.ErrorMessage) ? error : section.ErrorMessage + "\n" + error;
                        section.Items.Add(item);
                    }
                }
                catch (Exception exception)
                {
                    section.ErrorMessage = exception.Message;
                }

                if (!string.IsNullOrEmpty(section.ErrorMessage))
                {
                    otherCsvRegexMatchSections.Add(section);
                    continue;
                }

                if (section.Items.Count == 0)
                {
                    continue;
                }

                SortRegexMatchDisplayItems(section.Items);
                otherCsvRegexMatchSections.Add(section);
            }
        }

        private bool TryBuildRegexMatchDisplayItem(
            string csvPath,
            string csvLabel,
            string regexId,
            string pattern,
            int patternIndex,
            List<DialogueEntry> entries,
            Dictionary<string, int> registrationOrderLookup,
            out RegexMatchDisplayItem item,
            out string error)
        {
            item = null;
            error = null;

            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            DialogueEntry bestEntry = null;
            int bestMatchLength = -1;
            string bestRegexSummary = string.Empty;

            for (int i = 0; i < entries.Count; i++)
            {
                DialogueEntry entry = entries[i];
                bool matched = TryEvaluateEntryRegexMatch(entry, out int matchLength, out string regexSummary, out string entryError);
                if (!string.IsNullOrEmpty(entryError) && (error == null || error.IndexOf(entryError, StringComparison.Ordinal) < 0))
                    error = string.IsNullOrEmpty(error) ? entryError : error + "\n" + entryError;
                if (!matched)
                {
                    continue;
                }

                if (bestEntry == null ||
                    matchLength > bestMatchLength ||
                    (matchLength == bestMatchLength && entry.Priority > bestEntry.Priority))
                {
                    bestEntry = entry;
                    bestMatchLength = matchLength;
                    bestRegexSummary = regexSummary;
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            item = new RegexMatchDisplayItem
            {
                CsvPath = NormalizeAssetPath(csvPath),
                CsvLabel = string.IsNullOrWhiteSpace(csvLabel) ? Path.GetFileName(csvPath ?? string.Empty) : csvLabel,
                RegexId = string.IsNullOrWhiteSpace(regexId) ? "(empty)" : regexId.Trim(),
                Pattern = pattern ?? string.Empty,
                Preview = bestEntry.Text ?? string.Empty,
                RegexSummary = bestRegexSummary,
                Priority = bestEntry.Priority,
                MatchLength = bestMatchLength,
                PatternIndex = patternIndex,
                SourceLineNumber = bestEntry.SourceLineNumber,
                RegistrationOrder = ResolveRegistrationOrder(registrationOrderLookup, csvPath),
            };
            return true;
        }

        private bool TryEvaluateEntryRegexMatch(DialogueEntry entry, out int bestMatchLength, out string regexSummary, out string error)
        {
            bestMatchLength = -1;
            regexSummary = string.Empty;
            error = null;

            if (entry == null)
            {
                return false;
            }

            List<string> matchedRegexSummaries = new List<string>();

            if (TryMatchRegexPattern(entry.RegexPattern, GetNormalizedJapaneseRegexInput(), "JP", out int japaneseMatchLength, out string japaneseError))
            {
                bestMatchLength = Mathf.Max(bestMatchLength, japaneseMatchLength);
                matchedRegexSummaries.Add($"JP:{TruncateForDisplay(entry.RegexPattern, 48)}");
            }
            else if (!string.IsNullOrEmpty(japaneseError))
            {
                error = string.IsNullOrEmpty(error) ? japaneseError : error + "\n" + japaneseError;
            }

            if (TryMatchRegexPattern(entry.RegexPatternChinese, regexInputChinese, "简中", out int chineseMatchLength, out string chineseError))
            {
                bestMatchLength = Mathf.Max(bestMatchLength, chineseMatchLength);
                matchedRegexSummaries.Add($"简中:{TruncateForDisplay(entry.RegexPatternChinese, 48)}");
            }
            else if (!string.IsNullOrEmpty(chineseError))
            {
                error = string.IsNullOrEmpty(error) ? chineseError : error + "\n" + chineseError;
            }

            if (TryMatchRegexPattern(entry.RegexPatternEnglish, regexInputEnglish, "EN", out int englishMatchLength, out string englishError))
            {
                bestMatchLength = Mathf.Max(bestMatchLength, englishMatchLength);
                matchedRegexSummaries.Add($"EN:{TruncateForDisplay(entry.RegexPatternEnglish, 48)}");
            }
            else if (!string.IsNullOrEmpty(englishError))
            {
                error = string.IsNullOrEmpty(error) ? englishError : error + "\n" + englishError;
            }

            if (bestMatchLength < 0)
            {
                return false;
            }

            regexSummary = string.Join(" / ", matchedRegexSummaries);
            return true;
        }

        private static bool TryMatchRegexPattern(
            string pattern,
            string input,
            string languageLabel,
            out int longestMatchLength,
            out string error)
        {
            longestMatchLength = -1;
            error = null;

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            try
            {
                string runtimePattern = languageLabel == "JP" ? JapaneseTextNormalizer.NormalizeToken(pattern) : pattern;
                MatchCollection matches = Regex.Matches(input, runtimePattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                for (int i = 0; i < matches.Count; i++)
                {
                    Match match = matches[i];
                    if (!match.Success)
                    {
                        continue;
                    }

                    if (match.Length > longestMatchLength)
                    {
                        longestMatchLength = match.Length;
                    }
                }

                return longestMatchLength >= 0;
            }
            catch (RegexMatchTimeoutException exception)
            {
                error = $"regexタイムアウト ({languageLabel}): {pattern} / {exception.Message}";
                return false;
            }
            catch (ArgumentException exception)
            {
                error = $"regexエラー ({languageLabel}): {pattern} / {exception.Message}";
                return false;
            }
        }

        private string GetNormalizedJapaneseRegexInput()
        {
            return JapaneseTextNormalizer.NormalizeInput(regexInput ?? string.Empty);
        }

        private void MarkTopRegexMatchItems()
        {
            List<RegexMatchDisplayItem> candidates = new List<RegexMatchDisplayItem>();
            candidates.AddRange(currentRegexMatchResults);
            for (int i = 0; i < otherCsvRegexMatchSections.Count; i++)
            {
                candidates.AddRange(otherCsvRegexMatchSections[i].Items);
            }

            for (int i = 0; i < currentRegexMatchResults.Count; i++)
            {
                currentRegexMatchResults[i].IsTopCandidate = false;
            }

            for (int sectionIndex = 0; sectionIndex < otherCsvRegexMatchSections.Count; sectionIndex++)
            {
                List<RegexMatchDisplayItem> items = otherCsvRegexMatchSections[sectionIndex].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].IsTopCandidate = false;
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            RegexMatchDisplayItem bestItem = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                if (CompareRegexMatchCandidate(candidates[i], bestItem) < 0)
                {
                    bestItem = candidates[i];
                }
            }

            if (bestItem != null)
            {
                bestItem.IsTopCandidate = true;
            }
        }

        private static void SortRegexMatchDisplayItems(List<RegexMatchDisplayItem> items)
        {
            items.Sort((left, right) =>
            {
                int regexCompare = CompareRegexIdOrder(left != null ? left.RegexId : null, right != null ? right.RegexId : null);
                if (regexCompare != 0)
                {
                    return regexCompare;
                }

                int patternCompare = ComparePatternOrder(left != null ? left.Pattern : null, right != null ? right.Pattern : null);
                if (patternCompare != 0)
                {
                    return patternCompare;
                }

                int bestCompare = CompareRegexMatchCandidate(left, right);
                if (bestCompare != 0)
                {
                    return bestCompare;
                }

                return 0;
            });
        }

        private static string TruncateForDisplay(string value, int maxLength)
        {
            string text = value ?? string.Empty;
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, Mathf.Max(0, maxLength - 3)) + "...";
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace("\\", "/");
        }

        private void OpenRegexMatchResult(RegexMatchDisplayItem item)
        {
            if (item == null)
            {
                return;
            }

            int targetPatternIndex = ResolvePatternIndexForRegexMatch(item);
            if (targetPatternIndex < 0)
            {
                return;
            }

            ClearActiveTextFieldFocus();
            currentPatternIndex = targetPatternIndex;
            currentSequenceIndex = FindSequenceIndexBySourceLineNumber(patternGroups[targetPatternIndex], item.SourceLineNumber);
            selectedTab = 0;
            SyncStateFromCurrentSelectionIfNeeded(true);
            Repaint();
        }

        private int ResolvePatternIndexForRegexMatch(RegexMatchDisplayItem item)
        {
            if (item == null || patternGroups.Count == 0)
            {
                return -1;
            }

            // Current-CSV results keep their pattern index. It is the authoritative target
            // even when a result has an empty regex_id or pattern value.
            if (item.PatternIndex >= 0 && item.PatternIndex < patternGroups.Count)
            {
                return item.PatternIndex;
            }

            return FindPatternIndexForRegexMatch(item.RegexId, item.Pattern, item.SourceLineNumber);
        }

        private int FindPatternIndexForRegexMatch(string regexId, string patternValue, int sourceLineNumber)
        {
            string normalizedRegexId = string.IsNullOrWhiteSpace(regexId) ? string.Empty : regexId.Trim();
            string normalizedPattern = string.IsNullOrWhiteSpace(patternValue) ? string.Empty : patternValue.Trim();
            int fallbackIndex = -1;

            for (int i = 0; i < patternGroups.Count; i++)
            {
                DialoguePatternGroup group = patternGroups[i];
                if (sourceLineNumber > 0 &&
                    group != null &&
                    group.Entries != null &&
                    group.Entries.Any(entry => entry != null && entry.SourceLineNumber == sourceLineNumber))
                {
                    return i;
                }

                if (!IsRegexMatchTarget(group, normalizedRegexId, normalizedPattern))
                {
                    continue;
                }

                if (fallbackIndex < 0)
                {
                    fallbackIndex = i;
                }

            }

            return fallbackIndex;
        }

        private static bool IsRegexMatchTarget(DialoguePatternGroup group, string regexId, string patternValue)
        {
            if (group == null)
            {
                return false;
            }

            string normalizedRegexId = NormalizeRegexIdForComparison(regexId);
            string normalizedPattern = string.IsNullOrWhiteSpace(patternValue) ? string.Empty : patternValue.Trim();
            return string.Equals(NormalizeRegexIdForComparison(group.RegexId), normalizedRegexId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((group.Pattern ?? string.Empty).Trim(), normalizedPattern, StringComparison.Ordinal);
        }

        private static string NormalizeRegexIdForComparison(string regexId)
        {
            return string.IsNullOrWhiteSpace(regexId) || string.Equals(regexId.Trim(), "(empty)", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : regexId.Trim();
        }

        private static int FindSequenceIndexBySourceLineNumber(DialoguePatternGroup group, int sourceLineNumber)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return 0;
            }

            if (sourceLineNumber > 0)
            {
                for (int i = 0; i < group.Entries.Count; i++)
                {
                    DialogueEntry entry = group.Entries[i];
                    if (entry != null && entry.SourceLineNumber == sourceLineNumber)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        private void LoadCsvAndSelectPattern(RegexMatchDisplayItem item)
        {
            string normalizedPath = NormalizeAssetPath(item != null ? item.CsvPath : string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            LoadCsv(normalizedPath);
            if (dataSet == null ||
                !string.Equals(NormalizeAssetPath(dataSet.SourcePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OpenRegexMatchResult(item);
        }

        private static int CompareRegexMatchCandidate(RegexMatchDisplayItem left, RegexMatchDisplayItem right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int matchCompare = right.MatchLength.CompareTo(left.MatchLength);
            if (matchCompare != 0)
            {
                return matchCompare;
            }

            int priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int registrationCompare = left.RegistrationOrder.CompareTo(right.RegistrationOrder);
            if (registrationCompare != 0)
            {
                return registrationCompare;
            }

            int lineCompare = left.SourceLineNumber.CompareTo(right.SourceLineNumber);
            if (lineCompare != 0)
            {
                return lineCompare;
            }

            int regexCompare = CompareRegexIdOrder(left.RegexId, right.RegexId);
            if (regexCompare != 0)
            {
                return regexCompare;
            }

            return ComparePatternOrder(left.Pattern, right.Pattern);
        }

        private static Dictionary<string, int> BuildRegistrationOrderLookup(List<string> registeredPaths)
        {
            Dictionary<string, int> lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < registeredPaths.Count; i++)
            {
                string normalizedPath = NormalizeAssetPath(registeredPaths[i]);
                if (!string.IsNullOrWhiteSpace(normalizedPath) && !lookup.ContainsKey(normalizedPath))
                {
                    lookup.Add(normalizedPath, i);
                }
            }

            return lookup;
        }

        private static int ResolveRegistrationOrder(Dictionary<string, int> registrationOrderLookup, string csvPath)
        {
            if (registrationOrderLookup == null)
            {
                return int.MaxValue;
            }

            return registrationOrderLookup.TryGetValue(NormalizeAssetPath(csvPath), out int order)
                ? order
                : int.MaxValue;
        }

        private static List<string> GetConversationDataManagerRegisteredCsvPaths(out string error)
        {
            error = null;
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> sourceCsvPaths = DialogueDataLoader.GetResourceCsvPaths();
            for (int i = 0; i < sourceCsvPaths.Count; i++)
            {
                string sourcePath = NormalizeAssetPath(sourceCsvPaths[i]);
                if (!string.IsNullOrWhiteSpace(sourcePath) && seen.Add(sourcePath))
                {
                    paths.Add(sourcePath);
                }
            }

            Backgammon.Conversation.ConversationDataManager[] managers = Resources.FindObjectsOfTypeAll<Backgammon.Conversation.ConversationDataManager>();
            if (managers == null || managers.Length == 0)
            {
                if (paths.Count == 0)
                {
                    error = "TalkSource/TalkCSV に CSV がなく、ConversationDataManager も開いているシーン上に見つかりません。";
                }

                return paths;
            }

            for (int i = 0; i < managers.Length; i++)
            {
                Backgammon.Conversation.ConversationDataManager manager = managers[i];
                if (manager == null || !manager.gameObject.scene.IsValid())
                {
                    continue;
                }

                List<TextAsset> activeAssets = manager.GetActiveCsvAssets();
                for (int assetIndex = 0; assetIndex < activeAssets.Count; assetIndex++)
                {
                    TextAsset asset = activeAssets[assetIndex];
                    if (asset == null)
                    {
                        continue;
                    }

                    string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(asset));
                    if (string.IsNullOrWhiteSpace(assetPath) ||
                        !assetPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                        !seen.Add(assetPath))
                    {
                        continue;
                    }

                    paths.Add(assetPath);
                }
            }

            if (paths.Count == 0)
            {
                error = "TalkSource/TalkCSV に CSV がなく、ConversationDataManager に有効な CSV 登録もありません。";
            }

            return paths;
        }

        private static List<DialoguePatternGroup> BuildPatternGroupsForDataSet(DialogueDataLoader.DataSet sourceDataSet)
        {
            List<DialoguePatternGroup> groups = new List<DialoguePatternGroup>();
            if (sourceDataSet == null || sourceDataSet.Entries == null || sourceDataSet.Entries.Count == 0)
            {
                return groups;
            }

            Dictionary<string, DialoguePatternGroup> groupsByKey = new Dictionary<string, DialoguePatternGroup>();
            for (int i = 0; i < sourceDataSet.Entries.Count; i++)
            {
                DialogueEntry entry = sourceDataSet.Entries[i];
                string key = BuildSavedPatternKey(entry, i);

                if (!groupsByKey.TryGetValue(key, out DialoguePatternGroup group))
                {
                    group = new DialoguePatternGroup
                    {
                        RegexId = entry.RegexId,
                        Pattern = entry.Pattern,
                        FirstEntryIndex = i
                    };
                    groupsByKey.Add(key, group);
                    groups.Add(group);
                }

                group.Entries.Add(entry);
            }

            for (int i = 0; i < groups.Count; i++)
            {
                groups[i].Entries.Sort((left, right) =>
                {
                    int patternCompare = ComparePatternOrder(left != null ? left.Pattern : null, right != null ? right.Pattern : null);
                    if (patternCompare != 0)
                    {
                        return patternCompare;
                    }

                    int orderCompare = (left != null ? left.Order : 0).CompareTo(right != null ? right.Order : 0);
                    if (orderCompare != 0)
                    {
                        return orderCompare;
                    }

                    return CompareInternalIdOrder(left != null ? left.InternalId : null, right != null ? right.InternalId : null);
                });
            }

            groups.Sort((left, right) =>
            {
                int idCompare = CompareInternalIdOrder(
                    left != null ? left.GetRepresentativeEntry()?.InternalId : null,
                    right != null ? right.GetRepresentativeEntry()?.InternalId : null);
                if (idCompare != 0)
                {
                    return idCompare;
                }

                int patternCompare = ComparePatternOrder(left != null ? left.Pattern : null, right != null ? right.Pattern : null);
                if (patternCompare != 0)
                {
                    return patternCompare;
                }

                int regexCompare = CompareRegexIdOrder(left != null ? left.RegexId : null, right != null ? right.RegexId : null);
                if (regexCompare != 0)
                {
                    return regexCompare;
                }

                return (left != null ? left.FirstEntryIndex : 0).CompareTo(right != null ? right.FirstEntryIndex : 0);
            });

            return groups;
        }

        private void OpenTextEditScene()
        {
            PushCurrentPreviewToTextEditScene(true);
            if (TextEditSceneUtility.OpenOrCreateTextEditScene())
            {
                ShowNotification(new GUIContent("TextEdit Scene opened"));
            }
        }

        private void PushCurrentPreviewToTextEditScene(bool force)
        {
            if (force)
            {
                TextEditSceneUtility.EnsureActiveTextEditSceneBindings();
            }

            TextEditPreviewState previewState = TextEditSceneUtility.EnsurePreviewStateAsset();
            if (previewState == null)
            {
                return;
            }

            DialoguePatternGroup group = GetCurrentGroup();
            DialogueEntry entry = group != null ? GetCurrentEntry(group) : null;
            DialogueStateSimulator.EvaluationResult evaluation = group != null
                ? DialogueStateSimulator.Evaluate(group, state)
                : new DialogueStateSimulator.EvaluationResult(false, "CSV未ロード");

            string message = ReplacePreviewVariables(entry != null ? entry.Text : "CSV をロードして Pattern を選択してください。");
            string messageChinese = ReplacePreviewVariables(entry != null ? entry.TextChinese : string.Empty);
            string messageEnglish = ReplacePreviewVariables(entry != null ? entry.TextEnglish : string.Empty);
            string warnings = BuildPreviewWarnings(group);
            string syncKey = string.Join(
                "\n",
                dataSet != null ? dataSet.SourcePath ?? string.Empty : string.Empty,
                group != null ? group.RegexId ?? string.Empty : string.Empty,
                group != null ? group.Pattern ?? string.Empty : string.Empty,
                entry != null ? entry.Order.ToString() : "0",
                currentSequenceIndex.ToString(),
                entry != null ? entry.ResponseType ?? string.Empty : string.Empty,
                entry != null ? entry.ActionId ?? string.Empty : string.Empty,
                entry != null ? entry.Condition ?? string.Empty : string.Empty,
                message,
                messageChinese,
                messageEnglish,
                warnings,
                BuildStateSummary(),
                evaluation.IsMatch.ToString(),
                evaluation.Message ?? string.Empty);

            if (!force && string.Equals(syncKey, lastPreviewSyncKey, StringComparison.Ordinal))
            {
                return;
            }

            previewState.SourceCsvPath = dataSet != null ? dataSet.SourcePath ?? string.Empty : string.Empty;
            previewState.RegexId = group != null ? group.RegexId ?? string.Empty : string.Empty;
            previewState.Pattern = group != null ? group.Pattern ?? string.Empty : string.Empty;
            previewState.SequenceIndex = entry != null ? currentSequenceIndex + 1 : 0;
            previewState.SequenceCount = group != null ? group.Entries.Count : 0;
            previewState.Order = entry != null ? entry.Order : 0;
            previewState.ResponseType = entry != null ? NormalizeResponseType(entry.ResponseType) : string.Empty;
            previewState.ActionId = entry != null ? entry.ActionId ?? string.Empty : string.Empty;
            previewState.Condition = entry != null ? entry.Condition ?? string.Empty : string.Empty;
            previewState.StateSummary = $"{BuildStateSummary()} / flow:{BuildDialogueFlowSummary(entry)} / {(evaluation.IsMatch ? "有効" : "無効")} / {evaluation.Message}";
            previewState.SpeakerName = "猫又";
            previewState.Message = message;
            previewState.MessageChinese = messageChinese;
            previewState.MessageEnglish = messageEnglish;
            previewState.Warnings = warnings;
            previewState.Revision++;

            EditorUtility.SetDirty(previewState);
            if (force)
            {
                AssetDatabase.SaveAssets();
            }

            TextEditSceneUtility.RefreshLoadedTextEditScenePreview();
            lastPreviewSyncKey = syncKey;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private string ReplacePreviewVariables(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            Dictionary<string, string> replacements = new Dictionary<string, string>
            {
                { "{{CAT_NAME}}", "猫又" },
                { "{{CAT_PRONOUN}}", "私" },
                { "{{CAT_GENDER}}", "メス" },
                { "{{PARENT}}", "親" },
                { "{{PLAYER_CALLING}}", "ご主人" },
                { "{{PLAYER_NAME}}", "八雲" },
                { "{{TIME_OF_DAY}}", TimeZoneLabel(state.timeZone) },
                { "{{NEXT_TIME_OF_DAY}}", NextTimeZoneLabel(state.timeZone) }
            };

            string processed = text
                .Replace(@"\{\{", "{{")
                .Replace(@"\}\}", "}}")
                .Replace(@"\_", "_");
            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                processed = processed.Replace(replacement.Key, replacement.Value);
            }

            return processed;
        }

        private string BuildStateSummary()
        {
            List<string> items = new List<string>
            {
                $"愛情:{StateLevelLabel(state.affectionLevel)}",
                $"ドS:{StateLevelLabel(state.sadisticLevel)}",
                $"心配:{StateLevelLabel(state.concernLevel)}",
                $"敵対:{StateLevelLabel(state.hostilityLevel)}",
                $"従順:{StateLevelLabel(state.obedienceLevel)}",
                $"本能:{StateLevelLabel(state.instinctLevel)}",
                $"時間:{TimeZoneLabel(state.timeZone)}"
            };

            string defeatReason = ResolvePreviewDefeatReason();
            if (!string.IsNullOrWhiteSpace(defeatReason))
            {
                items.Add($"死亡理由:{defeatReason}");
            }

            return string.Join(" / ", items);
        }

        private string ResolvePreviewDefeatReason()
        {
            if (!string.IsNullOrWhiteSpace(state.lastPlayerDefeatReason))
            {
                return state.lastPlayerDefeatReason.Trim();
            }

            if (state.playerDefeatedByNekomata) return "Nekomata";
            if (state.playerDefeatedByPredation) return "Predation";
            if (state.playerDefeatedByAnger) return "Anger";
            if (state.playerDefeatedByAccident) return "Accident";
            return string.Empty;
        }

        private string BuildPreviewWarnings(DialoguePatternGroup group)
        {
            List<string> warnings = new List<string>();
            DialogueEntry currentEntry = group != null ? GetCurrentEntry(group) : null;

            if (group != null && group.Entries.Count >= 2)
            {
                warnings.Add($"Lineが{group.Entries.Count}行あります（現在選択中のLineを表示中）");
            }

            string speechControlBadge = BuildSpeechControlBadge(currentEntry);
            if (!string.IsNullOrEmpty(speechControlBadge))
            {
                warnings.Add(speechControlBadge);
            }

            if (currentEntry != null && currentEntry.CallOnly)
            {
                warnings.Add("[CALL ONLY]");
            }

            if (currentEntry != null)
            {
                string normalizedResponseType = NormalizeResponseType(currentEntry.ResponseType);
                bool usesYarn = IsYarnActionId(currentEntry.ActionId);
                List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(currentEntry);

                if (string.Equals(normalizedResponseType, "Event", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(currentEntry.ActionId))
                    {
                        warnings.Add("[EVENT] action_id が空です。Yarn 開始先が未設定です");
                    }
                    else if (!usesYarn)
                    {
                        warnings.Add("[EVENT] action_id が yarn: ではないため Yarn 進行になりません");
                    }
                }

                if (string.Equals(normalizedResponseType, "Choice", StringComparison.Ordinal))
                {
                    if (additionalChoiceNumbers.Count > 0 && !usesYarn)
                    {
                        warnings.Add("[CHOICE] 3択以上は CSV ランタイム未対応です。action_id=yarn:NodeName で Yarn に委譲してください");
                    }
                }

                if (string.Equals(normalizedResponseType, "Reaction", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(currentEntry.ActionId))
                {
                    warnings.Add("[REACTIVE] action_id は実行されません。Yarn/外部処理に渡すなら Event か Action を使ってください");
                }

                if (string.Equals(normalizedResponseType, "Normal", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(currentEntry.ActionId))
                {
                    warnings.Add("[NORMAL] action_id は旧互換扱いです。新規運用では Event / Action / Choice を使ってください");
                }
            }

            string sequenceResetSummary = BuildSequenceResetSummary(group);
            if (!string.IsNullOrEmpty(sequenceResetSummary))
            {
                warnings.Add(sequenceResetSummary);
            }

            return string.Join("\n", warnings);
        }

        private string BuildChoiceSummary(DialogueEntry entry)
        {
            if (entry == null || !string.Equals(NormalizeResponseType(entry.ResponseType), "Choice", StringComparison.Ordinal))
            {
                return "-";
            }

            List<string> parts = new List<string>
            {
                $"Yes->{BuildChoiceTargetSummary(entry.ChoiceYesPattern)}",
                $"No->{BuildChoiceTargetSummary(entry.ChoiceNoPattern)}"
            };

            List<int> additionalChoiceNumbers = GetAdditionalChoiceNumbers(entry);
            for (int i = 0; i < additionalChoiceNumbers.Count; i++)
            {
                int choiceNumber = additionalChoiceNumbers[i];
                string pattern = entry.GetAdditionalValue(GetAdditionalChoicePatternKey(choiceNumber));
                string label = choiceNumber.ToString();
                string japaneseLabel = entry.GetAdditionalValue(GetAdditionalChoiceLanguageKey(choiceNumber, "ja"));
                if (!string.IsNullOrWhiteSpace(japaneseLabel))
                {
                    label = japaneseLabel.Trim();
                }

                parts.Add($"{label}->{BuildChoiceTargetSummary(pattern)}");
            }

            return string.Join(" / ", parts);
        }

        private static string BuildChoiceTargetSummary(string pattern)
        {
            return string.IsNullOrWhiteSpace(pattern) ? "Action" : pattern.Trim();
        }

        private string BuildDialogueFlowSummary(DialogueEntry entry)
        {
            if (entry == null)
            {
                return "-";
            }

            string responseType = NormalizeResponseType(entry.ResponseType);
            bool usesYarn = IsYarnActionId(entry.ActionId);
            int additionalChoiceCount = GetAdditionalChoiceNumbers(entry).Count;

            if (string.Equals(responseType, "Reaction", StringComparison.Ordinal))
            {
                return "CSV Reactive";
            }

            if (string.Equals(responseType, "Choice", StringComparison.Ordinal))
            {
                if (usesYarn || additionalChoiceCount > 0)
                {
                    return "Yarn Choice Gateway";
                }

                return "CSV Choice";
            }

            if (string.Equals(responseType, "Event", StringComparison.Ordinal))
            {
                return usesYarn ? "Yarn Event" : "Event Transition";
            }

            if (string.Equals(responseType, "Action", StringComparison.Ordinal))
            {
                return usesYarn ? "Yarn Action Gateway" : "System Action";
            }

            return "Legacy Normal";
        }

        private string BuildResponseRoutingGuidance(DialogueEntry entry, string responseType, string actionId, out MessageType messageType)
        {
            string normalizedResponseType = NormalizeResponseTypeStatic(responseType);
            bool usesYarn = IsYarnActionId(actionId);
            int additionalChoiceCount = entry != null ? GetAdditionalChoiceNumbers(entry).Count : 0;

            if (string.Equals(normalizedResponseType, "Reaction", StringComparison.Ordinal))
            {
                messageType = string.IsNullOrWhiteSpace(actionId) ? MessageType.Info : MessageType.Warning;
                return string.IsNullOrWhiteSpace(actionId)
                    ? "CSV完結の1往復返答です。自由会話の基本運用です"
                    : "Reaction では action_id は実行されません。Yarnや外部処理に渡すなら Event か Action を使ってください";
            }

            if (string.Equals(normalizedResponseType, "Choice", StringComparison.Ordinal))
            {
                if (additionalChoiceCount > 0 && !usesYarn)
                {
                    messageType = MessageType.Warning;
                    return "追加選択肢があります。CSVランタイムは基本2択なので、3択以上は action_id=yarn:NodeName で Yarn に委譲してください";
                }

                if (usesYarn)
                {
                    messageType = MessageType.Info;
                    return "CSVで入力意図を拾って Yarn に渡す Choice です。長い分岐や3択以上はこちらが適しています";
                }

                messageType = MessageType.Info;
                return "CSV完結の Choice です。単純な Yes/No に向いています";
            }

            if (string.Equals(normalizedResponseType, "Event", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    messageType = MessageType.Warning;
                    return "Event ですが action_id が空です。Yarn に渡すなら yarn:NodeName を設定してください";
                }

                if (!usesYarn)
                {
                    messageType = MessageType.Warning;
                    return "Event ですが action_id が yarn: ではありません。会話進行を Yarn に任せるなら yarn:NodeName を使ってください";
                }

                messageType = MessageType.Info;
                return "CSVで意図を理解し、Yarnでイベント会話を進める設定です";
            }

            if (string.Equals(normalizedResponseType, "Action", StringComparison.Ordinal))
            {
                if (usesYarn)
                {
                    messageType = MessageType.Info;
                    return "Action でも Yarn 起動はできますが、会話進行の本線には Event の方が分かりやすいです";
                }

                messageType = MessageType.Info;
                return "CSVから外部処理やシステム行動を呼ぶ設定です";
            }

            messageType = string.IsNullOrWhiteSpace(actionId) ? MessageType.Info : MessageType.Warning;
            return string.IsNullOrWhiteSpace(actionId)
                ? "旧互換の Normal です。特別な後処理は行いません"
                : "Normal の action_id は旧互換扱いです。新規運用では Event / Action / Choice に寄せてください";
        }

        private static bool IsYarnActionId(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   actionId.Trim().StartsWith("yarn:", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildSequenceResetSummary(DialoguePatternGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            DialogueEntry representative = GetRepresentativeEntry(group);
            if (representative == null ||
                !string.Equals(NormalizeSpeechControl(representative.SpeechControl), "Sequence", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string resetTargetPattern = ResolveSequenceResetTargetPattern(group);
            if (string.IsNullOrWhiteSpace(resetTargetPattern))
            {
                return string.Empty;
            }

            return $"[SEQUENCE RESET] 3日経過で Pattern {resetTargetPattern} に戻る";
        }

        private string ResolveSequenceResetTargetPattern(DialoguePatternGroup currentGroup)
        {
            if (currentGroup == null)
            {
                return string.Empty;
            }

            string regexId = currentGroup.RegexId ?? string.Empty;
            string fallbackPattern = currentGroup.Pattern ?? string.Empty;
            string resetPattern = string.Empty;

            for (int i = 0; i < patternGroups.Count; i++)
            {
                DialoguePatternGroup candidateGroup = patternGroups[i];
                if (candidateGroup == null ||
                    !string.Equals(candidateGroup.RegexId ?? string.Empty, regexId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DialogueEntry representative = GetRepresentativeEntry(candidateGroup);
                if (representative == null ||
                    !string.Equals(NormalizeSpeechControl(representative.SpeechControl), "Sequence", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resetPattern))
                {
                    resetPattern = candidateGroup.Pattern ?? string.Empty;
                }

                if (representative.SequenceProgressHold)
                {
                    resetPattern = candidateGroup.Pattern ?? resetPattern;
                }

                if (ReferenceEquals(candidateGroup, currentGroup))
                {
                    break;
                }
            }

            return string.IsNullOrWhiteSpace(resetPattern) ? fallbackPattern : resetPattern;
        }

        private static string StateLevelLabel(StateLevel level)
        {
            switch (level)
            {
                case StateLevel.High:
                    return "High";
                case StateLevel.Max:
                    return "MAX";
                default:
                    return "Low";
            }
        }

        private static string TimeZoneLabel(DialoguePreviewTimeZone timeZone)
        {
            switch (timeZone)
            {
                case DialoguePreviewTimeZone.Morning:
                    return "朝";
                case DialoguePreviewTimeZone.Evening:
                    return "夕";
                case DialoguePreviewTimeZone.Night:
                    return "夜";
                default:
                    return "昼";
            }
        }

        private static string NextTimeZoneLabel(DialoguePreviewTimeZone timeZone)
        {
            switch (timeZone)
            {
                case DialoguePreviewTimeZone.Morning:
                    return "昼";
                case DialoguePreviewTimeZone.Day:
                    return "夕";
                case DialoguePreviewTimeZone.Evening:
                    return "夜";
                default:
                    return "朝";
            }
        }

        private void RebuildPatternCache()
        {
            RebuildPatternCache(null);
        }

        private void RebuildPatternCache(DialogueEntry focusEntry)
        {
            regexAuthoringValidationDirty = true;
            patternGroups.Clear();

            if (dataSet == null || dataSet.Entries == null)
            {
                currentPatternIndex = 0;
                currentSequenceIndex = 0;
                return;
            }

            Dictionary<string, DialoguePatternGroup> groupsByKey = new Dictionary<string, DialoguePatternGroup>();
            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                DialogueEntry entry = dataSet.Entries[i];
                string patternKey = BuildPatternKey(entry, i);
                if (!groupsByKey.TryGetValue(patternKey, out DialoguePatternGroup group))
                {
                    group = new DialoguePatternGroup
                    {
                        RegexId = entry.RegexId,
                        Pattern = entry.Pattern,
                        FirstEntryIndex = i
                    };
                    groupsByKey.Add(patternKey, group);
                    patternGroups.Add(group);
                }

                group.Entries.Add(entry);
            }

            for (int i = 0; i < patternGroups.Count; i++)
            {
                SortPatternEntries(patternGroups[i]);
            }

            patternGroups.Sort((left, right) =>
            {
                int idCompare = CompareInternalIdOrder(
                    left != null ? left.GetRepresentativeEntry()?.InternalId : null,
                    right != null ? right.GetRepresentativeEntry()?.InternalId : null);
                if (idCompare != 0)
                {
                    return idCompare;
                }

                int patternCompare = ComparePatternOrder(left != null ? left.Pattern : null, right != null ? right.Pattern : null);
                if (patternCompare != 0)
                {
                    return patternCompare;
                }

                int regexCompare = CompareRegexIdOrder(left != null ? left.RegexId : null, right != null ? right.RegexId : null);
                if (regexCompare != 0)
                {
                    return regexCompare;
                }

                int leftFirstEntryIndex = left != null ? left.FirstEntryIndex : int.MaxValue;
                int rightFirstEntryIndex = right != null ? right.FirstEntryIndex : int.MaxValue;
                return leftFirstEntryIndex.CompareTo(rightFirstEntryIndex);
            });

            if (focusEntry != null)
            {
                for (int patternIndex = 0; patternIndex < patternGroups.Count; patternIndex++)
                {
                    int sequenceIndex = patternGroups[patternIndex].Entries.IndexOf(focusEntry);
                    if (sequenceIndex >= 0)
                    {
                        currentPatternIndex = patternIndex;
                        currentSequenceIndex = sequenceIndex;
                        return;
                    }
                }
            }

            currentPatternIndex = Mathf.Clamp(currentPatternIndex, 0, Mathf.Max(0, patternGroups.Count - 1));
            currentSequenceIndex = 0;
        }

        private void SortPatternEntries(DialoguePatternGroup group)
        {
            group.Entries.Sort((a, b) =>
            {
                int patternCompare = ComparePatternOrder(a != null ? a.Pattern : null, b != null ? b.Pattern : null);
                if (patternCompare != 0)
                {
                    return patternCompare;
                }

                int orderCompare = (a != null ? a.Order : int.MaxValue).CompareTo(b != null ? b.Order : int.MaxValue);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                int idCompare = CompareInternalIdOrder(a != null ? a.InternalId : null, b != null ? b.InternalId : null);
                if (idCompare != 0)
                {
                    return idCompare;
                }

                int regexCompare = CompareRegexIdOrder(a != null ? a.RegexId : null, b != null ? b.RegexId : null);
                if (regexCompare != 0)
                {
                    return regexCompare;
                }

                int leftLine = a != null ? a.SourceLineNumber : int.MaxValue;
                int rightLine = b != null ? b.SourceLineNumber : int.MaxValue;
                return leftLine.CompareTo(rightLine);
            });
        }

        private void NormalizeGroupEntryOrders(DialoguePatternGroup group)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return;
            }

            SortPatternEntries(group);
            for (int i = 0; i < group.Entries.Count; i++)
            {
                group.Entries[i].Order = i + 1;
            }
        }

        private int GetNextSourceLineNumber()
        {
            if (dataSet == null || dataSet.Entries == null || dataSet.Entries.Count == 0)
            {
                return 1;
            }

            int maxSourceLineNumber = 0;
            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                maxSourceLineNumber = Mathf.Max(maxSourceLineNumber, dataSet.Entries[i].SourceLineNumber);
            }

            return maxSourceLineNumber + 1;
        }

        private static DialogueEntry CloneEntry(DialogueEntry source)
        {
            DialogueEntry clone = new DialogueEntry
            {
                SourceLineNumber = source.SourceLineNumber,
                InternalId = source.InternalId,
                RegexPattern = source.RegexPattern,
                RegexPatternChinese = source.RegexPatternChinese,
                RegexPatternEnglish = source.RegexPatternEnglish,
                RegexId = source.RegexId,
                Pattern = source.Pattern,
                Order = source.Order,
                Text = source.Text,
                TextChinese = source.TextChinese,
                TextEnglish = source.TextEnglish,
                ResponseType = source.ResponseType,
                ActionId = source.ActionId,
                TimedEventKey = source.TimedEventKey,
                Condition = source.Condition,
                EmotionChangeType = source.EmotionChangeType,
                EmotionChangeValue = source.EmotionChangeValue,
                WaitTime = source.WaitTime,
                HideUI = source.HideUI,
                Animation = source.Animation,
                ReactionIcon = source.ReactionIcon,
                Priority = source.Priority,
                Intent = source.Intent,
                ReactionType = source.ReactionType,
                RepeatCount = source.RepeatCount,
                SpeechControl = source.SpeechControl,
                TargetPattern = source.TargetPattern,
                CallOnly = source.CallOnly,
                ChoiceYesPattern = source.ChoiceYesPattern,
                ChoiceNoPattern = source.ChoiceNoPattern,
                SequenceProgressHold = source.SequenceProgressHold,
                RandomRepeatLimit = source.RandomRepeatLimit
            };

            for (int fieldIndex = 0; fieldIndex < source.AdditionalFields.Count; fieldIndex++)
            {
                DialogueExtraField field = source.AdditionalFields[fieldIndex];
                clone.AdditionalFields.Add(new DialogueExtraField(field.Key, field.Value));
            }

            return clone;
        }

        private static int ComparePatternOrder(string leftPattern, string rightPattern)
        {
            bool leftParsed = int.TryParse(leftPattern, out int leftValue);
            bool rightParsed = int.TryParse(rightPattern, out int rightValue);
            if (leftParsed && rightParsed)
            {
                return leftValue.CompareTo(rightValue);
            }

            return string.Compare(leftPattern, rightPattern, StringComparison.Ordinal);
        }

        private static int CompareRegexIdOrder(string leftRegexId, string rightRegexId)
        {
            string left = string.IsNullOrWhiteSpace(leftRegexId) ? string.Empty : leftRegexId.Trim();
            string right = string.IsNullOrWhiteSpace(rightRegexId) ? string.Empty : rightRegexId.Trim();

            bool leftParsed = int.TryParse(left, out int leftValue);
            bool rightParsed = int.TryParse(right, out int rightValue);
            if (leftParsed && rightParsed)
            {
                int numericCompare = leftValue.CompareTo(rightValue);
                if (numericCompare != 0)
                {
                    return numericCompare;
                }
            }

            if (leftParsed != rightParsed)
            {
                return leftParsed ? -1 : 1;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareInternalIdOrder(string leftId, string rightId)
        {
            bool leftParsed = int.TryParse(leftId, out int leftValue);
            bool rightParsed = int.TryParse(rightId, out int rightValue);
            if (leftParsed && rightParsed)
            {
                return leftValue.CompareTo(rightValue);
            }

            return string.Compare(leftId ?? string.Empty, rightId ?? string.Empty, StringComparison.Ordinal);
        }

        private string BuildPatternKey(DialogueEntry entry, int index)
        {
            if (entry == null)
            {
                return $"(null)|__line_{index}";
            }

            if (!transientPatternGroupKeys.TryGetValue(entry, out string patternKey))
            {
                patternKey = BuildSavedPatternKey(entry, index);
                transientPatternGroupKeys[entry] = patternKey;
            }

            return patternKey;
        }

        private static string BuildSavedPatternKey(DialogueEntry entry, int index)
        {
            string regexId = string.IsNullOrWhiteSpace(entry.RegexId) ? "(empty)" : entry.RegexId.Trim();
            string internalId = string.IsNullOrWhiteSpace(entry.InternalId) ? "(empty-id)" : entry.InternalId.Trim();
            string pattern = string.IsNullOrWhiteSpace(entry.Pattern) ? $"__line_{index}" : entry.Pattern.Trim();
            return regexId + "|" + internalId + "|" + pattern;
        }

        private void ResetTransientPatternGroupKeys()
        {
            transientPatternGroupKeys.Clear();
            if (dataSet == null || dataSet.Entries == null)
            {
                return;
            }

            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                DialogueEntry entry = dataSet.Entries[i];
                transientPatternGroupKeys[entry] = BuildSavedPatternKey(entry, i);
            }
        }

        private string NormalizeResponseType(string responseType)
        {
            if (string.Equals(responseType, "Choice", StringComparison.OrdinalIgnoreCase))
            {
                return "Choice";
            }

            if (string.Equals(responseType, "Action", StringComparison.OrdinalIgnoreCase))
            {
                return "Action";
            }

            if (string.Equals(responseType, "Event", StringComparison.OrdinalIgnoreCase))
            {
                return "Event";
            }

            if (string.Equals(responseType, "Reaction", StringComparison.OrdinalIgnoreCase))
            {
                return "Reaction";
            }

            return "Normal";
        }

        private static string NormalizeEmotionChangeType(string emotionChangeType)
        {
            if (string.IsNullOrWhiteSpace(emotionChangeType))
            {
                return string.Empty;
            }

            string normalized = emotionChangeType.Trim().ToLowerInvariant();
            for (int i = 1; i < EmotionChangeTypeOptions.Length; i++)
            {
                if (string.Equals(EmotionChangeTypeOptions[i], normalized, StringComparison.Ordinal))
                {
                    return EmotionChangeTypeOptions[i];
                }
            }

            return string.Empty;
        }

        private static int GetEmotionChangeValuePopupIndex(int value)
        {
            int normalizedValue = NormalizeEmotionChangeValue(value);
            int index = Array.IndexOf(EmotionChangeValueOptions, normalizedValue);
            return index >= 0 ? index : 0;
        }

        private static int NormalizeEmotionChangeValue(int value)
        {
            if (value <= -3)
            {
                return -3;
            }

            if (value >= 3)
            {
                return 3;
            }

            if (value == 0)
            {
                return 0;
            }

            return value;
        }

        private bool ConfirmDiscardChanges()
        {
            if (!hasPendingChanges)
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Unsaved Changes",
                "現在の編集内容は未保存です。破棄して別のCSVを読み込みますか？",
                "破棄して続行",
                "キャンセル");
        }

        private void ApplyLoadedDataSet(DialogueDataLoader.DataSet loadedDataSet)
        {
            dataSet = loadedDataSet ?? new DialogueDataLoader.DataSet();
            DialogueAuthoringIdentity.Bind(dataSet);
            if (string.IsNullOrWhiteSpace(dataSet.SavePath) && !dataSet.IsDerivedFromGroupedSource)
                dataSet.SavePath = dataSet.SourcePath ?? string.Empty;
            dataSet.SaveLabel = string.IsNullOrWhiteSpace(dataSet.SaveLabel) ? Path.GetFileName(dataSet.SavePath) : dataSet.SaveLabel;
            ResetTransientPatternGroupKeys();
            regexStatus = "未実行";
            regexAuthoringValidationDirty = true;
            regexTestWarnings.Clear();
            currentRegexMatchResults.Clear();
            otherCsvRegexMatchSections.Clear();
            ResetUnknownWordPreview();
            matchedRegexIds.Clear();
            matchedPatternIndices.Clear();
            cachedPreviewStatesBySelectionKey.Clear();
            lastStateSyncSelectionKey = string.Empty;
            hasPendingChanges = false;
        }

        private void RestoreSessionState()
        {
            DialoguePreviewSessionState sessionState = ReadSessionState();
            if (sessionState == null)
            {
                return;
            }

            selectedTab = Mathf.Clamp(sessionState.SelectedTab, 0, 1);
            if (dataSet != null && dataSet.Entries != null && dataSet.Entries.Count > 0)
            {
                if (Path.GetFileName(dataSet.SourcePath) == "DialoguePreview_Integrated.csv" && !hasPendingChanges)
                    ApplyLoadedDataSet(DialogueDataLoader.LoadFromPath("TalkSource/TalkCSV/DialoguePreview.csv"));
                return;
            }

            string restorePath = sessionState.SavePath;
            if (Path.GetFileName(restorePath) == "DialoguePreview_Integrated.csv" || Path.GetFileName(sessionState.SourcePath) == "DialoguePreview_Integrated.csv")
            { restorePath = "TalkSource/TalkCSV/DialoguePreview.csv"; sessionState.SourcePath = sessionState.SavePath = restorePath; }
            if (string.IsNullOrWhiteSpace(restorePath))
            {
                restorePath = sessionState.SourcePath;
            }

            if (string.IsNullOrWhiteSpace(restorePath))
            {
                return;
            }

            try
            {
                string restoreFullPath = DialogueDataLoader.GetFullPath(restorePath);
                if (!File.Exists(restoreFullPath) && !string.IsNullOrWhiteSpace(sessionState.SourcePath))
                {
                    restorePath = sessionState.SourcePath;
                }

                ApplyLoadedDataSet(DialogueDataLoader.LoadFromPath(restorePath));
                if (!string.IsNullOrWhiteSpace(sessionState.SavePath))
                {
                    dataSet.SavePath = sessionState.SavePath;
                    dataSet.SaveLabel = Path.GetFileName(sessionState.SavePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialoguePreview] 前回のCSVを復元できませんでした: {sessionState.SourcePath}\n{exception.Message}");
            }
        }

        private void ApplySessionSelection()
        {
            DialoguePreviewSessionState sessionState = ReadManuscriptState() ?? ReadSessionState();
            if (sessionState == null || patternGroups.Count == 0 || dataSet == null)
            {
                return;
            }

            selectedTab = Mathf.Clamp(sessionState.SelectedTab, 0, 1);
            if (!string.Equals(DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath), DialogueAuthoringIdentity.NormalizeSource(sessionState.SourcePath), StringComparison.OrdinalIgnoreCase) &&
                !(CurrentProfile?.Id == "normal-dialogue" && Path.GetFileName(sessionState.SourcePath) == "DialoguePreview_Integrated.csv"))
            {
                return;
            }

            RestoreAuthoringView(sessionState);
            int restoredPatternIndex = FindPatternIndex(sessionState);
            if (restoredPatternIndex < 0)
            {
                return;
            }

            currentPatternIndex = restoredPatternIndex;
            currentSequenceIndex = FindSequenceIndex(patternGroups[restoredPatternIndex], sessionState);
        }

        private void TryPersistSessionState()
        {
            if (isPersistingSessionState)
            {
                return;
            }

            try
            {
                isPersistingSessionState = true;
                PersistSessionState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialoguePreview] セッション状態の保存に失敗しました: {exception.Message}");
            }
            finally
            {
                isPersistingSessionState = false;
            }
        }

        private void PersistSessionState()
        {
            string json = JsonUtility.ToJson(BuildSessionState());
            EditorPrefs.SetString(SessionStateEditorPrefsKey, json);
            if (!string.IsNullOrWhiteSpace(dataSet.SourcePath)) EditorPrefs.SetString(ManuscriptStateKey(dataSet.SourcePath), json);
        }

        private DialoguePreviewSessionState BuildSessionState()
        {
            DialoguePatternGroup group = null;
            if (currentPatternIndex >= 0 && currentPatternIndex < patternGroups.Count)
            {
                group = patternGroups[currentPatternIndex];
            }

            DialogueEntry entry = null;
            if (group != null &&
                group.Entries != null &&
                currentSequenceIndex >= 0 &&
                currentSequenceIndex < group.Entries.Count)
            {
                entry = group.Entries[currentSequenceIndex];
            }

            return new DialoguePreviewSessionState
            {
                SourcePath = dataSet != null ? dataSet.SourcePath ?? string.Empty : string.Empty,
                SavePath = dataSet != null ? dataSet.SavePath ?? string.Empty : string.Empty,
                RegexId = group != null ? group.RegexId ?? string.Empty : string.Empty,
                DialogueId = entry != null ? entry.EditorDialogueId ?? string.Empty : string.Empty,
                Pattern = group != null ? group.Pattern ?? string.Empty : string.Empty,
                PatternIndex = currentPatternIndex,
                SequenceIndex = currentSequenceIndex,
                EntryOrder = entry != null ? entry.Order : 0,
                SelectedTab = selectedTab, RelatedTab = activeRelatedTab, Details = showDetails, Behaviour = showBehaviour,
                RelatedLayoutVersion = 1, TimeDiary = showTimeDiary, Topics = showTopics, Readings = showReadings,
                Branches = showBranches, Development = showDevelopment, ConditionExpression = showConditionExpression, Teaching = showTeaching, RegexInput = regexInput
            };
        }

        private DialoguePreviewSessionState ReadSessionState()
        {
            string json = EditorPrefs.GetString(SessionStateEditorPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<DialoguePreviewSessionState>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private int FindPatternIndex(DialoguePreviewSessionState sessionState)
        {
            if (!string.IsNullOrEmpty(sessionState.DialogueId))
            {
                int stableIndex = patternGroups.FindIndex(group => group.Entries.Any(entry => entry.EditorDialogueId == sessionState.DialogueId));
                if (stableIndex >= 0) return stableIndex;
            }
            for (int i = 0; i < patternGroups.Count; i++)
            {
                DialoguePatternGroup group = patternGroups[i];
                if (string.Equals(group.RegexId ?? string.Empty, sessionState.RegexId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(group.Pattern ?? string.Empty, sessionState.Pattern ?? string.Empty, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            if (sessionState.PatternIndex >= 0 && sessionState.PatternIndex < patternGroups.Count)
            {
                return sessionState.PatternIndex;
            }

            return -1;
        }

        private int FindSequenceIndex(DialoguePatternGroup group, DialoguePreviewSessionState sessionState)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < group.Entries.Count; i++)
            {
                if (group.Entries[i].Order == sessionState.EntryOrder)
                {
                    return i;
                }
            }

            return Mathf.Clamp(sessionState.SequenceIndex, 0, group.Entries.Count - 1);
        }

        private void EnsureStyles()
        {
            if (compactInfoStyle == null)
            {
                compactInfoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }
        }
    }
}
