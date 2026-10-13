using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nekolpos.System;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public partial class DialoguePreviewWindow
    {
        private bool relatedDataExpanded = true;
        private string relatedDataError;
        private DialogueRelatedCsvRepository timedEvents;
        private string newTimedEventKey = "";
        private string referenceSummary = "未検索";
        private DialogueRelatedCsvRepository topics;
        private DialogueRelatedCsvRepository readings;
        private string readingSurface = "";
        private string readingDiagnosticsCacheKey;
        private string readingDiagnostics;
        private DialogueRelatedCsvRepository diaryCandidates;
        private DialogueRelatedCsvRepository diaryLinks;
        private string newDiaryId = "";
        [SerializeField] private string relatedDraftSnapshot;
        [Serializable] private sealed class RelatedDrafts { public List<DialogueRelatedCsvRepository.Draft> drafts = new List<DialogueRelatedCsvRepository.Draft>(); }
        private void SaveRelatedDrafts(Action publish = null)
        {
            if (timedEvents != null) { MigrateTimedEventDiaries(timedEvents); ValidateTimedEvents(timedEvents); }
            if (topics != null) { topics.RequireUnique("TalkID"); topics.RequireUnique("Source", "SourceKey"); }
            var changed = new[] { timedEvents, topics, readings, diaryCandidates, diaryLinks }.Where(repository => repository != null && repository.HasChanges).ToList();
            void SaveAt(int index)
            {
                if (index >= changed.Count) { if (topics != null) TalkTopicMasterCsvUtility.Synchronize(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.RuntimePath); publish?.Invoke(); return; }
                changed[index].Save(() => SaveAt(index + 1));
            }
            SaveAt(0); CaptureRelatedDrafts();
            SystemTimedEventCatalog.ResetCache(); DialogueDiaryCatalog.ResetCache();
        }
        private void CaptureRelatedDrafts()
        {
            var snapshot = new RelatedDrafts();
            foreach (var repository in new[] { timedEvents, topics, readings, diaryCandidates, diaryLinks }) if (repository != null) snapshot.drafts.Add(repository.Capture());
            relatedDraftSnapshot = JsonUtility.ToJson(snapshot);
        }
        private void RestoreRelatedDrafts()
        {
            if (string.IsNullOrWhiteSpace(relatedDraftSnapshot)) return;
            try
            {
                foreach (var draft in JsonUtility.FromJson<RelatedDrafts>(relatedDraftSnapshot).drafts)
                {
                    var repository = DialogueRelatedCsvRepository.Restore(draft);
                    if (draft.path == "TalkSource/TalkCSV/SystemTimedEvent.csv") timedEvents = repository;
                    else if (draft.path == TalkTopicMasterCsvUtility.SourcePath) topics = repository;
                    else if (draft.path == "TalkSource/TalkCSV/KanjiReadingDictionary.csv") readings = repository;
                    else if (draft.path == DialogueDiaryAuthoring.CandidatePath) diaryCandidates = repository;
                    else if (draft.path == DialogueDiaryAuthoring.LinkPath) diaryLinks = repository;
                }
            }
            catch (Exception exception) { relatedDataError = "関連データ草稿の復元失敗: " + exception.Message; }
        }
        private static readonly string[] ReadingHeaders = { "Surface", "Reading", "Enabled", "NeedsReview", "Category", "SourceFiles", "Note" };
        private static readonly string[] TopicEditableFields = { "TalkTitle", "Genre", "Idea", "Import", "Keywords", "Enabled" };
        private static readonly string[] TimedEventHeaders = { "key", "ja", "en", "zh", "time_minutes", "display_seconds", "notes", "diary_id", "diary_kind", "diary_ja", "diary_en", "diary_zh", "display_variants", "diary_variants", "diary_selection_mode" };

        private void DrawRelatedData(DialoguePatternGroup group)
        {
            relatedDataExpanded = EditorGUILayout.Foldout(relatedDataExpanded, "関連データ", true);
            if (!relatedDataExpanded) return;
            try
            {
                DialogueEntry entry = GetCurrentEntry(group);
                if (entry == null) return;
                if (string.IsNullOrEmpty(entry.EditorDialogueId)) DialogueAuthoringIdentity.Bind(dataSet);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Editor DialogueID", entry.EditorDialogueId ?? "未保存");
                    EditorGUILayout.LabelField("Source CSV", dataSet.SourcePath ?? "未保存");
                    EditorGUILayout.LabelField("既存グループキー", DialogueAuthoringIdentity.GroupKey(entry, dataSet.Entries.IndexOf(entry)));
                    DrawCompactNotice("専用形式は形式別Adapterで拡張します。関連CSVは会話本体とは別に保存します。", MessageType.Info);
                    DrawTimedEvents(entry);
                    DrawTalkTopic(entry);
                    DrawReadingDictionary(entry);
                    DrawExistingDiaryRoute(entry);
                    DrawIndependentDiary(entry);
                }
            }
            catch (Exception exception) { relatedDataError = exception.Message; }
            if (!string.IsNullOrEmpty(relatedDataError)) DrawCompactNotice(relatedDataError, MessageType.Error);
        }
        private void DrawTimedEvents(DialogueEntry entry)
        {
            timedEvents ??= new DialogueRelatedCsvRepository("TalkSource/TalkCSV/SystemTimedEvent.csv", TimedEventHeaders);
            EditorGUILayout.LabelField("時間経過", EditorStyles.boldLabel);
            DrawCompactNotice("action_idは通常Action経路、timed_event_keyは死亡等の専用経路です。0分以下は通常経路で120分へ補完されます。既存keyは共有参照保護のため改名・削除できません。", MessageType.Info);
            foreach (var link in TimedEventLinks(entry.ActionId, entry.TimedEventKey))
                DrawTimedEventLink("時間経過イベント / 参照元: " + link.sources, link.key, link.dedicated);
            bool hasLinkedEvent = new[] { entry.ActionId, entry.TimedEventKey }.Any(key => timedEvents.Find("key", key).Count > 0);
            if (!hasLinkedEvent)
            {
            newTimedEventKey = EditorGUILayout.TextField(FocusInput("新規イベントID"), newTimedEventKey);
            if (GUILayout.Button("時間経過イベントを作成"))
            {
                if (string.IsNullOrWhiteSpace(newTimedEventKey) || timedEvents.Find("key", newTimedEventKey).Count > 0) throw new InvalidDataException("新規keyは空欄・重複不可です。");
                var row = timedEvents.Add(); row["key"] = newTimedEventKey.Trim(); row["time_minutes"] = "120"; row["display_seconds"] = "2.8"; row["diary_kind"] = "normal"; row["diary_id"] = GenerateDiaryId(newTimedEventKey.Trim(), timedEvents);
                entry.ActionId = newTimedEventKey.Trim(); hasPendingChanges = true; newTimedEventKey = "";
            }
            }
            if (GUILayout.Button("時間経過・日記を保存・暗号化"))
            {
                MigrateTimedEventDiaries(timedEvents); ValidateTimedEvents(timedEvents); timedEvents.Save(() => CSVCryptoCompiler.CompileForBuildOrThrow());
                SystemTimedEventCatalog.ResetCache(); relatedDataError = null; ShowNotification(new GUIContent("TimedEvent保存・Runtime更新完了。会話本体は別途保存してください。"));
            }
            if (GUILayout.Button("TimedEventを再読込（関連編集を破棄）")) { timedEvents = null; relatedDataError = null; authorDiagnostics = null; SystemTimedEventCatalog.ResetCache(); }
        }

        private void DrawTimedEventLink(string label, string key, bool dedicated)
        {
            EditorGUILayout.LabelField(label, string.IsNullOrWhiteSpace(key) ? "未設定" : key);
            var matches = timedEvents.Find("key", key);
            if (matches.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(key)) DrawCompactNotice("TimedEventとして未登録（他のActionの場合は正常）", MessageType.Info);
                return;
            }
            if (matches.Count != 1) { DrawCompactNotice("重複key。保存前に正本を修正してください。", MessageType.Error); return; }
            var row = matches[0];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawRelatedField(row, "time_minutes"); DrawRelatedField(row, "display_seconds");
                DrawVariantEditor(row, false);
                if (GUILayout.Button("このkeyのCSV・コード参照元を検索")) referenceSummary = FindTimedEventReferences(key);
                DrawCompactNotice("共有Eventの編集は全参照元へ反映されます。\n" + referenceSummary, MessageType.Warning);
                if (dedicated) DrawCompactNotice("timed_event_keyの実行・時間進行はAction側フローに依存します。通常Actionと同一視しないでください。", MessageType.Info);
            }
        }

        internal static List<(string key, string sources, bool dedicated)> TimedEventLinks(string action, string timed)
        {
            var links = new List<(string key, string sources, bool dedicated)>();
            if (!string.IsNullOrWhiteSpace(action)) links.Add((action.Trim(), "ActionID", false));
            if (!string.IsNullOrWhiteSpace(timed))
            {
                int index = links.FindIndex(l => string.Equals(l.key, timed.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index >= 0) links[index] = (links[index].key, "ActionID / timed_event_key", true);
                else links.Add((timed.Trim(), "timed_event_key", true));
            }
            return links;
        }

        private void DrawRelatedField(Dictionary<string, string> row, string field)
        {
            if (!showOtherLanguages && (field == "en" || field == "zh")) return;
            string label = RelatedFieldLabel(field);
            string value = TalkTopicMasterCsvUtility.ReadValue(row, field);
            if (field is "ja" or "en" or "zh" or "body" or "Idea" or "Note" or "notes")
            {
                EditorGUILayout.LabelField(label); row[field] = EditorGUILayout.TextArea(FocusInput(value), GUILayout.MinHeight(36));
            }
            else if (field is "Enabled" or "NeedsReview") row[field] = EditorGUILayout.Toggle(FocusInput(label), Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(value)) ? "TRUE" : "FALSE";
            else if (field == "time_minutes" || field == "display_seconds")
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    row[field] = EditorGUILayout.TextField(FocusInput(field == "time_minutes" ? "ゲーム内経過時間" : "結果表示時間"), value);
                    GUILayout.Label(field == "time_minutes" ? "分" : "秒", GUILayout.Width(20));
                }
            }
            else row[field] = EditorGUILayout.TextField(FocusInput(label), value);
        }

        private readonly Dictionary<string, int> variantTabs = new Dictionary<string, int>();
        internal static string RelatedFieldLabel(string field) => field switch
        {
            "ja" => "日本語", "en" => "英語", "zh" => "中国語", "TalkTitle" => "話題タイトル",
            "Genre" => "ジャンル", "Idea" => "表示する話題", "Import" => "入力する言葉", "Keywords" => "キーワード",
            "Enabled" => "有効", "Surface" => "表記", "Reading" => "読み", "NeedsReview" => "要確認",
            "Category" => "分類", "SourceFiles" => "出典", "Note" => "制作メモ", "condition" => "条件式", _ => field
        };
        private void DrawSpecialDiary(Dictionary<string, string> row, string field)
        {
            row[field] = EditorGUILayout.Toggle(FocusInput("特別な日記"), string.Equals(TalkTopicMasterCsvUtility.ReadValue(row, field), "special", StringComparison.OrdinalIgnoreCase)) ? "special" : "normal";
        }
        internal static string GenerateDiaryId(string key, DialogueRelatedCsvRepository repository)
        {
            string stem = Regex.Replace(key.Trim(), @"[^\p{L}\p{N}_-]", "_") + "_diary";
            string id = stem; int suffix = 2;
            while (repository.Find("diary_id", id).Count > 0) id = stem + "_" + suffix++;
            return id;
        }
        internal static void MigrateTimedEventDiaries(DialogueRelatedCsvRepository repository)
        {
            foreach (var row in repository.Document.Rows)
            {
                if (!row.ContainsKey("diary_ja"))
                    foreach (string language in new[] { "ja", "en", "zh" }) row["diary_" + language] = TalkTopicMasterCsvUtility.ReadValue(row, language);
                // A legacy empty ID resolved to key: preserve that identity for existing saves.
                if (string.IsNullOrWhiteSpace(TalkTopicMasterCsvUtility.ReadValue(row, "diary_id"))) row["diary_id"] = TalkTopicMasterCsvUtility.ReadValue(row, "key");
            }
        }
        internal static List<DiaryTextVariants.Text> ReadVariants(Dictionary<string, string> row, bool diary, bool independent = false)
        {
            string prefix = diary && !independent ? "diary_" : "";
            if (diary && !independent && !row.ContainsKey("diary_ja")) prefix = "";
            return DiaryTextVariants.Read(TalkTopicMasterCsvUtility.ReadValue(row, prefix + "ja"), TalkTopicMasterCsvUtility.ReadValue(row, prefix + "en"),
                TalkTopicMasterCsvUtility.ReadValue(row, prefix + "zh"), TalkTopicMasterCsvUtility.ReadValue(row, diary ? "diary_variants" : "display_variants"));
        }
        internal static void WriteVariants(Dictionary<string, string> row, bool diary, List<DiaryTextVariants.Text> variants, bool independent = false)
        {
            if (variants.Count == 0) variants.Add(new DiaryTextVariants.Text());
            string prefix = diary && !independent ? "diary_" : "";
            row[prefix + "ja"] = variants[0].ja ?? ""; row[prefix + "en"] = variants[0].en ?? ""; row[prefix + "zh"] = variants[0].zh ?? "";
            row[diary ? "diary_variants" : "display_variants"] = DiaryTextVariants.WriteAdditional(variants);
        }
        private void DrawVariantEditor(Dictionary<string, string> row, bool diary, bool independent = false)
        {
            EditorGUILayout.LabelField(diary ? "日記本文" : "時間経過表示", EditorStyles.boldLabel);
            if (diary)
            {
                int mode = DiaryTextVariants.IsSequence(TalkTopicMasterCsvUtility.ReadValue(row, "diary_selection_mode")) ? 1 : 0;
                row["diary_selection_mode"] = EditorGUILayout.Popup(FocusInput("選択方式"), mode, new[] { "ランダム", "順番（Sequence）" }) == 1 ? "Sequence" : "Random";
                if (!independent && !row.ContainsKey("diary_ja")) DrawCompactNotice("専用本文未設定: 既存表示文をコピーして移行します。", MessageType.Warning);
            }
            var variants = ReadVariants(row, diary, independent);
            string key = TalkTopicMasterCsvUtility.ReadValue(row, independent ? "DiaryId" : "key") + (diary ? ":diary" : ":display");
            variantTabs.TryGetValue(key, out int selected); selected = Mathf.Clamp(selected, 0, variants.Count - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                selected = GUILayout.Toolbar(selected, Enumerable.Range(1, variants.Count).Select(number => number.ToString()).ToArray());
                if (GUILayout.Button("＋", GUILayout.Width(28))) { variants.Add(new DiaryTextVariants.Text()); selected = variants.Count - 1; }
            }
            var item = variants[selected];
            EditorGUILayout.LabelField(diary ? "日記文（日本語）" : "表示文（日本語）"); item.ja = EditorGUILayout.TextArea(FocusInput(item.ja ?? ""), GUILayout.MinHeight(36));
            if (showOtherLanguages)
            {
                EditorGUILayout.LabelField("英語"); item.en = EditorGUILayout.TextArea(FocusInput(item.en ?? ""), GUILayout.MinHeight(36));
                EditorGUILayout.LabelField("中国語"); item.zh = EditorGUILayout.TextArea(FocusInput(item.zh ?? ""), GUILayout.MinHeight(36));
            }
            using (new EditorGUI.DisabledScope(variants.Count <= 1))
                if (GUILayout.Button("このパターンを削除")) { variants.RemoveAt(selected); selected = Mathf.Min(selected, variants.Count - 1); }
            variantTabs[key] = selected; WriteVariants(row, diary, variants, independent);
        }

        internal static void ValidateTimedEvents(DialogueRelatedCsvRepository repository)
        {
            repository.RequireUnique("key");
            foreach (var row in repository.Document.Rows)
            {
                string minutes = TalkTopicMasterCsvUtility.ReadValue(row, "time_minutes"), seconds = TalkTopicMasterCsvUtility.ReadValue(row, "display_seconds");
                if ((!string.IsNullOrWhiteSpace(minutes) && !int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) ||
                    (!string.IsNullOrWhiteSpace(seconds) && (!float.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsInfinity(value) || float.IsNaN(value))))
                    throw new InvalidDataException("time_minutesは整数、display_secondsは有限の数値で指定してください。");
                ReadVariants(row, false); var diaryVariants = ReadVariants(row, true);
                if (row.ContainsKey("diary_ja") && diaryVariants.Any(item => string.IsNullOrWhiteSpace(item.ja)))
                    throw new InvalidDataException("日記本文（日本語）を各パターンへ入力してください: " + TalkTopicMasterCsvUtility.ReadValue(row, "key"));
                string mode = TalkTopicMasterCsvUtility.ReadValue(row, "diary_selection_mode");
                if (!string.IsNullOrWhiteSpace(mode) && !mode.Equals("Random", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Sequence", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("日記の選択方式はRandom/Sequenceです。");
                string kind = TalkTopicMasterCsvUtility.ReadValue(row, "diary_kind");
                if (!string.IsNullOrWhiteSpace(kind) && kind != "normal" && kind != "special") throw new InvalidDataException("diary_kindはnormal/specialです。");
            }
        }

        internal static string FindTimedEventReferences(string key)
        {
            var references = new List<string>();
            foreach (string path in Directory.GetFiles("TalkSource/TalkCSV", "*.csv"))
            {
                var doc = TalkTopicMasterCsvUtility.ReadDocument(path);
                for (int i = 0; i < doc.Rows.Count; i++)
                    if (new[] { "action_id", "timed_event_key" }.Any(column => TalkTopicMasterCsvUtility.ReadValue(doc.Rows[i], column).Trim() == key.Trim())) references.Add(path + ":行" + (i + 2));
            }
            foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                if (File.ReadAllText(path).Contains("\"" + key.Trim() + "\"")) references.Add(path);
            return references.Count == 0 ? "直接参照なし（動的keyは検索対象外）" : string.Join("\n", references) + "\n動的keyは検索対象外";
        }

        internal static string TopicSourceKey(DialogueEntry entry, int row)
        {
            var values = new Dictionary<string, string> { ["id"] = entry.InternalId, ["regex_id"] = entry.RegexId, ["intent"] = entry.Intent, ["pattern"] = entry.Pattern };
            return TalkTopicMasterGenerator.BuildSourceKey(values, row + 1);
        }

        private void RemoveLinkedRecordsForDeletedDialogue(DialogueEntry entry, int rowIndex)
        {
            topics ??= new DialogueRelatedCsvRepository(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.MasterHeaders);
            diaryCandidates ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.CandidatePath, DialogueDiaryAuthoring.CandidateHeaders);
            diaryLinks ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.LinkPath, DialogueDiaryAuthoring.LinkHeaders);

            string source = DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath).Replace('\\', '/');
            if (source.StartsWith("TalkSource/TalkCSV/", StringComparison.OrdinalIgnoreCase))
            {
                source = source.Substring("TalkSource/TalkCSV/".Length);
                string sourceKey = TopicSourceKey(entry, rowIndex);
                RemoveTalkTopicLink(source, sourceKey, topics);
            }

            RemoveIndependentDiaryLinks(entry.EditorDialogueId, diaryCandidates, diaryLinks);
            CaptureRelatedDrafts();
        }

        internal static void RemoveTalkTopicLink(string source, string sourceKey, DialogueRelatedCsvRepository repository)
        {
            if (repository == null) return;
            repository.Document.Rows.RemoveAll(topic =>
                string.Equals(TalkTopicMasterCsvUtility.ReadValue(topic, "Source").Replace('\\', '/'), source?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(TalkTopicMasterCsvUtility.ReadValue(topic, "SourceKey"), sourceKey, StringComparison.OrdinalIgnoreCase));
        }

        internal static void RemoveIndependentDiaryLinks(
            string dialogueId,
            DialogueRelatedCsvRepository candidates,
            DialogueRelatedCsvRepository links)
        {
            if (string.IsNullOrWhiteSpace(dialogueId) || candidates == null || links == null) return;
            var removedDiaryIds = new HashSet<string>(
                links.Find("DialogueId", dialogueId).Select(link => TalkTopicMasterCsvUtility.ReadValue(link, "DiaryId")),
                StringComparer.OrdinalIgnoreCase);
            links.Document.Rows.RemoveAll(link =>
                string.Equals(TalkTopicMasterCsvUtility.ReadValue(link, "DialogueId"), dialogueId, StringComparison.OrdinalIgnoreCase));
            candidates.Document.Rows.RemoveAll(candidate =>
            {
                string diaryId = TalkTopicMasterCsvUtility.ReadValue(candidate, "DiaryId");
                return removedDiaryIds.Contains(diaryId) && links.Find("DiaryId", diaryId).Count == 0;
            });
        }

        private void DrawTalkTopic(DialogueEntry entry)
        {
            topics ??= new DialogueRelatedCsvRepository(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.MasterHeaders);
            string source = DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath);
            if (!source.StartsWith("TalkSource/TalkCSV/", StringComparison.OrdinalIgnoreCase))
            {
                DrawCompactNotice("TalkTopic登録はTalkSource/TalkCSVの制作元で行ってください。", MessageType.Info); return;
            }
            source = source.Substring("TalkSource/TalkCSV/".Length);
            string key = TopicSourceKey(entry, dataSet.Entries.IndexOf(entry));
            EditorGUILayout.LabelField("話題候補", EditorStyles.boldLabel);
            var matches = topics.Document.Rows.Where(row => TalkTopicMasterCsvUtility.ReadValue(row, "Source").Replace('\\', '/') == source && TalkTopicMasterCsvUtility.ReadValue(row, "SourceKey") == key).ToList();
            if (matches.Count > 1) { DrawCompactNotice("同じSource + SourceKeyが重複しています。", MessageType.Error); return; }
            if (matches.Count == 0)
            {
                if (GUILayout.Button("この会話をTalkTopicへ登録（空欄・無効）"))
                {
                    var row = topics.Add(); row["Source"] = source; row["SourceKey"] = key; row["TalkID"] = "TALK_" + Guid.NewGuid().ToString("N"); row["Enabled"] = "FALSE";
                }
                return;
            }
            var topic = matches[0];
            EditorGUILayout.LabelField("TalkID", TalkTopicMasterCsvUtility.ReadValue(topic, "TalkID"));
            foreach (string field in TopicEditableFields) DrawRelatedField(topic, field);
            DrawCompactNotice("登録OFFはEnabledをOFFにします。Importを入力欄へ挿入し通常入力判定へ渡すため、条件・優先度・他Regexによって別会話が選ばれることがあります。SourceKeyによる直接実行ではありません。", MessageType.Info);
            if (GUILayout.Button("Importの入力照合を検証"))
            {
                string normalized = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeInput(TalkTopicMasterCsvUtility.ReadValue(topic, "Import"));
                string message = ValidateTopicImport(entry, normalized);
                referenceSummary = message; relatedDataError = null;
                EditorUtility.DisplayDialog("TalkTopic Import検証", message + "\n条件・優先度・プレースホルダーは実プレイで確認してください。", "OK");
            }
            if (GUILayout.Button("TalkTopic正本を保存・Runtimeへ同期"))
            {
                topics.RequireUnique("TalkID"); topics.RequireUnique("Source", "SourceKey");
                topics.Save(() => TalkTopicMasterCsvUtility.Synchronize(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.RuntimePath));
                AssetDatabase.Refresh(); relatedDataError = null; ShowNotification(new GUIContent("TalkTopic正本・Runtime同期完了"));
            }
            if (GUILayout.Button("TalkTopicを再読込（関連編集を破棄）")) { topics = null; relatedDataError = null; }
        }

        internal static string ValidateTopicImport(DialogueEntry entry, string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return "Importが空です。";
            if (string.IsNullOrWhiteSpace(entry.RegexPattern)) return "対象会話に入力Regexがありません。";
            bool match = Regex.IsMatch(normalized, entry.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return match ? "対象Regexへ到達可能です。Runtimeの最終選択は状態・他候補に依存します。" : "対象Regexへ一致しません。Importまたは入力照合用辞書を確認してください。";
        }

        private void DrawReadingDictionary(DialogueEntry entry)
        {
            string diagnostics = GetReadingDiagnostics(entry);
            EditorGUILayout.LabelField("漢字読み", EditorStyles.boldLabel);
            DrawCompactNotice("input / regex_jp / Importのみを検査します。発話・日記本文の漢字登録は必須ではありません。\n" + diagnostics, ReadingNoticeType(diagnostics));
            DrawReadingDictionaryFields();
        }

        private string GetReadingDiagnostics(DialogueEntry entry)
        {
            readings ??= new DialogueRelatedCsvRepository("TalkSource/TalkCSV/KanjiReadingDictionary.csv", ReadingHeaders);
            string input = entry.GetValue("input") ?? "";
            if (CurrentProfile?.TalkTopic == true) topics ??= new DialogueRelatedCsvRepository(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.MasterHeaders);
            string import = "";
            if (topics != null)
            {
                string source = DialogueAuthoringIdentity.NormalizeSource(dataSet.SourcePath).Replace("TalkSource/TalkCSV/", "");
                string sourceKey = TopicSourceKey(entry, dataSet.Entries.IndexOf(entry));
                import = string.Join("\n", topics.Document.Rows.Where(row => TalkTopicMasterCsvUtility.ReadValue(row, "Source") == source && TalkTopicMasterCsvUtility.ReadValue(row, "SourceKey") == sourceKey).Select(row => TalkTopicMasterCsvUtility.ReadValue(row, "Import")));
            }
            string inputText = input + "\n" + entry.RegexPattern + "\n" + import;
            if (readingDiagnosticsCacheKey != inputText)
            {
                readingDiagnostics = DiagnoseReadings(readings.Document, inputText);
                readingDiagnosticsCacheKey = inputText;
            }
            return readingDiagnostics;
        }

        private void DrawReadingDictionaryFields()
        {
            readingSurface = EditorGUILayout.TextField(FocusInput("表記を確認・編集"), readingSurface);
            var matches = readings.Find("Surface", readingSurface);
            foreach (var row in matches)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    foreach (string field in ReadingHeaders.Skip(1)) DrawRelatedField(row, field);
            }
            if (matches.Count == 0 && GUILayout.Button("共有辞書へ空の読みを追加（無効・要確認）"))
            {
                string surface = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeSurfaceForDictionary(readingSurface);
                if (string.IsNullOrWhiteSpace(surface)) throw new InvalidDataException("Surfaceを入力してください。");
                if (readings.Find("Surface", surface).Count > 0) throw new InvalidDataException("正規化後のSurfaceは既に登録されています。");
                readingSurface = surface; var row = readings.Add(); row["Surface"] = surface; row["Enabled"] = "FALSE"; row["NeedsReview"] = "TRUE";
            }
            if (GUILayout.Button("共有辞書を保存・暗号化・キャッシュ更新"))
            {
                foreach (var row in readings.Document.Rows)
                    if (Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(TalkTopicMasterCsvUtility.ReadValue(row, "Enabled")) &&
                        (string.IsNullOrWhiteSpace(TalkTopicMasterCsvUtility.ReadValue(row, "Surface")) || string.IsNullOrWhiteSpace(TalkTopicMasterCsvUtility.ReadValue(row, "Reading"))))
                        throw new InvalidDataException("有効な辞書項目にはSurfaceとReadingが必要です。");
                readings.Save(() => CSVCryptoCompiler.CompileForBuildOrThrow());
                Backgammon.Conversation.KanjiReadingDictionary.ResetDefaultForTests(); readingDiagnosticsCacheKey = null; relatedDataError = null;
                ShowNotification(new GUIContent("共有辞書保存・Runtime更新完了"));
            }
            if (GUILayout.Button("辞書警告を更新")) readingDiagnosticsCacheKey = null;
            if (GUILayout.Button("共有辞書を再読込（関連編集を破棄）")) { readings = null; readingDiagnosticsCacheKey = null; relatedDataError = null; }
        }

        internal static string DiagnoseReadings(TalkTopicMasterCsvUtility.CsvDocument document, string input)
        {
            string text = Regex.Replace(input ?? "", @"\{\{?[^{}]+\}?\}", "");
            var dictionary = Backgammon.Conversation.KanjiReadingDictionary.FromCsv(TalkTopicMasterCsvUtility.SerializeDocument(document));
            string converted = new Backgammon.Conversation.ProjectDictionaryReadingConverter(dictionary.Entries).Convert(text);
            var messages = Regex.Matches(converted, @"[\p{IsCJKUnifiedIdeographs}々]+").Cast<Match>().Select(match => "未登録表記: " + match.Value).Distinct().ToList();
            foreach (var group in document.Rows.GroupBy(row => Backgammon.Conversation.JapaneseTextNormalizer.NormalizeSurfaceForDictionary(TalkTopicMasterCsvUtility.ReadValue(row, "Surface"))))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) { messages.Add("無効辞書項目: Surface空欄"); continue; }
                if (!text.Contains(group.Key)) continue;
                bool conflict = group.Select(row => Backgammon.Conversation.JapaneseTextNormalizer.NormalizeReadingForDictionary(TalkTopicMasterCsvUtility.ReadValue(row, "Reading"))).Distinct().Count() > 1;
                foreach (var row in group)
                {
                    string reading = TalkTopicMasterCsvUtility.ReadValue(row, "Reading");
                    string status = conflict ? "読み競合" : string.IsNullOrWhiteSpace(reading) ? "無効辞書項目" : !Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(TalkTopicMasterCsvUtility.ReadValue(row, "Enabled")) ? "無効" : "有効";
                    if (Backgammon.Conversation.ReadingDictionaryEntry.ParseBool(TalkTopicMasterCsvUtility.ReadValue(row, "NeedsReview"))) status += " / NeedsReview";
                    messages.Add(group.Key + " → " + reading + " : " + status);
                }
            }
            return messages.Count == 0 ? "未登録漢字なし" : string.Join("\n", messages.Distinct());
        }

        private void DrawExistingDiaryRoute(DialogueEntry entry)
        {
            timedEvents ??= new DialogueRelatedCsvRepository("TalkSource/TalkCSV/SystemTimedEvent.csv", TimedEventHeaders);
            EditorGUILayout.LabelField("TimedEventに付随する日記", EditorStyles.boldLabel);
            foreach (string key in new[] { entry.ActionId, entry.TimedEventKey }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
            {
                var matches = timedEvents.Find("key", key);
                if (matches.Count == 1)
                {
                    DrawVariantEditor(matches[0], true);
                    DrawSpecialDiary(matches[0], "diary_kind");
                }
            }
            DrawCompactNotice("通常TimedEventは暗転後・時間進行前の発生日/時間帯で記録します。表示文と日記本文は独立です。専用本文未設定の旧データは表示文をコピーして移行します。通常は選択時間帯の最新草稿、特別はPending発生順で保持します。timed_event_keyだけの設定は記録を保証しません。死亡フローはkindによらず特別Pending、DiaryIdはtimed_event_key優先です。", MessageType.Info);
        }

        internal static string DescribeDiaryRoute(Dictionary<string, string> row)
        {
            string key = TalkTopicMasterCsvUtility.ReadValue(row, "key"), id = TalkTopicMasterCsvUtility.ReadValue(row, "diary_id");
            string kind = TalkTopicMasterCsvUtility.ReadValue(row, "diary_kind");
            return "Dialogue → TimedEvent " + key + " → " + (kind.Equals("special", StringComparison.OrdinalIgnoreCase) ? "特別Pending" : "通常草稿") +
                "\nDiaryId: " + (string.IsNullOrWhiteSpace(id) ? key + "（key fallback）" : id) +
                "\n日記素材（表示用本文と共通、実行時に変数展開）:\n" + TalkTopicMasterCsvUtility.ReadValue(row, "ja");
        }

        private void DrawIndependentDiary(DialogueEntry entry)
        {
            diaryCandidates ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.CandidatePath, DialogueDiaryAuthoring.CandidateHeaders);
            diaryLinks ??= new DialogueRelatedCsvRepository(DialogueDiaryAuthoring.LinkPath, DialogueDiaryAuthoring.LinkHeaders);
            EditorGUILayout.LabelField("独立日記候補（時間経過不要）", EditorStyles.boldLabel);
            DrawCompactNotice("会話の開始時に記録します。条件・候補なしなら従来経路を維持。1件以上記録した再生では同会話のTimedEvent日記を抑止します。通常は同時間帯の最新候補、特別は全Pendingを保持。会話本体を先に保存してください。", MessageType.Info);
            foreach (var link in diaryLinks.Find("DialogueId", entry.EditorDialogueId))
            {
                string id = TalkTopicMasterCsvUtility.ReadValue(link, "DiaryId");
                var candidates = diaryCandidates.Find("DiaryId", id);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("DiaryId", id);
                    DrawRelatedField(link, "Enabled"); DrawRelatedField(link, "condition");
                    if (candidates.Count != 1) { DrawCompactNotice("候補の欠落・重複を修正してください。", MessageType.Error); continue; }
                    var candidate = candidates[0];
                    DrawVariantEditor(candidate, true, true); DrawSpecialDiary(candidate, "kind"); DrawRelatedField(candidate, "Enabled");
                    int references = diaryLinks.Find("DiaryId", id).Count;
                    if (references > 1) DrawCompactNotice("共有候補: " + references + "会話から参照。本文編集は全参照へ反映します。", MessageType.Warning);
                }
            }
            newDiaryId = EditorGUILayout.TextField(FocusInput("追加・関連付けするDiaryId"), newDiaryId);
            if (GUILayout.Button("日記候補にする（既存候補を共有／未登録なら空欄作成）"))
            {
                string id = newDiaryId.Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(entry.EditorDialogueId)) throw new InvalidDataException("DiaryIdと保存済みDialogueIDが必要です。");
                if (diaryLinks.Find("DialogueId", entry.EditorDialogueId).Any(row => TalkTopicMasterCsvUtility.ReadValue(row, "DiaryId") == id)) throw new InvalidDataException("同じ日記候補は既に関連付いています。");
                if (diaryCandidates.Find("DiaryId", id).Count == 0) { var candidate = diaryCandidates.Add(); candidate["DiaryId"] = id; candidate["kind"] = "normal"; candidate["Enabled"] = "FALSE"; }
                var link = diaryLinks.Add(); link["DialogueId"] = entry.EditorDialogueId; link["DiaryId"] = id; link["Enabled"] = "TRUE"; newDiaryId = "";
            }
            using (new EditorGUI.DisabledScope(hasPendingChanges))
                if (GUILayout.Button("独立日記候補・関連を保存・Runtimeへ反映"))
                {
                    diaryCandidates.RequireUnique("DiaryId"); diaryLinks.RequireUnique("DialogueId", "DiaryId");
                    diaryCandidates.Save(() => diaryLinks.Save(() => CSVCryptoCompiler.CompileForBuildOrThrow()));
                    DialogueDiaryCatalog.ResetCache(); relatedDataError = null; ShowNotification(new GUIContent("独立日記設定・Runtime更新完了"));
                }
            if (GUILayout.Button("独立日記設定を再読込（関連編集を破棄）")) { diaryCandidates = null; diaryLinks = null; relatedDataError = null; }
        }
    }
}
