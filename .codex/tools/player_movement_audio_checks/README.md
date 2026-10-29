# PlayerSafety と移動SEの接続検証

Python 3.10 以上 / .NET SDK 10 が必要。Unity・実音・実入力・物理・通常設定・保存・Registry・ネットワークは使わない。NuGet source は空で追加 package はない。

PR を checkout したリポジトリのルートで、その PlayerSafety を確認する。

```text
python .codex/tools/player_movement_audio_checks/verify.py
```

任意の候補ソースは `--source <PlayerSafetyMovementController.cs>`、別 checkout は `--repo-root <checkout>` で指定する。対象 source は GitHub PR の checkout または作業元から入手する。特定 base commit や過去のローカル output は不要で、fetch や製品変更は行わない。

Update / TeleportTo / TryStartJump / OnDisable / jump計算・受付 / CanJump を対象ソースから本文無加工で抽出し、検証用 class shell でコンパイルする。単純な base 版では RefreshWalkableState も抽出する。追加足場状態を持つ現行版では StartJump と IsGroundedForTabletFootInput を抽出し、複雑な物理 Refresh は代役にする。対象の必要メソッドが無い・曖昧・未対応の文字列構文・compile 不可なら失敗する。

27 ケースで実水平変位 / deltaTime、入力のみ・縦移動・事前復帰距離の除外、接地 gate、最新 Collider、Console / Pause / Settings の Suspend、成立 jump1回と受付拒否 / 初速0、Teleport / Disable の Reset、既存 CC enabled / SafePosition の契約、音声 component 不在を確認する。Audio controller 本体の検証は別の `movement_audio_checks` を使う。

結果は毎回新規 `output/player-movement-audio-checks/<日時-ID>/` へ保存する。`--output <repo/output配下の新directory>` も指定でき、既存結果は上書きしない。manifest は開始時 incomplete / completed=false / passed=false、正常終端のみ completed=true。source と tool の SHA-256、抽出member hash、前後不変、compile状態、不足型、27件の結果を残す。成功には終了値0と今回 manifest の completed=true / passed=true の両方が必要。1 は期待不一致、2 は未完了。

これは実メソッドの **通知境界** の検証。MovePlayer、床検出、CC、Transform、Input と Audio callback はメモリー上の代役で、衝突・SphereCast・実移動・実入力・Timeline・音源・Mixer・描画・Scene接続の実機合格ではない。root と PR base の物理実装が同一であるとも主張しない。今回の変更が音声hookだけである全文差分照合は監査証跡の別項目で、一般的なこのツールの合格から推測しない。
