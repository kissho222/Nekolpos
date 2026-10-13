using System;
using System.Reflection;
using Nekolpos.System;
using Nekolpos.Data;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DebugSaveIsolationTests
{
    private string key;
    private static void SetIsolated(bool value) => typeof(DebugSaveSession).GetField("<IsIsolated>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    [SetUp] public void SetUp() { key = "Nekolpos.Tests.DebugIsolation." + Guid.NewGuid().ToString("N"); PlayerPrefs.SetString(key, "normal-save"); SetIsolated(true); }
    [TearDown] public void TearDown() { SetIsolated(false); PlayerPrefs.DeleteKey(key); }
    [Test]
    public void ReadsWritesDeletesAndShutdownRemainInTestDomain()
    {
        Assert.That(ProgressSaveStore.HasKey(key), Is.False);
        Assert.That(ProgressSaveStore.GetString(key, "missing"), Is.EqualTo("missing"));
        ProgressSaveStore.SetString(key, "test-save"); ProgressSaveStore.Save();
        Assert.That(ProgressSaveStore.GetString(key), Is.EqualTo("test-save"));
        Assert.That(PlayerPrefs.GetString(key), Is.EqualTo("normal-save"));
        ProgressSaveStore.DeleteKey(key); ProgressSaveStore.SetInt(key, 42);
        Assert.That(ProgressSaveStore.GetInt(key), Is.EqualTo(42));
        DebugSaveSession.Finish();
        ProgressSaveStore.SetString(key, "shutdown-save"); ProgressSaveStore.Save();
        Assert.That(DebugSaveSession.IsIsolated, Is.True);
        Assert.That(DebugSaveSession.CanRunScenarios, Is.False);
        Assert.That(PlayerPrefs.GetString(key), Is.EqualTo("normal-save"));
    }
    [Test]
    public void DeviceSettingsAndCatAssetCannotBeOverwritten()
    {
        string previous = PlayerPrefs.GetString(GameSettingsStore.Key, "");
        GameSettingsStore.Save(GameSettings.Defaults(1280, 720, true));
        Assert.That(PlayerPrefs.GetString(GameSettingsStore.Key, ""), Is.EqualTo(previous));
        var source = ScriptableObject.CreateInstance<CatDataSO>();
        try
        {
            var copy = DebugSaveSession.GetRuntimeCatData(source);
            Assert.That(copy, Is.Not.SameAs(source));
            copy.catName = "fixture"; Assert.That(source.catName, Is.Not.EqualTo("fixture"));
            Assert.That(DebugSaveSession.GetRuntimeCatData(source), Is.SameAs(copy));
            Assert.That(DebugSaveSession.GetRuntimeCatData(copy), Is.SameAs(copy));
            Object.DestroyImmediate(copy);
        }
        finally { Object.DestroyImmediate(source); }
    }
    [Test]
    public void CatalogOnlyAcceptsExactAllowlistedIds()
    {
        Assert.That(DebugScenarioCatalog.Find("normal.table"), Is.Not.Null);
        Assert.That(DebugScenarioCatalog.Find("normal.table; arbitrary"), Is.Null);
        Assert.That(DebugScenarioCatalog.Find("Normal.Table"), Is.Null);
    }
}
