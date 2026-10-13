# 会話診断UI（Issue #65）

## 正本・実装

会話本文・Choice値・Runtime仕様は変更しない。診断は保存済みDialoguePreview.csv、SystemTimedEvent.csv、ActionDataと既存Runtime入口を照合する。

TimedEventは従来ActionIDとtimed_event_keyを独立描画していたため同じ正本keyでも二重表示になっていた。DialoguePreviewWindow.TimedEventLinksでTrim・大文字小文字を無視したkey単位に統合し、同keyは編集欄1セット＋参照元併記、別keyは別枠にする。共有Event警告・専用経路説明・正本Repository保存を維持する。

TimedEvent再読込はRepository・作者診断・RuntimeのTimedEventキャッシュを破棄する。通常CSV再読込も作者診断を破棄する。「診断を再読込」とPhase1比較Windowの再読込は毎回ImportFileする。ResolveSharedReferencesの再実行は旧外部診断を除去し、重複加算しない。

通常会話Editorの「作者確認・診断」はWarning/Errorを日本語で表示し、内部codeをTooltipへ保持する。CSV行、id、regex_id、pattern、order、response_type、対象列、現在値、日本語本文冒頭40文字、解決できたEditor DialogueID・Segment IDを表示。「この会話を開く」は既存破棄確認を通し、出典行からPattern/Lineを選択する。未保存編集は診断対象外。0をEndへ変換・削除しない。

Editor限定の新DialogueAuthoringActionResolver.csはRegistry、DialogueManagerの既存static判定（教示・死亡・直前会話）、walk/StartEvent/unknown_word_teach_event、ActionManager既定ChoiceSet、Yarn node、Timeline・基本会話地点入口を照合し、Actionを実行しない。Timeline分類はresolver入口の確認で、Scene実再生成功は保証しない。今回のMoveToDesk bindingはMainScene正本に存在する。

Action ID単位に出現行数・参照Root数・出典行を集約。未解決だけUnresolvedAction Error、解決したIDはResolvedAction Infoとし、開発比較Windowで全分類を表示する。汎用fallbackで何らかの動作ができることを、指定IDの解決とは扱わない。未解決IDを推測で実装しない。Phase2ゲートはErrorの有無で判断し、正常な特殊処理だけでは保留しない。潜在0参照のShadowMissingSegmentIndexは従来どおり残す。

## 最新集計（2026-10-08 JST）

| Validator | Error | Warning | Info |
| --- | ---: | ---: | ---: |
| Phase1 | 3 | 6 | 305 |
| Phase2 | 5 | 6 | 305 |
| Phase3（Phase2診断の集約値） | 5 | 6 | 305 |

Warning6は代表属性不一致4＋0参照2。Phase1 Error3は未解決ActionのユニークID3。Phase2/3はShadowMissingSegmentIndex2も含み公開ゲートfalse。TimedEvent参照切れ0。action_paw_press_death_resultは登録済みで正常。

| Action分類 | ユニークID | 出現行 |
| --- | ---: | ---: |
| 通常Registry | 19 | 45 |
| Runtime resolver | 3 | 3 |
| 既存専用処理 | 14 | 32 |
| 未解決 | 3 | 4 |
| 合計 | 39 | 84 |

旧40行は21 ID。うちaction_paw_press_death_resultの1行が最新正本でRegistry解決、残り39行は20 ID（resolver3・専用14・未解決3）。未解決はaction_ball_play（行542）、yarn_accident_talk（行599/600）、action_paw_press（行711）。yarn_accident_talkというnodeは存在するが、直接Yarn入口はyarn:付きであり、同名だけではAction解決にならない。元値を保持し作者判断に残す。

| 行 | id | regex_id | Pattern | order | response_type | 対象列 | 値 | 本文冒頭 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 276 | 00470 | RE_INTENT_AFFECTION_001 | 7 | 1 | Normal | choice_yes_pattern | 0 | しないの？そっか。それじゃあ代わりに何かする？ |
| 281 | 00470 | RE_INTENT_AFFECTION_001 | 5 | 0 | Normal | yes_next_pattern | 0 | どうしよっかな～ |

作者が旧設定削除・意図的0・別Pattern修正を判断する。Normal経路で発火する不具合とは断定しない。

## 検証

新DialogueAuthoringDiagnosticsTests.csの21ケース＋Phase1/2/3の51ケース、Unity正規Runner計72件成功（job be97d5b0cabd494fafadb405b23bf5e3）。同key/別key・参照元・単一正本保存・最新TimedEvent・古い診断除去・0参照の詳細/無変更・分類/集約・行276/281ジャンプを検証。以前のhold修正に対して残っていたPhase2期待値1件を現在仕様へ同期。CSVセルloss0・旧Engine比較差分0・Phase3 DTO比較成功。

MCP jobはsucceeded・completed/total72・失敗通知0。詳細resultがnullのため最終job JSONを証跡に保存。初期0件実行と全体Runner誤開始（公式キャンセル）は成功件数に含めない。UnitySkills RESTは使わず、通常ファイル編集とUnity MCP Compile/Runnerで代替。REST固有履歴は未検証。Build・モバイル実機・各Action実再生は未実施。

証跡: C:/Users/shoho/.codex/tmp/issue65-20261008/のPhase1.json、Summary.json、FinalJob.json、baseline.json。
