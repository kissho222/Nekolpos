# 通常会話のAuthoring / Runtime分離 調査案（Issue #63）

調査日: 2026-10-07。対象は現在のローカル作業ツリー（#59追記13〜17の未コミット実装を含む）。調査のみ。CSV・Runtime・Scene・Prefab・生成bytes・Migrationは変更していない。

結論: **Editor完成後に、階層Authoring JSON → 型付きRuntime DBへの段階移行を行う**。今は読取変換・比較設計までに留め、体験版/OBT前のRuntime切替を必須化しない。人間向けUIと機械向け保存の分離は可能だが、「CSVをJSONに置換」だけでは現在の暗黙ルールや二重読込を解消できない。

## A. 現行データ構造図・棚卸し

Unity 6000.3.10f1、URP17.3、UniTask、Yarn、Timeline、既存Editorテストを使用。新依存は不要。通常原稿は750行・41列・139種類のid。空regex_idは184行でRuntimeはregex_jpを意味キーへ代用する。非空regex_idは89種類。規定の文字正規化を模した静的集計では139意味キー・351Pattern群・257複数行群。Editorの(regex_id,id,pattern)群も351、今回の通常CSVではRuntime群をまたぐ複数idは0。**両者のキー定義は違うが、現在の通常CSVで351対351であり、即座に混線しているという意味ではない**。他原稿・複数ソース横断では別途照合が必要。

```text
TalkSource/TalkCSV/DialoguePreview.csv（制作正本）
  ├─ Editor: DialogueDataLoader -> DialogueEntry / DataSet
  │    ├─ DialoguePreviewWindow（regex_id + id + patternで束ねる）
  │    ├─ DialogueIdentity.json（source + 上記キー -> Editor GUID）
  │    └─ 関連CSV Repository（Topic / TimedEvent / Diary / Kanji）
  └─ CSVCryptoCompiler
       ├─ CSV文字列をAES-CBC暗号化 -> Resources/TalkData/*.bytes
       ├─ Topic正本をRuntime CSVへ同期
       └─ 独立Diary CSV + Identity -> JSON -> DialogueDiary.bytes
          ↓ ConversationDataManager（復号 / ソース選別）
          ├─ ConversationDataLoader -> RouteCatalog（別表現）
          └─ DialogueEngine: 同じ復号CSVを再パース
               -> RegexPatternData[]
               -> Dictionary<LabelHash, ReactionData[]>
               -> Patternで群化 / Orderで行ソート
               -> 代表行Condition / Repeat / Random / Sequence
               -> DialogueManager（行表示 / Choice / Action / Call）
```

現コンパイラの通常CSV処理は意味構造のコンパイルではなくテキスト暗号化である。独立Diaryのみ、既にAuthoring→Runtime JSON変換の先例がある。ConversationRouteCatalogが存在しても、現在のDialogueEngineの主経路はそのcatalogではなくGetActiveCsvSources→LoadReactionStyleCsvである。BuildPreviewPatternId/ParsePreviewSpeechControlの存在を主経路と誤認しない。

識別は次の別物を保つ。

- CSV id: 会話制作上の番号。1行固有IDではない（139値/750行）。Engineでは主に日記出典・教示fallbackに使う。
- regex_id: 意味キー。空欄はregex_jpにfallback。正規化後MD5先頭4バイトをLabelHashにする。ファイル名/idはハッシュに含まれず、複数ソースの同じ意味キーは同じバケットへ入る。衝突検証なし。
- pattern: 意味キー内の複数行再生群/分岐先。数値文字列は数値順、それ以外は初出順を保持。
- order: 群内発話順。Sequence群を進める順序そのものではない。SpeechControl空欄時はorderの有無がSequence推定にも使われる。
- Editor DialogueID: source + regex_id|id|patternへのGUID。現在は**Pattern群単位**であり、意味キー全体のIDではない。新DialogueRootのIDにそのまま1対1移さない。
- Topic Source/SourceKey: filename + id:regex_id（空欄ならid等）の制作出典。Patternを含まない。
- Route ID: ConversationDataLoaderがgroupId/regexId/pattern/order/type/actionから生成する別キー。EngineのLabelHash/Patternとは異なる。
- teaching EntryID/SeriesGroupID: 既読やカテゴリ進行の別キー。CSV番号からfallbackする場合もあり、既存セーブ互換を維持する。

### 全41列の分類

R=主Engine/Managerで使用、P=別Route読込で保持・使用、E=Editorで保持/編集、L=互換列。単位は実効単位。全列はEditor往復保存で保持される（専用プロパティまたはAdditionalFields）。件数は非空セル数。

|列|非空件数|使用区分|実効単位|意味・重複・互換|
|---|---:|---|---|---|
|id|750|R/P/E|出典/制作会話|日記・教示fallback。群内で重複、行固有IDではない|
|input|750|P/E|入力説明/群|主Engine入力照合はRegex。別Route metadata/input、Editor/漢字検査|
|regex_jp|750|R/P/E|Matcher/群|主Engine照合、空regex_id時の意味キーfallback|
|regex_zh|452|P/E|Matcher/群|別Route metadataとEditor保持。主Engine照合で未使用|
|regex_en|452|P/E|Matcher/群|同上|
|output_ja|749|R/P/E|行|主Engine TextJP、変数展開後表示|
|output_zh|256|P/E|行|別Route locale解決。主EngineはJP固定|
|output_en|256|P/E|行|同上|
|alt|208|P/E|行|metadata.altText/未知列保持、主Engine未参照。独立候補へ勝手に変換しない|
|regex_id|566|R/P/E|意味キー/群|空はJP Regex。hash/正規化するため表示名とStableIdを分離すべき|
|pattern|750|R/P/E|再生群/分岐|群の参照先と候補順を兼務|
|order|750|R/P/E|行|群内順。空SpeechControl時に有無で推定も行う|
|response_type|750|R/P/E|行/主に群終端|Normal/Reaction/Choice/Action/Event。中間行と終端で実効差|
|action_id|84|R/P/E|外部終端|Action/Event/空Choice等の専用経路。全行共通属性ではない|
|condition|51|R/P/E|代表群、経路により行|通常候補は先頭代表、postActionは行filter。空=無条件|
|SpeechControl|676|R/P/E|代表群、Call/Returnは行|Random/Sequence/RepeatEvent/Call/Return。response_typeとは直交|
|emotion_change_type|180|R/P/E|群先頭effect|Managerの先頭心理変化。各行繰返し適用しない|
|emotion_change_value|750|R/P/E|群先頭effect|値-3..3へclamp、既存単位へ変換。0=なし|
|WaitTime|750|R限定/P/E|行|通常Managerでは読み捨て、死亡専用等の待ち時間/Editor previewで使用|
|HideUI|750|P/E/R読込のみ|行|主通常Managerは読み捨て。Editor previewとの実効差を保持|
|Animation|10|R/P/E|行/外部演出|timeline prefixは行開始で専用Controllerへ。任意名を自動再生すると誤る|
|Priority|750|R/P/E|Matcher|重複Regexは最初の登録値。照合最長一致等の既存tie ruleも保持|
|call_only|750|R/P/E|群access|どの行にもtrueがあれば群除外、代表RepeatEventは例外|
|choice_yes_pattern|19|R/P/E|Choice終端の枝|新列優先、空のみyes_next fallback|
|choice_no_pattern|61|R/P/E|Choice終端の枝|新列優先、空のみno_next fallback|
|sequence_progress_hold|750|R/P/E|Sequence代表|3日reset復帰anchor。行途中の指定は群選択で無効|
|RepeatCount|750|R/E|RepeatEvent代表|閾値。明示Sequence/RandomならRepeat選択に不使用|
|question_ja|51|R/E|Choice終端|表示質問、空時の既存fallback|
|question_en|6|E|Choice終端|主Engineは未参照、AdditionalFields等で保存|
|question_cn|6|E|Choice終端|同上、zhとの列名不統一|
|choice_yes_ja|10|R/E|Choice枝|Yesラベル、空時既存UI fallback|
|choice_yes_en|0|E|Choice枝|主Engineは未参照|
|choice_yes_cn|0|E|Choice枝|同上|
|choice_no_ja|10|R/E|Choice枝|Noラベル、空時既存UI fallback|
|choice_no_en|0|E|Choice枝|主Engineは未参照|
|choice_no_cn|0|E|Choice枝|同上|
|yes_next_pattern|21|R/L/E|Choice枝|新参照が空の場合のみ使用。重複意味だが値のあるfallbackは保持必須|
|no_next_pattern|66|R/L/E|Choice枝|同上|
|random_repeat_limit|14|R/P/E|意味キーselection policy|成立代表の最初の非負値、未指定-1/既定3。0も有効|
|timed_event_key|11|R/E|外部command context|専用死亡等/Action経路で解決。値だけで汎用トリガーにならない|
|target_pattern|6|R/E|行Call/外部終端継続|Call先/Action後/死亡後Pattern。Choice枝参照と異なる役割|


補足: intent/reaction_type/Speaker/NextAction/ReactionIcon/TeachingCategory/EntryID/SeriesGroupID/DisplayName/teaching_prompt_disabledなどは共通ローダーが扱えるが**この通常CSVの41列にはない**。ReactionIconはEditorで追加可能な任意の行属性。IntentID/ReactionTypeの現在の通常データをregex_idから勝手に生成しない。入力正規化は既存KanjiReadingDictionary/ProjectDictionaryReadingConverterとUnknownWord/Teaching経路を共有している。

### 「1行」と複雑例

1行は基本的に1発話兼再生指示。Random候補は1行ではなく1Pattern群（複数ページを含む）。Sequence要素も1Pattern群。1入力意味キーには複数Patternがあり、反復/条件でその群を選ぶ。Choiceは選ばれた群の終端行に紐づく分岐で、分岐先も同じ意味キーのPattern群。会話全体の行数は固定でない。

現在のid00920 / regex_id=talking_mechanism（行607〜610、台詞本文は転記しない）:

```text
Matcher: talking_mechanism
Selection: Sequence（Pattern数値順、call_only群を除外）
  Pattern 1: order1 / Choice -> Yes:2, No:99
  Pattern 2: order1 / Normal
  Pattern 3: order1 / Choice -> Yes:2, No:99
  Pattern 99: order1 / Normal / call_only=true
Choice -> 2/99 は直接群再生。Sequenceの抽選/進行を再実行しない。
```

別例id00600のPattern4は行380(order1 Normal)と390(order2 Event/death:vore、timed_event_key=action_vore1_result、target_pattern=7)。前半の発話と終端の外部処理は別属性であり、Actionを全行へ複製する新形式にしない。

## B. Random / Sequence / Choice / Repeatの実挙動

|機能|保存・実行|状態/保存|暗黙条件・Editor解釈|
|---|---|---|---|
|Random|SpeechControl=RandomのPattern代表から群抽選。未発話Patternを優先し、全消化後は直前Patternを避ける。残ったpool内は等確率|Engine ConversationStateのspokenRandomPatternIds/lastRandomPatternId。メモリのみ|単純な毎回独立等確率ではない。Editorは行/Pattern編集と入力照合を提供するがRuntime履歴の同一再現器ではない|
|Sequence|代表がSequenceの群をPattern順に進む。RandomがあればrepeatCount<=limit中はRandom優先。limitは最初の非負値、既定3|sequenceIndex/resetAnchor/lastSpokenDay。メモリのみ、Engine破棄/Resetで消える|末尾保持。末尾がAction/Event/Choiceなら前の通常群へ戻して外部処理連打を避ける。通常群がなければ末尾。3ゲーム日空くとholdの復帰位置へ。日記Sequenceのループ/永続保存とは別|
|RepeatEvent|同じ意味キーの連続入力数を増やし、最優先で群選択。初回は対象外、threshold=max(2,RepeatCount)。完全一致優先、最大閾値以上は最大群を再利用|repeatCount/lastPatternHash。メモリのみ|話題変更でrepeat=1。threshold同値は候補順依存。RepeatEvent-onlyなら初回候補なし。RepeatCount値だけ付けても明示SequenceをRepeatEventへ変えない|
|Choice|主に終端行response_type=Choice。ChoiceYes/No優先、空ならyes/no_next互換値。同じLabelHash内のPatternを直接取得し全行再生|Managerの現在群/Call stack、永続Sequence状態とは別|分岐先のRandom/Sequenceを抽選しない。通常分岐は代表Conditionを再評価する経路でない。空分岐はActionへfallbackし、未指定/失敗時は既存入力復帰等。UIは新旧列を同期可能|
|Call/Return|行進行時にtarget_patternを直接呼び、次行復帰位置をstackへ格納。Return/群末尾で戻る|Manager内stack、深度上限|通常抽選候補はRandom/Sequence/RepeatEventのみ。Call-onlyやReturn-onlyを直接入力候補と期待しない|

call_only=trueは群内のどれか1行でも指定があれば群を通常候補から除外する。ただし**代表SpeechControl=RepeatEventなら除外の例外**となる。ConditionはOrder最小の代表行で通常抽選群を絞る。行ごとのConditionを常に実行するわけではない。敗北条件文字列を含む成立候補があれば、その候補群を優先する特例もある。Action後targetの再生ではManagerが別途行Conditionをfilterするので、通常Choiceとは条件評価単位が異なる。

通常行は最後の行でHandleResponseTypeを実行する。中間行のAction/Choiceを一律終端として解釈しない。ただし空本文Actionの表示省略入口、Call/Return、死亡などに専用処理があり、コンパイラには終端と行指示の両方が必要。心理変化は最初の行の値を1回適用（値を-3..3へclampして既存単位を乗算）。再生履歴の「前の会話Repeat」はRepeatEventと別であり、Action/Event/Choice即時Actionの副作用を抑える。

### 複合動作の成立範囲

|組合せ|現在の可否・意味|
|---|---|
|Random + Condition|成立。代表Conditionが成立した群だけ抽選。その他行の条件を個別抽選に使わない|
|Sequence + Condition|成立。ただし成立候補だけでsequenceIndexを解釈するため、条件変化で指す群がずれる可能性。新設計で勝手にstable cursorへ修正しない|
|Sequence + Repeat|同一意味キーにSequence群とRepeatEvent群を併設可能、Repeatが優先。単一代表のSpeechControlが両方になることはない|
|Choice + Sequence / Random|群選択側はSequence/Random、終端がChoice。Choice先は直接再生であり、そこから新たなRandom/Sequence群選択が始まるわけではない|
|Choice + Action|成立。分岐先があればそちら、空分岐なら即時Action。単一ActionIdをYes/No別のActionに自動展開するのは挙動変更|
|Choice + TimedEvent|Action/専用死亡等の実行経路で成立。Choice行へtimed_event_keyだけ置いても汎用的に時間が進む保証はない|
|Random + TimedEvent / Repeat + TimedEvent|選ばれた群終端のAction/EventがTimedEvent経路を実行する場合に成立。キー列単独はトリガーではない|

明確な禁止型バリデーションは現在限定的。「列が同居できる」ことと「全組合せが実行される」ことは違う。初回Repeat-only、通常抽選のCall-only、Choice先での再抽選、通常会話WaitTime/HideUIの全面適用を成立済み仕様として移行しない。

### 実データ上の曖昧箇所

- 同一群の代表向け属性差異を3箇所検出: id00470/Pattern6のsequence_progress_hold(false/true)、id00600/Pattern3と4のRepeatCount(4/0、5/0)。前者はRandomの後続行なので現状holdとして効かず、後者は明示SequenceなのでRepeatEventにならない。値は消さず、LegacyMetadataに保持し、現行の実効代表値と分ける。
- Choice新旧参照列の非空同士の不一致0。空の新列と非空旧列はfallback対象なので不要と判断して消せない。
- 明示非ゼロChoice/targetの同一意味キー内参照切れ0。ただし行276と281のYes互換値0は対応Pattern0なし。既存回帰テストは0をsentinelとして除外するが、主Managerは空欄だけを特別扱いし、文字列0を直接参照する。両行はresponse_type=Normalであり通常Choice経路で現在発火する不具合とは断定しない。移行では「潜在的な未解決参照」とし、0を無条件終端へ変換しない。
- 74行のSpeechControl空欄はorderが全750行にあるため、主EngineではSequenceへ推定される。Editorや別Routeの空欄defaultと混同しない。
- Order同値、代表値の重複、複数ソースの同ラベル、全CSVのalias/Unicode正規化は移行前の追加検査が必要。今回通常CSVの集計は主Engineの文字正規化を模した静的検査で、実Engine全ソースを起動した証跡ではない。

## C. 現行CSVの技術的問題一覧

1. Matcher/Input/Priorityなど群属性が750行へ繰り返される。Regex登録は(labelHash,正規化Regex)で重複排除し、最初の登録行のPriority/Teaching情報が効く。同じRegexへの後の高Priority指定は上書きにならない。
2. Editor/Topic/Diary/Route/Engineでキー粒度が異なる。名称変更・CSV移動にIdentity/Topic/教示既読まで連動が必要。32bitハッシュ衝突と複数ファイル同一ラベルの統合が明示されない。
3. 行に置かれたCondition/SpeechControl/Repeat/心理変更が実際には代表単位。終端response_type/Actionと最初の行の心理変更、全行Timelineが混在する。
4. orderは群内順でありながらSpeechControl推定も兼ねる。空欄、0、false、未指定-1の意味が異なる。Pattern群の順序には数値名と初出順が混在する。
5. Choice新旧参照列と多言語表記(question_cn対output_zh)が並存。無効な組合せも平坦な行へ保存できる。
6. 保存データのidは一意でない。行IDはなく、Orderを変えると生成Route IDまで変わる。Editor GUIDもPattern群なので意味キー全体の永続IDではない。
7. RuntimeはCSV形式/filenameから読込対象・TeachingCategoryを推定する。通常、心理、内部、教示、専用形式のスキーマ境界がコードへ散在する。
8. 同じCSVをRouteCatalogとEngineが別々に読んでいる。主EngineはJP Regex/output/question/choiceだけ参照し、別Route側のEN/ZH読込が存在しても主自由入力会話の多言語対応完了とはいえない。
9. altは別RouteのmetadataとEditorに保持されるが、主Engineの通常発話fallbackには使われない。WaitTime/HideUIも通常ラインでは読み取るだけ、Editor preview/死亡Waitなどとは用途差がある。
10. Runtime入力ごとに群化/ソート/Regex評価/Condition文字列解析を行う構造。起動・入力時間を未測定なので性能障害とは断定しないが、Compile時に固定部分を解決できる。
11. 暗号化処理は通常CSVの分岐/群意味を検証していない。全入力を暗号化してから出力するが、複数bytes書込は単一トランザクションではなく、途中IO失敗で生成物の世代混在余地がある。
12. Editor本体SaveToPathはバックアップ後File.WriteAllText、関連Repositoryは原子的書込＋外部更新検出＋公開失敗rollback。複数ファイル保存保証は同一でない。新Authoring保存では正本保存とRuntime公開を別ステップにし、生成manifestで一世代を識別する。
13. 新機能ごとに列とalias処理が増え、Unknown列往復保存も必要。入力/UI/Runtimeの重複意味が保守コストになっている。

## D. 再設計候補比較

以下は設計上の評価。ロード速度・サイズ・WebGL性能のベンチマークは今回実施していない。

|案|概要|長所|短所|移行難易度|推奨度|
|---|---|---|---|---|---|
|A|正規化CSV複数表|型/責務分離、既存CSV処理再用、表単位diff|外部キー/多表transaction/横断mergeが増え、Runtime joinが必要|中〜高|Authoringとしては可能、単独Runtime案には非推奨|
|B|階層JSONをRuntimeも直接読む|Variant/Choiceを明示、会話単位diff、未知情報保持|起動時の解析/索引構築が残り、Authoring詳細を配布する|中〜高|CのAuthoring形式として推奨|
|C|Authoring DB -> Build Runtime DB|編集モデルと検索モデルを分離、検証/索引生成、デバッグ出典map|Compiler/Loader/二系統比較が必要|高（段階化可能）|最終形として推奨|
|D|現CSVをEditorで抽象化|移行不要、既存互換、現#59を活用|暗黙規則・二重parser・列肥大は残る|低|今の制作継続策として推奨|

|比較軸|A|B|C|D|
|---|---|---|---|---|
|Runtime読込|多表parse/join|JSON parse/index|固定配列/索引を直接復元、改善候補|既存CSV parse/群化|
|WebGL|Unityで読める実装が条件、CSV IO増|既存JsonUtility相当の対応範囲を守る|SQLite/動的コードを使わずDTO/配列、実Buildで検証|既存経路|
|Buildサイズ|重複減、表/キー増|名前/構造のoverheadあり|locale textと構造を分離、結果は実測|現状基準|
|暗号化|複数表を一bundleへ|UTF8 JSONを既存AES経路へ|version付き生成bundleを既存AES経路へ|既存CSV暗号化|
|Git diff|表ごと、横断参照は追跡要|会話単位の整形JSONは追いやすい|Authoring JSONをレビュー、生成物は再生成|長い行/共通列変更が大きい|
|Merge conflict|参照更新が多表へ波及|1会話1ファイル/固定順で局所化|B同様、生成物はmergeしない|同一巨大CSVに集中|
|Migration|joinのキー設計が必要|階層化＋互換ルール移植|B＋Runtime parityが必要|不要|
|Debug|複数表/行を追う|StableId/ノードを追う|配布内番号→StableId→SourceMapで追う|文字列PatternとCSV行を追う|
|Editor実装量|多表transaction/参照UI|階層モデルadapter|B＋compiler/diagnostics|少ないが複雑さを抱える|
|多言語|Text表追加が自然|LocaleMap/TextKeyが自然|TextKey→locale bundle、明示fallback|列追加/alias/主Engine差異|
|将来拡張|表追加|version/type追加|Authoring拡張とRuntime opcode変更を分離|列/判定の追加|
|既存互換|legacy import adapter必須|同左|legacy behavior policyと比較が必須|最高|
|Codex編集|キー整合をvalidatorで補助|小ファイル/型付きschemaで扱いやすい|仕様/validator/sourceMapが揃えば最良|誤列/群粒度を理解する負担大|

Cは保存形式の名称ではなくパイプラインであり、Bと排他的でない。推奨は**BでAuthoring保存、CでRuntime生成、移行中はD維持**。Aを中間で全面導入する必要はない。Editor用SQL DB/SQLite追加も不要。

## E. 推奨内部データモデル

汎用無制限グラフより、現行の「入力意味キー→群選択→複数行再生→外部終端」に合う型を使う。以下は設計案で実装していない。

```text
AuthoringProject(version, schemaVersion)
  DialogueRoot(stableId, displayName, sourceCategory, legacyLabel)
    Matchers[] (locale, regex, priority, discoveryRef)
    SelectionPolicy
      LegacyHybridPolicy
        RepeatOverrides[] (threshold, guard, segmentRef)
        RandomPool[] (guard, segmentRef)
        Sequence[] (guard, segmentRef, resetAnchor)
        randomThreshold / noRepeatPolicy / staleDays / tailReplayPolicy
    Segments[] (stableSegmentId, legacyPattern, access, selectionGuard)
      Lines[] (stableLineId, textKey, presentation, call/returnDirective)
      EnterEffect / EndCommand
        End | Choice(options[]) | Action(actionRef, afterSegment)
        Event(eventRef, timedEventRef, afterSegment)
    TopicRefs / DiaryBindings / LegacyMetadata
  TextCatalog: textKey -> localeText / fallbackPolicy
  Shared refs: Action, TimedEvent, Diary, Topic, Teaching, Kanji

RuntimeBundle(version, contentHash)
  Matcher[] -> dialogueIndex（priority / tie policy / source map）
  Dialogue[] -> selectionPolicyIndex
  Policy[] -> Repeat / Random / Sequence typed pools
  Segment[] -> firstLine/count, guardIndex, terminalIndex
  Line[] -> textIndex, presentationIndex, directiveIndex
  Choice[] -> typed options { targetSegmentIndex | externalCommand | end }
  Condition[] -> validated expression/AST（既存評価意味を保持）
  Commands[] -> kind + validated externalRef + continuation
  LocaleText[] / SourceMap / LegacyKeyMap
```

Random/Sequenceは「行Type」ではなく選択policy/node、Choiceは終端node、Repeatは優先override node。4つを排他的な単一enumへ押し込めない。LineとSegmentを分離し、Sequence順とLine順を別配列で明示する。Runtime参照は配列index、配布間安定性はStableId/LegacyKeyMapが担う。32bitラベルハッシュだけを唯一IDにしない。

初期Runtimeコンパイルは既存ReactionData群に近いDTO＋明示索引でよい。独自バイナリopcode化は計測後に判断する。JSONでもCompile/Validateして解決済み参照・固定配列を出すならCであり、binaryが前提ではない。Regexは実行時文字列評価が残るので、DB変更だけで入力照合全体が高速化するとは保証しない。条件AST化も既存OR/否定/数値/敗北特例の同等性を確立してから行う。

LegacyHybridPolicyは3日reset、未発話優先、repeat初回除外、call_only Repeat例外、条件filter後のSequence index、終端外部flow時の再生先まで保持する。新会話用の素直なSequencePolicyを後から設ける場合は別versionとして明示し、旧データへの無断適用を避ける。

## F. Editorとの責務分離

```text
Authoring Editor: 作者の会話/候補/分岐/条件/外部連携編集
    ↓ stable model adapter / save transaction
Authoring JSON: 型・GUID・多言語・編集メモ・legacy provenance
    ↓ Compiler: schema検査、参照解決、互換policy、索引、source map、暗号化
Runtime DTO bundle: 実行に必要な配列/条件/本文/解決済み参照
    ↓ Loader / DialogueEngine: 入力照合、候補選択、メモリ進行
DialogueManager: UI・副作用・Timeline/Yarn/Action/TimeFlow呼出
ProgressSaveStore: 保存が必要な教示/日記/関係性、明示したversion付き進行
```

既存DialogueDataLoaderにCSV reader/legacy importerを残し、新Authoring adapterを並設。Windowの全UIを先に書き換えず、モデルを差し替える境界を作る。RelatedRepositoryはshared Topic/TimedEvent/Diary/Kanjiの正本を引き続き担当し、DBへ値を二重コピーしない。Compilerでstable参照だけ解決する。

通常会話・Condition・Choice・Random・Sequence・Repeat・Action・TimedEvent・Diary・TalkTopic・Kanjiはいずれも現在のUIから編集可能なモデルへ移せる。Choice作成でChoice終端を生成、候補追加でRandom/Sequence policyへ追加、分岐先は会話名/ノード選択、Pattern/id/regex_id/orderは自動生成またはlegacy read-onlyへ。多ページ追加と候補追加は別操作にする。Repeatは「連続何回で切替」、通常Sequenceと日記Sequenceは別欄。内部キー検索・移行警告は開発情報へ残す。UIを唯一の通常入口にしても外部Codex編集用schema/validator/CLIを備え、未知フィールドを消さない。

既存CSVCryptoCompilerの入口・環境変数・Build hook・Topic同期を維持し、通常会話用SemanticCompilerを内部から呼ぶ方向を推奨。他の専用CSVの暗号化を同時置換しない。全bundleを一時生成→検証→世代manifest公開、前世代rollbackを設計し、暗号化成功を意味検証成功と混同しない。復号可能な実行クライアントに配布するため、暗号化はAuthoring構造の設計理由そのものではない。

## G. Migration方針

手作業で台詞を作り直す必要はない。**読取専用CSV importerで自動変換可能**。ただし曖昧行を作者意図で「修正」せず、実効挙動と元値を両方保存する。

1. 全正本・Identity・Topic・Diary・教示キー・生成物をhashでsnapshot。#59保存系を確定し、正本を固定する。
2. Runtimeと同じalias/文字正規化/blank推定を使用し、まず有効意味キー→Pattern群→Order行へ束ねる。初出順とソース読込順、Priority重複、同Orderを保存する。照合の優先規則は最長一致→Priority→明示Regex優先で、Priorityのみの単純sortへ変更しない。
3. 現Editor GUIDをSegment IDとして保持。新Root IDとLine IDを別に発行し、永続mappingへ固定。再変換しても同IDとなる冪等性を検証。
4. 新旧Choice値を優先規則で実効参照へ解決し元列もLegacyMetadataへ保持。0、参照切れ、異なる代表値、未知条件/typeはdiagnosticとして出し、要判断データを黙って捨てない。
5. ActionId/TimedEventKey/targetを既存registryへ接続。Timeline文字列、Yarn node、death/Teaching等special commandは型付き参照へ分類し、未知外部IDは旧adapterへ残す。外部副作用をMigration中に実行しない。
6. TopicのSource/SourceKey、Diary linksの既存Editor GUID、教示EntryID/SeriesGroupIDをLegacyKeyMapで保持する。既存ProgressSaveStoreキーは改名しない。通常会話Sequenceは現在非永続であり、存在しない旧セーブcursorを移行する前提にしない。稼働中メモリcursorの移行が必要なら別の明示snapshot adapterを設計する。
7. CSV→Authoring→legacy exportを比較し、本文/翻訳/Regex/unknown列/順序のlossなしを確認。直書きを禁止する前に復旧/export手段を残す。

同一挙動の証明は文字列diffだけでは不足。旧/新selectorを同じGameState・時間・入力・RNG抽選番号・履歴snapshotで動かし、Match label/priority、選択Segment/Line順、Choice target、Call/Return、心理差分、Action/TimedEvent/Diaryの実行**指令trace**を比較する。副作用はspyで捕捉して二重実行しない。Randomはseedだけでなく抽選pool順とRNG呼出数も比較する。Sequence境界、話題変更、3日reset、hold、Condition変化、Repeat1/2/threshold/最大超過、全locale、未知語/教示/Title取消・夜境界のケースを含める。

参照モデルの差分検証→既存正規Unity回帰→MainScene統合→WebGL/PC/モバイルの順にゲート。既存回帰が通るだけではalias0等を網羅しないため、今回の静的diagnosticを追加fixtureにする。速度/サイズは旧CSVの復号/二重parse/初期化/入力選択時間、GC、bundleサイズを同機材で計測してから判断する。

## H. 段階移行案

|Phase|作業|切替ゲート / rollback|
|---|---|---|
|0|#59保存/UIをレビュー確定。キー/暗黙仕様台帳と全原稿validatorを整備|正本/既存セーブ/演出を変更せず、曖昧データを一覧化|
|1|Authoring DTO/schema＋読取CSV importerだけ。Editorで旧/新表示比較|本文/参照/unknown値のloss0、GUID冪等性。保存は旧CSVのみ|
|2|Compilerが新Runtime bundleをshadow生成。旧Runtimeを正としてtrace比較|全群/境界/多言語/外部指令parity。新生成物は配布未採用|
|3|Loader/Engine切替を開発flagで選択、旧loader維持|正式回帰＋実機/Build＋性能計測、旧flagで復帰可能|
|4|Authoring JSON保存を正本化、CSV書込停止、readonly import/export保持|既存全会話自動変換、作者確認、Topic/Diary/教示キー互換|
|5|互換期間/リリース実績後に旧コードを削除|復旧手順・保存version・参照mapを残し、不要部分だけ削除|

今回Phase0〜5はいずれも実装していない。Graph Editorの新設・全Timeline/Actionの統合は移行要件にしない。

## I. 今やる価値と結論

推奨区分は**「Editor完成後にやるべき」**。当面Dを継続し、最終目標B+Cを設計する。750行/351群/139意味キーで、既に41列・2parser・キー粒度差があり構造分離の価値はある。一方#59は最終作者レビュー前、#60時間/日記・#62身体反応も最近連携したばかりで、体験版/OBTの中心体験にRuntime移行を重ねるリスクが大きい。

英語/中国語は現在452行にRegex、256行に出力があり、多言語TextCatalogと主Engineとの差異解消は将来価値が高い。ただし発売日、OBT締切、今後の会話増加量、実際の不具合発生率/制作時間、負荷数値は未確認。数字を推測して投資判断しない。安定したEditorで追加制作を進めながら、互換importer・validator・比較traceへ限定投資するのが妥当。締切が近ければPhase3を体験版/OBT後へ送り、データ量/制作エラーが増えればPhase1/2を前倒しする。

大型置換自体を目的にせず、会話が詰まらないこと・既存外部反応・日記/時間の安全性を採用基準とする。

## 根拠と検証範囲

主なコード位置（現在の作業ツリー）:

- Editor/DialogueDataLoader.cs: LoadFromPath/SaveToPath（読込/未知列/保存）
- Editor/DialogueAuthoringIdentity.cs: GroupKey/Bind、DialoguePreviewWindow.cs: BuildSavedPatternKey/RebuildPatternGroups/Choice UI
- System/DialogueEngine.cs: LoadConversationDataFromManager(2759)、LoadReactionStyleCsv(3290付近)、DetermineReaction(2139)、TryBuildReactionGroups(2614)、FindRepeatEvent(2322)、GetHybridRandomSequenceReaction(2362)、SelectRandomReaction(2439)、ResetConversationStateIfStale(3667付近)
- System/DialogueManager.cs: ProgressNextLine(438付近)、PrepareLineDirectives(465)、Choice分岐(554〜754)、HandleResponseType(835)、postAction Condition(1860〜1956)、心理変更(2313)、Call/Return(2480〜2572)
- Conversation/Data/ConversationDataManager.cs: GetActiveCsvSources(111)、ConversationDataLoader.cs: ParseDialoguePreviewRoute(637付近)
- Editor/CSVCryptoCompiler.cs: CompileForBuildOrThrow/CompileDirectoryOrThrow。Editor/DialogueDiaryAuthoring.cs: BuildDocument、System/DialogueDiaryCatalog.cs: Record
- Editor/TalkTopicMasterGenerator.cs: BuildSourceKey(445)、Editor/DialogueRelatedCsvRepository.cs: Save
- Conversation/Editor/ConversationDialogueCsvRegressionTests.cs: Choice検査は0参照を除外
- docs/DialogueAuthoring.md、DialogueDiaryDecision.md、DiaryJournal.md、TimedEventResultUI.md、ProjectSettings/ProjectVersion.txt、Packages/manifest.json

実施: コード/仕様の読取、Python標準csvによる750行41列の集計・キー/代表属性/参照検査、禁止対象3779ファイルの作業前後SHA256比較（変更/削除/新規0、ProtectedVerification.jsonで確認）。静的検査JSON/スクリプト/ハッシュ証跡は C:/Users/shoho/.codex/tmp/issue63-20261007。
未実施: Runtime起動/新形式実装/Migration/Unityテスト再実行/Build/性能測定/外部Issue内容の追加取得。#59の143件成功は別作業の結果であり、新モデルの検証結果ではない。
