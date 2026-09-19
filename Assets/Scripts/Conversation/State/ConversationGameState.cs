using System;
using System.Collections.Generic;

namespace Backgammon.Conversation
{
    public sealed class ConversationGameState
    {
        private readonly Dictionary<string, ConversationGameStateValue> values = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, ConversationGameStateValue> Values => values;

        public bool ContainsKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && values.ContainsKey(key);
        }

        public void Clear()
        {
            values.Clear();
        }

        public bool Remove(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && values.Remove(key);
        }

        public void SetInt(string key, int value)
        {
            SetValue(key, ConversationGameStateValue.FromInt(value));
        }

        public void SetBool(string key, bool value)
        {
            SetValue(key, ConversationGameStateValue.FromBool(value));
        }

        public void SetString(string key, string value)
        {
            SetValue(key, ConversationGameStateValue.FromString(value));
        }

        public void SetEnum<T>(string key, T value) where T : struct, Enum
        {
            SetValue(key, ConversationGameStateValue.FromEnum(value));
        }

        public void SetStringList(string key, IEnumerable<string> valuesToSet)
        {
            SetValue(key, ConversationGameStateValue.FromStringList(valuesToSet));
        }

        public void SetEnumList<T>(string key, IEnumerable<T> valuesToSet) where T : struct, Enum
        {
            var tokens = new List<string>();
            if (valuesToSet != null)
            {
                foreach (var item in valuesToSet)
                {
                    tokens.Add(item.ToString());
                }
            }

            SetStringList(key, tokens);
        }

        public void SetValue(string key, ConversationGameStateValue value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Game state key must not be empty.", nameof(key));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            values[key] = value.Clone();
        }

        public bool TryGetValue(string key, out ConversationGameStateValue value)
        {
            if (!string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out var existing))
            {
                value = existing.Clone();
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetInt(string key, out int value)
        {
            if (TryGetExistingValue(key, ConversationGameStateValueKind.Integer, out var existing))
            {
                value = existing.intValue;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetBool(string key, out bool value)
        {
            if (TryGetExistingValue(key, ConversationGameStateValueKind.Boolean, out var existing))
            {
                value = existing.boolValue;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetString(string key, out string value)
        {
            if (TryGetExistingValue(key, ConversationGameStateValueKind.String, out var existing))
            {
                value = existing.stringValue ?? string.Empty;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetEnum<T>(string key, out T value) where T : struct, Enum
        {
            if (TryGetString(key, out var token) && Enum.TryParse(token, false, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetStringList(string key, out List<string> value)
        {
            if (TryGetExistingValue(key, ConversationGameStateValueKind.StringList, out var existing))
            {
                value = new List<string>(existing.listValue);
                return true;
            }

            value = null;
            return false;
        }

        public int IncrementInt(string key, int amount = 1)
        {
            var current = 0;
            if (values.TryGetValue(key, out var existing))
            {
                if (existing.kind != ConversationGameStateValueKind.Integer)
                {
                    throw new InvalidOperationException($"Game state '{key}' is not an integer.");
                }

                current = existing.intValue;
            }

            current += amount;
            values[key] = ConversationGameStateValue.FromInt(current);
            return current;
        }

        public bool AddToStringList(string key, string value)
        {
            if (value == null)
            {
                return false;
            }

            var list = GetOrCreateStringList(key);
            if (list.listValue.Contains(value))
            {
                return false;
            }

            list.listValue.Add(value);
            return true;
        }

        public bool AddToEnumList<T>(string key, T value) where T : struct, Enum
        {
            return AddToStringList(key, value.ToString());
        }

        public bool RemoveFromStringList(string key, string value)
        {
            if (value == null || !values.TryGetValue(key, out var existing))
            {
                return false;
            }

            if (existing.kind != ConversationGameStateValueKind.StringList)
            {
                throw new InvalidOperationException($"Game state '{key}' is not a string list.");
            }

            return existing.listValue.Remove(value);
        }

        public string SaveToJson(bool prettyPrint = true)
        {
            var objectMap = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                objectMap[pair.Key] = pair.Value.ToPlainObject();
            }

            return ConversationMiniJson.Serialize(objectMap, prettyPrint);
        }

        public void LoadFromJson(string json)
        {
            Clear();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var parsed = ConversationMiniJson.Deserialize(json);
            if (parsed is not Dictionary<string, object> root)
            {
                throw new FormatException("Game state JSON root must be an object.");
            }

            foreach (var pair in root)
            {
                values[pair.Key] = ConvertParsedValue(pair.Value);
            }
        }

        public static ConversationGameState FromJson(string json)
        {
            var state = new ConversationGameState();
            state.LoadFromJson(json);
            return state;
        }

        private bool TryGetExistingValue(string key, ConversationGameStateValueKind expectedKind, out ConversationGameStateValue value)
        {
            if (!string.IsNullOrWhiteSpace(key)
                && values.TryGetValue(key, out var existing)
                && existing.kind == expectedKind)
            {
                value = existing;
                return true;
            }

            value = null;
            return false;
        }

        private ConversationGameStateValue GetOrCreateStringList(string key)
        {
            if (!values.TryGetValue(key, out var existing))
            {
                existing = ConversationGameStateValue.FromStringList(null);
                values[key] = existing;
                return existing;
            }

            if (existing.kind != ConversationGameStateValueKind.StringList)
            {
                throw new InvalidOperationException($"Game state '{key}' is not a string list.");
            }

            return existing;
        }

        private static ConversationGameStateValue ConvertParsedValue(object parsedValue)
        {
            switch (parsedValue)
            {
                case null:
                    return ConversationGameStateValue.FromString(string.Empty);
                case int intValue:
                    return ConversationGameStateValue.FromInt(intValue);
                case long longValue:
                    checked
                    {
                        return ConversationGameStateValue.FromInt((int)longValue);
                    }
                case double doubleValue:
                    if (Math.Abs(doubleValue % 1d) > double.Epsilon)
                    {
                        throw new FormatException($"Floating-point values are not supported in game state JSON: {doubleValue}");
                    }

                    checked
                    {
                        return ConversationGameStateValue.FromInt((int)doubleValue);
                    }
                case bool boolValue:
                    return ConversationGameStateValue.FromBool(boolValue);
                case string stringValue:
                    return ConversationGameStateValue.FromString(stringValue);
                case List<object> listValue:
                    return ConversationGameStateValue.FromStringList(ConvertList(listValue));
                default:
                    throw new FormatException($"Unsupported game state value type: {parsedValue.GetType().Name}");
            }
        }

        private static List<string> ConvertList(List<object> listValue)
        {
            var result = new List<string>(listValue.Count);
            for (var i = 0; i < listValue.Count; i++)
            {
                var item = listValue[i];
                switch (item)
                {
                    case null:
                        result.Add(string.Empty);
                        break;
                    case string stringItem:
                        result.Add(stringItem);
                        break;
                    case bool boolItem:
                        result.Add(boolItem ? "true" : "false");
                        break;
                    case int intItem:
                        result.Add(intItem.ToString());
                        break;
                    case long longItem:
                        result.Add(longItem.ToString());
                        break;
                    case double doubleItem:
                        result.Add(doubleItem.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    default:
                        throw new FormatException($"Unsupported list item type: {item.GetType().Name}");
                }
            }

            return result;
        }
    }
}
