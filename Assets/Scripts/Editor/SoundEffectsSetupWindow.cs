using Nekolpos.Audio;
using UnityEditor;
using UnityEngine;

// Explicit authoring/navigation only: Inspector values remain the source of truth.
public sealed class SoundEffectsSetupWindow : EditorWindow
{
    private Vector2 scroll;
    private FootstepSurfaceProfile[] profiles;
    private Component[] targets;

    [MenuItem("ネコルポス/音声/SE設定")]
    public static void Open()
    {
        GetWindow<SoundEffectsSetupWindow>("SE設定").Show();
    }

    private void OnEnable()
    {
        RefreshTargets();
        EditorApplication.hierarchyChanged += RefreshTargets;
        EditorApplication.projectChanged += RefreshTargets;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= RefreshTargets;
        EditorApplication.projectChanged -= RefreshTargets;
    }

    private void RefreshTargets()
    {
        var found = new global::System.Collections.Generic.List<Component>();
        foreach (Component component in Resources.FindObjectsOfTypeAll<Component>())
        {
            if (component == null || EditorUtility.IsPersistent(component) ||
                !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded) continue;
            if (component is CharacterMovementAudioController || component is CatVoiceAudioController ||
                component is CatFootstepAudioController ||
                component is FootstepSurface) found.Add(component);
        }
        found.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
        targets = found.ToArray();
        string[] guids = AssetDatabase.FindAssets("t:FootstepSurfaceProfile", new[] { "Assets" });
        profiles = new FootstepSurfaceProfile[guids.Length];
        for (int i = 0; i < guids.Length; i++)
            profiles[i] = AssetDatabase.LoadAssetAtPath<FootstepSurfaceProfile>(AssetDatabase.GUIDToAssetPath(guids[i]));
        Repaint();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("SE基盤 #81")) Application.OpenURL("https://github.com/kissho222/Nekolpos/issues/81");
            if (GUILayout.Button("音量プレビュー #61")) Application.OpenURL("https://github.com/kissho222/Nekolpos/issues/61");
            if (GUILayout.Button("更新", GUILayout.Width(50))) RefreshTargets();
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawMissingSetup();
        EditorGUILayout.LabelField("開いているScene・Prefab", EditorStyles.boldLabel);
        if (targets == null || targets.Length == 0)
            EditorGUILayout.HelpBox("MainSceneまたは設定対象のPrefabを開いてください。", MessageType.Info);
        if (targets != null)
            foreach (Component component in targets)
            {
                if (component == null) continue;
                string label = component is CharacterMovementAudioController ? "プレイヤー移動" :
                    component is CatVoiceAudioController ? "猫ボイス" :
                    component is CatFootstepAudioController ? "猫の足音" :
                    "床材の接続";
                DrawTarget(label + "：" + component.gameObject.name, component);
            }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("足音の床材", EditorStyles.boldLabel);
        if (profiles != null)
            foreach (FootstepSurfaceProfile profile in profiles)
                if (profile != null) DrawTarget(profile.DisplayName, profile);
        if (GUILayout.Button("床材プロファイルを作成"))
            ProjectWindowUtil.CreateAsset(CreateInstance<FootstepSurfaceProfile>(), "FootstepSurface.asset");
        if (GUILayout.Button("猫ボイスのTimeline Signal"))
        {
            Object folder = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/Audio/Signals");
            if (folder != null) { Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); }
        }
        EditorGUILayout.HelpBox("選択後、Inspectorで音源を割り当てます。空の床材は明示的な無音です。猫の通常歩行は接地Observerと姿勢確定後の通知が別途必要です。", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void DrawMissingSetup()
    {
        bool canAdd = SoundEffectsSetupActions.TryDescribeActiveScene(out string description);
        using (new EditorGUI.DisabledScope(!canAdd))
        {
            if (GUILayout.Button("現在のSceneへ不足するSE基盤を追加"))
            {
                try
                {
                    int added = SoundEffectsSetupActions.AddMissingToActiveScene();
                    Debug.Log($"[SE設定] コンポーネント{added}件を追加しました。既存設定を保持し、Sceneは保存していません。");
                    RefreshTargets();
                }
                catch (global::System.Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }
        EditorGUILayout.HelpBox(description, MessageType.Info);
    }

    private static void DrawTarget(string label, Object target)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label);
            if (GUILayout.Button("選択", GUILayout.Width(50)))
            {
                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
            }
        }
    }
}

