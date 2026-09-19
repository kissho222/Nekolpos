using System;
using System.Collections.Generic;

namespace Backgammon.Conversation
{
    [Serializable]
    public sealed class ConversationRouteDefinition
    {
        public string id;
        public int priority;
        public bool playOnce;
        public int randomWeight = 1;
        public bool allowImmediateRepeat;
        public List<string> intents = new();
        public List<string> requiredTags = new();
        public List<string> excludedTags = new();
        public List<string> stateConditions = new();
        public List<string> responses = new();
        public List<string> events = new();
        public List<ConversationGameStateEffect> effects = new();
        public ConversationRouteMetadata metadata = new();
    }

    [Serializable]
    public sealed class ConversationRouteMetadata
    {
        public string sourceFormat;
        public int sourceRowNumber;
        public string sourceGroupId;
        public string input;
        public string regexId;
        public string regexJa;
        public string regexZh;
        public string regexEn;
        public string altText;
        public string responseType;
        public string actionId;
        public string condition;
        public string speechControl;
        public string emotionChangeType;
        public int emotionChangeValue;
        public int pattern;
        public int order;
        public int waitTime;
        public bool hideUi;
        public string animation;
        public bool callOnly;
        public string choiceYesPattern;
        public string choiceNoPattern;
        public bool sequenceProgressHold;
        public int randomRepeatLimit = -1;
    }

    [Serializable]
    public sealed class ConversationRouteRequest
    {
        public ConversationParseResult parseResult;
        public ConversationGameState gameState;
        public List<string> tags = new();
        public int? randomSeed;
    }

    [Serializable]
    public sealed class ConversationRouteResult
    {
        public bool matched;
        public string conversationId;
        public int priority;
        public string response;
        public List<string> events = new();
        public List<ConversationGameStateEffect> effects = new();
        public string nextStateJson = "{}";
        public List<ConversationRouteCandidateDebug> candidates = new();
    }

    [Serializable]
    public sealed class ConversationRouteCandidateDebug
    {
        public string conversationId;
        public int priority;
        public bool passedAllConditions;
        public bool selected;
        public string rejectionReason;
        public List<ConversationRouteConditionDebug> conditions = new();
    }

    [Serializable]
    public sealed class ConversationRouteConditionDebug
    {
        public string label;
        public bool passed;
        public string reason;
    }
}
