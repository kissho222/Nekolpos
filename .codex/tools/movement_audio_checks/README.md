# 移動SE・猫ボイスの外部メモリー検証

Python 3.10 以上と .NET SDK 10 が必要。リポジトリのルートで実行する。

```text
python .codex/tools/movement_audio_checks/verify.py
```

`Assets/Scripts/Audio/` の `CharacterMovementAudioController.cs`、`CatVoiceAudioController.cs`、`FootstepSurfaceProfile.cs`、`FootstepSurface.cs` の全文を、その実パスから直接コンパイルする。製品ソースを抽出・書換しない。Unity / SettingsManager / Timeline は同梱のメモリー上の代役を使用する。Unity DLL、実音声、実 Mixer、通常設定、保存、Registry、ネットワークを使用しない。NuGet source は空で、追加 package はない。

67 ケースで床材の直接・親参照、明示無音と未設定時の fallback、複数 clip の連続重複防止、停止・空中・Pause、間隔と Auto / Event 排他、Jump / Land、Disable / re-enable、明示 Reset と Pause 所有の維持、soft Suspend の event 拒否と Pause をまたぐ着地保持、猫4カテゴリ・空・Ignore / Replace・プレビュー、4 Timeline Signal と既存反応保持、SE / CatVoice routing 引数と専用 AudioSource の呼出回数、日本語 InspectorName / Tooltip / enum を確認する。

別 checkout は `--repo-root <checkout>` で指定する。生成先は毎回新しい `output/movement-audio-checks/<日時-ID>/`。`--output <repo/output配下の新directory>` でも指定でき、既存結果は上書きしない。開始時に manifest を `incomplete / completed=false / passed=false` とし、SDK・ソース不足や途中失敗で成功を残さない。成功には今回の終了値0と manifest の `completed=true / passed=true` の両方が必要。1 は検証完了・期待不一致、2 は実行不完了。source / tool の SHA-256、前後不変、compile 終了値、不足型、ケース結果、判定を保存する。source-snapshot は証跡で、compile 対象は実入力パスである。

修正前の4ソースを別ディレクトリーへ保持している場合は、任意の負の対照として使用できる。

```text
python .codex/tools/movement_audio_checks/verify.py --baseline <修正前4ソースのdirectory>
```

旧ソースは、無効中の公開 API 3件と古い接地・滞空状態2件が失敗し、新規 Reset API の3ケース、Suspend API の2ケースも未実装のため失敗することを要求する。67 件中この10件だけが失敗して残り57件が成功しなければ、負の対照は不合格。Git fetch や旧ソースの自動生成は行わない。baseline なしの場合は現在ソースの67件だけを評価する。

ツールの破損結果拒否、環境・入力不足、途中 compile 失敗、source 変更、既存出力保持は `python .codex/tools/movement_audio_checks/test_verify.py` で合成入力と mock subprocess のみを使い確認できる。自己検査は Unity / dotnet / 実設定を起動・参照しない。

fake AudioSource は API 呼出と状態だけを観測する。実音の重なり、音量0の聴感、Mixer の減衰、自動 Lifecycle、Unity の破棄・null意味、Animator / Timeline 評価順、物理接地、Scene 接続、通常四足、端末を保証しない。Awake / Update / OnDisable はテストが明示呼出しする。Random は候補探索用の決定的な代役で、実乱数の分布試験ではない。PlayerSafetyMovementController の呼出側と実 Mixer の検証は別に必要。
