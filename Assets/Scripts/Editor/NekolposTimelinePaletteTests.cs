using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Nekolpos.EditorTools;

public sealed class NekolposTimelinePaletteTests
{
    private TimelineAsset timeline;
    private SignalAsset signal;
    private GameObject owner;
    private string path;

    [SetUp]
    public void SetUp()
    {
        path = "Assets/TempPaletteTest-" + Guid.NewGuid().ToString("N") + ".playable";
        timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        signal = ScriptableObject.CreateInstance<SignalAsset>();
        owner = new GameObject("Palette Test");
    }

    [TearDown]
    public void TearDown()
    {
        Undo.ClearUndo(timeline);
        UnityEngine.Object.DestroyImmediate(owner);
        UnityEngine.Object.DestroyImmediate(signal);
        AssetDatabase.DeleteAsset(path);
    }

    [Test]
    public void CreatesTrackAndSignalAtExactTimeWithUndoRedo()
    {
        var marker = NekolposTimelinePalette.AddSignal(timeline, signal, 2.375);
        Assert.That(marker.asset, Is.SameAs(signal));
        Assert.That(marker.time, Is.EqualTo(2.375));
        Assert.That(timeline.GetRootTracks().Count(), Is.EqualTo(1));
        Undo.PerformUndo();
        Assert.That(timeline.GetRootTracks().Count(), Is.Zero);
        Undo.PerformRedo();
        var restored = timeline.GetRootTracks().Single().GetMarkers().Cast<SignalEmitter>().Single();
        Assert.That(restored.asset, Is.SameAs(signal));
        Assert.That(restored.time, Is.EqualTo(2.375));
    }

    [Test]
    public void ReusesReceiverTrackWithoutChangingBindingOrReactions()
    {
        timeline.CreateTrack<SignalTrack>(null, "Unbound");
        var bound = timeline.CreateTrack<SignalTrack>(null, "Bound");
        var director = owner.AddComponent<PlayableDirector>();
        var receiver = owner.AddComponent<SignalReceiver>();
        receiver.AddReaction(signal, new UnityEngine.Events.UnityEvent());
        director.playableAsset = timeline;
        director.SetGenericBinding(bound, receiver);
        var marker = NekolposTimelinePalette.AddSignal(timeline, signal, 1, null, director);
        Assert.That(marker.parent, Is.SameAs(bound));
        Assert.That(director.GetGenericBinding(bound), Is.SameAs(receiver));
        Assert.That(receiver.Count(), Is.EqualTo(1));
        Undo.PerformUndo();
        Assert.That(bound.GetMarkers().Count(), Is.Zero);
        Assert.That(timeline.GetRootTracks().Count(), Is.EqualTo(2));
    }

    [Test]
    public void ExplicitTrackWinsAndOtherMarkersStayUnchanged()
    {
        var first = timeline.CreateTrack<SignalTrack>(null, "First");
        var second = timeline.CreateTrack<SignalTrack>(null, "Second");
        var existing = first.CreateMarker<SignalEmitter>(0.5);
        existing.asset = signal;
        var marker = NekolposTimelinePalette.AddSignal(timeline, signal, 3, second);
        Assert.That(marker.parent, Is.SameAs(second));
        Assert.That(first.GetMarkers().Single(), Is.SameAs(existing));
        Assert.That(existing.time, Is.EqualTo(0.5));
    }

    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void RejectsInvalidTimeWithoutCreatingTracks(double time)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NekolposTimelinePalette.AddSignal(timeline, signal, time));
        Assert.That(timeline.GetRootTracks(), Is.Empty);
    }

    [Test]
    public void RejectsMissingSignalWithoutCreatingTracks()
    {
        Assert.Throws<ArgumentException>(() => NekolposTimelinePalette.AddSignal(timeline, null, 0));
        Assert.That(timeline.GetRootTracks(), Is.Empty);
    }

    [Test]
    public void UsesTimelineWindowLocalPlayheadInAssetEditMode()
    {
        var window = UnityEditor.Timeline.TimelineEditor.GetOrCreateWindow();
        var previousAsset = UnityEditor.Timeline.TimelineEditor.inspectedAsset;
        var previousDirector = UnityEditor.Timeline.TimelineEditor.inspectedDirector;
        bool wasLocked = window.locked;
        try
        {
            window.locked = true;
            window.SetTimeline(timeline);
            window.playbackControls.SetCurrentTime(1.625, UnityEditor.Timeline.TimelinePlaybackControls.Context.Local);
            Assert.That(UnityEditor.Timeline.TimelineEditor.inspectedAsset, Is.SameAs(timeline));
            var marker = NekolposTimelinePalette.AddSignal(UnityEditor.Timeline.TimelineEditor.inspectedAsset, signal,
                window.playbackControls.GetCurrentTime(UnityEditor.Timeline.TimelinePlaybackControls.Context.Local));
            Assert.That(marker.time, Is.EqualTo(1.625).Within(0.001));
        }
        finally
        {
            if (previousDirector != null) window.SetTimeline(previousDirector);
            else window.SetTimeline(previousAsset);
            window.locked = wasLocked;
        }
    }

    [Test]
    public void RejectsForeignAndMutedTracksWithoutMarkers()
    {
        var other = ScriptableObject.CreateInstance<TimelineAsset>();
        try
        {
            var foreign = other.CreateTrack<SignalTrack>();
            Assert.Throws<ArgumentException>(() => NekolposTimelinePalette.AddSignal(timeline, signal, 0, foreign));
            var muted = timeline.CreateTrack<SignalTrack>();
            muted.muted = true;
            Assert.Throws<ArgumentException>(() => NekolposTimelinePalette.AddSignal(timeline, signal, 0, muted));
            Assert.That(muted.GetMarkers(), Is.Empty);
            Assert.That(foreign.GetMarkers(), Is.Empty);
        }
        finally { UnityEngine.Object.DestroyImmediate(other); }
    }

    [Test]
    public void LockedParentIsNotModified()
    {
        var group = timeline.CreateTrack<GroupTrack>(null, "Locked");
        var track = timeline.CreateTrack<SignalTrack>(group, "Child");
        group.locked = true;
        Assert.Throws<ArgumentException>(() => NekolposTimelinePalette.AddSignal(timeline, signal, 0, track));
        Assert.That(track.GetMarkers(), Is.Empty);
        var marker = NekolposTimelinePalette.AddSignal(timeline, signal, 0);
        Assert.That(marker.parent, Is.Not.SameAs(track));
    }
}
