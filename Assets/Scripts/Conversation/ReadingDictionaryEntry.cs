using System;

namespace Backgammon.Conversation
{
    public sealed class ReadingDictionaryEntry
    {
        public string Surface { get; set; } = string.Empty;
        public string Reading { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool NeedsReview { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SourceFiles { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;

        public bool IsUsable =>
            Enabled &&
            !string.IsNullOrWhiteSpace(Surface) &&
            !string.IsNullOrWhiteSpace(Reading);

        public ReadingDictionaryEntry Clone()
        {
            return new ReadingDictionaryEntry
            {
                Surface = Surface ?? string.Empty,
                Reading = Reading ?? string.Empty,
                Enabled = Enabled,
                NeedsReview = NeedsReview,
                Category = Category ?? string.Empty,
                SourceFiles = SourceFiles ?? string.Empty,
                Note = Note ?? string.Empty
            };
        }

        public static bool ParseBool(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
