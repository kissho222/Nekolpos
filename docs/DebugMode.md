# デバッグモード

`IntroPopupPanel` の `DebugButton` から開始すると、保存済みの名前設定をそのまま使用して命名画面を飛ばす。OP の Timeline は再生しないが、OP完了用に設定した `CameraEventPivot` の最終姿勢を適用してから、既存のOP完了処理を通じて通常猫・視線・会話 UI を復帰する。古いタイトル Scene に `DebugButton` が未保存の場合は、既存の開始ボタンと同じスタイルで実行時に補う。

この開始状態は Day 1 / Morning であり、天気の初期化は `TimeManager` の通常処理を利用する。Debug Start 中だけ `TimeOfDayWidget` の左に `Debug` ボタンを表示し、日数、時間帯、現在天気、翌日予報を既存 `TimeManager` API 経由で変更できる。加えて6種類の心理値（Affection、Sadistic、Concern、Hostility、Obedience、Instinct）を `StatusManager` 経由で±10ずつ確認・変更でき、上位2値を使った代表心理Stateも表示する。AffectionまたはObedienceが70以上で、AudioClip設定済みかつプレイヤーが可聴距離内ならゴロゴロ音の条件も再評価される。デバッグ専用の GameState、時間帯、天気、心理値データは持たない。
