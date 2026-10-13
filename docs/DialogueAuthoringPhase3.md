# 会話Authoring Phase3: 開発切替Loader

最新正本の診断集計は[会話診断UI](DialogueAuthoringDiagnostics.md)を参照。#65ではEditor診断のみ更新し、配布bytes・Runtime Loader・会話本文は変更していない。

通常会話 `TalkSource/TalkCSV/DialoguePreview.csv` だけを対象に、Phase1モデルから既存Runtime型へ復元可能なCompiled Bundleを生成する。選択・Sequence・Repeat・3日リセット・Choice・Action等の実行ロジックは既存`DialogueEngine`を共有し、Phase3で置き換えるのは起動時の読込だけである。会話本文とCSV正本は変更しない。

## 配布物と切替

`CSVCryptoCompiler` は通常のCSV暗号化と同じ鍵/IVで `Assets/Resources/TalkData/DialoguePreviewRuntime.bytes` を生成する。平文JSONは配布せず、Editor確認用だけを `Library/DialogueAuthoringShadow/DialoguePreviewRuntime.json` に生成できる。

開発時は次のメニューで即時切替し、起動済みEngineも再読込する。

- `Nekolpos/開発/会話Runtime/Compiled Bundleを使用`
- `Nekolpos/開発/会話Runtime/Legacy CSVへ戻す`

Development Buildは起動引数 `-dialogue-runtime-bundle` でもCompiledを要求できる。通常Player Buildでは常にLegacy CSVを使用する。現在のPhase2公開ゲートはfalseであり、Compiled Bundleは開発比較用である。

## 検証とフォールバック

Loaderはformat version、対象source、復号済みDialoguePreview.csvのSHA-256、Pattern/Reactionの必須ID、重複Line ID、Label参照を検証する。検証または復号に失敗した場合は部分登録せず、理由をWarningへ記録して同じ起動中にLegacy CSVを読む。旧Loaderは削除しない。

Compiled側は142 RegexPattern、750 DialogueReaction、139 Labelを既存`RegexPatternData`と`DialogueReactionData`へ復元する。旧CSV private parserとの全public field比較で一致を確認する。Phase2で残るTimedEvent不足1件、潜在Choice `0` 2件、特殊Action resolver確認40件は元値と診断を保持し、このPhaseで推測修正しない。

## 2026-10-08 検証結果

- Phase3新規テスト: 5/5成功、失敗0、Skip0。全Runtime field、SHA不一致、version不一致、重複ID、決定的JSON、開発flag rollback、Loader時間を確認。
- Phase1/2回帰: 46/46成功、失敗0、Skip0。
- Editor実Engine切替: Compiledを読込し、`ActiveRuntimeSourceMode=CompiledBundle`、error空を確認。確認後は元のLegacy設定へ復帰。
- Engine全体再読込3回平均: Legacy約311.5ms、Compiled約313.0ms。同等範囲で、速度改善とは評価しない。
- 暗号化サイズ: Legacy DialoguePreview 336,640 bytes、Compiled Bundle 1,089,664 bytes。構造情報を含むため約3.24倍。
- Windows 64-bit Development Build: 成功、error 0、warning 0、72.83秒。出力は検証用一時フォルダー、約2.13GB。`-batchmode -nographics -dialogue-runtime-bundle -quit` のPlayer起動はexit 0だが、`-quit`によりScene Runtimeログまでは取得しない。

統合/UIを含む広域RunnerはUnityが非foregroundの状態で開始待ちとなり、公式clear-stuckで解除した。Phase3新規、Phase1/2、実Engine切替、暗号化、Development Buildは個別に完了している。PC画面操作とiOS/Android実機は未確認。

## Phase4へ残すもの

Authoring JSONを正本にはしない。CSV保存を止めず、旧Loaderも削除しない。Phase4へ進む前にPhase2公開ゲートの既知診断を作者判断で解消し、実Playerで通常会話・Choice・Action・TimedEvent・日記・身体反応を確認する。Compiledサイズ最適化は計測結果と配布要件に基づいて別途判断する。

## Sequence hold修正（2026-10-08）

「噛んで」は通常SequenceのPattern 1→2→3→6を進み、Pattern 6に`sequence_progress_hold=true`がある。従来の終端fallbackはこの保持位置を参照せず、外部処理ではない直前のPattern 2へ戻るため、2回目の噛みつき死亡後にPattern 2だけを再生していた。Compiled固有の欠落ではなくLegacy CSVでも同じ共通Engine不具合だった。

Sequence終端では明示hold位置を最優先し、holdがない会話だけ従来の外部処理連打防止fallbackを使うよう修正した。「噛んで」は1→2→3→6→6となり、死亡後もPattern 6の選択へ戻る。Compiled/Legacyの実通常データとPhase2 selectorの専用回帰2件が成功。CSV本文・Pattern・暗号化Bundleは変更していない。
