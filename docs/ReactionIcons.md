# 漫符機能（撤去済み）

2026-10-08、ユーザー判断により実装を取り下げた。ゲーム内表示、気づきの三本線、付属発話SEと追加待機、会話制作ウィンドウの選択欄、設定Toggle、専用Presenter・設置Editorツール・Prefab・Anchor・専用画像と音声を撤去した。

既存CSV・Compiled Bundleの互換性に必要なReactionIcon enum/DTO/読み書き/複製だけを保持する。旧設定JSONのshowReactionIconsは未知フィールドとして無視する。台詞と会話原稿は変更しない。通常の猫演出待機は維持する。
