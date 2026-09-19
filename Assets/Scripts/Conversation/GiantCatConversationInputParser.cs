using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class GiantCatConversationInputParser : MonoBehaviour
    {
        [SerializeField] private bool logSampleOnStart = true;
        [SerializeField] [TextArea(2, 4)] private string sampleInput = "おさかなたべたい";
        [SerializeField] private ConversationParseConfig config = new();

        public ConversationParseResult Parse(string input)
        {
            return CreateParser().Parse(input);
        }

        public ConversationParseResult Parse(PlayerInputContext inputContext)
        {
            return CreateParser().Parse(inputContext);
        }

        public string ParseToJson(string input, bool prettyPrint = true)
        {
            return CreateParser().ParseToJson(input, prettyPrint);
        }

        public string ParseToJson(PlayerInputContext inputContext, bool prettyPrint = true)
        {
            return CreateParser().ParseToJson(inputContext, prettyPrint);
        }

        [ContextMenu("Apply Default Giant Cat Rules")]
        public void ApplyDefaultRules()
        {
            config = GiantCatConversationDefaults.CreateConfig();
        }

        private void Reset()
        {
            ApplyDefaultRules();
        }

        private void Start()
        {
            if (!logSampleOnStart)
            {
                return;
            }

            Debug.Log(ParseToJson(sampleInput, true), this);
        }

        private RegexInputParser CreateParser()
        {
            return new RegexInputParser(config);
        }
    }
}
