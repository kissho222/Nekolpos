using System;
using System.Collections.Generic;

namespace Nekolpos.Dialogue.UnknownWord
{
    [Serializable]
    public sealed class UnknownWordResult
    {
        public bool HasUnknownWord;
        public string UnknownWord;
        public string EstimatedCategory;
        public List<string> RemovedPatterns = new List<string>();
        public List<string> MatchedKnownWords = new List<string>();
        public string NormalizedInput;
    }
}
