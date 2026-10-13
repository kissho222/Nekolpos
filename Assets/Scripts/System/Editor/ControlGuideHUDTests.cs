using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using System.Collections;

public sealed class ControlGuideHUDTests
{
    [Test]
    public void ChatInputWidthAdaptsAndReturnsToAuthoredSize()
    {
        var root = new GameObject("Responsive input test", typeof(RectTransform));
        var owner = new GameObject("Responsive guide test");
        try
        {
            var parent = root.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2(1280, 720);
            var inputObject = new GameObject("Input", typeof(RectTransform), typeof(InputField));
            var input = inputObject.GetComponent<RectTransform>();
            input.SetParent(parent, false);
            input.anchorMin = input.anchorMax = new Vector2(.5f, 0);
            input.sizeDelta = new Vector2(665, 54);
            input.anchoredPosition = new Vector2(0, 42);
            var chat = root.AddComponent<Nekolpos.System.ChatUIController>();
            chat.chatInputField = inputObject.GetComponent<InputField>();
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.Bind(null, chat);
            guide.SetMode(ControlGuideMode.Conversation);
            guide.Panel.SetParent(parent, false);
            guide.Panel.sizeDelta = new Vector2(200, 124);
            guide.Panel.anchoredPosition = new Vector2(-18, 16);
            var update = typeof(Nekolpos.System.ChatUIController).GetMethod("UpdateResponsiveInputWidth",
                global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic);
            foreach (float width in new[] { 1280f, 900f, 700f, 900f, 1280f, 2000f })
            {
                parent.sizeDelta = new Vector2(width, 720);
                update.Invoke(chat, null);
                Assert.That(input.anchoredPosition, Is.EqualTo(new Vector2(0, 42)));
                Assert.That(input.rect.height, Is.EqualTo(54));
                Assert.That(input.rect.width, Is.LessThanOrEqualTo(665));
                Assert.That(input.rect.width / 2, Is.LessThanOrEqualTo(width / 2 - 230f + .01f));
                if (width >= 1280) Assert.That(input.rect.width, Is.EqualTo(665));
            }
            guide.SetMode(ControlGuideMode.None);
            parent.sizeDelta = new Vector2(500, 720);
            update.Invoke(chat, null);
            Assert.That(input.rect.width, Is.EqualTo(476).Within(.01f));
            parent.sizeDelta = new Vector2(1280, 720);
            update.Invoke(chat, null);
            Assert.That(input.rect.width, Is.EqualTo(665));
        }
        finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(root); }
    }

    [TearDown]
    public void NoUnownedGuideCanvasSurvivesTestCleanup()
    {
        var owners = Resources.FindObjectsOfTypeAll<ControlGuideHUD>();
        foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (canvas.name != "ControlGuideCanvas") continue;
            bool owned = false;
            foreach (var owner in owners)
                if (owner != null && owner.Panel != null && owner.Panel.GetComponentInParent<Canvas>() == canvas)
                    owned = true;
            Assert.That(owned, Is.True, "An unowned HUD escaped Scene cleanup: " + canvas.GetInstanceID());
        }
    }

    [Test]
    public void EditModeOwnerDestructionReleasesViewsAcrossRepeatedModeChanges()
    {
        Assert.That(Application.isPlaying, Is.False);
        for (int repeat = 0; repeat < 6; repeat++)
        {
            var owner = new GameObject("EditMode guide lifetime test");
            GameObject canvas = null;
            try
            {
                var guide = owner.AddComponent<ControlGuideHUD>();
                guide.SetMode(ControlGuideMode.Conversation);
                canvas = guide.Panel.GetComponentInParent<Canvas>().gameObject;
                guide.SetMode(ControlGuideMode.Walk);
                guide.SetMode(ControlGuideMode.WalkChoice);
                guide.SetMode(ControlGuideMode.Conversation);
                Assert.That(guide.Panel.Find("move").gameObject.activeSelf, Is.False);
                Assert.That(guide.Panel.Find("submit").gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(owner); }
            Assert.That(canvas == null, Is.True, "EditMode must invoke ownership teardown without EnterPlayMode.");
        }
    }

    [Test]
    public void OrphanRecoveryDoesNotDeleteAnotherOwnersView()
    {
        var first = new GameObject("First guide owner");
        var second = new GameObject("Second guide owner");
        GameObject orphan = null;
        try
        {
            var guide = first.AddComponent<ControlGuideHUD>();
            guide.SetMode(ControlGuideMode.Conversation);
            var owned = guide.Panel.GetComponentInParent<Canvas>().gameObject;
            orphan = new GameObject("ControlGuideCanvas", typeof(Canvas), typeof(CanvasGroup));
            orphan.hideFlags = HideFlags.DontSaveInEditor;
            new GameObject("ControlGuideHUD", typeof(RectTransform)).transform.SetParent(orphan.transform, false);
            second.AddComponent<ControlGuideHUD>();
            Assert.That(orphan == null, Is.True);
            Assert.That(owned != null, Is.True);
            Assert.That(guide.IsVisible, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            if (orphan != null) Object.DestroyImmediate(orphan);
        }
    }

    [Test]
    public void LostSerializedViewIsRecreatedWithoutLeavingOldCanvas()
    {
        var owner = new GameObject("Guide serialization test");
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.SetMode(ControlGuideMode.Walk);
            var panel = guide.Panel;
            var oldCanvas = panel.GetComponentInParent<Canvas>().gameObject;
            string snapshot = UnityEditor.EditorJsonUtility.ToJson(guide);
            guide.SetMode(ControlGuideMode.Conversation);
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(snapshot, guide);
            typeof(ControlGuideHUD).GetMethod("OnEnable", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic).Invoke(guide, null);
            Assert.That(oldCanvas == null, Is.True, "Lost DontSave references must not leave a visible old canvas.");
            guide.SetMode(ControlGuideMode.Walk);
            panel = guide.Panel;
            Assert.That(guide.Mode, Is.EqualTo(ControlGuideMode.Walk));
            Assert.That(panel.Find("move").gameObject.activeSelf, Is.True);
            Assert.That(panel.Find("submit").gameObject.activeSelf, Is.False);
            guide.SetMode(ControlGuideMode.WalkChoice);
            Assert.That(panel.Find("move").gameObject.activeSelf, Is.False);
            Assert.That(panel.Find("choiceSubmit").gameObject.activeSelf, Is.True);
            Assert.That(global::System.Array.FindAll(Resources.FindObjectsOfTypeAll<Canvas>(), c => c.name == "ControlGuideCanvas").Length, Is.EqualTo(1));
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void DisabledEditorOwnerReleasesViewAndReEnableCreatesOneView()
    {
        var owner = new GameObject("Guide disable lifecycle test");
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.SetMode(ControlGuideMode.Conversation);
            var old = guide.Panel.GetComponentInParent<Canvas>().gameObject;
            guide.enabled = false;
            Assert.That(old == null, Is.True);
            guide.SetMode(ControlGuideMode.Walk);
            Assert.That(guide.Panel, Is.Null, "An inactive owner must not recreate UI.");
            guide.enabled = true;
            guide.SetMode(ControlGuideMode.WalkChoice);
            Assert.That(guide.Panel.Find("choiceSubmit").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("submit").gameObject.activeSelf, Is.False);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void MainSceneInputKeepsUserBottomCenterBaseline()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var chat = root.GetComponentInChildren<Nekolpos.System.ChatUIController>(true);
                if (chat == null) continue;
                var input = chat.chatInputField.GetComponent<RectTransform>();
                Assert.That(input.anchorMin, Is.EqualTo(new Vector2(.5f, 0)));
                Assert.That(input.anchorMax, Is.EqualTo(new Vector2(.5f, 0)));
                Assert.That(input.anchoredPosition, Is.EqualTo(new Vector2(0, 42)));
                Assert.That(input.sizeDelta, Is.EqualTo(new Vector2(665, 54)));
                return;
            }
            Assert.Fail("MainScene ChatUI was not found.");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    [TestCase(2560, 1440)]
    [TestCase(3840, 2160)]
    [TestCase(1024, 768)]
    [TestCase(2560, 1080)]
    public void EnlargedGuideAndSceneLogButton_DoNotOverlapOtherConversationControls(int width, int height)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        var owner = new GameObject("Guide layout test");
        try
        {
            var chat = scene.GetRootGameObjects()[0].GetComponentInChildren<Nekolpos.System.ChatUIController>(true);
            if (chat == null)
                foreach (var root in scene.GetRootGameObjects())
                {
                    chat = root.GetComponentInChildren<Nekolpos.System.ChatUIController>(true);
                    if (chat != null) break;
                }
            Assert.That(chat, Is.Not.Null);
            Canvas logCanvas = chat.logButton.GetComponentInParent<Canvas>();
            logCanvas.renderMode = RenderMode.WorldSpace;
            var logScaler = logCanvas.GetComponent<CanvasScaler>();
            SetCanvasSize(logCanvas, logScaler.referenceResolution, width, height);
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.SetInteractionPrompt("E：コードを登る");
            guide.SetMode(ControlGuideMode.Walk);
            Canvas guideCanvas = guide.Panel.GetComponentInParent<Canvas>();
            guideCanvas.renderMode = RenderMode.WorldSpace;
            SetCanvasSize(guideCanvas, guideCanvas.GetComponent<CanvasScaler>().referenceResolution, width, height);
            Rect logRect = ScreenRect(chat.logButton.GetComponent<RectTransform>(), logCanvas, width, height);
            Assert.That(logRect.width, Is.GreaterThan(131.78f * width / ((RectTransform)logCanvas.transform).rect.width));
            foreach (ControlGuideMode mode in new[] { ControlGuideMode.Conversation, ControlGuideMode.Walk, ControlGuideMode.WalkChoice, ControlGuideMode.WalkDialogue })
            {
                guide.SetMode(mode);
                LayoutRebuilder.ForceRebuildLayoutImmediate(guide.Panel);
                Rect guideRect = ScreenRect(guide.Panel, guideCanvas, width, height);
                Assert.That(logRect.Overlaps(guideRect), Is.False, "Log button / guide");
                foreach (string name in new[] { "ChatInputField", "ChatWindowPanel" })
                {
                    var rect = (RectTransform)chat.transform.Find(name);
                    Rect other = ScreenRect(rect, logCanvas, width, height);
                    Assert.That(logRect.Overlaps(other), Is.False, "Log button / " + name);
                    Assert.That(guideRect.Overlaps(other), Is.False, "Guide / " + name);
                }
                Assert.That(logRect.yMin, Is.GreaterThan(guideRect.yMax), "History belongs above the guide");
                Assert.That(guide.Panel.anchoredPosition.y, Is.EqualTo(24));
                Assert.That(guide.Panel.Find("UIStyleBorder").GetComponent<LayoutElement>().ignoreLayout, Is.True);
                Assert.That(guideRect.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(guideRect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(guideRect.xMax, Is.LessThanOrEqualTo(width));
                Assert.That(guideRect.yMax, Is.LessThanOrEqualTo(height));
                foreach (TMP_Text label in guide.Panel.GetComponentsInChildren<TMP_Text>())
                {
                    label.ForceMeshUpdate();
                    Assert.That(label.renderedWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + 1f), label.name);
                    Assert.That(label.renderedHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1f), label.name);
                }
            }
            Assert.That(logRect.yMax, Is.LessThanOrEqualTo(height));
            TMP_Text logText = chat.logButton.GetComponentInChildren<TMP_Text>(true);
            logText.ForceMeshUpdate(true);
            Assert.That(logText.isTextTruncated, Is.False);
            Assert.That(logText.renderedWidth, Is.LessThanOrEqualTo(logText.rectTransform.rect.width + 1f));
            Assert.That(logText.fontSize, Is.GreaterThan(22));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void ConversationChoicesHideInputGuideAndHistoryUntilClosed()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        try
        {
            Nekolpos.System.ChatUIController chat = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                chat = root.GetComponentInChildren<Nekolpos.System.ChatUIController>(true);
                if (chat != null) break;
            }
            Assert.That(chat, Is.Not.Null);
            for (Transform parent = chat.transform; parent != null; parent = parent.parent) parent.gameObject.SetActive(true);
            chat.enabled = true;
            chat.chatInputField.gameObject.SetActive(true);
            chat.chatInputField.interactable = true;
            chat.choicePanel.SetActive(false);
            chat.RouteSubmittedInputToDialogueEngine = true;
            chat.SetLogButtonAllowedDuringConversation(true);
            Assert.That(chat.IsControlGuideInputActive, Is.True);
            Assert.That(chat.logButton.gameObject.activeSelf, Is.True);
            chat.choicePanel.SetActive(true);
            chat.SetLogButtonAllowedDuringConversation(true);
            Assert.That(chat.IsControlGuideInputActive, Is.False);
            Assert.That(chat.logButton.gameObject.activeSelf, Is.False);
            chat.choicePanel.SetActive(false);
            chat.SetLogButtonAllowedDuringConversation(true);
            Assert.That(chat.IsControlGuideInputActive, Is.True);
            Assert.That(chat.logButton.gameObject.activeSelf, Is.True);
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void MainSceneDoesNotPersistRuntimeGuideCanvas()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        try
        {
            foreach (var root in scene.GetRootGameObjects())
                Assert.That(root.name, Is.Not.EqualTo("ControlGuideCanvas"), "A saved Runtime HUD would appear alongside the current mode's guide.");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void SetCanvasSize(Canvas canvas, Vector2 reference, int width, int height)
    {
        float scale = Mathf.Sqrt((width / reference.x) * (height / reference.y));
        // Preview scenes have no rendered camera, so overlay Canvas scale may be zero.
        canvas.transform.localScale = Vector3.one;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width / scale, height / scale);
    }

    private static Rect ScreenRect(RectTransform rect, Canvas canvas, int width, int height)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var canvasRect = (RectTransform)canvas.transform;
        Vector3 min = canvasRect.InverseTransformPoint(corners[0]);
        Vector3 max = canvasRect.InverseTransformPoint(corners[2]);
        Rect bounds = canvasRect.rect;
        return Rect.MinMaxRect((min.x - bounds.xMin) * width / bounds.width,
            (min.y - bounds.yMin) * height / bounds.height,
            (max.x - bounds.xMin) * width / bounds.width,
            (max.y - bounds.yMin) * height / bounds.height);
    }

    [TestCase("E：話す", "話す")]
    [TestCase("E: 床に移動", "床に移動")]
    [TestCase("机に移動", "机に移動")]
    [TestCase("", "インタラクト")]
    [TestCase(null, "インタラクト")]
    public void ContextLabelUsesExistingPrompt(string prompt, string expected)
    {
        Assert.That(ControlGuideHUD.NormalizeInteractionLabel(prompt), Is.EqualTo(expected));
    }

    [UnityTest]
    public IEnumerator ModesSwitchRowsAndKeepBottomRightAnchorWithoutBlockingInput()
    {
        yield return new EnterPlayMode();
        GameObject owner = new GameObject("Guide test");
        GameObject canvas = null;
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.SetMode(ControlGuideMode.Conversation);
            canvas = guide.Panel.GetComponentInParent<Canvas>().gameObject;
            Assert.That(canvas.scene, Is.EqualTo(owner.scene));
            Assert.That(guide.Panel.anchorMin, Is.EqualTo(new Vector2(1, 0)));
            Assert.That(guide.Panel.anchorMax, Is.EqualTo(new Vector2(1, 0)));
            Assert.That(guide.Panel.pivot, Is.EqualTo(new Vector2(1, 0)));
            Assert.That(canvas.hideFlags & HideFlags.DontSaveInEditor, Is.EqualTo(HideFlags.DontSaveInEditor));
            Assert.That(guide.Panel.Find("submit").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("history").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("move").gameObject.activeSelf, Is.False);
            Assert.That(guide.Panel.Find("submit/Keys/Key_Enter").GetComponent<Image>().sprite, Is.Not.Null);
            guide.SetMode(ControlGuideMode.Walk);
            Assert.That(guide.Panel.Find("submit").gameObject.activeSelf, Is.False);
            foreach (string id in new[] { "move", "jump" })
                Assert.That(guide.Panel.Find(id).gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("interact").gameObject.activeSelf, Is.False);
            guide.SetInteractionPrompt("E：話す");
            Assert.That(guide.Panel.Find("interact").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("interact").GetSiblingIndex(), Is.LessThan(guide.Panel.Find("move").GetSiblingIndex()));
            Assert.That(guide.Panel.Find("interact/Label").GetComponent<TMP_Text>().text, Is.EqualTo("話す"));
            guide.SetInteractionPrompt(null);
            Assert.That(guide.Panel.Find("interact").gameObject.activeSelf, Is.False);
            Assert.That(guide.InteractionLabel, Is.EqualTo("インタラクト"));
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False);
            guide.SetMode(ControlGuideMode.WalkChoice);
            foreach (string id in new[] { "submit", "history", "interact", "move", "jump", "advance" })
                Assert.That(guide.Panel.Find(id).gameObject.activeSelf, Is.False, id);
            foreach (string id in new[] { "choiceSelect", "choiceSubmit", "choiceCancel" })
                Assert.That(guide.Panel.Find(id).gameObject.activeSelf, Is.True, id);
            guide.SetMode(ControlGuideMode.WalkDialogue);
            Assert.That(guide.Panel.Find("advance").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("choiceSubmit").gameObject.activeSelf, Is.False);
            guide.SetMode(ControlGuideMode.Conversation);
            foreach (string id in new[] { "interact", "move", "jump", "choiceSelect", "choiceSubmit", "choiceCancel", "advance" })
                Assert.That(guide.Panel.Find(id).gameObject.activeSelf, Is.False, id);
            guide.SetMode(ControlGuideMode.None);
            Assert.That(guide.IsVisible, Is.False);
            Object.Destroy(owner);
            yield return null;
            yield return null;
            Assert.That(canvas == null, Is.True, "Player destruction must remove its guide canvas.");
        }
        finally
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (canvas != null) Object.DestroyImmediate(canvas);
        }
        yield return new ExitPlayMode();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TeardownIsSafeWhenCanvasOrGuideIsDestroyedFirst(bool destroyGuideFirst)
    {
        var owner = new GameObject("Guide teardown test");
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            var prompt = owner.AddComponent<PlayerInteractionPrompt>();
            guide.SetMode(ControlGuideMode.Walk);
            prompt.SetPrompt("E：話す");
            if (destroyGuideFirst) Object.DestroyImmediate(guide);
            else Object.DestroyImmediate(guide.Panel.GetComponentInParent<Canvas>().gameObject);
            Assert.DoesNotThrow(() => prompt.Hide());
            Assert.That(prompt.IsVisible, Is.False);
            Assert.DoesNotThrow(() => owner.SetActive(false));
        }
        finally { Object.DestroyImmediate(owner); }
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void DestroyedCanvasCanBeRecreatedWithoutStaleRows()
    {
        var owner = new GameObject("Guide recreate test");
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            guide.SetMode(ControlGuideMode.Walk);
            int originalChildCount = guide.Panel.childCount;
            Object.DestroyImmediate(guide.Panel.GetComponentInParent<Canvas>().gameObject);
            guide.SetInteractionPrompt("E：話す");
            guide.SetMode(ControlGuideMode.Walk);
            Assert.That(guide.Panel.childCount, Is.EqualTo(originalChildCount));
            Assert.That(guide.Panel.Find("interact").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("interact/Label").GetComponent<TMP_Text>().text, Is.EqualTo("話す"));
            guide.SetMode(ControlGuideMode.Conversation);
            Assert.That(guide.Panel.Find("submit").gameObject.activeSelf, Is.True);
            Assert.That(guide.Panel.Find("move").gameObject.activeSelf, Is.False);
        }
        finally { Object.DestroyImmediate(owner); }
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator SceneUnloadReleasesCanvasAndPromptWithoutExceptions()
    {
        yield return new EnterPlayMode();
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("Guide teardown scene");
        var owner = new GameObject("Guide scene owner");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, scene);
        var guide = owner.AddComponent<ControlGuideHUD>();
        var prompt = owner.AddComponent<PlayerInteractionPrompt>();
        guide.SetMode(ControlGuideMode.Walk);
        prompt.SetPrompt("E：話す");
        var canvas = guide.Panel.GetComponentInParent<Canvas>().gameObject;
        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        yield return null;
        Assert.That(owner == null, Is.True);
        Assert.That(canvas == null, Is.True);
        LogAssert.NoUnexpectedReceived();
        yield return new ExitPlayMode();
    }

    [Test]
    public void PromptAdapterResetsHudContextWithoutCreatingLegacyCanvas()
    {
        var owner = new GameObject("Guide adapter test");
        try
        {
            var guide = owner.AddComponent<ControlGuideHUD>();
            var prompt = owner.AddComponent<PlayerInteractionPrompt>();
            prompt.SetPrompt("E：コードを登る");
            Assert.That(guide.InteractionLabel, Is.EqualTo("コードを登る"));
            Assert.That(prompt.IsVisible, Is.True);
            prompt.Hide();
            Assert.That(guide.InteractionLabel, Is.EqualTo("インタラクト"));
            Assert.That(prompt.IsVisible, Is.False);
            Assert.That(guide.Panel, Is.Null, "Prompt forwarding should not create a separate canvas.");
        }
        finally { Object.DestroyImmediate(owner); }
    }
}
