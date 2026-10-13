# Nekolpos Dialogue Graph

`Nekolpos > Dialogue Graph` は、会話CSVと現行の未知語学習状態を表示する、読み取り専用のUnity Editor用可視化ツールです。ゲームの会話データ、CSV、Timeline Asset、Runtimeコードは変更しません。

## Data flow

`TalkSource/TalkCSV/*.csv`（開発用の正本）を読み取り、`DialogueGraphDataProvider` が表示専用の中間モデルを組み立てます。正本がない環境では `Assets/Resources` のTalkCSVをフォールバックとして読みます。表示UIはそのモデルだけを参照します。

## Current coverage

- Sourceを先に選ぶ数値順の「Dialogue ID | input」会話ピッカー（文字検索、マウスホイール、スクロールバー対応）、Dialogue ID / Pattern ID検索、CSVソース、台詞プレビュー。`Source = All`では候補を列挙せず、ピッカー内でSourceを選択する
- 選択した会話を自動的に画面へ配置し、マウスホイール、Shift+ホイール、水平・垂直スクロールバーで閲覧可能。Graphの描画はビュー領域内へクリップされ、Toolbarへ重ならない
- Graph Windowでは、ピッカー横の◀/▶ボタン、またはテキスト入力中でない場合のShift+↑/↓で前後のDialogue IDを選択できる
- `condition`、`Choice`のYES/NO、`target_pattern`、Sequenceの次行、終了・Runtime resolved
- `emotion_change_*`、`timed_event_key`と`SystemTimedEvent.csv`
- CSVに記述されたIDと完全一致するTimeline Assetだけ（ダブルクリックまたは詳細ボタンで選択）
- 欠けたPattern・Timed Eventの明確な警告

未知語学習以外のAction IDやYarn、コードが実行時に選ぶ遷移は推測せず、`Runtime resolved` として表示します。
同じCSV・Dialogue ID・Patternの行は、`SpeechControl`の値にかかわらずRuntimeが一つの再生グループとして`order`昇順に読み上げます。Graphも `Order 1 → Order 2 → … → Input` として接続します。`Random`と`Sequence`はグループを選ぶ方法であり、グループ内の各行を途中でReturnさせる指定ではありません。明示的な`SpeechControl = Return`だけをCall元へ戻る`Return`ノードとして表示します。

Pattern分岐は、同じCSV・Dialogue ID・Patternの先頭行へだけ接続します。複数CSVや同一CSV内の別Dialogue IDで再利用されるPattern値だけを根拠に、別会話へ接続することはありません。

会話ピッカーで選んだ会話は、Dialogue IDだけでなくCSVのSourcePathと組にして保持します。IDが別Sourceに重複していても、選択済み会話の表示へ他Sourceの同ID行は混入しません。

## 会話の回数指定

Runtimeの選択単位は`regex_id`ごとのPatternグループで、複数ページの`order`は入力回数を増やしません。

- `SpeechControl=Random`：条件を満たす通常候補から抽選する。未発言候補と直前と異なる候補を優先する。
- `random_repeat_limit`：同じ話題を連続入力した際に、Randomを何回まで選び、次の入力からSequenceへ移るかを指定する。`4`なら1〜4回目はRandom、5回目からSequence。空欄または`-1`は既定の3、`0`は初回からSequence。Sequence候補がなければRandomを継続する。これは個々のRandom候補の出現をN回目まで禁止する設定ではない。
- `SpeechControl=RepeatEvent`と`RepeatCount=N`：同じ話題の連続入力がN回目に達した際に、そのPatternを通常抽選より優先する。1以下は2回目として扱う。最大のRepeatCountを超えた後は、その最大回数のPatternを再利用する。

`DialoguePreview_Integrated`の「ひわい」Pattern 6は`RepeatEvent / RepeatCount=5`とし、1〜4回目の通常抽選から除外する。`random_repeat_limit`は空欄とする。台詞本文は変更しない。

回数はRuntimeの候補選択時に1回更新し、会話の各ページ終了では更新・リセットしない。別の話題に切り替わると連続回数は1に戻る。タイトルからの新規開始・タイトル復帰は明示的に状態をリセットし、3日以上空いた話題も既存のリセット規則に従う。回数をセーブや新しいSceneのEngineへ永続化する機能はない。Previewの直接Pattern再生は候補抽選を通らないため、回数検証には通常のRuntime抽選を使用する。

## Editor-only persistence

ドラッグ後のノード座標だけを `Library/NekolposDialogueGraphLayout.asset` に保存します。Buildには含まれず、CSVやゲームデータは書き換えません。

## 未知語学習フロー（2026-10-13）

上部の「未知語フロー」ボタンで現在のコード制御フローを表示する。既存のSource選択でも「未知語学習（Runtime）」→会話UnknownWordを選択できる。これはEditor専用DialogueGraphDataProviderの表示用投影で、会話CSVや保存データを生成・変更しない。

初回の「○○って何？もしかして言葉を教えてくれるの？」［はい／いいえ］→意味を尋ねる→次へ→右側の表記修正→かな以外を含む時の読み方→ジャンル→1問/あいさつ条件→保存・要約→通常入力、既知一般語の追加/訂正/別語登録、あいさつの条件一致・条件外と再設定、タブレットでの忘却確認/取消/復帰を示す。入力不正・保存失敗は「内部記録・会話強制終了」へ表示する。エラー文をゲームに表示するフローは持たない。質問ノードの詳細には現在のWordKnowledgeの質問ID・質問・肯定/否定要約を直接読み込む。

ノードの詳細は実装ファイルと処理名を示し、「実装を選択」やダブルクリックでソースAssetを選択できる。CSV分岐と区別してRuntimeフローと表示する。循環する再学習にも読みやすい既定位置を用意し、ドラッグした座標は従来どおりLibraryだけへ保存する。コード状態の投影はRuntime改修時に同期更新する。

BasicSystemDialogueの旧unknown_word_prompt/unknown_word_teach_eventからも実際の学習フローへ接続する。未使用の120分定義を現行単語学習の時間経過として表示しない。従来の通常会話Graphと未解決Runtime表示は維持する。新C#・Runtime変更なし。

検証: 関連EditMode 5/5成功・失敗0/Skip0。はい→意味質問→次へ→編集、全エッジ参照/入口からの到達性、既知語/挨拶訂正、保存失敗・忘却取消、実質問プール・保存値非変更、旧120分ノード不表示を確認。実Graph Windowで未知語選択の33ノードを確認し同表示で開いた。Console Error0、Runtime/CSV/Scene変更なし、結果 output/learned-words/tests-graph.xml。

2026-10-13追加指示に同期: かな以外の読み方判定、教え直し→元ID維持・空欄編集、続ける→新ID・元入力/読み方とも空。「さらに教える」の中間画面ノードを撤去し、質問追加/回答訂正のセレクターは最新直接指示で撤去済み。初回聞き返しと意味質問は別ノード・はい分岐で保持。内部例外は無表示の終了へ、忘却例外は無表示のメモ復帰へ接続。

最終確認: 関連80/80と遅延空入力・通常Regex優先2/2成功、失敗0/Skip0。更新した実Graph Windowを開き、未知語の28ノードを確認。

最新追加修正: 訂正ドロップダウンノードを削除。保存後「続ける」は新ID・表記/読み空欄の新規登録、「終了」はunknown_word_teach_eventへ接続。学習発話はタイプライター表示。

Issue #73: 質問詳細はWordLearningQuestions.csv由来の途中形・終止形の両方を表示する。学習取消の表示名を「やっぱりやめる」に同期。
