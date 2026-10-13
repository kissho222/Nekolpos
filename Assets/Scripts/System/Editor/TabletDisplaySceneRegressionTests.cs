using System.IO;
using Nekolpos.Data;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Nekolpos.System.Editor
{
    public sealed class TabletDisplaySceneRegressionTests
    {
        private const string TitleScenePath = "Assets/Scenes/MainScene.unity";
        private const string DiaryControllerPath = "Assets/Scripts/System/DiaryCalendarController.cs";
        private const string TimedEventCsvPath = "TalkSource/TalkCSV/SystemTimedEvent.csv";

        [Test]
        public void RuntimeDiaryFormatterResolvesEveryProvidedTokenInOnePass()
        {
            var values = new Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase)
            {
                ["PLAYER_CALLING"] = "呼称", ["PLAYER_INPUT"] = "入力", ["PENDING_VALUE"] = "候補",
                ["TIME_OF_DAY"] = "朝", ["NEXT_TIME_OF_DAY"] = "昼", ["WEATHER"] = "晴れ",
                ["CURRENTWEATHER"] = "Sunny", ["TOMORROWWEATHER"] = "雨", ["FORECAST_WEATHER"] = "雨",
                ["FORCAST_WEATHER"] = "雨", ["FORCAST?WEATHER"] = "雨", ["CURRENTWEATHERFORECAST"] = "Rainy",
                ["NEXTACTUALWEATHER"] = "Cloudy", ["WEATHERFORECASTCORRECT"] = "true", ["FutureVariable"] = "拡張"
            };
            var source = new DialogueReactionData { RuntimePlaceholders = values };
            foreach (var pair in values)
            {
                string token = pair.Key.ToLowerInvariant().Replace("_", @"\_");
                Assert.That(DialogueManager.ResolveRuntimeText("<b>{{ " + token + " }}</b>", responseEntry: source),
                    Is.EqualTo("<b>" + pair.Value + "</b>"), pair.Key);
            }
            source.RuntimePlaceholders["PLAYER_INPUT"] = "{{PLAYER_CALLING}}";
            Assert.That(DialogueManager.ResolveRuntimeText("{{PLAYER_INPUT}}", responseEntry: source),
                Is.EqualTo("{{PLAYER_CALLING}}"), "Resolved values are literal, not new templates.");
            Assert.That(DialogueManager.ResolveRuntimeText("{{MISSING_DIARY_VALUE}}/{{broken"),
                Is.EqualTo("{{MISSING_DIARY_VALUE}}/{{broken"), "Unknown values must not be fabricated or removed.");
        }

        [Test]
        public void DiaryFormatterWithoutManagerResolvesAllGameStateValueKinds()
        {
            var host = new GameObject("Diary variable context regression");
            host.SetActive(false);
            var stateManager = Object.FindFirstObjectByType<Backgammon.Conversation.ConversationGameStateManager>(FindObjectsInactive.Include);
            if (stateManager == null) stateManager = host.AddComponent<Backgammon.Conversation.ConversationGameStateManager>();
            string original = stateManager.State.SaveToJson();
            try
            {
                var state = stateManager.State;
                state.SetString("DiaryString", "状態");
                state.SetInt("DiaryNumber", 12);
                state.SetBool("DiaryFlag", true);
                state.SetStringList("DiaryList", new[] { "A", "B" });
                var method = typeof(DiaryCalendarController).GetMethod("ResolveDiaryBody", BindingFlags.Static | BindingFlags.NonPublic);
                string resolved = (string)method.Invoke(null, new object[] { "{{ diarystring }}/{DiaryNumber}/{{DiaryFlag}}/{{DiaryList}}" });
                Assert.That(resolved, Is.EqualTo("状態/12/true/A, B"));
            }
            finally
            {
                stateManager.State.LoadFromJson(original);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpecialDiaryThumbnail_IncludesDateAndAuthoredPreview()
        {
            var method = typeof(DiaryCalendarController).GetMethod("FormatSpecialDayCellText", BindingFlags.Static | BindingFlags.NonPublic);
            string preview = "確認用の日記本文が長く続きます";
            string text = (string)method.Invoke(null, new object[] { new global::System.DateTime(2026, 10, 1), preview });
            Assert.That(text, Does.Contain("10/1"));
            Assert.That(text, Does.Contain("確認用の日記本文"));
            Assert.That(text, Does.Contain("<size=16>"));
        }

        [Test]
        public void DiaryBodyScroll_ClipsLongEntriesAndCanReachTheirEnd()
        {
            var canvas = new GameObject("Diary scroll regression", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var bodyObject = new GameObject("Body", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            bodyObject.transform.SetParent(canvas.transform, false);
            try
            {
                var body = bodyObject.GetComponent<TMPro.TMP_Text>();
                body.font = TMPro.TMP_Settings.defaultFontAsset;
                body.fontSize = 24f;
                body.rectTransform.anchorMin = body.rectTransform.anchorMax = Vector2.one * 0.5f;
                body.rectTransform.sizeDelta = new Vector2(300f, 120f);
                body.text = string.Join("\n", global::System.Linq.Enumerable.Range(1, 40));
                var method = typeof(DiaryCalendarController).GetMethod("EnsureBodyScroll", BindingFlags.Static | BindingFlags.NonPublic);
                var scroll = (ScrollRect)method.Invoke(null, new object[] { body });
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
                Assert.That(body.raycastTarget, Is.False);
                Assert.That(scroll.viewport.GetComponent<Image>().raycastTarget, Is.True);
                scroll.verticalNormalizedPosition = 0f;
                Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f));
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0f).Within(0.001f));
                Assert.That(method.Invoke(null, new object[] { body }), Is.SameAs(scroll));
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void DetailPagePaw_IsVisibleForSpecialEntriesAndDoesNotBlockInput()
        {
            var root = new GameObject("Detail paw regression");
            root.SetActive(false);
            try
            {
                var diary = root.AddComponent<DiaryCalendarController>();
                var title = new GameObject("DetailTitle", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                title.transform.SetParent(root.transform, false);
                typeof(DiaryCalendarController).GetField("detailTitleText", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(diary, title.GetComponent<TMPro.TMP_Text>());
                var pawObject = new GameObject("SpecialDiaryPaw", typeof(RectTransform), typeof(Image));
                pawObject.transform.SetParent(root.transform, false);
                var authoredPaw = pawObject.GetComponent<Image>();
                authoredPaw.sprite = Resources.Load<Sprite>("Diary/肉球マーク");
                authoredPaw.raycastTarget = false;
                typeof(DiaryCalendarController).GetField("detailView", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(diary, root);
                var method = typeof(DiaryCalendarController).GetMethod("SetDetailPaw", BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(diary, new object[] { true });
                Image paw = root.transform.Find("SpecialDiaryPaw").GetComponent<Image>();
                Assert.That(paw.sprite, Is.Not.Null);
                Assert.That(paw.raycastTarget, Is.False);
                method.Invoke(diary, new object[] { false });
                Assert.That(paw.gameObject.activeSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityEngine.TestTools.UnityTest]
        public global::System.Collections.IEnumerator SpecialWritingFinishButton_ReceivesRaycastAfterHiddenReferenceRefresh()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(TitleScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Scene main = SceneManager.GetSceneByPath(TitleScenePath);
            SceneManager.SetActiveScene(main);
            var sceneBootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
            if (!sceneBootstrap.enabled)
            {
                sceneBootstrap.enabled = true;
                typeof(NekolposGameBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sceneBootstrap, null);
            }
            yield return new WaitForSecondsRealtime(0.2f);
            GameObject original = (GameObject)typeof(DiaryCalendarController).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(DiaryCalendarController.Instance);
            var canvas = new GameObject("Rewrite raycast regression", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            canvas.GetComponent<Canvas>().scaleFactor = Mathf.Min(Screen.width / 1871f, Screen.height / 1213f);
            var host = new GameObject("Diary input regression");
            host.SetActive(false);
            GameObject events = null;
            var previousFocus = UnityEditor.EditorWindow.focusedWindow;
            var gameView = global::System.Linq.Enumerable.First(Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>(), window => window.titleContent.text == "Game");
            gameView.Focus();
            try
            {
                GameObject panel = UnityEngine.Object.Instantiate(original, canvas.transform, false);
                panel.SetActive(true);
                var panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
                var panelGroup = panel.GetComponent<CanvasGroup>();
                if (panelGroup != null)
                {
                    panelGroup.alpha = 1f;
                    panelGroup.blocksRaycasts = true;
                    panelGroup.interactable = true;
                }
                var diary = host.AddComponent<DiaryCalendarController>();
                typeof(DiaryCalendarController).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(diary, panel);
                typeof(DiaryCalendarController).GetMethod("ResolveCalendarReferences", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(diary, null);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var writing = (GameObject)typeof(DiaryCalendarController).GetField("writingView", flags).GetValue(diary);
                typeof(DiaryCalendarController).GetMethod("HideApplicationWindows", flags).Invoke(diary, null);
                typeof(DiaryCalendarController).GetMethod("ShowWindowImmediately", flags).Invoke(diary, new object[] { writing });
                var prompt = (GameObject)typeof(DiaryCalendarController).GetField("promptParentPanel", flags).GetValue(diary);
                prompt.SetActive(false);
                typeof(DiaryCalendarController).GetMethod("ShowWritingFinish", flags).Invoke(diary, null);
                // Reopening special writing refreshes references with hidden actions.
                // Existing hit areas must survive that refresh and be clickable again.
                var finish = (Button)typeof(DiaryCalendarController).GetField("writingFinishButton", flags).GetValue(diary);
                finish.gameObject.SetActive(false);
                typeof(DiaryCalendarController).GetMethod("ResolveCalendarReferences", flags).Invoke(diary, null);
                Assert.That(finish.transform.Find("DiaryButtonHitArea").GetComponent<Image>().raycastTarget, Is.True);
                finish.transform.Find("DiaryButtonHitArea").GetComponent<Image>().raycastTarget = false;
                typeof(DiaryCalendarController).GetField("confirmingSpecialEntries", flags).SetValue(diary, true);
                typeof(DiaryCalendarController).GetMethod("ShowWritingFinish", flags).Invoke(diary, null);
                var rewrite = (Button)typeof(DiaryCalendarController).GetField("reWritingButton", flags).GetValue(diary);
                Assert.That(rewrite.gameObject.activeSelf, Is.False);
                var button = finish;
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem == null)
                {
                    events = new GameObject("Rewrite raycast events", typeof(UnityEngine.EventSystems.EventSystem));
                    eventSystem = events.GetComponent<UnityEngine.EventSystems.EventSystem>();
                }
                yield return null;
                Canvas.ForceUpdateCanvases();
                var rect = button.GetComponent<RectTransform>();
                var hitArea = button.transform.Find("DiaryButtonHitArea").GetComponent<Image>();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (hitArea.depth < 0)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Game View must render the button hit area.");
                    gameView.Repaint();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                }
                var buttonCanvas = button.GetComponentInParent<Canvas>();
                var raycaster = buttonCanvas.GetComponent<GraphicRaycaster>() ?? buttonCanvas.gameObject.AddComponent<GraphicRaycaster>();
                foreach (float x in new[] { 0.1f, 0.5f, 0.9f })
                foreach (float y in new[] { 0.1f, 0.5f, 0.9f })
                {
                    Vector3 local = new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, x), Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, y));
                    var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem)
                    { position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(local)) };
                    var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                    raycaster.Raycast(pointer, hits);
                    Assert.That(hits, Is.Not.Empty, $"No hit at {x}/{y}, canvas={buttonCanvas.name}, depth={button.GetComponent<Graphic>().depth}");
                    string hitNames = string.Join(", ", hits.ConvertAll(hit => hit.gameObject.name + ":" + hit.depth));
                    Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), $"Covered at {x}/{y}: {hitNames}, buttonDepth={button.GetComponent<Graphic>().depth}, buttonRaycast={button.GetComponent<Graphic>().raycastTarget}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(canvas);
                if (events != null) UnityEngine.Object.DestroyImmediate(events);
                if (previousFocus != null) previousFocus.Focus();
            }
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        [TestCase(2026, 9, 21, "9/21の日記")]
        [TestCase(2026, 12, 3, "12/3の日記")]
        public void DiaryDetailTitle_UsesTheSelectedDate(int year, int month, int day, string expected)
        {
            MethodInfo formatTitle = typeof(DiaryCalendarController).GetMethod(
                "FormatDetailTitle",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(formatTitle, Is.Not.Null);
            Assert.That(formatTitle.Invoke(null, new object[] { new global::System.DateTime(year, month, day) }), Is.EqualTo(expected));
        }

        [Test]
        public void DiaryDetail_ConnectsBothDayNavigationButtons()
        {
            string controller = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("FindChildComponent<Button>(\"BeforeDayButton\")", controller);
            StringAssert.Contains("FindChildComponent<Button>(\"NextDayButton\")", controller);
            StringAssert.Contains("BindButton(beforeDayButton, () => MoveDetailDay(-1))", controller);
            StringAssert.Contains("BindButton(nextDayButton, () => MoveDetailDay(1))", controller);
        }

        [Test]
        public void TabletButton_AvailabilitySynchronizesClickabilityWithVisibility()
        {
            string bootstrap = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");

            StringAssert.Contains("bool shouldBeVisible = CanOpenTabletFromButton();", bootstrap);
            StringAssert.Contains("tabletButton.interactable != shouldBeVisible", bootstrap);
            StringAssert.Contains("tabletButton.interactable = shouldBeVisible;", bootstrap);
        }

        [Test]
        public void DiaryViews_UseOneDirectionAwareHorizontalSlidePath()
        {
            string controller = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("private enum SlideDirection", controller);
            StringAssert.Contains("SlideWindowRoutine(target, direction)", controller);
            StringAssert.Contains("BuildCalendar(direction: SlideDirection.Backward)", controller);
            StringAssert.Contains("Mathf.SmoothStep", controller);
            StringAssert.Contains("SetWindowInteraction(target, false)", controller);
        }

        [Test]
        public void DiaryDetailPageTurn_SlidesOnlyTheDetailBackImagePrefab()
        {
            string controller = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("Instantiate(outgoingPage, outgoingPage.transform.parent, false)", controller);
            StringAssert.Contains("incomingPage.name = \"DetailBackImageIncoming\"", controller);
            StringAssert.Contains("SlideDetailPageRoutine(nextDate, notes, direction)", controller);
            StringAssert.Contains("SetWindowInteraction(detailView, false)", controller);
            StringAssert.Contains("detailBackImage = incomingPage", controller);
        }

        [Test]
        public void DiaryDetailPage_ReplacesDestroyedCanvasGroupsBeforeAnimating()
        {
            string controller = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("CanvasGroup group = target.GetComponent<CanvasGroup>();", controller);
            StringAssert.Contains("if (group == null)", controller);
            StringAssert.Contains("group = target.AddComponent<CanvasGroup>();", controller);
        }

        [Test]
        public void DiaryTransitionViewport_ClipsPagesToTheTabletBounds()
        {
            GameObject controllerObject = new GameObject("Diary transition viewport fixture");
            controllerObject.SetActive(false);
            GameObject panel = new GameObject("DiaryPanel", typeof(RectTransform));

            try
            {
                DiaryCalendarController controller = controllerObject.AddComponent<DiaryCalendarController>();
                FieldInfo panelField = typeof(DiaryCalendarController).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo ensureMask = typeof(DiaryCalendarController).GetMethod("EnsureTransitionViewportMask", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(panelField, Is.Not.Null);
                Assert.That(ensureMask, Is.Not.Null);
                panelField.SetValue(controller, panel);
                ensureMask.Invoke(controller, null);

                Assert.That(panel.GetComponent<RectMask2D>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void SpecialDiaryDates_AreUniqueSortedAndUsedForDetailPaging()
        {
            GameObject root = new GameObject("Special diary fixture");
            root.SetActive(false);

            try
            {
                DiaryCalendarController controller = root.AddComponent<DiaryCalendarController>();
                FieldInfo entriesField = typeof(DiaryCalendarController).GetField(
                    "specialEntriesByDate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo dateListMethod = typeof(DiaryCalendarController).GetMethod(
                    "GetSpecialEntryDates",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo adjacentMethod = typeof(DiaryCalendarController).GetMethod(
                    "FindAdjacentSpecialEntryDate",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(entriesField, Is.Not.Null);
                Assert.That(dateListMethod, Is.Not.Null);
                Assert.That(adjacentMethod, Is.Not.Null);
                var entries = (Dictionary<string, List<string>>)entriesField.GetValue(controller);
                entries["2026-09-21"] = new List<string> { "A", "B" };
                entries["2026-09-03"] = new List<string> { "C" };
                entries["2026-09-08"] = new List<string> { "D" };

                var dates = (List<global::System.DateTime>)dateListMethod.Invoke(controller, null);
                Assert.That(dates, Is.EqualTo(new[]
                {
                    new global::System.DateTime(2026, 9, 3),
                    new global::System.DateTime(2026, 9, 8),
                    new global::System.DateTime(2026, 9, 21),
                }));
                Assert.That(
                    adjacentMethod.Invoke(controller, new object[] { new global::System.DateTime(2026, 9, 8), 1 }),
                    Is.EqualTo(new global::System.DateTime(2026, 9, 21)));
                Assert.That(
                    adjacentMethod.Invoke(controller, new object[] { new global::System.DateTime(2026, 9, 8), -1 }),
                    Is.EqualTo(new global::System.DateTime(2026, 9, 3)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SpecialDiaryDuplicates_ShowEarliestDateAndCountWithoutChangingSavedEntries()
        {
            var root = new GameObject("Grouped special diary regression");
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<DiaryCalendarController>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var entries = (Dictionary<string, List<string>>)typeof(DiaryCalendarController)
                    .GetField("specialEntriesByDate", flags).GetValue(controller);
                entries["2026-10-13"] = new List<string> { "A" };
                entries["2026-10-11"] = new List<string> { "A", "B" };
                entries["2026-10-12"] = new List<string> { "A", "A " };
                entries["2026-10-10"] = new List<string> { "A" };
                object[] args = { null };
                var grouped = (Dictionary<global::System.DateTime, List<string>>)typeof(DiaryCalendarController)
                    .GetMethod("BuildSpecialEntryGroups", flags).Invoke(controller, args);
                var counts = (Dictionary<global::System.DateTime, int>)args[0];
                var first = new global::System.DateTime(2026, 10, 10);
                Assert.That(grouped[first], Is.EqualTo(new[] { "A" }));
                Assert.That(counts[first], Is.EqualTo(3));
                Assert.That(grouped[new global::System.DateTime(2026, 10, 11)], Is.EqualTo(new[] { "B" }));
                Assert.That(grouped[new global::System.DateTime(2026, 10, 12)], Is.EqualTo(new[] { "A " }));
                Assert.That(grouped.ContainsKey(new global::System.DateTime(2026, 10, 13)), Is.False);
                Assert.That(entries["2026-10-11"], Is.EqualTo(new[] { "A", "B" }));
                Assert.That(entries.Count, Is.EqualTo(4));
                var label = (string)typeof(DiaryCalendarController).GetMethod("FormatSpecialDateLabel", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { first, counts[first] });
                Assert.That(label, Is.EqualTo("10/10 他3件"));
                var title = new GameObject("DetailTitle", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                title.transform.SetParent(root.transform);
                typeof(DiaryCalendarController).GetField("detailTitleText", flags).SetValue(controller, title.GetComponent<TMPro.TMP_Text>());
                typeof(DiaryCalendarController).GetMethod("ApplyDetailEntry", flags).Invoke(controller, new object[] { first, grouped[first], true });
                Assert.That(title.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("10/10 他3件の日記"));
                Assert.That(typeof(DiaryCalendarController).GetMethod("FindAdjacentSpecialEntryDate", flags)
                    .Invoke(controller, new object[] { new global::System.DateTime(2026, 10, 12), 1 }), Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SpecialDiarySortUnlock_IsPersistedInPlayerPrefs()
        {
            const string key = "Nekolpos.Diary.SpecialSortUnlocked.v1";
            bool hadValue = PlayerPrefs.HasKey(key);
            int originalValue = PlayerPrefs.GetInt(key, 0);

            try
            {
                PlayerPrefs.DeleteKey(key);
                DiaryJournalStore.UnlockSpecialDiarySort();
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
            }
            finally
            {
                if (hadValue)
                {
                    PlayerPrefs.SetInt(key, originalValue);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }

                PlayerPrefs.Save();
            }
        }

        [Test]
        public void TemporarySpecialDiary_UsesTheSpecialDiaryDataAndPawAsset()
        {
            string csv = File.ReadAllText(TimedEventCsvPath);
            string controller = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("TEMP_SPECIAL_DIARY_001", csv);
            StringAssert.Contains("TEMP_SPECIAL_DIARY_001", controller);
            StringAssert.Contains("PawSpriteResourcePath", controller);
            Assert.That(File.Exists("Assets/Resources/Diary/肉球マーク.png"), Is.True);
        }

        [Test]
        public void DebugOctoberFirstSpecialDiary_UsesGameStartDateAndDoesNotReadThePcClock()
        {
            MethodInfo dayNumberMethod = typeof(DiaryCalendarController).GetMethod(
                "CalculateDebugOctoberFirstDayNumber",
                BindingFlags.Static | BindingFlags.NonPublic);
            string controller = File.ReadAllText(DiaryControllerPath);

            Assert.That(dayNumberMethod, Is.Not.Null);
            Assert.That(dayNumberMethod.Invoke(null, new object[] { new global::System.DateTime(2026, 9, 25) }), Is.EqualTo(7));
            Assert.That(dayNumberMethod.Invoke(null, new object[] { new global::System.DateTime(2026, 10, 2) }), Is.EqualTo(365));
            StringAssert.Contains("TEMP_SPECIAL_DIARY_OCT01", controller);
            StringAssert.Contains("timeManager.SetCurrentTime(targetDay, timeManager.CurrentPeriod)", controller);
            StringAssert.DoesNotContain("DateTime.Today", controller.Substring(controller.IndexOf("AddDebugOctoberFirstSpecialDiary", global::System.StringComparison.Ordinal)));
        }

        [Test]
        public void DebugDiaryCompletion_PersistsNextDayAndExposesResetControls()
        {
            string diaryController = File.ReadAllText(DiaryControllerPath);
            string bootstrap = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");
            string debugPanel = File.ReadAllText("Assets/Scripts/System/NekolposDebugPanel.cs");

            StringAssert.Contains("CompleteNightDiary", diaryController);
            StringAssert.Contains("DiscardUnconfirmedDebugNightDiary", diaryController);
            StringAssert.Contains("DiaryJournalStore.DiscardNormalDraftsForDay", diaryController);
            StringAssert.DoesNotContain("timeManager.AdvanceToNextMorning()", diaryController);
            StringAssert.Contains("timeManager.AdvanceToNextMorning()", File.ReadAllText("Assets/Scripts/System/DialogueManager.cs"));
            StringAssert.Contains("SaveDebugStartDay", bootstrap);
            StringAssert.Contains("TimeManager.CurrentDayPrefsKey", bootstrap);
            StringAssert.Contains("ResetDateButton", debugPanel);
            StringAssert.Contains("ResetDiaryButton", debugPanel);
            StringAssert.Contains("ResetGameStartDateForDebug", debugPanel);
            StringAssert.Contains("ResetAllDiaryDataForDebug", debugPanel);
        }

        [Test]
        public void TabletCanvas_IsOnTheCameraSideOfDisplayArea()
        {
            using var scope = new MainSceneScope();
            Transform canvas = FindSceneObject(scope.Scene, "TabletCanvas").transform;
            Transform emission = FindSceneObject(scope.Scene, "EmissionPanel  ").transform;
            Assert.That(canvas.localPosition.z, Is.GreaterThan(0f));
            Assert.That(canvas.localPosition.z, Is.GreaterThan(emission.localPosition.z));
        }

        [Test]
        public void TabletCanvas_PreservesTheVerifiedOrientation()
        {
            using var scope = new MainSceneScope();
            Quaternion rotation = FindSceneObject(scope.Scene, "TabletCanvas").transform.localRotation;
            Assert.That(rotation.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(rotation.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(rotation.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(rotation.w, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void TabletPowerOn_RestoresCanvasGroupVisibility()
        {
            GameObject bootstrapObject = new GameObject("Tablet power regression fixture");
            bootstrapObject.SetActive(false);
            GameObject displayCanvas = new GameObject("Tablet display fixture", typeof(CanvasGroup));
            GameObject emissionPanel = new GameObject("Tablet emission fixture");
            displayCanvas.SetActive(false);
            emissionPanel.SetActive(false);

            try
            {
                CanvasGroup group = displayCanvas.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;

                NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "tabletDisplayCanvas", displayCanvas);
                SetField(bootstrap, "tabletEmissionPanel", emissionPanel);

                Invoke(bootstrap, "SetTabletDisplayPowered");

                Assert.That(displayCanvas.activeSelf, Is.True);
                Assert.That(emissionPanel.activeSelf, Is.True);
                Assert.That(group.alpha, Is.EqualTo(1f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(displayCanvas);
                Object.DestroyImmediate(emissionPanel);
            }
        }

        [Test]
        public void PromptParentPanel_IsIndependentOfWritingView()
        {
            using var scope = new MainSceneScope();
            Transform diaryPanel = FindSceneObject(scope.Scene, "DiaryPanel").transform;
            Transform writingView = FindSceneObject(scope.Scene, "WritingView").transform;
            Transform prompt = FindSceneObject(scope.Scene, "PromptParentPanel").transform;
            Assert.That(prompt.parent, Is.EqualTo(diaryPanel));
            Assert.That(prompt.parent, Is.Not.EqualTo(writingView));
        }

        [Test]
        public void DiaryCalendar_SeparatesManualAndNightEntryPoints()
        {
            string source = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("OpenCalendar(playIconTransition: true);", source);
            StringAssert.Contains("BuildCalendar(direction: SlideDirection.Backward);", source);
            StringAssert.Contains("OpenApplication(panel, playIconTransition: true)", source);
            StringAssert.Contains("ResetWritingPresentation();", source);
        }

        [Test]
        public void CalendarDayCell_UsesLargeTopLeftDateAndShortPreview()
        {
            MethodInfo format = typeof(DiaryCalendarController).GetMethod(
                "FormatDayCellText",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(format, Is.Not.Null);
            string result = (string)format.Invoke(null, new object[] { 14, "1234567890123" });

            Assert.That(result, Is.EqualTo("<size=35>14</size>\n<size=16>123456789012…</size>"));
            StringAssert.Contains("TextAlignmentOptions.TopLeft", File.ReadAllText(DiaryControllerPath));
        }

        [Test]
        public void TabletPowerReturn_UsesReciprocalEaseOutInsteadOfSnapping()
        {
            MethodInfo easing = typeof(NekolposGameBootstrap).GetMethod(
                "EvaluateReciprocalEaseOut",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(easing, Is.Not.Null);
            float halfway = (float)easing.Invoke(null, new object[] { 0.5f, 2f });
            Assert.That(halfway, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That((float)easing.Invoke(null, new object[] { 0f, 2f }), Is.EqualTo(0f));
            Assert.That((float)easing.Invoke(null, new object[] { 1f, 2f }), Is.EqualTo(1f));

            string source = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");
            StringAssert.Contains("ExitTabletCameraView(useReciprocalEaseOut: true)", source);
        }

        [TestCase("NyanstaIcon", "NyanstaPanel")]
        [TestCase("ShopIcon", "ShopPanel")]
        [TestCase("demaeIcon", "DemaePanel")]
        [TestCase("GameIcon", "GameCenterPanel")]
        public void TabletHome_MapsEachApplicationIconToItsWindow(string iconName, string expectedPanelName)
        {
            Assert.That(
                TabletHomeApplicationController.TryGetPanelName(iconName, out string panelName),
                Is.True);
            Assert.That(panelName, Is.EqualTo(expectedPanelName));
        }

        [Test]
        public void TabletApplicationClose_RevealsHomeBeforeFinishingTheShrinkAnimation()
        {
            GameObject transitionObject = new GameObject("Tablet transition fixture", typeof(RectTransform));
            GameObject icon = new GameObject("Icon fixture", typeof(RectTransform));
            GameObject target = new GameObject("Target fixture", typeof(RectTransform), typeof(CanvasGroup));
            GameObject home = new GameObject("Home fixture", typeof(RectTransform), typeof(CanvasGroup));
            home.SetActive(false);

            try
            {
                TabletApplicationWindowTransition transition = transitionObject.AddComponent<TabletApplicationWindowTransition>();
                transition.PlayClose((RectTransform)icon.transform, target, home, 0.01f, 0.1f);

                Assert.That(home.activeSelf, Is.True);
                Assert.That(target.activeSelf, Is.True, "The target remains active until its shrink animation finishes.");
            }
            finally
            {
                Object.DestroyImmediate(transitionObject);
                Object.DestroyImmediate(icon);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(home);
            }
        }

        [Test]
        public void TabletHome_RepairsSavedRuntimePagesAndUsesOneSharedDiaryTransition()
        {
            string homeSource = File.ReadAllText("Assets/Scripts/System/TabletHomeApplicationController.cs");
            string diarySource = File.ReadAllText(DiaryControllerPath);

            StringAssert.Contains("ResolvePanelRoot(iconParent, existingStartPanel)", homeSource);
            StringAssert.Contains("PrepareApplicationWindow(page);", homeSource);
            StringAssert.Contains("Stretch(rect);", homeSource);
            StringAssert.Contains("OpenApplication(panel, playIconTransition: true)", diarySource);
            StringAssert.DoesNotContain("panel.AddComponent<TabletApplicationWindowTransition>()", diarySource);
        }

        [Test]
        public void TabletWindowTransition_RestoresTheCompletedPositionWhenInterrupted()
        {
            string source = File.ReadAllText("Assets/Scripts/System/TabletApplicationWindowTransition.cs");

            StringAssert.Contains("private Vector3 activeTargetPosition;", source);
            StringAssert.Contains("activeTarget.position = activeTargetPosition;", source);
            StringAssert.Contains("activeTarget.localScale = activeTargetScale;", source);
            StringAssert.Contains("GetOrAddCanvasGroup", source);
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.scene == scene && candidate.name == objectName) return candidate;
            }

            Assert.Fail($"{objectName} was not found in {TitleScenePath}.");
            return null;
        }

        private sealed class MainSceneScope : global::System.IDisposable
        {
            public MainSceneScope()
            {
                Scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);
            }

            public Scene Scene { get; }
            public void Dispose() => EditorSceneManager.CloseScene(Scene, removeScene: true);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} was not found.");
            field.SetValue(target, value);
        }

        private static void Invoke(NekolposGameBootstrap target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName} was not found.");
            method.Invoke(target, new object[] { true });
        }
    }
}
