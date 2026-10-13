# 日記カレンダー

OPスキップ開始時も `NekolposGameBootstrap.StartNormalGame()` から既存の `PlayGameBgmNow()` を呼び、タイトルBGMをフェードアウトしてゲーム中BGMへ切り替える。通常OPの猫への呼びかけ時と同じ切り替え処理・フェード時間を使う。

`TimeManager` がゲーム1日目のPCローカル日付を `PlayerPrefs` に保存し、以後は `CurrentDay` から `GameStartDate + (CurrentDay - 1)` としてゲーム内年月日を算出する。既存セーブに開始日がない場合だけ、その最初の起動日に確定する。後からPC日時を変えても保存済みの開始日は変わらない。

日数も `TimeManager` を正本とし、既存の `Nekolpos.Debug.StartDay.v1` キーを通常プレイとDebug Startで共有する（Issue #31）。日送り・日数の明示変更時に保存し、未保存・0以下・日付範囲を超える値はDay1へ戻す。タイトルは有効な保存日数がある場合だけ「オープニングをスキップする」チェックボックスを表示する。初期OFFで選択は永続化しない。「会話デモを始める」は未チェック時に名前付け→猫への呼びかけ→OPを実行し、チェック時は保存済みの名前を使って名前付けとOPを省略し、保存日のMorningでOPの最終配置と通常カメラへ戻す。開始時にも保存日数を再確認し、保存なしではスキップできない。保存日数・日記は消去しない。Debug Startはチェック状態によらず従来どおり名前付けとOPをスキップする。OpenBetaIntroPopupPanelが既存タイトルのフォントと開始ボタン位置からToggleを実行時生成するため、Sceneの作り直しは不要。横並びボタン群ではレイアウト対象外として開始ボタンの右に配置する。時刻・散歩・タブレット・会話途中の状態は保存しない。日付リセットはDay1を保存し、日記リセットは独立した既存操作として扱う。

タイトル復帰時は `OpenBetaPauseMenuController.ResetOpenBetaState()` に解除を集約する（Issue #33）。BootstrapのTimeline・タブレット復帰を止めてから、会話Sequence・日記記入・アプリ開閉・エリア移動・散歩・Promptを止める。フェードは完了イベントを発火せず透明化し、古い待機処理が後から入力を有効にしない。Chat/Choice/Log、Pause、カーソル、TimeScaleも解除する。確定日記・特別Pending・保存日数は消さない。WalkDialogueCanvasは所有Playerと同じSceneで破棄する。

`NekolposGameBootstrap` は実行時に `DiaryCalendarController` を追加する。`DiaryIcon` はカレンダーを開き、開始月より前・現在のゲーム内月より後へは移動できない。`AddEntry(string)` は今後の夜の記入フロー用の入口で、日付ごとの本文をPlayerPrefsへ保存する。通常のDiaryIconは閲覧だけを開く。

手動でDiaryIconを押した時は、`TabletHomeApplicationController` が管理する共通コンポーネント `TabletApplicationWindowTransition` により、DiaryPanel自体をアイコンの位置・大きさから全画面へ拡大表示する。日記固有の遷移コンポーネントは生成しないため、Nyansta・Shop・demaeと同じ親・同じ開閉経路を使う。途中で別Windowへ切り替えた場合は、遷移中のWindowを開始前の全画面座標・通常スケール・不透明状態へ復帰してから次のWindowを表示し、透明・縮小・移動途中の座標を残さない。

`TabletHomeApplicationController` はホームの `StartPanel` と、`NyanstaIcon → NyanstaPanel`、`ShopIcon → ShopPanel`、`demaeIcon → DemaePanel`、`GameIcon → GameCenterPanel` を管理する。起動時に、StartPanelが誤ってアプリPanelの共通親として保存されている旧Sceneも検出し、StartPanelと各アプリPanelを共通Root直下の全画面RectTransformへ正規化する。対応PanelがSceneに未配置の場合は仮ページと`CloseButton`を生成するため、後から同名PanelをSceneに置けば内容だけを差し替えられる。各CloseButtonとDiaryのCloseButtonは、対象Windowを対応アイコンへ縮小しながらStartPanelへ戻る。開く時の拡大と閉じる時の縮小は、共通の `TabletApplicationWindowTransition` で統一する。

`MainScene > TabletDisplay > DisplayArea > TabletCanvas > Panel > DiaryPanel` には、Sceneで編集できる `CalendarView` と `DetailView` を配置する。基本ページと42セルはScene配置を使い、スクロール領域・肉球装飾・日送り用の本文ページだけを実行時に補う。`CalendarView/DayGrid` の `Day01`〜`Day42`、ヘッダー、ボタン、`DetailView` の本文は、TextMeshProUGUI、RectTransform、Image、Layout GroupをInspectorから調整できる。日付セルは左上に35ptの日付を表示し、日記がある日はその下へ本文先頭12文字（超過時は省略記号付き）を16ptで表示する。タブレット内のテキストは、ZenMaruGothicを元にした動的Bitmap TMPフォント `TabletZenMaruGothicDynamicBitmap` を使用する。SDFの追加アトラスによる豆腐化を避け、日本語・中国語の未収録文字は登録済みフォールバックで補う。

`OpenBetaCanvas/TabletButton` は、タブレット閲覧カメラを開く専用UIである。`UIStyle.ApplyTree` の一括ボタン変換・子テキスト色変換の対象外とし、Sceneで調整された画像、色、Selectable遷移をそのまま保持する。

`DetailView/DetailTitle` は選択日の月日を `M/dの日記`（例: `9/21の日記`）として表示する。`BeforeDayButton` と `NextDayButton` は、記録済みの前後の日記へ移動する。間に記録がない日は飛ばし、移動先がない端では該当ボタンを無効化する。

特別日記を初めて取得すると、`DiaryJournalStore` が保存済みの解禁フラグを立て、`CalendarView` の肉球付き `SpecialDiarySortButton` を表示する。通常時の月間表示は維持する。ボタンをONにすると既存の42セルを左から右へ再利用し、特別日記の保存本文をOrdinal完全一致（空白・改行も区別、IDでは判定しない）で集約し、各本文の最初の日付にだけ日付ごとに1セルずつ表示する。同一本文の追加記録数を初日のセルに合算し、重複があればCalendarViewは「10/10 他3件」、DetailViewは「10/10 他3件の日記」とする（他3件は初回を除いた3記録）。重複がなければ従来の日付表記を維持する。同日複数件はセルを増やさず、セル右上の肉球（`Assets/Picture/肉球マーク.png` を実行時用に `Assets/Resources/Diary/肉球マーク.png` へ同梱）だけで示す。特別ソートでDetailViewを開いた場合は、その日が初回となる特別日記本文だけを記録順で連結し、日送りは重複集約後の日付リストの前後へ移動する。重複本文と異なる本文が混在する後日には、異なる本文だけを表示する。保存記録や通常の月間Calendar/Detail表示は変更しない。DetailViewからCalendarViewへ戻る時もソート状態は保つ。

`TEMP_SPECIAL_DIARY_001` は特別日記の表示確認用データである。`DiaryCalendarController` のInspectorコンテキストメニュー `Add Temporary Special Diary` は、現在のゲーム内日付へこの仮データを追加して、解禁・肉球・ソート・日送りをまとめて確認する。通常のゲーム開始時には自動追加しない。

10月への月またぎ確認用として、Debug Start時だけ作られる `NekolposDebugPanel` に `10/1 特別日記を追加` を置く。同操作は `TEMP_SPECIAL_DIARY_OCT01` を追加し、保存済みの `GameStartDate` から次の10月1日となるDay Numberを算出して既存の `TimeManager.SetCurrentTime` で時刻を合わせる。PC実日時を読まず、通常ゲーム開始時には日記を追加しない。Inspectorの `Add Debug October 1 Special Diary` も同じデバッグ用入口である。

Debug Startでも、夜日記の通常草稿を `WritingFinishButton` で確定すると、選んだ日記を保存してカレンダーへ戻る。タブレット終了後の入眠会話と暗転を経て内部Dayを翌朝へ進める。そのDayは `Nekolpos.Debug.StartDay.v1` に保存され、次回のDebug Startも翌Dayの朝から始まる。確定せずに日記を閉じるかPlayを終了した場合は、そのDayの未記入の朝・昼・夕草稿を破棄する。特別日記のPendingは従来どおり後日の夜まで保持する。

`NekolposDebugPanel` の `日付リセット` はDebug StartのDayを1へ戻し、ゲーム内Day 1の日付を実行PCの当日へ再設定する。`日記リセット` は保存済みの通常・特別日記、未記入草稿、特別日記ソート解禁を削除する。どちらもDebug Start中だけ使用する開発用操作である。

夜専用の記入画面は同じ `DiaryPanel/WritingView` に配置する。時間帯を選ぶ `PromptParentPanel` は `DiaryPanel` 直下に分離され、日記アプリUIではなくプレイヤーの思考UIとして扱う。`WritingBody`、`MicrophoneIndicator`、`WritingFinishButton`、`ReWritingButton` はEditorで調整できる。通常の `DiaryIcon` はこの画面を開かず、夜の `BeginNightDiary()` だけが選択UIまたは特別Pending用の記入画面を開く。

現在のSceneでは `WritingView` をDiaryPanelのEditorプレビューとして有効にしている。Play時は `DiaryCalendarController` が必要なViewだけを切り替えるため、プレビュー状態は実行時の挙動に影響しない。

`CalendarView`、`DetailView`、`WritingView` は共通の横スライドで切り替える。次の画面は右側から入り、現在画面は左側へ出る。戻る操作はこの逆方向であり、`Mathf.SmoothStep` による加減速を使用する。`DiaryPanel` は `RectMask2D` を遷移用の表示領域として持つため、移動中のViewや日送りページはタブレット画面の範囲外へ描画されない。遷移中は双方の`CanvasGroup`で入力とRaycastを止め、途中中断時も位置・可視状態・操作状態を正規化する。

夜の日記はまず`CalendarView`を表示し、通常草稿の選択または特別Pendingの開始時に`WritingView`へ右方向に進む。確定保存後は`WritingView`から`CalendarView`へ左方向に戻る。

DetailViewの日送りは画面全体を動かさない。配置済みの`DetailBackImage`（背景と`DetailBody`を含むPrefabインスタンス）だけを複製して前後ページとして横スライドさせ、`DetailTitle`・戻るボタン・日送りボタンなどの固定UIはその場に残す。日送り中はDetailView全体を入力不可にし、完了時に旧ページを破棄して新ページを通常位置へ正規化する。


夜記入の通常日記確定は通常/Debug共通でカレンダーへ戻り、タブレット終了後の入眠会話・暗転・翌朝への移動・明転・朝の挨拶へ続く。死亡時は日記を書かず翌朝へ進み、死亡した翌日の夜に特別日記をまとめて書く。特別日記は書き直し不可、決定後に同日の通常日記記入へ進む。日付変更時のTable復帰と変数展開の詳細は [夜の日記記入](DiaryJournal.md) を参照。

特別日記ソート中も各セルに本文先頭12文字を表示する。詳細ページは通常/特別ソート共通で、その日に特別日記があればDetailViewの画面右上にScene配置したSpecialDiaryPaw（120×120、右/上余白60）を表示し、通常だけの日には非表示にする。肉球・記入画面の罫線・見出し・マイク装飾、DiaryPanel直下のベータ版案内等の表示専用テキストはRaycastを遮らない。記入完了/書き直しボタンはWritingView内の最前面へ配置し、絵の透明部やラベル表示に左右されないボタン全体の透明HitAreaを持つ。

WritingBodyとDetailBodyには既存の本文領域を維持した縦ScrollRectとRectMask2Dを実行時に追加し、複数件の本文は高さを自動計算して画面内でスクロールできる。マウスホイール・ドラッグ・タッチに対応し、文字送りは末尾へ追従、詳細ページを開く/日送りする時は先頭へ戻す。固定タイトル・肉球・操作ボタンはスクロールしない。

保存済みOPスキップの最初の会話はBasicSystemDialogueの朝挨拶を使用し、呼びかけ正誤のOPレスポンスを再生しない。

タイトルのOPスキップ選択はPlayerPrefsの`Nekolpos.Title.SkipOpening`にON/OFFを即時保存し、タイトル再表示・次回起動で復元する。初回はOFF。ゲーム進行の保存がない間は選択肢を隠してスキップを禁止するが、選択の記憶は消さない。

## 日記の全変数展開（2026-10-05）

日記本文は既存DialogueManager.ResolveRuntimeTextを共通入口として、名前・呼称・一人称・性別・親の呼び方、改名候補PENDING_VALUE、会話のRuntimePlaceholders（PLAYER_INPUT等）、時間・天気・予報の既存別名、ConversationGameStateの文字列／整数／真偽値／リストを展開する。Formatter不在でも既存ChatUI・RenameSystem・世界状態・GameStateから取得する。変数を追加した時は固定の名前リストへ追記する必要がない。

構文処理はConversationVariableResolver.ReplaceRuntimePlaceholdersで共通化。{{KEY}}、空白・大小文字・既存CamelCase別名、{KEY}、Markdownでエスケープされた波括弧・アンダースコアに対応する。例: {{PLAYER\_CALLING}}も呼称へ展開する。値は一回だけ置換し、値に含まれる別トークンを再展開しない。未定義・取得不能な変数と壊れた構文は架空の値で補わず保持する。TMPの既存書式・改行は維持する。

草稿・Pending・記入・確定保存・詳細・画面遷移に加え、通常カレンダーの保存済み本文プレビューでも展開する。保存形式の変更や過去日記の一括書換えは行わず、残存変数を表示時に解決する。新しい日記本文・台詞は生成しない。

検証: 正規Unity Runner関連93/93成功、失敗0/Skip0（2026-10-05）。名前6項目×構文5種、Runtime提供変数と天気別名、全GameState値型、未知/破損構文、置換値の再展開抑止、既存日記・会話・UI・Weatherを回帰確認。最終Console Error0。Windows再Buildと実Player/タブレット実操作は未実施。

Issue #8のユーザーレビューにより、個別日記の肉球をタイトル内36×36から画面右上120×120へ変更。固定ImageはMainSceneを正本とし、DiaryCalendarController.detailSpecialPawは表示切替だけを担当する。位置/サイズはInspectorから調整でき、本文スクロールや日送りの影響を受けない。日付セルとソートボタンの肉球は従来どおり。
