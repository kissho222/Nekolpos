using System;
using System.Reflection;
using Nekolpos.Audio;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CatVoiceAudioController)), CanEditMultipleObjects]
public sealed class CatVoiceAudioControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SoundEffectsInspectorGUI.Draw(serializedObject);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editor試聴", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("登録済み候補から1つをUnity標準のPreview音量で試聴します。Runtimeの基本音量・カテゴリ音量・Mixer音量は反映されません。未登録カテゴリは無音です。", MessageType.Info);

        using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawPreviewButton("呼ぶ", CatVoiceType.Call);
            DrawPreviewButton("怒り", CatVoiceType.Angry);
            DrawPreviewButton("嬉しい", CatVoiceType.Happy);
            DrawPreviewButton("不満", CatVoiceType.Displeased);
        }
    }

    private void DrawPreviewButton(string label, CatVoiceType type)
    {
        if (!GUILayout.Button(label)) return;
        CatVoiceAudioController controller = (CatVoiceAudioController)target;
        if (!controller.TryGetPreview(type, out AudioClip clip, out _)) return;
        Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        MethodInfo method = audioUtil?.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);
        if (method != null)
        {
            method.Invoke(null, new object[] { clip, 0, false });
            return;
        }

        Debug.LogWarning($"[CatVoiceAudioControllerEditor] Unity {Application.unityVersion} ではEditor試聴APIを取得できません。Play Modeでイベント等からPlayCatVoiceを呼び出して確認してください。", controller);
    }
}
