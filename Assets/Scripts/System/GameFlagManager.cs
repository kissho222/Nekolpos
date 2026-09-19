using System.Collections.Generic;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【ゲーム内フラグ管理クラス】
    /// 1日の間に起きた出来事（例：「今日死んだ」「特別な会話を見た」など）を記録します。
    /// 夜の『日記ステート』でここで保存したフラグを読み取り、日記の内容を決定するのに使います。
    /// </summary>
    public class GameFlagManager : MonoBehaviour
    {
        // 他のクラスから簡単にアクセスできるようにする（シングルトン）
        public static GameFlagManager Instance { get; private set; }

        // フラグを「名前（文字列）」と「ON/OFF（bool）」の辞書で管理します
        // これにより、後から「"SawSpecialBird", true」のように自由にフラグを投げ込める高い拡張性を持ちます
        private Dictionary<string, bool> temporaryFlags = new Dictionary<string, bool>();

        private void Awake()
        {
            // シングルトンの初期化（シーンに1つだけにする）
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // シーン遷移してもフラグを消さないようにする
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// フラグをセットします（true/false自由）
        /// 例：GameFlagManager.Instance.SetFlag("IsDeadToday", true);
        /// </summary>
        public void SetFlag(string flagName, bool state)
        {
            if (temporaryFlags.ContainsKey(flagName))
            {
                temporaryFlags[flagName] = state;
            }
            else
            {
                temporaryFlags.Add(flagName, state);
            }
            Debug.Log($"[FlagManager] フラグ更新: {flagName} = {state}");
        }

        /// <summary>
        /// 指定したフラグがON（true）になっているか確認します。
        /// フラグが存在しない場合は false を返します。
        /// </summary>
        public bool GetFlag(string flagName)
        {
            if (temporaryFlags.TryGetValue(flagName, out bool state))
            {
                return state;
            }
            return false;
        }

        /// <summary>
        /// その日の深夜（就寝時など）に、一時的なフラグをすべてリセットするための関数です。
        /// </summary>
        public void ClearTemporaryFlags()
        {
            temporaryFlags.Clear();
            Debug.Log("[FlagManager] 1日のフラグをすべてリセットしました。");
        }

        public IReadOnlyDictionary<string, bool> GetAllFlags()
        {
            return new Dictionary<string, bool>(temporaryFlags);
        }

        public void SetFlags(IReadOnlyDictionary<string, bool> flags, bool clearExisting = true)
        {
            if (clearExisting)
            {
                temporaryFlags.Clear();
            }

            if (flags == null)
            {
                return;
            }

            foreach (KeyValuePair<string, bool> pair in flags)
            {
                temporaryFlags[pair.Key] = pair.Value;
            }
        }
    }
}
