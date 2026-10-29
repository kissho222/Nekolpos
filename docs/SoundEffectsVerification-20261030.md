# Issue #81 SE基盤の検証状況

2026-10-30 JST。**Issueは未完了、PRはDraft、`codex-working`を継続する。** 限定NativeHelper検証で既存MainSceneのメモリーhashが変化し、原因を特定できないためUnity操作を停止した。Sceneはdirty=trueのまま保存・推測復元していない。ユーザーの意図するScene状態を確認する必要がある。

## 実装と依存範囲

素材未登録でも無音で動く床材・移動SE・猫ボイス基盤を、既存SettingsManager／Mixer（#38）と音量プレビュー（#61）へ接続する。足音・ジャンプ・着地はSE、猫声はCatVoiceへ通知し、常駐マネージャーや通常会話への一括発火を追加しない。仕様は [SoundEffects.md](SoundEffects.md) を参照。

移動SEの無効中API拒否、Teleport／Disable時Reset、一時停止時Suspendを補い、base版PlayerSafetyへ実水平変位・既存床検索・成立した上向きジャンプのhookだけを追加した。壁に向かう入力や安全復帰距離は歩行音に数えない。音源Pause所有権と停止前の滞空履歴を区別する。

新EditorのSoundEffectsSetupActionsは対象を事前検証し、不足componentだけをUndo付きで追加する。既存値・null・空配列を上書きせず、新規床材だけを未確定／無音Profileで作る。成功後のUndoは追加componentを戻し、独立Profile Assetは残す。Sceneを保存しない。SoundEffectsInspectorsは既存のInspectorName／Tooltipを明示GUIContentへ変換し、通常field・nestedカテゴリ・配列を日本語表示する。標準PropertyFieldによる編集を維持し、複数選択時の配列サイズ変更と猫声Previewを無効化する。

baseは `930b607317b1755aa3c9070855cec3301e6b9a7c`。baseの210 Runtime CSへ新4 CSを加え、PlayerSafetyだけを局所変更した独立候補である。PlayerSafetyの音声hookを逆置換するとbase全文と改行正規化後に一致し、既存30メソッド中25は不変。既存2回の床検索を再利用し、新しいSphereCastや現checkoutの別物理拡張を混入させていない。

現checkoutは他作業を含むため、そこでのUnity結果をPR単独の実行成功とは扱わない。base自身のSettingsManager／Mixer／プレビュー基盤を確認し、現checkoutのゲームDLLを参照せず条件コンパイルした。既知PR #95～#98との変更ファイル重複は調査時点でHANDOFFのみ。MainScene大差分・音素材・旧CatFootstep・FootContact・Tablet・Hockey・Pettingは今回のPR対象外である。

## 外部検証とコンパイル

| 対象 | 結果 |
| --- | --- |
| Audio 4 Runtime CS全文 | メモリー67/67成功。変更前の負の対照で期待10件失敗を検出（旧挙動5件、新API不在5件）。 |
| PlayerSafetyの通知境界 | base候補27/27、現checkout27/27を別々に成功。公開入口でもbase候補27/27成功。実メソッドを無加工抽出し、物理／入力／Transform／音声通知は代役。 |
| Setup helper全文 | メモリー44/44成功。既存非空／空／null保持、inactive親、OP／通常猫区別、拒否・巻戻し・再実行を確認。 |
| 検証ツール自体 | Audioの合成13件、Setupの最終manifest書込／成功print失敗注入2件成功。Setupの例外時bool修正後は合成2件のみ確認し、製品44件は再実行していない。 |
| base Runtime＋今回分 | 214 CSで成功。Windows／WebGLのRelease／Development各条件でbase・候補ともError 0。 |
| base製品Editor＋今回分 | 最終74 CS、Error 0／Warning 1。全9候補CSのhashを記録。テストsourceを除いた製品Editorの確認である。 |
| 全Editor | base・候補ともPlayerWalkModeControllerTestsのWrightAngle未解決8エラーで失敗。除外して全体成功とは扱わない。 |
| 現checkoutの実ロードソース | Runtime269／Editor154の全PDB source checksumと現ソース一致、PDB／DLL対応とlive MVID一致を確認。これはbase候補の通し実行証明ではない。 |

外部ツールは `.codex/tools/movement_audio_checks/`、`player_movement_audio_checks/`、`sound_effects_setup_checks/`。Python 3.10以上／.NET SDK 10が必要で、通常保存・実音・Unityを使わず新規outputへ記録する。代役による成功は実物理・実Undo・実音の成功に読み替えない。条件コンパイルもUnity Player Buildではない。

## 限定Native検証

既存EditorのEditModeで、所有するPreviewScene・一時component・無音PCM／muteだけを使った。SettingsManager初期化、通常設定／保存API、実素材Preview、Play、正式NUnit／workerは実行していない。実Mixer4group・4exposed parameterは確認したが、EditModeのRouteAudioはno-opなのでRuntime自動接続の実証ではない。

| 試行 | 機能と保全の結果 |
| --- | --- |
| Audio初回 | 12チェック成功後、inactive親の床材をRuntimeで探索するfixture期待に失敗。実接地Colliderに合わせactive fixtureへ修正した。既存Scene memory／disk、全保存・Assets・Prefsは差分0。失敗記録は保持。 |
| Audio2回目 | 21チェック成功後、通常fieldの日本語表示に失敗。InspectorNameがあってもSerializedProperty表示は英語だった。専用Editorを追加した。保全差分0。失敗記録は保持。 |
| Audio最終 | **132/132成功**。27個の実GUIContentと対応custom Editor登録を確認。一時物0、Scene memory／dirty状態維持。全disk保存・Assets・Prefs監査も差分0／preserved=true。 |
| Setup helper | 局所**31チェック成功**、Undo件数1→1／Redo0→0、一時物0・cleanup成功。ただしMainScene memory hashが **92ad740c…→5f16b239…** と不一致で、**全体passed=false／scenePreserved=false**。Scene一覧・dirty=true・root32件は同じ。全disk保存・Assets・Prefs監査は差分0／preserved=trueだが、メモリー保全成功とは扱わない。 |

Setup helperの不一致は独立読取レビューでも原因を特定できなかった。具体的なfield差分を復元できる前状態がなく、実試験が原因かという断定もできない。Undo全消去、Scene保存、reopen、推測復元、追加Unity試験は行わず停止した。**diskが不変でも、ユーザーの未保存Sceneを保全したと断言できないことがDraft継続理由である。**

## 判明した問題と対応

helperの初案では旧CatFootstep所有者を通常猫と仮定していたが、実読取で所有者はOP_Cat Bと判明した。誤対象へのapply前に止め、bootstrap.normalCatRoot参照優先／一意のNormal_Cat＋root Animatorへ修正した。子Animatorが複数ある正常構成を許容し、OP／通常同一参照や複数候補は拒否する。現Sceneの読取では最終「不足なし」を確認した。

Editor helperはinactive対象も扱うため、親床材探索にincludeInactive=trueを明示した。既存null／空Profileを見落として子へ追加しない44件の回帰と、旧helperが同fixtureで失敗する負の対照を残した。日本語Inspectorはソース属性だけで合格とせず、専用EditorのGUIContentと登録を実Unityで確認した。ただしユーザーによる画面評価・配列編集・Undo／Prefab overrideの全操作試験は別途必要。

## 残件とユーザー確認

- 最優先は現在の未保存MainSceneの意図確認。原因未特定のまま保存・復元やUnity操作を再開しない。Issueは未完了、codex-workingのまま、Draft PRとしてレビューする。
- base Sceneへ不足追加操作は未適用。現checkoutに接続があることを、base Scene導入済みとは扱わない。
- 通常猫の四足接地・姿勢確定後通知は未接続。旧CatFootstep経路は維持し、汎用接続口だけを用意する。
- 実聴感、Runtime Master／SE／CatVoice Mixer反映、Pause聴感、実移動・段差・斜面・着地、Timelineの演出、今回構成のPlayer Build／端末、正式9テスト／workerは未検証。
- 既存の机5音、着地1音、嬉しい／不満各1音、その他空欄を維持する。音素材・床材・音量／音程／タイミング・四足校正・演出はユーザー判断。Editor試聴はRuntime Mixerの受入試験を代替しない。

機密値を含まない集計・ソースhash・証跡hashは [evidence](SoundEffectsVerification-20261030.evidence.json) に記録した。ローカルoutput参照は保存証跡の場所であり公開URLではない。Scene／Prefab／会話CSVの保存変更、Issue Close、mainへの直接mergeは行っていない。
