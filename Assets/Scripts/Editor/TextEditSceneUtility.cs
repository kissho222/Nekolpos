using System.IO;
using Nekolpos.System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace Nekolpos.EditorTools
{
    internal static class TextEditSceneUtility
    {
        public const string TextEditScenePath = "Assets/Scenes/TextEditScene.unity";

        private const string SourceScenePath = "Assets/Scenes/GameScene.unity";
        private const string PreviewStateFolderPath = "Assets/Editor/TextEditPreview";
        private const string PreviewStateAssetPath = "Assets/Editor/TextEditPreview/TextEditPreviewState.asset";

        public static void OpenTextEditSceneFromMenu()
        {
            OpenOrCreateTextEditScene();
        }

        public static TextEditPreviewState EnsurePreviewStateAsset()
        {
            TextEditPreviewState existing = AssetDatabase.LoadAssetAtPath<TextEditPreviewState>(PreviewStateAssetPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder("Assets/Editor");
            EnsureFolder(PreviewStateFolderPath);

            TextEditPreviewState asset = ScriptableObject.CreateInstance<TextEditPreviewState>();
            AssetDatabase.CreateAsset(asset, PreviewStateAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        public static bool OpenOrCreateTextEditScene()
        {
            EnsurePreviewStateAsset();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TextEditScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, TextEditScenePath))
                {
                    EditorUtility.DisplayDialog("TextEdit Scene", $"Scene copy failed: {SourceScenePath}", "OK");
                    return false;
                }

                AssetDatabase.Refresh();
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            Scene scene = EditorSceneManager.OpenScene(TextEditScenePath, OpenSceneMode.Single);
            EnsurePreviewController(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        public static void EnsureActiveTextEditSceneBindings()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            if (!string.Equals(activeScene.path, TextEditScenePath, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            EnsurePreviewController(activeScene);
        }

        public static void RefreshLoadedTextEditScenePreview()
        {
            TextEditPreviewState previewState = EnsurePreviewStateAsset();
            if (previewState == null)
            {
                return;
            }

            List<TextEditScenePreviewController> controllers = new List<TextEditScenePreviewController>(
                Resources.FindObjectsOfTypeAll<TextEditScenePreviewController>());

            for (int i = 0; i < controllers.Count; i++)
            {
                TextEditScenePreviewController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                Scene scene = controller.gameObject.scene;
                if (!scene.IsValid() ||
                    !scene.isLoaded ||
                    !string.Equals(scene.path, TextEditScenePath, global::System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ChatUIController chatUI = FindChatUi(scene);
                controller.Bind(previewState, chatUI);
                controller.RefreshPreview();
                EditorUtility.SetDirty(controller);
            }
        }

        private static void EnsurePreviewController(Scene scene)
        {
            GameObject manager = FindRootObject(scene, "GameSystemManager");
            if (manager == null)
            {
                manager = new GameObject("GameSystemManager");
                SceneManager.MoveGameObjectToScene(manager, scene);
            }

            TextEditScenePreviewController controller = manager.GetComponent<TextEditScenePreviewController>();
            if (controller == null)
            {
                controller = manager.AddComponent<TextEditScenePreviewController>();
            }

            ChatUIController chatUI = FindChatUi(scene);
            controller.Bind(EnsurePreviewStateAsset(), chatUI);
            if (chatUI == null)
            {
                Debug.LogWarning("[TextEditScene] ChatUIController が見つからないため、プレビュー同期はまだ表示されません。");
            }

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static ChatUIController FindChatUi(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ChatUIController chatUI = roots[i].GetComponentInChildren<ChatUIController>(true);
                if (chatUI != null)
                {
                    return chatUI;
                }
            }

            return null;
        }

        private static GameObject FindRootObject(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == objectName)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parentPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string folderName = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parentPath) && !AssetDatabase.IsValidFolder(parentPath))
            {
                EnsureFolder(parentPath);
            }

            if (!string.IsNullOrEmpty(parentPath))
            {
                AssetDatabase.CreateFolder(parentPath, folderName);
            }
        }
    }
}
