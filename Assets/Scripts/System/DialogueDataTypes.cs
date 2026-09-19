using System.Collections.Generic;

namespace Nekolpos.Data
{
    // ============================================
    // 会話エンジン用の列挙型・状態管理構造
    // ============================================

    /// <summary>
    /// AI用発言制御タイプ
    /// </summary>
    public enum SpeechControlType
    {
        Random,       // 通常ランダム発言
        Sequence,     // 同じパターンが続いた順序発言
        RepeatEvent,  // 同じパターンが一定回数入力された際の特殊発言
        Call,         // 別Patternを呼び出す
        Return        // Call元へ戻る
    }

    /// <summary>
    /// パターン（ラベルハッシュ）ごとに保持する会話状態
    /// </summary>
    public class ConversationState
    {
        public int patternHash;      // 紐づくパターンのハッシュ
        public int repeatCount = 0;  // 連続で入力された回数
        public int sequenceIndex = 0;// Sequence がどこまで進行したかのインデックス
        public int sequenceResetAnchorIndex = 0; // 3日経過リセット時の復帰先
        public int lastSpokenDay = -1; // 最後にこの話題を発言したゲーム内日数
        public readonly HashSet<string> spokenRandomPatternIds = new HashSet<string>(global::System.StringComparer.Ordinal);
        public string lastRandomPatternId = string.Empty;
    }
    // ============================================
    // 会話エンジンのデータ構造（暗号化バイナリから復元される）
    // ============================================

    /// <summary>
    /// 第2関門：正規表現と、そこから抽出されるラベル（意味キー）のデータ
    /// </summary>
    public class RegexPatternData
    {
        public string SourceRegex;
        public int LabelHash; // 「なでてほしい」等のラベルをそのまま持たず、ハッシュ値で保持（高速化＆解析防止）
        public int Priority;  // 判定の優先度（数字が大きいほど優先）
        public string Sensitivity; // SAFE, SUGGESTIVE, EXPLICIT 等（下品フィルタ用）
        public Nekolpos.System.TeachingCategory TeachingCategory = Nekolpos.System.TeachingCategory.None;
        public string TeachingEntryId;
        public string TeachingSeriesGroupId;
        public string TeachingDisplayName;
        public bool TeachingPromptDisabled;
        public bool TeachingRevisitDisabled;
    }

    /// <summary>
    /// 第3関門：特定のラベル（意味キー）に対して、どう反応するかの「台本」
    /// </summary>
    public class DialogueReactionData
    {
        public int LabelHash;      // どの言葉に対する反応か
        public string PatternID;   // 同一の会話（複数ウィンドウ表示）を束ねるグループID
        
        // 新・発言制御システム (Random, Sequence, RepeatEvent)
        public SpeechControlType SpeechControl = SpeechControlType.Random;
        public int Order = 0;          // Sequence進行順序
        public int RepeatCount = 0;    // RepeatEventトリガー回数
        public int RandomRepeatLimit = -1; // Sequenceへ移行する前にRandomを許す連続回数。未設定はエンジン既定値
        public string TargetPatternID; // Call先 PatternID
        public bool CallOnly;          // true の場合、通常抽選から除外して Call からのみ再生する
        public string ChoiceYesPatternID; // response_type=Choice で Yes 選択時の分岐先
        public string ChoiceNoPatternID;  // response_type=Choice で No 選択時の分岐先
        public bool SequenceProgressHold; // 3日経過後の Sequence リセット時にこの Pattern へ戻す

        public string IntentID;    // INTENT_AFFECTION など
        public string ReactionType;// REACT_CHILD_HAPPY など（ボディモーションの基）
        public string Topic;       // TOPIC_BODY など
        public string SourceRegexLabel; // __source_regex など、元の意味キー
        public string ChoiceQuestionJa; // question_ja など、Choice表示文
        public string ChoiceYesLabel; // Choice UI に表示する Yes 側ラベル
        public string ChoiceNoLabel;  // Choice UI に表示する No 側ラベル
        public string ActionId;    // response_type=Action/Event/Choice 時の外部処理ID
        public string TimedEventKey; // 死亡/Action/Event中に表示する SystemTimedEvent.csv の key
        public string Speaker;     // Cat/System など、内部呼び出し発話の話者
        public string NextAction;  // 旧互換: AskAgain, Repeat などの遷移指定
        public string TextJP;      // Nekomataが実際に喋る日本語テキスト
        public string ResponseType;
        public string Condition;
        public float WaitTime;
        public bool HideUI;
        public string Animation;
        public string EmotionChangeType;
        public int EmotionChangeValue;
        public string BranchResolver; // Choice 分岐先の解決元。BasicSystemDialogue など
        public Dictionary<string, string> RuntimePlaceholders; // {unknown_word} など、分岐先にも引き継ぐ実行時値
        public string TeachingCategory;
        public string TeachingEntryId;
        public string TeachingSeriesGroupId;
        public string TeachingDisplayName;
        public List<Nekolpos.System.DialogueLogQuestionLink> QuestionLinks =
            new List<Nekolpos.System.DialogueLogQuestionLink>();
    }

    /// <summary>
    /// 第3関門(サブ)：ReactionTypeに対応する「身体の動き・アニメーション」
    /// </summary>
    public class BodyMotionData
    {
        public string ReactionType; // 例: 警戒反応
        public string Actions;      // ears_back, pupils_dilate など
    }
}
