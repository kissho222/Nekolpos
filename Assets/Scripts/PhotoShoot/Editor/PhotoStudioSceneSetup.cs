using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Nekolpos.System;

namespace Nekolpos.PhotoShoot.Authoring
{
    /// <summary>Explicit, idempotent authoring; never edits or saves another scene.</summary>
    public static class PhotoStudioSceneSetup
    {
        [MenuItem("Nekolpos/Photo Studio/Configure MainPhotoScene")]
        public static void ConfigureOpenScene()
        {
            Configure(SceneManager.GetSceneByName("MainPhotoScene"));
        }

        public static void Configure(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "MainPhotoScene" || Application.isPlaying)
                throw new InvalidOperationException("Open MainPhotoScene in Edit Mode first.");
            var roots = scene.GetRootGameObjects();
            var subject = roots.Single(r => r.name == "Normal_Cat");
            var look = subject.GetComponentInChildren<NekomataLookRigController>(true);
            var animator = subject.GetComponentInChildren<Animator>(true);
            var sourceCamera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First(c => c.name == "Main CameraB");
            var sourceLighting = roots.SelectMany(r => r.GetComponentsInChildren<RoomLightingManager>(true)).First(c => c.name == "OpenBetaSystems");
            var studioRoot = roots.FirstOrDefault(r => r.name == "PhotoStudio");
            if (studioRoot == null) { studioRoot = new GameObject("PhotoStudio"); SceneManager.MoveGameObjectToScene(studioRoot, scene); }
            var controller = studioRoot.GetComponent<PhotoStudioController>() ?? studioRoot.AddComponent<PhotoStudioController>();
            if (controller.photoCamera == null)
            {
                var cameraRoot = new GameObject("PhotoCamera"); cameraRoot.transform.SetParent(studioRoot.transform);
                controller.photoCamera = cameraRoot.AddComponent<Camera>();
                controller.photoCamera.CopyFrom(sourceCamera);
                var sourceLook = new SerializedObject(look);
                var head = (Transform)sourceLook.FindProperty("headBone").objectReferenceValue;
                var position = head.position + subject.transform.forward * .65f + subject.transform.right * .25f + Vector3.up * .07f;
                cameraRoot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(head.position - Vector3.up * .07f - position));
                controller.photoCamera.fieldOfView = 55f;
                controller.photoCamera.nearClipPlane = .01f;
                var data = cameraRoot.AddComponent<UniversalAdditionalCameraData>();
                var sourceData = sourceCamera.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) EditorUtility.CopySerialized(sourceData, data);
                data.renderPostProcessing = true;
                cameraRoot.AddComponent<AudioListener>();
                controller.freeCamera = cameraRoot.AddComponent<PhotoShootCameraController>();
                controller.freeCamera.moveSpeed = .15f; controller.freeCamera.fastMoveSpeed = .8f;
            }
            foreach (var camera in roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)))
                if (camera != controller.photoCamera) camera.enabled = false;
            foreach (var listener in roots.SelectMany(r => r.GetComponentsInChildren<AudioListener>(true)))
                if (listener.gameObject != controller.photoCamera.gameObject) listener.enabled = false;
            controller.subject = subject.transform; controller.animator = animator; controller.lookRig = look;
            var lookSettings = new SerializedObject(look);
            lookSettings.FindProperty("prioritizeCameraTarget").boolValue = false;
            controller.head = (Transform)lookSettings.FindProperty("headBone").objectReferenceValue;
            controller.eyes = new[] { (Transform)lookSettings.FindProperty("leftEyeBone").objectReferenceValue, (Transform)lookSettings.FindProperty("rightEyeBone").objectReferenceValue };
            lookSettings.ApplyModifiedPropertiesWithoutUndo();
            var necks = new List<Transform>();
            for (var bone = controller.head.parent; bone != null && bone != subject.transform; bone = bone.parent)
                if (bone.name.IndexOf("neck", StringComparison.OrdinalIgnoreCase) >= 0) necks.Add(bone);
            controller.neckBones = necks.ToArray();
            if (controller.lighting == null)
            {
                var lightRoot = new GameObject("PhotoStudioLighting"); lightRoot.transform.SetParent(studioRoot.transform);
                controller.lighting = lightRoot.AddComponent<RoomLightingManager>();
                EditorUtility.CopySerialized(sourceLighting, controller.lighting);
            }
            var lightingSettings = new SerializedObject(controller.lighting);
            lightingSettings.FindProperty("photoStudioPreview").boolValue = true;
            lightingSettings.ApplyModifiedPropertiesWithoutUndo();
            if (controller.postVolume == null)
            {
                var volumeRoot = new GameObject("PhotoStudioPost"); volumeRoot.transform.SetParent(studioRoot.transform);
                controller.postVolume = volumeRoot.AddComponent<Volume>();
                controller.postVolume.isGlobal = true; controller.postVolume.priority = 100f;
                const string profilePath = "Assets/Settings/PhotoStudioPost.asset";
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath);
                    var dof = profile.Add<DepthOfField>(); dof.active = false;
                    var bloom = profile.Add<Bloom>(); bloom.active = false;
                    profile.Add<ColorAdjustments>();
                    foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                    EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                }
                controller.postVolume.sharedProfile = profile;
            }
            foreach (var behaviour in subject.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != look && behaviour.GetType().Namespace != "UnityEngine.Animations.Rigging") behaviour.enabled = false;
            foreach (var agent in subject.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
            subject.SetActive(true); animator.enabled = true; animator.applyRootMotion = false;
            controller.directors = roots.SelectMany(r => r.GetComponentsInChildren<PlayableDirector>(true))
                .Where(d => d.playableAsset != null && (d.transform.IsChildOf(subject.transform) || d.name == "TL_Test_NerukoMove")).ToArray();
            foreach (var director in roots.SelectMany(r => r.GetComponentsInChildren<PlayableDirector>(true))) director.playOnAwake = false;
            var states = new List<string>();
            void ReadStates(AnimatorStateMachine machine)
            {
                foreach (var child in machine.states) states.Add(child.state.name);
                foreach (var child in machine.stateMachines) ReadStates(child.stateMachine);
            }
            if (animator.runtimeAnimatorController is AnimatorController ac) ReadStates(ac.layers[0].stateMachine);
            controller.animationStates = states.Distinct().ToArray();
            var gameplayRoots = new HashSet<string> { "OpenBetaSystems", "OpenBetaCanvas", "DialogueSystem", "CatPresentationController", "UnityroomApiClient", "Player", "PlayerAreas", "PlayerTransitionPoints", "CatNavigation", "OP_Cat B", "TabletDisplay", "OpenBetaTimelineDirector", "NekolposDepthOfFieldController" };
            foreach (var root in roots) if (gameplayRoots.Contains(root.name)) root.SetActive(false);
            foreach (var root in roots.Where(r => r.name == "LookTargetControls"))
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour.GetType().Name != "LookTargetManager") behaviour.enabled = false;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
