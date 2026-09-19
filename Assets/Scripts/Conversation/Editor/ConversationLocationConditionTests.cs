using System;
using System.Text.RegularExpressions;
using Nekolpos.System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Backgammon.Conversation.Editor
{
    public sealed class ConversationLocationConditionTests
    {
        private GameObject locationControllerObject;
        private CatPositionController locationController;

        [SetUp]
        public void SetUp()
        {
            locationControllerObject = new GameObject("ConversationLocationConditionTests_Controller");
            locationController = locationControllerObject.AddComponent<CatPositionController>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(locationControllerObject);
        }

        [TestCase(CatHomeLocation.Table)]
        [TestCase(CatHomeLocation.Desk)]
        [TestCase(CatHomeLocation.Bathroom)]
        [TestCase(CatHomeLocation.Bed)]
        public void Evaluate_WithoutLocationCondition_IsValidAtEveryLocation(CatHomeLocation location)
        {
            SetCurrentLocation(location);
            var state = new ConversationGameState();
            state.SetBool("ExistingCondition", true);

            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "ExistingCondition == true"), Is.True);
        }

        [Test]
        public void Evaluate_DeskLocationCondition_MatchesOnlyDesk()
        {
            AssertLocationCondition("H: Desk", CatHomeLocation.Desk, true);
            AssertLocationCondition("H: Desk", CatHomeLocation.Table, false);
            AssertLocationCondition("H: Desk", CatHomeLocation.Bathroom, false);
            AssertLocationCondition("H: Desk", CatHomeLocation.Bed, false);
        }

        [Test]
        public void Evaluate_MultipleLocationIds_AreOrConditions()
        {
            AssertLocationCondition("H: Desk Table Bathroom", CatHomeLocation.Desk, true);
            AssertLocationCondition("H: Desk Table Bathroom", CatHomeLocation.Table, true);
            AssertLocationCondition("H: Desk Table Bathroom", CatHomeLocation.Bathroom, true);
            AssertLocationCondition("H: Desk Table Bathroom", CatHomeLocation.Bed, false);
        }

        [Test]
        public void Evaluate_BedLocationCondition_MatchesOnlyBed()
        {
            AssertLocationCondition("H: Bed", CatHomeLocation.Bed, true);
            AssertLocationCondition("H: Bed", CatHomeLocation.Table, false);
            AssertLocationCondition("H: Bed", CatHomeLocation.Desk, false);
            AssertLocationCondition("H: Bed", CatHomeLocation.Bathroom, false);
        }

        [Test]
        public void Evaluate_ExistingConditionAndLocationCondition_RequireBoth()
        {
            var state = new ConversationGameState();
            state.SetInt("Affection", 50);

            SetCurrentLocation(CatHomeLocation.Desk);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "Affection >= 50 && H: Desk"), Is.True);

            SetCurrentLocation(CatHomeLocation.Table);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "Affection >= 50 && H: Desk"), Is.False);

            SetCurrentLocation(CatHomeLocation.Desk);
            state.SetInt("Affection", 49);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(state, "Affection >= 50 && H: Desk"), Is.False);
        }

        [Test]
        public void Evaluate_NegatedLocationCondition_ExcludesOnlyItsLocation()
        {
            AssertLocationCondition("H: !Desk", CatHomeLocation.Desk, false);
            AssertLocationCondition("H: !Desk", CatHomeLocation.Table, true);
            AssertLocationCondition("H: !Desk", CatHomeLocation.Bathroom, true);
            AssertLocationCondition("H: !Desk", CatHomeLocation.Bed, true);
        }

        [Test]
        public void Evaluate_LocationOnlyCondition_DoesNotRequireConversationGameState()
        {
            SetCurrentLocation(CatHomeLocation.Table);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(null, "H: !Desk"), Is.True);

            SetCurrentLocation(CatHomeLocation.Desk);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(null, "H: !Desk"), Is.False);

            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(null, "Affection >= 50"), Is.False);
        }

        [Test]
        public void Evaluate_NegatedDropdownConditions_RequiresEveryConditionToBeFalse()
        {
            var state = new ConversationGameState();
            state.SetString("Weather", "Sunny");
            state.SetString("Season", "Summer");
            state.SetString("Phase", "Night");

            SetCurrentLocation(CatHomeLocation.Desk);
            Assert.That(
                ConversationGameStateConditionEvaluator.Evaluate(state, "!Rain && !Spring && !Morning && H: !Table"),
                Is.True);

            state.SetString("Weather", "Rainy");
            Assert.That(
                ConversationGameStateConditionEvaluator.Evaluate(state, "!Rain && !Spring && !Morning && H: !Table"),
                Is.False);

            state.SetString("Weather", "Sunny");
            SetCurrentLocation(CatHomeLocation.Table);
            Assert.That(
                ConversationGameStateConditionEvaluator.Evaluate(state, "!Rain && !Spring && !Morning && H: !Table"),
                Is.False);
        }

        [Test]
        public void Evaluate_UnknownLocationId_WarnsAndDoesNotMatch()
        {
            string unknownLocationId = "Desk_Unknown_" + Guid.NewGuid().ToString("N");
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(unknownLocationId)));

            SetCurrentLocation(CatHomeLocation.Desk);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(new ConversationGameState(), "H: " + unknownLocationId), Is.False);
        }

        private void AssertLocationCondition(string condition, CatHomeLocation location, bool expected)
        {
            SetCurrentLocation(location);
            Assert.That(ConversationGameStateConditionEvaluator.Evaluate(new ConversationGameState(), condition), Is.EqualTo(expected));
        }

        private void SetCurrentLocation(CatHomeLocation location)
        {
            locationController.SetHomeLocation(location);
        }
    }
}
