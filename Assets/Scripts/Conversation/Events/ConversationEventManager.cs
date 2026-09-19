using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class ConversationEventManager : MonoBehaviour
    {
        [SerializeField] [TextArea(10, 40)] private string eventDefinitionsJson =
            "[\n" +
            "  {\n" +
            "    \"id\": \"cat_go_out_sequence\",\n" +
            "    \"sequence\": [\n" +
            "      {\n" +
            "        \"event\": \"play_se\",\n" +
            "        \"params\": { \"name\": \"door_open\" }\n" +
            "      },\n" +
            "      {\n" +
            "        \"event\": \"fade_out\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"event\": \"time_skip\",\n" +
            "        \"params\": { \"hours\": 2 }\n" +
            "      },\n" +
            "      {\n" +
            "        \"event\": \"fade_in\"\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "]";

        private IConversationEventBridge bridge;

        public string EventDefinitionsJson => eventDefinitionsJson;

        public IConversationEventBridge Bridge
        {
            get => bridge ??= new LoggingConversationEventBridge();
            set => bridge = value;
        }

        public Task<ConversationEventExecutionResult> ExecuteEventByIdAsync(
            string eventId,
            ConversationGameState gameState,
            CancellationToken cancellationToken = default)
        {
            var executor = new ConversationEventExecutor(ConversationEventExecutor.ParseDefinitionsJson(eventDefinitionsJson));
            return executor.ExecuteEventByIdAsync(eventId, gameState, Bridge, cancellationToken);
        }

        public Task<ConversationEventExecutionResult> ExecuteEventSequenceAsync(
            IEnumerable<string> eventIds,
            ConversationGameState gameState,
            CancellationToken cancellationToken = default)
        {
            var executor = new ConversationEventExecutor(ConversationEventExecutor.ParseDefinitionsJson(eventDefinitionsJson));
            return executor.ExecuteSequenceByIdsAsync(eventIds, gameState, Bridge, cancellationToken);
        }

        public ConversationEventExecutor CreateExecutor()
        {
            return new ConversationEventExecutor(ConversationEventExecutor.ParseDefinitionsJson(eventDefinitionsJson));
        }
    }
}
