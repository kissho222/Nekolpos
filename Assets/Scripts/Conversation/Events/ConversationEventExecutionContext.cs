using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Backgammon.Conversation
{
    public interface IConversationEventBridge
    {
        Task PerformEventAsync(string eventName, ConversationEventParameters parameters, CancellationToken cancellationToken);
    }

    public interface IConversationEventHandler
    {
        string EventName { get; }
        Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken);
    }

    public sealed class ConversationEventInvocation
    {
        public ConversationEventInvocation(
            string eventName,
            ConversationEventParameters parameters,
            ConversationEventExecutionContext context,
            ConversationEventExecutor executor)
        {
            EventName = eventName;
            Parameters = parameters ?? new ConversationEventParameters();
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public string EventName { get; }
        public ConversationEventParameters Parameters { get; }
        public ConversationEventExecutionContext Context { get; }
        public ConversationEventExecutor Executor { get; }
    }

    public sealed class ConversationEventExecutionContext
    {
        public ConversationEventExecutionContext(ConversationGameState state, IConversationEventBridge bridge)
        {
            State = state ?? new ConversationGameState();
            Bridge = bridge ?? new LoggingConversationEventBridge();
        }

        public ConversationGameState State { get; }
        public IConversationEventBridge Bridge { get; }
        public List<string> ExecutedEventIds { get; } = new();
        public List<string> DispatchedEvents { get; } = new();
        public List<string> EventLogs { get; } = new();
        public List<ConversationEffectExecutionDebug> AppliedEffects { get; } = new();
    }
}
