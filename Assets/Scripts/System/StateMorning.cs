using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【具体的な状態クラス：朝（StateMorning）】
    /// GameManagerから「君の番だよ」と言われたときに動く、朝専用の設計図です。
    /// </summary>
    public class StateMorning : IGameState
    {
        private GameManager gm;

        // コンストラクタ（この状態が作られた瞬間に、GameManagerの親玉を記憶しておく）
        public StateMorning(GameManager gameManager)
        {
            this.gm = gameManager;
        }

        public void Enter()
        {
            Debug.Log("[StateMorning] チュンチュン…朝になりました。");
            if (RoomLightingManager.Instance != null)
            {
                Debug.Log("[Lighting][TimeChange] StateMorning.Enter -> Request Morning lighting");
                RoomLightingManager.Instance.SetTimeOfDay(LightTimeOfDay.Morning);
            }
            else
            {
                Debug.LogWarning("[Lighting][TimeChange] StateMorning.Enter -> RoomLightingManager.Instance is null");
            }

            // 状態（朝・昼ステート）は、会話システム等と連携するために DialogueManager を探す
            DialogueManager dm = Object.FindFirstObjectByType<DialogueManager>();
            bool suppressGreeting = gm.ConsumeGreetingSuppression();

            // 朝起きた瞬間、昨日「死んだまま朝を迎えた」というフラグがないかチェックする
            if (suppressGreeting)
            {
                return;
            }

            if (GameFlagManager.Instance != null && GameFlagManager.Instance.GetFlag("IsDeadToday"))
            {
                // ここで DialogueManager（会話システム）を呼んで、特別なセリフを流す
                if (dm != null)
                {
                    dm.PlayIsolatedGreeting(BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingRevivedMorningKey,
                        "お前、死んじゃったのかと思ったよ…"));
                }
                
                // ※一度怒られたら、あるいは生存確認したらフラグを折ってもいいですし、夜の『日記ステート』でまとめて消してもOKです。
                // 今回は「夜に日記を書く」システムがあるので、夜までフラグは残しておきます。
            }
            else
            {
                // 普通の朝
                if (dm != null)
                {
                    dm.PlayIsolatedGreeting(BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingMorningKey,
                        "おはよう！朝だね。今日はなにしようか。"));
                }
            }

            // （例）数秒後に昼にする、あるいは「家を出るボタンボタン」で昼にするなど
            // 今回はテスト体験として、何らかの入力ですぐ昼にするように Execute() で書きます。
        }

        public void Execute()
        {
            // スペースキーが押されたら「昼ステート」へ移行！
            if (Input.GetKeyDown(KeyCode.Space))
            {
                gm.ChangeState(new StateDay(gm));
            }
            // エンターキーでいきなり死亡テスト
            if (Input.GetKeyDown(KeyCode.X))
            {
                gm.ChangeState(new StateDead(gm));
            }
        }

        public void Exit()
        {
            Debug.Log("[StateMorning] 朝の支度完了。昼に向かいます。");
        }
    }
}
