using System.IO;
using Nekolpos.System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools
{
    public static class DebugConsoleAuthoring
    {
        public const string PrefabPath = "Assets/Resources/Debug/NekolposDebugConsole.prefab";
        [MenuItem("Nekolpos/Debug/Upgrade Console Conversation Link")]
        public static void UpgradeConversationLink()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = root.transform.Find("Window/ConversationView");
                if (view.Find("OpenExistingConversation") != null) return;
                var button = Button("OpenExistingConversation", view, "既存会話デバッグを開く", new Vector2(0, -210), new Vector2(380, 42));
                var so = new SerializedObject(root.GetComponent<NekolposDebugConsole>());
                so.FindProperty("openConversationPanel").objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Nekolpos/Debug/Upgrade Console Return Action")]
        public static void UpgradeReturnAction()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var controls = root.transform.Find("Window/ScenarioControls");
                if (controls.Find("ReturnInitial") != null) return;
                var execute = controls.Find("Execute").GetComponent<RectTransform>(); execute.sizeDelta = new Vector2(220, 36);
                var back = Button("ReturnInitial", controls, "初期状態へ", new Vector2(1010, 0), new Vector2(190, 36));
                var so = new SerializedObject(root.GetComponent<NekolposDebugConsole>());
                so.FindProperty("returnInitial").objectReferenceValue = back;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Nekolpos/Debug/Upgrade Console Scenarios")]
        public static void UpgradeScenarios()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var window = root.transform.Find("Window");
                if (window.Find("ScenariosTab") != null) return;
                var tab = Button("ScenariosTab", window, "シナリオ", new Vector2(664, -62), new Vector2(150, 36));
                var controls = Rect("ScenarioControls", window, new Vector2(24, -105), new Vector2(1200, 40));
                var search = Input("Search", controls, "ID・名前で検索", Vector2.zero, new Vector2(430, 36));
                var previous = Button("Previous", controls, "前の候補", new Vector2(440, 0), new Vector2(160, 36));
                var next = Button("Next", controls, "次の候補", new Vector2(610, 0), new Vector2(160, 36));
                var execute = Button("Execute", controls, "実行（隔離必須）", new Vector2(780, 0), new Vector2(300, 36));
                execute.interactable = false; controls.gameObject.SetActive(false);
                var so = new SerializedObject(root.GetComponent<NekolposDebugConsole>());
                so.FindProperty("scenariosTab").objectReferenceValue = tab;
                so.FindProperty("scenarioControls").objectReferenceValue = controls.gameObject;
                so.FindProperty("scenarioSearch").objectReferenceValue = search;
                so.FindProperty("previousScenario").objectReferenceValue = previous;
                so.FindProperty("nextScenario").objectReferenceValue = next;
                so.FindProperty("executeScenario").objectReferenceValue = execute;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Nekolpos/Debug/Upgrade Console Diagnostics")]
        public static void UpgradeDiagnostics()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var window = root.transform.Find("Window");
                if (window.Find("DiagnosticsTab") != null) return;
                var tab = Button("DiagnosticsTab", window, "診断", new Vector2(504, -62), new Vector2(150, 36));
                var controls = Rect("DiagnosticsControls", window, new Vector2(24, -105), new Vector2(1200, 40));
                var kind = Button("Kind", controls, "全種別", Vector2.zero, new Vector2(150, 36));
                var correlation = Input("Correlation", controls, "相関IDで絞込", new Vector2(160, 0), new Vector2(350, 36));
                var snap = Button("CopySnapshot", controls, "状態JSONコピー", new Vector2(520, 0), new Vector2(210, 36));
                var trace = Button("CopyTrace", controls, "ログコピー", new Vector2(740, 0), new Vector2(190, 36));
                var export = Button("ExportTrace", controls, "ローカル書出し", new Vector2(940, 0), new Vector2(230, 36));
                controls.gameObject.SetActive(false);
                var viewport = window.Find("Viewport").GetComponent<RectTransform>(); viewport.anchoredPosition = new Vector2(24, -160); viewport.sizeDelta = new Vector2(1232, 530);
                var so = new SerializedObject(root.GetComponent<NekolposDebugConsole>());
                so.FindProperty("diagnosticsTab").objectReferenceValue = tab;
                so.FindProperty("diagnosticsControls").objectReferenceValue = controls.gameObject;
                so.FindProperty("diagnosticKind").objectReferenceValue = kind;
                so.FindProperty("diagnosticCorrelation").objectReferenceValue = correlation;
                so.FindProperty("copySnapshotButton").objectReferenceValue = snap;
                so.FindProperty("copyTraceButton").objectReferenceValue = trace;
                so.FindProperty("exportTraceButton").objectReferenceValue = export;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        public static TMP_InputField Input(string name, Transform parent, string hint, Vector2 position, Vector2 size)
        {
            var rect = Rect(name, parent, position, size); rect.gameObject.AddComponent<Image>().color = new Color(.12f, .17f, .23f, 1);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            var area = Rect("TextArea", rect, new Vector2(8, -3), size - new Vector2(16, 6)); area.gameObject.AddComponent<RectMask2D>();
            var text = Text("Text", area, "", Vector2.zero, area.sizeDelta, 18);
            var placeholder = Text("Placeholder", area, hint, Vector2.zero, area.sizeDelta, 18); placeholder.color = new Color(.65f, .65f, .65f, 1);
            input.textViewport = area; input.textComponent = (TextMeshProUGUI)text; input.placeholder = placeholder; input.characterLimit = 64;
            return input;
        }
        [MenuItem("Nekolpos/Debug/Upgrade Console Operations")]
        public static void UpgradeOperations()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var window = root.transform.Find("Window");
                if (window.Find("StateTab") != null) return;
                var state = Button("StateTab", window, "現在状態", new Vector2(24, -62), new Vector2(150, 36));
                var ops = Button("OperationsTab", window, "状態・場所", new Vector2(184, -62), new Vector2(150, 36));
                var talk = Button("ConversationTab", window, "会話", new Vector2(344, -62), new Vector2(150, 36));
                var legacy = Button("LegacyDebugButton", root.transform, "Debug (F1)", new Vector2(1010, -24), new Vector2(150, 40));
                legacy.gameObject.SetActive(false);
                var viewport = window.Find("Viewport").GetComponent<RectTransform>();
                viewport.anchoredPosition = new Vector2(24, -110); viewport.sizeDelta = new Vector2(1232, 580);
                var op = Rect("LegacyOperations", window, Vector2.zero, new Vector2(340, 740)).gameObject.AddComponent<NekolposDebugPanel>();
                op.BuildAuthoredUi();
                op.transform.Find("DebugButton").gameObject.SetActive(false);
                var controls = op.transform.Find("Nekolpos Debug Window").GetComponent<RectTransform>();
                controls.anchoredPosition = new Vector2(0, -100); controls.localScale = Vector3.one * .78f;
                var conversation = Text("ConversationView", window, "会話", new Vector2(24, -120), new Vector2(1180, 540), 22);
                conversation.gameObject.SetActive(false);
                var so = new SerializedObject(root.GetComponent<NekolposDebugConsole>());
                so.FindProperty("stateView").objectReferenceValue = viewport.gameObject;
                so.FindProperty("operations").objectReferenceValue = op;
                so.FindProperty("conversationView").objectReferenceValue = conversation.gameObject;
                so.FindProperty("conversationText").objectReferenceValue = conversation;
                so.FindProperty("stateTab").objectReferenceValue = state;
                so.FindProperty("operationsTab").objectReferenceValue = ops;
                so.FindProperty("conversationTab").objectReferenceValue = talk;
                so.FindProperty("legacyDebugButton").objectReferenceValue = legacy;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Nekolpos/Debug/Create Missing Console Prefab")]
        public static void CreateMissingPrefab()
        {
            if (File.Exists(PrefabPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var root = new GameObject("NekolposDebugConsole", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
                var controller = root.AddComponent<NekolposDebugConsole>();
                var window = Rect("Window", root.transform, Vector2.zero, Vector2.zero);
                window.anchorMin = Vector2.zero; window.anchorMax = Vector2.one; window.offsetMin = window.offsetMax = Vector2.zero;
                window.gameObject.AddComponent<Image>().color = new Color(.04f, .06f, .09f, .97f);
                var close = Button("Close", window, "閉じる (F1)", new Vector2(1080, -20), new Vector2(180, 42));
                Text("Title", window, "開発コンソール — 現在状態", new Vector2(24, -20), new Vector2(1000, 42), 26);
                var viewport = Rect("Viewport", window, new Vector2(24, -85), new Vector2(1232, 610));
                viewport.gameObject.AddComponent<Image>().color = new Color(.07f, .09f, .13f, 1);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect("Content", viewport, Vector2.zero, new Vector2(1200, 600));
                var text = Text("CurrentState", content, "未接続", new Vector2(12, -8), new Vector2(1176, 580), 21);
                text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = text.rectTransform;
                scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
                var so = new SerializedObject(controller);
                so.FindProperty("window").objectReferenceValue = window.gameObject;
                so.FindProperty("currentStateText").objectReferenceValue = text;
                so.FindProperty("closeButton").objectReferenceValue = close;
                so.ApplyModifiedPropertiesWithoutUndo();
                window.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        public static TMP_Text Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            var chat = Object.FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
            text.font = chat != null && chat.messageText != null ? chat.messageText.font : TMP_Settings.defaultFontAsset;
            text.text = value; text.fontSize = fontSize; text.color = Color.white;
            text.raycastTarget = false; text.richText = false; return text;
        }
        public static Button Button(string name, Transform parent, string value, Vector2 position, Vector2 size)
        {
            var rect = Rect(name, parent, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.22f, .30f, .42f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var text = Text("Label", rect, value, Vector2.zero, size, 19); text.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
