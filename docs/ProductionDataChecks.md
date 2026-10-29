# 制作データの静的品質チェック（Issue #93）

`Assets/Scripts/Editor/ProductionDataChecks.cs` は保存済み制作データを読み、既存Validatorの結果を集約するEditor専用入口。正本、暗号化配布データ、ID対応表、Scene/Prefab、Runtime、セーブは変更しない。Action実行、条件の成立評価、Play開始、Runner起動はしない。

## 最短手順

1. CSV等の編集を保存する。Unityのコンパイル完了を待ち、Playを停止する。
2. `Tools > Nekolpos > 制作データ > 静的品質チェック` を選ぶ。
3. `Library/ProductionDataChecks/report.txt` で件数・出典行を確認する。機械処理には同じ場所の `report.json` / `report.csv` を使う。
4. 指摘を確認して必要な箇所だけ手動修正し、再実行する。最後に正本の差分を確認する。

出力は既存のGit無視対象 `Library/` 内。ネットワーク送信はしない。理由・参照ID等を含むため、公開前には内容を確認する。毎回3ファイルを上書きする。複数Unityプロセスから同じprojectで同時実行しない。

Unity 6000.3.10f1のCLI（同一projectをEditorで開いている間は使用しない）:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\shoho\Nekolpos' -executeMethod Nekolpos.EditorTools.ProductionDataChecks.RunBatch -logFile 'C:\Users\shoho\Nekolpos\Library\ProductionDataChecks-batch.log'
$LASTEXITCODE
```

入口自身が終了コードを確定してUnityを終了する。将来#80から接続する場合も、Runner/Play開始とは分離してこの静的段階のコードを判定する。projectの自動テスト専用workerを起動する手順ではない。保存隔離の制約は [TestSaveIsolation.md](TestSaveIsolation.md) を維持する。

|終了コード|status|意味|
|---|---|---|
|0|success|対象の静的検査が完了しErrorなし。Warning/Infoが残る場合あり|
|1|inspection_failed|検査完了、制作データにErrorを検出|
|2|environment_unavailable|Play/compile中、入力読取不可、診断処理失敗、入力が検査中に変化など|

Unity自体の起動/コンパイル失敗では入口へ到達できずレポートを更新できない。**以前のreport.jsonを今回の成功と扱わず**、プロセス終了コード、Unityログ、`generatedUtc`を確認する。出力書込失敗等はbatch入口で2を返すが、レポートが作れない場合がある。Menuでは例外として表示される。

## Phase A：既存実装と依存関係

|入力/関連|再利用する実装|扱う規則|
|---|---|---|
|共通会話CSV→分岐/条件/Action/TimedEvent|DialogueLegacyImporter.ImportFile、DialogueAuthoringValidator|安定ID・hash、重複列/列数、分岐、条件構文、整数、Action分類、TimedEvent参照。既存severityを維持|
|全TalkCSV＋日記2表＋話題表|TalkTopicMasterCsvUtility.Parse|複数行セルと物理行番号、未終端引用符、ヘッダー/列数。Runtimeと同じCSV文法|
|SystemTimedEvent|SystemTimedEventCatalog、既存制作Editorの仕様|大文字小文字を無視したkey一意、空/0は既定、分は非負int、秒は非負有限float|
|独立Diary→DialogueID対応表→原稿|DialogueDiaryAuthoring.BuildDocument、DialogueDiaryCatalog.Validate、DialogueAuthoringIdentity.Load|候補ID・kind、リンク複合キー・候補参照・会話ID/出典・既存公開検証|
|InternalDialogue|InternalDialogueCatalogの読込規則|regexなしを許可、pattern必須、target_patternは同原稿内の大文字小文字を区別した参照、条件構文|
|TalkTopicMaster|既存Source/SourceKey設計|必要列、TalkID一意のみ。SourceKeyの意味的照合は対象外|
|漢字読み辞書|ReadingDictionaryEntry.ParseBool、JapaneseTextNormalizer|有効行の空表記/読み、正規化後の異なる読みはWarning|
|日記/時間結果の多言語候補JSON|DiaryTextVariants.Read|既存JSONパーサーの形式検査。翻訳空欄は許可|

対象は `TalkSource/TalkCSV/*.csv`、`TalkSource/Diary/{DiaryCandidates,DialogueDiaryLinks}.csv`、`TalkSource/Dialogue/TalkTopicMaster.csv`。`regex_jp`列がある原稿を共通会話Validatorへ渡すが、専用Runtimeを持つInternalDialogueは専用規則に振り分ける。他の専用形式はCSV構造だけを検査する。未知の新規形式に既存列を無理に要求しない。

出力にはUnityバージョン、UTC日時、対象CSV/ID対応表/Yarn原稿/ActionDataのSHA-256を含む。初回読取後に再照合して変更を検出する。フォルダー内の新規追加や、検査中の一時変更→元へ戻す操作まで検知する仕組みではない。YarnとActionDataのhashは既存Action分類の依存入力であり、Yarnをコンパイル・実行した保証ではない。

## MVPの8項目

1. 既存Dialogue診断を全共通原稿から集約。
2. CSVヘッダー・列数・パース。
3. 対象形式の必要列。
4. TimedEvent/独立Diary/リンク/TalkTopicの安定キー・重複・空値。
5. TimedEventの時間値。
6. 独立日記の関連参照、条件、既存公開検証。
7. 読み辞書の実効値と競合。
8. 行/ID/列/理由/改善案、severity別件数、環境・hash、JSON/CSV/TXT、0/1/2終了コード。

本文パターンJSONとInternalDialogueの専用規則は、上記の形式・参照検証に含む。Editor画面や新しいRuntime管理機構は追加しない。

## 例外と判定限界

- 同じ会話の複数行id、同じ読みの重複、無効な辞書行、未参照データ、多言語空欄、任意の本文空欄を即Errorにしない。
- TimedEventの`diary_id`は時間経過日記の識別子。独立DiaryCandidatesへの外部キーと解釈しない。
- 内部会話はpattern/idを使うため、regex/regex_id空欄は正常。`timed_event_key`もRuntimeで読まないため、値があれば列ずれ等の可能性を示す要確認Warningにする。自動修正しない。
- 既存ValidatorのInfo（意味キーfallback等）もそのまま出力する。Infoの多さは不合格理由ではない。既存の曖昧な0参照などのWarningをErrorへ格上げしない。
- 日記公開検証は既存処理が最初の例外で停止するため、集約診断の行番号0は「行特定不可」。CSV単体・候補/リンクキー・未解決IDは別途行番号付きで報告する。
- Action分類は解決入口の存在確認。Scene bindingや実再生を保証しない。条件は`ValidateSyntax`のみ、`Evaluate`を呼ばない。学習語/時刻/PlayerPrefsを参照しない。
- 変数名の全面allowlist、ローカライズ意味/品質、正規表現の全実行、TalkTopic SourceKeyの意味、Yarn経路到達性、実Play、Build、Windows/iOS/Androidは保証しない。
- 未知のboolや選択モードの独自制限を追加せず、現行fallbackを維持する。候補辞書CSVは構造検査のみ。

## 検証とルール追加

`ProductionDataChecks.VerifyFixtures()` は正常CSV、引用符/複数行の位置、重複列、列数不足、未終端引用符、空ヘッダー、非有限/負/不正な秒、非整数分、イベントkey衝突、必要列不足、辞書空値/競合/無効行、内部会話、後段条件不正、候補JSON不正をメモリー上で確認する。共通会話/候補JSONの列名は大文字小文字を区別せず、日記kindは既存公開処理と同じ前後空白除去を適用する。これらの検査経路も正常・不正fixtureで確認する。正式Test Runnerや保存adapterを使わず、失敗時は例外を返す。テスト用台詞は追加しない。

ルール追加時は先にRuntime/Importerの実際の読込規則を確認する。`InspectCsv`のTableを使う専用`Check...`を追加し、`Run`の形式別分岐へ接続する。`VerifyFixtures`へ正常・不正・許可空値の回帰を追加し、実データへ実行する。必要列や参照を推測で増やさない。既存Validatorで保証済みなら呼出しを再利用し、severity/codeを保持する。新しい必須入力は読取前hashと環境不足分類にも加える。

導入前は各原稿のEditor診断と日記公開検証を個別に開き結果を転記する必要があった。導入後はMenu 1操作で対象CSVと既存診断を集約し3形式へ保存する。削減秒数は未計測。未実施のCLI起動や正式Runnerを検証済み件数へ含めない。

## 導入時の確認（2026-10-25）

- Unity 6000.3.10f1の最新コードがコンパイル完了、`scriptCompilationFailed=false`。
- `VerifyFixtures()` の26検証が成功。正式Test Runnerは未実行。
- `Run()` と実Menu入口の2回で、findings全件・入力hashが一致。Menu入口は約2915ms。
- 実データはError 0 / Warning 29 / Info 1103、status=success・exitCode=0。JSON/CSVの1132診断件数が一致し、全診断に改善案あり。
- WarningはDuplicateOrder 14、EditorIdentitySplit 3、LegacyIdCollision 1、RepresentativeConflict 1、内部会話の未使用TimedEvent列10。作者判断が必要であり、削除や書換えはしていない。
- TalkSourceとResources/TalkDataの103ファイルを比較し、検証前後のSHA-256一致。検査入力manifestは27ファイル。
- Play=false。最終Menu前後のScene dirtyはtrue→true、Scene保存操作なし。作業冒頭のdirty=falseからコンパイル後のtrueへの変化は観測したが、原因を特定・推測復元していない。
- batchmodeの別プロセス起動、正式Runner、Build、実機は未実行。MCPの公式stdioクライアントから起動中Editorで実行した。クライアントの文字コード・戻り値属性差異を修正して再接続後の成功結果だけを採用。

導入時の生成証跡: `Library/ProductionDataChecks/{report.json,report.csv,report.txt,verification.json}`。reportは後続実行で上書きされるため、導入時の記録と最新結果を区別する。

## 再検証とPR分離（2026-10-29）

- 最新の旧reportは10/28 JSTのenvironment_unavailable/exit2だった。ユーザーによる並行編集停止・保存の確認後、起動中のUnity 6000.3.10f1が非Play・compile/import完了であることを確認して再検証した。
- 列名が大文字の場合に会話/候補JSON検査を省略する不具合と、日記kindの前後空白を誤検知する不具合を修正。実dispatchを通る回帰5検証を追加し、メモリー上の31検証が成功した。
- Runと実Menu入口がともに成功し、findings/入力hashが一致。Error 0 / Warning 12 / Info 1105、入力28件。残るWarningはLegacyIdCollision 1、RepresentativeConflict 1、InternalUnusedTimedEvent 10で、原稿は自動修正していない。
- 対象原稿・Runtime配布データ・C#・MainSceneの611ファイルを前後比較し、変更は今回修正したProductionDataChecks.csだけ。Scene dirtyはtrue→true、Play=false。Scene保存、通常保存API、正式Runner、別Unity起動、Buildは行っていない。
- 検証前reportは `output/issue93-20261029/previous-report/` へ退避。今回の記録は同フォルダーのrun-report.json、verification.json。Libraryの旧verification.jsonを最新成功の代用にしない。
- 独立PRには本チェッカー/meta、保存や実状態を読まないValidateSyntaxの追加、既存ValidatorのEvaluate→ValidateSyntax置換、仕様と短い引継ぎだけを含める。DiaryCatalogのspecialOnly等の別Runtime変更、原稿、Scene/Prefabは含めない。
- PR base `930b607` の341 C#からsnapshotを作り、対象3 C#だけを重ねてUnity同梱Roslynで外部compileした。Runtime210 C#はbase/変更後ともError 0・Warning 193（同一診断）。Editor全体は両版ともGit未収録のWrightAngle.Waypointを参照する既存PlayerWalkModeControllerTestsの8エラーで不合格。同じ61テストファイルを除く補助確認では、製品Editor70/71 C#が両版ともError 0・Warning 1。全Editor合格や正式テスト成功とは扱わない。現行rootの自作Assembly-CSharp DLLは参照せず、snapshotから生成したRuntime DLLを使用した。Unity/生成DLLの起動なし。詳細は `output/issue93-20261029/pr-compile/`。この既存テスト依存欠落は#93の範囲外として記録し、勝手にテストや依存素材を削除・追加しない。
