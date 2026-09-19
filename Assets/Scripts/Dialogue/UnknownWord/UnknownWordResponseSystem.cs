using System.Collections.Generic;
using Nekolpos.Data;
using Nekolpos.System;

namespace Nekolpos.Dialogue.UnknownWord
{
    public sealed class UnknownWordResponseSystem
    {
        public DialogueReactionData CreateReaction(UnknownWordResult result)
        {
            if (result == null || !result.HasUnknownWord || string.IsNullOrWhiteSpace(result.UnknownWord))
            {
                return null;
            }

            Dictionary<string, string> placeholders = new Dictionary<string, string>
            {
                { "unknown_word", result.UnknownWord }
            };

            DialogueReactionData reaction = BasicSystemDialogueCatalog.CreateReaction(
                BasicSystemDialogueCatalog.UnknownWordPromptKey,
                placeholders,
                result.UnknownWord + "ってなに？");

            if (reaction == null)
            {
                return null;
            }

            reaction.IntentID = "UNKNOWN_WORD";
            reaction.ReactionType = "UnknownWord";
            return reaction;
        }

        public string CreateResponse(UnknownWordResult result)
        {
            return CreateReaction(result)?.TextJP ?? string.Empty;
        }
    }
}
