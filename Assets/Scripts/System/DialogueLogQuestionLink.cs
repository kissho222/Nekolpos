using System;

namespace Nekolpos.System
{
    [Serializable]
    public sealed class DialogueLogQuestionLink
    {
        public string keyword;
        public string insertText;

        public DialogueLogQuestionLink()
        {
        }

        public DialogueLogQuestionLink(string keyword, string insertText)
        {
            this.keyword = keyword;
            this.insertText = insertText;
        }

        public DialogueLogQuestionLink Clone()
        {
            return new DialogueLogQuestionLink(keyword, insertText);
        }
    }
}
