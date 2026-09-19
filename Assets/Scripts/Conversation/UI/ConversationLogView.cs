using System.Collections.Generic;
using System.Text;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Backgammon.Conversation
{
    public sealed class ConversationLogView : MonoBehaviour
    {
        [SerializeField] private TMP_Text logText;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private int maxEntries = 200;

        private readonly List<ConversationLogEntry> entries = new();
        private readonly StringBuilder builder = new();

        public IReadOnlyList<ConversationLogEntry> Entries => entries;

        public void AddEntry(ConversationLogEntryType type, string speaker, string text)
        {
            if (type == ConversationLogEntryType.PlayerInput)
            {
                PlayerInputFontAssetProvider.AddCharacters(text);
            }

            entries.Add(new ConversationLogEntry
            {
                type = type,
                speaker = speaker ?? string.Empty,
                text = text ?? string.Empty
            });

            if (maxEntries > 0 && entries.Count > maxEntries)
            {
                entries.RemoveAt(0);
            }

            RebuildText();
            ScrollToBottom();
        }

        public void Clear()
        {
            entries.Clear();
            if (logText != null)
            {
                logText.text = string.Empty;
            }
        }

        private void Reset()
        {
            logText = GetComponentInChildren<TMP_Text>(true);
            scrollRect = GetComponentInChildren<ScrollRect>(true);
        }

        private void Awake()
        {
            if (logText == null)
            {
                logText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void RebuildText()
        {
            if (logText == null)
            {
                return;
            }

            if (ContainsPlayerInputEntry())
            {
                PlayerInputFontAssetProvider.ApplyToPlayerInput(logText);
            }

            builder.Length = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendLine();
                }

                AppendEntry(builder, entries[i]);
            }

            logText.text = builder.ToString();
        }

        private bool ContainsPlayerInputEntry()
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].type == ConversationLogEntryType.PlayerInput)
                {
                    return true;
                }
            }

            return false;
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private static void AppendEntry(StringBuilder target, ConversationLogEntry entry)
        {
            switch (entry.type)
            {
                case ConversationLogEntryType.Message:
                    if (!string.IsNullOrWhiteSpace(entry.speaker))
                    {
                        target.Append(entry.speaker);
                        target.Append(": ");
                    }
                    target.Append(entry.text ?? string.Empty);
                    return;
                case ConversationLogEntryType.PlayerInput:
                    target.Append("> ");
                    target.Append(RichTextEscaper.Escape(entry.text));
                    return;
                case ConversationLogEntryType.Choice:
                    target.Append("[Choice] ");
                    target.Append(entry.text ?? string.Empty);
                    return;
                case ConversationLogEntryType.Event:
                    target.Append("[Event] ");
                    target.Append(entry.text ?? string.Empty);
                    return;
                default:
                    target.Append(entry.text ?? string.Empty);
                    return;
            }
        }
    }
}
