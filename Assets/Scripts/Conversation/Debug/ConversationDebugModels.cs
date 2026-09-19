using System;
using System.Collections.Generic;

namespace Backgammon.Conversation
{
    public enum ConversationDebugStateValueKind
    {
        Integer,
        Boolean,
        String,
        StringList
    }

    [Serializable]
    public sealed class ConversationDebugRunResult
    {
        public string rawInput;
        public ConversationParseResult parseResult;
        public ConversationRouteResult routeResult;
        public ConversationEventExecutionResult eventResult;
        public List<string> activeTags = new();
        public string stateBeforeJson = "{}";
        public string stateAfterRoutingJson = "{}";
        public string stateAfterEventsJson = "{}";
        public bool wasForcedRoute;
        public string forcedRouteId;
    }
}
