# 会話Authoring Phase2: Shadow生成・旧Engine比較

診断の最新状態とAction分類は[会話診断UI](DialogueAuthoringDiagnostics.md)を参照。#65で未解決ActionをID単位Errorへ整理し、旧resolver確認Warningだけで公開を保留する条件を廃止した。比較・選択ロジックは維持する。

通常会話 `TalkSource/TalkCSV/DialoguePreview.csv` の保存済み原稿をPhase1モデルへ読み込み、Editor内だけで型付きshadow bundleを生成する。CSV・暗号化bytes・Runtime Loader/Engine・Scene・Prefab・既存セーブは切り替えない。本文は既存値だけを保持する。

## 操作・成果物

会話制作の開発ボタン「Authoring Phase1 読取比較」→「Phase2 Shadow生成・旧Engine比較」。`Library/DialogueAuthoringShadow/Bundle.json` と `Comparison.json` を生成する。Libraryは検証用で配布・Build入力・JSON正本ではない。表示は保存済み原稿のsnapshotで、未保存編集は含まない。再読込で更新する。

`publishable` はデータ検証ゲートの通過を示すだけで、Runtime切替の承認ではない。参照エラーや特殊Actionのresolver確認が残る現在の通常原稿はfalse。

## 新しいEditor C#の責務

- `DialogueShadowCompiler`: formatVersion=1 / authoringVersion=1、出典SHA256、Root/Segment/Lineの安定ID、旧意味hash/Pattern/Editor GUID、全locale、JP実効本文、代表属性、各行の実行予定データ、型付き共有参照を保持する。LegacyRow/cells/originalCsvはbundleへ入れない。intent/reaction/心理変更/日記元ID/NextActionをCSV互換adapterで取り出す。Call/Choice/継続先を同RootのSegment indexへ解決し、不在/未解決は-1と元キーで区別する。
- `DialogueShadowSelector`: インスタンスごとの旧Hybrid互換状態。条件、ゲーム日、乱数を引数で受け、保存・外部処理を呼ばない。Regex照合はCSV登場順、最長一致→Priority→EXPLICIT→先出。Regex実行は100ms timeoutで、timeoutを成功扱いにしない。
- `DialogueShadowComparison`: 非activeの一時GameObjectへ旧Engineを作り、parser、群化、Repeat/Hybrid/3日reset、Regex比較の既存private関数を隔離して呼ぶ。条件と日はテスト側から与え、DetermineReactionの状態更新手順だけをprobe側で組み合わせる。実シーンの状態更新やDialogueManager再生は実行しない。条件Evaluatorは共有するため独立した条件文法実装の検証ではない。教示parserは発見キーを登録しうるため、TeachingCategory/category_idヘッダーを持つ入力は開始前に拒否する。
- `DialogueAuthoringPhaseTwoTests`: 実通常原稿と本文空欄fixtureで、選択・RNG・進行・分岐index・条件変化・異常系・snapshot分離を検証する。

## 互換規則・比較範囲

通常候補はCallOnlyを除いてから旧比較関数でPattern順を再計算。RepeatEventのCallOnly例外を保持。条件を通った代表のうちPlayerDefeatedBy条件があれば優先。候補なしは進行を更新しない。

Repeatは初回除外、閾値の一致を先に使い、最大閾値以降は最大を再利用。Random limitは条件通過後の最初の非負値、なければ3。Randomは未発言を優先し、全消化後は直前Patternを避ける。Sequence cursor/hold anchorは条件通過後の候補index。3日以上空いた次回選択でRepeatとSequenceを戻し、Random履歴は保持。Sequence終端では明示されたhold anchorをループ位置として優先し、holdがない場合だけ外部末尾fallbackを代表行のResponseTypeで判定する（最終行とは別）。

同じUnity Random.stateから旧/新を実行し、選択群、Repeat/cursor/anchor/最終日/既読/直前Random/直前話題と乱数消費を比較。全体比較後は呼出前のglobal RNGへ復元する。全群・全行の本文/条件/Speech/Call/Response/Action/TimedEvent/Choice/Wait/Hide/Animation/Icon/intent/reaction/日記元ID/NextAction/心理変更の実行予定データを比較。直接分岐はCallOnly・条件を問わず同Root全群から解決。post-Actionの行別条件フィルターも比較する。

外部処理は実行予定データと参照先まで。実Action/TimedEvent/日記記録/時間進行/3D演出、Call stackの実再生、非同期キャンセル、Choice UI、未知語/教示/食事等の専用入力入口は実行しない。副作用の実回数・順序は隔離再生・統合検証ゲートへ残す。

## 公開を保留する条件

ImporterのError、特殊Action resolver未確認、Segment index不整合、空Pattern、同Order、数値/非数値Pattern混在、Regex構文エラー、未対応Hybrid policy、差分は公開不可。旧GUID再発行、不安定Sort、混在Patternの非全順序を独断で新仕様へ変更しない。潜在的0参照も元値を保持し、未解決indexとして明示する。

対象は通常CSV。全共有ソース・教示/内部/Basic専用Loader契約・ファイル間hash統合、行挿入/移動時のID移行、JSON Schemaの汎用完全検証、Player Build/配布形式/性能は今回の切替対象に含めない。Phase3へはこれらと実副作用の確認を踏まえて進む。

## 検証結果

2026-10-08 JST: 139 Root / 351 Segment / 750 Line。入力比較840、選択10,008、行実行予定データ24,447、直接参照2,250、post-Action群フィルター702、差分0。seed=0/7/42、Morning/低心理値・Night/高心理値/死亡条件の2状態、各Root12回（同日・2日差・3日差）を比較し、fixtureでは候補変化/話題切替/hold/外部末尾/Repeat/異常乱数を追加検証。

新規11/11成功。既存回帰込み189件は188成功・1既存UI保存テストのFile.Replace IOException（置換対象の削除失敗）、同ケースの単独再実行1/1成功・Skip0。新規変更で再現した恒常的な保存不具合とは確認できず、IO例外は今回未修正の一時的な回帰実行失敗として記録する。既存実Playを含む。Domain Reload中のjob接続不調はinstances再取得/公式set_active_instanceで再試行し、長時間待機した結果取得だけを中断。Editorを前面へ戻して実テストXMLの完了を確認した（テスト二重開始なし）。

実データ診断はInfo269/Warning46/Error3。Errorは既知TimedEvent不足1件と、Normalに残る潜在的Choice `0` の未解決Segment index2件。特殊Action resolver確認40件も公開を保留。元データは変更しない。

作業前3790対象のSHA256比較で、既存変更はDialogueAuthoringPreviewWindow.csのみ、削除0。新規Editor C#4件＋meta。別途画像素材DemaeNyanLogoのmeta2件が増えていたが今回の実装対象ではなく保持。CSV/全TalkSource/既存Runtime/bytes/Scene/Prefabの変更0。最後にConsole Error0/Play=false/compiling=false/Scene dirty=false、一時probe残留0、対象diff-check成功。Player Build/実機/性能・実外部副作用再生は未実施。

UnitySkills REST未実行、ファイル編集とUnity MCPの正規Compile/Runner/execute_codeで代替。computer-useスキルは待機中の画面確認・Unityを前面へ戻す操作だけに使用。証跡: `C:/Users/shoho/.codex/tmp/dialogue-phase2-20261008/` のFinalNew.xml、Regression.xml、SaveRetry.xml、Bundle.json、Comparison.json、Safety.json。Libraryにも同bundle/reportを生成済み。GitHub新規Issue・コメント・ラベル更新、コミット、Closeなし。
