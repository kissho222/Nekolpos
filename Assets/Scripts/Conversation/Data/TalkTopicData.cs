using System;

namespace Backgammon.Conversation
{
    [Serializable]
    public sealed class TalkTopicData
    {
        public string TalkID;
        public string TalkTitle;
        public string Source;
        public string SourceKey;
        public string Genre;
        public string Idea;
        public string Import;
        public string Keywords;
        public bool Enabled;
    }
}
