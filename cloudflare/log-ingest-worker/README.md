# Nekolpos Dialogue Log Ingest API

Cloudflare Workers + D1で、UnityからPOSTされるネコルポス会話ログを保存するAPIです。

## Endpoint

```text
POST /api/dialogue-logs
```

Production URL:

```text
https://nekolpos-log-ingest.<Cloudflareのサブドメイン>.workers.dev/api/dialogue-logs
```

成功時は `204 No Content` を返します。レスポンス本文はありません。

## Setup

依存関係をインストールします。

```sh
npm install
```

## D1作成手順

D1 databaseを作成します。

```sh
npx wrangler d1 create nekolpos_dialogue_logs
```

出力された `database_id` を `wrangler.toml` の `database_id` に設定してください。

## Migration実行手順

ローカルD1へ適用:

```sh
npm run db:migrate:local
```

Cloudflare上のD1へ適用:

```sh
npm run db:migrate:remote
```

作成されるテーブルは `dialogue_logs` です。`log_id` はPRIMARY KEYで、再送時の重複は `INSERT OR IGNORE` で無視されます。

## 認証設定

開発時は `wrangler.toml` の `REQUIRE_UPLOAD_KEY = "false"` のままにすると、`X-Nekolpos-Upload-Key` なしでテストできます。

本番では `REQUIRE_UPLOAD_KEY = "true"` に変更し、secretを設定してください。

```sh
npx wrangler secret put NEKOLPOS_UPLOAD_KEY
```

Unity側からは以下のヘッダーを送ります。

```text
X-Nekolpos-Upload-Key: <NEKOLPOS_UPLOAD_KEYの値>
```

## ローカル起動手順

```sh
npm run dev
```

ローカルURL:

```text
http://localhost:8787/api/dialogue-logs
```

## デプロイ手順

```sh
npm run deploy
```

## Unity側 uploadEndpoint

ローカルテスト:

```text
http://localhost:8787/api/dialogue-logs
```

デプロイ後:

```text
https://nekolpos-log-ingest.<Cloudflareのサブドメイン>.workers.dev/api/dialogue-logs
```

Unityの `DialogueLogManager` の `uploadEndpoint` に設定してください。

## curlでのテスト方法

PowerShellでは行末の `\` ではなくバッククォートを使ってください。

```sh
curl -X POST "http://localhost:8787/api/dialogue-logs" \
  -H "Content-Type: application/json; charset=utf-8" \
  -H "X-Nekolpos-Upload-Key: test-key" \
  -d '{
    "schema_version": "nekolpos-dialogue-log/v1",
    "logs": [
      {
        "log_id": "test_001",
        "timestamp": "2026-06-23T12:34:56.789Z",
        "language": "Japanese",
        "speaker": "Cat",
        "speaker_display_name": "ネルコ",
        "source": "RegexReaction",
        "text": "なに？呼んだ？",
        "upload_status": "queued"
      }
    ]
  }'
```

同じ `log_id` を再送してもAPIは成功し、保存済み行は増えません。

## D1からログを確認する方法

ローカルD1:

```sh
npx wrangler d1 execute nekolpos_dialogue_logs --local --command "SELECT log_id, timestamp, received_at, speaker, source, text FROM dialogue_logs ORDER BY received_at DESC LIMIT 10;"
```

Cloudflare上のD1:

```sh
npx wrangler d1 execute nekolpos_dialogue_logs --remote --command "SELECT log_id, timestamp, received_at, speaker, source, text FROM dialogue_logs ORDER BY received_at DESC LIMIT 10;"
```

本文全体やraw payloadをCloudflareのconsole logには出しません。ログ出力は受信件数、保存件数、重複件数、エラー理由、request id程度に限定しています。
