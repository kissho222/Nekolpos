using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Backgammon.Conversation
{
    public sealed class ConversationDebugRunner
    {
        private readonly ConversationRouteCatalog catalog;
        private readonly List<ConversationEventDefinition> eventDefinitions;
        private readonly IConversationEventBridge eventBridge;

        public ConversationDebugRunner(
            ConversationRouteCatalog routeCatalog,
            IEnumerable<ConversationEventDefinition> definitions,
            IConversationEventBridge bridge = null)
        {
            catalog = routeCatalog ?? new ConversationRouteCatalog(null);
            eventDefinitions = definitions != null ? new List<ConversationEventDefinition>(definitions) : new List<ConversationEventDefinition>();
            eventBridge = bridge;
        }

        public async Task<ConversationDebugRunResult> RunAsync(
            ConversationParseResult parseResult,
            ConversationGameState gameState,
            IEnumerable<string> tags,
            bool executeEvents,
            CancellationToken cancellationToken = default)
        {
            var activeTags = NormalizeTags(tags);
            var stateBefore = CloneState(gameState);
            var router = new ConversationRouter(catalog);
            var routeResult = router.Route(new ConversationRouteRequest
            {
                parseResult = parseResult ?? new ConversationParseResult(),
                gameState = stateBefore,
                tags = activeTags
            });

            var stateAfterRouting = ConversationGameState.FromJson(routeResult.nextStateJson);
            var eventResult = await ExecuteEventsAsync(routeResult.events, stateAfterRouting, executeEvents, cancellationToken);
            return new ConversationDebugRunResult
            {
                rawInput = parseResult?.input ?? string.Empty,
                parseResult = parseResult,
                routeResult = routeResult,
                eventResult = eventResult,
                activeTags = activeTags,
                stateBeforeJson = stateBefore.SaveToJson(true),
                stateAfterRoutingJson = stateAfterRouting.SaveToJson(true),
                stateAfterEventsJson = eventResult.nextStateJson
            };
        }

        public async Task<ConversationDebugRunResult> ForceRouteAsync(
            string routeId,
            ConversationGameState gameState,
            bool executeEvents,
            CancellationToken cancellationToken = default)
        {
            if (!catalog.TryGetById(routeId, out var route))
            {
                throw new KeyNotFoundException($"Conversation route '{routeId}' was not found.");
            }

            var stateBefore = CloneState(gameState);
            var stateAfterRouting = CloneState(stateBefore);
            var clonedEffects = CloneEffects(route.effects);
            ConversationGameStateEffectApplier.Apply(stateAfterRouting, clonedEffects);
            UpdateRoutingHistory(stateAfterRouting, route);

            var routeResult = new ConversationRouteResult
            {
                matched = true,
                conversationId = route.id,
                priority = route.priority,
                response = ConversationVariableResolver.Resolve(route.responses.FirstOrDefault() ?? string.Empty, stateAfterRouting),
                events = new List<string>(route.events),
                effects = clonedEffects,
                nextStateJson = stateAfterRouting.SaveToJson(true),
                candidates = new List<ConversationRouteCandidateDebug>
                {
                    new()
                    {
                        conversationId = route.id,
                        priority = route.priority,
                        passedAllConditions = true,
                        selected = true
                    }
                }
            };

            var eventResult = await ExecuteEventsAsync(route.events, stateAfterRouting, executeEvents, cancellationToken);
            return new ConversationDebugRunResult
            {
                rawInput = string.Empty,
                parseResult = new ConversationParseResult(),
                routeResult = routeResult,
                eventResult = eventResult,
                stateBeforeJson = stateBefore.SaveToJson(true),
                stateAfterRoutingJson = stateAfterRouting.SaveToJson(true),
                stateAfterEventsJson = eventResult.nextStateJson,
                wasForcedRoute = true,
                forcedRouteId = route.id
            };
        }

        private async Task<ConversationEventExecutionResult> ExecuteEventsAsync(
            IEnumerable<string> eventIds,
            ConversationGameState stateAfterRouting,
            bool executeEvents,
            CancellationToken cancellationToken)
        {
            var eventIdList = eventIds != null ? eventIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() : new List<string>();
            if (!executeEvents || eventIdList.Count == 0)
            {
                return new ConversationEventExecutionResult
                {
                    succeeded = true,
                    nextStateJson = stateAfterRouting.SaveToJson(true)
                };
            }

            var executor = new ConversationEventExecutor(eventDefinitions);
            return await executor.ExecuteSequenceByIdsAsync(eventIdList, stateAfterRouting, eventBridge, cancellationToken);
        }

        private static List<string> NormalizeTags(IEnumerable<string> tags)
        {
            return tags != null
                ? tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.Ordinal).ToList()
                : new List<string>();
        }

        private static ConversationGameState CloneState(ConversationGameState source)
        {
            return source == null
                ? new ConversationGameState()
                : ConversationGameState.FromJson(source.SaveToJson(false));
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

        private static void UpdateRoutingHistory(ConversationGameState state, ConversationRouteDefinition route)
        {
            state.SetString(ConversationRouter.LastRouteIdStateKey, route.id);
            if (route.playOnce)
            {
                state.AddToStringList(ConversationRouter.PlayedOnceIdsStateKey, route.id);
            }
        }
    }
}
