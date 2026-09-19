using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using UnityEngine;

namespace Nekolpos.Dialogue.UnknownWord
{
    public sealed class UnknownWordExtractor
    {
        private const string IgnorePatternsResourcePath = "Dialogue/UnknownWord/IgnorePatterns";
        private const string KnownWordsResourcePath = "Dialogue/UnknownWord/KnownWords";
        private const int MinimumUnknownWordLength = 1;

        private List<string> ignorePatterns;
        private List<KnownWordEntry> knownWords;

        public UnknownWordResult Extract(PlayerInputContext inputContext, DialogueIntentResult intentResult)
        {
            EnsureResourcesLoaded();

            string normalized = NormalizeInput(inputContext != null ? inputContext.NormalizedInput : string.Empty);
            UnknownWordResult result = new UnknownWordResult
            {
                NormalizedInput = normalized
            };

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return result;
            }

            List<NormalizedSegment> remainingSegments = BuildNormalizedSegments(inputContext);
            string remaining = RemoveIgnorePatterns(normalized, result.RemovedPatterns, remainingSegments);
            remaining = RemoveKnownWords(remaining, intentResult, result.MatchedKnownWords, remainingSegments);
            remaining = RemoveMatchedPatterns(remaining, intentResult, result.RemovedPatterns, remainingSegments);
            remaining = NormalizeInput(remaining);
            if (IsIgnorableRemainder(remaining))
            {
                return result;
            }

            string originalUnknownWord = BuildOriginalUnknownWord(inputContext, remainingSegments);
            if (!HasMinimumUnknownWordLength(remaining, originalUnknownWord))
            {
                return result;
            }

            result.HasUnknownWord = true;
            result.UnknownWord = !string.IsNullOrWhiteSpace(originalUnknownWord) ? originalUnknownWord : remaining;
            result.EstimatedCategory = null;
            return result;
        }

        public UnknownWordResult Extract(string normalizedInput, DialogueIntentResult intentResult)
        {
            return Extract(new PlayerInputContext(normalizedInput), intentResult);
        }

        private string RemoveIgnorePatterns(string input, List<string> removedPatterns, List<NormalizedSegment> remainingSegments)
        {
            string remaining = input ?? string.Empty;

            while (!string.IsNullOrEmpty(remaining))
            {
                string matchedSuffix = FindBestBoundaryMatch(remaining, ignorePatterns, matchSuffix: true);
                if (!string.IsNullOrEmpty(matchedSuffix))
                {
                    removedPatterns.Add(matchedSuffix);
                    RemoveSegmentRange(remainingSegments, remaining.Length - matchedSuffix.Length, matchedSuffix.Length);
                    remaining = remaining.Substring(0, remaining.Length - matchedSuffix.Length);
                    continue;
                }

                string matchedPrefix = FindBestBoundaryMatch(remaining, ignorePatterns, matchSuffix: false);
                if (!string.IsNullOrEmpty(matchedPrefix))
                {
                    removedPatterns.Add(matchedPrefix);
                    RemoveSegmentRange(remainingSegments, 0, matchedPrefix.Length);
                    remaining = remaining.Substring(matchedPrefix.Length);
                    continue;
                }

                break;
            }

            return remaining;
        }

        private string RemoveMatchedPatterns(string input, DialogueIntentResult intentResult, List<string> removedPatterns, List<NormalizedSegment> remainingSegments)
        {
            string remaining = input ?? string.Empty;
            if (string.IsNullOrEmpty(remaining) || intentResult == null || intentResult.MatchedPatterns == null)
            {
                return remaining;
            }

            for (int i = 0; i < intentResult.MatchedPatterns.Count; i++)
            {
                string pattern = NormalizeInput(intentResult.MatchedPatterns[i]);
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                remaining = RemoveRegexOccurrences(remaining, pattern, removedPatterns, remainingSegments);
            }

            return remaining;
        }

        private static string RemoveRegexOccurrences(string input, string pattern, List<string> removedPatterns, List<NormalizedSegment> remainingSegments)
        {
            string remaining = input ?? string.Empty;
            if (string.IsNullOrEmpty(remaining) || string.IsNullOrWhiteSpace(pattern))
            {
                return remaining;
            }

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return remaining;
            }

            Match match = regex.Match(remaining);
            while (match.Success && match.Length > 0)
            {
                removedPatterns?.Add(match.Value);
                RemoveSegmentRange(remainingSegments, match.Index, match.Length);
                remaining = remaining.Remove(match.Index, match.Length);
                match = regex.Match(remaining);
            }

            return remaining;
        }

        private string RemoveKnownWords(string input, DialogueIntentResult intentResult, List<string> matchedKnownWords, List<NormalizedSegment> remainingSegments)
        {
            string remaining = input ?? string.Empty;
            List<string> wordsToRemove = new List<string>();

            if (intentResult != null && intentResult.MatchedKnownWords != null)
            {
                for (int i = 0; i < intentResult.MatchedKnownWords.Count; i++)
                {
                    string knownWord = NormalizeInput(intentResult.MatchedKnownWords[i]);
                    if (string.IsNullOrWhiteSpace(knownWord))
                    {
                        continue;
                    }

                    wordsToRemove.Add(knownWord);
                }
            }

            for (int i = 0; i < knownWords.Count; i++)
            {
                wordsToRemove.Add(knownWords[i].Word);
            }

            wordsToRemove = wordsToRemove
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(word => word.Length)
                .ToList();

            for (int i = 0; i < wordsToRemove.Count; i++)
            {
                remaining = RemoveKnownWordOccurrences(remaining, wordsToRemove[i], matchedKnownWords, remainingSegments);
            }

            return remaining;
        }

        private string RemoveKnownWordOccurrences(string input, string knownWord, List<string> matchedKnownWords, List<NormalizedSegment> remainingSegments)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(knownWord))
            {
                return input;
            }

            string remaining = input;
            int searchIndex = remaining.IndexOf(knownWord, StringComparison.Ordinal);
            while (searchIndex >= 0)
            {
                matchedKnownWords.Add(knownWord);
                RemoveSegmentRange(remainingSegments, searchIndex, knownWord.Length);
                remaining = remaining.Remove(searchIndex, knownWord.Length);
                searchIndex = remaining.IndexOf(knownWord, StringComparison.Ordinal);
            }

            return remaining;
        }

        private static string FindBestBoundaryMatch(string value, List<string> patterns, bool matchSuffix)
        {
            if (string.IsNullOrEmpty(value) || patterns == null)
            {
                return null;
            }

            for (int i = 0; i < patterns.Count; i++)
            {
                string pattern = patterns[i];
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                if (matchSuffix && value.EndsWith(pattern, StringComparison.Ordinal))
                {
                    return pattern;
                }

                if (!matchSuffix && value.StartsWith(pattern, StringComparison.Ordinal))
                {
                    return pattern;
                }
            }

            return null;
        }

        private void EnsureResourcesLoaded()
        {
            if (ignorePatterns == null)
            {
                ignorePatterns = LoadIgnorePatterns();
            }

            if (knownWords == null)
            {
                knownWords = LoadKnownWords();
            }
        }

        private static List<string> LoadIgnorePatterns()
        {
            List<string> patterns = new List<string>();
            TextAsset asset = Resources.Load<TextAsset>(IgnorePatternsResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[UnknownWordExtractor] IgnorePatterns.csv was not found.");
                return patterns;
            }

            string[] lines = SplitLines(asset.text);
            for (int i = 1; i < lines.Length; i++)
            {
                string pattern = NormalizeInput(lines[i]);
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    patterns.Add(pattern);
                }
            }

            return patterns
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(pattern => pattern.Length)
                .ToList();
        }

        private static List<KnownWordEntry> LoadKnownWords()
        {
            List<KnownWordEntry> entries = new List<KnownWordEntry>();
            TextAsset asset = Resources.Load<TextAsset>(KnownWordsResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[UnknownWordExtractor] KnownWords.csv was not found.");
                return entries;
            }

            string[] lines = SplitLines(asset.text);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] columns = line.Split(',');
                if (columns.Length <= 0)
                {
                    continue;
                }

                string word = NormalizeInput(columns[0]);
                if (string.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                string category = columns.Length > 1 ? columns[1].Trim() : string.Empty;
                entries.Add(new KnownWordEntry(word, category));
            }

            return entries
                .GroupBy(entry => entry.Word, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(entry => entry.Word.Length)
                .ToList();
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static string NormalizeInput(string input)
        {
            string normalized = InputNormalizer.NormalizeForMatching(input);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            char[] buffer = normalized.ToCharArray();
            int writeIndex = 0;
            char previous = '\0';
            for (int i = 0; i < buffer.Length; i++)
            {
                char current = buffer[i];
                if ((current == 'ー' || current == '・') && previous == current)
                {
                    continue;
                }

                buffer[writeIndex++] = current;
                previous = current;
            }

            return new string(buffer, 0, writeIndex);
        }

        private static bool HasMinimumUnknownWordLength(string normalized, string originalUnknownWord)
        {
            if (!string.IsNullOrEmpty(normalized) && normalized.Length >= MinimumUnknownWordLength)
            {
                return true;
            }

            string display = (originalUnknownWord ?? string.Empty).Trim(' ', '　', '。', '、', '！', '？', '!', '?');
            return display.Length >= MinimumUnknownWordLength;
        }

        private static bool IsIgnorableRemainder(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!IsIgnorableRemainderCharacter(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIgnorableRemainderCharacter(char character)
        {
            switch (character)
            {
                case 'が':
                case 'を':
                case 'は':
                case 'も':
                case 'に':
                case 'へ':
                case 'と':
                case 'の':
                case 'ね':
                case 'よ':
                case 'な':
                    return true;
                default:
                    return false;
            }
        }

        private static List<NormalizedSegment> BuildNormalizedSegments(PlayerInputContext inputContext)
        {
            List<NormalizedSegment> segments = new List<NormalizedSegment>();
            if (inputContext == null)
            {
                return segments;
            }

            if (inputContext.NormalizedSegments != null && inputContext.NormalizedSegments.Count > 0)
            {
                for (int i = 0; i < inputContext.NormalizedSegments.Count; i++)
                {
                    Backgammon.Conversation.NormalizedInputSegment segment = inputContext.NormalizedSegments[i];
                    segments.Add(new NormalizedSegment(segment.OriginalStart, segment.OriginalEnd));
                }

                return segments;
            }

            if (string.IsNullOrEmpty(inputContext.OriginalInput))
            {
                return segments;
            }

            for (int i = 0; i < inputContext.OriginalInput.Length; i++)
            {
                string normalizedChar = NormalizeInput(inputContext.OriginalInput[i].ToString());
                if (string.IsNullOrEmpty(normalizedChar))
                {
                    continue;
                }

                for (int j = 0; j < normalizedChar.Length; j++)
                {
                    segments.Add(new NormalizedSegment(i, i + 1));
                }
            }

            return segments;
        }

        private static void RemoveSegmentRange(List<NormalizedSegment> segments, int start, int length)
        {
            if (segments == null || start < 0 || length <= 0 || start >= segments.Count)
            {
                return;
            }

            int safeLength = Math.Min(length, segments.Count - start);
            segments.RemoveRange(start, safeLength);
        }

        private static string BuildOriginalUnknownWord(PlayerInputContext inputContext, List<NormalizedSegment> segments)
        {
            if (inputContext == null || segments == null || segments.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            int lastEnd = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                NormalizedSegment segment = segments[i];
                if (segment.OriginalStart < 0 ||
                    segment.OriginalEnd <= segment.OriginalStart ||
                    segment.OriginalEnd > inputContext.OriginalInput.Length ||
                    segment.OriginalEnd <= lastEnd)
                {
                    continue;
                }

                if (lastEnd >= 0 && segment.OriginalStart > lastEnd)
                {
                    AppendSkippedDisplayJoiners(
                        builder,
                        inputContext.OriginalInput,
                        lastEnd,
                        segment.OriginalStart);
                }

                builder.Append(inputContext.OriginalInput.Substring(segment.OriginalStart, segment.OriginalEnd - segment.OriginalStart));
                lastEnd = segment.OriginalEnd;
            }

            if (lastEnd >= 0 && lastEnd < inputContext.OriginalInput.Length)
            {
                AppendSkippedDisplayJoiners(
                    builder,
                    inputContext.OriginalInput,
                    lastEnd,
                    inputContext.OriginalInput.Length);
            }

            return builder.ToString().Trim(' ', '　', '。', '、', '！', '？', '!', '?');
        }

        private static void AppendSkippedDisplayJoiners(StringBuilder builder, string originalInput, int start, int end)
        {
            if (builder == null || string.IsNullOrEmpty(originalInput) || start < 0 || end <= start)
            {
                return;
            }

            int safeEnd = Math.Min(end, originalInput.Length);
            for (int i = start; i < safeEnd; i++)
            {
                char character = originalInput[i];
                if (!IsDisplayJoiner(character))
                {
                    break;
                }

                if (!string.IsNullOrEmpty(NormalizeInput(character.ToString())))
                {
                    break;
                }

                builder.Append(character);
            }
        }

        private static bool IsDisplayJoiner(char character)
        {
            switch (character)
            {
                case 'ー':
                case 'ｰ':
                case '－':
                case '―':
                case '‐':
                case '‑':
                case '–':
                case '—':
                case '〜':
                case '～':
                case '~':
                    return true;
                default:
                    return false;
            }
        }

        private readonly struct NormalizedSegment
        {
            public NormalizedSegment(int originalStart, int originalEnd)
            {
                OriginalStart = originalStart;
                OriginalEnd = originalEnd;
            }

            public int OriginalStart { get; }
            public int OriginalEnd { get; }
        }

        private readonly struct KnownWordEntry
        {
            public KnownWordEntry(string word, string category)
            {
                Word = word;
                Category = category;
            }

            public string Word { get; }
            public string Category { get; }
        }
    }
}
