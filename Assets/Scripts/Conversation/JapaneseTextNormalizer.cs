using System;
using System.Collections.Generic;
using System.Text;

namespace Backgammon.Conversation
{
    public static class InputNormalizer
    {
        public static string NormalizeForMatching(string input)
        {
            return JapaneseTextNormalizer.NormalizeInput(input);
        }
    }

    public static class JapaneseTextNormalizer
    {
        public static string NormalizeInput(string input)
        {
            return NormalizeInputDetailed(input).NormalizedText;
        }

        public static JapaneseInputNormalizationResult NormalizeInputDetailed(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new JapaneseInputNormalizationResult(string.Empty, new List<NormalizedInputSegment>());
            }

            List<TextFragment> fragments = RemoveSkippedInputFragments(BuildTextFragments(input));
            JapaneseInputNormalizationResult dictionaryResult = new ProjectDictionaryReadingConverter(KanjiReadingDictionary.Default.Entries)
                .Convert(fragments);

            StringBuilder builder = new StringBuilder(dictionaryResult.NormalizedText.Length);
            List<NormalizedInputSegment> segments = new List<NormalizedInputSegment>(dictionaryResult.Segments.Count);
            for (int i = 0; i < dictionaryResult.NormalizedText.Length; i++)
            {
                char normalizedChar = NormalizeInputChar(dictionaryResult.NormalizedText[i]);
                if (ShouldSkip(normalizedChar))
                {
                    continue;
                }

                builder.Append(normalizedChar);
                if (i < dictionaryResult.Segments.Count)
                {
                    segments.Add(dictionaryResult.Segments[i]);
                }
                else
                {
                    segments.Add(new NormalizedInputSegment(0, 0));
                }
            }

            return new JapaneseInputNormalizationResult(builder.ToString(), segments);
        }

        public static string NormalizeToken(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var normalizedChar = NormalizeChar(input[i]);
                if (ShouldSkipInToken(normalizedChar))
                {
                    continue;
                }

                builder.Append(normalizedChar);
            }

            return builder.ToString();
        }

        public static string NormalizeNameCallToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var normalizedChar = NormalizeChar(input[i]);
                if (normalizedChar == '\uFEFF' || normalizedChar == '\u200B')
                {
                    continue;
                }

                builder.Append(normalizedChar);
            }

            return builder.ToString().Trim().Trim('　');
        }

        public static List<TextFragment> BuildTextFragments(string input)
        {
            List<TextFragment> fragments = new List<TextFragment>();
            if (string.IsNullOrEmpty(input))
            {
                return fragments;
            }

            for (int i = 0; i < input.Length; i++)
            {
                string normalized = NormalizeUnicode(input[i].ToString());
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                for (int j = 0; j < normalized.Length; j++)
                {
                    fragments.Add(new TextFragment(normalized[j].ToString(), i, i + 1));
                }
            }

            return fragments;
        }

        private static List<TextFragment> RemoveSkippedInputFragments(List<TextFragment> fragments)
        {
            if (fragments == null || fragments.Count == 0)
            {
                return new List<TextFragment>();
            }

            List<TextFragment> result = new List<TextFragment>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++)
            {
                TextFragment fragment = fragments[i];
                if (string.IsNullOrEmpty(fragment.Text))
                {
                    continue;
                }

                bool skip = true;
                for (int j = 0; j < fragment.Text.Length; j++)
                {
                    if (!ShouldSkip(NormalizeInputChar(fragment.Text[j])))
                    {
                        skip = false;
                        break;
                    }
                }

                if (!skip)
                {
                    result.Add(fragment);
                }
            }

            return result;
        }

        public static string NormalizeSurfaceForDictionary(string input)
        {
            return NormalizeUnicode(input ?? string.Empty).Trim();
        }

        public static string NormalizeReadingForDictionary(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string normalized = NormalizeUnicode(input);
            StringBuilder builder = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char normalizedChar = NormalizeInputChar(normalized[i]);
                if (ShouldSkip(normalizedChar))
                {
                    continue;
                }

                builder.Append(normalizedChar);
            }

            return builder.ToString();
        }

        private static string NormalizeUnicode(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            try
            {
                return input.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return input;
            }
        }

        private static char NormalizeInputChar(char value)
        {
            if (value == '?')
            {
                return '？';
            }

            return NormalizeChar(value);
        }

        private static char NormalizeChar(char value)
        {
            if (IsWaveDashLike(value))
            {
                return 'ー';
            }

            if (value >= 'ァ' && value <= 'ヶ')
            {
                return (char)(value - 'ァ' + 'ぁ');
            }

            if (value >= 'A' && value <= 'Z')
            {
                return char.ToLowerInvariant(value);
            }

            if (value == '　')
            {
                return ' ';
            }

            return value;
        }

        private static bool IsWaveDashLike(char value)
        {
            switch (value)
            {
                case '~':
                case '～':
                case '〜':
                case '∼':
                case '∽':
                case '≋':
                case '〰':
                case '﹋':
                case '﹌':
                case '﹏':
                    return true;
                default:
                    return false;
            }
        }

        public readonly struct TextFragment
        {
            public TextFragment(string text, int originalStart, int originalEnd)
            {
                Text = text ?? string.Empty;
                OriginalStart = originalStart;
                OriginalEnd = originalEnd;
            }

            public string Text { get; }
            public int OriginalStart { get; }
            public int OriginalEnd { get; }
        }

        private static bool ShouldSkip(char value)
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
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldSkipInToken(char value)
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
                    return true;
                default:
                    return false;
            }
        }
    }
}
