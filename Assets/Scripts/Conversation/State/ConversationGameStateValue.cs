using System;
using System.Collections.Generic;
using System.Linq;

namespace Backgammon.Conversation
{
    public enum ConversationGameStateValueKind
    {
        Integer,
        Boolean,
        String,
        StringList
    }

    [Serializable]
    public sealed class ConversationGameStateValue
    {
        public ConversationGameStateValueKind kind;
        public int intValue;
        public bool boolValue;
        public string stringValue;
        public List<string> listValue = new();

        public static ConversationGameStateValue FromInt(int value)
        {
            return new ConversationGameStateValue
            {
                kind = ConversationGameStateValueKind.Integer,
                intValue = value
            };
        }

        public static ConversationGameStateValue FromBool(bool value)
        {
            return new ConversationGameStateValue
            {
                kind = ConversationGameStateValueKind.Boolean,
                boolValue = value
            };
        }

        public static ConversationGameStateValue FromString(string value)
        {
            return new ConversationGameStateValue
            {
                kind = ConversationGameStateValueKind.String,
                stringValue = value ?? string.Empty
            };
        }

        public static ConversationGameStateValue FromEnum<T>(T value) where T : struct, Enum
        {
            return FromString(value.ToString());
        }

        public static ConversationGameStateValue FromStringList(IEnumerable<string> values)
        {
            return new ConversationGameStateValue
            {
                kind = ConversationGameStateValueKind.StringList,
                listValue = values != null ? new List<string>(values.Where(value => value != null)) : new List<string>()
            };
        }

        public ConversationGameStateValue Clone()
        {
            return new ConversationGameStateValue
            {
                kind = kind,
                intValue = intValue,
                boolValue = boolValue,
                stringValue = stringValue,
                listValue = new List<string>(listValue)
            };
        }

        public object ToPlainObject()
        {
            switch (kind)
            {
                case ConversationGameStateValueKind.Integer:
                    return intValue;
                case ConversationGameStateValueKind.Boolean:
                    return boolValue;
                case ConversationGameStateValueKind.String:
                    return stringValue ?? string.Empty;
                case ConversationGameStateValueKind.StringList:
                    return new List<object>(listValue.Cast<object>());
                default:
                    throw new InvalidOperationException($"Unsupported game state value kind: {kind}");
            }
        }

        public string ToTokenString()
        {
            switch (kind)
            {
                case ConversationGameStateValueKind.Integer:
                    return intValue.ToString();
                case ConversationGameStateValueKind.Boolean:
                    return boolValue ? "true" : "false";
                case ConversationGameStateValueKind.String:
                    return stringValue ?? string.Empty;
                default:
                    throw new InvalidOperationException($"Value kind '{kind}' does not map to a single token.");
            }
        }
    }
}
