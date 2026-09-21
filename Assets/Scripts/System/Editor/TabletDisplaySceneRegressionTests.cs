using System.Globalization;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System.Editor
{
    public sealed class TabletDisplaySceneRegressionTests
    {
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
        private const string DiaryControllerPath = "Assets/Scripts/System/DiaryCalendarController.cs";
        private const string TimedEventCsvPath = "TalkSource/TalkCSV/SystemTimedEvent.csv";

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
        public void TabletCanvas_IsOnTheCameraSideOfDisplayArea()
        {
            string scene = File.ReadAllText(TitleScenePath);
            float canvasZ = ReadLocalZ(scene, "TabletCanvas");
            float emissionZ = ReadLocalZ(scene, "EmissionPanel  ");

            Assert.That(canvasZ, Is.GreaterThan(0f), "TabletCanvas must stay on DisplayArea's camera-facing side.");
            Assert.That(canvasZ, Is.GreaterThan(emissionZ), "TabletCanvas must render in front of EmissionPanel.");
        }

        [Test]
        public void TabletCanvas_PreservesTheVerifiedOrientation()
        {
            string scene = File.ReadAllText(TitleScenePath);
            string canvas = FindObjectSection(scene, "TabletCanvas");

            StringAssert.Contains("m_LocalRotation: {x: 0, y: 1, z: 0, w: 0}", canvas);
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

                OpenBetaTitleBootstrap bootstrap = bootstrapObject.AddComponent<OpenBetaTitleBootstrap>();
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
            string scene = File.ReadAllText(TitleScenePath);
            Match diaryPanelTransform = FindRectTransformSection(scene, "DiaryPanel");
            Match writingViewTransform = FindRectTransformSection(scene, "WritingView");
            Match promptTransform = FindRectTransformSection(scene, "PromptParentPanel");

            StringAssert.Contains(
                $"m_Father: {{fileID: {diaryPanelTransform.Groups["id"].Value}}}",
                promptTransform.Value);
            StringAssert.DoesNotContain(
                $"- {{fileID: {promptTransform.Groups["id"].Value}}}",
                writingViewTransform.Value);
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
            MethodInfo easing = typeof(OpenBetaTitleBootstrap).GetMethod(
                "EvaluateReciprocalEaseOut",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(easing, Is.Not.Null);
            float halfway = (float)easing.Invoke(null, new object[] { 0.5f, 2f });
            Assert.That(halfway, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That((float)easing.Invoke(null, new object[] { 0f, 2f }), Is.EqualTo(0f));
            Assert.That((float)easing.Invoke(null, new object[] { 1f, 2f }), Is.EqualTo(1f));

            string source = File.ReadAllText("Assets/Scripts/System/OpenBetaTitleBootstrap.cs");
            StringAssert.Contains("ExitTabletCameraView(useReciprocalEaseOut: true)", source);
        }

        [TestCase("NyanstaIcon", "NyanstaPanel")]
        [TestCase("ShopIcon", "ShopPanel")]
        [TestCase("demaeIcon", "DemaePanel")]
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

        private static float ReadLocalZ(string scene, string objectName)
        {
            string section = FindObjectSection(scene, objectName);
            Match position = Regex.Match(
                section,
                @"m_LocalPosition: \{x: [^,]+, y: [^,]+, z: (?<z>[^\}]+)\}");

            Assert.That(position.Success, Is.True, $"{objectName} local position was not found.");
            return float.Parse(position.Groups["z"].Value, CultureInfo.InvariantCulture);
        }

        private static string FindObjectSection(string scene, string objectName)
        {
            Match match = Regex.Match(
                scene,
                $@"(?m)^  m_Name: '?{Regex.Escape(objectName)}'?\r?$");

            Assert.That(match.Success, Is.True, $"{objectName} was not found in {TitleScenePath}.");
            int sectionStart = scene.LastIndexOf("--- !u!1 ", match.Index, global::System.StringComparison.Ordinal);
            int sectionEnd = scene.IndexOf("--- !u!1 ", match.Index + match.Length, global::System.StringComparison.Ordinal);
            if (sectionEnd < 0)
            {
                sectionEnd = scene.Length;
            }

            return scene.Substring(sectionStart, sectionEnd - sectionStart);
        }

        private static Match FindRectTransformSection(string scene, string objectName)
        {
            string objectSection = FindObjectSection(scene, objectName);
            Match gameObject = Regex.Match(objectSection, @"^--- !u!1 &(?<id>\d+)\r?$", RegexOptions.Multiline);
            Assert.That(gameObject.Success, Is.True, $"{objectName} GameObject ID was not found.");

            MatchCollection transforms = Regex.Matches(
                scene,
                @"(?ms)^--- !u!224 &(?<id>\d+)\r?\nRectTransform:.*?(?=^--- !u!)");
            foreach (Match transform in transforms)
            {
                if (transform.Value.Contains($"m_GameObject: {{fileID: {gameObject.Groups["id"].Value}}}"))
                {
                    return transform;
                }
            }

            Assert.Fail($"{objectName} RectTransform was not found.");
            return Match.Empty;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} was not found.");
            field.SetValue(target, value);
        }

        private static void Invoke(OpenBetaTitleBootstrap target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName} was not found.");
            method.Invoke(target, new object[] { true });
        }
    }
}
