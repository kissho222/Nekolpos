# 時間経過・行動結果パネル（Issue #45）

`ResultTextUI`は会話・教示・行動・散歩終了などの既存結果表示を担当する。表示内容、時間進行、表示順、待機時間は各既存フローの責務とし、このパネルでは変更しない。

Issue #67で時間経過の背面へ `TransitionBackground.prefab` を接続した。本文・TMP・半透明背景・余白・表示時間はこの既存パネルを再利用する。時間帯の色/太陽の補間は背面のViewが担当し、DialogueManagerが完全遮蔽後に時間更新・既存結果表示を行い、補間終了後に明転する。詳細は `docs/TransitionBackground.md`。

## 編集と参照

見た目の正本は`Assets/Prefab/ActionResultPanel.prefab`。MainSceneの`OpenBetaCanvas/ActionResultPanel`へ配置し、`OpenBetaSystems`の既存`ResultTextUI`コンポーネントへCanvas・CanvasGroup・ResultTextを接続する。DialogueManagerとActionManagerは従来どおり同じホストのコンポーネントを使用する。新しいRuntime C#は追加しない。

Runtimeで固定UIを新規生成しない。Initializeは非表示にし、未設定のフォントだけ補完する。表示時は本文・Active・alpha・描画順だけ変更する。参照不足時は明瞭なErrorを記録する。別Sceneでこの表示を使う場合も、事前に同じPrefabを配置して参照を接続する。

## 共通レイアウト

- 外側はCanvasの左右3%、上下10%を安全余白として確保する。
- 中央寄せのVerticalLayoutGroupで、背景を文字の推奨サイズに合わせ、外側の表示領域を上限とする。短文でも最低240×96の背景を保つ。
- 背景の文字余白は左右24、上下20。色は従来の黒・alpha 0.78、本文は白を維持する。
- TMPは中央寄せ、Auto Size 12〜34、No Wrap。CSVが指定した改行だけを使い、狭い画面では文字サイズを下げる。OverflowはTruncateとし、現在の全日本語イベントが省略されないことをテストする。将来のより長い文言や翻訳は同じテストで確認し、レイアウトまたは文面の明示改行を見直す。最低文字サイズを下回る任意長の文言を保証するものではない。
- 設定値は1280×720基準のCanvas単位。CanvasScalerは既存のScale With Screen Size・Match 0.5を使用する。文言別のサイズ分岐は追加しない。

## 検証

`ResultTextUILayoutTests`は実際のSystemTimedEventCatalog全日本語文言について、1280×720、1920×1080、2560×1440、3840×2160、1024×768、2560×1080、720×1280相当のCanvasで明示改行・文字省略なし・本文と背景の領域内収容を確認する。長文後の短文への縮小、Initializeの設定保持、表示終了時の非表示、参照不足エラーも確認する。CSV末尾の空改行は表示行数に含めない。

実行結果・画像確認・実Playerで未検証の範囲は`.ai/HANDOFF.md`とIssue完了報告を正とする。


Issue #59（2026-10-07）ではSystemTimedEventCatalogが表示Variant集合から等確率で1件選ぶ。旧ja/en/zhは第1候補、display_variantsは追加候補。日記のdiary_variants/Random/Sequenceとは独立し、結果パネルは選択済み本文を表示する責務を維持する。時間進行仕様とPrefab/Sceneレイアウトは変更しない。

死亡時は既存SystemTimedEvent本文を黒画面に表示した後、専用の朝日復活演出へ進む。新しい翌朝説明文は追加しない。ResultTextUIはキャンセルトークンと表示世代を扱い、HideInstant後の旧表示終了が新表示を隠さない。詳細は docs/DeathRevivalTransition.md。
