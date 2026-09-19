using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Backgammon.Conversation
{
    public interface IConversationEffectPresetResolver
    {
        bool TryResolve(string presetId, out IReadOnlyList<ConversationGameStateEffect> effects);
    }

    [Serializable]
    public sealed class ConversationDataValidationError
    {
        public string sourceName;
        public int lineNumber;
        public string routeId;
        public string message;

        public override string ToString()
        {
            var source = string.IsNullOrWhiteSpace(sourceName) ? "<unknown>" : sourceName;
            var route = string.IsNullOrWhiteSpace(routeId) ? string.Empty : $" [id={routeId}]";
            return $"{source}:{lineNumber}{route} {message}";
        }
    }

    public sealed class ConversationRouteFieldSchema
    {
        private readonly Dictionary<string, HashSet<string>> allowedValuesByField = new(StringComparer.OrdinalIgnoreCase);

        public ConversationRouteFieldSchema RegisterEnum<T>(params string[] fieldNames) where T : struct, Enum
        {
            var values = Enum.GetNames(typeof(T));
            if (fieldNames != null)
            {
                for (var i = 0; i < fieldNames.Length; i++)
                {
                    RegisterAllowedValues(fieldNames[i], values);
                }
            }

            return this;
        }

        public ConversationRouteFieldSchema RegisterAllowedValues(string fieldName, IEnumerable<string> values)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name must not be empty.", nameof(fieldName));
            }

            if (!allowedValuesByField.TryGetValue(fieldName, out var allowedValues))
            {
                allowedValues = new HashSet<string>(StringComparer.Ordinal);
                allowedValuesByField[fieldName] = allowedValues;
            }

            if (values != null)
            {
                foreach (var value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        allowedValues.Add(value);
                    }
                }
            }

            return this;
        }

        public bool IsValid(string fieldName, string value)
        {
            if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return !allowedValuesByField.TryGetValue(fieldName, out var allowedValues)
                || allowedValues.Contains(value);
        }

        public static ConversationRouteFieldSchema CreateDefault()
        {
            return new ConversationRouteFieldSchema()
                .RegisterEnum<FoodCategory>("food_category", "FoodCategory", "KnownFoodCategories", "FavoriteFoodCategory")
                .RegisterAllowedValues("phase", new[] { "Morning", "Lunch", "Evening", "Night", "Emergency" })
                .RegisterAllowedValues("Phase", new[] { "Morning", "Lunch", "Evening", "Night", "Emergency" });
        }
    }

    [Serializable]
    public sealed class ConversationDataLoadOptions
    {
        public string locale = "ja";
        public bool logErrorsToConsole = true;
        public char csvListSeparator = '|';
        public ConversationRouteFieldSchema schema = ConversationRouteFieldSchema.CreateDefault();
        public IConversationEffectPresetResolver effectPresetResolver;
    }

    [Serializable]
    public sealed class ConversationDataLoadResult
    {
        public ConversationRouteCatalog catalog = new(null);
        public List<ConversationDataValidationError> errors = new();

        public bool Succeeded => errors.Count == 0;
    }

    public static class ConversationDataLoader
    {
        private const string TimelineSequenceActionPrefix = "timeline_sequence:";
        private static readonly HashSet<string> CsvReservedColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "priority",
            "Priority",
            "intent",
            "intents",
            "tags",
            "excluded_tags",
            "exclude_tags",
            "state_conditions",
            "conditions",
            "responses",
            "response",
            "effects",
            "events",
            "play_once",
            "random_weight",
            "allow_immediate_repeat",
            "input",
            "regex_jp",
            "regex_zh",
            "regex_en",
            "output_ja",
            "output_zh",
            "output_en",
            "alt",
            "regex_id",
            "pattern",
            "order",
            "response_type",
            "action_id",
            "condition",
            "SpeechControl",
            "emotion_change_type",
            "emotion_change_value",
            "WaitTime",
            "HideUI",
            "Animation",
            "call_only",
            "choice_yes_pattern",
            "choice_no_pattern",
            "sequence_progress_hold",
            "random_repeat_limit"
        };

        public static ConversationDataLoadResult LoadFromJsonFile(string path, ConversationDataLoadOptions options = null)
        {
            return LoadFromTextFile(path, true, options);
        }

        public static ConversationDataLoadResult LoadFromCsvFile(string path, ConversationDataLoadOptions options = null)
        {
            return LoadFromTextFile(path, false, options);
        }

        public static ConversationDataLoadResult LoadFromJsonTextAsset(TextAsset asset, ConversationDataLoadOptions options = null)
        {
            if (asset == null)
            {
                return BuildFileErrorResult("<null>", 1, "JSON TextAsset is null.", options);
            }

            return LoadFromJsonText(asset.text, asset.name, options);
        }

        public static ConversationDataLoadResult LoadFromCsvTextAsset(TextAsset asset, ConversationDataLoadOptions options = null)
        {
            if (asset == null)
            {
                return BuildFileErrorResult("<null>", 1, "CSV TextAsset is null.", options);
            }

            return LoadFromCsvText(asset.text, asset.name, options);
        }

        public static ConversationDataLoadResult LoadFromJsonText(string json, string sourceName = "conversation.json", ConversationDataLoadOptions options = null)
        {
            options ??= new ConversationDataLoadOptions();
            var errors = new List<ConversationDataValidationError>();
            var routes = new List<ConversationRouteDefinition>();

            try
            {
                var parsed = ConversationMiniJson.Deserialize(json ?? string.Empty);
                var routeItems = ResolveRouteItems(parsed);
                var lineNumbers = LocateJsonRouteLineNumbers(json, routeItems.Count);
                var seenIds = new HashSet<string>(StringComparer.Ordinal);

                for (var i = 0; i < routeItems.Count; i++)
                {
                    var lineNumber = i < lineNumbers.Count ? lineNumbers[i] : 1;
                    if (routeItems[i] is not Dictionary<string, object> routeObject)
                    {
                        errors.Add(CreateError(sourceName, lineNumber, null, "Each route entry must be an object."));
                        continue;
                    }

                    TryAddRoute(ParseRoute(routeObject, options), sourceName, lineNumber, seenIds, options, errors, routes);
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                errors.Add(CreateError(sourceName, InferJsonLineNumber(json, exception.Message), null, exception.Message));
            }

            return FinalizeResult(routes, errors, options);
        }

        public static ConversationDataLoadResult LoadFromCsvText(string csv, string sourceName = "conversation.csv", ConversationDataLoadOptions options = null)
        {
            options ??= new ConversationDataLoadOptions();
            var errors = new List<ConversationDataValidationError>();
            var routes = new List<ConversationRouteDefinition>();

            try
            {
                var records = ConversationCsvParser.Parse(csv ?? string.Empty);
                if (records.Count == 0)
                {
                    return FinalizeResult(routes, errors, options);
                }

                var headers = NormalizeCsvHeaders(records[0].fields);
                if (LooksLikeDialoguePreviewFormat(headers))
                {
                    return LoadDialoguePreviewCsv(records, sourceName, options);
                }

                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                for (var rowIndex = 1; rowIndex < records.Count; rowIndex++)
                {
                    var record = records[rowIndex];
                    if (IsEmptyRecord(record.fields))
                    {
                        continue;
                    }

                    try
                    {
                        var row = ToRowDictionary(headers, record.fields);
                        TryAddRoute(ParseCsvRoute(row, options), sourceName, record.lineNumber, seenIds, options, errors, routes);
                    }
                    catch (Exception exception) when (exception is FormatException or OverflowException)
                    {
                        errors.Add(CreateError(sourceName, record.lineNumber, null, exception.Message));
                    }
                }
            }
            catch (FormatException exception)
            {
                errors.Add(CreateError(sourceName, 1, null, exception.Message));
            }

            return FinalizeResult(routes, errors, options);
        }

        private static ConversationDataLoadResult LoadDialoguePreviewCsv(
            List<ConversationCsvRecord> records,
            string sourceName,
            ConversationDataLoadOptions options)
        {
            var errors = new List<ConversationDataValidationError>();
            var routes = new List<ConversationRouteDefinition>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var headers = NormalizeCsvHeaders(records[0].fields);

            for (var rowIndex = 1; rowIndex < records.Count; rowIndex++)
            {
                var record = records[rowIndex];
                if (IsEmptyRecord(record.fields))
                {
                    continue;
                }

                try
                {
                    var row = ToRowDictionary(headers, record.fields);
                    var parseResult = ParseDialoguePreviewRoute(row, record.lineNumber, options);
                    TryAddRoute(parseResult, sourceName, record.lineNumber, seenIds, options, errors, routes);
                }
                catch (Exception exception) when (exception is FormatException or OverflowException)
                {
                    errors.Add(CreateError(sourceName, record.lineNumber, null, exception.Message));
                }
            }

            return FinalizeResult(routes, errors, options);
        }

        public static ConversationDataLoadResult Merge(
            IEnumerable<ConversationDataLoadResult> results,
            string mergedSourceName = "merged")
        {
            var routes = new List<ConversationRouteDefinition>();
            var errors = new List<ConversationDataValidationError>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            if (results != null)
            {
                foreach (var result in results)
                {
                    if (result == null)
                    {
                        continue;
                    }

                    errors.AddRange(result.errors);
                    foreach (var route in result.catalog.Routes)
                    {
                        if (!seenIds.Add(route.id))
                        {
                            errors.Add(CreateError(mergedSourceName, 1, route.id, $"Duplicate route id '{route.id}' detected while merging catalogs."));
                            continue;
                        }

                        routes.Add(route);
                    }
                }
            }

            return new ConversationDataLoadResult
            {
                catalog = new ConversationRouteCatalog(routes),
                errors = errors
            };
        }

        private static ConversationDataLoadResult LoadFromTextFile(string path, bool isJson, ConversationDataLoadOptions options)
        {
            options ??= new ConversationDataLoadOptions();
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                return isJson
                    ? LoadFromJsonText(text, path, options)
                    : LoadFromCsvText(text, path, options);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return BuildFileErrorResult(path, 1, exception.Message, options);
            }
        }

        private static ConversationDataLoadResult BuildFileErrorResult(
            string sourceName,
            int lineNumber,
            string message,
            ConversationDataLoadOptions options)
        {
            var result = new ConversationDataLoadResult
            {
                errors = new List<ConversationDataValidationError>
                {
                    CreateError(sourceName, lineNumber, null, message)
                }
            };

            if (options?.logErrorsToConsole ?? true)
            {
                LogErrors(result.errors);
            }

            return result;
        }

        private static ConversationDataLoadResult FinalizeResult(
            List<ConversationRouteDefinition> routes,
            List<ConversationDataValidationError> errors,
            ConversationDataLoadOptions options)
        {
            var result = new ConversationDataLoadResult
            {
                catalog = new ConversationRouteCatalog(routes),
                errors = errors
            };

            if (options?.logErrorsToConsole ?? true)
            {
                LogErrors(errors);
            }

            return result;
        }

        private static void LogErrors(List<ConversationDataValidationError> errors)
        {
            if (errors == null)
            {
                return;
            }

            for (var i = 0; i < errors.Count; i++)
            {
                Debug.LogError($"[ConversationDataLoader] {errors[i]}");
            }
        }

        private static ConversationDataValidationError CreateError(string sourceName, int lineNumber, string routeId, string message)
        {
            return new ConversationDataValidationError
            {
                sourceName = sourceName ?? string.Empty,
                lineNumber = Math.Max(1, lineNumber),
                routeId = routeId ?? string.Empty,
                message = message ?? "Unknown error."
            };
        }

        private static void TryAddRoute(
            RouteParseResult parseResult,
            string sourceName,
            int lineNumber,
            HashSet<string> seenIds,
            ConversationDataLoadOptions options,
            List<ConversationDataValidationError> errors,
            List<ConversationRouteDefinition> routes)
        {
            if (parseResult.errors.Count > 0)
            {
                for (var i = 0; i < parseResult.errors.Count; i++)
                {
                    errors.Add(CreateError(sourceName, lineNumber, parseResult.route?.id, parseResult.errors[i]));
                }
                return;
            }

            if (parseResult.route == null || string.IsNullOrWhiteSpace(parseResult.route.id))
            {
                errors.Add(CreateError(sourceName, lineNumber, null, "Required field 'id' is missing."));
                return;
            }

            if (!seenIds.Add(parseResult.route.id))
            {
                errors.Add(CreateError(sourceName, lineNumber, parseResult.route.id, $"Duplicate route id '{parseResult.route.id}' detected."));
                return;
            }

            ValidateRoute(parseResult.route, sourceName, lineNumber, options, errors);
            if (!errors.Any(error => error.lineNumber == Math.Max(1, lineNumber) && string.Equals(error.routeId, parseResult.route.id, StringComparison.Ordinal)))
            {
                routes.Add(parseResult.route);
            }
        }

        private static void ValidateRoute(
            ConversationRouteDefinition route,
            string sourceName,
            int lineNumber,
            ConversationDataLoadOptions options,
            List<ConversationDataValidationError> errors)
        {
            if (route.responses.Count == 0 && route.events.Count == 0 && route.effects.Count == 0)
            {
                errors.Add(CreateError(sourceName, lineNumber, route.id, "Route must define at least one response, event, or effect."));
            }

            ValidateFieldValues(route.id, sourceName, lineNumber, "tags", route.requiredTags, options, errors);
            ValidateFieldValues(route.id, sourceName, lineNumber, "excluded_tags", route.excludedTags, options, errors);
            ValidateFieldValues(route.id, sourceName, lineNumber, "intents", route.intents, options, errors);
            ValidateStateConditions(route.id, sourceName, lineNumber, route.stateConditions, options, errors);
        }

        private static void ValidateStateConditions(
            string routeId,
            string sourceName,
            int lineNumber,
            List<string> stateConditions,
            ConversationDataLoadOptions options,
            List<ConversationDataValidationError> errors)
        {
            if (stateConditions == null || stateConditions.Count == 0)
            {
                return;
            }

            for (var i = 0; i < stateConditions.Count; i++)
            {
                if (TryExtractConditionFieldAndValue(stateConditions[i], out var field, out var value)
                    && !options.schema.IsValid(field, value))
                {
                    errors.Add(CreateError(sourceName, lineNumber, routeId, $"Invalid enum value '{value}' for field '{field}'."));
                }
            }
        }

        private static bool TryExtractConditionFieldAndValue(string condition, out string field, out string value)
        {
            field = null;
            value = null;
            if (string.IsNullOrWhiteSpace(condition))
            {
                return false;
            }

            var operators = new[] { " not contains ", " contains ", " == ", " != ", " >= ", " <= ", " > ", " < " };
            for (var i = 0; i < operators.Length; i++)
            {
                var op = operators[i];
                var opIndex = condition.IndexOf(op, StringComparison.Ordinal);
                if (opIndex < 0)
                {
                    continue;
                }

                field = condition.Substring(0, opIndex).Trim();
                value = condition.Substring(opIndex + op.Length).Trim().Trim('"');
                return !string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static void ValidateFieldValues(
            string routeId,
            string sourceName,
            int lineNumber,
            string fieldName,
            List<string> values,
            ConversationDataLoadOptions options,
            List<ConversationDataValidationError> errors)
        {
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (!options.schema.IsValid(fieldName, values[i]))
                {
                    errors.Add(CreateError(sourceName, lineNumber, routeId, $"Invalid enum value '{values[i]}' for field '{fieldName}'."));
                }
            }
        }

        private static List<object> ResolveRouteItems(object parsed)
        {
            switch (parsed)
            {
                case List<object> routeList:
                    return routeList;
                case Dictionary<string, object> root when root.TryGetValue("routes", out var routesObject) && routesObject is List<object> routes:
                    return routes;
                default:
                    throw new FormatException("Routes JSON must be an array or an object with a 'routes' array.");
            }
        }

        private static RouteParseResult ParseRoute(Dictionary<string, object> routeObject, ConversationDataLoadOptions options)
        {
            var result = new RouteParseResult
            {
                route = new ConversationRouteDefinition
                {
                    id = ReadString(routeObject, "id"),
                    priority = ReadOptionalInt(routeObject, "priority", 0),
                    playOnce = ReadOptionalBool(routeObject, "play_once", false),
                    randomWeight = Math.Max(1, ReadOptionalInt(routeObject, "random_weight", 1)),
                    allowImmediateRepeat = ReadOptionalBool(routeObject, "allow_immediate_repeat", false),
                    responses = ReadLocalizedResponses(routeObject, options.locale),
                    events = ReadStringList(routeObject, "events"),
                    effects = ReadEffects(routeObject, options)
                }
            };

            if (string.IsNullOrWhiteSpace(result.route.id))
            {
                result.errors.Add("Required field 'id' is missing.");
            }

            if (routeObject.TryGetValue("conditions", out var conditionsObject) && conditionsObject is Dictionary<string, object> conditions)
            {
                ParseConditions(result.route, conditions);
            }

            result.route.intents.AddRange(ReadStringList(routeObject, "intents"));
            result.route.intents.AddRange(ReadStringList(routeObject, "intent"));
            result.route.requiredTags.AddRange(ReadStringList(routeObject, "tags"));
            result.route.excludedTags.AddRange(ReadStringList(routeObject, "excluded_tags"));
            result.route.excludedTags.AddRange(ReadStringList(routeObject, "exclude_tags"));
            result.route.stateConditions.AddRange(ReadStringList(routeObject, "state_conditions"));
            NormalizeRoute(result.route);
            return result;
        }

        private static RouteParseResult ParseCsvRoute(Dictionary<string, string> row, ConversationDataLoadOptions options)
        {
            var result = new RouteParseResult();
            var route = new ConversationRouteDefinition
            {
                id = ReadCsvString(row, "id"),
                priority = ReadCsvInt(row, "priority", 0),
                playOnce = ReadCsvBool(row, "play_once", false),
                randomWeight = Math.Max(1, ReadCsvInt(row, "random_weight", 1)),
                allowImmediateRepeat = ReadCsvBool(row, "allow_immediate_repeat", false),
                responses = ReadCsvResponses(row, options),
                events = ReadCsvList(row, "events", options),
                effects = ReadCsvEffects(row, options, result.errors)
            };

            route.intents.AddRange(ReadCsvList(row, "intents", options));
            route.intents.AddRange(ReadCsvList(row, "intent", options));
            route.requiredTags.AddRange(ReadCsvList(row, "tags", options));
            route.excludedTags.AddRange(ReadCsvList(row, "excluded_tags", options));
            route.excludedTags.AddRange(ReadCsvList(row, "exclude_tags", options));
            route.stateConditions.AddRange(ReadCsvConditionList(row, options));
            AddCsvConditionColumns(route.stateConditions, row, options);
            NormalizeRoute(route);

            result.route = route;

            if (string.IsNullOrWhiteSpace(route.id))
            {
                result.errors.Add("Required field 'id' is missing.");
            }

            return result;
        }

        private static RouteParseResult ParseDialoguePreviewRoute(
            Dictionary<string, string> row,
            int lineNumber,
            ConversationDataLoadOptions options)
        {
            var result = new RouteParseResult();
            var groupId = DecodePreviewToken(ReadCsvString(row, "id"));
            var regexId = DecodePreviewToken(ReadCsvString(row, "regex_id"));
            var pattern = ReadCsvInt(row, "pattern", 0);
            var order = ReadCsvInt(row, "order", 0);
            var priority = ReadCsvInt(row, "Priority", ReadCsvInt(row, "priority", 0));
            var responseType = DecodePreviewToken(ReadCsvString(row, "response_type"));
            var animation = DecodePreviewToken(ReadCsvString(row, "Animation"));
            var actionId = ResolveDialoguePreviewActionId(
                responseType,
                DecodePreviewToken(ReadCsvString(row, "action_id")),
                animation);
            var localizedResponse = DecodePreviewToken(ReadDialoguePreviewResponse(row, options.locale));

            var route = new ConversationRouteDefinition
            {
                id = BuildDialoguePreviewRouteId(groupId, regexId, pattern, order, responseType, actionId),
                priority = priority,
                randomWeight = 1,
                allowImmediateRepeat = false,
                metadata = new ConversationRouteMetadata
                {
                    sourceFormat = "DialoguePreviewCsv",
                    sourceRowNumber = lineNumber,
                    sourceGroupId = groupId,
                    input = DecodePreviewToken(ReadCsvString(row, "input")),
                    regexId = regexId,
                    regexJa = DecodePreviewToken(ReadCsvString(row, "regex_jp")),
                    regexZh = DecodePreviewToken(ReadCsvString(row, "regex_zh")),
                    regexEn = DecodePreviewToken(ReadCsvString(row, "regex_en")),
                    altText = DecodePreviewToken(ReadCsvString(row, "alt")),
                    responseType = responseType,
                    actionId = actionId,
                    condition = DecodePreviewToken(ReadCsvString(row, "condition")),
                    speechControl = DecodePreviewToken(ReadCsvString(row, "SpeechControl")),
                    emotionChangeType = DecodePreviewToken(ReadCsvString(row, "emotion_change_type")),
                    emotionChangeValue = ReadCsvInt(row, "emotion_change_value", 0),
                    pattern = pattern,
                    order = order,
                    waitTime = ReadCsvInt(row, "WaitTime", 0),
                    hideUi = ReadCsvBool(row, "HideUI", false),
                    animation = animation,
                    callOnly = ReadCsvBool(row, "call_only", false),
                    choiceYesPattern = DecodePreviewToken(ReadCsvString(row, "choice_yes_pattern")),
                    choiceNoPattern = DecodePreviewToken(ReadCsvString(row, "choice_no_pattern")),
                    sequenceProgressHold = ReadCsvBool(row, "sequence_progress_hold", false),
                    randomRepeatLimit = ReadCsvInt(row, "random_repeat_limit", -1)
                }
            };

            if (!string.IsNullOrWhiteSpace(regexId))
            {
                route.intents.Add(regexId);
            }

            if (!string.IsNullOrWhiteSpace(localizedResponse))
            {
                route.responses.Add(localizedResponse);
            }

            if (!string.IsNullOrWhiteSpace(actionId))
            {
                route.events.Add(actionId);
            }

            AddDialoguePreviewEffects(route.effects, route.metadata.emotionChangeType, route.metadata.emotionChangeValue);
            AddDialoguePreviewConditions(route, row, options);

            NormalizeRoute(route);
            result.route = route;

            if (string.IsNullOrWhiteSpace(route.id))
            {
                result.errors.Add("DialoguePreview route id could not be generated.");
            }

            return result;
        }

        private static string ResolveDialoguePreviewActionId(string responseType, string actionId, string animation)
        {
            if (!string.IsNullOrWhiteSpace(actionId))
            {
                return actionId;
            }

            if (string.Equals(responseType, "Action", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(animation) &&
                animation.StartsWith(TimelineSequenceActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return animation;
            }

            return string.Empty;
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

        private static void AddCsvConditionColumns(
            List<string> stateConditions,
            Dictionary<string, string> row,
            ConversationDataLoadOptions options)
        {
            foreach (var pair in row)
            {
                if (CsvReservedColumns.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var parsed = ParseScalarOrList(pair.Value, options);
                if (parsed is List<object> listValue)
                {
                    for (var i = 0; i < listValue.Count; i++)
                    {
                        stateConditions.Add($"{pair.Key} contains {FormatToken(listValue[i])}");
                    }
                    continue;
                }

                stateConditions.Add($"{pair.Key} == {FormatToken(parsed)}");
            }
        }

        private static void AddDialoguePreviewConditions(
            ConversationRouteDefinition route,
            Dictionary<string, string> row,
            ConversationDataLoadOptions options)
        {
            var rawCondition = DecodePreviewToken(ReadCsvString(row, "condition"));
            if (!string.IsNullOrWhiteSpace(rawCondition))
            {
                var conditionItems = ParseCsvListValue(rawCondition, options);
                for (var i = 0; i < conditionItems.Count; i++)
                {
                    route.stateConditions.Add(conditionItems[i]);
                }
            }
        }

        private static void AddDialoguePreviewEffects(
            List<ConversationGameStateEffect> effects,
            string emotionChangeType,
            int emotionChangeValue)
        {
            if (string.IsNullOrWhiteSpace(emotionChangeType) || emotionChangeValue == 0)
            {
                return;
            }

            effects.Add(new ConversationGameStateEffect
            {
                type = "increment",
                key = MapEmotionStateKey(emotionChangeType),
                value = ConversationGameStateValue.FromInt(emotionChangeValue)
            });
        }

        private static List<string> ReadLocalizedResponses(Dictionary<string, object> routeObject, string locale)
        {
            if (!string.IsNullOrWhiteSpace(locale))
            {
                var localeKey = $"responses_{locale}";
                var localized = ReadStringList(routeObject, localeKey);
                if (localized.Count > 0)
                {
                    return localized;
                }

                if (routeObject.TryGetValue("localized_responses", out var localizedResponsesObject)
                    && localizedResponsesObject is Dictionary<string, object> localizedMap)
                {
                    if (localizedMap.TryGetValue(locale, out var value))
                    {
                        return ReadStringListValue(value);
                    }
                }
            }

            var responses = ReadStringList(routeObject, "responses");
            if (responses.Count > 0)
            {
                return responses;
            }

            return ReadStringList(routeObject, "response");
        }

        private static List<string> ReadCsvResponses(Dictionary<string, string> row, ConversationDataLoadOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.locale))
            {
                var key = $"responses_{options.locale}";
                if (row.TryGetValue(key, out var localizedValue) && !string.IsNullOrWhiteSpace(localizedValue))
                {
                    return ParseCsvListValue(localizedValue, options, splitNewlines: false);
                }

                var localizedAliases = GetCsvResponseAliases(options.locale);
                for (var i = 0; i < localizedAliases.Length; i++)
                {
                    if (row.TryGetValue(localizedAliases[i], out var aliasValue) && !string.IsNullOrWhiteSpace(aliasValue))
                    {
                        return ParseCsvListValue(aliasValue, options, splitNewlines: false);
                    }
                }
            }

            if (row.TryGetValue("responses", out var responses))
            {
                return ParseCsvListValue(responses, options, splitNewlines: false);
            }

            if (row.TryGetValue("response", out var response))
            {
                return ParseCsvListValue(response, options, splitNewlines: false);
            }

            var fallbackAliases = GetCsvResponseAliases("ja");
            for (var i = 0; i < fallbackAliases.Length; i++)
            {
                if (row.TryGetValue(fallbackAliases[i], out var aliasValue) && !string.IsNullOrWhiteSpace(aliasValue))
                {
                    return ParseCsvListValue(aliasValue, options, splitNewlines: false);
                }
            }

            return new List<string>();
        }

        private static string[] GetCsvResponseAliases(string locale)
        {
                switch ((locale ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "zh":
                    case "zh-cn":
                    case "zh-hans":
                    case "zh-tw":
                        return new[] { "text_cn", "output_zh" };
                    case "en":
                        return new[] { "text_en", "output_en" };
                default:
                    return new[] { "text_jp 1", "text_jp", "output_ja" };
            }
        }

        private static string ReadDialoguePreviewResponse(Dictionary<string, string> row, string locale)
        {
            switch ((locale ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "zh":
                case "zh-cn":
                case "zh-hans":
                case "zh-tw":
                    if (row.TryGetValue("output_zh", out var zhValue) && !string.IsNullOrWhiteSpace(zhValue))
                    {
                        return zhValue;
                    }
                    break;
                case "en":
                    if (row.TryGetValue("output_en", out var enValue) && !string.IsNullOrWhiteSpace(enValue))
                    {
                        return enValue;
                    }
                    break;
            }

            if (row.TryGetValue("output_ja", out var jaValue) && !string.IsNullOrWhiteSpace(jaValue))
            {
                return jaValue;
            }

            if (row.TryGetValue("output_en", out var fallbackEnValue) && !string.IsNullOrWhiteSpace(fallbackEnValue))
            {
                return fallbackEnValue;
            }

            return row.TryGetValue("output_zh", out var fallbackZhValue) ? fallbackZhValue : string.Empty;
        }

        private static List<ConversationGameStateEffect> ReadEffects(
            Dictionary<string, object> routeObject,
            ConversationDataLoadOptions options)
        {
            if (!routeObject.TryGetValue("effects", out var effectListObject) || effectListObject == null)
            {
                return new List<ConversationGameStateEffect>();
            }

            var wrapper = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["effects"] = effectListObject is List<object> listValue ? listValue : new List<object> { effectListObject }
            };

            return ConversationGameStateEffectApplier.ParseEffectSetJson(ConversationMiniJson.Serialize(wrapper, false)).effects;
        }

        private static List<ConversationGameStateEffect> ReadCsvEffects(
            Dictionary<string, string> row,
            ConversationDataLoadOptions options,
            List<string> errors)
        {
            if (!row.TryGetValue("effects", out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                return new List<ConversationGameStateEffect>();
            }

            var trimmed = rawValue.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var json = trimmed.StartsWith("{", StringComparison.Ordinal)
                    ? trimmed
                    : "{\"effects\":" + trimmed + "}";
                return ConversationGameStateEffectApplier.ParseEffectSetJson(json).effects;
            }

            var effects = new List<ConversationGameStateEffect>();
            var tokens = ParseCsvListValue(trimmed, options);
            for (var i = 0; i < tokens.Count; i++)
            {
                if (TryParseEffectDsl(tokens[i], out var effect))
                {
                    effects.Add(effect);
                    continue;
                }

                if (options.effectPresetResolver != null
                    && options.effectPresetResolver.TryResolve(tokens[i], out var resolvedEffects)
                    && resolvedEffects != null)
                {
                    for (var effectIndex = 0; effectIndex < resolvedEffects.Count; effectIndex++)
                    {
                        effects.Add(CloneEffect(resolvedEffects[effectIndex]));
                    }

                    continue;
                }

                errors.Add($"Effect token '{tokens[i]}' could not be resolved. Use JSON, DSL 'type:key:value', or a preset resolver.");
            }

            return effects;
        }

        private static ConversationGameStateEffect CloneEffect(ConversationGameStateEffect source)
        {
            if (source == null)
            {
                return null;
            }

            return new ConversationGameStateEffect
            {
                type = source.type,
                key = source.key,
                value = source.value?.Clone()
            };
        }

        private static bool TryParseEffectDsl(string token, out ConversationGameStateEffect effect)
        {
            effect = null;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var parts = token.Split(new[] { ':' }, 3);
            if (parts.Length < 2)
            {
                return false;
            }

            effect = new ConversationGameStateEffect
            {
                type = parts[0].Trim(),
                key = parts[1].Trim(),
                value = parts.Length >= 3 ? ConvertParsedValue(ParseScalar(parts[2].Trim())) : null
            };
            return !string.IsNullOrWhiteSpace(effect.type) && !string.IsNullOrWhiteSpace(effect.key);
        }

        private static ConversationGameStateValue ConvertParsedValue(object parsedValue)
        {
            return parsedValue switch
            {
                null => null,
                bool boolValue => ConversationGameStateValue.FromBool(boolValue),
                int intValue => ConversationGameStateValue.FromInt(intValue),
                long longValue => ConversationGameStateValue.FromInt(checked((int)longValue)),
                string stringValue => ConversationGameStateValue.FromString(stringValue),
                List<object> listValue => ConversationGameStateValue.FromStringList(listValue.Select(item => item?.ToString() ?? string.Empty)),
                _ => ConversationGameStateValue.FromString(parsedValue.ToString())
            };
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
            return value switch
            {
                null => "\"\"",
                bool boolValue => boolValue ? "true" : "false",
                int intValue => intValue.ToString(CultureInfo.InvariantCulture),
                long longValue => longValue.ToString(CultureInfo.InvariantCulture),
                string stringValue => QuoteString(stringValue),
                _ => QuoteString(value.ToString())
            };
        }

        private static string QuoteString(string value)
        {
            var safeValue = value ?? string.Empty;
            return "\"" + safeValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void NormalizeRoute(ConversationRouteDefinition route)
        {
            route.intents = DistinctNonEmpty(route.intents);
            route.requiredTags = DistinctNonEmpty(route.requiredTags);
            route.excludedTags = DistinctNonEmpty(route.excludedTags);
            route.stateConditions = route.stateConditions.Where(condition => !string.IsNullOrWhiteSpace(condition)).ToList();
            route.responses = route.responses.Where(response => response != null).ToList();
            route.events = DistinctNonEmpty(route.events);
        }

        private static List<string> DistinctNonEmpty(List<string> values)
        {
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> ReadStringList(Dictionary<string, object> source, string key)
        {
            return source.TryGetValue(key, out var value)
                ? ReadStringListValue(value)
                : new List<string>();
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
                    return listValue.Select(ReadStringValue).ToList();
                default:
                    return new List<string> { ReadStringValue(value) };
            }
        }

        private static string ReadStringValue(object value)
        {
            return value?.ToString() ?? string.Empty;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
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

        private static List<string> ReadCsvConditionList(Dictionary<string, string> row, ConversationDataLoadOptions options)
        {
            if (!row.TryGetValue("state_conditions", out var value) || string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return ParseCsvListValue(value, options);
        }

        private static List<string> ReadCsvList(Dictionary<string, string> row, string key, ConversationDataLoadOptions options)
        {
            return row.TryGetValue(key, out var value)
                ? ParseCsvListValue(value, options)
                : new List<string>();
        }

        private static List<string> ParseCsvListValue(string value, ConversationDataLoadOptions options, bool splitNewlines = true)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var parsed = ConversationMiniJson.Deserialize(trimmed);
                if (parsed is List<object> items)
                {
                    return items.Select(item => item?.ToString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
                }

                throw new FormatException("CSV list field JSON must be an array.");
            }

            return trimmed
                .Split(splitNewlines ? new[] { options.csvListSeparator, '\n' } : new[] { options.csvListSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static object ParseScalarOrList(string rawValue, ConversationDataLoadOptions options)
        {
            var list = ParseCsvListValue(rawValue, options);
            if (list.Count > 1)
            {
                return list.Cast<object>().ToList();
            }

            return ParseScalar(rawValue);
        }

        private static object ParseScalar(string rawValue)
        {
            var trimmed = rawValue?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (bool.TryParse(trimmed, out var boolValue))
            {
                return boolValue;
            }

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return intValue;
            }

            return trimmed;
        }

        private static string ReadCsvString(Dictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
        }

        private static int ReadCsvInt(Dictionary<string, string> row, string key, int defaultValue)
        {
            if (!row.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new FormatException($"Field '{key}' must be an integer.");
            }

            return parsed;
        }

        private static bool ReadCsvBool(Dictionary<string, string> row, string key, bool defaultValue)
        {
            if (!row.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (!bool.TryParse(value.Trim(), out var parsed))
            {
                throw new FormatException($"Field '{key}' must be a boolean.");
            }

            return parsed;
        }

        private static Dictionary<string, string> ToRowDictionary(List<string> headers, List<string> fields)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                row[header] = i < fields.Count ? fields[i] ?? string.Empty : string.Empty;
            }

            return row;
        }

        private static List<string> NormalizeCsvHeaders(List<string> headers)
        {
            var result = new List<string>(headers.Count);
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i] ?? string.Empty;
                if (i == 0)
                {
                    header = header.TrimStart('\uFEFF');
                }

                result.Add(NormalizeCsvHeaderAlias(header));
            }

            return result;
        }

        private static bool IsEmptyRecord(List<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(fields[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LooksLikeDialoguePreviewFormat(List<string> headers)
        {
            if (headers == null || headers.Count == 0)
            {
                return false;
            }

            var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
            return headerSet.Contains("regex_id")
                && (headerSet.Contains("output_ja") || headerSet.Contains("text_jp") || headerSet.Contains("text_jp 1"))
                && headerSet.Contains("pattern")
                && headerSet.Contains("order");
        }

        private static string NormalizeCsvHeaderAlias(string header)
        {
            var trimmed = (header ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "regex":
                    return "regex_jp";
                case "regex_cn":
                    return "regex_zh";
                case "regex id":
                    return "regex_id";
                case "text_jp 1":
                case "text_jp":
                    return "output_ja";
                case "text_cn":
                    return "output_zh";
                case "text_en":
                    return "output_en";
                case "reaction_type":
                    return "action_id";
                default:
                    return trimmed;
            }
        }

        private static string BuildDialoguePreviewRouteId(
            string sourceId,
            string regexId,
            int pattern,
            int order,
            string responseType,
            string actionId)
        {
            var idCore = !string.IsNullOrWhiteSpace(regexId) ? regexId : sourceId;
            if (string.IsNullOrWhiteSpace(idCore))
            {
                return string.Empty;
            }

            var normalizedCore = SanitizeRouteIdToken(idCore);
            var normalizedType = string.IsNullOrWhiteSpace(responseType) ? "normal" : SanitizeRouteIdToken(responseType);
            var normalizedAction = string.IsNullOrWhiteSpace(actionId) ? string.Empty : "_" + SanitizeRouteIdToken(actionId);
            return $"dlg_{SanitizeRouteIdToken(sourceId)}_{normalizedCore}_p{pattern}_o{order}_{normalizedType}{normalizedAction}";
        }

        private static string SanitizeRouteIdToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "none";
            }

            var decoded = DecodePreviewToken(value).Trim().ToLowerInvariant();
            var builder = new StringBuilder(decoded.Length);
            for (var i = 0; i < decoded.Length; i++)
            {
                var current = decoded[i];
                if (char.IsLetterOrDigit(current))
                {
                    builder.Append(current);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }

        private static string DecodePreviewToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\_", "_")
                .Replace("\\|", "|")
                .Replace("\\n", "\n");
        }

        private static string MapEmotionStateKey(string emotionChangeType)
        {
            return emotionChangeType.Trim().ToLowerInvariant() switch
            {
                "hostility" => "Hostility",
                "anxiety" => "Anxiety",
                "affection" => "Affection",
                "sadistic" => "Sadistic",
                "obedience" => "Obedience",
                "instinct" => "Instinct",
                "concern" => "Concern",
                _ => emotionChangeType
            };
        }

        private static List<int> LocateJsonRouteLineNumbers(string json, int expectedCount)
        {
            var lineNumbers = new List<int>();
            if (string.IsNullOrWhiteSpace(json) || expectedCount <= 0)
            {
                return lineNumbers;
            }

            var arrayStartIndex = FindRouteArrayStartIndex(json);
            if (arrayStartIndex < 0)
            {
                return lineNumbers;
            }

            var inString = false;
            var objectDepth = 0;
            var nestedArrayDepth = 0;
            var lineNumber = CountLines(json, 0, arrayStartIndex) + 1;

            for (var index = arrayStartIndex + 1; index < json.Length; index++)
            {
                var current = json[index];
                if (current == '\n')
                {
                    lineNumber++;
                }

                if (current == '"' && !IsEscaped(json, index))
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                switch (current)
                {
                    case '{' when objectDepth == 0 && nestedArrayDepth == 0:
                        lineNumbers.Add(lineNumber);
                        objectDepth++;
                        break;
                    case '{':
                        objectDepth++;
                        break;
                    case '}':
                        objectDepth = Math.Max(0, objectDepth - 1);
                        break;
                    case '[' when objectDepth > 0:
                        nestedArrayDepth++;
                        break;
                    case ']' when objectDepth > 0 && nestedArrayDepth > 0:
                        nestedArrayDepth--;
                        break;
                    case ']' when objectDepth == 0 && nestedArrayDepth == 0:
                        return lineNumbers;
                }

                if (lineNumbers.Count >= expectedCount && objectDepth == 0 && current == ']')
                {
                    break;
                }
            }

            return lineNumbers;
        }

        private static int FindRouteArrayStartIndex(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return -1;
            }

            for (var i = 0; i < json.Length; i++)
            {
                if (!char.IsWhiteSpace(json[i]))
                {
                    if (json[i] == '[')
                    {
                        return i;
                    }

                    break;
                }
            }

            var routesIndex = json.IndexOf("\"routes\"", StringComparison.Ordinal);
            if (routesIndex < 0)
            {
                return -1;
            }

            for (var i = routesIndex; i < json.Length; i++)
            {
                if (json[i] == '[')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int InferJsonLineNumber(string json, string message)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(message))
            {
                return 1;
            }

            var digits = new string(message.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (!int.TryParse(digits, out var position))
            {
                var positionToken = "position ";
                var positionIndex = message.IndexOf(positionToken, StringComparison.Ordinal);
                if (positionIndex < 0)
                {
                    return 1;
                }

                var start = positionIndex + positionToken.Length;
                var end = start;
                while (end < message.Length && char.IsDigit(message[end]))
                {
                    end++;
                }

                if (!int.TryParse(message.Substring(start, end - start), out position))
                {
                    return 1;
                }
            }

            return CountLines(json, 0, Math.Min(position, json.Length)) + 1;
        }

        private static int CountLines(string text, int startIndex, int endIndex)
        {
            var count = 0;
            for (var i = Math.Max(0, startIndex); i < Math.Min(endIndex, text.Length); i++)
            {
                if (text[i] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsEscaped(string text, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                slashCount++;
            }

            return slashCount % 2 == 1;
        }

        private sealed class RouteParseResult
        {
            public ConversationRouteDefinition route;
            public List<string> errors = new();
        }

        private sealed class ConversationCsvRecord
        {
            public int lineNumber;
            public List<string> fields = new();
        }

        private static class ConversationCsvParser
        {
            public static List<ConversationCsvRecord> Parse(string csv)
            {
                var records = new List<ConversationCsvRecord>();
                var currentFields = new List<string>();
                var currentField = new StringBuilder();
                var inQuotes = false;
                var lineNumber = 1;
                var rowLineNumber = 1;

                for (var index = 0; index < csv.Length; index++)
                {
                    var current = csv[index];
                    if (current == '"')
                    {
                        if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            currentField.Append('"');
                            index++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && current == ',')
                    {
                        currentFields.Add(currentField.ToString());
                        currentField.Length = 0;
                        continue;
                    }

                    if (!inQuotes && (current == '\r' || current == '\n'))
                    {
                        currentFields.Add(currentField.ToString());
                        currentField.Length = 0;
                        records.Add(new ConversationCsvRecord
                        {
                            lineNumber = rowLineNumber,
                            fields = currentFields
                        });

                        currentFields = new List<string>();
                        if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        {
                            index++;
                        }

                        lineNumber++;
                        rowLineNumber = lineNumber;
                        continue;
                    }

                    currentField.Append(current);
                    if (current == '\n')
                    {
                        lineNumber++;
                    }
                }

                if (inQuotes)
                {
                    throw new FormatException("CSV contains an unterminated quoted field.");
                }

                currentFields.Add(currentField.ToString());
                if (currentFields.Count > 1 || currentFields[0].Length > 0 || records.Count == 0)
                {
                    records.Add(new ConversationCsvRecord
                    {
                        lineNumber = rowLineNumber,
                        fields = currentFields
                    });
                }

                return records;
            }
        }
    }
}
