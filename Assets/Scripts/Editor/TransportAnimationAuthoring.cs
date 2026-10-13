#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Nekolpos.System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.TransportAuthoring
{
    public static class TransportAnimationAuthoring
    {
        public const string MaterialRoot = "Assets/Picture/きつねねね/ネルコロードアニメーション/アニメーション 20260923";
        public const string PrefabPath = "Assets/Prefab/TransportAnimationView.prefab";
        private static readonly string[] CatLayers = { "ベース白毛", "ベース茶毛", "ネルコスタンダード", "ネルコハチワレ", "トラ模様", "白1", "白2", "耳ピンク", "影", "ネルコ線画", "ネルコ不機嫌線" };
        private static readonly string[] PlayerLayers = { "プレイヤーカゴ", "プレイヤー服襟", "プレイヤー吊り", "プレイヤー頭", "プレイヤー背中" };

        [MenuItem("Nekolpos/UI/Install Transport Animation")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Playを停止してから設置してください。");
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>();
            if (bootstrap == null) throw new InvalidOperationException("本編BootstrapがあるSceneを開いてください。");
            var existing = UnityEngine.Object.FindFirstObjectByType<TransportAnimationView>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Debug.Log("[TransportAnimation] 既存Viewを保持します。レイアウトとプリセットを再生成しません。", existing);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("既存Prefabを上書きしません。既存PrefabをSceneへ配置してBootstrapのView参照を設定してください。");

            foreach (string id in CatLayers.Concat(PlayerLayers))
            {
                string folder = MaterialRoot + (id.StartsWith("プレイヤー", StringComparison.Ordinal) ? "/プレイヤー/" : "/ネルコ/") + id;
                string[] files = Directory.GetFiles(folder, "*.png");
                if (files.Length != 6) throw new InvalidOperationException($"{id}: PNGは6枚必要です（実際{files.Length}枚）。");
                for (int frame = 1; frame <= 6; frame++)
                {
                    string path = folder + "/" + id + frame + ".png";
                    if (!File.Exists(path)) throw new FileNotFoundException($"{id}: コマ{frame}がありません。", path);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) { AssetDatabase.ImportAsset(path); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
                    if (importer == null) throw new InvalidOperationException($"TextureImporterを取得できません: {path}");
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    importer.SaveAndReimport();
                }
            }

            Shader shader = Shader.Find("Nekolpos/UI/TransportMultiply");
            if (shader == null) throw new InvalidOperationException("TransportMultiply Shaderが未コンパイルです。");
            const string materialPath = "Assets/Prefab/TransportShadow.mat";
            var shadow = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (shadow == null) { shadow = new Material(shader); AssetDatabase.CreateAsset(shadow, materialPath); }

            var root = new GameObject("TransportAnimationView", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(AudioSource), typeof(TransportAnimationView));
            Undo.RegisterCreatedObjectUndo(root, "Install Transport Animation");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = false;
            group.interactable = false;
            var audio = root.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0;
            audio.ignoreListenerPause = true;

            var bounds = new GameObject("DisplayBounds", typeof(RectTransform));
            bounds.transform.SetParent(root.transform, false);
            var boundsRect = bounds.GetComponent<RectTransform>();
            boundsRect.anchorMin = new Vector2(.1f, .1f);
            boundsRect.anchorMax = new Vector2(.9f, .9f);
            boundsRect.offsetMin = boundsRect.offsetMax = Vector2.zero;
            var content = new GameObject("Layers", typeof(RectTransform), typeof(AspectRatioFitter));
            content.transform.SetParent(bounds.transform, false);
            var aspect = content.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;

            var view = root.GetComponent<TransportAnimationView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("visibility").objectReferenceValue = group;
            serialized.FindProperty("soundSource").objectReferenceValue = audio;
            var sounds = serialized.FindProperty("startSounds"); sounds.arraySize = 3;
            string[] names = { "15(Slow-Long-1-Up)", "17(Slow-Long-2-Up)", "19(Slow-Long-3-Up)" };
            for (int i = 0; i < names.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Xylophone04-mp3/Xylophone04/Xylophone04-" + names[i] + ".mp3");
                if (clip == null) throw new InvalidOperationException($"SEがありません: {names[i]}");
                sounds.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
            var layers = serialized.FindProperty("layers");
            string[] all = CatLayers.Concat(PlayerLayers).ToArray(); layers.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                string id = all[i];
                var node = new GameObject(id, typeof(RectTransform), typeof(Image));
                node.transform.SetParent(content.transform, false);
                var rect = node.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var image = node.GetComponent<Image>(); image.raycastTarget = false; image.enabled = false;
                if (id == "影") image.material = shadow;
                var layer = layers.GetArrayElementAtIndex(i);
                layer.FindPropertyRelative("id").stringValue = id;
                layer.FindPropertyRelative("image").objectReferenceValue = image;
                var frames = layer.FindPropertyRelative("frames"); frames.arraySize = 6;
                string folder = MaterialRoot + (id.StartsWith("プレイヤー", StringComparison.Ordinal) ? "/プレイヤー/" : "/ネルコ/") + id;
                for (int frame = 0; frame < 6; frame++)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/" + id + (frame + 1) + ".png");
                    if (sprite == null) throw new InvalidOperationException($"Spriteがありません: {id}{frame + 1}");
                    frames.GetArrayElementAtIndex(frame).objectReferenceValue = sprite;
                }
            }
            var presets = serialized.FindProperty("presets"); presets.arraySize = 7;
            string[] presetIds = { "Basket", "Collar", "Feet", "FeetWeary", "Head", "HeadWeary", "Back" };
            string[] playerIds = { PlayerLayers[0], PlayerLayers[1], PlayerLayers[2], PlayerLayers[2], PlayerLayers[3], PlayerLayers[3], PlayerLayers[4] };
            float[] holds = { .16f, .17f, .17f, .16f, .17f, .17f };
            for (int i = 0; i < 7; i++)
            {
                var preset = presets.GetArrayElementAtIndex(i);
                preset.FindPropertyRelative("id").stringValue = presetIds[i];
                string[] ids = i == 3 || i == 5
                    ? new[] { "ネルコスタンダード", "影", "ネルコ線画", playerIds[i], "ネルコ不機嫌線" }
                    : new[] { "ネルコスタンダード", "影", "ネルコ線画", playerIds[i] };
                var ls = preset.FindPropertyRelative("layers"); ls.arraySize = ids.Length;
                for (int j = 0; j < ids.Length; j++) ls.GetArrayElementAtIndex(j).stringValue = ids[j];
                var timing = preset.FindPropertyRelative("frameSeconds"); timing.arraySize = 6;
                for (int j = 0; j < 6; j++) timing.GetArrayElementAtIndex(j).floatValue = holds[j];
                preset.FindPropertyRelative("loop").boolValue = true;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ConfigureTravelLayout(root);
            foreach (var preset in view.Presets)
                if (!view.ValidatePreset(preset.id, out string error)) throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            Undo.RecordObject(bootstrap, "Connect Transport Animation");
            var bootstrapData = new SerializedObject(bootstrap);
            bootstrapData.FindProperty("transportAnimationView").objectReferenceValue = view;
            var bindings = bootstrapData.FindProperty("conversationTimelineBindings");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                string id = binding.FindPropertyRelative("id").stringValue;
                if (id != "MoveToDesk" && id != "MoveToTable") continue;
                binding.FindPropertyRelative("transportAnimationPreset").stringValue = "Collar";
                binding.FindPropertyRelative("transportAnimationSeconds").floatValue = 2f;
            }
            bootstrapData.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            Debug.Log("[TransportAnimation] 96 Sprite・7プリセット・3SE・首襟2秒を本編Table⇄Deskへ設置。Sceneは保存前です。", view);
        }

        [MenuItem("Nekolpos/UI/Apply Transport Travel Layout")]
        public static void ApplyTravelLayout()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Playを停止してから調整してください。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ConfigureTravelLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[TransportAnimation] 共通親の左右反転・約1/3サイズ・左→右の横移動をPrefabへ設定しました。");
        }

        private static void ConfigureTravelLayout(GameObject root)
        {
            var bounds = root.GetComponentsInChildren<RectTransform>(true).First(x => x.name == "DisplayBounds");
            var content = bounds.GetComponentsInChildren<RectTransform>(true).First(x => x.name == "Layers");
            var motion = root.transform.Find("MotionRoot") as RectTransform;
            if (motion == null)
            {
                var node = new GameObject("MotionRoot", typeof(RectTransform));
                node.transform.SetParent(root.transform, false);
                motion = node.GetComponent<RectTransform>();
                motion.anchorMin = Vector2.zero; motion.anchorMax = Vector2.one;
                motion.offsetMin = motion.offsetMax = Vector2.zero;
                bounds.SetParent(motion, false);
            }
            // The delivered artwork occupies at most 1089/1440 of its canvas height.
            // Keep the transparent margins: 1/3 visible screen height needs 0.44077 canvas height.
            const float halfHeight = (500f / 1080f) / (1089f / 1440f) / 2f;
            bounds.anchorMin = new Vector2(bounds.anchorMin.x, .5f - halfHeight);
            bounds.anchorMax = new Vector2(bounds.anchorMax.x, .5f + halfHeight);
            bounds.offsetMin = bounds.offsetMax = Vector2.zero;
            Vector3 scale = content.localScale;
            scale.x = -Mathf.Abs(scale.x);
            content.localScale = scale;
            var data = new SerializedObject(root.GetComponent<TransportAnimationView>());
            data.FindProperty("motionRoot").objectReferenceValue = motion;
            data.FindProperty("enableHorizontalTravel").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
