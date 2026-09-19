using System.Collections.Generic;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationDataLoaderTests
    {
        [Test]
        public void LoadFromJsonText_BuildsCatalogAndSupportsSearch()
        {
            var result = ConversationDataLoader.LoadFromJsonText(
                "[\n" +
                "  {\n" +
                "    \"id\": \"meal_refusal_1\",\n" +
                "    \"priority\": 100,\n" +
                "    \"conditions\": {\n" +
                "      \"intent\": \"request_food\",\n" +
                "      \"phase\": \"Lunch\"\n" +
                "    },\n" +
                "    \"responses\": [\"{{CAT_NAME}}は食べたくない\"],\n" +
                "    \"events\": [\"cat_eat_alone\"]\n" +
                "  }\n" +
                "]",
                "routes.json",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.TryGetById("meal_refusal_1", out var route), Is.True);
            Assert.That(route.priority, Is.EqualTo(100));
            Assert.That(result.catalog.FindByIntent("request_food").Count, Is.EqualTo(1));
            Assert.That(result.catalog.FindByTag("Lunch").Count, Is.EqualTo(0));

            var state = new ConversationGameState();
            state.SetString("phase", "Lunch");
            var filtered = result.catalog.Filter(new ConversationRouteCatalogFilter
            {
                intent = "request_food",
                gameState = state
            });

            Assert.That(filtered.Count, Is.EqualTo(1));
        }

        [Test]
        public void LoadFromCsvText_SupportsQuotedNewlinesAndJsonArrays()
        {
            var csv =
                "id,priority,intents,tags,responses,effects,events,phase\n" +
                "meal_refusal_1,100,request_food,Lunch,\"1行目\n2行目\",\"[{\"\"type\"\":\"\"increment\"\",\"\"key\"\":\"\"MealRefusalCount\"\",\"\"value\"\":1}]\",cat_eat_alone,Lunch\n";

            var result = ConversationDataLoader.LoadFromCsvText(
                csv,
                "routes.csv",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.TryGetById("meal_refusal_1", out var route), Is.True);
            Assert.That(route.responses[0], Is.EqualTo("1行目\n2行目"));
            Assert.That(route.effects.Count, Is.EqualTo(1));
            Assert.That(route.stateConditions, Does.Contain("phase == \"Lunch\""));
        }

        [TestCase("responses")]
        [TestCase("responses_ja")]
        [TestCase("response")]
        public void CsvResponse_PreservesParagraphsButStillSupportsExplicitAlternatives(string column)
        {
            var result = ConversationDataLoader.LoadFromCsvText(
                "id," + column + "\nrow,\"A\nB|C\"\n", "paragraph.csv",
                new ConversationDataLoadOptions { logErrorsToConsole = false, locale = "ja" });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.Routes[0].responses, Is.EqualTo(new[] { "A\nB", "C" }));
        }

        [Test]
        public void CsvResponse_RejectsMalformedJsonArray()
        {
            var result = ConversationDataLoader.LoadFromCsvText("id,responses\nrow,[invalid\n", "invalid.csv",
                new ConversationDataLoadOptions { logErrorsToConsole = false });
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.errors, Is.Not.Empty);
        }

        [Test]
        public void LoadFromJsonText_DetectsDuplicateIdAndInvalidEnumValue()
        {
            var result = ConversationDataLoader.LoadFromJsonText(
                "[\n" +
                "  {\n" +
                "    \"id\": \"dup_route\",\n" +
                "    \"conditions\": { \"KnownFoodCategories\": [\"DragonFruit\"] },\n" +
                "    \"responses\": [\"a\"]\n" +
                "  },\n" +
                "  {\n" +
                "    \"id\": \"dup_route\",\n" +
                "    \"responses\": [\"b\"]\n" +
                "  }\n" +
                "]",
                "invalid_routes.json",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.errors.Count, Is.EqualTo(2));
            StringAssert.Contains("Invalid enum value 'DragonFruit'", result.errors[0].message);
            StringAssert.Contains("Duplicate route id 'dup_route'", result.errors[1].message);
        }

        [Test]
        public void ResolveResponse_ReplacesVariablesFromGameState()
        {
            var loadResult = ConversationDataLoader.LoadFromJsonText(
                "[\n" +
                "  {\n" +
                "    \"id\": \"call_player\",\n" +
                "    \"priority\": 100,\n" +
                "    \"conditions\": { \"intent\": \"greet\" },\n" +
                "    \"responses\": [\"{{PLAYER_CALLING}}、{{CAT_NAME}}だよ\"],\n" +
                "    \"events\": [\"play_se\"]\n" +
                "  }\n" +
                "]",
                "routes.json",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            var router = new ConversationRouter(loadResult.catalog);
            var state = new ConversationGameState();
            state.SetString("PLAYER_CALLING", "ごしゅじん");
            state.SetString("CAT_NAME", "でかねこ");

            var result = router.Route(new ConversationRouteRequest
            {
                parseResult = new ConversationParseResult
                {
                    intents = new List<IntentMatch>
                    {
                        new()
                        {
                            intent = "greet",
                            priority = 1
                        }
                    }
                },
                gameState = state
            });

            Assert.That(result.response, Is.EqualTo("ごしゅじん、でかねこだよ"));
        }

        [Test]
        public void VariableResolver_TrimsCatPronounInSentence()
        {
            var state = new ConversationGameState();
            state.SetString("CAT_PRONOUN", " 私 ");

            string resolved = ConversationVariableResolver.Resolve("{{CAT_PRONOUN}}はねこだよ", state);

            Assert.That(resolved, Is.EqualTo("私はねこだよ"));
        }

        [Test]
        public void LoadFromCsvText_DialoguePreviewFormat_MapsLatestColumns()
        {
            var csv =
                "id,input,regex_jp,regex_zh,regex_en,output_ja,output_zh,output_en,alt,regex_id,pattern,order,response_type,action_id,condition,SpeechControl,emotion_change_type,emotion_change_value,WaitTime,HideUI,Animation,Priority,call_only,choice_yes_pattern,choice_no_pattern\n" +
                "18,ねこじゃない,(ねこじゃない),,,{{CAT\\_PRONOUN}}はねこだよ,,,,not\\_cat,5,6,Action,Punch,,Sequence,hostility,1,0,false,接近,10,false,,\n";

            var result = ConversationDataLoader.LoadFromCsvText(
                csv,
                "DialoguePreview.csv",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.Routes.Count, Is.EqualTo(1));
            var route = result.catalog.Routes[0];
            Assert.That(route.id, Is.EqualTo("dlg_18_not_cat_p5_o6_action_punch"));
            Assert.That(route.intents, Is.EqualTo(new[] { "not_cat" }));
            Assert.That(route.responses, Is.EqualTo(new[] { "{{CAT_PRONOUN}}はねこだよ" }));
            Assert.That(route.events, Is.EqualTo(new[] { "Punch" }));
            Assert.That(route.effects.Count, Is.EqualTo(1));
            Assert.That(route.effects[0].key, Is.EqualTo("Hostility"));
            Assert.That(route.metadata.responseType, Is.EqualTo("Action"));
            Assert.That(route.metadata.animation, Is.EqualTo("接近"));
        }

        [Test]
        public void LoadFromCsvText_DialoguePreviewAction_UsesTimelineSequenceAnimationAsEvent()
        {
            var csv =
                "id,input,regex_jp,regex_zh,regex_en,output_ja,output_zh,output_en,alt,regex_id,pattern,order,response_type,action_id,condition,SpeechControl,emotion_change_type,emotion_change_value,WaitTime,HideUI,Animation,Priority,call_only,choice_yes_pattern,choice_no_pattern\n" +
                "350,タブレット,(たぶれっと),,,,,phone,2,2,Action,,,Random,,0,0,true,timeline_sequence:MoveStartToDeskStart,40,true,,,\n";

            var result = ConversationDataLoader.LoadFromCsvText(
                csv,
                "DialoguePreview.csv",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.Routes, Has.Count.EqualTo(1));
            Assert.That(result.catalog.Routes[0].events, Is.EqualTo(new[] { "timeline_sequence:MoveStartToDeskStart" }));
            Assert.That(result.catalog.Routes[0].metadata.actionId, Is.EqualTo("timeline_sequence:MoveStartToDeskStart"));
        }

        [Test]
        public void LoadFromCsvText_DialoguePreviewFormat_MapsLegacyColumnsAndGeneratesUniqueIds()
        {
            var csv =
                "regex,regex_cn,regex_en,Order,Pattern,RepeatCount ,SpeechControl,response_type,id,intent,reaction_type,regex ID,text_jp 1,text_cn,text_en,condition,emotion_change_type,emotion_change_value,WaitTime,HideUI,Animation,Priority\n" +
                "(あそ),(玩),(play),0,1,0,,Normal,1,INTENT_AFFECTION,REACT_CHILD_NEUTRAL,RE_INTENT_AFFECTION_001,いいよ,好呀,Sure,,,0,0,false,,20\n" +
                "(あそ),(玩),(play),1,1,0,,Normal,1,INTENT_AFFECTION,REACT_CHILD_NEUTRAL,RE_INTENT_AFFECTION_001,なにしようか,做什么呢,What shall we do?,,,0,0,false,,20\n";

            var result = ConversationDataLoader.LoadFromCsvText(
                csv,
                "LegacyPreview.csv",
                new ConversationDataLoadOptions
                {
                    logErrorsToConsole = false
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.catalog.Routes.Count, Is.EqualTo(2));
            Assert.That(result.catalog.Routes[0].id, Is.EqualTo("dlg_1_re_intent_affection_001_p1_o0_normal_react_child_neutral"));
            Assert.That(result.catalog.Routes[1].id, Is.EqualTo("dlg_1_re_intent_affection_001_p1_o1_normal_react_child_neutral"));
            Assert.That(result.catalog.Routes[0].responses, Is.EqualTo(new[] { "いいよ" }));
            Assert.That(result.catalog.Routes[0].events, Is.EqualTo(new[] { "REACT_CHILD_NEUTRAL" }));
        }
    }
}
