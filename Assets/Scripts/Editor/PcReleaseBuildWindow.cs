using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekolpos.EditorTools
{
    public sealed class PcReleaseBuildWindow : EditorWindow
    {
        private readonly List<string> inactiveObjects = new List<string>();
        private readonly List<string> errors = new List<string>();
        private string version;
        private bool changeVersion;
        private bool compileCsvBeforeBuild;
        private bool reviewed;
        private Vector2 scroll;

        [MenuItem("Nekolpos/Build/PC版ビルド...")]
        public static void Open()
        {
            var window = GetWindow<PcReleaseBuildWindow>(true, "PC版ビルドの確認", true);
            window.minSize = new Vector2(640f, 480f);
            window.version = PlayerSettings.bundleVersion;
            window.changeVersion = false;
            window.RefreshChecks();
            window.Show();
        }

        public static bool IsValidVersion(string value)
        {
            return value != null && Regex.IsMatch(value, @"\A[0-9]+\.[0-9]+\.[0-9]+\z");
        }

        public static string BuildName(string value)
        {
            if (!IsValidVersion(value)) throw new ArgumentException("バージョンは X.X.X 形式で指定してください。", nameof(value));
            return "Nekolpos " + value;
        }

        private static string[] BuildScenes()
        {
            return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Windows 64bit・リリースビルド", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("現在のバージョン", PlayerSettings.bundleVersion);
            changeVersion = EditorGUILayout.ToggleLeft("バージョンを変更する", changeVersion);
            if (changeVersion) version = EditorGUILayout.TextField("新しいバージョン（X.X.X）", version);
            compileCsvBeforeBuild = EditorGUILayout.ToggleLeft("CSVを暗号化バイナリに変換してからビルド", compileCsvBeforeBuild);
            string selectedVersion = changeVersion ? version : PlayerSettings.bundleVersion;
            bool valid = IsValidVersion(selectedVersion);
            EditorGUILayout.LabelField("出力フォルダー名", valid ? BuildName(selectedVersion) : "バージョンの形式を修正してください");
            EditorGUILayout.LabelField("起動ファイル名", "Nekolpos.exe");
            EditorGUILayout.HelpBox("Development Build・Script Debugging・Profiler接続をOFFにします。デバッグUI・デバッグ表示/ログ設定はビルドするScene内でOFFにし、編集用Sceneには保存しません。", MessageType.Info);
            if (GUILayout.Button("シーンと非Active一覧を再確認")) RefreshChecks();
            EditorGUILayout.LabelField("主要項目の検査", errors.Count == 0 ? "問題なし" : $"要修正: {errors.Count}件");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.LabelField($"非ActiveのObject（{inactiveObjects.Count}件）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("待機中のパネルなども含みます。自動でActiveにはしません。", EditorStyles.wordWrappedLabel);
            foreach (string item in inactiveObjects) EditorGUILayout.SelectableLabel(item, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(20f));
            if (inactiveObjects.Count == 0) EditorGUILayout.LabelField("該当なし");
            EditorGUILayout.EndScrollView();
            reviewed = EditorGUILayout.ToggleLeft("非Active一覧を確認した（意図した待機状態である）", reviewed);
            using (new EditorGUI.DisabledScope(!valid || !reviewed || errors.Count != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer))
            {
                if (GUILayout.Button("保存先を選んでPC版をビルド", GUILayout.Height(36f))) Build(selectedVersion);
            }
        }

        private void RefreshChecks()
        {
            reviewed = false;
            inactiveObjects.Clear();
            errors.Clear();
            string[] scenes = BuildScenes();
            if (scenes.Length == 0) errors.Add("Build Settingsに有効なSceneがありません。");
            foreach (string path in scenes)
            {
                if (!File.Exists(path)) { errors.Add("Sceneが見つかりません: " + path); continue; }
                Scene scene = SceneManager.GetSceneByPath(path);
                bool preview = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (preview) scene = EditorSceneManager.OpenPreviewScene(path);
                    InspectScene(scene, inactiveObjects, errors);
                }
                catch (Exception exception)
                {
                    errors.Add(path + ": 検査失敗: " + exception.Message);
                }
                finally
                {
                    if (preview && scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            inactiveObjects.Sort(StringComparer.Ordinal);
        }

        public static void InspectScene(Scene scene, List<string> inactive, List<string> failures)
        {
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (Transform transform in transforms)
            {
                if (!transform.gameObject.activeSelf)
                    inactive.Add(scene.name + "/" + ObjectPath(transform));
            }
            // MainScene's startup roots are required; event-only panels and cats can intentionally wait inactive.
            if (scene.name == "MainScene")
            {
                foreach (string required in new[] { "OpenBetaCanvas", "OpenBetaSystems", "DialogueSystem", "EventSystem", "PlayerCameraRoot" })
                {
                    Transform item = transforms.FirstOrDefault(t => t.name == required);
                    if (item == null) failures.Add(scene.name + ": 主要Objectがありません: " + required);
                    else if (!IsHierarchyActive(item)) failures.Add(scene.name + ": 主要Objectまたは親が非Active: " + ObjectPath(item));
                }
                if (!transforms.Any(t => t.GetComponent<Nekolpos.System.NekolposGameBootstrap>() != null))
                    failures.Add(scene.name + ": NekolposGameBootstrapがありません。");
                if (!HasActiveMainCamera(scene))
                    failures.Add(scene.name + ": 起動時に有効なMainCameraがありません。");
            }
            foreach (Transform transform in transforms)
            {
                foreach (Behaviour behaviour in transform.GetComponents<Behaviour>())
                {
                    if (behaviour == null) continue;
                    string type = behaviour.GetType().Name;
                    bool required = type == "NekolposGameBootstrap" || type == "TimeManager" || type == "ConversationDataManager" || type == "GameManager" || type == "EventSystem";
                    if (transform.name == "OpenBetaCanvas" && behaviour is Canvas) required = true;
                    if (required && (!behaviour.enabled || !IsHierarchyActive(transform)))
                        failures.Add(scene.name + "/" + ObjectPath(transform) + ": " + type + " が無効です。");
                }
            }
        }

        public static bool HasActiveMainCamera(Scene scene)
        {
            return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true))
                .Any(camera => camera.CompareTag("MainCamera") && camera.enabled && IsHierarchyActive(camera.transform));
        }

        private static bool IsHierarchyActive(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }

        private static string ObjectPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        private void Build(string selectedVersion)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string path in BuildScenes())
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isDirty)
                {
                    EditorUtility.DisplayDialog("ビルド中止", "対象Sceneの未保存変更を保存してから再実行してください。", "OK");
                    return;
                }
            }
            string[] previousInactive = inactiveObjects.ToArray();
            RefreshChecks();
            if (errors.Count != 0 || !previousInactive.SequenceEqual(inactiveObjects))
            {
                EditorUtility.DisplayDialog("再確認が必要", "検査結果が変わりました。画面の一覧を確認してください。", "OK");
                return;
            }
            string folder = EditorUtility.OpenFolderPanel("PC版ビルドの保存先", Path.GetFullPath("Build"), "");
            if (string.IsNullOrEmpty(folder)) return;
            string name = BuildName(selectedVersion);
            string output = Path.Combine(folder, name, "Nekolpos.exe");
            if (Directory.Exists(Path.Combine(folder, name)) && !EditorUtility.DisplayDialog("既存ビルドの更新", output + "\n既存のビルドを更新しますか？", "更新する", "キャンセル")) return;
            try
            {
                if (compileCsvBeforeBuild)
                {
                    int count = CSVCryptoCompiler.CompileForBuildOrThrow();
                    Debug.Log($"[PcReleaseBuildWindow] CSV暗号化完了: {count}件。ビルドを開始します。");
                }
                if (changeVersion) PlayerSettings.bundleVersion = selectedVersion;
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = BuildScenes(), locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                });
                var summary = report.summary;
                EditorUtility.DisplayDialog("PC版ビルド結果", $"{summary.result}\n{summary.outputPath}\nエラー: {summary.totalErrors} / 警告: {summary.totalWarnings}", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("PC版ビルド失敗", exception.Message, "OK");
            }
        }
    }

    // Operates on the build pipeline's Scene copy; never saves changes into authoring scenes.
    public sealed class PcReleaseDebugSanitizer : IProcessSceneWithReport
    {
        public int callbackOrder => 1000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || (report.summary.options & BuildOptions.Development) != 0) return;
            DisableDebug(scene);
        }

        public static void DisableDebug(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == "DebugButton") transform.gameObject.SetActive(false);
                foreach (MonoBehaviour component in transform.GetComponents<MonoBehaviour>())
                {
                    if (component == null) continue;
                    var serialized = new SerializedObject(component);
                    foreach (string name in new[] { "enableDebugLogs", "showDebugAimTarget", "showDebugMarker" })
                    {
                        var property = serialized.FindProperty(name);
                        if (property != null && property.propertyType == SerializedPropertyType.Boolean) property.boolValue = false;
                    }
                    if (component.GetType().Name == "ConversationDebugPanel" || component.GetType().Name == "NekolposDebugPanel" || component.GetType().Name == "NekolposDebugConsole")
                    {
                        if (component.GetType().Name == "NekolposDebugConsole") component.gameObject.SetActive(false);
                        var debugRoot = serialized.FindProperty("debugRoot");
                        if (debugRoot != null && debugRoot.objectReferenceValue is GameObject debugObject) debugObject.SetActive(false);
                        component.enabled = false;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
