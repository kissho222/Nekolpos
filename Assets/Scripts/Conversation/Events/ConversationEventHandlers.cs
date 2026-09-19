using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class LoggingConversationEventBridge : IConversationEventBridge
    {
        public Task PerformEventAsync(string eventName, ConversationEventParameters parameters, CancellationToken cancellationToken)
        {
            Debug.Log($"Conversation event performed: {eventName}");
            return Task.CompletedTask;
        }
    }

    public sealed class ConversationEventHandlerRegistry
    {
        private readonly Dictionary<string, IConversationEventHandler> handlers = new(StringComparer.Ordinal);

        public void Register(IConversationEventHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.EventName))
            {
                return;
            }

            handlers[handler.EventName] = handler;
        }

        public bool TryGet(string eventName, out IConversationEventHandler handler)
        {
            if (!string.IsNullOrWhiteSpace(eventName) && handlers.TryGetValue(eventName, out var existing))
            {
                handler = existing;
                return true;
            }

            handler = null;
            return false;
        }

        public static ConversationEventHandlerRegistry CreateDefault()
        {
            var registry = new ConversationEventHandlerRegistry();
            registry.Register(new BridgeDelegatingEventHandler("fade_in"));
            registry.Register(new BridgeDelegatingEventHandler("fade_out"));
            registry.Register(new BridgeDelegatingEventHandler("play_se"));
            registry.Register(new BridgeDelegatingEventHandler("bgm_change"));
            registry.Register(new TimeSkipEventHandler());
            registry.Register(new CatGoOutEventHandler());
            registry.Register(new CatReturnEventHandler());
            registry.Register(new PhaseChangeEventHandler());
            registry.Register(new WaitForRunningEventHandler());
            return registry;
        }
    }

    public sealed class BridgeDelegatingEventHandler : IConversationEventHandler
    {
        public BridgeDelegatingEventHandler(string eventName)
        {
            EventName = eventName;
        }

        public string EventName { get; }

        public async Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.DispatchedEvents.Add(EventName);
            invocation.Context.EventLogs.Add($"event:{EventName}");
            await invocation.Context.Bridge.PerformEventAsync(EventName, invocation.Parameters, cancellationToken);
        }
    }

    public sealed class TimeSkipEventHandler : IConversationEventHandler
    {
        public string EventName => "time_skip";

        public async Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.DispatchedEvents.Add(EventName);
            invocation.Parameters.TryGetInt("hours", out var hours);
            if (hours == 0)
            {
                hours = 1;
            }

            invocation.Context.State.IncrementInt("TotalTimeSkippedHours", hours);
            invocation.Context.State.SetInt("LastTimeSkipHours", hours);

            if (invocation.Parameters.TryGetString("state_key", out var stateKey) && !string.IsNullOrWhiteSpace(stateKey))
            {
                invocation.Context.State.IncrementInt(stateKey, hours);
            }

            invocation.Context.EventLogs.Add($"event:{EventName} hours={hours}");
            await invocation.Context.Bridge.PerformEventAsync(EventName, invocation.Parameters, cancellationToken);
        }
    }

    public sealed class CatGoOutEventHandler : IConversationEventHandler
    {
        public string EventName => "cat_go_out";

        public async Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.DispatchedEvents.Add(EventName);
            invocation.Context.State.SetBool("CatIsOut", true);
            invocation.Context.State.SetString("CatLocation", "Outside");
            invocation.Context.EventLogs.Add($"event:{EventName} location=Outside");
            await invocation.Context.Bridge.PerformEventAsync(EventName, invocation.Parameters, cancellationToken);
        }
    }

    public sealed class CatReturnEventHandler : IConversationEventHandler
    {
        public string EventName => "cat_return";

        public async Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.DispatchedEvents.Add(EventName);
            invocation.Context.State.SetBool("CatIsOut", false);
            invocation.Context.State.SetString("CatLocation", "Home");
            invocation.Context.EventLogs.Add($"event:{EventName} location=Home");
            await invocation.Context.Bridge.PerformEventAsync(EventName, invocation.Parameters, cancellationToken);
        }
    }

    public sealed class PhaseChangeEventHandler : IConversationEventHandler
    {
        public string EventName => "phase_change";

        public async Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.DispatchedEvents.Add(EventName);
            if (!invocation.Parameters.TryGetString("phase", out var phase))
            {
                phase = string.Empty;
            }

            var stateKey = "Phase";
            if (invocation.Parameters.TryGetString("state_key", out var customKey) && !string.IsNullOrWhiteSpace(customKey))
            {
                stateKey = customKey;
            }

            invocation.Context.State.SetString(stateKey, phase);
            invocation.Context.EventLogs.Add($"event:{EventName} {stateKey}={phase}");
            await invocation.Context.Bridge.PerformEventAsync(EventName, invocation.Parameters, cancellationToken);
        }
    }

    public sealed class WaitForRunningEventHandler : IConversationEventHandler
    {
        public string EventName => "wait_for_running";

        public Task ExecuteAsync(ConversationEventInvocation invocation, CancellationToken cancellationToken)
        {
            invocation.Context.EventLogs.Add($"event:{EventName}");
            return invocation.Executor.WaitForPendingAsync(cancellationToken);
        }
    }
}
