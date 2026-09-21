# 日記カレンダー

`TimeManager` がゲーム1日目のPCローカル日付を `PlayerPrefs` に保存し、以後は `CurrentDay` から `GameStartDate + (CurrentDay - 1)` としてゲーム内年月日を算出する。既存セーブに開始日がない場合だけ、その最初の起動日に確定する。後からPC日時を変えても保存済みの開始日は変わらない。

`OpenBetaTitleBootstrap` は実行時に `DiaryCalendarController` を追加する。`DiaryIcon` はカレンダーを開き、開始月より前・現在のゲーム内月より後へは移動できない。`AddEntry(string)` は今後の夜の記入フロー用の入口で、日付ごとの本文をPlayerPrefsへ保存する。通常のDiaryIconは閲覧だけを開く。

手動でDiaryIconを押した時は、`TabletHomeApplicationController` が管理する共通コンポーネント `TabletApplicationWindowTransition` により、DiaryPanel自体をアイコンの位置・大きさから全画面へ拡大表示する。日記固有の遷移コンポーネントは生成しないため、Nyansta・Shop・demaeと同じ親・同じ開閉経路を使う。途中で別Windowへ切り替えた場合は、遷移中のWindowを開始前の全画面座標・通常スケール・不透明状態へ復帰してから次のWindowを表示し、透明・縮小・移動途中の座標を残さない。

`TabletHomeApplicationController` はホームの `StartPanel` と、`NyanstaIcon → NyanstaPanel`、`ShopIcon → ShopPanel`、`demaeIcon → DemaePanel` を管理する。起動時に、StartPanelが誤ってアプリPanelの共通親として保存されている旧Sceneも検出し、StartPanelと各アプリPanelを共通Root直下の全画面RectTransformへ正規化する。対応PanelがSceneに未配置の場合は仮ページと`CloseButton`を生成するため、後から同名PanelをSceneに置けば内容だけを差し替えられる。各CloseButtonとDiaryのCloseButtonは、対象Windowを対応アイコンへ縮小しながらStartPanelへ戻る。開く時の拡大と閉じる時の縮小は、共通の `TabletApplicationWindowTransition` で統一する。

`TitleScene > TabletDisplay > DisplayArea > TabletCanvas > Panel > DiaryPanel` には、Sceneで編集できる `CalendarView` と `DetailView` を配置する。表示物は実行時に生成・破棄しない。`CalendarView/DayGrid` の `Day01`〜`Day42`、ヘッダー、ボタン、`DetailView` の本文は、TextMeshProUGUI、RectTransform、Image、Layout GroupをInspectorから調整できる。日付セルは左上に35ptの日付を表示し、日記がある日はその下へ本文先頭12文字（超過時は省略記号付き）を16ptで表示する。タブレット内のテキストは、ZenMaruGothicを元にした動的Bitmap TMPフォント `TabletZenMaruGothicDynamicBitmap` を使用する。SDFの追加アトラスによる豆腐化を避け、日本語・中国語の未収録文字は登録済みフォールバックで補う。

`OpenBetaCanvas/TabletButton` は、タブレット閲覧カメラを開く専用UIである。`UIStyle.ApplyTree` の一括ボタン変換・子テキスト色変換の対象外とし、Sceneで調整された画像、色、Selectable遷移をそのまま保持する。

`DetailView/DetailTitle` は選択日の月日を `M/dの日記`（例: `9/21の日記`）として表示する。`BeforeDayButton` と `NextDayButton` は、記録済みの前後の日記へ移動する。間に記録がない日は飛ばし、移動先がない端では該当ボタンを無効化する。

特別日記を初めて取得すると、`DiaryJournalStore` が保存済みの解禁フラグを立て、`CalendarView` の肉球付き `SpecialDiarySortButton` を表示する。通常時の月間表示は維持する。ボタンをONにすると既存の42セルを左から右へ再利用し、特別日記がある日だけを日付ごとに1セルずつ表示する。同日複数件はセルを増やさず、セル右上の肉球（`Assets/Picture/肉球マーク.png` を実行時用に `Assets/Resources/Diary/肉球マーク.png` へ同梱）だけで示す。特別ソートでDetailViewを開いた場合は、その日の特別日記だけを記録順で連結し、日送りは特別日記の日付リストの前後へ移動する。DetailViewからCalendarViewへ戻る時もソート状態は保つ。

`TEMP_SPECIAL_DIARY_001` は特別日記の表示確認用データである。`DiaryCalendarController` のInspectorコンテキストメニュー `Add Temporary Special Diary` は、現在のゲーム内日付へこの仮データを追加して、解禁・肉球・ソート・日送りをまとめて確認する。通常のゲーム開始時には自動追加しない。

夜専用の記入画面は同じ `DiaryPanel/WritingView` に配置する。時間帯を選ぶ `PromptParentPanel` は `DiaryPanel` 直下に分離され、日記アプリUIではなくプレイヤーの思考UIとして扱う。`WritingBody`、`MicrophoneIndicator`、`WritingFinishButton`、`ReWritingButton` はEditorで調整できる。通常の `DiaryIcon` はこの画面を開かず、夜の `BeginNightDiary()` だけが選択UIまたは特別Pending用の記入画面を開く。

現在のSceneでは `WritingView` をDiaryPanelのEditorプレビューとして有効にしている。Play時は `DiaryCalendarController` が必要なViewだけを切り替えるため、プレビュー状態は実行時の挙動に影響しない。

`CalendarView`、`DetailView`、`WritingView` は共通の横スライドで切り替える。次の画面は右側から入り、現在画面は左側へ出る。戻る操作はこの逆方向であり、`Mathf.SmoothStep` による加減速を使用する。`DiaryPanel` は `RectMask2D` を遷移用の表示領域として持つため、移動中のViewや日送りページはタブレット画面の範囲外へ描画されない。遷移中は双方の`CanvasGroup`で入力とRaycastを止め、途中中断時も位置・可視状態・操作状態を正規化する。

夜の日記はまず`CalendarView`を表示し、通常草稿の選択または特別Pendingの開始時に`WritingView`へ右方向に進む。確定保存後は`WritingView`から`CalendarView`へ左方向に戻る。

DetailViewの日送りは画面全体を動かさない。配置済みの`DetailBackImage`（背景と`DetailBody`を含むPrefabインスタンス）だけを複製して前後ページとして横スライドさせ、`DetailTitle`・戻るボタン・日送りボタンなどの固定UIはその場に残す。日送り中はDetailView全体を入力不可にし、完了時に旧ページを破棄して新ページを通常位置へ正規化する。
