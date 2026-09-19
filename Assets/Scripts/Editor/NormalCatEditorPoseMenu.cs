using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    // Store the normal idle as the scene pose so leaving an animation preview
    // restores that pose instead of the pose inherited from the OP model.
    public static class NormalCatEditorPoseMenu
    {
        private const string Menu = "Tools/Nekolpos/Normal Cat/";
        private const string Idle = "CatSimple_Lie_belly_loop_1";

        [MenuItem(Menu + "Verify Selection Stability")]
        public static void VerifySelectionStability()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Verify outside Play mode.");
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var normal = roots.Single(g => g.name == "Normal_Cat");
            var opening = roots.Single(g => g.name.StartsWith("OP_Cat", StringComparison.Ordinal));
            var transforms = normal.GetComponentsInChildren<Transform>(true);
            var positions = transforms.Select(t => t.localPosition).ToArray();
            var rotations = transforms.Select(t => t.localRotation).ToArray();
            var selected = Selection.objects;
            Selection.activeGameObject = normal;
            EditorApplication.delayCall += () =>
            {
                Selection.activeGameObject = opening;
                EditorApplication.delayCall += () =>
                {
                    int changed = transforms.Where((t, i) => t == null ||
                        Vector3.Distance(t.localPosition, positions[i]) > 0.00001f ||
                        Quaternion.Angle(t.localRotation, rotations[i]) > 0.05f).Count();
                    Selection.objects = selected;
                    if (changed != 0)
                        Debug.LogError($"[NormalCatEditorPose] Selection stability FAILED: {changed} transforms changed.");
                    else
                        Debug.Log("[NormalCatEditorPose] Selection stability PASS: Normal_Cat -> OP_Cat; all transforms unchanged.");
                };
            };
        }

        [MenuItem(Menu + "Restore Saved Idle Pose")]
        public static void Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Restore the editor pose outside Play mode.");

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var cats = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "Normal_Cat").ToArray();
            if (cats.Length != 1)
                throw new InvalidOperationException("Expected exactly one Normal_Cat in the active scene.");

            var cat = cats[0];
            var animator = cat.GetComponent<Animator>();
            var clip = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == Idle) : null;
            if (clip == null)
                throw new InvalidOperationException("Normal_Cat has no configured normal idle clip.");

            foreach (var window in Resources.FindObjectsOfTypeAll<AnimationWindow>())
                window.previewing = false;
            var timeline = UnityEditor.Timeline.TimelineEditor.GetWindow();
            if (timeline != null) timeline.Close();
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();

            var transforms = cat.GetComponentsInChildren<Transform>(true);
            var before = transforms.Select(t => t.localRotation).ToArray();
            Undo.RegisterCompleteObjectUndo(transforms, "Restore Normal Cat Idle Pose");
            var position = cat.localPosition;
            var rotation = cat.localRotation;
            var scale = cat.localScale;
            clip.SampleAnimation(cat.gameObject, 0f);
            cat.localPosition = position;
            cat.localRotation = rotation;
            cat.localScale = scale;
            int changed = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (Quaternion.Angle(before[i], transforms[i].localRotation) > 0.01f) changed++;
                PrefabUtility.RecordPrefabInstancePropertyModifications(transforms[i]);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
            Debug.Log($"[NormalCatEditorPose] Restored {Idle}; changed rotations={changed}; transforms={transforms.Length}; preview={AnimationMode.InAnimationMode()}. Scene remains unsaved for review.", cat);
        }
    }
}
