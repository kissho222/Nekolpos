using System.Threading.Tasks;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationDebugRunnerTests
    {
        [Test]
        public async Task RunAsync_CollectsParseRouteAndEventDebugInformation()
        {
            var parser = new RegexInputParser(new ConversationParseConfig
            {
                intentRules =
                {
                    new RegexIntentRule
                    {
                        pattern = "(たべたい|ごはん)",
                        intent = "request_food",
                        priority = 100
                    }
                },
                categoryRules =
                {
                    new KeywordCategoryRule
                    {
                        word = "さかな",
                        type = "food_category",
                        value = "Fish"
                    }
                }
            });

            var routes = ConversationRouter.ParseRoutesJson(
                "[{\"id\":\"meal\",\"priority\":100,\"conditions\":{\"intent\":\"request_food\"},\"effects\":[{\"type\":\"increment\",\"key\":\"MealRefusalCount\",\"value\":1}],\"events\":[\"go_out\"],\"responses\":[\"{{CAT_NAME}}だ\"]}]");

            var events = ConversationEventExecutor.ParseDefinitionsJson(
                "[{\"id\":\"go_out\",\"sequence\":[{\"event\":\"cat_go_out\"}]}]");

            var state = new ConversationGameState();
            state.SetString("CAT_NAME", "でかねこ");

            var runner = new ConversationDebugRunner(new ConversationRouteCatalog(routes), events);
            var result = await runner.RunAsync(parser.Parse("さかなたべたい"), state, null, true);

            Assert.That(result.parseResult.normalizedInput, Is.EqualTo("さかなたべたい"));
            Assert.That(result.parseResult.matchedRegexRules.Count, Is.EqualTo(1));
            Assert.That(result.routeResult.conversationId, Is.EqualTo("meal"));
            Assert.That(result.routeResult.candidates.Count, Is.EqualTo(1));
            Assert.That(result.eventResult.eventLogs.Count, Is.GreaterThan(0));
            StringAssert.Contains("CatLocation", result.eventResult.nextStateJson);
        }
    }
}
