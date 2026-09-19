using Backgammon.Conversation;
using Nekolpos.StatusSystem;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.Tests.Editor
{
    public sealed class StatusManagerTests
    {
        [Test]
        public void SetValue_ChangesOnlyRequestedStatus_AndProjectsToConversationState()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                ConversationGameStateManager conversationState = gameObject.AddComponent<ConversationGameStateManager>();
                StatusManager manager = gameObject.AddComponent<StatusManager>();

                int previousHostility = manager.GetValue(StatusType.Hostility);
                manager.SetValue(StatusType.Affection, 80, "Test", "single-status-change");

                Assert.That(manager.GetValue(StatusType.Affection), Is.EqualTo(80));
                Assert.That(manager.GetValue(StatusType.Hostility), Is.EqualTo(previousHostility));
                Assert.That(conversationState.State.TryGetInt("Affection", out int projectedAffection), Is.True);
                Assert.That(projectedAffection, Is.EqualTo(80));
                Assert.That(conversationState.State.TryGetInt("affection", out int projectedAffectionLowercase), Is.True);
                Assert.That(projectedAffectionLowercase, Is.EqualTo(80));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RepresentativeState_UsesTopTwoValues_AndPrefersMostRecentlyChangedTie()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                ConversationGameStateManager conversationState = gameObject.AddComponent<ConversationGameStateManager>();
                StatusManager manager = gameObject.AddComponent<StatusManager>();
                manager.SetValue(StatusType.Affection, 80, "Test");
                manager.SetValue(StatusType.Hostility, 80, "Test");
                manager.SetValue(StatusType.Concern, 70, "Test");

                StatusRepresentativeState state = manager.GetRepresentativeState();

                Assert.That(state.Primary, Is.EqualTo(StatusType.Hostility));
                Assert.That(state.Secondary, Is.EqualTo(StatusType.Affection));
                Assert.That(state.DisplayName, Is.EqualTo("Hostility + Affection"));
                Assert.That(conversationState.State.TryGetString("PsychologyState", out string projectedState), Is.True);
                Assert.That(projectedState, Is.EqualTo("AFFECTION_HOSTILITY"));
                Assert.That(
                    ConversationGameStateConditionEvaluator.Evaluate(
                        conversationState.State,
                        "PsychologyState == \"AFFECTION_HOSTILITY\""),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetValue_ClampsAndRetainsChangeMetadata()
        {
            GameObject gameObject = new GameObject("StatusManagerTests");
            try
            {
                StatusManager manager = gameObject.AddComponent<StatusManager>();
                StatusChangeResult result = manager.SetValue(StatusType.Instinct, 140, "Yarn", "TestNode");

                Assert.That(result.PreviousValue, Is.EqualTo(50));
                Assert.That(result.CurrentValue, Is.EqualTo(100));
                Assert.That(result.Delta, Is.EqualTo(50));
                Assert.That(result.Reason, Is.EqualTo("Yarn"));
                Assert.That(result.SourceId, Is.EqualTo("TestNode"));
                Assert.That(manager.ChangeHistory.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
