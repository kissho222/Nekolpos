using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using System.Collections;
using System.Reflection;
using Nekolpos.System;

public sealed class WalkDialogueCatalogTests
{
    private const string Header = "TriggerType,TriggerId,Speaker,PsychologyState,Text,Order\n";
    private static int completedSequences;
    private static void MarkSequenceCompleted() => completedSequences++;

    [Test]
    public void AuthoredWalkWindowUsesSharedThemeAndHasNoFooterGuide()
    {
        var view = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/WalkDialogueView.prefab");
        var panel = view.transform.Find("WalkChoicePanel");
        Assert.That(panel.Find("ControlsHint"), Is.Null);
        Assert.That(panel.GetComponent<Image>().color, Is.EqualTo(Nekolpos.UI.UIStyle.NormalPanel));
        foreach (var text in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
            Assert.That(text.color, Is.EqualTo(Nekolpos.UI.UIStyle.NormalText), text.name);
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
        {
            Assert.That(button.image.color, Is.EqualTo(Color.white), "Button colors must not be multiplied twice.");
            Assert.That(button.colors.normalColor, Is.EqualTo(Nekolpos.UI.UIStyle.NormalButton));
            Assert.That(button.colors.selectedColor, Is.Not.EqualTo(button.colors.normalColor));
        }
    }

    [Test]
    public void MissingAuthoredView_ReportsErrorWithoutGeneratingFixedUi()
    {
        var owner = new GameObject("Walk missing view test");
        owner.SetActive(false);
        try
        {
            var controller = owner.AddComponent<WalkDialogueController>();
            LogAssert.Expect(LogType.Error, "[WalkDialogueController] Assign the authored WalkDialogueView and its five buttons in the Scene.");
            bool ready = (bool)typeof(WalkDialogueController).GetMethod("EnsureView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Assert.That(ready, Is.False);
            Assert.That(owner.transform.childCount, Is.Zero);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void WalkChoices_PlaceEndThenNothingLast_AndNothingClosesMenu()
    {
        var owner = new GameObject("Walk choice order test");
        var canvas = new GameObject("Walk choice canvas", typeof(Canvas));
        try
        {
            owner.SetActive(false);
            var controller = CreateAuthoredController(owner, out var view);
            view.transform.SetParent(canvas.transform, false);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var panel = (GameObject)typeof(WalkDialogueController).GetField("choicePanel", flags).GetValue(controller);
            typeof(WalkDialogueController).GetMethod("EnsureView", flags).Invoke(controller, null);
            string[] names = { "ついてきて", "運んでほしい", "ちょっと下がって", "お散歩終わり！", "なんでもない" };
            float previousY = float.PositiveInfinity;
            for (int i = 0; i < names.Length; i++)
            {
                var button = panel.GetComponentsInChildren<Button>(true)[i];
                Assert.That(button.name, Is.EqualTo(names[i]));
                float y = button.GetComponent<RectTransform>().anchoredPosition.y;
                Assert.That(y, Is.LessThan(previousY));
                previousY = y;
            }
            Assert.That(panel.transform.Find("運んでほしい").GetComponent<UnityEngine.UI.Button>().interactable, Is.False);
            Assert.That(panel.transform.Find("お散歩終わり！").GetComponent<UnityEngine.UI.Button>().interactable, Is.True);
            typeof(WalkDialogueController).GetField("choicePanel", flags).SetValue(controller, panel);
            typeof(WalkDialogueController).GetField("isChoiceOpen", flags).SetValue(controller, true);
            panel.transform.Find("なんでもない").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(panel.activeSelf, Is.False);
            Assert.That(controller.IsDialogueBusy, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(canvas);
        }
    }

    private static WalkDialogueController CreateAuthoredController(GameObject owner, out GameObject view)
    {
        var controller = owner.AddComponent<WalkDialogueController>();
        view = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/WalkDialogueView.prefab"));
        var serialized = new UnityEditor.SerializedObject(controller);
        serialized.FindProperty("canvasObject").objectReferenceValue = view;
        serialized.FindProperty("nerukoBubble").objectReferenceValue = view.transform.Find("NerukoBubble");
        serialized.FindProperty("nerukoText").objectReferenceValue = view.transform.Find("NerukoBubble/Text").GetComponent<TMPro.TextMeshProUGUI>();
        serialized.FindProperty("playerBubble").objectReferenceValue = view.transform.Find("PlayerDescriptionBubble");
        serialized.FindProperty("playerText").objectReferenceValue = view.transform.Find("PlayerDescriptionBubble/Text").GetComponent<TMPro.TextMeshProUGUI>();
        var panel = view.transform.Find("WalkChoicePanel");
        serialized.FindProperty("choicePanel").objectReferenceValue = panel.gameObject;
        var buttons = panel.GetComponentsInChildren<Button>(true);
        serialized.FindProperty("followChoiceText").objectReferenceValue = buttons[0].GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        var array = serialized.FindProperty("choiceButtons");
        array.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return controller;
    }

    [UnityTest]
    public IEnumerator KeyboardNavigation_SkipsDisabledCarry_AndSubmitAndCancelUseExistingButtons()
    {
        yield return new EnterPlayMode();
        var owner = new GameObject("Walk navigation test");
        owner.SetActive(false);
        GameObject view = null;
        GameObject eventsObject = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            if (EventSystem.current == null) eventsObject = new GameObject("Walk test events", typeof(EventSystem));
            Assert.That(EventSystem.current, Is.Not.Null);
            var controller = CreateAuthoredController(owner, out view);
            var cat = owner.AddComponent<CatLocomotionController>();
            typeof(WalkDialogueController).GetField("catLocomotionController", flags).SetValue(controller, cat);
            typeof(WalkDialogueController).GetMethod("OpenChoicePanel", flags).Invoke(controller, null);
            typeof(WalkDialogueController).GetField("inputReadyFrame", flags).SetValue(controller, -1);
            var buttons = view.transform.Find("WalkChoicePanel").GetComponentsInChildren<Button>(true);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[0].gameObject));
            Assert.That(buttons[1].interactable, Is.False);
            Assert.That(buttons[0].colors.selectedColor, Is.Not.EqualTo(buttons[0].colors.normalColor));
            var move = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down };
            ExecuteEvents.Execute(buttons[0].gameObject, move, ExecuteEvents.moveHandler);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[2].gameObject));
            Assert.That(WalkDialogueController.TryCancelActiveChoice(), Is.True);
            Assert.That(WalkDialogueController.CancelledThisFrame, Is.True);
            Assert.That(view.transform.Find("WalkChoicePanel").gameObject.activeSelf, Is.False);
            typeof(WalkDialogueController).GetMethod("OpenChoicePanel", flags).Invoke(controller, null);
            typeof(WalkDialogueController).GetField("inputReadyFrame", flags).SetValue(controller, -1);
            EventSystem.current.SetSelectedGameObject(buttons[4].gameObject);
            ExecuteEvents.Execute(buttons[4].gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.That(controller.IsDialogueBusy, Is.False);
            Assert.That(WalkDialogueController.TryCancelActiveChoice(), Is.False);
            Assert.That(WalkDialogueController.HandledInputThisFrame, Is.True, "Menu submit must not reach walking E/Space in the same frame.");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            if (view != null) Object.DestroyImmediate(view);
            if (eventsObject != null) Object.DestroyImmediate(eventsObject);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Typewriter_SkipThenAdvance_ConsumesOnePress_AndCancellationStopsOldSequence()
    {
        yield return new EnterPlayMode();
        var owner = new GameObject("Walk typewriter test");
        owner.SetActive(false);
        GameObject view = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        completedSequences = 0;
        float previousScale = Time.timeScale;
        try
        {
            var controller = CreateAuthoredController(owner, out view);
            owner.SetActive(true);
            typeof(WalkDialogueController).GetField("typingSpeedSource", flags).SetValue(controller, null);
            typeof(WalkDialogueController).GetField("typingSecondsPerCharacter", flags).SetValue(controller, .2f);
            var entry = WalkDialogueCatalog.LoadDefault().Find("Choice", "NerukoTalk")[0];
            entry = new WalkDialogueCatalog.Entry(entry.TriggerType, entry.TriggerId, entry.Speaker,
                entry.PsychologyState, "<b>" + entry.Text + "</b>", entry.Order);
            var entries = new System.Collections.Generic.List<WalkDialogueCatalog.Entry> { entry };
            typeof(WalkDialogueController).GetMethod("StartSequence", flags).Invoke(controller,
                new object[] { entries, new System.Action(MarkSequenceCompleted), false });
            var text = view.transform.Find("NerukoBubble/Text").GetComponent<TMPro.TextMeshProUGUI>();
            Assert.That(controller.IsTyping, Is.True);
            Assert.That(text.maxVisibleCharacters, Is.EqualTo(0));
            Assert.That(text.textInfo.characterCount, Is.LessThan(text.text.Length), "Rich text tags must not be typed as characters.");
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(text.maxVisibleCharacters, Is.EqualTo(0), "Pause must not type or consume advancement.");
            controller.RequestAdvance();
            Assert.That(controller.IsTyping, Is.True);
            Time.timeScale = 1;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(text.maxVisibleCharacters, Is.GreaterThan(0));
            Assert.That(controller.IsTyping, Is.True);
            controller.RequestAdvance();
            controller.RequestAdvance();
            Assert.That(controller.IsTyping, Is.False);
            Assert.That(text.maxVisibleCharacters, Is.EqualTo(int.MaxValue));
            Assert.That(completedSequences, Is.Zero, "Skipping must not advance the same line.");
            int skippedFrame = Time.frameCount;
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(Time.frameCount, Is.GreaterThan(skippedFrame));
            controller.RequestAdvance();
            Assert.That((bool)typeof(WalkDialogueController).GetField("advanceRequested", flags).GetValue(controller), Is.True);
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(completedSequences, Is.EqualTo(1));
            completedSequences = 0;
            typeof(WalkDialogueController).GetMethod("StartSequence", flags).Invoke(controller,
                new object[] { entries, new System.Action(MarkSequenceCompleted), false });
            controller.ClearForWalkExit();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(completedSequences, Is.Zero);
            Assert.That(controller.IsTyping, Is.False);
            Assert.That(controller.IsDialogueBusy, Is.False);
            Assert.That(view.transform.Find("NerukoBubble").gameObject.activeSelf, Is.False);
        }
        finally
        {
            Time.timeScale = previousScale;
            Object.DestroyImmediate(owner);
            if (view != null) Object.DestroyImmediate(view);
        }
        yield return new ExitPlayMode();
    }

    [Test]
    public void DefaultCatalog_StepBackUsesRequestedReply()
    {
        var entries = WalkDialogueCatalog.LoadDefault().Find("ChoiceResult", "StepBack");
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Text, Is.EqualTo("ああごめん。ちょっと下がるね。"));
        Assert.That(entries[0].Speaker, Is.EqualTo("Neruko"));
    }

    [Test]
    public void Parse_LoadsOrderedPlayerThenNerukoSequence()
    {
        WalkDialogueCatalog catalog = WalkDialogueCatalog.Parse(Header +
            "Interaction,MouseToy,Player,NORMAL,説明,10\nInteraction,MouseToy,Neruko,NORMAL,返答,20\n");

        var entries = catalog.Find("Interaction", "MouseToy", "NORMAL");
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Speaker, Is.EqualTo("Player"));
        Assert.That(entries[1].Speaker, Is.EqualTo("Neruko"));
    }

    [Test]
    public void Find_FallsBackToNormalAndBlankTextDoesNotCreateEntry()
    {
        WalkDialogueCatalog catalog = WalkDialogueCatalog.Parse(Header +
            "Choice,NerukoTalk,Neruko,NORMAL,通常,10\nChoice,NerukoTalk,Neruko,AFFECTION,,20\n");

        Assert.That(catalog.Find("Choice", "NerukoTalk", "AFFECTION"), Has.Count.EqualTo(1));
        Assert.That(catalog.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_MalformedRowWarnsAndContinues()
    {
        WalkDialogueCatalog catalog = WalkDialogueCatalog.Parse(Header +
            "Interaction,Good,Neruko,NORMAL,有効,10\nInteraction,Bad,Neruko,NORMAL,壊れた,not_number\n");

        Assert.That(catalog.Find("Interaction", "Good"), Has.Count.EqualTo(1));
        Assert.That(catalog.Find("Interaction", "Bad"), Is.Empty);
    }

    [Test]
    public void WalkDialogueController_UsesRandomExistingWalkResultTimedEvent()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/WalkDialogue/WalkDialogueController.cs");

        StringAssert.Contains("action_walk_result", source);
        StringAssert.Contains("action_walk_result2", source);
        StringAssert.Contains("action_walk_result3", source);
        StringAssert.Contains("UnityEngine.Random.Range(0, WalkResultActionIds.Length)", source);
        StringAssert.Contains("dialogueManager.TryRunSystemTimedEvent(actionId, CompleteWalkWhileOpaque)", source);
    }

    [Test]
    public void WalkDialogueController_SupportsClickAdvanceAndReturnsDuringTimedEventFade()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/WalkDialogue/WalkDialogueController.cs");

        StringAssert.Contains("Input.GetMouseButtonDown(0)", source);
        StringAssert.Contains("while (!advanceRequested", source);
        StringAssert.Contains("areaTransitionController.TeleportImmediately(anchor, expectedArea)", source);
        StringAssert.Contains("walkModeController.TryExitWalkMode()", source);
    }
}
