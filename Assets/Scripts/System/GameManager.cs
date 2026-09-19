using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    /// <summary>
    /// 【ゲームマネージャーの心臓部】
    /// 状態遷移パターン（State Pattern）を使って、ゲームの「今」の進行状況を管理します。
    /// 例えば「朝」から始まって、プレイヤーが操作したり時間が経つと「昼」になり、
    /// もし敵にやられたら無理やり「死亡状態」に切り替える、といった処理をここでコントロールします。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private const string TitleSceneName = "TitleScene";

        // 他のクラスから GameManager.Instance.ChangeState() のように呼べるようにする
        public static GameManager Instance { get; private set; }

        public event Action<IGameState> OnStateChanged;

        // 現在の「状態（State）」を入れる箱です
        private IGameState currentState;
        private string currentTimeOfDayLabel = string.Empty;
        private string nextTimeOfDayLabel = string.Empty;
        private bool suppressGreetingOnNextStateChange;
        private bool hasStartedGameFlow;

        public IGameState CurrentState => currentState;

        public string CurrentTimeOfDayLabel => currentTimeOfDayLabel;

        public string NextTimeOfDayLabel => nextTimeOfDayLabel;

        public void ResetForTitleScreen()
        {
            Debug.Log($"[GameManager] ResetForTitleScreen begin currentState={(currentState != null ? currentState.GetType().Name : "<null>")}");
            if (currentState != null)
            {
                Debug.Log("[GameManager] ResetForTitleScreen currentState.Exit begin");
                currentState.Exit();
                Debug.Log("[GameManager] ResetForTitleScreen currentState.Exit end");
                currentState = null;
            }

            currentTimeOfDayLabel = string.Empty;
            nextTimeOfDayLabel = string.Empty;
            suppressGreetingOnNextStateChange = false;
            hasStartedGameFlow = false;
            InvokeStateChangedSafely(null);
            Debug.Log("[GameManager] ResetForTitleScreen end");
        }

        private void Awake()
        {
            // まずはシングルトンパターンの初期化
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // シーン遷移時に消えないようにする

                if (GetComponent<TimeShortcutHUD>() == null)
                {
                    gameObject.AddComponent<TimeShortcutHUD>();
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (string.Equals(SceneManager.GetActiveScene().name, TitleSceneName, StringComparison.Ordinal))
            {
                return;
            }

            // 他のUIマネージャー群（DialogueManagerなど）が Start() で初期化を終えるのを少し待ってから
            // 一番初めの状態として「朝」をセットします。
            Invoke(nameof(StartGameFlow), 0.1f);
        }

        private void StartGameFlow()
        {
            if (hasStartedGameFlow && currentState != null)
            {
                return;
            }

            hasStartedGameFlow = true;
            ChangeState(new StateMorning(this));
        }

        public void EnsureGameFlowStarted(bool suppressGreeting = false)
        {
            if (hasStartedGameFlow && currentState != null)
            {
                return;
            }

            suppressGreetingOnNextStateChange = suppressGreeting;
            StartGameFlow();
        }

        private void Update()
        {
            if (string.Equals(SceneManager.GetActiveScene().name, TitleSceneName, StringComparison.Ordinal))
            {
                return;
            }

            // 現在の「状態（State）」に対して、「毎フレーム処理（Execute）」を実行させます。
            // 例：朝の状態なら「朝」専用のプログラムが走り、死んでいる状態なら「何もしない」が走る等。
            if (currentState != null)
            {
                currentState.Execute();
            }
        }

        /// <summary>
        /// 別の状態（フェーズ）に切り替えるための最も重要な関数です。
        /// 他のスクリプト（UIボタンや、当たり判定クラス）から呼ばれます。
        /// </summary>
        /// <param name="newState">次に移行する新しい状態（例：new StateDead(this)）</param>
        public void ChangeState(IGameState newState)
        {
            hasStartedGameFlow = true;

            // 現在の状態があれば、終わる前の片付け（Exit）を呼ぶ
            if (currentState != null)
            {
                currentState.Exit();
            }

            // 新しい状態に入れ替える
            currentState = newState;
            RefreshTimeOfDayLabels(newState);

            // 新しい状態の最初の挨拶（Enter）を呼ぶ
            if (currentState != null)
            {
                currentState.Enter();
                InvokeStateChangedSafely(currentState);
                Debug.Log($"[GameManager] {newState.GetType().Name} ステートに入りました。");
            }
        }

        private void InvokeStateChangedSafely(IGameState state)
        {
            if (OnStateChanged == null)
            {
                return;
            }

            Delegate[] listeners = OnStateChanged.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] is not Action<IGameState> listener)
                {
                    continue;
                }

                UnityEngine.Object targetObject = listener.Target as UnityEngine.Object;
                if (listener.Target != null && targetObject == null)
                {
                    OnStateChanged -= listener;
                    Debug.Log("[GameManager] Removed destroyed OnStateChanged listener.");
                    continue;
                }

                try
                {
                    listener.Invoke(state);
                }
                catch (MissingReferenceException exception)
                {
                    OnStateChanged -= listener;
                    Debug.LogWarning($"[GameManager] Removed OnStateChanged listener after MissingReferenceException: {exception.Message}");
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void ChangeState(IGameState newState, bool suppressGreeting)
        {
            suppressGreetingOnNextStateChange = suppressGreeting;
            ChangeState(newState);
        }

        public bool ConsumeGreetingSuppression()
        {
            bool shouldSuppress = suppressGreetingOnNextStateChange;
            suppressGreetingOnNextStateChange = false;
            return shouldSuppress;
        }

        private static string ResolveCurrentTimeOfDayLabel(IGameState state)
        {
            if (state is StateMorning) return "朝";
            if (state is StateDay) return "昼";
            if (state is StateEvening) return "夕";
            if (state is StateNight) return "夜";
            return string.Empty;
        }

        private static string ResolveNextTimeOfDayLabel(IGameState state)
        {
            if (state is StateMorning) return "昼";
            if (state is StateDay) return "夕";
            if (state is StateEvening) return "夜";
            if (state is StateNight) return "朝";
            if (state is StateDead) return "朝";
            return string.Empty;
        }

        private void RefreshTimeOfDayLabels(IGameState state)
        {
            string resolvedCurrent = ResolveCurrentTimeOfDayLabel(state);
            if (!string.IsNullOrEmpty(resolvedCurrent))
            {
                currentTimeOfDayLabel = resolvedCurrent;
            }

            nextTimeOfDayLabel = ResolveNextTimeOfDayLabel(state);
        }
    }
}
