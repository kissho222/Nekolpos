using System;
using System.IO;
using System.Linq;
using Nekolpos.Dialogue.UnknownWord;
using Nekolpos.System;
using Nekolpos.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools
{
    /// <summary>Creates missing authored UI once; existing Inspector geometry is preserved.</summary>
    public static class WordLearningAuthoring
    {
        public const string LearningPrefab = "Assets/Prefab/WordLearningView.prefab";
        public const string MemoPrefab = "Assets/Prefab/WordMemoPanel.prefab";
        private static TMP_FontAsset font;
        [MenuItem("Nekolpos/UI/Add Missing Word Learning UI")]
        public static void InstallOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before authoring word UI.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var chat = Object.FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
            if (chat == null || chat.messageText == null) throw new InvalidOperationException("MainScene ChatUI is missing.");
            font = chat.messageText.font;
            if (!File.Exists(LearningPrefab)) MakeLearningPrefab();
            if (!File.Exists(MemoPrefab)) MakeMemoPrefab();
            if (Object.FindFirstObjectByType<WordLearningController>(FindObjectsInactive.Include) == null)
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LearningPrefab), chat.messageText.canvas.transform);
                view.name = "WordLearningHost";
                Undo.RegisterCreatedObjectUndo(view, "Add word learning form");
            }
            var icons = Find("IconParent"); var home = Find("StartPanel");
            var appParent = home != null ? home.transform.parent : icons?.transform.parent;
            if (icons == null || appParent == null) throw new InvalidOperationException("Tablet home is missing.");
            if (Find("WordMemoPanel") == null)
            {
                var memo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MemoPrefab), appParent);
                memo.name = "WordMemoPanel"; memo.SetActive(false);
                Undo.RegisterCreatedObjectUndo(memo, "Add word memo");
            }
            if (Find("WordMemoIcon") == null)
            {
                var diary = Find("DiaryIcon");
                if (diary == null) throw new InvalidOperationException("Tablet diary icon is missing.");
                var icon = Object.Instantiate(diary, diary.transform.parent);
                icon.name = "WordMemoIcon";
                foreach (var c in icon.GetComponents<MonoBehaviour>()) if (!(c is Button) && !(c is Image)) Object.DestroyImmediate(c);
                var button = icon.GetComponent<Button>();
                if (button != null) { Object.DestroyImmediate(button); icon.AddComponent<Button>(); }
                var rect = icon.GetComponent<RectTransform>(); rect.anchoredPosition += new Vector2(0, -140);
                var label = icon.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "言葉メモ";
                else Text("Label", icon.transform, "言葉メモ", new Vector2(0, -50), new Vector2(180, 48), 26);
                Undo.RegisterCreatedObjectUndo(icon, "Add word memo icon");
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
        public static void InstallBatch()
        {
            Directory.CreateDirectory("output/learned-words");
            if (!File.Exists("output/learned-words/MainScene-before.unity")) File.Copy("Assets/Scenes/MainScene.unity", "output/learned-words/MainScene-before.unity");
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            InstallOpenScene(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            CSVCryptoCompiler.CompileForBuildOrThrow();
            File.WriteAllText("output/learned-words/install-success.txt", "Authored learning/memo prefabs placed; CSV compiled.");
        }
        private static GameObject Find(string name) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.gameObject.scene.IsValid() && t.name == name)?.gameObject;
        private static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform)); var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = pos; r.sizeDelta = size; return r;
        }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static TMP_Text Text(string name, Transform parent, string text, Vector2 pos, Vector2 size, int fontSize = 30)
        {
            var t = Rect(name, parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = fontSize; t.text = text; t.color = new Color(.15f, .12f, .08f); t.richText = false; t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.MidlineLeft; return t;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 pos, Vector2 size)
        {
            var r = Rect(name, parent, pos, size); var img = r.gameObject.AddComponent<Image>(); img.color = new Color(.94f, .83f, .55f);
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var label = Text("Label", r, text, Vector2.zero, size - new Vector2(12, 8), 27); label.alignment = TextAlignmentOptions.Center; return b;
        }
        private static TMP_InputField Input(string name, Transform parent, string title, float y)
        {
            var group = Rect(name + "Group", parent, new Vector2(0, y), new Vector2(850, 100));
            Text("Caption", group, title, new Vector2(0, 28), new Vector2(820, 35), 26);
            var g = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources()); g.name = name; g.transform.SetParent(group, false);
            var r = g.GetComponent<RectTransform>(); r.anchoredPosition = new Vector2(0, -22); r.sizeDelta = new Vector2(820, 55);
            foreach (var t in g.GetComponentsInChildren<TMP_Text>(true)) { t.font = font; t.fontSize = 28; t.richText = false; }
            var field = g.GetComponent<TMP_InputField>(); field.richText = false; field.text = ""; return field;
        }
        private static Toggle Toggle(Transform parent, string caption, Vector2 pos)
        {
            var r = Rect(caption, parent, pos, new Vector2(230, 55)); var background = r.gameObject.AddComponent<Image>(); background.color = Color.white;
            var toggle = r.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = background;
            var check = Rect("Check", r, new Vector2(-94, 0), new Vector2(26, 26)).gameObject.AddComponent<Image>(); check.color = new Color(.3f, .55f, .23f); toggle.graphic = check;
            Text("Label", r, caption, new Vector2(25, 0), new Vector2(170, 45), 28); return toggle;
        }
        private static void MakeLearningPrefab()
        {
            var host = Rect("WordLearningHost", null, Vector2.zero, Vector2.zero); Stretch(host);
            var c = host.gameObject.AddComponent<WordLearningController>();
            var form = Rect("WordLearningForm", host, Vector2.zero, new Vector2(980, 620));
            form.gameObject.AddComponent<Image>().color = new Color(.98f, .96f, .88f); c.form = form.gameObject;
            c.prompt = Text("Prompt", form, "", new Vector2(0, 255), new Vector2(880, 65));
            c.errorText = Text("Error", form, "", new Vector2(0, -180), new Vector2(880, 80), 24); c.errorText.color = new Color(.65f, .1f, .1f);
            var edit = Rect("Edit", form, Vector2.zero, new Vector2(900, 350)); c.editSection = edit.gameObject;
            c.wordInput = Input("Word", edit, "", 85);
            c.readingInput = Input("Reading", edit, "読み方", -45);
            var genre = Rect("Genre", form, Vector2.zero, new Vector2(900, 300)); c.genreSection = genre.gameObject;
            var dropdown = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources()); dropdown.transform.SetParent(genre, false);
            dropdown.GetComponent<RectTransform>().sizeDelta = new Vector2(820, 60); c.genreInput = dropdown.GetComponent<TMP_Dropdown>();
            foreach (var t in dropdown.GetComponentsInChildren<TMP_Text>(true)) { t.font = font; t.fontSize = 27; }
            c.genreInput.template.sizeDelta = new Vector2(0, 300);
            var item = c.genreInput.template.GetComponentInChildren<Toggle>(true).GetComponent<RectTransform>();
            item.sizeDelta = new Vector2(item.sizeDelta.x, 50);
            var greeting = Rect("Greeting", form, Vector2.zero, new Vector2(900, 350)); c.greetingSection = greeting.gameObject;
            Text("Times", greeting, "時間帯", new Vector2(0, 135), new Vector2(840, 45), 28);
            c.timeToggles = new[] { Toggle(greeting, "朝", new Vector2(-275, 75)), Toggle(greeting, "昼", new Vector2(0, 75)), Toggle(greeting, "夕", new Vector2(275, 75)), Toggle(greeting, "夜", new Vector2(-275, 10)) };
            Text("Scenes", greeting, "場面", new Vector2(0, -65), new Vector2(840, 45), 28);
            c.sceneToggles = new[] { Toggle(greeting, "会った時", new Vector2(-160, -120)), Toggle(greeting, "別れる時", new Vector2(160, -120)) };
            c.confirmButton = Button("Confirm", form, "確認", new Vector2(230, -265), new Vector2(280, 62));
            c.cancelButton = Button("Cancel", form, "やっぱりやめる", new Vector2(-230, -265), new Vector2(280, 62));
            ApplyLearningStyle(c);
            ApplySimpleCopy(c);
            ApplyGenreClickProtection(c);
            ApplyRightSideLayout(c);
            form.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(host.gameObject, LearningPrefab); Object.DestroyImmediate(host.gameObject);
        }
        public static void ApplyRightSideLayout(WordLearningController c)
        {
            var panel = c.form.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-24, -70); panel.sizeDelta = new Vector2(500, 490);
            Place(c.prompt.rectTransform, new Vector2(0, 190), new Vector2(448, 70));
            c.prompt.enableAutoSizing = true; c.prompt.fontSizeMax = 26; c.prompt.fontSizeMin = 20;
            Place(c.errorText.rectTransform, new Vector2(0, -162), new Vector2(448, 48)); c.errorText.fontSize = 20;
            // Child positions use the panel's center despite its top-right pivot.
            foreach (var child in c.form.GetComponentsInChildren<RectTransform>(true).Where(r => r.parent == panel))
            { child.anchorMin = child.anchorMax = new Vector2(.5f, .5f); }
            Place(c.editSection.GetComponent<RectTransform>(), new Vector2(0, 20), new Vector2(448, 240));
            var inputs = new[] { c.wordInput, c.readingInput };
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i]; var group = (RectTransform)input.transform.parent;
                Place(group, new Vector2(0, i == 0 ? 55 : -60), new Vector2(448, 90));
                Place(input.GetComponent<RectTransform>(), new Vector2(0, -22), new Vector2(432, 50));
                var caption = group.Find("Caption").GetComponent<TMP_Text>();
                Place(caption.rectTransform, new Vector2(0, 28), new Vector2(432, 35)); caption.fontSize = 24;
                foreach (var text in input.GetComponentsInChildren<TMP_Text>(true)) text.fontSize = 24;
            }
            Place(c.genreSection.GetComponent<RectTransform>(), Vector2.zero, new Vector2(448, 260));
            c.genreInput.GetComponent<RectTransform>().sizeDelta = new Vector2(432, 50);
            c.genreInput.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 70);
            c.genreInput.template.sizeDelta = new Vector2(0, 250);
            c.genreInput.template.GetComponentInChildren<Toggle>(true).GetComponent<RectTransform>().sizeDelta = new Vector2(0, 44);
            foreach (var text in c.genreInput.GetComponentsInChildren<TMP_Text>(true)) text.fontSize = 24;
            Place(c.greetingSection.GetComponent<RectTransform>(), new Vector2(0, 10), new Vector2(448, 270));
            var greeting = c.greetingSection.transform;
            var timesLabel = greeting.Find("Times").GetComponent<TMP_Text>();
            Place(timesLabel.rectTransform, new Vector2(0, 112), new Vector2(432, 42)); timesLabel.fontSize = 22;
            var scenesLabel = greeting.Find("Scenes").GetComponent<TMP_Text>();
            Place(scenesLabel.rectTransform, new Vector2(0, -52), new Vector2(432, 42)); scenesLabel.text = "場面"; scenesLabel.fontSize = 22;
            for (int i = 0; i < c.timeToggles.Length; i++) PlaceToggle(c.timeToggles[i], new Vector2(i % 2 == 0 ? -116 : 116, i < 2 ? 58 : 7));
            for (int i = 0; i < c.sceneToggles.Length; i++) PlaceToggle(c.sceneToggles[i], new Vector2(i == 0 ? -116 : 116, -105));
            Place(c.confirmButton.GetComponent<RectTransform>(), new Vector2(112, -210), new Vector2(208, 52));
            Place(c.cancelButton.GetComponent<RectTransform>(), new Vector2(-112, -210), new Vector2(208, 52));
            foreach (var button in new[] { c.confirmButton, c.cancelButton })
            { var text = button.GetComponentInChildren<TMP_Text>(); Place(text.rectTransform, Vector2.zero, new Vector2(196, 44)); text.fontSize = 24; }
        }
        public static void ApplyGenreClickProtection(WordLearningController c)
        {
            if (c.form.GetComponent<CanvasGroup>() == null) c.form.AddComponent<CanvasGroup>();
            foreach (var bar in c.genreInput.template.GetComponentsInChildren<Scrollbar>(true))
            {
                if (bar.targetGraphic != null) bar.targetGraphic.color = new Color32(118, 89, 63, 255);
                if (bar.GetComponent<Image>() is Image track) track.color = new Color32(237, 230, 212, 255);
                var tint = bar.colors;
                tint.normalColor = tint.selectedColor = Color.white;
                tint.highlightedColor = new Color(1f, .95f, .86f);
                tint.pressedColor = new Color(.72f, .72f, .72f);
                bar.colors = tint;
            }
            foreach (var toggle in c.genreInput.template.GetComponentsInChildren<Toggle>(true))
            {
                foreach (var shadow in toggle.GetComponentsInChildren<Shadow>(true)) Object.DestroyImmediate(shadow);
                foreach (var rect in toggle.GetComponentsInChildren<RectTransform>(true))
                    if (rect.name == "UIStyleBorder") Object.DestroyImmediate(rect.gameObject);
                if (toggle.targetGraphic is Image background) { background.sprite = null; background.color = Color.white; }
                var colors = toggle.colors; colors.normalColor = Color.clear;
                colors.highlightedColor = new Color(.55f, .51f, .43f, .18f);
                colors.pressedColor = colors.selectedColor = new Color(.55f, .51f, .43f, .3f);
                toggle.colors = colors;
            }
        }
        public static void ApplySimpleCopy(WordLearningController c)
        {
            c.wordInput.transform.parent.Find("Caption").gameObject.SetActive(false);
            c.wordInput.placeholder.GetComponent<TMP_Text>().text = "言葉を入力";
            c.readingInput.transform.parent.Find("Caption").GetComponent<TMP_Text>().text = "読み方";
            c.readingInput.placeholder.GetComponent<TMP_Text>().text = "読み方を入力";
            c.greetingSection.transform.Find("Times").GetComponent<TMP_Text>().text = "時間帯";
            c.greetingSection.transform.Find("Scenes").GetComponent<TMP_Text>().text = "場面";
            var cancel = c.cancelButton.GetComponentInChildren<TMP_Text>();
            cancel.text = WordLearningDialogue.Format("button_cancel");
            c.confirmButton.GetComponentInChildren<TMP_Text>().text = WordLearningDialogue.Format("button_confirm"); cancel.enableAutoSizing = true; cancel.fontSizeMax = 24; cancel.fontSizeMin = 20;
        }
        public static void ApplyLearningStyle(WordLearningController c)
        {
            var obsolete = c.genreSection.transform.Find("QuestionSelector");
            if (obsolete != null) Object.DestroyImmediate(obsolete.gameObject);
            var profile = AssetDatabase.LoadAssetAtPath<UIThemeProfile>("Assets/Resources/UIThemes/NekolposNormalUITheme.asset");
            Nekolpos.UI.UIStyle.ApplyTree(c.form.transform, profile);
            Nekolpos.UI.UIStyle.ApplyPanel(c.form, profile);
            Nekolpos.UI.UIStyle.ApplyPanel(c.genreInput.gameObject, profile, false);
            Nekolpos.UI.UIStyle.ApplyPanel(c.genreInput.template.gameObject, profile);
            foreach (var scroll in c.genreInput.GetComponentsInChildren<ScrollRect>(true)) scroll.scrollSensitivity = 40f;
            ApplyGenreClickProtection(c);
        }
        private static void Place(RectTransform rect, Vector2 pos, Vector2 size) { rect.anchoredPosition = pos; rect.sizeDelta = size; }
        private static void PlaceToggle(Toggle toggle, Vector2 pos)
        {
            Place(toggle.GetComponent<RectTransform>(), pos, new Vector2(200, 42));
            var label = toggle.GetComponentInChildren<TMP_Text>(); Place(label.rectTransform, new Vector2(14, 0), new Vector2(150, 38)); label.fontSize = 24;
            toggle.graphic.rectTransform.anchoredPosition = new Vector2(-78, 0);
        }
        private static void MakeMemoPrefab()
        {
            // The tablet's very small world Canvas uses a bitmap font to keep glyphs legible.
            var tabletLabel = Find("DiaryText")?.GetComponent<TMP_Text>();
            if (tabletLabel != null) font = tabletLabel.font;
            var root = Rect("WordMemoPanel", null, Vector2.zero, new Vector2(1200, 800)); Stretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(.98f, .96f, .86f); root.gameObject.AddComponent<CanvasGroup>();
            var c = root.gameObject.AddComponent<WordMemoController>();
            Text("Title", root, "ネルコに教えた言葉メモ", new Vector2(-60, 300), new Vector2(950, 80), 40);
            Button("CloseButton", root, WordLearningDialogue.Format("button_close"), new Vector2(440, 300), new Vector2(160, 64));
            var viewport = Rect("WordList", root, new Vector2(-310, -20), new Vector2(480, 540));
            viewport.gameObject.AddComponent<Image>().color = new Color(.92f, .9f, .8f); viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var content = Rect("Content", viewport, Vector2.zero, new Vector2(460, 0));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.spacing = 8; layout.padding = new RectOffset(8, 8, 8, 8);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content; c.content = content;
            var row = Button("WordRow", root, "", Vector2.zero, new Vector2(460, 65)); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 65;
            Directory.CreateDirectory("Assets/Prefab"); PrefabUtility.SaveAsPrefabAsset(row.gameObject, "Assets/Prefab/WordMemoRow.prefab"); Object.DestroyImmediate(row.gameObject);
            c.rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/WordMemoRow.prefab").GetComponent<Button>();
            c.detail = Text("Detail", root, "", new Vector2(245, 5), new Vector2(550, 480), 27); c.detail.alignment = TextAlignmentOptions.TopLeft;
            c.forgetButton = Button("Forget", root, WordLearningDialogue.Format("button_forget"), new Vector2(245, -285), new Vector2(300, 65));
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, MemoPrefab); Object.DestroyImmediate(root.gameObject);
        }
    }
}
