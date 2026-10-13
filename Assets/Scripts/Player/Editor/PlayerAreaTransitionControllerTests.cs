using System.Reflection;
using Nekolpos.ActionSystem;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerAreaTransitionControllerTests
{
    [Test]
    public void TitleCleanup_CancelsTransitionAndFadeWithoutFiringCompletionEvents()
    {
        GameObject player = CreatePlayer();
        GameObject overlay = new GameObject("CleanupFade", typeof(CanvasGroup));
        try
        {
            var transition = player.GetComponent<PlayerAreaTransitionController>();
            var safety = player.GetComponent<PlayerSafetyMovementController>();
            SetPrivateField(transition, "isTransitioning", true);
            safety.SuspendSafetyRecovery();
            var fade = overlay.AddComponent<FadeController>();
            var group = overlay.GetComponent<CanvasGroup>();
            SetPrivateField(fade, "fadeGroup", group);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            int completions = 0;
            fade.BecameTransparent += () => completions++;
            transition.ResetForTitleReload();
            fade.ResetForTitleReload();
            transition.ResetForTitleReload();
            fade.ResetForTitleReload();
            Assert.That(transition.IsTransitioning, Is.False);
            Assert.That(safety.SafetyRecoverySuspended, Is.False);
            Assert.That(safety.enabled, Is.False);
            Assert.That(group.alpha, Is.Zero);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(completions, Is.Zero);
        }
        finally { DestroyFixtures(player, overlay); }
    }

    [Test]
    public void TeleportImmediately_MovesToDestinationUpdatesSafePositionAndRefreshesArea()
    {
        GameObject player = CreatePlayer();
        GameObject destination = new GameObject("Transition destination");
        GameObject area = CreateArea("Desk", new Vector3(4f, 0f, 0f));

        try
        {
            destination.transform.position = new Vector3(4f, 0f, 0f);
            PlayerAreaTransitionController transition = player.GetComponent<PlayerAreaTransitionController>();
            PlayerSafetyMovementController safetyMovement = player.GetComponent<PlayerSafetyMovementController>();
            PlayerAreaTracker areaTracker = player.GetComponent<PlayerAreaTracker>();

            Assert.That(transition.TeleportImmediately(destination.transform, "Desk"), Is.True);
            Assert.That(player.transform.position, Is.EqualTo(destination.transform.position));
            Assert.That(safetyMovement.LastSafePosition, Is.EqualTo(destination.transform.position));
            Assert.That(safetyMovement.HasLastSafePosition, Is.True);
            Assert.That(safetyMovement.SafetyRecoverySuspended, Is.False);
            Assert.That(safetyMovement.enabled, Is.True);
            Assert.That(areaTracker.CurrentAreaId, Is.EqualTo("Desk"));
            Assert.That(transition.IsTransitioning, Is.False);
        }
        finally
        {
            DestroyFixtures(player, destination, area);
        }
    }

    [Test]
    public void TeleportImmediately_PreservesExternallySuspendedSafetyAndDisabledMovement()
    {
        GameObject player = CreatePlayer();
        try
        {
            PlayerAreaTransitionController transition = player.GetComponent<PlayerAreaTransitionController>();
            PlayerSafetyMovementController safetyMovement = player.GetComponent<PlayerSafetyMovementController>();
            safetyMovement.SuspendSafetyRecovery();
            safetyMovement.enabled = false;

            Assert.That(transition.TeleportImmediately(new Vector3(2f, 0f, 0f)), Is.True);
            Assert.That(safetyMovement.SafetyRecoverySuspended, Is.True);
            Assert.That(safetyMovement.enabled, Is.False);
        }
        finally
        {
            DestroyFixtures(player);
        }
    }

    [Test]
    public void TeleportImmediately_RejectsReentrantRequest()
    {
        GameObject player = CreatePlayer();
        try
        {
            PlayerAreaTransitionController transition = player.GetComponent<PlayerAreaTransitionController>();
            SetPrivateField(transition, "isTransitioning", true);

            Assert.That(transition.TeleportImmediately(new Vector3(3f, 0f, 0f)), Is.False);
            Assert.That(player.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(transition.IsTransitioning, Is.True);
        }
        finally
        {
            DestroyFixtures(player);
        }
    }

    [Test]
    public void TransitionToAsync_DoesNotTeleportBeforeFadeBecomesOpaque()
    {
        GameObject player = CreatePlayer();
        GameObject fadeObject = new GameObject("Uninitialized fade", typeof(FadeController));
        try
        {
            PlayerAreaTransitionController transition = player.GetComponent<PlayerAreaTransitionController>();
            SetPrivateField(transition, "fadeController", fadeObject.GetComponent<FadeController>());

            bool result = transition.TransitionToAsync(new Vector3(5f, 0f, 0f)).GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            Assert.That(player.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(transition.IsTransitioning, Is.False);
        }
        finally
        {
            DestroyFixtures(player, fadeObject);
        }
    }

    private static GameObject CreatePlayer()
    {
        GameObject player = new GameObject(
            "Player area transition test",
            typeof(CharacterController),
            typeof(PlayerSafetyMovementController),
            typeof(PlayerAreaTracker),
            typeof(PlayerAreaTransitionController));
        CharacterController characterController = player.GetComponent<CharacterController>();
        characterController.height = 0.05f;
        characterController.radius = 0.01f;
        characterController.center = new Vector3(0f, 0.025f, 0f);
        return player;
    }

    private static GameObject CreateArea(string areaId, Vector3 center)
    {
        GameObject areaObject = new GameObject("Transition area", typeof(BoxCollider), typeof(PlayerAreaVolume));
        BoxCollider collider = areaObject.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.center = new Vector3(0f, 0.025f, 0f);
        collider.size = new Vector3(2f, 0.1f, 2f);
        areaObject.transform.position = center;

        SetPrivateField(areaObject.GetComponent<PlayerAreaVolume>(), "areaId", areaId);
        return areaObject;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private static void DestroyFixtures(params GameObject[] fixtures)
    {
        foreach (GameObject fixture in fixtures)
        {
            Object.DestroyImmediate(fixture);
        }
    }
}
