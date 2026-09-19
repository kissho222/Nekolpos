using System.Collections.Generic;

namespace Backgammon.Conversation
{
    public sealed class JapaneseInputNormalizationResult
    {
        public JapaneseInputNormalizationResult(string normalizedText, List<NormalizedInputSegment> segments)
        {
            NormalizedText = normalizedText ?? string.Empty;
            Segments = segments ?? new List<NormalizedInputSegment>();
        }

        public string NormalizedText { get; }
        public List<NormalizedInputSegment> Segments { get; }
    }

    public readonly struct NormalizedInputSegment
    {
        public NormalizedInputSegment(int originalStart, int originalEnd)
        {
            OriginalStart = originalStart;
            OriginalEnd = originalEnd;
        }

        public int OriginalStart { get; }
        public int OriginalEnd { get; }
    }
}
