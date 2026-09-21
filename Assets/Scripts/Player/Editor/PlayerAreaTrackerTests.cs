using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerAreaTrackerTests
{
    [Test]
    public void RefreshAreaNow_UsesPriorityResolveOrderAndAreaIdForDeterministicOverlapResolution()
    {
        GameObject player = CreatePlayer();
        GameObject lowPriority = CreateVolume("Desk", priority: 1, resolveOrder: 100);
        GameObject highPriority = CreateVolume("Bed", priority: 2, resolveOrder: 0);
        GameObject highResolveOrder = CreateVolume("Table", priority: 2, resolveOrder: 1);
        GameObject alphabeticalTieBreaker = CreateVolume("Bathroom", priority: 2, resolveOrder: 1);

        try
        {
            PlayerAreaTracker tracker = player.GetComponent<PlayerAreaTracker>();
            tracker.RefreshAreaNow();

            Assert.That(tracker.CurrentAreaId, Is.EqualTo("Bathroom"));
            Assert.That(tracker.IsInArea("Bathroom"), Is.True);
            Assert.That(tracker.CurrentCandidateAreas.Count, Is.EqualTo(4));
        }
        finally
        {
            DestroyFixtures(player, lowPriority, highPriority, highResolveOrder, alphabeticalTieBreaker);
        }
    }

    [Test]
    public void RefreshAreaNow_ReportsNoneOutsideVolumesAndDoesNotRepeatIdenticalAreaEvents()
    {
        GameObject player = CreatePlayer();
        GameObject desk = CreateVolume("Desk", priority: 0, resolveOrder: 0);

        try
        {
            PlayerAreaTracker tracker = player.GetComponent<PlayerAreaTracker>();
            int eventCount = 0;
            string previousArea = null;
            string currentArea = null;
            tracker.AreaChanged += (previous, current) =>
            {
                eventCount++;
                previousArea = previous;
                currentArea = current;
            };

            tracker.RefreshAreaNow();
            tracker.RefreshAreaNow();

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(previousArea, Is.EqualTo(PlayerAreaTracker.NoneAreaId));
            Assert.That(currentArea, Is.EqualTo("Desk"));

            player.transform.position = new Vector3(10f, 0f, 0f);
            tracker.RefreshAreaNow();

            Assert.That(tracker.CurrentAreaId, Is.EqualTo(PlayerAreaTracker.NoneAreaId));
            Assert.That(tracker.HasCurrentArea, Is.False);
            Assert.That(tracker.IsInArea("Desk"), Is.False);
            Assert.That(eventCount, Is.EqualTo(2));
        }
        finally
        {
            DestroyFixtures(player, desk);
        }
    }

    private static GameObject CreatePlayer()
    {
        GameObject player = new GameObject("Player area tracker test", typeof(CharacterController), typeof(PlayerAreaTracker));
        CharacterController characterController = player.GetComponent<CharacterController>();
        characterController.height = 0.05f;
        characterController.radius = 0.01f;
        characterController.center = new Vector3(0f, 0.025f, 0f);
        return player;
    }

    private static GameObject CreateVolume(string areaId, int priority, int resolveOrder)
    {
        GameObject volumeObject = new GameObject("Area volume test", typeof(BoxCollider), typeof(PlayerAreaVolume));
        BoxCollider collider = volumeObject.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.center = new Vector3(0f, 0.025f, 0f);
        collider.size = new Vector3(2f, 0.1f, 2f);

        PlayerAreaVolume volume = volumeObject.GetComponent<PlayerAreaVolume>();
        SetPrivateField(volume, "areaId", areaId);
        SetPrivateField(volume, "priority", priority);
        SetPrivateField(volume, "resolveOrder", resolveOrder);
        return volumeObject;
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
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }
}
