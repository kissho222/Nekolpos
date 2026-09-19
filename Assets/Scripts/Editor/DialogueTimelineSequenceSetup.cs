using System.Linq;
using Nekolpos.System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;

namespace Nekolpos.EditorTools
{
    public static class DialogueTimelineSequenceSetup
    {
        [MenuItem("Tools/Nekolpos/Timeline Sequences/Configure Maw")]
        public static void ConfigureMaw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new global::System.InvalidOperationException("Configure outside Play mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var manager = all.Select(t => t.GetComponent<DialogueManager>()).Where(c => c != null).Single();
            var cat = all.Single(t => t.name == "Normal_Cat").GetComponent<Animator>();
            var binding = new DialogueTimelineSequenceController.Sequence
            {
                id = "Maw",
                start = AssetDatabase.LoadAssetAtPath<TimelineAsset>("Assets/Timelines/MawStart.playable"),
                loop = AssetDatabase.LoadAssetAtPath<TimelineAsset>("Assets/Timelines/MawLoop.playable"),
                end = AssetDatabase.LoadAssetAtPath<TimelineAsset>("Assets/Timelines/MawEnd.playable")
            };
            if (cat == null || binding.start == null || binding.loop == null || binding.end == null)
                throw new global::System.InvalidOperationException("Normal_Cat and all three Maw Timelines are required.");
            var controller = manager.GetComponent<DialogueTimelineSequenceController>();
            if (controller == null) controller = Undo.AddComponent<DialogueTimelineSequenceController>(manager.gameObject);
            Undo.RecordObject(controller, "Configure Maw Timeline Sequence");
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("catAnimator").objectReferenceValue = cat;
            var entries = serialized.FindProperty("sequences");
            int index = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == "Maw") index = i;
            if (index < 0) { index = entries.arraySize; entries.arraySize++; }
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = "Maw";
            entry.FindPropertyRelative("start").objectReferenceValue = binding.start;
            entry.FindPropertyRelative("loop").objectReferenceValue = binding.loop;
            entry.FindPropertyRelative("end").objectReferenceValue = binding.end;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[DialogueTimelineSequence] Configured MawStart / MawLoop / MawEnd on DialogueManager.");
        }
    }
}
