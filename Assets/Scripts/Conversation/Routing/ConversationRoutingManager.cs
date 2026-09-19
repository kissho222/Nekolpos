using System.Collections.Generic;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class ConversationRoutingManager : MonoBehaviour
    {
        [SerializeField] [TextArea(8, 30)] private string routesJson =
            "[\n" +
            "  {\n" +
            "    \"id\": \"meal_refusal_4\",\n" +
            "    \"priority\": 1000,\n" +
            "    \"play_once\": true,\n" +
            "    \"conditions\": {\n" +
            "      \"intent\": \"request_food\",\n" +
            "      \"MealRefusalCount\": 4,\n" +
            "      \"tags\": [\"Emergency\"]\n" +
            "    },\n" +
            "    \"responses\": [\"何か食べないと！\"],\n" +
            "    \"effects\": [\n" +
            "      { \"type\": \"set\", \"key\": \"EmergencyFoodEvent\", \"value\": true }\n" +
            "    ],\n" +
            "    \"events\": [\"cat_go_out\"]\n" +
            "  }\n" +
            "]";

        public ConversationRouteResult RouteConversation(
            string parseResultJson,
            string gameStateJson,
            IEnumerable<string> tags = null,
            int? randomSeed = null)
        {
            var router = new ConversationRouter(ConversationRouter.ParseRoutesJson(routesJson));
            return router.RouteFromJson(parseResultJson, gameStateJson, tags, randomSeed);
        }

        public string RouteConversationToJson(
            string parseResultJson,
            string gameStateJson,
            IEnumerable<string> tags = null,
            int? randomSeed = null,
            bool prettyPrint = true)
        {
            return RouteConversation(parseResultJson, gameStateJson, tags, randomSeed) is { } result
                ? ConversationRouter.SerializeResult(result, prettyPrint)
                : "{}";
        }
    }
}
