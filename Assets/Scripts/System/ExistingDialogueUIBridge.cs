using System;
using System.Collections.Generic;
using UnityEngine;
using Yarn.Unity;

namespace Nekolpos.System
{
    /// <summary>
    /// Yarn Spinner の標準 UI を使わず、既存 ChatUIController に行表示と選択肢表示を委譲する。
    /// </summary>
    public sealed class ExistingDialogueUIBridge : DialoguePresenterBase
    {
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private string defaultSpeakerName = "{{CAT_NAME}}";
        [SerializeField] private bool returnToInputOnDialogueComplete = true;
        private int activeOptionRequestId;
        private YarnManager yarnManager;
        public ChatUIController ChatUI => chatUI;

        public void Bind(ChatUIController targetChatUI)
        {
            chatUI = targetChatUI;
        }

        public override YarnTask OnDialogueStartedAsync()
        {
            CancelActiveOptions();
            chatUI?.EnterDialogueMode();
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnDialogueCompleteAsync()
        {
            CancelActiveOptions();
            if (returnToInputOnDialogueComplete)
            {
                chatUI?.EnterPlayerInputMode();
            }

            return YarnTask.CompletedTask;
        }

        public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            if (chatUI == null)
            {
                return;
            }

            if (yarnManager == null)
            {
                yarnManager = FindFirstObjectByType<YarnManager>();
            }

            chatUI.EnterDialogueMode();
            chatUI.HideChoices();

            string speakerName = string.IsNullOrWhiteSpace(line.CharacterName)
                ? defaultSpeakerName
                : line.CharacterName;

            string message = line.TextWithoutCharacterName.Text;
            bool typingCompleted = false;

            void HandleTypingCompleted()
            {
                typingCompleted = true;
            }

            chatUI.OnTypingCompleted += HandleTypingCompleted;
            try
            {
                chatUI.ShowMessage(
                    speakerName,
                    message,
                    new DialogueLogEntry
                    {
                        Speaker = DialogueLogManager.SpeakerCat,
                        Source = DialogueLogManager.SourceYarn,
                        NodeId = yarnManager != null ? yarnManager.CurrentNodeName : string.Empty
                    });

                while (!typingCompleted && !token.IsNextContentRequested)
                {
                    if (token.IsHurryUpRequested)
                    {
                        chatUI.SkipCurrentTyping();
                    }

                    await YarnTask.Yield();
                }
            }
            finally
            {
                chatUI.OnTypingCompleted -= HandleTypingCompleted;
            }

            if (token.IsNextContentRequested)
            {
                chatUI.SkipCurrentTyping();
                return;
            }

            bool advanceRequested = false;

            void HandleAdvanceRequested()
            {
                advanceRequested = true;
            }

            chatUI.OnWaitInputCompleted += HandleAdvanceRequested;
            try
            {
                while (!advanceRequested && !token.IsNextContentRequested)
                {
                    await YarnTask.Yield();
                }
            }
            finally
            {
                chatUI.OnWaitInputCompleted -= HandleAdvanceRequested;
            }
        }

        public override async YarnTask<DialogueOption> RunOptionsAsync(DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
        {
            if (chatUI == null || dialogueOptions == null || dialogueOptions.Length == 0)
            {
                return null;
            }

            List<DialogueOption> availableOptions = new List<DialogueOption>();
            for (int i = 0; i < dialogueOptions.Length; i++)
            {
                if (dialogueOptions[i].IsAvailable)
                {
                    availableOptions.Add(dialogueOptions[i]);
                }
            }

            if (availableOptions.Count == 0)
            {
                return null;
            }

            chatUI.EnterDialogueMode();

            List<string> labels = new List<string>(availableOptions.Count);
            for (int i = 0; i < availableOptions.Count; i++)
            {
                labels.Add(availableOptions[i].Line.TextWithoutCharacterName.Text);
            }

            int optionRequestId = ++activeOptionRequestId;
            int selectedIndex = -1;
            chatUI.ShowChoiceOptions(labels, index =>
            {
                if (optionRequestId != activeOptionRequestId)
                {
                    return;
                }

                selectedIndex = index;
            });

            try
            {
                while (selectedIndex < 0 &&
                       optionRequestId == activeOptionRequestId &&
                       !cancellationToken.IsNextContentRequested)
                {
                    await YarnTask.Yield();
                }
            }
            finally
            {
                chatUI.HideChoices();
                if (optionRequestId == activeOptionRequestId)
                {
                    activeOptionRequestId++;
                }
            }

            if (optionRequestId != activeOptionRequestId - 1 ||
                cancellationToken.IsNextContentRequested ||
                selectedIndex < 0 ||
                selectedIndex >= availableOptions.Count)
            {
                return null;
            }

            return availableOptions[selectedIndex];
        }

        private void CancelActiveOptions()
        {
            activeOptionRequestId++;
            chatUI?.HideChoices();
        }
    }
}
