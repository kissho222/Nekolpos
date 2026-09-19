using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nekolpos.StatusSystem;

namespace Backgammon.Conversation
{
    public sealed class ConversationEventExecutor
    {
        private readonly Dictionary<string, ConversationEventDefinition> eventDefinitions;
        private readonly ConversationEventHandlerRegistry handlerRegistry;
        private readonly List<Task> pendingTasks = new();

        public ConversationEventExecutor(
            IEnumerable<ConversationEventDefinition> definitions,
            ConversationEventHandlerRegistry registry = null)
        {
            eventDefinitions = new Dictionary<string, ConversationEventDefinition>(StringComparer.Ordinal);
            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                    {
                        continue;
                    }

                    eventDefinitions[definition.id] = definition;
                }
            }

            handlerRegistry = registry ?? ConversationEventHandlerRegistry.CreateDefault();
        }

        public async Task<ConversationEventExecutionResult> ExecuteEventByIdAsync(
            string eventId,
            ConversationGameState gameState,
            IConversationEventBridge bridge = null,
            CancellationToken cancellationToken = default)
        {
            if (!eventDefinitions.TryGetValue(eventId ?? string.Empty, out var definition))
            {
                throw new KeyNotFoundException($"Conversation event definition '{eventId}' was not found.");
            }

            var context = new ConversationEventExecutionContext(CloneState(gameState), bridge);
            context.ExecutedEventIds.Add(definition.id);
            await ExecuteNodeCoreAsync(definition, context, cancellationToken);
            await WaitForPendingAsync(cancellationToken);
            return BuildResult(context);
        }

        public async Task<ConversationEventExecutionResult> ExecuteSequenceByIdsAsync(
            IEnumerable<string> eventIds,
            ConversationGameState gameState,
            IConversationEventBridge bridge = null,
            CancellationToken cancellationToken = default)
        {
            var context = new ConversationEventExecutionContext(CloneState(gameState), bridge);
            if (eventIds != null)
            {
                foreach (var eventId in eventIds)
                {
                    if (!eventDefinitions.TryGetValue(eventId ?? string.Empty, out var definition))
                    {
                        throw new KeyNotFoundException($"Conversation event definition '{eventId}' was not found.");
                    }

                    context.ExecutedEventIds.Add(definition.id);
                    await ExecuteNodeCoreAsync(definition, context, cancellationToken);
                }
            }

            await WaitForPendingAsync(cancellationToken);
            return BuildResult(context);
        }

        public async Task WaitForPendingAsync(CancellationToken cancellationToken = default)
        {
            while (pendingTasks.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tasks = pendingTasks.ToArray();
                pendingTasks.Clear();
                await Task.WhenAll(tasks);
            }
        }

        public static List<ConversationEventDefinition> ParseDefinitionsJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConversationEventDefinition>();
            }

            var parsed = ConversationMiniJson.Deserialize(json);
            var definitionItems = ResolveDefinitionItems(parsed);
            var result = new List<ConversationEventDefinition>(definitionItems.Count);
            for (var i = 0; i < definitionItems.Count; i++)
            {
                if (definitionItems[i] is not Dictionary<string, object> definitionObject)
                {
                    throw new FormatException("Each event definition entry must be an object.");
                }

                result.Add(ParseDefinition(definitionObject));
            }

            return result;
        }

        private static List<object> ResolveDefinitionItems(object parsed)
        {
            switch (parsed)
            {
                case List<object> definitionList:
                    return definitionList;
                case Dictionary<string, object> root when root.TryGetValue("events", out var eventsValue) && eventsValue is List<object> eventList:
                    return eventList;
                case Dictionary<string, object> root when root.TryGetValue("definitions", out var definitionsValue) && definitionsValue is List<object> definitionArray:
                    return definitionArray;
                default:
                    throw new FormatException("Event definition JSON must be an array or an object with an 'events' array.");
            }
        }

        private static ConversationEventDefinition ParseDefinition(Dictionary<string, object> source)
        {
            var definition = new ConversationEventDefinition
            {
                id = ReadOptionalString(source, "id"),
                eventName = ReadOptionalString(source, "event"),
                eventId = ReadOptionalString(source, "event_id"),
                waitForCompletion = ReadOptionalBool(source, "wait_for_completion", true),
                parameters = ParseParameters(source),
                effects = ParseEffects(source),
                sequence = ParseChildDefinitions(source, "sequence"),
                parallel = ParseChildDefinitions(source, "parallel")
            };

            if (string.IsNullOrWhiteSpace(definition.eventId))
            {
                definition.eventId = ReadOptionalString(source, "eventId");
            }

            return definition;
        }

        private async Task ExecuteNodeAsync(
            ConversationEventDefinition definition,
            ConversationEventExecutionContext context,
            CancellationToken cancellationToken)
        {
            var task = ExecuteNodeCoreAsync(definition, context, cancellationToken);
            if (definition.waitForCompletion)
            {
                await task;
                return;
            }

            pendingTasks.Add(task);
        }

        private async Task ExecuteNodeCoreAsync(
            ConversationEventDefinition definition,
            ConversationEventExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(definition.eventId))
            {
                if (!eventDefinitions.TryGetValue(definition.eventId, out var referencedDefinition))
                {
                    throw new KeyNotFoundException($"Referenced event definition '{definition.eventId}' was not found.");
                }

                context.ExecutedEventIds.Add(referencedDefinition.id);
                await ExecuteNodeCoreAsync(referencedDefinition, context, cancellationToken);
                ApplyEffects(context, definition.effects);
                return;
            }

            if (definition.parallel.Count > 0)
            {
                var childTasks = definition.parallel
                    .Select(child => ExecuteNodeAsync(child, context, cancellationToken))
                    .ToArray();
                await Task.WhenAll(childTasks);
                ApplyEffects(context, definition.effects);
                return;
            }

            if (definition.sequence.Count > 0)
            {
                for (var i = 0; i < definition.sequence.Count; i++)
                {
                    await ExecuteNodeAsync(definition.sequence[i], context, cancellationToken);
                }

                ApplyEffects(context, definition.effects);
                return;
            }

            if (string.IsNullOrWhiteSpace(definition.eventName))
            {
                ApplyEffects(context, definition.effects);
                return;
            }

            if (!handlerRegistry.TryGet(definition.eventName, out var handler))
            {
                throw new KeyNotFoundException($"Conversation event handler '{definition.eventName}' is not registered.");
            }

            var invocation = new ConversationEventInvocation(
                definition.eventName,
                definition.parameters.Clone(),
                context,
                this);
            await handler.ExecuteAsync(invocation, cancellationToken);
            ApplyEffects(context, definition.effects);
        }

        private static ConversationEventParameters ParseParameters(Dictionary<string, object> source)
        {
            var result = new ConversationEventParameters();
            if (!source.TryGetValue("params", out var paramsObject) || paramsObject is not Dictionary<string, object> parameters)
            {
                return result;
            }

            foreach (var pair in parameters)
            {
                result.Set(pair.Key, CloneParameterValue(pair.Value));
            }

            return result;
        }

        private static object CloneParameterValue(object value)
        {
            return value switch
            {
                List<object> listValue => new List<object>(listValue),
                Dictionary<string, object> objectValue => new Dictionary<string, object>(objectValue, StringComparer.Ordinal),
                _ => value
            };
        }

        private static List<ConversationGameStateEffect> ParseEffects(Dictionary<string, object> source)
        {
            if (!source.TryGetValue("effects", out var effectsValue) || effectsValue is not List<object> effectItems)
            {
                return new List<ConversationGameStateEffect>();
            }

            var wrapper = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["effects"] = effectItems
            };
            return ConversationGameStateEffectApplier.ParseEffectSetJson(ConversationMiniJson.Serialize(wrapper, false)).effects;
        }

        private static List<ConversationEventDefinition> ParseChildDefinitions(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var childValue) || childValue is not List<object> childItems)
            {
                return new List<ConversationEventDefinition>();
            }

            var result = new List<ConversationEventDefinition>(childItems.Count);
            for (var i = 0; i < childItems.Count; i++)
            {
                if (childItems[i] is not Dictionary<string, object> childObject)
                {
                    throw new FormatException($"Each child entry under '{key}' must be an object.");
                }

                result.Add(ParseDefinition(childObject));
            }

            return result;
        }

        private static string ReadOptionalString(Dictionary<string, object> source, string key)
        {
            return source.TryGetValue(key, out var value) && value is string stringValue
                ? stringValue
                : string.Empty;
        }

        private static bool ReadOptionalBool(Dictionary<string, object> source, string key, bool defaultValue)
        {
            return source.TryGetValue(key, out var value) && value is bool boolValue
                ? boolValue
                : defaultValue;
        }

        private static void ApplyEffects(ConversationEventExecutionContext context, List<ConversationGameStateEffect> effects)
        {
            if (effects == null || effects.Count == 0)
            {
                return;
            }

            var clonedEffects = CloneEffects(effects);
            for (var i = 0; i < clonedEffects.Count; i++)
            {
                var effect = clonedEffects[i];
                if (effect == null)
                {
                    continue;
                }

                var beforeValue = ReadStateValue(context.State, effect.key);
                StatusManager statusManager = StatusManager.FindOrCreate();
                if (statusManager == null || !statusManager.TryApplyConversationEffect(effect, "ConversationEvent", string.Empty, out _))
                {
                    ConversationGameStateEffectApplier.Apply(context.State, effect);
                }

                var afterValue = ReadStateValue(context.State, effect.key);

                context.AppliedEffects.Add(new ConversationEffectExecutionDebug
                {
                    type = effect.type ?? string.Empty,
                    key = effect.key ?? string.Empty,
                    beforeValue = beforeValue,
                    afterValue = afterValue
                });
                context.EventLogs.Add($"effect:{effect.type}:{effect.key} {beforeValue} -> {afterValue}");
            }
        }

        private static List<ConversationGameStateEffect> CloneEffects(List<ConversationGameStateEffect> effects)
        {
            var result = new List<ConversationGameStateEffect>(effects.Count);
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

        private static ConversationGameState CloneState(ConversationGameState source)
        {
            return source == null
                ? new ConversationGameState()
                : ConversationGameState.FromJson(source.SaveToJson(false));
        }

        private static ConversationEventExecutionResult BuildResult(ConversationEventExecutionContext context)
        {
            return new ConversationEventExecutionResult
            {
                succeeded = true,
                executedEventIds = new List<string>(context.ExecutedEventIds),
                dispatchedEvents = new List<string>(context.DispatchedEvents),
                eventLogs = new List<string>(context.EventLogs),
                appliedEffects = new List<ConversationEffectExecutionDebug>(context.AppliedEffects),
                nextStateJson = context.State.SaveToJson(true)
            };
        }

        private static string ReadStateValue(ConversationGameState state, string key)
        {
            if (state == null || string.IsNullOrWhiteSpace(key) || !state.TryGetValue(key, out var value) || value == null)
            {
                return "<missing>";
            }

            return value.kind switch
            {
                ConversationGameStateValueKind.Integer => value.intValue.ToString(),
                ConversationGameStateValueKind.Boolean => value.boolValue ? "true" : "false",
                ConversationGameStateValueKind.String => value.stringValue ?? string.Empty,
                ConversationGameStateValueKind.StringList => "[" + string.Join(", ", value.listValue) + "]",
                _ => "<unknown>"
            };
        }
    }
}
