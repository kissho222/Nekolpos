using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【具体的な状態クラス：昼（StateDay）】
    /// プレイヤーが外を歩き回る、いわゆるメインのゲームプレイ時間帯です。
    /// </summary>
    public class StateDay : IGameState
    {
        private GameManager gm;

        public StateDay(GameManager gameManager)
        {
            this.gm = gameManager;
        }

        public void Enter()
        {
            Debug.Log("[StateDay] 日中になりました。探索やゲームプレイが始まります！");
            if (RoomLightingManager.Instance != null)
            {
                Debug.Log("[Lighting][TimeChange] StateDay.Enter -> Request Day lighting");
                RoomLightingManager.Instance.SetTimeOfDay(LightTimeOfDay.Day);
            }
            else
            {
                Debug.LogWarning("[Lighting][TimeChange] StateDay.Enter -> RoomLightingManager.Instance is null");
            }

            if (gm.ConsumeGreetingSuppression())
            {
                return;
            }

            DialogueManager dm = Object.FindFirstObjectByType<DialogueManager>();
            if (dm != null)
            {
                dm.PlayIsolatedGreeting(BasicSystemDialogueCatalog.Get(
                    BasicSystemDialogueCatalog.GreetingDayKey,
                    "昼だね。お腹空いてない？"));
            }
        }

        public void Execute() { } // Development time advancement uses the debug panel's shared TimeFlow.


        public void Exit()
        {
            Debug.Log("[StateDay] 日が傾いてきました……。");
            
            // （例）探索を強制終了してUIを消すなど
        }
    }
}
