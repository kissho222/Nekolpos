# 時間経過の共通レール（Issue #60）

## 責務と入口

`TimeFlowCoordinator.cs`は時間経過の排他所有・夜境界へのクランプ・日記/挨拶/後続会話/入力復帰の優先順位を管理する通常C#クラス。既存`DialogueManager.RequestTimeAdvanceAsync(TimeAdvanceRequest, presentation, followUp)`がSceneの暗転・結果表示・キャンセル・会話/日記UIを接続する。新しいScene Managerや固定UIは作らない。`TimeManager`は時計・天気・照明/状態通知を保持する。自身のGameManager同期中は状態変更通知を取り込み直さず、分単位の時刻と通知回数を保持する。

正式入口はSystemTimedEvent（場所移動/5会話結果を含む）、FoodHungry完了、Teaching時間経過、ActionManagerの選択Action/YarnのAction実行、Debugの時間帯送り/日付送り。各入口は分数/理由/結果表示/必要な後続会話を渡し、時間帯を判定しない。ActionExecutorは効果と結果本文だけを実行し、時計を変更しない。旧StateのSpace/E/N/Xによる直通遷移と、未使用StateDeadの自動翌朝遷移を撤去。正式死亡入口は既存DialogueManagerへ渡す。

## 順序・夜ゲート

通常は入力抑制/表示終了→暗転→時計1回更新→結果演出→明転→昼/夕の挨拶→イベント固有後続→入力復帰。5会話ごとの結果は既存どおり結果表示の後に時計を更新する。操作の同時要求は拒否し、翌日へキューを持ち越さない。Title再開/Destroyは共通tokenをキャンセルする。

どの入口も夜に到達したら当日Night境界で止まる。要求の残り時間は破棄する。夜の挨拶・後続Pattern・通常入力復帰を実行せず、夜日記を当日1回だけ開始する。後続Patternは、その日の行動に対応するため翌日へ持ち越さず破棄する。すでに開始済みの夜への要求は時計/UI/結果を再実行しない。日記UIが欠落している場合は明示エラーを出し、通常入力へ逃がさない。

夜Stateは照明と状態同期のみ担当し、Greeting抑制で日記を止める問題や日記二重起動を避ける。日記後は既存Issue #41のタブレット終了→`greeting_after_diary`→完全暗転3秒→翌朝→朝挨拶を維持する。

## 明示的な例外

死亡は`RecordPlayerDefeatDiaryPending`→死亡演出→`AdvanceToNextMorning`を維持し、夜日記を開かない。Pendingは翌日以降の日記へ残す。就寝後の翌朝更新も明示的な別経路。新規開始/保存朝復元、開発用の絶対時刻指定・日付巻き戻し、日記の開発日付指定は時間経過イベントではなくデータ設定として`SetCurrentTime`を保持する。正式な進行では`AdvanceTime`や`ChangeState(new StateNight(...))`を直接呼ばず共通窓口へ依頼する。

## 検証

TimeFlowCoordinatorTestsで全理由の長時間要求、時計通知1回、排他、昼/夕の挨拶優先、夜日記1回・後続/入力抑制、入力不正、およびMainSceneの各入口を確認する。既存Weather/会話間隔/日記就寝/死亡Pendingの回帰を併用する。実施結果と実機未確認範囲はHANDOFFへ記録する。

Issue #71の自由単語学習は時間を進めない。旧unknown_word_teach_event ActionもWordLearningControllerへの入口へ変更し、SystemTimedEventの120分定義は互換データとして残すが単語学習から実行しない。既存TeachingModeの時間進行は維持する。
