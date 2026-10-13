using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public sealed class PlayerCameraWallCollisionControllerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AdditionalDoor_OnlyConfiguredGeometryPushesCamera(bool includeDoor)
    {
        var root = new GameObject("Door camera clearance test");
        var door = new GameObject("Door surface", typeof(BoxCollider));
        var furniture = new GameObject("Unrelated furniture", typeof(BoxCollider));
        try
        {
            Vector3 origin = new Vector3(1000, 1000, 1000);
            root.transform.position = origin;
            var camera = root.AddComponent<Camera>();
            var controller = root.AddComponent<PlayerCameraWallCollisionController>();
            door.transform.position = origin + Vector3.forward * .02f;
            door.GetComponent<BoxCollider>().size = new Vector3(.1f, .1f, .01f);
            furniture.transform.position = origin + Vector3.right * .02f;
            furniture.GetComponent<BoxCollider>().size = new Vector3(.01f, .1f, .1f);
            var data = new UnityEditor.SerializedObject(controller);
            var roots = data.FindProperty("additionalWallRoots");
            roots.arraySize = includeDoor ? 1 : 0;
            if (includeDoor) roots.GetArrayElementAtIndex(0).objectReferenceValue = door.transform;
            data.ApplyModifiedPropertiesWithoutUndo();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PlayerCameraWallCollisionController).GetMethod("RebuildWallColliders", flags).Invoke(controller, null);
            Physics.SyncTransforms();
            var correction = (Vector3)typeof(PlayerCameraWallCollisionController).GetMethod("CalculateWorldCorrection", flags).Invoke(controller, new object[] { origin });
            Assert.That(correction.x, Is.EqualTo(0).Within(.0001f), "Unregistered furniture must be ignored.");
            if (includeDoor) Assert.That(correction.z, Is.LessThan(0));
            else Assert.That(correction, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(door);
            Object.DestroyImmediate(furniture);
        }
    }

    [TestCase(0.016f, 0.016f, 0f)]
    [TestCase(0.01f, 0.016f, 0.006f)]
    [TestCase(0.03f, 0.016f, 0f)]
    public void CalculateRequiredPushDistance_OnlyPushesWhenInsideConfiguredClearance(
        float hitDistance,
        float clearance,
        float expected)
    {
        Assert.That(
            PlayerCameraWallCollisionController.CalculateRequiredPushDistance(hitDistance, clearance),
            Is.EqualTo(expected).Within(0.00001f));
    }
}
