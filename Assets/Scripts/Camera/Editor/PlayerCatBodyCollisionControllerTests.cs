using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerCatBodyCollisionControllerTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root = new GameObject("Cat body collision player");
        public readonly GameObject Obstacle = new GameObject("Cat body collision obstacle");
        public readonly CharacterController Character;
        public readonly Camera Camera;
        public readonly BoxCollider Body;
        public readonly PlayerCatBodyCollisionController Guard;

        public Fixture()
        {
            Root.transform.position = new Vector3(1000f, 1000f, 1000f);
            Character = Root.AddComponent<CharacterController>();
            Character.radius = 0.01f;
            Character.height = 0.1f;
            Character.center = new Vector3(0f, 0.025f, 0f);
            Character.skinWidth = 0.001f;
            Character.stepOffset = 0.01f;
            Character.minMoveDistance = 0f;
            var cameraObject = new GameObject("Cat body collision camera");
            cameraObject.transform.SetParent(Root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.044f, 0f);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.nearClipPlane = 0.01f;
            Camera.fieldOfView = 60f;
            Camera.aspect = 16f / 9f;
            Obstacle.transform.position = Root.transform.position + Vector3.forward * 0.3f;
            Body = Obstacle.AddComponent<BoxCollider>();
            Body.size = Vector3.one * 0.08f;
            Guard = Root.AddComponent<PlayerCatBodyCollisionController>();
            SetField(Guard, "playerCamera", Camera);
            SetField(Guard, "bodyColliders", new Collider[] { Body });
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Root);
            UnityEngine.Object.DestroyImmediate(Obstacle);
        }
    }

    [Test]
    public void PitchedPlayer_ProtectsTheUprightCharacterCapsule()
    {
        using var fixture = new Fixture();
        fixture.Root.transform.rotation = Quaternion.Euler(70f, 20f, 0f);
        fixture.Camera.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        Vector3 before = fixture.Root.transform.position;
        Vector3 bodyCenter = fixture.Root.transform.TransformPoint(fixture.Character.center);
        fixture.Obstacle.transform.position = bodyCenter + Vector3.up * 0.04f;
        fixture.Body.size = new Vector3(0.04f, 0.02f, 0.04f);
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(Vector3.Distance(before, fixture.Root.transform.position), Is.GreaterThan(0.001f));
    }

    [Test]
    public void SavedScene_SeparatesBodyBlockingFromInteractionAndInactiveBones()
    {
        const string scenePath = "Assets/Scenes/MainScene.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            GameObject player = Array.Find(scene.GetRootGameObjects(), g => g.name == "Player");
            Assert.That(player, Is.Not.Null);
            Assert.That(LayerMask.LayerToName(player.layer), Is.EqualTo("PlayerBody"));
            var guard = new UnityEditor.SerializedObject(player.GetComponent<PlayerCatBodyCollisionController>());
            Assert.That(guard.FindProperty("bodyColliders").arraySize, Is.EqualTo(44));
            var interactor = new UnityEditor.SerializedObject(player.GetComponent<PlayerInteractor>());
            int bodyLayer = LayerMask.NameToLayer("CatBodyBlocker");
            Assert.That(interactor.FindProperty("interactionLayers").intValue & (1 << bodyLayer), Is.Zero);
            for (int layer = 0; layer < 32; layer++)
                Assert.That(Physics.GetIgnoreLayerCollision(bodyLayer, layer), Is.EqualTo(layer != player.layer));

            foreach (string catName in new[] { "Normal_Cat", "OP_Cat B" })
            {
                GameObject cat = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == catName) cat = candidate.gameObject;
                Assert.That(cat, Is.Not.Null);
                var follower = new UnityEditor.SerializedObject(cat.GetComponent<CatBodyColliderFollower>());
                var bindings = follower.FindProperty("bindings");
                Assert.That(bindings.arraySize, Is.EqualTo(22));
                for (int index = 0; index < bindings.arraySize; index++)
                {
                    var binding = bindings.GetArrayElementAtIndex(index);
                    var bone = (Transform)binding.FindPropertyRelative("bone").objectReferenceValue;
                    var target = (Transform)binding.FindPropertyRelative("colliderTransform").objectReferenceValue;
                    Assert.That(bone, Is.Not.Null);
                    Assert.That(target.parent.name, Is.EqualTo("PlayerBodyBlockers"));
                    Assert.That(target.IsChildOf(bone), Is.False, "非Active骨の子に戻すと侵入防止Colliderが無効になる。");
                    Assert.That(target.gameObject.activeSelf && target.parent.gameObject.activeSelf, Is.True);
                    Collider collider = target.GetComponent<Collider>();
                    Assert.That(collider.isTrigger, Is.False);
                    Assert.That(collider.attachedRigidbody, Is.Null);
                    Assert.That(collider is CapsuleCollider || collider is BoxCollider || collider is SphereCollider, Is.True);
                    Assert.That(target.gameObject.layer, Is.EqualTo(bodyLayer));
                }
            }
        }
        finally
        {
            if (openedHere) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void Follower_TracksInactiveBoneWithoutChangingItsActiveState()
    {
        var root = new GameObject("Cat body follower fixture");
        try
        {
            var bone = new GameObject("Inactive body bone").transform;
            bone.SetParent(root.transform, false);
            bone.gameObject.SetActive(false);
            var target = new GameObject("Active body blocker").transform;
            target.SetParent(root.transform, false);
            var follower = root.AddComponent<CatBodyColliderFollower>();
            SetField(follower, "bindings", new[] { new CatBodyColliderFollower.Binding { bone = bone, colliderTransform = target } });
            bone.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(20f, 30f, 40f));
            bone.localScale = Vector3.one * 1.2f;
            follower.SyncToBones();
            Assert.That(bone.gameObject.activeSelf, Is.False);
            Assert.That(target.gameObject.activeInHierarchy, Is.True);
            Assert.That(Vector3.Distance(target.position, bone.position), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(target.rotation, bone.rotation), Is.LessThan(0.001f));
            Assert.That(target.lossyScale.x, Is.EqualTo(bone.lossyScale.x).Within(0.00001f));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void CameraRadius_CoversNearPlaneCornersAndAspectChanges()
    {
        using var fixture = new Fixture();
        float radius = PlayerCatBodyCollisionController.CalculateCameraRadius(fixture.Camera);
        Assert.That(radius, Is.GreaterThan(fixture.Character.radius));
        Assert.That(radius, Is.EqualTo(0.01545f).Within(0.0001f));
        fixture.Camera.aspect = 3f;
        Assert.That(PlayerCatBodyCollisionController.CalculateCameraRadius(fixture.Camera), Is.GreaterThan(radius));
    }

    [Test]
    public void BodyMovingOntoStationaryPlayer_ResolvesBothBodyAndCamera()
    {
        using var fixture = new Fixture();
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Vector3 before = fixture.Root.transform.position;
        fixture.Obstacle.transform.position = before + new Vector3(0f, 0.04f, 0.015f);
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(Vector3.Distance(before, fixture.Root.transform.position), Is.GreaterThan(0.01f));
        AssertCameraClear(fixture);
    }

    [Test]
    public void CameraExtendingBeyondCharacter_IsPreventedFromEnteringBody()
    {
        using var fixture = new Fixture();
        fixture.Camera.transform.localPosition += Vector3.forward * 0.06f;
        fixture.Obstacle.transform.position = fixture.Root.transform.position + new Vector3(0f, 0.044f, 0.08f);
        fixture.Body.size = new Vector3(0.2f, 0.2f, 0.04f);
        Vector3 before = fixture.Root.transform.position;
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(fixture.Root.transform.position.z, Is.LessThan(before.z - 0.01f));
        AssertCameraClear(fixture);
    }

    [Test]
    public void DistantBody_LeavesPlayerAndCameraUnchanged()
    {
        using var fixture = new Fixture();
        Vector3 before = fixture.Root.transform.position;
        Vector3 cameraBefore = fixture.Camera.transform.localPosition;
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(fixture.Root.transform.position, Is.EqualTo(before));
        Assert.That(fixture.Camera.transform.localPosition, Is.EqualTo(cameraBefore));
    }

    [Test]
    public void InactiveCameraAndTriggerBody_DoNotMovePlayer()
    {
        using var fixture = new Fixture();
        Vector3 before = fixture.Root.transform.position;
        fixture.Obstacle.transform.position = before;
        fixture.Camera.enabled = false;
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(fixture.Root.transform.position, Is.EqualTo(before));
        fixture.Camera.enabled = true;
        fixture.Body.isTrigger = true;
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(fixture.Root.transform.position, Is.EqualTo(before));
    }

    [Test]
    public void BoneRotation_UsesCurrentObstaclePose()
    {
        using var fixture = new Fixture();
        Vector3 before = fixture.Root.transform.position;
        fixture.Body.size = new Vector3(0.25f, 0.12f, 0.02f);
        fixture.Obstacle.transform.position = before + new Vector3(0f, 0.025f, 0.06f);
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(fixture.Root.transform.position, Is.EqualTo(before));
        fixture.Obstacle.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
        Assert.That(Vector3.Distance(before, fixture.Root.transform.position), Is.GreaterThan(0.01f));
        AssertCameraClear(fixture);
    }

    [Test]
    public void BodyPressingPlayerAgainstWall_RestoresStillClearHistoryPosition()
    {
        using var fixture = new Fixture();
        var wall = new GameObject("Cat body collision wall");
        try
        {
            Vector3 origin = fixture.Root.transform.position;
            wall.transform.position = origin + Vector3.forward * 0.10f;
            var wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(0.8f, 0.8f, 0.02f);
            Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
            fixture.Character.enabled = false;
            fixture.Root.transform.position = origin + Vector3.forward * 0.13f;
            fixture.Character.enabled = true;
            Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
            fixture.Obstacle.transform.position = origin + new Vector3(0f, 0.025f, 0.17f);
            fixture.Body.size = new Vector3(0.8f, 0.8f, 0.08f);
            Assert.That(fixture.Guard.ResolveBodyOverlap(), Is.True);
            Assert.That(fixture.Root.transform.position.z, Is.LessThan(origin.z + 0.08f));
            Assert.That(fixture.Guard.HasUnresolvedOverlap, Is.False);
            AssertCameraClear(fixture);
        }
        finally { UnityEngine.Object.DestroyImmediate(wall); }
    }

    private static void AssertCameraClear(Fixture fixture)
    {
        Vector3 closest = fixture.Body.ClosestPoint(fixture.Camera.transform.position);
        Assert.That(Vector3.Distance(closest, fixture.Camera.transform.position),
            Is.GreaterThanOrEqualTo(PlayerCatBodyCollisionController.CalculateCameraRadius(fixture.Camera) - 0.0003f));
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
}
