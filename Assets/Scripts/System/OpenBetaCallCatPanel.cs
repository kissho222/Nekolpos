using System;
using System.Collections;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Audio;
using Nekolpos.Data;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.System
{
    public sealed class OpenBetaCallCatPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private TMP_Text sendButtonLabel;
        [SerializeField] private InputField callInput;
        [SerializeField] private Button sendButton;

        private Action<string> submitted;

        public void Configure(string body, string sendLabel, Action<string> onSubmitted)
        {
            submitted = onSubmitted;
            if (bodyText != null)
            {
                bodyText.richText = true;
                bodyText.text = body;
            }

            bool showInputControls = submitted != null;
            if (sendButtonLabel != null) sendButtonLabel.text = sendLabel;
            if (callInput != null)
            {
                NormalizeInputCaret(callInput);
                callInput.gameObject.SetActive(showInputControls);
                EnsureWebGLImeInputBridge(callInput);
            }

            if (sendButton != null) sendButton.gameObject.SetActive(showInputControls);
            if (sendButton != null)
            {
                sendButton.onClick.RemoveAllListeners();
                if (showInputControls)
                {
                    sendButton.onClick.AddListener(Submit);
                }
            }
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshWebGLImeInputBridge(callInput);
#endif
        }

        private void Submit()
        {
            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                callInput?.ActivateInputField();
                return;
            }

            HideWebGLImeInputBridge(callInput);
            submitted?.Invoke(callInput != null ? callInput.text.Trim() : string.Empty);
        }

        private void OnDisable()
        {
            HideWebGLImeInputBridge(callInput);
        }

        private static void NormalizeInputCaret(InputField input)
        {
            if (input == null)
            {
                return;
            }

            string text = input.text ?? string.Empty;
            int caret = Mathf.Clamp(input.caretPosition, 0, text.Length);
            input.caretPosition = caret;
            input.selectionAnchorPosition = caret;
            input.selectionFocusPosition = caret;
        }

        private static void EnsureWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            if (bridge == null)
            {
                bridge = input.gameObject.AddComponent<WebGLImeInputBridge>();
            }

            bridge.Bind(input);
#endif
        }

        private static void RefreshWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            bridge?.Refresh();
#endif
        }

        private static void HideWebGLImeInputBridge(InputField input)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input == null)
            {
                return;
            }

            if (input.isFocused)
            {
                input.DeactivateInputField();
            }

            WebGLImeInputBridge bridge = input.GetComponent<WebGLImeInputBridge>();
            bridge?.ForceHideDomInput();
#endif
        }
    }
}
