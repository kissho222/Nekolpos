# 夜の日記記入とPending

`DiaryJournalStore` は、カレンダーへ確定済みの本文とは別に、通常イベントの草稿と特別日記のPendingを `PlayerPrefs` へ保存する。

- `SystemTimedEvent` 完了時、`DialogueManager` は発生時点のゲーム内日・時間帯・順番と、CSVから解決済みの本文を記録する。
- CSVの `diary_id` は日記識別子、`diary_kind` が `special` の行は特別Pendingになる。空欄は従来どおり通常草稿で、`key` をDiaryIdとして使う。
- 死亡フローは時間を翌朝へ送る前に特別Pendingを作るため、夜を飛ばしても消えない。
- `DiaryCalendarController.BeginNightDiary()` は、通常草稿では日記アプリ外の `PromptParentPanel` で朝・昼・夕方を選ばせる。選択後、またはPendingがある場合だけ `WritingView` を開き、本文を音声入力風に文字送りする。
- 本文は `WritingFinishButton` による終了時にだけカレンダーへ確定保存する。`ReWritingButton` は表示中の未確定本文、音声入力表示、完了／書き直し操作を初期化して選択UIへ戻すため、通常草稿・Pendingを失わない。`PromptParentPanel` はWritingViewの子ではないので、WritingViewを閉じた後でも選択UIだけを正しく再表示できる。
- Pendingは全件が確定保存された時だけ削除する。記入中断時や書き直し時は残る。

通常の `DiaryIcon` はカレンダー閲覧専用であり、夜記入画面 `DiaryPanel/WritingView` は夜遷移だけが開く。
