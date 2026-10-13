using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WalkTalkContextMenu : MonoBehaviour
{
    private GameObject panel;
    private PlayerWalkModeController walkModeController;

    public bool IsOpen => panel != null && panel.activeSelf;

    public static WalkTalkContextMenu GetOrCreate()
    {
        WalkTalkContextMenu existing = FindFirstObjectByType<WalkTalkContextMenu>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        GameObject menuObject = new GameObject(nameof(WalkTalkContextMenu));
        DontDestroyOnLoad(menuObject);
        return menuObject.AddComponent<WalkTalkContextMenu>();
    }

    public bool Open(PlayerWalkModeController controller)
    {
        if (controller == null || IsOpen || !controller.TrySetInteractionMenuOpen(true)) return false;
        walkModeController = controller;
        EnsureView();
        panel.SetActive(true);
        return true;
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        if (walkModeController != null) walkModeController.TrySetInteractionMenuOpen(false);
        walkModeController = null;
    }

    private void OnDestroy()
    {
        if (walkModeController != null) walkModeController.TrySetInteractionMenuOpen(false);
    }

    private void EnsureView()
    {
        if (panel != null) return;
        GameObject canvasObject = new GameObject("WalkTalkContextMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        panel = CreatePanel(canvasObject.transform);
        CreateButton(panel.transform, "ここで待ってて（未実装）", false, null, 80f);
        CreateButton(panel.transform, "付いてきて（未実装）", false, null, 20f);
        CreateButton(panel.transform, "運んでほしい（未実装）", false, null, -40f);
        CreateButton(panel.transform, "何もしない", true, Close, -100f);
        panel.SetActive(false);
    }

    private static GameObject CreatePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, 300f);
        panelObject.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.9f);
        return panelObject;
    }

    private static void CreateButton(Transform parent, string label, bool interactable, UnityEngine.Events.UnityAction action, float y)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(350f, 48f);
        buttonObject.GetComponent<Image>().color = interactable ? new Color(0.18f, 0.32f, 0.5f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.interactable = interactable;
        if (action != null) button.onClick.AddListener(action);

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = interactable ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
        text.text = label;
        text.raycastTarget = false;
    }
}
