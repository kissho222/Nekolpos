using UnityEngine;

namespace Nekolpos.System
{
    public class TextEditPreviewState : ScriptableObject
    {
        public string SourceCsvPath;
        public string RegexId;
        public string Pattern;
        public int SequenceIndex;
        public int SequenceCount;
        public int Order;
        public string ResponseType;
        public string ActionId;
        public string Condition;
        public string StateSummary;
        public string SpeakerName = "猫又";

        [TextArea(3, 8)]
        public string Message;

        [TextArea(3, 8)]
        public string MessageChinese;

        [TextArea(3, 8)]
        public string MessageEnglish;

        [TextArea(2, 6)]
        public string Warnings;

        public long Revision;
    }
}
