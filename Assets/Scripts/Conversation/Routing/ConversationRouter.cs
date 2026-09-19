using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Backgammon.Conversation
{
    public sealed class ConversationRouter
    {
        public const string LastRouteIdStateKey = "__conversation.last_route_id";
        public const string PlayedOnceIdsStateKey = "__conversation.played_once_ids";

        private readonly List<ConversationRouteDefinition> routes;

        public ConversationRouter(IEnumerable<ConversationRouteDefinition> routeDefinitions)
        {
            routes = routeDefinitions != null
                ? new List<ConversationRouteDefinition>(routeDefinitions.Where(route => route != null && !string.IsNullOrWhiteSpace(route.id)))
                : new List<ConversationRouteDefinition>();
        }

        public ConversationRouter(ConversationRouteCatalog catalog)
            : this(catalog?.Routes)
        {
        }

        public ConversationRouteResult Route(ConversationRouteRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var workingState = CloneState(request.gameState);
            var evaluations = EvaluateRoutes(request, workingState);
            var selectedEvaluation = SelectRoute(evaluations, workingState, request.randomSeed);
            var selectedRoute = selectedEvaluation?.Route;

            if (selectedRoute == null)
            {
                return new ConversationRouteResult
                {
                    matched = false,
                    nextStateJson = workingState.SaveToJson(true),
                    candidates = evaluations.ConvertAll(evaluation => evaluation.Debug)
                };
            }

            var rng = CreateRandom(request.randomSeed);
            var clonedEffects = CloneEffects(selectedRoute.effects);
            ConversationGameStateEffectApplier.Apply(workingState, clonedEffects);
            UpdateRoutingHistory(workingState, selectedRoute);

            return new ConversationRouteResult
            {
                matched = true,
                conversationId = selectedRoute.id,
                priority = selectedRoute.priority,
                response = ConversationVariableResolver.Resolve(SelectResponse(selectedRoute.responses, rng), workingState),
                events = new List<string>(selectedRoute.events),
                effects = clonedEffects,
                nextStateJson = workingState.SaveToJson(true),
                candidates = evaluations.ConvertAll(evaluation => evaluation.Debug)
            };
        }

        public ConversationRouteResult RouteFromJson(
            string parseResultJson,
            string gameStateJson,
            IEnumerable<string> tags = null,
            int? randomSeed = null)
        {
            var request = new ConversationRouteRequest
            {
                parseResult = ParseConversationParseResultJson(parseResultJson),
                gameState = ConversationGameState.FromJson(gameStateJson),
                randomSeed = randomSeed
            };

            if (tags != null)
            {
                request.tags.AddRange(tags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
            }

            return Route(request);
        }

        public string RouteToJson(
            string parseResultJson,
            string gameStateJson,
            IEnumerable<string> tags = null,
            int? randomSeed = null,
            bool prettyPrint = true)
        {
            return SerializeResult(RouteFromJson(parseResultJson, gameStateJson, tags, randomSeed), prettyPrint);
        }

        public static List<ConversationRouteDefinition> ParseRoutesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConversationRouteDefinition>();
            }

            var parsed = ConversationMiniJson.Deserialize(json);
            var routeItems = ResolveRouteItems(parsed);
            var result = new List<ConversationRouteDefinition>(routeItems.Count);

            for (var i = 0; i < routeItems.Count; i++)
            {
                if (routeItems[i] is not Dictionary<string, object> routeObject)
                {
                    throw new FormatException("Each route entry must be an object.");
                }

                result.Add(ParseRoute(routeObject));
            }

            return result;
        }

        public static string SerializeResult(ConversationRouteResult result, bool prettyPrint = true)
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["matched"] = result != null && result.matched,
                ["conversation_id"] = result?.conversationId ?? string.Empty,
                ["priority"] = result?.priority ?? 0,
                ["response"] = result?.response ?? string.Empty,
                ["events"] = ToObjectList(result?.events),
                ["effects"] = SerializeEffects(result?.effects)
            };

            var nextState = string.IsNullOrWhiteSpace(result?.nextStateJson)
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : ConversationMiniJson.Deserialize(result.nextStateJson);
            root["next_state"] = nextState;

            return ConversationMiniJson.Serialize(root, prettyPrint);
        }

        public static ConversationParseResult ParseConversationParseResultJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ConversationParseResult();
            }

            var parsed = ConversationMiniJson.Deserialize(json);
            if (parsed is not Dictionary<string, object> root)
            {
                throw new FormatException("Parse result JSON root must be an object.");
            }

            var result = new ConversationParseResult
            {
                input = ReadOptionalString(root, "input")
            };

            if (root.TryGetValue("intents", out var intentsObject) && intentsObject is List<object> intentItems)
            {
                for (var i = 0; i < intentItems.Count; i++)
                {
                    if (intentItems[i] is not Dictionary<string, object> intentObject)
                    {
                        throw new FormatException("Each intent entry must be an object.");
                    }

                    result.intents.Add(new IntentMatch
                    {
                        intent = ReadRequiredString(intentObject, "intent"),
                        priority = ReadOptionalInt(intentObject, "priority", 0)
                    });
                }
            }

            if (root.TryGetValue("categories", out var categoriesObject) && categoriesObject is List<object> categoryItems)
            {
                for (var i = 0; i < categoryItems.Count; i++)
                {
                    if (categoryItems[i] is not Dictionary<string, object> categoryObject)
                    {
                        throw new FormatException("Each category entry must be an object.");
                    }

                    result.categories.Add(new CategoryMatch
                    {
                        type = ReadRequiredString(categoryObject, "type"),
                        value = ReadRequiredString(categoryObject, "value")
                    });
                }
            }

            return result;
        }

        private static List<object> ResolveRouteItems(object parsed)
        {
            switch (parsed)
            {
                case List<object> routeList:
                    return routeList;
                case Dictionary<string, object> root when root.TryGetValue("routes", out var routeItems) && routeItems is List<object> routes:
                    return routes;
                default:
                    throw new FormatException("Routes JSON must be an array or an object with a 'routes' array.");
            }
        }

        private static ConversationRouteDefinition ParseRoute(Dictionary<string, object> routeObject)
        {
            var route = new ConversationRouteDefinition
            {
                id = ReadRequiredString(routeObject, "id"),
                priority = ReadOptionalInt(routeObject, "priority", 0),
                playOnce = ReadOptionalBool(routeObject, "play_once", false),
                randomWeight = Math.Max(1, ReadOptionalInt(routeObject, "random_weight", 1)),
                allowImmediateRepeat = ReadOptionalBool(routeObject, "allow_immediate_repeat", false),
                responses = ReadStringList(routeObject, "responses"),
                events = ReadStringList(routeObject, "events"),
                effects = ReadEffects(routeObject)
            };

            if (routeObject.TryGetValue("conditions", out var conditionsObject) && conditionsObject is Dictionary<string, object> conditions)
            {
                ParseConditions(route, conditions);
            }

            route.intents.AddRange(ReadStringList(routeObject, "intents"));
            route.requiredTags.AddRange(ReadStringList(routeObject, "tags"));
            route.excludedTags.AddRange(ReadStringList(routeObject, "excluded_tags"));
            route.stateConditions.AddRange(ReadStringList(routeObject, "state_conditions"));

            return NormalizeRoute(route);
        }

        private static void ParseConditions(ConversationRouteDefinition route, Dictionary<string, object> conditions)
        {
            foreach (var pair in conditions)
            {
                switch (pair.Key)
                {
                    case "intent":
                        route.intents.Add(ReadStringValue(pair.Value));
                        break;
                    case "intents":
                        route.intents.AddRange(ReadStringListValue(pair.Value));
                        break;
                    case "tags":
                        route.requiredTags.AddRange(ReadStringListValue(pair.Value));
                        break;
                    case "excluded_tags":
                    case "exclude_tags":
                        route.excludedTags.AddRange(ReadStringListValue(pair.Value));
                        break;
                    case "state_conditions":
                        route.stateConditions.AddRange(ReadStringListValue(pair.Value));
                        break;
                    default:
                        AddStateConditionFromValue(route.stateConditions, pair.Key, pair.Value);
                        break;
                }
            }
        }

        private static void AddStateConditionFromValue(List<string> targetConditions, string key, object value)
        {
            switch (value)
            {
                case List<object> listValue:
                    for (var i = 0; i < listValue.Count; i++)
                    {
                        targetConditions.Add($"{key} contains {FormatToken(listValue[i])}");
                    }

                    break;
                default:
                    targetConditions.Add($"{key} == {FormatToken(value)}");
                    break;
            }
        }

        private static string FormatToken(object value)
        {
            switch (value)
            {
                case null:
                    return "\"\"";
                case bool boolValue:
                    return boolValue ? "true" : "false";
                case int intValue:
                    return intValue.ToString(CultureInfo.InvariantCulture);
                case long longValue:
                    return longValue.ToString(CultureInfo.InvariantCulture);
                case double doubleValue:
                    if (Math.Abs(doubleValue % 1d) <= double.Epsilon)
                    {
                        return ((long)doubleValue).ToString(CultureInfo.InvariantCulture);
                    }

                    throw new FormatException("Floating-point route condition values are not supported.");
                default:
                    return QuoteString(value.ToString());
            }
        }

        private static string QuoteString(string value)
        {
            var safeValue = value ?? string.Empty;
            return "\"" + safeValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static ConversationRouteDefinition NormalizeRoute(ConversationRouteDefinition route)
        {
            route.intents = route.intents
                .Where(intent => !string.IsNullOrWhiteSpace(intent))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            route.requiredTags = route.requiredTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            route.excludedTags = route.excludedTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            route.stateConditions = route.stateConditions
                .Where(condition => !string.IsNullOrWhiteSpace(condition))
                .ToList();

            return route;
        }

        private List<RouteEvaluation> EvaluateRoutes(ConversationRouteRequest request, ConversationGameState currentState)
        {
            var result = new List<ConversationRouteDefinition>();
            var activeTags = request.tags != null
                ? new HashSet<string>(request.tags.Where(tag => !string.IsNullOrWhiteSpace(tag)), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            currentState.TryGetStringList(PlayedOnceIdsStateKey, out var playedOnceIds);
            var playedOnceSet = new HashSet<string>(playedOnceIds ?? new List<string>(), StringComparer.Ordinal);
            var availableIntents = GetIntentSet(request.parseResult);
            var evaluations = new List<RouteEvaluation>(routes.Count);

            for (var i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                var evaluation = new RouteEvaluation(route)
                {
                    Debug =
                    {
                        conversationId = route.id,
                        priority = route.priority
                    }
                };

                var intentPassed = AddIntentConditions(evaluation.Debug, route, availableIntents);
                var tagsPassed = AddTagConditions(evaluation.Debug, route, activeTags);
                var statesPassed = AddStateConditions(evaluation.Debug, route, currentState);
                var playOncePassed = AddPlayOnceCondition(evaluation.Debug, route, playedOnceSet);

                evaluation.PassedAllConditions = intentPassed && tagsPassed && statesPassed && playOncePassed;
                evaluation.Debug.passedAllConditions = evaluation.PassedAllConditions;
                if (!evaluation.PassedAllConditions)
                {
                    evaluation.Debug.rejectionReason = GetFirstFailedReason(evaluation.Debug.conditions);
                }

                evaluations.Add(evaluation);
            }

            return evaluations;
        }

        private static HashSet<string> GetIntentSet(ConversationParseResult parseResult)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (parseResult?.intents == null)
            {
                return result;
            }

            for (var i = 0; i < parseResult.intents.Count; i++)
            {
                var intent = parseResult.intents[i];
                if (!string.IsNullOrWhiteSpace(intent?.intent))
                {
                    result.Add(intent.intent);
                }
            }

            return result;
        }

        private static bool AddIntentConditions(
            ConversationRouteCandidateDebug debug,
            ConversationRouteDefinition route,
            HashSet<string> availableIntents)
        {
            if (route.intents.Count == 0)
            {
                return true;
            }

            var matched = false;
            for (var i = 0; i < route.intents.Count; i++)
            {
                var intent = route.intents[i];
                var passed = availableIntents.Contains(intent);
                matched |= passed;
                debug.conditions.Add(new ConversationRouteConditionDebug
                {
                    label = $"intent={intent}",
                    passed = passed,
                    reason = passed ? "OK" : "Intent not extracted."
                });
            }

            return matched;
        }

        private static bool AddTagConditions(
            ConversationRouteCandidateDebug debug,
            ConversationRouteDefinition route,
            HashSet<string> activeTags)
        {
            var passedAll = true;
            for (var i = 0; i < route.requiredTags.Count; i++)
            {
                var tag = route.requiredTags[i];
                var passed = activeTags.Contains(tag);
                passedAll &= passed;
                debug.conditions.Add(new ConversationRouteConditionDebug
                {
                    label = $"tag={tag}",
                    passed = passed,
                    reason = passed ? "OK" : "Required tag is missing."
                });
            }

            for (var i = 0; i < route.excludedTags.Count; i++)
            {
                var tag = route.excludedTags[i];
                var passed = !activeTags.Contains(tag);
                passedAll &= passed;
                debug.conditions.Add(new ConversationRouteConditionDebug
                {
                    label = $"not tag={tag}",
                    passed = passed,
                    reason = passed ? "OK" : "Excluded tag is active."
                });
            }

            return passedAll;
        }

        private static bool AddStateConditions(
            ConversationRouteCandidateDebug debug,
            ConversationRouteDefinition route,
            ConversationGameState currentState)
        {
            var passedAll = true;
            for (var i = 0; i < route.stateConditions.Count; i++)
            {
                var condition = route.stateConditions[i];
                bool passed;
                string reason;

                try
                {
                    passed = ConversationGameStateConditionEvaluator.Evaluate(currentState, condition);
                    reason = passed ? "OK" : "State condition failed.";
                }
                catch (FormatException exception)
                {
                    passed = false;
                    reason = exception.Message;
                }

                passedAll &= passed;
                debug.conditions.Add(new ConversationRouteConditionDebug
                {
                    label = condition,
                    passed = passed,
                    reason = reason
                });
            }

            return passedAll;
        }

        private static bool AddPlayOnceCondition(
            ConversationRouteCandidateDebug debug,
            ConversationRouteDefinition route,
            HashSet<string> playedOnceSet)
        {
            if (!route.playOnce)
            {
                return true;
            }

            var passed = !playedOnceSet.Contains(route.id);
            debug.conditions.Add(new ConversationRouteConditionDebug
            {
                label = "play_once",
                passed = passed,
                reason = passed ? "OK" : "Already played once."
            });
            return passed;
        }

        private static RouteEvaluation SelectRoute(
            List<RouteEvaluation> evaluations,
            ConversationGameState currentState,
            int? randomSeed)
        {
            if (evaluations == null || evaluations.Count == 0)
            {
                return null;
            }

            var matchedRoutes = evaluations.Where(evaluation => evaluation.PassedAllConditions).ToList();
            if (matchedRoutes.Count == 0)
            {
                return null;
            }

            var topPriority = matchedRoutes.Max(evaluation => evaluation.Route.priority);
            var topCandidates = matchedRoutes.Where(evaluation => evaluation.Route.priority == topPriority).ToList();
            currentState.TryGetString(LastRouteIdStateKey, out var lastRouteId);

            var repeatFiltered = topCandidates
                .Where(evaluation => evaluation.Route.allowImmediateRepeat || !string.Equals(evaluation.Route.id, lastRouteId, StringComparison.Ordinal))
                .ToList();

            if (repeatFiltered.Count > 0)
            {
                for (var i = 0; i < topCandidates.Count; i++)
                {
                    if (!repeatFiltered.Contains(topCandidates[i]))
                    {
                        topCandidates[i].Debug.rejectionReason = "Immediate repeat avoided.";
                    }
                }

                topCandidates = repeatFiltered;
            }

            for (var i = 0; i < matchedRoutes.Count; i++)
            {
                if (matchedRoutes[i].Route.priority < topPriority)
                {
                    matchedRoutes[i].Debug.rejectionReason = $"Lower priority than selected candidate ({topPriority}).";
                }
            }

            var rng = CreateRandom(randomSeed);
            var selected = SelectWeighted(topCandidates, rng);
            if (selected != null)
            {
                selected.Debug.selected = true;
                for (var i = 0; i < topCandidates.Count; i++)
                {
                    if (!ReferenceEquals(topCandidates[i], selected) && string.IsNullOrWhiteSpace(topCandidates[i].Debug.rejectionReason))
                    {
                        topCandidates[i].Debug.rejectionReason = "Lost weighted random selection.";
                    }
                }
            }

            return selected;
        }

        private static RouteEvaluation SelectWeighted(List<RouteEvaluation> candidates, Random rng)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var totalWeight = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                totalWeight += Math.Max(1, candidates[i].Route.randomWeight);
            }

            var roll = rng.Next(totalWeight);
            for (var i = 0; i < candidates.Count; i++)
            {
                roll -= Math.Max(1, candidates[i].Route.randomWeight);
                if (roll < 0)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static string SelectResponse(List<string> responses, Random rng)
        {
            if (responses == null || responses.Count == 0)
            {
                return string.Empty;
            }

            if (responses.Count == 1)
            {
                return responses[0] ?? string.Empty;
            }

            var index = rng.Next(responses.Count);
            return responses[index] ?? string.Empty;
        }

        private static void UpdateRoutingHistory(ConversationGameState state, ConversationRouteDefinition selectedRoute)
        {
            state.SetString(LastRouteIdStateKey, selectedRoute.id);
            if (selectedRoute.playOnce)
            {
                state.AddToStringList(PlayedOnceIdsStateKey, selectedRoute.id);
            }
        }

        private static ConversationGameState CloneState(ConversationGameState source)
        {
            if (source == null)
            {
                return new ConversationGameState();
            }

            return ConversationGameState.FromJson(source.SaveToJson(false));
        }

        private static List<ConversationGameStateEffect> CloneEffects(List<ConversationGameStateEffect> effects)
        {
            var result = new List<ConversationGameStateEffect>();
            if (effects == null)
            {
                return result;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                result.Add(new ConversationGameStateEffect
                {
                    type = effect.type,
                    key = effect.key,
                    value = effect.value?.Clone()
                });
            }

            return result;
        }

        private static Random CreateRandom(int? randomSeed)
        {
            return randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        private static string GetFirstFailedReason(List<ConversationRouteConditionDebug> conditions)
        {
            for (var i = 0; i < conditions.Count; i++)
            {
                if (!conditions[i].passed)
                {
                    return $"{conditions[i].label}: {conditions[i].reason}";
                }
            }

            return "Route did not match.";
        }

        private static List<object> SerializeEffects(List<ConversationGameStateEffect> effects)
        {
            var result = new List<object>();
            if (effects == null)
            {
                return result;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                var effectObject = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = effect.type ?? string.Empty,
                    ["key"] = effect.key ?? string.Empty,
                    ["value"] = effect.value?.ToPlainObject()
                };
                result.Add(effectObject);
            }

            return result;
        }

        private static List<object> ToObjectList(List<string> values)
        {
            var result = new List<object>();
            if (values == null)
            {
                return result;
            }

            for (var i = 0; i < values.Count; i++)
            {
                result.Add(values[i] ?? string.Empty);
            }

            return result;
        }

        private static List<ConversationGameStateEffect> ReadEffects(Dictionary<string, object> routeObject)
        {
            if (!routeObject.TryGetValue("effects", out var effectListObject) || effectListObject is not List<object> effectItems)
            {
                return new List<ConversationGameStateEffect>();
            }

            var wrapper = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["effects"] = effectItems
            };
            return ConversationGameStateEffectApplier.ParseEffectSetJson(ConversationMiniJson.Serialize(wrapper, false)).effects;
        }

        private static string ReadRequiredString(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
            {
                throw new FormatException($"Required string field '{key}' is missing.");
            }

            return stringValue;
        }

        private static string ReadOptionalString(Dictionary<string, object> source, string key)
        {
            return source.TryGetValue(key, out var value) && value is string stringValue
                ? stringValue
                : string.Empty;
        }

        private static int ReadOptionalInt(Dictionary<string, object> source, string key, int defaultValue)
        {
            if (!source.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            return value switch
            {
                int intValue => intValue,
                long longValue => checked((int)longValue),
                double doubleValue => checked((int)doubleValue),
                _ => defaultValue
            };
        }

        private static bool ReadOptionalBool(Dictionary<string, object> source, string key, bool defaultValue)
        {
            if (!source.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            return value is bool boolValue ? boolValue : defaultValue;
        }

        private static List<string> ReadStringList(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value))
            {
                return new List<string>();
            }

            return ReadStringListValue(value);
        }

        private static List<string> ReadStringListValue(object value)
        {
            switch (value)
            {
                case null:
                    return new List<string>();
                case string stringValue:
                    return new List<string> { stringValue };
                case List<object> listValue:
                {
                    var result = new List<string>(listValue.Count);
                    for (var i = 0; i < listValue.Count; i++)
                    {
                        result.Add(ReadStringValue(listValue[i]));
                    }

                    return result;
                }
                default:
                    return new List<string> { ReadStringValue(value) };
            }
        }

        private static string ReadStringValue(object value)
        {
            return value?.ToString() ?? string.Empty;
        }

        private sealed class RouteEvaluation
        {
            public RouteEvaluation(ConversationRouteDefinition route)
            {
                Route = route;
                Debug = new ConversationRouteCandidateDebug();
            }

            public ConversationRouteDefinition Route { get; }
            public ConversationRouteCandidateDebug Debug { get; }
            public bool PassedAllConditions { get; set; }
        }
    }
}
