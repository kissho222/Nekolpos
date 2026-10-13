using System.Collections;
using Nekolpos.EditorTools;
using Nekolpos.System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class DebugConsoleTests
{
    [TearDown]
    public void Release() => typeof(DebugConsoleInput).GetMethod("Reset", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic).Invoke(null, null);

    [Test]
    public void AuthoredViewExistsAndDoesNotOpenOutsideGameplay()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DebugConsoleAuthoring.PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<NekolposDebugConsole>(), Is.Not.Null);
        Assert.That(prefab.transform.Find("Window").gameObject.activeSelf, Is.False);
        Assert.That(NekolposDebugConsole.CanDisplay(), Is.False);
    }

    [Test]
    public void SnapshotDoesNotCreateManagersOrWriteProgress()
    {
        int count = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        string day = ProgressSaveStore.GetInt(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey, -1).ToString();
        Assert.That(NekolposDebugConsole.ReadCurrentState(), Does.Contain("TimeScale:"));
        Assert.That(Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(count));
        Assert.That(ProgressSaveStore.GetInt(Nekolpos.TimeSystem.TimeManager.CurrentDayPrefsKey, -1).ToString(), Is.EqualTo(day));
    }

    [Test]
    public void ClosingFrameBlocksClickThroughThenReleases()
    {
        DebugConsoleInput.SetOwned(true); Assert.That(DebugConsoleInput.IsBlocked, Is.True);
        DebugConsoleInput.SetOwned(false); Assert.That(DebugConsoleInput.IsBlocked, Is.True);
        Assert.That(DebugConsoleInput.ShouldBlock(false, 20, 20), Is.True);
        Assert.That(DebugConsoleInput.ShouldBlock(false, 20, 21), Is.False);
        Assert.That(DebugConsoleInput.ShouldBlock(false, -1, -1), Is.False);
    }
    [Test]
    public void SharedLeaseKeepsPauseAndOriginalRaycasterState()
    {
        var owner = new GameObject("Lease owner", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        var other = new GameObject("Lease other", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        float scale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            var raycaster = other.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            using (var lease = DebugConsoleInput.Acquire(owner.transform))
            {
                Assert.That(lease, Is.Not.Null); Assert.That(DebugConsoleInput.ConsoleOwnsInput, Is.True);
                Assert.That(raycaster.enabled, Is.False); Assert.That(Time.timeScale, Is.Zero);
                Assert.That(DebugConsoleInput.Acquire(other.transform), Is.Null);
            }
            Assert.That(raycaster.enabled, Is.True); Assert.That(Time.timeScale, Is.Zero);
            raycaster.enabled = false;
            using (var lease = DebugConsoleInput.Acquire(owner.transform)) lease.Refresh();
            Assert.That(raycaster.enabled, Is.False);
        }
        finally { Time.timeScale = scale; Object.DestroyImmediate(other); Object.DestroyImmediate(owner); }
    }
    [Test]
    public void ClosingMissingViewStillReleasesLease()
    {
        var owner = new GameObject("Missing console view");
        try
        {
            var console = owner.AddComponent<NekolposDebugConsole>();
            var lease = DebugConsoleInput.Acquire(owner.transform);
            typeof(NekolposDebugConsole).GetField("inputLease", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic).SetValue(console, lease);
            Assert.That(DebugConsoleInput.ConsoleOwnsInput, Is.True);
            console.Close();
            Assert.That(DebugConsoleInput.ConsoleOwnsInput, Is.False);
            console.Close();
            Assert.That(DebugConsoleInput.Acquire(owner.transform), Is.Not.Null);
        }
        finally { DebugConsoleInput.ReleaseAll(); Object.DestroyImmediate(owner); }
    }
}
