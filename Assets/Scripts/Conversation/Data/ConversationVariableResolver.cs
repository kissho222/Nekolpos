using System;
using System.Collections.Generic;
using System.Text;

namespace Backgammon.Conversation
{
    public static class ConversationVariableResolver
    {
        public static string Resolve(string template, ConversationGameState state, IReadOnlyDictionary<string, string> overrides = null)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(template.Length);
            var index = 0;
            while (index < template.Length)
            {
                var start = template.IndexOf("{{", index, StringComparison.Ordinal);
                if (start < 0)
                {
                    builder.Append(template, index, template.Length - index);
                    break;
                }

                builder.Append(template, index, start - index);
                var end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    builder.Append(template, start, template.Length - start);
                    break;
                }

                var token = template.Substring(start + 2, end - start - 2).Trim();
                builder.Append(ResolveToken(token, state, overrides));
                index = end + 2;
            }

            return builder.ToString();
        }

        private static string ResolveToken(string token, ConversationGameState state, IReadOnlyDictionary<string, string> overrides)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            if (overrides != null && overrides.TryGetValue(token, out var overrideValue))
            {
                return SanitizeResolvedValue(token, overrideValue);
            }

            if (state == null || !state.TryGetValue(token, out var value) || value == null)
            {
                return string.Empty;
            }

            return value.kind switch
            {
                ConversationGameStateValueKind.Integer => value.intValue.ToString(),
                ConversationGameStateValueKind.Boolean => value.boolValue ? "true" : "false",
                ConversationGameStateValueKind.String => SanitizeResolvedValue(token, value.stringValue),
                ConversationGameStateValueKind.StringList => string.Join(", ", value.listValue),
                _ => string.Empty
            };
        }

        private static string SanitizeResolvedValue(string token, string value)
        {
            string resolved = value ?? string.Empty;
            return IsRenameToken(token) ? resolved.Trim() : resolved;
        }

        private static bool IsRenameToken(string token)
        {
            return string.Equals(token, "CAT_NAME", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "CatName", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "CAT_PRONOUN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "CatPronoun", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "PLAYER_CALLING", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "PlayerCalling", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "PLAYER_NAME", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "PlayerName", StringComparison.OrdinalIgnoreCase);
        }
    }
}
