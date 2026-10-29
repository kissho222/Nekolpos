# 会話制作統合Editor

保存済み全原稿の静的診断とJSON/CSV/TXT出力は [制作データの静的品質チェック](ProductionDataChecks.md) を参照。

通常会話の「作者確認・診断」、診断からの会話ジャンプ、TimedEvent同key統合、Action分類は[会話診断UI](DialogueAuthoringDiagnostics.md)を参照。

## 原稿UI（Issue #59）

上部に日本語名の原稿選択と「英語・中国語を表示」（初期OFF）を固定し、中央を左の本文編集・右の関連アコーディオンへ分割。下部の行/Pattern移動・保存・入力照合テスト・IDジャンプは固定する。左右それぞれが独立スクロールし、通常の日本語本文編集は1100×760で全体スクロール不要。長文、多言語表示、詳細展開、関連レコードが多い場合は各ペインだけスクロールする。

左はPattern・応答・条件4枠・日本語本文。会話本文の入力欄は日本語・英語・中国語とも2行分の高さ（Editorの行高と内側余白から算出）とし、入力内容の文字数や改行数は制限しない。条件式/会話後の心理変化、分岐/選択肢→再生/遷移→詳細の順に折り畳む。開発情報は右列最下部へ配置する。パス・保存先・Editor DialogueID・既存グループキー・Backup・その他CSVを開く・Resources一覧・TextEdit Scene操作は開発情報へ移した。上部心理値の条件指定UIは表示しない。

### 原稿プロファイル

`DialogueAuthoringProfile` が表示機能を定義する。新規C#は同クラス、Window部分クラス `DialoguePreviewAuthoringLayout`、Editorテスト `DialogueAuthoringUiTests`。Runtime変更なし。

|原稿名|ファイル|関連セクション|
|---|---|---|
|通常会話|DialoguePreview.csv|時間経過・日記・話題候補・漢字読み|
|ネコ科|teacchFelidae.csv|漢字読み・教示設定|
|猫作品・シリーズ|teacchseries_dialogue_source_2201.csv|漢字読み・教示設定|
|身体|teacchTalk_body.csv|漢字読み・教示設定|
|猫の表現|teacchCatExpression.csv|漢字読み・教示設定|
|猫種|teacchCatBreed.csv|漢字読み・教示設定|
|好きな天気|teacchFavoriteWeather.csv|漢字読み・教示設定|
|心理反応|PsychologyReactionDialogue.csv|時間経過・日記・話題候補・漢字読み|
|内部会話|InternalDialogue.csv|時間経過・日記・話題候補・漢字読み|

全9原稿で既存conditionと心理条件を利用可能。教示はinputと実際にあるEntryID/SeriesGroupID/TeachingCategory/DisplayName/teaching_prompt_disabledだけを編集し、存在しない列は追加しない。共通形式の新規CSVは「その他の会話」として候補へ追加。BasicSystemDialogue/TimedEvent/OBTTutorial/Diary_Normalなど専用形式は共通会話として選択させず、従来の関連編集・開発情報から扱う。


### 更新仕様11・12（2026-10-06）

右列の「時間経過・日記」は「時間経過」「TimedEventに付随する日記」「独立日記候補（時間経過不要）」の小見出しで整理する。TimedEventのdiary_id/diary_kindは付随日記側で編集し、本文・実際の日記素材・通常草稿/特別Pending経路を確認できる。独立候補の保存先とRuntime経路は維持する。

time_minutesは「ゲーム内経過時間 [値] 分」、display_secondsは「結果表示時間 [値] 秒」と表示し、CSV内部列名・整数/有限数の検証・保存値を維持する。開発情報にSourceKey、内部ID、Runtime参照を含める。

事前調査: SystemTimedEvent正本43件のtime_minutesは120分3件・空欄40件、通常経路は0以下を120分補完。標準Actionは120分だがActionDataは個別TimeCost（既定60分）を扱う。散歩終了も共通イベント入口、通常自由入力は5会話ごと、教示は対象外0分。#60の夜境界停止、死亡/就寝の翌朝直行、Debugの次境界までの残分を保持する。通常制作向け「1時間帯進める」簡略案をIssueへ報告したが、固定120分と次境界移動は等価でないため、今回は可変整数入力とRuntimeを変更しない。

### 二段Condition

既存のカテゴリを維持した。A季節、B天気、C空腹、D時間帯、E性別、F発情、G敗北、H現在位置、I心理（心理値・代表心理）。各枠はカテゴリ→そのカテゴリの成立/否定を選ぶ。既存A〜Hの候補を削除していない。

Iは「心理値」「代表心理」を選ぶ。心理値は愛情/ドS/心配/敵対/従順/本能の70以上・70未満・100（18件＋なし）。代表心理は単独6・複合15（21件＋なし）、複合の以外は新規候補に出さない。旧60閾値・最大否定・通常状態等は使用中の枠だけ旧条件として保持し、既存CSVを自動変換しない。CSV列追加なし。旧 `!Rain` 等は否定候補、Hの `H: !Desk` は位置の否定へ往復する。4枠を超える条件や独自比較式は保持。複雑なORは直接条件式として維持し、二段選択変更で置換されることを警告する。

### 表示・状態

多言語チェックはEditorPrefsで共有・永続化。OFF時もEN/ZHの本文、Regex、選択肢、質問、時間経過/日記本文をモデルと保存CSVに保持する。

原稿別EditorPrefsへ安定DialogueID、RegexId/Pattern・行順/番号、会話/入力照合タブ、関連セクションごとの展開状態、各折り畳み、入力照合テキストを保存し復元する。旧単一セッションの選択状態はフォールバックで読み取る。通常会話→教示原稿→通常会話で前の位置へ戻る。同じRegexId/Patternの別会話を誤選択しないよう安定IDを優先する。会話本文の未保存変更は従来の破棄確認を維持し、共有関連CSV草稿は従来の独立保持を継続する。

右側の説明/警告はセクションヘッダーへ件数を集約し、Tooltipまたはバッジ押下による展開で確認する。展開時は短い説明を表示し、同じHelpBoxを重ねない。操作不能エラーはその場にも表示する。関連セクションは排他でなく複数同時展開でき、非対応機能は非表示。閉じたセクションを余白埋めのために展開しない。旧関連タブの保存状態は対応セクションへ移行する。


### 検証（2026-10-06）

更新仕様11・12の正規Unity Runnerは43/43成功・失敗0・Skip0（UI24件、関連制作統合16件、位置Condition3件）。複数同時展開/原稿別復元/旧タブ移行、閉じたヘッダーの日記参照エラー、選択肢→再生設定の配置、固定フッター、9原稿の未知列/EN/ZH保存、関連CSVの失敗時ロールバックを確認。実Playを含む既存制作統合回帰も成功。実画面で右列の複数展開と開発情報の下部配置を確認。最終Console Error0、Play=false/compiling=false/Scene dirty=false。初回・複数文字列filter指定のRunnerは未開始タイムアウト、1本のgroup正規表現で正規実行を確認。Domain Reload後のjob表示残留は正規XML/Play停止を確認し公式ClearStuckJobで解除。

前回の広い回帰96件中95成功で残ったexercise→chaseの参照欠落（id01280）は会話内容判断が必要な既知事項で、今回のUI変更では対象外。Runtime時間進行・台詞・CSV正本の編集操作なし。実Player Buildとユーザーの最終操作確認は未実施。

## 独立日記候補（Phase 6）

`DialogueDiaryAuthoring` は独立CSV2表とEditorのID対応表を照合し、既存暗号化コンパイラへJSONを渡す。Runtime `DialogueDiaryCatalog` は出典CSV/id/regex_id/patternの対応から候補を取得し、既存DiaryJournalStoreへ記録する。DialogueEngineは既存キーを維持して出典情報だけ保持し、DialogueManagerの会話開始で1回通知する。関連パネルから既存候補の共有・空候補追加・本文/種別/条件/有効フラグを編集し、2表を保存・Runtimeへ反映する。仕様と比較は `docs/DialogueDiaryDecision.md` を参照。関連設定のためのキャラクター本文は生成していない。

## 既存の日記経路（Phase 5）

関連TimedEventのDiaryId（空欄時key fallback）、通常草稿/特別Pending、表示本文と共通の日記素材を可視化する。設定編集は同一パネルのTimedEvent欄と連動。通常TimedEventの発生日・時間帯は暗転後かつ時間進行前であり、保存本文は実行時変数展開後。通常は選択時間帯の最新草稿、特別は発生順で保持。死亡専用フローはkindによらず特別Pending、timed_event_keyがDiaryId優先となる。専用keyを置くだけで日記記録されるとは表示しない。このPhaseでRuntimeの日記選択規則は変更しない。

## 共有漢字辞書（Phase 4）

input・regex_jp・関連Importだけを既存KanjiReadingDictionaryとProjectDictionaryReadingConverterへ通し、残った漢字を未登録表記として示す。元項目から無効・空Reading・読み競合・NeedsReview・現在Readingを区別する。本文・日記本文は検査しない。Surfaceを指定して既存共有項目を編集、未登録項目を空Reading/無効/要確認で追加する。読みは自動生成しない。保存は同じRepositoryと暗号化経路を使い既存共有キャッシュを更新する。最長一致・競合判定を独自に再実装しない。

## TalkTopic関連編集（Phase 3）

選択会話のSource/SourceKeyは既存Generatorのキー構築を共有する。関連パネルから未登録会話の空・無効Topicを追加、TalkTitle/Genre/Idea/Import/Keywords/Enabledを編集する。登録解除はEnabled OFFで行う。TalkIDとSource+SourceKey重複を拒否しPhase 0の正本同期を必ず使用する。Import検証は既存入力正規化と対象Regexで到達可能性を確認し、状態・優先度・他会話・変数展開による最終選択の差を警告する。

## TimedEvent関連編集（Phase 2）

関連パネルにaction_id経路とtimed_event_key専用経路を分けて表示し、既存イベントの全9項目を編集、新規空イベントを作成できる。既存keyは共有参照保護のため改名・削除不可。CSV・コードリテラルの参照検索と共有警告を表示（動的参照は検索対象外）。time_minutesが0以下の場合は通常Runtimeで120分補完される。専用経路ではAction側に依存する。

`DialogueRelatedCsvRepository` は既存CSVパーサーを共有し未知列、バックアップ、原子的保存、外部変更検出、公開失敗時ロールバックを担当。保存後は既存暗号化コンパイラで反映しSystemTimedEventCatalogのキャッシュを更新。関連CSVと会話本体は別保存で、通知にも明示する。

## TalkTopicの正本と同期（Phase 0）

正本は `TalkSource/Dialogue/TalkTopicMaster.csv`。`Assets/Resources/Dialogue/TalkTopicMaster.csv` はRuntime向けの複製であり、直接編集しない。
Generator、統合Editor、CSV暗号化・Build前処理は同じCsvUtilityの同期を使用する。外部で正本を編集した場合は `Nekolpos/TalkTopic正本をRuntimeへ同期` を実行する。
同期は一時ファイルから置換し、保存と同期の組合せが失敗した場合は正本を元へ戻して例外を通知する。

2026-10-06の移行ではRuntimeの既存217行と共有キーの既存値を維持し、制作元だけの雑談・散歩2行を追加。統合219行、有効なIdea/Import候補52件。移行前2ファイルは作業証跡フォルダーに保存。台詞の新規生成は行っていない。

新規Editorテスト `DialogueAuthoringIntegrationTests` は追加・更新・無効化・削除、同期失敗時のロールバック、正本とRuntime一致を検証する。

会話行はPattern内の最後の1行も削除できる。最後の1行を削除する場合は警告を表示し、ツールバーの「連携IDも削除」でTalkTopic、独立日記リンク、他の会話から共有されていない日記候補、Editor DialogueIDを保存時に一括削除するか選択する。共有参照される可能性があるTimedEvent/action_idの正本は一括削除しない。

### パターン追加と行選択（2026-10-10）

2026-10-12: 照合式の編集は現在のCSVの同一id全行へJP/CN/ENを共有反映する。Pattern・regex_idが異なっても同じidなら対象。別id・別CSVの行は変更しない。空idは識別不能のため編集中の行だけを対象とする。regex_id自体の編集は従来のPattern単位を維持し、regex_idだけの変更で照合式を共有上書きしない。

同日、idと照合式列を持つ正本CSV10件を監査。差異はDialoguePreview.csv/DialoguePreview_Integrated.csv各8 ID（00030/00160/00190/00350/00530/00540/00600/00660）、teacchCatExpression.csvの00320/00330、teacchFelidae.csvの00140、シリーズCSVの22340。JPの表記/選択肢差とCN/ENの空欄/非空差を含む。既存CSVの自動統一は行わず、詳細は`output/regex-id-consistency/audit.md`と`audit.json`へ記録した。

2026-10-12: 正規表現エラーを独立した上部警告から既存「作者確認・診断」へ統合。全原稿で利用でき、閉じた見出しにも正規表現エラー件数を表示する。各エラーの「この会話を開く」は未保存データの実際の行を参照し、再読込・破棄せず該当Pattern/行へ移動して詳細設定を開く。入力照合タブからも同診断へアクセス可能。既存の通常会話の保存済み診断と未保存を含む正規表現診断は対象を明示する。

2026-10-12: `teacchFelidae.csv`の空regex_id34項目・139行を英名の小文字snake_caseで補完（`tiger`、`snow_leopard`、`clouded_tiger_cat`等）。既存の53行のregex_idは維持。旧Runtimeが空regex_idから照合式をラベルとして生成していた教示履歴キーを、既存EntryID列へ139行明示して保持する。Editor対応表34件は同じGUIDで新regex_idへ追従。英名の確認先は[IUCN Cat Specialist Group](https://www.catsg.org/cats/livingspecies)、[ITIS Clouded Tiger Cat](https://www.itis.gov/servlet/SingleRpt/SingleRpt?print_version=PRT&search_topic=TSN&search_value=1290510&source=to_print)、[環境省 Tsushima leopard cat](https://www.env.go.jp/en/headline/405.html)。本文・照合式・数値ID・Patternは変更しない。

2026-10-11: 会話/入力照合タブの上部に「正規表現エラー N件」の警告を常設し、展開すると現在CSVの不正な照合式をID/regex_id/言語/構文エラー付きで表示する。検索入力・テスト実行は不要。JP/CN/ENの未保存編集を対象に、入力欄のフォーカス移動・制作ウィンドウからの離脱で再検証し、修正後はエラーを解除する。空欄は制作中として許可。JPは入力された式とRuntime正規化後の式の両方を構文チェックし、他言語を非表示にしても検証する。検証結果は照合式単位でキャッシュし、本文の自動生成・自動保存・強制書き換えは行わない。

2026-10-11: 原稿ドロップダウン右に現在CSVの項目一覧を追加。表示は `id | regex_id | input`、同じID/regex_idの複数Patternは1項目にまとめ、選択時に最初のPattern/行へ移動する。未保存編集を保持し、入力照合タブからも現在項目を切り替えられる。

入力照合はJP入力を既存NormalizeInput、JP照合式をゲームのロードと同じNormalizeTokenで正規化する。旧処理の不正な式1件で全検索を中断する動作を廃止し、当該式だけを飛ばして現在/他CSVの有効一致を継続表示。不正な式や250msのRegexタイムアウトは警告として表示する。同じPatternや別言語に有効な式があれば結果を維持する。最長一致・優先度・登録順表示は既存仕様を維持し、Runtimeの状態条件・教示解禁・特殊入力経路を完全再現するシミュレーターではない。

2026-10-10のID整形で、全正本CSVのid列を確認し、4桁の数値IDだけ末尾に0を追加した（シリーズCSVの177項目・667行）。TalkTopicのSourceKey135行とEditor識別対応表177件のグループキーも更新し、安定GUIDと教示のEntryIDは維持する。本文やregex_id、Pattern等は変更しない。

「新項目追加」は編集中の数値IDから10ずつ進め、現在のCSV全体（未保存行を含む）で未使用のIDを採番する。先頭ゼロの桁数を維持し、桁上がりは許可する。表記の異なる同じ数値IDも使用済みとして扱う。非数値ID・数値上限では追加せず通知する。regex_idはCSVプロファイルの固有接頭辞（シリーズは`series_`、身体は`body_`、心理反応は`PRD_`）だけを初期設定する。その他はCSV内の非空値が共通接頭辞を持つ場合だけそれを使用し、なければ空欄とする。旧項目に命名例外があっても、プロファイルの接頭辞を優先する。Pattern1/行順1、全言語の本文・照合式・input・分岐・関連参照が空の新項目へ移動する。既存項目のregex_idは変更しない。

会話本文上部の「新パターン追加」は、現在のID・RegexId・入力照合情報・条件・優先度・再生方式を引き継ぎ、同じID/RegexId内で未使用の正整数Patternを自動設定する。行順1、全言語本文が空の1行を作り、そのパターンへ移動する。分岐・Action・Timeline・日記参照や安定DialogueIDはコピーしない。既存の保存処理で新しい安定DialogueIDを割り当てる。

同じパターン内の行は上部の「1」「2」「3」…の横並びボタンで選択する。ボタン幅はウィンドウ幅と行数に応じて伸縮し、最小幅32を超えて収まらない分は横スクロールで選択できる。行追加でボタンも増える。各ボタンのTooltipに従来の行表示情報を保持する。「行追加」は現在のパターン内へ追加する従来動作を維持する。

## 安定DialogueID（Phase 1）

`DialogueAuthoringIdentity` が `TalkSource/Editor/DialogueIdentity.json` にEditor用GUIDと出典CSV・既存グループキーの対応を保存する。会話本体DataSetと別責務であり、Dialogue CSVやRuntimeキーを変更しない。同一会話の複数行はIDを共有する。Window内でキーを変更して保存した場合はIDを保持し、別グループの複製とSave Asには新IDを発行する。外部でCSVキーを改名した場合は自動推測せず新IDになるため、関連設定の再確認が必要。CSVファイル移動は対応表のsourceも同時に移動する（Save Asは移動ではなく複製）。対応表をVersion管理する。

`DialoguePreviewRelatedData` は既存Windowの関連パネル拡張箇所。専用形式を共通DialogueEntryへ変換するAdapterは追加していない。CSV本体の未知列保持・保存・バックアップは既存DataLoaderを利用。暗号化失敗を保存成功として表示しないよう、Window保存経路は例外を返すコンパイラ入口を使用する。

## 検証（2026-10-06）

正規Unity Runner127件中126成功・1失敗・Skip0、新規16件すべて成功。残る失敗は既存exercise→chaseの参照先欠落で、分岐内容の判断が必要。MainScene実Playで会話開始記録、複数行・リピートの重複防止、通常/特別の日記Calendar保存、時間不変を確認。暗号化18件成功、最終Console Error0。Windows/iOS/Androidの実Build・実機操作は未実施。

## 漫符機能の撤去（2026-10-08）

ユーザー判断により漫符機能を撤去。会話制作ウィンドウの選択欄、表示処理、気づき表示・付属SE・追加待機、設定画面の表示切替を削除した。旧ReactionIcon/reaction_iconデータはCSV・Compiled Bundle互換性のため保持するが、表示には使用しない。

## 更新仕様13〜17（2026-10-07）

通常会話の正本は `TalkSource/TalkCSV/DialoguePreview.csv`。旧Integrated原稿は2026-10-12に使用終了の直接指示で削除し、原稿候補・Runtime自動読込・暗号化対象には含めない。TopicのSource参照も新名へ移行する。既存の新名DialogueIdentityは維持し、旧名IDを勝手に上書きしない。新正本へ改名前のID・挨拶条件・欠落分岐修正29セルを追従し、対応する新名Identityキーを移行した。本文と新正本のボール遊びActionは保持した。

EditorPrefsはプロジェクト内原稿の安定Profile ID（通常会話は `normal-dialogue`）をキーとする。外部CSVは正規化パスで独立管理する。保存直前と直後にUI状態を保存し、復元は選択解決より先に行う。各ペインの複数Foldout、選択、検索は維持する。関連Repositoryの監視基準は公開/Asset refreshより前に更新し、草稿復元で自保存を外部変更として扱わない。本当の外部内容変更はバイト比較で拒否する。固定保存は読み込んだ関連草稿も保存する。通常形式の読込直後にSavePathが空だった既存不具合を修正し、読込元CSV自身を自動保存先とする。派生Grouped形式は従来の保存先指定を維持する。

時間経過表示と日記本文は独立したVariant集合。既存 `ja/en/zh` を表示Variant1、新規 `diary_ja/diary_en/diary_zh` を日記Variant1とし、2件目以降だけ `display_variants` / `diary_variants` 列へJSON `{ "items": [{ "ja": "...", "en": "...", "zh": "..." }] }` で保存する。同一keyの行を増やさない。追加/切替/削除は集合別、最後の1件の削除は禁止。EN/ZH非表示でも値を保持する。notesはRuntime未参照の制作メモのため通常UIから除外し、CSV値を保持する。

旧単一本文はVariant1へ読込。専用日記列の無い旧データは表示本文をコピーする。既存43イベントは既存本文だけをコピーして移行し、以後独立編集する。明示diary_idは保持し、旧空欄IDは従来fallbackと同じkeyへ補完する。新規イベントはkeyの英数字・Unicode文字・数字・`_`・`-`を保持し他文字を`_`へ置換、`_diary`を付け、衝突時は`_2`等を付ける。日記IDは通常UIで編集せず、特別な日記チェックからnormal/specialへ変換する。

新Runtime C# `DiaryTextVariants` は多言語本文集合の共通読込/保存/選択を担当する。表示はUnityの等確率Random.Rangeを使用する。日記は群共通の `diary_selection_mode=Random|Sequence`、空欄はRandom。Sequenceは先頭から末尾へ進み先頭に戻る。選択中Editorタブとは無関係。TimedEvent/独立候補/通常/特別/死亡の記録は共通 `DiaryJournalStore.RecordVariants` を使う。次位置は正式なProgressSaveStoreの `Nekolpos.Diary.VariantSequence.v1` にdiary_idとnextを保存する。候補取得やプレビューでは進めず、日記追加と保存が成功した時だけ更新し、失敗時はメモリと保存用値を戻す。候補数変更は剰余で補正し、0候補は記録せず例外を出さない。Weight/連続回避は追加しない。

独立候補CSVにも `diary_variants` / `diary_selection_mode` を追加可能とし、暗号化JSONへ同じ群をコンパイルする。Event keyとdiary_idはVariant操作で変えない。既存時間進行API・夜境界停止・Calendar表示は維持する。

可視・有効な編集欄だけ、描画順（左上から下、続いて右の展開中収納）にIMGUIコントロール名を登録する。Tab/Shift+Tabは描画前に消費し、登録された次/前へFocusControlで移動する。折り畳み/非表示/無効欄は登録せず、TextAreaへタブ文字を挿入しない。本文・Condition・Choice・詳細・関連Variant入力も対象。Variant切替ボタンはフォーカス順へ含めない。

## 読取専用Authoring比較（Issue #64 / Phase1）

通常原稿の開発情報に「Authoring Phase1 読取比較」を追加した。保存済みDialoguePreview.csvを型付きRoot/Segment/Line/LegacyHybridPolicyとして表示し、750行41列の損失/Editor差分とDiagnosticsを確認できる。未保存編集は比較へ含めず、保存先・Runtime・暗号化配布形式は維持する。モデル、Sequence reset anchor、ID、Schema、検証結果は `docs/DialogueAuthoringPhase1.md` を参照。

同画面の「Phase2 Shadow生成・旧Engine比較」でEditor検証用bundleと比較reportをLibraryへ生成する。入力照合・選択・RNG・状態・外部処理の実行予定データを比較し、Runtime切替は行わない。詳しくは `docs/DialogueAuthoringPhase2.md` を参照。

Phase3では通常会話だけを暗号化Compiled Bundleへ変換し、Editor/Development Build限定flagで旧CSV Loaderと切り替えられる。通常Player BuildはLegacy固定で、検証失敗時も同じ起動中にLegacyへ戻る。操作、SHA検証、性能・Build結果は `docs/DialogueAuthoringPhase3.md` を参照。

## 保存ファイルの安全な置換

会話CSV、関連CSV、DialogueIdentity、暗号化出力などEditorの共通保存は、同じディレクトリへ一時ファイルを書いてから置換する。WindowsではMonoの `File.Replace` を使わず、OSの同一ボリューム置換をwrite-throughで実行する。一時的な共有違反・ロック・アクセス拒否は25/50/100/200msの4回だけ再試行する。置換前に元ファイルを削除しないため、全試行失敗時も元データを保持する。

保存先が読み取り専用なら置換前に拒否する。ロックが継続する場合は対象の絶対パスとWindowsエラーを表示し、他アプリで対象を開いていないか確認できるようにする。同一内容は書き換えず、timestampとAsset再importを維持する。一時ファイルは成功・失敗とも削除する。

2026-10-12: 上部の登録一覧をスクロール可能なポップアップへ変更。マウスホイール、縦スクロールバーのドラッグ・トラッククリックで移動し、行クリックで選択する。開くたび現在選択中の項目を中央付近へ表示（先頭・末尾では範囲内に補正）、選択行を強調する。

2026-10-12: 心理反応CSVのみ左本文パネルに反応タイミング（朝/昼/夕/夜開始時・就寝前）を追加。既存context列へMORNING/DAY/EVENING/NIGHT/BEFORE_SLEEPを同一Pattern全行へ保存し、既存分類も保持・再選択可能。これは原稿分類の編集で、Runtimeの発火処理を追加しない。条件A〜Dは心理状態の等値式（J:心理状態）、心理値==100、本能>=70/<70を既存の意味のまま表示し、未知条件・複雑ORは保持する。

2026-10-12: 条件A〜Dの心理状態選択肢を日本語化（通常/敵対/ドS/心配/愛情/従順/本能/発情、複合は「・」で連結）。心理値100比較も日本語名、高/最大の表示へ統一。保存用条件式・Runtime値は不変。

2026-10-12: 反応タイミング欄の旧context分類も日本語化。COMPOSITEは複合心理分類で、朝/昼/夕/日記が混在するため「複合心理（タイミング未分類）」と明示。現CSV209行中70行がCOMPOSITE、MORNING7/DAY6/EVENING6等は別に存在。時間帯挨拶はBasicSystemDialogue経路であり、心理反応contextによる挨拶選択は未接続。既存原稿のタイミング分類移行やRuntime接続は今回行っていない。

2026-10-12 Issue #70: 旧JカテゴリをI心理へUI統合。心理値19択/代表心理22択へ整理し、削除対象の旧条件は使用枠だけに表示、別枠編集でも元の条件句を保持する。詳細はdocs/StatusSystem.md参照。
