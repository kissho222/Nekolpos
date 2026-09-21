using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>
    /// Owns the tablet home screen and transitions between its application windows.
    /// Missing authored pages are created at runtime, so designers can later replace
    /// them with same-named Scene objects without changing navigation code.
    /// </summary>
    public sealed class TabletHomeApplicationController : MonoBehaviour
    {
        private readonly struct ApplicationDefinition
        {
            public ApplicationDefinition(string iconName, string panelName, Color backgroundColor)
            {
                IconName = iconName;
                PanelName = panelName;
                BackgroundColor = backgroundColor;
            }

            public string IconName { get; }
            public string PanelName { get; }
            public Color BackgroundColor { get; }
        }

        private static readonly ApplicationDefinition[] Applications =
        {
            new ApplicationDefinition("NyanstaIcon", "NyanstaPanel", new Color(0.96f, 0.82f, 0.88f, 1f)),
            new ApplicationDefinition("ShopIcon", "ShopPanel", new Color(0.83f, 0.92f, 0.82f, 1f)),
            new ApplicationDefinition("demaeIcon", "DemaePanel", new Color(0.96f, 0.88f, 0.72f, 1f))
        };

        public static TabletHomeApplicationController Instance { get; private set; }

        [SerializeField, Min(0.01f)] private float transitionSeconds = 0.22f;
        [SerializeField, Range(0.05f, 0.5f)] private float iconScaleFallback = 0.14f;

        private readonly Dictionary<string, RectTransform> iconsByPanelName = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, GameObject> panelsByName = new Dictionary<string, GameObject>();
        private GameObject panelRoot;
        private GameObject startPanel;
        private TabletApplicationWindowTransition transition;

        private void Awake()
        {
            Instance = this;
            Initialize();
        }

        private void OnDestroy()
        {
            transition?.Stop();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static bool TryGetPanelName(string iconName, out string panelName)
        {
            for (int index = 0; index < Applications.Length; index++)
            {
                if (string.Equals(Applications[index].IconName, iconName, StringComparison.Ordinal))
                {
                    panelName = Applications[index].PanelName;
                    return true;
                }
            }

            panelName = null;
            return false;
        }

        /// <summary>Closes an app window and returns to StartPanel. Returns false when uninitialized.</summary>
        public bool CloseApplication(GameObject targetWindow)
        {
            if (targetWindow == null || startPanel == null || transition == null)
            {
                return false;
            }

            iconsByPanelName.TryGetValue(targetWindow.name, out RectTransform icon);
            transition.PlayClose(icon, targetWindow, startPanel, transitionSeconds, iconScaleFallback);
            return true;
        }

        /// <summary>Opens a registered application using the shared tablet window transition.</summary>
        public bool OpenApplication(GameObject targetWindow, bool playIconTransition)
        {
            if (targetWindow == null || startPanel == null || transition == null)
            {
                return false;
            }

            PrepareApplicationWindow(targetWindow);
            transition.Stop();
            foreach (KeyValuePair<string, GameObject> pair in panelsByName)
            {
                if (pair.Value != null && pair.Value != targetWindow)
                {
                    pair.Value.SetActive(false);
                }
            }

            iconsByPanelName.TryGetValue(targetWindow.name, out RectTransform icon);
            if (playIconTransition && icon != null)
            {
                transition.PlayOpen(icon, targetWindow, transitionSeconds, iconScaleFallback);
            }
            else
            {
                transition.ShowImmediately(targetWindow);
            }

            return true;
        }

        private void Initialize()
        {
            GameObject iconParent = FindSceneObject("IconParent");
            if (iconParent == null)
            {
                Debug.LogWarning("[TabletApps] IconParent が見つからないため、タブレットアプリを初期化できません。", this);
                return;
            }

            GameObject existingStartPanel = FindSceneObject("StartPanel");
            panelRoot = ResolvePanelRoot(iconParent, existingStartPanel);
            if (panelRoot == null)
            {
                Debug.LogWarning("[TabletApps] タブレットのアプリ親Panelが見つかりません。", this);
                return;
            }

            startPanel = existingStartPanel ?? CreateStartPanel(iconParent);
            PrepareStartPanel(iconParent);
            transition = panelRoot.GetComponent<TabletApplicationWindowTransition>();
            if (transition == null)
            {
                transition = panelRoot.AddComponent<TabletApplicationWindowTransition>();
            }

            for (int index = 0; index < Applications.Length; index++)
            {
                ApplicationDefinition application = Applications[index];
                GameObject icon = FindSceneObject(application.IconName);
                if (icon == null)
                {
                    Debug.LogWarning($"[TabletApps] {application.IconName} が見つかりません。", this);
                    continue;
                }

                GameObject page = FindSceneObject(application.PanelName) ?? CreatePage(application);
                PrepareApplicationWindow(page);
                RegisterApplication(icon, page);
            }

            GameObject diaryPanel = FindSceneObject("DiaryPanel");
            GameObject diaryIcon = FindSceneObject("DiaryIcon");
            if (diaryPanel != null && diaryIcon != null)
            {
                PrepareApplicationWindow(diaryPanel);
                panelsByName[diaryPanel.name] = diaryPanel;
                iconsByPanelName[diaryPanel.name] = diaryIcon.GetComponent<RectTransform>();
            }
        }

        private GameObject ResolvePanelRoot(GameObject iconParent, GameObject existingStartPanel)
        {
            Transform currentParent = iconParent != null ? iconParent.transform.parent : null;
            if (currentParent == null)
            {
                return null;
            }

            // A previous runtime-generated StartPanel may have become the direct parent of every
            // tablet child when the Scene was saved. Its parent is the actual common app root.
            if (existingStartPanel != null && currentParent == existingStartPanel.transform)
            {
                return existingStartPanel.transform.parent != null
                    ? existingStartPanel.transform.parent.gameObject
                    : null;
            }

            return currentParent.gameObject;
        }

        private void PrepareStartPanel(GameObject iconParent)
        {
            if (startPanel == null || panelRoot == null)
            {
                return;
            }

            RectTransform homeRect = startPanel.GetComponent<RectTransform>();
            if (homeRect == null)
            {
                return;
            }

            if (homeRect.parent != panelRoot.transform)
            {
                homeRect.SetParent(panelRoot.transform, false);
            }

            Stretch(homeRect);
            if (iconParent != null && iconParent.transform.parent != homeRect)
            {
                iconParent.transform.SetParent(homeRect, false);
            }

            GameObject powerIcon = FindSceneObject("PowerIcon");
            if (powerIcon != null && powerIcon.transform.parent != homeRect)
            {
                powerIcon.transform.SetParent(homeRect, false);
            }
        }

        private GameObject CreateStartPanel(GameObject iconParent)
        {
            GameObject home = new GameObject("StartPanel", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rect = home.GetComponent<RectTransform>();
            rect.SetParent(panelRoot.transform, false);
            Stretch(rect);
            rect.SetSiblingIndex(0);

            iconParent.transform.SetParent(rect, false);
            GameObject powerIcon = FindSceneObject("PowerIcon");
            if (powerIcon != null)
            {
                powerIcon.transform.SetParent(rect, false);
            }

            return home;
        }

        private GameObject CreatePage(ApplicationDefinition application)
        {
            GameObject page = new GameObject(
                application.PanelName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.SetParent(panelRoot.transform, false);
            Stretch(rect);
            page.GetComponent<Image>().color = application.BackgroundColor;
            CreateCloseButton(page.transform);
            page.SetActive(false);
            return page;
        }

        private void RegisterApplication(GameObject icon, GameObject page)
        {
            if (icon == null || page == null)
            {
                return;
            }

            Button iconButton = icon.GetComponent<Button>() ?? icon.AddComponent<Button>();
            iconButton.targetGraphic ??= icon.GetComponent<Graphic>();
            RectTransform iconTransform = icon.GetComponent<RectTransform>();
            Button closeButton = FindCloseButton(page) ?? CreateCloseButton(page.transform);

            panelsByName[page.name] = page;
            iconsByPanelName[page.name] = iconTransform;
            iconButton.onClick.AddListener(() => OpenApplication(iconTransform, page));
            closeButton.onClick.AddListener(() => CloseApplication(page));
            page.SetActive(false);
        }

        private void OpenApplication(RectTransform icon, GameObject page)
        {
            if (icon == null || page == null || transition == null)
            {
                return;
            }

            OpenApplication(page, playIconTransition: true);
        }

        private void PrepareApplicationWindow(GameObject page)
        {
            if (page == null || panelRoot == null)
            {
                return;
            }

            RectTransform rect = page.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            if (rect.parent != panelRoot.transform)
            {
                rect.SetParent(panelRoot.transform, false);
            }

            Stretch(rect);
        }

        private static Button FindCloseButton(GameObject page)
        {
            foreach (Button button in page.GetComponentsInChildren<Button>(true))
            {
                if (string.Equals(button.name, "CloseButton", StringComparison.Ordinal))
                {
                    return button;
                }
            }

            return null;
        }

        private static Button CreateCloseButton(Transform parent)
        {
            GameObject buttonObject = new GameObject(
                "CloseButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(parent, false);
            buttonRect.anchorMin = Vector2.one;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.pivot = Vector2.one;
            buttonRect.anchoredPosition = new Vector2(-44f, -44f);
            buttonRect.sizeDelta = new Vector2(88f, 88f);
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.16f, 0.14f, 0.11f, 0.82f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;

            CreateCloseStroke(buttonObject.transform, 45f);
            CreateCloseStroke(buttonObject.transform, -45f);
            return button;
        }

        private static void CreateCloseStroke(Transform parent, float rotationZ)
        {
            GameObject stroke = new GameObject("Stroke", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = stroke.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(45f, 7f);
            rect.localEulerAngles = new Vector3(0f, 0f, rotationZ);
            Image image = stroke.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (transform.gameObject.scene.IsValid() && string.Equals(transform.name, objectName, StringComparison.Ordinal))
                {
                    return transform.gameObject;
                }
            }

            return null;
        }
    }
}
