using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using Newtonsoft.Json;
using UnityEngine;

namespace Nekolpos.Dialogue.UnknownWord
{
    public enum WordGenre { Food, Animal, Person, Place, Object, Work, Action, Greeting, Other, Word }
    public enum WordAnswer { Unknown = 0, Yes = 1, No = 2, Unanswered = Unknown }
    [Flags] public enum GreetingTimes { None = 0, Morning = 1, Afternoon = 2, Evening = 4, Night = 8 }
    [Flags] public enum GreetingScenes { None = 0, Meeting = 1, Parting = 2 }

    [Serializable]
    public sealed class LearnedWord
    {
        public string id;
        public string display;
        public string normalized;
        public string reading;
        public List<string> aliases = new List<string>();
        public WordGenre genre;
        public Dictionary<string, WordAnswer> answers = new Dictionary<string, WordAnswer>();
        public GreetingTimes times;
        public GreetingScenes scenes;
        public LearnedWord Copy() => JsonConvert.DeserializeObject<LearnedWord>(JsonConvert.SerializeObject(this));
    }

    public sealed class WordQuestion
    {
        public readonly string Id, Prompt, YesSummary, NoSummary, YesFinal, NoFinal;
        public readonly int Order;
        public WordQuestion(string id, string prompt, string yes, string yesFinal, string no, string noFinal, int order)
        { Id = id; Prompt = prompt; YesSummary = yes; NoSummary = no; YesFinal = yesFinal; NoFinal = noFinal; Order = order; }
    }

    /// <summary>Player-authored knowledge only; never used for food safety or gameplay effects.</summary>
    public sealed class LearnedWordStore
    {
        public const string SaveKey = "Nekolpos.LearnedWords.v1";
        private sealed class Document
        {
            [JsonProperty(Required = Required.Always)] public int version = 1;
            [JsonProperty(Required = Required.Always)] public List<LearnedWord> words = new List<LearnedWord>();
        }
        private static LearnedWordStore shared;
        public static LearnedWordStore Shared => shared ??= new LearnedWordStore(
            () => ProgressSaveStore.GetString(SaveKey), json => { ProgressSaveStore.SetString(SaveKey, json); ProgressSaveStore.Save(); });
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { shared = null; WordKnowledge.ResetCache(); WordVariables.BeginConversation(); }
        private readonly Action<string> save;
        private Document document;
        private bool invalidSave;
        public event Action Changed;
        public IReadOnlyList<LearnedWord> Words => document.words.Select(w => w.Copy()).ToList();
        public LearnedWordStore(Func<string> load, Action<string> save)
        {
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            string json = load();
            try
            {
                document = string.IsNullOrEmpty(json) ? new Document() : JsonConvert.DeserializeObject<Document>(json);
                if (document == null || document.version != 1 || document.words == null) throw new FormatException("Unsupported learned word save.");
                var ids = new HashSet<string>();
                var recognition = new HashSet<string>();
                foreach (var word in document.words)
                {
                    Validate(word);
                    if (!ids.Add(word.id)) throw new FormatException("Duplicate learned word ID.");
                    word.normalized = Normalize(word.display);
                    foreach (string spelling in Spellings(word))
                        if (!recognition.Add(spelling)) throw new FormatException("Duplicate learned word spelling.");
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentException)
            {
                invalidSave = true;
                document = new Document();
                Debug.LogError("[LearnedWords] 保存辞書を読み込めません。元データを保持して書き込みを停止します: " + ex.Message);
            }
        }
        public static string Normalize(string input) => JapaneseTextNormalizer.NormalizeInput(input ?? "");
        public static string SafeText(string input) => (input ?? "").Replace("<", "‹").Replace(">", "›").Replace("{", "｛").Replace("}", "｝");
        public static bool HasLatin(string input) => Regex.IsMatch((input ?? "").Normalize(global::System.Text.NormalizationForm.FormKC), "[A-Za-z]");
        public static bool RequiresReading(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return input.Normalize(global::System.Text.NormalizationForm.FormKC).Any(c =>
                !(c >= '\u3041' && c <= '\u3096') && !(c >= '\u3099' && c <= '\u309f') &&
                !(c >= '\u30a1' && c <= '\u30fa') && !(c >= '\u30fc' && c <= '\u30ff'));
        }
        public static void ValidateText(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Trim().Length > 64 || input.Any(char.IsControl) || string.IsNullOrEmpty(Normalize(input)))
                throw new ArgumentException("言葉は1〜64文字で入力してください。改行・制御文字は使用できません。");
        }
        private static void Validate(LearnedWord word)
        {
            if (word == null || !Guid.TryParse(word.id, out _) || !Enum.IsDefined(typeof(WordGenre), word.genre)) throw new FormatException("Invalid word identity or genre.");
            ValidateText(word.display);
            if (HasLatin(word.display) && string.IsNullOrWhiteSpace(word.reading)) throw new ArgumentException("英字の言葉には読み方が必要です。");
            if (!string.IsNullOrEmpty(word.reading)) ValidateText(word.reading);
            if (word.aliases == null || word.answers == null || word.aliases.Count > 16 || ((int)word.times & ~15) != 0 || ((int)word.scenes & ~3) != 0)
                throw new FormatException("Invalid word data.");
            foreach (string alias in word.aliases) ValidateText(alias);
            foreach (var answer in word.answers)
                if (!Enum.IsDefined(typeof(WordAnswer), answer.Value) || !WordKnowledge.AllQuestionIds.Contains(answer.Key)) throw new FormatException("Invalid answer.");
        }
        private static IEnumerable<string> Spellings(LearnedWord word) => new[] { word.display, word.reading }.Concat(word.aliases)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(Normalize).Distinct(StringComparer.Ordinal);
        public LearnedWord Find(string id) => document.words.FirstOrDefault(w => w.id == id)?.Copy();
        public LearnedWord Match(string input, out bool exact)
        {
            string key = Normalize(input);
            var match = document.words.FirstOrDefault(w => Spellings(w).Contains(key));
            exact = match != null;
            if (match == null && key.Length > 0)
                match = document.words.OrderByDescending(w => Spellings(w).Max(s => s.Length))
                    .FirstOrDefault(w => Spellings(w).Any(s => key.Contains(s)));
            return match?.Copy();
        }
        public void Put(LearnedWord word)
        {
            if (word == null) throw new ArgumentNullException(nameof(word));
            if (invalidSave) throw new InvalidOperationException("保存辞書に問題があるため登録できません。元のセーブを確認してください。");
            var copy = word.Copy();
            copy.display = copy.display?.Trim(); copy.reading = copy.reading?.Trim();
            Validate(copy); copy.normalized = Normalize(copy.display);
            if (document.words.Any(w => w.id != copy.id && Spellings(w).Intersect(Spellings(copy)).Any()))
                throw new ArgumentException("同じ言葉または読み方が登録されています。既存の言葉を教え直してください。");
            var next = new Document { words = document.words.Where(w => w.id != copy.id).Concat(new[] { copy }).ToList() };
            Commit(next);
        }
        public bool Forget(string id)
        {
            if (invalidSave) throw new InvalidOperationException("保存辞書に問題があるため削除できません。");
            if (!document.words.Any(w => w.id == id)) return false;
            Commit(new Document { words = document.words.Where(w => w.id != id).ToList() }); return true;
        }
        private void Commit(Document next)
        {
            save(JsonConvert.SerializeObject(next)); document = next;
            WordVariables.BeginConversation(); Changed?.Invoke();
        }
    }

    public static class WordKnowledge
    {
        public const string SourcePath = "TalkSource/TalkCSV/WordLearningQuestions.csv";
        public const string ResourcePath = "TalkData/WordLearningQuestions";
        private static Definition definition;
        public static void ResetCache() { definition = null; WordLearningDialogue.ResetCache(); }
        private static Definition Data
        {
            get
            {
                if (definition != null) return definition;
                string csv = null;
#if UNITY_EDITOR
                if (global::System.IO.File.Exists(SourcePath)) csv = global::System.IO.File.ReadAllText(SourcePath, global::System.Text.Encoding.UTF8);
#endif
                if (csv == null && !ConversationDataManager.TryLoadEncryptedCsvResource(ResourcePath, out csv, out string error))
                    throw new InvalidOperationException("WordLearningQuestions could not be loaded: " + error);
                definition = ParseCsv(csv);
                return definition;
            }
        }
        public static string[] GenreLabels => Data.Labels;
        private static readonly string[] retiredQuestionIds = { "food_heart", "work_heart" };
        public static IReadOnlyCollection<string> AllQuestionIds => Data.QuestionIds.Concat(retiredQuestionIds).Distinct().ToArray();
        public static IReadOnlyList<WordQuestion> Questions(WordGenre genre) => Data.Questions(genre);
        public static WordQuestion Next(LearnedWord word) => Questions(word.genre).FirstOrDefault(q => !word.answers.ContainsKey(q.Id));
        public static WordQuestion RandomQuestion(LearnedWord word)
        {
            var pool = Questions(word.genre);
            var candidates = pool.Where(q => !word.answers.ContainsKey(q.Id)).ToList();
            // Explicit reteaching remains possible after completion.
            if (candidates.Count == 0) candidates.AddRange(pool);
            return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }
        public static string Summary(LearnedWord word) => Data.Summary(word);

        public sealed class Definition
        {
            internal readonly Dictionary<WordGenre, List<WordQuestion>> Pools = new Dictionary<WordGenre, List<WordQuestion>>();
            internal readonly Dictionary<WordGenre, string[]> Genres = new Dictionary<WordGenre, string[]>();
            internal readonly HashSet<string> QuestionIds = new HashSet<string>(StringComparer.Ordinal);
            public string[] Labels => Enum.GetValues(typeof(WordGenre)).Cast<WordGenre>().Select(g => Genres[g][0]).ToArray();
            public IReadOnlyList<WordQuestion> Questions(WordGenre genre) => Pools[genre];
            public string Summary(LearnedWord word)
            {
                var answered = Questions(word.genre).Where(q => word.answers.TryGetValue(q.Id, out var a) && (a == WordAnswer.Yes || a == WordAnswer.No)).ToList();
                var parts = new List<string> { answered.Count == 0 ? Genres[word.genre][2] : Genres[word.genre][1] };
                for (int i = 0; i < answered.Count; i++)
                {
                    var q = answered[i]; bool yes = word.answers[q.Id] == WordAnswer.Yes;
                    parts.Add(i == answered.Count - 1 ? (yes ? q.YesFinal : q.NoFinal) : (yes ? q.YesSummary : q.NoSummary));
                }
                return WordLearningDialogue.Format("summary", word.display, facts: string.Join("、", parts));
            }
        }
        public static Definition ParseCsv(string csv)
        {
            var rows = ReadCsv(csv); if (rows.Count < 2) throw new FormatException("WordLearningQuestions: CSV is empty.");
            string[] columns = { "row_type", "category", "question_id", "order", "label", "category_middle", "category_final", "question_text", "yes_middle", "yes_final", "no_middle", "no_final" };
            var header = rows[0].Select(v => v.Trim().Trim('\uFEFF')).ToList();
            if (header.Count != header.Distinct(StringComparer.Ordinal).Count() || columns.Any(c => !header.Contains(c)))
                throw new FormatException("WordLearningQuestions: missing or duplicate columns.");
            var result = new Definition(); var common = new List<WordQuestion>(); var general = new List<WordQuestion>();
            for (int row = 1; row < rows.Count; row++)
            {
                var cells = rows[row]; if (cells.Count != header.Count) throw new FormatException("WordLearningQuestions: wrong column count at record " + (row + 1));
                string Get(string name, bool required = true)
                {
                    string value = cells[header.IndexOf(name)].Trim();
                    if ((required && value.Length == 0) || value.Any(char.IsControl)) throw new FormatException("WordLearningQuestions: invalid " + name + " at record " + (row + 1));
                    return value;
                }
                string kind = Get("row_type"), category = Get("category");
                bool isGeneral = category == "General";
                bool isCommon = category == "Common" || isGeneral;
                if (category == "word") category = "Word";
                WordGenre genre = default;
                if (!isCommon && (!Enum.TryParse(category, false, out genre) || !Enum.IsDefined(typeof(WordGenre), genre) || genre.ToString() != category))
                    throw new FormatException("WordLearningQuestions: invalid category at record " + (row + 1));
                if (kind == "genre")
                {
                    if (isCommon || result.Genres.ContainsKey(genre)) throw new FormatException("WordLearningQuestions: duplicate/invalid genre.");
                    result.Genres.Add(genre, new[] { Get("label"), Fragment(Get("category_middle")), Fragment(Get("category_final")) });
                }
                else if (kind == "question")
                {
                    string id = Get("question_id");
                    if (!Regex.IsMatch(id, "^[a-z][a-z0-9_]*$") || !result.QuestionIds.Add(id)) throw new FormatException("WordLearningQuestions: duplicate/invalid question_id: " + id);
                    if (!int.TryParse(Get("order"), out int order) || order < 0) throw new FormatException("WordLearningQuestions: invalid order.");
                    var question = new WordQuestion(id, Get("question_text"), Fragment(Get("yes_middle")), Fragment(Get("yes_final")), Fragment(Get("no_middle")), Fragment(Get("no_final")), order);
                    if (isGeneral) general.Add(question);
                    else if (isCommon) common.Add(question);
                    else { if (!result.Pools.ContainsKey(genre)) result.Pools.Add(genre, new List<WordQuestion>()); result.Pools[genre].Add(question); }
                }
                else throw new FormatException("WordLearningQuestions: invalid row_type.");
            }
            general = general.OrderBy(q => q.Order).ToList();
            foreach (WordGenre genre in Enum.GetValues(typeof(WordGenre)))
            {
                if (!result.Genres.ContainsKey(genre)) throw new FormatException("WordLearningQuestions: missing genre " + genre);
                if (!result.Pools.ContainsKey(genre)) result.Pools.Add(genre, new List<WordQuestion>());
                var questions = result.Pools[genre]; questions.AddRange(common);
                if (questions.Count == 0 && general.Count == 0)
                    throw new FormatException("WordLearningQuestions: missing questions in " + genre);
                var sorted = questions.OrderBy(q => q.Order).ToList();
                questions.Clear(); questions.AddRange(sorted);
                questions.AddRange(general);
            }
            return result;
        }
        private static string Fragment(string value)
        {
            if (value.IndexOfAny(new[] { '、', ',', '，' }) >= 0) throw new FormatException("WordLearningQuestions: fragments must not contain separators.");
            return value;
        }
        internal static List<List<string>> ReadCsv(string csv)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            csv = csv.TrimStart('\uFEFF');
            var records = new List<List<string>>(); var row = new List<string>(); var cell = new global::System.Text.StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                    else cell.Append(c);
                }
                else if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (c != ',') { if (row.Any(v => !string.IsNullOrWhiteSpace(v))) records.Add(row); row = new List<string>(); if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; }
                }
                else if (c == '"' && cell.Length == 0 && !closed) quoted = true;
                else { if (closed || c == '"') throw new FormatException("WordLearningQuestions: invalid CSV quoting."); cell.Append(c); }
            }
            if (quoted) throw new FormatException("WordLearningQuestions: unclosed CSV quote.");
            if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); if (row.Any(v => !string.IsNullOrWhiteSpace(v))) records.Add(row); }
            return records;
        }
        public static GreetingTimes Time(DayPeriod period) => (GreetingTimes)(1 << (int)period);
        public static bool MatchesGreeting(LearnedWord word, DayPeriod period, GreetingScenes scene) =>
            (word.times == GreetingTimes.None || (word.times & Time(period)) != 0) &&
            (word.scenes == GreetingScenes.None || (word.scenes & scene) != 0);
    }

    /// <summary>Existing learning lines authored in CSV; no automatic dialogue generation.</summary>
    public static class WordLearningDialogue
    {
        public const string SourcePath = "TalkSource/TalkCSV/WordLearningDialogue.csv";
        public const string ResourcePath = "TalkData/WordLearningDialogue";
        private static IReadOnlyDictionary<string, string> lines;
        private static readonly string[] requiredKeys = { "unknown_prompt", "meaning_prompt", "greeting_match", "greeting_mismatch", "known_exact", "known_partial", "question", "summary", "saved", "saved_complete", "forget_prompt", "forget_complete", "known_complete", "teacch_more", "button_yes", "button_no", "button_unknown", "button_cancel", "button_confirm", "button_reteach", "button_keep", "button_other_word", "button_nothing", "button_teach_more", "button_return_chat", "button_forget", "button_return_memo", "button_close" };
        private static readonly HashSet<string> tokens = new HashSet<string> { "word", "summary", "question", "facts" };
        public static void ResetCache() => lines = null;
        private static IReadOnlyDictionary<string, string> Data
        {
            get
            {
                if (lines != null) return lines;
                string csv = null;
#if UNITY_EDITOR
                if (global::System.IO.File.Exists(SourcePath)) csv = global::System.IO.File.ReadAllText(SourcePath, global::System.Text.Encoding.UTF8);
#endif
                if (csv == null && !ConversationDataManager.TryLoadEncryptedCsvResource(ResourcePath, out csv, out string error))
                    throw new InvalidOperationException("WordLearningDialogue could not be loaded: " + error);
                lines = ParseCsv(csv); return lines;
            }
        }
        public static IReadOnlyDictionary<string, string> ParseCsv(string csv)
        {
            var rows = WordKnowledge.ReadCsv(csv);
            if (rows.Count < 2) throw new FormatException("WordLearningDialogue: CSV is empty.");
            var headers = rows[0].Select(v => v.Trim().Trim('\uFEFF')).ToList();
            if (!headers.Contains("key") || !headers.Contains("ja") || headers.Distinct().Count() != headers.Count)
                throw new FormatException("WordLearningDialogue: missing/duplicate columns.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].Count != headers.Count) throw new FormatException("WordLearningDialogue: wrong column count at record " + (i + 1));
                string key = rows[i][headers.IndexOf("key")].Trim(), text = rows[i][headers.IndexOf("ja")].Trim();
                if (!Regex.IsMatch(key, "^[a-z][a-z0-9_]*$") || result.ContainsKey(key) || string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && c != '\n' && c != '\r'))
                    throw new FormatException("WordLearningDialogue: empty/invalid/duplicate row at record " + (i + 1));
                foreach (Match match in Regex.Matches(text, @"(?<!\{)\{([^{}]+)\}(?!\})"))
                    if (!tokens.Contains(match.Groups[1].Value)) throw new FormatException("WordLearningDialogue: unknown placeholder in " + key);
                result.Add(key, text);
            }
            foreach (string key in requiredKeys)
                if (!result.ContainsKey(key)) throw new FormatException("WordLearningDialogue: required key missing: " + key);
            return result;
        }
        public static string Format(string key, string word = "", string summary = "", string question = "", string facts = "", IReadOnlyDictionary<string, string> definition = null)
        {
            var data = definition ?? Data;
            if (!data.TryGetValue(key, out string text)) throw new FormatException("WordLearningDialogue: unknown key " + key);
            return text.Replace("{word}", LearnedWordStore.SafeText(word)).Replace("{summary}", summary ?? "")
                .Replace("{question}", question ?? "").Replace("{facts}", facts ?? "");
        }
    }

    public static class WordVariables
    {
        private static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, WordGenre> genres = new Dictionary<string, WordGenre>(StringComparer.OrdinalIgnoreCase)
        { ["random_food"] = WordGenre.Food, ["random_animal"] = WordGenre.Animal, ["random_person"] = WordGenre.Person, ["random_place"] = WordGenre.Place, ["random_object"] = WordGenre.Object, ["random_work"] = WordGenre.Work, ["random_action"] = WordGenre.Action, ["random_other"] = WordGenre.Other, ["random_greeting"] = WordGenre.Greeting };
        private static readonly string[][] defaults = { new[] { "りんご", "水" }, new[] { "猫", "犬" }, new[] { "ご主人" }, new[] { "部屋", "公園" }, new[] { "タブレット", "机" }, new[] { "かくれんぼ" }, new[] { "散歩" }, new[] { "こんにちは" }, new[] { "気持ち" }, Array.Empty<string>() };
        public static void BeginConversation() => values.Clear();
        public static bool IsVariable(string key) => genres.ContainsKey(Clean(key)) || Clean(key) == "random_word" || Clean(key).StartsWith("random_greeting_", StringComparison.Ordinal);
        private static string Clean(string key) => (key ?? "").Trim().Trim('{', '}').Trim().ToLowerInvariant();
        public static DayPeriod CurrentPeriod(ConversationGameState state = null)
        {
            if (state != null && (state.TryGetString("CurrentPhase", out string phase) || state.TryGetString("Phase", out phase)))
            { if (phase == "Day") return DayPeriod.Afternoon; if (Enum.TryParse(phase, true, out DayPeriod p)) return p; }
            var manager = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            return manager != null ? manager.CurrentPeriod : DayPeriod.Morning;
        }
        private static GreetingScenes Scene(ConversationGameState state)
        {
            return state != null && state.TryGetString("GreetingScene", out string s) && Enum.TryParse(s, true, out GreetingScenes scene) ? scene : GreetingScenes.None;
        }
        private static List<string> Candidates(string key, ConversationGameState state, LearnedWordStore store)
        {
            key = Clean(key); var result = new List<string>();
            bool greeting = key.StartsWith("random_greeting_", StringComparison.Ordinal);
            WordGenre genre = WordGenre.Other;
            if (!greeting && key != "random_word" && !genres.TryGetValue(key, out genre)) return result;
            DayPeriod period = CurrentPeriod(state); GreetingScenes scene = Scene(state);
            if (greeting)
            {
                string suffix = key.Substring("random_greeting_".Length);
                if (suffix == "meeting" || suffix == "parting")
                { var expected = suffix == "meeting" ? GreetingScenes.Meeting : GreetingScenes.Parting; if (scene != expected) return result; }
                else
                {
                    DayPeriod expected;
                    if (suffix == "day") expected = DayPeriod.Afternoon;
                    else if (!Enum.TryParse(suffix, true, out expected)) return result;
                    if (period != expected) return result;
                }
                genre = WordGenre.Greeting;
            }
            foreach (var word in store.Words)
            {
                if (key != "random_word" && word.genre != genre) continue;
                if (word.genre == WordGenre.Greeting && !WordKnowledge.MatchesGreeting(word, period, scene)) continue;
                int weight = key == "random_word" && word.genre == WordGenre.Other ? 1 : 4;
                for (int i = 0; i < weight; i++) result.Add(word.display);
            }
            if (!greeting)
            {
                if (key == "random_word")
                    for (int i = 0; i < defaults.Length; i++) { if (i == (int)WordGenre.Greeting) continue; result.AddRange(defaults[i]); }
                else result.AddRange(defaults[(int)genre]);
            }
            return result;
        }
        public static bool HasValue(string key, ConversationGameState state = null, LearnedWordStore store = null) => IsVariable(key) && Candidates(key, state, store ?? LearnedWordStore.Shared).Count > 0;
        public static string Resolve(string key, ConversationGameState state = null)
        {
            key = Clean(key);
            if (values.TryGetValue(key, out string value)) return value;
            var candidates = Candidates(key, state, LearnedWordStore.Shared);
            value = candidates.Count == 0 ? "" : LearnedWordStore.SafeText(candidates[UnityEngine.Random.Range(0, candidates.Count)]);
            values[key] = value; return value;
        }
        public static bool CanResolve(string template, ConversationGameState state)
        {
            bool valid = true;
            ConversationVariableResolver.ReplaceRuntimePlaceholders(template, key => { if (IsVariable(key) && !HasValue(key, state)) valid = false; return null; });
            return valid;
        }
    }
}
