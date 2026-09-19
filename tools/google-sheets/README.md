# Googleスプレッドシート連携

Cloudflare D1に保存されたネコルポス会話ログを、閲覧・分析用のGoogleスプレッドシートへ反映するためのApps Scriptです。正式な元DBはCloudflare D1で、スプレッドシートはミラーとして使います。

## Googleスプレッドシート作成手順

1. Google Driveで新しいスプレッドシートを作成します。
2. 任意の名前を付けます。例: `ネコルポス 会話ログ`
3. 初回実行時に以下のシートが作成・更新されます。

- `LatestLogs`
- `ClientSummary`
- `UnknownWords`
- `IntentSummary`
- `RawLogs`
- `Settings`

## Apps Scriptを開く手順

1. スプレッドシートを開きます。
2. メニューの `拡張機能` から `Apps Script` を開きます。
3. 既存の `Code.gs` がある場合は内容を置き換えます。

## Code.gsを貼る手順

1. このリポジトリの `tools/google-sheets/Code.gs` を開きます。
2. Apps Scriptエディタの `Code.gs` に内容を貼り付けます。
3. 保存します。
4. スプレッドシートを再読み込みすると、`ネコルポスログ` メニューが追加されます。

## Script PropertiesにNEKOLPOS_ADMIN_KEYを設定する手順

ADMIN_KEYはシートへ直接書かず、Apps ScriptのScript Propertiesに保存します。

1. Apps Scriptエディタ左側の `プロジェクトの設定` を開きます。
2. `スクリプト プロパティ` の `スクリプト プロパティを追加` を選びます。
3. プロパティ名に `NEKOLPOS_ADMIN_KEY` を入力します。
4. 値にCloudflare Worker側と同じ管理用secretを入力します。
5. 保存します。

## Cloudflare側でNEKOLPOS_ADMIN_KEYを設定する手順

`Server/nekolpos-log-ingest` で以下を実行します。

```bash
npx wrangler secret put NEKOLPOS_ADMIN_KEY
```

ゲームクライアント用の `NEKOLPOS_UPLOAD_KEY` とは別の値にしてください。管理APIは以下のヘッダーだけを認証に使います。

```text
X-Nekolpos-Admin-Key: <NEKOLPOS_ADMIN_KEY>
```

## 初回同期手順

1. スプレッドシートを再読み込みします。
2. `Settings` シートの `API_BASE_URL` が正しいことを確認します。
   デフォルトは `https://nekolpos-log-ingest.nekolpos.workers.dev` です。
3. メニュー `ネコルポスログ` から `全部更新` を実行します。
4. Googleの承認ダイアログが出たら、Apps Scriptの実行を許可します。

`LatestLogs` は初回に最新1000件を取り込みます。2回目以降は `Settings` シートの `LAST_SYNC_RECEIVED_AT` と `LAST_SYNC_LOG_ID` の複合カーソルを使い、それより後のログだけを古い順に取り込みます。未同期ログが1000件を超える場合も、実行のたびに次の1000件へ進むため取りこぼしません。重複は `log_id` で除外します。

`RawLogs` は最新100件だけを保持します。`payload_json` は長くなるため折り返し表示にしています。

## トラブルシュート

- 401になる場合: Apps ScriptのScript Propertiesに設定した `NEKOLPOS_ADMIN_KEY` と、Cloudflare Workerの `NEKOLPOS_ADMIN_KEY` が一致していません。
- 0件の場合: D1の `dialogue_logs` に対象ログがないか、`LAST_SYNC_RECEIVED_AT` より新しいログがありません。
- 重い場合: `Code.gs` は全列の自動サイズ変更やフィルタ再作成を毎回行わず、重複確認も直近2000件に限定しています。それでも実行時間が長い場合は、`SYNC_BATCH_SIZE` を下げてください。`refreshUnknownWords()` は `limit: 200` です。
- `We're sorry, a server error occurred` で長時間後に終了する場合: 外部API通信は1回15秒で打ち切り、最大3回再試行します。Apps Scriptエディタの `実行数` で、`[refreshAll]` から始まるログを確認すると失敗した更新段階と所要時間を特定できます。
- `Script PropertiesにNEKOLPOS_ADMIN_KEYを設定してください。` と出る場合: Apps ScriptのScript Propertiesに管理キーが未設定です。
