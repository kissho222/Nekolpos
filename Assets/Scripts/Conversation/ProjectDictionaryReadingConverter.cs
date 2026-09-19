using System.Collections.Generic;
using System.Text;

namespace Backgammon.Conversation
{
    public sealed class ProjectDictionaryReadingConverter : IJapaneseReadingConverter
    {
        private readonly IReadOnlyList<ReadingDictionaryEntry> entries;

        public ProjectDictionaryReadingConverter(IReadOnlyList<ReadingDictionaryEntry> entries)
        {
            this.entries = entries ?? new List<ReadingDictionaryEntry>();
        }

        public string Convert(string input)
        {
            return Convert(JapaneseTextNormalizer.BuildTextFragments(input)).NormalizedText;
        }

        public JapaneseInputNormalizationResult Convert(IReadOnlyList<JapaneseTextNormalizer.TextFragment> fragments)
        {
            List<JapaneseTextNormalizer.TextFragment> source = fragments != null
                ? new List<JapaneseTextNormalizer.TextFragment>(fragments)
                : new List<JapaneseTextNormalizer.TextFragment>();

            StringBuilder builder = new StringBuilder(source.Count);
            List<NormalizedInputSegment> segments = new List<NormalizedInputSegment>();
            int index = 0;
            while (index < source.Count)
            {
                ReadingDictionaryEntry match = FindMatch(source, index);
                if (match != null)
                {
                    int originalStart = source[index].OriginalStart;
                    int originalEnd = source[index + match.Surface.Length - 1].OriginalEnd;
                    string reading = match.Reading ?? string.Empty;
                    for (int i = 0; i < reading.Length; i++)
                    {
                        builder.Append(reading[i]);
                        segments.Add(new NormalizedInputSegment(originalStart, originalEnd));
                    }

                    index += match.Surface.Length;
                    continue;
                }

                JapaneseTextNormalizer.TextFragment fragment = source[index];
                builder.Append(fragment.Text);
                for (int i = 0; i < fragment.Text.Length; i++)
                {
                    segments.Add(new NormalizedInputSegment(fragment.OriginalStart, fragment.OriginalEnd));
                }

                index++;
            }

            return new JapaneseInputNormalizationResult(builder.ToString(), segments);
        }

        private ReadingDictionaryEntry FindMatch(List<JapaneseTextNormalizer.TextFragment> source, int startIndex)
        {
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                ReadingDictionaryEntry entry = entries[entryIndex];
                if (entry == null || !entry.IsUsable)
                {
                    continue;
                }

                string surface = entry.Surface;
                if (startIndex + surface.Length > source.Count)
                {
                    continue;
                }

                bool matched = true;
                for (int i = 0; i < surface.Length; i++)
                {
                    if (source[startIndex + i].Text.Length != 1 || source[startIndex + i].Text[0] != surface[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
