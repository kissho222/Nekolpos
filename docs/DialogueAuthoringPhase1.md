# 会話Authoring Phase 1（Issue #64）

診断表示・Action分類・最新件数は[会話診断UI](DialogueAuthoringDiagnostics.md)を参照。初回検証の診断件数は当時の記録で、#65の再検証が最新。

Editor側だけに型付きモデルと読取Importerを追加する。通常CSV、既存保存先、Runtime、暗号化形式、Scene/Prefabは切り替えない。

## 追加コード

`Assets/Scripts/Editor/DialogueAuthoring/`:
- DialogueAuthoringModel: Project/Root/Matcher/Segment/Line、Condition、Presentation、Terminal/Choice、Reference、LegacyRow/Cell、Diagnostic、LegacyHybridPolicy。
- DialogueLegacyImporter: 既存CSV parserから全セルを保持し、主Engineの意味キー/推定/代表/Pattern順を新モデルへ写す。ImportFileは共有参照を読取検査する。既存Identityへ書き込まない。
- DialogueAuthoringValidation: Validator、共有参照解決、Legacy Export Simulation、Round-trip lossとEditor parity。
- DialogueAuthoringPreviewWindow: 保存済み通常原稿を読取比較する専用開発Window。メニュー「Nekolpos/開発/会話Authoring Phase1 読取比較」と通常Editor開発情報のボタンから選択中の出典行へ対応するRootを表示する。未保存のEditor編集は含まない。
- DialogueAuthoringPhaseOneTests: 全件/fixture/独立旧Engine parser/Editor/損失/保存安全性を検証。

## schema / ID

schemaVersion=1。Rootはsource＋正規化意味キー、SegmentはRoot＋legacy Pattern、Lineはsource＋元行ordinalに対し、長さ付き連結をSHA256して種別prefixを付ける。現時点の同一入力に対する冪等IDであり、行挿入後のLine ID維持やsource改名追従はPhase2以降の対応表が必要。既存Editor GUIDはSegment.legacyEditorIdentityIdsへ読取保持し、Root IDへ流用しない。既存関連CSVの参照は変更しない。

全列・未知列・alias値はLegacyRow.cellsとして元の列順/値で保存。Project.originalCsvは原文証跡。Export SimulationはoriginalCsvを返さず、モデルのLegacyRowをordinal順で再構築する。引用符/改行/BOM等のバイト表現は同一化の対象外、セル意味情報を比較する。typed実効値とEditor表示値の差（Runtimeのquote除去等）は診断し、原文は保持。

JSONはListの多言語(locale/value)と型付き階層、固定field順を採用。JsonUtilityがenumを数値にするため、出力可能なDraft2020-12 JSON Schemaのenum値/説明に対応名を記載。Windowから選択Root/全Project JSON/schemaをコピーできる。Phase1では保存正本化しない。将来はmanifest＋1Root1ファイルを推奨（一巨大JSONは共有競合が集中、複数Root単位はサイズ/競合の妥協）。元全CSVと重複するoriginalCsvはPhase1比較用で、将来正本分割時は独立snapshotへ移す。

## LegacyHybridPolicy / Sequence

Repeat override優先、Random未発話優先→全消化後直前回避、Random limit既定3、条件成立後の候補配列でSequence cursor解釈、数値Pattern順/他初出順、群内Order、群内call_only/代表Repeat例外を明示。LineのSpeechはCall/Return等の行指示のためにも保持し、候補選択はSegment代表とPolicy配列で表す。

SequenceProgressPolicyはResettable/HoldResetAnchor。最後の会話日から3ゲーム日以上空いた次選択でreset。Hold指定の群を選んだ時だけreset anchorを更新し、そのanchorへ戻る。初期anchorは先頭。保存永続化ではなくEngine内メモリ。日記Sequenceのループ/ProgressSaveStore永続化とは別。末尾が外部Action/Event/Choiceの場合の通常群へ戻る互換policyも保持する。

Conditionは既存評価器名/式/適用scope/unconditionalを構造化し、独自に意味を変えたASTを作らない。主通常選択は代表、元行ConditionはLegacyLine、Action後は別経路。Phase2では条件評価器の既存文法の共有/完全構文検査を検討する。

## Terminal / 外部参照

群末尾のEnd/Choice/Action/Eventをtyped化し、各行の元Terminalも保持する。Choice新列が非空なら優先、空ならyes/no_nextを使用、元値/使用列を保持。通常Choice先は直接群再生で再抽選しない。Action/TimedEvent/continuation、Topic/DiaryはReference(kind,key,resolved)で表し、Shared Dataの内容を複製しない。特殊Action/Yarn等はRuntime resolverがあるため、registryで確認できないActionを即Error/自動削除とはしない。

## Diagnostics

Info: MeaningFallback/ChoiceFallback/SpeechFallback/EmptyConditionIsUnconditional/LegacyQuoteRemoval。
Warning: UnknownSpeechControl/UnknownResponseType/RepresentativeConflict/DuplicateOrder/LegacyIdCollision/EditorIdentitySplit/EmptyPattern/ChoiceAliasConflict/AmbiguousZeroTarget/InvalidInteger/EmptyConditionOperand/ActionRequiresRuntimeResolver。
Error: MissingHeader/ColumnCount/DuplicateColumn/EmptyMeaning/LabelHashCollision/StableIdCollision/MissingSegment/InvalidCondition/MissingTimedEvent。

重複Orderや空Patternは現行の不安定な挙動を勝手に安定仕様へ改めず、元順を保持して警告する。0参照は勝手にEndへ直さない。Condition構文は全論理clauseを既存Evaluatorへ渡し、short-circuitで後半の不正条件が隠れないよう診断する。

## 検証・Phase2

検証結果は実行後に追記する。Phase2への条件: 全セルloss0、Root/Segment/Line mapping、旧Engine effective fieldsとEditor表示の一致、既存回帰、禁止対象hash不変。将来の新selector挙動、Random履歴/RNG、Condition変化時cursor、外部副作用trace/全locale、Build性能はPhase1で切り替えず別ゲートとする。

Phase1 preview JSONではJsonUtilityがnullのinline参照を空オブジェクトへ展開する。legacyKeyが空の参照は不在として扱い、JSON再読込で偽の参照切れを作らない。本文・Choice日本語はEditor向け原値と旧Runtime quote除去後の値を別属性で保持する。
## Phase1の確定結果（2026-10-08 JST）

- Import: 139 Root、351 Segment、750 Line、41列。意味キーfallback184行、既存Editor GUID351件を保持。
- Round-trip: 750×41=30,750セル、ヘッダー/列順/元行順の意味情報loss0。本文/多言語/Regex/条件/Choice/Random/Sequence/Repeat/Action/Event/未知列を保持。Exportはモデルのcellsから構築し、変更したcellが損失検査で検出されるテストを含む。
- Editor parity: DialogueDataLoaderの全750行・全41列と群数の差分0。旧Engine parserとは全行のOrder/Speech/本文/条件/Action/TimedEvent/Choice/WaitTime/HideUI/Animationと、全群のPattern順/代表Repeat/Random limit/hold/心理変更を比較して一致。
- JSON再読込後も全セルloss0。生成Schemaのtype/ref/array/enum範囲の検査は違反0（汎用Draft2020-12 validatorでの完全検証は未実施）。サンプル出力は既存会話からのみで新規本文生成なし。
- 新規35件成功。最終既存回帰込み178/178成功、失敗0、Skip0（2026-10-08 00:10:06〜00:10:35 JST、EditMode内の既存実Playを含む）。初回新規31件成功、広い回帰174件中173成功/1既存原稿保存先null、同9原稿の単独再実行9/9成功、最終178件では同ケースも成功。初期compileのnamespace解決2件をglobal::Systemで修正。
- Domain Reload後のMCP job表示は残留/初期化timeoutを示したが、正規XML/Play停止で実結果を確認して終了済みjobだけ公式ClearStuckで解除。UnitySkills REST未実行、通常ファイル編集とUnity MCPのCompile/正規Runnerで代替。REST固有の履歴/認可フローは未検証。
- 既存3779対象のSHA256比較では既存変更はDeveloperボタンを追加したDialoguePreviewAuthoringLayout.csだけ。新規はEditor/DialogueAuthoringの5cs＋meta。通常CSV、全TalkSource、既存Runtime cs、暗号化bytes、Scene/Prefabは変更0。Console Error0/Play=false/compiling=false/Scene dirty=false。Player Build/実機/新selectorの実行trace・性能は未実施。

### 実データの診断

Info269: 意味キーfallback184、Speech推定74、Choice旧列fallback10、空Condition無条件の説明1。
Warning46: 代表属性不一致4、0参照2、Runtime resolver確認を要するAction40（行別、同じキーの重複を含む。全件を確定した参照切れと断定しない）。
Error1: CSV行713の `action_paw_press_death_result` がSystemTimedEvent正本にない。

代表不一致は#63の3箇所に、行218のemotion_change_type差を追加検出。Hold/RepeatCountの元値は消さず、現在の代表行の実効値と別に保持。0参照2行はNormalに残ったChoice列であり、勝手に終端へ変換しない。参照切れも今回修正禁止のためそのまま記録する。

### Phase2へ進める範囲

読取変換・表示・セル損失・旧parser実効フィールドの基盤は成立し、**新Runtimeデータのshadow生成/並列比較の準備は可能**。ただしRuntime公開/切替や新JSON正本化は未承認・未実施。上記TimedEvent参照切れの意味確認、特殊Action resolver確認、全共有参照/全ソースの検証、同Order/混在Pattern順の完全互換、行挿入/移動後のStable ID維持、条件文法の完全共有、RNG/進行/副作用trace parity、JSON schema完全検証をPhase2ゲートへ残す。

証跡: `C:/Users/shoho/.codex/tmp/issue64-20261007/` のFinalNew.xml、FinalRegression.xml、SaveTargetRetry.xml、Summary.json、SafetyVerification.json、AuthoringPreview.json、AuthoringSchema.json、RootSample.json、SchemaVerification.json。フォルダー名は着手日のまま。

ユーザー指示「phase2へ」でEditor限定のshadow compiler/selector/旧Engine比較を追加。操作、互換規則、公開ゲート、実副作用を実行しない検証境界は `docs/DialogueAuthoringPhase2.md` を参照。
