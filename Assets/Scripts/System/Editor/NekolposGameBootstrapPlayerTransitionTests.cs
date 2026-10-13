using System;
using System.IO;
using System.Reflection;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Nekolpos.System.Editor
{
    public sealed class NekolposGameBootstrapPlayerTransitionTests
    {
        private bool hadSkipPreference;
        private int previousSkipPreference;

        [SetUp]
        public void PreserveSkipPreference()
        {
            hadSkipPreference = PlayerPrefs.HasKey(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey);
            previousSkipPreference = PlayerPrefs.GetInt(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey);
            PlayerPrefs.DeleteKey(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey);
        }

        [TearDown]
        public void RestoreSkipPreference()
        {
            if (hadSkipPreference) PlayerPrefs.SetInt(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey, previousSkipPreference);
            else PlayerPrefs.DeleteKey(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public global::System.Collections.IEnumerator SkipOpening_SwitchesTitleMusicToGameMusic()
        {
            yield return new EnterPlayMode();
            bool hadDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey);
            int savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/MainScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity"));
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!bootstrap.enabled)
            {
                bootstrap.enabled = true;
                typeof(NekolposGameBootstrap).GetMethod("Awake", flags).Invoke(bootstrap, null);
            }
            yield return new WaitForSecondsRealtime(0.2f);
            try
            {
                var music = (Nekolpos.Audio.BackgroundMusicController)typeof(NekolposGameBootstrap)
                    .GetField("backgroundMusicController", flags).GetValue(bootstrap);
                var source = (AudioSource)typeof(Nekolpos.Audio.BackgroundMusicController)
                    .GetField("audioSource", flags).GetValue(music);
                var title = (AudioClip)typeof(Nekolpos.Audio.BackgroundMusicController)
                    .GetField("titleBgm", flags).GetValue(music);
                var gameClips = (AudioClip[])typeof(Nekolpos.Audio.BackgroundMusicController)
                    .GetField("gameBgms", flags).GetValue(music);
                Assert.That(source.clip, Is.EqualTo(title));
                Assert.That(source.loop, Is.True);
                TimeManager.SaveCurrentDay(3);
                var intro = (OpenBetaIntroPopupPanel)typeof(NekolposGameBootstrap).GetField("introPanel", flags).GetValue(bootstrap);
                intro.RefreshSkipOpeningOption();
                var toggle = (UnityEngine.UI.Toggle)typeof(OpenBetaIntroPopupPanel).GetField("skipOpeningToggle", flags).GetValue(intro);
                toggle.isOn = true;
                Assert.That(intro.SkipOpeningRequested, Is.True);
                typeof(NekolposGameBootstrap).GetMethod("StartNormalGame", flags).Invoke(bootstrap, null);
                float deadline = Time.realtimeSinceStartup + 8f;
                while (source.clip == title)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "OP skip must fade out the title BGM and begin a game track.");
                    yield return null;
                }
                Assert.That(gameClips, Does.Contain(source.clip));
                Assert.That(source.loop, Is.False);
                Assert.That(source.isPlaying, Is.True);
                Assert.That(UnityEngine.Object.FindFirstObjectByType<TimeManager>().CurrentPeriod, Is.EqualTo(DayPeriod.Morning));
            }
            finally
            {
                if (hadDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay); else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                PlayerPrefs.Save();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public global::System.Collections.IEnumerator NightDiary_ClosesTabletThenSleepsAndGreetsAfterMorningFade()
        {
            yield return new EnterPlayMode();
            string[] keys = { "Nekolpos.Diary.Drafts.v1", "Nekolpos.Diary.SpecialPending.v1" };
            bool[] present = global::System.Array.ConvertAll(keys, PlayerPrefs.HasKey);
            string[] saved = global::System.Array.ConvertAll(keys, key => PlayerPrefs.GetString(key));
            bool hadDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey);
            int savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/MainScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            Scene main = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
            SceneManager.SetActiveScene(main);
            var sceneBootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
            if (!sceneBootstrap.enabled)
            {
                sceneBootstrap.enabled = true;
                typeof(NekolposGameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sceneBootstrap, null);
            }
            yield return new WaitForSecondsRealtime(0.2f);
            try
            {
                foreach (string key in keys) PlayerPrefs.DeleteKey(key);
                typeof(DiaryJournalStore).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
                var bootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>();
                var diary = DiaryCalendarController.Instance;
                var dialogue = DialogueManager.Instance;
                var chat = UnityEngine.Object.FindFirstObjectByType<ChatUIController>();
                var clock = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                Assert.That(bootstrap, Is.Not.Null);
                Assert.That(diary, Is.Not.Null);
                Assert.That(dialogue, Is.Not.Null);
                bootstrap.GetType().GetMethod("HideAllPanels", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bootstrap, null);
                var presentation = (CatPresentationModeController)bootstrap.GetType().GetField("catPresentationMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
                presentation.EnterNormalMode();
                chat.SetUiSuppressed(false);
                chat.typingSpeed = 0f;
                clock.SetCurrentTime(5, DayPeriod.Evening);
                clock.SetCurrentTime(5, DayPeriod.Night);
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerAreaTracker>();
                Assert.That(player.CurrentAreaId, Is.EqualTo("Desk"));
                diary.BeginNightDiary();
                Assert.That(diary.TryBeginCompletedNightSleep(), Is.False, "Sleep must not begin before diary completion.");
                yield return new WaitForSecondsRealtime(0.8f);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DiaryCalendarController).GetMethod("ConfirmWritingAndOpenCalendar", flags).Invoke(diary, null);
                yield return new WaitForSecondsRealtime(0.4f);
                var calendar = (GameObject)typeof(DiaryCalendarController).GetField("calendarView", flags).GetValue(diary);
                Assert.That(calendar.activeInHierarchy, Is.True);
                Assert.That(clock.CurrentDay, Is.EqualTo(5));
                typeof(NekolposGameBootstrap).GetMethod("HandleTabletPowerButtonClicked", flags).Invoke(bootstrap, null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (typeof(NekolposGameBootstrap).GetField("tabletPowerTransitionCoroutine", flags).GetValue(bootstrap) != null)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    Assert.That(typeof(DialogueManager).GetField("nightSleepCancellation", flags).GetValue(dialogue), Is.Null, "Sleep dialogue must wait for the tablet camera return.");
                    yield return null;
                }
                Assert.That(clock.CurrentDay, Is.EqualTo(5), "Tablet exit must show sleep dialogue before advancing the day.");
                var sleep = BasicSystemDialogueCatalog.Resolve(BasicSystemDialogueCatalog.GreetingAfterDiaryKey);
                Assert.That(sleep, Is.Not.Null);
                Assert.That(chat.CurrentMessage, Is.EqualTo(dialogue.FormatRuntimeText(sleep.Text)));
                Assert.That(dialogue.TryBeginNightSleep(), Is.False, "Sleep dialogue must not start twice.");
                Assert.That(diary.TryBeginCompletedNightSleep(), Is.False);
                var fade = dialogue.GetComponent<Nekolpos.ActionSystem.FadeController>();
                bool advancedWhileOpaque = false;
                bool fadedInAfterAdvance = false;
                float opaqueAt = -1f;
                float advancedAt = -1f;
                int morningAdvanceCount = 0;
                fade.BecameOpaque += () => opaqueAt = Time.realtimeSinceStartup;
                clock.OnTimeAdvanced += result =>
                {
                    if (result.CurrentDay != 6 || result.CurrentPeriod != DayPeriod.Morning) return;
                    morningAdvanceCount++;
                    advancedAt = Time.realtimeSinceStartup;
                    advancedWhileOpaque = fade.IsOpaque;
                };
                fade.BecameTransparent += () => fadedInAfterAdvance = clock.CurrentDay == 6;
                yield return null;
                dialogue.ProgressNextLine();
                deadline = Time.realtimeSinceStartup + 6f;
                while (!fade.IsOpaque)
                {
                    Assert.That(clock.CurrentDay, Is.EqualTo(5));
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(2.5f);
                Assert.That(clock.CurrentDay, Is.EqualTo(5));
                Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Night));
                Assert.That(fade.IsOpaque, Is.True, "The old night must remain completely opaque throughout the hold.");
                while (clock.CurrentDay != 6 || !fade.IsTransparent || !fadedInAfterAdvance)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                yield return null;
                Assert.That(advancedWhileOpaque, Is.True);
                Assert.That(opaqueAt, Is.GreaterThanOrEqualTo(0));
                Assert.That(advancedAt - opaqueAt, Is.GreaterThanOrEqualTo(3f));
                Assert.That(morningAdvanceCount, Is.EqualTo(1));
                Assert.That(fadedInAfterAdvance, Is.True);
                Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Morning));
                Assert.That(player.CurrentAreaId, Is.EqualTo("Table"));
                Assert.That(chat.CurrentMessage, Is.EqualTo(dialogue.FormatRuntimeText(BasicSystemDialogueCatalog.Get(BasicSystemDialogueCatalog.GreetingMorningKey))));
                dialogue.ProgressNextLine();
                deadline = Time.realtimeSinceStartup + 3f;
                while (typeof(DialogueManager).GetField("nightSleepCancellation", flags).GetValue(dialogue) != null)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                clock.SetCurrentTime(6, DayPeriod.Night);
                Assert.That(dialogue.TryBeginNightSleep(), Is.True);
                yield return null;
                dialogue.ProgressNextLine();
                deadline = Time.realtimeSinceStartup + 3f;
                while (!fade.IsOpaque)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                dialogue.ResetRuntimeStateForTitleRestart();
                yield return new WaitForSecondsRealtime(3.3f);
                Assert.That(clock.CurrentDay, Is.EqualTo(6), "Title restart during the opaque hold must cancel the pending next morning.");
                Assert.That(clock.CurrentPeriod, Is.EqualTo(DayPeriod.Night));
                Assert.That(morningAdvanceCount, Is.EqualTo(1));
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    if (present[i]) PlayerPrefs.SetString(keys[i], saved[i]); else PlayerPrefs.DeleteKey(keys[i]);
                if (hadDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay); else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                PlayerPrefs.Save();
            }
            yield return new ExitPlayMode();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TimeTransition_ResumesAtTableForMorningAndDeskForNight(bool nextDay)
        {
            CatHomeLocation destination = nextDay ? CatHomeLocation.Table : CatHomeLocation.Desk;
            string areaId = nextDay ? "Table" : "Desk";
            GameObject root = new GameObject("Day resume regression");
            root.SetActive(false);
            GameObject player = CreatePlayer();
            GameObject anchor = new GameObject("Table day resume anchor");
            GameObject area = CreateArea(areaId, -4f);
            try
            {
                anchor.transform.position = new Vector3(-4f, 0f, 0f);
                var bootstrap = root.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "startupCameraPivot", root.transform);
                SetField(bootstrap, "playerAreaTransitionController", player.GetComponent<PlayerAreaTransitionController>());
                SetField(bootstrap, "conversationTimelineBindings", new[] { new NekolposGameBootstrap.ConversationTimelineBinding
                {
                    id = "MoveTo" + areaId, destination = destination,
                    playerDestination = anchor.transform, expectedPlayerAreaId = areaId
                } });
                typeof(NekolposGameBootstrap).GetMethod("HandleDayChangedResume", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { nextDay
                        ? new TimeAdvanceResult(DayPeriod.Night, DayPeriod.Morning, 1, 2, 0)
                        : new TimeAdvanceResult(DayPeriod.Evening, DayPeriod.Night, 1, 1, 0) });
                Assert.That(player.transform.position, Is.EqualTo(anchor.transform.position));
                Assert.That(player.GetComponent<PlayerAreaTracker>().CurrentAreaId, Is.EqualTo(areaId));
            }
            finally { DestroyFixtures(root, player, anchor, area); }
        }

        [Test]
        public void SkipOpening_StartMessageUsesMorningCatalogRegardlessOfNameResponse()
        {
            var root = new GameObject("Morning greeting regression");
            root.SetActive(false);
            try
            {
                var bootstrap = root.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "wasCalledCorrectly", false);
                string greeting = (string)typeof(NekolposGameBootstrap).GetMethod("ResolveOpeningStartMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { true });
                string expected = BasicSystemDialogueCatalog.Get(BasicSystemDialogueCatalog.GreetingMorningKey, "おはよう！朝だね。今日はなにしようか。");
                var formatter = UnityEngine.Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
                if (formatter != null) expected = formatter.FormatRuntimeText(expected);
                Assert.That(greeting, Is.Not.Empty);
                Assert.That(greeting, Is.EqualTo(expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void NightDiaryConfirmation_KeepsCalendarOpenAndDoesNotAdvanceTime()
        {
            var root = new GameObject("Night diary completion regression");
            root.SetActive(false);
            try
            {
                var clock = root.AddComponent<TimeManager>();
                SetField(clock, "initialized", true);
                SetField(clock, "currentDay", 9998);
                var diary = root.AddComponent<DiaryCalendarController>();
                var panel = new GameObject("DiaryPanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                SetField(diary, "panel", panel);
                SetField(diary, "timeManager", clock);
                SetField(diary, "isNightDiaryActive", true);
                SetField(diary, "activeNightDiaryDay", 9998);
                bool advanced = false;
                clock.OnTimeAdvanced += _ => advanced = true;
                typeof(DiaryCalendarController).GetMethod("CompleteNightDiary", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(diary, null);
                Assert.That(clock.CurrentDay, Is.EqualTo(9998));
                Assert.That(advanced, Is.False);
                Assert.That(panel.activeSelf, Is.True);
                Assert.That(typeof(DiaryCalendarController).GetField("completedNightDiaryDay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(diary), Is.EqualTo(9998));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DiaryFinish_RewriteIsVisibleOnlyForNormalEntries(bool special)
        {
            GameObject root = new GameObject("Diary finish regression");
            root.SetActive(false);
            try
            {
                var diary = root.AddComponent<DiaryCalendarController>();
                var prompt = new GameObject("Prompt", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                var finish = new GameObject("Finish", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                var rewrite = new GameObject("Rewrite", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                foreach (var item in new[] { prompt, finish, rewrite }) item.transform.SetParent(root.transform);
                SetField(diary, "writingPromptText", prompt.GetComponent<TMPro.TMP_Text>());
                SetField(diary, "writingFinishButton", finish.GetComponent<UnityEngine.UI.Button>());
                SetField(diary, "reWritingButton", rewrite.GetComponent<UnityEngine.UI.Button>());
                SetField(diary, "confirmingSpecialEntries", special);
                typeof(DiaryCalendarController).GetMethod("ShowWritingFinish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(diary, null);
                Assert.That(finish.activeSelf, Is.True);
                Assert.That(rewrite.activeSelf, Is.EqualTo(!special));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SpecialDiaryWriting_GroupsOnlyEarliestDayInChronologicalOrder(bool sameDay)
        {
            var root = new GameObject("Sequential special diary regression");
            root.SetActive(false);
            try
            {
                var diary = root.AddComponent<DiaryCalendarController>();
                var body = new GameObject("Body", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                var microphone = new GameObject("Microphone");
                body.transform.SetParent(root.transform);
                microphone.transform.SetParent(root.transform);
                SetField(diary, "writingBodyText", body.GetComponent<TMPro.TMP_Text>());
                SetField(diary, "microphoneIndicator", microphone);
                var first = new DiaryJournalStore.DiaryDraft { diaryId = "first", occurredDay = 1, period = DayPeriod.Morning, sequence = 3, body = "A" };
                var second = new DiaryJournalStore.DiaryDraft { diaryId = "second", occurredDay = sameDay ? 1 : 2, period = DayPeriod.Evening, sequence = 1, body = "B" };
                var third = new DiaryJournalStore.DiaryDraft { diaryId = "third", occurredDay = 1, period = DayPeriod.Morning, sequence = 4, body = "C" };
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var pending = (global::System.Collections.IEnumerator)typeof(DiaryCalendarController)
                    .GetMethod("WritePendingSpecialEntriesRoutine", flags).Invoke(diary, new object[] { new[] { second, third, first } });
                Assert.That(pending.MoveNext(), Is.True);
                var writing = (global::System.Collections.IEnumerator)pending.Current;
                Assert.That(writing.MoveNext(), Is.True);
                Assert.That(body.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("A"));
                var awaiting = (global::System.Collections.Generic.List<DiaryJournalStore.DiaryDraft>)typeof(DiaryCalendarController)
                    .GetField("entriesAwaitingConfirmation", flags).GetValue(diary);
                Assert.That(awaiting, Is.EqualTo(sameDay ? new[] { first, third, second } : new[] { first, third }));
                Assert.That(writing.MoveNext(), Is.True);
                Assert.That(body.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("A\n\nそれと……\nC"));
                if (sameDay)
                {
                    Assert.That(writing.MoveNext(), Is.True);
                    Assert.That(body.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("A\n\nそれと……\nC\n\nそれと……\nB"));
                }
                Assert.That(second.body, Is.EqualTo("B"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(5, 2)]
        [TestCase(5, 5)]
        [TestCase(5, 0)]
        public void WritingDate_UsesEntryDayOrCurrentNightForEmptyDiary(int nightDay, int entryDay)
        {
            var root = new GameObject("Writing date regression");
            root.SetActive(false);
            try
            {
                var clock = root.AddComponent<TimeManager>();
                SetField(clock, "initialized", true);
                SetField(clock, "currentDay", nightDay);
                var diary = root.AddComponent<DiaryCalendarController>();
                SetField(diary, "timeManager", clock);
                SetField(diary, "activeNightDiaryDay", nightDay);
                foreach (string name in new[] { "writingDateText", "writingBodyText", "writingPromptText" })
                {
                    var text = new GameObject(name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                    text.transform.SetParent(root.transform);
                    SetField(diary, name, text.GetComponent<TMPro.TMP_Text>());
                }
                foreach (string name in new[] { "writingFinishButton", "reWritingButton" })
                {
                    var button = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Button));
                    button.transform.SetParent(root.transform);
                    SetField(diary, name, button.GetComponent<UnityEngine.UI.Button>());
                }
                var microphone = new GameObject("Microphone");
                microphone.transform.SetParent(root.transform);
                SetField(diary, "microphoneIndicator", microphone);
                var entries = entryDay == 0 ? Array.Empty<DiaryJournalStore.DiaryDraft>()
                    : new[] { new DiaryJournalStore.DiaryDraft { occurredDay = entryDay, body = "A" } };
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var routine = (global::System.Collections.IEnumerator)typeof(DiaryCalendarController)
                    .GetMethod("WriteEntriesRoutine", flags).Invoke(diary, new object[] { entries });
                routine.MoveNext();
                var date = clock.GetCalendarDate(entryDay == 0 ? nightDay : entryDay);
                var label = (TMPro.TMP_Text)typeof(DiaryCalendarController).GetField("writingDateText", flags).GetValue(diary);
                Assert.That(label.text, Is.EqualTo($"{date.Month}/{date.Day}の日記"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase("{{CAT_NAME}}/{{PLAYER_NAME}}/{{PLAYER_CALLING}}/{{CAT_PRONOUN}}/{{CAT_GENDER}}/{{PARENT}}")]
        [TestCase(@"{{CAT\_NAME}}/{{PLAYER\_NAME}}/{{PLAYER\_CALLING}}/{{CAT\_PRONOUN}}/{{CAT\_GENDER}}/{{PARENT}}")]
        [TestCase(@"\{\{CAT\_NAME\}\}/\{\{PLAYER\_NAME\}\}/\{\{PLAYER\_CALLING\}\}/\{\{CAT\_PRONOUN\}\}/\{\{CAT\_GENDER\}\}/\{\{PARENT\}\}")]
        [TestCase("{{ cat_name }}/{{ PlayerName }}/{{ playerCalling }}/{{ CatPronoun }}/{{ cat_gender }}/{{ parent }}")]
        [TestCase("{CAT_NAME}/{PLAYER_NAME}/{PLAYER_CALLING}/{CAT_PRONOUN}/{CAT_GENDER}/{PARENT}")]
        public void DiaryBody_ResolvesExistingNameVariables(string template)
        {
            GameObject host = null;
            var formatter = UnityEngine.Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
            if (formatter == null)
            {
                host = new GameObject("Diary formatter regression");
                host.SetActive(false);
                formatter = host.AddComponent<DialogueManager>();
            }
            var original = formatter.catData;
            var data = ScriptableObject.CreateInstance<Nekolpos.Data.CatDataSO>();
            try
            {
                data.catName = "猫";
                data.playerName = "名前";
                data.playerCalling = "呼称";
                data.catPronoun = "一人称";
                data.catGender = "メス";
                data.parentCall = "親";
                formatter.catData = data;
                var method = typeof(DiaryCalendarController).GetMethod("ResolveDiaryBody", BindingFlags.Static | BindingFlags.NonPublic);
                string text = (string)method.Invoke(null, new object[] { template });
                Assert.That(text, Is.EqualTo("猫/名前/呼称/一人称/メス/親"));
            }
            finally
            {
                formatter.catData = original;
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [TestCase(0)]
        [TestCase(3)]
        public void SkipOpeningOption_IsAvailableOnlyWithSavedProgress(int savedDay)
        {
            bool hadSavedDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey);
            int previousDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            GameObject root = new GameObject("Skip option regression", typeof(RectTransform));
            root.SetActive(false);
            try
            {
                if (savedDay == 0) PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                else PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay);
                OpenBetaIntroPopupPanel panel = root.AddComponent<OpenBetaIntroPopupPanel>();
                GameObject button = new GameObject("StartDemoButton", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                button.transform.SetParent(root.transform, false);
                GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                label.transform.SetParent(button.transform, false);
                GameObject toggleObject = new GameObject("SkipOpeningToggle", typeof(RectTransform), typeof(UnityEngine.UI.Toggle));
                toggleObject.transform.SetParent(root.transform, false);
                UnityEngine.UI.Toggle toggle = toggleObject.GetComponent<UnityEngine.UI.Toggle>();
                var serializedPanel = new UnityEditor.SerializedObject(panel);
                serializedPanel.FindProperty("skipOpeningToggle").objectReferenceValue = toggle;
                serializedPanel.ApplyModifiedPropertiesWithoutUndo();
                panel.RefreshSkipOpeningOption();
                Assert.That(panel.SkipOpeningRequested, Is.False);
                Assert.That(toggle.gameObject.activeSelf, Is.EqualTo(savedDay > 0));
                if (savedDay > 0)
                {
                    Assert.That(toggle.gameObject.activeSelf, Is.True);
                    // The fixture parent is inactive, so validate selection without enabling the full title.
                    toggle.isOn = true;
                    Assert.That(panel.SkipOpeningRequested, Is.True);
                    Assert.That(PlayerPrefs.GetInt(OpenBetaIntroPopupPanel.SkipOpeningPreferenceKey), Is.EqualTo(1));
                    toggle.SetIsOnWithoutNotify(false);
                    panel.RefreshSkipOpeningOption();
                    Assert.That(panel.SkipOpeningRequested, Is.True, "Saved selection must survive refreshing the title.");
                    toggle.isOn = false;
                    toggle.SetIsOnWithoutNotify(true);
                    panel.RefreshSkipOpeningOption();
                    Assert.That(panel.SkipOpeningRequested, Is.False, "Turning skip off must also be saved.");
                    toggle.isOn = true;
                    Assert.That(root.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).Length, Is.EqualTo(1));
                    PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                    panel.RefreshSkipOpeningOption();
                    Assert.That(toggle.gameObject.activeSelf, Is.False);
                    Assert.That(panel.SkipOpeningRequested, Is.False);
                    PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay);
                    panel.RefreshSkipOpeningOption();
                    Assert.That(panel.SkipOpeningRequested, Is.True, "Unavailable progress must not erase the preference.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (hadSavedDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, previousDay);
                else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
            }
        }

        [Test]
        public void StopConversationTimelinePlayback_AllowsAlreadyDestroyedDirectors()
        {
            GameObject root = new GameObject("Timeline cleanup regression");
            root.SetActive(false);
            GameObject directorObject = new GameObject("Destroyed timeline", typeof(UnityEngine.Playables.PlayableDirector));
            try
            {
                NekolposGameBootstrap bootstrap = root.AddComponent<NekolposGameBootstrap>();
                var director = directorObject.GetComponent<UnityEngine.Playables.PlayableDirector>();
                SetField(bootstrap, "activeConversationTimelineBinding", new NekolposGameBootstrap.ConversationTimelineBinding
                {
                    startDirector = director,
                    endDirector = director
                });
                UnityEngine.Object.DestroyImmediate(directorObject);
                MethodInfo stop = typeof(NekolposGameBootstrap).GetMethod("StopConversationTimelinePlayback", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.DoesNotThrow(() => stop.Invoke(bootstrap, new object[] { false }));
            }
            finally
            {
                if (directorObject != null) UnityEngine.Object.DestroyImmediate(directorObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(0)]
        [TestCase(3)]
        public void StartNormalGame_ShowsCharacterSetupEvenWithSavedProgress(int savedDay)
        {
            bool hadSavedDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey);
            int previousDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            FieldInfo debugStart = typeof(NekolposGameBootstrap).GetField("isDebugStart", BindingFlags.Static | BindingFlags.NonPublic);
            bool previousDebugStart = NekolposGameBootstrap.IsDebugStart;
            GameObject root = new GameObject("Normal start regression");
            root.SetActive(false);
            try
            {
                if (savedDay == 0) PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
                else PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay);
                Assert.That(TimeManager.HasSavedProgress, Is.EqualTo(savedDay > 0));

                NekolposGameBootstrap bootstrap = root.AddComponent<NekolposGameBootstrap>();
                GameObject panelObject = new GameObject("Character setup", typeof(RectTransform));
                panelObject.transform.SetParent(root.transform);
                OpenBetaCharacterSetupPanel panel = panelObject.AddComponent<OpenBetaCharacterSetupPanel>();
                panelObject.SetActive(false);
                SetField(bootstrap, "characterSetupPanel", panel);
                debugStart.SetValue(null, true);

                typeof(NekolposGameBootstrap).GetMethod("StartNormalGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bootstrap, null);

                Assert.That(panelObject.activeSelf, Is.True);
                Assert.That(NekolposGameBootstrap.IsDebugStart, Is.False);
                Assert.That(PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey, 0), Is.EqualTo(savedDay));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                debugStart.SetValue(null, previousDebugStart);
                if (hadSavedDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, previousDay);
                else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
            }
        }

        [TestCase(CatHomeLocation.Desk, "Desk", 4f)]
        [TestCase(CatHomeLocation.Table, "Table", -4f)]
        public void ApplyConversationLocationWhileOpaque_TeleportsPlayerToTheBindingAnchor(
            CatHomeLocation destination,
            string expectedAreaId,
            float destinationX)
        {
            GameObject bootstrapObject = new GameObject("Conversation transition bootstrap");
            GameObject player = CreatePlayer();
            GameObject anchor = new GameObject("Player transition anchor");
            GameObject area = CreateArea(expectedAreaId, destinationX);

            try
            {
                anchor.transform.position = new Vector3(destinationX, 0f, 0f);
                NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "playerAreaTransitionController", player.GetComponent<PlayerAreaTransitionController>());

                InvokeApplyWhileOpaque(bootstrap, new NekolposGameBootstrap.ConversationTimelineBinding
                {
                    id = "MoveTo" + expectedAreaId,
                    destination = destination,
                    playerDestination = anchor.transform,
                    expectedPlayerAreaId = expectedAreaId
                });

                PlayerSafetyMovementController safetyMovement = player.GetComponent<PlayerSafetyMovementController>();
                Assert.That(player.transform.position, Is.EqualTo(anchor.transform.position));
                Assert.That(safetyMovement.LastSafePosition, Is.EqualTo(anchor.transform.position));
                Assert.That(player.GetComponent<PlayerAreaTracker>().CurrentAreaId, Is.EqualTo(expectedAreaId));
            }
            finally
            {
                DestroyFixtures(bootstrapObject, player, anchor, area);
            }
        }

        [Test]
        public void ApplyConversationLocationWhileOpaque_ContinuesCatFlowWhenPlayerAnchorIsMissing()
        {
            GameObject bootstrapObject = new GameObject("Conversation transition bootstrap");
            GameObject player = CreatePlayer();

            try
            {
                NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "playerAreaTransitionController", player.GetComponent<PlayerAreaTransitionController>());
                LogAssert.Expect(LogType.Warning, "[OpenBetaTitle] 会話Timeline 'MoveToDesk' のPlayer Destinationが未設定のため、猫側の移動だけを継続します。");

                InvokeApplyWhileOpaque(bootstrap, new NekolposGameBootstrap.ConversationTimelineBinding
                {
                    id = "MoveToDesk",
                    destination = CatHomeLocation.Desk
                });

                Assert.That(player.transform.position, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                DestroyFixtures(bootstrapObject, player);
            }
        }

        [Test]
        public void ConversationTimelinePlayerMove_IsScheduledOnlyAfterTheOpaqueFadeWait()
        {
            string source = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");
            int opaqueWait = source.IndexOf("yield return FadeConversationToOpaque(fadeController,", StringComparison.Ordinal);
            int playerMove = source.IndexOf("ApplyConversationLocationWhileOpaque(binding);", opaqueWait, StringComparison.Ordinal);

            Assert.That(opaqueWait, Is.GreaterThanOrEqualTo(0));
            Assert.That(playerMove, Is.GreaterThan(opaqueWait));
            StringAssert.Contains("TeleportImmediately(binding.playerDestination, expectedAreaId)", source);
        }

        [Test]
        public void MainScene_ConversationMoveBindingsHavePlayerTransitionAnchors()
        {
            const string path = "Assets/Scenes/MainScene.unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                NekolposGameBootstrap bootstrap = null;
                foreach (NekolposGameBootstrap candidate in Resources.FindObjectsOfTypeAll<NekolposGameBootstrap>())
                {
                    if (candidate.gameObject.scene == scene) { bootstrap = candidate; break; }
                }

                Assert.That(bootstrap, Is.Not.Null);
                var serialized = new SerializedObject(bootstrap);
                Assert.That(serialized.FindProperty("playerAreaTransitionController").objectReferenceValue, Is.Not.Null);
                SerializedProperty bindings = serialized.FindProperty("conversationTimelineBindings");
                AssertBinding(bindings, "MoveToDesk", "Desk", false);
                AssertBinding(bindings, "MoveToTablet", "Desk", true);
                AssertBinding(bindings, "MoveToTable", "Table", false);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void AssertBinding(SerializedProperty bindings, string id, string expectedAreaId, bool opensTablet)
        {
            for (int index = 0; index < bindings.arraySize; index++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                if (binding.FindPropertyRelative("id").stringValue != id) continue;
                Assert.That(binding.FindPropertyRelative("playerDestination").objectReferenceValue, Is.Not.Null);
                Assert.That(binding.FindPropertyRelative("expectedPlayerAreaId").stringValue, Is.EqualTo(expectedAreaId));
                Assert.That(binding.FindPropertyRelative("enterTabletCameraAfterPlayback").boolValue, Is.EqualTo(opensTablet), id);
                return;
            }

            Assert.Fail($"{id} binding was not found.");
        }

        private static GameObject CreatePlayer()
        {
            GameObject player = new GameObject("Conversation transition player", typeof(CharacterController), typeof(PlayerSafetyMovementController), typeof(PlayerAreaTracker), typeof(PlayerAreaTransitionController));
            CharacterController characterController = player.GetComponent<CharacterController>();
            characterController.height = 0.05f;
            characterController.radius = 0.01f;
            characterController.center = new Vector3(0f, 0.025f, 0f);
            return player;
        }

        private static GameObject CreateArea(string areaId, float centerX)
        {
            GameObject areaObject = new GameObject("Conversation transition area", typeof(BoxCollider), typeof(PlayerAreaVolume));
            BoxCollider collider = areaObject.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 0.025f, 0f);
            collider.size = new Vector3(2f, 0.1f, 2f);
            areaObject.transform.position = new Vector3(centerX, 0f, 0f);
            SetField(areaObject.GetComponent<PlayerAreaVolume>(), "areaId", areaId);
            return areaObject;
        }

        private static void InvokeApplyWhileOpaque(NekolposGameBootstrap bootstrap, NekolposGameBootstrap.ConversationTimelineBinding binding)
        {
            MethodInfo method = typeof(NekolposGameBootstrap).GetMethod("ApplyConversationLocationWhileOpaque", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(bootstrap, new object[] { binding });
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void DestroyFixtures(params GameObject[] fixtures)
        {
            foreach (GameObject fixture in fixtures)
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }
    }
}
