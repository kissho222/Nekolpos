using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

namespace Nekolpos.System
{
    /// <summary>
    /// 【会話テスト用UIコントローラー】
    /// 画面上のInputFieldからテキストを受け取り、暗号化会話エンジン（DialogueEngine）に送ります。
    /// （一時的な実験用UIです。本格的なチャットUIは別で作成します）
    /// </summary>
    public class DialogueUIController : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("プレイヤーが文字を入力する欄")]
        public InputField chatInputField;

        private void Start()
        {
            if (chatInputField != null)
            {
                // Enterキー（またはフォーカス外れ）の際に呼ばれるイベントを登録
                chatInputField.onEndEdit.AddListener(OnEndEditSubmit);
            }
            else
            {
                Debug.LogWarning("[DialogueUIController] InputFieldがアタッチされていません！");
            }
        }

        private void Update()
        {
            if (chatInputField == null) return;

            // 1. 強制フィルター（毎フレームチェックして許可外の文字を消去）
            // ※IME確定前は text プロパティに入らないため、確定された瞬間に削られます
            string rawText = chatInputField.text;
            if (!string.IsNullOrEmpty(rawText))
            {
                string filtered = Regex.Replace(rawText, @"[^ぁ-んァ-ン！？、。　ー]", "");
                if (rawText != filtered)
                {
                    chatInputField.text = filtered;
                }
            }
        }

        private void OnEndEditSubmit(string text)
        {
            // Unityの仕様上、エンターキーで確定されたかどうかの判定が難しいため、
            // onEndEdit（確定アクション）が呼ばれたら送信します。
            OnInputSubmit(text);
        }

        private void OnInputSubmit(string inputText)
        {
            // DialogueEngineに投げる
            if (DialogueEngine.Instance != null)
            {
                Debug.Log($"<color=cyan>📝 [Player Input] {inputText}</color>");
                DialogueEngine.Instance.ProcessInput(inputText);
            }
            else
            {
                Debug.LogError("[DialogueUIController] DialogueEngineが見つかりません！");
            }

            // テストしやすいように、送信後は入力欄を空にして再フォーカスする
            chatInputField.text = "";
            chatInputField.ActivateInputField();
        }
    }
}
