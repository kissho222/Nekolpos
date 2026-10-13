using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.Dialogue.UnknownWord;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Nekolpos.EditorTools.DialogueGraph;

namespace Nekolpos.EditorTools.Tests
{
    public sealed class UnknownWordGraphTests
    {
        private static DialogueGraphData Flow()
        { var data = new DialogueGraphData(); DialogueGraphDataProvider.AddUnknownWordFlow(data); return data; }
        private static bool Edge(DialogueGraphData data, string from, string to, string label) => data.Edges.Any(e =>
            e.From == "runtime:unknown:" + from && e.To == "runtime:unknown:" + to && e.Label == label);
        [Test]
        public void InitialFlowRequiresYesThenAdvanceBeforeEditing()
        {
            var data = Flow(); Assert.That(Edge(data, "prompt", "meaning", "はい"), Is.True);
            Assert.That(Edge(data, "meaning", "edit", "次へ"), Is.True);
            Assert.That(Edge(data, "prompt", "return", "いいえ"), Is.True);
            Assert.That(data.Edges.Any(e => e.From == "runtime:unknown:prompt" && e.To == "runtime:unknown:edit"), Is.False);
            Assert.That(data.Nodes["runtime:unknown:edit"].Body, Does.Contain("確認前は記憶しない"));
        }
        [Test]
        public void EveryProjectionNodeHasImplementationAndReachableEndpoints()
        {
            var data = Flow();
            Assert.That(data.Edges.All(e => data.Nodes.ContainsKey(e.From) && data.Nodes.ContainsKey(e.To)), Is.True);
            Assert.That(data.Nodes.Values.All(n => n.Asset != null && n.SuggestedPosition.HasValue && n.Entry.IsRuntimeFlow), Is.True);
            var reachable = new HashSet<string> { "runtime:unknown:entry", "runtime:unknown:memo" };
            for (int i = 0; i < data.Nodes.Count; i++) foreach (var edge in data.Edges) if (reachable.Contains(edge.From)) reachable.Add(edge.To);
            Assert.That(reachable.Count, Is.EqualTo(data.Nodes.Count));
        }
        [Test]
        public void KnownWordAndGreetingCanBeCorrectedWithoutErasingOriginalInput()
        {
            var data = Flow(); Assert.That(Edge(data, "known", "other", "別の言葉"), Is.True);
            Assert.That(data.Nodes["runtime:unknown:other"].Body, Does.Contain("元入力全体"));
            Assert.That(Edge(data, "known", "edit", "教え直す"), Is.True);
            Assert.That(data.Nodes.ContainsKey("runtime:unknown:more"), Is.False);
            Assert.That(data.Nodes.ContainsKey("runtime:unknown:questionSelect"), Is.False);
            Assert.That(Edge(data, "greetingChoice", "greetingForm", "教え直す"), Is.True);
            Assert.That(Edge(data, "greetingChoice", "return", "そのまま"), Is.True);
        }
        [Test]
        public void SaveFailuresAndForgetCancelHaveExplicitSafeRoutes()
        {
            var data = Flow(); Assert.That(Edge(data, "save", "abort", "保存失敗"), Is.True);
            Assert.That(Edge(data, "abort", "return", "強制終了"), Is.True);
            Assert.That(data.Nodes["runtime:unknown:abort"].Body, Does.Contain("ゲームに出さない"));
            Assert.That(Edge(data, "forgetChoice", "forget", "確定"), Is.True);
            Assert.That(Edge(data, "forgetChoice", "memoReturn", "取消"), Is.True);
            Assert.That(data.Nodes["runtime:unknown:forget"].Body, Does.Contain("基本語・Teaching・ログは維持"));
        }
        [Test]
        public void QuestionDetailReadsCurrentPoolAndLoadingDoesNotChangeSave()
        {
            string before = ProgressSaveStore.GetString(LearnedWordStore.SaveKey);
            var data = DialogueGraphDataProvider.Load();
            Assert.That(data.Sources, Contains.Item(DialogueGraphDataProvider.UnknownWordSource));
            foreach (WordGenre genre in Enum.GetValues(typeof(WordGenre)))
                foreach (var q in WordKnowledge.Questions(genre))
                    Assert.That(data.Nodes["runtime:unknown:question"].Detail, Does.Contain(q.Id).And.Contain(q.Prompt));
            Assert.That(ProgressSaveStore.GetString(LearnedWordStore.SaveKey), Is.EqualTo(before));
            var oldEvents = data.Nodes.Values.Where(n => n.Entry != null && n.Entry.ActionId == "unknown_word_teach_event");
            Assert.That(oldEvents.Any(), Is.True); Assert.That(oldEvents.All(n => n.Kind != DialogueGraphNodeKind.TimedEvent), Is.True);
        }
    }

    public sealed class LearnedWordTests
    {
        private string json;
        private LearnedWordStore store;
        private object previousShared;
        private static readonly FieldInfo SharedField = typeof(LearnedWordStore).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
        [SetUp]
        public void SetUp()
        {
            json = ""; store = new LearnedWordStore(() => json, v => json = v);
            previousShared = SharedField.GetValue(null); SharedField.SetValue(null, store); WordVariables.BeginConversation();
        }
        [TearDown]
        public void TearDown() { SharedField.SetValue(null, previousShared); WordVariables.BeginConversation(); }
        private static LearnedWord Word(string display, WordGenre genre = WordGenre.Food) => new LearnedWord { id = Guid.NewGuid().ToString("N"), display = display, genre = genre };
        [Test]
        public void FoodPersistsAndCanAccumulateCorrectAndComplete()
        {
            var word = Word("たらこ"); word.answers["food_sweet"] = WordAnswer.No; store.Put(word);
            var reopened = new LearnedWordStore(() => json, v => json = v);
            var remembered = reopened.Match("たらこ", out bool exact); Assert.That(exact, Is.True); Assert.That(remembered.id, Is.EqualTo(word.id));
            Assert.That(WordKnowledge.Summary(remembered), Does.Contain("甘くないんだよね！"));
            foreach (var q in WordKnowledge.Questions(word.genre)) remembered.answers[q.Id] = WordAnswer.Yes;
            reopened.Put(remembered); Assert.That(WordKnowledge.Next(reopened.Find(word.id)), Is.Null);
            remembered.answers["food_sweet"] = WordAnswer.No; reopened.Put(remembered);
            Assert.That(WordKnowledge.Summary(reopened.Find(word.id)), Does.Contain("甘くなくて"));
        }
        [Test]
        public void PartialMatchDoesNotDestroyNewWordAndExactWins()
        {
            var first = Word("たらこ"); store.Put(first);
            Assert.That(store.Match("たらこスパ", out bool exact).id, Is.EqualTo(first.id)); Assert.That(exact, Is.False);
            var second = Word("たらこスパ"); store.Put(second);
            Assert.That(store.Match("たらこスパ", out exact).id, Is.EqualTo(second.id)); Assert.That(exact, Is.True); Assert.That(store.Words.Count, Is.EqualTo(2));
        }
        [Test]
        public void LatinDisplayAndJapaneseReadingRemainSeparate()
        {
            var word = Word("VR", WordGenre.Work); word.reading = "ぶいあーる"; store.Put(word);
            Assert.That(store.Match("ぶいあーる", out bool exact).display, Is.EqualTo("VR")); Assert.That(exact, Is.True);
            Assert.That(store.Match("ＶＲ", out exact).reading, Is.EqualTo("ぶいあーる")); Assert.That(exact, Is.True);
        }
        [TestCase("ちげ", false)][TestCase("チゲ", false)][TestCase("ﾁｹﾞ", false)][TestCase("スーパー", false)]
        [TestCase("猫", true)][TestCase("VR", true)][TestCase("ねこ1", true)][TestCase("ねこ！", true)]
        public void ReadingPredicateDistinguishesKanaFromEveryOtherCharacter(string input, bool required)
        { Assert.That(LearnedWordStore.RequiresReading(input), Is.EqualTo(required)); }
        [TestCase("")][TestCase("  ")][TestCase("a\nb")]
        public void InvalidInputCannotMutateDictionary(string input)
        {
            var w = Word(input); Assert.Throws<ArgumentException>(() => store.Put(w)); Assert.That(json, Is.Empty); Assert.That(store.Words, Is.Empty);
        }
        [Test]
        public void MissingReadingAndOversizeInputAreRejected()
        {
            Assert.Throws<ArgumentException>(() => store.Put(Word("VR")));
            Assert.Throws<ArgumentException>(() => store.Put(Word(new string('猫', 65))));
        }
        [Test]
        public void DuplicateSpellingOrAliasIsRejectedWithoutChangingSave()
        {
            store.Put(Word("たらこ")); string before = json;
            Assert.Throws<ArgumentException>(() => store.Put(Word("タラコ")));
            var w = Word("別語"); w.aliases.Add("たらこ"); Assert.Throws<ArgumentException>(() => store.Put(w)); Assert.That(json, Is.EqualTo(before));
        }
        [Test]
        public void BrokenSaveIsRetainedAndCannotBeOverwritten()
        {
            string broken = "{broken";
            LogAssert.Expect(LogType.Error, new Regex("\\[LearnedWords\\] 保存辞書を読み込めません"));
            var invalid = new LearnedWordStore(() => broken, v => broken = v);
            Assert.Throws<InvalidOperationException>(() => invalid.Put(Word("猫"))); Assert.That(broken, Is.EqualTo("{broken"));
        }
        [Test]
        public void SaveFailureDoesNotCommitInMemory()
        {
            var failing = new LearnedWordStore(() => "", _ => throw new InvalidOperationException("disk failure"));
            Assert.Throws<InvalidOperationException>(() => failing.Put(Word("たらこ"))); Assert.That(failing.Words, Is.Empty);
        }
        [Test]
        public void UnknownAnswerIsCompletedAndGenreChangeKeepsHistoricalAnswers()
        {
            var w = Word("たらこ"); w.answers["food_sweet"] = WordAnswer.Unknown;
            Assert.That(WordKnowledge.Next(w).Id, Is.EqualTo("food_big")); w.genre = WordGenre.Work; store.Put(w);
            Assert.That(store.Find(w.id).answers.ContainsKey("food_sweet"), Is.True);
            Assert.That(WordKnowledge.Summary(w), Does.Not.Contain("甘"));
        }
        [Test]
        public void AllUnknownAnswersPersistCompleteWithoutAddingFactsToSummary()
        {
            var word = Word("たらこ");
            foreach (var q in WordKnowledge.Questions(word.genre)) word.answers[q.Id] = WordAnswer.Unknown;
            store.Put(word); var reopened = new LearnedWordStore(() => json, _ => {}); var saved = reopened.Find(word.id);
            Assert.That(WordKnowledge.Next(saved), Is.Null);
            Assert.That(WordKnowledge.Summary(saved), Is.EqualTo(WordKnowledge.Summary(Word("たらこ"))));
            Assert.That((int)saved.answers["food_sweet"], Is.Zero);
        }
        [Test]
        public void RandomQuestionsMixGeneralAndSpecificAndExcludeAllAnsweredValues()
        {
            var previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(731);
                var word = Word("たらこ"); var selected = new HashSet<string>();
                for (int i = 0; i < 128; i++) selected.Add(WordKnowledge.RandomQuestion(word).Id);
                Assert.That(selected, Is.EquivalentTo(WordKnowledge.Questions(word.genre).Select(q => q.Id)));
                word.answers["food_sweet"] = WordAnswer.Yes; word.answers["food_big"] = WordAnswer.No; word.answers["food_cat"] = WordAnswer.Unknown;
                for (int i = 0; i < 64; i++) Assert.That(word.answers.ContainsKey(WordKnowledge.RandomQuestion(word).Id), Is.False);
            }
            finally { UnityEngine.Random.state = previous; }
        }
        [Test]
        public void RandomQuestionUsesLastUnansweredAndCompletedReteachingUsesExistingPool()
        {
            var previous = UnityEngine.Random.state;
            try
            {
                var word = Word("たらこ"); var pool = WordKnowledge.Questions(word.genre);
                foreach (var q in pool.Skip(1)) word.answers[q.Id] = WordAnswer.Unknown;
                Assert.That(WordKnowledge.RandomQuestion(word).Id, Is.EqualTo(pool[0].Id));
                word.answers[pool[0].Id] = WordAnswer.Unknown;
                Assert.That(WordKnowledge.Next(word), Is.Null);
                Assert.That(pool.Select(q => q.Id), Does.Contain(WordKnowledge.RandomQuestion(word).Id));
            }
            finally { UnityEngine.Random.state = previous; }
        }
        [Test]
        public void GreetingAxesUseOrWithinAxisAndAndBetweenAxes()
        {
            var w = Word("グッドモーニング", WordGenre.Greeting); w.times = GreetingTimes.Morning | GreetingTimes.Evening; w.scenes = GreetingScenes.Meeting;
            Assert.That(WordKnowledge.MatchesGreeting(w, DayPeriod.Morning, GreetingScenes.Meeting), Is.True);
            Assert.That(WordKnowledge.MatchesGreeting(w, DayPeriod.Evening, GreetingScenes.Meeting), Is.True);
            Assert.That(WordKnowledge.MatchesGreeting(w, DayPeriod.Afternoon, GreetingScenes.Meeting), Is.False);
            Assert.That(WordKnowledge.MatchesGreeting(w, DayPeriod.Morning, GreetingScenes.None), Is.False);
            store.Put(w); Assert.That(store.Match("グッドモーニング", out _), Is.Not.Null);
        }
        [Test]
        public void GreetingHasValueRequiresLearnedWordsAndCurrentConditions()
        {
            var state = new ConversationGameState(); state.SetString("Phase", "Morning");
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "HasValue:random_greeting_morning && Morning"), Is.False);
            var w = Word("グッドモーニング", WordGenre.Greeting); w.times = GreetingTimes.Morning; store.Put(w);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "HasValue:random_greeting_morning && Morning"), Is.True);
            state.SetString("Phase", "Day"); Assert.That(WordVariables.HasValue("random_greeting_morning", state), Is.False);
            w.scenes = GreetingScenes.Meeting; store.Put(w); state.SetString("Phase", "Morning"); Assert.That(WordVariables.HasValue("random_greeting_morning", state), Is.False);
            state.SetString("GreetingScene", "Meeting"); Assert.That(WordVariables.HasValue("random_greeting_morning", state), Is.True);
        }
        [TestCase("random_food")][TestCase("random_animal")][TestCase("random_person")][TestCase("random_place")][TestCase("random_object")][TestCase("random_work")][TestCase("random_action")][TestCase("random_other")][TestCase("random_word")]
        public void DefaultsExistWithoutLearning(string variable)
        { Assert.That(WordVariables.HasValue(variable), Is.True); Assert.That(WordVariables.Resolve(variable), Is.Not.Empty); }
        [Test]
        public void SameConversationKeepsVariableAndForgetInvalidatesItImmediately()
        {
            var w = Word("グッドモーニング", WordGenre.Greeting); var state = new ConversationGameState(); state.SetString("Phase", "Morning"); store.Put(w);
            string text = ConversationVariableResolver.Resolve("{{random_greeting_morning}}・{{random_greeting_morning}}", state);
            Assert.That(text, Is.EqualTo("グッドモーニング・グッドモーニング")); store.Forget(w.id);
            Assert.That(WordVariables.HasValue("random_greeting_morning", state), Is.False);
            Assert.That(WordVariables.Resolve("random_greeting_morning", state), Is.Empty);
            Assert.That(WordVariables.CanResolve("{{random_greeting_morning}}", state), Is.False);
        }
        [Test]
        public void ForgetDeletesOnlyRequestedIdAndPersists()
        {
            var first = Word("たらこ"); var second = Word("たらこスパ"); store.Put(first); store.Put(second); store.Forget(first.id);
            var reopened = new LearnedWordStore(() => json, _ => {});
            Assert.That(reopened.Words.Single().id, Is.EqualTo(second.id)); Assert.That(reopened.Match("たらこ", out _), Is.Null);
        }
        [Test]
        public void SnapshotsAndStagedEditsCannotMutateSavedWord()
        {
            var w = Word("たらこ"); store.Put(w); w.display = "未確定";
            var staged = store.Find(w.id); staged.answers["food_sweet"] = WordAnswer.Yes;
            Assert.That(store.Find(w.id).display, Is.EqualTo("たらこ")); Assert.That(store.Find(w.id).answers, Is.Empty);
        }
        [Test]
        public void RichTextAndPlaceholderSyntaxAreSafeWhenQuoted()
        {
            Assert.That(LearnedWordStore.SafeText("<size=0>たらこ</size>{{CAT_NAME}}"), Does.Not.Contain("<").And.Not.Contain("{"));
        }
        [Test]
        public void AuthoredViewsHaveRequiredReferencesAndButtons()
        {
            var learning = AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.LearningPrefab).GetComponent<WordLearningController>();
            Assert.That(learning.form.activeSelf, Is.False); Assert.That(learning.wordInput, Is.Not.Null); Assert.That(learning.readingInput, Is.Not.Null);
            Assert.That(learning.timeToggles.Length, Is.EqualTo(4)); Assert.That(learning.sceneToggles.Length, Is.EqualTo(2)); Assert.That(learning.cancelButton, Is.Not.Null);
            Assert.That(learning.genreSection.transform.Find("QuestionSelector"), Is.Null);
            var memo = AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.MemoPrefab).GetComponent<WordMemoController>();
            Assert.That(memo.rowPrefab, Is.Not.Null); Assert.That(memo.content, Is.Not.Null); Assert.That(memo.forgetButton, Is.Not.Null);
        }
        [Test]
        public void LearnedFoodIsFavoredOverDefaults()
        {
            var random = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(71); store.Put(Word("たらこ")); int learned = 0;
                for (int i = 0; i < 300; i++) { WordVariables.BeginConversation(); if (WordVariables.Resolve("random_food") == "たらこ") learned++; }
                Assert.That(learned, Is.GreaterThan(150));
            }
            finally { UnityEngine.Random.state = random; }
        }
        [Test]
        public void ExistingRegexResponseWinsOverLearnedWordFallback()
        {
            var host = new GameObject("Learned priority test"); host.SetActive(false);
            try
            {
                store.Put(Word("たらこ")); var engine = host.AddComponent<DialogueEngine>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DialogueEngine).GetMethod("InitializeUnknownWordSystem", flags).Invoke(engine, null);
                string csv = "id,regex_id,regex_jp,Pattern,order,output_ja,response_type\n71,test,^たらこ$,1,1,既存応答,Normal\n";
                Assert.That((bool)typeof(DialogueEngine).GetMethod("TryLoadConversationCsvText", flags).Invoke(engine, new object[] { csv, "DialoguePreview" }), Is.True);
                List<Nekolpos.Data.DialogueReactionData> received = null; engine.OnDialogueDetermined += group => received = group;
                engine.ProcessInput("たらこ"); Assert.That(received, Is.Not.Empty); Assert.That(received[0].TextJP, Is.EqualTo("既存応答"));
                engine.ProcessInput("たらこスパ"); Assert.That(received[0].ReactionType, Is.EqualTo("UnknownWord"));
                Assert.That(received[0].RuntimePlaceholders["WORD_ORIGINAL_INPUT"], Is.EqualTo("たらこスパ"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
    public sealed class WordKnowledgeCsvTests
    {
        private static string Source => global::System.IO.File.ReadAllText(WordKnowledge.SourcePath);
        private static LearnedWord Word() => new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "たらこ", genre = WordGenre.Food };
        [Test]
        public void ZeroAndSingleAnswersUseAuthoredFinalForms()
        {
            var data = WordKnowledge.ParseCsv(Source); var word = Word();
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物なんだよね！"));
            word.answers["food_sweet"] = WordAnswer.Yes;
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物で、甘いんだよね！"));
            word.answers["food_sweet"] = WordAnswer.No;
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物で、甘くないんだよね！"));
            word.answers["food_sweet"] = WordAnswer.Unknown;
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物なんだよね！"));
        }
        [Test]
        public void MultipleAnswersUseOrderedMiddleAndOnlyLastFinal()
        {
            var data = WordKnowledge.ParseCsv(Source); var word = Word();
            word.answers["other_heart"] = WordAnswer.Yes; word.answers["food_big"] = WordAnswer.No; word.answers["food_sweet"] = WordAnswer.No;
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物で、甘くなくて、大きくなくて、胸キュンなんだよね！"));
            foreach (var q in data.Questions(WordGenre.Food)) word.answers[q.Id] = WordAnswer.Yes;
            Assert.That(data.Summary(word), Is.EqualTo("たらこは食べ物で、甘くて、大きくて、猫にも食べられて、好きで、楽しくて、胸キュンなんだよね！"));
        }
        [Test]
        public void EditingOneCsvFragmentOnlyChangesItsSummary()
        {
            var original = WordKnowledge.ParseCsv(Source);
            var changed = WordKnowledge.ParseCsv(Source.Replace("甘いんだよね！", "甘いと教わったよ！"));
            var word = Word(); word.answers["food_sweet"] = WordAnswer.Yes;
            Assert.That(changed.Summary(word), Is.EqualTo("たらこは食べ物で、甘いと教わったよ！"));
            word.answers["food_sweet"] = WordAnswer.No;
            Assert.That(changed.Summary(word), Is.EqualTo(original.Summary(word)));
        }
        [Test]
        public void CommonQuestionCanBeAddedWithoutChangingExistingIds()
        {
            var data = WordKnowledge.ParseCsv(Source + "\nquestion,Common,common_test,100,,,,テスト？,途中で,最後だよ！,途中ではなくて,最後ではないよ！\n");
            Assert.That(data.Questions(WordGenre.Food).Any(q => q.Id == "common_test"), Is.True);
            Assert.That(data.Questions(WordGenre.Greeting).Any(q => q.Id == "common_test"), Is.True);
            Assert.That(data.Questions(WordGenre.Food).First().Id, Is.EqualTo("food_sweet"));
        }
        [TestCase("food_sweet,10", "food_big,10")]
        [TestCase("question,Food", "question,Invalid")]
        [TestCase("甘くて", "")]
        [TestCase("甘くて", "甘くて、")]
        [TestCase("food_big,20", "food_big,-1")]
        [TestCase("question_id,", "missing_id,")]
        public void InvalidDefinitionsAreRejected(string before, string after)
        { Assert.Throws<FormatException>(() => WordKnowledge.ParseCsv(Source.Replace(before, after))); }
        [Test]
        public void DuplicateOrderKeepsCsvRowOrderWithoutPreventingLearning()
        {
            var data = WordKnowledge.ParseCsv(Source.Replace("food_big,20", "food_big,10"));
            Assert.That(data.Questions(WordGenre.Food).Take(2).Select(q => q.Id), Is.EqualTo(new[] { "food_sweet", "food_big" }));
        }
        [Test]
        public void EscapedQuotesAreReadAndBrokenQuotingRejected()
        {
            var data = WordKnowledge.ParseCsv("\uFEFF" + Source.Replace("row_type", "\"row_type\"").Replace("甘い？", "\"\"\"甘い\"\"？\""));
            Assert.That(data.Questions(WordGenre.Food).First().Prompt, Is.EqualTo("\"甘い\"？"));
            Assert.Throws<FormatException>(() => WordKnowledge.ParseCsv(Source + "\n\"unclosed"));
        }
        [Test]
        public void CompiledResourceHasSameDefinitionsAndIsNotConversationRoutes()
        {
            Assert.That(ConversationDataManager.TryLoadEncryptedCsvResource(WordKnowledge.ResourcePath, out string compiled, out string error), Is.True, error);
            Assert.That(compiled, Is.EqualTo(Source));
            Assert.That(WordKnowledge.ParseCsv(compiled).Questions(WordGenre.Food).Select(q => q.Id), Is.EqualTo(WordKnowledge.Questions(WordGenre.Food).Select(q => q.Id)));
            var exclude = typeof(ConversationDataManager).GetMethod("IsStandaloneCatalogAssetName", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)exclude.Invoke(null, new object[] { "WordLearningQuestions" }), Is.True);
        }
        [Test]
        public void GeneralAppliesToEveryGenreAndNewWordGenreKeepsExistingNumbers()
        {
            foreach (WordGenre genre in Enum.GetValues(typeof(WordGenre)))
                Assert.That(WordKnowledge.Questions(genre).Select(q => q.Id), Does.Contain("other_like").And.Contain("other_fun").And.Contain("other_heart"));
            Assert.That((int)WordGenre.Other, Is.EqualTo(8)); Assert.That((int)WordGenre.Word, Is.EqualTo(9));
            Assert.That(WordKnowledge.GenreLabels[9], Is.EqualTo("有名な言葉"));
            var word = Word(); word.genre = WordGenre.Word;
            string json = ""; var store = new LearnedWordStore(() => json, value => json = value); store.Put(word);
            Assert.That(new LearnedWordStore(() => json, _ => {}).Find(word.id).genre, Is.EqualTo(WordGenre.Word));
        }
        [Test]
        public void ExistingQuestionIdsAndSaveAnswersRemainCompatible()
        {
            var expected = new[] { "food_sweet", "food_big", "food_cat", "food_heart", "animal_big", "animal_cute", "animal_fly", "person_kind", "person_cool", "person_like", "place_near", "place_fun", "place_big", "object_big", "object_useful", "object_soft", "work_fun", "work_cute", "work_heart", "action_fun", "action_together", "action_tiring", "greeting_like", "greeting_fun", "greeting_cool", "greeting_cute", "other_like", "other_fun", "other_heart" };
            Assert.That(WordKnowledge.AllQuestionIds, Is.SupersetOf(expected));
            var word = Word(); word.answers["food_sweet"] = WordAnswer.No; word.answers["work_cute"] = WordAnswer.Yes;
            string json = ""; var store = new LearnedWordStore(() => json, value => json = value); store.Put(word);
            var reopened = new LearnedWordStore(() => json, _ => {});
            Assert.That(reopened.Find(word.id).answers, Is.EquivalentTo(word.answers));
            Assert.That(WordKnowledge.Next(reopened.Find(word.id)).Id, Is.EqualTo("food_big"));
        }
    }

    public sealed class WordLearningDialogueCsvTests
    {
        private static string Source => global::System.IO.File.ReadAllText(WordLearningDialogue.SourcePath);
        [Test]
        public void ExistingLearningLinesAreLoadedWithoutChangingWording()
        {
            var data = WordLearningDialogue.ParseCsv(Source);
            Assert.That(WordLearningDialogue.Format("unknown_prompt", "たらこ", definition: data), Is.EqualTo("たらこって何？もしかして言葉を教えてくれるの？"));
            Assert.That(WordLearningDialogue.Format("meaning_prompt", "たらこ", definition: data), Is.EqualTo("じゃあたらこってどういう意味？"));
            Assert.That(WordLearningDialogue.Format("saved_complete", "たらこ", "要約。", definition: data), Does.Contain("たらこマスター").And.Contain("要約。"));
            Assert.That(WordLearningDialogue.Format("question", "たらこ", question: "甘い？", definition: data), Is.EqualTo("たらこは甘い？"));
        }
        [Test]
        public void EditingCsvChangesOnlySelectedLineAndInsertedWordIsLiteral()
        {
            var data = WordLearningDialogue.ParseCsv(Source.Replace("じゃあ{word}ってどういう意味？", "{word}の意味は？"));
            Assert.That(WordLearningDialogue.Format("meaning_prompt", "たらこ", definition: data), Is.EqualTo("たらこの意味は？"));
            Assert.That(WordLearningDialogue.Format("greeting_match", "こんにちは", definition: data), Is.EqualTo("こんにちは！ばっちり覚えたからね！"));
            Assert.That(WordLearningDialogue.Format("meaning_prompt", "{summary}<test>", definition: data), Is.EqualTo("｛summary｝‹test›の意味は？"));
        }
        [TestCase("unknown_prompt,", "meaning_prompt,")]
        [TestCase("{word}って何？もしかして言葉を教えてくれるの？", "")]
        [TestCase("{word}", "{typo}")]
        [TestCase("key,ja", "key,missing")]
        public void InvalidDialogueCsvIsRejected(string before, string after)
        { Assert.Throws<FormatException>(() => WordLearningDialogue.ParseCsv(Source.Replace(before, after))); }
        [Test]
        public void CompiledDialogueResourceMatchesSourceAndIsExcludedFromRoutes()
        {
            Assert.That(ConversationDataManager.TryLoadEncryptedCsvResource(WordLearningDialogue.ResourcePath, out string compiled, out string error), Is.True, error);
            Assert.That(compiled, Is.EqualTo(Source)); Assert.That(WordLearningDialogue.ParseCsv(compiled).Count, Is.EqualTo(WordLearningDialogue.ParseCsv(Source).Count));
            var exclude = typeof(ConversationDataManager).GetMethod("IsStandaloneCatalogAssetName", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)exclude.Invoke(null, new object[] { "WordLearningDialogue" }), Is.True);
        }
    }

}
