# Yarn Spinner 既存資産再利用レポート

## 全体所見
- 現行の本番会話は `Assets/Scripts/System/DialogueEngine.cs` + `Assets/Scripts/System/DialogueManager.cs` + `Assets/Scripts/System/ChatUIController.cs` が中心です。
- これとは別に `Assets/Scripts/Conversation/*` に、新しい CSV 正規化・状態管理・デバッグ基盤があり、`ConversationDataManager` は `DialoguePreview.csv` と `RegexDict ... all.csv` をロードしています。
- `Assets/Scenes/GameScene.unity` には `DialogueRunner` が既に存在しますが、`variableStorage` / `lineProvider` / `dialoguePresenters` は未接続です。

## 分類

### 1. 会話 UI
- そのまま再利用可能
  - `ChatUIController` の `speakerNameText` / `messageText` / `chatWindowGroup`
  - 既存 Typewriter (`ShowMessage`, `OnTypingCompleted`)
  - フェード表示 (`FadeInWindow`, `FadeOutWindow`)
  - 入力待ち (`OnWaitInputCompleted`)
- 軽微修正で利用可能
  - Choice UI
  - 既存は 2 択前提だったため、Yarn ブリッジ用にラベル差し替え API を追加
- 新規実装必要
  - Yarn の `DialoguePresenterBase` を既存 UI に接続するブリッジ

### 2. 会話データ構造
- そのまま再利用可能
  - `DialoguePreview.csv` の `response_type`, `SpeechControl`, `condition`, `action_id`, `choice_yes_pattern`, `choice_no_pattern`
  - 多言語列 (`output_ja`, `output_zh`, `output_en`)
- 軽微修正で利用可能
  - `action_id` に `yarn:NodeName` を入れることで、旧会話から Yarn ノードへ遷移可能
- 新規実装必要
  - 本格的な CSV → Yarn node 変換パイプライン
  - Scenario/Event CSV と Yarn node の生成規約

### 3. 状態管理
- そのまま再利用可能
  - `CatDataSO` の affection / hostility / instinct / obedience / concern / sadistic
  - `ConversationGameStateManager` の JSON ベース状態
- 軽微修正で利用可能
  - `GameFlagManager` に全フラグ取得 API を追加し、Yarn 変数同期に利用
- 新規実装必要
  - Save/Load の本統合
  - Yarn 文字列変数から時間帯変更などをゲーム本体へ反映する制御

### 4. Dialogue Preview
- そのまま再利用可能
  - `DialoguePreviewWindow` の CSV 編集 UI
  - `TextEditScenePreviewController` の既存 ChatUI プレビュー
- 軽微修正で利用可能
  - Yarn ノードのジャンプ先可視化
  - `yarn:` action_id のプレビュー起動
- 新規実装必要
  - Yarn ノードブラウザ / ノード遷移グラフ

### 5. 入力解析
- そのまま再利用可能
  - `DialogueEngine` の既存 Regex 解析
  - `RegexInputParser` の intent / category 解析
- 軽微修正で利用可能
  - intent → node 名マッピング
- 新規実装必要
  - 本番フローで `RegexInputParser` を直接使って Yarn 分岐する統合ルータ

## 実装済み最小構成
- `Assets/Scripts/System/ExistingDialogueUIBridge.cs`
  - Yarn line / option を `ChatUIController` に流すブリッジ
- `Assets/Scripts/System/YarnManager.cs`
  - `DialogueRunner` 管理
  - 既存 UI ブリッジ自動接続
  - CatData / ConversationGameState / flags / phase の Yarn 変数同期
  - intent → node マッピング API
- `Assets/Scenes/GameScene.unity`
  - `GameSystemManager` に `YarnManager` を追加
- `Assets/Scripts/System/DialogueManager.cs`
  - `action_id` が `yarn:NodeName` のときだけ Yarn へ委譲
- `Assets/Yarn/test.yarn`
  - `MorningGreeting` 検証ノード追加

## 次の実装順
1. `MorningGreeting` を `yarn:MorningGreeting` で呼ぶテスト CSV を 1 件作る
2. `DialoguePreviewWindow` に `action_id` が `yarn:` の行を識別表示させる
3. 朝イベント / 固定イベントから順に Yarn 化
4. `RegexInputParser` または既存 intent 管理から `TryStartMappedNode` を呼ぶ本番ルータを作る
