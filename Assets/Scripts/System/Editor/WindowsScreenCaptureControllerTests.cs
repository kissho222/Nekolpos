using System;
using System.IO;
using System.Reflection;
using Nekolpos.ActionSystem;
using Nekolpos.System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class WindowsScreenCaptureControllerTests
{
    private GameObject canvasObject;
    private GameObject owner;
    private WindowsScreenCaptureController controller;

    [SetUp]
    public void SetUp()
    {
        canvasObject = new GameObject("Capture HUD fixture", typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        owner = new GameObject("Capture controller fixture");
        controller = owner.AddComponent<WindowsScreenCaptureController>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(owner);
        Object.DestroyImmediate(canvasObject);
    }

    private GameObject Child(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        child.transform.SetParent(parent, false);
        return child;
    }

    [Test]
    public void HideRestorePreservesInputAndLatestUiState()
    {
        var hud = Child("HUD", canvasObject.transform);
        var group = hud.GetComponent<CanvasGroup>();
        group.alpha = .65f;
        group.interactable = false;
        var button = hud.AddComponent<Button>();
        controller.SetHudHidden(true);
        controller.ApplyHudVisibility();
        Assert.That(group.alpha, Is.Zero);
        Assert.That(hud.activeSelf, Is.True);
        Assert.That(button.enabled, Is.True);
        Assert.That(group.interactable, Is.False);
        Assert.That(group.blocksRaycasts, Is.True);
        controller.SetHudHidden(false);
        Assert.That(group.alpha, Is.EqualTo(.65f));
        group.alpha = .3f;
        controller.SetHudHidden(true);
        controller.ApplyHudVisibility();
        controller.enabled = false;
        // Ordinary MonoBehaviour lifecycle callbacks do not run in EditMode.
        typeof(WindowsScreenCaptureController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, null);
        Assert.That(group.alpha, Is.EqualTo(.3f));
        Assert.That(controller.HudHidden, Is.False);
    }

    [Test]
    public void NewHudAndIgnoringPopupHideWhileFadeAndWorldCanvasStayVisible()
    {
        var hud = Child("HUD", canvasObject.transform);
        var popup = Child("Popup", hud.transform);
        popup.GetComponent<CanvasGroup>().ignoreParentGroups = true;
        var fadeObject = Child("Fade", canvasObject.transform);
        var fade = fadeObject.AddComponent<FadeController>();
        typeof(FadeController).GetField("fadeGroup", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(fade, fadeObject.GetComponent<CanvasGroup>());
        var world = Child("World", canvasObject.transform);
        var worldCanvas = world.AddComponent<Canvas>();
        // World Canvas must be a separate root like the real tablet.
        world.transform.SetParent(null);
        worldCanvas.renderMode = RenderMode.WorldSpace;
        var worldContent = Child("World content", world.transform);
        try
        {
            controller.SetHudHidden(true);
            controller.ApplyHudVisibility();
            Assert.That(hud.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(popup.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(fadeObject.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(worldContent.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            var dynamicHud = Child("New hint", canvasObject.transform);
            controller.ApplyHudVisibility();
            Assert.That(dynamicHud.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Object.DestroyImmediate(popup);
            controller.SetHudHidden(false);
            Assert.That(hud.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
        }
        finally { Object.DestroyImmediate(world); }
    }

    [Test]
    public void SceneChangeRestoresHud()
    {
        var hud = Child("HUD", canvasObject.transform);
        controller.SetHudHidden(true);
        controller.ApplyHudVisibility();
        typeof(WindowsScreenCaptureController).GetMethod("OnActiveSceneChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new object[] { default(UnityEngine.SceneManagement.Scene), default(UnityEngine.SceneManagement.Scene) });
        Assert.That(controller.HudHidden, Is.False);
        Assert.That(hud.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
    }

    [Test]
    public void PngSaveCreatesDirectoryAndNeverOverwrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NekolposCaptureTest_" + Guid.NewGuid().ToString("N"));
        var texture = new Texture2D(3, 2);
        try
        {
            byte[] bytes = texture.EncodeToPNG();
            string first = WindowsScreenCaptureController.SavePng(directory, bytes);
            string second = WindowsScreenCaptureController.SavePng(directory, bytes);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(Path.GetFileName(first), Does.StartWith("Nekolpos_"));
            Assert.That(Path.GetDirectoryName(first), Is.EqualTo(Path.Combine(directory, "Screenshots")));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(first));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(second));
            Assert.Throws<ArgumentException>(() => WindowsScreenCaptureController.SavePng(directory, Array.Empty<byte>()));
            string fileAsDirectory = Path.Combine(directory, "blocked");
            File.WriteAllText(fileAsDirectory, "fixture");
            Assert.Throws<IOException>(() => WindowsScreenCaptureController.SavePng(fileAsDirectory, bytes));
        }
        finally
        {
            Object.DestroyImmediate(texture);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
