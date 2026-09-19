using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Backgammon.Conversation;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class KanjiReadingDictionaryCandidateGenerator
    {
        private const string ApprovedDictionaryPath = "TalkSource/TalkCSV/KanjiReadingDictionary.csv";
        private const string CandidateDictionaryPath = "TalkSource/TalkCSV/KanjiReadingDictionaryCandidates.csv";
        private const string ConflictReportPath = "TalkSource/TalkCSV/KanjiReadingDictionaryConflicts.txt";

        private static readonly string[] SourceRoots =
        {
            "Assets",
            "TalkSource/TalkCSV",
            "TalkSource/Dialogue"
        };

        private static readonly Dictionary<string, CandidateSeed> Seeds = new Dictionary<string, CandidateSeed>(StringComparer.Ordinal)
        {
            ["対馬山猫"] = new("つしまやまねこ", true, false, "FELIDAE"),
            ["ツシマヤマネコ"] = new("つしまやまねこ", true, false, "FELIDAE"),
            ["山猫"] = new("やまねこ", true, false, "FELIDAE"),
            ["猫又"] = new("ねこまた", true, false, "PROJECT_TERM"),
            ["猫股"] = new("ねこまた", true, false, "PROJECT_TERM"),
            ["猫"] = new("ねこ", true, false, "CAT_WORD"),
            ["三毛猫"] = new("みけねこ", true, false, "CAT_BREED"),
            ["黒猫"] = new("くろねこ", true, false, "CAT_BREED"),
            ["白猫"] = new("しろねこ", true, false, "CAT_BREED"),
            ["肉球"] = new("にくきゅう", true, false, "CAT_WORD"),
            ["甘噛み"] = new("あまがみ", true, false, "CAT_WORD"),
            ["毛繕い"] = new("けづくろい", true, false, "CAT_WORD"),
            ["猫パンチ"] = new("ねこぱんち", true, false, "CAT_WORD"),
            ["今日"] = new("きょう", true, false, "COMMON"),
            ["雨"] = new("あめ", true, false, "WEATHER"),
            ["天気"] = new("てんき", true, false, "WEATHER"),
            ["気象"] = new("きしょう", true, false, "METEOROLOGY"),
            ["お腹"] = new("おなか", true, false, "COMMON"),
            ["御飯"] = new("ごはん", true, false, "COMMON"),
            ["ご飯"] = new("ごはん", true, false, "COMMON"),
            ["食事"] = new("しょくじ", true, false, "COMMON"),
            ["食べ"] = new("たべ", true, false, "COMMON"),
            ["眠い"] = new("ねむい", true, false, "COMMON"),
            ["睡眠"] = new("すいみん", true, false, "COMMON"),
            ["好き"] = new("すき", true, false, "COMMON"),
            ["可愛い"] = new("かわいい", true, false, "COMMON"),
            ["爪"] = new("つめ", false, true, "CAT_WORD"),
            ["犬"] = new("いぬ", false, true, "COMMON"),
            ["職場"] = new("しょくば", false, true, "COMMON"),
            ["悪口"] = new("わるぐち", false, true, "COMMON"),
            ["罵倒"] = new("ばとう", false, true, "COMMON"),
            ["家"] = new("いえ", false, true, "COMMON"),
            ["人気"] = new("", false, true, "COMMON"),
            ["一日"] = new("", false, true, "COMMON"),
            ["生物"] = new("", false, true, "COMMON"),
            ["上手"] = new("", false, true, "COMMON"),
            ["行った"] = new("", false, true, "COMMON"),
            ["明日"] = new("", false, true, "COMMON"),
            ["大人"] = new("", false, true, "COMMON"),
            ["方"] = new("ほう", false, true, "COMMON"),
            ["言って"] = new("いって", false, true, "COMMON"),
            ["言った"] = new("いった", false, true, "COMMON"),
            ["一度"] = new("いちど", false, true, "COMMON"),
            ["もう一度"] = new("もういちど", false, true, "COMMON"),
            ["毎日"] = new("まいにち", false, true, "COMMON"),
            ["自分"] = new("じぶん", false, true, "COMMON"),
            ["自覚"] = new("じかく", false, true, "COMMON"),
            ["心配"] = new("しんぱい", false, true, "COMMON"),
            ["時間"] = new("じかん", false, true, "COMMON"),
            ["一緒"] = new("いっしょ", false, true, "COMMON"),
            ["気になる"] = new("きになる", false, true, "COMMON"),
            ["気にな"] = new("きにな", false, true, "COMMON"),
            ["縮め"] = new("ちぢめ", false, true, "COMMON"),
            ["戦え"] = new("たたかえ", false, true, "COMMON"),
            ["遊び"] = new("あそび", false, true, "COMMON"),
            ["遊ぶ"] = new("あそぶ", false, true, "COMMON")
        };

        [MenuItem("Tools/Nekolpos/Generate Kanji Reading Dictionary Candidates")]
        public static void Generate()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CandidateDictionaryPath));

            List<ReadingDictionaryEntry> approved = LoadDictionaryFile(ApprovedDictionaryPath);
            List<ReadingDictionaryEntry> existingCandidates = LoadDictionaryFile(CandidateDictionaryPath);
            Dictionary<string, ReadingDictionaryEntry> approvedBySurface = ToSurfaceMap(approved);
            Dictionary<string, ReadingDictionaryEntry> candidateBySurface = ToSurfaceMap(existingCandidates);

            int beforeCount = candidateBySurface.Count;
            Dictionary<string, CandidateDiscovery> discoveredSources = DiscoverSources();
            List<string> conflicts = new List<string>();

            foreach (KeyValuePair<string, CandidateDiscovery> pair in discoveredSources)
            {
                string surface = pair.Key;
                CandidateDiscovery discovery = pair.Value;
                bool hasSeed = Seeds.TryGetValue(surface, out CandidateSeed seed);
                string reading = hasSeed ? seed.Reading : discovery.Reading;
                bool enabled = hasSeed && seed.Enabled;
                bool needsReview = hasSeed ? seed.NeedsReview : true;
                string category = hasSeed ? seed.Category : discovery.Category;
                string note = hasSeed
                    ? (seed.NeedsReview ? "Generated candidate; review before enabling." : "Generated high-confidence candidate.")
                    : discovery.Note;

                if (!hasSeed && string.IsNullOrWhiteSpace(reading))
                {
                    continue;
                }

                string sourceFiles = string.Join("|", discovery.SourceFiles);
                if (approvedBySurface.TryGetValue(surface, out ReadingDictionaryEntry approvedEntry))
                {
                    approvedEntry.SourceFiles = MergePipeList(approvedEntry.SourceFiles, sourceFiles);
                    continue;
                }

                if (candidateBySurface.TryGetValue(surface, out ReadingDictionaryEntry existing))
                {
                    if (!string.IsNullOrWhiteSpace(existing.Reading) &&
                        !string.IsNullOrWhiteSpace(reading) &&
                        !string.Equals(existing.Reading, reading, StringComparison.Ordinal))
                    {
                        existing.Enabled = false;
                        existing.NeedsReview = true;
                        existing.Note = MergePipeList(existing.Note, $"Reading conflict: {existing.Reading}|{reading}");
                        conflicts.Add($"{surface}: {existing.Reading} / {reading}");
                    }

                    existing.SourceFiles = MergePipeList(existing.SourceFiles, sourceFiles);
                    existing.Note = MergePipeList(existing.Note, note);
                    continue;
                }

                candidateBySurface.Add(surface, new ReadingDictionaryEntry
                {
                    Surface = surface,
                    Reading = reading,
                    Enabled = enabled,
                    NeedsReview = needsReview,
                    Category = category,
                    SourceFiles = sourceFiles,
                    Note = note
                });
            }

            List<ReadingDictionaryEntry> output = candidateBySurface.Values
                .OrderBy(entry => entry.Enabled ? 0 : 1)
                .ThenByDescending(entry => (entry.Surface ?? string.Empty).Length)
                .ThenBy(entry => entry.Surface, StringComparer.Ordinal)
                .ToList();

            SaveDictionaryFile(CandidateDictionaryPath, output);
            SaveConflictReport(conflicts);
            AssetDatabase.Refresh();

            int afterCount = output.Count;
            Debug.Log(
                $"[KanjiReadingDictionaryCandidateGenerator] candidates before={beforeCount}, after={afterCount}, added={Math.Max(0, afterCount - beforeCount)}, conflicts={conflicts.Count}. " +
                $"Output: {CandidateDictionaryPath}");
        }

        private static Dictionary<string, CandidateDiscovery> DiscoverSources()
        {
            Dictionary<string, CandidateDiscovery> result = new Dictionary<string, CandidateDiscovery>(StringComparer.Ordinal);
            List<ReadingDictionaryEntry> knownEntries = LoadDictionaryFile(ApprovedDictionaryPath)
                .Concat(LoadDictionaryFile(CandidateDictionaryPath))
                .Where(entry => entry != null && entry.IsUsable)
                .OrderByDescending(entry => (entry.Surface ?? string.Empty).Length)
                .ToList();

            foreach (string root in SourceRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(root, "*.csv", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i].Replace('\\', '/');
                    if (path.Contains("ProjectBackups/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = ReadAllTextWithFallback(path);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[KanjiReadingDictionaryCandidateGenerator] Failed to read {path}: {exception.Message}");
                        continue;
                    }

                    foreach (string surface in Seeds.Keys)
                    {
                        if (!text.Contains(surface, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        GetOrCreateDiscovery(result, surface).SourceFiles.Add(Path.GetFileName(path));
                    }

                    DiscoverCsvPairedReadings(result, path, text, knownEntries);
                }
            }

            return result;
        }

        private static string ReadAllTextWithFallback(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    return Encoding.GetEncoding("shift_jis").GetString(bytes);
                }
                catch
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
        }

        private static void DiscoverCsvPairedReadings(
            Dictionary<string, CandidateDiscovery> result,
            string path,
            string csv,
            IReadOnlyList<ReadingDictionaryEntry> knownEntries)
        {
            List<CsvRecord> records;
            try
            {
                records = CsvParser.Parse(csv ?? string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[KanjiReadingDictionaryCandidateGenerator] Failed to parse {path}: {exception.Message}");
                return;
            }

            if (records.Count <= 1)
            {
                return;
            }

            Dictionary<string, int> headers = BuildHeaderMap(records[0].Fields);
            AddCandidatePairs(result, records, headers, path, knownEntries, "TalkTitle", "Import", "TalkTopicMaster paired TalkTitle/Import.");
            AddCandidatePairs(result, records, headers, path, knownEntries, "input", "regex_jp", "Dialogue regex input/regex_jp pair.");
        }

        private static void AddCandidatePairs(
            Dictionary<string, CandidateDiscovery> result,
            IReadOnlyList<CsvRecord> records,
            Dictionary<string, int> headers,
            string path,
            IReadOnlyList<ReadingDictionaryEntry> knownEntries,
            string surfaceHeader,
            string readingHeader,
            string note)
        {
            if (!headers.ContainsKey(surfaceHeader) || !headers.ContainsKey(readingHeader))
            {
                return;
            }

            string sourceFile = Path.GetFileName(path);
            for (int i = 1; i < records.Count; i++)
            {
                List<string> fields = records[i].Fields;
                string surfaceText = Read(fields, headers, surfaceHeader);
                string readingText = Read(fields, headers, readingHeader);
                foreach (ReadingDictionaryEntry candidate in InferCandidatesFromPair(surfaceText, readingText, knownEntries))
                {
                    CandidateDiscovery discovery = GetOrCreateDiscovery(result, candidate.Surface);
                    discovery.SourceFiles.Add(sourceFile);
                    discovery.Category = MergeCategory(discovery.Category, candidate.Category);
                    discovery.Note = MergePipeList(discovery.Note, note);

                    if (string.IsNullOrWhiteSpace(discovery.Reading))
                    {
                        discovery.Reading = candidate.Reading;
                        continue;
                    }

                    if (!string.Equals(discovery.Reading, candidate.Reading, StringComparison.Ordinal))
                    {
                        discovery.Note = MergePipeList(discovery.Note, $"Reading conflict: {discovery.Reading}|{candidate.Reading}");
                    }
                }
            }
        }

        private static IEnumerable<ReadingDictionaryEntry> InferCandidatesFromPair(
            string surfaceText,
            string readingText,
            IReadOnlyList<ReadingDictionaryEntry> knownEntries)
        {
            string normalizedReading = JapaneseTextNormalizer.NormalizeReadingForDictionary(readingText);
            if (string.IsNullOrWhiteSpace(surfaceText) || string.IsNullOrWhiteSpace(normalizedReading))
            {
                yield break;
            }

            string normalizedSurface = JapaneseTextNormalizer.NormalizeSurfaceForDictionary(RemoveTemplateTokens(surfaceText));
            if (!ContainsKanji(normalizedSurface))
            {
                yield break;
            }

            List<string> chunks = BuildCandidateSubstrings(normalizedSurface);
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                CandidateMatch match = MatchReading(chunks[chunkIndex], normalizedReading, knownEntries);
                if (!match.IsValid)
                {
                    continue;
                }

                for (int i = 0; i < match.Surfaces.Count; i++)
                {
                    string surface = match.Surfaces[i];
                    string reading = match.Readings[i];
                    if (string.IsNullOrWhiteSpace(surface) ||
                        string.IsNullOrWhiteSpace(reading) ||
                        surface.Length < 2 ||
                        !ContainsKanji(surface) ||
                        surface.Length > 12 ||
                        reading.Length > 24)
                    {
                        continue;
                    }

                    yield return new ReadingDictionaryEntry
                    {
                        Surface = surface,
                        Reading = reading,
                        Enabled = false,
                        NeedsReview = true,
                        Category = "AUTO_PAIR",
                        Note = "Auto-inferred from paired kanji/kana CSV text; review before enabling."
                    };
                }
            }
        }

        private static CandidateMatch MatchReading(string surface, string reading, IReadOnlyList<ReadingDictionaryEntry> knownEntries)
        {
            List<CandidateToken> tokens = TokenizeSurface(surface, knownEntries);
            if (tokens.Count == 0 ||
                !tokens.Any(token => token.IsUnknownKanji) ||
                !tokens.Any(token => !token.IsUnknownKanji && !string.IsNullOrEmpty(token.Reading)))
            {
                return CandidateMatch.Invalid;
            }

            return MatchTokens(tokens, 0, reading, 0, new List<string>(), new List<string>(), true);
        }

        private static CandidateMatch MatchTokens(
            IReadOnlyList<CandidateToken> tokens,
            int tokenIndex,
            string reading,
            int readingIndex,
            List<string> surfaces,
            List<string> readings,
            bool requireEnd)
        {
            if (tokenIndex >= tokens.Count)
            {
                return !requireEnd || readingIndex == reading.Length
                    ? new CandidateMatch(true, new List<string>(surfaces), new List<string>(readings))
                    : CandidateMatch.Invalid;
            }

            CandidateToken token = tokens[tokenIndex];
            if (!token.IsUnknownKanji)
            {
                if (readingIndex + token.Reading.Length > reading.Length ||
                    !string.Equals(reading.Substring(readingIndex, token.Reading.Length), token.Reading, StringComparison.Ordinal))
                {
                    return CandidateMatch.Invalid;
                }

                return MatchTokens(tokens, tokenIndex + 1, reading, readingIndex + token.Reading.Length, surfaces, readings, requireEnd);
            }

            int remainingLiteralLength = 0;
            for (int i = tokenIndex + 1; i < tokens.Count; i++)
            {
                if (!tokens[i].IsUnknownKanji)
                {
                    remainingLiteralLength += tokens[i].Reading.Length;
                }
            }

            int maxLength = reading.Length - readingIndex - remainingLiteralLength;
            for (int length = 1; length <= maxLength; length++)
            {
                string candidateReading = reading.Substring(readingIndex, length);
                if (!IsKanaOnly(candidateReading))
                {
                    continue;
                }

                surfaces.Add(token.Surface);
                readings.Add(candidateReading);
                CandidateMatch match = MatchTokens(tokens, tokenIndex + 1, reading, readingIndex + length, surfaces, readings, requireEnd);
                if (match.IsValid)
                {
                    return match;
                }

                surfaces.RemoveAt(surfaces.Count - 1);
                readings.RemoveAt(readings.Count - 1);
            }

            return CandidateMatch.Invalid;
        }

        private static List<CandidateToken> TokenizeSurface(string surface, IReadOnlyList<ReadingDictionaryEntry> knownEntries)
        {
            List<CandidateToken> tokens = new List<CandidateToken>();
            int index = 0;
            while (index < surface.Length)
            {
                ReadingDictionaryEntry known = FindKnownEntry(surface, index, knownEntries);
                if (known != null)
                {
                    tokens.Add(new CandidateToken(known.Surface, known.Reading, false));
                    index += known.Surface.Length;
                    continue;
                }

                char current = surface[index];
                if (IsKanji(current))
                {
                    int start = index;
                    index++;
                    while (index < surface.Length && IsKanji(surface[index]) && FindKnownEntry(surface, index, knownEntries) == null)
                    {
                        index++;
                    }

                    tokens.Add(new CandidateToken(surface.Substring(start, index - start), string.Empty, true));
                    continue;
                }

                char normalized = NormalizeSurfaceLiteral(current);
                if (!ShouldIgnoreLiteral(normalized))
                {
                    tokens.Add(new CandidateToken(current.ToString(), normalized.ToString(), false));
                }

                index++;
            }

            return tokens;
        }

        private static ReadingDictionaryEntry FindKnownEntry(string surface, int index, IReadOnlyList<ReadingDictionaryEntry> knownEntries)
        {
            for (int i = 0; i < knownEntries.Count; i++)
            {
                ReadingDictionaryEntry entry = knownEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Surface) || string.IsNullOrWhiteSpace(entry.Reading))
                {
                    continue;
                }

                if (index + entry.Surface.Length <= surface.Length &&
                    string.Equals(surface.Substring(index, entry.Surface.Length), entry.Surface, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private static List<string> BuildCandidateSubstrings(string surface)
        {
            List<string> result = new List<string>();
            string compact = RemoveIgnoredLiterals(surface);
            for (int start = 0; start < compact.Length; start++)
            {
                for (int length = 1; length <= 24 && start + length <= compact.Length; length++)
                {
                    string candidate = compact.Substring(start, length);
                    if (ContainsKanji(candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        private static string RemoveTemplateTokens(string value)
        {
            StringBuilder builder = new StringBuilder(value?.Length ?? 0);
            bool inTemplate = false;
            for (int i = 0; i < (value?.Length ?? 0); i++)
            {
                if (!inTemplate && i + 1 < value.Length && value[i] == '{' && value[i + 1] == '{')
                {
                    inTemplate = true;
                    i++;
                    continue;
                }

                if (inTemplate && i + 1 < value.Length && value[i] == '}' && value[i + 1] == '}')
                {
                    inTemplate = false;
                    i++;
                    continue;
                }

                if (!inTemplate)
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }

        private static string RemoveIgnoredLiterals(string value)
        {
            StringBuilder builder = new StringBuilder(value?.Length ?? 0);
            for (int i = 0; i < (value?.Length ?? 0); i++)
            {
                char normalized = NormalizeSurfaceLiteral(value[i]);
                if (!ShouldIgnoreLiteral(normalized))
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }

        private static CandidateDiscovery GetOrCreateDiscovery(Dictionary<string, CandidateDiscovery> result, string surface)
        {
            if (!result.TryGetValue(surface, out CandidateDiscovery discovery))
            {
                discovery = new CandidateDiscovery();
                result.Add(surface, discovery);
            }

            return discovery;
        }

        private static string MergeCategory(string left, string right)
        {
            return string.IsNullOrWhiteSpace(left) ? right ?? string.Empty : left;
        }

        private static bool ContainsKanji(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (IsKanji(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKanji(char value)
        {
            return (value >= '\u4E00' && value <= '\u9FFF') || value == '々' || value == '〆' || value == 'ヵ' || value == 'ヶ';
        }

        private static bool IsKanaOnly(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'ぁ' && c <= 'ゖ') || c == 'ー'))
                {
                    return false;
                }
            }

            return true;
        }

        private static char NormalizeSurfaceLiteral(char value)
        {
            if (value >= 'ァ' && value <= 'ヶ')
            {
                return (char)(value - 'ァ' + 'ぁ');
            }

            if (value >= 'A' && value <= 'Z')
            {
                return char.ToLowerInvariant(value);
            }

            if (value == '?')
            {
                return '？';
            }

            if (value == '　')
            {
                return ' ';
            }

            return value;
        }

        private static bool ShouldIgnoreLiteral(char value)
        {
            switch (value)
            {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '、':
                case '。':
                case '！':
                case '!':
                case '？':
                case '〜':
                case '~':
                case 'ー':
                case 'ｰ':
                case '－':
                case '―':
                case '‐':
                case '‑':
                case '–':
                case '—':
                case '…':
                case '・':
                    return true;
                default:
                    return false;
            }
        }

        private static Dictionary<string, ReadingDictionaryEntry> ToSurfaceMap(IEnumerable<ReadingDictionaryEntry> entries)
        {
            Dictionary<string, ReadingDictionaryEntry> result = new Dictionary<string, ReadingDictionaryEntry>(StringComparer.Ordinal);
            if (entries == null)
            {
                return result;
            }

            foreach (ReadingDictionaryEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Surface))
                {
                    continue;
                }

                if (!result.TryGetValue(entry.Surface, out ReadingDictionaryEntry existing))
                {
                    result.Add(entry.Surface, entry.Clone());
                    continue;
                }

                if (string.Equals(existing.Reading, entry.Reading, StringComparison.Ordinal))
                {
                    existing.SourceFiles = MergePipeList(existing.SourceFiles, entry.SourceFiles);
                }
                else
                {
                    existing.Enabled = false;
                    existing.NeedsReview = true;
                    existing.Note = MergePipeList(existing.Note, $"Reading conflict: {existing.Reading}|{entry.Reading}");
                }
            }

            return result;
        }

        private static List<ReadingDictionaryEntry> LoadDictionaryFile(string path)
        {
            if (!File.Exists(path))
            {
                return new List<ReadingDictionaryEntry>();
            }

            return LoadDictionaryText(File.ReadAllText(path, Encoding.UTF8));
        }

        private static List<ReadingDictionaryEntry> LoadDictionaryText(string csv)
        {
            List<CsvRecord> records = CsvParser.Parse(csv ?? string.Empty);
            List<ReadingDictionaryEntry> result = new List<ReadingDictionaryEntry>();
            if (records.Count <= 1)
            {
                return result;
            }

            Dictionary<string, int> headers = BuildHeaderMap(records[0].Fields);
            for (int i = 1; i < records.Count; i++)
            {
                List<string> fields = records[i].Fields;
                if (fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                result.Add(new ReadingDictionaryEntry
                {
                    Surface = Read(fields, headers, "Surface"),
                    Reading = Read(fields, headers, "Reading"),
                    Enabled = ReadingDictionaryEntry.ParseBool(Read(fields, headers, "Enabled")),
                    NeedsReview = ReadingDictionaryEntry.ParseBool(Read(fields, headers, "NeedsReview")),
                    Category = Read(fields, headers, "Category"),
                    SourceFiles = Read(fields, headers, "SourceFiles"),
                    Note = Read(fields, headers, "Note")
                });
            }

            return result;
        }

        private static void SaveDictionaryFile(string path, IReadOnlyList<ReadingDictionaryEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Surface,Reading,Enabled,NeedsReview,Category,SourceFiles,Note");
            for (int i = 0; i < entries.Count; i++)
            {
                ReadingDictionaryEntry entry = entries[i];
                builder.Append(CsvEscape(entry.Surface)).Append(',')
                    .Append(CsvEscape(entry.Reading)).Append(',')
                    .Append(entry.Enabled ? "true" : "false").Append(',')
                    .Append(entry.NeedsReview ? "true" : "false").Append(',')
                    .Append(CsvEscape(entry.Category)).Append(',')
                    .Append(CsvEscape(entry.SourceFiles)).Append(',')
                    .Append(CsvEscape(entry.Note)).AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static void SaveConflictReport(List<string> conflicts)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Kanji reading dictionary conflicts");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Count: " + (conflicts?.Count ?? 0));
            if (conflicts != null)
            {
                for (int i = 0; i < conflicts.Count; i++)
                {
                    builder.AppendLine(conflicts[i]);
                }
            }

            File.WriteAllText(ConflictReportPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static Dictionary<string, int> BuildHeaderMap(List<string> headers)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string header = (headers[i] ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (!string.IsNullOrWhiteSpace(header) && !result.ContainsKey(header))
                {
                    result.Add(header, i);
                }
            }

            return result;
        }

        private static string Read(List<string> fields, Dictionary<string, int> headers, string key)
        {
            return headers.TryGetValue(key, out int index) && index >= 0 && index < fields.Count
                ? fields[index]?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static string CsvEscape(string value)
        {
            string safe = value ?? string.Empty;
            if (safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return safe;
            }

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static string MergePipeList(string left, string right)
        {
            SortedSet<string> values = new SortedSet<string>(StringComparer.Ordinal);
            AddPipeValues(values, left);
            AddPipeValues(values, right);
            return string.Join("|", values);
        }

        private static void AddPipeValues(SortedSet<string> values, string raw)
        {
            if (values == null || string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        private sealed class CandidateDiscovery
        {
            public string Reading = string.Empty;
            public string Category = "AUTO_PAIR";
            public readonly SortedSet<string> SourceFiles = new SortedSet<string>(StringComparer.Ordinal);
            public string Note = string.Empty;
        }

        private readonly struct CandidateToken
        {
            public CandidateToken(string surface, string reading, bool isUnknownKanji)
            {
                Surface = surface ?? string.Empty;
                Reading = reading ?? string.Empty;
                IsUnknownKanji = isUnknownKanji;
            }

            public string Surface { get; }
            public string Reading { get; }
            public bool IsUnknownKanji { get; }
        }

        private readonly struct CandidateMatch
        {
            public static readonly CandidateMatch Invalid = new CandidateMatch(false, new List<string>(), new List<string>());

            public CandidateMatch(bool isValid, List<string> surfaces, List<string> readings)
            {
                IsValid = isValid;
                Surfaces = surfaces ?? new List<string>();
                Readings = readings ?? new List<string>();
            }

            public bool IsValid { get; }
            public List<string> Surfaces { get; }
            public List<string> Readings { get; }
        }

        private readonly struct CandidateSeed
        {
            public CandidateSeed(string reading, bool enabled, bool needsReview, string category)
            {
                Reading = reading ?? string.Empty;
                Enabled = enabled;
                NeedsReview = needsReview;
                Category = category ?? string.Empty;
            }

            public string Reading { get; }
            public bool Enabled { get; }
            public bool NeedsReview { get; }
            public string Category { get; }
        }

        private sealed class CsvRecord
        {
            public int LineNumber;
            public List<string> Fields = new List<string>();
        }

        private static class CsvParser
        {
            public static List<CsvRecord> Parse(string csv)
            {
                List<CsvRecord> records = new List<CsvRecord>();
                List<string> fields = new List<string>();
                StringBuilder field = new StringBuilder();
                bool inQuotes = false;
                int lineNumber = 1;
                int rowLineNumber = 1;

                for (int index = 0; index < csv.Length; index++)
                {
                    char current = csv[index];
                    if (current == '"')
                    {
                        if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && current == ',')
                    {
                        fields.Add(field.ToString());
                        field.Length = 0;
                        continue;
                    }

                    if (!inQuotes && (current == '\r' || current == '\n'))
                    {
                        fields.Add(field.ToString());
                        field.Length = 0;
                        records.Add(new CsvRecord { LineNumber = rowLineNumber, Fields = fields });
                        fields = new List<string>();
                        if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        {
                            index++;
                        }

                        lineNumber++;
                        rowLineNumber = lineNumber;
                        continue;
                    }

                    field.Append(current);
                    if (current == '\n')
                    {
                        lineNumber++;
                    }
                }

                if (inQuotes)
                {
                    throw new FormatException("CSV contains an unterminated quoted field.");
                }

                fields.Add(field.ToString());
                if (fields.Count > 1 || fields[0].Length > 0 || records.Count == 0)
                {
                    records.Add(new CsvRecord { LineNumber = rowLineNumber, Fields = fields });
                }

                return records;
            }
        }
    }
}
