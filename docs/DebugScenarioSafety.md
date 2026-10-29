# 既存シナリオRunnerの保存保護

Issue #80の既存Editor Runnerを補強する。Catalog、Executor、8つの許可IDは変更せず、新しい実行入口やworker起動処理を追加しない。

## 隔離と終了

`DebugSaveSession`はArm時のActive IDをSessionStateへ保持する。最初のSubsystemRegistrationだけメモリーへのアクセスを許可し、Domain Reloadによるメモリー喪失やPending/Activeの不整合では、隔離判定を維持したままアクセスを拒否する。通常保存へのfallbackを避け、Playを終了する。Finish後も隔離を維持し、EnteredEditMode後のdelayCallで解除する。

`RoomLightingManager`は隔離Editorで一時VolumeProfileを使い、元のpersistent profileを変更・保存しない。破棄時もpersistent参照を除外する。通常Editorの照明作成経路は維持する。

## 共有Prefs窓口

`WorkerPlayerPrefs.cs`は周辺PrefsのGetInt/GetString/SetInt/SetString/Saveを共通化する。優先順位は、要求された専用workerメモリー、DebugSaveSessionの隔離メモリー、既存PlayerPrefs。Runtime Source選択、ログ用install ID、OPスキップ設定、性能診断の設定読取で使用する。元のキーと通常動作を維持する。

`TestPersistenceContext.cs`はこの共有窓口が既に持つworker分岐の型依存を保持する。compile marker、環境、manifest、owner/epoch付きcapability、終了後の拒否を扱う。今回worker bootstrap、専用project作成、fixtureの有効化、他保存Storeのworker移行は追加しない。**このPR単体を専用workerの起動・通常保存隔離の完成とみなさない。** worker分岐を削除して別実装へ置換することもしていない。

## 終了監査と結果

Runnerの終了時は通常保存・製品Prefs・CatDataに加え、Assets全ファイルとglobal EditorPrefsのハッシュを比較する。EditorPrefsキー名もハッシュ化し、未知の変化は失敗とする。Unityの従来のセッションキー除外に加え、6000.3.10f1で確認した完全キー `unity_connect.mega_session_id_h1802243016` だけを除外する。包括的な `unity_` 除外はしない。

終了後のハッシュ集合をレポートの`.after`へ保存する。終了監査例外はstatus/automatedStatusの双方をEnvironmentUnavailable、終了前のPlay停止はCancelledにする。trace/report保存に失敗してもfinallyでFinishとPlay停止を行う。

この監査は副作用の事後検出であり、任意Plugin/nativeコードの完全隔離を保証しない。automatedStatus=Passedでも描画・IME・実PlayerはManualVerificationRequiredとして残る。旧fixture、worker、新Player、保存adapterの実行許可を拡張しない。

## 日記の進行待ちと一括実行の停止

日記シナリオは、時間進行APIの内部で夜の挨拶がクリック送りを待つ。Editor RunnerがExecutorの完了だけをawaitすると、その後の待機期限へ到達できない。この2IDに限り、夜・TimeFlow中・日記未開始・隔離とゲーム状態が有効な間、`AdvanceIsolatedScenarioMessage`を0.2秒間隔で呼ぶ。会話本文、選択肢、製品の入力仕様は変更しない。

通常入力の`IsBlocked`は従来どおりConsoleの所有/閉じた同フレームとシナリオ実行中を遮断する。既存の`AdvanceIsolatedDebugMessage`もこの遮断を維持する。Scenario専用APIは隔離かつ実行中だけ有効で、Consoleの所有/閉じた同フレームと選択肢表示を拒否し、通常入力と共通のprivate処理でメニュー・遅延処理・文字送りの規則を保つ。テスト自身のExecutor実行中フラグだけを別扱いにし、任意の入力解放口を設けない。

待機全体に非スケール時間の60秒期限と連動取消を設ける。完了済みなら追加の送りをしない。期限・例外・セッション終了では先に実処理をキャンセルし、未協調taskの完了を無期限に待たず既存のFinish/Play停止へ戻る。遅延した例外は型名だけを観測し、失敗結果を成功へ上書きしない。

一括実行は`Finished`、`automatedStatus=Passed`、通常保存とSceneの両保全成功をすべて要求する。開始拒否、開始例外、実行失敗、取消、終了監査例外では残りqueueを消す。次IDの遅延callbackも、実行時点のqueueが予約時点と一致する場合だけ進める。予約後の取消や差替えを無視して古いIDを起動しない。

## 再検証手順

MainSceneを保存し、他のAssets編集を止め、compile/import・Play・前runの隔離解除が完了していることを確認する。既存MCPから次の1件を要求し、返されたローカルJSONが`phase=Finished`になるまで待つ。

```csharp
Nekolpos.EditorTools.DebugScenarioEditorRunner.Request("diary.night.normal");
```

`normal.table`、`normal.desk`、`transition.table-desk`、`walk`、`tablet.desk`、`diary.night.normal`、`diary.night.special`、`death.respawn`が既存の許可ID。各IDの前提は既存Catalog/Fixturesが検査し、未接続を成功扱いしない。初めての範囲は独立レビュー後に1件ずつ実行する。

自動結果、checks、正式なTable復帰、保存/Scene保全を別々に確認し、Play停止・IsIsolated=false・Pending/Batch空まで確認してから次へ進む。終了レポートが成功でも、遅延解除が待機中の場合は次要求を出さない。保全不一致のレポートとbaselineは残し、原因不明の項目を除外したり保存値を復元したりしない。

今回、Editorが背面のときに終了後のdelayCallが残る状態を観測した。Unityを前面にした後、通常のEditor更新による解除を確認できた。SessionStateや隔離フラグを手動で消して次へ進めない。ウィンドウ位置変更もEditorPrefs監査へ影響し得るため、画面を移動した場合は安定してから新baselineを取る。

Runnerの純粋な待機・queue制御は、Python 3.10以上と.NET SDK 10.0で次のメモリー検証を実行できる。追加NuGet、Unity、正式NUnit、実保存adapterを使わない。結果とsource hashは`output/debug-scenario-runner-checks/`へ出力する。

```powershell
python .codex/tools/debug_scenario_runner_checks/verify.py
```

新しいシナリオは既存CatalogにIDを追加し、Executorの正式API経路とFixturesの前提検査、正常終了/拒否/取消/保存保全を同時に定義する。Runnerだけに別の登録表や任意実行入口を作らない。

## 分離検証

base `930b607317b1755aa3c9070855cec3301e6b9a7c` に12CSだけを重ね、外部RoslynでRuntime/製品Editor、Windows Development/Releaseのソース条件、worker定義ありをcompileした。すべて該当範囲でError 0。baseと追加版の警告内容は同じ。このcompileではUnity/生成Unity assembly/旧fixtureを実行していない。

同じ限定ソースに公開メモリー検証を実行し、Runner22件と入力14件の36件が成功した。別のfake Unity/SessionState/Prefs環境では隔離の原文3CSとRunnerのfinallyを使い36観測成功。旧binaryの既知2漏れも再現した。これらは実Unity Domain Reloadや通常保存adapterの実行証明ではない。

Editor全体のcompileはbaseから存在するPlayerWalkModeControllerTestsのWrightAngle.Waypoint型欠落8件で不合格。製品Editor限定の確認は61個のTests.csを除いた補助結果であり、全Editorやテストの成功ではない。現行checkoutのnormal.table実績は、別のRuntime/Scene変更も含むためこのPRだけの実行成功証拠には使わない。
