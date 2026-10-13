# 会話ID・回答分岐の調査（2026-10-06）

ユーザー依頼: 00710のボールあそび混入、その他のID間違い、アンケート回答結果のループを確認後、末尾1桁の変更で修正。末尾だけでは採番できない身体教示104・作品教示2317は、ユーザーが別の桁の変更も許可した。

## 修正結果

|対象|旧ID|新ID|
|---|---|---|
|つらい|00490（一部は既に00491）|00491へ統一|
|ねこですよろしくおねがいします|00490|00492|
|おやつちょうだい|00620|00621|
|ボールあそび|00710|00711|
|かくれんぼ|00720|00721|
|においかいで|00730|00731|
|どうも|00740|00741|
|しっぽ|104|111（例外許可）|
|Puss in Boots|2317|2333（例外許可）|

他の内容は既存IDを維持。00370はgoodmorningへ統一し、同時成立できないNight && Evening && DayをNight || Evening || Dayへ修正。2245の175行をseries_nyanpuppuへ戻し、4行のorderを0/1/2/3へ整理して冒頭行を先に表示する。

01280/exerciseは既存action_id=action_gameoftag_play_resultを維持し、欠落するchoice_yes_pattern=chaseを空へ修正。「はい」で既存の即時Action経路を使う。台詞や新しい分岐は作成しない。

TalkTopicMasterの関連SourceKey4件を更新し、Pussの既存series_pusuinbutsu参照も実データのpuss_in_bootsへ修正。DialogueIdentity.jsonは既存GUIDを保持してキーを移行。「つらい」の分割済み同patternだけ00491側GUIDへ統合（関連日記リンク0件を確認）。教示2原稿に任意EntryID列を追加し、移動したしっぽ/Pussと統合したにゃんぷっぷーは旧正規グループの教示識別子を明示して既読記録の変更を防ぐ。

既存ConversationDialogueCsvRegressionTestsにID混在、既読キー保持、修正した会話の到達性・順序の回帰5件を追加。13/13成功、失敗・Skip0。既存CSV検証・全Choice/Call参照検証も成功。通常DialogueEngineの7原稿のChoice回答先は欠落0・循環0。修正3原稿の台詞・翻訳・入力Regex・行数は作業前と一致する。

正規CSVCryptoCompilerで18件を変換し、変更されたRuntimeバイナリは修正3原稿のみ。TalkTopic正本/Runtime複製は一致、3バイナリの復号結果も各正本と一致。最終Console Error0、Play=false、compiling=false、Scene dirty=false。Scene/Prefab/Runtime C#の変更なし、新C#なし、コミットなし。

UnitySkills REST操作は未実行で、既存Unity MCP経由の正規Compiler/Test Runnerを代替使用。実Playでの会話・Action演出と外部フォーム回答は未確認。証跡: C:/Users/shoho/.codex/tmp/dialogue-id-fix-20261006（作業前ファイル、ID/GUID移行記録、TestResult13.json）。

## 別内容が同じIDを使用している箇所

行番号はCSVファイルの物理行（複数行セルを含む）。同一会話の複数行・別名入力・末尾空白の差は除外した。

|正本CSV|ID|内容と先頭行|
|---|---|---|
|DialoguePreview_Integrated.csv|00490|もにか 290 / つらい 430 / ねこですよろしくおねがいします 527|
|DialoguePreview_Integrated.csv|00620|すって 400 / おやつちょうだい 481|
|DialoguePreview_Integrated.csv|00710|おやつあげる 471 / ボールあそび 542|
|DialoguePreview_Integrated.csv|00720|おいしそう 490 / かくれんぼ 547|
|DialoguePreview_Integrated.csv|00730|ねこになりたい 500 / においかいで 550|
|DialoguePreview_Integrated.csv|00740|はつじょうしてる 504 / どうも 553|
|teacchTalk_body.csv|104|くち 12 / しっぽ 17|
|teacchseries_dialogue_source_2201.csv|2317|ダヤン 274 / Puss in Boots 452|

これはID重複の確認結果であり、本文がRuntimeで同じグループへ混入したという実プレイ検証ではない。現行EditorのBuildSavedPatternKeyはregex_id + id + patternで区別する。DialogueEngineはregex_id（空ならregex_jp）のラベルを使用し、上表の会話は異なるRuntimeラベルを持つ。IDだけを見てまとめると混在するが、ID重複だけでは回答ループを説明できない。

## 同じ内容でregex_idが食い違う箇所

- DialoguePreview_Integrated.csv 00370: 211行のgoodmorningに対し212～213行はgoodmornin。入力Regexは同じで反応グループが分かれる。
- teacchseries_dialogue_source_2201.csv 2245: 175行のにゃんぷっぷーはseries_burobukyatto、176～178行はseries_nyanpuppu。入力Regexは同じ。series_burobukyattoはBlobCat側でも使用しており、175行がそのグループへ入る。

上記の修正ではTalkTopicMasterのSourceKey、DialogueIdentity.json、教示EntryIDの保存済み記録への影響を確認して対応した。

## 回答結果のループ確認

TalkSource/TalkCSVのpattern列を持つ11原稿、2,905行（Choice 84行）をCSVとして読み込み、Runtimeラベルごとにchoice_yes_pattern / choice_no_pattern、空の場合のyes_next_pattern / no_next_pattern、およびtarget_patternを静的追跡した。BasicSystemDialogueとInternalDialogueはそれぞれ専用カタログのpattern参照として分離。条件は無視して候補を広く抽出し、循環参照は検出されなかった。通常DialogueEngineのChoiceからChoiceへの直接遷移も検出されなかった。

- ボールあそびはpattern 1/2/3のChoice → はい10（Normal/action_ball_play_result）、いいえ99（Normal）。99にはquestion_jaが残っているが、DialogueManagerはresponse_typeによりChoiceを開始するため、この文字列だけで質問は再表示されない。
- かくれんぼはChoice → はいは空の遷移先とaction_hideandseek_result、いいえ99（Normal）。空の遷移先は即時Actionまたは入力復帰であり、元の質問への遷移ではない。
- 調査時の別問題: DialoguePreview_Integrated.csv 736行、01280/exercise/pattern 1のchoice_yes_pattern=chaseは同じラベル内の遷移先が存在しなかった。今回、既存Actionへ直接進む経路に修正済み。
- PsychologyReactionDialogue.csv 79～84行のtarget_patternは心理イベント識別子を指定し、対象の識別子自体は85～86行に存在する。通常会話の同ラベルpatternとしては解決しないため、専用実行経路の検証なしに追加の欠落やループとは断定していない。

OBTの「アンケートに回答」はLogWindowPanel / OpenBetaPauseMenuControllerで外部URLを開く処理。外部フォームの回答内容・回答履歴・分岐設定は今回取得していない。「アンケート」がこの外部フォームを指す場合、そのループ有無は未確認。

初回調査は正本CSVと現行コードの静的調査。修正後に正規Unityテストと暗号化済みRuntimeデータの一致を検証した。Unity実Play、Action後の実再生、外部フォームの回答は未検証。
