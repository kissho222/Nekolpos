#if UNITY_EDITOR
using System;
using System.Linq;
using Nekolpos.System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace Nekolpos.EditorTools
{
    public static class PettingEventTimelineSetupMenu
    {
        private const string ScenePath = "Assets/Scenes/TitleScene_NerukoTailTest.unity";
        private const string TimelineFolder = "Assets/Timelines";
        private const string TimelinePath = "Assets/Timelines/PettingEventTimeline.playable";
        private const string GuideClipPath = "Assets/Timelines/PettingGuide_PettingMotion.anim";
        private const string WhiteHandPrefabPath = "Assets/DL Assets/SimpleHands/Prefabs/WhiteHand.prefab";

        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Camera mainCamera = ResolveSceneCamera(scene);
            if (mainCamera == null)
            {
                throw new InvalidOperationException("No Camera found in TitleScene_NerukoTailTest.");
            }

            GameObject cameraRoot = FindSceneObject(scene, "PlayerCameraRoot") ?? mainCamera.gameObject;
            GameObject directorObject = FindOrCreateSceneObject(scene, "PettingEventDirector");
            directorObject.transform.SetParent(null, true);
            directorObject.transform.position = Vector3.zero;
            directorObject.transform.rotation = Quaternion.identity;
            directorObject.transform.localScale = Vector3.one;

            PlayableDirector director = EnsureComponent<PlayableDirector>(directorObject);
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;

            GameObject guide = FindOrCreateSceneObject(scene, "PettingGuide");
            guide.transform.SetParent(null, true);

            Animator guideAnimator = EnsureComponent<Animator>(guide);
            guideAnimator.runtimeAnimatorController = null;
            guideAnimator.applyRootMotion = false;

            GameObject whiteHand = FindSceneObject(scene, "WhiteHand") ?? FindSceneObject(scene, "HandVisual");
            if (whiteHand == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WhiteHandPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException("WhiteHand prefab not found: " + WhiteHandPrefabPath);
                }

                whiteHand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                whiteHand.name = "WhiteHand";
            }

            GameObject handRoot = FindSceneObject(scene, "HandRoot");
            if (handRoot == null)
            {
                handRoot = new GameObject("HandRoot");
                Transform handTransform = whiteHand.transform;
                handRoot.transform.SetParent(cameraRoot.transform, false);
                handRoot.transform.localPosition = handTransform.parent == cameraRoot.transform
                    ? handTransform.localPosition
                    : new Vector3(-0.21f, 0.427f, -1.14f);
                handRoot.transform.localRotation = handTransform.parent == cameraRoot.transform
                    ? handTransform.localRotation
                    : Quaternion.Euler(90f, 0f, 0f);
                handRoot.transform.localScale = Vector3.one;
            }

            whiteHand.transform.SetParent(handRoot.transform, true);
            whiteHand.transform.localPosition = Vector3.zero;
            whiteHand.transform.localRotation = Quaternion.identity;
            whiteHand.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            GameObject cat = FindSceneObject(scene, "Normal_Cat") ??
                             FindSceneObject(scene, "Normal_Cat_NerukoTail_Test") ??
                             FindSceneObject(scene, "OP_Cat_B_NerukoTail_Test") ??
                             FindSceneObjectContains(scene, "Cat");
            Animator catAnimator = cat != null ? cat.GetComponentInChildren<Animator>(true) : null;
            Bounds catBounds = ResolveCatBounds(cat, mainCamera);
            EnsurePettingCollider(cat, catBounds);

            Vector3 leftGuidePoint;
            Vector3 rightGuidePoint;
            ResolveGuidePositions(out leftGuidePoint, out rightGuidePoint);
            guide.transform.position = leftGuidePoint;

            PettingSurfaceFollower follower = EnsureComponent<PettingSurfaceFollower>(directorObject);
            SerializedObject followerObject = new SerializedObject(follower);
            followerObject.FindProperty("mainCamera").objectReferenceValue = mainCamera;
            followerObject.FindProperty("pettingGuide").objectReferenceValue = guide.transform;
            followerObject.FindProperty("handRoot").objectReferenceValue = handRoot.transform;
            followerObject.FindProperty("pettableRoot").objectReferenceValue = cat != null ? cat.transform : null;
            followerObject.FindProperty("pettableMask").intValue = ~0;
            followerObject.FindProperty("maxDistance").floatValue = 30f;
            followerObject.FindProperty("surfaceOffset").floatValue = 0.025f;
            followerObject.FindProperty("handLocalEulerAngles").vector3Value = new Vector3(90f, 0f, 0f);
            followerObject.FindProperty("followGuideWhenNoHit").boolValue = true;
            followerObject.FindProperty("hideHandWhenNoHit").boolValue = false;
            followerObject.ApplyModifiedPropertiesWithoutUndo();

            AnimationClip guideClip = CreateOrUpdateGuideClip(leftGuidePoint, rightGuidePoint);
            TimelineAsset timeline = CreateOrUpdateTimeline(guideClip, guideAnimator, catAnimator, director);

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(directorObject);
            EditorUtility.SetDirty(guide);
            EditorUtility.SetDirty(handRoot);
            EditorUtility.SetDirty(whiteHand);
            if (cat != null)
            {
                EditorUtility.SetDirty(cat);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[PettingEventTimelineSetup] Completed. " +
                $"Timeline={TimelinePath}, GuideClip={GuideClipPath}, Camera={mainCamera.name}, Cat={(cat != null ? cat.name : "<none>")}");
        }

        private static TimelineAsset CreateOrUpdateTimeline(
            AnimationClip guideClip,
            Animator guideAnimator,
            Animator catAnimator,
            PlayableDirector director)
        {
            if (!AssetDatabase.IsValidFolder(TimelineFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Timelines");
            }

            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = "PettingEventTimeline";
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }

            foreach (TrackAsset rootTrack in timeline.GetRootTracks().ToArray())
            {
                timeline.DeleteTrack(rootTrack);
            }

            AnimationTrack guideTrack = timeline.CreateTrack<AnimationTrack>(null, "PettingGuide");
            TimelineClip timelineClip = guideTrack.CreateClip<AnimationPlayableAsset>();
            timelineClip.displayName = "PettingGuide Motion";
            timelineClip.start = 0d;
            timelineClip.duration = 4d;

            AnimationPlayableAsset playableAsset = (AnimationPlayableAsset)timelineClip.asset;
            playableAsset.clip = guideClip;
            playableAsset.position = Vector3.zero;
            playableAsset.rotation = Quaternion.identity;
            playableAsset.useTrackMatchFields = false;
            playableAsset.matchTargetFields = (MatchTargetFields)0;
            playableAsset.removeStartOffset = false;

            director.playableAsset = timeline;
            director.SetGenericBinding(guideTrack, guideAnimator);

            AnimationTrack reactionTrack = timeline.CreateTrack<AnimationTrack>(null, "NerukoReaction");
            if (catAnimator != null)
            {
                director.SetGenericBinding(reactionTrack, catAnimator);
            }

            return timeline;
        }

        private static AnimationClip CreateOrUpdateGuideClip(Vector3 leftGuidePoint, Vector3 rightGuidePoint)
        {
            if (!AssetDatabase.IsValidFolder(TimelineFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Timelines");
            }

            AnimationClip guideClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(GuideClipPath);
            if (guideClip == null)
            {
                guideClip = new AnimationClip { name = "PettingGuide_PettingMotion" };
                AssetDatabase.CreateAsset(guideClip, GuideClipPath);
            }

            guideClip.ClearCurves();
            guideClip.frameRate = 30f;

            float[] times = { 0f, 1f, 2f, 3f, 4f };
            Vector3[] positions =
            {
                leftGuidePoint,
                rightGuidePoint,
                leftGuidePoint,
                rightGuidePoint,
                leftGuidePoint
            };
            Keyframe[] x = new Keyframe[times.Length];
            Keyframe[] y = new Keyframe[times.Length];
            Keyframe[] z = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                x[i] = new Keyframe(times[i], positions[i].x);
                y[i] = new Keyframe(times[i], positions[i].y);
                z[i] = new Keyframe(times[i], positions[i].z);
            }

            guideClip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", new AnimationCurve(x));
            guideClip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(y));
            guideClip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.z", new AnimationCurve(z));
            EditorUtility.SetDirty(guideClip);
            return guideClip;
        }

        private static void ResolveGuidePositions(out Vector3 leftGuidePoint, out Vector3 rightGuidePoint)
        {
            leftGuidePoint = new Vector3(-0.266400009f, 0.460299999f, -1.57299995f);
            rightGuidePoint = new Vector3(-0.239199996f, 0.460299999f, -1.57299995f);
        }

        private static Bounds ResolveCatBounds(GameObject cat, Camera mainCamera)
        {
            if (cat == null)
            {
                return new Bounds(mainCamera.transform.position + mainCamera.transform.forward * 2f, Vector3.one);
            }

            Renderer[] renderers = cat.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(cat.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void EnsurePettingCollider(GameObject cat, Bounds catBounds)
        {
            if (cat == null || cat.GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            CapsuleCollider collider = cat.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = cat.transform.InverseTransformPoint(catBounds.center);
            collider.radius = Mathf.Max(0.08f, Mathf.Min(catBounds.extents.x, catBounds.extents.z) * 0.8f);
            collider.height = Mathf.Max(collider.radius * 2f, catBounds.size.y);
        }

        private static Camera ResolveSceneCamera(Scene scene)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.gameObject.scene == scene)
            {
                return mainCamera;
            }

            return Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera != null && camera.gameObject.scene.IsValid() && camera.gameObject.scene == scene);
        }

        private static GameObject FindOrCreateSceneObject(Scene scene, string name)
        {
            return FindSceneObject(scene, name) ?? new GameObject(name);
        }

        private static GameObject FindSceneObject(Scene scene, string name)
        {
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(go => go != null && go.scene.IsValid() && go.scene == scene && go.name == name);
        }

        private static GameObject FindSceneObjectContains(Scene scene, string fragment)
        {
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(go => go != null &&
                                      go.scene.IsValid() &&
                                      go.scene == scene &&
                                      go.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
#endif
