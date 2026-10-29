// Historical negative control: Executor.IsRunning blocks the original debug message API.
public static class OldDebugConsoleInput {
    public static bool owned; public static object activeLease; public static int releasedFrame=-1;
    public static bool IsBlocked => ShouldBlock(owned || activeLease != null, releasedFrame, Time.frameCount) || DebugScenarioExecutor.IsRunning;
public static bool ShouldBlock(bool ownsInput, int released, int frame) => ownsInput || (released >= 0 && released == frame);
}
public sealed class OldChatUIController {
    public bool menuInputBlocked, isTyping, skipTyping;
    public object delayedDialogueProcessCoroutine;
    public Panel choicePanel=new Panel(); public event Action OnWaitInputCompleted;
    public void Normal() => HandleNextInput();
            private void HandleNextInput()
        {
            if (OldDebugConsoleInput.IsBlocked || menuInputBlocked || delayedDialogueProcessCoroutine != null)
            {
                return;
            }

            // タイプライター表示中ならスキップする
            if (isTyping)
            {
                skipTyping = true;
            }
            else
            {
                // すでに表示完了していて、選択肢待機中でなければ次へ
                if (choicePanel != null && !choicePanel.activeSelf)
                {
                    OnWaitInputCompleted?.Invoke();
                }
            }
        }
        public bool AdvanceIsolatedDebugMessage()
        {
            if (!DebugSaveSession.CanRunScenarios || OldDebugConsoleInput.IsBlocked) return false;
            HandleNextInput(); return true;
        }
}
