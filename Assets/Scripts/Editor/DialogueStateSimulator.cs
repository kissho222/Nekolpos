using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Nekolpos.System;

namespace Nekolpos.EditorTools
{
    public static class DialogueStateSimulator
    {
        public struct EvaluationResult
        {
            public bool IsMatch;
            public string Message;

            public EvaluationResult(bool isMatch, string message)
            {
                IsMatch = isMatch;
                Message = message;
            }
        }

        private static readonly Regex ClauseRegex = new Regex(
            @"^\s*(?<key>[\p{L}\p{N}_]+)\s*(?<op>>=|<=|==|!=|=|>|<)\s*(?<value>.+?)\s*$",
            RegexOptions.Compiled);
        private static readonly Regex LocationConditionRegex = new Regex(
            @"^\s*H\s*:\s*(?<values>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static EvaluationResult Evaluate(DialoguePatternGroup group, DialogueState state)
        {
            if (group == null || group.Entries == null || group.Entries.Count == 0)
            {
                return new EvaluationResult(false, "Pattern が空です");
            }

            for (int i = 0; i < group.Entries.Count; i++)
            {
                EvaluationResult entryResult = Evaluate(group.Entries[i], state);
                if (!entryResult.IsMatch)
                {
                    return new EvaluationResult(false, $"order {group.Entries[i].Order}: {entryResult.Message}");
                }
            }

            return new EvaluationResult(true, "条件一致");
        }

        public static EvaluationResult Evaluate(DialogueEntry entry, DialogueState state)
        {
            if (entry == null)
            {
                return new EvaluationResult(false, "Entry is null.");
            }

            if (string.IsNullOrWhiteSpace(entry.Condition))
            {
                return new EvaluationResult(true, "条件なし");
            }

            string normalized = entry.Condition
                .Replace(" AND ", " && ")
                .Replace(" and ", " && ")
                .Replace("AND", "&&")
                .Replace("かつ", "&&")
                .Replace(" または ", " || ")
                .Replace(" OR ", " || ")
                .Replace(" or ", " || ")
                .Replace("OR", "||");

            string[] orGroups = normalized.Split(new[] { "||" }, StringSplitOptions.None);
            string lastFailure = "条件不一致";

            for (int groupIndex = 0; groupIndex < orGroups.Length; groupIndex++)
            {
                string[] andClauses = orGroups[groupIndex].Split(new[] { "&&" }, StringSplitOptions.None);
                bool groupMatched = true;
                Dictionary<string, List<string>> shorthandGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                for (int clauseIndex = 0; clauseIndex < andClauses.Length; clauseIndex++)
                {
                    string clause = andClauses[clauseIndex].Trim();
                    if (string.IsNullOrEmpty(clause))
                    {
                        continue;
                    }

                    if (TryEvaluateLocationCondition(clause, out EvaluationResult locationResult))
                    {
                        if (!locationResult.IsMatch)
                        {
                            groupMatched = false;
                            lastFailure = locationResult.Message;
                            break;
                        }

                        continue;
                    }

                    if (TryEvaluateNegatedShorthandToken(state, clause, out EvaluationResult negatedResult))
                    {
                        if (!negatedResult.IsMatch)
                        {
                            groupMatched = false;
                            lastFailure = negatedResult.Message;
                            break;
                        }

                        continue;
                    }

                    if (TryGetConditionOptionCategory(clause, out string category))
                    {
                        if (!shorthandGroups.TryGetValue(category, out List<string> tokens))
                        {
                            tokens = new List<string>();
                            shorthandGroups[category] = tokens;
                        }

                        tokens.Add(clause);
                        continue;
                    }

                    EvaluationResult clauseResult = EvaluateClause(entry, state, clause);
                    if (!clauseResult.IsMatch)
                    {
                        groupMatched = false;
                        lastFailure = clauseResult.Message;
                        break;
                    }
                }

                if (groupMatched &&
                    !EvaluateShorthandGroups(state, shorthandGroups, out string shorthandFailure))
                {
                    groupMatched = false;
                    lastFailure = shorthandFailure;
                }

                if (groupMatched)
                {
                    return new EvaluationResult(true, "条件一致");
                }
            }

            return new EvaluationResult(false, lastFailure);
        }

        private static bool EvaluateShorthandGroups(
            DialogueState state,
            Dictionary<string, List<string>> shorthandGroups,
            out string failure)
        {
            failure = "条件不一致";
            if (shorthandGroups == null || shorthandGroups.Count == 0)
            {
                return true;
            }

            foreach (KeyValuePair<string, List<string>> pair in shorthandGroups)
            {
                List<string> tokens = pair.Value;
                bool matched = false;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (EvaluateShorthandToken(state, tokens[i]))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    failure = $"{pair.Key}: {string.Join(" / ", tokens)} が不一致";
                    return false;
                }
            }

            return true;
        }

        private static bool EvaluateShorthandToken(DialogueState state, string token)
        {
            switch (NormalizeConditionOptionToken(token))
            {
                case "morning":
                    return state.timeZone == DialoguePreviewTimeZone.Morning;
                case "day":
                    return state.timeZone == DialoguePreviewTimeZone.Day;
                case "evening":
                    return state.timeZone == DialoguePreviewTimeZone.Evening;
                case "night":
                    return state.timeZone == DialoguePreviewTimeZone.Night;
                case "playerdefeatedbynekomata":
                    return state.playerDefeatedByNekomata;
                case "playerdefeatedbypredation":
                    return state.playerDefeatedByPredation;
                case "playerdefeatedbyanger":
                    return state.playerDefeatedByAnger;
                case "playerdefeatedbyaccident":
                    return state.playerDefeatedByAccident;
                default:
                    if (NormalizeConditionOptionToken(token).StartsWith("playerdefeatedby", StringComparison.Ordinal))
                    {
                        string reason = token.Substring("PlayerDefeatedBy".Length);
                        return string.Equals(
                            NormalizeReasonToken(state.lastPlayerDefeatReason),
                            NormalizeReasonToken(reason),
                            StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
            }
        }

        private static bool TryEvaluateNegatedShorthandToken(DialogueState state, string clause, out EvaluationResult result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(clause) || !clause.TrimStart().StartsWith("!", StringComparison.Ordinal))
            {
                return false;
            }

            string token = clause.TrimStart().Substring(1).TrimStart();
            if (!TryGetConditionOptionCategory(token, out _))
            {
                return false;
            }

            bool matchesPositiveCondition = EvaluateShorthandToken(state, token);
            result = new EvaluationResult(
                !matchesPositiveCondition,
                $"{token} が成立しているため不成立フラグの条件に一致しません");
            return true;
        }

        private static bool TryEvaluateLocationCondition(string clause, out EvaluationResult result)
        {
            result = default;
            Match match = LocationConditionRegex.Match(clause ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            string[] locationTokens = match.Groups["values"].Value.Split(
                new[] { ' ', '\t', '\r', '\n', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (locationTokens.Length == 0)
            {
                result = new EvaluationResult(false, "H: に位置IDが指定されていません");
                return true;
            }

            CatHomeLocation currentLocation = CatPositionController.CurrentConversationLocation;
            bool hasPositiveLocation = false;
            bool matchesPositiveLocation = false;
            for (int i = 0; i < locationTokens.Length; i++)
            {
                string locationToken = locationTokens[i];
                bool isNegated = locationToken.StartsWith("!", StringComparison.Ordinal);
                string locationId = isNegated ? locationToken.Substring(1) : locationToken;
                if (!Enum.TryParse(locationId, false, out CatHomeLocation location) ||
                    !Enum.IsDefined(typeof(CatHomeLocation), location))
                {
                    result = new EvaluationResult(false, $"未定義の基本会話地点ID: {locationId}");
                    return true;
                }

                if (isNegated)
                {
                    if (currentLocation == location)
                    {
                        result = new EvaluationResult(false, $"現在地 {currentLocation} は H: !{location} の除外対象です");
                        return true;
                    }

                    continue;
                }

                hasPositiveLocation = true;
                matchesPositiveLocation |= currentLocation == location;
            }

            bool isMatch = !hasPositiveLocation || matchesPositiveLocation;
            result = new EvaluationResult(
                isMatch,
                $"現在地 {currentLocation} は指定された H: 条件に一致しません");
            return true;
        }

        private static EvaluationResult EvaluateClause(DialogueEntry entry, DialogueState state, string clause)
        {
            Match match = ClauseRegex.Match(clause);
            if (!match.Success)
            {
                return new EvaluationResult(false, $"解釈できない条件: {clause}");
            }

            string originalKey = match.Groups["key"].Value;
            string key = NormalizeKey(originalKey);
            string op = match.Groups["op"].Value;
            string rawValue = TrimQuotes(match.Groups["value"].Value.Trim());

            switch (key)
            {
                case "affection":
                    return CompareInt("affection", state.AffectionValue, rawValue, op);
                case "sadistic":
                    return CompareInt("sadistic", state.SadisticValue, rawValue, op);
                case "concern":
                    return CompareInt("concern", state.ConcernValue, rawValue, op);
                case "hostility":
                    return CompareInt("hostility", state.HostilityValue, rawValue, op);
                case "obedience":
                    return CompareInt("obedience", state.ObedienceValue, rawValue, op);
                case "instinct":
                    return CompareInt("instinct", state.InstinctValue, rawValue, op);
                case "timezone":
                    return CompareString("timeZone", TimeZoneToToken(state.timeZone), NormalizeTimeZone(rawValue), op);
                case "pattern":
                    return CompareString("pattern", entry.Pattern, rawValue, op);
                case "order":
                    return CompareInt("order", entry.Order, rawValue, op);
                case "regexid":
                    return CompareString("regexId", entry.RegexId, rawValue, op);
                case "responsetype":
                    return CompareString("responseType", entry.ResponseType, rawValue, op);
                case "actionid":
                    return CompareString("actionId", entry.ActionId, rawValue, op);
                case "priority":
                    return CompareInt("priority", entry.Priority, rawValue, op);
                case "hideui":
                    return CompareBool("hideUI", entry.HideUI, rawValue, op);
                case "animation":
                    return CompareString("animation", entry.Animation, rawValue, op);
                case "lastplayerdefeatreason":
                    return CompareString("LastPlayerDefeatReason", NormalizeReasonToken(state.lastPlayerDefeatReason), NormalizeReasonToken(rawValue), op);
                case "intent":
                    return CompareString("intent", entry.Intent, rawValue, op);
                case "reactiontype":
                    return CompareString("reactionType", entry.ReactionType, rawValue, op);
                case "repeatcount":
                    return CompareInt("repeatCount", entry.RepeatCount, rawValue, op);
                case "speechcontrol":
                    return CompareString("speechControl", entry.SpeechControl, rawValue, op);
                case "id":
                    return CompareString("id", entry.InternalId, rawValue, op);
                default:
                    string extraValue = entry.GetAdditionalValue(originalKey);
                    if (!string.IsNullOrEmpty(extraValue))
                    {
                        return CompareString(originalKey, extraValue, rawValue, op);
                    }

                    return new EvaluationResult(false, $"未対応の条件キー: {originalKey}");
            }
        }

        private static EvaluationResult CompareInt(string key, int actual, string rawExpected, string op)
        {
            if (!int.TryParse(rawExpected, out int expected))
            {
                return new EvaluationResult(false, $"{key} の数値解釈に失敗: {rawExpected}");
            }

            bool matched = op switch
            {
                "=" => actual == expected,
                "==" => actual == expected,
                "!=" => actual != expected,
                ">" => actual > expected,
                "<" => actual < expected,
                ">=" => actual >= expected,
                "<=" => actual <= expected,
                _ => false
            };

            return new EvaluationResult(matched, $"{key}: {actual} {op} {expected} が不一致");
        }

        private static EvaluationResult CompareString(string key, string actual, string expected, string op)
        {
            actual = (actual ?? string.Empty).Trim();
            expected = (expected ?? string.Empty).Trim();

            if (op != "=" && op != "==" && op != "!=")
            {
                return new EvaluationResult(false, $"{key} は文字列比較で {op} を使えません");
            }

            bool matched = op == "!="
                ? !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                : string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

            return new EvaluationResult(matched, $"{key}: {actual} {op} {expected} が不一致");
        }

        private static EvaluationResult CompareBool(string key, bool actual, string expected, string op)
        {
            if (op != "=" && op != "==" && op != "!=")
            {
                return new EvaluationResult(false, $"{key} は真偽値比較で {op} を使えません");
            }

            bool parsedExpected = ParseBool(expected);
            bool matched = op == "!=" ? actual != parsedExpected : actual == parsedExpected;
            return new EvaluationResult(matched, $"{key}: {actual} {op} {parsedExpected} が不一致");
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "true" || normalized == "1" || normalized == "yes" || normalized == "on";
        }

        private static string NormalizeKey(string key)
        {
            string normalized = DialogueEntry.NormalizeHeader(key);
            switch (normalized)
            {
                case "love":
                case "aff":
                case "affection":
                case "愛情":
                    return "affection";
                case "sadistic":
                case "sadism":
                case "ドs":
                case "どs":
                    return "sadistic";
                case "concern":
                case "care":
                case "worry":
                case "心配":
                    return "concern";
                case "enemy":
                case "hostile":
                case "hostility":
                case "敵対":
                    return "hostility";
                case "obedience":
                case "submissive":
                case "従順":
                    return "obedience";
                case "instinct":
                case "本能":
                    return "instinct";
                case "time":
                case "timezone":
                case "時間帯":
                    return "timezone";
                case "regexid":
                case "regex":
                    return "regexid";
                case "responsetype":
                case "type":
                    return "responsetype";
                case "actionid":
                case "action":
                    return "actionid";
                default:
                    return normalized;
            }
        }

        private static string NormalizeTimeZone(string value)
        {
            string normalized = DialogueEntry.NormalizeHeader(value);
            switch (normalized)
            {
                case "朝":
                case "morning":
                    return "morning";
                case "昼":
                case "day":
                case "noon":
                    return "day";
                case "夕":
                case "evening":
                    return "evening";
                case "夜":
                case "night":
                    return "night";
                default:
                    return normalized;
            }
        }

        private static bool TryGetConditionOptionCategory(string clause, out string category)
        {
            string normalized = NormalizeConditionOptionToken(clause);
            switch (normalized)
            {
                case "winter":
                case "fall":
                case "summer":
                case "spring":
                    category = "season";
                    return true;
                case "snow":
                case "sunny":
                case "rain":
                    category = "weather";
                    return true;
                case "cathungry":
                case "playerhungry":
                    category = "hunger";
                    return true;
                case "night":
                case "evening":
                case "day":
                case "morning":
                    category = "time";
                    return true;
                case "male":
                case "female":
                    category = "gender";
                    return true;
                case "duringestrus":
                    category = "estrus";
                    return true;
                case "playerdefeatedbynekomata":
                case "playerdefeatedbypredation":
                case "playerdefeatedbyanger":
                case "playerdefeatedbyaccident":
                    category = "defeat";
                    return true;
                default:
                    if (normalized.StartsWith("playerdefeatedby", StringComparison.Ordinal))
                    {
                        category = "defeat";
                        return true;
                    }

                    category = null;
                    return false;
            }
        }

        private static string NormalizeConditionOptionToken(string value)
        {
            return DialogueEntry.NormalizeHeader(value);
        }

        private static string NormalizeReasonToken(string value)
        {
            return DialogueEntry.NormalizeHeader(value);
        }

        private static string TimeZoneToToken(DialoguePreviewTimeZone timeZone)
        {
            switch (timeZone)
            {
                case DialoguePreviewTimeZone.Morning:
                    return "morning";
                case DialoguePreviewTimeZone.Evening:
                    return "evening";
                case DialoguePreviewTimeZone.Night:
                    return "night";
                default:
                    return "day";
            }
        }

        private static string TrimQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}
