using System;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Audio;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class OpenBetaPauseMenuController : MonoBehaviour
    {
        private const float MenuButtonIdleAlpha = 0.7f;
        private const float MenuButtonHoverAlpha = 1f;
        private const float PopupDuration = 0.14f;
        private const float ReferenceRefreshSeconds = 1f;
        private const int MenuButtonSortingOrder = 7000;
        private const int MenuPanelSortingOrder = 7100;
        private const string SaveKeyBgmMuted = "OpenBeta.Audio.BgmMuted";
        private const string SaveKeySeMuted = "OpenBeta.Audio.SeMuted";
        private const string GoTitleButtonKey = "OBT_GoTitleButton";
        private const string RirekiButtonKey = "OBT_RirekiButton";
        private const string TeachingResetButtonKey = "TEACH_RESET_BUTTON";
        private static readonly bool TeachingResetButtonEnabled = false;
        private const string DefaultEnqueteUrl = "https://forms.gle/";

        [Header("Scene References")]
        [SerializeField] private Button menuButton;
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private Button goTitleButton;
        [SerializeField] private Button bgmButton;
        [SerializeField] private Button seButton;
        [SerializeField] private Button logButton;
        [SerializeField] private Button teachingResetButton;
        [SerializeField] private Button enqueteButton;
        [SerializeField] private Button endButton;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private LogWindowPanel logWindowPanel;
        [SerializeField] private BackgroundMusicController backgroundMusicController;

        [Header("External Links")]
        [SerializeField] private string enqueteUrl = DefaultEnqueteUrl;

        private CanvasGroup menuButtonGroup;
        private CanvasGroup menuPanelGroup;
        private RectTransform menuPanelRect;
        private Canvas rootCanvas;
        private Vector3 menuPanelOpenScale = Vector3.one;
        private float previousTimeScale = 1f;
        private float popupElapsed;
        private bool isPaused;
        private bool isMenuButtonHovered;
        private bool initialConversationCompleted;
        private bool bgmMuted;
        private bool seMuted;
        private float nextReferenceRefreshTime;
        private readonly List<AudioSource> bgmSources = new List<AudioSource>();
        private readonly List<AudioSource> seSources = new List<AudioSource>();
        public bool IsPaused => isPaused;

        private void Awake()
        {
            ResolveReferences();
            ApplyTheme();
            BindButtons();
            LoadAudioState();
            RefreshAudioSources();
            SetMenuButtonVisible(false);
            SetMenuPanelVisible(false, true);
            RefreshLabels();
            ApplyPlatformButtonVisibility();
            ApplyAudioMuteState();
        }

        private void Update()
        {
            // Optional controls may not exist. Retrying their discovery every frame scans the whole scene.
            if (Time.unscaledTime >= nextReferenceRefreshTime)
            {
                ResolveReferences();
                RefreshAudioSources();
                ApplyAudioMuteState();
                nextReferenceRefreshTime = Time.unscaledTime + ReferenceRefreshSeconds;
            }
            DetectInitialConversationCompletion();
            UpdateMenuButtonAlpha();
            AnimateMenuPanel();

            if (initialConversationCompleted && Input.GetKeyDown(KeyCode.Escape))
            {
                ToggleMenu();
            }
        }

        private void OnDisable()
        {
            if (isPaused)
            {
                CloseMenu();
                SetMenuPanelVisible(false, true);
            }
        }

        public void ToggleMenu()
        {
            if (isPaused)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        public void TryToggleMenuFromExternalInput()
        {
            if (!initialConversationCompleted)
            {
                DetectInitialConversationCompletion();
            }

            if (!initialConversationCompleted)
            {
                return;
            }

            ToggleMenu();
        }

        public void OpenMenu()
        {
            if (isPaused || !isActiveAndEnabled)
            {
                return;
            }

            ResolveReferences();
            if (menuPanel == null)
            {
                return;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isPaused = true;
            chatUI?.SetMenuInputBlocked(true);
            if (chatUI != null)
            {
                WebGLImeInputBridge imeBridge = chatUI.GetComponent<WebGLImeInputBridge>();
                imeBridge?.SetSuspended(true);
            }

            SetMenuPanelVisible(true, false);
        }

        public void CloseMenu()
        {
            if (!isPaused)
            {
                return;
            }

            Time.timeScale = previousTimeScale;
            isPaused = false;
            chatUI?.SetMenuInputBlocked(false);
            if (chatUI != null)
            {
                WebGLImeInputBridge imeBridge = chatUI.GetComponent<WebGLImeInputBridge>();
                imeBridge?.SetSuspended(false);
            }

            SetMenuPanelVisible(false, false);
        }

        private void ResolveReferences()
        {
            menuButton ??= FindSceneButton("MenuButton");
            rootCanvas ??= ResolveRootCanvas();
            menuPanel ??= FindCanvasObject("MenuPanel") ?? FindSceneObject("MenuPanel");
            goTitleButton ??= FindMenuPanelButton("GoTitleButton") ?? FindSceneButton("GoTitleButton");
            bgmButton ??= FindMenuPanelButton("BGMButton") ?? FindSceneButton("BGMButton");
            seButton ??= FindMenuPanelButton("SEButton") ?? FindSceneButton("SEButton");
            logButton ??= FindMenuPanelButton("LogButton") ?? FindPauseLogButton();
            teachingResetButton ??= FindMenuPanelButton("TeachingResetButton");
            if (TeachingResetButtonEnabled)
            {
                teachingResetButton ??= CreateTeachingResetButton();
            }
            else if (teachingResetButton != null)
            {
                teachingResetButton.gameObject.SetActive(false);
            }
            enqueteButton ??= FindMenuPanelButton("EnqueteButton") ?? FindPauseEnqueteButton();
            endButton ??= FindMenuPanelButton("EndButton") ?? FindSceneButton("EndButton");
            chatUI ??= FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
            logWindowPanel ??= FindFirstObjectByType<LogWindowPanel>(FindObjectsInactive.Include);
            if (backgroundMusicController == null)
            {
                backgroundMusicController = FindFirstObjectByType<BackgroundMusicController>(FindObjectsInactive.Include);
            }

            if (menuButton != null && menuButtonGroup == null)
            {
                MoveObjectOutOfDialogueUi(menuButton.gameObject, true);

                menuButtonGroup = menuButton.GetComponent<CanvasGroup>();
                if (menuButtonGroup == null)
                {
                    menuButtonGroup = menuButton.gameObject.AddComponent<CanvasGroup>();
                }

                menuButtonGroup.ignoreParentGroups = true;
                PauseMenuHoverRelay relay = menuButton.GetComponent<PauseMenuHoverRelay>();
                if (relay == null)
                {
                    relay = menuButton.gameObject.AddComponent<PauseMenuHoverRelay>();
                }

                relay.Bind(this);
                EnsureOverlayCanvas(menuButton.gameObject, MenuButtonSortingOrder);
                RectTransform buttonRect = menuButton.GetComponent<RectTransform>();
                if (buttonRect != null)
                {
                    buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 1f);
                    buttonRect.pivot = new Vector2(0f, 1f);
                    buttonRect.anchoredPosition = new Vector2(12f, -12f);
                    buttonRect.sizeDelta = new Vector2(64f, 64f);
                }
            }

            if (menuPanel != null && menuPanelGroup == null)
            {
                MoveMenuPanelOutOfDialogueUi();
                EnsureOverlayCanvas(menuPanel, MenuPanelSortingOrder);

                menuPanelGroup = menuPanel.GetComponent<CanvasGroup>();
                if (menuPanelGroup == null)
                {
                    menuPanelGroup = menuPanel.AddComponent<CanvasGroup>();
                }

                menuPanelGroup.ignoreParentGroups = true;
                menuPanelRect = menuPanel.GetComponent<RectTransform>();
                if (menuPanelRect != null)
                {
                    menuPanelOpenScale = menuPanelRect.localScale == Vector3.zero ? Vector3.one : menuPanelRect.localScale;
                }

                PauseMenuPanelActivationRelay relay = menuPanel.GetComponent<PauseMenuPanelActivationRelay>();
                if (relay == null)
                {
                    relay = menuPanel.AddComponent<PauseMenuPanelActivationRelay>();
                }

                relay.Bind(this);
                EnsureMenuDismissControls();
            }

            ApplyPlatformButtonVisibility();
        }

        private void BindButtons()
        {
            Bind(menuButton, ToggleMenu);
            Bind(goTitleButton, GoTitle);
            Bind(bgmButton, ToggleBgm);
            Bind(seButton, ToggleSe);
            Bind(logButton, OpenLogFromMenu);
            if (TeachingResetButtonEnabled)
            {
                Bind(teachingResetButton, OpenTeachingResetConfirm);
            }
            Bind(enqueteButton, OpenEnquete);
            Bind(endButton, QuitGame);
        }

        private void ApplyTheme()
        {
            if (menuPanel != null)
            {
                UIStyle.ApplyPanel(menuPanel);
            }

            ApplyButtonStyle(menuButton);
            ApplyButtonStyle(goTitleButton);
            ApplyButtonStyle(bgmButton);
            ApplyButtonStyle(seButton);
            ApplyButtonStyle(logButton);
            if (TeachingResetButtonEnabled)
            {
                ApplyButtonStyle(teachingResetButton);
            }
            ApplyButtonStyle(enqueteButton);
            ApplyButtonStyle(endButton);
        }

        private void RefreshLabels()
        {
            SetButtonLabel(goTitleButton, BasicSystemDialogueCatalog.Get(GoTitleButtonKey, "タイトルへ"));
            SetButtonLabel(bgmButton, bgmMuted ? "BGM OFF" : "BGM ON");
            SetButtonLabel(seButton, seMuted ? "SE OFF" : "SE ON");
            SetButtonLabel(logButton, BasicSystemDialogueCatalog.Get(RirekiButtonKey, "履歴"));
            if (TeachingResetButtonEnabled)
            {
                SetButtonLabel(teachingResetButton, BasicSystemDialogueCatalog.Get(TeachingResetButtonKey, "教えた内容を初期化"));
            }
            SetButtonLabel(enqueteButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.EnqueteButton, "アンケートに回答"));
            SetButtonLabel(endButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.QuitButton, "ゲーム終了"));
        }

        private void ApplyPlatformButtonVisibility()
        {
            if (endButton == null)
            {
                return;
            }

            endButton.gameObject.SetActive(!ShouldHideQuitButtonInCurrentBuild());
        }

        private static bool ShouldHideQuitButtonInCurrentBuild()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void DetectInitialConversationCompletion()
        {
            if (initialConversationCompleted || chatUI == null)
            {
                return;
            }

            InputField input = chatUI.chatInputField;
            bool inputReady = input != null && input.gameObject.activeInHierarchy && input.interactable;
            bool conversationVisible = chatUI.IsInDialogueMode || chatUI.IsTyping;
            if (inputReady || conversationVisible)
            {
                initialConversationCompleted = true;
                SetMenuButtonVisible(true);
            }
        }

        private void SetMenuButtonVisible(bool visible)
        {
            if (menuButton == null)
            {
                return;
            }

            menuButton.gameObject.SetActive(visible);
            if (menuButtonGroup != null)
            {
                menuButtonGroup.alpha = visible ? MenuButtonIdleAlpha : 0f;
                menuButtonGroup.interactable = visible;
                menuButtonGroup.blocksRaycasts = visible;
            }
        }

        private void SetMenuPanelVisible(bool visible, bool instant)
        {
            if (menuPanel == null)
            {
                return;
            }

            if (!visible && instant)
            {
                if (menuPanelGroup != null)
                {
                    menuPanelGroup.alpha = 1f;
                    menuPanelGroup.interactable = false;
                    menuPanelGroup.blocksRaycasts = false;
                }

                if (menuPanelRect != null)
                {
                    menuPanelRect.localScale = menuPanelOpenScale * 0.92f;
                }

                menuPanel.SetActive(false);
                return;
            }

            menuPanel.SetActive(true);
            if (menuPanelGroup != null)
            {
                menuPanelGroup.alpha = 1f;
                menuPanelGroup.interactable = visible;
                menuPanelGroup.blocksRaycasts = visible;
            }

            popupElapsed = visible ? 0f : PopupDuration;

            if (instant)
            {
                if (menuPanelGroup != null)
                {
                    menuPanelGroup.alpha = visible ? 1f : 0f;
                }

                if (menuPanelRect != null)
                {
                    menuPanelRect.localScale = visible ? menuPanelOpenScale : menuPanelOpenScale * 0.92f;
                }

                menuPanel.SetActive(visible);
            }
        }

        private void AnimateMenuPanel()
        {
            if (menuPanel == null || !menuPanel.activeSelf)
            {
                return;
            }

            float target = isPaused ? PopupDuration : 0f;
            popupElapsed = Mathf.MoveTowards(popupElapsed, target, Time.unscaledDeltaTime);
            float t = PopupDuration <= 0f ? 1f : Mathf.Clamp01(popupElapsed / PopupDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (menuPanelGroup != null)
            {
                menuPanelGroup.alpha = 1f;
            }

            if (menuPanelRect != null)
            {
                menuPanelRect.localScale = Vector3.Lerp(menuPanelOpenScale * 0.92f, menuPanelOpenScale, eased);
            }

            if (!isPaused && popupElapsed <= 0f)
            {
                menuPanel.SetActive(false);
            }
        }

        private void UpdateMenuButtonAlpha()
        {
            if (menuButtonGroup == null || !initialConversationCompleted || isPaused)
            {
                return;
            }

            float targetAlpha = isMenuButtonHovered ? MenuButtonHoverAlpha : MenuButtonIdleAlpha;
            menuButtonGroup.alpha = Mathf.MoveTowards(menuButtonGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 8f);
        }

        private void MoveMenuPanelOutOfDialogueUi()
        {
            if (menuPanel == null)
            {
                return;
            }

            MoveObjectOutOfDialogueUi(menuPanel, true);
        }

        private void MoveObjectOutOfDialogueUi(GameObject target, bool setAsLastSibling)
        {
            if (target == null)
            {
                return;
            }

            rootCanvas ??= target.GetComponentInParent<Canvas>(true);
            if (rootCanvas == null)
            {
                rootCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            }

            Transform canvasTransform = rootCanvas != null ? rootCanvas.transform : null;
            if (canvasTransform == null || target.transform.parent == canvasTransform)
            {
                if (setAsLastSibling)
                {
                    target.transform.SetAsLastSibling();
                }

                return;
            }

            RectTransform rect = target.GetComponent<RectTransform>();
            Vector2 anchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            Vector2 anchorMin = rect != null ? rect.anchorMin : Vector2.zero;
            Vector2 anchorMax = rect != null ? rect.anchorMax : Vector2.one;
            Vector2 pivot = rect != null ? rect.pivot : new Vector2(0.5f, 0.5f);
            Vector2 sizeDelta = rect != null ? rect.sizeDelta : Vector2.zero;
            Vector3 localScale = target.transform.localScale;

            target.transform.SetParent(canvasTransform, false);
            if (setAsLastSibling)
            {
                target.transform.SetAsLastSibling();
            }

            if (rect != null)
            {
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.sizeDelta = sizeDelta;
                rect.anchoredPosition = anchoredPosition;
            }

            target.transform.localScale = localScale;
        }

        private void ToggleBgm()
        {
            bgmMuted = !bgmMuted;
            PlayerPrefs.SetInt(SaveKeyBgmMuted, bgmMuted ? 1 : 0);
            PlayerPrefs.Save();
            RefreshAudioSources();
            ApplyBgmMuteState();
            RefreshLabels();
        }

        private void ToggleSe()
        {
            seMuted = !seMuted;
            PlayerPrefs.SetInt(SaveKeySeMuted, seMuted ? 1 : 0);
            PlayerPrefs.Save();
            RefreshAudioSources();
            ApplySeMuteStateToRuntimeSources();
            RefreshLabels();
        }

        private void LoadAudioState()
        {
            bgmMuted = PlayerPrefs.GetInt(SaveKeyBgmMuted, 0) != 0;
            seMuted = PlayerPrefs.GetInt(SaveKeySeMuted, 0) != 0;
        }

        private void ApplyAudioMuteState()
        {
            ApplyBgmMuteState();
            ApplySeMuteStateToRuntimeSources();
        }

        private void ApplyBgmMuteState()
        {
            for (int i = 0; i < bgmSources.Count; i++)
            {
                if (bgmSources[i] != null)
                {
                    bgmSources[i].mute = bgmMuted;
                }
            }
        }

        private void ApplySeMuteStateToRuntimeSources()
        {
            for (int i = 0; i < seSources.Count; i++)
            {
                AudioSource source = seSources[i];
                if (source == null)
                {
                    continue;
                }

                source.mute = seMuted;
            }
        }

        private void RefreshAudioSources()
        {
            bgmSources.Clear();
            seSources.Clear();
            AudioSource[] sources = FindSceneComponents<AudioSource>();
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source.GetComponentInParent<BackgroundMusicController>(true) != null)
                    bgmSources.Add(source);
                else
                    seSources.Add(source);
            }
        }

        // Audio created after the menu's discovery pass must start muted, before its first Play call.
        public static void ApplySavedAudioState(AudioSource source, bool isBgm)
        {
            if (source != null)
                source.mute = PlayerPrefs.GetInt(isBgm ? SaveKeyBgmMuted : SaveKeySeMuted, 0) != 0;
        }

        private void EnsureMenuDismissControls()
        {
            if (rootCanvas == null || menuPanel == null) return;

            if (menuPanel.transform.Find("CloseMenuButton") != null) return;
            GameObject closeObject = new GameObject("CloseMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(menuPanel.transform, false);
            RectTransform closeRect = (RectTransform)closeObject.transform;
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-12f, -12f);
            closeRect.sizeDelta = new Vector2(64f, 64f);
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(closeObject.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            TMP_Text existingLabel = menuPanel.GetComponentInChildren<TMP_Text>(true);
            if (existingLabel != null) label.font = existingLabel.font;
            label.text = "×";
            label.fontSize = 36f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            Button closeButton = closeObject.GetComponent<Button>();
            closeButton.targetGraphic = closeObject.GetComponent<Image>();
            closeButton.onClick.AddListener(CloseMenu);
            UIStyle.ApplyButton(closeButton);
        }

        private void OpenLogFromMenu()
        {
            CloseMenu();

            if (chatUI != null)
            {
                chatUI.OpenLogWindowFromExternalContext();
                return;
            }

            logWindowPanel?.Open();
        }

        private void OpenTeachingResetConfirm()
        {
            CloseMenu();
            DialogueEngine.Instance?.TryHandleTeachingAction("TeachingMode:ResetConfirm", true);
        }

        private void OpenEnquete()
        {
            if (string.IsNullOrWhiteSpace(enqueteUrl))
            {
                return;
            }

            Application.OpenURL(enqueteUrl);
        }

        private void GoTitle()
        {
            Debug.Log($"[OpenBetaPause][GoTitle] begin frame={Time.frameCount} timeScale={Time.timeScale} scene={SceneManager.GetActiveScene().name}");
            CloseMenu();
            Time.timeScale = 1f;
            ResetOpenBetaState();
            GameManager.Instance?.ResetForTitleScreen();
            PlayTitleBgmNow();
            Debug.Log($"[OpenBetaPause][GoTitle] loading title scene={OpenBetaSceneNames.Title} frame={Time.frameCount}");
            SceneManager.LoadScene(OpenBetaSceneNames.Title, LoadSceneMode.Single);
        }

        private void QuitGame()
        {
            Time.timeScale = 1f;
            Application.Quit();
        }

        private static void ResetOpenBetaState()
        {
            OpenBetaTitleBootstrap[] bootstraps = FindSceneComponents<OpenBetaTitleBootstrap>();
            Debug.Log($"[OpenBetaPause][GoTitle] ResetOpenBetaState titleBootstraps={bootstraps.Length}");
            for (int i = 0; i < bootstraps.Length; i++)
            {
                bootstraps[i]?.AbortRuntimeFlowForTitleReload();
            }

            ConversationGameStateManager[] managers = FindSceneComponents<ConversationGameStateManager>();
            Debug.Log($"[OpenBetaPause][GoTitle] ResetOpenBetaState conversationStateManagers={managers.Length}");
            for (int i = 0; i < managers.Length; i++)
            {
                managers[i]?.ResetStateFromInitialJson();
            }

            ChatUIController[] chatUis = FindSceneComponents<ChatUIController>();
            Debug.Log($"[OpenBetaPause][GoTitle] ResetOpenBetaState chatUIs={chatUis.Length} dialogueManager={(DialogueManager.Instance != null)} dialogueEngine={(DialogueEngine.Instance != null)}");
            for (int i = 0; i < chatUis.Length; i++)
            {
                chatUis[i]?.ResetRuntimeStateForTitleRestart(true);
            }

            DialogueManager.Instance?.ResetRuntimeStateForTitleRestart();
            DialogueEngine.Instance?.ResetRuntimeConversationState();

            if (GameFlagManager.Instance != null)
            {
                int flagCount = GameFlagManager.Instance.GetAllFlags().Count;
                GameFlagManager.Instance.ClearTemporaryFlags();
                Debug.Log($"[OpenBetaPause][GoTitle] ResetOpenBetaState gameFlagsCleared={flagCount}");
            }
            else
            {
                Debug.Log("[OpenBetaPause][GoTitle] ResetOpenBetaState gameFlagsSkipped: instance is null.");
            }
        }

        private void PlayTitleBgmNow()
        {
            if (backgroundMusicController == null)
            {
                backgroundMusicController = FindFirstObjectByType<BackgroundMusicController>(FindObjectsInactive.Include);
            }

            if (backgroundMusicController != null)
            {
                backgroundMusicController.PlayTitleMusic(true);
            }

            ApplyBgmMuteState();
        }

        private void SetHover(bool hovered)
        {
            isMenuButtonHovered = hovered;
            if (menuButtonGroup != null && initialConversationCompleted && !isPaused)
            {
                menuButtonGroup.alpha = hovered ? MenuButtonHoverAlpha : MenuButtonIdleAlpha;
            }
        }

        private void HandleMenuPanelEnabled()
        {
            ResolveReferences();
            if (menuPanelGroup == null)
            {
                return;
            }

            menuPanelGroup.alpha = 1f;
            menuPanelGroup.interactable = true;
            menuPanelGroup.blocksRaycasts = true;
        }

        private static Button FindPauseLogButton()
        {
            Button[] buttons = FindSceneComponents<Button>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button != null &&
                    string.Equals(button.gameObject.name, "LogButton", StringComparison.Ordinal) &&
                    HasAncestorNamed(button.transform, "MenuPanel"))
                {
                    return button;
                }
            }

            return null;
        }

        private static Button FindPauseEnqueteButton()
        {
            Button[] buttons = FindSceneComponents<Button>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button != null &&
                    string.Equals(button.gameObject.name, "EnqueteButton", StringComparison.Ordinal) &&
                    HasAncestorNamed(button.transform, "MenuPanel"))
                {
                    return button;
                }
            }

            return null;
        }

        private static Button FindSceneButton(string objectName)
        {
            GameObject target = FindSceneObject(objectName);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private Button FindMenuPanelButton(string objectName)
        {
            Transform target = menuPanel != null ? FindDescendant(menuPanel.transform, objectName) : null;
            return target != null ? target.GetComponent<Button>() : null;
        }

        private Button CreateTeachingResetButton()
        {
            if (menuPanel == null || logButton == null)
            {
                return null;
            }

            GameObject clone = Instantiate(logButton.gameObject, logButton.transform.parent, false);
            clone.name = "TeachingResetButton";
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            RectTransform logRect = logButton.GetComponent<RectTransform>();
            if (cloneRect != null && logRect != null)
            {
                cloneRect.anchoredPosition = logRect.anchoredPosition + new Vector2(0f, -44f);
            }

            Button button = clone.GetComponent<Button>();
            ApplyButtonStyle(button);
            return button;
        }

        private Canvas ResolveRootCanvas()
        {
            if (rootCanvas != null)
            {
                return rootCanvas;
            }

            if (menuButton != null)
            {
                Canvas canvas = menuButton.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                {
                    return canvas;
                }
            }

            if (menuPanel != null)
            {
                Canvas canvas = menuPanel.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                {
                    return canvas;
                }
            }

            return FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        }

        private GameObject FindCanvasObject(string objectName)
        {
            if (rootCanvas == null || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            Transform target = FindDescendant(rootCanvas.transform, objectName);
            return target != null ? target.gameObject : null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            GameObject[] candidates = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < candidates.Length; i++)
            {
                GameObject candidate = candidates[i];
                if (candidate != null &&
                    candidate.scene.IsValid() &&
                    string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, objectName, StringComparison.Ordinal))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindDescendant(root.GetChild(i), objectName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static T[] FindSceneComponents<T>() where T : Component
        {
            T[] candidates = Resources.FindObjectsOfTypeAll<T>();
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].gameObject.scene.IsValid())
                {
                    count++;
                }
            }

            T[] result = new T[count];
            int index = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].gameObject.scene.IsValid())
                {
                    result[index++] = candidates[i];
                }
            }

            return result;
        }

        private static bool HasAncestorNamed(Transform transform, string objectName)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (string.Equals(current.name, objectName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyButtonStyle(Button button)
        {
            if (button != null)
            {
                UIStyle.ApplyButton(button);
            }
        }

        private static void EnsureOverlayCanvas(GameObject target, int sortingOrder)
        {
            if (target == null)
            {
                return;
            }

            Canvas canvas = target.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = target.AddComponent<Canvas>();
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            if (target.GetComponent<GraphicRaycaster>() == null)
            {
                target.AddComponent<GraphicRaycaster>();
            }
        }

        private static void Bind(Button button, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label ?? string.Empty;
            }
        }

        private sealed class PauseMenuHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private OpenBetaPauseMenuController owner;

            public void Bind(OpenBetaPauseMenuController controller)
            {
                owner = controller;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                owner?.SetHover(true);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                owner?.SetHover(false);
            }

        }

        private sealed class PauseMenuPanelActivationRelay : MonoBehaviour
        {
            private OpenBetaPauseMenuController owner;

            public void Bind(OpenBetaPauseMenuController controller)
            {
                owner = controller;
            }

            private void OnEnable()
            {
                owner?.HandleMenuPanelEnabled();
            }
        }
    }
}
