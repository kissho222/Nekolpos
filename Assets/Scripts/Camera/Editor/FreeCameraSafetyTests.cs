using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public sealed class PlayerSafetyMovementTests
{
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

            controller.TeleportTo(destination, updateSafePosition: true);

            Assert.That(fixture.transform.position, Is.EqualTo(destination));
            Assert.That(controller.LastSafePosition, Is.EqualTo(destination));
            Assert.That(controller.HasLastSafePosition, Is.True);
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
}
