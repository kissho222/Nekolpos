using System.Collections.Generic;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationRouterTests
    {
        [Test]
        public void Route_SelectsHighestPriorityMatchingConversation()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"normal_food\",\n" +
                "    \"priority\": 100,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"events\": [\"normal\"]\n" +
                "  },\n" +
                "  {\n" +
                "    \"id\": \"meal_refusal_4\",\n" +
                "    \"priority\": 1000,\n" +
                "    \"conditions\": { \"intent\": \"request_food\", \"MealRefusalCount\": 4 },\n" +
                "    \"events\": [\"emergency\"]\n" +
                "  }\n" +
                "]");

            var result = router.RouteFromJson(
                "{\"input\":\"ごはん\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[]}",
                "{ \"MealRefusalCount\": 4, \"MealTakenToday\": false }",
                null,
                1);

            Assert.That(result.matched, Is.True);
            Assert.That(result.conversationId, Is.EqualTo("meal_refusal_4"));
            CollectionAssert.AreEqual(new[] { "emergency" }, result.events);
        }

        [Test]
        public void Route_AppliesEffectsAndReturnsNextState()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"food_followup\",\n" +
                "    \"priority\": 500,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"effects\": [\n" +
                "      { \"type\": \"increment\", \"key\": \"MealRefusalCount\", \"value\": 1 },\n" +
                "      { \"type\": \"set\", \"key\": \"MealTakenToday\", \"value\": true }\n" +
                "    ],\n" +
                "    \"events\": [\"cat_go_out\"]\n" +
                "  }\n" +
                "]");

            var result = router.RouteFromJson(
                "{\"input\":\"ごはん\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[]}",
                "{ \"MealRefusalCount\": 2, \"MealTakenToday\": false }",
                null,
                2);

            var nextState = ConversationGameState.FromJson(result.nextStateJson);
            Assert.That(nextState.TryGetInt("MealRefusalCount", out var mealRefusalCount), Is.True);
            Assert.That(mealRefusalCount, Is.EqualTo(3));
            Assert.That(nextState.TryGetBool("MealTakenToday", out var mealTakenToday), Is.True);
            Assert.That(mealTakenToday, Is.True);
            Assert.That(nextState.TryGetString(ConversationRouter.LastRouteIdStateKey, out var lastRouteId), Is.True);
            Assert.That(lastRouteId, Is.EqualTo("food_followup"));
        }

        [Test]
        public void Route_SkipsPlayOnceConversationAlreadyMarkedInState()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"only_once\",\n" +
                "    \"priority\": 1000,\n" +
                "    \"play_once\": true,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"events\": [\"once\"]\n" +
                "  },\n" +
                "  {\n" +
                "    \"id\": \"fallback\",\n" +
                "    \"priority\": 500,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"events\": [\"fallback\"]\n" +
                "  }\n" +
                "]");

            var result = router.RouteFromJson(
                "{\"input\":\"ごはん\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[]}",
                "{ \"__conversation.played_once_ids\": [\"only_once\"] }",
                null,
                3);

            Assert.That(result.conversationId, Is.EqualTo("fallback"));
        }

        [Test]
        public void Route_AvoidsImmediateRepeatWhenAlternativeExists()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"route_a\",\n" +
                "    \"priority\": 100,\n" +
                "    \"random_weight\": 1,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"events\": [\"a\"]\n" +
                "  },\n" +
                "  {\n" +
                "    \"id\": \"route_b\",\n" +
                "    \"priority\": 100,\n" +
                "    \"random_weight\": 1,\n" +
                "    \"conditions\": { \"intent\": \"request_food\" },\n" +
                "    \"events\": [\"b\"]\n" +
                "  }\n" +
                "]");

            var result = router.RouteFromJson(
                "{\"input\":\"ごはん\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[]}",
                "{ \"__conversation.last_route_id\": \"route_a\" }",
                null,
                0);

            Assert.That(result.conversationId, Is.EqualTo("route_b"));
        }

        [Test]
        public void Route_HonorsTagConditionsAndStateContainsConditions()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"morning_fish\",\n" +
                "    \"priority\": 700,\n" +
                "    \"conditions\": {\n" +
                "      \"intent\": \"request_food\",\n" +
                "      \"tags\": [\"Morning\"],\n" +
                "      \"KnownFoodCategories\": [\"Fish\"]\n" +
                "    },\n" +
                "    \"events\": [\"morning\"]\n" +
                "  }\n" +
                "]");

            var result = router.RouteFromJson(
                "{\"input\":\"おさかなたべたい\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[{\"type\":\"food_category\",\"value\":\"Fish\"}]}",
                "{ \"KnownFoodCategories\": [\"Fish\"] }",
                new[] { "Morning" },
                4);

            Assert.That(result.matched, Is.True);
            Assert.That(result.conversationId, Is.EqualTo("morning_fish"));
        }

        [Test]
        public void SerializeResult_WritesExpectedShape()
        {
            var router = CreateRouter(
                "[\n" +
                "  {\n" +
                "    \"id\": \"meal_refusal_4\",\n" +
                "    \"priority\": 1000,\n" +
                "    \"play_once\": true,\n" +
                "    \"conditions\": { \"intent\": \"request_food\", \"MealRefusalCount\": 4 },\n" +
                "    \"effects\": [\n" +
                "      { \"type\": \"set\", \"key\": \"EmergencyFoodEvent\", \"value\": true }\n" +
                "    ],\n" +
                "    \"events\": [\"cat_go_out\"]\n" +
                "  }\n" +
                "]");

            var json = router.RouteToJson(
                "{\"input\":\"ごはん\",\"intents\":[{\"intent\":\"request_food\",\"priority\":100}],\"categories\":[]}",
                "{ \"MealRefusalCount\": 4 }",
                null,
                5,
                false);

            StringAssert.Contains("\"conversation_id\":\"meal_refusal_4\"", json);
            StringAssert.Contains("\"events\":[\"cat_go_out\"]", json);
            StringAssert.Contains("\"effects\":[{\"type\":\"set\",\"key\":\"EmergencyFoodEvent\",\"value\":true}]", json);
            StringAssert.Contains("\"next_state\"", json);
        }

        private static ConversationRouter CreateRouter(string routesJson)
        {
            return new ConversationRouter(ConversationRouter.ParseRoutesJson(routesJson));
        }
    }
}
