using System;
using System.Collections.Generic;
using Backgammon.Conversation;

namespace Nekolpos.Dialogue.UnknownWord
{
    [Serializable]
    public sealed class DialogueIntentResult
    {
        public string NormalizedInput;
        public string PrimaryIntent;
        public List<string> MatchedPatterns = new List<string>();
        public List<string> MatchedKnownWords = new List<string>();

        public static DialogueIntentResult FromConversationParseResult(ConversationParseResult parseResult)
        {
            DialogueIntentResult result = new DialogueIntentResult
            {
                NormalizedInput = parseResult != null ? parseResult.normalizedInput ?? string.Empty : string.Empty,
                PrimaryIntent = parseResult != null && parseResult.PrimaryIntent != null
                    ? parseResult.PrimaryIntent.intent
                    : string.Empty
            };

            if (parseResult == null)
            {
                return result;
            }

            for (int i = 0; i < parseResult.matchedRegexRules.Count; i++)
            {
                RegexIntentRuleMatch match = parseResult.matchedRegexRules[i];
                if (match != null && !string.IsNullOrWhiteSpace(match.pattern))
                {
                    result.MatchedPatterns.Add(match.pattern);
                }
            }

            for (int i = 0; i < parseResult.matchedCategoryRules.Count; i++)
            {
                KeywordCategoryRuleMatch match = parseResult.matchedCategoryRules[i];
                if (match != null && !string.IsNullOrWhiteSpace(match.word))
                {
                    result.MatchedKnownWords.Add(match.word);
                }
            }

            return result;
        }
    }
}
