using System.Collections.Generic;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationGameStateTests
    {
        [Test]
        public void SaveAndLoad_RoundTripsKeyValueState()
        {
            var state = new ConversationGameState();
            state.SetInt("MealRefusalCount", 2);
            state.SetBool("MealTakenToday", false);
            state.SetEnumList("KnownFoodCategories", new[] { FoodCategory.Fish });

            var json = state.SaveToJson(true);
            var loaded = ConversationGameState.FromJson(json);

            Assert.That(loaded.TryGetInt("MealRefusalCount", out var mealRefusalCount), Is.True);
            Assert.That(mealRefusalCount, Is.EqualTo(2));
            Assert.That(loaded.TryGetBool("MealTakenToday", out var mealTakenToday), Is.True);
            Assert.That(mealTakenToday, Is.False);
            Assert.That(loaded.TryGetStringList("KnownFoodCategories", out var knownCategories), Is.True);
            CollectionAssert.AreEquivalent(new[] { "Fish" }, knownCategories);
        }

        [Test]
        public void Evaluate_HandlesNumericBooleanAndContainsConditions()
        {
            var state = CreateSampleState();

            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "MealRefusalCount >= 4"), Is.True);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "MealTakenToday == false"), Is.True);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "KnownFoodCategories contains Fish"), Is.True);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "KnownFoodCategories not contains Meat"), Is.True);
        }

        [Test]
        public void ApplyEffects_UpdatesStateByEffectType()
        {
            var state = new ConversationGameState();
            state.SetInt("MealRefusalCount", 3);
            state.SetBool("MealTakenToday", false);
            state.SetStringList("KnownFoodCategories", new[] { "Fish" });

            var effectSet = ConversationGameStateEffectApplier.ParseEffectSetJson(
                "{\n" +
                "  \"effects\": [\n" +
                "    { \"type\": \"increment\", \"key\": \"MealRefusalCount\", \"value\": 1 },\n" +
                "    { \"type\": \"set\", \"key\": \"MealTakenToday\", \"value\": true },\n" +
                "    { \"type\": \"add_to_list\", \"key\": \"KnownFoodCategories\", \"value\": \"Meat\" }\n" +
                "  ]\n" +
                "}");

            ConversationGameStateEffectApplier.Apply(state, effectSet.effects);

            Assert.That(state.TryGetInt("MealRefusalCount", out var mealRefusalCount), Is.True);
            Assert.That(mealRefusalCount, Is.EqualTo(4));
            Assert.That(state.TryGetBool("MealTakenToday", out var mealTakenToday), Is.True);
            Assert.That(mealTakenToday, Is.True);
            Assert.That(state.TryGetStringList("KnownFoodCategories", out var knownCategories), Is.True);
            CollectionAssert.AreEquivalent(new[] { "Fish", "Meat" }, knownCategories);
        }

        [Test]
        public void EnumState_CanBeStoredAndReadAsEnum()
        {
            var state = new ConversationGameState();
            state.SetEnum("FavoriteFoodCategory", FoodCategory.Noodle);
            state.AddToEnumList("KnownFoodCategories", FoodCategory.Fish);
            state.AddToEnumList("KnownFoodCategories", FoodCategory.Meat);

            Assert.That(state.TryGetEnum("FavoriteFoodCategory", out FoodCategory favoriteCategory), Is.True);
            Assert.That(favoriteCategory, Is.EqualTo(FoodCategory.Noodle));
            Assert.That(state.TryGetStringList("KnownFoodCategories", out var knownCategories), Is.True);
            CollectionAssert.AreEquivalent(new[] { "Fish", "Meat" }, knownCategories);
        }

        [Test]
        public void EvaluateAll_ReturnsFalseWhenAnyConditionFails()
        {
            var state = CreateSampleState();
            var expressions = new List<string>
            {
                "MealRefusalCount >= 4",
                "MealTakenToday == false",
                "KnownFoodCategories contains Meat"
            };

            Assert.That(ConversationGameStateConditionEvaluator.EvaluateAll(state, expressions), Is.False);
        }

        private static ConversationGameState CreateSampleState()
        {
            var state = new ConversationGameState();
            state.SetInt("MealRefusalCount", 4);
            state.SetBool("MealTakenToday", false);
            state.SetStringList("KnownFoodCategories", new[] { "Fish" });
            return state;
        }
    }
}
