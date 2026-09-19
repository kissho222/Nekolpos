# Nekolpos Dialogue Graph

`Nekolpos > Dialogue Graph` は、会話CSVを読み取るだけのUnity Editor用可視化ツールです。ゲームの会話データ、CSV、Timeline Asset、Runtimeコードは変更しません。

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

Action IDやYarn、コードが実行時に選ぶ遷移は推測せず、`Runtime resolved` として表示します。
同じCSV・Dialogue ID・Patternの行は、`SpeechControl`の値にかかわらずRuntimeが一つの再生グループとして`order`昇順に読み上げます。Graphも `Order 1 → Order 2 → … → Input` として接続します。`Random`と`Sequence`はグループを選ぶ方法であり、グループ内の各行を途中でReturnさせる指定ではありません。明示的な`SpeechControl = Return`だけをCall元へ戻る`Return`ノードとして表示します。

Pattern分岐は、同じCSV・Dialogue ID・Patternの先頭行へだけ接続します。複数CSVや同一CSV内の別Dialogue IDで再利用されるPattern値だけを根拠に、別会話へ接続することはありません。

会話ピッカーで選んだ会話は、Dialogue IDだけでなくCSVのSourcePathと組にして保持します。IDが別Sourceに重複していても、選択済み会話の表示へ他Sourceの同ID行は混入しません。

## Editor-only persistence

ドラッグ後のノード座標だけを `Library/NekolposDialogueGraphLayout.asset` に保存します。Buildには含まれず、CSVやゲームデータは書き換えません。
