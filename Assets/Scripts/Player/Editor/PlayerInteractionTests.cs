using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public sealed class PlayerInteractionTests
{
    [Test]
    public void InteractionAvailabilityRequiresWalkAndUsableTargetAndUnblockedInput()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        float previousScale = Time.timeScale;
        try
        {
            PlayerInteractor interactor = null;
            CableTravelInteractable target = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (interactor == null) interactor = root.GetComponentInChildren<PlayerInteractor>(true);
                if (target == null) target = root.GetComponentInChildren<CableTravelInteractable>(true);
            }
            Assert.That(interactor, Is.Not.Null);
            Assert.That(target, Is.Not.Null);
            var walk = interactor.GetComponent<PlayerWalkModeController>();
            var state = new UnityEditor.SerializedObject(walk);
            interactor.enabled = true;
            target.enabled = true;
            target.gameObject.SetActive(true);
            Time.timeScale = 1;
            typeof(PlayerInteractor).GetField("currentInteractable", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(interactor, target);
            state.FindProperty("isWalkMode").boolValue = false;
            state.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(interactor.IsInteractionAvailable, Is.False, "Conversation must not show E.");
            state.FindProperty("isWalkMode").boolValue = true;
            state.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(interactor.IsInteractionAvailable, Is.True);
            foreach (string field in new[] { "isTransitioning", "isPauseMenuOpen", "isInteractionMenuOpen", "isTabletInteractionActive" })
            {
                state.FindProperty(field).boolValue = true;
                state.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(interactor.IsInteractionAvailable, Is.False, field);
                state.FindProperty(field).boolValue = false;
                state.ApplyModifiedPropertiesWithoutUndo();
            }
            target.enabled = false;
            Assert.That(interactor.IsInteractionAvailable, Is.False, "Disabled target");
            target.enabled = true;
            Time.timeScale = 0;
            Assert.That(interactor.IsInteractionAvailable, Is.False, "Paused time");
            Time.timeScale = 1;
            interactor.ResetInteractionState();
            Assert.That(interactor.IsInteractionAvailable, Is.False, "No target");
        }
        finally
        {
            Time.timeScale = previousScale;
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [TestCase("reset")]
    [TestCase("disable")]
    [TestCase("title")]
    public void ResetAndDisable_ClearTargetAndPromptAndAllowRedetection(string path)
    {
        GameObject player = new GameObject("Prompt lifecycle player");
        GameObject target = new GameObject("Prompt lifecycle cable");
        try
        {
            PlayerInteractionPrompt prompt = player.AddComponent<PlayerInteractionPrompt>();
            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            typeof(PlayerInteractor).GetField("prompt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(interactor, prompt);
            CableTravelInteractable cable = target.AddComponent<CableTravelInteractable>();
            MethodInfo select = typeof(PlayerInteractor).GetMethod("SetCurrentInteractable", BindingFlags.Instance | BindingFlags.NonPublic);
            select.Invoke(interactor, new object[] { cable });
            Assert.That(prompt.IsVisible, Is.True);

            if (path == "title") typeof(Nekolpos.System.OpenBetaPauseMenuController).GetMethod("StopInteractionsForTitleReload", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            else if (path == "disable") typeof(PlayerInteractor).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interactor, null);
            else interactor.ResetInteractionState();
            Assert.That(interactor.CurrentInteractable, Is.Null);
            Assert.That(prompt.IsVisible, Is.False);
            if (path == "title") Assert.That(interactor.enabled, Is.False);

            select.Invoke(interactor, new object[] { cable });
            Assert.That(prompt.IsVisible, Is.True, "Resume must be able to rediscover an interaction.");
            Object.DestroyImmediate(target);
            select.Invoke(interactor, new object[] { cable });
            Assert.That(interactor.CurrentInteractable, Is.Null);
            Assert.That(prompt.IsVisible, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(player);
            if (target != null) Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void PlayerInteractor_UsesShortCameraSphereCastAndIncludesTriggerColliders()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/Interaction/PlayerInteractor.cs");

        StringAssert.Contains("Physics.SphereCastNonAlloc", source);
        StringAssert.Contains("QueryTriggerInteraction.Collide", source);
        StringAssert.Contains("Input.GetKeyDown(KeyCode.E)", source);
        StringAssert.Contains("!walkModeController.IsWalkMode", source);
        StringAssert.Contains("IsTextEntryFocused()", source);
    }

    [Test]
    public void CableTravelInteractable_DelegatesToExistingSafeAreaTransition()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/Interaction/CableTravelInteractable.cs");

        StringAssert.Contains("PlayerAreaTransitionController", source);
        StringAssert.Contains("TransitionToAsync(destination, expectedAreaId)", source);
        StringAssert.DoesNotContain("transform.position =", source);
    }

    [Test]
    public void NerukoInteraction_UsesWalkOnlyDialogueWithoutStartingNormalConversation()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/Interaction/NerukoTalkInteractable.cs");

        StringAssert.Contains("walkDialogueController.OpenWalkChoice()", source);
        StringAssert.DoesNotContain("TryExitWalkMode", source);
    }
}
