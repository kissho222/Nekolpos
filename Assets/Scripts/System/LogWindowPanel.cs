using System;
using System.Collections;
using System.Collections.Generic;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    public sealed class LogWindowPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GameObject logEntryPrefab;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private Color playerTextColor = new Color(0.16f, 0.19f, 0.24f, 1f);
        [SerializeField] private Color catTextColor = new Color(0.40f, 0.23f, 0.14f, 1f);
        [SerializeField] private Color systemTextColor = new Color(0.39f, 0.43f, 0.48f, 1f);
        [SerializeField] private Color playerBackgroundColor = new Color(0.95f, 0.97f, 1f, 1f);
        [SerializeField] private Color catBackgroundColor = new Color(1f, 0.96f, 0.91f, 1f);
        [SerializeField] private Color systemBackgroundColor = new Color(0.93f, 0.94f, 0.95f, 1f);
        [SerializeField] private Color questionLinkColor = new Color(0.10f, 0.55f, 0.85f, 1f);
        [SerializeField] private Button deleteUnselectedLogsButton;
        [SerializeField] private Button confirmUploadSelectionButton;
        [SerializeField] private TMP_Text historySummaryText;
        [SerializeField] private TMP_Text logPrecautionsText;
        [SerializeField] private GameObject submissionConfirmationPanel;
        [SerializeField] private TMP_Text submissionConfirmationText;
        [SerializeField] private Button submissionConfirmationButton;
        [SerializeField] private TMP_Text submissionConfirmationButtonText;
        [SerializeField] private Button submissionConfirmationBackButton;
        [SerializeField] private GameObject logSentSuccessfullyPanel;
        [SerializeField] private TMP_Text logSentSuccessfullyText;
        [SerializeField] private Button logSentSuccessfullyBackButton;
        [SerializeField] private Button enqueteButton;
        [SerializeField] private string enqueteUrl;

        private readonly HashSet<string> selectedUploadLogIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> deselectedUploadLogIds = new HashSet<string>(StringComparer.Ordinal);
        private Coroutine scrollCoroutine;
        private bool deleteConfirmationArmed;
        private bool waitingForUploadCompletion;
        private bool loggedRefreshEntryCreationWarning;
        private readonly Dictionary<string, GameObject> renderedRows = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, DialogueLogEntry> renderedEntries = new Dictionary<string, DialogueLogEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> visibleLogIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> removedLogIds = new List<string>();
        private bool rowsInitialized;
        private float lastContentWidth = -1f;

        private const int LogWindowSortingOrder = 7200;
        private const int LogPopupSortingOrder = 7300;
        private const string TalkTopicHintMarkerSpriteResourcePath = "UIThemes/Sprites/talk_topic_bubble_circle";
        private static Sprite cachedTalkTopicHintMarkerSprite;

        public void Configure(Transform contentParent, GameObject entryPrefab, ChatUIController owner = null)
        {
            contentRoot = contentParent as RectTransform;
            logEntryPrefab = entryPrefab;
            chatUI = owner != null ? owner : chatUI;
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (contentRoot != null && rowsInitialized && Mathf.Abs(contentRoot.rect.width - lastContentWidth) > 0.5f)
                Refresh();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (DialogueLogManager.Instance != null)
            {
                DialogueLogManager.Instance.LogsChanged -= HandleLogsChanged;
                DialogueLogManager.Instance.LogsChanged += HandleLogsChanged;
                DialogueLogManager.Instance.UploadCompleted -= HandleUploadCompleted;
                DialogueLogManager.Instance.UploadCompleted += HandleUploadCompleted;
            }

            EnsureSubmissionPopupControls();
            HideSubmissionPopups();
            Refresh(true);
        }

        private void OnDisable()
        {
            if (scrollCoroutine != null)
            {
                StopCoroutine(scrollCoroutine);
                scrollCoroutine = null;
            }
            if (DialogueLogManager.Instance != null)
            {
                DialogueLogManager.Instance.LogsChanged -= HandleLogsChanged;
                DialogueLogManager.Instance.UploadCompleted -= HandleUploadCompleted;
            }
        }

        public void Open()
        {
            transform.SetAsLastSibling();
            EnsureOverlayCanvas(gameObject, LogWindowSortingOrder);
            if (gameObject.activeSelf)
            {
                Refresh(true);
                return;
            }

            gameObject.SetActive(true);
        }

        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(false);
        }

        public void Refresh()
        {
            Refresh(false);
        }

        private void Refresh(bool scrollToLatest)
        {
            ResolveReferences();
            EnsureHistoryControls();
            List<DialogueLogEntry> logs = DialogueLogManager.Instance != null
                ? DialogueLogManager.Instance.GetConversationHistoryLogs()
                : new List<DialogueLogEntry>();
            RefreshEntries(logs, scrollToLatest);
        }

        private void RefreshEntries(IReadOnlyList<DialogueLogEntry> logs, bool scrollToLatest)
        {
            if (contentRoot == null || logEntryPrefab == null || logs == null)
            {
                return;
            }

            bool followLatest = scrollToLatest || !rowsInitialized || scrollRect == null
                || scrollRect.verticalNormalizedPosition <= 0.02f;
            Vector2 savedPosition = contentRoot.anchoredPosition;
            bool widthChanged = Mathf.Abs(contentRoot.rect.width - lastContentWidth) > 0.5f;

            if (!rowsInitialized)
            {
                // Detach immediately: Destroy is deferred, so old rows must not enter this frame's layout.
                while (contentRoot.childCount > 0)
                    RemoveRow(contentRoot.GetChild(0).gameObject);
                rowsInitialized = true;
            }

            EnsureDefaultUploadSelections(logs);

            visibleLogIds.Clear();
            for (int i = 0; i < logs.Count; i++)
            {
                DialogueLogEntry entry = logs[i];
                if (entry == null) continue;
                string key = string.IsNullOrEmpty(entry.LogId) ? "row:" + i : entry.LogId;
                if (!visibleLogIds.Add(key)) continue;
                renderedRows.TryGetValue(key, out GameObject row);
                if (row == null || !renderedEntries.TryGetValue(key, out DialogueLogEntry previous)
                    || !HasSamePresentation(previous, entry))
                {
                    if (row != null) RemoveRow(row);
                    row = CreateLogEntryView(entry);
                    renderedRows[key] = row;
                    renderedEntries[key] = entry;
                }
                else
                {
                    Toggle toggle = row.GetComponentInChildren<Toggle>(true);
                    bool uploaded = entry.UploadStatus == DialogueLogEntry.UploadStatusUploaded;
                    if (toggle != null)
                    {
                        toggle.SetIsOnWithoutNotify(uploaded || selectedUploadLogIds.Contains(entry.LogId));
                        toggle.interactable = !uploaded;
                    }
                    RefreshUploadStatusLabel(row.transform, uploaded);
                    if (widthChanged)
                    {
                        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
                        ConfigureLogRowLayout(row, FindText(texts, "SpeakerText", "SpeakerNameText", "SpealerNameText"),
                            FindText(texts, "MessageText", "ChatLogText", "ChatLog"));
                    }
                }
                if (row.transform.GetSiblingIndex() != i) row.transform.SetSiblingIndex(i);
            }

            removedLogIds.Clear();
            foreach (var pair in renderedRows)
                if (!visibleLogIds.Contains(pair.Key)) removedLogIds.Add(pair.Key);
            foreach (string key in removedLogIds)
            {
                RemoveRow(renderedRows[key]);
                renderedRows.Remove(key);
                renderedEntries.Remove(key);
            }

            RefreshHistoryControls(logs);
            lastContentWidth = contentRoot.rect.width;
            QueueScrollPosition(followLatest, savedPosition);
        }

        private static bool HasSamePresentation(DialogueLogEntry before, DialogueLogEntry after)
        {
            if (before.Text != after.Text || before.RawInput != after.RawInput || before.Speaker != after.Speaker
                || before.SpeakerDisplayName != after.SpeakerDisplayName || before.Source != after.Source
                || before.talkTopicHintUsed != after.talkTopicHintUsed) return false;
            int count = before.QuestionLinks?.Count ?? 0;
            if (count != (after.QuestionLinks?.Count ?? 0)) return false;
            for (int i = 0; i < count; i++)
            {
                DialogueLogQuestionLink left = before.QuestionLinks[i];
                DialogueLogQuestionLink right = after.QuestionLinks[i];
                if (left?.keyword != right?.keyword || left?.insertText != right?.insertText) return false;
            }
            return true;
        }

        private void RemoveRow(GameObject row)
        {
            if (row == null) return;
            row.SetActive(false);
            row.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(row);
            else DestroyImmediate(row);
        }

        private void HandleLogsChanged()
        {
            if (gameObject.activeInHierarchy)
            {
                Refresh();
            }
        }

        private void ResolveReferences()
        {
            if (scrollRect == null)
            {
                scrollRect = GetComponentInChildren<ScrollRect>(true);
            }

            if (contentRoot == null && scrollRect != null)
            {
                contentRoot = scrollRect.content;
            }

            EnsureVerticalScrollbar();

            if (chatUI == null)
            {
                ChatUIController[] candidates = Resources.FindObjectsOfTypeAll<ChatUIController>();
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] != null && candidates[i].gameObject.scene == gameObject.scene)
                    {
                        chatUI = candidates[i];
                        break;
                    }
                }
            }
        }

        private GameObject CreateLogEntryView(DialogueLogEntry entry)
        {
            GameObject lineObject = Instantiate(logEntryPrefab, contentRoot);
            TMP_Text[] texts = lineObject.GetComponentsInChildren<TMP_Text>(true);
            Image backgroundImage = lineObject.GetComponent<Image>();

            TMP_Text speakerText = FindText(texts, "SpeakerText", "SpeakerNameText", "SpealerNameText");
            TMP_Text messageText = FindText(texts, "MessageText", "ChatLogText", "ChatLog");
            Color lineColor = ResolveLineColor(entry);
            Color backgroundColor = ResolveBackgroundColor(entry);
            string speakerLabel = ResolveSpeakerLabel(entry);

            if (backgroundImage != null)
            {
                backgroundImage.color = backgroundColor;
            }

            if (speakerText != null)
            {
                speakerText.text = string.IsNullOrWhiteSpace(speakerLabel) ? string.Empty : speakerLabel + "：";
                speakerText.color = lineColor;
            }

            if (messageText != null)
            {
                bool isPlayerInputOriginalLog = IsPlayerInputOriginalLog(entry);
                messageText.text = entry != null
                    ? isPlayerInputOriginalLog ? RichTextEscaper.Escape(entry.Text) : entry.Text ?? string.Empty
                    : string.Empty;
                messageText.color = lineColor;

                if (isPlayerInputOriginalLog)
                {
                    try
                    {
                        PlayerInputFontAssetProvider.ApplyToPlayerInput(messageText);
                        PlayerInputFontAssetProvider.AddCharacters(messageText.text);
                    }
                    catch (Exception exception)
                    {
                        if (!loggedRefreshEntryCreationWarning)
                        {
                            loggedRefreshEntryCreationWarning = true;
                            Debug.LogWarning($"[LogWindowPanel] Player input font setup failed. Continuing log refresh with the default font: {exception.Message}");
                        }
                    }
                }

                if (entry != null &&
                    entry.Speaker == DialogueLogManager.SpeakerCat &&
                    entry.QuestionLinks != null &&
                    entry.QuestionLinks.Count > 0)
                {
                    DialogueLogQuestionLinkView linkView =
                        messageText.gameObject.AddComponent<DialogueLogQuestionLinkView>();
                    linkView.Configure(messageText, entry.QuestionLinks, chatUI, questionLinkColor);
                }
            }

            CreateUploadSelectionToggle(lineObject, entry);
            CreateTalkTopicHintMarker(lineObject, entry);
            ConfigureLogRowLayout(lineObject, speakerText, messageText);
            return lineObject;
        }

        private void ConfigureLogRowLayout(GameObject row, TMP_Text speakerText, TMP_Text messageText)
        {
            const float textLeft = 80f;
            const float textRight = 24f;
            float width = Mathf.Max(100f, contentRoot.rect.width - 24f - textLeft - textRight);
            float messageHeight = 30f;
            if (messageText != null)
            {
                messageText.enableAutoSizing = false;
                messageText.fontSize = 22f;
                messageText.alignment = TextAlignmentOptions.TopLeft;
                messageText.textWrappingMode = TextWrappingModes.Normal;
                messageText.overflowMode = TextOverflowModes.Overflow;
                messageHeight = Mathf.Max(30f, messageText.GetPreferredValues(messageText.text, width, Mathf.Infinity).y);
                SetRowTextRect(messageText.rectTransform, textLeft, textRight, 36f, messageHeight);
            }
            if (speakerText != null)
            {
                speakerText.enableAutoSizing = false;
                speakerText.fontSize = 18f;
                speakerText.fontStyle = FontStyles.Bold;
                speakerText.alignment = TextAlignmentOptions.TopLeft;
                speakerText.textWrappingMode = TextWrappingModes.NoWrap;
                speakerText.overflowMode = TextOverflowModes.Ellipsis;
                SetRowTextRect(speakerText.rectTransform, textLeft, textRight, 8f, 26f);
            }
            float height = Mathf.Max(84f, messageHeight + 50f);
            ((RectTransform)row.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            LayoutElement layout = row.GetComponent<LayoutElement>();
            if (layout == null) layout = row.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        private static void SetRowTextRect(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void EnsureVerticalScrollbar()
        {
            if (scrollRect == null || scrollRect.verticalScrollbar != null)
            {
                return;
            }

            RectTransform scrollTransform = scrollRect.GetComponent<RectTransform>();
            if (scrollTransform == null)
            {
                return;
            }

            Scrollbar scrollbar = FindDescendantScrollbar(scrollTransform, "Scrollbar Vertical");
            if (scrollbar == null)
            {
                scrollbar = CreateVerticalScrollbar(scrollTransform);
            }

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.verticalScrollbarSpacing = -2f;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            RectTransform viewport = scrollRect.viewport;
            if (viewport != null)
            {
                viewport.offsetMin = new Vector2(viewport.offsetMin.x, viewport.offsetMin.y);
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

        private static Scrollbar FindDescendantScrollbar(Transform root, string objectName)
        {
            Transform target = FindDescendant(root, objectName);
            return target != null ? target.GetComponent<Scrollbar>() : null;
        }

        private static bool IsPlayerInputOriginalLog(DialogueLogEntry entry)
        {
            if (entry == null ||
                !string.Equals(entry.Speaker, DialogueLogManager.SpeakerPlayer, StringComparison.Ordinal))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(entry.RawInput) ||
                   string.Equals(entry.Source, DialogueLogManager.SourceFreeInput, StringComparison.Ordinal) ||
                   string.Equals(entry.Source, DialogueLogManager.SourceUnmatchedInput, StringComparison.Ordinal);
        }

        private void CreateTalkTopicHintMarker(GameObject lineObject, DialogueLogEntry entry)
        {
            if (lineObject == null ||
                entry == null ||
                !entry.TalkTopicHintUsed ||
                !string.Equals(entry.Speaker, DialogueLogManager.SpeakerPlayer, StringComparison.Ordinal))
            {
                return;
            }

            GameObject markerObject = new GameObject("TalkTopicHintMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            markerObject.transform.SetParent(lineObject.transform, false);
            markerObject.transform.SetAsLastSibling();

            RectTransform markerRect = markerObject.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(1f, 1f);
            markerRect.anchorMax = new Vector2(1f, 1f);
            markerRect.pivot = new Vector2(1f, 1f);
            markerRect.anchoredPosition = new Vector2(-12f, -8f);
            markerRect.sizeDelta = new Vector2(20f, 20f);

            Image markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = GetTalkTopicHintMarkerSprite();
            markerImage.color = new Color(0.78f, 0.93f, 1f, 0.88f);
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
        }

        private static Sprite GetTalkTopicHintMarkerSprite()
        {
            if (cachedTalkTopicHintMarkerSprite != null)
            {
                return cachedTalkTopicHintMarkerSprite;
            }

            cachedTalkTopicHintMarkerSprite = Resources.Load<Sprite>(TalkTopicHintMarkerSpriteResourcePath);
            if (cachedTalkTopicHintMarkerSprite == null)
            {
                cachedTalkTopicHintMarkerSprite = CreateFallbackTalkTopicHintMarkerSprite();
            }

            return cachedTalkTopicHintMarkerSprite;
        }

        private static Sprite CreateFallbackTalkTopicHintMarkerSprite()
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color clear = Color.clear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float ny = ((y + 0.5f) / size) * 2f - 1f;
                    float distance = Mathf.Sqrt((nx * nx) + (ny * ny));
                    float rim = Mathf.SmoothStep(0.98f, 0.84f, distance) * Mathf.SmoothStep(0.64f, 0.82f, distance);
                    float highlight = Mathf.Exp(-22f * (((nx + 0.33f) * (nx + 0.33f)) + ((ny - 0.34f) * (ny - 0.34f))));
                    float alpha = Mathf.Clamp01((rim * 0.72f) + (highlight * 0.45f));
                    Color color = alpha > 0.005f
                        ? new Color(0.72f + highlight * 0.24f, 0.91f, 1f, alpha)
                        : clear;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void EnsureDefaultUploadSelections(IReadOnlyList<DialogueLogEntry> logs)
        {
            if (logs == null)
            {
                return;
            }

            for (int i = 0; i < logs.Count; i++)
            {
                DialogueLogEntry entry = logs[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogId))
                {
                    continue;
                }

                if (string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal) ||
                    string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusExcluded, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!deselectedUploadLogIds.Contains(entry.LogId))
                {
                    selectedUploadLogIds.Add(entry.LogId);
                }
            }
        }

        private void CreateUploadSelectionToggle(GameObject lineObject, DialogueLogEntry entry)
        {
            if (lineObject == null || entry == null || string.IsNullOrWhiteSpace(entry.LogId))
            {
                return;
            }

            Toggle toggle = lineObject.GetComponentInChildren<Toggle>(true);
            if (toggle == null)
            {
                toggle = CreateLineToggle(lineObject.transform);
            }

            bool uploaded = string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal);
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(uploaded || selectedUploadLogIds.Contains(entry.LogId));
            toggle.interactable = !uploaded;
            RefreshUploadStatusLabel(lineObject.transform, uploaded);
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    deselectedUploadLogIds.Remove(entry.LogId);
                    selectedUploadLogIds.Add(entry.LogId);
                }
                else
                {
                    selectedUploadLogIds.Remove(entry.LogId);
                    deselectedUploadLogIds.Add(entry.LogId);
                }

                deleteConfirmationArmed = false;
                RefreshHistoryControls(GetCurrentHistoryLogs());
            });
        }

        private Toggle CreateLineToggle(Transform parent)
        {
            GameObject toggleObject = new GameObject("UploadSelectionToggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(parent, false);

            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0f, 0.5f);
            toggleRect.anchorMax = new Vector2(0f, 0.5f);
            toggleRect.pivot = new Vector2(0f, 0.5f);
            toggleRect.anchoredPosition = new Vector2(8f, 0f);
            toggleRect.sizeDelta = new Vector2(56f, 56f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(toggleObject.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.sizeDelta = Vector2.zero;
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.color = UIStyle.NormalInput;
            backgroundImage.sprite = null;
            UIStyle.ApplyPanel(backgroundObject, null, false);

            GameObject checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.25f, 0.25f);
            checkmarkRect.anchorMax = new Vector2(0.75f, 0.75f);
            checkmarkRect.sizeDelta = Vector2.zero;
            Image checkmarkImage = checkmarkObject.GetComponent<Image>();
            checkmarkImage.color = new Color(0.43f, 0.50f, 0.55f, 1f);

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            return toggle;
        }

        private void RefreshUploadStatusLabel(Transform lineTransform, bool uploaded)
        {
            if (lineTransform == null)
            {
                return;
            }

            TMP_Text statusText = FindDescendantText(lineTransform, "UploadStatusText");
            if (statusText == null)
            {
                statusText = CreateUploadStatusText(lineTransform);
            }

            statusText.text = uploaded
                ? GetLogUiText(OpenBetaDialogueKeys.LogStatusUploaded, "送信済")
                : string.Empty;
            statusText.gameObject.SetActive(uploaded);
        }

        private TMP_Text CreateUploadStatusText(Transform parent)
        {
            GameObject textObject = new GameObject("UploadStatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(6f, 2f);
            rect.sizeDelta = new Vector2(64f, 18f);

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 14f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 14f;
            text.color = systemTextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            ApplyUploadStatusFont(text, parent);
            return text;
        }

        private static void ApplyUploadStatusFont(TMP_Text statusText, Transform lineTransform)
        {
            if (statusText == null || lineTransform == null)
            {
                return;
            }

            TMP_Text[] lineTexts = lineTransform.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < lineTexts.Length; i++)
            {
                TMP_Text sourceText = lineTexts[i];
                if (sourceText == null || sourceText == statusText || sourceText.font == null)
                {
                    continue;
                }

                statusText.font = sourceText.font;
                statusText.fontSharedMaterial = sourceText.fontSharedMaterial;
                return;
            }
        }

        private void EnsureHistoryControls()
        {
            if (deleteUnselectedLogsButton == null)
            {
                deleteUnselectedLogsButton = FindChildButton("DeleteUnselectedLogsButton");
            }

            if (confirmUploadSelectionButton == null)
            {
                confirmUploadSelectionButton = FindChildButton("ConfirmUploadSelectionButton");
            }

            if (historySummaryText == null)
            {
                historySummaryText = FindChildText("HistorySummaryText");
            }

            if (logPrecautionsText == null)
            {
                logPrecautionsText = FindChildText("LOGPrecautionsText");
            }

            if (confirmUploadSelectionButton == null)
            {
                confirmUploadSelectionButton = CreateConfirmUploadSelectionButton();
            }

            if (deleteUnselectedLogsButton == null)
            {
                deleteUnselectedLogsButton = CreateDeleteUnselectedButton();
            }

            if (historySummaryText == null)
            {
                historySummaryText = CreateHistorySummaryText();
            }

            if (logPrecautionsText == null)
            {
                logPrecautionsText = CreateLogPrecautionsText();
            }

            RefreshLogPrecautionsText();

            if (deleteUnselectedLogsButton != null)
            {
                deleteUnselectedLogsButton.onClick.RemoveListener(HandleDeleteUnselectedLogsClicked);
                deleteUnselectedLogsButton.onClick.AddListener(HandleDeleteUnselectedLogsClicked);
            }

            if (confirmUploadSelectionButton != null)
            {
                confirmUploadSelectionButton.onClick.RemoveListener(HandleConfirmUploadSelectionClicked);
                confirmUploadSelectionButton.onClick.AddListener(HandleConfirmUploadSelectionClicked);
            }

            EnsureSubmissionPopupControls();
        }

        private void RefreshHistoryControls(IReadOnlyList<DialogueLogEntry> logs)
        {
            int totalCount = logs != null ? logs.Count : 0;
            int selectedCount = 0;
            int deletableCount = 0;
            int queuedCount = 0;
            int reviewCount = 0;

            if (logs != null)
            {
                for (int i = 0; i < logs.Count; i++)
                {
                    DialogueLogEntry entry = logs[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.LogId))
                    {
                        continue;
                    }

                    bool uploaded = string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal);
                    bool selected = selectedUploadLogIds.Contains(entry.LogId);
                    bool queued = string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusQueued, StringComparison.Ordinal) ||
                                  string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusFailed, StringComparison.Ordinal);
                    bool review = string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusReview, StringComparison.Ordinal) ||
                                  string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusPending, StringComparison.Ordinal) ||
                                  string.IsNullOrWhiteSpace(entry.UploadStatus);
                    if (queued)
                    {
                        queuedCount++;
                    }
                    else if (review)
                    {
                        reviewCount++;
                    }
                    if (!uploaded && selected)
                    {
                        selectedCount++;
                    }

                    if (!uploaded && !selected)
                    {
                        deletableCount++;
                    }
                }
            }

            if (historySummaryText != null)
            {
                historySummaryText.text = GetLogUiText(
                    OpenBetaDialogueKeys.LogHistorySummary,
                    "履歴 {TOTAL_COUNT}件 / 確認待ち {REVIEW_COUNT}件 / 送信待ち {QUEUED_COUNT}件",
                    new Dictionary<string, string>
                    {
                        ["TOTAL_COUNT"] = totalCount.ToString(),
                        ["REVIEW_COUNT"] = reviewCount.ToString(),
                        ["QUEUED_COUNT"] = queuedCount.ToString()
                    });
            }


            if (confirmUploadSelectionButton != null)
            {
                confirmUploadSelectionButton.interactable = selectedCount > 0 && !waitingForUploadCompletion;
                SetButtonLabel(
                    confirmUploadSelectionButton,
                    GetLogUiText(
                        OpenBetaDialogueKeys.LogConfirmUploadSelectionButton,
                        "選択を送信待ちに確定 ({SELECTED_COUNT})",
                        new Dictionary<string, string> { ["SELECTED_COUNT"] = selectedCount.ToString() }));
            }

            if (deleteUnselectedLogsButton != null)
            {
                deleteUnselectedLogsButton.interactable = deletableCount > 0;
                SetButtonLabel(
                    deleteUnselectedLogsButton,
                    deleteConfirmationArmed
                        ? GetLogUiText(OpenBetaDialogueKeys.LogDeleteUnselectedButtonConfirm, "もう一度押すと削除")
                        : GetLogUiText(
                            OpenBetaDialogueKeys.LogDeleteUnselectedButton,
                            "送信しない履歴を削除 ({DELETABLE_COUNT})",
                            new Dictionary<string, string> { ["DELETABLE_COUNT"] = deletableCount.ToString() }));
            }
        }

        private void HandleConfirmUploadSelectionClicked()
        {
            deleteConfirmationArmed = false;
            ShowSubmissionConfirmationPopup();
        }

        private void HandleSubmissionConfirmationClicked()
        {
            List<DialogueLogEntry> logs = GetCurrentHistoryLogs();
            List<string> queuedIds = new List<string>();
            List<string> excludedIds = new List<string>();
            for (int i = 0; i < logs.Count; i++)
            {
                DialogueLogEntry entry = logs[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogId) ||
                    string.Equals(entry.UploadStatus, DialogueLogEntry.UploadStatusUploaded, StringComparison.Ordinal))
                {
                    continue;
                }

                if (selectedUploadLogIds.Contains(entry.LogId))
                {
                    queuedIds.Add(entry.LogId);
                }
                else
                {
                    excludedIds.Add(entry.LogId);
                }
            }

            if (submissionConfirmationButton != null)
            {
                submissionConfirmationButton.interactable = false;
            }

            SetSubmissionButtonLabel(OpenBetaDialogueKeys.SubmissionConfirmationButtonSending, "ログ送信中　少しお待ちください");
            waitingForUploadCompletion = true;
            bool started = DialogueLogManager.Instance != null &&
                           DialogueLogManager.Instance.SaveUploadReview(queuedIds, excludedIds);
            if (!started)
            {
                waitingForUploadCompletion = false;
                if (submissionConfirmationButton != null)
                {
                    submissionConfirmationButton.interactable = true;
                }

                SetSubmissionButtonLabel(OpenBetaDialogueKeys.SubmissionConfirmationButton, "ログ送信 ※少し時間が掛かります");
                Debug.LogWarning("[LogWindowPanel] Log upload did not start. Check DialogueLogManager uploadEndpoint and autoUploadQueuedLogs.");
            }

            deleteConfirmationArmed = false;
            RefreshHistoryControls(GetCurrentHistoryLogs());
        }

        private void HandleUploadCompleted(bool succeeded)
        {
            waitingForUploadCompletion = false;
            if (submissionConfirmationButton != null)
            {
                submissionConfirmationButton.interactable = true;
            }

            SetSubmissionButtonLabel(OpenBetaDialogueKeys.SubmissionConfirmationButton, "ログ送信 ※少し時間が掛かります");
            if (succeeded)
            {
                ShowLogSentSuccessfullyPopup();
            }

            Refresh();
        }

        private void ShowSubmissionConfirmationPopup()
        {
            EnsureSubmissionPopupControls();
            if (submissionConfirmationText != null)
            {
                submissionConfirmationText.text = BasicSystemDialogueCatalog.Get(
                    OpenBetaDialogueKeys.SubmissionConfirmation,
                    string.Empty);
            }

            SetSubmissionButtonLabel(OpenBetaDialogueKeys.SubmissionConfirmationButton, "ログ送信 ※少し時間が掛かります");
            if (submissionConfirmationButton != null)
            {
                submissionConfirmationButton.interactable = true;
            }

            SetButtonLabel(submissionConfirmationBackButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.CloseButton, "閉じる"));
            SetButtonLabel(logSentSuccessfullyBackButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.CloseButton, "閉じる"));
            SetButtonLabel(enqueteButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.EnqueteButton, "アンケートに回答"));
            if (logSentSuccessfullyPanel != null)
            {
                logSentSuccessfullyPanel.SetActive(false);
            }

            if (submissionConfirmationPanel != null)
            {
                submissionConfirmationPanel.SetActive(true);
                submissionConfirmationPanel.transform.SetAsLastSibling();
                EnsureOverlayCanvas(submissionConfirmationPanel, LogPopupSortingOrder);
                EnsurePopupRaycasts(submissionConfirmationPanel, true);
            }
        }

        private void ShowLogSentSuccessfullyPopup()
        {
            EnsureSubmissionPopupControls();
            if (logSentSuccessfullyText != null)
            {
                logSentSuccessfullyText.text = BasicSystemDialogueCatalog.Get(
                    OpenBetaDialogueKeys.SubmissionConfirmationAfter,
                    string.Empty);
            }

            SetButtonLabel(enqueteButton, BasicSystemDialogueCatalog.Get(OpenBetaDialogueKeys.EnqueteButton, "アンケートに回答"));

            if (submissionConfirmationPanel != null)
            {
                submissionConfirmationPanel.SetActive(true);
                EnsurePopupRaycasts(submissionConfirmationPanel, true);
            }

            if (logSentSuccessfullyPanel != null)
            {
                logSentSuccessfullyPanel.SetActive(true);
                logSentSuccessfullyPanel.transform.SetAsLastSibling();
                EnsureOverlayCanvas(logSentSuccessfullyPanel, LogPopupSortingOrder + 1);
                EnsurePopupRaycasts(logSentSuccessfullyPanel, true);
            }
        }

        private void HideSubmissionPopups()
        {
            waitingForUploadCompletion = false;
            if (submissionConfirmationPanel != null)
            {
                EnsurePopupRaycasts(submissionConfirmationPanel, false);
                submissionConfirmationPanel.SetActive(false);
            }

            if (logSentSuccessfullyPanel != null)
            {
                EnsurePopupRaycasts(logSentSuccessfullyPanel, false);
                logSentSuccessfullyPanel.SetActive(false);
            }
        }

        private void HandleSubmissionPopupBackClicked()
        {
            HideSubmissionPopups();
            Refresh();
        }

        private void HandleEnqueteButtonClicked()
        {
            if (string.IsNullOrWhiteSpace(enqueteUrl))
            {
                Debug.LogWarning("[LogWindowPanel] Enquete URL is not set.");
                return;
            }

            Application.OpenURL(enqueteUrl);
        }

        private void SetSubmissionButtonLabel(string key, string fallback)
        {
            string label = BasicSystemDialogueCatalog.Get(key, fallback);
            if (submissionConfirmationButtonText != null)
            {
                submissionConfirmationButtonText.text = label;
                return;
            }

            SetButtonLabel(submissionConfirmationButton, label);
        }

        private void HandleDeleteUnselectedLogsClicked()
        {
            if (!deleteConfirmationArmed)
            {
                deleteConfirmationArmed = true;
                RefreshHistoryControls(GetCurrentHistoryLogs());
                return;
            }

            deleteConfirmationArmed = false;
            int deletedCount = DialogueLogManager.Instance != null
                ? DialogueLogManager.Instance.DeleteLogsNotSelectedForUpload(selectedUploadLogIds)
                : 0;
            Debug.Log($"[LogWindowPanel] Deleted {deletedCount} conversation log entries that were not selected for upload.");
            Refresh();
        }

        private List<DialogueLogEntry> GetCurrentHistoryLogs()
        {
            return DialogueLogManager.Instance != null
                ? DialogueLogManager.Instance.GetConversationHistoryLogs()
                : new List<DialogueLogEntry>();
        }

        private Button CreateDeleteUnselectedButton()
        {
            GameObject buttonObject = new GameObject("DeleteUnselectedLogsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(230f, 36f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.58f, 0.38f, 0.34f, 0.96f);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 16f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 16f;
            label.color = UIStyle.NormalText;
            label.text = GetLogUiText(
                OpenBetaDialogueKeys.LogDeleteUnselectedButton,
                "送信しない履歴を削除 ({DELETABLE_COUNT})",
                new Dictionary<string, string> { ["DELETABLE_COUNT"] = "0" });

            Button button = buttonObject.GetComponent<Button>();
            UIStyle.ApplyButton(button);
            image.color = new Color(0.58f, 0.38f, 0.34f, 0.96f);
            return button;
        }

        private Button CreateConfirmUploadSelectionButton()
        {
            GameObject buttonObject = new GameObject("ConfirmUploadSelectionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-270f, -24f);
            rect.sizeDelta = new Vector2(250f, 36f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.43f, 0.50f, 0.55f, 0.96f);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 15f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 15f;
            label.color = UIStyle.NormalText;
            label.text = GetLogUiText(
                OpenBetaDialogueKeys.LogConfirmUploadSelectionButton,
                "選択を送信待ちに確定 ({SELECTED_COUNT})",
                new Dictionary<string, string> { ["SELECTED_COUNT"] = "0" });
            Button button = buttonObject.GetComponent<Button>();
            UIStyle.ApplyButton(button);
            image.color = new Color(0.43f, 0.50f, 0.55f, 0.96f);
            return button;
        }

        private TMP_Text CreateHistorySummaryText()
        {
            GameObject textObject = new GameObject("HistorySummaryText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(260f, 32f);

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 15f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 15f;
            text.color = systemTextColor;
            text.text = string.Empty;
            return text;
        }

        private TMP_Text CreateLogPrecautionsText()
        {
            GameObject textObject = new GameObject("LOGPrecautionsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -58f);
            rect.sizeDelta = new Vector2(520f, 32f);

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 15f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 15f;
            text.richText = true;
            text.text = string.Empty;
            return text;
        }

        private void RefreshLogPrecautionsText()
        {
            if (logPrecautionsText == null)
            {
                return;
            }

            logPrecautionsText.text = BasicSystemDialogueCatalog.Get(
                OpenBetaDialogueKeys.LogPrecautions,
                "<color=#ff3333>個人情報などは送付しないでください。</color>");
        }

        private void EnsureSubmissionPopupControls()
        {
            Transform popupRoot = ResolvePopupSearchRoot();
            if (submissionConfirmationPanel == null)
            {
                submissionConfirmationPanel = FindDescendantGameObject(popupRoot, "SubmissionConfirmationPanel");
            }

            if (submissionConfirmationText == null && submissionConfirmationPanel != null)
            {
                submissionConfirmationText = FindDescendantText(submissionConfirmationPanel.transform, "SubmissionConfirmationText");
            }

            if (submissionConfirmationButton == null && submissionConfirmationPanel != null)
            {
                submissionConfirmationButton = FindDescendantButton(submissionConfirmationPanel.transform, "SubmissionConfirmationButton");
            }

            if (submissionConfirmationButtonText == null && submissionConfirmationButton != null)
            {
                submissionConfirmationButtonText = submissionConfirmationButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (submissionConfirmationBackButton == null && submissionConfirmationPanel != null)
            {
                submissionConfirmationBackButton = FindDescendantButton(submissionConfirmationPanel.transform, "BackButton");
            }

            if (logSentSuccessfullyPanel == null)
            {
                logSentSuccessfullyPanel = FindDescendantGameObject(popupRoot, "LogSentSuccessfullyPanel");
            }

            if (logSentSuccessfullyText == null && logSentSuccessfullyPanel != null)
            {
                logSentSuccessfullyText = FindDescendantText(logSentSuccessfullyPanel.transform, "LogSentSuccessfullyText")
                    ?? FindDescendantText(logSentSuccessfullyPanel.transform, "SubmissionConfirmationAfterText");
            }

            if (logSentSuccessfullyPanel == null && logSentSuccessfullyText == null)
            {
                logSentSuccessfullyText = FindDescendantText(popupRoot, "LogSentSuccessfullyText");
            }

            if (logSentSuccessfullyPanel == null && logSentSuccessfullyText != null)
            {
                logSentSuccessfullyPanel = logSentSuccessfullyText.transform.parent != null
                    ? logSentSuccessfullyText.transform.parent.gameObject
                    : logSentSuccessfullyText.gameObject;
            }

            if (logSentSuccessfullyBackButton == null && logSentSuccessfullyPanel != null)
            {
                logSentSuccessfullyBackButton = FindDescendantButton(logSentSuccessfullyPanel.transform, "BackButton");
            }

            if (enqueteButton == null && logSentSuccessfullyPanel != null)
            {
                enqueteButton = FindDescendantButton(logSentSuccessfullyPanel.transform, "EnqueteButton");
            }

            if (submissionConfirmationButton != null)
            {
                submissionConfirmationButton.onClick.RemoveListener(HandleSubmissionConfirmationClicked);
                submissionConfirmationButton.onClick.AddListener(HandleSubmissionConfirmationClicked);
            }

            if (submissionConfirmationBackButton != null)
            {
                submissionConfirmationBackButton.onClick.RemoveListener(HandleSubmissionPopupBackClicked);
                submissionConfirmationBackButton.onClick.AddListener(HandleSubmissionPopupBackClicked);
            }

            if (logSentSuccessfullyBackButton != null)
            {
                logSentSuccessfullyBackButton.onClick.RemoveListener(HandleSubmissionPopupBackClicked);
                logSentSuccessfullyBackButton.onClick.AddListener(HandleSubmissionPopupBackClicked);
            }

            if (enqueteButton != null)
            {
                enqueteButton.onClick.RemoveListener(HandleEnqueteButtonClicked);
                enqueteButton.onClick.AddListener(HandleEnqueteButtonClicked);
            }
        }

        private Transform ResolvePopupSearchRoot()
        {
            return transform.parent != null ? transform.parent : transform.root;
        }

        private static string GetLogUiText(string key, string fallback, IReadOnlyDictionary<string, string> placeholders = null)
        {
            return placeholders == null
                ? BasicSystemDialogueCatalog.Get(key, fallback)
                : BasicSystemDialogueCatalog.Format(key, placeholders, fallback);
        }

        private Button FindChildButton(string objectName)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button != null && button.gameObject.name == objectName)
                {
                    return button;
                }
            }

            return null;
        }

        private static Button FindDescendantButton(Transform root, string objectName)
        {
            Transform target = FindDescendant(root, objectName);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private static TMP_Text FindDescendantText(Transform root, string objectName)
        {
            Transform target = FindDescendant(root, objectName);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private static GameObject FindDescendantGameObject(Transform root, string objectName)
        {
            Transform target = FindDescendant(root, objectName);
            return target != null ? target.gameObject : null;
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

        private static void EnsurePopupRaycasts(GameObject target, bool enabled)
        {
            if (target == null)
            {
                return;
            }

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            group.alpha = enabled ? 1f : group.alpha;
            group.interactable = enabled;
            group.blocksRaycasts = enabled;
            group.ignoreParentGroups = true;
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
                Transform child = root.GetChild(i);
                Transform result = FindDescendant(child, objectName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private TMP_Text FindChildText(string objectName)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text != null && text.gameObject.name == objectName)
                {
                    return text;
                }
            }

            return null;
        }

        private void QueueScrollPosition(bool followLatest, Vector2 savedPosition)
        {
            if (!isActiveAndEnabled || scrollRect == null)
            {
                return;
            }

            if (scrollCoroutine != null)
            {
                StopCoroutine(scrollCoroutine);
            }

            scrollCoroutine = StartCoroutine(RestoreScrollPositionDeferred(followLatest, savedPosition));
        }

        private IEnumerator RestoreScrollPositionDeferred(bool followLatest, Vector2 savedPosition)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollRect == null || contentRoot == null)
            {
                scrollCoroutine = null;
                yield break;
            }
            scrollRect.StopMovement();
            if (followLatest) scrollRect.verticalNormalizedPosition = 0f;
            else
            {
                // Preserve the reading position measured from the top when new lines arrive below it.
                RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
                savedPosition.y = Mathf.Clamp(savedPosition.y, 0f, Mathf.Max(0f, contentRoot.rect.height - viewport.rect.height));
                contentRoot.anchoredPosition = savedPosition;
            }
            scrollCoroutine = null;
        }

        private Color ResolveLineColor(DialogueLogEntry entry)
        {
            if (entry == null)
            {
                return playerTextColor;
            }

            if (entry.Speaker == DialogueLogManager.SpeakerSystem || entry.Source == DialogueLogManager.SourceSystem)
            {
                return systemTextColor;
            }

            if (entry.Speaker == DialogueLogManager.SpeakerCat)
            {
                return catTextColor;
            }

            return playerTextColor;
        }

        private Color ResolveBackgroundColor(DialogueLogEntry entry)
        {
            if (entry == null)
            {
                return playerBackgroundColor;
            }

            if (entry.Speaker == DialogueLogManager.SpeakerSystem || entry.Source == DialogueLogManager.SourceSystem)
            {
                return systemBackgroundColor;
            }

            if (entry.Speaker == DialogueLogManager.SpeakerCat)
            {
                return catBackgroundColor;
            }

            return playerBackgroundColor;
        }

        private string ResolveSpeakerLabel(DialogueLogEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Speaker))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.SpeakerDisplayName))
            {
                return entry.SpeakerDisplayName;
            }

            switch (entry.Speaker)
            {
                case DialogueLogManager.SpeakerPlayer:
                    return ResolveDisplayTextOrFallback("{{PLAYER_NAME}}", "あなた");
                case DialogueLogManager.SpeakerCat:
                    return ResolveDisplayTextOrFallback("{{CAT_NAME}}", "ネコ");
                case DialogueLogManager.SpeakerSystem:
                    return "システム";
                default:
                    return entry.Speaker;
            }
        }

        private string ResolveDisplayTextOrFallback(string template, string fallback)
        {
            if (chatUI == null)
            {
                return fallback;
            }

            string resolved = chatUI.ResolveDisplayText(template);
            return string.IsNullOrWhiteSpace(resolved) || resolved == template ? fallback : resolved;
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

        private static TMP_Text FindText(IReadOnlyList<TMP_Text> texts, params string[] names)
        {
            if (texts == null)
            {
                return null;
            }

            for (int i = 0; i < names.Length; i++)
            {
                string targetName = names[i];
                for (int j = 0; j < texts.Count; j++)
                {
                    TMP_Text text = texts[j];
                    if (text != null && text.gameObject.name == targetName)
                    {
                        return text;
                    }
                }
            }

            return null;
        }
    }
}
