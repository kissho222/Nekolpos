using System;
using System.Collections.Generic;
using System.Linq;

namespace Backgammon.Conversation
{
    [Serializable]
    public sealed class ConversationRouteCatalogFilter
    {
        public string intent;
        public string tag;
        public List<string> activeTags = new();
        public ConversationGameState gameState;
        public int? minimumPriority;
        public int? maximumPriority;
    }

    public sealed class ConversationRouteCatalog
    {
        private readonly List<ConversationRouteDefinition> routes;
        private readonly Dictionary<string, ConversationRouteDefinition> routesById;
        private readonly Dictionary<string, List<ConversationRouteDefinition>> routesByIntent;
        private readonly Dictionary<string, List<ConversationRouteDefinition>> routesByTag;

        public ConversationRouteCatalog(IEnumerable<ConversationRouteDefinition> routeDefinitions)
        {
            routes = routeDefinitions != null
                ? routeDefinitions
                    .Where(route => route != null && !string.IsNullOrWhiteSpace(route.id))
                    .OrderByDescending(route => route.priority)
                    .ThenBy(route => route.id, StringComparer.Ordinal)
                    .ToList()
                : new List<ConversationRouteDefinition>();

            routesById = new Dictionary<string, ConversationRouteDefinition>(StringComparer.Ordinal);
            routesByIntent = new Dictionary<string, List<ConversationRouteDefinition>>(StringComparer.Ordinal);
            routesByTag = new Dictionary<string, List<ConversationRouteDefinition>>(StringComparer.Ordinal);

            for (var i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                routesById[route.id] = route;

                for (var intentIndex = 0; intentIndex < route.intents.Count; intentIndex++)
                {
                    AddIndexedRoute(routesByIntent, route.intents[intentIndex], route);
                }

                for (var tagIndex = 0; tagIndex < route.requiredTags.Count; tagIndex++)
                {
                    AddIndexedRoute(routesByTag, route.requiredTags[tagIndex], route);
                }
            }
        }

        public IReadOnlyList<ConversationRouteDefinition> Routes => routes;

        public bool TryGetById(string id, out ConversationRouteDefinition route)
        {
            if (!string.IsNullOrWhiteSpace(id) && routesById.TryGetValue(id, out route))
            {
                return true;
            }

            route = null;
            return false;
        }

        public List<ConversationRouteDefinition> FindByIntent(string intent)
        {
            return FindIndexed(routesByIntent, intent);
        }

        public List<ConversationRouteDefinition> FindByTag(string tag)
        {
            return FindIndexed(routesByTag, tag);
        }

        public List<ConversationRouteDefinition> GetRoutesSortedByPriority()
        {
            return new List<ConversationRouteDefinition>(routes);
        }

        public List<ConversationRouteDefinition> Filter(ConversationRouteCatalogFilter filter)
        {
            if (filter == null)
            {
                return GetRoutesSortedByPriority();
            }

            IEnumerable<ConversationRouteDefinition> query = routes;
            if (!string.IsNullOrWhiteSpace(filter.intent))
            {
                query = query.Where(route => route.intents.Contains(filter.intent));
            }

            if (!string.IsNullOrWhiteSpace(filter.tag))
            {
                query = query.Where(route => route.requiredTags.Contains(filter.tag));
            }

            if (filter.minimumPriority.HasValue)
            {
                query = query.Where(route => route.priority >= filter.minimumPriority.Value);
            }

            if (filter.maximumPriority.HasValue)
            {
                query = query.Where(route => route.priority <= filter.maximumPriority.Value);
            }

            if (filter.activeTags != null && filter.activeTags.Count > 0)
            {
                var activeTagSet = new HashSet<string>(filter.activeTags.Where(tag => !string.IsNullOrWhiteSpace(tag)), StringComparer.Ordinal);
                query = query.Where(route => MatchesTags(route, activeTagSet));
            }

            if (filter.gameState != null)
            {
                query = query.Where(route => ConversationGameStateConditionEvaluator.EvaluateAll(filter.gameState, route.stateConditions));
            }

            return query.ToList();
        }

        public static ConversationRouteCatalog Merge(IEnumerable<ConversationRouteCatalog> catalogs)
        {
            var routes = new List<ConversationRouteDefinition>();
            if (catalogs != null)
            {
                foreach (var catalog in catalogs)
                {
                    if (catalog == null)
                    {
                        continue;
                    }

                    routes.AddRange(catalog.routes);
                }
            }

            return new ConversationRouteCatalog(routes);
        }

        private static void AddIndexedRoute(
            Dictionary<string, List<ConversationRouteDefinition>> index,
            string key,
            ConversationRouteDefinition route)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!index.TryGetValue(key, out var routesForKey))
            {
                routesForKey = new List<ConversationRouteDefinition>();
                index[key] = routesForKey;
            }

            routesForKey.Add(route);
        }

        private static List<ConversationRouteDefinition> FindIndexed(
            Dictionary<string, List<ConversationRouteDefinition>> index,
            string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && index.TryGetValue(key, out var routesForKey))
            {
                return new List<ConversationRouteDefinition>(routesForKey);
            }

            return new List<ConversationRouteDefinition>();
        }

        private static bool MatchesTags(ConversationRouteDefinition route, HashSet<string> activeTags)
        {
            for (var i = 0; i < route.requiredTags.Count; i++)
            {
                if (!activeTags.Contains(route.requiredTags[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < route.excludedTags.Count; i++)
            {
                if (activeTags.Contains(route.excludedTags[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
