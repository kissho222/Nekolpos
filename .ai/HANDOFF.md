# 開発引き継ぎ

> 現在状態のみを記録する。秘密情報、APIキー、個人情報、会話・Issueの全文は記載しない。

## 更新日時

2026-09-19 15:22:21 JST

## 現在の作業

Unityプロジェクトの公開可能なソースコード・設定・ゲームデータをGit管理へ追加し、外部素材と秘密情報を除外する作業（完了）。

## 現在の状態

Unity 6000.3.10f1 / WebGLプロジェクトの追跡対象を`.gitignore`で定義し、`chore/ai-handoff-foundation`上で公開可能なUnity本体1,510ファイル（21.38 MiB）と、会話ソース・補助ソース65ファイル（861.4 KiB）をコミット`21211de`・`f61df05`として`origin`へpushした。UnityのRuntimeコード、Scene、Prefab、ゲーム挙動は変更していない。

## 完了したこと

- Unity生成物・ローカルIDE状態・ビルド成果物・認証情報・署名ファイルを除外するルールを追加した。
- `Assets/DL Assets`、`Assets/Security Systems`、音源、フォント、外注または出所確認前の画像原稿、Steam配布画像、単体FBXモデルを公開対象から保守的に除外した。
- Scripts、Scenes、Prefab、Timeline、Yarn、Dialogueデータ、ProjectSettings、PackagesとローカルMCPパッケージを追跡対象へ追加した。
- ローカルMCPパッケージはパッケージ定義が示すMIT Licenseを確認して追跡対象とした。別管理の`unity-mcp`チェックアウトは除外した。
- 高確度のトークン・秘密鍵パターンをステージ済みテキストへ検査し、該当0件を確認した。
- `21211de`を`origin/chore/ai-handoff-foundation`へpushした。
- `TalkSource`の会話CSV、プロジェクト資料、Cloudflareログ受信Worker、補助ツールと設定を追跡対象へ追加し、`f61df05`をpushした。
- ネストした別Gitリポジトリである`Server/nekolpos-log-ingest`を確認し、意図しないサブモジュール化を防ぐため外側リポジトリから除外した。

## 変更した主要ファイル

- `.gitignore`
- `Assets/`（公開可能なプロジェクト固有のコード・設定・ゲームデータ）
- `Packages/`
- `ProjectSettings/`
- `TalkSource/`
- `cloudflare/log-ingest-worker/`
- `docs/`
- `tools/`

## 実施した動作確認・テスト

- Unityプロジェクト情報を確認し、Unity 6000.3.10f1およびWebGL設定を確認した。
- 追跡対象の件数・容量を確認した（1,510ファイル、21.38 MiB）。
- `git check-ignore`で代表的な外部素材ディレクトリが除外されることを確認した。
- ステージ済み対象に高確度のGitHub Token、OpenAI APIキー、秘密鍵、AWS Secret Access Keyパターンがないことを確認した。
- `git diff --cached --check`は既存Unity YAML/メタデータ由来の末尾空白を8,155件検出した。シリアライズ内容を不必要に変更しないため、自動整形はしていない。
- 会話ソース・補助ソース65ファイル（861.4 KiB）についても高確度の秘密情報パターンがないことを確認した。既存ファイル由来の末尾空白79件は自動整形していない。

## 未解決事項

- 除外した素材の利用許諾・再配布可否を個別に確認していない。公開許諾が確認できた場合だけ、対象ディレクトリを個別にGit管理へ戻す。
- 既存のUnity YAML/メタデータにある末尾空白の扱いは、Unityでの再保存を含む別作業として判断する。
- `Server/nekolpos-log-ingest`は未コミット変更を含む別リポジトリであり、外側リポジトリへ取り込むか、独立して公開・管理するかは別途判断が必要である。

## 今回確定した仕様・判断

- 公開可否が明確でない外注・購入・第三者・音源・フォント素材は、公開リポジトリでは追跡しない。
- Unityのソース、設定、プロジェクト固有データと、それらの再現に必要でMIT Licenseを確認できたローカルパッケージは追跡する。
- `.gitignore`は生成物・ローカル設定・秘密情報も恒久的に除外する。
- Node.js依存ディレクトリ、Cloudflareのローカル状態、開発用変数ファイル、Unity APIから生成されたルートC#、ローカルのブラウザ記録JSONは追跡しない。
- ネストしたGitリポジトリは、明示的な運用判断なしにGitサブモジュールへ変換しない。
- Git管理対象の追加はゲーム内容の変更ではない。

## 次に行う候補

- このHANDOFF更新を`origin/chore/ai-handoff-foundation`へpushする。
- 公開前に、除外済み素材を含む全アセットの権利情報を確認し、許諾済みのものだけを個別に追加する。
- 必要になった時点で、作業ブランチのPull Request作成または既定ブランチへの統合を行う。
