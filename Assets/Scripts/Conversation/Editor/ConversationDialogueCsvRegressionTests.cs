using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.System;
using NUnit.Framework;

namespace Nekolpos.Conversation.Editor
{
    public sealed class ConversationDialogueCsvRegressionTests
    {
        private static readonly string[] DialogueEngineCsvFiles =
        {
            "DialoguePreview_Integrated.csv",
            "teacchseries_dialogue_source_2201.csv",
            "teacchTalk_body.csv",
            "teacchCatExpression.csv",
            "teacchFelidae.csv",
            "teacchCatBreed.csv",
            "teacchFavoriteWeather.csv"
        };

        [Test]
        public void SourceDialogueEngineCsvs_LoadWithoutValidationErrors()
        {
            foreach (string fileName in DialogueEngineCsvFiles)
            {
                string csvText = ReadTalkCsv(fileName);
                ConversationDataLoadResult result = ConversationDataLoader.LoadFromCsvText(
                    csvText,
                    fileName,
                    new ConversationDataLoadOptions
                    {
                        logErrorsToConsole = false
                    });

                Assert.That(result.Succeeded, Is.True, DescribeErrors(result));
                Assert.That(result.catalog.Routes.Count, Is.GreaterThan(0), fileName);
            }
        }

        [Test]
        public void DialoguePreview_RegexPatternsCompileAndImportantRoutesExist()
        {
            List<Dictionary<string, string>> rows = ReadCsvRows(ReadTalkCsv("DialoguePreview_Integrated.csv"));
            AssertRegexColumnsCompile("DialoguePreview_Integrated.csv", rows);

            HashSet<string> routeKeys = BuildRouteKeySet(rows);

            Assert.That(routeKeys, Does.Contain("nail|1"));
            Assert.That(routeKeys, Does.Contain("why_kill|1"));
            Assert.That(routeKeys, Does.Contain("why_kill|3"));

            string[] defeatConditions =
            {
                "PlayerDefeatedByNekomata",
                "PlayerDefeatedByPredation",
                "PlayerDefeatedByAnger",
                "PlayerDefeatedByAccident"
            };

            foreach (string condition in defeatConditions)
            {
                Assert.That(
                    rows.Any(row => Read(row, "regex_id").Replace("\\_", "_") == "why_kill" &&
                                    Read(row, "condition").Equals(condition, StringComparison.Ordinal)),
                    Is.True,
                    condition);
            }
        }

        [Test]
        public void DialoguePreview_ChoiceAndCallTargetsResolveWithinSameRegexGroup()
        {
            List<Dictionary<string, string>> rows = ReadCsvRows(ReadTalkCsv("DialoguePreview_Integrated.csv"));
            HashSet<string> routeKeys = BuildRouteKeySet(rows);

            foreach (Dictionary<string, string> row in rows)
            {
                string group = Read(row, "regex_id").Replace("\\_", "_");
                if (string.IsNullOrWhiteSpace(group))
                {
                    group = Read(row, "id");
                }

                foreach (string column in new[] { "choice_yes_pattern", "choice_no_pattern", "yes_next_pattern", "no_next_pattern", "target_pattern" })
                {
                    string target = Read(row, column);
                    if (string.IsNullOrWhiteSpace(target) || target == "0")
                    {
                        continue;
                    }

                    Assert.That(
                        routeKeys,
                        Does.Contain(group + "|" + target),
                        $"Missing {column} target {target} for group {group} at source id {Read(row, "id")}.");
                }
            }
        }

        [Test]
        public void InternalDialogue_NoTalkAndTeachingChoicesAreReachable()
        {
            ResetCatalogLoadState(typeof(InternalDialogueCatalog));

            for (int i = 1; i <= 6; i++)
            {
                Assert.That(
                    InternalDialogueCatalog.TryCreateReactionGroupByPatternId(
                        "No_talk" + i,
                        new Dictionary<string, string>(),
                        out List<DialogueReactionData> group),
                    Is.True,
                    "No_talk" + i);

                Assert.That(group.Count, Is.GreaterThan(0), "No_talk" + i);
                Assert.That(
                    group.Any(reaction => (reaction.ActionId ?? string.Empty).StartsWith("TeachingMode:Start:", StringComparison.Ordinal)),
                    Is.True,
                    "No_talk" + i);
            }

            Assert.That(
                InternalDialogueCatalog.TryCreateReactionGroupByPatternId(
                    "TEACH_CONFIRM_CAT_SERIES",
                    new Dictionary<string, string>(),
                    out List<DialogueReactionData> teachingConfirm),
                Is.True);
            Assert.That(teachingConfirm[0].ResponseType, Is.EqualTo("Choice"));
            Assert.That(teachingConfirm[0].ActionId, Is.EqualTo("TeachingMode:Confirm:CatSeries"));
        }

        [Test]
        public void BasicSystemAndTimedEventCatalogs_LoadEditorSourceFallbacks()
        {
            ResetCatalogLoadState(typeof(BasicSystemDialogueCatalog));
            ResetCatalogLoadState(typeof(SystemTimedEventCatalog));

            DialogueReactionData unknownWord = BasicSystemDialogueCatalog.CreateReaction(
                BasicSystemDialogueCatalog.UnknownWordPromptKey,
                new Dictionary<string, string> { ["unknown_word"] = "パウバート" });

            Assert.That(unknownWord, Is.Not.Null);
            Assert.That(unknownWord.ResponseType, Is.EqualTo("Choice"));
            Assert.That(unknownWord.TextJP, Does.Contain("パウバート"));
            Assert.That(unknownWord.ChoiceYesPatternID, Is.EqualTo("unknown_word_teach_yes"));
            Assert.That(unknownWord.ChoiceNoPatternID, Is.EqualTo("unknown_word_teach_no"));

            SystemTimedEventCatalog.ResolvedTimedEvent timedEvent =
                SystemTimedEventCatalog.Resolve(SystemTimedEventCatalog.UnknownWordTeachEventKey);

            Assert.That(timedEvent, Is.Not.Null);
            Assert.That(timedEvent.TimeMinutes, Is.EqualTo(120));
            Assert.That(timedEvent.DisplaySeconds, Is.EqualTo(2.8f).Within(0.001f));
        }

        [Test]
        public void TalkTopicMaster_EnabledHintsHaveImportTextAndSourceCsvs()
        {
            string sourceText = ReadText(Path.Combine(ProjectRoot, "TalkSource", "Dialogue", "TalkTopicMaster.csv"));
            string resourceText = ReadText(Path.Combine(ProjectRoot, "Assets", "Resources", "Dialogue", "TalkTopicMaster.csv"));

            AssertTalkTopicHintsAreUsable(sourceText, "TalkSource/Dialogue/TalkTopicMaster.csv");
            AssertTalkTopicHintsAreUsable(resourceText, "Assets/Resources/Dialogue/TalkTopicMaster.csv");
        }

        [Test]
        public void JapaneseNormalization_CoversTalkTopicReactiveInputs()
        {
            Assert.That(JapaneseTextNormalizer.NormalizeInput("オナカスイタ"), Is.EqualTo("おなかすいた"));
            Assert.That(JapaneseTextNormalizer.NormalizeInput("お腹が空いた"), Is.EqualTo("おなかがすいた"));
            Assert.That(JapaneseTextNormalizer.NormalizeInput("御手"), Is.EqualTo("おて"));
            Assert.That(JapaneseTextNormalizer.NormalizeInput("猫又"), Is.EqualTo("ねこまた"));
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

        private static string ReadTalkCsv(string fileName)
        {
            return ReadText(Path.Combine(ProjectRoot, "TalkSource", "TalkCSV", fileName));
        }

        private static string ReadText(string path)
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string DescribeErrors(ConversationDataLoadResult result)
        {
            if (result == null || result.errors == null || result.errors.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, result.errors.Select(error => error.ToString()));
        }

        private static void AssertRegexColumnsCompile(string sourceName, List<Dictionary<string, string>> rows)
        {
            foreach (Dictionary<string, string> row in rows)
            {
                string regex = Read(row, "regex_jp");
                if (string.IsNullOrWhiteSpace(regex))
                {
                    continue;
                }

                Assert.DoesNotThrow(
                    () => _ = new Regex(regex),
                    sourceName + " id=" + Read(row, "id") + " regex_id=" + Read(row, "regex_id"));
            }
        }

        private static string BuildGroupPatternKey(Dictionary<string, string> row)
        {
            string group = Read(row, "regex_id").Replace("\\_", "_");
            if (string.IsNullOrWhiteSpace(group))
            {
                group = Read(row, "id");
            }

            string pattern = Read(row, "pattern");
            return string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(pattern)
                ? string.Empty
                : group + "|" + pattern;
        }

        private static HashSet<string> BuildRouteKeySet(List<Dictionary<string, string>> rows)
        {
            HashSet<string> routeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dictionary<string, string> row in rows)
            {
                string key = BuildGroupPatternKey(row);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    routeKeys.Add(key);
                }
            }

            return routeKeys;
        }

        private static string Read(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out string value)
                ? value?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static List<Dictionary<string, string>> ReadCsvRows(string csvText)
        {
            List<List<string>> records = ParseCsv(csvText);
            Assert.That(records.Count, Is.GreaterThan(0));

            List<string> headers = records[0]
                .Select(header => (header ?? string.Empty).Trim().TrimStart('\uFEFF'))
                .ToList();

            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            for (int i = 1; i < records.Count; i++)
            {
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int column = 0; column < headers.Count; column++)
                {
                    if (string.IsNullOrWhiteSpace(headers[column]))
                    {
                        continue;
                    }

                    row[headers[column]] = column < records[i].Count ? records[i][column] ?? string.Empty : string.Empty;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static List<List<string>> ParseCsv(string csvText)
        {
            List<List<string>> records = new List<List<string>>();
            List<string> currentRecord = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < csvText.Length; index++)
            {
                char current = csvText[index];
                if (current == '"')
                {
                    if (inQuotes && index + 1 < csvText.Length && csvText[index + 1] == '"')
                    {
                        currentField.Append('"');
                        index++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && current == ',')
                {
                    currentRecord.Add(currentField.ToString());
                    currentField.Length = 0;
                    continue;
                }

                if (!inQuotes && (current == '\r' || current == '\n'))
                {
                    currentRecord.Add(currentField.ToString());
                    currentField.Length = 0;
                    records.Add(currentRecord);
                    currentRecord = new List<string>();
                    if (current == '\r' && index + 1 < csvText.Length && csvText[index + 1] == '\n')
                    {
                        index++;
                    }

                    continue;
                }

                currentField.Append(current);
            }

            Assert.That(inQuotes, Is.False, "CSV contains an unterminated quoted field.");
            currentRecord.Add(currentField.ToString());
            if (currentRecord.Count > 1 || currentRecord[0].Length > 0 || records.Count == 0)
            {
                records.Add(currentRecord);
            }

            return records;
        }

        private static void ResetCatalogLoadState(Type catalogType)
        {
            FieldInfo isLoadedField = catalogType.GetField("isLoaded", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(isLoadedField, Is.Not.Null, catalogType.FullName);
            isLoadedField.SetValue(null, false);
        }

        private static void AssertTalkTopicHintsAreUsable(string csvText, string sourceName)
        {
            List<TalkTopicData> hints = TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(csvText, sourceName);
            Assert.That(hints.Count, Is.GreaterThan(0), sourceName);
            Assert.That(hints.All(hint => !string.IsNullOrWhiteSpace(hint.Import)), Is.True, sourceName);
            Assert.That(hints.Any(hint => hint.Source == "DialoguePreview_Integrated.csv"), Is.True, sourceName);
        }
    }
}
