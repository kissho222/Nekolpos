using System;
using System.Collections.Generic;
using Nekolpos.ActionSystem;
using Nekolpos.System;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum ControlGuideMode { None, Conversation, Walk, WalkChoice, WalkDialogue }

[Serializable]
public sealed class ControlGuideEntry
{
    public ControlGuideMode mode;
    public string id;
    public string[] keys;
    public string label;
    public bool visible = true;

    public ControlGuideEntry(ControlGuideMode mode, string id, string label, params string[] keys)
    { this.mode = mode; this.id = id; this.label = label; this.keys = keys; }
}

/// <summary>既存状態を参照する操作案内。入力処理・対象検出は所有しない。</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ControlGuideHUD : MonoBehaviour
{
    [SerializeField] private PlayerWalkModeController walk;
    [SerializeField] private ChatUIController chat;
    [SerializeField] private FadeController fade;
    [SerializeField] private CatPresentationModeController presentation;
    [SerializeField] private ControlGuideEntry[] entries = {
        new ControlGuideEntry(ControlGuideMode.Conversation, "submit", "確定", "Enter"),
        new ControlGuideEntry(ControlGuideMode.Conversation, "history", "履歴から選ぶ", "↑", "↓"),
        new ControlGuideEntry(ControlGuideMode.Walk, "interact", "インタラクト", "E"),
        new ControlGuideEntry(ControlGuideMode.Walk, "move", "移動", "W", "A", "S", "D"),
        new ControlGuideEntry(ControlGuideMode.Walk, "jump", "ジャンプ", "Space"),
        new ControlGuideEntry(ControlGuideMode.WalkChoice, "choiceSelect", "選択", "↑", "↓"),
        new ControlGuideEntry(ControlGuideMode.WalkChoice, "choiceSubmit", "決定", "Enter", "Space"),
        new ControlGuideEntry(ControlGuideMode.WalkChoice, "choiceCancel", "閉じる", "Esc"),
        new ControlGuideEntry(ControlGuideMode.WalkDialogue, "advance", "全文表示／次へ", "Enter", "Space")
    };
    private WalkDialogueController walkDialogue;
    private PlayerInteractor interactor;
    [SerializeField, HideInInspector] private GameObject canvasObject;
    [SerializeField, HideInInspector] private RectTransform panel;
    [SerializeField, HideInInspector] private List<GameObject> rows = new List<GameObject>();
    [SerializeField, HideInInspector] private TextMeshProUGUI interactionLabel;
    private string interactionText = "インタラクト";
    private bool hasInteractionPrompt;
    [SerializeField, HideInInspector] private ControlGuideMode mode;
    public ControlGuideMode Mode => mode;
    public bool IsVisible => panel != null && panel.gameObject.activeSelf;
    public string InteractionLabel => interactionText;
    public RectTransform Panel => panel;

    private void OnEnable()
    {
        if (chat != null) chat.BindInputControlGuide(this);
        RemoveOrphanedViews();
        if (panel != null)
        {
            RefreshRows();
            panel.gameObject.SetActive(Mode != ControlGuideMode.None);
        }
    }

    private static void RemoveOrphanedViews()
    {
        // DontSaveのViewはScene外にも残れるため、Scene root検索だけでは見つからない。
        // 参照が失われたViewは回収し、他の所有者のViewは消さない。
        var owners = Resources.FindObjectsOfTypeAll<ControlGuideHUD>();
        foreach (Canvas candidate in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            GameObject view = candidate.gameObject;
            if (view.name != "ControlGuideCanvas" || view.transform.parent != null ||
                (view.hideFlags & HideFlags.DontSaveInEditor) == 0 ||
                view.GetComponent<CanvasGroup>() == null || view.transform.Find("ControlGuideHUD") == null) continue;
            bool owned = false;
            foreach (ControlGuideHUD owner in owners)
                if (owner != null && owner.canvasObject == view) { owned = true; break; }
            if (owned) continue;
            view.SetActive(false);
            if (Application.isPlaying) Destroy(view); else DestroyImmediate(view);
        }
    }

    public void Bind(PlayerWalkModeController controller, ChatUIController ui)
    {
        walk = controller;
        chat = ui;
        if (chat != null) chat.BindInputControlGuide(this);
        walkDialogue = controller != null ? controller.GetComponent<WalkDialogueController>() : null;
        interactor = controller != null ? controller.GetComponent<PlayerInteractor>() : null;
        // 組立時だけ解決する。毎フレームのScene探索は行わない。
        fade = FindFirstObjectByType<FadeController>(FindObjectsInactive.Include);
        presentation = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
    }

    private void LateUpdate()
    {
        bool blocked = !Application.isPlaying || Time.timeScale <= 0f ||
            (presentation != null && (presentation.CurrentMode == CatPresentationMode.Opening ||
                                      presentation.CurrentMode == CatPresentationMode.Timeline ||
                                      presentation.CurrentMode == CatPresentationMode.Event)) ||
            (fade != null && !fade.IsTransparent) || walk == null || !walk.isActiveAndEnabled ||
            walk.IsTransitioning || walk.IsPauseMenuOpen || walk.IsTabletInteractionActive;
        if (hasInteractionPrompt && (interactor == null || !interactor.IsInteractionAvailable))
            SetInteractionPrompt(null);
        SetMode(blocked ? ControlGuideMode.None : walk.IsWalkMode ?
            walkDialogue != null && walkDialogue.IsChoiceOpen ? ControlGuideMode.WalkChoice :
            walkDialogue != null && walkDialogue.IsDialogueBusy ? ControlGuideMode.WalkDialogue :
            walk.IsInteractionMenuOpen ? ControlGuideMode.None : ControlGuideMode.Walk :
            chat != null && chat.IsControlGuideInputActive ? ControlGuideMode.Conversation : ControlGuideMode.None);
        if (chat != null) chat.UpdateResponsiveInputWidth();
    }

    public void SetInteractionPrompt(string prompt)
    {
        hasInteractionPrompt = !string.IsNullOrWhiteSpace(prompt);
        interactionText = NormalizeInteractionLabel(prompt);
        if (interactionLabel != null) interactionLabel.text = interactionText;
        RefreshRows();
    }

    public static string NormalizeInteractionLabel(string prompt)
    {
        string value = (prompt ?? string.Empty).Trim();
        if (value.StartsWith("E：", StringComparison.Ordinal) || value.StartsWith("E:", StringComparison.Ordinal))
            value = value.Substring(2).Trim();
        return string.IsNullOrEmpty(value) ? "インタラクト" : value;
    }

    public void SetMode(ControlGuideMode mode)
    {
        if (!isActiveAndEnabled) mode = ControlGuideMode.None;
        if (panel == null && mode == ControlGuideMode.None) { this.mode = mode; return; }
        EnsureView();
        if (Mode == mode && panel.gameObject.activeSelf == (mode != ControlGuideMode.None)) return;
        this.mode = mode;
        RefreshRows();
        panel.gameObject.SetActive(mode != ControlGuideMode.None);
    }

    private void RefreshRows()
    {
        // Scene終了ではCanvasがPlayerより先に破棄されることがある。
        if (panel == null) return;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null)
                rows[i].SetActive(entries[i].visible && entries[i].mode == Mode &&
                    (entries[i].id != "interact" || hasInteractionPrompt));
    }

    private void EnsureView()
    {
        if (panel != null) return;
        ReleaseView();
        canvasObject = new GameObject("ControlGuideCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        // 可変行のRuntime ViewをEditor操作からSceneへ保存させない。
        canvasObject.hideFlags = HideFlags.DontSaveInEditor;
        SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        CanvasGroup group = canvasObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        panel = CreateRect("ControlGuideHUD", canvasObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
        panel.anchoredPosition = new Vector2(-28, 24);
        panel.sizeDelta = new Vector2(300, 0);
        Image background = panel.gameObject.AddComponent<Image>();
        UIStyle.ApplyPanel(panel.gameObject, shadow: false);
        background.color = new Color(0.12f, 0.11f, 0.10f, 0.48f);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        foreach (ControlGuideEntry entry in entries)
        {
            RectTransform row = CreateRect(entry.id, panel);
            rows.Add(row.gameObject);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
            var vertical = row.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 0;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            RectTransform keyGroup = CreateRect("Keys", row);
            keyGroup.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var keysLayout = keyGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            keysLayout.spacing = 5;
            keysLayout.childControlWidth = true;
            keysLayout.childControlHeight = true;
            keysLayout.childForceExpandWidth = false;
            foreach (string key in entry.keys ?? Array.Empty<string>())
            {
                RectTransform cap = CreateRect("Key_" + key, keyGroup);
                cap.gameObject.AddComponent<LayoutElement>().preferredWidth = key.Length > 1 ? 88 : 36;
                Image image = cap.gameObject.AddComponent<Image>();
                UIStyle.ApplyPanel(cap.gameObject, shadow: false);
                image.color = new Color(0.94f, 0.91f, 0.84f, 0.96f);
                TextMeshProUGUI keyText = CreateText("KeyName", cap, key, 22, UIStyle.NormalText);
                keyText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI label = CreateText("Label", row, entry.label, 25, new Color(1, 0.98f, 0.93f));
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            if (entry.id == "interact") { interactionLabel = label; label.text = interactionText; }
            row.gameObject.SetActive(false);
        }
        foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        panel.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text; label.fontSize = size; label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private void OnDisable()
    {
        SetMode(ControlGuideMode.None);
        SetInteractionPrompt(null);
        // Editorの再コンパイルもここを通る。DontSave参照の再シリアライズに寿命を依存させない。
        ReleaseView();
    }
    private void OnDestroy() => ReleaseView();

    private void ReleaseView()
    {
        GameObject previousCanvas = canvasObject;
        canvasObject = null;
        panel = null;
        interactionLabel = null;
        rows.Clear();
        if (previousCanvas == null) return;
        previousCanvas.SetActive(false);
        if (Application.isPlaying) Destroy(previousCanvas); else DestroyImmediate(previousCanvas);
    }
}
