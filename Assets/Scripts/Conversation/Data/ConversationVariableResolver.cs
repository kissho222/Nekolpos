using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Backgammon.Conversation
{
    public static class ConversationVariableResolver
    {
        private static readonly Regex RuntimePlaceholderRegex = new Regex(
            @"\{\{\s*([A-Za-z0-9_?]+)\s*\}\}|(?<!\{)\{\s*([A-Za-z_][A-Za-z0-9_?]*)\s*\}(?!\})", RegexOptions.Compiled);

        /// <summary>既存・保存済みテキストの表記揺れを1回の走査で展開する。nullの結果は未定義として保持する。</summary>
        public static string ReplaceRuntimePlaceholders(string template, Func<string, string> resolve)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            string normalized = template.Replace(@"\{", "{").Replace(@"\}", "}").Replace(@"\_", "_");
            return RuntimePlaceholderRegex.Replace(normalized, match =>
            {
                string key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return resolve(key) ?? match.Value;
            });
        }

        public static string CanonicalizeRuntimeKey(string key)
        {
            string normalized = (key ?? string.Empty).Trim().Trim('{', '}').Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "CATNAME": return "CAT_NAME";
                case "CATPRONOUN": return "CAT_PRONOUN";
                case "CATGENDER": return "CAT_GENDER";
                case "PLAYERCALLING": return "PLAYER_CALLING";
                case "PLAYERNAME": return "PLAYER_NAME";
                case "PENDINGVALUE": return "PENDING_VALUE";
                default: return normalized;
            }
        }

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

            if (Nekolpos.Dialogue.UnknownWord.WordVariables.IsVariable(token))
                return Nekolpos.Dialogue.UnknownWord.WordVariables.Resolve(token, state);

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
