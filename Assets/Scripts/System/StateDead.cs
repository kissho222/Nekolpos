using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【具体的な状態クラス：死亡・イレギュラー対応（StateDead）】
    /// ゲームの途中、いつドコから死んでも、この状態に飛び込めばすべてを後始末してくれます。
    /// 「死んだら暗転し、時間を翌朝に強制セットする」という要望を実現する重要なパーツです。
    /// </summary>
    public class StateDead : IGameState
    {
        private GameManager gm;
        private float delayTimer = 0f;

        public StateDead(GameManager gameManager)
        {
            this.gm = gameManager;
        }

        public void Enter()
        {
            Debug.Log("[StateDead] 💀 死亡しました（画面真っ暗にして、操作不能にする処理）");

            // "死んだ事実" を GameFlagManager に刻み込む
            // => これが明日の朝の「特別な会話」や、夜の「死亡日記」の内容を変えるトリガーになります！
            if (GameFlagManager.Instance != null)
            {
                GameFlagManager.Instance.SetFlag("IsDeadToday", true);
            }

            // （例）カメラのブラックアウト演出の再生など
            // GameManager内や、他の FadeManager のようなクラスを叩く。
        }

        public void Execute()
        {
            // 死亡演出を見せるため、ちょっとだけ時間を待つ（例として3秒待つ）
            delayTimer += Time.deltaTime;

            if (delayTimer > 3f)
            {
                Debug.Log("（時間を翌朝に強制セット……タイムトラベル中……）");
                
                // 問答無用で朝にしてしまう！
                gm.ChangeState(new StateMorning(gm));
            }
        }

        public void Exit()
        {
            Debug.Log("[StateDead] 復活！暗転の解除などの処理。");
        }

    }
}
