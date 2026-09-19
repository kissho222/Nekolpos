using System.Linq;
using Nekolpos.Dialogue.UnknownWord;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class RegexInputParserTests
    {
        private RegexInputParser parser;

        [SetUp]
        public void SetUp()
        {
            parser = new RegexInputParser(GiantCatConversationDefaults.CreateConfig());
        }

        [Test]
        public void Parse_FindsFoodIntentAndFishCategory()
        {
            var result = parser.Parse("おさかなたべたい");

            Assert.That(result.PrimaryIntent, Is.Not.Null);
            Assert.That(result.PrimaryIntent.intent, Is.EqualTo("request_food"));
            Assert.That(result.PrimaryIntent.priority, Is.EqualTo(100));
            Assert.That(result.categories.Any(category => category.type == "food_category" && category.value == "Fish"), Is.True);
        }

        [Test]
        public void Parse_NormalizesKatakanaBeforeMatching()
        {
            var result = parser.Parse("オサカナ タベタイ");

            Assert.That(result.input, Is.EqualTo("オサカナ タベタイ"));
            Assert.That(result.normalizedInput, Is.EqualTo("おさかなたべたい"));
            Assert.That(result.intents.Any(intent => intent.intent == "request_food"), Is.True);
            Assert.That(result.categories.Any(category => category.type == "food_category" && category.value == "Fish"), Is.True);
        }

        [TestCase("おなかすいた")]
        [TestCase("オナカスイタ")]
        [TestCase("お腹が空いた")]
        public void Parse_FoodIntentMatchesHiraganaKatakanaAndKanji(string input)
        {
            var result = parser.Parse(input);

            Assert.That(result.intents.Any(intent => intent.intent == "request_food"), Is.True);
        }

        [TestCase("ねむい")]
        [TestCase("ネムイ")]
        [TestCase("眠い")]
        public void Parse_SleepIntentMatchesHiraganaKatakanaAndKanji(string input)
        {
            var result = parser.Parse(input);

            Assert.That(result.intents.Any(intent => intent.intent == "request_sleep"), Is.True);
        }

        [TestCase("かわいい")]
        [TestCase("カワイイ")]
        [TestCase("可愛い")]
        public void Parse_PraiseIntentMatchesHiraganaKatakanaAndKanji(string input)
        {
            var result = parser.Parse(input);

            Assert.That(result.intents.Any(intent => intent.intent == "praise"), Is.True);
        }

        [Test]
        public void Parse_PreservesElongationMarksWhileMatchingPartialGreeting()
        {
            var greetingParser = new RegexInputParser(new ConversationParseConfig
            {
                intentRules =
                {
                    new RegexIntentRule
                    {
                        pattern = "こん(に)?ち(わ|は)",
                        intent = "greeting",
                        priority = 100
                    }
                }
            });

            var result = greetingParser.Parse("こんにちわー");

            Assert.That(result.normalizedInput, Is.EqualTo("こんにちわー"));
            Assert.That(result.intents.Any(intent => intent.intent == "greeting"), Is.True);
        }

        [Test]
        public void Parse_PreservesElongationMarksInDictionaryTokens()
        {
            var result = parser.Parse("サーモンたべたい");

            Assert.That(result.normalizedInput, Is.EqualTo("さーもんたべたい"));
            Assert.That(result.categories.Any(category => category.type == "food_category" && category.value == "Fish"), Is.True);
        }

        [Test]
        public void UnknownWordExtractor_ReturnsOriginalInputForDisplay()
        {
            var inputContext = new PlayerInputContext("ハタハタなべ");
            var parseResult = parser.Parse(inputContext);
            var intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
            var result = new UnknownWordExtractor().Extract(inputContext, intentResult);

            Assert.That(inputContext.NormalizedInput, Is.EqualTo("はたはたなべ"));
            Assert.That(result.HasUnknownWord, Is.True);
            Assert.That(result.NormalizedInput, Is.EqualTo("はたはたなべ"));
            Assert.That(result.UnknownWord, Is.EqualTo("ハタハタなべ"));
        }

        [Test]
        public void UnknownWordExtractor_PreservesMiddleElongationMarkForDisplay()
        {
            // Use an unknown fixture: ラーメン is now a registered food in KnownWords.csv.
            var input = new PlayerInputContext("ナーゾ");
            var result = new UnknownWordExtractor().Extract(
                input,
                DialogueIntentResult.FromConversationParseResult(parser.Parse(input)));

            Assert.That(input.NormalizedInput, Is.EqualTo("なーぞ"));
            Assert.That(result.HasUnknownWord, Is.True);
            Assert.That(result.UnknownWord, Is.EqualTo("ナーゾ"));
        }

        [Test]
        public void UnknownWordExtractor_PreservesTrailingElongationMarkForDisplay()
        {
            var superInput = new PlayerInputContext("スーパー");
            var superResult = new UnknownWordExtractor().Extract(
                superInput,
                DialogueIntentResult.FromConversationParseResult(parser.Parse(superInput)));

            Assert.That(superResult.HasUnknownWord, Is.True);
            Assert.That(superResult.UnknownWord, Is.EqualTo("スーパー"));
        }

        [Test]
        public void UnknownWordExtractor_AcceptsTwoCharacterUnknownWord()
        {
            var inputContext = new PlayerInputContext("単語");
            var parseResult = parser.Parse(inputContext);
            var intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
            var result = new UnknownWordExtractor().Extract(inputContext, intentResult);

            // Unregistered kanji are preserved, rather than guessed by the project dictionary.
            Assert.That(inputContext.NormalizedInput, Is.EqualTo("単語"));
            Assert.That(result.HasUnknownWord, Is.True);
            Assert.That(result.UnknownWord, Is.EqualTo("単語"));
        }

        [Test]
        public void UnknownWordExtractor_DoesNotSplitPawPadIntoMeatAndUnknownSuffix()
        {
            var inputContext = new PlayerInputContext("にくきゅう");
            var parseResult = parser.Parse(inputContext);
            var intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
            var result = new UnknownWordExtractor().Extract(inputContext, intentResult);

            Assert.That(parseResult.matchedCategoryRules.Any(match => match.word == "にくきゅう"), Is.True);
            Assert.That(parseResult.matchedCategoryRules.Any(match => match.word == "にく"), Is.True);
            Assert.That(result.HasUnknownWord, Is.False);
            Assert.That(result.MatchedKnownWords, Does.Contain("にくきゅう"));
        }

        [Test]
        public void UnknownWordExtractor_DoesNotTreatFoodRequestSuffixAsUnknown()
        {
            AssertFoodRequestHasNoUnknownWord("まぐろがたべたいね", "まぐろ");
            AssertFoodRequestHasNoUnknownWord("餌が欲しい", "えさ");
        }

        [Test]
        public void Parse_RetainsMultipleIntentCandidatesSortedByPriority()
        {
            var result = parser.Parse("ごはんもほしいしなでて");

            Assert.That(result.intents.Count, Is.EqualTo(2));
            Assert.That(result.intents[0].intent, Is.EqualTo("request_pet"));
            Assert.That(result.intents[0].priority, Is.EqualTo(120));
            Assert.That(result.intents[1].intent, Is.EqualTo("request_food"));
            Assert.That(result.intents[1].priority, Is.EqualTo(100));
        }

        [Test]
        public void Parse_DeduplicatesCategoriesWithSameTypeAndValue()
        {
            var result = parser.Parse("まぐろとおさかなたべたい");

            Assert.That(result.categories.Count(category => category.type == "food_category" && category.value == "Fish"), Is.EqualTo(1));
        }

        [Test]
        public void ParseToJson_WritesExpectedShape()
        {
            var json = parser.ParseToJson("なでさせて", false);

            StringAssert.Contains("\"input\":\"なでさせて\"", json);
            StringAssert.Contains("\"intents\"", json);
            StringAssert.Contains("\"categories\"", json);
            StringAssert.Contains("\"intent\":\"request_pet\"", json);
        }

        private void AssertFoodRequestHasNoUnknownWord(string rawInput, string expectedKnownWord)
        {
            var inputContext = new PlayerInputContext(rawInput);
            var parseResult = parser.Parse(inputContext);
            var intentResult = DialogueIntentResult.FromConversationParseResult(parseResult);
            var result = new UnknownWordExtractor().Extract(inputContext, intentResult);

            Assert.That(parseResult.intents.Any(intent => intent.intent == "request_food"), Is.True);
            Assert.That(result.HasUnknownWord, Is.False);
            Assert.That(result.MatchedKnownWords, Does.Contain(expectedKnownWord));
        }
    }
}
