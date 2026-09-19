using Backgammon.Conversation;
using Nekolpos.System;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class FoodHungryInputMatcherTests
    {
        [Test]
        public void TryMatch_DetectsHungryInput()
        {
            Assert.That(
                FoodHungryInputMatcher.TryMatch(new PlayerInputContext("おなかすいた"), out var result),
                Is.True);
            Assert.That(result.TriggerType, Is.EqualTo(FoodHungryInputMatcher.TriggerType));
            Assert.That(result.MatchedPattern, Is.EqualTo("hungry"));
            Assert.That(result.FoodName, Is.Empty);
        }

        [TestCase("オナカスイタ")]
        [TestCase("お腹が空いた")]
        [TestCase("御飯を食べたい")]
        public void TryMatch_DetectsNormalizedJapaneseVariants(string input)
        {
            Assert.That(
                FoodHungryInputMatcher.TryMatch(new PlayerInputContext(input), out var result),
                Is.True);
            Assert.That(result.TriggerType, Is.EqualTo(FoodHungryInputMatcher.TriggerType));
        }

        [Test]
        public void TryMatch_ExtractsFoodNameEatWant()
        {
            Assert.That(
                FoodHungryInputMatcher.TryMatch(new PlayerInputContext("ぷりんたべたい"), out var result),
                Is.True);
            Assert.That(result.MatchedPattern, Is.EqualTo("foodNameEatWant"));
            Assert.That(result.FoodName, Is.EqualTo("ぷりん"));
        }

        [Test]
        public void TryMatch_DetectsMealInput()
        {
            Assert.That(
                FoodHungryInputMatcher.TryMatch(new PlayerInputContext("しょくじ"), out var result),
                Is.True);
            Assert.That(result.MatchedPattern, Is.EqualTo("meal"));
        }
    }
}
