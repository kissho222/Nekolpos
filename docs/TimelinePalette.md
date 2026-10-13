# Timeline演出パレット（Issue #58）

`Window > Nekolpos > Timeline Palette` を開き、Timelineウィンドウで編集対象と再生ヘッドを指定してSignal名をクリックする。名前・パス検索とカテゴリ絞り込みに対応する。追加先は表示中のTimelineであり、Project選択中の別アセットには追加しない。DirectorなしのAsset編集でも利用できる。子Timelineの時刻は公開APIのLocal時刻を使用する。

## 既存構成と対象

AssetDatabaseでAssets内のSignalAssetを自動収集する。2026-10-14時点で5件: FadeToBlack / FadeFromBlack（暗転・明転）、ApplyLocationChange（場所変更）、SwitchToNormalCatSignal / PlayConversationTimeline（猫・会話）。MainSceneの既存ReceiverにはFadeToBlack、ApplyLocationChange、FadeFromBlack、SwitchToNormalCatの接続がある。既存Editor拡張は会話Sequence設定、演出Scene設定、Avatar Mask生成などであり、Signal配置用パレットは存在しなかった。視線・UI専用Signalは現在ないため作成しない。

一覧は起動時・Project変更時・更新ボタンで再取得し、手動登録を不要にする。カテゴリはSignal名のfade/look/gaze/ui/location/cat/conversationを基準に分類し、未知名は「その他」とする。新しいSignalのカテゴリ登録は不要。確認ボタンでProjectアセットをPingする。

## 配置・安全性

- 追加先指定がある場合はそのSignal Trackを使用。自動では、既存DirectorのBinding先Receiverが対象Signalを登録済みのTrackを優先し、次に使用可能な既存Track、最後に新規Signals Trackを作成する。
- 親Groupを含めロック・ミュート中のTrackは自動選択しない。明示指定された使用不可Trackや別TimelineのTrackは変更前に拒否する。
- ReceiverのReaction・DirectorのBinding・既存Markerの設定は変更しない。新規TrackはBinding未設定なのでユーザーが設定する。配置だけでは反応の実行を保証しない。
- Play・コンパイル中、編集不可Asset、非有限・負時刻、参照欠落を拒否する。追加したEmitterと必要な新規Trackは一括Undo/Redo可能。自動保存は行わず、通常のUnity保存を使う。
- SignalEmitterのRetroactive/Emit OnceはUnityの既定値を維持する。Scene・Runtimeコード・会話本文は変更しない。

## 公開APIと拡張範囲

現行PackageCacheのTimelineEditor、TimelineEditorWindow、TimelinePlaybackControlsを確認した。再生ヘッドは`GetCurrentTime(Context.Local)`で取得できる。ドロップ位置から時刻への変換は内部TimelineHelpers/WindowStateに依存するため、直接ドラッグ配置は実装せず、Issueで許容された再生ヘッドへのクリック配置を正式仕様とする。内部API・Reflectionには依存しない。

Windowの一覧・分類と配置処理を分離している。Animation/Audio/カメラ等は今後それぞれの配置処理を追加できるが、今回の実装範囲はSignalのみ。新規C#はEditor専用のNekolposTimelinePaletteとその振る舞いテストであり、Playerへ含まれない。

## 検証結果

正式Unity EditMode Runnerで10/10成功、失敗・Skip0。配置時刻、実TimelineウィンドウのLocal再生ヘッド、Track作成/再利用/指定、Undo/Redo、既存Receiver/Binding/Marker保全、不正時刻・Signal欠落・別Timeline/ミュート/親ロックを確認した。Unityでパレットを開きConsole Error0、MainScene dirty=false・Play停止を確認。実マウスによる一覧検索/配置と演出再生の見た目は手動確認対象。
