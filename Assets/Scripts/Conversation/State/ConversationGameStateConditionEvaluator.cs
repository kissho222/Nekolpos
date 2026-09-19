using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Nekolpos.System;
using UnityEngine;

namespace Backgammon.Conversation
{
    public static class ConversationGameStateConditionEvaluator
    {
        private sealed class ShorthandConditionDefinition
        {
            public string Category;
            public string[] CandidateKeys;
            public string ExpectedValue;
            public bool? ExpectedBool;
        }

        private static readonly Regex ExpressionRegex = new(
            @"^\s*(?<key>[^\s]+)\s*(?<op>not\s+contains|contains|>=|<=|==|!=|>|<)\s*(?<value>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex LocationConditionRegex = new(
            @"(?<prefix>^|&&|\|\||;)\s*H\s*:\s*(?<values>[^;&|]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> WarnedUnknownLocationIds = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ShorthandConditionDefinition> ShorthandConditions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Winter"] = new ShorthandConditionDefinition { Category = "season", CandidateKeys = new[] { "Season", "CurrentSeason" }, ExpectedValue = "Winter" },
                ["Fall"] = new ShorthandConditionDefinition { Category = "season", CandidateKeys = new[] { "Season", "CurrentSeason" }, ExpectedValue = "Fall" },
                ["Summer"] = new ShorthandConditionDefinition { Category = "season", CandidateKeys = new[] { "Season", "CurrentSeason" }, ExpectedValue = "Summer" },
                ["Spring"] = new ShorthandConditionDefinition { Category = "season", CandidateKeys = new[] { "Season", "CurrentSeason" }, ExpectedValue = "Spring" },
                ["Snow"] = new ShorthandConditionDefinition { Category = "weather", CandidateKeys = new[] { "Weather", "CurrentWeather" }, ExpectedValue = "Snow" },
                ["Sunny"] = new ShorthandConditionDefinition { Category = "weather", CandidateKeys = new[] { "Weather", "CurrentWeather" }, ExpectedValue = "Sunny" },
                ["Cloudy"] = new ShorthandConditionDefinition { Category = "weather", CandidateKeys = new[] { "Weather", "CurrentWeather" }, ExpectedValue = "Cloudy" },
                ["Rain"] = new ShorthandConditionDefinition { Category = "weather", CandidateKeys = new[] { "Weather", "CurrentWeather" }, ExpectedValue = "Rainy" },
                ["Rainy"] = new ShorthandConditionDefinition { Category = "weather", CandidateKeys = new[] { "Weather", "CurrentWeather" }, ExpectedValue = "Rainy" },
                ["CatHungry"] = new ShorthandConditionDefinition { Category = "hunger", CandidateKeys = new[] { "CatHungry" }, ExpectedBool = true },
                ["PlayerHungry"] = new ShorthandConditionDefinition { Category = "hunger", CandidateKeys = new[] { "PlayerHungry" }, ExpectedBool = true },
                ["Night"] = new ShorthandConditionDefinition { Category = "time", CandidateKeys = new[] { "Phase", "CurrentPhase" }, ExpectedValue = "Night" },
                ["Evening"] = new ShorthandConditionDefinition { Category = "time", CandidateKeys = new[] { "Phase", "CurrentPhase" }, ExpectedValue = "Evening" },
                ["Day"] = new ShorthandConditionDefinition { Category = "time", CandidateKeys = new[] { "Phase", "CurrentPhase" }, ExpectedValue = "Lunch" },
                ["Morning"] = new ShorthandConditionDefinition { Category = "time", CandidateKeys = new[] { "Phase", "CurrentPhase" }, ExpectedValue = "Morning" },
                ["Male"] = new ShorthandConditionDefinition { Category = "gender", CandidateKeys = new[] { "Gender", "PlayerGender" }, ExpectedValue = "Male" },
                ["Female"] = new ShorthandConditionDefinition { Category = "gender", CandidateKeys = new[] { "Gender", "PlayerGender" }, ExpectedValue = "Female" },
                ["During estrus"] = new ShorthandConditionDefinition { Category = "estrus", CandidateKeys = new[] { "DuringEstrus", "Estrus" }, ExpectedBool = true },
                ["PlayerDefeatedByNekomata"] = new ShorthandConditionDefinition { Category = "defeat", CandidateKeys = new[] { "PlayerDefeatedByNekomata" }, ExpectedBool = true },
                ["PlayerDefeatedByPredation"] = new ShorthandConditionDefinition { Category = "defeat", CandidateKeys = new[] { "PlayerDefeatedByPredation" }, ExpectedBool = true },
                ["PlayerDefeatedByAnger"] = new ShorthandConditionDefinition { Category = "defeat", CandidateKeys = new[] { "PlayerDefeatedByAnger" }, ExpectedBool = true },
                ["PlayerDefeatedByAccident"] = new ShorthandConditionDefinition { Category = "defeat", CandidateKeys = new[] { "PlayerDefeatedByAccident" }, ExpectedBool = true },
            };

        public static bool Evaluate(ConversationGameState state, string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            string normalized = NormalizeLogicalOperators(expression);
            if (!TryEvaluateLocationConditions(normalized, out normalized))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            // H: は基本会話地点だけを参照するため、会話状態の初期化前でも評価できる。
            // それ以外の既存Conditionは、従来どおり状態なしでは成立させない。
            if (state == null)
            {
                return false;
            }

            string[] orGroups = normalized.Split(new[] { "||" }, StringSplitOptions.None);
            for (int groupIndex = 0; groupIndex < orGroups.Length; groupIndex++)
            {
                if (EvaluateAndGroup(state, orGroups[groupIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryEvaluateLocationConditions(string expression, out string expressionWithoutLocationConditions)
        {
            MatchCollection matches = LocationConditionRegex.Matches(expression ?? string.Empty);
            if (matches.Count == 0)
            {
                expressionWithoutLocationConditions = expression;
                return true;
            }

            bool passedAllLocationConditions = true;
            for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                if (!EvaluateLocationValues(matches[matchIndex].Groups["values"].Value))
                {
                    passedAllLocationConditions = false;
                }
            }

            expressionWithoutLocationConditions = LocationConditionRegex.Replace(expression, string.Empty);
            expressionWithoutLocationConditions = TrimDanglingLogicalOperators(expressionWithoutLocationConditions);
            return passedAllLocationConditions;
        }

        private static bool EvaluateLocationValues(string rawValues)
        {
            string[] tokens = (rawValues ?? string.Empty).Split(
                new[] { ' ', '\t', '\r', '\n', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                Debug.LogWarning("[ConversationCondition] H: に位置IDが指定されていません。");
                return false;
            }

            bool hasPositiveLocation = false;
            bool matchesAnyPositiveLocation = false;
            CatHomeLocation currentLocation = CatPositionController.CurrentConversationLocation;

            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                bool isNegated = token.StartsWith("!", StringComparison.Ordinal);
                string locationId = isNegated ? token.Substring(1) : token;
                if (!TryParseLocationId(locationId, out CatHomeLocation location))
                {
                    WarnUnknownLocationId(locationId);
                    return false;
                }

                if (isNegated)
                {
                    if (currentLocation == location)
                    {
                        return false;
                    }

                    continue;
                }

                hasPositiveLocation = true;
                matchesAnyPositiveLocation |= currentLocation == location;
            }

            return !hasPositiveLocation || matchesAnyPositiveLocation;
        }

        private static bool TryParseLocationId(string value, out CatHomeLocation location)
        {
            location = default;
            return !string.IsNullOrWhiteSpace(value) &&
                   Enum.TryParse(value, false, out location) &&
                   Enum.IsDefined(typeof(CatHomeLocation), location);
        }

        private static void WarnUnknownLocationId(string locationId)
        {
            string normalizedId = locationId ?? string.Empty;
            if (WarnedUnknownLocationIds.Add(normalizedId))
            {
                Debug.LogWarning($"[ConversationCondition] 未定義の基本会話地点ID '{normalizedId}' が H: に指定されています。条件は不成立として扱います。");
            }
        }

        private static string TrimDanglingLogicalOperators(string expression)
        {
            string trimmed = (expression ?? string.Empty).Trim();
            while (trimmed.StartsWith("&&", StringComparison.Ordinal) ||
                   trimmed.StartsWith("||", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(2).TrimStart();
            }

            while (trimmed.EndsWith("&&", StringComparison.Ordinal) ||
                   trimmed.EndsWith("||", StringComparison.Ordinal) ||
                   trimmed.EndsWith(";", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(trimmed.EndsWith(";", StringComparison.Ordinal) ? trimmed.Length - 1 : trimmed.Length - 2).TrimEnd();
            }

            return trimmed;
        }

        private static bool EvaluateAndGroup(ConversationGameState state, string groupExpression)
        {
            string[] clauses = groupExpression.Split(new[] { "&&" }, StringSplitOptions.None);
            Dictionary<string, List<ShorthandConditionDefinition>> shorthandGroups = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < clauses.Length; i++)
            {
                string clause = clauses[i].Trim();
                if (string.IsNullOrWhiteSpace(clause))
                {
                    continue;
                }

                if (TryGetShorthandCondition(clause, out ShorthandConditionDefinition shorthand))
                {
                    if (!shorthandGroups.TryGetValue(shorthand.Category, out List<ShorthandConditionDefinition> definitions))
                    {
                        definitions = new List<ShorthandConditionDefinition>();
                        shorthandGroups.Add(shorthand.Category, definitions);
                    }

                    definitions.Add(shorthand);
                    continue;
                }

                if (TryGetNegatedShorthandCondition(clause, out shorthand))
                {
                    if (EvaluateShorthandCondition(state, shorthand))
                    {
                        return false;
                    }

                    continue;
                }

                if (!EvaluateSingleClause(state, clause))
                {
                    return false;
                }
            }

            foreach (KeyValuePair<string, List<ShorthandConditionDefinition>> pair in shorthandGroups)
            {
                bool matched = false;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (EvaluateShorthandCondition(state, pair.Value[i]))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EvaluateSingleClause(ConversationGameState state, string expression)
        {
            var match = ExpressionRegex.Match(expression);
            if (!match.Success)
            {
                throw new FormatException($"Invalid condition expression: '{expression}'");
            }

            var key = match.Groups["key"].Value;
            var op = NormalizeOperator(match.Groups["op"].Value);
            var token = ParseToken(match.Groups["value"].Value);

            if (!state.TryGetValue(key, out var actualValue))
            {
                return false;
            }

            switch (op)
            {
                case "contains":
                    return EvaluateContains(actualValue, token);
                case "not contains":
                    return !EvaluateContains(actualValue, token);
                case "==":
                    return EvaluateEquality(actualValue, token);
                case "!=":
                    return !EvaluateEquality(actualValue, token);
                case ">":
                case "<":
                case ">=":
                case "<=":
                    return EvaluateNumeric(actualValue, token, op);
                default:
                    throw new InvalidOperationException($"Unsupported operator: {op}");
            }
        }

        private static string NormalizeLogicalOperators(string expression)
        {
            return (expression ?? string.Empty)
                .Replace(" AND ", " && ")
                .Replace(" and ", " && ")
                .Replace("AND", "&&")
                .Replace("かつ", "&&")
                .Replace(" または ", " || ")
                .Replace(" OR ", " || ")
                .Replace(" or ", " || ")
                .Replace("OR", "||");
        }

        private static bool TryGetShorthandCondition(string expression, out ShorthandConditionDefinition definition)
        {
            string trimmed = expression.Trim();
            if (ShorthandConditions.TryGetValue(trimmed, out definition))
            {
                return true;
            }

            if (trimmed.StartsWith("PlayerDefeatedBy", StringComparison.OrdinalIgnoreCase))
            {
                definition = new ShorthandConditionDefinition
                {
                    Category = "defeat",
                    CandidateKeys = new[] { trimmed },
                    ExpectedBool = true
                };
                return true;
            }

            return false;
        }

        private static bool TryGetNegatedShorthandCondition(string expression, out ShorthandConditionDefinition definition)
        {
            definition = null;
            string trimmed = expression?.Trim();
            return !string.IsNullOrEmpty(trimmed) &&
                   trimmed.StartsWith("!", StringComparison.Ordinal) &&
                   TryGetShorthandCondition(trimmed.Substring(1).TrimStart(), out definition);
        }

        private static bool EvaluateShorthandCondition(ConversationGameState state, ShorthandConditionDefinition definition)
        {
            if (state == null || definition == null || definition.CandidateKeys == null)
            {
                return false;
            }

            for (int i = 0; i < definition.CandidateKeys.Length; i++)
            {
                if (!state.TryGetValue(definition.CandidateKeys[i], out var actualValue) || actualValue == null)
                {
                    continue;
                }

                if (definition.ExpectedBool.HasValue)
                {
                    if (actualValue.kind == ConversationGameStateValueKind.Boolean &&
                        actualValue.boolValue == definition.ExpectedBool.Value)
                    {
                        return true;
                    }

                    continue;
                }

                if (actualValue.kind == ConversationGameStateValueKind.String &&
                    string.Equals(actualValue.stringValue ?? string.Empty, definition.ExpectedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool EvaluateAll(ConversationGameState state, IEnumerable<string> expressions)
        {
            if (expressions == null)
            {
                return true;
            }

            foreach (var expression in expressions)
            {
                if (!Evaluate(state, expression))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EvaluateContains(ConversationGameStateValue actualValue, ParsedToken token)
        {
            var needle = token.ToComparableString();
            if (actualValue.kind == ConversationGameStateValueKind.StringList)
            {
                return actualValue.listValue.Contains(needle);
            }

            if (actualValue.kind == ConversationGameStateValueKind.String)
            {
                return (actualValue.stringValue ?? string.Empty).IndexOf(needle, StringComparison.Ordinal) >= 0;
            }

            return false;
        }

        private static bool EvaluateEquality(ConversationGameStateValue actualValue, ParsedToken token)
        {
            switch (actualValue.kind)
            {
                case ConversationGameStateValueKind.Integer:
                    return token.kind == ParsedTokenKind.Integer && actualValue.intValue == token.intValue;
                case ConversationGameStateValueKind.Boolean:
                    return token.kind == ParsedTokenKind.Boolean && actualValue.boolValue == token.boolValue;
                case ConversationGameStateValueKind.String:
                    return string.Equals(actualValue.stringValue ?? string.Empty, token.ToComparableString(), StringComparison.Ordinal);
                case ConversationGameStateValueKind.StringList:
                    return false;
                default:
                    return false;
            }
        }

        private static bool EvaluateNumeric(ConversationGameStateValue actualValue, ParsedToken token, string op)
        {
            if (actualValue.kind != ConversationGameStateValueKind.Integer || token.kind != ParsedTokenKind.Integer)
            {
                return false;
            }

            return op switch
            {
                ">" => actualValue.intValue > token.intValue,
                "<" => actualValue.intValue < token.intValue,
                ">=" => actualValue.intValue >= token.intValue,
                "<=" => actualValue.intValue <= token.intValue,
                _ => false
            };
        }

        private static string NormalizeOperator(string value)
        {
            return Regex.Replace(value.Trim(), "\\s+", " ").ToLowerInvariant();
        }

        private static ParsedToken ParseToken(string tokenText)
        {
            var trimmed = tokenText.Trim();
            if (trimmed.Length >= 2
                && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
            {
                return ParsedToken.FromString(UnescapeQuoted(trimmed.Substring(1, trimmed.Length - 2)));
            }

            if (bool.TryParse(trimmed, out var boolValue))
            {
                return ParsedToken.FromBool(boolValue);
            }

            if (int.TryParse(trimmed, out var intValue))
            {
                return ParsedToken.FromInt(intValue);
            }

            return ParsedToken.FromString(trimmed);
        }

        private static string UnescapeQuoted(string value)
        {
            return value
                .Replace("\\\"", "\"")
                .Replace("\\'", "'")
                .Replace("\\\\", "\\");
        }

        private enum ParsedTokenKind
        {
            Integer,
            Boolean,
            String
        }

        private readonly struct ParsedToken
        {
            private ParsedToken(ParsedTokenKind kind, int intValue, bool boolValue, string stringValue)
            {
                this.kind = kind;
                this.intValue = intValue;
                this.boolValue = boolValue;
                this.stringValue = stringValue;
            }

            public ParsedTokenKind kind { get; }
            public int intValue { get; }
            public bool boolValue { get; }
            public string stringValue { get; }

            public string ToComparableString()
            {
                return kind switch
                {
                    ParsedTokenKind.Integer => intValue.ToString(),
                    ParsedTokenKind.Boolean => boolValue ? "true" : "false",
                    _ => stringValue ?? string.Empty
                };
            }

            public static ParsedToken FromInt(int value)
            {
                return new ParsedToken(ParsedTokenKind.Integer, value, false, null);
            }

            public static ParsedToken FromBool(bool value)
            {
                return new ParsedToken(ParsedTokenKind.Boolean, 0, value, null);
            }

            public static ParsedToken FromString(string value)
            {
                return new ParsedToken(ParsedTokenKind.String, 0, false, value ?? string.Empty);
            }
        }
    }
}
