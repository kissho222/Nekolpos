using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.IO;
#endif
using System.Text;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.StatusSystem;
using UnityEngine;

namespace Nekolpos.System
{
    public static class BasicSystemDialogueCatalog
    {
        public const string GreetingMorningKey = "greeting_morning";
        public const string GreetingDayKey = "greeting_day";
        public const string GreetingNightKey = "greeting_night";
        public const string GreetingEveningKey = "greeting_evening";
        public const string GreetingRevivedMorningKey = "greeting_revived_morning";
        public const string UnknownWordPromptKey = "unknown_word_prompt";
        public const string FallbackUnknownInputKey = "fallback_unknown_input";
        public const string BranchResolverId = "BasicSystemDialogue";

        private const string ResourcePath = "Dialogue/BasicSystemDialogue";
        private const string EncryptedResourcePath = "TalkData/BasicSystemDialogue";

        private static readonly Dictionary<string, List<DialogueRow>> RowsByKey =
            new Dictionary<string, List<DialogueRow>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<DialogueRow>> RowsByPattern =
            new Dictionary<string, List<DialogueRow>>(StringComparer.OrdinalIgnoreCase);

        private static bool isLoaded;

        public sealed class ResolvedDialogue
        {
            public string Key;
            public string Pattern;
            public string Condition;
            public int Priority;
            public string Text;
            public string ResponseType;
            public string ActionId;
            public string ChoiceQuestion;
            public string ChoiceYesLabel;
            public string ChoiceNoLabel;
            public string ChoiceYesPattern;
            public string ChoiceNoPattern;
        }

        private sealed class DialogueRow
        {
            public string Key;
            public string Pattern;
            public string Condition;
            public int Priority;
            public string ResponseType;
            public string ActionId;
            public string ChoiceYesPattern;
            public string ChoiceNoPattern;
            public readonly Dictionary<string, string> TextByLocale =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> QuestionByLocale =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> ChoiceYesByLocale =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> ChoiceNoByLocale =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static string Get(string key, string fallback = "", string locale = null)
        {
            ResolvedDialogue resolved = Resolve(key, locale);
            return resolved != null && !string.IsNullOrWhiteSpace(resolved.Text)
                ? resolved.Text
                : fallback ?? string.Empty;
        }

        public static string Format(
            string key,
            IReadOnlyDictionary<string, string> placeholders,
            string fallback = "",
            string locale = null)
        {
            return FormatTemplate(Get(key, fallback, locale), placeholders);
        }

        public static ResolvedDialogue Resolve(string key, string locale = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            EnsureLoaded();

            return RowsByKey.TryGetValue(key.Trim(), out List<DialogueRow> rows)
                ? ResolveFromRows(rows, locale)
                : null;
        }

        public static ResolvedDialogue ResolveByKeyOrPattern(string keyOrPattern, string locale = null)
        {
            if (string.IsNullOrWhiteSpace(keyOrPattern))
            {
                return null;
            }

            EnsureLoaded();

            string lookup = keyOrPattern.Trim();
            if (RowsByKey.TryGetValue(lookup, out List<DialogueRow> keyRows))
            {
                ResolvedDialogue resolved = ResolveFromRows(keyRows, locale);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return RowsByPattern.TryGetValue(lookup, out List<DialogueRow> patternRows)
                ? ResolveFromRows(patternRows, locale)
                : null;
        }

        public static DialogueReactionData CreateReaction(
            string key,
            IReadOnlyDictionary<string, string> placeholders,
            string fallback = "",
            string locale = null)
        {
            return CreateReactionFromResolvedDialogue(Resolve(key, locale), placeholders, fallback);
        }

        public static DialogueReactionData CreateReactionFromKeyOrPattern(
            string keyOrPattern,
            IReadOnlyDictionary<string, string> placeholders,
            string fallback = "",
            string locale = null)
        {
            return CreateReactionFromResolvedDialogue(ResolveByKeyOrPattern(keyOrPattern, locale), placeholders, fallback);
        }

        public static bool TryCreateReactionGroup(
            string keyOrPattern,
            IReadOnlyDictionary<string, string> placeholders,
            out List<DialogueReactionData> reactions,
            string fallback = "",
            string locale = null)
        {
            reactions = null;
            DialogueReactionData reaction = CreateReactionFromKeyOrPattern(keyOrPattern, placeholders, fallback, locale);
            if (reaction == null)
            {
                return false;
            }

            reactions = new List<DialogueReactionData> { reaction };
            return true;
        }

        private static ResolvedDialogue ResolveFromRows(List<DialogueRow> rows, string locale)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            string resolvedLocale = NormalizeLocale(string.IsNullOrWhiteSpace(locale) ? ResolveCurrentLocale() : locale);
            ConversationGameState state = BuildEvaluationState();
            List<DialogueRow> candidates = new List<DialogueRow>();
            int highestPriority = int.MinValue;

            for (int i = 0; i < rows.Count; i++)
            {
                DialogueRow row = rows[i];
                if (!IsConditionMatched(state, row))
                {
                    continue;
                }

                if (row.Priority > highestPriority)
                {
                    highestPriority = row.Priority;
                    candidates.Clear();
                    candidates.Add(row);
                    continue;
                }

                if (row.Priority == highestPriority)
                {
                    candidates.Add(row);
                }
            }

            if (candidates.Count <= 0)
            {
                return null;
            }

            DialogueRow selected = candidates.Count == 1
                ? candidates[0]
                : candidates[UnityEngine.Random.Range(0, candidates.Count)];

            if (!TryResolveLocalizedValue(selected.TextByLocale, resolvedLocale, out string text))
            {
                return null;
            }

            TryResolveLocalizedValue(selected.QuestionByLocale, resolvedLocale, out string question);
            TryResolveLocalizedValue(selected.ChoiceYesByLocale, resolvedLocale, out string choiceYesLabel);
            TryResolveLocalizedValue(selected.ChoiceNoByLocale, resolvedLocale, out string choiceNoLabel);

            return new ResolvedDialogue
            {
                Key = selected.Key,
                Pattern = selected.Pattern,
                Condition = selected.Condition,
                Priority = selected.Priority,
                Text = text,
                ResponseType = NormalizeResponseType(selected.ResponseType),
                ActionId = selected.ActionId,
                ChoiceQuestion = question,
                ChoiceYesLabel = choiceYesLabel,
                ChoiceNoLabel = choiceNoLabel,
                ChoiceYesPattern = selected.ChoiceYesPattern,
                ChoiceNoPattern = selected.ChoiceNoPattern
            };
        }

        private static DialogueReactionData CreateReactionFromResolvedDialogue(
            ResolvedDialogue resolved,
            IReadOnlyDictionary<string, string> placeholders,
            string fallback)
        {
            string formattedFallback = FormatTemplate(fallback, placeholders);
            if (resolved == null)
            {
                if (string.IsNullOrWhiteSpace(formattedFallback))
                {
                    return null;
                }

                return new DialogueReactionData
                {
                    TextJP = formattedFallback,
                    ResponseType = "Reaction"
                };
            }

            string formattedText = FormatTemplate(resolved.Text, placeholders);
            if (string.IsNullOrWhiteSpace(formattedText))
            {
                formattedText = formattedFallback;
            }

            if (string.IsNullOrWhiteSpace(formattedText))
            {
                return null;
            }

            string formattedQuestion = FormatTemplate(
                string.IsNullOrWhiteSpace(resolved.ChoiceQuestion) ? resolved.Text : resolved.ChoiceQuestion,
                placeholders);

            return new DialogueReactionData
            {
                PatternID = !string.IsNullOrWhiteSpace(resolved.Pattern) ? resolved.Pattern.Trim() : resolved.Key,
                IntentID = "BASIC_SYSTEM",
                SourceRegexLabel = BranchResolverId,
                TextJP = formattedText,
                ResponseType = NormalizeResponseType(resolved.ResponseType),
                Condition = resolved.Condition ?? string.Empty,
                ActionId = resolved.ActionId ?? string.Empty,
                ChoiceQuestionJa = formattedQuestion,
                ChoiceYesLabel = FormatTemplate(resolved.ChoiceYesLabel, placeholders),
                ChoiceNoLabel = FormatTemplate(resolved.ChoiceNoLabel, placeholders),
                ChoiceYesPatternID = resolved.ChoiceYesPattern ?? string.Empty,
                ChoiceNoPatternID = resolved.ChoiceNoPattern ?? string.Empty,
                BranchResolver = BranchResolverId,
                RuntimePlaceholders = ClonePlaceholders(placeholders)
            };
        }

        private static bool IsConditionMatched(ConversationGameState state, DialogueRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.Condition))
            {
                return true;
            }

            try
            {
                return ConversationGameStateConditionEvaluator.Evaluate(state, row.Condition);
            }
            catch (FormatException exception)
            {
                Debug.LogWarning($"[BasicSystemDialogueCatalog] condition 解釈失敗: key={row.Key}, pattern={row.Pattern}, condition={row.Condition}, error={exception.Message}");
                return false;
            }
        }

        private static ConversationGameState BuildEvaluationState()
        {
            ConversationGameState workingState = CloneConversationState();
            OverlayTimeState(workingState);
            OverlayCatData(workingState);
            return workingState;
        }

        private static ConversationGameState CloneConversationState()
        {
            ConversationGameStateManager manager = UnityEngine.Object.FindFirstObjectByType<ConversationGameStateManager>();
            if (manager == null || manager.State == null)
            {
                return new ConversationGameState();
            }

            return ConversationGameState.FromJson(manager.State.SaveToJson(false));
        }

        private static void OverlayCatData(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            DialogueManager dialogueManager = DialogueManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<DialogueManager>();
            CatDataSO catData = dialogueManager != null ? dialogueManager.catData : null;
            if (catData == null)
            {
                return;
            }

            StatusManager statusManager = UnityEngine.Object.FindFirstObjectByType<StatusManager>();
            if (statusManager != null)
            {
                SetEmotionState(state, "Affection", statusManager.GetValue(StatusType.Affection));
                SetEmotionState(state, "Sadistic", statusManager.GetValue(StatusType.Sadistic));
                SetEmotionState(state, "Concern", statusManager.GetValue(StatusType.Concern));
                SetEmotionState(state, "Hostility", statusManager.GetValue(StatusType.Hostility));
                SetEmotionState(state, "Obedience", statusManager.GetValue(StatusType.Obedience));
                SetEmotionState(state, "Instinct", statusManager.GetValue(StatusType.Instinct));
                SetEmotionState(state, "Anxiety", statusManager.GetValue(StatusType.Concern));
            }

            state.SetString("PlayerCalling", SanitizeRuntimeNameValue(catData.playerCalling));
            state.SetString("PLAYER_CALLING", SanitizeRuntimeNameValue(catData.playerCalling));
            state.SetString("PlayerName", SanitizeRuntimeNameValue(catData.playerName));
            state.SetString("PLAYER_NAME", SanitizeRuntimeNameValue(catData.playerName));
            state.SetString("CatName", SanitizeRuntimeNameValue(catData.catName));
            state.SetString("CAT_NAME", SanitizeRuntimeNameValue(catData.catName));
            state.SetString("CAT_PRONOUN", SanitizeRuntimeNameValue(catData.catPronoun));
            state.SetString("FavoriteFoodName", catData.favoriteFoodName ?? string.Empty);
        }

        private static string SanitizeRuntimeNameValue(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private static void OverlayTimeState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            TimeSystem.TimeManager timeManager = UnityEngine.Object.FindFirstObjectByType<TimeSystem.TimeManager>();
            if (timeManager != null)
            {
                string phase = ConvertPeriodToConversationPhase(timeManager.CurrentPeriod);
                if (!string.IsNullOrWhiteSpace(phase))
                {
                    state.SetString("Phase", phase);
                    state.SetString("CurrentPhase", phase);
                }

                state.SetInt("CurrentDay", Mathf.Max(1, timeManager.CurrentDay));
                state.SetString(TimeSystem.WeatherSystem.WeatherKey, TimeSystem.WeatherSystem.ToDisplayText(timeManager.Weather));
                state.SetString(TimeSystem.WeatherSystem.CurrentWeatherKey, timeManager.Weather.ToString());
                state.SetString(TimeSystem.WeatherSystem.TomorrowWeatherKey, TimeSystem.WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
                state.SetString(TimeSystem.WeatherSystem.CurrentWeatherForecastKey, timeManager.TomorrowWeather.ToString());
                state.SetString(TimeSystem.WeatherSystem.ForecastWeatherKey, TimeSystem.WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
                state.SetString(TimeSystem.WeatherSystem.ForecastWeatherMisspelledKey, TimeSystem.WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
                state.SetString(TimeSystem.WeatherSystem.ForecastWeatherQuestionKey, TimeSystem.WeatherSystem.ToDisplayText(timeManager.TomorrowWeather));
                state.SetString(TimeSystem.WeatherSystem.NextActualWeatherKey, timeManager.NextActualWeather.ToString());
                state.SetBool(TimeSystem.WeatherSystem.ForecastCorrectKey, timeManager.ForecastCorrect);
                return;
            }

            GameManager manager = GameManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if (manager == null)
            {
                return;
            }

            if (TryResolveCurrentConversationPhase(manager.CurrentState, out string currentPhase))
            {
                state.SetString("Phase", currentPhase);
                state.SetString("CurrentPhase", currentPhase);
            }
        }

        private static string ConvertPeriodToConversationPhase(TimeSystem.DayPeriod period)
        {
            switch (period)
            {
                case TimeSystem.DayPeriod.Morning:
                    return "Morning";
                case TimeSystem.DayPeriod.Afternoon:
                    return "Lunch";
                case TimeSystem.DayPeriod.Evening:
                    return "Evening";
                case TimeSystem.DayPeriod.Night:
                    return "Night";
                default:
                    return string.Empty;
            }
        }

        private static bool TryResolveCurrentConversationPhase(IGameState state, out string phase)
        {
            if (state is StateMorning)
            {
                phase = "Morning";
                return true;
            }

            if (state is StateDay)
            {
                phase = "Lunch";
                return true;
            }

            if (state is StateEvening)
            {
                phase = "Evening";
                return true;
            }

            if (state is StateNight)
            {
                phase = "Night";
                return true;
            }

            phase = string.Empty;
            return false;
        }

        private static void SetEmotionState(ConversationGameState state, string key, int value)
        {
            if (state == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            int clamped = Mathf.Clamp(value, 0, 100);
            state.SetInt(key, clamped);
            state.SetInt(key.ToLowerInvariant(), clamped);
        }

        private static bool TryResolveLocalizedValue(
            Dictionary<string, string> localizedValues,
            string locale,
            out string text)
        {
            if (localizedValues == null)
            {
                text = string.Empty;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(locale) &&
                localizedValues.TryGetValue(locale, out text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (localizedValues.TryGetValue("ja", out text) && !string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            foreach (KeyValuePair<string, string> pair in localizedValues)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    text = pair.Value;
                    return true;
                }
            }

            text = string.Empty;
            return false;
        }

        private static void EnsureLoaded()
        {
            if (isLoaded)
            {
                return;
            }

            isLoaded = true;
            RowsByKey.Clear();
            RowsByPattern.Clear();

            string csvText = string.Empty;
            if (!ConversationDataManager.TryLoadEncryptedCsvResource(EncryptedResourcePath, out csvText, out string decryptError))
            {
                TextAsset csvAsset = Resources.Load<TextAsset>(ResourcePath);
                csvText = csvAsset != null ? csvAsset.text : string.Empty;
#if UNITY_EDITOR
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    const string editorSourcePath = "TalkSource/TalkCSV/BasicSystemDialogue.csv";
                    if (File.Exists(editorSourcePath))
                    {
                        csvText = File.ReadAllText(editorSourcePath, Encoding.UTF8);
                    }
                }
#endif
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    Debug.LogWarning($"[BasicSystemDialogueCatalog] CSV が見つかりません: Resources/{EncryptedResourcePath}.bytes / Resources/{ResourcePath}.csv ({decryptError})");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(csvText))
            {
                return;
            }

            LoadCsv(csvText);
        }

        private static void LoadCsv(string csvText)
        {
            List<string> lines = SplitCsvLines(csvText);
            if (lines.Count <= 0)
            {
                return;
            }

            string[] headers = ParseCsvLine(lines[0]);
            int keyIndex = FindColumnIndex(headers, "key");
            int patternIndex = FindColumnIndex(headers, "pattern");
            int responseTypeIndex = FindColumnIndex(headers, "response_type", "ResponseType");
            int actionIdIndex = FindColumnIndex(headers, "action_id", "ActionId");
            int conditionIndex = FindColumnIndex(headers, "condition");
            int priorityIndex = FindColumnIndex(headers, "priority");
            int choiceYesPatternIndex = FindColumnIndex(headers, "choice_yes_pattern", "ChoiceYesPattern");
            int choiceNoPatternIndex = FindColumnIndex(headers, "choice_no_pattern", "ChoiceNoPattern");
            int questionJaIndex = FindColumnIndex(headers, "question_ja", "question");
            int questionEnIndex = FindColumnIndex(headers, "question_en");
            int questionCnIndex = FindColumnIndex(headers, "question_cn", "question_zh");
            int choiceYesJaIndex = FindColumnIndex(headers, "choice_yes_ja");
            int choiceYesEnIndex = FindColumnIndex(headers, "choice_yes_en");
            int choiceYesCnIndex = FindColumnIndex(headers, "choice_yes_cn", "choice_yes_zh");
            int choiceNoJaIndex = FindColumnIndex(headers, "choice_no_ja");
            int choiceNoEnIndex = FindColumnIndex(headers, "choice_no_en");
            int choiceNoCnIndex = FindColumnIndex(headers, "choice_no_cn", "choice_no_zh");
            if (keyIndex < 0)
            {
                Debug.LogWarning("[BasicSystemDialogueCatalog] key 列が見つかりません。");
                return;
            }

            Dictionary<int, string> localeColumns = new Dictionary<int, string>();
            for (int i = 0; i < headers.Length; i++)
            {
                if (i == keyIndex ||
                    i == patternIndex ||
                    i == responseTypeIndex ||
                    i == actionIdIndex ||
                    i == conditionIndex ||
                    i == priorityIndex ||
                    i == choiceYesPatternIndex ||
                    i == choiceNoPatternIndex ||
                    i == questionJaIndex ||
                    i == questionEnIndex ||
                    i == questionCnIndex ||
                    i == choiceYesJaIndex ||
                    i == choiceYesEnIndex ||
                    i == choiceYesCnIndex ||
                    i == choiceNoJaIndex ||
                    i == choiceNoEnIndex ||
                    i == choiceNoCnIndex)
                {
                    continue;
                }

                string locale = NormalizeLocale(headers[i]);
                if (string.IsNullOrWhiteSpace(locale) || locale.Equals("notes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                localeColumns[i] = locale;
            }

            for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
            {
                string[] columns = ParseCsvLine(lines[lineIndex]);
                string key = GetColumn(columns, keyIndex).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                DialogueRow row = new DialogueRow
                {
                    Key = key,
                    Pattern = GetColumn(columns, patternIndex).Trim(),
                    ResponseType = GetColumn(columns, responseTypeIndex).Trim(),
                    ActionId = GetColumn(columns, actionIdIndex).Trim(),
                    Condition = GetColumn(columns, conditionIndex).Trim(),
                    Priority = ParseInt(GetColumn(columns, priorityIndex), 0),
                    ChoiceYesPattern = GetColumn(columns, choiceYesPatternIndex).Trim(),
                    ChoiceNoPattern = GetColumn(columns, choiceNoPatternIndex).Trim()
                };

                foreach (KeyValuePair<int, string> pair in localeColumns)
                {
                    string value = GetColumn(columns, pair.Key);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        row.TextByLocale[pair.Value] = value;
                    }
                }

                AddLocalizedValue(row.QuestionByLocale, "ja", GetColumn(columns, questionJaIndex));
                AddLocalizedValue(row.QuestionByLocale, "en", GetColumn(columns, questionEnIndex));
                AddLocalizedValue(row.QuestionByLocale, "zh-Hans", GetColumn(columns, questionCnIndex));
                AddLocalizedValue(row.ChoiceYesByLocale, "ja", GetColumn(columns, choiceYesJaIndex));
                AddLocalizedValue(row.ChoiceYesByLocale, "en", GetColumn(columns, choiceYesEnIndex));
                AddLocalizedValue(row.ChoiceYesByLocale, "zh-Hans", GetColumn(columns, choiceYesCnIndex));
                AddLocalizedValue(row.ChoiceNoByLocale, "ja", GetColumn(columns, choiceNoJaIndex));
                AddLocalizedValue(row.ChoiceNoByLocale, "en", GetColumn(columns, choiceNoEnIndex));
                AddLocalizedValue(row.ChoiceNoByLocale, "zh-Hans", GetColumn(columns, choiceNoCnIndex));

                if (!RowsByKey.TryGetValue(key, out List<DialogueRow> rows))
                {
                    rows = new List<DialogueRow>();
                    RowsByKey.Add(key, rows);
                }

                rows.Add(row);

                if (!string.IsNullOrWhiteSpace(row.Pattern))
                {
                    if (!RowsByPattern.TryGetValue(row.Pattern, out List<DialogueRow> patternRows))
                    {
                        patternRows = new List<DialogueRow>();
                        RowsByPattern.Add(row.Pattern, patternRows);
                    }

                    patternRows.Add(row);
                }
            }
        }

        private static void AddLocalizedValue(Dictionary<string, string> localizedValues, string locale, string value)
        {
            if (localizedValues == null || string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            localizedValues[locale] = value;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static string ResolveCurrentLocale()
        {
            ConversationDataManager manager = UnityEngine.Object.FindFirstObjectByType<ConversationDataManager>();
            if (manager == null)
            {
                return "ja";
            }

            return NormalizeLocale(manager.Locale);
        }

        private static string NormalizeLocale(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            if (normalized.Equals("jp", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "ja";
            }

            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return normalized;
        }

        private static string NormalizeResponseType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Reaction";
            }

            if (value.Equals("Choice", StringComparison.OrdinalIgnoreCase))
            {
                return "Choice";
            }

            if (value.Equals("Action", StringComparison.OrdinalIgnoreCase))
            {
                return "Action";
            }

            if (value.Equals("Event", StringComparison.OrdinalIgnoreCase))
            {
                return "Event";
            }

            if (value.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            {
                return "Normal";
            }

            return "Reaction";
        }

        private static string FormatTemplate(string template, IReadOnlyDictionary<string, string> placeholders)
        {
            if (string.IsNullOrEmpty(template) || placeholders == null || placeholders.Count == 0)
            {
                return template ?? string.Empty;
            }

            string formatted = template;
            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                formatted = formatted.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
                formatted = formatted.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
            }

            return formatted;
        }

        private static Dictionary<string, string> ClonePlaceholders(IReadOnlyDictionary<string, string> placeholders)
        {
            if (placeholders == null || placeholders.Count == 0)
            {
                return null;
            }

            Dictionary<string, string> clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in placeholders)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                clone[pair.Key] = pair.Value ?? string.Empty;
            }

            return clone;
        }

        private static int FindColumnIndex(string[] headers, params string[] candidates)
        {
            if (headers == null || candidates == null)
            {
                return -1;
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidate = NormalizeHeader(candidates[candidateIndex]);
                for (int headerIndex = 0; headerIndex < headers.Length; headerIndex++)
                {
                    if (NormalizeHeader(headers[headerIndex]) == candidate)
                    {
                        return headerIndex;
                    }
                }
            }

            return -1;
        }

        private static string NormalizeHeader(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
        }

        private static string GetColumn(string[] columns, int index)
        {
            if (columns == null || index < 0 || index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index] ?? string.Empty;
        }

        private static List<string> SplitCsvLines(string raw)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(raw))
            {
                return lines;
            }

            bool inQuotes = false;
            int startIndex = 0;

            for (int i = 0; i < raw.Length; i++)
            {
                char current = raw[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < raw.Length && raw[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if ((current == '\n' || current == '\r') && !inQuotes)
                {
                    lines.Add(raw.Substring(startIndex, i - startIndex));
                    if (current == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    startIndex = i + 1;
                }
            }

            if (startIndex < raw.Length)
            {
                string last = raw.Substring(startIndex);
                if (!string.IsNullOrEmpty(last))
                {
                    lines.Add(last);
                }
            }

            return lines;
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char currentChar = line[i];
                if (currentChar == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (currentChar == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(currentChar);
            }

            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
