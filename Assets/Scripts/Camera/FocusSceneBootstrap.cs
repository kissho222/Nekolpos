using UnityEngine;

namespace Nekolpos.CameraSystem
{
    public static class FocusSceneBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureCameraController();
            EnsureTimelineReceiver();
            EnsureOpenBetaCatTargets();
        }

        private static void EnsureCameraController()
        {
            if (Object.FindFirstObjectByType<FocusCameraController>() != null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            mainCamera.gameObject.AddComponent<FocusCameraController>();
        }

        private static void EnsureTimelineReceiver()
        {
            if (Object.FindFirstObjectByType<FocusTimelineSignalReceiver>() != null)
            {
                return;
            }

            GameObject receiverObject = new GameObject("FocusTimelineSignalReceiver");
            receiverObject.AddComponent<FocusTimelineSignalReceiver>();
        }

        private static void EnsureOpenBetaCatTargets()
        {
            Transform catRoot = FindTransformByName("Cat_Simple");
            if (catRoot == null)
            {
                catRoot = FindTransformByName("Arm_Cat");
            }

            if (catRoot == null)
            {
                return;
            }

            Transform head = FindDescendant(catRoot, "head");
            Transform leftEye = FindDescendant(catRoot, "eye.L");
            Transform rightEye = FindDescendant(catRoot, "eye.R");
            Transform nose = FindDescendant(catRoot, "mouth") ?? head;
            Transform paw = FindDescendant(catRoot, "foot_f.L") ?? FindDescendant(catRoot, "claw_f.L");
            Transform tail = FindDescendant(catRoot, "tail_03") ?? FindDescendant(catRoot, "tail_02") ?? FindDescendant(catRoot, "tail_01");

            Transform eyesParent = head != null ? head : leftEye != null ? leftEye.parent : catRoot;
            Vector3 eyeCenter = ResolveEyeCenter(eyesParent, leftEye, rightEye);
            EnsureTarget(eyesParent, "FocusCatEyes", "猫又の目", "FocusCatEyes", eyeCenter, 100, 0.18f);
            EnsureTarget(nose, "FocusCatNose", "猫又の鼻", "FocusCatNose", Vector3.zero, 90, 0.16f);
            EnsureTarget(paw, "FocusCatPaw", "猫又の前足", "FocusCatPaw", Vector3.zero, 80, 0.22f);
            EnsureTarget(tail, "FocusCatTail", "猫又のしっぽ", "FocusCatTail", Vector3.zero, 70, 0.24f);
        }

        private static Vector3 ResolveEyeCenter(Transform parent, Transform leftEye, Transform rightEye)
        {
            if (parent == null)
            {
                return Vector3.zero;
            }

            if (leftEye != null && rightEye != null)
            {
                return parent.InverseTransformPoint((leftEye.position + rightEye.position) * 0.5f);
            }

            if (leftEye != null)
            {
                return parent.InverseTransformPoint(leftEye.position);
            }

            if (rightEye != null)
            {
                return parent.InverseTransformPoint(rightEye.position);
            }

            return Vector3.zero;
        }

        private static void EnsureTarget(
            Transform parent,
            string objectName,
            string displayName,
            string dialogueKey,
            Vector3 localOffset,
            int priority,
            float colliderRadius)
        {
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(objectName);
            GameObject targetObject = existing != null ? existing.gameObject : new GameObject(objectName);
            if (existing == null)
            {
                targetObject.transform.SetParent(parent, false);
            }

            FocusTarget target = targetObject.GetComponent<FocusTarget>();
            if (target == null)
            {
                target = targetObject.AddComponent<FocusTarget>();
            }

            target.DisplayName = displayName;
            target.DialogueKey = dialogueKey;
            target.FocusPriority = priority;
            target.FocusOffset = localOffset;
            target.AutoFocusable = true;

            SphereCollider collider = targetObject.GetComponent<SphereCollider>();
            if (collider == null)
            {
                collider = targetObject.AddComponent<SphereCollider>();
            }

            collider.isTrigger = true;
            collider.radius = colliderRadius;
            collider.center = localOffset;
        }

        private static Transform FindTransformByName(string objectName)
        {
            GameObject found = GameObject.Find(objectName);
            if (found != null)
            {
                return found.transform;
            }

            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == objectName)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == objectName)
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
