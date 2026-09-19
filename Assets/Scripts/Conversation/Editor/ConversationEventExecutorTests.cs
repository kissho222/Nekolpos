using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationEventExecutorTests
    {
        [Test]
        [Timeout(5000)]
        public async Task ExecuteEventById_RunsSequenceInOrderAndUpdatesState()
        {
            var executor = CreateExecutor(
                "[\n" +
                "  {\n" +
                "    \"id\": \"cat_go_out_sequence\",\n" +
                "    \"sequence\": [\n" +
                "      { \"event\": \"play_se\", \"params\": { \"name\": \"door_open\" } },\n" +
                "      { \"event\": \"fade_out\" },\n" +
                "      { \"event\": \"time_skip\", \"params\": { \"hours\": 2 } },\n" +
                "      { \"event\": \"fade_in\" }\n" +
                "    ]\n" +
                "  }\n" +
                "]");

            var bridge = new RecordingBridge();
            var result = await WithTimeout(
                executor.ExecuteEventByIdAsync("cat_go_out_sequence", new ConversationGameState(), bridge),
                1000);

            CollectionAssert.AreEqual(
                new[] { "play_se", "fade_out", "time_skip", "fade_in" },
                bridge.StartedEvents);

            var nextState = ConversationGameState.FromJson(result.nextStateJson);
            Assert.That(nextState.TryGetInt("TotalTimeSkippedHours", out var hours), Is.True);
            Assert.That(hours, Is.EqualTo(2));
        }

        [Test]
        [Timeout(5000)]
        public async Task ExecuteSequenceByIds_RunsReferencedDefinitionsInOrder()
        {
            var executor = CreateExecutor(
                "[\n" +
                "  { \"id\": \"first\", \"event\": \"fade_out\" },\n" +
                "  { \"id\": \"second\", \"event\": \"fade_in\" }\n" +
                "]");

            var bridge = new RecordingBridge();
            var result = await WithTimeout(
                executor.ExecuteSequenceByIdsAsync(new[] { "first", "second" }, new ConversationGameState(), bridge),
                1000);

            CollectionAssert.AreEqual(new[] { "first", "second" }, result.executedEventIds);
            CollectionAssert.AreEqual(new[] { "fade_out", "fade_in" }, bridge.StartedEvents);
        }

        [Test]
        [Timeout(5000)]
        public async Task ExecuteEventById_SupportsAsyncOverlapAndWaitForRunning()
        {
            var executor = CreateExecutor(
                "[\n" +
                "  {\n" +
                "    \"id\": \"audio_mix\",\n" +
                "    \"sequence\": [\n" +
                "      { \"event\": \"bgm_change\", \"wait_for_completion\": false },\n" +
                "      { \"event\": \"play_se\", \"params\": { \"name\": \"notify\" } },\n" +
                "      { \"event\": \"wait_for_running\" }\n" +
                "    ]\n" +
                "  }\n" +
                "]");

            var bridge = new RecordingBridge();
            var executionTask = executor.ExecuteEventByIdAsync("audio_mix", new ConversationGameState(), bridge);

            Assert.That(
                SpinWait.SpinUntil(() => bridge.StartedEvents.Contains("play_se"), 1000),
                Is.True,
                "play_se was not dispatched before the timeout.");
            Assert.That(bridge.StartedEvents[0], Is.EqualTo("bgm_change"));
            Assert.That(bridge.StartedEvents[1], Is.EqualTo("play_se"));
            Assert.That(executionTask.IsCompleted, Is.False);

            bridge.Complete("bgm_change");
            var result = await WithTimeout(executionTask, 1000);

            Assert.That(result.succeeded, Is.True);
        }

        [Test]
        [Timeout(5000)]
        public async Task ExecuteEventById_UpdatesPhaseAndCatState()
        {
            var executor = CreateExecutor(
                "[\n" +
                "  {\n" +
                "    \"id\": \"lunch_return\",\n" +
                "    \"sequence\": [\n" +
                "      { \"event\": \"cat_go_out\" },\n" +
                "      { \"event\": \"phase_change\", \"params\": { \"phase\": \"Lunch\" } },\n" +
                "      { \"event\": \"cat_return\" }\n" +
                "    ]\n" +
                "  }\n" +
                "]");

            var result = await WithTimeout(
                executor.ExecuteEventByIdAsync("lunch_return", new ConversationGameState(), new RecordingBridge()),
                1000);
            var nextState = ConversationGameState.FromJson(result.nextStateJson);

            Assert.That(nextState.TryGetString("Phase", out var phase), Is.True);
            Assert.That(phase, Is.EqualTo("Lunch"));
            Assert.That(nextState.TryGetBool("CatIsOut", out var catIsOut), Is.True);
            Assert.That(catIsOut, Is.False);
            Assert.That(nextState.TryGetString("CatLocation", out var catLocation), Is.True);
            Assert.That(catLocation, Is.EqualTo("Home"));
        }

        [Test]
        [Timeout(5000)]
        public async Task ExecuteEventById_AppliesNodeEffects()
        {
            var executor = CreateExecutor(
                "[\n" +
                "  {\n" +
                "    \"id\": \"effect_only\",\n" +
                "    \"event\": \"fade_out\",\n" +
                "    \"effects\": [\n" +
                "      { \"type\": \"set\", \"key\": \"EmergencyFoodEvent\", \"value\": true }\n" +
                "    ]\n" +
                "  }\n" +
                "]");

            var result = await WithTimeout(
                executor.ExecuteEventByIdAsync("effect_only", new ConversationGameState(), new RecordingBridge()),
                1000);
            var nextState = ConversationGameState.FromJson(result.nextStateJson);

            Assert.That(nextState.TryGetBool("EmergencyFoodEvent", out var value), Is.True);
            Assert.That(value, Is.True);
        }

        private static ConversationEventExecutor CreateExecutor(string json)
        {
            return new ConversationEventExecutor(ConversationEventExecutor.ParseDefinitionsJson(json));
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMilliseconds)
        {
            var timeoutTask = Task.Delay(timeoutMilliseconds);
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail($"Async operation did not complete within {timeoutMilliseconds} ms.");
            }

            return await task;
        }

        private sealed class RecordingBridge : IConversationEventBridge
        {
            private readonly Dictionary<string, TaskCompletionSource<bool>> pending = new(StringComparer.Ordinal);

            public List<string> StartedEvents { get; } = new();

            public Task PerformEventAsync(string eventName, ConversationEventParameters parameters, CancellationToken cancellationToken)
            {
                StartedEvents.Add(eventName);
                if (eventName == "bgm_change")
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending[eventName] = tcs;
                    cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                    return tcs.Task;
                }

                return Task.CompletedTask;
            }

            public void Complete(string eventName)
            {
                if (pending.TryGetValue(eventName, out var tcs))
                {
                    tcs.TrySetResult(true);
                }
            }
        }
    }
}
