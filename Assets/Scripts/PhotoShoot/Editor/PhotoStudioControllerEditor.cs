using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.PhotoShoot.Authoring
{
    [CustomEditor(typeof(PhotoStudioController))]
    public sealed class PhotoStudioControllerEditor : Editor
    {
        private Transform selectedBone;
        private string search = "";
        private bool references;

        public override void OnInspectorGUI()
        {
            var studio = (PhotoStudioController)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("ぼけはGameビューで確認してください。URPのSceneビューではDoFが描画されません。Volumeへの直接変更は撮影Controllerの値で上書きされます。", MessageType.Info);
            EditorGUILayout.LabelField("撮影Post設定", EditorStyles.boldLabel);
            foreach (var name in new[] { "dofEnabled", "focusDistance", "aperture", "focalLength", "bloomEnabled", "bloomIntensity", "exposureOverride", "exposure" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            serializedObject.ApplyModifiedProperties();
            if (studio.IsStudioActive)
            {
                var data = studio.photoCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                var stack = data != null && data.volumeStack != null ? data.volumeStack : UnityEngine.Rendering.VolumeManager.instance.stack;
                var dof = stack != null ? stack.GetComponent<UnityEngine.Rendering.Universal.DepthOfField>() : null;
                if (dof != null) EditorGUILayout.LabelField("描画中のDoF", $"{dof.mode.value} / Focus {dof.focusDistance.value:0.###}m / F{dof.aperture.value:0.#} / {dof.focalLength.value:0.#}mm");
            }
            if (GUILayout.Button("頭にピントを合わせる"))
            {
                Undo.RecordObject(studio, "Focus photo camera on head");
                studio.FocusOnHead();
                EditorUtility.SetDirty(studio);
            }
            EditorGUILayout.LabelField("部位をEditorで調整", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Play中も編集できます。角度を変更するとポーズ停止・視線保持へ切り替わります。Ctrl+Z / Ctrl+Yに対応。Play中の変更は停止時に戻ります。", MessageType.None);
            search = EditorGUILayout.TextField("部位検索", search);
            var bones = studio.GetEditableBones().Where(b => b != null && b.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (bones.Length > 0)
            {
                int index = Array.IndexOf(bones, selectedBone);
                selectedBone = bones[EditorGUILayout.Popup("部位", Mathf.Max(0, index), bones.Select(b => b.name).ToArray())];
                EditorGUILayout.ObjectField("Transform", selectedBone, typeof(Transform), true);
                if (GUILayout.Button("Hierarchyで部位を選択")) Selection.activeTransform = selectedBone;
                EditorGUI.BeginChangeCheck();
                var position = EditorGUILayout.Vector3Field("Local Position", selectedBone.localPosition);
                var angles = selectedBone.localEulerAngles;
                angles = new Vector3(Mathf.DeltaAngle(0, angles.x), Mathf.DeltaAngle(0, angles.y), Mathf.DeltaAngle(0, angles.z));
                angles = EditorGUILayout.Vector3Field("Local Rotation", angles);
                angles.x = EditorGUILayout.Slider("X", angles.x, -180, 180);
                angles.y = EditorGUILayout.Slider("Y", angles.y, -180, 180);
                angles.z = EditorGUILayout.Slider("Z", angles.z, -180, 180);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(selectedBone, "Adjust photo subject bone");
                    if (studio.IsStudioActive) studio.SetBonePose(selectedBone, position, Quaternion.Euler(angles));
                    else { selectedBone.localPosition = position; selectedBone.localRotation = Quaternion.Euler(angles); }
                    EditorUtility.SetDirty(selectedBone);
                }
            }
            if (studio.IsStudioActive)
            {
                if (GUILayout.Button(studio.PosePaused ? "ポーズ再生を再開" : "ポーズを停止")) studio.SetPaused(!studio.PosePaused);
                if (GUILayout.Button("視線をカメラへ戻す")) studio.EnableLook();
            }
            serializedObject.Update();
            foreach (var name in new[] { "ignoreHeadXLimit", "ignoreHeadYLimit", "subjectPosition", "subjectRotation" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            references = EditorGUILayout.Foldout(references, "参照・詳細設定", true);
            if (references)
                DrawPropertiesExcluding(serializedObject, "m_Script", "dofEnabled", "focusDistance", "aperture", "focalLength", "bloomEnabled", "bloomIntensity", "exposureOverride", "exposure", "ignoreHeadXLimit", "ignoreHeadYLimit", "subjectPosition", "subjectRotation");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [InitializeOnLoad]
    internal static class PhotoStudioEditorPoseSync
    {
        static PhotoStudioEditorPoseSync()
        {
            Undo.postprocessModifications += OnTransformModified;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private static UndoPropertyModification[] OnTransformModified(UndoPropertyModification[] modifications)
        {
            if (Application.isPlaying && modifications.Any(m => m.currentValue.target is Transform)) Synchronize();
            return modifications;
        }

        private static void OnUndoRedo()
        {
            if (Application.isPlaying) Synchronize();
        }

        private static void Synchronize()
        {
            foreach (var studio in UnityEngine.Object.FindObjectsByType<PhotoStudioController>(FindObjectsSortMode.None))
                if (studio.IsStudioActive) { studio.SynchronizeEditorBoneEdits(); studio.ApplyPostSettings(); }
        }
    }
}
