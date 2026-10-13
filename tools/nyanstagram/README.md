# ニャンスタグラム実猫写真パイプライン

このフォルダは、使用許可を得た実猫写真を、ローカル版ニャンスタグラムの検証用JPEGへ変換するためのものです。ネットワーク送信、公募フォーム、サーバー連携は行いません。変換元は変更も削除もされません。

## 置き場所とGitの扱い

- 原本: `tools/nyanstagram/input/`
- 私用の変換設定: `tools/nyanstagram/conversion-manifest.json`
- 出力JPEG: `Assets/StreamingAssets/Nyanstagram/Local/Images/CAT_000001.jpg` の形式
- 私用のゲーム内カタログ: `Assets/StreamingAssets/Nyanstagram/Local/nyanstagram-local-catalog.json`

これらの原本・出力・実データ入りカタログは `.gitignore` 対象です。公開GitHubへ実猫写真や投稿者情報をpushしないでください。Gitに含めてよいのは、変換ツール、README、`*.example.json`、空フォルダを維持する`.gitkeep`だけです。

## 初回設定と変換

PowerShellでプロジェクト直下から実行します。

```powershell
python -m pip install -r tools/nyanstagram/requirements.txt
Copy-Item tools/nyanstagram/conversion-manifest.example.json tools/nyanstagram/conversion-manifest.json
Copy-Item Assets/StreamingAssets/Nyanstagram/Local/nyanstagram-local-catalog.example.json Assets/StreamingAssets/Nyanstagram/Local/nyanstagram-local-catalog.json
```

すでに `tools/nyanstagram` をカレントフォルダにしている場合は、プロジェクト相対パスを重ねず、スクリプト名だけを指定します。

```powershell
python .\convert_cat_photos.py --dry-run
python .\convert_cat_photos.py
```

原本を `input` へ置いた後、画像名一覧から私用マニフェストを作る場合は次を実行します。既存のマニフェストは保護されるため、作り直す場合だけ `--force` を付けます。

```powershell
python tools/nyanstagram/convert_cat_photos.py --write-manifest-template
```

`conversion-manifest.json` の各項目を設定します。`CatName` と `ContributorName` は必須、`ContributorUrl` は任意です。`Approved`、`AllowGameUse`、`AllowPromotionUse` は許諾状態をそのまま `true`/`false` で記録します。最初は全て `false` なので、未確認写真が誤ってゲームに出ません。猫名・投稿者名は出力ファイル名や内部IDに使われません。

検証後、変換します。

```powershell
python tools/nyanstagram/convert_cat_photos.py --dry-run
python tools/nyanstagram/convert_cat_photos.py
```

変換はEXIF Orientationを反映し、RGB JPEGへ変換します。縦横比は保ったまま長辺を1024px以下に縮小し、既定品質は82です。EXIF、GPS、ICCを含む入力メタデータは出力へ引き継ぎません。出力名は `CAT_000001.jpg` の連番だけです。必要なら `--max-dimension` と `--quality` で調整できます。

変換した写真をゲームで扱うには、`Approved` と `AllowGameUse` の両方を `true` にします。`AllowPromotionUse` はゲーム内表示とは独立して残す許諾記録で、将来の告知素材選定に使えます。

## Unity側の接続点

現在はUIを実装しません。`Nekolpos.Nyanstagram.LocalNyanstagramPhotoSource` が `StreamingAssets` の私用カタログとJPEGを読み込み、`INyanstagramPhotoSource` を通じて結果を返します。UIや日付更新ロジックはこのインターフェースだけに依存させてください。

将来Remote/Cacheを追加する時は、同じ `INyanstagramPhotoSource` を実装する `RemoteCachedNyanstagramPhotoSource` を追加し、生成地点で `LocalNyanstagramPhotoSource` を差し替えます。カタログ形式とUI側の取得呼び出しは変更不要です。Remote実装時に初めてURL認可、ダウンロード、キャッシュ期限、削除、通信失敗時のフォールバックを追加します。
