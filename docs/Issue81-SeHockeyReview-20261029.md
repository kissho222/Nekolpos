# Issue #81 SE基盤とホッケー衝突音の候補調査

2026-10-29 JST。今回の直接依頼は候補検討・依存確認・独立PRでの報告。ゲーム実装は変更していない。

## 結論

ホッケーの壁際で、同じ物理更新内にマレット音と壁音の要求が続く経路を確認した。現行C#を用いた外部計算でも上下壁際の2ケースを再現した。ただし実プレイ・実音は未確認で、報告された聞こえ方の原因や仕様違反までは確定していない。中央のマレット衝突まで常に壁音を鳴らす誤配線は、調査した経路では見つからなかった。

SE基盤全体とホッケーの主要ファイルは既定ブランチへ未統合で、別チャットが同じcheckoutでWindows版の検証を進めている。未検証依存のある実装を開始しない指示に従い、今回は調査文書・証跡・HANDOFFだけのPRとする。

## 対象版とIssue

- ローカルHEAD: `ea13005bed6941ce6c5393c75bab9ab022c51039`。多数の未コミット・未追跡変更を含むため、HEADだけでは調査対象を表さない。
- PR base: `chore/ai-handoff-foundation` の `930b607317b1755aa3c9070855cec3301e6b9a7c`。originの既定ブランチであり、`main` refは存在しない。既定ブランチへ直接コミット・マージしない。
- 実Unity版は `ProjectSettings/ProjectVersion.txt` の6000.3.10f1。Issue本文の2022.3 LTSとは異なる。
- [#81](https://github.com/kissho222/Nekolpos/issues/81) はOpen・`codex-working`、更新日時2026-10-15 09:27:06 JST、コメント0件。Openかつ`codex-task`の自動指示ではない。今回の直接依頼の資料として参照し、ラベル・本文・状態は変更しない。Openの`codex-task`通常Issueは照会時0件。
- 下記のファイル名・行番号はローカル調査版。実装をこのPRへ同梱したという意味ではない。26入力のSHA-256とbase存在確認、外部計算の結果は [証跡](Issue81-SeHockeyReview-20261029.evidence.json) を正本とする。

## ホッケーの同時通知

`TabletHockeyMatch.Step`（84–105行）は各substepでマレット接触を解決し、その直後に上下・左右の壁を判定する。`HitMallet`（116行）は重なり解消のため、パック中心をマレット中心から半径合計+0.1だけ離す。壁際ではこの移動後のパックが壁境界を越え、反射速度も壁向きになるため、マレット通知に続いて壁通知が出る。

`TabletHockeyController.OnImpact`（340–344行）は1通知につき対応する1種類だけを要求する。`TabletSoundEffectsPlayer.Play`（43行）には両種を排他化する処理がなく、同じ専用AudioSourceへ `PlayOneShot` を順に要求する。実音が重なる可能性はあるが、今回音声出力は実行していない。

設定の取り違えの有無も確認した。

- `GameCenterView.prefab`（8458–8461行）には専用AudioSource参照があり、Player／Catのマレット音・壁音の個別指定は空。共通 `TabletSoundEffects.asset` を使う構成。
- 共通Assetの `mallet` と `wall` は別GUID。8種類すべてに参照が登録済みで、仕様書の「全8種未登録」は初期状態の古い記述。
- 猫が画面を叩く音とパック衝突音は別イベントであり、両方鳴ること自体が既存仕様。マレット音／壁音の重複と混同しない。

### 外部計算の再現条件

現行 `TabletHockeyMatch.cs` を変更せず.NET 9プロジェクトへリンクした。Vector2／Mathfだけを管理コードの計算代替にし、初期状態をreflectionで設定して `Step(0.004f, ...)` を1回呼んだ。Unity・native API・AudioSource・保存APIは実行していない。通常プレイからその状態へ至る入力列を再現した試験ではない。

フィールド1872×1152、パック半径44、マレット半径65、goalHalf384、初速110、加速25。上壁側の例はパック(500,520)、速度(0,-110)、マレット(500,450)。マレット接触でパックy=559.1・速度y=130.96924となり、続く壁判定がy=532・速度y=-130.96924へ戻す。同じ4msのStep内で `PlayerMallet → Wall` を通知した。

|外部fixture|件数|観測した通知|
|---|---:|---|
|中央のマレット接触|1|PlayerMalletのみ1回|
|上壁のみの反射|1|Wallのみ1回|
|上下の壁際でマレット反射|2|各PlayerMallet→Wall|
|継続接触の押し出し・壁から離れる速度|2|両通知なし|
|上下左右の壁外位置・内向き速度|4|Wallなし|

10件とも期待した分岐を確認した。無条件に壁音が追加される問題はこの範囲で再現しなかった。Unityと計算代替の完全同等性、実プレイ、猫側・角・高速連続衝突、聴感、Mixer、Pause、端末は未検証。

ローカル再実行: `dotnet run --project output/issue81-20261029-audit/HockeyAudioAudit.csproj`。この作業領域のharnessを使用するコマンドで、PRにはharness／Runtime原本を同梱しない。証跡には実行時harnessの内容・hashと全結果を収録する。

### 次の修正候補

実プレイで中央・壁際を区別し、鳴った種別と時刻を確認してから選択する。一律のWall抑制は、本当に続けて衝突した壁音も消すため、今回適用しない。

|候補|利点|トレードオフ・開始条件|
|---|---|---|
|同時に要求された衝突SEの優先順位を決める|物理・得点を維持し、聞こえ方だけ調整できる|正当な壁音も減る。ユーザーの音の方針と実音確認が必要|
|壁際の重なり解消・衝突順序を見直す|壁外への押し出し自体を扱える|反射・速度・得点への影響が大きい。独立した物理回帰と未統合ホッケー基盤の分離が必要|

## #81の現行実装・依存

|項目|確認結果|残る条件|
|---|---|---|
|#38の音量管理|Open／codex-reviewed。既存SettingsManager.RouteAudioとMaster/BGM/SE/CatVoice Mixerがbaseに存在|68+1件の旧成功報告。現版の実聴感・端末は未確認|
|#61のプレビュー|Open／codex-reviewed。専用音源・SE間隔制限・猫声ループを既存設定画面で実装|19件の旧成功報告。実機・聴感は未確認|
|移動SE・猫声|床材の明示無音、ジャンプ／着地、4カテゴリ、Pause、Timeline入口がローカルに存在|同等マネージャーを新設しない。現行正式テスト未実施|
|通常猫の足音／#83|#83はOpen／codex-working。MainSceneのcontactObserverはnull、SampleAfterPose呼出はSinglePawContactSessionのみ|四足ボーン校正・地面Layer・姿勢確定後通知が未接続。ゲーム用単足sessionを流用しない|
|Inspector|CatFootstepAudioControllerに英語Header／編集項目、Observer／既定床材の日本語名・Tooltip不足が残る|独立差分にする前にFootContact依存を分離する|
|設定の現物|ジャンプ空、着地1音、呼ぶ／怒り空、嬉しい／不満各1音、旧猫足音2音。机Profile5音・ちゃぶ台空|初期仕様の「机も無音」「全音源なし」と混同しない。素材の採否は変更しない|

[#38の報告](https://github.com/kissho222/Nekolpos/issues/38#issuecomment-5978237494)、[#61の報告](https://github.com/kissho222/Nekolpos/issues/61#issuecomment-6014264433) を取得し、ラベルだけで依存完了と判定していない。

10/15のMovementAudioTests 8/8・Windows Development Build成功は旧版の記録。現在は9テストで、`NEKOLPOS_TEST_WORKER && NEKOLPOS_TEST_REVIEWED_LEGACY` のcompile gateがある。現行9件の正式合格へ読み替えない。`docs/TestSaveIsolation.md` のworker・正式Runner・実保存adapter制限を維持する。別のシナリオへの限定許可をSEの全テスト再開許可に拡張しない。

## 独立PRと他作業の競合

1. baseには移動SE／猫声／床材Profile／MovementAudioTests、ホッケーController／Match、TabletSoundEffects、GameCenterView等が存在しない。2行の音抑制でもbaseに対する単独修正にはならない。
2. CatFootstepAudioControllerの追加接地経路は未追跡FootContactへ依存する。PlayerSafetyMovementControllerにもSE以外の安全移動変更がある。現行ファイル全体の採用でこれらを混入させない。
3. 調査時、別チャット「NEKOLPOS Issue候補を順次評価」が同checkoutで#32のWindowsビルド検証中で、HANDOFFではその後#81を検討予定。今回Assets・Scene・Prefab・Unityを変更せず、追加指示や停止依頼も送っていない。実装再開前に担当範囲と基準snapshotを確定する必要がある。
4. Open PR #94は先行候補調査、#95は制作データチェック、#96はシナリオ保存保護、#84は心理会話データ。今回の新規報告書・証跡は専用名にし、共有HANDOFFには短い現在状態だけを追加する。HANDOFFは他PRと重なるため、取り込み時に各要約を保持して統合する。

次の着手順は、既存基盤と必要差分の分離 → 対象版・検証条件の固定 → ホッケー実音の確認 → 必要な音または物理の変更。通常四足接続・新規マネージャー・全テスト起動へ広げない。

## 今回実施した範囲

GitHub最新情報、現行コード・Scene・Prefab・設定・仕様の読み取り、外部計算10件、入力hash再照合、文書／証跡／PR変更範囲の静的確認。今回のUnity操作・Play・正式NUnit・Player Build・音再生・通常セーブ操作は0件。別チャットのビルド結果を本調査の検証結果に数えない。IssueのClose・進捗ラベル変更・既定ブランチへのマージは行わない。
