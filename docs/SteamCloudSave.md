# 進行セーブとSteam Auto-Cloud

## 保存構成

Windows等のネイティブPlayerでは `Application.persistentDataPath/Saves/progress-v1.json` を進行データの正本とする。現行Company/ProductはCatLover/NekolposなのでWindowsの保存先は `%USERPROFILE%/AppData/LocalLow/CatLover/Nekolpos/Saves/progress-v1.json`。

`Assets/Scripts/System/ProgressSaveStore.cs` はSceneへの配置が不要な保存窓口。既存の保存キーとint/string型を維持し、日数・開始日、日記本文・草稿・特別Pending・ソート解禁、プレイヤー/猫の名前・呼び方・一人称、教えた言葉の発見履歴・件数、死亡理由フラグを保存する。時刻・位置・会話途中の状態は追加保存しない。`ProgressSaveFile` は同ファイル内のファイルバックエンドであり、テスト用に保存先を注入できる。

初回起動時、正本とバックアップが存在しない場合だけPlayerPrefsから移行する。発見履歴の動的キーは既存KnownEntries/KnownSeries一覧から列挙する。既存PlayerPrefsを消去しない。ファイルが存在する場合はファイルを優先し、別PCのPlayerPrefsや削除済みキーを再取り込みしない。過去のバージョンで一覧に記録されなかった動的発見キーは自動列挙できない。

Unity Editorは既存PlayerPrefsを使用し、開発用Play/Testがインストール済みゲームのクラウドセーブを変更しない。WebGLも既存PlayerPrefs/IndexedDBの保存を維持する。WebGLとネイティブPlayerの自動移行は行わない。

## 書き込み・異常系

JSONはversion=1、integers、stringsを持ち、読込時に必須項目と進行キーを検証する。書込みは同じフォルダの `progress-v1.json.tmp` に完了してから正本へ置換する。既存正本は `progress-v1.json.bak` として直前の1世代だけ保持する。正本破損時はバックアップを検証して警告付きで復旧する。両方破損、読書き権限不足、未対応versionは例外で明示し、空セーブへの自動上書きはしない。バックアップはローカル復旧用でクラウド同期しない。

## Steamworks設定

|項目|値|
|---|---|
|Root|`WinAppDataLocalLow`|
|Subdirectory|`CatLover/Nekolpos/Saves`|
|Pattern|`progress-v1.json`|
|OS|Windows|
|Recursive|無効|

`*` や `*.json` は使用しない。`.tmp`/`.bak`、会話ログ、ログ送信履歴、性能ログ、Unityログ、撮影画像・カメラプリセットは同期対象外。音声ミュートと `nekolpos.client_install_id` 等の端末設定は引き続きPlayerPrefsに残す。

Steamworks側の設定・公開は別操作。現行パスはSteamアカウントごとに分かれていないため、同一Windowsユーザー内のSteamアカウント切替時はセーブ共有に留意する。将来アカウント別保存を導入する場合は保存パスとAuto-Cloud設定を同時に変更する。

## 検証

Issue #71: 自由登録辞書 `Nekolpos.LearnedWords.v1` と心理保存キー `Nekolpos.Status.State.v1` をファイルバックエンドの許可キーへ追加。後者は既存の移行対象だが許可一覧から漏れていたため、移行テストで検出して修正した。学習辞書は既存TeachingDiscoveryとは独立し、同じprogress-v1.jsonへ保存される。実ファイル経由の登録・再読込・対象語だけの削除・心理保存値の維持を追加検証する。詳細は `docs/LearnedWords.md`。

`ProgressSaveStoreTests` でファイル再読込、Unicode、削除の永続化、初回移行、端末設定除外、正本破損からの復旧、破損JSON/未知versionの保持、書込み失敗時の既存正本保持を確認する。Windows Playerの実プロセス再起動とSteam実同期、iOS/Android実機は別途確認が必要。

2026-10-04: Sceneを保存しないEditor直接NUnit実行で追加14ケースと既存日付/日記4ケースが成功。Unity Console Error0。Windows/WebGL条件コンパイルは双方Error0、既存警告3（PlayerLookTarget.DestroyObject、TMP旧API、WalkDialogue未使用field）。これはPlayer Build/実Player起動の結果ではない。テストは一時フォルダに保存し、移行検証で変更したPlayerPrefsは復元済み。実ユーザー進行ファイルの移行は次回ネイティブPlayer起動時に行う。Steamworksは未操作。
