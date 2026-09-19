using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【具体的な状態クラス：夜・日記（StateNight）】
    /// 1日の終わりに発生する、日記を書くフェーズです。
    /// ここで本日のイベントフラグをチェックして、優先順位の高い日記データを引っ張ります。
    /// </summary>
    public class StateNight : IGameState
    {
        private GameManager gm;

        public StateNight(GameManager gameManager)
        {
            this.gm = gameManager;
        }

        public void Enter()
        {
            Debug.Log("[StateNight] 夜になりました。『日記システム』のお時間です。");
            if (RoomLightingManager.Instance != null)
            {
                Debug.Log("[Lighting][TimeChange] StateNight.Enter -> Request Night lighting (temporary Evening visual)");
                RoomLightingManager.Instance.SetTimeOfDay(LightTimeOfDay.Night);
            }
            else
            {
                Debug.LogWarning("[Lighting][TimeChange] StateNight.Enter -> RoomLightingManager.Instance is null");
            }

            if (gm.ConsumeGreetingSuppression())
            {
                return;
            }

            DialogueManager dm = Object.FindFirstObjectByType<DialogueManager>();
            if (dm != null)
            {
                dm.PlayIsolatedGreeting(BasicSystemDialogueCatalog.Get(
                    BasicSystemDialogueCatalog.GreetingNightKey,
                    "夜だね。寝る前に何かする？"));
            }

            // その日立てられたフラグをすべて計算し、何を日記に書くか決める処理
            CheckDiaryEvents();
        }

        private void CheckDiaryEvents()
        {
            // ====== 💡ここで「日記システムとの連携」をこなす ======
            // 実際には、事前に作っておいた複数の DiaryDataSO の中から、
            // 「フラグが合致する」かつ「Priority(優先度)が一番高い」ものを探し出す仕組みを作ります。

            // 例としての簡易テスト
            if (GameFlagManager.Instance != null && GameFlagManager.Instance.GetFlag("IsDeadToday"))
            {
                Debug.Log("📔【日記の内容】「今日は大変な一日だった…。一度死んでしまったが、なんとかなった…」");
            }
            else if (GameFlagManager.Instance != null && GameFlagManager.Instance.GetFlag("TalkedToSpecialCat"))
            {
                Debug.Log("📔【日記の内容】「黒い猫又と秘密の話をした。」");
            }
            else
            {
                Debug.Log("📔【日記の内容】「今日は特に何もない、平和な１日だった。」");
            }

            Debug.Log("================================================");
            Debug.Log("スペースキーで寝て、明日の【朝】を迎えます...");
        }

        public void Execute()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                // 朝へループする前に、1日の出来事をリセットして心機一転します！
                if (GameFlagManager.Instance != null)
                {
                    GameFlagManager.Instance.ClearTemporaryFlags();
                }

                // 翌朝ステートへＧＯ
                gm.ChangeState(new StateMorning(gm));
            }
        }

        public void Exit()
        {
            Debug.Log("[StateNight] ぐうぐう……就寝します。");
        }
    }
}
