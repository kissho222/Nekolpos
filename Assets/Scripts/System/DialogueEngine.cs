using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.Dialogue.UnknownWord;
using Nekolpos.TimeSystem;
using Nekolpos.StatusSystem;

namespace Nekolpos.System
{
    /// <summary>
    /// 【解析不能な会話エンジン】
    /// プレイヤーの入力テキストを受け取り、4つの関門（フィルタ➔正規表現➔反応決定➔フラグ記録）を
    /// 突破して最終的なNekomataの行動を決定する心臓部。
    /// ConversationDataManager にアタッチされた会話CSVを読み取り、旧会話ロジックへ流し込みます。
    /// </summary>
    public class DialogueEngine : MonoBehaviour
    {
        // =======================================================
        // イベント
        // =======================================================
        /// <summary>
        /// Nekomataの反応が決定した際に発火するイベント。
        /// UIコントローラーなどがこれを購読してテキストやアニメーションを再生する。
        /// 複数ページにまたがる会話（Orderで連なっているもの）は、Listとしてまとめて渡される。
        /// </summary>
        public event Action<List<DialogueReactionData>> OnDialogueDetermined;

        public static DialogueEngine Instance { get; private set; }
        public DialogueLogEntry LastInputLogContext => lastInputLogContext != null ? lastInputLogContext.Clone() : null;
        public bool IsTeachingModeActive => currentTeachingCategory != TeachingCategory.None;

        // =======================================================
        [Header("Legacy Load Settings")]
        [Tooltip("旧 .bytes ロード設定は未使用です。DialogueEngine は ConversationDataManager からのみ読み込みます。")]
        public string regexDictFileName = "Regular Expression";
        public string reactionDictFileName = "RegexDict";
        public string vulgarDictFileName = "VulgarLanguage";
        [Tooltip("旧 DialoguePreview 直接ロード設定は未使用です。DialogueEngine は ConversationDataManager からのみ読み込みます。")]
        public bool loadDialoguePreviewCsv = true;
        public string dialoguePreviewCsvResourcePath = "TalkCSV/DialoguePreview";
        [SerializeField] private bool useFoodHungryShortcut;

        // メモリ上に展開される解析データ
        private List<RegexPatternData> regexPatterns = new List<RegexPatternData>();
        
        // メモリ上に展開される下品な言葉のリスト
        private List<string> vulgarWords = new List<string>();
        private const int DefaultSequenceResetDays = 3;
        private const int DefaultRandomRepeatLimit = 3;
        private const int CatCharacterPriority = 30;

        // ハッシュ化された意味キー(Label)をインデックスにして、Nekomataの台詞群を高速検索する辞書
        private Dictionary<int, List<DialogueReactionData>> reactionDatabase = new Dictionary<int, List<DialogueReactionData>>();

        // 進行状況や繰り返し回数を記憶するステート管理辞書（第3関門で使用）
        private Dictionary<int, ConversationState> conversationStates = new Dictionary<int, ConversationState>();
        private int lastPatternHash = 0; // 前回入力されたパターンのハッシュ（連続判定用）
        private Nekolpos.TimeSystem.TimeManager timeManager;
        private ConversationGameStateManager conversationGameStateManager;
        private ConversationDataManager conversationDataManager;
        private RegexInputParser unknownWordIntentParser;
        private UnknownWordExtractor unknownWordExtractor;
        private UnknownWordResponseSystem unknownWordResponseSystem;
        private DialogueLogEntry lastInputLogContext;
        private const string CatNameCallDialogueKey = "{{CAT_NAME}}";
        private const string PlayerNameCallDialogueKey = "{{PLAYER_NAME}}";
        private const string PlayerNameCallPatternId = "PlayerNameCall";
        private TeachingCategory currentTeachingCategory = TeachingCategory.None;
        private TeachingCategory pendingTeachingCategory = TeachingCategory.None;
        private TeachingCategory pendingSourceTeachingCategory = TeachingCategory.None;
        private string pendingTeachingInput = string.Empty;
        private string pendingTeachingNormalizedInput = string.Empty;
        private PendingTeachingChoice pendingTeachingChoice = PendingTeachingChoice.None;
        private TeachingMatch pendingDiscoveryMatch;
        private int lastRevisitReactionIndex = -1;
        private const string TeachingIntentId = "TEACHING_MODE";
        private const string TeachingActionPrefix = "TeachingMode:";
        private const string TeachingConfirmPrefix = TeachingActionPrefix + "Confirm:";
        private const string TeachingSwitchPrefix = TeachingActionPrefix + "Switch:";
        private const string TeachingPromptPrefix = TeachingActionPrefix + "Prompt:";
        private const string TeachingContinueAction = TeachingActionPrefix + "Continue";
        private const string TeachingOutOfCategoryConfirmAction = TeachingActionPrefix + "OutOfCategoryConfirm";
        private const string TeachingOutOfCategoryTeachAction = TeachingActionPrefix + "OutOfCategoryTeach";
        private const string TeachingStartPrefix = TeachingActionPrefix + "Start:";
        private const string TeachingResetConfirmAction = TeachingActionPrefix + "ResetConfirm";
        private const string TeachingResetExecuteAction = TeachingActionPrefix + "ResetExecute";
        private const string TeachingKnownWordChoiceAction = TeachingActionPrefix + "KnownWordChoice";
        private const string TeachingKnownWordChoiceActionPrefix = TeachingKnownWordChoiceAction + ":";
        private static readonly string[] NoTalkPatternIds =
        {
            "No_talk1",
            "No_talk2",
            "No_talk3",
            "No_talk4",
            "No_talk5",
            "No_talk6"
        };
        private static readonly Dictionary<string, string> CatCharacterDestinationLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pokemon"] = "ねこぽけもんたち",
                ["AnimalCrossing"] = "どうぶつのもりのねこ"
            };

        // 特殊なラベルのハッシュ値を事前に計算しておく（下品フィルタ等）
        private int hashKoubi;
        private int hashHiwai;
        private int hashGehin;

        private enum PendingTeachingChoice
        {
            None,
            NormalConfirm,
            SwitchConfirm,
            ContinueConfirm,
            OutOfCategoryConfirm,
            ResetConfirm,
            KnownWordChoice
        }

        private struct TeachingMatch
        {
            public TeachingCategory Category;
            public RegexPatternData Pattern;
            public string MatchedText;
            public int MatchedIndex;
            public string OriginalInput;

            public bool HasValue => Category != TeachingCategory.None && Pattern != null;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                InitializeEngine();
            }
            else
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitializeEngine()
        {
            hashKoubi = GetDeterministicHash(NormalizeLabelForHash("こうび"));
            hashHiwai = GetDeterministicHash(NormalizeLabelForHash("ひわい"));
            hashGehin = GetDeterministicHash(NormalizeLabelForHash("げひん"));

            Debug.Log("[DialogueEngine] ConversationDataManager から会話CSVのロードを開始します...");

            LoadConversationDataFromManager();
            InitializeUnknownWordSystem();

            // 優先度が高い順に正規表現を評価できるよう、リストを降順ソートしておく
            regexPatterns.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            Debug.Log($"[DialogueEngine] ✅ エンジン起動完了！ 正規表現: {regexPatterns.Count}件, 反応ラベル: {reactionDatabase.Count}種");
        }

        public void RebuildRuntimeData(ConversationDataManager manager = null)
        {
            if (manager != null)
            {
                conversationDataManager = manager;
            }

            InitializeEngine();
        }

        public void ResetRuntimeConversationState()
        {
            Debug.Log($"[DialogueEngine][Reset] ResetRuntimeConversationState states={conversationStates.Count} lastPatternHash={lastPatternHash} hasLastInputLog={lastInputLogContext != null}");
            conversationStates.Clear();
            lastPatternHash = 0;
            lastInputLogContext = null;
        }

        // =======================================================
        // 文字列・ハッシュ処理用ヘルパーメソッド
        // =======================================================

        /// <summary>
        /// カタカナを平仮名に変換する（表記揺れ吸収用）
        /// 全角カタカナのUnicode範囲(0x30A1〜0x30F6)を平仮名(0x3041〜0x3096)へシフトする。
        /// </summary>
        private string ToHiragana(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            
            char[] chars = src.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= 0x30A1 && c <= 0x30F6)
                {
                    chars[i] = (char)(c - 0x0060);
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// ラベル文字列をハッシュ化前に正規化する。
        /// 改行/タブ/BOM/ゼロ幅空白/全角空白などの差分で Hash がズレるのを防ぐ。
        /// </summary>
        private string NormalizeLabelForHash(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel)) return string.Empty;

            string normalized = ToHiragana(rawLabel);
            normalized = normalized.Replace("\uFEFF", "").Replace("\u200B", "");
            normalized = normalized.Trim();      // 半角空白・タブ・改行など
            normalized = normalized.Trim('　');  // 全角空白
            return normalized;
        }

        /// <summary>
        /// .NET Core(Unity)標準の string.GetHashCode() は起動ごとに結果が変わる可能性があるため、
        /// 辞書の紐づけに使うIDは、常に同じ結果を返すMD5ベースの決定論的ハッシュ関数を使用する。
        /// </summary>
        private int GetDeterministicHash(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(str));
                // MD5の最初の4バイトを使いintに変換
                return BitConverter.ToInt32(hashBytes, 0);
            }
        }

        /// <summary>
        /// CSVの1行をカンマで分割する。
        /// ダブルクォーテーションで囲まれたカンマは無視して1つのフィールドとして扱う。
        /// </summary>
        // =======================================================
        // 4Stage パイプライン（会話エンジンのメイン処理）
        // =======================================================
        
        /// <summary>
        /// プレイヤーからの入力テキストを処理し、Nekomataの反応を返すメインメソッド
        /// </summary>
        public void ProcessInput(string input)
        {
            ProcessInputInternal(input, ignoreTeachingForCurrentEvaluation: false);
        }

        private void ProcessInputInternal(string input, bool ignoreTeachingForCurrentEvaluation)
        {
            PlayerInputContext inputContext = new PlayerInputContext(input);

            if (string.IsNullOrWhiteSpace(inputContext.OriginalInput))
            {
                if (TryEmitNoTalkReaction(inputContext))
                {
                    return;
                }
            }

            if (RenameSystem.TryCreateRenameRequestReaction(inputContext, out DialogueReactionData renameReaction))
            {
                lastInputLogContext = CreateUnmatchedLogContext(inputContext, new MatchEvaluationResult(), null);
                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
                OnDialogueDetermined?.Invoke(new List<DialogueReactionData> { renameReaction });
                return;
            }

            if (TryCreateCatNameCallReaction(inputContext.OriginalInput, out DialogueReactionData catNameCallReaction))
            {
                lastInputLogContext = CreateUnmatchedLogContext(inputContext, new MatchEvaluationResult(), null);
                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
                OnDialogueDetermined?.Invoke(new List<DialogueReactionData> { catNameCallReaction });
                return;
            }

            if (TryCreatePlayerNameCallReactionGroup(inputContext.OriginalInput, out List<DialogueReactionData> playerNameCallGroup))
            {
                lastInputLogContext = CreateUnmatchedLogContext(inputContext, new MatchEvaluationResult(), null);
                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
                OnDialogueDetermined?.Invoke(playerNameCallGroup);
                return;
            }

            if (useFoodHungryShortcut &&
                TryCreateFoodHungryReaction(inputContext, out DialogueReactionData foodHungryReaction, out FoodHungryMatchResult foodHungryMatch))
            {
                lastInputLogContext = CreateFoodHungryLogContext(inputContext, foodHungryReaction, foodHungryMatch);
                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
                OnDialogueDetermined?.Invoke(new List<DialogueReactionData> { foodHungryReaction });
                return;
            }

            if (!ignoreTeachingForCurrentEvaluation && TryHandleTeachingInput(inputContext))
            {
                return;
            }

            // 第1関門 ＆ 第2関門：正規表現マッチング ＋ フィルタ検索
            // （リストを1度走査するだけで、下品フィルタと通常マッチを両方探す）
            MatchEvaluationResult matchResult = FindBestMatch(inputContext, pattern => !IsTeachingPattern(pattern));
            RegexPatternData matchedPattern = matchResult.BestPattern;

            if (matchedPattern == null)
            {
                Debug.Log("💬 [DialogueEngine] どの正規表現にもマッチしませんでした。「わかんない」等の処理に移行します。");

                UnknownWordAnalysisResult unknownWordAnalysis = TryExtractUnknownWord(inputContext);
                lastInputLogContext = CreateUnmatchedLogContext(inputContext, matchResult, unknownWordAnalysis);

                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());

                UnknownWordResult unknownWordResult = unknownWordAnalysis != null ? unknownWordAnalysis.UnknownWordResult : null;
                if (unknownWordResult != null && unknownWordResult.HasUnknownWord)
                {
                    List<DialogueReactionData> unknownWordGroup = new List<DialogueReactionData>();
                    DialogueReactionData unknownWordReaction = unknownWordResponseSystem != null
                        ? unknownWordResponseSystem.CreateReaction(unknownWordResult)
                        : null;

                    if (unknownWordReaction == null)
                    {
                        unknownWordReaction = new DialogueReactionData
                        {
                            TextJP = unknownWordResult.UnknownWord + "ってなに？",
                            NextAction = string.Empty,
                            IntentID = "UNKNOWN_WORD",
                            ReactionType = "UnknownWord",
                            ResponseType = "Reaction"
                        };
                    }

                    unknownWordGroup.Add(unknownWordReaction);
                    OnDialogueDetermined?.Invoke(unknownWordGroup);
                    return;
                }
                
                OnDialogueDetermined?.Invoke(CreateUnknownWordFallbackGroup(inputContext));
                return;
            }

            // 第3関門：マッチした「意味キーのハッシュ（LabelHash）」を元に反応を決定
            List<DialogueReactionData> reactionGroup = DetermineReaction(matchedPattern.LabelHash);
            if (reactionGroup != null && reactionGroup.Count > 0)
            {
                reactionGroup = CloneReactionGroupWithPlayerInput(
                    reactionGroup,
                    inputContext,
                    matchResult);

                // 代表データ（1ページ目）をログやフラグ登録に使う
                DialogueReactionData firstReact = reactionGroup[0];
                lastInputLogContext = CreateMatchedLogContext(inputContext, matchResult, firstReact);

                // 3. 反応の引き渡し・実行
                Debug.Log($"💬 [DialogueEngine] 反応決定! パターンID: {firstReact.PatternID}, ページ数: {reactionGroup.Count}, 台詞(1P目): 「{firstReact.TextJP}」, アクション: {firstReact.ReactionType}");
                
                // ゲーム側のフラグ管理へ登録（日記など）
                RegisterEventFlag(firstReact);

                OnDialogueDetermined?.Invoke(reactionGroup);
            }
            else
            {
                Debug.Log($"<color=orange>⚠️ [DialogueEngine] マッチしたラベル(Hash:{matchedPattern.LabelHash}) に対する反応データが辞書に存在しません。</color>");
                ForceShowInputFieldForRecovery();
            }
        }

        private bool TryCreateCatNameCallReaction(string input, out DialogueReactionData reaction)
        {
            reaction = null;

            if (!IsCatNameCallInput(input))
            {
                return false;
            }

            Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
            reaction = BasicSystemDialogueCatalog.CreateReactionFromKeyOrPattern(
                CatNameCallDialogueKey,
                placeholders,
                "なに？呼んだ？どうしたの？");

            if (reaction == null)
            {
                return false;
            }

            reaction.IntentID = "BASIC_SYSTEM";
            reaction.ReactionType = "CatNameCall";
            reaction.SourceRegexLabel = BasicSystemDialogueCatalog.BranchResolverId;
            return true;
        }

        private bool TryCreatePlayerNameCallReactionGroup(string input, out List<DialogueReactionData> reactions)
        {
            reactions = null;

            if (!IsPlayerNameCallInput(input))
            {
                return false;
            }

            Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
            if (!InternalDialogueCatalog.TryCreateReactionGroupByPatternId(
                    PlayerNameCallPatternId,
                    placeholders,
                    out List<DialogueReactionData> group) ||
                group == null ||
                group.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] == null)
                {
                    continue;
                }

                group[i].IntentID = "INTERNAL_DIALOGUE";
                group[i].ReactionType = PlayerNameCallPatternId;
                group[i].SourceRegexLabel = InternalDialogueCatalog.BranchResolverId;
            }

            reactions = group;
            return true;
        }

        private bool TryCreateFoodHungryReaction(
            PlayerInputContext inputContext,
            out DialogueReactionData reaction,
            out FoodHungryMatchResult matchResult)
        {
            reaction = null;
            matchResult = default;

            if (!FoodHungryInputMatcher.TryMatch(inputContext, out matchResult))
            {
                return false;
            }

            Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
            if (!string.IsNullOrWhiteSpace(matchResult.FoodName))
            {
                placeholders["FOOD_NAME"] = matchResult.FoodName;
            }

            if (!TryCreateDialoguePreviewReaction(
                    FoodHungryInputMatcher.DialogueLabel,
                    FoodHungryInputMatcher.ConfirmDialogueKey,
                    placeholders,
                    out reaction) ||
                reaction == null)
            {
                return false;
            }

            reaction.IntentID = FoodHungryInputMatcher.TriggerType;
            reaction.ReactionType = FoodHungryInputMatcher.TriggerType;
            reaction.SourceRegexLabel = matchResult.MatchedPattern;
            return true;
        }

        private bool TryCreateDialoguePreviewReaction(
            string label,
            string patternId,
            IReadOnlyDictionary<string, string> placeholders,
            out DialogueReactionData reaction)
        {
            reaction = null;
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(patternId))
            {
                return false;
            }

            int labelHash = GetDeterministicHash(NormalizeLabelForHash(label));
            if (!TryGetPatternGroup(labelHash, patternId.Trim(), out List<DialogueReactionData> reactionGroup) ||
                reactionGroup == null ||
                reactionGroup.Count == 0)
            {
                return false;
            }

            reaction = CloneReaction(reactionGroup[0], placeholders);
            return reaction != null;
        }

        private static DialogueReactionData CloneReaction(
            DialogueReactionData source,
            IReadOnlyDictionary<string, string> placeholders)
        {
            if (source == null)
            {
                return null;
            }

            Dictionary<string, string> runtimePlaceholders = ClonePlaceholders(placeholders);
            return new DialogueReactionData
            {
                LabelHash = source.LabelHash,
                PatternID = source.PatternID,
                SpeechControl = source.SpeechControl,
                Order = source.Order,
                RepeatCount = source.RepeatCount,
                RandomRepeatLimit = source.RandomRepeatLimit,
                TargetPatternID = source.TargetPatternID,
                CallOnly = source.CallOnly,
                ChoiceYesPatternID = source.ChoiceYesPatternID,
                ChoiceNoPatternID = source.ChoiceNoPatternID,
                SequenceProgressHold = source.SequenceProgressHold,
                IntentID = source.IntentID,
                ReactionType = source.ReactionType,
                Topic = source.Topic,
                SourceRegexLabel = source.SourceRegexLabel,
                ChoiceQuestionJa = FormatRuntimeText(source.ChoiceQuestionJa, runtimePlaceholders),
                ChoiceYesLabel = FormatRuntimeText(source.ChoiceYesLabel, runtimePlaceholders),
                ChoiceNoLabel = FormatRuntimeText(source.ChoiceNoLabel, runtimePlaceholders),
                ActionId = source.ActionId,
                TimedEventKey = source.TimedEventKey,
                Speaker = source.Speaker,
                NextAction = source.NextAction,
                TextJP = FormatRuntimeText(source.TextJP, runtimePlaceholders),
                ResponseType = source.ResponseType,
                Condition = source.Condition,
                WaitTime = source.WaitTime,
                HideUI = source.HideUI,
                Animation = source.Animation,
                EmotionChangeType = source.EmotionChangeType,
                EmotionChangeValue = source.EmotionChangeValue,
                BranchResolver = source.BranchResolver,
                RuntimePlaceholders = runtimePlaceholders,
                TeachingCategory = source.TeachingCategory,
                TeachingEntryId = source.TeachingEntryId,
                TeachingSeriesGroupId = source.TeachingSeriesGroupId,
                TeachingDisplayName = source.TeachingDisplayName,
                QuestionLinks = CloneQuestionLinks(source.QuestionLinks, runtimePlaceholders)
            };
        }

        private static List<DialogueLogQuestionLink> CloneQuestionLinks(
            IReadOnlyList<DialogueLogQuestionLink> source,
            IReadOnlyDictionary<string, string> placeholders)
        {
            var result = new List<DialogueLogQuestionLink>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DialogueLogQuestionLink link = source[i];
                if (link == null)
                {
                    continue;
                }

                result.Add(new DialogueLogQuestionLink(
                    FormatRuntimeText(link.keyword, placeholders),
                    FormatRuntimeText(link.insertText, placeholders)));
            }

            return result;
        }

        private static List<DialogueReactionData> CloneReactionGroupWithPlayerInput(
            IReadOnlyList<DialogueReactionData> source,
            PlayerInputContext inputContext,
            MatchEvaluationResult matchResult)
        {
            string matchedInput = inputContext != null && matchResult != null
                ? inputContext.GetOriginalSubstringForNormalizedRange(
                    matchResult.BestMatchIndex,
                    matchResult.BestMatchedText != null ? matchResult.BestMatchedText.Length : 0)
                : string.Empty;

            if (string.IsNullOrEmpty(matchedInput) && matchResult != null)
            {
                matchedInput = matchResult.BestMatchedText ?? string.Empty;
            }

            Dictionary<string, string> placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLAYER_INPUT"] = matchedInput
            };

            List<DialogueReactionData> cloned = new List<DialogueReactionData>(source != null ? source.Count : 0);
            if (source == null)
            {
                return cloned;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DialogueReactionData reaction = source[i];
                if (reaction == null)
                {
                    continue;
                }

                cloned.Add(CloneReaction(reaction, placeholders));
            }

            return cloned;
        }

        private static Dictionary<string, string> ClonePlaceholders(IReadOnlyDictionary<string, string> placeholders)
        {
            Dictionary<string, string> clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (placeholders == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    clone[pair.Key] = pair.Value != null ? pair.Value.Trim() : string.Empty;
                }
            }

            return clone;
        }

        private static string FormatRuntimeText(string template, IReadOnlyDictionary<string, string> placeholders)
        {
            if (string.IsNullOrEmpty(template) || placeholders == null || placeholders.Count == 0)
            {
                return template ?? string.Empty;
            }

            string formatted = template;
            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                formatted = formatted.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
                formatted = formatted.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
            }

            return formatted;
        }

        private bool IsCatNameCallInput(string input)
        {
            string normalizedInput = NormalizeNameCallToken(input);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return false;
            }

            if (string.Equals(normalizedInput, NormalizeNameCallToken(CatNameCallDialogueKey), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string catName = ResolveRuntimeCatName();
            return CatNameCallMatcher.IsNameCall(
                normalizedInput,
                NormalizeNameCallToken(catName));
        }

        private bool IsPlayerNameCallInput(string input)
        {
            string normalizedInput = NormalizeNameCallToken(input);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return false;
            }

            if (string.Equals(normalizedInput, NormalizeNameCallToken(PlayerNameCallDialogueKey), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string playerName = ResolveRuntimePlayerName();
            return CatNameCallMatcher.IsNameCall(
                normalizedInput,
                NormalizeNameCallToken(playerName));
        }

        private string ResolveRuntimeCatName()
        {
            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            if (dialogueManager != null &&
                dialogueManager.catData != null &&
                !string.IsNullOrWhiteSpace(dialogueManager.catData.catName))
            {
                return dialogueManager.catData.catName;
            }

            ConversationGameState state = GetConversationGameState();
            if (state != null && state.TryGetString("CAT_NAME", out string stateCatName))
            {
                return stateCatName;
            }

            return string.Empty;
        }

        private string ResolveRuntimePlayerName()
        {
            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            if (dialogueManager != null &&
                dialogueManager.catData != null &&
                !string.IsNullOrWhiteSpace(dialogueManager.catData.playerName))
            {
                return dialogueManager.catData.playerName;
            }

            ConversationGameState state = GetConversationGameState();
            if (state != null && state.TryGetString("PLAYER_NAME", out string statePlayerName))
            {
                return statePlayerName;
            }

            return string.Empty;
        }

        private Dictionary<string, string> BuildRuntimePlaceholders()
        {
            Dictionary<string, string> placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            CatDataSO catData = dialogueManager != null ? dialogueManager.catData : null;
            if (catData != null)
            {
                placeholders["CAT_NAME"] = SanitizeRuntimePlaceholderValue(catData.catName);
                placeholders["CAT_PRONOUN"] = SanitizeRuntimePlaceholderValue(catData.catPronoun);
                placeholders["PLAYER_CALLING"] = SanitizeRuntimePlaceholderValue(catData.playerCalling);
                placeholders["PLAYER_NAME"] = SanitizeRuntimePlaceholderValue(catData.playerName);
                placeholders["CAT_GENDER"] = SanitizeRuntimePlaceholderValue(catData.catGender);
            }

            ConversationGameState state = GetConversationGameState();
            AddPlaceholderFromState(state, placeholders, "CAT_NAME");
            AddPlaceholderFromState(state, placeholders, "CAT_PRONOUN");
            AddPlaceholderFromState(state, placeholders, "PLAYER_CALLING");
            AddPlaceholderFromState(state, placeholders, "PLAYER_NAME");
            AddPlaceholderFromState(state, placeholders, "CAT_GENDER");
            AddPlaceholderFromState(state, placeholders, WeatherSystem.WeatherKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.CurrentWeatherKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.TomorrowWeatherKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.CurrentWeatherForecastKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.ForecastWeatherKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.ForecastWeatherMisspelledKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.ForecastWeatherQuestionKey);
            AddPlaceholderFromState(state, placeholders, WeatherSystem.NextActualWeatherKey);
            AddWeatherPlaceholdersFromTimeManager(placeholders);
            return placeholders;
        }

        private static string SanitizeRuntimePlaceholderValue(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private static void AddWeatherPlaceholdersFromTimeManager(Dictionary<string, string> placeholders)
        {
            if (placeholders == null)
            {
                return;
            }

            TimeManager manager = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            if (manager == null)
            {
                return;
            }

            placeholders[WeatherSystem.WeatherKey] = WeatherSystem.ToDisplayText(manager.Weather);
            placeholders[WeatherSystem.CurrentWeatherKey] = manager.Weather.ToString();
            placeholders[WeatherSystem.TomorrowWeatherKey] = WeatherSystem.ToDisplayText(manager.TomorrowWeather);
            placeholders[WeatherSystem.CurrentWeatherForecastKey] = manager.TomorrowWeather.ToString();
            placeholders[WeatherSystem.ForecastWeatherKey] = WeatherSystem.ToDisplayText(manager.TomorrowWeather);
            placeholders[WeatherSystem.ForecastWeatherMisspelledKey] = WeatherSystem.ToDisplayText(manager.TomorrowWeather);
            placeholders[WeatherSystem.ForecastWeatherQuestionKey] = WeatherSystem.ToDisplayText(manager.TomorrowWeather);
            placeholders[WeatherSystem.NextActualWeatherKey] = manager.NextActualWeather.ToString();
        }

        private static void AddPlaceholderFromState(
            ConversationGameState state,
            Dictionary<string, string> placeholders,
            string key)
        {
            if (state == null || placeholders == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (state.TryGetString(key, out string value))
            {
                placeholders[key] = SanitizeRuntimePlaceholderValue(value);
            }
        }

        private string NormalizeNameCallToken(string value)
        {
            return JapaneseTextNormalizer.NormalizeNameCallToken(value);
        }

        /// <summary>
        /// 反応辞書未ヒット等の例外時に入力不能へ陥らないよう、InputField を復帰させる。
        /// </summary>
        private void ForceShowInputFieldForRecovery()
        {
            ChatUIController chatUI = FindFirstObjectByType<ChatUIController>();
            if (chatUI != null)
            {
                chatUI.EnterPlayerInputMode();
                Debug.Log("[DialogueEngine] 例外復帰: ChatInputField を再表示しました。");
            }
            else
            {
                Debug.LogWarning("[DialogueEngine] 例外復帰失敗: ChatUIController が見つからないため InputField を再表示できません。");
            }
        }

        private bool TryHandleTeachingInput(PlayerInputContext inputContext)
        {
            if (inputContext == null)
            {
                return false;
            }

            if (currentTeachingCategory != TeachingCategory.None)
            {
                if (TryFindTeachingMatch(inputContext, category => category == currentTeachingCategory, out TeachingMatch currentMatch))
                {
                    if (IsKnownTeachingMatch(currentMatch, currentTeachingCategory != TeachingCategory.FavoritePoint))
                    {
                        ShowKnownTeachingWordChoice(currentMatch);
                        return true;
                    }

                    PlayTeachingReaction(currentMatch, useRevisitReaction: false);
                    return true;
                }

                if (TryFindTeachingMatch(inputContext, IsSwitchableTeachingCategory, out TeachingMatch switchMatch))
                {
                    pendingTeachingInput = inputContext.OriginalInput;
                    pendingTeachingCategory = switchMatch.Category;
                    pendingSourceTeachingCategory = currentTeachingCategory;
                    pendingTeachingChoice = PendingTeachingChoice.SwitchConfirm;
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(GetConfirmDialogueKey(switchMatch.Category), null, BuildTeachingActionId(TeachingSwitchPrefix, switchMatch.Category)));
                    return true;
                }

                pendingTeachingInput = inputContext.OriginalInput;
                pendingTeachingNormalizedInput = inputContext.NormalizedInput;
                pendingTeachingCategory = currentTeachingCategory;
                pendingSourceTeachingCategory = currentTeachingCategory;
                pendingTeachingChoice = PendingTeachingChoice.OutOfCategoryConfirm;

                Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
                placeholders["word"] = inputContext.OriginalInput;
                OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(
                    GetOutOfCategoryConfirmDialogueKey(currentTeachingCategory),
                    placeholders,
                    TeachingOutOfCategoryConfirmAction));
                return true;
            }

            MatchEvaluationResult normalMatch = FindBestMatch(inputContext, pattern => !IsTeachingPattern(pattern));
            if (TryFindLongerTeachingMatch(inputContext, IsSwitchableTeachingCategory, normalMatch, out TeachingMatch candidateMatch))
            {
                pendingTeachingInput = inputContext.OriginalInput;
                pendingTeachingNormalizedInput = inputContext.NormalizedInput;
                pendingTeachingCategory = candidateMatch.Category;
                pendingSourceTeachingCategory = TeachingCategory.None;
                pendingTeachingChoice = PendingTeachingChoice.NormalConfirm;
                OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(GetConfirmDialogueKey(candidateMatch.Category), null, BuildTeachingActionId(TeachingConfirmPrefix, candidateMatch.Category)));
                return true;
            }

            if (normalMatch.BestPattern != null)
            {
                return false;
            }

            return false;
        }

        private bool TryFindLongerTeachingMatch(
            PlayerInputContext inputContext,
            Func<TeachingCategory, bool> categoryFilter,
            MatchEvaluationResult normalMatch,
            out TeachingMatch match)
        {
            match = default;
            if (!TryFindTeachingMatch(inputContext, categoryFilter, out TeachingMatch teachingMatch, includePromptDisabled: false))
            {
                return false;
            }

            int normalMatchLength = normalMatch != null &&
                                    normalMatch.BestPattern != null &&
                                    !string.IsNullOrEmpty(normalMatch.BestMatchedText)
                ? normalMatch.BestMatchedText.Length
                : -1;
            int teachingMatchLength = !string.IsNullOrEmpty(teachingMatch.MatchedText)
                ? teachingMatch.MatchedText.Length
                : -1;

            if (normalMatchLength >= 0 && teachingMatchLength <= normalMatchLength)
            {
                return false;
            }

            match = teachingMatch;
            return true;
        }

        public bool TryHandleTeachingAction(string actionId, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(actionId) || !actionId.StartsWith(TeachingActionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string trimmedActionId = actionId.Trim();
            if (trimmedActionId.StartsWith(TeachingStartPrefix, StringComparison.Ordinal))
            {
                if (TryParseTeachingCategory(trimmedActionId.Substring(TeachingStartPrefix.Length), out TeachingCategory startCategory))
                {
                    EnterTeachingCategory(startCategory);
                    ShowTeachingInputWait();
                }

                return true;
            }

            if (trimmedActionId.StartsWith(TeachingKnownWordChoiceActionPrefix, StringComparison.Ordinal))
            {
                if (pendingTeachingChoice != PendingTeachingChoice.KnownWordChoice)
                {
                    return true;
                }

                string rawIndex = trimmedActionId.Substring(TeachingKnownWordChoiceActionPrefix.Length);
                int.TryParse(rawIndex, out int selectedIndex);
                TeachingMatch match = pendingDiscoveryMatch;

                switch (selectedIndex)
                {
                    case 0:
                        pendingDiscoveryMatch = default;
                        ClearPendingTeachingChoice();
                        ShowTeachingInputWait();
                        return true;
                    case 1:
                        ClearPendingTeachingChoice();
                        if (match.HasValue)
                        {
                            PlayTeachingReaction(match, useRevisitReaction: false);
                        }
                        else
                        {
                            ShowTeachingInputWait();
                        }
                        return true;
                    case 2:
                        TeachingCategory endingCategory = currentTeachingCategory;
                        ExitTeachingMode();
                        ClearPendingTeachingChoice();
                        DialogueManager.Instance?.RunTeachingTimePassage("そっか！じゃあもうちょっと話に付き合って！");
                        return true;
                    default:
                        return true;
                }
            }

            if (trimmedActionId.StartsWith(TeachingConfirmPrefix, StringComparison.Ordinal))
            {
                if (accepted &&
                    TryParseTeachingCategory(trimmedActionId.Substring(TeachingConfirmPrefix.Length), out TeachingCategory confirmCategory))
                {
                    string confirmedInput = pendingTeachingInput;
                    EnterTeachingCategory(confirmCategory);
                    ClearPendingTeachingChoice();

                    // 通常会話からの確認では、確認前の入力をティーチング辞書へ再評価して
                    // 最初の説明を表示する。入力がない起点からの確認は待機状態へ戻す。
                    if (!string.IsNullOrWhiteSpace(confirmedInput))
                    {
                        ProcessInput(confirmedInput);
                    }
                    else
                    {
                        ShowTeachingInputWait();
                    }
                }
                else
                {
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup("TEACH_CONFIRM_NO_RETURN"));
                }

                return true;
            }

            if (trimmedActionId.StartsWith(TeachingPromptPrefix, StringComparison.Ordinal))
            {
                if (accepted &&
                    TryParseTeachingCategory(trimmedActionId.Substring(TeachingPromptPrefix.Length), out TeachingCategory promptCategory))
                {
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(
                        GetConfirmDialogueKey(promptCategory),
                        null,
                        BuildTeachingActionId(TeachingStartPrefix, promptCategory)));
                }
                else
                {
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup("TEACH_CONFIRM_NO_RETURN"));
                }

                return true;
            }

            if (string.Equals(trimmedActionId, TeachingResetConfirmAction, StringComparison.Ordinal))
            {
                pendingTeachingChoice = PendingTeachingChoice.ResetConfirm;
                OnDialogueDetermined?.Invoke(CreateBasicReactionGroup("TEACH_RESET_CONFIRM", null, TeachingResetExecuteAction));
                return true;
            }

            if (string.Equals(trimmedActionId, TeachingResetExecuteAction, StringComparison.Ordinal))
            {
                if (accepted)
                {
                    TeachingDiscoveryStore.ResetAll();
                    OnDialogueDetermined?.Invoke(CreateBasicReactionGroup("TEACH_RESET_DONE"));
                }
                else
                {
                    OnDialogueDetermined?.Invoke(CreateBasicReactionGroup("TEACH_RESET_CANCEL"));
                }

                ClearPendingTeachingChoice();
                return true;
            }

            if (string.Equals(trimmedActionId, TeachingOutOfCategoryConfirmAction, StringComparison.Ordinal))
            {
                if (pendingTeachingChoice != PendingTeachingChoice.OutOfCategoryConfirm)
                {
                    return true;
                }

                if (accepted)
                {
                    Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
                    placeholders["word"] = pendingTeachingInput;
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(
                        "TEACH_OUT_OF_CATEGORY_TELL_REQUEST",
                        placeholders,
                        TeachingOutOfCategoryTeachAction));
                    return true;
                }

                string reevaluationInput = pendingTeachingInput;
                ExitTeachingMode();
                ProcessInputInternal(reevaluationInput, ignoreTeachingForCurrentEvaluation: true);
                return true;
            }

            if (string.Equals(trimmedActionId, TeachingOutOfCategoryTeachAction, StringComparison.Ordinal))
            {
                TeachingCategory taughtCategory = pendingTeachingCategory != TeachingCategory.None
                    ? pendingTeachingCategory
                    : currentTeachingCategory;
                string taughtWord = pendingTeachingInput;
                string normalizedWord = pendingTeachingNormalizedInput;

                if (taughtCategory != TeachingCategory.None && !string.IsNullOrWhiteSpace(normalizedWord))
                {
                    string entryId = "out_of_category:" + normalizedWord.Trim();
                    TeachingDiscoveryStore.MarkDiscovered(taughtCategory, entryId, null);
                    TeachingDiscoveryStore.RememberEntryKey(taughtCategory, entryId);
                }

                Dictionary<string, string> placeholders = BuildRuntimePlaceholders();
                placeholders["word"] = taughtWord;
                List<DialogueReactionData> followUp = CreateInternalReactionGroup(
                    taughtCategory == TeachingCategory.FavoritePoint ? "TEACH_CONTINUE_FAVORITE_POINT" : "TEACH_CONTINUE_COMMON",
                    null,
                    TeachingContinueAction);
                ClearPendingTeachingChoice();
                DialogueManager.Instance?.RunTeachingResult(
                    SystemTimedEventCatalog.Get("TEACH_OUT_OF_CATEGORY_TOLD_RESULT", string.Empty),
                    placeholders,
                    followUp);
                return true;
            }

            if (string.Equals(trimmedActionId, TeachingContinueAction, StringComparison.Ordinal))
            {
                if (accepted)
                {
                    ClearPendingTeachingChoice();
                    ShowTeachingInputWait();
                }
                else
                {
                    TeachingCategory endingCategory = currentTeachingCategory;
                    ExitTeachingMode();
                    ClearPendingTeachingChoice();
                    DialogueManager.Instance?.RunTeachingTimePassage(GetTimePassageText(endingCategory));
                }

                return true;
            }

            if (pendingTeachingChoice == PendingTeachingChoice.NormalConfirm ||
                pendingTeachingChoice == PendingTeachingChoice.SwitchConfirm)
            {
                if (accepted)
                {
                    TeachingCategory targetCategory = pendingTeachingCategory;
                    string input = pendingTeachingInput;
                    ClearPendingTeachingChoice();
                    EnterTeachingCategory(targetCategory);
                    ProcessInput(input);
                    return true;
                }

                if (pendingTeachingChoice == PendingTeachingChoice.SwitchConfirm)
                {
                    TeachingCategory sourceCategory = pendingSourceTeachingCategory;
                    ClearPendingTeachingChoice();
                    currentTeachingCategory = sourceCategory;
                    OnDialogueDetermined?.Invoke(CreateInternalReactionGroup(GetSwitchDeclinedDialogueKey(sourceCategory)));
                    return true;
                }

                ClearPendingTeachingChoice();
                OnDialogueDetermined?.Invoke(CreateInternalReactionGroup("TEACH_CONFIRM_NO_RETURN"));
                return true;
            }

            return true;
        }

        public bool TryHandleTeachingReactionCompleted(List<DialogueReactionData> completedReactions, out List<DialogueReactionData> followUpReactions)
        {
            followUpReactions = null;
            if (!pendingDiscoveryMatch.HasValue || completedReactions == null || completedReactions.Count == 0)
            {
                return false;
            }

            TeachingMatch completed = pendingDiscoveryMatch;
            pendingDiscoveryMatch = default;
            if (completed.Category != TeachingCategory.FavoritePoint)
            {
                TeachingDiscoveryStore.MarkDiscovered(
                    completed.Category,
                    completed.Pattern.TeachingEntryId,
                    completed.Pattern.TeachingSeriesGroupId);
            }
            else if (!string.IsNullOrWhiteSpace(completed.Pattern.TeachingEntryId))
            {
                TeachingDiscoveryStore.MarkDiscovered(completed.Category, completed.Pattern.TeachingEntryId, null);
            }

            followUpReactions = CreateInternalReactionGroup(
                completed.Category == TeachingCategory.FavoritePoint ? "TEACH_CONTINUE_FAVORITE_POINT" : "TEACH_CONTINUE_COMMON",
                null,
                TeachingContinueAction);
            return followUpReactions != null && followUpReactions.Count > 0;
        }

        private bool TryFindTeachingMatch(
            PlayerInputContext inputContext,
            Func<TeachingCategory, bool> categoryFilter,
            out TeachingMatch match,
            bool includePromptDisabled = true)
        {
            match = default;

            if (!TryFindBestTeachingMatch(inputContext, categoryFilter, includePromptDisabled, out MatchEvaluationResult result) ||
                result.BestPattern == null)
            {
                return false;
            }

            match = new TeachingMatch
            {
                Category = result.BestPattern.TeachingCategory,
                Pattern = result.BestPattern,
                MatchedText = result.BestMatchedText,
                MatchedIndex = result.BestMatchIndex,
                OriginalInput = inputContext.OriginalInput
            };
            return true;
        }

        private bool TryFindBestTeachingMatch(
            PlayerInputContext inputContext,
            Func<TeachingCategory, bool> categoryFilter,
            bool includePromptDisabled,
            out MatchEvaluationResult result)
        {
            result = new MatchEvaluationResult
            {
                NormalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty
            };

            if (inputContext == null || string.IsNullOrWhiteSpace(inputContext.NormalizedInput))
            {
                return false;
            }

            RegexPatternData bestMatch = null;
            int bestMatchLength = -1;
            string bestMatchedText = string.Empty;

            for (int i = 0; i < regexPatterns.Count; i++)
            {
                RegexPatternData pattern = regexPatterns[i];
                if (!IsTeachingPattern(pattern) ||
                    categoryFilter == null ||
                    !categoryFilter(pattern.TeachingCategory) ||
                    (!includePromptDisabled && pattern.TeachingPromptDisabled) ||
                    !reactionDatabase.ContainsKey(pattern.LabelHash))
                {
                    continue;
                }

                try
                {
                    Match regexMatch = GetLongestRegexMatch(inputContext.NormalizedInput, pattern.SourceRegex);
                    if (!regexMatch.Success)
                    {
                        continue;
                    }

                    if (IsBetterMatchCandidate(pattern, regexMatch.Length, bestMatch, bestMatchLength))
                    {
                        bestMatch = pattern;
                        bestMatchLength = regexMatch.Length;
                        result.BestMatchIndex = regexMatch.Index;
                        bestMatchedText = regexMatch.Value;
                    }
                }
                catch (ArgumentException e)
                {
                    Debug.Log($"<color=orange>⚠️ [DialogueEngine] ティーチング正規表現の記述エラーをスキップしました。\n該当パターン: {pattern.SourceRegex}\nエラー内容: {e.Message}</color>");
                }
            }

            result.BestPattern = bestMatch;
            result.BestMatchedText = bestMatchedText;
            if (bestMatch != null)
            {
                Debug.Log($"✅ [DialogueEngine] 最終マッチ（ティーチング）: {bestMatch.SourceRegex} / match='{bestMatchedText}' / length={bestMatchLength}");
            }

            return bestMatch != null;
        }

        private void PlayTeachingReaction(TeachingMatch match, bool useRevisitReaction)
        {
            if (!match.HasValue)
            {
                return;
            }

            lastInputLogContext = CreateMatchedLogContext(
                new PlayerInputContext(match.OriginalInput),
                new MatchEvaluationResult
                {
                    BestPattern = match.Pattern,
                    BestMatchIndex = match.MatchedIndex,
                    BestMatchedText = match.MatchedText,
                    NormalizedInput = JapaneseTextNormalizer.NormalizeToken(match.OriginalInput)
                },
                null);
            DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());

            bool isKnown = useRevisitReaction &&
                           !match.Pattern.TeachingRevisitDisabled &&
                           TeachingDiscoveryStore.IsDiscovered(match.Category, match.Pattern.TeachingEntryId);
            if (isKnown && TryCreateRevisitReaction(match, out List<DialogueReactionData> revisitGroup))
            {
                pendingDiscoveryMatch = default;
                OnDialogueDetermined?.Invoke(revisitGroup);
                return;
            }

            if (TryDetermineReactionGroup(match.Pattern.LabelHash, out List<DialogueReactionData> reactionGroup) &&
                reactionGroup != null &&
                reactionGroup.Count > 0)
            {
                reactionGroup = CloneReactionGroupWithPlayerInput(
                    reactionGroup,
                    new PlayerInputContext(match.OriginalInput),
                    new MatchEvaluationResult
                    {
                        BestPattern = match.Pattern,
                        BestMatchIndex = match.MatchedIndex,
                        BestMatchedText = match.MatchedText,
                        NormalizedInput = JapaneseTextNormalizer.NormalizeToken(match.OriginalInput)
                    });
                ApplyTeachingMetadata(reactionGroup, match);
                pendingDiscoveryMatch = match;
                OnDialogueDetermined?.Invoke(reactionGroup);
            }
        }

        private bool TryCreateRevisitReaction(TeachingMatch match, out List<DialogueReactionData> reactions)
        {
            reactions = null;
            string[] keys = { "TEACH_REVISIT_1", "TEACH_REVISIT_2", "TEACH_REVISIT_3" };
            int index = UnityEngine.Random.Range(0, keys.Length);
            if (keys.Length > 1 && index == lastRevisitReactionIndex)
            {
                index = (index + 1) % keys.Length;
            }

            lastRevisitReactionIndex = index;
            Dictionary<string, string> placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ITEM_NAME"] = ResolveTeachingDisplayName(match)
            };
            reactions = CreateInternalReactionGroup(keys[index], placeholders);
            return reactions != null && reactions.Count > 0;
        }

        private bool IsKnownTeachingMatch(TeachingMatch match, bool allowRevisitReaction)
        {
            return allowRevisitReaction &&
                   match.HasValue &&
                   !match.Pattern.TeachingRevisitDisabled &&
                   TeachingDiscoveryStore.IsDiscovered(match.Category, match.Pattern.TeachingEntryId);
        }

        private void ShowKnownTeachingWordChoice(TeachingMatch match)
        {
            pendingDiscoveryMatch = match;
            pendingTeachingInput = match.OriginalInput;
            pendingTeachingNormalizedInput = JapaneseTextNormalizer.NormalizeToken(match.OriginalInput);
            pendingTeachingCategory = match.Category;
            pendingSourceTeachingCategory = currentTeachingCategory;
            pendingTeachingChoice = PendingTeachingChoice.KnownWordChoice;

            Dictionary<string, string> placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ITEM_NAME"] = ResolveTeachingDisplayName(match)
            };

            OnDialogueDetermined?.Invoke(CreateKnownTeachingWordChoiceGroup(placeholders));
        }

        private void ApplyTeachingMetadata(List<DialogueReactionData> reactions, TeachingMatch match)
        {
            for (int i = 0; i < reactions.Count; i++)
            {
                if (reactions[i] == null)
                {
                    continue;
                }

                reactions[i].TeachingCategory = match.Category.ToString();
                reactions[i].TeachingEntryId = match.Pattern.TeachingEntryId;
                reactions[i].TeachingSeriesGroupId = match.Pattern.TeachingSeriesGroupId;
                reactions[i].TeachingDisplayName = ResolveTeachingDisplayName(match);
            }
        }

        private List<DialogueReactionData> CreateBasicReactionGroup(
            string key,
            IReadOnlyDictionary<string, string> placeholders = null,
            string actionId = null)
        {
            if (!BasicSystemDialogueCatalog.TryCreateReactionGroup(key, placeholders, out List<DialogueReactionData> group) ||
                group == null ||
                group.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] == null)
                {
                    continue;
                }

                group[i].IntentID = TeachingIntentId;
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    group[i].ActionId = actionId;
                }
            }

            return group;
        }

        private List<DialogueReactionData> CreateInternalReactionGroup(
            string patternId,
            IReadOnlyDictionary<string, string> placeholders = null,
            string actionId = null)
        {
            if (!InternalDialogueCatalog.TryCreateReactionGroupByPatternId(patternId, placeholders, out List<DialogueReactionData> group) ||
                group == null ||
                group.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] == null)
                {
                    continue;
                }

                group[i].IntentID = TeachingIntentId;
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    group[i].ActionId = actionId;
                }
            }

            return group;
        }

        private List<DialogueReactionData> CreateKnownTeachingWordChoiceGroup(IReadOnlyDictionary<string, string> placeholders)
        {
            string itemName = placeholders != null && placeholders.TryGetValue("ITEM_NAME", out string value)
                ? value
                : "その言葉";

            return new List<DialogueReactionData>
            {
                new DialogueReactionData
                {
                    TextJP = $"{itemName}は、もう教えてもらった言葉だよ。どうする？",
                    ChoiceQuestionJa = string.Empty,
                    ChoiceYesLabel = "他の言葉を教える",
                    ChoiceNoLabel = string.Empty,
                    ActionId = TeachingKnownWordChoiceAction,
                    IntentID = TeachingIntentId,
                    ResponseType = "Choice"
                }
            };
        }

        private List<DialogueReactionData> CreateSingleTeachingReaction(string text)
        {
            return new List<DialogueReactionData>
            {
                new DialogueReactionData
                {
                    TextJP = text,
                    IntentID = TeachingIntentId,
                    ResponseType = "Normal"
                }
            };
        }

        private bool TryEmitNoTalkReaction(PlayerInputContext inputContext)
        {
            int startIndex = UnityEngine.Random.Range(0, NoTalkPatternIds.Length);
            for (int offset = 0; offset < NoTalkPatternIds.Length; offset++)
            {
                string patternId = NoTalkPatternIds[(startIndex + offset) % NoTalkPatternIds.Length];
                List<DialogueReactionData> group = CreateInternalReactionGroup(patternId);
                if (group == null || group.Count == 0)
                {
                    continue;
                }

                MatchEvaluationResult matchResult = new MatchEvaluationResult
                {
                    NormalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty
                };
                lastInputLogContext = CreateUnmatchedLogContext(inputContext, matchResult, null);
                DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
                OnDialogueDetermined?.Invoke(group);
                return true;
            }

            Debug.LogWarning("[DialogueEngine] 空白入力用の No_talk InternalDialogue が見つかりませんでした。");
            return false;
        }

        private void EmitFallbackUnknown(PlayerInputContext inputContext, MatchEvaluationResult matchResult)
        {
            lastInputLogContext = CreateUnmatchedLogContext(inputContext, matchResult, null);
            DialogueLogManager.Instance?.ExportOnlyLog(lastInputLogContext.Clone());
            OnDialogueDetermined?.Invoke(CreateUnknownWordFallbackGroup(inputContext));
        }

        private List<DialogueReactionData> CreateUnknownWordFallbackGroup(PlayerInputContext inputContext)
        {
            string unknownWord = GetUnknownWordFallbackDisplay(inputContext);
            if (!string.IsNullOrWhiteSpace(unknownWord))
            {
                UnknownWordResult result = new UnknownWordResult
                {
                    HasUnknownWord = true,
                    UnknownWord = unknownWord,
                    NormalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty
                };

                DialogueReactionData unknownWordReaction = unknownWordResponseSystem != null
                    ? unknownWordResponseSystem.CreateReaction(result)
                    : null;

                if (unknownWordReaction != null)
                {
                    return new List<DialogueReactionData> { unknownWordReaction };
                }

                return new List<DialogueReactionData>
                {
                    new DialogueReactionData
                    {
                        TextJP = unknownWord + "ってなに？もしかして、言葉を教えてくれるの？",
                        NextAction = string.Empty,
                        IntentID = "UNKNOWN_WORD",
                        ReactionType = "UnknownWord",
                        ResponseType = "Choice"
                    }
                };
            }

            DialogueReactionData fallbackReact = new DialogueReactionData
            {
                TextJP = BasicSystemDialogueCatalog.Get(
                    BasicSystemDialogueCatalog.FallbackUnknownInputKey,
                    "うーん、なんのことだろう？"),
                NextAction = string.Empty,
                ResponseType = "Reaction"
            };
            return new List<DialogueReactionData> { fallbackReact };
        }

        private static string GetUnknownWordFallbackDisplay(PlayerInputContext inputContext)
        {
            string value = inputContext != null && !string.IsNullOrWhiteSpace(inputContext.OriginalInput)
                ? inputContext.OriginalInput
                : inputContext != null ? inputContext.NormalizedInput : string.Empty;

            return (value ?? string.Empty).Trim(' ', '　', '。', '、', '！', '？', '!', '?');
        }

        private void EnterTeachingCategory(TeachingCategory category)
        {
            currentTeachingCategory = category;
        }

        private void ShowTeachingInputWait()
        {
            ChatUIController chatUI = FindFirstObjectByType<ChatUIController>();
            if (chatUI != null)
            {
                chatUI.EnterTeachingInputMode();
            }
        }

        private void ClearPendingTeachingChoice()
        {
            pendingTeachingInput = string.Empty;
            pendingTeachingNormalizedInput = string.Empty;
            pendingTeachingCategory = TeachingCategory.None;
            pendingSourceTeachingCategory = TeachingCategory.None;
            pendingTeachingChoice = PendingTeachingChoice.None;
        }

        private void ExitTeachingMode()
        {
            currentTeachingCategory = TeachingCategory.None;
            pendingDiscoveryMatch = default;
            ClearPendingTeachingChoice();
        }

        private static bool IsTeachingPattern(RegexPatternData pattern)
        {
            return pattern != null && pattern.TeachingCategory != TeachingCategory.None;
        }

        private static bool IsSwitchableTeachingCategory(TeachingCategory category)
        {
            return category != TeachingCategory.None;
        }

        private static string BuildTeachingActionId(string prefix, TeachingCategory category)
        {
            return prefix + category;
        }

        private static bool TryParseTeachingCategory(string value, out TeachingCategory category)
        {
            return Enum.TryParse(value ?? string.Empty, true, out category) && category != TeachingCategory.None;
        }

        private string ResolveTeachingDisplayName(TeachingMatch match)
        {
            if (!string.IsNullOrWhiteSpace(match.Pattern.TeachingDisplayName))
            {
                return match.Pattern.TeachingDisplayName;
            }

            string reactionDisplayName = ResolveTeachingDisplayNameFromReactionDatabase(match.Pattern.LabelHash);
            if (!string.IsNullOrWhiteSpace(reactionDisplayName))
            {
                return reactionDisplayName;
            }

            string originalMatchedText = ResolveOriginalMatchedText(match);
            if (!string.IsNullOrWhiteSpace(originalMatchedText))
            {
                return originalMatchedText;
            }

            if (!string.IsNullOrWhiteSpace(match.OriginalInput))
            {
                return match.OriginalInput.Trim();
            }

            return match.MatchedText ?? string.Empty;
        }

        private string ResolveTeachingDisplayNameFromReactionDatabase(int labelHash)
        {
            if (!reactionDatabase.TryGetValue(labelHash, out List<DialogueReactionData> reactions) ||
                reactions == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < reactions.Count; i++)
            {
                DialogueReactionData reaction = reactions[i];
                if (reaction == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(reaction.TeachingDisplayName))
                {
                    return reaction.TeachingDisplayName.Trim();
                }

                string extracted = ExtractTeachingDisplayNameFromText(reaction.TextJP);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }

            return string.Empty;
        }

        private static string ResolveOriginalMatchedText(TeachingMatch match)
        {
            if (string.IsNullOrWhiteSpace(match.OriginalInput) ||
                match.MatchedIndex < 0 ||
                string.IsNullOrEmpty(match.MatchedText))
            {
                return string.Empty;
            }

            PlayerInputContext inputContext = new PlayerInputContext(match.OriginalInput);
            string original = inputContext.GetOriginalSubstringForNormalizedRange(
                match.MatchedIndex,
                match.MatchedText.Length);
            return (original ?? string.Empty).Trim();
        }

        private static string ExtractTeachingDisplayNameFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            string[] separators = { "って", "とは", "は" };
            for (int i = 0; i < separators.Length; i++)
            {
                int index = trimmed.IndexOf(separators[i], StringComparison.Ordinal);
                if (index > 0)
                {
                    return trimmed.Substring(0, index).Trim(' ', '　', '「', '」', '『', '』', '“', '”', '"');
                }
            }

            return string.Empty;
        }

        private static string GetConfirmDialogueKey(TeachingCategory category)
        {
            switch (category)
            {
                case TeachingCategory.CatExpression:
                    return "TEACH_CONFIRM_CAT_EXPRESSION";
                case TeachingCategory.Felidae:
                    return "TEACH_CONFIRM_FELIDAE";
                case TeachingCategory.CatBreed:
                    return "TEACH_CONFIRM_CAT_BREED";
                case TeachingCategory.CatSeries:
                    return "TEACH_CONFIRM_CAT_SERIES";
                case TeachingCategory.FavoriteWeather:
                    return "TEACH_CONFIRM_FAVORITE_WEATHER";
                case TeachingCategory.FavoritePoint:
                    return "TEACH_CONFIRM_FAVORITE_POINT";
                default:
                    return string.Empty;
            }
        }

        private static string GetOutOfCategoryConfirmDialogueKey(TeachingCategory category)
        {
            switch (category)
            {
                case TeachingCategory.CatExpression:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_CAT_EXPRESSION";
                case TeachingCategory.Felidae:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_FELIDAE";
                case TeachingCategory.CatBreed:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_CAT_BREED";
                case TeachingCategory.CatSeries:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_CAT_SERIES";
                case TeachingCategory.FavoriteWeather:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_FAVORITE_WEATHER";
                case TeachingCategory.FavoritePoint:
                    return "TEACH_OUT_OF_CATEGORY_CONFIRM_FAVORITE_POINT";
                default:
                    return string.Empty;
            }
        }

        private static string GetSwitchDeclinedDialogueKey(TeachingCategory category)
        {
            switch (category)
            {
                case TeachingCategory.CatExpression:
                    return "TEACH_SWITCH_NO_CAT_EXPRESSION";
                case TeachingCategory.Felidae:
                    return "TEACH_SWITCH_NO_FELIDAE";
                case TeachingCategory.CatBreed:
                    return "TEACH_SWITCH_NO_CAT_BREED";
                case TeachingCategory.CatSeries:
                    return "TEACH_SWITCH_NO_CAT_SERIES";
                case TeachingCategory.FavoriteWeather:
                    return "TEACH_SWITCH_NO_FAVORITE_WEATHER";
                case TeachingCategory.FavoritePoint:
                    return "TEACH_SWITCH_NO_FAVORITE_POINT";
                default:
                    return string.Empty;
            }
        }

        private static string GetTimePassageText(TeachingCategory category)
        {
            return SystemTimedEventCatalog.Get("TEACH_TIME_PASSAGE", string.Empty);
        }

        /// <summary>
        /// 第4関門: 決定した反応をゲーム進行全体のフラグ管理に登録し、日記システム等に渡す
        /// </summary>
        private void RegisterEventFlag(DialogueReactionData reaction)
        {
            if (GameFlagManager.Instance != null)
            {
                // ハッシュ値をそのままフラグ名としてString化して保存（生テキストでの記録を避ける）
                string flagName = $"Talk_{reaction.LabelHash}";
                GameFlagManager.Instance.SetFlag(flagName, true);
                Debug.Log($"📔 [DialogueEngine] 日記フラグ登録: {flagName}");
            }
        }

        /// <summary>
        /// 第1・2関門: テキストを正規表現リストにぶつけ、最長一致を優先してマッチを返す。
        /// 同じ長さのときだけ辞書の優先度を比較する。
        /// （EXPLICIT が見つかれば即座に「こうび」等のペナルティパターンとして返す）
        /// </summary>
        private MatchEvaluationResult FindBestMatch(PlayerInputContext inputContext, Func<RegexPatternData, bool> patternFilter = null)
        {
            MatchEvaluationResult result = new MatchEvaluationResult();
            RegexPatternData bestMatch = null;
            int bestMatchLength = -1;
            int bestMatchIndex = -1;
            string bestMatchedText = string.Empty;
            
            string originalInput = inputContext != null ? inputContext.OriginalInput : string.Empty;
            string normalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty;
            result.NormalizedInput = normalizedInput;
            Debug.Log($"🔍 [DialogueEngine] 判定開始: 入力'{originalInput}' -> 正規化'{normalizedInput}'");

            bool hasFoundExplicitDebug = false;
            HashSet<int> seenCandidateLabels = new HashSet<int>();

            // VulgarLanguage による第一関門フィルタチェック
            string bestVulgarWord = null;
            foreach (string vulgarWord in vulgarWords)
            {
                if (normalizedInput.Contains(vulgarWord))
                {
                    if (bestVulgarWord == null || vulgarWord.Length > bestVulgarWord.Length)
                    {
                        bestVulgarWord = vulgarWord;
                    }
                }
            }

            bool isVulgar = !string.IsNullOrWhiteSpace(bestVulgarWord);
            if (isVulgar)
            {
                Debug.Log($"🚨 [DialogueEngine] 下品フィルタ(VulgarLanguage)に抵触しました！ 最長検知単語: {bestVulgarWord}");
            }

            foreach (var pattern in regexPatterns)
            {
                if (patternFilter != null && !patternFilter(pattern))
                {
                    continue;
                }

                try
                {
                    // 正規表現のコンパイルと実行（実際のゲームではキャッシュ化すると更に高速）
                    // 辞書側の正規表現パターンも平仮名に統一されたと見なして比較する
                    Match match = GetLongestRegexMatch(normalizedInput, pattern.SourceRegex);
                    if (match.Success)
                    {
                        if (!HasNormalReactionForLabel(pattern.LabelHash))
                        {
                            continue;
                        }

                        AddIntentCandidates(pattern.LabelHash, result.IntentCandidates, seenCandidateLabels);

                        int matchLength = match.Length;
                        if (IsBetterMatchCandidate(pattern, matchLength, bestMatch, bestMatchLength))
                        {
                            bestMatch = pattern;
                            bestMatchLength = matchLength;
                            bestMatchIndex = match.Index;
                            bestMatchedText = match.Value;
                        }
                    }
                }
                catch (ArgumentException e)
                {
                    // 正規表現の括弧閉じ忘れなどの構文エラーでエンジンが止まらないようにする
                    Debug.Log($"<color=orange>⚠️ [DialogueEngine] 正規表現の記述エラーをスキップしました。\n該当パターン: {pattern.SourceRegex}\nエラー内容: {e.Message}</color>");
                }
                
                // デバッグ用: もし辞書内に「ちんちん」が含まれるパターンがあれば、それをログに出しておく
                if (!hasFoundExplicitDebug && pattern.SourceRegex.Contains("ちんちん"))
                {
                    // Debug.Log($"[DialogueEngine-Debug] 辞書内に存在するテスト用パターン: {pattern.SourceRegex} (Sensitivity: {pattern.Sensitivity})");
                    hasFoundExplicitDebug = true; // 1回だけ出す
                }
            }

            // VulgarLanguage も通常 regex と同じく「より長く一致したもの」を優先する。
            // 同じ長さなら、実データに紐づく regex 側を優先して誤爆を減らす。
            if (isVulgar && bestVulgarWord.Length > bestMatchLength)
            {
                RegexPatternData gehinPattern = new RegexPatternData();
                gehinPattern.SourceRegex = $"VulgarFilter:{bestVulgarWord}";
                gehinPattern.LabelHash = hashGehin;
                gehinPattern.Priority = 999;
                gehinPattern.Sensitivity = "EXPLICIT";

                bestMatch = gehinPattern;
                bestMatchLength = bestVulgarWord.Length;
                bestMatchIndex = normalizedInput.IndexOf(bestVulgarWord, StringComparison.Ordinal);
                bestMatchedText = bestVulgarWord;
            }

            if (bestMatch != null)
            {
                Debug.Log($"✅ [DialogueEngine] 最終マッチ（通常）: {bestMatch.SourceRegex} / match='{bestMatchedText}' / length={bestMatchLength}");
            }

            result.BestPattern = bestMatch;
            result.BestMatchIndex = bestMatchIndex;
            result.BestMatchedText = bestMatchedText;
            return result; // 何もなければ BestPattern は null
        }

        private void AddIntentCandidates(int labelHash, List<string> target, HashSet<int> seenCandidateLabels)
        {
            if (!seenCandidateLabels.Add(labelHash) ||
                !reactionDatabase.TryGetValue(labelHash, out List<DialogueReactionData> reactions) ||
                reactions == null)
            {
                return;
            }

            for (int i = 0; i < reactions.Count; i++)
            {
                string intentId = reactions[i] != null ? reactions[i].IntentID : string.Empty;
                if (!string.IsNullOrWhiteSpace(intentId) && !target.Contains(intentId))
                {
                    target.Add(intentId);
                }
            }
        }

        private DialogueLogEntry CreateMatchedLogContext(string input, MatchEvaluationResult matchResult, DialogueReactionData selectedReaction)
        {
            return CreateMatchedLogContext(new PlayerInputContext(input), matchResult, selectedReaction);
        }

        private DialogueLogEntry CreateMatchedLogContext(PlayerInputContext inputContext, MatchEvaluationResult matchResult, DialogueReactionData selectedReaction)
        {
            DialogueLogEntry entry = CreateBaseInputLogContext(inputContext, matchResult);
            if (selectedReaction == null)
            {
                return entry;
            }

            entry.Source = string.Equals(selectedReaction.IntentID, "UNKNOWN_WORD", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(selectedReaction.ReactionType, "UnknownWord", StringComparison.OrdinalIgnoreCase)
                ? DialogueLogManager.SourceUnknownWord
                : DialogueLogManager.SourceRegexReaction;
            entry.Intent = selectedReaction.IntentID;
            entry.SelectedIntent = selectedReaction.IntentID;
            entry.SelectedResponseId = !string.IsNullOrWhiteSpace(selectedReaction.PatternID)
                ? selectedReaction.PatternID
                : selectedReaction.ActionId;
            entry.NodeId = entry.SelectedResponseId;
            entry.ReactionResult = selectedReaction.TextJP;
            return entry;
        }

        private DialogueLogEntry CreateUnmatchedLogContext(PlayerInputContext inputContext, MatchEvaluationResult matchResult, UnknownWordAnalysisResult unknownWordAnalysis)
        {
            DialogueLogEntry entry = CreateBaseInputLogContext(inputContext, matchResult);
            entry.Speaker = DialogueLogManager.SpeakerPlayer;
            entry.Source = DialogueLogManager.SourceUnmatchedInput;
            entry.Text = inputContext != null ? inputContext.OriginalInput : string.Empty;
            entry.ReactionResult = null;

            if (unknownWordAnalysis?.ParseResult?.intents != null)
            {
                for (int i = 0; i < unknownWordAnalysis.ParseResult.intents.Count; i++)
                {
                    IntentMatch intentMatch = unknownWordAnalysis.ParseResult.intents[i];
                    string candidate = intentMatch != null ? intentMatch.intent : string.Empty;
                    if (!string.IsNullOrWhiteSpace(candidate) && !entry.IntentCandidates.Contains(candidate))
                    {
                        entry.IntentCandidates.Add(candidate);
                    }
                }
            }

            if (unknownWordAnalysis?.UnknownWordResult?.HasUnknownWord ?? false)
            {
                string unknownWord = unknownWordAnalysis.UnknownWordResult.UnknownWord;
                if (!string.IsNullOrWhiteSpace(unknownWord))
                {
                    entry.UnknownWords.Add(unknownWord);
                }
            }

            return entry;
        }

        private DialogueLogEntry CreateFoodHungryLogContext(
            PlayerInputContext inputContext,
            DialogueReactionData selectedReaction,
            FoodHungryMatchResult foodMatch)
        {
            DialogueLogEntry entry = CreateBaseInputLogContext(inputContext, new MatchEvaluationResult
            {
                NormalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty
            });

            entry.Source = DialogueLogManager.SourceRegexReaction;
            entry.Intent = FoodHungryInputMatcher.TriggerType;
            entry.SelectedIntent = FoodHungryInputMatcher.TriggerType;
            entry.IntentCandidates.Add(FoodHungryInputMatcher.TriggerType);
            entry.SelectedResponseId = selectedReaction != null && !string.IsNullOrWhiteSpace(selectedReaction.PatternID)
                ? selectedReaction.PatternID
                : FoodHungryInputMatcher.ConfirmDialogueKey;
            entry.NodeId = entry.SelectedResponseId;
            entry.MatchedRegex = foodMatch.MatchedPattern;
            entry.ReactionResult = selectedReaction != null ? selectedReaction.TextJP : string.Empty;
            entry.TriggerType = foodMatch.TriggerType;
            entry.MatchedPattern = foodMatch.MatchedPattern;
            entry.FoodName = foodMatch.FoodName;
            return entry;
        }

        private DialogueLogEntry CreateBaseInputLogContext(PlayerInputContext inputContext, MatchEvaluationResult matchResult)
        {
            string originalInput = inputContext != null ? inputContext.OriginalInput : string.Empty;
            string normalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty;
            return new DialogueLogEntry
            {
                Speaker = DialogueLogManager.SpeakerPlayer,
                Source = DialogueLogManager.SourceFreeInput,
                Text = originalInput,
                RawInput = originalInput,
                NormalizedInput = matchResult != null && !string.IsNullOrWhiteSpace(matchResult.NormalizedInput)
                    ? matchResult.NormalizedInput
                    : normalizedInput,
                MatchedRegex = matchResult != null && matchResult.BestPattern != null
                    ? matchResult.BestPattern.SourceRegex
                    : null,
                IntentCandidates = matchResult != null && matchResult.IntentCandidates != null
                    ? new List<string>(matchResult.IntentCandidates)
                    : new List<string>(),
                UploadStatus = DialogueLogEntry.UploadStatusPending
            };
        }

        private static Match GetLongestRegexMatch(string input, string sourceRegex)
        {
            MatchCollection matches = Regex.Matches(input, sourceRegex);
            Match bestMatch = Match.Empty;
            for (int i = 0; i < matches.Count; i++)
            {
                Match current = matches[i];
                if (!current.Success)
                {
                    continue;
                }

                if (!bestMatch.Success || current.Length > bestMatch.Length)
                {
                    bestMatch = current;
                }
            }

            return bestMatch;
        }

        private static bool IsBetterMatchCandidate(
            RegexPatternData candidate,
            int candidateMatchLength,
            RegexPatternData currentBest,
            int currentBestMatchLength)
        {
            if (candidate == null)
            {
                return false;
            }

            if (currentBest == null)
            {
                return true;
            }

            if (candidateMatchLength != currentBestMatchLength)
            {
                return candidateMatchLength > currentBestMatchLength;
            }

            if (candidate.Priority != currentBest.Priority)
            {
                return candidate.Priority > currentBest.Priority;
            }

            bool candidateExplicit = IsExplicitPattern(candidate);
            bool currentBestExplicit = IsExplicitPattern(currentBest);
            if (candidateExplicit != currentBestExplicit)
            {
                return candidateExplicit;
            }

            return false;
        }

        private static bool IsExplicitPattern(RegexPatternData pattern)
        {
            return pattern != null &&
                string.Equals(pattern.Sensitivity, "EXPLICIT", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasNormalReactionForLabel(int labelHash)
        {
            if (!TryBuildReactionGroups(labelHash, false, out Dictionary<string, List<DialogueReactionData>> groupsByPattern, out List<string> orderedPatternIds))
            {
                return false;
            }

            List<DialogueReactionData> representatives = new List<DialogueReactionData>();
            for (int i = 0; i < orderedPatternIds.Count; i++)
            {
                if (!groupsByPattern.TryGetValue(orderedPatternIds[i], out List<DialogueReactionData> group) ||
                    group == null ||
                    group.Count == 0)
                {
                    continue;
                }

                group.Sort((a, b) => a.Order.CompareTo(b.Order));
                representatives.Add(group[0]);
            }

            return FilterRepresentativesByCondition(representatives).Count > 0;
        }

        /// <summary>
        /// 第3関門: ラベルハッシュから状態を更新し、仕様書に従った優先順位で台本を【グループ単位】で選択する
        /// </summary>
        private List<DialogueReactionData> DetermineReaction(int patternHash)
        {
            if (!TryBuildReactionGroups(patternHash, false, out Dictionary<string, List<DialogueReactionData>> groupsByPattern, out List<string> orderedPatternIDs))
            {
                return null;
            }

            // 2. 各グループの先頭データを「代表値」として抽出し、ロジック判定にかける
            List<DialogueReactionData> groupRepresentatives = new List<DialogueReactionData>();
            foreach (var pid in orderedPatternIDs)
            {
                var group = groupsByPattern[pid];
                group.Sort((a, b) => a.Order.CompareTo(b.Order)); // 同一Patternの中から全てセリフをOrderの若い順に読み上げる
                groupRepresentatives.Add(group[0]);
            }

            groupRepresentatives = FilterRepresentativesByCondition(groupRepresentatives);
            groupRepresentatives = PreferPlayerDefeatConditionRepresentatives(groupRepresentatives);
            if (groupRepresentatives.Count == 0)
            {
                Debug.Log($"<color=orange>⚠️ [DialogueEngine] Hash:{patternHash} のグループは condition を満たさなかったため候補がありませんでした。</color>");
                return null;
            }

            // 3. 状態の取得・作成
            if (!conversationStates.TryGetValue(patternHash, out ConversationState state))
            {
                state = new ConversationState { patternHash = patternHash };
                conversationStates[patternHash] = state;
            }

            int currentDay = GetCurrentGameDay();
            ResetConversationStateIfStale(patternHash, groupRepresentatives, state, currentDay);

            // 4. 連続入力（repeatCount）の更新
            if (lastPatternHash == patternHash)
            {
                state.repeatCount++;
            }
            else
            {
                state.repeatCount = 1;      // 別の話題になったらリセット
            }
            lastPatternHash = patternHash; // 今回の話題を記憶

            // --- 5. 発言グループ代表の決定 ---
            DialogueReactionData selectedRep = null;

            // 優先A: RepeatEvent判定（指定回数に達した、あるいは最大回数を超えている場合）
            DialogueReactionData repeatRep = FindRepeatEvent(groupRepresentatives, state.repeatCount);
            if (repeatRep != null)
            {
                selectedRep = repeatRep;
            }
            else
            {
                // 優先B: Sequence & Random 判定
                List<DialogueReactionData> flowReps = new List<DialogueReactionData>();
                foreach (var r in groupRepresentatives)
                {
                    if (r.SpeechControl == SpeechControlType.Random || r.SpeechControl == SpeechControlType.Sequence)
                    {
                        flowReps.Add(r);
                    }
                }

                selectedRep = GetHybridRandomSequenceReaction(flowReps, state);
            }

            if (selectedRep == null)
            {
                Debug.Log($"<color=orange>⚠️ [DialogueEngine] Hash:{patternHash} のグループ（{orderedPatternIDs.Count}個）から該当する反応が見つかりませんでした。</color>");
                return null;
            }

            // 6. 選ばれた代表値が属する「ソート済みのマルチページ会話リスト本体」を丸ごと返す
            foreach (var pid in orderedPatternIDs)
            {
                if (groupsByPattern[pid].Contains(selectedRep))
                {
                    state.lastSpokenDay = currentDay;
                    return groupsByPattern[pid];
                }
            }

            return null;
        }

        private static List<DialogueReactionData> PreferPlayerDefeatConditionRepresentatives(List<DialogueReactionData> representatives)
        {
            if (representatives == null || representatives.Count <= 1)
            {
                return representatives ?? new List<DialogueReactionData>();
            }

            List<DialogueReactionData> defeatContextRepresentatives = new List<DialogueReactionData>();
            for (int i = 0; i < representatives.Count; i++)
            {
                DialogueReactionData representative = representatives[i];
                if (representative != null && IsPlayerDefeatCondition(representative.Condition))
                {
                    defeatContextRepresentatives.Add(representative);
                }
            }

            return defeatContextRepresentatives.Count > 0 ? defeatContextRepresentatives : representatives;
        }

        private static bool IsPlayerDefeatCondition(string condition)
        {
            return !string.IsNullOrWhiteSpace(condition) &&
                   condition.IndexOf("PlayerDefeatedBy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool TryGetPatternGroup(int labelHash, string patternId, out List<DialogueReactionData> reactionGroup)
        {
            reactionGroup = null;
            if (string.IsNullOrWhiteSpace(patternId))
            {
                return false;
            }

            if (!TryBuildReactionGroups(labelHash, true, out Dictionary<string, List<DialogueReactionData>> groupsByPattern, out List<string> orderedPatternIds))
            {
                return false;
            }

            for (int i = 0; i < orderedPatternIds.Count; i++)
            {
                string currentPatternId = orderedPatternIds[i];
                if (!string.Equals(currentPatternId, patternId.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                reactionGroup = groupsByPattern[currentPatternId];
                return reactionGroup != null && reactionGroup.Count > 0;
            }

            return false;
        }

        public bool TryDetermineReactionGroup(int labelHash, out List<DialogueReactionData> reactionGroup)
        {
            reactionGroup = null;
            if (labelHash == 0)
            {
                return false;
            }

            reactionGroup = DetermineReaction(labelHash);
            return reactionGroup != null && reactionGroup.Count > 0;
        }

        public bool TryFindPatternGroupByPatternId(string patternId, out List<DialogueReactionData> reactionGroup)
        {
            reactionGroup = null;
            if (string.IsNullOrWhiteSpace(patternId))
            {
                return false;
            }

            string trimmedPatternId = patternId.Trim();
            foreach (KeyValuePair<int, List<DialogueReactionData>> entry in reactionDatabase)
            {
                if (!TryGetPatternGroup(entry.Key, trimmedPatternId, out List<DialogueReactionData> group) ||
                    group == null ||
                    group.Count == 0)
                {
                    continue;
                }

                reactionGroup = group;
                return true;
            }

            return false;
        }

        // =======================================================
        // 会話制御用ヘルパーメソッド
        // =======================================================
        
        private static DialogueReactionData FindRepeatEvent(List<DialogueReactionData> reactions, int currentRepeatCount)
        {
            if (currentRepeatCount < 2)
            {
                return null;
            }

            DialogueReactionData bestRepeat = null;
            int maxRepeatTrigger = -1;

            foreach (var r in reactions)
            {
                if (r.SpeechControl == SpeechControlType.RepeatEvent)
                {
                    int triggerCount = Mathf.Max(2, r.RepeatCount);

                    // ぴったり一致する回数があれば即採用
                    if (triggerCount == currentRepeatCount)
                    {
                        return r;
                    }
                    
                    // 「最大値を超えた場合」への備えとして、一番大きいトリガー回数を記憶しておく
                    if (triggerCount > maxRepeatTrigger)
                    {
                        maxRepeatTrigger = triggerCount;
                        bestRepeat = r;
                    }
                }
            }

            // ぴったり一致はしなかったが、現在の回数が最大トリガー回数を超過（または同等）している場合はそれを再利用する
            if (bestRepeat != null && currentRepeatCount >= maxRepeatTrigger)
            {
                return bestRepeat;
            }

            return null;
        }

        private DialogueReactionData GetHybridRandomSequenceReaction(List<DialogueReactionData> targetGroupReps, ConversationState state)
        {
            if (targetGroupReps.Count == 0) return null;

            int randomRepeatLimit = ResolveRandomRepeatLimit(targetGroupReps);

            // --- 優先1: 設定回数以内なら Random なグループから引く ---
            if (state.repeatCount <= randomRepeatLimit)
            {
                List<DialogueReactionData> randomPool = new List<DialogueReactionData>();
                foreach (var r in targetGroupReps)
                {
                    // 代表が Random 指定されているグループを追加
                    if (r.SpeechControl == SpeechControlType.Random)
                    {
                        randomPool.Add(r);
                    }
                }

                if (randomPool.Count > 0)
                {
                    return SelectRandomReaction(randomPool, state);
                }
            }

            // --- 優先2: 設定回数を超えた後（またはRandomが存在しない場合）は Sequence グループを順番に消化する ---
            List<DialogueReactionData> seqGroups = new List<DialogueReactionData>();
            foreach (var r in targetGroupReps)
            {
                if (r.SpeechControl == SpeechControlType.Sequence)
                {
                    seqGroups.Add(r);
                }
            }

            if (seqGroups.Count > 0)
            {
                // 現在の sequenceIndex が指すグループを取得
                if (state.sequenceIndex < seqGroups.Count)
                {
                    DialogueReactionData currentSeq = seqGroups[state.sequenceIndex];
                    if (currentSeq.SequenceProgressHold)
                    {
                        state.sequenceResetAnchorIndex = state.sequenceIndex;
                    }
                    state.sequenceIndex++; // 次回は１つ先の Sequence トピックへ進める
                    return currentSeq;
                }
                else
                {
                    // Sequenceを最後まで言い切った後は通常末尾に留まる。
                    // ただし末尾が Action/Event/Choice の場合は、再入力で外部フローを連打しないよう
                    // 直前の通常会話グループへ戻す。
                    DialogueReactionData replaySequence = ResolveSequenceReplayReaction(seqGroups, out int replayIndex);
                    if (replaySequence != null)
                    {
                        if (replaySequence.SequenceProgressHold)
                        {
                            state.sequenceResetAnchorIndex = replayIndex;
                        }

                        return replaySequence;
                    }

                    // フォールバック: 旧挙動どおり一番最後の Sequence グループに留まる
                    if (seqGroups[seqGroups.Count - 1].SequenceProgressHold)
                    {
                        state.sequenceResetAnchorIndex = seqGroups.Count - 1;
                    }
                    return seqGroups[seqGroups.Count - 1];
                }
            }

            // --- 優先3: Sequenceグループが１つもなく、Randomグループしかない場合は純粋なRandomとして全体から引く ---
            return SelectRandomReaction(targetGroupReps, state);
        }

        private static DialogueReactionData SelectRandomReaction(List<DialogueReactionData> randomPool, ConversationState state)
        {
            if (randomPool == null || randomPool.Count == 0)
            {
                return null;
            }

            if (state == null)
            {
                return randomPool[UnityEngine.Random.Range(0, randomPool.Count)];
            }

            List<DialogueReactionData> unspokenPool = new List<DialogueReactionData>();
            for (int i = 0; i < randomPool.Count; i++)
            {
                DialogueReactionData candidate = randomPool[i];
                string patternId = NormalizePatternId(candidate);
                if (string.IsNullOrWhiteSpace(patternId) ||
                    !state.spokenRandomPatternIds.Contains(patternId))
                {
                    unspokenPool.Add(candidate);
                }
            }

            List<DialogueReactionData> selectionPool = unspokenPool.Count > 0
                ? unspokenPool
                : BuildImmediateRepeatAvoidancePool(randomPool, state.lastRandomPatternId);

            DialogueReactionData selected = selectionPool[UnityEngine.Random.Range(0, selectionPool.Count)];
            string selectedPatternId = NormalizePatternId(selected);
            if (!string.IsNullOrWhiteSpace(selectedPatternId))
            {
                state.spokenRandomPatternIds.Add(selectedPatternId);
                state.lastRandomPatternId = selectedPatternId;
            }

            return selected;
        }

        private static List<DialogueReactionData> BuildImmediateRepeatAvoidancePool(
            List<DialogueReactionData> randomPool,
            string lastRandomPatternId)
        {
            if (randomPool == null || randomPool.Count <= 1 || string.IsNullOrWhiteSpace(lastRandomPatternId))
            {
                return randomPool;
            }

            List<DialogueReactionData> filtered = new List<DialogueReactionData>();
            for (int i = 0; i < randomPool.Count; i++)
            {
                DialogueReactionData candidate = randomPool[i];
                if (!string.Equals(NormalizePatternId(candidate), lastRandomPatternId, StringComparison.Ordinal))
                {
                    filtered.Add(candidate);
                }
            }

            return filtered.Count > 0 ? filtered : randomPool;
        }

        private static string NormalizePatternId(DialogueReactionData reaction)
        {
            if (reaction == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(reaction.PatternID))
            {
                return reaction.PatternID.Trim();
            }

            return $"{reaction.SourceRegexLabel ?? string.Empty}\u001f{reaction.Order}\u001f{reaction.TextJP ?? string.Empty}";
        }

        private static int ResolveRandomRepeatLimit(List<DialogueReactionData> targetGroupReps)
        {
            if (targetGroupReps == null)
            {
                return DefaultRandomRepeatLimit;
            }

            for (int i = 0; i < targetGroupReps.Count; i++)
            {
                DialogueReactionData reaction = targetGroupReps[i];
                if (reaction != null && reaction.RandomRepeatLimit >= 0)
                {
                    return reaction.RandomRepeatLimit;
                }
            }

            return DefaultRandomRepeatLimit;
        }

        private static DialogueReactionData ResolveSequenceReplayReaction(List<DialogueReactionData> seqGroups, out int replayIndex)
        {
            replayIndex = -1;
            if (seqGroups == null || seqGroups.Count == 0)
            {
                return null;
            }

            int lastIndex = seqGroups.Count - 1;
            DialogueReactionData lastGroup = seqGroups[lastIndex];
            if (!IsExternalFlowResponseType(lastGroup))
            {
                replayIndex = lastIndex;
                return lastGroup;
            }

            for (int i = lastIndex - 1; i >= 0; i--)
            {
                DialogueReactionData candidate = seqGroups[i];
                if (candidate == null || IsExternalFlowResponseType(candidate))
                {
                    continue;
                }

                replayIndex = i;
                return candidate;
            }

            replayIndex = lastIndex;
            return lastGroup;
        }

        private static bool IsExternalFlowResponseType(DialogueReactionData reaction)
        {
            if (reaction == null || string.IsNullOrWhiteSpace(reaction.ResponseType))
            {
                return false;
            }

            string responseType = reaction.ResponseType.Trim();
            return string.Equals(responseType, "Action", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(responseType, "Event", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(responseType, "Choice", StringComparison.OrdinalIgnoreCase);
        }

        private List<DialogueReactionData> FilterRepresentativesByCondition(List<DialogueReactionData> representatives)
        {
            List<DialogueReactionData> filtered = new List<DialogueReactionData>();
            ConversationGameState gameState = GetConversationGameState();

            for (int i = 0; i < representatives.Count; i++)
            {
                DialogueReactionData representative = representatives[i];
                if (representative == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(representative.Condition))
                {
                    filtered.Add(representative);
                    continue;
                }

                try
                {
                    if (ConversationGameStateConditionEvaluator.Evaluate(gameState, representative.Condition))
                    {
                        filtered.Add(representative);
                    }
                }
                catch (FormatException exception)
                {
                    Debug.LogWarning($"[DialogueEngine] condition 解釈失敗: {representative.Condition} / {exception.Message}");
                }
            }

            return filtered;
        }

        private bool TryBuildReactionGroups(int patternHash, bool includeCallOnly, out Dictionary<string, List<DialogueReactionData>> groupsByPattern, out List<string> orderedPatternIDs)
        {
            groupsByPattern = new Dictionary<string, List<DialogueReactionData>>();
            orderedPatternIDs = new List<string>();

            if (!reactionDatabase.TryGetValue(patternHash, out List<DialogueReactionData> reactions) || reactions.Count == 0)
            {
                return false;
            }

            foreach (DialogueReactionData reaction in reactions)
            {
                string patternId = string.IsNullOrWhiteSpace(reaction.PatternID)
                    ? global::System.Guid.NewGuid().ToString()
                    : reaction.PatternID.Trim();

                if (!groupsByPattern.TryGetValue(patternId, out List<DialogueReactionData> group))
                {
                    group = new List<DialogueReactionData>();
                    groupsByPattern[patternId] = group;
                    orderedPatternIDs.Add(patternId);
                }

                group.Add(reaction);
            }

            for (int i = 0; i < orderedPatternIDs.Count; i++)
            {
                List<DialogueReactionData> group = groupsByPattern[orderedPatternIDs[i]];
                group.Sort((a, b) => a.Order.CompareTo(b.Order));
            }

            if (!includeCallOnly)
            {
                for (int i = orderedPatternIDs.Count - 1; i >= 0; i--)
                {
                    string patternId = orderedPatternIDs[i];
                    List<DialogueReactionData> group = groupsByPattern[patternId];
                    if (!IsCallOnlyGroup(group) || IsRepeatEventGroup(group))
                    {
                        continue;
                    }

                    groupsByPattern.Remove(patternId);
                    orderedPatternIDs.RemoveAt(i);
                }
            }

            SortNumericPatternIdsStable(orderedPatternIDs);
            return orderedPatternIDs.Count > 0;
        }

        private static void SortNumericPatternIdsStable(List<string> patternIds)
        {
            if (patternIds == null || patternIds.Count <= 1)
            {
                return;
            }

            Dictionary<string, int> originalIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < patternIds.Count; i++)
            {
                if (!originalIndices.ContainsKey(patternIds[i]))
                {
                    originalIndices.Add(patternIds[i], i);
                }
            }

            patternIds.Sort((left, right) =>
            {
                if (int.TryParse(left, out int leftNumber) && int.TryParse(right, out int rightNumber))
                {
                    int numberCompare = leftNumber.CompareTo(rightNumber);
                    if (numberCompare != 0)
                    {
                        return numberCompare;
                    }
                }

                int leftIndex = originalIndices.TryGetValue(left, out int foundLeftIndex) ? foundLeftIndex : int.MaxValue;
                int rightIndex = originalIndices.TryGetValue(right, out int foundRightIndex) ? foundRightIndex : int.MaxValue;
                return leftIndex.CompareTo(rightIndex);
            });
        }

        private static bool IsCallOnlyGroup(List<DialogueReactionData> group)
        {
            if (group == null || group.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] != null && group[i].CallOnly)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRepeatEventGroup(List<DialogueReactionData> group)
        {
            if (group == null || group.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] != null && group[i].SpeechControl == SpeechControlType.RepeatEvent)
                {
                    return true;
                }
            }

            return false;
        }

        // =======================================================
        // CSV暗号化バイナリのメモリ内復号 ＆ パース処理
        // =======================================================

        private void LoadRegexDictionary()
        {
            Debug.LogWarning("[DialogueEngine] LoadRegexDictionary は無効です。ConversationDataManager からのみ読み込みます。");
        }

        private void LoadReactionDictionary()
        {
            Debug.LogWarning("[DialogueEngine] LoadReactionDictionary は無効です。ConversationDataManager からのみ読み込みます。");
        }

        private void LoadVulgarDictionary()
        {
            Debug.LogWarning("[DialogueEngine] LoadVulgarDictionary は無効です。ConversationDataManager からのみ読み込みます。");
        }

        private void LoadDialoguePreviewCsv()
        {
            Debug.LogWarning("[DialogueEngine] LoadDialoguePreviewCsv は無効です。ConversationDataManager からのみ読み込みます。");
        }

        private void LoadConversationDataFromManager()
        {
            regexPatterns.Clear();
            reactionDatabase.Clear();
            vulgarWords.Clear();

            if (conversationDataManager == null)
            {
                conversationDataManager = FindFirstObjectByType<ConversationDataManager>();
            }

            if (conversationDataManager == null)
            {
                ConversationDataManager[] managers = Resources.FindObjectsOfTypeAll<ConversationDataManager>();
                for (int i = 0; i < managers.Length; i++)
                {
                    ConversationDataManager candidate = managers[i];
                    if (candidate == null || !candidate.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    conversationDataManager = candidate;
                    break;
                }
            }

            if (conversationDataManager == null)
            {
                GameObject runtimeManagerObject = new GameObject("ConversationDataManager (Runtime)");
                DontDestroyOnLoad(runtimeManagerObject);
                conversationDataManager = runtimeManagerObject.AddComponent<ConversationDataManager>();
                Debug.LogWarning("[DialogueEngine] ConversationDataManager が見つからないため、Resources/TalkData からランタイム生成しました。");
            }

            conversationDataManager.Reload();

            List<string> vulgarCsvTexts = conversationDataManager.GetActiveVulgarCsvTexts();
            for (int i = 0; i < vulgarCsvTexts.Count; i++)
            {
                string vulgarCsvText = vulgarCsvTexts[i];
                if (string.IsNullOrWhiteSpace(vulgarCsvText))
                {
                    continue;
                }

                LoadVulgarStyleCsv(vulgarCsvText, "VulgarLanguage");
            }

            List<ConversationDataManager.CsvSource> csvSources = conversationDataManager.GetActiveCsvSources();
            if (csvSources == null || csvSources.Count == 0)
            {
                Debug.LogError("[DialogueEngine] ConversationDataManager に会話CSVソースが設定されていません。");
                EnsureCoreConversationFallbacks();
                return;
            }

            Debug.Log($"[DialogueEngine] 会話CSVソース: {string.Join(", ", csvSources.ConvertAll(source => source.Name))}");

            int parsedAssetCount = 0;
            for (int i = 0; i < csvSources.Count; i++)
            {
                ConversationDataManager.CsvSource source = csvSources[i];
                if (string.IsNullOrWhiteSpace(source.Text))
                {
                    continue;
                }

                if (TryLoadConversationCsvText(source.Text, source.Name))
                {
                    parsedAssetCount++;
                }
            }

            if (parsedAssetCount == 0)
            {
                Debug.LogError("[DialogueEngine] 実行用の会話CSVを読み込めませんでした。ConversationDataManager に RegexDict 系CSVを設定してください。");
            }

            EnsureCoreConversationFallbacks();
        }

        private void EnsureCoreConversationFallbacks()
        {
            EnsureOteConversationFallback();
        }

        private void EnsureOteConversationFallback()
        {
            string normalizedRegex = JapaneseTextNormalizer.NormalizeToken("おて");
            for (int i = 0; i < regexPatterns.Count; i++)
            {
                RegexPatternData pattern = regexPatterns[i];
                if (pattern == null ||
                    IsTeachingPattern(pattern) ||
                    !string.Equals(pattern.SourceRegex, normalizedRegex, StringComparison.Ordinal))
                {
                    continue;
                }

                if (HasNormalReactionForLabel(pattern.LabelHash))
                {
                    return;
                }
            }

            int labelHash = GetDeterministicHash(NormalizeLabelForHash("ote"));
            if (!reactionDatabase.ContainsKey(labelHash))
            {
                reactionDatabase[labelHash] = new List<DialogueReactionData>();
            }

            if (!HasNormalReactionForLabel(labelHash))
            {
                reactionDatabase[labelHash].Add(new DialogueReactionData
                {
                    LabelHash = labelHash,
                    PatternID = "ote_fallback",
                    Order = 0,
                    ResponseType = "Normal",
                    SpeechControl = SpeechControlType.Random,
                    TextJP = "お手って何？",
                    SourceRegexLabel = "ote"
                });
                reactionDatabase[labelHash].Add(new DialogueReactionData
                {
                    LabelHash = labelHash,
                    PatternID = "ote_fallback",
                    Order = 1,
                    ResponseType = "Normal",
                    SpeechControl = SpeechControlType.Random,
                    TextJP = "{{PLAYER_CALLING}}の手のひらに、{{CAT_PRONOUN}}の前足を乗せればいいの？",
                    SourceRegexLabel = "ote"
                });
                reactionDatabase[labelHash].Add(new DialogueReactionData
                {
                    LabelHash = labelHash,
                    PatternID = "ote_fallback",
                    Order = 2,
                    ResponseType = "Choice",
                    SpeechControl = SpeechControlType.Random,
                    TextJP = "そんなちっちゃい所に乗せられるかな……やってみていい？",
                    ChoiceYesLabel = "やってみよう",
                    ChoiceNoLabel = "やめておこう",
                    ChoiceNoPatternID = "99",
                    SourceRegexLabel = "ote"
                });
                reactionDatabase[labelHash].Add(new DialogueReactionData
                {
                    LabelHash = labelHash,
                    PatternID = "ote_fallback_no",
                    Order = 99,
                    ResponseType = "Normal",
                    ActionId = "action_ote",
                    SpeechControl = SpeechControlType.Random,
                    TextJP = "そ、そうだよね！危ないよね！何か他のことしよっか！",
                    HideUI = true,
                    SourceRegexLabel = "ote"
                });
            }

            regexPatterns.Add(new RegexPatternData
            {
                SourceRegex = normalizedRegex,
                LabelHash = labelHash,
                Priority = 10,
                Sensitivity = "SAFE"
            });

            Debug.LogWarning("[DialogueEngine] おて通常会話がCSVから登録されていなかったため、ランタイムフォールバックを追加しました。");
        }

        private void InitializeUnknownWordSystem()
        {
            unknownWordIntentParser = new RegexInputParser(GiantCatConversationDefaults.CreateConfig());
            unknownWordExtractor = new UnknownWordExtractor();
            unknownWordResponseSystem = new UnknownWordResponseSystem();
        }

        private UnknownWordAnalysisResult TryExtractUnknownWord(PlayerInputContext inputContext)
        {
            if (unknownWordIntentParser == null || unknownWordExtractor == null)
            {
                InitializeUnknownWordSystem();
            }

            try
            {
                ConversationParseResult parseResult = unknownWordIntentParser != null
                    ? unknownWordIntentParser.Parse(inputContext)
                    : null;

                DialogueIntentResult intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
                UnknownWordResult unknownWordResult = unknownWordExtractor != null
                    ? unknownWordExtractor.Extract(inputContext, intentResult)
                    : null;

                return new UnknownWordAnalysisResult
                {
                    UnknownWordResult = unknownWordResult,
                    IntentResult = intentResult,
                    ParseResult = parseResult
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DialogueEngine] Unknown word extraction failed: {exception.Message}");
                return null;
            }
        }

        private sealed class MatchEvaluationResult
        {
            public RegexPatternData BestPattern;
            public int BestMatchIndex = -1;
            public string BestMatchedText = string.Empty;
            public string NormalizedInput = string.Empty;
            public List<string> IntentCandidates = new List<string>();
        }

        private sealed class UnknownWordAnalysisResult
        {
            public UnknownWordResult UnknownWordResult;
            public DialogueIntentResult IntentResult;
            public ConversationParseResult ParseResult;
        }

        private bool TryLoadConversationCsvAsset(TextAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return false;
            }

            return TryLoadConversationCsvText(asset.text, asset.name);
        }

        private bool TryLoadConversationCsvText(string csvText, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return false;
            }

            string resolvedSourceName = string.IsNullOrWhiteSpace(sourceName) ? "conversation.csv" : sourceName;
            List<string> lines = SplitCsvLinesRobust(csvText);
            if (lines.Count <= 1)
            {
                Debug.LogWarning($"[DialogueEngine] {resolvedSourceName} は有効なデータ行がないためスキップします。");
                return false;
            }

            string[] headers = ParseCSVLine(lines[0]);
            if (LooksLikeCatCharactersCsv(headers))
            {
                LoadCatCharactersCsv(csvText, resolvedSourceName);
                return true;
            }

            int idxRegexPattern = FindColumnIndex(headers, "regex", "regex_jp", "正規表現", "pattern_text");
            int idxSourceRegex = FindColumnIndex(headers, "__source_regex", "source_regex", "意味キー", "label", "meaning", "regex");
            int idxText = FindColumnIndex(headers, "text_jp 1", "output_ja", "Line", "text_jp1", "text");

            if (idxRegexPattern >= 0 && (idxSourceRegex >= 0 || idxText >= 0))
            {
                LoadReactionStyleCsv(csvText, resolvedSourceName, true);
                return true;
            }

            Debug.LogWarning($"[DialogueEngine] {resolvedSourceName} は対応していないCSV形式のためスキップします。");
            return false;
        }

        private TeachingCategory ResolveTeachingCategoryFromSource(string sourceName)
        {
            string normalized = (sourceName ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            if (normalized.Contains("talkbody") || normalized.Contains("body"))
            {
                return TeachingCategory.FavoritePoint;
            }

            if (normalized.Contains("series"))
            {
                return TeachingCategory.CatSeries;
            }

            if (normalized.Contains("catexpression") || normalized.Contains("expression") || normalized.Contains("idiom"))
            {
                return TeachingCategory.CatExpression;
            }

            if (normalized.Contains("felidae"))
            {
                return TeachingCategory.Felidae;
            }

            if (normalized.Contains("catbreed") || normalized.Contains("breed"))
            {
                return TeachingCategory.CatBreed;
            }

            if (normalized.Contains("favoriteweather") || normalized.Contains("weather"))
            {
                return TeachingCategory.FavoriteWeather;
            }

            return TeachingCategory.None;
        }

        private TeachingCategory ResolveTeachingCategory(string[] columns, int categoryIndex, TeachingCategory fallback)
        {
            if (categoryIndex >= 0 && columns != null && columns.Length > categoryIndex)
            {
                string raw = columns[categoryIndex];
                if (TryParseTeachingCategory(raw, out TeachingCategory parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private string ResolveTeachingEntryId(string[] columns, int entryIdIndex, string sourceName, string labelRaw)
        {
            string explicitId = entryIdIndex >= 0 && columns != null && columns.Length > entryIdIndex ? columns[entryIdIndex] : string.Empty;
            if (!string.IsNullOrWhiteSpace(explicitId))
            {
                return explicitId.Trim();
            }

            string rowId = columns != null && columns.Length > 0 ? columns[0] : string.Empty;
            if (!string.IsNullOrWhiteSpace(rowId))
            {
                return $"{NormalizeSourceId(sourceName)}:{rowId.Trim()}:{labelRaw}";
            }

            return $"{NormalizeSourceId(sourceName)}:{labelRaw}";
        }

        private string ResolveTeachingSeriesGroupId(string[] columns, int groupIndex, string labelRaw)
        {
            string explicitId = groupIndex >= 0 && columns != null && columns.Length > groupIndex ? columns[groupIndex] : string.Empty;
            if (!string.IsNullOrWhiteSpace(explicitId))
            {
                return explicitId.Trim();
            }

            return labelRaw ?? string.Empty;
        }

        private string ResolveTeachingDisplayName(string[] columns, int displayNameIndex)
        {
            if (displayNameIndex >= 0 && columns != null && columns.Length > displayNameIndex && !string.IsNullOrWhiteSpace(columns[displayNameIndex]))
            {
                return columns[displayNameIndex].Trim();
            }

            return string.Empty;
        }

        private static string NormalizeSourceId(string sourceName)
        {
            return string.IsNullOrWhiteSpace(sourceName)
                ? "ConversationCsv"
                : sourceName.Trim().Replace(" ", "_");
        }

        private static bool LooksLikeCatCharactersCsv(string[] headers)
        {
            if (headers == null || headers.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < headers.Length; i++)
            {
                string header = (headers[i] ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(header) && CatCharacterDestinationLabels.ContainsKey(header))
                {
                    return true;
                }
            }

            return false;
        }

        private void LoadCatCharactersCsv(string csvRaw, string sourceName)
        {
            List<string> lines = SplitCsvLinesRobust(csvRaw);
            if (lines.Count <= 1)
            {
                return;
            }

            string[] headers = ParseCSVLine(lines[0]);
            int addedRegexCount = 0;
            var knownRegexKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < regexPatterns.Count; i++)
            {
                RegexPatternData pattern = regexPatterns[i];
                if (pattern == null)
                {
                    continue;
                }

                knownRegexKeys.Add($"{pattern.LabelHash}|{pattern.SourceRegex}");
            }

            for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
            {
                string destinationLabel = ResolveCatCharacterDestinationLabel(headers[columnIndex]);
                if (string.IsNullOrWhiteSpace(destinationLabel))
                {
                    continue;
                }

                int labelHash = GetDeterministicHash(NormalizeLabelForHash(destinationLabel));
                TeachingDiscoveryStore.RememberEntryKey(TeachingCategory.CatSeries, destinationLabel);
                TeachingDiscoveryStore.RememberSeriesKey(destinationLabel);
                for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
                {
                    string[] columns = ParseCSVLine(lines[lineIndex]);
                    if (columns.Length <= columnIndex)
                    {
                        continue;
                    }

                    string rawName = (columns[columnIndex] ?? string.Empty).Trim(' ', '　', '\uFEFF', '\u200B', '\t');
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        continue;
                    }

                    string normalizedName = JapaneseTextNormalizer.NormalizeToken(rawName);
                    if (string.IsNullOrWhiteSpace(normalizedName))
                    {
                        continue;
                    }

                    string regexKey = $"{labelHash}|{normalizedName}";
                    if (!knownRegexKeys.Add(regexKey))
                    {
                        continue;
                    }

                    regexPatterns.Add(new RegexPatternData
                    {
                        SourceRegex = normalizedName,
                        LabelHash = labelHash,
                        Priority = CatCharacterPriority,
                        Sensitivity = "SAFE",
                        TeachingCategory = TeachingCategory.CatSeries,
                        // キャラクター個別ではなく、列単位で作品カテゴリを記録する。
                        TeachingEntryId = destinationLabel,
                        TeachingSeriesGroupId = destinationLabel,
                        TeachingRevisitDisabled = true
                    });
                    addedRegexCount++;
                }
            }

            Debug.Log($"[DialogueEngine] {sourceName} から猫キャラ名誘導を {addedRegexCount} 件ロードしました。");
        }

        private static string ResolveCatCharacterDestinationLabel(string header)
        {
            string normalizedHeader = (header ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedHeader))
            {
                return string.Empty;
            }

            return CatCharacterDestinationLabels.TryGetValue(normalizedHeader, out string destinationLabel) &&
                   !string.IsNullOrWhiteSpace(destinationLabel)
                ? destinationLabel
                : normalizedHeader;
        }

        private void LoadVulgarStyleCsv(string csvRaw, string sourceName)
        {
            List<string> lines = SplitCsvLinesRobust(csvRaw);
            if (lines.Count <= 1)
            {
                return;
            }

            string[] headers = ParseCSVLine(lines[0]);
            int idxName = FindColumnIndex(headers, "名前", "name", "word");
            int firstDataLine = 1;
            if (idxName < 0)
            {
                idxName = 0;
                firstDataLine = 0;
            }

            int addedWordCount = 0;
            for (int i = firstDataLine; i < lines.Count; i++)
            {
                string[] columns = ParseCSVLine(lines[i]);
                if (columns.Length <= idxName)
                {
                    continue;
                }

                string colWord = columns[idxName];
                if (string.IsNullOrWhiteSpace(colWord))
                {
                    continue;
                }

                string cleanWord = ToHiragana(colWord.Trim(' ', '　', '\uFEFF', '\u200B', '\t'));
                if (!string.IsNullOrEmpty(cleanWord) && !vulgarWords.Contains(cleanWord))
                {
                    vulgarWords.Add(cleanWord);
                    addedWordCount++;
                }
            }

            Debug.Log($"[DialogueEngine] {sourceName} から下品フィルタ単語を {addedWordCount} 件ロードしました。");
        }

        private void LoadReactionStyleCsv(string csvRaw, string sourceName, bool registerRegexPatterns)
        {
            List<string> lines = SplitCsvLinesRobust(csvRaw);
            if (lines.Count <= 1) return;

            string[] headers = ParseCSVLine(lines[0]);
            Debug.Log($"[DialogueEngine] {sourceName} ヘッダー一覧: {string.Join(", ", headers)}");

            int idxRegexPattern = FindColumnIndex(headers, "regex", "regex_jp", "正規表現", "pattern_text");
            int idxRegexLabel = FindColumnIndex(headers, "regex_id", "RegexID", "__source_regex", "source_regex", "意味キー", "label", "meaning", "regex");
            int idxPriority = FindColumnIndex(headers, "Priority", "priority", "優先度");
            int idxSensitivity = FindColumnIndex(headers, "sensitivity_level", "sensitivity", "フィルタ");
            int idxIntent = FindColumnIndex(headers, "intent");
            int idxReaction = FindColumnIndex(headers, "reaction_type");
            int idxText = FindColumnIndex(headers, "text_jp 1", "output_ja", "Line", "text_jp1", "text");
            int idxPattern = FindColumnIndex(headers, "Pattern", "pattern", "Pattern ID");
            int idxActionId = FindColumnIndex(headers, "action_id", "ActionID");
            int idxTimedEventKey = FindColumnIndex(headers, "timed_event_key", "TimedEventKey", "system_timed_event_key", "SystemTimedEventKey");
            int idxNextAction = FindColumnIndex(headers, "NextAction", "反応リンク", "next_action");
            int idxResponseType = FindColumnIndex(headers, "response_type", "ResponseType");
            int idxCondition = FindColumnIndex(headers, "condition", "Condition", "条件", "tag", "tags");
            int idxWaitTime = FindColumnIndex(headers, "WaitTime", "wait_time");
            int idxHideUi = FindColumnIndex(headers, "HideUI", "hide_ui");
            int idxAnimation = FindColumnIndex(headers, "Animation", "animation");
            int idxEmotionChangeType = FindColumnIndex(headers, "emotion_change_type", "EmotionChangeType");
            int idxEmotionChangeValue = FindColumnIndex(headers, "emotion_change_value", "EmotionChangeValue");
            int idxCallOnly = FindColumnIndex(headers, "call_only", "CallOnly", "pattern_access", "PatternAccess");
            int idxChoiceYesPattern = FindColumnIndex(headers, "choice_yes_pattern", "ChoiceYesPattern", "choice_yes", "yes_pattern");
            int idxChoiceNoPattern = FindColumnIndex(headers, "choice_no_pattern", "ChoiceNoPattern", "choice_no", "no_pattern");
            int idxChoiceYesNextPattern = FindColumnIndex(headers, "yes_next_pattern", "YesNextPattern", "yes_next");
            int idxChoiceNoNextPattern = FindColumnIndex(headers, "no_next_pattern", "NoNextPattern", "no_next");
            int idxChoiceQuestionJa = FindColumnIndex(headers, "question_ja", "ChoiceQuestionJa", "question");
            int idxChoiceYesJa = FindColumnIndex(headers, "choice_yes_ja", "ChoiceYesLabel", "choice_yes_label");
            int idxChoiceNoJa = FindColumnIndex(headers, "choice_no_ja", "ChoiceNoLabel", "choice_no_label");
            int idxSequenceProgressHold = FindColumnIndex(headers, "sequence_progress_hold", "SequenceProgressHold", "progress_hold", "進行保持");
            int idxRandomRepeatLimit = FindColumnIndex(headers, "random_repeat_limit", "RandomRepeatLimit", "random_limit", "RandomLimit");
            int idxSpeechControl = FindColumnIndex(headers, "SpeechControl", "speech_control");
            int idxOrder = FindColumnIndex(headers, "Order");
            int idxRepeatCount = FindColumnIndex(headers, "RepeatCount", "repeat_count");
            int idxTargetPattern = FindColumnIndex(headers, "target_pattern", "TargetPattern", "targetPattern");
            int idxStateSeq = FindColumnIndex(headers, "state_seq");
            int idxEndCount = FindColumnIndex(headers, "終了カウント");
            int idxQuestionKeyword = FindColumnIndex(headers, "question_keyword", "QuestionKeyword", "keyword");
            int idxQuestionInsertText = FindColumnIndex(headers, "question_insert_text", "QuestionInsertText", "insertText");
            int idxTeachingCategory = FindColumnIndex(headers, "TeachingCategory", "teaching_category", "category_id", "CategoryId");
            int idxTeachingEntryId = FindColumnIndex(headers, "EntryID", "entry_id", "TeachingEntryID", "teaching_entry_id");
            int idxTeachingSeriesGroupId = FindColumnIndex(headers, "SeriesGroupID", "series_group_id", "TeachingSeriesGroupID", "teaching_series_group_id");
            int idxTeachingDisplayName = FindColumnIndex(headers, "DisplayName", "display_name", "ItemName", "item_name", "input");
            int idxTeachingPromptDisabled = FindColumnIndex(headers, "teaching_prompt_disabled", "TeachingPromptDisabled", "disable_teaching_prompt", "DisableTeachingPrompt");
            TeachingCategory sourceTeachingCategory = ResolveTeachingCategoryFromSource(sourceName);

            var knownRegexKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < regexPatterns.Count; i++)
            {
                RegexPatternData pattern = regexPatterns[i];
                knownRegexKeys.Add($"{pattern.LabelHash}|{pattern.SourceRegex}");
            }

            int addedRegexCount = 0;
            int addedReactionCount = 0;

            for (int i = 1; i < lines.Count; i++)
            {
                string[] columns = ParseCSVLine(lines[i]);
                if (columns.Length == 0) continue;

                string sourceRegex = (idxRegexPattern >= 0 && columns.Length > idxRegexPattern) ? columns[idxRegexPattern] : string.Empty;
                string labelRaw = (idxRegexLabel >= 0 && columns.Length > idxRegexLabel) ? columns[idxRegexLabel] : string.Empty;

                if (string.IsNullOrWhiteSpace(labelRaw))
                {
                    labelRaw = sourceRegex;
                }

                if (string.IsNullOrWhiteSpace(labelRaw))
                {
                    continue;
                }

                labelRaw = NormalizeLabelForHash(labelRaw);
                int labelHash = GetDeterministicHash(labelRaw);

                if (registerRegexPatterns && !string.IsNullOrWhiteSpace(sourceRegex))
                {
                    string normalizedRegex = JapaneseTextNormalizer.NormalizeToken(sourceRegex);
                    string regexKey = $"{labelHash}|{normalizedRegex}";
                    if (knownRegexKeys.Add(regexKey))
                    {
                        RegexPatternData data = new RegexPatternData
                        {
                            SourceRegex = normalizedRegex,
                            LabelHash = labelHash,
                            Priority = 0,
                            Sensitivity = "SAFE",
                            TeachingCategory = ResolveTeachingCategory(columns, idxTeachingCategory, sourceTeachingCategory),
                            TeachingEntryId = ResolveTeachingEntryId(columns, idxTeachingEntryId, sourceName, labelRaw),
                            TeachingSeriesGroupId = ResolveTeachingSeriesGroupId(columns, idxTeachingSeriesGroupId, labelRaw),
                            TeachingDisplayName = ResolveTeachingDisplayName(columns, idxTeachingDisplayName),
                            TeachingPromptDisabled = idxTeachingPromptDisabled >= 0 &&
                                                     columns.Length > idxTeachingPromptDisabled &&
                                                     ParseBool(columns[idxTeachingPromptDisabled])
                        };

                        if (data.TeachingCategory != TeachingCategory.None)
                        {
                            TeachingDiscoveryStore.RememberEntryKey(data.TeachingCategory, data.TeachingEntryId);
                            if (data.TeachingCategory == TeachingCategory.CatSeries)
                            {
                                TeachingDiscoveryStore.RememberSeriesKey(data.TeachingSeriesGroupId);
                            }
                        }

                        if (idxPriority >= 0 && columns.Length > idxPriority && int.TryParse(columns[idxPriority], out int prio))
                        {
                            data.Priority = prio;
                        }

                        if (idxSensitivity >= 0 && columns.Length > idxSensitivity && !string.IsNullOrWhiteSpace(columns[idxSensitivity]))
                        {
                            data.Sensitivity = columns[idxSensitivity];
                        }

                        regexPatterns.Add(data);
                        addedRegexCount++;
                    }
                }

                DialogueReactionData reaction = new DialogueReactionData()
                {
                    LabelHash = labelHash,
                    TextJP = (idxText >= 0 && columns.Length > idxText) ? columns[idxText] : "",
                    TeachingCategory = ResolveTeachingCategory(columns, idxTeachingCategory, sourceTeachingCategory).ToString(),
                    TeachingEntryId = ResolveTeachingEntryId(columns, idxTeachingEntryId, sourceName, labelRaw),
                    TeachingSeriesGroupId = ResolveTeachingSeriesGroupId(columns, idxTeachingSeriesGroupId, labelRaw),
                    TeachingDisplayName = ResolveTeachingDisplayName(columns, idxTeachingDisplayName)
                };

                if (idxPattern >= 0 && columns.Length > idxPattern) reaction.PatternID = columns[idxPattern];
                if (idxQuestionKeyword >= 0 && idxQuestionInsertText >= 0 &&
                    columns.Length > idxQuestionKeyword && columns.Length > idxQuestionInsertText)
                {
                    string[] keywords = (columns[idxQuestionKeyword] ?? string.Empty)
                        .Split(new[] { "||" }, StringSplitOptions.None);
                    string[] insertTexts = (columns[idxQuestionInsertText] ?? string.Empty)
                        .Split(new[] { "||" }, StringSplitOptions.None);
                    int questionCount = Math.Min(keywords.Length, insertTexts.Length);
                    for (int questionIndex = 0; questionIndex < questionCount; questionIndex++)
                    {
                        string keyword = keywords[questionIndex].Trim();
                        string insertText = insertTexts[questionIndex].Trim();
                        if (!string.IsNullOrEmpty(keyword) && !string.IsNullOrEmpty(insertText))
                        {
                            reaction.QuestionLinks.Add(
                                new DialogueLogQuestionLink(keyword, insertText));
                        }
                    }
                }
                if (idxIntent >= 0 && columns.Length > idxIntent) reaction.IntentID = columns[idxIntent];
                if (idxReaction >= 0 && columns.Length > idxReaction) reaction.ReactionType = columns[idxReaction];
                if (idxRegexLabel >= 0 && columns.Length > idxRegexLabel) reaction.SourceRegexLabel = columns[idxRegexLabel];
                if (idxActionId >= 0 && columns.Length > idxActionId) reaction.ActionId = columns[idxActionId];
                if (idxTimedEventKey >= 0 && columns.Length > idxTimedEventKey) reaction.TimedEventKey = columns[idxTimedEventKey]?.Trim();
                if (idxNextAction >= 0 && columns.Length > idxNextAction) reaction.NextAction = columns[idxNextAction];
                if (idxResponseType >= 0 && columns.Length > idxResponseType) reaction.ResponseType = columns[idxResponseType];
                if (idxCondition >= 0 && columns.Length > idxCondition) reaction.Condition = columns[idxCondition];
                if (idxAnimation >= 0 && columns.Length > idxAnimation) reaction.Animation = columns[idxAnimation];
                if (idxWaitTime >= 0 && columns.Length > idxWaitTime && TryParseFloat(columns[idxWaitTime], out float waitTime))
                {
                    reaction.WaitTime = waitTime;
                }
                if (idxHideUi >= 0 && columns.Length > idxHideUi)
                {
                    reaction.HideUI = ParseBool(columns[idxHideUi]);
                }
                if (idxEmotionChangeType >= 0 && columns.Length > idxEmotionChangeType) reaction.EmotionChangeType = columns[idxEmotionChangeType];
                if (idxEmotionChangeValue >= 0 && columns.Length > idxEmotionChangeValue && int.TryParse(columns[idxEmotionChangeValue], out int emotionChangeValue))
                {
                    reaction.EmotionChangeValue = emotionChangeValue;
                }
                if (idxCallOnly >= 0 && columns.Length > idxCallOnly)
                {
                    reaction.CallOnly = ParseCallOnlyValue(columns[idxCallOnly]);
                }
                if (idxChoiceYesPattern >= 0 && columns.Length > idxChoiceYesPattern)
                {
                    reaction.ChoiceYesPatternID = columns[idxChoiceYesPattern]?.Trim();
                }
                if (string.IsNullOrWhiteSpace(reaction.ChoiceYesPatternID) &&
                    idxChoiceYesNextPattern >= 0 &&
                    columns.Length > idxChoiceYesNextPattern)
                {
                    reaction.ChoiceYesPatternID = columns[idxChoiceYesNextPattern]?.Trim();
                }
                if (idxChoiceNoPattern >= 0 && columns.Length > idxChoiceNoPattern)
                {
                    reaction.ChoiceNoPatternID = columns[idxChoiceNoPattern]?.Trim();
                }
                if (string.IsNullOrWhiteSpace(reaction.ChoiceNoPatternID) &&
                    idxChoiceNoNextPattern >= 0 &&
                    columns.Length > idxChoiceNoNextPattern)
                {
                    reaction.ChoiceNoPatternID = columns[idxChoiceNoNextPattern]?.Trim();
                }
                if (idxChoiceQuestionJa >= 0 && columns.Length > idxChoiceQuestionJa)
                {
                    reaction.ChoiceQuestionJa = columns[idxChoiceQuestionJa];
                }
                if (idxChoiceYesJa >= 0 && columns.Length > idxChoiceYesJa)
                {
                    reaction.ChoiceYesLabel = columns[idxChoiceYesJa];
                }
                if (idxChoiceNoJa >= 0 && columns.Length > idxChoiceNoJa)
                {
                    reaction.ChoiceNoLabel = columns[idxChoiceNoJa];
                }
                if (idxTargetPattern >= 0 && columns.Length > idxTargetPattern)
                {
                    reaction.TargetPatternID = columns[idxTargetPattern]?.Trim();
                }
                if (idxSequenceProgressHold >= 0 && columns.Length > idxSequenceProgressHold)
                {
                    reaction.SequenceProgressHold = ParseBool(columns[idxSequenceProgressHold]);
                }
                if (idxRandomRepeatLimit >= 0 && columns.Length > idxRandomRepeatLimit && int.TryParse(columns[idxRandomRepeatLimit], out int randomRepeatLimit))
                {
                    reaction.RandomRepeatLimit = Math.Max(0, randomRepeatLimit);
                }

                bool isSpeechControlExplicitlySet = false;
                if (idxSpeechControl >= 0 && columns.Length > idxSpeechControl && !string.IsNullOrWhiteSpace(columns[idxSpeechControl]))
                {
                    string scStr = columns[idxSpeechControl].Trim();
                    if (string.Equals(scStr, "Sequence", StringComparison.OrdinalIgnoreCase))
                    {
                        reaction.SpeechControl = SpeechControlType.Sequence;
                        isSpeechControlExplicitlySet = true;
                    }
                    else if (string.Equals(scStr, "RepeatEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        reaction.SpeechControl = SpeechControlType.RepeatEvent;
                        isSpeechControlExplicitlySet = true;
                    }
                    else if (string.Equals(scStr, "Random", StringComparison.OrdinalIgnoreCase))
                    {
                        reaction.SpeechControl = SpeechControlType.Random;
                        isSpeechControlExplicitlySet = true;
                    }
                    else if (string.Equals(scStr, "Call", StringComparison.OrdinalIgnoreCase))
                    {
                        reaction.SpeechControl = SpeechControlType.Call;
                        isSpeechControlExplicitlySet = true;
                    }
                    else if (string.Equals(scStr, "Return", StringComparison.OrdinalIgnoreCase))
                    {
                        reaction.SpeechControl = SpeechControlType.Return;
                        isSpeechControlExplicitlySet = true;
                    }
                }

                bool hasOrderData = false;
                if (idxOrder >= 0 && columns.Length > idxOrder && !string.IsNullOrWhiteSpace(columns[idxOrder]))
                {
                    if (int.TryParse(columns[idxOrder], out int order))
                    {
                        reaction.Order = order;
                        hasOrderData = true;
                    }
                }
                else if (idxStateSeq >= 0 && columns.Length > idxStateSeq && !string.IsNullOrWhiteSpace(columns[idxStateSeq]))
                {
                    if (int.TryParse(columns[idxStateSeq], out int seqVal))
                    {
                        reaction.Order = seqVal;
                        hasOrderData = true;
                    }
                }

                bool hasRepeatData = false;
                if (idxRepeatCount >= 0 && columns.Length > idxRepeatCount && !string.IsNullOrWhiteSpace(columns[idxRepeatCount]))
                {
                    if (int.TryParse(columns[idxRepeatCount], out int repeat))
                    {
                        reaction.RepeatCount = repeat;
                        hasRepeatData = true;
                    }
                }
                else if (idxEndCount >= 0 && columns.Length > idxEndCount && !string.IsNullOrWhiteSpace(columns[idxEndCount]))
                {
                    if (int.TryParse(columns[idxEndCount], out int endCount))
                    {
                        reaction.RepeatCount = endCount;
                        hasRepeatData = true;
                    }
                }

                if (!isSpeechControlExplicitlySet)
                {
                    if (hasOrderData)
                    {
                        reaction.SpeechControl = SpeechControlType.Sequence;
                    }
                    else if (hasRepeatData)
                    {
                        reaction.SpeechControl = SpeechControlType.RepeatEvent;
                    }
                    else
                    {
                        reaction.SpeechControl = SpeechControlType.Random;
                    }
                }

                if (!reactionDatabase.ContainsKey(labelHash))
                {
                    reactionDatabase[labelHash] = new List<DialogueReactionData>();
                }

                reactionDatabase[labelHash].Add(reaction);
                addedReactionCount++;
            }

            Debug.Log($"[DialogueEngine] {sourceName} 取込完了: regex {addedRegexCount}件, reaction {addedReactionCount}件, total regex {regexPatterns.Count}件, total labels {reactionDatabase.Count}種");
        }

        private static string BuildPreviewPatternId(ConversationRouteDefinition route)
        {
            string groupId = !string.IsNullOrWhiteSpace(route?.metadata?.sourceGroupId)
                ? route.metadata.sourceGroupId
                : !string.IsNullOrWhiteSpace(route?.metadata?.regexId)
                    ? route.metadata.regexId
                    : route?.id ?? string.Empty;

            if (route?.metadata == null)
            {
                return groupId;
            }

            return route.metadata.pattern > 0
                ? $"{groupId}:{route.metadata.pattern}"
                : groupId;
        }

        private static SpeechControlType ParsePreviewSpeechControl(string speechControl)
        {
            switch ((speechControl ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sequence":
                    return SpeechControlType.Sequence;
                case "repeatevent":
                    return SpeechControlType.RepeatEvent;
                case "call":
                    return SpeechControlType.Call;
                case "return":
                    return SpeechControlType.Return;
                default:
                    return SpeechControlType.Random;
            }
        }

        private void ResetConversationStateIfStale(
            int patternHash,
            List<DialogueReactionData> groupRepresentatives,
            ConversationState state,
            int currentDay)
        {
            if (state == null || state.lastSpokenDay < 0)
            {
                return;
            }

            if (currentDay - state.lastSpokenDay < DefaultSequenceResetDays)
            {
                return;
            }

            if (lastPatternHash == patternHash)
            {
                lastPatternHash = 0;
            }

            state.repeatCount = 0;

            List<DialogueReactionData> sequenceGroups = new List<DialogueReactionData>();
            for (int i = 0; i < groupRepresentatives.Count; i++)
            {
                DialogueReactionData representative = groupRepresentatives[i];
                if (representative != null && representative.SpeechControl == SpeechControlType.Sequence)
                {
                    sequenceGroups.Add(representative);
                }
            }

            if (sequenceGroups.Count <= 0)
            {
                return;
            }

            state.sequenceResetAnchorIndex = Mathf.Clamp(state.sequenceResetAnchorIndex, 0, sequenceGroups.Count - 1);
            state.sequenceIndex = state.sequenceResetAnchorIndex;
        }

        private int GetCurrentGameDay()
        {
            if (timeManager == null)
            {
                timeManager = FindFirstObjectByType<Nekolpos.TimeSystem.TimeManager>();
            }

            if (timeManager != null)
            {
                return Mathf.Max(1, timeManager.CurrentDay);
            }

            if (conversationGameStateManager == null)
            {
                conversationGameStateManager = FindFirstObjectByType<ConversationGameStateManager>();
            }

            if (conversationGameStateManager != null &&
                conversationGameStateManager.State != null &&
                conversationGameStateManager.State.TryGetInt("CurrentDay", out int currentDay))
            {
                return Mathf.Max(1, currentDay);
            }

            return 1;
        }

        private ConversationGameState GetConversationGameState()
        {
            if (conversationGameStateManager == null)
            {
                conversationGameStateManager = FindFirstObjectByType<ConversationGameStateManager>();
            }

            ConversationGameState state = conversationGameStateManager != null ? conversationGameStateManager.State : null;
            RefreshConversationTimeState(state);
            RefreshConversationCatState(state);
            RefreshPersistentPlayerDefeatState(state);
            return state;
        }

        private static void RefreshConversationCatState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            StatusManager statusManager = FindFirstObjectByType<StatusManager>();
            if (statusManager == null)
            {
                return;
            }

            SetConversationInt(state, "Affection", statusManager.GetValue(StatusType.Affection));
            SetConversationInt(state, "Sadistic", statusManager.GetValue(StatusType.Sadistic));
            SetConversationInt(state, "Concern", statusManager.GetValue(StatusType.Concern));
            SetConversationInt(state, "Hostility", statusManager.GetValue(StatusType.Hostility));
            SetConversationInt(state, "Obedience", statusManager.GetValue(StatusType.Obedience));
            SetConversationInt(state, "Instinct", statusManager.GetValue(StatusType.Instinct));
        }

        private static void RefreshPersistentPlayerDefeatState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            string[] defeatReasons =
            {
                "Nekomata",
                "Predation",
                "Anger",
                "Accident"
            };

            string lastReason = PlayerPrefs.GetString(DialogueManager.PlayerDefeatLastReasonPrefsKey, string.Empty);
            state.SetString("LastPlayerDefeatReason", lastReason ?? string.Empty);

            for (int i = 0; i < defeatReasons.Length; i++)
            {
                string reason = defeatReasons[i];
                state.SetBool(
                    "PlayerDefeatedBy" + reason,
                    PlayerPrefs.GetInt(DialogueManager.PlayerDefeatReasonPrefsPrefix + reason, 0) != 0);
            }
        }

        private static void SetConversationInt(ConversationGameState state, string key, int value)
        {
            int clamped = Mathf.Clamp(value, 0, 100);
            state.SetInt(key, clamped);
            state.SetInt(key.ToLowerInvariant(), clamped);
        }

        private void RefreshConversationTimeState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            if (TryResolveCurrentConversationPhase(out string phase))
            {
                state.SetString("Phase", phase);
                state.SetString("CurrentPhase", phase);
            }

            state.SetInt("CurrentDay", GetCurrentGameDay());
            RefreshConversationWeatherState(state);
        }

        private void RefreshConversationWeatherState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            if (timeManager == null)
            {
                timeManager = FindFirstObjectByType<TimeManager>();
            }

            if (timeManager == null)
            {
                return;
            }

            state.SetString(WeatherSystem.WeatherKey, WeatherSystem.ToDisplayText(timeManager.Weather));
            state.SetString(WeatherSystem.CurrentWeatherKey, timeManager.Weather.ToString());
            state.SetString(WeatherSystem.TomorrowWeatherKey, WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            state.SetString(WeatherSystem.CurrentWeatherForecastKey, timeManager.TomorrowWeather.ToString());
            state.SetString(WeatherSystem.ForecastWeatherKey, WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            state.SetString(WeatherSystem.ForecastWeatherMisspelledKey, WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            state.SetString(WeatherSystem.ForecastWeatherQuestionKey, WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
            state.SetString(WeatherSystem.NextActualWeatherKey, timeManager.NextActualWeather.ToString());
            state.SetBool(WeatherSystem.ForecastCorrectKey, timeManager.ForecastCorrect);
        }

        private bool TryResolveCurrentConversationPhase(out string phase)
        {
            if (timeManager == null)
            {
                timeManager = FindFirstObjectByType<Nekolpos.TimeSystem.TimeManager>();
            }

            if (timeManager != null)
            {
                phase = ConvertPeriodToConversationPhase(timeManager.CurrentPeriod);
                return !string.IsNullOrWhiteSpace(phase);
            }

            GameManager manager = GameManager.Instance ?? FindFirstObjectByType<GameManager>();
            if (manager != null && TryResolveCurrentConversationPhase(manager.CurrentState, out phase))
            {
                return true;
            }

            phase = string.Empty;
            return false;
        }

        private static string ConvertPeriodToConversationPhase(Nekolpos.TimeSystem.DayPeriod period)
        {
            switch (period)
            {
                case Nekolpos.TimeSystem.DayPeriod.Morning:
                    return "Morning";
                case Nekolpos.TimeSystem.DayPeriod.Afternoon:
                    return "Lunch";
                case Nekolpos.TimeSystem.DayPeriod.Evening:
                    return "Evening";
                case Nekolpos.TimeSystem.DayPeriod.Night:
                    return "Night";
                default:
                    return string.Empty;
            }
        }

        private static bool TryResolveCurrentConversationPhase(IGameState state, out string phase)
        {
            if (state is StateMorning)
            {
                phase = "Morning";
                return true;
            }

            if (state is StateDay)
            {
                phase = "Lunch";
                return true;
            }

            if (state is StateEvening)
            {
                phase = "Evening";
                return true;
            }

            if (state is StateNight)
            {
                phase = "Night";
                return true;
            }

            phase = string.Empty;
            return false;
        }

        /// <summary>
        /// Resources/TalkData/ フォルダ内にある .bytes ファイルを読み込み、メモリ上で復号する
        /// </summary>
        private string DecryptBytesAsset(string fileName)
        {
            Debug.LogWarning($"[DialogueEngine] .bytes 直接読込は無効です。{fileName} は ConversationDataManager から設定してください。");
            return null;
        }

        /// <summary>
        /// 単純な改行Splitではなく、ダブルクォーテーション("")で囲まれた文章内の改行（\n）は無視して、
        /// 正しくCSVの1行（レコード単位）で分割する堅牢なパーサー。
        /// </summary>
        private List<string> SplitCsvLinesRobust(string csvData)
        {
            List<string> lines = new List<string>();
            bool inQuotes = false;
            int startIndex = 0;

            for (int i = 0; i < csvData.Length; i++)
            {
                if (csvData[i] == '\"')
                {
                    inQuotes = !inQuotes; // クォートの中に入った、あるいは出た
                }
                else if ((csvData[i] == '\n' || csvData[i] == '\r') && !inQuotes)
                {
                    // クォートの外で改行文字を見つけたら、そこまでを1行として切り出す
                    if (i > startIndex)
                    {
                        lines.Add(csvData.Substring(startIndex, i - startIndex));
                    }
                    
                    // Windows(\r\n)の場合は1文字進める
                    if (csvData[i] == '\r' && i + 1 < csvData.Length && csvData[i + 1] == '\n')
                    {
                        i++;
                    }
                    startIndex = i + 1;
                }
            }

            // 最後の1行を追加
            if (startIndex < csvData.Length)
            {
                lines.Add(csvData.Substring(startIndex));
            }

            return lines;
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "true" || normalized == "1" || normalized == "yes" || normalized == "on";
        }

        private static bool ParseCallOnlyValue(string value)
        {
            if (ParseBool(value))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            return normalized == "callonly";
        }

        private string[] ParseCSVLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            StringBuilder currentVal = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '\"')
                {
                    inQuotes = !inQuotes; // クォートの中・外を切り替え
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentVal.ToString());
                    currentVal.Clear();
                }
                else
                {
                    currentVal.Append(c);
                }
            }
            result.Add(currentVal.ToString());
            return result.ToArray();
        }

        /// <summary>
        /// CSVのヘッダー名からインデックスを探す（大文字小文字・空白などを無視、指定した引数の順番を優先）
        /// </summary>
        private int FindColumnIndex(string[] headers, params string[] possibleNames)
        {
            // 第一引数にある候補ほど優先的に探す
            foreach (var name in possibleNames)
            {
                string cleanName = name.Replace(" ", "").Replace("_", "").ToLower();
                
                for (int i = 0; i < headers.Length; i++)
                {
                    string cleanHeader = headers[i].Replace(" ", "").Replace("_", "").ToLower().Trim('\uFEFF', '\u200B');
                    if (cleanHeader == cleanName)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
    }
}
