# ネルコに教えた言葉（Issue #71）

## 構成と保存

`LearnedWordStore.cs` は自由登録辞書・質問プール・カテゴリ変数を管理する。既存TeachingDiscoveryStoreとは独立する。保存キーは `Nekolpos.LearnedWords.v1`。既存ProgressSaveStoreを使い、PC Playerはprogress-v1.json、Editor/WebGLは既存PlayerPrefs経路へ保存する。固定ID、表示名、正規化名、読み方、別名、ジャンル、質問ID別Unanswered/Yes/No、あいさつ時間帯・場面を保持。旧セーブにキーがなければ空辞書から開始。不正保存は元データを保持し上書き禁止。同名・読みの別ID登録、空文字、制御文字、64文字超過を拒否する。教え直しは元の固定IDを維持して更新し、かな/カナ同一認識の表記変更でも新規登録しない。内部エラーはゲームに表示せず記録して会話を強制終了する。

通常会話・既存TeachingMode・名前等の既存優先処理を維持し、通常Regexが不一致のときだけ学習語を完全一致→長い部分一致順で参照する。部分一致時も元の入力を保持する。食物についての回答をゲーム効果・安全性判定に利用しない。

## 操作

未知語の「○○って何？もしかして言葉を教えてくれるの？」［はい／いいえ］→はい→「じゃあ○○ってどういう意味？」→会話を次へ進める→右側の学習フォームで表記修正・確認→ジャンル選択→1問（はい／いいえ／わからない）→保存→終了選択→unknown_word_teach_eventへ。わからないも回答済みで保存し、要約からは除外する。確定前のキャンセルは辞書を変更しない。専用TMP入力欄では自由な表記を許可する。「ひらがな・カタカナ以外を含む」場合は、編集欄を離れた時／確認時に読み方欄を表示し、元の入力を初期値にする。漢字・英字・数字・記号を対象とし、長音と半角カナのNFKC変換はかなとして扱う。既存の漢字語で読みが未登録の旧保存は読み込める。通常ChatUIの日本語制約とIME処理は変更しない。学習中は時間を進めず、保存して終了した時に既存unknown_word_teach_event（120分・結果表示・日記）へ1回進む。初回否定・未保存取消・内部エラーはイベントを実行せず通常入力へ復帰する。

既知一般語は「教え直す／別の言葉だよ／なんでもない」。教え直すは元IDを維持した空欄編集へ進む。保存後の続けて教えるは新IDを作り、元入力・表記・読み方・回答を空にした新規登録へ進む。「さらに教える」の別ウィンドウは使わない。質問・回答の訂正ドロップダウンは削除。ジャンル確認後は次の未回答質問（全回答時は最初の質問）へ進む。「別の言葉」は元入力全体を初期値とする独立登録。ジャンル変更時も旧回答を保持し、要約と完成判定は現在ジャンルの質問だけを見る。全部回答した後も訂正可能。

あいさつ初回は質問を消費せず時間帯4トグル・場面2トグルを1画面で設定し保存する。各軸の未選択は制限なし、軸内OR・軸間AND。再入力は条件外でも認識し「教え直す／そのままでいい」。教え直すは現設定から開始、保存後の「続けて教える」は新しい語の登録となる。場面が未定義の通常会話・時間帯遷移では、場面指定のあるあいさつを自発抽選に含めない。

## 変数とCondition

|変数|ジャンル|
|---|---|
|random_food|食べ物・飲み物|
|random_animal|生き物|
|random_person|人物・キャラクター|
|random_place|場所|
|random_object|物・道具|
|random_work|作品・遊び|
|random_action|行動・出来事|
|random_other|その他|
|random_greeting|あいさつ（基本語も含む汎用引用）|
|random_word|全ジャンル（デフォルトあいさつは除外）|
|random_greeting_morning / random_greeting_day / random_greeting_evening / random_greeting_night|現在時間帯と使用条件が合うプレイヤー学習あいさつのみ|
|random_greeting_meeting / random_greeting_parting|GreetingSceneがMeeting/Partingのとき、条件が合う学習あいさつのみ|

各ジャンルに1〜2基本語を用意。基本語1票・学習語4票、random_wordの「その他」学習語だけ1票。一会話の変数値はWordVariablesのキャッシュで固定、会話開始・保存・削除時に破棄する。TMPの山括弧と変数括弧は表示用の安全な文字へ変換する。

`HasValue:random_greeting_morning && Morning` のように既存条件と併用可能。HasValueは抽選可能候補の存在を調べ、抽選を消費しない。あいさつ専用変数はデフォルト語だけでは成立しない。空候補の変数を含む通常会話グループは候補から除外する。BasicSystemDialogueの汎用挨拶Patternは同じ条件を用いて既存挨拶より高い優先度で選ぶ。場面イベントの追加者はConversationGameStateの `GreetingScene` をイベント中だけMeeting/Partingへ設定し終了時に除去する。

## UI・タブレット

`WordLearningController.cs` は固定WordLearningView.prefabの入力／ジャンル／あいさつ条件を制御する。WordMemoController.csはWordMemoPanel.prefabの学習一覧と詳細を制御し、可変行のみWordMemoRow.prefabをInstantiateする。Editor限定WordLearningAuthoring.csのメニュー `Nekolpos > UI > Add Missing Word Learning UI` は不足分だけ追加し、既存Prefab・Inspector配置は再生成しない。MainSceneへの配置も必要。学習フォームは右上アンカー・幅500/高さ490、左右の会話・3D表示を全面で覆わない。初回生成時のレイアウトだけEditorで設定し、Runtimeは位置・寸法を上書きしない。

タブレットの「言葉メモ」から1語選択→忘れさせる→保存済み会話視点へ既存カメラ補間→ネルコの確認→確定削除／キャンセル→タブレットへ復帰。カメラ往復中もタブレット閲覧所有権と散歩状態を保持する。削除対象は固定IDの学習語だけ。基本語・Teaching辞書・ログを削除しない。削除直後に変数キャッシュと一覧を更新する。

## 検証

2026-10-13: 正式Unity Test Runnerで関連142/142成功・失敗0・Skip0。最終追加の日本語チャット制約とPrefab参照の2/2も成功。保存/破損/重複/累積/訂正/完了/部分一致/VR読み/あいさつAND・OR/HasValue/変数固定/重み/忘却、実Playerファイルバックエンド、通常Regex優先、既存入力・タブレット・5会話間隔を検証した。結果は output/learned-words/tests.xml と tests-final-ui.xml。

MainScene実Playで「たらこって知ってる？」から修正・food・いいえ・保存・再入力、メモ表示・忘却取消・確定削除・タブレット復帰を確認。学習済み朝あいさつがBasicSystemDialogueの優先度20の汎用Patternへ入り、グッドモーニングを置換できた。最終フォームは小さいGameViewでも上端が見切れない位置へ修正。メモは既存Diaryと同じTabletZenMaruGothicDynamicBitmapを使い、小さいWorld CanvasでのSDF文字の潰れを回避。画像は learning-form.png / word-memo.png、結果は play-result.json / greeting-play.json。検証用のEditor保存キーは復元した。

WebGL定義で現在のRuntime 273ソースを条件コンパイルしError0（既存ソース・参照由来Warning264）。実WebGL Player Build/ブラウザー実IME・モバイル実機・ネイティブPlayerのプロセス再起動は未実施。再読込の永続化は注入ファイルバックエンドで確認済み。人による入力・解像度別UIとカメラの最終演出は別途確認する。Unity起動前のMCPは3回接続試行でも利用できずバッチ実行も別Editorの起動と競合したが、その後MCP再接続しScene配置・保存・コンパイル・正式テスト・Playの全操作をEditorで実施済み。バッチ失敗を検証成功には数えていない。

2026-10-13追加指示: 初回聞き返しを［はい／いいえ］へ戻し、はい後の「じゃあ○○ってどういう意味？」を読み進めてからフォームを開く。フォームを右上幅500/高さ490へ変更し、英字読み・ジャンル・あいさつ条件・ボタンも同じ幅へ配置。固定位置はPrefabで保持。関連Runner10/10成功・失敗0/Skip0、初回否定・中間台詞中の取消・読進後表示と既存学習を確認。実Playの通常Engine経路で台詞→右フォームと入力非表示を確認し、フォーム・あいさつ条件の画像QA実施。証跡tests-right-side.xml/right-side-form.png/right-side-greeting.png。検証用Editor保存値を復元、最終Play停止/Console Error0。#72は現在の直接指示と別件のため未着手。

2026-10-13追加修正: 送信したEnter等が初回はいを同フレームで自動決定しないようChatUIの選択肢受付を次フレームから許可。学習中は遅延した空入力を含む別の自由入力を受理せず、聞き返しを上書きしない。教え直しの元ID/空欄直行、かな以外の読み方、質問訂正の同一パネル化、内部例外の無表示・強制終了を実装した。入力不正/重複/保存IO/忘却カメラ失敗を含む関連80/80成功。通常Engine実Playで聞き返しの同フレーム自動選択防止、意味質問後にフォーム、ちげ→チゲ/Workの同ID・1語更新を確認。追加の入力競合テスト結果は完了報告を参照。

最終入力保護: 選択肢生成フレームの送信操作を選択確定へ流用しない。学習中の遅延空入力・別入力はDialogueEngine入口で受け付けず、聞き返しを維持する。追加Runner2/2成功、通常Regex優先も維持。最終WebGL定義コンパイルError0。

2026-10-13追加修正: 学習のネルコ発話は通常ChatUI.ShowMessageのタイプライター・表示完了後に選択肢。フォームは既存UIStyleのボーダー/影/入力/ボタンをPrefabへ保存。ジャンルリストscrollSensitivity=40。連続登録の英字語の読み方は空欄から入力。

最新確認: 関連EditMode62/62成功（tests-continuous-final.xml）、WebGL定義Error0。実Playで新規2語・別ID保存、読み初期空、通常タイプライターと装飾、終了イベント開始を確認。イベントは開始直後に中止し今回の全終了は未確認。保存値復元・Play停止。

## 質問と日本語要約のCSV（Issue #73）

正本は `TalkSource/TalkCSV/WordLearningQuestions.csv`。WordKnowledge内の固定質問定義を撤去し、質問・ジャンル名称・活用をこのCSVだけから読む。既存29問のquestion_idと順序は維持。保存JSONのID/Yes/No/Unknownは変更しない。保存済みIDを削除・変更せず、追加時は新しい安定IDを使う。

|列|用途|
|---|---|
|row_type|`genre` または `question`|
|category|`Food / Animal / Person / Place / Object / Work / Action / Greeting / Other`。質問だけ `Common`（全ジャンル共通）も可|
|question_id|question行の一意なID。英小文字から始まる英小文字・数字・アンダースコア|
|order|question行の0以上の整数。小さい順。同値はCSVの行順|
|label|genre行のDropdown/メモ表示名|
|category_middle / category_final|genre行の接続形／回答ゼロの終止形。例：食べ物で／食べ物なんだね！|
|question_text|質問本文。例：甘い？|
|yes_middle / yes_final|肯定の途中形／最後の形。例：甘くて／甘いんだよね！|
|no_middle / no_final|否定の途中形／最後の形。例：甘くなくて／甘くないんだよね！|

該当row_typeで使わない列は空欄。9ジャンルのgenre行と各ジャンルの質問は必要。活用は作者が全て明記し、自動活用・文法推測はしない。各ジャンルとCommonの質問をorder順で参照し、回答済みだけを合成。途中はmiddle、最後の1つだけfinalを使い、回答ゼロではcategory_final。区切り「、」は合成側が1回ずつ入れる。活用欄に「、」「,」「，」を含めない。句点・感嘆符等の文末はfinal欄に記入する。

例：`たらこは食べ物で、甘くなくて、大きくなくて、胸キュンなんだよね！`

UTF-8 CSV（BOM可）、引用符と二重引用符のCSVエスケープを扱う。必要列/重複列/空欄/ID重複/カテゴリ/順序/列数/引用符/活用欄の区切り/制御文字を検証する。不正定義はコンパイル前に例外として停止し、壊れたデータを公開しない。

編集後は `Nekolpos > CSVを暗号化バイナリへ変換`（既存環境変数の鍵/IVが必要）。既存CSVCryptoCompiler/Build経路に検証を追加し、`Assets/Resources/TalkData/WordLearningQuestions.bytes`へ暗号化出力する。Editorは正本CSVを読み、コンパイル後にキャッシュ破棄。Player/WebGLは既存ConversationDataManagerの暗号化Resources読込を使い、外部ファイル読込はしない。新CSVは通常会話ルート読込から除外。Graphとメモも同じ質問定義を参照する。

## 学習UIの表示（Issue #73）

見出しは「教える言葉」、表記欄の重複ラベルを非表示にし、未入力時のみ「言葉を入力」。読み方は「読み方」「読み方を入力」。挨拶は「いつ使うあいさつ？」「時間帯」「場面」。複数選択・軸未選択なら無制限の挙動を維持し、常設括弧注釈は表示しない。学習フォーム・質問中・メモ忘却確認の取消を「やっぱりやめる」で統一し、処理は従来どおり。メモの内部実装/ゲーム安全性の常設注記を撤去（食物知識をゲーム判定へ使わない仕様は維持）。他機能のキャンセル表記は変更しない。

Issue #73検証: 正式EditMode96/96成功、失敗0/Skip0。暗号化CSVコンパイル20件、実resourceと正本の一致、最終WebGL定義273RuntimeソースError0。実Play821x462のフォーム/挨拶画面を画像確認し「やっぱりやめる」overflow=false、取消後語数0。保存値復元・Play停止。結果output/issue73。モバイル実機・WebGL実Playerは未検証。

ジャンル画面の誤クリック対策: 表示後0.5秒（unscaledTime）はCanvasGroupで確認/取消/Dropdownの操作を止め、コールバックでも連打を拒否する。プログラムからの取消・エラー復帰は待機しない。Dropdown選択肢の行には枠/影/角丸画像を使わず、ホバー・選択ハイライトのみ保持。UIStyleの一括適用もDropdown内Toggleの背景を装飾対象から除外する。

## 学習モードのセリフCSV

正本は `TalkSource/TalkCSV/WordLearningDialogue.csv`（`key,ja`）。現在の文面を移したもので、新しいキャラクター台詞は生成していない。初回聞き返し/意味質問、あいさつの一致/条件外反応、既知語の完全/部分一致、質問の文型、要約の文型、保存/全問完了、メモの忘却確認/結果を発話14キーとボタン14キーで指定。`{word}` は対象語、`{summary}` は合成済み要約、`{question}` は質問本文、`{facts}` はジャンルと回答の合成結果。プレイヤーの単語はTMP/変数括弧を安全化し、作者の `{{CAT_PRONOUN}}` 等は既存ChatUIの変数置換へ渡す。

質問本文・Yes/No活用は引き続きWordLearningQuestions.csv。フォーム見出しはセリフCSVの対象外、ボタン/選択肢の文字はbutton_*キーから読む。BasicSystemDialogue.csvの旧unknown_word行は旧入口の定義であり、現行学習モードの発話編集はこの2つのCSVで行う。

編集後は既存「CSVを暗号化バイナリへ変換」でコンパイル。Editorは正本、Player/WebGLはTalkData/WordLearningDialogue.bytesの暗号化Resourcesを読む。空欄/重複/必須キー欠落/未知プレースホルダー/CSV構文を検証し、通常会話ルートには混ぜない。Compilerは既存AtomicWriteを使用し、Unityがbytesを読み込んでいる場合の直接上書きエラーを避ける。

## 追加カテゴリとGeneral

ユーザーの直接指示により `word`（`Word`も可）は「有名な言葉」のジャンル。セーブ互換のためOther=8を維持しWord=9へ追加。CSVのgenre行から名前/接続形を参照し、現時点で固有質問がなくてもGeneralを使う。`General` は全ジャンル共通の質問で、ジャンル固有質問の後にGeneral内order順で並ぶ。General内の同値orderはCSV行順。従来 `Common` も有効だが、こちらはジャンル固有質問のorderへ混ぜる従来仕様を維持する。削除された旧food_heart/work_heartの保存回答は保持し、現行質問/完成判定/要約には使用しない。

## 回答完了とボタン文字

回答辞書にIDがない質問だけ未回答。値0は「わからない」（WordAnswer.Unknown、旧Unansweredと同値）として回答済みに数え、次の質問・コンプリート判定から外す。値1/2の肯定/否定だけ要約へ入れる。既存セーブの値0も回答済みになり、繰り返さない。全てわからないの場合の要約はジャンルの終止形だけ。

保存時の全回答完了はsaved_complete、コンプ済み語の再入力（完全/部分一致、挨拶も含む）はknown_complete。未完了語は従来のknown_exact/known_partialまたは挨拶の条件反応。保存後「続けて教える」を押すとteacch_moreをタイプライター表示し、表示完了後に新ID/元入力・表記・読み空欄のフォームへ進む。キー名teacch_moreは作者が追加した綴りを維持。

WordLearningDialogue.csvにbutton_yes/no/unknown/cancel/confirm/reteach/keep/other_word/nothing/teach_more/return_chat/forget/return_memo/closeを追加。学習選択肢・固定確認/取消・メモ忘却/戻る/閉じるの文字に反映。ボタンは既存PrefabのText値だけを更新し、形状・位置・イベントは変更しない。

最新確認: 関連EditMode90/90成功、失敗0/Skip0、暗号化21件/WebGL定義コンパイルError0。全わからないの保存/完了/要約除外、完全・部分一致のknown_complete、teacch_more、CSVボタン値の反映を検証。実Play・実機確認は未実施。証跡output/word-learning-complete。

質問する順序は未回答から均等ランダム。ジャンル固有/Generalをまとめて抽選し、Yes/No/わからないの全回答済みIDを除外する。全問回答後の明示的な教え直しは既存質問全体からランダム。orderとGeneralの後置は要約/メモの順序として維持し、コンプ確認だけで乱数を消費しない。

ジャンルDropdownの初期化復旧: 学習開始時にControllerを有効化し、0.5秒のロック解除Updateを動かす。CSV定義を取得してからイベントを接続し、全初期化完了後だけinitializedを立てるため、CSV修正後に再試行できる。order同値は許容し、要約では安定したCSV行順を維持。
