# 心理値・StatusSystem

`StatusManager` はランタイム中の6心理値の唯一の正本である。対象は `Affection`、`Sadistic`、`Concern`、`Hostility`、`Obedience`、`Instinct` であり、すべて0から100の独立値として扱う。いずれかの値の変更によって、他の値を自動変更しない。

- `Affection`: プレイヤーへの愛着・愛おしさを表す長期的な関係値。
- `Sadistic`: プレイヤーを支配・からかう・いじめたい欲求。嫌悪とは独立し、Affectionと同時に高くなり得る。
- `Concern`: プレイヤーの安全への心配・保護欲。比較的変動しやすい。
- `Hostility`: プレイヤーへの怒り・対立・敵対姿勢。Affectionの反対値ではない。
- `Obedience`: プレイヤーを主人として扱い、要求や命令に従いたい傾向。Affectionや信頼とは独立する。
- `Instinct`: 理性的な判断より猫としての本能・衝動が前面に出る度合い。捕食・遊び・狩猟を含む。

`CatDataSO` はキャラクター固定設定と初期値だけを保持する。ゲーム開始時に `StatusManager` が読み取り、その後の値をアセットへ書き戻してはならない。

変更は会話、行動、イベント、Yarn、デバッグUIから `StatusManager.SetValue` または `ApplyEffects` を通して行う。変更時には `StatusChangeResult` に種類、変更前後、差分、Reason、SourceIdを記録し、`ConversationGameState` とYarnへ投影する。`ConversationGameState` は会話条件評価用の投影データであり、心理値の正本ではない。

会話CSVでは、心理値別の分岐を `condition` 列で制御する。`StatusManager` は各数値に加えて、代表心理状態を `PsychologyState` と `psychology_state` へ投影する。値は `NORMAL`、単一状態の `HOSTILITY`、または値が高い上位2状態をアルファベット順で連結した `AFFECTION_HOSTILITY` の形式となる。例えば `condition` には `PsychologyState == "HOSTILITY"`、または `PsychologyState == "AFFECTION_HOSTILITY"` を指定する。

70以上の値をActiveとする。Activeがなければ代表心理Stateは`Normal`、1つならその種類、複数なら数値が高い上位2つを代表Stateにする。同値は直近に変更された値を優先し、未変更同士は `StatusType` の固定順を使う。最大値100の判定はイベント条件として利用可能だが、既読フラグは別のゲームStateで管理する。
