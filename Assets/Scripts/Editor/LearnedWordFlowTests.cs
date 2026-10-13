using System;
using System.Linq;
using System.Reflection;
using Nekolpos.Dialogue.UnknownWord;
using Nekolpos.System;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools.Tests
{
    public sealed class LearnedWordFlowTests
    {
        private GameObject root;
        private ChatUIController chat;
        private WordLearningController learning;
        private LearnedWordStore store;
        private object oldStore;
        private bool completed;
        private static readonly FieldInfo StoreField = typeof(LearnedWordStore).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
        [SetUp]
        public void SetUp()
        {
            oldStore = StoreField.GetValue(null); store = new LearnedWordStore(() => "", _ => {}); StoreField.SetValue(null, store);
            root = new GameObject("Learning flow tests", typeof(RectTransform), typeof(Canvas)); root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            chat = root.AddComponent<ChatUIController>();
            var window = new GameObject("Window", typeof(RectTransform), typeof(CanvasGroup)); window.transform.SetParent(root.transform, false); chat.chatWindowGroup = window.GetComponent<CanvasGroup>();
            chat.messageText = MakeText(window.transform); chat.speakerNameText = MakeText(window.transform);
            chat.choicePanel = new GameObject("Choices", typeof(RectTransform)); chat.choicePanel.transform.SetParent(root.transform, false);
            chat.choiceAcceptButton = MakeButton(chat.choicePanel.transform); chat.choiceDeclineButton = MakeButton(chat.choicePanel.transform);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.LearningPrefab);
            learning = Object.Instantiate(prefab, root.transform).GetComponent<WordLearningController>(); learning.Initialize(); completed = false;
        }
        private static TextMeshProUGUI MakeText(Transform parent)
        { var g = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); g.transform.SetParent(parent, false); return g.GetComponent<TextMeshProUGUI>(); }
        private static Button MakeButton(Transform parent)
        { var g = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources()); g.transform.SetParent(parent, false); return g.GetComponent<Button>(); }
        [TearDown]
        public void TearDown() { Object.DestroyImmediate(root); StoreField.SetValue(null, oldStore); WordVariables.BeginConversation(); }
        private void Begin(string input, string extracted)
        { learning.Begin(input, extracted, () => completed = true, chat); FinishSpeech(); }
        private void FinishSpeech()
        {
            if (!chat.IsTyping) return;
            string text = (string)typeof(ChatUIController).GetField("currentFullMessage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chat);
            chat.ShowMessageInstant("{{CAT_NAME}}", text);
        }
        private void Choose(string label)
        {
            FinishSpeech();
            var buttons = chat.choicePanel.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeSelf);
            var button = buttons.FirstOrDefault(b => b.GetComponentsInChildren<TMP_Text>().Any(t => t.text.EndsWith(label, StringComparison.Ordinal)));
            Assert.That(button, Is.Not.Null, "Missing choice: " + label);
            // A deliberate new click follows the frame in which this choice appeared.
            typeof(ChatUIController).GetField("choiceOpenedFrame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(chat, -1);
            button.onClick.Invoke();
            FinishSpeech();
        }
        private void Teach()
        {
            Choose("はい");
            Assert.That(learning.form.activeSelf, Is.False);
            Assert.That(chat.messageText.text, Does.StartWith("じゃあ").And.EndWith("ってどういう意味？"));
            ((Action)typeof(ChatUIController).GetField("OnWaitInputCompleted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chat))?.Invoke();
            Assert.That(learning.form.activeSelf, Is.True);
        }
        private void Confirm()
        {
            // Existing flow checks represent a new gesture after the transition lock.
            typeof(WordLearningController).GetField("genreClickReadyAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(learning, -1f);
            learning.confirmButton.onClick.Invoke();
        }
        [Test]
        public void LearningReenablesControllerSoGenreLockCanExpire()
        {
            learning.enabled = false;
            Begin("たらこ", "たらこ");
            Assert.That(learning.enabled, Is.True);
            Teach(); learning.confirmButton.onClick.Invoke();
            typeof(WordLearningController).GetField("genreClickReadyAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(learning, Time.unscaledTime - .01f);
            typeof(WordLearningController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(learning, null);
            Assert.That(learning.form.GetComponent<CanvasGroup>().interactable, Is.True);
            Assert.That(learning.genreInput.IsInteractable(), Is.True);
        }
        [Test]
        public void FailedInitializationCanRetryAfterCsvIsCorrected()
        {
            var field = typeof(WordLearningDialogue).GetField("lines", BindingFlags.NonPublic | BindingFlags.Static);
            object previous = field.GetValue(null);
            var data = WordLearningDialogue.ParseCsv(global::System.IO.File.ReadAllText(WordLearningDialogue.SourcePath)).ToDictionary(k => k.Key, k => k.Value);
            data.Remove("button_confirm"); GameObject view = null;
            try
            {
                field.SetValue(null, data);
                view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.LearningPrefab), root.transform);
                var controller = view.GetComponent<WordLearningController>();
                Assert.Throws<FormatException>(() => controller.Initialize());
                Assert.That(typeof(WordLearningController).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller), Is.False);
                field.SetValue(null, previous); controller.Initialize();
                Assert.That(controller.genreInput.options.Count, Is.EqualTo(WordKnowledge.GenreLabels.Length));
            }
            finally { if (view != null) Object.DestroyImmediate(view); field.SetValue(null, previous); }
        }
        [Test]
        public void GenreEntryRejectsImmediateCancelAndConfirmationThenUnlocks()
        {
            Begin("たらこ", "たらこ"); Teach();
            learning.confirmButton.onClick.Invoke();
            var group = learning.form.GetComponent<CanvasGroup>();
            Assert.That(group.interactable, Is.False);
            var ready = typeof(WordLearningController).GetField("genreClickReadyAt", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((float)ready.GetValue(learning) - Time.unscaledTime, Is.EqualTo(.5f).Within(.02f));
            learning.cancelButton.onClick.Invoke(); learning.confirmButton.onClick.Invoke();
            Assert.That(learning.IsActive, Is.True); Assert.That(learning.genreSection.activeSelf, Is.True); Assert.That(completed, Is.False);
            ready.SetValue(learning, Time.unscaledTime - .01f);
            typeof(WordLearningController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(learning, null);
            Assert.That(group.interactable, Is.True); learning.cancelButton.onClick.Invoke(); Assert.That(completed, Is.True);
        }
        [Test]
        public void DropdownOptionRowsStayPlainWhenThemeIsApplied()
        {
            Nekolpos.UI.UIStyle.ApplyTree(learning.form.transform);
            var row = learning.genreInput.template.GetComponentInChildren<Toggle>(true);
            Assert.That(row.GetComponentsInChildren<Shadow>(true), Is.Empty);
            Assert.That(row.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "UIStyleBorder"), Is.False);
            Assert.That(((Image)row.targetGraphic).sprite, Is.Null);
        }
        private void EditAndAnswer(string word, WordGenre genre, string answer, int question = 0)
        {
            learning.wordInput.text = word; Confirm();
            learning.genreInput.value = (int)genre;
            Confirm(); Choose(answer);
        }
        [Test]
        public void ConsecutiveLearningCreatesIndependentWordWithEmptyReading()
        {
            Begin("たらこ", "たらこ"); Teach(); EditAndAnswer("たらこ", WordGenre.Food, "いいえ");
            string firstId = store.Words.Single().id;
            Choose("続けて教える");
            Assert.That(learning.wordInput.text, Is.Empty); Assert.That(learning.readingInput.text, Is.Empty);
            learning.wordInput.text = "VR"; learning.wordInput.onEndEdit.Invoke("VR");
            Assert.That(learning.readingInput.text, Is.Empty);
            learning.readingInput.text = "ぶいあーる"; EditAndAnswer("VR", WordGenre.Work, "はい");
            Assert.That(store.Words.Count, Is.EqualTo(2));
            Assert.That(store.Find(firstId).display, Is.EqualTo("たらこ"));
            Assert.That(store.Match("ぶいあーる", out _).display, Is.EqualTo("VR"));
            Choose("会話に戻る"); Assert.That(learning.CompletedTeaching, Is.True); Assert.That(completed, Is.True);
        }
        [Test]
        public void LearningSpeechUsesTypewriterBeforeChoices()
        {
            learning.Begin("たらこ", "たらこ", () => completed = true, chat);
            Assert.That(chat.IsTyping, Is.True); Assert.That(chat.choicePanel.activeSelf, Is.False);
            var routine = (global::System.Collections.IEnumerator)typeof(ChatUIController).GetMethod("TypewriterRoutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, null);
            routine.MoveNext(); routine.MoveNext();
            Assert.That(chat.messageText.maxVisibleCharacters, Is.LessThan(28));
            FinishSpeech(); Assert.That(chat.choicePanel.activeSelf, Is.True);
            Choose("はい");
            Assert.That(learning.form.activeSelf, Is.False);
            learning.Cancel();
        }
        [Test]
        public void CompleteWordUsesKnownCompleteForExactAndPartialRecognition()
        {
            var word = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "たらこ", genre = WordGenre.Food };
            foreach (var q in WordKnowledge.Questions(word.genre)) word.answers[q.Id] = WordAnswer.Unknown;
            store.Put(word);
            foreach (string input in new[] { "たらこ", "たらこスパ" })
            {
                Begin(input, input);
                Assert.That(Regex.Replace(chat.messageText.text, "<[^>]+>", ""), Is.EqualTo(chat.ResolveDisplayText(WordLearningDialogue.Format("known_complete", word.display, WordKnowledge.Summary(word)))));
                learning.Cancel();
            }
        }
        [Test]
        public void UnknownChoiceDoesNotRepeatQuestionAndMoreTeachingSpeaksBeforeNewForm()
        {
            Begin("たらこ", "たらこ"); Teach(); EditAndAnswer("たらこ", WordGenre.Food, "わからない");
            var word = store.Words.Single(); Assert.That(word.answers.Single().Value, Is.EqualTo(WordAnswer.Unknown));
            Assert.That(word.answers.ContainsKey(WordKnowledge.Next(word).Id), Is.False);
            Choose("続けて教える");
            Assert.That(chat.messageText.text, Is.EqualTo(chat.ResolveDisplayText(WordLearningDialogue.Format("teacch_more"))));
            Assert.That(learning.form.activeSelf, Is.True); Assert.That(learning.wordInput.text, Is.Empty);
        }
        [Test]
        public void FixedButtonsLoadTheirLabelsFromDialogueCsv()
        {
            var field = typeof(WordLearningDialogue).GetField("lines", BindingFlags.NonPublic | BindingFlags.Static);
            object old = field.GetValue(null);
            var data = WordLearningDialogue.ParseCsv(global::System.IO.File.ReadAllText(WordLearningDialogue.SourcePath)).ToDictionary(k => k.Key, k => k.Value);
            data["button_confirm"] = "確認のテスト"; data["button_cancel"] = "取消のテスト";
            GameObject view = null;
            try
            {
                field.SetValue(null, data);
                view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.LearningPrefab), root.transform);
                var controller = view.GetComponent<WordLearningController>(); controller.Initialize();
                Assert.That(controller.confirmButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("確認のテスト"));
                Assert.That(controller.cancelButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("取消のテスト"));
            }
            finally { if (view != null) Object.DestroyImmediate(view); field.SetValue(null, old); }
        }
        [Test]
        public void PartialMatchNewWordRetainsWholeOriginalInput()
        {
            store.Put(new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "たらこ", genre = WordGenre.Food });
            Begin("たらこスパ", "スパ"); Choose("別の言葉だよ"); Assert.That(learning.wordInput.text, Is.EqualTo("たらこスパ"));
            EditAndAnswer("たらこスパ", WordGenre.Food, "いいえ"); Assert.That(store.Words.Count, Is.EqualTo(2));
        }
        [Test]
        public void LatinCorrectionShowsOriginalReadingAndRecognizesItAfterConfirmation()
        {
            Begin("ぶいあーる", "ぶいあーる"); Teach(); learning.wordInput.text = "VR"; learning.wordInput.onEndEdit.Invoke("VR");
            Assert.That(learning.readingInput.transform.parent.gameObject.activeSelf, Is.True); Assert.That(learning.readingInput.text, Is.EqualTo("ぶいあーる"));
            EditAndAnswer("VR", WordGenre.Work, "はい"); Assert.That(store.Match("ぶいあーる", out bool exact).display, Is.EqualTo("VR")); Assert.That(exact, Is.True);
        }
        [Test]
        public void GreetingFirstLearningUsesOneConditionsScreenAndCanReconfigure()
        {
            Begin("グッドモーニング", "グッドモーニング"); Teach(); Confirm();
            learning.genreInput.value = (int)WordGenre.Greeting; Confirm();
            Assert.That(learning.greetingSection.activeSelf, Is.True); learning.timeToggles[0].isOn = true; Confirm();
            Assert.That(store.Words.Single().times, Is.EqualTo(GreetingTimes.Morning)); Assert.That(store.Words.Single().answers, Is.Empty); Choose("会話に戻る");
            Begin("グッドモーニング", "グッドモーニング"); Choose("教え直す"); Assert.That(learning.timeToggles[0].isOn, Is.True);
            learning.timeToggles[0].isOn = false; learning.timeToggles[1].isOn = true; Confirm();
            Assert.That(store.Words.Single().times, Is.EqualTo(GreetingTimes.Afternoon));
        }
        [Test]
        public void AuthoredFormUsesShortLabelsAndSameCancelAction()
        {
            Assert.That(learning.wordInput.transform.parent.Find("Caption").gameObject.activeSelf, Is.False);
            Assert.That(learning.wordInput.placeholder.GetComponent<TMP_Text>().text, Is.EqualTo("言葉を入力"));
            Assert.That(learning.greetingSection.transform.Find("Times").GetComponent<TMP_Text>().text, Is.EqualTo("時間帯"));
            Assert.That(learning.greetingSection.transform.Find("Scenes").GetComponent<TMP_Text>().text, Is.EqualTo("場面"));
            Assert.That(learning.cancelButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("やっぱりやめる"));
            Begin("たらこ", "たらこ"); Teach();
            learning.cancelButton.onClick.Invoke();
            Assert.That(completed, Is.True); Assert.That(store.Words, Is.Empty);
        }
        [Test]
        public void CancelBeforeQuestionAnswerDoesNotSave()
        {
            Begin("たらこ", "たらこ"); Teach(); Confirm(); learning.genreInput.value = 0; Confirm();
            Choose("やっぱりやめる"); Assert.That(store.Words, Is.Empty); Assert.That(completed, Is.True);
        }
        [Test]
        public void DuplicateRegistrationEndsConversationWithoutDisplayingError()
        {
            var word = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "たらこ", genre = WordGenre.Food }; store.Put(word);
            Begin("知らない語", "知らない語"); Teach();
            LogAssert.Expect(LogType.Error, new Regex("\\[WordLearning\\] 会話を終了しました"));
            EditAndAnswer("たらこ", WordGenre.Food, "はい");
            Assert.That(chat.messageText.text, Is.Empty); Assert.That(learning.errorText.text, Is.Empty);
            Assert.That(learning.IsActive, Is.False); Assert.That(completed, Is.True); Assert.That(store.Words.Single().id, Is.EqualTo(word.id));
        }
        [Test]
        public void TeachingLatinDisplayDoesNotUnlockNormalJapaneseChat()
        {
            Begin("ぶいあーる", "ぶいあーる"); Teach(); learning.wordInput.text = "VR";
            var sanitize = typeof(ChatUIController).GetMethod("SanitizeInput", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(ChatUIController.InputLanguageMode) }, null);
            Assert.That((string)sanitize.Invoke(chat, new object[] { "VR", ChatUIController.InputLanguageMode.JapaneseKana }), Is.Empty);
            Assert.That((string)sanitize.Invoke(chat, new object[] { "ぶいあーる", ChatUIController.InputLanguageMode.JapaneseKana }), Is.EqualTo("ぶいあーる"));
            Assert.That(learning.wordInput.text, Is.EqualTo("VR"));
        }
        [Test]
        public void SameSubmissionFrameCannotConfirmInitialYes()
        {
            Begin("たらこ", "たらこ");
            chat.choiceAcceptButton.onClick.Invoke();
            Assert.That(chat.messageText.text, Is.EqualTo("たらこって何？もしかして言葉を教えてくれるの？"));
            Assert.That(chat.choicePanel.activeSelf, Is.True); Assert.That(learning.form.activeSelf, Is.False);
            Choose("はい"); Assert.That(chat.messageText.text, Is.EqualTo("じゃあたらこってどういう意味？"));
        }
        [Test]
        public void DelayedEmptyInputCannotReplaceActiveLearningPrompt()
        {
            Begin("たらこ", "たらこ"); string prompt = chat.messageText.text;
            var engine = root.AddComponent<DialogueEngine>(); bool emitted = false;
            engine.OnDialogueDetermined += _ => emitted = true;
            engine.ProcessInput(""); engine.ProcessInput("別の入力");
            Assert.That(emitted, Is.False); Assert.That(chat.messageText.text, Is.EqualTo(prompt));
            Assert.That(chat.choicePanel.activeSelf, Is.True); Assert.That(learning.IsActive, Is.True);
        }
        [Test]
        public void ReteachingChigeAsKatakanaAndNewGenreOverwritesSameId()
        {
            var word = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "ちげ", genre = WordGenre.Food };
            store.Put(word); Begin("ちげ", "ちげ"); Choose("教え直す");
            Assert.That(learning.wordInput.text, Is.Empty); Assert.That(learning.form.activeSelf, Is.True);
            EditAndAnswer("チゲ", WordGenre.Work, "はい");
            Assert.That(store.Words.Count, Is.EqualTo(1)); Assert.That(store.Words.Single().id, Is.EqualTo(word.id));
            Assert.That(store.Words.Single().display, Is.EqualTo("チゲ")); Assert.That(store.Words.Single().genre, Is.EqualTo(WordGenre.Work));
        }
        [TestCase("猫")][TestCase("ねこ1")][TestCase("VR")][TestCase("ねこ！")]
        public void NonKanaDisplayRequiresReading(string display)
        {
            Begin("ねこ", "ねこ"); Teach(); learning.wordInput.text = display; learning.wordInput.onEndEdit.Invoke(display);
            Assert.That(learning.readingInput.transform.parent.gameObject.activeSelf, Is.True);
        }
        [Test]
        public void InvalidFormEndsConversationWithoutShowingError()
        {
            Begin("たらこ", "たらこ"); Teach(); learning.wordInput.text = "";
            LogAssert.Expect(LogType.Error, new Regex("\\[WordLearning\\] 会話を終了しました"));
            Confirm();
            Assert.That(learning.IsActive, Is.False); Assert.That(completed, Is.True);
            Assert.That(chat.messageText.text, Is.Empty); Assert.That(learning.errorText.text, Is.Empty); Assert.That(store.Words, Is.Empty);
        }
        [Test]
        public void SaveIOExceptionEndsConversationWithoutExposingDiagnostic()
        {
            var failing = new LearnedWordStore(() => "", _ => throw new global::System.IO.IOException("INTERNAL SAVE FAILURE"));
            StoreField.SetValue(null, failing); Begin("たらこ", "たらこ"); Teach();
            LogAssert.Expect(LogType.Error, new Regex("\\[WordLearning\\] 会話を終了しました"));
            EditAndAnswer("たらこ", WordGenre.Food, "はい");
            Assert.That(learning.IsActive, Is.False); Assert.That(completed, Is.True); Assert.That(chat.messageText.text, Is.Empty);
            Assert.That(learning.errorText.text, Is.Empty); Assert.That(failing.Words, Is.Empty);
        }
        [Test]
        public void MemoCameraFailureDoesNotDisplayErrorOrDeleteWord()
        {
            var word = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "ちげ", genre = WordGenre.Food }; store.Put(word);
            var memo = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WordLearningAuthoring.MemoPrefab), root.transform).GetComponent<WordMemoController>();
            memo.Select(word.id); string before = memo.detail.text;
            LogAssert.Expect(LogType.Error, new Regex("\\[WordMemo\\] 会話カメラへ移動できない"));
            memo.RequestForget(); Assert.That(memo.detail.text, Is.EqualTo(before)); Assert.That(store.Words.Single().id, Is.EqualTo(word.id));
        }
        [Test]
        public void DecliningUnknownWordLeavesDictionaryAndFormUnchanged()
        {
            Begin("たらこ", "たらこ");
            Assert.That(chat.messageText.text, Is.EqualTo("たらこって何？もしかして言葉を教えてくれるの？"));
            Choose("いいえ"); Assert.That(learning.IsActive, Is.False); Assert.That(learning.form.activeSelf, Is.False);
            Assert.That(store.Words, Is.Empty); Assert.That(completed, Is.True);
        }
        [Test]
        public void CancelDuringMeaningPromptCannotReopenFormOnNextAdvance()
        {
            Begin("たらこ", "たらこ"); Choose("はい"); learning.Cancel();
            ((Action)typeof(ChatUIController).GetField("OnWaitInputCompleted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chat))?.Invoke();
            Assert.That(learning.form.activeSelf, Is.False); Assert.That(store.Words, Is.Empty);
        }
    }
}
