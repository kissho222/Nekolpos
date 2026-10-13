# ニャンスタグラム実猫写真基盤

ニャンスタグラムUIは未実装であり、今回追加したのはローカル検証用の実猫写真変換・メタデータ・読み込み基盤だけである。原本、出力JPEG、実データを含むカタログはGit管理しない。

`tools/nyanstagram/convert_cat_photos.py` は、私用マニフェストに記録した許諾情報から `CAT_000001.jpg` 形式のJPEGと `StreamingAssets` 用カタログを作る。原本を変更・削除せず、向き補正、RGB化、長辺1024px上限、品質82、入力メタデータ非継承を行う。詳細な操作は `tools/nyanstagram/README.md` を正とする。

Unity Runtimeは `Nekolpos.Nyanstagram.INyanstagramPhotoSource` を写真取得の境界とする。現在の `LocalNyanstagramPhotoSource` は `Assets/StreamingAssets/Nyanstagram/Local` を読む。将来のRemote/Cacheは同じインターフェースを実装して生成地点だけを差し替えるため、UI・日付更新・許諾フィルターは取得元を直接分岐しない。

カタログの項目は `PhotoId`、`CatName`、`ContributorName`、任意の`ContributorUrl`、`ImageFileName`、`Approved`、`AllowGameUse`、`AllowPromotionUse`。ゲーム内に列挙可能なのは `Approved` と `AllowGameUse` がともに真で、IDと出力JPEG名が安全な形式の項目だけである。`AllowPromotionUse` はゲーム内使用と独立した記録として保持する。
