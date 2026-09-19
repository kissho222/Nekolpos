using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueTimelineSequenceTests
    {
        private readonly List<Object> created = new List<Object>();
        private DialogueTimelineSequenceController player;
        private Animator animator;

        private sealed class TimelineConflictProbe : MonoBehaviour
        {
        }

        [TestCase("timeline_sequence:MawStart", "Maw", false)]
        [TestCase("timeline_sequence:MawEnd", "Maw", true)]
        [TestCase("timeline_sequence:Maw Start", "Maw", false)]
        [TestCase("timeline_sequence:Maw:End", "Maw", true)]
        public void ParseExplicitCommands(string command, string id, bool end)
        {
            Assert.That(DialogueTimelineSequenceController.TryParse(command, out var actual, out var actualEnd));
            Assert.That(actual, Is.EqualTo(id));
            Assert.That(actualEnd, Is.EqualTo(end));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("timeline_sequence:Maw")]
        [TestCase("timeline_sequence:Start")]
        [TestCase("timeline:MawStart")]
        public void RejectMalformedCommands(string command)
        {
            Assert.That(DialogueTimelineSequenceController.TryParse(command, out _, out _), Is.False);
        }

        [Test]
        public void RegisteredTimelineUsesSequenceLookAtPolicy()
        {
            SetupPlayer();
            TimelineAsset start = MakeTimeline();
            TimelineAsset loop = MakeTimeline();
            TimelineAsset end = MakeTimeline();
            var sequence = new DialogueTimelineSequenceController.Sequence
            {
                id = "PreviewPolicy",
                start = start,
                loop = loop,
                end = end,
                keepLookAtDuringTimeline = false
            };
            player.Configure(animator, new[] { sequence });

            Assert.That(player.TryGetTimelineLookAtSetting(start, out bool startLookAt), Is.True);
            Assert.That(startLookAt, Is.False);
            Assert.That(player.TryGetTimelineLookAtSetting(loop, out bool loopLookAt), Is.True);
            Assert.That(loopLookAt, Is.False);
            Assert.That(player.TryGetTimelineLookAtSetting(end, out bool endLookAt), Is.True);
            Assert.That(endLookAt, Is.False);

            sequence.keepLookAtDuringTimeline = true;
            Assert.That(player.TryGetTimelineLookAtSetting(loop, out loopLookAt), Is.True);
            Assert.That(loopLookAt, Is.True);
            Assert.That(player.TryGetTimelineLookAtSetting(MakeTimeline(), out _), Is.False);
        }

        [Test]
        public void PlayerLookTargetSynchronizeNowAppliesRuntimeOffsetsInEditMode()
        {
            var sourceObject = new GameObject("LookSource");
            var targetObject = new GameObject("LookTarget");
            created.Add(sourceObject);
            created.Add(targetObject);
            sourceObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.Euler(10f, 20f, 30f));

            PlayerLookTarget target = targetObject.AddComponent<PlayerLookTarget>();
            target.Source = sourceObject.transform;
            var serializedTarget = new SerializedObject(target);
            serializedTarget.FindProperty("localOffset").vector3Value = new Vector3(0.5f, 0.25f, -0.5f);
            serializedTarget.FindProperty("worldOffset").vector3Value = new Vector3(0f, -0.07f, 0f);
            serializedTarget.FindProperty("copyRotation").boolValue = true;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();

            target.SynchronizeNow();

            Vector3 expectedPosition = sourceObject.transform.TransformPoint(new Vector3(0.5f, 0.25f, -0.5f)) +
                                       new Vector3(0f, -0.07f, 0f);
            Assert.That(Vector3.Distance(target.transform.position, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(target.transform.rotation, sourceObject.transform.rotation), Is.LessThan(0.001f));
        }

        [Test]
        public void EditorLookPreviewDoesNotOverrideTimelineByDefault()
        {
            var host = new GameObject("LookPreviewTestHost");
            created.Add(host);
            var lookController = host.AddComponent<NekomataLookRigController>();

            var serializedController = new SerializedObject(lookController);
            SerializedProperty applyDuringTimeline =
                serializedController.FindProperty("applyEditorPreviewDuringTimeline");

            Assert.That(applyDuringTimeline, Is.Not.Null);
            Assert.That(applyDuringTimeline.boolValue, Is.False);
        }

        private void SetupPlayer()
        {
            var host = new GameObject("SequenceTestHost");
            created.Add(host);
            var cat = new GameObject("SequenceTestCat");
            created.Add(cat);
            animator = cat.AddComponent<Animator>();
            var controller = new AnimatorController();
            created.Add(controller);
            controller.AddLayer("Base Layer");
            var stateMachine = controller.layers[0].stateMachine;
            created.Add(stateMachine);
            var idle = stateMachine.AddState("CatSimple_Lie_belly_loop_1");
            created.Add(idle);
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0);
            player = host.AddComponent<DialogueTimelineSequenceController>();
            var sequence = new DialogueTimelineSequenceController.Sequence { id = "Maw" };
            sequence.start = MakeTimeline();
            sequence.loop = MakeTimeline();
            sequence.end = MakeTimeline();
            player.Configure(animator, new[] { sequence });
        }

        private TimelineAsset MakeTimeline()
        {
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            created.Add(asset);
            asset.durationMode = TimelineAsset.DurationMode.FixedLength;
            asset.fixedDuration = 1;
            return asset;
        }

        [Test]
        public void StartLoopsUntilExplicitEndThenRestoresRoot()
        {
            SetupPlayer();
            var origin = new Vector3(2, 3, 4);
            animator.transform.localPosition = origin;
            Assert.That(player.Execute("timeline_sequence:MawStart"));
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Start));
            player.Advance(1.1);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Loop));
            player.Advance(100);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Loop));
            Assert.That(player.Execute("timeline_sequence:MawStart"));
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Loop));
            animator.transform.localPosition = Vector3.zero;
            Assert.That(player.Execute("timeline_sequence:MawEnd"));
            player.Advance(1.1);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));
            Assert.That(animator.transform.localPosition, Is.EqualTo(origin));
        }

        [Test]
        public void EndInterruptsStartAndRepeatedEndDoesNotRestart()
        {
            SetupPlayer();
            player.Execute("timeline_sequence:MawStart");
            player.Execute("timeline_sequence:MawEnd");
            player.Advance(0.6);
            player.Execute("timeline_sequence:MawEnd");
            player.Advance(0.6);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));
            Assert.That(player.Execute("timeline_sequence:MawEnd"));
        }

        [Test]
        public void UnknownIdDoesNotInterruptActiveSequence()
        {
            SetupPlayer();
            player.Execute("timeline_sequence:MawStart");
            LogAssert.Expect(LogType.Error, "[DialogueTimelineSequence] Unknown sequence: Missing");
            Assert.That(player.Execute("timeline_sequence:MissingStart"), Is.False);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Start));
        }

        [Test]
        public void FinishCleansUpSequence()
        {
            SetupPlayer();
            player.Execute("timeline_sequence:MawStart");
            player.Finish();
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));
        }

        [Test]
        public void SingleTimelineEntryFinishesAndCanBePlayedAgain()
        {
            SetupPlayer();
            TimelineAsset timeline = MakeTimeline();

            Assert.That(player.PlayTimeline(timeline), Is.True);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Start));
            player.Advance(1.1);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));

            Assert.That(player.PlayTimeline(timeline), Is.True);
            player.Advance(1.1);
            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));
        }

        [Test]
        public void SequenceKeepsPresentationEnabledAndSuspendsOnlyExplicitConflicts()
        {
            SetupPlayer();
            var presentationHost = new GameObject("PresentationTestHost");
            presentationHost.SetActive(false);
            created.Add(presentationHost);
            var presentation = presentationHost.AddComponent<CatPresentationModeController>();
            var serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("initializeOnAwake").boolValue = false;
            serializedPresentation.FindProperty("normalCatAnimator").objectReferenceValue = animator;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            presentationHost.SetActive(true);

            var conflict = player.gameObject.AddComponent<TimelineConflictProbe>();
            var sequence = new DialogueTimelineSequenceController.Sequence
            {
                id = "Masked",
                start = MakeTimeline(),
                loop = MakeTimeline(),
                end = MakeTimeline(),
                additionalBehavioursToSuspend = new Behaviour[] { conflict }
            };
            player.Configure(animator, new[] { sequence });

            Assert.That(player.Execute("timeline_sequence:MaskedStart"), Is.True);
            Assert.That(presentation.enabled, Is.True);
            Assert.That(presentation.CurrentMode, Is.EqualTo(CatPresentationMode.Timeline));
            Assert.That(conflict.enabled, Is.False);

            player.Finish();

            Assert.That(presentation.enabled, Is.True);
            Assert.That(presentation.CurrentMode, Is.EqualTo(CatPresentationMode.Idle));
            Assert.That(conflict.enabled, Is.True);
        }

        [Test]
        public void EndingTimelineDuringDialogueKeepsFinalMessageVisibleUntilPlayerAdvances()
        {
            SetupPlayer();

            var chatHost = new GameObject("TimelineDialogueChat");
            created.Add(chatHost);
            var chatUi = chatHost.AddComponent<ChatUIController>();

            var openingCat = new GameObject("TimelineOpeningCat");
            created.Add(openingCat);
            var presentationHost = new GameObject("TimelineDialoguePresentation");
            presentationHost.SetActive(false);
            created.Add(presentationHost);
            var presentation = presentationHost.AddComponent<CatPresentationModeController>();
            var serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("initializeOnAwake").boolValue = false;
            serializedPresentation.FindProperty("normalCatAnimator").objectReferenceValue = animator;
            serializedPresentation.FindProperty("enableIdleVariations").boolValue = false;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            presentationHost.SetActive(true);
            presentation.Configure(openingCat, animator.gameObject, animator.transform, chatUi);
            presentation.SwitchToNormalCat();
            chatUi.EnterDialogueMode();

            Assert.That(player.Execute("timeline_sequence:MawStart"), Is.True);
            Assert.That(player.Execute("timeline_sequence:MawEnd"), Is.True);
            player.Advance(1.1);

            Assert.That(player.CurrentPhase, Is.EqualTo(DialogueTimelineSequenceController.Phase.Idle));
            Assert.That(chatUi.IsInDialogueMode, Is.True);
        }

        [TearDown]
        public void Cleanup()
        {
            if (player != null) player.Finish();
            foreach (var item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }
    }
}
