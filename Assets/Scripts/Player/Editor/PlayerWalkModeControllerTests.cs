using System.Reflection;
using Nekolpos.System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.TestTools;

public sealed class PlayerWalkModeControllerTests
{
    [UnityTest]
    public IEnumerator Waypoints_HideWhenWalkCameraStopsAndReturnWhenItResumes()
    {
        yield return new EnterPlayMode();
        var root = new GameObject("Waypoint regression", typeof(RectTransform), typeof(Canvas));
        root.SetActive(false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var cameraObject = new GameObject("Waypoint regression camera", typeof(Camera));
        cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.GetComponent<Camera>();
        var parent = (RectTransform)root.transform;
        var manager = root.AddComponent<WrightAngle.Waypoint.WaypointUIManager>();
        SetField(manager, "settings", AssetDatabase.LoadAssetAtPath<WrightAngle.Waypoint.WaypointSettings>("Assets/DL Assets/WaypointSystem/Demo/WaypointSettings3D.asset"));
        SetField(manager, "waypointCamera", camera);
        SetField(manager, "markerParentCanvas", parent);
        root.SetActive(true);
        bool cameraEnabled = camera.enabled;
        bool managerEnabled = manager.enabled;
        float originalTimeScale = Time.timeScale;
        var targetObject = new GameObject("Walk marker regression target");
        targetObject.transform.position = camera.transform.position + camera.transform.forward * .2f;
        var target = targetObject.AddComponent<WrightAngle.Waypoint.WaypointTarget>();
        target.WorldOffset = Vector3.zero;
        manager.Register(target);
        try
        {
            Time.timeScale = 1;
            camera.enabled = true;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(parent.GetComponentsInChildren<WrightAngle.Waypoint.WaypointMarkerUI>().Length, Is.GreaterThan(0));
            camera.enabled = false;
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(parent.GetComponentsInChildren<WrightAngle.Waypoint.WaypointMarkerUI>().Length, Is.Zero,
                "Returning to conversation must hide even while time is paused.");
            Time.timeScale = 1;
            camera.enabled = true;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(parent.GetComponentsInChildren<WrightAngle.Waypoint.WaypointMarkerUI>().Length, Is.GreaterThan(0));
            manager.enabled = false;
            Assert.That(parent.GetComponentsInChildren<WrightAngle.Waypoint.WaypointMarkerUI>().Length, Is.Zero);
            manager.enabled = true;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(parent.GetComponentsInChildren<WrightAngle.Waypoint.WaypointMarkerUI>().Length, Is.GreaterThan(0));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            camera.enabled = cameraEnabled;
            manager.enabled = managerEnabled;
            Time.timeScale = originalTimeScale;
            Object.DestroyImmediate(root);
        }
        yield return new ExitPlayMode();
    }

    [Test]
    public void Unstuck_OnlyDuringWalkAndKeepsPauseUntilMenuCloses()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryUnstuck(), Is.False);
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Vector3 spawn = fixture.player.transform.position;
            fixture.playerSafety.TeleportTo(spawn + Vector3.up * 10);
            fixture.controller.SetPauseMenuOpen(true);
            Assert.That(fixture.controller.TryUnstuck(), Is.True);
            Assert.That(fixture.player.transform.position, Is.EqualTo(spawn));
            Assert.That(fixture.playerSafety.LastSafePosition, Is.EqualTo(spawn));
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsPauseMenuOpen, Is.True);
            Assert.That(fixture.playerSafety.enabled, Is.False);
            fixture.controller.SetPauseMenuOpen(false);
            Assert.That(fixture.playerSafety.enabled, Is.True);
            Assert.That(fixture.controller.TryBeginTabletInteraction(), Is.True);
            Assert.That(fixture.controller.TryUnstuck(), Is.False);
        }
        finally { fixture.Destroy(); }
    }

    [Test]
    public void TitleReset_IsIdempotentAndClearsTabletWalkInputWithoutMovingPlayer()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Assert.That(fixture.controller.TryBeginTabletInteraction(), Is.True);
            Vector3 position = fixture.player.transform.position;
            int exits = 0;
            fixture.controller.WalkModeChanged += walking => { if (!walking) exits++; };
            fixture.controller.ResetForTitleReload();
            fixture.controller.ResetForTitleReload();
            Assert.That(exits, Is.EqualTo(1));
            Assert.That(fixture.controller.IsWalkMode, Is.False);
            Assert.That(fixture.controller.IsTabletInteractionActive, Is.False);
            Assert.That(fixture.controller.IsTransitioning, Is.False);
            Assert.That(fixture.controller.IsPauseMenuOpen, Is.False);
            Assert.That(fixture.playerSafety.enabled, Is.False);
            Assert.That(fixture.playerCamera.enabled, Is.False);
            Assert.That(fixture.normalCamera.enabled, Is.True);
            Assert.That(fixture.player.transform.position, Is.EqualTo(position));
        }
        finally { fixture.Destroy(); }
    }

    [Test]
    public void TabletInteraction_RestoresPlayerAndCameraPoseWithoutLeavingWalkMode()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Vector3 savedPosition = new Vector3(4f, 0.03f, 3f);
            Quaternion savedRotation = Quaternion.Euler(-12f, 35f, 0f);
            fixture.playerSafety.TeleportTo(savedPosition);
            fixture.playerSafety.SetLookRotation(savedRotation);
            fixture.playerCamera.transform.SetParent(fixture.player.transform, false);
            fixture.playerCamera.transform.localPosition = new Vector3(0.01f, 0.045f, 0f);
            fixture.playerCamera.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
            Vector3 cameraPosition = fixture.playerCamera.transform.position;
            Quaternion cameraRotation = fixture.playerCamera.transform.rotation;
            Vector3 safePosition = fixture.playerSafety.LastSafePosition;
            var tabletChanges = new System.Collections.Generic.List<bool>();
            fixture.controller.TabletInteractionChanged += active => tabletChanges.Add(active);
            Assert.That(fixture.controller.TryBeginTabletInteraction(), Is.True);
            Assert.That(tabletChanges, Is.EqualTo(new[] { true }), "Cat navigation must receive the pause during entry, before the next frame.");
            Assert.That(fixture.controller.TryGetTabletReturnCameraPose(out Vector3 position, out Quaternion rotation), Is.True);
            Assert.That(position, Is.EqualTo(cameraPosition));
            Assert.That(Quaternion.Angle(rotation, cameraRotation), Is.LessThan(0.001f));
            Assert.That(fixture.playerSafety.enabled, Is.False);

            fixture.player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            fixture.playerCamera.transform.localRotation = Quaternion.identity;
            fixture.controller.CompleteTabletInteraction();
            Assert.That(tabletChanges, Is.EqualTo(new[] { true, false }));
            Assert.That(fixture.player.transform.position, Is.EqualTo(savedPosition));
            Assert.That(Quaternion.Angle(fixture.player.transform.rotation, savedRotation), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(fixture.playerCamera.transform.position, cameraPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(fixture.playerCamera.transform.rotation, cameraRotation), Is.LessThan(0.001f));
            Assert.That(fixture.playerSafety.LastSafePosition, Is.EqualTo(safePosition));
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsTabletInteractionActive, Is.False);
            Assert.That(fixture.playerCamera.enabled, Is.True);
            Assert.That(fixture.normalCamera.enabled, Is.False);
            Assert.That(fixture.playerSafety.enabled, Is.True);
        }
        finally { fixture.Destroy(); }
    }

    [Test]
    public void TabletEntryFailure_ReleasesTabletBlockAndResumesWalk()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            var bootstrap = fixture.bootstrapObject.GetComponent<NekolposGameBootstrap>();
            LogAssert.Expect(LogType.Warning, "[OpenBetaTitle] Tablet Camera Point が未設定です。");
            Assert.That(bootstrap.EnterTabletCameraViewFromWalk(fixture.controller), Is.False);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsTabletInteractionActive, Is.False);
            Assert.That(fixture.playerCamera.enabled, Is.True);
            Assert.That(fixture.playerSafety.enabled, Is.True);
        }
        finally { fixture.Destroy(); }
    }

    [UnityTest]
    public IEnumerator TabletReturn_InterpolatesToWalkCameraWithoutConversationPoseAndKeepsConversationEntry()
    {
        yield return new EnterPlayMode();
        WalkModeFixture fixture = CreateFixture();
        GameObject pivot = new GameObject("Tablet test pivot");
        GameObject tabletPoint = new GameObject("Tablet test target");
        try
        {
            SetField(fixture.playerSafety, "gravity", 0f);
            SetField(fixture.playerSafety, "groundedDownwardSpeed", 0f);
            SetField(fixture.playerSafety, "enableMouseLook", false);
            fixture.playerCamera.transform.SetParent(fixture.player.transform, false);
            fixture.playerCamera.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            fixture.normalCamera.transform.SetParent(pivot.transform, false);
            fixture.normalCamera.transform.localPosition = new Vector3(0.05f, 0.02f, 0f);
            fixture.normalCamera.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
            pivot.transform.SetPositionAndRotation(new Vector3(10f, 2f, 10f), Quaternion.Euler(0f, 70f, 0f));
            Vector3 conversationPosition = pivot.transform.position;
            Quaternion conversationRotation = pivot.transform.rotation;
            tabletPoint.transform.SetPositionAndRotation(new Vector3(2f, 0.5f, 3f), Quaternion.Euler(80f, 0f, 0f));
            var bootstrap = fixture.bootstrapObject.GetComponent<NekolposGameBootstrap>();
            SetField(bootstrap, "startupCameraPivot", pivot.transform);
            SetField(bootstrap, "tabletCameraPoint", tabletPoint.transform);
            SetField(bootstrap, "tabletCameraTransitionSeconds", 0.15f);
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            fixture.playerSafety.SetLookRotation(Quaternion.Euler(-10f, 32f, 0f));
            Vector3 walkPosition = fixture.playerCamera.transform.position;
            Quaternion walkRotation = fixture.playerCamera.transform.rotation;
            Assert.That(bootstrap.EnterTabletCameraViewFromWalk(fixture.controller), Is.True);
            Assert.That(bootstrap.CurrentTabletEntryContext, Is.EqualTo(NekolposGameBootstrap.TabletEntryContext.WalkMode));
            while (bootstrap.IsTabletCameraTransitioning) yield return null;
            Assert.That(bootstrap.ExitTabletCameraView(), Is.True);
            double deadline = EditorApplication.timeSinceStartup + 10d;
            while (bootstrap.IsTabletCameraTransitioning)
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                Assert.That(Vector3.Distance(fixture.normalCamera.transform.position, conversationPosition), Is.GreaterThan(1f), "Conversation pose must not appear during the return.");
                yield return null;
            }
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.playerCamera.enabled, Is.True);
            Assert.That(fixture.normalCamera.enabled, Is.False);
            Assert.That(Vector3.Distance(fixture.playerCamera.transform.position, walkPosition), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(fixture.playerCamera.transform.rotation, walkRotation), Is.LessThan(0.001f));
            Assert.That(pivot.transform.position, Is.EqualTo(conversationPosition));
            Assert.That(Quaternion.Angle(pivot.transform.rotation, conversationRotation), Is.LessThan(0.001f));

            Assert.That(fixture.controller.TryExitWalkMode(), Is.True);
            conversationPosition = pivot.transform.position;
            conversationRotation = pivot.transform.rotation;
            Assert.That(bootstrap.EnterTabletCameraView(), Is.True);
            Assert.That(bootstrap.CurrentTabletEntryContext, Is.EqualTo(NekolposGameBootstrap.TabletEntryContext.Conversation));
            while (bootstrap.IsTabletCameraTransitioning) yield return null;
            Assert.That(bootstrap.ExitTabletCameraView(), Is.True);
            while (bootstrap.IsTabletCameraTransitioning) yield return null;
            Assert.That(fixture.controller.IsWalkMode, Is.False);
            Assert.That(fixture.normalCamera.enabled, Is.True);
            Assert.That(pivot.transform.position, Is.EqualTo(conversationPosition));
            Assert.That(Quaternion.Angle(pivot.transform.rotation, conversationRotation), Is.LessThan(0.001f));
        }
        finally
        {
            fixture.Destroy();
            Object.DestroyImmediate(pivot);
            Object.DestroyImmediate(tabletPoint);
        }
        yield return new ExitPlayMode();
    }

    [TestCase("Desk", CatHomeLocation.Desk)]
    [TestCase("Table", CatHomeLocation.Table)]
    [TestCase("None", CatHomeLocation.Table)]
    [TestCase("Bathroom", CatHomeLocation.Table)]
    [TestCase(null, CatHomeLocation.Table)]
    public void ResolveReturnHomeLocation_UsesDeskOnlyForDesk(string areaId, CatHomeLocation expected)
    {
        Assert.That(PlayerWalkModeController.ResolveReturnHomeLocation(areaId), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveWalkStartDestination_UsesDeskOnlyForDesk()
    {
        GameObject table = new GameObject("PlayerSpawn_Table");
        GameObject desk = new GameObject("PlayerSpawn_Desk");
        try
        {
            Assert.That(PlayerWalkModeController.ResolveWalkStartDestination("Desk", table.transform, desk.transform), Is.EqualTo(desk.transform));
            Assert.That(PlayerWalkModeController.ResolveWalkStartDestination("Table", table.transform, desk.transform), Is.EqualTo(table.transform));
            Assert.That(PlayerWalkModeController.ResolveWalkStartDestination("Desk", table.transform, null), Is.EqualTo(table.transform));
        }
        finally
        {
            Object.DestroyImmediate(table);
            Object.DestroyImmediate(desk);
        }
    }

    [Test]
    public void TryEnterAndExitWalkMode_StartsAtTableRespawnPointAndSwitchesCamerasInputAndUi()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            int changeCount = 0;
            bool? lastWalkModeState = null;
            fixture.controller.WalkModeChanged += enabled =>
            {
                changeCount++;
                lastWalkModeState = enabled;
            };

            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(changeCount, Is.EqualTo(1));
            Assert.That(lastWalkModeState, Is.True);
            Assert.That(fixture.playerSafety.enabled, Is.True);
            Assert.That(fixture.playerCamera.enabled, Is.True);
            Assert.That(fixture.playerListener.enabled, Is.True);
            Assert.That(fixture.normalCamera.enabled, Is.False);
            Assert.That(fixture.normalListener.enabled, Is.False);
            Assert.That(fixture.player.transform.position, Is.EqualTo(fixture.walkStartDestination.transform.position));
            Assert.That(fixture.playerSafety.LastSafePosition, Is.EqualTo(fixture.walkStartDestination.transform.position));
            // Batch Modeには表示ウィンドウがなく、Unity側がCursor LockをNoneへ戻す。
            Assert.That(Cursor.lockState, Is.EqualTo(Application.isBatchMode ? CursorLockMode.None : CursorLockMode.Locked));
            Assert.That(Cursor.visible, Is.False);

            Assert.That(fixture.controller.TryExitWalkMode(), Is.True);
            Assert.That(fixture.controller.IsWalkMode, Is.False);
            Assert.That(changeCount, Is.EqualTo(2));
            Assert.That(lastWalkModeState, Is.False);
            Assert.That(fixture.playerSafety.enabled, Is.False);
            Assert.That(fixture.playerCamera.enabled, Is.False);
            Assert.That(fixture.playerListener.enabled, Is.False);
            Assert.That(fixture.normalCamera.enabled, Is.True);
            Assert.That(fixture.normalListener.enabled, Is.True);
            Assert.That(fixture.player.transform.position, Is.EqualTo(fixture.walkStartDestination.transform.position));
        }
        finally
        {
            fixture.Destroy();
        }
    }

    [Test]
    public void TryEnterAndExitWalkMode_RejectsDuplicateRequests()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryExitWalkMode(), Is.False);
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.False);
            Assert.That(fixture.controller.TryExitWalkMode(), Is.True);
            Assert.That(fixture.controller.TryExitWalkMode(), Is.False);
        }
        finally
        {
            fixture.Destroy();
        }
    }

    [Test]
    public void InteractionMenu_KeepsWalkModeButSuspendsOnlyPlayerMovement()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            Assert.That(fixture.controller.TrySetInteractionMenuOpen(true), Is.True);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsInteractionMenuOpen, Is.True);
            Assert.That(fixture.playerSafety.enabled, Is.False);

            Assert.That(fixture.controller.TrySetInteractionMenuOpen(false), Is.True);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsInteractionMenuOpen, Is.False);
            Assert.That(fixture.playerSafety.enabled, Is.True);
        }
        finally
        {
            fixture.Destroy();
        }
    }

    [Test]
    public void WalkExit_ImmediatelyClearsCurrentInteractionAndPrompt()
    {
        WalkModeFixture fixture = CreateFixture();
        GameObject target = new GameObject("Walk exit cable");
        try
        {
            var guide = fixture.player.AddComponent<ControlGuideHUD>();
            var prompt = fixture.player.AddComponent<PlayerInteractionPrompt>();
            var interactor = fixture.player.AddComponent<PlayerInteractor>();
            SetField(interactor, "prompt", prompt);
            SetField(interactor, "walkModeController", fixture.controller);
            typeof(PlayerInteractor).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interactor, null);
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);
            var cable = target.AddComponent<CableTravelInteractable>();
            typeof(PlayerInteractor).GetMethod("SetCurrentInteractable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interactor, new object[] { cable });
            Assert.That(prompt.IsVisible, Is.True);
            Assert.That(fixture.controller.TryExitWalkMode(), Is.True);
            Assert.That(prompt.IsVisible, Is.False);
            Assert.That(interactor.CurrentInteractable, Is.Null);
            Assert.That(guide.InteractionLabel, Is.EqualTo("インタラクト"));
        }
        finally
        {
            fixture.Destroy();
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void PauseMenu_KeepsWalkModeButSuspendsMouseLookAndReleasesCursor()
    {
        WalkModeFixture fixture = CreateFixture();
        try
        {
            Assert.That(fixture.controller.TryEnterWalkMode(), Is.True);

            fixture.controller.SetPauseMenuOpen(true);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsPauseMenuOpen, Is.True);
            Assert.That(fixture.playerSafety.enabled, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);

            fixture.controller.SetPauseMenuOpen(false);
            Assert.That(fixture.controller.IsWalkMode, Is.True);
            Assert.That(fixture.controller.IsPauseMenuOpen, Is.False);
            Assert.That(fixture.playerSafety.enabled, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(Application.isBatchMode ? CursorLockMode.None : CursorLockMode.Locked));
            Assert.That(Cursor.visible, Is.False);
        }
        finally
        {
            fixture.Destroy();
        }
    }

    [Test]
    public void DialogueManager_WalkActionUsesWalkModeControllerBeforeOtherSpecialActions()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/System/DialogueManager.cs");
        int walkAction = source.IndexOf("TryStartWalkModeAction(actionId)", System.StringComparison.Ordinal);
        int timelineAction = source.IndexOf("TryExecuteConversationTimelineAction(actionId)", System.StringComparison.Ordinal);

        Assert.That(walkAction, Is.GreaterThanOrEqualTo(0));
        Assert.That(timelineAction, Is.GreaterThan(walkAction));
        StringAssert.Contains("string.Equals(actionId?.Trim(), \"walk\", global::System.StringComparison.OrdinalIgnoreCase)", source);
    }

    [Test]
    public void MainScene_WalkModeControllerHasAllRequiredInspectorReferences()
    {
        const string scenePath = "Assets/Scenes/MainScene.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool closeOnExit = !scene.IsValid() || !scene.isLoaded;
        if (closeOnExit)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        try
        {
            PlayerWalkModeController controller = null;
            foreach (PlayerWalkModeController candidate in Resources.FindObjectsOfTypeAll<PlayerWalkModeController>())
            {
                if (candidate != null && candidate.gameObject.scene == scene)
                {
                    controller = candidate;
                    break;
                }
            }

            Assert.That(controller, Is.Not.Null);
            var serialized = new SerializedObject(controller);
            string[] requiredReferences =
            {
                "playerSafetyMovementController",
                "playerAreaTracker",
                "gameBootstrap",
                "chatUI",
                "walkStartDestination",
                "walkStartDeskDestination",
                "playerCamera",
                "normalConversationCamera"
            };
            foreach (string propertyName in requiredReferences)
            {
                SerializedProperty property = serialized.FindProperty(propertyName);
                Assert.That(property, Is.Not.Null, $"Missing serialized property: {propertyName}");
                Assert.That(property.objectReferenceValue, Is.Not.Null, $"MainScene reference is missing: {propertyName}");
            }
        }
        finally
        {
            if (closeOnExit && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    [Test]
    public void WalkModeController_ResolvesExistingAreaRespawnPointsWhenNoInspectorReferenceIsSaved()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/PlayerWalkModeController.cs");

        StringAssert.Contains("transitionPoints?.Find(\"PlayerSpawn_Table\")", source);
        StringAssert.Contains("transitionPoints?.Find(\"PlayerSpawn_Desk\")", source);
    }

    [Test]
    public void NormalConversationState_ReleasesCursorForUiInput()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/Player/PlayerWalkModeController.cs");

        StringAssert.Contains("Cursor.lockState = CursorLockMode.None;", source);
        StringAssert.Contains("Cursor.visible = true;", source);
    }

    private static WalkModeFixture CreateFixture()
    {
        GameObject player = new GameObject("Walk mode player", typeof(CharacterController), typeof(PlayerSafetyMovementController), typeof(PlayerAreaTracker));
        CharacterController characterController = player.GetComponent<CharacterController>();
        characterController.height = 0.05f;
        characterController.radius = 0.01f;
        characterController.center = new Vector3(0f, 0.025f, 0f);
        characterController.stepOffset = 0.01f;

        GameObject bootstrapObject = new GameObject("Walk mode bootstrap");
        NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
        GameObject chatObject = new GameObject("Walk mode chat");
        ChatUIController chatUi = chatObject.AddComponent<ChatUIController>();
        GameObject walkStartDestination = new GameObject("PlayerSpawn_Table");
        walkStartDestination.transform.position = new Vector3(1f, 0.02f, 2f);
        GameObject playerCameraObject = new GameObject("Walk mode player camera", typeof(Camera), typeof(AudioListener));
        GameObject normalCameraObject = new GameObject("Walk mode normal camera", typeof(Camera), typeof(AudioListener));

        PlayerWalkModeController controller = player.AddComponent<PlayerWalkModeController>();
        SetField(controller, "playerSafetyMovementController", player.GetComponent<PlayerSafetyMovementController>());
        SetField(controller, "playerAreaTracker", player.GetComponent<PlayerAreaTracker>());
        SetField(controller, "gameBootstrap", bootstrap);
        SetField(controller, "chatUI", chatUi);
        SetField(controller, "walkStartDestination", walkStartDestination.transform);
        SetField(controller, "playerCamera", playerCameraObject.GetComponent<Camera>());
        SetField(controller, "normalConversationCamera", normalCameraObject.GetComponent<Camera>());
        controller.enabled = false;
        controller.enabled = true;

        return new WalkModeFixture(
            player,
            bootstrapObject,
            chatObject,
            walkStartDestination,
            playerCameraObject,
            normalCameraObject,
            controller,
            player.GetComponent<PlayerSafetyMovementController>());
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private sealed class WalkModeFixture
    {
        public WalkModeFixture(
            GameObject player,
            GameObject bootstrapObject,
            GameObject chatObject,
            GameObject walkStartDestination,
            GameObject playerCameraObject,
            GameObject normalCameraObject,
            PlayerWalkModeController controller,
            PlayerSafetyMovementController playerSafety)
        {
            this.player = player;
            this.bootstrapObject = bootstrapObject;
            this.chatObject = chatObject;
            this.walkStartDestination = walkStartDestination;
            this.playerCameraObject = playerCameraObject;
            this.normalCameraObject = normalCameraObject;
            this.controller = controller;
            this.playerSafety = playerSafety;
        }

        public readonly GameObject player;
        public readonly GameObject bootstrapObject;
        public readonly GameObject chatObject;
        public readonly GameObject walkStartDestination;
        public readonly GameObject playerCameraObject;
        public readonly GameObject normalCameraObject;
        public readonly PlayerWalkModeController controller;
        public readonly PlayerSafetyMovementController playerSafety;
        public Camera playerCamera => playerCameraObject.GetComponent<Camera>();
        public AudioListener playerListener => playerCameraObject.GetComponent<AudioListener>();
        public Camera normalCamera => normalCameraObject.GetComponent<Camera>();
        public AudioListener normalListener => normalCameraObject.GetComponent<AudioListener>();

        public void Destroy()
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(bootstrapObject);
            Object.DestroyImmediate(chatObject);
            Object.DestroyImmediate(walkStartDestination);
            Object.DestroyImmediate(playerCameraObject);
            Object.DestroyImmediate(normalCameraObject);
        }
    }
}
