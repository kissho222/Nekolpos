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

            // Night entry only synchronizes the state. TimeFlow exclusively owns diary startup.
            gm.ConsumeGreetingSuppression();
        }

        public void Execute() { }

        public void Exit()
        {
            Debug.Log("[StateNight] ぐうぐう……就寝します。");
        }
    }
}
