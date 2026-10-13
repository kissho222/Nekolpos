using System;
using Nekolpos.System;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;

public sealed class CatMotionControllerTests
{
    [Test]
    public void PlayMotion_StillSnapsToConversationHome()
    {
        using var fixture = new MotionFixture();
        fixture.Cat.transform.position = Vector3.zero;

        fixture.Motion.PlayMotion(MotionFixture.ConversationState);

        Assert.That(fixture.Cat.transform.position, Is.EqualTo(fixture.HomePosition));
        Assert.That(fixture.Motion.IsPlayingMotion, Is.True);
        Assert.That(fixture.Motion.IsPlayingLocomotion, Is.False);
        Assert.That(fixture.Animator.applyRootMotion, Is.False);
    }

    [Test]
    public void PlayLocomotion_DoesNotSnapHomeAndExposesSeparateState()
    {
        using var fixture = new MotionFixture();
        Vector3 walkingPosition = new Vector3(2f, 0f, -3f);
        fixture.Cat.transform.position = walkingPosition;

        bool played = fixture.Motion.PlayLocomotion(MotionFixture.WalkState, 0f);

        Assert.That(played, Is.True);
        Assert.That(fixture.Cat.transform.position, Is.EqualTo(walkingPosition));
        Assert.That(fixture.Motion.IsPlayingLocomotion, Is.True);
        Assert.That(fixture.Motion.CurrentLocomotionState, Is.EqualTo(MotionFixture.WalkState));
        Assert.That(fixture.Motion.IsPlayingMotion, Is.False);
        Assert.That(fixture.Animator.applyRootMotion, Is.False);
    }

    [Test]
    public void StopLocomotion_ReturnsToIdleWithoutChangingTransform_AndConversationTakesPriority()
    {
        using var fixture = new MotionFixture();
        Vector3 walkingPosition = new Vector3(-4f, 0f, 1f);
        fixture.Cat.transform.position = walkingPosition;
        Assert.That(fixture.Motion.PlayLocomotion(MotionFixture.WalkState, 0f), Is.True);

        Assert.That(fixture.Motion.StopLocomotionAndReturnToIdle(), Is.True);
        Assert.That(fixture.Cat.transform.position, Is.EqualTo(walkingPosition));
        Assert.That(fixture.Motion.IsPlayingLocomotion, Is.False);
        Assert.That(fixture.Motion.CurrentLocomotionState, Is.Empty);
        Assert.That(IsCurrentOrNextState(fixture.Animator, MotionFixture.IdleState), Is.True);

        fixture.Motion.PlayLocomotion(MotionFixture.WalkState, 0f);
        fixture.Motion.PlayMotion(MotionFixture.ConversationState);

        Assert.That(fixture.Motion.IsPlayingLocomotion, Is.False);
        Assert.That(fixture.Motion.IsPlayingMotion, Is.True);
        Assert.That(fixture.Cat.transform.position, Is.EqualTo(fixture.HomePosition));
        Assert.That(fixture.Motion.StopLocomotionAndReturnToIdle(), Is.False);
        Assert.That(IsCurrentOrNextState(fixture.Animator, MotionFixture.ConversationState), Is.True);
    }

    [Test]
    public void LocomotionApi_DoesNotOwnLookRigOrPresentationMode()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/System/CatMotionController.cs");

        StringAssert.DoesNotContain("NekomataLookRigController", source);
        StringAssert.DoesNotContain("CatPresentationModeController", source);
    }

    private static bool IsCurrentOrNextState(Animator animator, string stateName)
    {
        return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) ||
               (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName));
    }

    [TestCase(MotionFixture.WalkState, CatPresentationMode.Conversation, CatHomeLocation.Desk)]
    [TestCase(MotionFixture.ConversationState, CatPresentationMode.Conversation, CatHomeLocation.Table)]
    [TestCase(MotionFixture.WalkState, CatPresentationMode.Idle, CatHomeLocation.Table)]
    public void TimelineResume_RestoresBaseIdleAndMotionOwnershipAtSelectedHome(
        string interruptedState, CatPresentationMode previousMode, CatHomeLocation destination)
    {
        using var fixture = new MotionFixture();
        var host = new GameObject("TimelineResume.Presentation");
        var table = new GameObject("TimelineResume.Table");
        var desk = new GameObject("TimelineResume.Desk");
        host.SetActive(false);
        try
        {
            table.transform.position = new Vector3(2f, 0f, 3f);
            desk.transform.position = new Vector3(-4f, 0f, 7f);
            var presentation = host.AddComponent<CatPresentationModeController>();
            var settings = new SerializedObject(presentation);
            settings.FindProperty("initializeOnAwake").boolValue = false;
            settings.FindProperty("enableIdleVariations").boolValue = false;
            settings.FindProperty("idleStateName").stringValue = MotionFixture.IdleState;
            settings.ApplyModifiedPropertiesWithoutUndo();
            presentation.Configure(null, fixture.Cat, table.transform, desk.transform, null);
            host.SetActive(true);
            typeof(CatPresentationModeController).GetProperty("CurrentMode").SetValue(presentation, previousMode);
            presentation.PlayMotion(interruptedState);
            Assert.That(IsCurrentOrNextState(fixture.Animator, interruptedState), Is.True);

            presentation.BeginTimelinePresentation();
            presentation.SetHomeLocation(destination);
            presentation.ResumeIdleAfterTimeline();
            fixture.Animator.Update(.2f);

            Assert.That(fixture.Animator.GetCurrentAnimatorStateInfo(0).IsName(MotionFixture.IdleState), Is.True);
            Assert.That(fixture.Motion.IsPlayingMotion, Is.False);
            Assert.That(fixture.Motion.IsPlayingLocomotion, Is.False);
            Assert.That(presentation.IsDelayingDialogueResponse, Is.False);
            Assert.That(presentation.CurrentMode, Is.EqualTo(previousMode));
            Assert.That(presentation.PositionController.CurrentHomeLocation, Is.EqualTo(destination));
            Assert.That(fixture.Cat.transform.position,
                Is.EqualTo(destination == CatHomeLocation.Desk ? desk.transform.position : table.transform.position));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(table);
            UnityEngine.Object.DestroyImmediate(desk);
        }
    }

    private sealed class MotionFixture : IDisposable
    {
        public const string IdleState = "Idle";
        public const string WalkState = "Walk";
        public const string ConversationState = "Conversation";

        public GameObject Cat { get; }
        public Animator Animator { get; }
        public CatMotionController Motion { get; }
        public Vector3 HomePosition { get; } = new Vector3(9f, 0f, 5f);

        public MotionFixture()
        {
            Cat = new GameObject("CatMotionControllerTests.Cat");
            Animator = Cat.AddComponent<Animator>();
            var controller = new AnimatorController();
            controller.AddLayer("Base Layer");
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            stateMachine.AddState(IdleState);
            stateMachine.AddState(WalkState);
            stateMachine.AddState(ConversationState);
            Animator.runtimeAnimatorController = controller;
            Animator.Rebind();
            Animator.Update(0f);

            var position = Cat.AddComponent<CatPositionController>();
            position.SetHome(HomePosition, Quaternion.identity);
            Motion = Cat.AddComponent<CatMotionController>();
            Motion.Configure(Animator, position, IdleState);
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Cat);
        }
    }
}
