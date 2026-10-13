using System.IO;
using Nekolpos.EditorTools;
using Nekolpos.System;
using NUnit.Framework;
using UnityEngine;

public sealed class DebugScenarioTests
{
    [TestCase("normal.table")]
    [TestCase("death.respawn")]
    [TestCase("arbitrary-command")]
    public void UnisolatedRequestsAreRejectedWithoutGameOrSaveChanges(string id)
    {
        DebugSaveSession.ReleaseAfterPlayStopped();
        var before = DebugNormalSaveAudit.Capture();
        float scale = Time.timeScale; var cursor = Cursor.lockState; bool visible = Cursor.visible;
        var result = DebugScenarioExecutor.RunAsync(id).GetAwaiter().GetResult();
        Assert.That(result.status, Is.EqualTo("Rejected"));
        Assert.That(result.reason, Is.Not.Empty);
        Assert.That(DebugScenarioExecutor.IsRunning, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(scale));
        Assert.That(Cursor.lockState, Is.EqualTo(cursor)); Assert.That(Cursor.visible, Is.EqualTo(visible));
        Assert.That(DebugNormalSaveAudit.Matches(before, DebugNormalSaveAudit.Capture()), Is.True);
    }
    [Test]
    public void UnregisteredEditorRequestReturnsReportWithoutExecutingOrEchoingCommand()
    {
        string path = DebugScenarioEditorRunner.Request("sensitive arbitrary input");
        try
        {
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("Rejected"));
            Assert.That(json, Does.Contain("<unregistered>"));
            Assert.That(json, Does.Not.Contain("sensitive arbitrary input"));
            Assert.That(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
        }
        finally { File.Delete(path); }
    }
    [Test]
    public void AuthoredDiaryFixturesContainRealTextAndCorrectKinds()
    {
        var normal = SystemTimedEventCatalog.Resolve(DebugScenarioFixtures.NormalDiaryKey);
        var special = SystemTimedEventCatalog.Resolve(DebugScenarioFixtures.SpecialDiaryKey);
        Assert.That(normal, Is.Not.Null); Assert.That(normal.IsSpecialDiary, Is.False); Assert.That(normal.DiaryVariants.Count, Is.GreaterThan(0));
        Assert.That(special, Is.Not.Null); Assert.That(special.IsSpecialDiary, Is.True); Assert.That(special.DiaryVariants.Count, Is.GreaterThan(0));
        var death = SystemTimedEventCatalog.Resolve("action_paw_press_death_result");
        Assert.That(death, Is.Not.Null); Assert.That(death.DiaryVariants.Count, Is.GreaterThan(0));
    }
}
