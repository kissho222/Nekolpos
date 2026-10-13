# 夜の日記記入とPending

Issue #60の時間経過は`DialogueManager.RequestTimeAdvanceAsync`と`TimeFlowCoordinator`へ集約する。Night到達時は挨拶/後続会話を再生せず日記を優先し、長時間要求も夜の境界で停止する。死亡のPending保存→翌朝直行と日記後のIssue #41就寝は維持する。入口・例外・排他の詳細は`docs/TimeFlow.md`を参照。

`DiaryJournalStore` は、カレンダーへ確定済みの本文とは別に、通常イベントの草稿と特別日記のPendingを既存の進行保存へ保存する。

通常会話からも、Editorの安定DialogueIDへ紐付けた独立日記候補を記録できる。会話開始時のゲーム内日・時間帯を使用し、複数行とリピートによる重複を防ぐ。独立候補を記録した会話では従来TimedEvent日記を重ねず、対象候補がない場合は従来経路を維持する。共有候補、条件、多言語本文、制作CSVと暗号化データの詳細は `docs/DialogueAuthoring.md`、設計判断は `docs/DialogueDiaryDecision.md` を参照。

- `SystemTimedEvent` 完了時、`DialogueManager` は発生時点のゲーム内日・時間帯・順番と、表示本文とは独立した専用日記本文を記録する。
- CSVの `diary_id` は日記識別子、`diary_kind` が `special` の行は特別Pendingになる。空欄は従来どおり通常草稿で、`key` をDiaryIdとして使う。
- 死亡フローは死亡日の特別Pendingを作り、死亡演出後に翌朝へ進む。特別日記は死亡した翌日の夜にPendingを発生日→時間帯（朝・昼・夕方・夜）→発生順で並べ、同日の特別日記だけをまとめて書く。同日の複数本文は「それと……」で連結する。「完成！」で表示日の各Pendingを確定保存して次の日付の特別日記のWritingViewへ進む。異なる日付の本文は連結しない。最後の特別日記の完成後にその夜の当日通常日記へ進む。
- `DiaryCalendarController.BeginNightDiary()` は、通常草稿では日記アプリ外の `PromptParentPanel` で朝・昼・夕方を選ばせる。選択後、またはPendingがある場合だけ `WritingView` を開き、本文を音声入力風に文字送りする。
- 本文は `WritingFinishButton` による終了時にだけカレンダーへ確定保存する。`ReWritingButton` は表示中の未確定本文、音声入力表示、完了／書き直し操作を初期化して選択UIへ戻すため、通常草稿を失わない（特別日記では書き直しを表示・実行しない）。`PromptParentPanel` はWritingViewの子ではないので、WritingViewを閉じた後でも選択UIだけを正しく再表示できる。
- Pendingは表示した日の各件の確定保存後にその対象だけ削除する。特別日記では「書き直す」を非表示にし、未確定の残りは記入中断時も保持する。次の特別日記と最後の特別日記後の通常記入（朝/昼/夕方の草稿選択）ではWritingViewを維持し、PromptParentPanelをWritingViewの上へ表示する。通常記入の完成後にカレンダーへ戻る。
- 記入画面の参照再取得では非表示のButton/InputFieldも操作要素として判定し、装飾のRaycast無効化に巻き込まない。完了操作の再表示時には既存の`DiaryButtonHitArea`のRaycastも復元する。これにより特別日記の再表示後も`WritingFinishButton`を押せる。

WritingView左上のScene配置TMP `WritingDate` は通常・特別共通で `M/dの日記` を表示する。特別はPendingのoccurredDay、通常は選択草稿のoccurredDay、本文がない通常記入や草稿選択待ちは当日のactiveNightDiaryDayを既存TimeManager.GetCalendarDateで日付へ変換する。特別日記ごとに更新し、Runtimeは位置・フォントを変更しない。

通常の `DiaryIcon` はカレンダー閲覧専用であり、夜記入画面 `DiaryPanel/WritingView` は夜遷移だけが開く。


日記本文は死亡Pending登録・文字送り・確定保存・既存日記閲覧でDialogueManager.ResolveRuntimeTextの共通処理を展開する。台詞/日記本文は新規生成しない。通常草稿がない日は空の通常記入画面で決定でき、空本文のEntryは追加しない。通常日記の確定後は通常/Debug共通で当日の未選択草稿を片付け、カレンダーへ戻る。この時点では日付を進めず、タブレットで確定内容を閲覧できる。

タブレット電源を終了し、カメラが作業台へ戻った後、DiaryCalendarController.TryBeginCompletedNightSleepがDialogueManager.TryBeginNightSleepへ引き継ぐ。就寝前会話はBasicSystemDialogue.csvの既存`greeting_after_diary`を使用し、BasicSystemDialogueCatalog.Resolveの条件・優先度・言語選択を再利用する（新規台詞なし）。現行データはこのキー1件で、同じキーの条件別行を追加しても既存選択に従う。入眠会話の送り完了→FadeOutAsync→画面が完全に暗くなってから実時間3秒の待機→AdvanceToNextMorningとTableへの移動→FadeInAsync→既存朝挨拶の順。3秒は暗転開始から数えず、待機中は前日のNightを維持する。重複実行を防ぎ、タイトル再起動・破棄で入眠待ちをキャンセルする。日記アプリのCloseだけでは入眠しない。

BootstrapはTimeManager.OnTimeAdvancedを購読し、日付変更時に散歩・タブレットCamera・会話Timelineを終了してTableへ復帰する。同日で夜へ移った場合は既存DeskのPlayer Destinationへ移動し、夜の挨拶と日記開始前に猫拠点・プレイヤー・カメラを作業台へ揃える。通常の時間経過演出では完全暗転中に確定する。死亡時はその場で翌朝へ進みTableへ復帰し、死亡した翌日の夜の日記ではPending確定後にその日の通常日記記入へ進む。Sceneの追加/台詞生成は行わない。

## 日記の全変数展開（2026-10-05）

日記本文は既存DialogueManager.ResolveRuntimeTextを共通入口として、名前・呼称・一人称・性別・親の呼び方、改名候補PENDING_VALUE、会話のRuntimePlaceholders（PLAYER_INPUT等）、時間・天気・予報の既存別名、ConversationGameStateの文字列／整数／真偽値／リストを展開する。Formatter不在でも既存ChatUI・RenameSystem・世界状態・GameStateから取得する。変数を追加した時は固定の名前リストへ追記する必要がない。

構文処理はConversationVariableResolver.ReplaceRuntimePlaceholdersで共通化。{{KEY}}、空白・大小文字・既存CamelCase別名、{KEY}、Markdownでエスケープされた波括弧・アンダースコアに対応する。例: {{PLAYER\_CALLING}}も呼称へ展開する。値は一回だけ置換し、値に含まれる別トークンを再展開しない。未定義・取得不能な変数と壊れた構文は架空の値で補わず保持する。TMPの既存書式・改行は維持する。

草稿・Pending・記入・確定保存・詳細・画面遷移に加え、通常カレンダーの保存済み本文プレビューでも展開する。保存形式の変更や過去日記の一括書換えは行わず、残存変数を表示時に解決する。新しい日記本文・台詞は生成しない。

検証: 正規Unity Runner関連93/93成功、失敗0/Skip0（2026-10-05）。名前6項目×構文5種、Runtime提供変数と天気別名、全GameState値型、未知/破損構文、置換値の再展開抑止、既存日記・会話・UI・Weatherを回帰確認。最終Console Error0。Windows再Buildと実Player/タブレット実操作は未実施。


## 日記本文Variant（Issue #59、2026-10-07）

SystemTimedEventのdiary_ja/en/zhとdiary_variants、独立候補のja/en/zhとdiary_variantsを共通DiaryTextVariantsで読む。選択方式は群単位のdiary_selection_mode（Random/Sequence、未設定Random）。表示文のパターン数・選択とは独立する。専用本文の無い旧TimedEventだけ表示文へfallbackし、Editor移行後は独立する。空候補は例外にせず記録を行わない。新イベント保存では空の日記本文を拒否し、素材が無いイベントを作らせない。

全経路はDiaryJournalStore.RecordVariantsで記録直前に選択する。Sequenceの次位置は正式なProgressSaveStoreのNekolpos.Diary.VariantSequence.v1へdiary_id単位で保存し、記録成功時だけ進める。通常草稿/特別Pendingと同じ保存境界で更新し、失敗時は戻す。プレビュー・条件不成立・空本文では進めない。候補数変更は剰余で補正、最後の候補から先頭へループする。ProgressSaveStoreの旧PlayerPrefs→進行ファイル移行にも同キーを含める。Debugの日記リセットはこの位置も消去する。夜の記入/Calendar/Detailの既存確定済み本文は変えない。
