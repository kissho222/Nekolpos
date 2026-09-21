using System.Reflection;
using System.Collections.Generic;
using Nekolpos.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System.Editor
{
    public sealed class UiInteractionRegressionTests
    {
        private GameObject root;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            root = new GameObject("UI regression fixture", typeof(RectTransform));
            root.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Time.timeScale = previousTimeScale;
        }

        [Test]
        public void Pause_BlocksDialogueAdvanceAndExternalSubmission()
        {
            ChatUIController chat = root.AddComponent<ChatUIController>();
            GameObject choices = new GameObject("Choices");
            choices.transform.SetParent(root.transform);
            choices.SetActive(false);
            chat.choicePanel = choices;
            int advanceCount = 0;
            int submitCount = 0;
            chat.OnWaitInputCompleted += () => advanceCount++;
            chat.OnPlayerInputSubmitted += _ => submitCount++;

            chat.SetMenuInputBlocked(true);
            Invoke(chat, "HandleNextInput");
            Assert.That(chat.SubmitExternalImeInput("test"), Is.False);
            Assert.That(advanceCount, Is.Zero);
            Assert.That(submitCount, Is.Zero);

            chat.SetMenuInputBlocked(false);
            Invoke(chat, "HandleNextInput");
            Assert.That(advanceCount, Is.EqualTo(1));
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void ClosingPause_RestoresExactPreviousTimeScale(float scale)
        {
            OpenBetaPauseMenuController menu = root.AddComponent<OpenBetaPauseMenuController>();
            SetField(menu, "previousTimeScale", scale);
            SetField(menu, "isPaused", true);
            Time.timeScale = 0f;

            menu.CloseMenu();

            Assert.That(Time.timeScale, Is.EqualTo(scale));
            Assert.That(menu.IsPaused, Is.False);
            menu.CloseMenu();
            Assert.That(Time.timeScale, Is.EqualTo(scale));
        }

        [Test]
        public void DisabledPause_RestoresTimeAndUnblocksChat()
        {
            OpenBetaPauseMenuController menu = root.AddComponent<OpenBetaPauseMenuController>();
            ChatUIController chat = root.AddComponent<ChatUIController>();
            chat.SetMenuInputBlocked(true);
            SetField(menu, "chatUI", chat);
            SetField(menu, "previousTimeScale", 0.5f);
            SetField(menu, "isPaused", true);
            Time.timeScale = 0f;

            Invoke(menu, "OnDisable");

            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Assert.That(chat.IsMenuInputBlocked, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Pause_PreservesInputDraftAndOriginalInteractability(bool wasInteractable)
        {
            ChatUIController chat = root.AddComponent<ChatUIController>();
            InputField input = root.AddComponent<InputField>();
            chat.chatInputField = input;
            input.text = "draft";
            input.interactable = wasInteractable;

            chat.SetMenuInputBlocked(true);
            chat.SetMenuInputBlocked(true);
            Assert.That(input.interactable, Is.False);
            Assert.That(chat.SubmitExternalImeInput("replacement"), Is.False);
            Assert.That(input.text, Is.EqualTo("draft"));
            chat.SetMenuInputBlocked(false);

            Assert.That(input.interactable, Is.EqualTo(wasInteractable));
            Assert.That(input.text, Is.EqualTo("draft"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NewAudioSource_UsesSavedMuteBeforePlayback(bool isBgm)
        {
            string key = isBgm ? "OpenBeta.Audio.BgmMuted" : "OpenBeta.Audio.SeMuted";
            bool hadPreference = PlayerPrefs.HasKey(key);
            int previous = PlayerPrefs.GetInt(key);
            try
            {
                AudioSource source = root.AddComponent<AudioSource>();
                PlayerPrefs.SetInt(key, 1);
                OpenBetaPauseMenuController.ApplySavedAudioState(source, isBgm);
                Assert.That(source.mute, Is.True);
                PlayerPrefs.SetInt(key, 0);
                OpenBetaPauseMenuController.ApplySavedAudioState(source, isBgm);
                Assert.That(source.mute, Is.False);
            }
            finally
            {
                if (hadPreference) PlayerPrefs.SetInt(key, previous);
                else PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ButtonTheme_AppliesSingleTintAndReadableText()
        {
            Image image = root.AddComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            TMP_Text label = labelObject.GetComponent<TMP_Text>();

            UIStyle.ApplyButton(button);

            Assert.That(image.color, Is.EqualTo(Color.white));
            Assert.That(label.color, Is.EqualTo(UIStyle.NormalText));
            Assert.That(Contrast(button.colors.normalColor, label.color), Is.GreaterThan(4.5f));
            Assert.That(Contrast(button.colors.pressedColor, label.color), Is.GreaterThan(4.5f));
        }

        [Test]
        public void ApplyTree_LeavesTabletButtonOutsideAutomaticButtonConversion()
        {
            GameObject tabletButtonObject = new GameObject("TabletButton", typeof(RectTransform), typeof(Image), typeof(Button));
            tabletButtonObject.transform.SetParent(root.transform, false);
            Image image = tabletButtonObject.GetComponent<Image>();
            image.color = Color.magenta;
            Button button = tabletButtonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock originalColors = button.colors;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(tabletButtonObject.transform, false);
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.color = Color.cyan;

            UIStyle.ApplyTree(root.transform);

            Assert.That(image.color, Is.EqualTo(Color.magenta));
            Assert.That(button.colors.normalColor, Is.EqualTo(originalColors.normalColor));
            Assert.That(label.color, Is.EqualTo(Color.cyan));
            Assert.That(tabletButtonObject.transform.Find("UIStyleBorder"), Is.Null);
            Assert.That(tabletButtonObject.GetComponent<Shadow>(), Is.Null);
        }

        [Test]
        public void Rewriting_ResetsWritingPresentationBeforeShowingThePrompt()
        {
            DiaryCalendarController diary = root.AddComponent<DiaryCalendarController>();
            GameObject bodyObject = new GameObject("WritingBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            GameObject microphone = new GameObject("MicrophoneIndicator");
            GameObject finishObject = new GameObject("WritingFinishButton", typeof(RectTransform), typeof(Image), typeof(Button));
            GameObject rewriteObject = new GameObject("ReWritingButton", typeof(RectTransform), typeof(Image), typeof(Button));
            bodyObject.transform.SetParent(root.transform, false);
            microphone.transform.SetParent(root.transform, false);
            finishObject.transform.SetParent(root.transform, false);
            rewriteObject.transform.SetParent(root.transform, false);

            bodyObject.GetComponent<TMP_Text>().text = "途中まで表示した本文";
            microphone.SetActive(true);
            finishObject.SetActive(true);
            rewriteObject.SetActive(true);
            SetField(diary, "writingBodyText", bodyObject.GetComponent<TMP_Text>());
            SetField(diary, "microphoneIndicator", microphone);
            SetField(diary, "writingFinishButton", finishObject.GetComponent<Button>());
            SetField(diary, "reWritingButton", rewriteObject.GetComponent<Button>());

            Invoke(diary, "ResetWritingPresentation");

            Assert.That(bodyObject.GetComponent<TMP_Text>().text, Is.Empty);
            Assert.That(microphone.activeSelf, Is.False);
            Assert.That(finishObject.activeSelf, Is.False);
            Assert.That(rewriteObject.activeSelf, Is.False);
        }

        private static float Contrast(Color a, Color b)
        {
            float la = Luminance(a);
            float lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        [Test]
        public void History_ReusesUnchangedRowsAndUpdatesOnlyChangedContent()
        {
            LogWindowPanel panel = root.AddComponent<LogWindowPanel>();
            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            GameObject prefab = new GameObject("Row", typeof(RectTransform), typeof(Image));
            prefab.transform.SetParent(root.transform, false);
            GameObject message = new GameObject("MessageText", typeof(RectTransform), typeof(TextMeshProUGUI));
            message.transform.SetParent(prefab.transform, false);
            SetField(panel, "contentRoot", (RectTransform)content.transform);
            SetField(panel, "logEntryPrefab", prefab);
            var first = new DialogueLogEntry { LogId = "one", Speaker = "System", Text = "A" };
            var second = new DialogueLogEntry { LogId = "two", Speaker = "System", Text = "B" };

            RenderHistory(panel, first, second);
            GameObject firstRow = content.transform.GetChild(0).gameObject;
            GameObject secondRow = content.transform.GetChild(1).gameObject;
            RenderHistory(panel, first.Clone(), second.Clone());
            Assert.That(content.transform.childCount, Is.EqualTo(2));
            Assert.That(content.transform.GetChild(0).gameObject, Is.SameAs(firstRow));
            Assert.That(content.transform.GetChild(1).gameObject, Is.SameAs(secondRow));

            var changed = second.Clone();
            changed.Text = "B2";
            RenderHistory(panel, first.Clone(), changed);
            Assert.That(content.transform.GetChild(0).gameObject, Is.SameAs(firstRow));
            Assert.That(secondRow == null, Is.True);
            Assert.That(content.transform.GetChild(1).Find("MessageText").GetComponent<TMP_Text>().text, Is.EqualTo("B2"));

            RenderHistory(panel, changed.Clone());
            Assert.That(content.transform.childCount, Is.EqualTo(1));
            Assert.That(firstRow == null, Is.True);
            RenderHistory(panel);
            Assert.That(content.transform.childCount, Is.Zero);
        }

        [Test]
        public void History_UploadCompletionLocksSelectionWithoutRecreatingRow()
        {
            LogWindowPanel panel = root.AddComponent<LogWindowPanel>();
            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            GameObject prefab = new GameObject("Row", typeof(RectTransform), typeof(Image));
            prefab.transform.SetParent(root.transform, false);
            SetField(panel, "contentRoot", (RectTransform)content.transform);
            SetField(panel, "logEntryPrefab", prefab);
            var entry = new DialogueLogEntry { LogId = "one", Speaker = "System", Text = "A" };
            RenderHistory(panel, entry);
            GameObject row = content.transform.GetChild(0).gameObject;
            var uploaded = entry.Clone();
            uploaded.UploadStatus = DialogueLogEntry.UploadStatusUploaded;

            RenderHistory(panel, uploaded);

            Assert.That(content.transform.GetChild(0).gameObject, Is.SameAs(row));
            Toggle toggle = row.GetComponentInChildren<Toggle>(true);
            Assert.That(toggle.isOn, Is.True);
            Assert.That(toggle.interactable, Is.False);
        }

        private static void RenderHistory(LogWindowPanel panel, params DialogueLogEntry[] entries)
        {
            panel.GetType().GetMethod("RefreshEntries", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(panel, new object[] { entries, false });
        }

        private static float Luminance(Color color)
        {
            Color linear = color.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
        }
    }
}
