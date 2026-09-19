using Backgammon.Conversation;
using Nekolpos.System;
using Nekolpos.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.EditorTools
{
    public static class OpenBetaTitleSceneBuilder
    {
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
        private const string OpenBetaYarnProjectGuid = "de6c9c325317d0a4898dff8bf9306d27";
        private const string PreferredTmpFontGuid = "20f1fc86d77d8e644a9eb04477e8615e";

        public static void BuildTitleSceneUi()
        {
            Debug.Log("[OpenBetaTitleSceneBuilder] BuildTitleSceneUi started.");
            var scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            BuildInOpenScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[OpenBetaTitleSceneBuilder] BuildTitleSceneUi finished.");
        }

        public static void BuildInOpenScene()
        {
            Debug.Log("[OpenBetaTitleSceneBuilder] BuildInOpenScene started.");
            EnsureEventSystem();

            GameObject canvasObject = GameObject.Find("OpenBetaCanvas");
            if (canvasObject != null)
            {
                Debug.LogError(
                    "[OpenBetaTitleSceneBuilder] Refusing to rebuild an existing OpenBetaCanvas. " +
                    "This builder is only for scaffolding a new scene; edit existing UI with targeted operations.");
                return;
            }

            canvasObject = new GameObject("OpenBetaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.localScale = Vector3.one;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            OpenBetaIntroPopupPanel introPanel = BuildIntroPanel(canvasObject.transform);
            OpenBetaAboutPanel aboutPanel = BuildAboutPanel(canvasObject.transform);
            OpenBetaCharacterSetupPanel characterSetupPanel = BuildCharacterSetupPanel(canvasObject.transform);
            OpenBetaCallCatPanel callCatPanel = BuildCallCatPanel(canvasObject.transform);
            ChatUIController chatUi = BuildDialogueUi(canvasObject.transform);

            GameObject systems = GameObject.Find("OpenBetaSystems");
            if (systems == null)
            {
                systems = new GameObject("OpenBetaSystems");
            }

            ConversationGameStateManager gameStateManager = GetOrAdd<ConversationGameStateManager>(systems);
            ConversationDataManager conversationDataManager = GetOrAdd<ConversationDataManager>(systems);
            DialogueManager dialogueManager = GetOrAdd<DialogueManager>(systems);
            DialogueEngine dialogueEngine = GetOrAdd<DialogueEngine>(systems);
            DialogueLogManager dialogueLogManager = GetOrAdd<DialogueLogManager>(systems);
            YarnProject yarnProject = LoadOpenBetaYarnProject();
            YarnManager yarnManager = BuildYarnSystem(yarnProject);
            OpenBetaTitleBootstrap bootstrap = GetOrAdd<OpenBetaTitleBootstrap>(systems);
            CatPresentationModeController catPresentationMode = FindExistingCatPresentationModeController()
                ?? GetOrAdd<CatPresentationModeController>(systems);
            PlayableDirector director = Object.FindFirstObjectByType<PlayableDirector>();

            dialogueManager.chatUI = chatUi;

            SerializedObject bootstrapObject = new SerializedObject(bootstrap);
            SetObject(bootstrapObject, "introPanel", introPanel);
            SetObject(bootstrapObject, "aboutPanel", aboutPanel);
            SetObject(bootstrapObject, "characterSetupPanel", characterSetupPanel);
            SetObject(bootstrapObject, "callCatPanel", callCatPanel);
            SetObject(bootstrapObject, "chatUI", chatUi);
            SetObject(bootstrapObject, "gameStateManager", gameStateManager);
            SetObject(bootstrapObject, "dialogueManager", dialogueManager);
            SetObject(bootstrapObject, "dialogueEngine", dialogueEngine);
            SetObject(bootstrapObject, "dialogueLogManager", dialogueLogManager);
            SetObject(bootstrapObject, "yarnManager", yarnManager);
            SetObject(bootstrapObject, "conversationDataManager", conversationDataManager);
            SetObject(bootstrapObject, "timelineDirector", director);
            SetObject(bootstrapObject, "catPresentationMode", catPresentationMode);
            SetObject(bootstrapObject, "yarnProject", yarnProject);
            bootstrapObject.ApplyModifiedPropertiesWithoutUndo();

            introPanel.gameObject.SetActive(true);
            aboutPanel.gameObject.SetActive(false);
            characterSetupPanel.gameObject.SetActive(false);
            callCatPanel.gameObject.SetActive(false);
            ApplyPreferredFont(canvasObject.transform);
            Debug.Log("[OpenBetaTitleSceneBuilder] BuildInOpenScene finished.");
        }

        private static CatPresentationModeController FindExistingCatPresentationModeController()
        {
            CatPresentationModeController[] controllers = Object.FindObjectsByType<CatPresentationModeController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            CatPresentationModeController fallback = null;
            for (int i = 0; i < controllers.Length; i++)
            {
                CatPresentationModeController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                fallback ??= controller;
                if (controller.OpCat != null && controller.NormalCat != null)
                {
                    return controller;
                }
            }

            return fallback;
        }

        private static OpenBetaIntroPopupPanel BuildIntroPanel(Transform parent)
        {
            GameObject panel = GetOrCreatePanel("IntroPopupPanel", parent);
            OpenBetaIntroPopupPanel component = GetOrAdd<OpenBetaIntroPopupPanel>(panel);
            ClearChildren(panel.transform);

            TextMeshProUGUI body = OpenBetaUiFactory.CreateBodyText(panel.transform);
            TextMeshProUGUI precautions = OpenBetaUiFactory.CreateTmpText("PrecautionsText", panel.transform, 18f, FontStyles.Normal);
            precautions.richText = true;
            precautions.alignment = TextAlignmentOptions.Center;
            AddLayout(precautions.gameObject, 660f, 34f);
            Button startButton = OpenBetaUiFactory.CreateButton("StartDemoButton", panel.transform, out TextMeshProUGUI startLabel, new Vector2(360f, 48f));
            Button aboutButton = OpenBetaUiFactory.CreateButton("AboutButton", panel.transform, out TextMeshProUGUI aboutLabel, new Vector2(360f, 48f));
            Button quitButton = OpenBetaUiFactory.CreateButton("QuitButton", panel.transform, out TextMeshProUGUI quitLabel, new Vector2(360f, 48f));

            SerializedObject so = new SerializedObject(component);
            SetObject(so, "bodyText", body);
            SetObject(so, "precautionsText", precautions);
            SetObject(so, "startButtonLabel", startLabel);
            SetObject(so, "aboutButtonLabel", aboutLabel);
            SetObject(so, "quitButtonLabel", quitLabel);
            SetObject(so, "startButton", startButton);
            SetObject(so, "aboutButton", aboutButton);
            SetObject(so, "quitButton", quitButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static OpenBetaAboutPanel BuildAboutPanel(Transform parent)
        {
            GameObject panel = GetOrCreatePanel("AboutPanel", parent);
            OpenBetaAboutPanel component = GetOrAdd<OpenBetaAboutPanel>(panel);
            ClearChildren(panel.transform);

            TextMeshProUGUI body = OpenBetaUiFactory.CreateBodyText(panel.transform);
            Button steamButton = OpenBetaUiFactory.CreateButton("SteamButton", panel.transform, out TextMeshProUGUI steamLabel, new Vector2(360f, 48f));
            Button backButton = OpenBetaUiFactory.CreateButton("BackButton", panel.transform, out TextMeshProUGUI backLabel, new Vector2(360f, 48f));

            SerializedObject so = new SerializedObject(component);
            SetObject(so, "bodyText", body);
            SetObject(so, "steamButtonLabel", steamLabel);
            SetObject(so, "backButtonLabel", backLabel);
            SetObject(so, "steamButton", steamButton);
            SetObject(so, "backButton", backButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static OpenBetaCharacterSetupPanel BuildCharacterSetupPanel(Transform parent)
        {
            GameObject panel = GetOrCreatePanel("CharacterSetupPanel", parent);
            OpenBetaCharacterSetupPanel component = GetOrAdd<OpenBetaCharacterSetupPanel>(panel);
            ClearChildren(panel.transform);

            TextMeshProUGUI title = OpenBetaUiFactory.CreateTmpText("TitleText", panel.transform, 30f, FontStyles.Bold);
            AddLayout(title.gameObject, 660f, 44f);
            TextMeshProUGUI playerNameLabel = CreateInputRow(panel.transform, "PlayerName", out InputField playerNameInput);
            TextMeshProUGUI catNameLabel = CreateInputRow(panel.transform, "CatName", out InputField catNameInput);
            catNameInput.characterLimit = 20;
            TextMeshProUGUI playerCallingLabel = CreateInputRow(panel.transform, "PlayerCalling", out InputField playerCallingInput);
            TextMeshProUGUI notice = OpenBetaUiFactory.CreateTmpText("RequiredNotice", panel.transform, 20f, FontStyles.Normal);
            notice.color = new Color(1f, 0.72f, 0.52f, 1f);
            notice.gameObject.SetActive(false);
            Button continueButton = OpenBetaUiFactory.CreateButton("ContinueButton", panel.transform, out TextMeshProUGUI continueLabel, new Vector2(260f, 48f));
            Button backButton = OpenBetaUiFactory.CreateButton("BackButton", panel.transform, out TextMeshProUGUI backLabel, new Vector2(260f, 48f));

            SerializedObject so = new SerializedObject(component);
            SetObject(so, "titleText", title);
            SetObject(so, "playerNameLabel", playerNameLabel);
            SetObject(so, "catNameLabel", catNameLabel);
            SetObject(so, "playerCallingLabel", playerCallingLabel);
            SetObject(so, "continueButtonLabel", continueLabel);
            SetObject(so, "backButtonLabel", backLabel);
            SetObject(so, "noticeText", notice);
            SetObject(so, "playerNameInput", playerNameInput);
            SetObject(so, "catNameInput", catNameInput);
            SetObject(so, "playerCallingInput", playerCallingInput);
            SetObject(so, "continueButton", continueButton);
            SetObject(so, "backButton", backButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static OpenBetaCallCatPanel BuildCallCatPanel(Transform parent)
        {
            GameObject panel = GetOrCreatePanel("CallCatPanel", parent);
            OpenBetaCallCatPanel component = GetOrAdd<OpenBetaCallCatPanel>(panel);
            ClearChildren(panel.transform);

            TextMeshProUGUI body = OpenBetaUiFactory.CreateBodyText(panel.transform);
            InputField input = OpenBetaUiFactory.CreateInputField("CallCatInput", panel.transform, new Vector2(360f, 48f));
            Button sendButton = OpenBetaUiFactory.CreateButton("SendButton", panel.transform, out TextMeshProUGUI sendLabel, new Vector2(240f, 48f));

            SerializedObject so = new SerializedObject(component);
            SetObject(so, "bodyText", body);
            SetObject(so, "sendButtonLabel", sendLabel);
            SetObject(so, "callInput", input);
            SetObject(so, "sendButton", sendButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static ChatUIController BuildDialogueUi(Transform parent)
        {
            GameObject root = GameObject.Find("DialogueUI");
            bool createdRoot = root == null;
            if (createdRoot)
            {
                root = new GameObject("DialogueUI", typeof(RectTransform));
                root.transform.SetParent(parent, false);
            }

            OpenBetaUiFactory.Stretch(root.GetComponent<RectTransform>());
            ChatUIController controller = GetOrAdd<ChatUIController>(root);
            controller.chatWindowGroup ??= FindChildComponent<CanvasGroup>(root.transform, "ChatWindowPanel");
            controller.speakerNameText ??= FindChildComponent<TextMeshProUGUI>(root.transform, "SpeakerName");
            controller.messageText ??= FindChildComponent<TextMeshProUGUI>(root.transform, "MessageText");
            controller.chatInputField ??= FindChildComponent<InputField>(root.transform, "ChatInputField");
            controller.choicePanel ??= FindChildGameObject(root.transform, "ChoicePanel");
            controller.choiceAcceptButton ??= FindChildComponent<Button>(root.transform, "ChoiceAcceptButton");
            controller.choiceDeclineButton ??= FindChildComponent<Button>(root.transform, "ChoiceDeclineButton");
            controller.logButton ??= FindChildComponent<Button>(root.transform, "LogButton");

            if (createdRoot || controller.chatWindowGroup == null)
            {
                controller.chatWindowGroup = OpenBetaUiFactory.CreateCanvasGroupPanel("ChatWindowPanel", root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(860f, 180f), new Vector2(0f, 110f));
            }

            if (createdRoot || controller.speakerNameText == null)
            {
                controller.speakerNameText = OpenBetaUiFactory.CreateTmpText("SpeakerName", controller.chatWindowGroup.transform, 24f, FontStyles.Bold);
                RectTransform speakerRect = controller.speakerNameText.rectTransform;
                speakerRect.anchorMin = new Vector2(0f, 1f);
                speakerRect.anchorMax = new Vector2(1f, 1f);
                speakerRect.offsetMin = new Vector2(24f, -48f);
                speakerRect.offsetMax = new Vector2(-24f, -12f);
            }

            if (createdRoot || controller.messageText == null)
            {
                controller.messageText = OpenBetaUiFactory.CreateTmpText("MessageText", controller.chatWindowGroup.transform, 26f, FontStyles.Normal);
                RectTransform messageRect = controller.messageText.rectTransform;
                messageRect.anchorMin = Vector2.zero;
                messageRect.anchorMax = Vector2.one;
                messageRect.offsetMin = new Vector2(24f, 18f);
                messageRect.offsetMax = new Vector2(-24f, -54f);
            }

            if (createdRoot || controller.chatInputField == null)
            {
                controller.chatInputField = OpenBetaUiFactory.CreateInputField("ChatInputField", root.transform, new Vector2(680f, 54f));
                RectTransform inputRect = controller.chatInputField.GetComponent<RectTransform>();
                inputRect.anchorMin = new Vector2(0.5f, 0f);
                inputRect.anchorMax = new Vector2(0.5f, 0f);
                inputRect.anchoredPosition = new Vector2(0f, 42f);
            }

            if (createdRoot || controller.choicePanel == null)
            {
                controller.choicePanel = OpenBetaUiFactory.CreatePanel("ChoicePanel", root.transform);
                RectTransform choiceRect = controller.choicePanel.GetComponent<RectTransform>();
                choiceRect.anchorMin = new Vector2(0.5f, 0.5f);
                choiceRect.anchorMax = new Vector2(0.5f, 0.5f);
                choiceRect.sizeDelta = new Vector2(420f, 180f);
                choiceRect.anchoredPosition = Vector2.zero;
            }

            if (createdRoot || controller.choiceAcceptButton == null)
            {
                controller.choiceAcceptButton = OpenBetaUiFactory.CreateButton("ChoiceAcceptButton", controller.choicePanel.transform, out _, new Vector2(300f, 48f));
            }

            if (createdRoot || controller.choiceDeclineButton == null)
            {
                controller.choiceDeclineButton = OpenBetaUiFactory.CreateButton("ChoiceDeclineButton", controller.choicePanel.transform, out _, new Vector2(300f, 48f));
            }

            if (controller.logButton == null)
            {
                controller.logButton = OpenBetaUiFactory.CreateButton("LogButton", root.transform, out TextMeshProUGUI logButtonLabel, new Vector2(120f, 40f));
                RectTransform logButtonRect = controller.logButton.GetComponent<RectTransform>();
                logButtonRect.anchorMin = new Vector2(1f, 0f);
                logButtonRect.anchorMax = new Vector2(1f, 0f);
                logButtonRect.pivot = new Vector2(1f, 0f);
                logButtonRect.anchoredPosition = new Vector2(-24f, 24f);
                logButtonLabel.text = "履歴";
            }

            BuildLogWindow(root.transform, controller);
            ApplyPreferredFont(root.transform);
            return controller;
        }

        private static void BuildLogWindow(Transform root, ChatUIController controller)
        {
            Transform existingPanel = root.Find("LogWindowPanel");
            GameObject logPanel = existingPanel != null
                ? existingPanel.gameObject
                : OpenBetaUiFactory.CreatePanel("LogWindowPanel", root);
            LogWindowPanel logWindowPanel = GetOrAdd<LogWindowPanel>(logPanel);
            logPanel.SetActive(false);

            // CreatePanel adds a layout group for ordinary stacked panels. The log window
            // uses anchored overlay controls, so leaving it here rewrites every child rect
            // and can collapse the ScrollView to a negative size.
            VerticalLayoutGroup panelLayout = logPanel.GetComponent<VerticalLayoutGroup>();
            if (panelLayout != null)
            {
                UnityEngine.Object.DestroyImmediate(panelLayout);
            }

            RectTransform panelRect = logPanel.GetComponent<RectTransform>();
            OpenBetaUiFactory.Stretch(panelRect);
            Image panelImage = logPanel.GetComponent<Image>();
            if (panelImage != null)
            {
                UIStyle.ApplyPanel(logPanel);
            }

            TextMeshProUGUI summaryText = FindDirectChildComponent<TextMeshProUGUI>(logPanel.transform, "HistorySummaryText")
                ?? OpenBetaUiFactory.CreateTmpText("HistorySummaryText", logPanel.transform, 16f, FontStyles.Normal);
            summaryText.alignment = TextAlignmentOptions.MidlineLeft;
            summaryText.color = new Color(0.22f, 0.25f, 0.29f, 1f);
            RectTransform summaryRect = summaryText.rectTransform;
            summaryRect.anchorMin = new Vector2(0f, 1f);
            summaryRect.anchorMax = new Vector2(0f, 1f);
            summaryRect.pivot = new Vector2(0f, 1f);
            summaryRect.anchoredPosition = new Vector2(32f, -28f);
            summaryRect.sizeDelta = new Vector2(320f, 32f);

            TextMeshProUGUI precautionsText = FindDirectChildComponent<TextMeshProUGUI>(logPanel.transform, "LOGPrecautionsText")
                ?? OpenBetaUiFactory.CreateTmpText("LOGPrecautionsText", logPanel.transform, 15f, FontStyles.Normal);
            precautionsText.richText = true;
            precautionsText.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform precautionsRect = precautionsText.rectTransform;
            precautionsRect.anchorMin = new Vector2(0f, 1f);
            precautionsRect.anchorMax = new Vector2(0f, 1f);
            precautionsRect.pivot = new Vector2(0f, 1f);
            precautionsRect.anchoredPosition = new Vector2(32f, -62f);
            precautionsRect.sizeDelta = new Vector2(520f, 32f);

            Button deleteButton = FindDirectChildComponent<Button>(logPanel.transform, "DeleteUnselectedLogsButton");
            TextMeshProUGUI deleteLabel;
            if (deleteButton == null)
            {
                deleteButton = OpenBetaUiFactory.CreateButton(
                    "DeleteUnselectedLogsButton",
                    logPanel.transform,
                    out deleteLabel,
                    new Vector2(260f, 40f));
            }
            else
            {
                deleteLabel = deleteButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (deleteLabel != null)
            {
                deleteLabel.text = "送信しない履歴を削除";
            }
            RectTransform deleteRect = deleteButton.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(1f, 1f);
            deleteRect.anchorMax = new Vector2(1f, 1f);
            deleteRect.pivot = new Vector2(1f, 1f);
            deleteRect.anchoredPosition = new Vector2(-32f, -24f);
            Image deleteImage = deleteButton.GetComponent<Image>();
            if (deleteImage != null)
            {
                UIStyle.ApplyButton(deleteButton);
                deleteImage.color = new Color(0.58f, 0.38f, 0.34f, 0.96f);
            }

            Button confirmButton = FindDirectChildComponent<Button>(logPanel.transform, "ConfirmUploadSelectionButton");
            TextMeshProUGUI confirmLabel;
            if (confirmButton == null)
            {
                confirmButton = OpenBetaUiFactory.CreateButton(
                    "ConfirmUploadSelectionButton",
                    logPanel.transform,
                    out confirmLabel,
                    new Vector2(250f, 40f));
            }
            else
            {
                confirmLabel = confirmButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (confirmLabel != null)
            {
                confirmLabel.text = "選択を送信待ちに確定";
            }
            RectTransform confirmRect = confirmButton.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(1f, 1f);
            confirmRect.anchorMax = new Vector2(1f, 1f);
            confirmRect.pivot = new Vector2(1f, 1f);
            confirmRect.anchoredPosition = new Vector2(-304f, -24f);
            confirmRect.sizeDelta = new Vector2(250f, 40f);
            Image confirmImage = confirmButton.GetComponent<Image>();
            if (confirmImage != null)
            {
                UIStyle.ApplyButton(confirmButton);
                confirmImage.color = new Color(0.43f, 0.50f, 0.55f, 0.96f);
            }

            Button backButton = FindDirectChildComponent<Button>(logPanel.transform, "BackButton");
            TextMeshProUGUI backLabel;
            if (backButton == null)
            {
                backButton = OpenBetaUiFactory.CreateButton("BackButton", logPanel.transform, out backLabel, new Vector2(120f, 40f));
            }
            else
            {
                backLabel = backButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (backLabel != null)
            {
                backLabel.text = "閉じる";
            }
            UIStyle.ApplyButton(backButton);
            RectTransform backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(1f, 1f);
            backRect.anchorMax = new Vector2(1f, 1f);
            backRect.pivot = new Vector2(1f, 1f);
            backRect.anchoredPosition = new Vector2(-32f, -72f);

            ScrollRect scrollRect = FindDirectChildComponent<ScrollRect>(logPanel.transform, "ScrollView");
            RectTransform content;
            if (scrollRect != null && scrollRect.content != null)
            {
                content = scrollRect.content;
            }
            else
            {
                scrollRect = CreateLogScrollView(logPanel.transform, out content);
            }

            ConfigureLogScrollViewRect(scrollRect);

            GameObject logLinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/LogLinePrefab.prefab");

            controller.logWindowPanel = logPanel;
            controller.logContentTransform = content;
            controller.logLinePrefab = logLinePrefab;
            controller.logBackButton = backButton;

            SerializedObject controllerObject = new SerializedObject(controller);
            SetObject(controllerObject, "logWindowController", logWindowPanel);
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject logWindowObject = new SerializedObject(logWindowPanel);
            SetObject(logWindowObject, "contentRoot", content);
            SetObject(logWindowObject, "logEntryPrefab", logLinePrefab);
            SetObject(logWindowObject, "scrollRect", scrollRect);
            SetObject(logWindowObject, "chatUI", controller);
            SetObject(logWindowObject, "deleteUnselectedLogsButton", deleteButton);
            SetObject(logWindowObject, "confirmUploadSelectionButton", confirmButton);
            SetObject(logWindowObject, "historySummaryText", summaryText);
            SetObject(logWindowObject, "logPrecautionsText", precautionsText);
            logWindowObject.ApplyModifiedPropertiesWithoutUndo();
            ApplyPreferredFont(logPanel.transform);
        }

        private static void ConfigureLogScrollViewRect(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            RectTransform rect = scrollRect.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = new Vector2(32f, 32f);
            rect.offsetMax = new Vector2(-32f, -124f);

            EnsureLogScrollViewScrollbar(scrollRect);
        }

        private static ScrollRect CreateLogScrollView(Transform parent, out RectTransform content)
        {
            GameObject scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(32f, 32f);
            scrollRectTransform.offsetMax = new Vector2(-32f, -124f);
            scrollObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.72f);
            UIStyle.ApplyPanel(scrollObject, null, false);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            OpenBetaUiFactory.Stretch(viewportRect);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;
            EnsureLogScrollViewScrollbar(scrollRect);
            return scrollRect;
        }

        private static void EnsureLogScrollViewScrollbar(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            Scrollbar scrollbar = FindChildComponent<Scrollbar>(scrollRect.transform, "Scrollbar Vertical");
            if (scrollbar == null)
            {
                scrollbar = CreateVerticalScrollbar(scrollRect.transform);
            }

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.verticalScrollbarSpacing = -2f;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            RectTransform viewport = scrollRect.viewport;
            if (viewport != null)
            {
                viewport.offsetMax = new Vector2(-18f, viewport.offsetMax.y);
            }
        }

        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            GameObject scrollbarObject = new GameObject("Scrollbar Vertical", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(parent, false);

            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-16f, 4f);
            scrollbarRect.offsetMax = new Vector2(-4f, -4f);

            Image background = scrollbarObject.GetComponent<Image>();
            background.color = new Color(0.80f, 0.82f, 0.84f, 0.65f);

            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(scrollbarObject.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(2f, 2f);
            handleRect.offsetMax = new Vector2(-2f, -2f);

            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.39f, 0.43f, 0.48f, 0.9f);

            Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;
            return scrollbar;
        }

        private static YarnManager BuildYarnSystem(YarnProject yarnProject)
        {
            GameObject dialogueSystem = GameObject.Find("DialogueSystem");
            if (dialogueSystem == null)
            {
                dialogueSystem = new GameObject("DialogueSystem");
            }

            DialogueRunner dialogueRunner = GetOrAdd<DialogueRunner>(dialogueSystem);
            if (yarnProject != null)
            {
                dialogueRunner.SetProject(yarnProject);
            }

            YarnManager yarnManager = GetOrAdd<YarnManager>(dialogueSystem);
            SerializedObject so = new SerializedObject(yarnManager);
            SetObject(so, "dialogueRunner", dialogueRunner);
            SetObject(so, "yarnProject", yarnProject);
            so.ApplyModifiedPropertiesWithoutUndo();
            return yarnManager;
        }

        private static YarnProject LoadOpenBetaYarnProject()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(OpenBetaYarnProjectGuid);
            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<YarnProject>(assetPath);
        }

        private static TextMeshProUGUI CreateInputRow(Transform parent, string name, out InputField input)
        {
            GameObject row = new GameObject(name + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 48f;
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;

            TextMeshProUGUI label = OpenBetaUiFactory.CreateTmpText("Label", row.transform, 20f, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.MidlineRight;
            AddLayout(label.gameObject, 180f, 44f);
            input = OpenBetaUiFactory.CreateInputField(name + "Input", row.transform, new Vector2(340f, 44f));
            return label;
        }

        private static GameObject GetOrCreatePanel(string name, Transform parent)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            return OpenBetaUiFactory.CreatePanel(name, parent);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }

        private static void AddLayout(GameObject target, float width, float height)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = target.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = width;
            layout.preferredHeight = height;
        }

        private static void SetObject(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static T FindDirectChildComponent<T>(Transform parent, string childName) where T : Component
        {
            if (parent == null)
            {
                return null;
            }

            Transform child = parent.Find(childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static T FindChildComponent<T>(Transform parent, string childName) where T : Component
        {
            GameObject child = FindChildGameObject(parent, childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static GameObject FindChildGameObject(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            Transform[] children = parent.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && child.gameObject.name == childName)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static void ApplyPreferredFont(Transform root)
        {
            TMP_FontAsset preferredFont = LoadPreferredTmpFont();
            if (preferredFont == null || root == null)
            {
                return;
            }

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].font = preferredFont;
                }
            }
        }

        private static TMP_FontAsset LoadPreferredTmpFont()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(PreferredTmpFontGuid);
            return string.IsNullOrWhiteSpace(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }
    }
}
