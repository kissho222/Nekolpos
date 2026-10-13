using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Backgammon.Conversation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace Nekolpos.EditorTools
{
    public sealed class DialogueAuthoringUiTests
    {
        private const string SessionKey = "Nekolpos.DialoguePreviewWindow.SessionState";
        private string directory, session, metadata;
        private bool hadSession, hadMetadata;
        private DialoguePreviewWindow window;
        private bool windowShown;
        private readonly List<string> stateKeys = new();
        private static readonly BindingFlags InstanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags StaticFlags = BindingFlags.NonPublic | BindingFlags.Static;
        [SetUp] public void SetUp()
        {
            window = null; windowShown = false;
            directory = Path.Combine(Path.GetTempPath(), "NekolposAuthoringUI_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            hadSession = EditorPrefs.HasKey(SessionKey); session = EditorPrefs.GetString(SessionKey);
            hadMetadata = File.Exists(DialogueAuthoringIdentity.MetadataPath); if (hadMetadata) metadata = File.ReadAllText(DialogueAuthoringIdentity.MetadataPath);
        }
        [TearDown] public void TearDown()
        {
            if (window != null)
            {
                if (windowShown) window.Close(); else UnityEngine.Object.DestroyImmediate(window);
                window = null;
            }
            if (hadSession) EditorPrefs.SetString(SessionKey, session); else EditorPrefs.DeleteKey(SessionKey);
            foreach (string key in stateKeys) EditorPrefs.DeleteKey(key); stateKeys.Clear();
            if (hadMetadata) File.WriteAllText(DialogueAuthoringIdentity.MetadataPath, metadata); else if (File.Exists(DialogueAuthoringIdentity.MetadataPath)) File.Delete(DialogueAuthoringIdentity.MetadataPath);
            Directory.Delete(directory, true);
        }
        [Test] public void ProfilesExposeOnlySupportedFeaturesAndJapaneseNames()
        {
            var profiles = DialogueAuthoringProfile.Available(); Assert.That(profiles.Count, Is.GreaterThanOrEqualTo(9));
            foreach (var profile in profiles)
            {
                Assert.That(File.Exists(profile.Path), Is.True); Assert.That(profile.Name, Does.Not.EndWith(".csv"));
                var data = DialogueDataLoader.LoadFromPath(profile.Path); Assert.That(data.Entries, Is.Not.Empty);
                if (profile.Teaching) Assert.That(profile.Sections, Is.EqualTo(new[] { "漢字読み", "教示設定" }));
                else Assert.That(profile.Sections, Is.EqualTo(new[] { "時間経過・日記", "話題候補", "漢字読み" }));
            }
            Assert.That(DialogueAuthoringProfile.Resolve("DialoguePreview.csv").Diary, Is.True);
            Assert.That(DialogueAuthoringProfile.Resolve("teacchseries_dialogue_source_2201.csv").TimedEvent, Is.False);
        }
        [TestCase("DialoguePreview.csv")]
        [TestCase("teacchFelidae.csv")]
        [TestCase("teacchseries_dialogue_source_2201.csv")]
        [TestCase("teacchTalk_body.csv")]
        [TestCase("teacchCatExpression.csv")]
        [TestCase("teacchCatBreed.csv")]
        [TestCase("teacchFavoriteWeather.csv")]
        [TestCase("PsychologyReactionDialogue.csv")]
        [TestCase("InternalDialogue.csv")]
        public void EveryManuscriptSavesToItsOwnPathPreservingHiddenLanguagesAndUnknownColumns(string file)
        {
            var data = DialogueDataLoader.LoadFromPath("TalkSource/TalkCSV/" + file);
            data.Entries[0].TextEnglish = "retained-en"; data.Entries[0].TextChinese = "retained-zh";
            data.Headers.Add("CustomAuthoringColumn"); data.Entries[0].SetAdditionalValue("CustomAuthoringColumn", "keep");
            string path = Path.Combine(directory, file); data.SavePath = path;
            DialogueDataLoader.SaveToPath(data, data.SavePath);
            var restored = DialogueDataLoader.LoadFromPath(path);
            Assert.That(restored.Entries[0].TextEnglish, Is.EqualTo("retained-en")); Assert.That(restored.Entries[0].TextChinese, Is.EqualTo("retained-zh"));
            Assert.That(restored.Entries[0].GetAdditionalValue("CustomAuthoringColumn"), Is.EqualTo("keep"));
            Assert.That(File.Exists(Path.Combine(directory, file)), Is.True);
        }
        [TestCase("affection")][TestCase("sadistic")][TestCase("concern")][TestCase("hostility")][TestCase("obedience")][TestCase("instinct")]
        public void PsychologyHighMaxAndInverseConditionsUseExistingRuntimeThresholds(string metric)
        {
            foreach (int threshold in new[] { 60, 100 })
            foreach (bool negated in new[] { false, true })
            {
                string condition = (string)typeof(DialoguePreviewWindow).GetMethod("BuildConditionClause", StaticFlags).Invoke(null, new object[] { metric + " >= " + threshold, negated });
                foreach (int actual in new[] { threshold - 1, threshold, threshold + 1 })
                {
                    var state = new ConversationGameState(); state.SetInt(metric, actual);
                    Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, condition), Is.EqualTo(negated ? actual < threshold : actual >= threshold), condition + " @ " + actual);
                }
                object parsed = typeof(DialoguePreviewWindow).GetMethod("ParseConditionDropdownState", StaticFlags).Invoke(null, new object[] { condition });
                var flags = (bool[])parsed.GetType().GetField("IsNegatedOptions").GetValue(parsed);
                Assert.That(flags[0], Is.EqualTo(negated));
            }
        }
        [Test] public void GenresRetainEveryLegacyCategoryAndNewPsychology()
        {
            foreach (string category in DialoguePreviewWindow.AuthoringCategoryKeys.Skip(1)) Assert.That(DialoguePreviewWindow.ConditionValuesForCategory(category), Is.Not.Empty, category);
            Assert.That(DialoguePreviewWindow.NewPsychologyNumericConditions(), Has.Length.EqualTo(18));
            Assert.That(DialoguePreviewWindow.NewPsychologyStateConditions(), Has.Length.EqualTo(21));
            Assert.That(DialoguePreviewWindow.AuthoringCategoryKeys, Does.Not.Contain("J"));
            Assert.That(DialoguePreviewWindow.NewPsychologyNumericConditions().Any(value => value.Contains("60") || value.Contains("!=") || value.Contains("< 100")), Is.False);
            Assert.That(DialoguePreviewWindow.NewPsychologyStateConditions().Distinct().Count(), Is.EqualTo(21));
            Assert.That(DialoguePreviewWindow.GetAuthoringConditionCategory("Rain"), Is.EqualTo("B"));
            Assert.That(DialoguePreviewWindow.GetAuthoringConditionCategory("H: Bathroom"), Is.EqualTo("H"));
        }
        [Test] public void LegacyPsychologyClausesSurviveEditingAnotherSlot()
        {
            const string expression = "affection>=60 && !PsychologyState == \"CONCERN_AFFECTION\" && Rain";
            object parsed = typeof(DialoguePreviewWindow).GetMethod("ParseConditionDropdownState", StaticFlags).Invoke(null, new object[] { expression });
            var selections = (string[])((string[])parsed.GetType().GetField("SelectedOptions").GetValue(parsed)).Clone();
            var inverse = (bool[])parsed.GetType().GetField("IsNegatedOptions").GetValue(parsed);
            selections[2] = "Sunny";
            string result = (string)typeof(DialoguePreviewWindow).GetMethod("BuildConditionFromSelections", StaticFlags).Invoke(null, new object[] { selections, inverse, parsed });
            Assert.That(result, Does.Contain("affection>=60"));
            Assert.That(result, Does.Contain("!PsychologyState == \"CONCERN_AFFECTION\""));
            Assert.That(result, Does.Contain("Sunny"));
        }

        [Test] public void PsychologyTimingEditsOnlySelectedPatternAndPersists()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("PsychologyReactionDialogue.csv"));
            var groups = Get<List<DialoguePatternGroup>>("patternGroups");
            var other = groups[1].Entries[0];
            string original = other.GetAdditionalValue("context");
            foreach (string timing in DialoguePreviewWindow.PsychologyTimingValues)
            {
                Invoke("ApplyPsychologyTiming", groups[0], timing);
                Assert.That(groups[0].Entries.All(e => e.GetAdditionalValue("context") == timing), Is.True);
                Assert.That(other.GetAdditionalValue("context"), Is.EqualTo(original));
            }
            var data = Get<DialogueDataLoader.DataSet>("dataSet");
            string path = Path.Combine(directory, "timing.csv");
            DialogueDataLoader.SaveToPath(data, path);
            var edited = groups[0].Entries[0];
            Assert.That(DialogueDataLoader.LoadFromPath(path).Entries.Single(e => e.InternalId == edited.InternalId && e.Pattern == edited.Pattern && e.Order == edited.Order).GetAdditionalValue("context"), Is.EqualTo("BEFORE_SLEEP"));
            Set("hasPendingChanges", false);
        }

        [Test] public void PsychologyCsvConditionsAppearInDropdownsWithoutChangingExpressions()
        {
            var data = DialogueDataLoader.LoadFromPath("TalkSource/TalkCSV/PsychologyReactionDialogue.csv");
            foreach (var entry in data.Entries.Where(e => !string.IsNullOrEmpty(e.Condition)))
            {
                object parsed = typeof(DialoguePreviewWindow).GetMethod("ParseConditionDropdownState", StaticFlags).Invoke(null, new object[] { entry.Condition });
                var selected = (string[])parsed.GetType().GetField("SelectedOptions").GetValue(parsed);
                var preserved = (List<string>)parsed.GetType().GetField("PreservedClauses").GetValue(parsed);
                Assert.That(selected.Any(value => !string.IsNullOrEmpty(value)), Is.True, entry.Condition);
                Assert.That(preserved, Is.Empty, entry.Condition);
            }
        }

        [Test] public void ManuscriptSwitchRestoresPatternAndMultipleIndependentFoldouts()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            string normal = Fixture("DialoguePreview.csv"), teaching = Fixture("teacchFelidae.csv");
            Invoke("LoadCsv", normal); Set("currentPatternIndex", 5); Set("currentSequenceIndex", 0); Set("showTimeDiary", true); Set("showTopics", true); Set("showReadings", false); Set("showDetails", true); Set("regexInput", "normal-search");
            string id = CurrentEntryId();
            Invoke("LoadCsv", teaching); Set("currentPatternIndex", 2); Set("showTeaching", true); Set("showReadings", true);
            Invoke("LoadCsv", normal);
            Assert.That(Get<int>("currentPatternIndex"), Is.EqualTo(5)); Assert.That(Get<bool>("showTimeDiary"), Is.True); Assert.That(Get<bool>("showTopics"), Is.True); Assert.That(Get<bool>("showReadings"), Is.False); Assert.That(Get<bool>("showDetails"), Is.True);
            Assert.That(Get<string>("regexInput"), Is.EqualTo("normal-search")); Assert.That(CurrentEntryId(), Is.EqualTo(id));
            Invoke("LoadCsv", teaching); Assert.That(Get<int>("currentPatternIndex"), Is.EqualTo(2)); Assert.That(Get<bool>("showTeaching"), Is.True); Assert.That(Get<bool>("showReadings"), Is.True);
        }
        [TestCase("DialoguePreview.csv")][TestCase("teacchFelidae.csv")][TestCase("teacchseries_dialogue_source_2201.csv")]
        [TestCase("teacchTalk_body.csv")][TestCase("teacchCatExpression.csv")][TestCase("teacchCatBreed.csv")]
        [TestCase("teacchFavoriteWeather.csv")][TestCase("PsychologyReactionDialogue.csv")][TestCase("InternalDialogue.csv")]
        public void LoadingEveryManuscriptResolvesItsOwnSaveTarget(string file)
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); string path = Fixture(file); Invoke("LoadCsv", path);
            Assert.That(Get<DialogueDataLoader.DataSet>("dataSet").SavePath, Is.EqualTo(path.Replace('\\', '/')));
        }
        [Test] public void SaveAndReloadKeepAllFoldoutsAndSelection()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); string path = Fixture("DialoguePreview.csv"); Invoke("LoadCsv", path);
            Set("currentPatternIndex", 5); Set("showTimeDiary", true); Set("showTopics", true); Set("showReadings", true); Set("showDevelopment", true);
            Invoke("SaveCurrentCsv"); AssetDatabase.Refresh();
            foreach (string field in new[] { "showTimeDiary", "showTopics", "showReadings", "showDevelopment" }) Assert.That(Get<bool>(field), Is.True, field);
            Invoke("LoadCsv", path);
            foreach (string field in new[] { "showTimeDiary", "showTopics", "showReadings", "showDevelopment" }) Assert.That(Get<bool>(field), Is.True, field);
            Assert.That(Get<int>("currentPatternIndex"), Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator TabMovesAcrossVisibleFieldsWithoutInsertingText()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            Set("showTopics", false); Set("showReadings", false); Set("showTimeDiary", false);
            window.position = new Rect(30, 30, 1100, 760); window.Show(); windowShown = true; window.Focus(); window.Repaint();
            yield return null; yield return null; yield return null;
            var names = Get<List<string>>("inputFocusNames").ToArray(); Assert.That(names.Length, Is.GreaterThan(8));
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup")); string text = entry.Text;
            string beforeFocus = window.lastFocusedInput;
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab, character = '\t' });
            yield return null;
            Assert.That(entry.Text, Is.EqualTo(text));
            int next = DialoguePreviewWindow.NextFocusIndex(Array.IndexOf(names, beforeFocus), names.Length, false);
            Assert.That(window.lastFocusedInput, Is.EqualTo(names[next]));
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab, character = '\t', modifiers = EventModifiers.Shift });
            yield return null; Assert.That(entry.Text, Is.EqualTo(text));
            Assert.That(window.lastFocusedInput, Is.EqualTo(names[(next + names.Length - 1) % names.Length]));
            Set("hasPendingChanges", false);
        }
        [UnityTest] public IEnumerator TabTraversesExpandedVariantInputsInBothDirections()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup")); entry.ActionId = "focus-fixture";
            var repository = new DialogueRelatedCsvRepository(Path.Combine(directory, "event.csv"), "key", "ja", "diary_ja", "diary_variants");
            var row = repository.Add(); row["key"] = entry.ActionId; row["ja"] = "existing display"; row["diary_ja"] = "existing diary";
            row["diary_id"] = "focus-diary"; row["time_minutes"] = "120"; row["display_seconds"] = "2.8";
            Set("timedEvents", repository); Set("showTimeDiary", true); Set("showTopics", false); Set("showReadings", false); Set("showOtherLanguages", true);
            window.position = new Rect(30, 30, 1100, 760); window.Show(); windowShown = true; window.Focus(); window.Repaint();
            yield return null; yield return null; yield return null;
            var names = Get<List<string>>("inputFocusNames").ToArray(); Assert.That(names.Length, Is.GreaterThan(20));
            string csv = TalkTopicMasterCsvUtility.SerializeDocument(repository.Document);
            int focus = Array.IndexOf(names, window.lastFocusedInput);
            for (int i = 0; i < names.Length; i++)
            {
                focus = DialoguePreviewWindow.NextFocusIndex(focus, names.Length, false);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab, character = '\t' });
                yield return null; yield return null;
                Assert.That(window.lastFocusedInput, Is.EqualTo(names[focus]), "forward " + focus);
            }
            for (int i = 0; i < names.Length; i++)
            {
                focus = DialoguePreviewWindow.NextFocusIndex(focus, names.Length, true);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab, character = '\t', modifiers = EventModifiers.Shift });
                yield return null; yield return null;
                Assert.That(window.lastFocusedInput, Is.EqualTo(names[focus]), "reverse " + focus);
            }
            Assert.That(TalkTopicMasterCsvUtility.SerializeDocument(repository.Document), Is.EqualTo(csv));
            Set("hasPendingChanges", false); Set("timedEvents", null); Set("diaryCandidates", null); Set("diaryLinks", null);
        }
        [TestCase("日記", "showTimeDiary")][TestCase("漢字", "showReadings")][TestCase("教示", "showTeaching")]
        public void LegacyTabStateMigratesToItsCorrespondingSection(string oldTab, string section)
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            var type = typeof(DialoguePreviewWindow).GetNestedType("DialoguePreviewSessionState", BindingFlags.NonPublic);
            object view = Activator.CreateInstance(type); type.GetField("RelatedTab").SetValue(view, oldTab);
            Invoke("RestoreAuthoringView", view); Assert.That(Get<bool>(section), Is.True);
        }
        [Test] public void CollapsedHeadersDiagnoseMissingDiaryCandidatesWithoutChangingDrafts()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup"));
            Invoke("GetRelatedNotices", "時間経過・日記", entry);
            var links = Get<DialogueRelatedCsvRepository>("diaryLinks"); var row = links.Add();
            row["DialogueId"] = entry.EditorDialogueId; row["DiaryId"] = "missing-" + Guid.NewGuid().ToString("N");
            var notices = (List<(MessageType type, string message)>)Invoke("GetRelatedNotices", "時間経過・日記", entry);
            Assert.That(notices.Any(item => item.type == MessageType.Error && item.message.Contains(row["DiaryId"])), Is.True);
            Assert.That(links.Find("DialogueId", entry.EditorDialogueId).Contains(row), Is.True);
            Set("diaryLinks", null); Set("diaryCandidates", null); Set("timedEvents", null);
        }
        [UnityTest] public IEnumerator ChoicesPrecedePlaybackAndRelatedSectionsShareRightColumn()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup")); entry.ResponseType = "Choice";
            Set("showBranches", false); Set("showBehaviour", false); Set("showTimeDiary", false); Set("showTopics", true); Set("showReadings", true);
            window.position = new Rect(30, 30, 1100, 760); window.Show(); windowShown = true; window.Repaint();
            yield return null; yield return null; yield return null;
            Assert.That(window.branchesRect.yMax, Is.LessThanOrEqualTo(window.behaviourRect.y));
            Assert.That(window.relatedHeaderRects.Keys, Is.EquivalentTo(new[] { "時間経過・日記", "話題候補", "漢字読み" }));
            Assert.That(window.relatedHeaderRects["話題候補"].y, Is.LessThan(window.relatedHeaderRects["漢字読み"].y));
            Assert.That(window.developmentRect.y, Is.GreaterThan(window.relatedHeaderRects["漢字読み"].y));
            Assert.That(Get<bool>("showTopics"), Is.True); Assert.That(Get<bool>("showReadings"), Is.True);
            Assert.That(window.footerRect.yMax, Is.LessThanOrEqualTo(window.position.height + 1));
            Set("hasPendingChanges", false);
        }
        [UnityTest] public IEnumerator DefaultWritingFitsWithoutScrollingAndNavigationStaysFixed()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>(); Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            Set("showOtherLanguages", false); Set("selectedTab", 0); Set("showDetails", false); Set("showBehaviour", false); Set("showDevelopment", false); Set("showConditionExpression", false);
            window.position = new Rect(30, 30, 1100, 760); window.Show(); windowShown = true; window.Repaint();
            yield return null; yield return null; yield return null;
            Assert.That(window.footerRect.yMax, Is.LessThanOrEqualTo(window.position.height + 1));
            Assert.That(window.footerRect.y, Is.GreaterThan(600)); Assert.That(window.primaryContentBottom, Is.LessThan(550));
            Assert.That(window.primaryContentBottom, Is.GreaterThan(200));
            Set("scrollPosition", new Vector2(0, 200)); Set("relatedScroll", new Vector2(0, 200)); window.Repaint(); yield return null;
            Assert.That(window.footerRect.yMax, Is.LessThanOrEqualTo(window.position.height + 1));
        }
        [Test] public void NewPatternCreatesBlankIndependentPatternAndLineAdditionStaysWithinIt()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var originalGroup = (DialoguePatternGroup)Invoke("GetCurrentGroup");
            var source = (DialogueEntry)Invoke("GetCurrentEntry", originalGroup);
            var data = Get<DialogueDataLoader.DataSet>("dataSet");
            int before = data.Entries.Count;
            string originalText = source.Text;
            Invoke("AddNewPattern", originalGroup);
            var group = (DialoguePatternGroup)Invoke("GetCurrentGroup");
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", group);
            Assert.That(data.Entries.Count, Is.EqualTo(before + 1));
            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Assert.That(entry.InternalId, Is.EqualTo(source.InternalId));
            Assert.That(entry.RegexId, Is.EqualTo(source.RegexId));
            Assert.That(entry.Pattern, Is.Not.EqualTo(source.Pattern));
            Assert.That(entry.Order, Is.EqualTo(1));
            Assert.That(entry.Text, Is.Empty); Assert.That(entry.TextEnglish, Is.Empty); Assert.That(entry.TextChinese, Is.Empty);
            Assert.That(entry.ActionId, Is.Null.Or.Empty); Assert.That(entry.TargetPattern, Is.Null.Or.Empty);
            Assert.That(entry.EditorDialogueId, Is.Null.Or.Empty);
            Assert.That(source.Text, Is.EqualTo(originalText));
            Invoke("AddLineToCurrentPattern", group);
            group = (DialoguePatternGroup)Invoke("GetCurrentGroup");
            Assert.That(group.Entries.Select(line => line.Order), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Get<int>("currentSequenceIndex"), Is.EqualTo(1));
            Invoke("AddNewPattern", group);
            var next = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup"));
            Assert.That(next.Pattern, Is.Not.EqualTo(entry.Pattern));
            Assert.That(Get<bool>("hasPendingChanges"), Is.True);
            Set("hasPendingChanges", false);
        }

        [Test] public void NewPatternWithoutLoadedDataDoesNothing()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Assert.DoesNotThrow(() => Invoke("AddNewPattern", new object[] { null }));
            Assert.That(Get<bool>("hasPendingChanges"), Is.False);
        }

        [TestCase("teacchseries_dialogue_source_2201.csv", "series_")]
        [TestCase("teacchTalk_body.csv", "body_")]
        [TestCase("DialoguePreview.csv", "")]
        [TestCase("teacchFelidae.csv", "")]
        public void NewItemSkipsUsedNumericIdsAndKeepsOnlyManuscriptPrefix(string file, string prefix)
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture(file));
            var data = Get<DialogueDataLoader.DataSet>("dataSet");
            var group = (DialoguePatternGroup)Invoke("GetCurrentGroup");
            var source = (DialogueEntry)Invoke("GetCurrentEntry", group);
            source.InternalId = "90000";
            data.Entries.Add(new DialogueEntry { InternalId = "90010", RegexId = source.RegexId, Pattern = "1" });
            data.Entries.Add(new DialogueEntry { InternalId = "090020", RegexId = source.RegexId, Pattern = "1" });
            int before = data.Entries.Count;
            Invoke("AddNewItem", group);
            var entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup"));
            Assert.That(data.Entries.Count, Is.EqualTo(before + 1));
            Assert.That(entry.InternalId, Is.EqualTo("90030")); Assert.That(entry.RegexId, Is.EqualTo(prefix));
            Assert.That(entry.Pattern, Is.EqualTo("1")); Assert.That(entry.Order, Is.EqualTo(1));
            Assert.That(entry.RegexPattern, Is.Empty); Assert.That(entry.Text, Is.Empty);
            Assert.That(entry.TextEnglish, Is.Empty); Assert.That(entry.TextChinese, Is.Empty);
            Assert.That(entry.GetAdditionalValue("input"), Is.Null.Or.Empty);
            Assert.That(entry.ActionId, Is.Null.Or.Empty); Assert.That(entry.Condition, Is.Null.Or.Empty);
            Invoke("AddNewItem", Invoke("GetCurrentGroup"));
            entry = (DialogueEntry)Invoke("GetCurrentEntry", Invoke("GetCurrentGroup"));
            Assert.That(entry.InternalId, Is.EqualTo("90040")); Assert.That(entry.RegexId, Is.EqualTo(prefix));
            Set("hasPendingChanges", false);
        }

        [TestCase("00010", "00020")]
        [TestCase("99990", "100000")]
        [TestCase("NOT_NUMERIC", null)]
        [TestCase("9223372036854775800", null)]
        public void NewItemPreservesIdPaddingAndRejectsInvalidOrOverflowId(string id, string expected)
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var group = (DialoguePatternGroup)Invoke("GetCurrentGroup");
            var source = (DialogueEntry)Invoke("GetCurrentEntry", group);
            source.InternalId = id;
            var data = Get<DialogueDataLoader.DataSet>("dataSet");
            data.Entries.Clear(); data.Entries.Add(source);
            Invoke("AddNewItem", group);
            Assert.That(data.Entries.Count, Is.EqualTo(expected == null ? 1 : 2));
            if (expected != null) Assert.That(data.Entries.Last().InternalId, Is.EqualTo(expected));
            else Assert.That(Get<bool>("hasPendingChanges"), Is.False);
            Set("hasPendingChanges", false);
        }

        [TestCase("ネコ", "ねこ")]
        [TestCase("(ネコ|カメ)", "カメ")]
        [TestCase("ね こ", "ねこ")]
        public void JapaneseRegexPreviewUsesSamePatternAndInputNormalizationAsGame(string pattern, string input)
        {
            var method = typeof(DialoguePreviewWindow).GetMethod("TryMatchRegexPattern", StaticFlags);
            object[] args = { pattern, JapaneseTextNormalizer.NormalizeInput(input), "JP", 0, null };
            Assert.That((bool)method.Invoke(null, args), Is.True);
            Assert.That(args[4], Is.Null);
            var runtime = typeof(global::Nekolpos.System.DialogueEngine).GetMethod("GetLongestRegexMatch", StaticFlags);
            var match = (global::System.Text.RegularExpressions.Match)runtime.Invoke(null,
                new object[] { JapaneseTextNormalizer.NormalizeInput(input), JapaneseTextNormalizer.NormalizeToken(pattern) });
            Assert.That(args[3], Is.EqualTo(match.Length));
        }

        [Test] public void InvalidRegexDoesNotHideOtherMatchesAndRemainsVisibleAsWarning()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var data = Get<DialogueDataLoader.DataSet>("dataSet"); data.Entries.Clear();
            data.Entries.Add(new DialogueEntry { InternalId = "00010", RegexId = "broken", Pattern = "1", RegexPattern = "(", Text = "", Order = 1 });
            data.Entries.Add(new DialogueEntry { InternalId = "00020", RegexId = "valid", Pattern = "1", RegexPattern = "ネコ", Text = "", Order = 1 });
            Invoke("ResetTransientPatternGroupKeys");
            typeof(DialoguePreviewWindow).GetMethod("RebuildPatternCache", InstanceFlags, null, Type.EmptyTypes, null).Invoke(window, null);
            Set("regexInput", "ねこ");
            Invoke("CollectCurrentRegexMatchResults", new object[] { null });
            Assert.That(Get<global::System.Collections.IList>("currentRegexMatchResults").Count, Is.EqualTo(1));
            Assert.That(Get<List<string>>("regexTestWarnings"), Has.Count.EqualTo(1));
            Assert.That(Get<List<string>>("regexTestWarnings")[0], Does.Contain("broken"));
        }

        [TestCase(0, 100, 0f)]
        [TestCase(50, 100, 1036f)]
        [TestCase(99, 100, 2048f)]
        [TestCase(2, 3, 0f)]
        public void CsvItemPopupStartsAtSelectedItem(int selected, int count, float expected)
        {
            Assert.That(DialoguePreviewWindow.CsvItemListPopup.InitialScroll(selected, count, 352f), Is.EqualTo(expected));
        }

        [Test] public void CurrentCsvItemSelectorGroupsPatternsAndNavigatesWithoutDiscardingChanges()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var indices = (List<int>)Invoke("GetCurrentCsvItemIndices");
            var groups = Get<List<DialoguePatternGroup>>("patternGroups");
            Assert.That(indices.Count, Is.LessThan(groups.Count));
            var expected = groups[indices[1]].Entries[0];
            var label = (string)typeof(DialoguePreviewWindow).GetMethod("BuildCsvItemLabel", StaticFlags).Invoke(null, new object[] { expected });
            Assert.That(label, Is.EqualTo(expected.InternalId + " | " + expected.RegexId + " | " + expected.GetAdditionalValue("input")));
            Set("hasPendingChanges", true);
            Invoke("SelectCurrentCsvItem", indices[1]);
            Assert.That(Get<int>("currentPatternIndex"), Is.EqualTo(indices[1]));
            Assert.That(Get<int>("currentSequenceIndex"), Is.Zero);
            Assert.That(Get<bool>("hasPendingChanges"), Is.True);
            Set("hasPendingChanges", false);
        }

        [TestCase("(", true)]
        [TestCase("[", true)]
        [TestCase("(ねこ|いぬ)", false)]
        [TestCase("", false)]
        public void AuthoringRegexSyntaxCheckReportsInvalidPatternsWithoutInput(string pattern, bool invalid)
        {
            string error = (string)typeof(DialoguePreviewWindow).GetMethod("GetRegexSyntaxError", StaticFlags).Invoke(null, new object[] { pattern, true });
            Assert.That(!string.IsNullOrEmpty(error), Is.EqualTo(invalid));
        }

        [Test] public void WindowFocusLossValidatesUnsavedAllLanguagesAndClearsFixedErrors()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var data = Get<DialogueDataLoader.DataSet>("dataSet"); data.Entries.Clear();
            var entry = new DialogueEntry { InternalId = "00010", RegexId = "test", RegexPattern = "ねこ", RegexPatternEnglish = "(", RegexPatternChinese = "[" };
            data.Entries.Add(entry);
            Set("regexAuthoringValidationDirty", true); Set("hasPendingChanges", true);
            Invoke("OnLostFocus");
            var errors = Get<List<string>>("regexAuthoringErrors");
            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors.Any(error => error.Contains("英語")), Is.True);
            Assert.That(errors.Any(error => error.Contains("中国語")), Is.True);
            Assert.That(errors.All(error => error.Contains("00010") && error.Contains("test")), Is.True);
            Assert.That(Get<bool>("hasPendingChanges"), Is.True);
            entry.RegexPatternEnglish = "cat"; entry.RegexPatternChinese = "猫";
            Set("regexAuthoringValidationDirty", true); Invoke("OnLostFocus");
            Assert.That(errors, Is.Empty);
            Set("hasPendingChanges", false);
        }

        [Test] public void RegexDiagnosticOpensExactUnsavedLineAndKeepsDraft()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("teacchFelidae.csv"));
            var groups = Get<List<DialoguePatternGroup>>("patternGroups");
            int index = groups.FindIndex(group => group.Entries.Count > 1);
            var entry = groups[index].Entries[1];
            entry.RegexPattern = "(";
            Set("hasPendingChanges", true); Set("selectedTab", 1); Set("regexAuthoringValidationDirty", true);
            Invoke("ValidateAuthoringRegexes");
            var targets = Get<Dictionary<string, DialogueEntry>>("regexAuthoringTargets");
            Assert.That(targets.Values, Does.Contain(entry));
            Assert.That((bool)Invoke("OpenRegexAuthoringDiagnostic", entry), Is.True);
            Assert.That(Get<int>("currentPatternIndex"), Is.EqualTo(index));
            Assert.That(Get<int>("currentSequenceIndex"), Is.EqualTo(1));
            Assert.That(Get<int>("selectedTab"), Is.Zero);
            Assert.That(Get<bool>("showDetails"), Is.True);
            Assert.That(Get<bool>("hasPendingChanges"), Is.True);
            Assert.That(entry.RegexPattern, Is.EqualTo("("));
            Assert.That((bool)Invoke("OpenRegexAuthoringDiagnostic", new DialogueEntry()), Is.False);
            Set("hasPendingChanges", false);
        }

        [Test] public void EditingRegexSharesAllLanguagesAcrossSameIdPatternsAndSurvivesSave()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var data = Get<DialogueDataLoader.DataSet>("dataSet"); data.Entries.Clear();
            var source = new DialogueEntry { InternalId = "00010", RegexId = "one", Pattern = "1", Order = 1, RegexPattern = "old", Text = "" };
            var sibling = new DialogueEntry { InternalId = "00010", RegexId = "two", Pattern = "2", Order = 1, RegexPattern = "different", Text = "" };
            var other = new DialogueEntry { InternalId = "00020", RegexId = "one", Pattern = "1", Order = 1, RegexPattern = "other", Text = "" };
            data.Entries.AddRange(new[] { source, sibling, other });
            Invoke("ApplySharedRegexPatterns", source, "ねこ", "猫", "cat");
            Assert.That(sibling.RegexPattern, Is.EqualTo("ねこ"));
            Assert.That(sibling.RegexPatternChinese, Is.EqualTo("猫"));
            Assert.That(sibling.RegexPatternEnglish, Is.EqualTo("cat"));
            Assert.That(sibling.Pattern, Is.EqualTo("2")); Assert.That(sibling.RegexId, Is.EqualTo("two"));
            Assert.That(other.RegexPattern, Is.EqualTo("other"));
            Assert.That(Get<bool>("hasPendingChanges"), Is.True);
            string path = Path.Combine(directory, "SharedRegex.csv");
            DialogueDataLoader.SaveToPath(data, path);
            var restored = DialogueDataLoader.LoadFromPath(path).Entries.Where(entry => entry.InternalId == "00010").ToArray();
            Assert.That(restored, Has.Length.EqualTo(2));
            Assert.That(restored.All(entry => entry.RegexPattern == "ねこ" && entry.RegexPatternChinese == "猫" && entry.RegexPatternEnglish == "cat"), Is.True);
            Set("hasPendingChanges", false);
        }

        [Test] public void EmptyIdDoesNotShareRegexWithUnrelatedBlankIdRows()
        {
            window = ScriptableObject.CreateInstance<DialoguePreviewWindow>();
            Invoke("LoadCsv", Fixture("DialoguePreview.csv"));
            var data = Get<DialogueDataLoader.DataSet>("dataSet"); data.Entries.Clear();
            var source = new DialogueEntry { InternalId = "", RegexPattern = "one" };
            var other = new DialogueEntry { InternalId = "", RegexPattern = "two" };
            data.Entries.AddRange(new[] { source, other });
            Invoke("ApplySharedRegexPatterns", source, "ねこ", "", "");
            Assert.That(source.RegexPattern, Is.EqualTo("ねこ"));
            Assert.That(other.RegexPattern, Is.EqualTo("two"));
            Set("hasPendingChanges", false);
        }

        private string Fixture(string file)
        {
            string path = Path.Combine(directory, file); File.Copy("TalkSource/TalkCSV/" + file, path);
            stateKeys.Add(DialoguePreviewWindow.ManuscriptStateKey(path)); return path;
        }
        private string CurrentEntryId() { object group = Invoke("GetCurrentGroup"); return ((DialogueEntry)Invoke("GetCurrentEntry", group)).EditorDialogueId; }
        private object Invoke(string name, params object[] args) => typeof(DialoguePreviewWindow).GetMethod(name, InstanceFlags).Invoke(window, args);
        private void Set(string name, object value) => typeof(DialoguePreviewWindow).GetField(name, InstanceFlags).SetValue(window, value);
        private T Get<T>(string name) => (T)typeof(DialoguePreviewWindow).GetField(name, InstanceFlags).GetValue(window);
    }
}
