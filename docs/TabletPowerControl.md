# タブレット電源制御

`TabletDisplayController` がタブレットの電源状態の正本である。`IsPoweredOn`、`SetPower(bool)`、`ToggleDisplay()` を通じて状態を変更する。

- 電源ON時は `EmissionPanel` と `TabletCanvas` を有効化し、`TabletCanvas` を `CanvasGroup` で0.2秒かけてフェードインする。
- 電源OFF時は `TabletCanvas` を0.2秒かけてフェードアウトしてから、`TabletCanvas` と `EmissionPanel` を無効化する。
- `TabletCanvas` はタブレット配下から、`EmissionPanel` は同一シーン内から自動検出する。複数ある場合は `TabletDisplayController` の該当フィールドへ明示的に割り当てる。
- `TabletInput` はエディターまたはDevelopment Buildでのみ Shift+P を受け付け、電源を切り替える。リリース版ではキーボード入力を要求しない。

`m_powerFadeDuration` は Inspector から変更でき、標準値は0.2秒である。
