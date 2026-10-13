using Nekolpos.System;
using NUnit.Framework;

public sealed class DebugConsoleTraceTests
{
    [SetUp] public void SetUp() => DebugConsoleTrace.Reset();
    [TearDown] public void TearDown() => DebugConsoleTrace.Reset();
    [Test]
    public void InputSelectionAndStatusKeepCorrelationAndSequenceWithoutUtterance()
    {
        DebugConsoleTrace.InputAccepted(14);
        DebugConsoleTrace.DialogueSelected(12345, 3);
        DebugConsoleTrace.StatusChanged(0, 20, 30);
        var entries = DebugConsoleTrace.Read();
        Assert.That(entries.Length, Is.EqualTo(3));
        Assert.That(entries[0].after, Is.EqualTo("<masked:14>"));
        Assert.That(entries[2].correlationId, Is.EqualTo(entries[0].correlationId));
        Assert.That(entries[1].sequence, Is.GreaterThan(entries[0].sequence));
        Assert.That(DebugConsoleTrace.Read("Status", entries[0].correlationId).Length, Is.EqualTo(1));
        DebugConsoleTrace.InputAccepted(3);
        Assert.That(DebugConsoleTrace.Read()[3].correlationId, Is.Not.EqualTo(entries[0].correlationId));
    }
    [Test]
    public void RingEvictsOldestAndClampsConfiguration()
    {
        DebugConsoleTrace.SetCapacity(0); Assert.That(DebugConsoleTrace.Capacity, Is.EqualTo(16));
        for (int i = 0; i < 1000; i++) DebugConsoleTrace.StatusChanged(1, i, i + 1);
        Assert.That(DebugConsoleTrace.Count, Is.EqualTo(16));
        Assert.That(DebugConsoleTrace.Read()[0].before, Is.EqualTo("984"));
        DebugConsoleTrace.SetCapacity(int.MaxValue); Assert.That(DebugConsoleTrace.Capacity, Is.EqualTo(2000));
    }
    [Test]
    public void SnapshotIsReadOnlyAndContainsNoInputOrSavePaths()
    {
        var snapshot = DebugConsoleSnapshot.Capture();
        string json = snapshot.ToJson();
        Assert.That(json, Does.Contain("timeScale"));
        Assert.That(json, Does.Not.Contain("persistentDataPath"));
        Assert.That(json, Does.Not.Contain("playerName"));
        Assert.That(json, Does.Not.Contain("CurrentMessage"));
        Assert.That(json, Does.Not.Contain("inputText"));
    }
}
