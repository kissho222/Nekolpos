using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【具体的な状態クラス：夕方（StateEvening）】
    /// 昼から夜へ移り変わる前の、少しエモーショナルな時間帯です。
    /// 特定のイベントや、夕食の会話などがここで発生する可能性があります。
    /// </summary>
    public class StateEvening : IGameState
    {
        private GameManager gm;

        public StateEvening(GameManager gameManager)
        {
            this.gm = gameManager;
        }

        public void Enter()
        {
            Debug.Log("[StateEvening] 夕方になりました。空が赤く染まっています。");
            if (RoomLightingManager.Instance != null)
            {
                Debug.Log("[Lighting][TimeChange] StateEvening.Enter -> Request Evening lighting");
                RoomLightingManager.Instance.SetTimeOfDay(LightTimeOfDay.Evening);
            }
            else
            {
                Debug.LogWarning("[Lighting][TimeChange] StateEvening.Enter -> RoomLightingManager.Instance is null");
            }

            if (gm.ConsumeGreetingSuppression())
            {
                return;
            }

            DialogueManager dm = Object.FindFirstObjectByType<DialogueManager>();
            if (dm != null)
            {
                dm.PlayIsolatedGreeting(BasicSystemDialogueCatalog.Get(
                    BasicSystemDialogueCatalog.GreetingEveningKey,
                    "夕方だね。やり忘れたことはない？"));
            }
        }

        public void Execute()
        {
            // 時間経過や特定のアクションで夜にする
            
            // 例：Nキーを押すと夜（日記）になるテスト
            if (Input.GetKeyDown(KeyCode.N))
            {
                gm.ChangeState(new StateNight(gm));
            }
            
            // いつ死んでも大丈夫なようにイレギュラー対応を入れておく
            if (Input.GetKeyDown(KeyCode.X))
            {
                gm.ChangeState(new StateDead(gm));
            }
        }

        public void Exit()
        {
            Debug.Log("[StateEvening] 完全に日が沈みました……。");
        }
    }
}
