using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;
using UnityEngine.SceneManagement;

public sealed class PlayerSafetyMovementTests
{
    [Test]
    public void ChabudaiSupportsRoundTopButNotOldBoxCornersAndBlocksLegs()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        var fixture = new GameObject("Chabudai collision fixture");
        try
        {
            GameObject table = null, proxy = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "つくえ ダイニング-011 W90D90H36") table = t.gameObject;
                    if (t.name == "TableSurfaceProxy") proxy = t.gameObject;
                }
            Assert.That(table, Is.Not.Null);
            Assert.That(proxy.GetComponent<BoxCollider>(), Is.Null);
            var top = proxy.GetComponent<MeshCollider>();
            Assert.That(top.convex, Is.False);
            Assert.That(top.isTrigger, Is.False);
            Vector3 offset = new Vector3(100, 0, 100) - table.transform.position;
            foreach (var source in new[] { table.GetComponent<MeshCollider>(), top })
            {
                var copy = new GameObject("Static top").AddComponent<MeshCollider>();
                copy.transform.SetParent(fixture.transform);
                copy.transform.position = source.transform.position + offset;
                copy.sharedMesh = source.sharedMesh;
                copy.convex = false;
            }
            var legs = table.GetComponentsInChildren<BoxCollider>();
            Assert.That(legs, Has.Length.EqualTo(4));
            foreach (var source in legs)
            {
                var copy = new GameObject(source.name).AddComponent<BoxCollider>();
                copy.transform.SetParent(fixture.transform);
                copy.transform.position = source.transform.position + offset;
                copy.center = source.center;
                copy.size = source.size;
            }
            var floor = new GameObject("Floor").AddComponent<BoxCollider>();
            floor.transform.SetParent(fixture.transform);
            floor.transform.position = new Vector3(100, -.01f, 100);
            floor.size = new Vector3(2, .02f, 2);
            var player = new GameObject("Character", typeof(CharacterController));
            player.transform.SetParent(fixture.transform);
            var cc = player.GetComponent<CharacterController>();
            cc.height = .1f; cc.radius = .01f; cc.center = new Vector3(0, .05f, 0);
            cc.skinWidth = .0005f; cc.stepOffset = .005f; cc.minMoveDistance = 0;
            Physics.SyncTransforms();
            foreach (var point in new[] { Vector2.zero, new Vector2(.4f, 0), new Vector2(0, -.4f), new Vector2(.25f, .25f),
                new Vector2(.42f, .42f), new Vector2(-.42f, .42f), new Vector2(.42f, -.42f), new Vector2(-.42f, -.42f) })
            {
                cc.enabled = false;
                player.transform.position = new Vector3(100 + point.x, .4f, 100 + point.y);
                cc.enabled = true;
                Physics.SyncTransforms();
                for (int i = 0; i < 120; i++) cc.Move(Vector3.down * .005f);
                if (point.magnitude < .45f)
                    Assert.That(player.transform.position.y, Is.EqualTo(.36f).Within(.003f), "Top " + point);
                else
                    Assert.That(player.transform.position.y, Is.LessThan(.01f), "Old corner must fall to floor " + point);
            }
            foreach (var leg in legs)
            {
                Vector3 center = leg.transform.position + offset;
                cc.enabled = false;
                player.transform.position = new Vector3(center.x - .08f, .002f, center.z);
                cc.enabled = true;
                Physics.SyncTransforms();
                for (int i = 0; i < 40; i++) cc.Move(new Vector3(.005f, -.001f, 0));
                Assert.That(player.transform.position.x, Is.LessThan(center.x - .02f), leg.name + " must block CharacterController");
            }
        }
        finally { Object.DestroyImmediate(fixture); EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void WalkableNormal_AllowsFlatAndConfiguredGentleSlope()
    {
        Assert.That(PlayerSafetyMovementController.IsWalkableSurfaceNormal(Vector3.up, 55f), Is.True);
        Assert.That(PlayerSafetyMovementController.IsWalkableSurfaceNormal(new Vector3(0f, 0.8f, 0.6f), 55f), Is.True);
    }

    [Test]
    public void WalkableNormal_RejectsFurnitureSideAndSteepSlope()
    {
        Assert.That(PlayerSafetyMovementController.IsWalkableSurfaceNormal(Vector3.right, 55f), Is.False);
        Assert.That(PlayerSafetyMovementController.IsWalkableSurfaceNormal(new Vector3(0f, 0.4f, 0.9165f), 55f), Is.False);
    }

    [Test]
    public void SafetyApis_KeepExplicitSafePositionAndSupportTemporaryOverrides()
    {
        GameObject fixture = new GameObject("Player safety fixture", typeof(CharacterController), typeof(PlayerSafetyMovementController));
        try
        {
            PlayerSafetyMovementController controller = fixture.GetComponent<PlayerSafetyMovementController>();
            Vector3 expected = new Vector3(1f, 2f, 3f);

            controller.SetLastSafePosition(expected);
            controller.SetEdgeFallPreventionEnabled(false);
            controller.SuspendSafetyRecovery();

            Assert.That(controller.HasLastSafePosition, Is.True);
            Assert.That(controller.LastSafePosition, Is.EqualTo(expected));
            Assert.That(controller.EdgeFallPreventionEnabled, Is.False);
            Assert.That(controller.SafetyRecoverySuspended, Is.True);

            controller.ResumeSafetyRecovery();
            controller.ResetLastSafePosition();

            Assert.That(controller.SafetyRecoverySuspended, Is.False);
            Assert.That(controller.HasLastSafePosition, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(fixture);
        }
    }

    [Test]
    public void TeleportTo_ResetsMovementStateAndCanPromoteDestinationToSafePosition()
    {
        GameObject fixture = new GameObject("Player teleport fixture", typeof(CharacterController), typeof(PlayerSafetyMovementController));
        try
        {
            PlayerSafetyMovementController controller = fixture.GetComponent<PlayerSafetyMovementController>();
            Vector3 destination = new Vector3(-2f, 0.5f, 4f);
            FieldInfo verticalVelocity = typeof(PlayerSafetyMovementController).GetField(
                "verticalVelocity",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(verticalVelocity, Is.Not.Null);
            verticalVelocity.SetValue(controller, -1.5f);

            controller.TeleportTo(destination, updateSafePosition: true);

            Assert.That(fixture.transform.position, Is.EqualTo(destination));
            Assert.That(controller.LastSafePosition, Is.EqualTo(destination));
            Assert.That(controller.HasLastSafePosition, Is.True);
            Assert.That((float)verticalVelocity.GetValue(controller), Is.EqualTo(0f));
        }
        finally
        {
            Object.DestroyImmediate(fixture);
        }
    }

    [Test]
    public void WalkableLayers_StartUnconfiguredSoSceneAuthorsChooseTheCorrectLayer()
    {
        GameObject fixture = new GameObject("Player layer fixture", typeof(CharacterController), typeof(PlayerSafetyMovementController));
        try
        {
            FieldInfo field = typeof(PlayerSafetyMovementController).GetField(
                "walkableLayers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(((LayerMask)field.GetValue(fixture.GetComponent<PlayerSafetyMovementController>())).value, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(fixture);
        }
    }

    [Test]
    public void CalculateVerticalVelocity_ClampsAirborneFallToConfiguredMaximum()
    {
        MethodInfo method = GetCalculateVerticalVelocityMethod();

        float result = (float)method.Invoke(null, new object[]
        {
            false,
            -1.9f,
            9.81f,
            0.1f,
            2f,
            0.1f
        });

        Assert.That(result, Is.EqualTo(-2f).Within(0.0001f));
    }

    [Test]
    public void CalculateVerticalVelocity_PreservesGroundedDownwardSpeedWithoutFallClamp()
    {
        MethodInfo method = GetCalculateVerticalVelocityMethod();

        float result = (float)method.Invoke(null, new object[]
        {
            true,
            -3f,
            9.81f,
            0.1f,
            0.01f,
            0.1f
        });

        Assert.That(result, Is.EqualTo(-0.1f).Within(0.0001f));
    }

    [Test]
    public void CalculateJumpVelocity_UsesGravityAndConfiguredHeight()
    {
        float jumpVelocity = PlayerSafetyMovementController.CalculateJumpVelocity(9.81f, 0.02f);

        Assert.That(jumpVelocity, Is.EqualTo(Mathf.Sqrt(2f * 9.81f * 0.02f)).Within(0.0001f));
        Assert.That(PlayerSafetyMovementController.CalculateJumpVelocity(0f, 0.02f), Is.EqualTo(0f));
        Assert.That(PlayerSafetyMovementController.CalculateJumpVelocity(9.81f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void CanStartJump_RequiresEnabledGroundedWalkableState()
    {
        Assert.That(PlayerSafetyMovementController.CanStartJump(true, true, true), Is.True);
        Assert.That(PlayerSafetyMovementController.CanStartJump(true, false, true), Is.False);
        Assert.That(PlayerSafetyMovementController.CanStartJump(true, true, false), Is.False);
        Assert.That(PlayerSafetyMovementController.CanStartJump(false, true, true), Is.False);
    }

    [Test]
    public void CalculateVerticalVelocity_DoesNotClampJumpAscentToMaximumFallSpeed()
    {
        MethodInfo method = GetCalculateVerticalVelocityMethod();

        float result = (float)method.Invoke(null, new object[]
        {
            false,
            0.6f,
            9.81f,
            0.1f,
            0.02f,
            0.01f
        });

        Assert.That(result, Is.GreaterThan(0f));
        Assert.That(result, Is.EqualTo(0.5019f).Within(0.0001f));
    }

    [Test]
    public void SafePositionUpdate_IsBlockedDuringJumpAndResumesAfterLanding()
    {
        Assert.That(PlayerSafetyMovementController.ShouldUpdateLastSafePosition(false, true, 0.626f), Is.False);
        Assert.That(PlayerSafetyMovementController.ShouldUpdateLastSafePosition(false, false, 0f), Is.False);
        Assert.That(PlayerSafetyMovementController.ShouldUpdateLastSafePosition(true, true, 0f), Is.False);
        Assert.That(PlayerSafetyMovementController.ShouldUpdateLastSafePosition(false, true, -0.1f), Is.True);
    }

    [Test]
    public void WalkableMoveDuringJump_AllowsReachableLandingButBlocksVoid()
    {
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, 0.4f, true), Is.True);
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, 0.4f, false), Is.False);
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, -0.1f, true), Is.False);
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(true, -0.1f, false), Is.True);
    }

    [Test]
    public void ControlledStepDown_AllowsOnlyNearbyLowerWalkableLanding()
    {
        Assert.That(PlayerSafetyMovementController.IsControlledStepDownAllowed(true, 0.02f, 0.03f), Is.True);
        Assert.That(PlayerSafetyMovementController.IsControlledStepDownAllowed(false, 0.02f, 0.03f), Is.False);
        Assert.That(PlayerSafetyMovementController.IsControlledStepDownAllowed(true, 0.031f, 0.03f), Is.False);
        Assert.That(PlayerSafetyMovementController.IsControlledStepDownAllowed(true, 0f, 0.03f), Is.False);
        Assert.That(PlayerSafetyMovementController.IsControlledStepDownAllowed(true, -0.01f, 0.03f), Is.False);
    }

    [Test]
    public void WalkableMove_AllowsControlledStepDownButStillBlocksVoidAndLargeDrop()
    {
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, true, -0.1f, false), Is.True);
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, false, -0.1f, false), Is.False);
        Assert.That(PlayerSafetyMovementController.IsWalkableMoveAllowed(false, false, 0.4f, true), Is.True);
    }

    [Test]
    public void MainScene_PlayerSafetyStoresSmallJumpHeight()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity", OpenSceneMode.Additive);
        try
        {
            PlayerSafetyMovementController controller = null;
            foreach (PlayerSafetyMovementController candidate in Resources.FindObjectsOfTypeAll<PlayerSafetyMovementController>())
            {
                if (candidate.gameObject.scene == scene) { controller = candidate; break; }
            }

            Assert.That(controller, Is.Not.Null);
            var serialized = new SerializedObject(controller);
            Assert.That(serialized.FindProperty("jumpHeight").floatValue, Is.EqualTo(0.1f).Within(0.0001f));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    private static MethodInfo GetCalculateVerticalVelocityMethod()
    {
        MethodInfo method = typeof(PlayerSafetyMovementController).GetMethod(
            "CalculateVerticalVelocity",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return method;
    }
}
