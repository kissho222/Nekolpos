using System;
using System.Collections.Generic;

namespace Backgammon.Conversation
{
    public sealed class ConversationGameStateEffect
    {
        public string type;
        public string key;
        public ConversationGameStateValue value;
    }

    public sealed class ConversationGameStateEffectSet
    {
        public List<ConversationGameStateEffect> effects = new();
    }

    public static class ConversationGameStateEffectApplier
    {
        public static ConversationGameStateEffectSet ParseEffectSetJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ConversationGameStateEffectSet();
            }

            var parsed = ConversationMiniJson.Deserialize(json);
            if (parsed is not Dictionary<string, object> root)
            {
                throw new FormatException("Effect JSON root must be an object.");
            }

            if (!root.TryGetValue("effects", out var effectListObject) || effectListObject is not List<object> effectItems)
            {
                return new ConversationGameStateEffectSet();
            }

            var result = new ConversationGameStateEffectSet();
            for (var i = 0; i < effectItems.Count; i++)
            {
                if (effectItems[i] is not Dictionary<string, object> effectObject)
                {
                    throw new FormatException("Each effect entry must be an object.");
                }

                if (!effectObject.TryGetValue("type", out var typeObject) || typeObject is not string type)
                {
                    throw new FormatException("Effect type must be a string.");
                }

                if (!effectObject.TryGetValue("key", out var keyObject) || keyObject is not string key)
                {
                    throw new FormatException("Effect key must be a string.");
                }

                effectObject.TryGetValue("value", out var valueObject);
                result.effects.Add(new ConversationGameStateEffect
                {
                    type = type,
                    key = key,
                    value = ConvertValue(valueObject)
                });
            }

            return result;
        }

        public static void Apply(ConversationGameState state, IEnumerable<ConversationGameStateEffect> effects)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (effects == null)
            {
                return;
            }

            foreach (var effect in effects)
            {
                Apply(state, effect);
            }
        }

        public static void Apply(ConversationGameState state, ConversationGameStateEffect effect)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (effect == null || string.IsNullOrWhiteSpace(effect.type) || string.IsNullOrWhiteSpace(effect.key))
            {
                return;
            }

            switch (effect.type.Trim().ToLowerInvariant())
            {
                case "set":
                    state.SetValue(effect.key, effect.value ?? ConversationGameStateValue.FromString(string.Empty));
                    return;
                case "increment":
                    state.IncrementInt(effect.key, ReadIntValue(effect));
                    return;
                case "decrement":
                    state.IncrementInt(effect.key, -ReadIntValue(effect));
                    return;
                case "add_to_list":
                    state.AddToStringList(effect.key, ReadStringValue(effect));
                    return;
                case "remove_from_list":
                    state.RemoveFromStringList(effect.key, ReadStringValue(effect));
                    return;
                case "remove_key":
                    state.Remove(effect.key);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported effect type: {effect.type}");
            }
        }

        private static int ReadIntValue(ConversationGameStateEffect effect)
        {
            if (effect.value == null)
            {
                return 1;
            }

            if (effect.value.kind != ConversationGameStateValueKind.Integer)
            {
                throw new InvalidOperationException($"Effect '{effect.type}' requires an integer value.");
            }

            return effect.value.intValue;
        }

        private static string ReadStringValue(ConversationGameStateEffect effect)
        {
            if (effect.value == null)
            {
                return string.Empty;
            }

            if (effect.value.kind != ConversationGameStateValueKind.String)
            {
                throw new InvalidOperationException($"Effect '{effect.type}' requires a string value.");
            }

            return effect.value.stringValue ?? string.Empty;
        }

        private static ConversationGameStateValue ConvertValue(object parsedValue)
        {
            switch (parsedValue)
            {
                case null:
                    return null;
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
                        throw new FormatException($"Floating-point effect values are not supported: {doubleValue}");
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
                {
                    var items = new List<string>(listValue.Count);
                    for (var i = 0; i < listValue.Count; i++)
                    {
                        items.Add(listValue[i]?.ToString() ?? string.Empty);
                    }

                    return ConversationGameStateValue.FromStringList(items);
                }
                default:
                    throw new FormatException($"Unsupported effect value type: {parsedValue.GetType().Name}");
            }
        }
    }
}
