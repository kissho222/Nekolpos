using System;
using System.Collections.Generic;

namespace Backgammon.Conversation
{
    [Serializable]
    public sealed class ConversationEventDefinition
    {
        public string id;
        public string eventName;
        public string eventId;
        public bool waitForCompletion = true;
        public ConversationEventParameters parameters = new();
        public List<ConversationGameStateEffect> effects = new();
        public List<ConversationEventDefinition> sequence = new();
        public List<ConversationEventDefinition> parallel = new();
    }

    [Serializable]
    public sealed class ConversationEventParameters
    {
        private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object> Values => values;

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            values[key] = value;
        }

        public bool TryGetString(string key, out string value)
        {
            if (TryGetValue(key, out var raw))
            {
                value = raw?.ToString() ?? string.Empty;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetInt(string key, out int value)
        {
            if (!TryGetValue(key, out var raw) || raw == null)
            {
                value = default;
                return false;
            }

            switch (raw)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = checked((int)longValue);
                    return true;
                case double doubleValue:
                    value = checked((int)doubleValue);
                    return true;
                case string stringValue when int.TryParse(stringValue, out var parsed):
                    value = parsed;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public bool TryGetBool(string key, out bool value)
        {
            if (!TryGetValue(key, out var raw) || raw == null)
            {
                value = default;
                return false;
            }

            switch (raw)
            {
                case bool boolValue:
                    value = boolValue;
                    return true;
                case string stringValue when bool.TryParse(stringValue, out var parsed):
                    value = parsed;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public bool TryGetStringList(string key, out List<string> list)
        {
            if (!TryGetValue(key, out var raw) || raw == null)
            {
                list = null;
                return false;
            }

            switch (raw)
            {
                case List<object> objectList:
                    list = new List<string>(objectList.Count);
                    for (var i = 0; i < objectList.Count; i++)
                    {
                        list.Add(objectList[i]?.ToString() ?? string.Empty);
                    }

                    return true;
                case List<string> stringList:
                    list = new List<string>(stringList);
                    return true;
                case string stringValue:
                    list = new List<string> { stringValue };
                    return true;
                default:
                    list = null;
                    return false;
            }
        }

        public bool TryGetValue(string key, out object value)
        {
            if (!string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out var existing))
            {
                value = existing;
                return true;
            }

            value = null;
            return false;
        }

        public ConversationEventParameters Clone()
        {
            var clone = new ConversationEventParameters();
            foreach (var pair in values)
            {
                clone.values[pair.Key] = CloneValue(pair.Value);
            }

            return clone;
        }

        private static object CloneValue(object value)
        {
            return value switch
            {
                List<object> listValue => new List<object>(listValue),
                List<string> stringList => new List<string>(stringList),
                _ => value
            };
        }
    }

    [Serializable]
    public sealed class ConversationEventExecutionResult
    {
        public bool succeeded;
        public List<string> executedEventIds = new();
        public List<string> dispatchedEvents = new();
        public List<string> eventLogs = new();
        public List<ConversationEffectExecutionDebug> appliedEffects = new();
        public string nextStateJson = "{}";
    }

    [Serializable]
    public sealed class ConversationEffectExecutionDebug
    {
        public string type;
        public string key;
        public string beforeValue;
        public string afterValue;
    }
}
