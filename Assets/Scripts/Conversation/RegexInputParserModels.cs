using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class PlayerInputContext
    {
        public string OriginalInput { get; }
        public string NormalizedInput { get; }
        public IReadOnlyList<NormalizedInputSegment> NormalizedSegments { get; }

        public PlayerInputContext(string originalInput)
        {
            OriginalInput = originalInput ?? string.Empty;
            JapaneseInputNormalizationResult result = JapaneseTextNormalizer.NormalizeInputDetailed(OriginalInput);
            NormalizedInput = result.NormalizedText;
            NormalizedSegments = result.Segments;
        }

        public string GetOriginalSubstringForNormalizedRange(int normalizedIndex, int normalizedLength)
        {
            if (normalizedIndex < 0 || normalizedLength <= 0 || string.IsNullOrEmpty(OriginalInput))
            {
                return string.Empty;
            }

            int safeStart = Math.Max(0, normalizedIndex);
            int safeEnd = Math.Min(NormalizedSegments.Count, normalizedIndex + normalizedLength);
            int originalStart = -1;
            int originalEnd = -1;

            for (int i = safeStart; i < safeEnd; i++)
            {
                NormalizedInputSegment segment = NormalizedSegments[i];
                if (segment.OriginalStart < 0 || segment.OriginalEnd <= segment.OriginalStart)
                {
                    continue;
                }

                if (originalStart < 0 || segment.OriginalStart < originalStart)
                {
                    originalStart = segment.OriginalStart;
                }

                if (segment.OriginalEnd > originalEnd)
                {
                    originalEnd = segment.OriginalEnd;
                }
            }

            if (originalStart < 0 || originalEnd <= originalStart)
            {
                return string.Empty;
            }

            return OriginalInput.Substring(originalStart, originalEnd - originalStart);
        }
    }

    [Serializable]
    public sealed class RegexIntentRule
    {
        [TextArea(1, 2)] public string pattern;
        public string intent;
        public int priority = 100;
    }

    [Serializable]
    public sealed class KeywordCategoryRule
    {
        public string word;
        public string type = "food_category";
        public string value;
        public int priority = 100;
    }

    [Serializable]
    public sealed class ConversationParseConfig
    {
        public List<RegexIntentRule> intentRules = new();
        public List<KeywordCategoryRule> categoryRules = new();
    }

    [Serializable]
    public sealed class IntentMatch
    {
        public string intent;
        public int priority;
    }

    [Serializable]
    public sealed class CategoryMatch
    {
        public string type;
        public string value;
    }

    [Serializable]
    public sealed class ConversationParseResult
    {
        public string input;
        public string normalizedInput;
        public List<IntentMatch> intents = new();
        public List<CategoryMatch> categories = new();
        public List<RegexIntentRuleMatch> matchedRegexRules = new();
        public List<KeywordCategoryRuleMatch> matchedCategoryRules = new();

        public IntentMatch PrimaryIntent => intents.Count > 0 ? intents[0] : null;
    }

    [Serializable]
    public sealed class RegexIntentRuleMatch
    {
        public string pattern;
        public string intent;
        public int priority;
    }

    [Serializable]
    public sealed class KeywordCategoryRuleMatch
    {
        public string word;
        public string type;
        public string value;
        public int priority;
    }
}
