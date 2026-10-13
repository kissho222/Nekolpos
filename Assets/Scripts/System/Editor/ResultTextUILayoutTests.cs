using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nekolpos.ActionSystem;
using Nekolpos.System;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ResultTextUILayoutTests
{
    private GameObject canvasObject;
    private GameObject panel;
    private TextMeshProUGUI text;
    private ResultTextUI presenter;

    [SetUp]
    public void SetUp()
    {
        canvasObject = new GameObject("Result layout fixture", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/ActionResultPanel.prefab"), canvasObject.transform);
        panel.SetActive(true);
        text = panel.GetComponentInChildren<TextMeshProUGUI>(true);
        presenter = canvasObject.AddComponent<ResultTextUI>();
        var serialized = new SerializedObject(presenter);
        serialized.FindProperty("rootGroup").objectReferenceValue = panel.GetComponent<CanvasGroup>();
        serialized.FindProperty("resultText").objectReferenceValue = text;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        SetResolution(1280, 720);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(canvasObject);

    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    [TestCase(2560, 1440)]
    [TestCase(3840, 2160)]
    [TestCase(1024, 768)]
    [TestCase(2560, 1080)]
    [TestCase(720, 1280)]
    public void ActualTimedEvents_PreserveExplicitLinesAndFitViewport(int width, int height)
    {
        SetResolution(width, height);
        SystemTimedEventCatalog.Resolve(SystemTimedEventCatalog.ActionBallPlayResultKey, "ja");
        var rows = (IDictionary)typeof(SystemTimedEventCatalog).GetField("RowsByKey", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Assert.That(rows.Count, Is.GreaterThan(0));
        foreach (string key in rows.Keys)
        {
            string message = SystemTimedEventCatalog.Get(key, locale: "ja").Replace("{{CAT_NAME}}", "ネルコ").Replace("{{PLAYER_NAME}}", "プレイヤー");
            if (string.IsNullOrWhiteSpace(message)) continue;
            text.text = message;
            Rebuild();
            Assert.That(text.isTextTruncated, Is.False, key);
            Assert.That(text.textInfo.lineCount, Is.EqualTo(message.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length), key);
            Assert.That(text.renderedWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1f), key);
            Assert.That(text.renderedHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1f), key);
            Rect viewport = ((RectTransform)panel.transform).rect;
            Rect background = ((RectTransform)text.transform.parent).rect;
            Assert.That(background.width, Is.LessThanOrEqualTo(viewport.width + 1f), key);
            Assert.That(background.height, Is.LessThanOrEqualTo(viewport.height + 1f), key);
        }
    }

    [Test]
    public void ShortMessage_UsesCompactBackground_AfterLongMessage()
    {
        text.text = SystemTimedEventCatalog.Get("TEMP_SPECIAL_DIARY_001", locale: "ja").Replace("{{CAT_NAME}}", "ネルコ");
        Rebuild();
        float longWidth = ((RectTransform)text.transform.parent).rect.width;
        text.text = "少し時間が過ぎた。";
        Rebuild();
        Rect background = ((RectTransform)text.transform.parent).rect;
        Assert.That(background.width, Is.LessThan(longWidth));
        Assert.That(background.width, Is.LessThan(600));
        Assert.That(background.height, Is.LessThan(160));
        Assert.That(text.isTextTruncated, Is.False);
    }

    [Test]
    public void Initialize_PreservesAuthoredLayoutAndFont_WithoutCreatingUi()
    {
        var rect = (RectTransform)panel.transform;
        Vector2 anchors = rect.anchorMin;
        TMP_FontAsset font = text.font;
        int children = canvasObject.GetComponentsInChildren<Transform>(true).Length;
        presenter.Initialize(canvasObject.GetComponent<Canvas>(), TMP_Settings.defaultFontAsset);
        presenter.Initialize(canvasObject.GetComponent<Canvas>(), null);
        Assert.That(rect.anchorMin, Is.EqualTo(anchors));
        Assert.That(text.font, Is.SameAs(font));
        Assert.That(canvasObject.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
        Assert.That(panel.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator ShowResult_UsesAuthoredPanel_AndHidesAfterDuration()
    {
        presenter.Initialize(canvasObject.GetComponent<Canvas>(), text.font);
        var task = presenter.ShowResultAsync("少し時間が過ぎた。", .1f);
        Assert.That(panel.activeSelf, Is.True);
        Assert.That(panel.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
        yield return task.ToCoroutine();
        Assert.That(panel.activeSelf, Is.False);
        Assert.That(panel.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0));
    }

    [Test]
    public void MissingSceneReferences_ReportsClearError_WithoutCreatingUi()
    {
        var serialized = new SerializedObject(presenter);
        serialized.FindProperty("rootGroup").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        LogAssert.Expect(LogType.Error, "[ResultTextUI] Assign the authored result panel and text in the Scene before displaying a timed event.");
        presenter.ShowResultAsync("少し時間が過ぎた。", .1f).GetAwaiter().GetResult();
        Assert.That(canvasObject.transform.childCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator HiddenResultDoesNotLaterHideItsReplacement()
    {
        presenter.Initialize(canvasObject.GetComponent<Canvas>(), text.font);
        var old = presenter.ShowResultAsync("少し時間が過ぎた。", .1f);
        presenter.HideInstant();
        var replacement = presenter.ShowResultAsync("少し時間が過ぎた。", .4f);
        yield return old.ToCoroutine();
        Assert.That(panel.activeSelf, Is.True);
        yield return replacement.ToCoroutine();
        Assert.That(panel.activeSelf, Is.False);
    }

    private void SetResolution(int width, int height)
    {
        // Match the MainScene CanvasScaler's 1280x720 reference and 0.5 match value.
        float scale = Mathf.Sqrt((width / 1280f) * (height / 720f));
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width / scale, height / scale);
    }

    private void Rebuild()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        text.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        text.ForceMeshUpdate();
    }
}
