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
        public Vector2? SuggestedPosition;
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
        public bool IsRuntimeFlow;
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
        internal const string UnknownWordSource = "未知語学習（Runtime）";
        internal const string UnknownWordId = "UnknownWord";
        internal const string UnknownWordPath = "Assets/Scripts/Dialogue/UnknownWord/WordLearningController.cs";
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
            AddUnknownWordFlow(data);
            data.Sources.Sort(StringComparer.OrdinalIgnoreCase);
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
                if (entry.ActionId == "unknown_word_teach_event" ||
                    (entry.SourceName == "BasicSystemDialogue.csv" && entry.Pattern == "unknown_word_prompt"))
                {
                    data.Nodes[entry.Key].Detail += "\n\n現在はWordLearningControllerが表示と分岐を担当します。旧CSVの選択肢・120分イベントは実行しません。";
                    data.Edges.Add(new DialogueGraphEdge { From = tail, To = "runtime:unknown:prompt", Label = "学習フロー" });
                    continue;
                }
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
        /// <summary>Editor-only projection of the controller's explicit states, never executed or saved as dialogue.</summary>
        internal static void AddUnknownWordFlow(DialogueGraphData data)
        {
            var entry = new DialogueGraphEntry {
                Key = "runtime:unknown:entry", SourceName = UnknownWordSource, SourcePath = UnknownWordPath, IsRuntimeFlow = true,
                Values = new Dictionary<string, string> { ["id"] = UnknownWordId, ["input"] = "初回学習・再学習・あいさつ・忘却", ["pattern"] = "WordLearningController" }
            };
            data.Entries.Add(entry); data.Sources.Add(UnknownWordSource);
            void Node(string key, DialogueGraphNodeKind kind, string title, string body, int column, int lane, string method, string assetPath = UnknownWordPath)
            {
                string id = "runtime:unknown:" + key;
                AddNode(data, id, kind, title, body, entry, "現行Runtimeの表示用フロー（実行・保存はしません）\n\n" + title + "\n" + body + "\n\n実装: " + assetPath + "\n処理: " + method);
                data.Nodes[id].SuggestedPosition = new Vector2(30 + column * 270, 30 + lane * 145);
                data.Nodes[id].Asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            }
            void Link(string from, string to, string label = "") => data.Edges.Add(new DialogueGraphEdge { From = "runtime:unknown:" + from, To = "runtime:unknown:" + to, Label = label });
            Node("entry", DialogueGraphNodeKind.Dynamic, "未知語への入口", "通常Regex・既存Teaching優先\n不一致時だけ学習辞書へ", 0, 0, "DialogueEngine.ProcessInputInternal / DialogueManager.BeginWordLearning", "Assets/Scripts/System/DialogueEngine.cs");
            Node("match", DialogueGraphNodeKind.Condition, "学習語を照合", "完全一致→部分一致\n元入力全体を保持", 1, 0, "LearnedWordStore.Match", "Assets/Scripts/Dialogue/UnknownWord/LearnedWordStore.cs");
            Node("prompt", DialogueGraphNodeKind.Choice, "初めての言葉", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("unknown_prompt", "○○") + "\nタイプライター表示\n［はい］［いいえ］", 2, 0, "Begin");
            Node("meaning", DialogueGraphNodeKind.Dialogue, "意味を尋ねる", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("meaning_prompt", "○○") + "\n次へ進めるまでフォーム非表示", 3, 0, "Begin / OpenEditingAfterMeaningPrompt");
            Node("edit", DialogueGraphNodeKind.Dynamic, "右側：言葉を修正", "初回：抽出語を編集\n教え直す：空欄・元ID維持\n確認前は記憶しない", 4, 0, "Edit / ConfirmForm");
            Node("latin", DialogueGraphNodeKind.Condition, "ひらがなカタカナ以外？", "ひらがなカタカナ以外を含む？\n漢字・英字・数字・記号等\n通常チャットの制約は維持", 5, 0, "RefreshReading / ConfirmForm");
            Node("reading", DialogueGraphNodeKind.Dynamic, "右側：読み方", "元入力を初期値に編集\n表記と読みを別管理", 6, 0, "Edit / RefreshReading");
            Node("genre", DialogueGraphNodeKind.Choice, "右側：ジャンル", string.Join("／", Nekolpos.Dialogue.UnknownWord.WordKnowledge.GenreLabels), 7, 0, "ConfirmForm");
            Node("greetingForm", DialogueGraphNodeKind.Dynamic, "右側：いつ使う？", "朝／昼／夕／夜・会う／別れる\n複数選択、軸間AND\n未選択の軸は制限なし", 8, 0, "GreetingForm / ConfirmForm");
            string pool = string.Join("\n\n", Enum.GetValues(typeof(Nekolpos.Dialogue.UnknownWord.WordGenre)).Cast<Nekolpos.Dialogue.UnknownWord.WordGenre>()
                .Select(g => Nekolpos.Dialogue.UnknownWord.WordKnowledge.GenreLabels[(int)g] + "\n" + string.Join("\n", Nekolpos.Dialogue.UnknownWord.WordKnowledge.Questions(g).Select(q => q.Id + ": " + q.Prompt + "（はい: " + q.YesSummary + " → " + q.YesFinal + "／いいえ: " + q.NoSummary + " → " + q.NoFinal + "）"))));
            Node("question", DialogueGraphNodeKind.Choice, "未回答からランダムに1問", "はい／いいえ／わからない\nやっぱりやめる\n質問プールは右の詳細へ", 8, 1, "Ask / WordKnowledge.RandomQuestion・Questions");
            data.Nodes["runtime:unknown:question"].Detail += "\n\n現在の質問プール\n" + pool + "\n\n発話テンプレート: " + Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.SourcePath;
            Node("save", DialogueGraphNodeKind.StateChange, "単語を保存", "固定ID・読み・ジャンル・回答\nあいさつ条件を永続保存\n時間経過なし", 9, 0, "SaveAndFinish / LearnedWordStore.Put", "Assets/Scripts/Dialogue/UnknownWord/LearnedWordStore.cs");
            Node("summary", DialogueGraphNodeKind.Choice, "覚えた内容を要約", "全回答ならマスター反応\n続けて教える／会話に戻る", 10, 0, "SaveAndFinish / WordKnowledge.Summary");
            Node("return", DialogueGraphNodeKind.End, "通常会話へ復帰", "フォーム・選択肢を閉じる\n入力待ちへ", 11, 0, "Cancel / DialogueManager.BeginWordLearning callback");
            Node("complete", DialogueGraphNodeKind.Condition, "回答コンプリート？", "わからないも回答済み\nSummaryは肯定/否定だけ", 2, 3, "WordKnowledge.Next");
            Node("knownComplete", DialogueGraphNodeKind.Choice, "コンプ済みの言葉", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("known_complete", "○○", "要約") + "\n既存の教え直し・別語・取消", 3, 3, "Known");
            Node("known", DialogueGraphNodeKind.Choice, "覚えた一般語・部分一致", "教え直す／別の言葉だよ\nなんでもない", 2, 4, "Known");
            Node("other", DialogueGraphNodeKind.Dynamic, "別の言葉を登録", "元入力全体を初期値に\n新しい固定IDで独立登録", 3, 5, "Known: 別の言葉だよ");
            Node("greetingMatch", DialogueGraphNodeKind.Condition, "覚えたあいさつ（完全一致）", "条件外でも認識する\n今の時間帯・場面に合う？", 2, 7, "Known / WordKnowledge.MatchesGreeting");
            Node("greetingYes", DialogueGraphNodeKind.Dialogue, "条件に合う反応", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("greeting_match", "○○"), 3, 7, "Known");
            Node("greetingNo", DialogueGraphNodeKind.Dialogue, "条件外の反応", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("greeting_mismatch", "○○"), 3, 8, "Known");
            Node("greetingChoice", DialogueGraphNodeKind.Choice, "あいさつを教え直す？", "教え直す／そのままでいい\n教え直すは現設定から開始", 4, 7, "Known");
            Node("abort", DialogueGraphNodeKind.End, "内部記録・会話強制終了", "エラー文はゲームに出さない\nフォームと会話を閉じる", 9, 3, "AbortLearning");
            Node("nextWord", DialogueGraphNodeKind.Dynamic, "次の新しい言葉", Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.Format("teacch_more") + "\n新ID・表記/読み空欄", 4, 3, "BeginNextWord");
            Node("teachEvent", DialogueGraphNodeKind.TimedEvent, "unknown_word_teach_event", "教え終えて終了した時に1回\n既存120分・結果表示・日記経路", 10, 0, "BeginWordLearning / RunSystemTimedEventAsync", "Assets/Scripts/System/DialogueManager.cs");
            Node("variables", DialogueGraphNodeKind.Dynamic, "通常会話への再利用", "カテゴリRandom・HasValue\nあいさつは使用条件を照合\n学習語を優遇、会話内値固定", 10, 2, "WordVariables / BasicSystemDialogueCatalog", "Assets/Scripts/Dialogue/UnknownWord/LearnedWordStore.cs");
            Node("memo", DialogueGraphNodeKind.Dynamic, "タブレット：言葉メモ", "表記・読み・ジャンル・回答\nあいさつ条件を確認／語を選ぶ", 0, 11, "WordMemoController.Refresh / Select", "Assets/Scripts/Dialogue/UnknownWord/WordMemoController.cs");
            Node("forgetCamera", DialogueGraphNodeKind.Dynamic, "ネルコへカメラを向ける", "既存カメラで会話視点へ\nタブレット閲覧所有権を保持", 1, 11, "RequestForget / BeginTabletWordConversation", "Assets/Scripts/Dialogue/UnknownWord/WordMemoController.cs");
            Node("forgetChoice", DialogueGraphNodeKind.Choice, "忘れさせる確認", "○○ってもう言わないで欲しいの？\n忘れさせる／やっぱりやめる", 2, 11, "ForgetRoutine", "Assets/Scripts/Dialogue/UnknownWord/WordMemoController.cs");
            Node("forget", DialogueGraphNodeKind.StateChange, "対象語だけ削除", "固定IDの学習語だけ削除\nRandomキャッシュを破棄\n基本語・Teaching・ログは維持", 3, 11, "LearnedWordStore.Forget", "Assets/Scripts/Dialogue/UnknownWord/LearnedWordStore.cs");
            Node("forgetResult", DialogueGraphNodeKind.Choice, "忘却の結果", "わかったよ\nメモに戻る\n失敗は内部記録し即復帰", 4, 11, "ForgetRoutine", "Assets/Scripts/Dialogue/UnknownWord/WordMemoController.cs");
            Node("memoReturn", DialogueGraphNodeKind.End, "タブレットへ復帰", "元のメモへ安全に復帰\n取消時は記憶を変更しない", 5, 11, "RestoreTablet / EndTabletWordConversation", "Assets/Scripts/Dialogue/UnknownWord/WordMemoController.cs");
            Link("entry", "match"); Link("match", "prompt", "未学習"); Link("match", "complete", "学習済み"); Link("complete", "knownComplete", "全回答済み"); Link("complete", "known", "未完了一般・部分"); Link("complete", "greetingMatch", "未完了挨拶");
            Link("knownComplete", "edit", "一般語を教え直す"); Link("knownComplete", "other", "別の言葉"); Link("knownComplete", "greetingForm", "挨拶を教え直す"); Link("knownComplete", "return", "取消・そのまま");
            Link("prompt", "meaning", "はい"); Link("prompt", "return", "いいえ"); Link("meaning", "edit", "次へ");
            Link("edit", "latin", "確認"); Link("latin", "reading", "かな以外"); Link("latin", "genre", "かなのみ"); Link("reading", "genre", "確認");
            Link("edit", "return", "取消"); Link("reading", "return", "取消"); Link("genre", "return", "取消");
            Link("genre", "question", "初回一般語"); Link("genre", "greetingForm", "初回挨拶"); Link("greetingForm", "save", "確認"); Link("greetingForm", "return", "取消");
            Link("question", "save", "はい／いいえ"); Link("question", "save", "わからない（回答済み）"); Link("question", "return", "取消"); Link("save", "summary", "成功");
            Link("save", "variables", "辞書更新"); Link("summary", "teachEvent", "終了"); Link("teachEvent", "return", "結果後");
            Link("known", "other", "別の言葉"); Link("known", "return", "なんでもない"); Link("other", "edit");
            Link("greetingMatch", "greetingYes", "条件一致"); Link("greetingMatch", "greetingNo", "条件外"); Link("greetingYes", "greetingChoice"); Link("greetingNo", "greetingChoice"); Link("greetingChoice", "greetingForm", "教え直す"); Link("greetingChoice", "return", "そのまま");
            Link("known", "edit", "教え直す"); Link("summary", "nextWord", "続ける"); Link("nextWord", "edit");
            Link("edit", "abort", "入力不正"); Link("reading", "abort", "入力不正"); Link("save", "abort", "保存失敗"); Link("abort", "return", "強制終了");
            Link("forget", "memoReturn", "内部エラー");
            Link("memo", "forgetCamera", "忘れさせる"); Link("forgetCamera", "forgetChoice", "移動完了"); Link("forgetChoice", "forget", "確定"); Link("forgetChoice", "memoReturn", "取消"); Link("forget", "forgetResult"); Link("forget", "variables", "候補除去"); Link("forgetResult", "memoReturn", "戻る");
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