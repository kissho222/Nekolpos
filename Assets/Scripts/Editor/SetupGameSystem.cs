using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using Nekolpos.ActionSystem;
using Nekolpos.System;

namespace Nekolpos.EditorTools
{
    /// <summary>
    /// 【ゲームシステムの自動セットアップ】
    /// メニューから1クリックで、ゲーム進行に必要な中核マネージャー群をシーンに配置します。
    /// これにより、新しいシーンを作っても一瞬でゲームの土台が完成します。
    /// </summary>
    public class SetupGameSystem : Editor
    {
        private const string MANAGER_NAME = "GameSystemManager";

        public static void SetupManagers()
        {
            // 1. マネージャー群をまとめる親オブジェクトを探すか作成する
            GameObject managerObj = GameObject.Find("GameSystemManager");
            if (managerObj == null)
            {
                managerObj = new GameObject("GameSystemManager");
            }

            // 2. 必要なコンポーネント（スクリプト）がついていなければ、追加する
            
            // 進行管理の親玉
            if (managerObj.GetComponent<GameManager>() == null)
            {
                managerObj.AddComponent<GameManager>();
            }

            // フラグ（一時記憶）の管理者
            if (managerObj.GetComponent<GameFlagManager>() == null)
            {
                managerObj.AddComponent<GameFlagManager>();
            }

            // 会話パースエンジン
            DialogueEngine engine = managerObj.GetComponent<DialogueEngine>();
            if (engine == null)
            {
                engine = managerObj.AddComponent<DialogueEngine>();
            }

            // フォントプリローダー
            FontPreloader fontPreloader = managerObj.GetComponent<FontPreloader>();
            if (fontPreloader == null)
            {
                fontPreloader = managerObj.AddComponent<FontPreloader>();
            }
            
            // スクリプト上のデフォルト値（クリーンなファイル名）を強制的に適応させる
            engine.regexDictFileName = "Regular Expression";
            engine.reactionDictFileName = "RegexDict";

            // 3. 動作テスト用の簡易UI (Canvas & InputField) を生成する
            SetupTestUI(managerObj);

            // 4. 視認性を良くするために、ヒエラルキーの1番上にリセットしておく
            managerObj.transform.position = Vector3.zero;

            Debug.Log("[Setup] ✨ ゲームの土台（GameManager, FlagManager, DialogueManager）の配置が完了しました！\n再生ボタンを押して、Consoleから朝昼夜のローテーションをテストできます。");
            
            // 変更を保存対象にする（シーンに「*」マークをつける）
            EditorUtility.SetDirty(managerObj);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[SetupGameSystem] ✅ {MANAGER_NAME} とテストUIのセットアップが完了しました！Playボタンでテストできます。");
        }

        private static void SetupTestUI(GameObject managerObj)
        {
            // 旧UIコントローラーは引き続きテスト用として残すか、完全置き換えするか
            // 今回は新しいノベルUIコントローラーに置き換えます
            DialogueUIController oldUiController = managerObj.GetComponent<DialogueUIController>();
            if (oldUiController != null)
            {
                DestroyImmediate(oldUiController);
            }

            ChatUIController chatUI = managerObj.GetComponent<ChatUIController>();
            if (chatUI == null)
            {
                chatUI = managerObj.AddComponent<ChatUIController>();
            }

            DialogueManager dialogueManager = managerObj.GetComponent<DialogueManager>();
            if (dialogueManager == null)
            {
                dialogueManager = managerObj.AddComponent<DialogueManager>();
            }

            if (managerObj.GetComponent<ActionManager>() == null)
            {
                managerObj.AddComponent<ActionManager>();
            }
            // 相互リンク
            dialogueManager.chatUI = chatUI;

            // 既にCanvasがあるかチェック
            string canvasName = "TestDialogueCanvas";
            GameObject canvasObj = GameObject.Find(canvasName);
            
            if (canvasObj == null)
            {
                // Canvas作成
                canvasObj = new GameObject(canvasName);
                Canvas canvas = canvasObj.AddComponent<Canvas>();
                ConfigureCanvas(canvas);

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasObj.AddComponent<GraphicRaycaster>();
                
                // EventSystem作成（なければ）
                if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    GameObject eventSystem = new GameObject("EventSystem");
                    eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                }

                // InputField (レガシー) 作成
                GameObject inputFieldObj = DefaultControls.CreateInputField(new DefaultControls.Resources());
                inputFieldObj.transform.SetParent(canvasObj.transform, false);
                inputFieldObj.name = "ChatInputField";
                
                // 画面下部に配置
                RectTransform rt = inputFieldObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0);
                rt.anchorMax = new Vector2(0.5f, 0);
                rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(0, 50); // 下から少し浮かす
                rt.sizeDelta = new Vector2(600, 60);

                // Placeholderなどの文字を見やすく （Unityのバージョンによって子要素の名前が変わる対策）
                InputField inputField = inputFieldObj.GetComponent<InputField>();
                if (inputField != null)
                {
                    Text placeholder = inputField.placeholder as Text;
                    if (placeholder != null)
                    {
                        placeholder.text = "「なでて」「きもちいい？」等を入力しEnter...";
                        placeholder.fontSize = 24;
                    }

                    Text inputText = inputField.textComponent;
                    if (inputText != null)
                    {
                        inputText.fontSize = 24;
                    }
                    
                    // 新しいコントローラーに紐づけ
                    chatUI.chatInputField = inputField;
                }
            }

            // 何らかの理由で先ほど紐づけられなかった場合、探し出して再アタッチする
            if (chatUI.chatInputField == null)
            {
                GameObject existingInput = GameObject.Find("ChatInputField");
                if (existingInput != null)
                {
                    chatUI.chatInputField = existingInput.GetComponent<InputField>();
                }
            }
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = Object.FindFirstObjectByType<Camera>();
            }

            if (targetCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = targetCamera;
                canvas.planeDistance = 100f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
        }
    }
}
