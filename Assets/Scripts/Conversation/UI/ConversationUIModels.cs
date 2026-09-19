using System;

namespace Backgammon.Conversation
{
    public enum ConversationLogEntryType
    {
        Message,
        PlayerInput,
        Choice,
        Event
    }

    [Serializable]
    public sealed class ConversationLogEntry
    {
        public ConversationLogEntryType type;
        public string speaker;
        public string text;
    }

    [Serializable]
    public sealed class ConversationChoiceOption
    {
        public string id;
        public string text;
    }
}
