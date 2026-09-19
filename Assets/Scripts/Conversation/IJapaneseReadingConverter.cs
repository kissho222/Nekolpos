using System.Collections.Generic;

namespace Backgammon.Conversation
{
    public interface IJapaneseReadingConverter
    {
        string Convert(string input);
        JapaneseInputNormalizationResult Convert(IReadOnlyList<JapaneseTextNormalizer.TextFragment> fragments);
    }
}
