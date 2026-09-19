using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【相棒（猫又）の動きやアニメーション専用のコントローラー】
    /// GameManagerにすべての処理（移動、アニメーション、当たり判定）を書くと
    /// クラスが巨大化して破綻してしまう（神クラス・スパゲッティコード化）ため、分離します。
    /// 「猫又がどう動くか」はこのクラスだけに書きます。
    /// </summary>
    public class CatController : MonoBehaviour
    {
        private Animator animator;

        private void Start()
        {
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
            // ここにAIの移動処理（NavMeshAgentなど）や
            // アニメーションの切り替え処理を書いていきます。
        }

        // --- 以下、他クラスから呼ばれる「命令」の窓口を作る ---

        /// <summary>
        /// 特定のアニメーション再生を命令される窓口
        /// </summary>
        public void PlayAnimation(string triggerName)
        {
            if (animator != null)
            {
                animator.SetTrigger(triggerName);
            }
        }

        /// <summary>
        /// 死亡したときに、倒れるモーションを再生する命令窓口
        /// （GameManagerの StateDead Enter() から呼ばれる想定）
        /// </summary>
        public void Die()
        {
            // 倒れるアニメーションや、移動停止の処理を入れる
            Debug.Log("[CatController] 猫又は力尽きて倒れた…");
            PlayAnimation("Death");
        }
    }
}
