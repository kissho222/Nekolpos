using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backgammon.Conversation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Nekolpos.System
{
    public sealed class TalkTopicHintBubble : MonoBehaviour, IPointerDownHandler
    {
        private enum HintState
        {
            Idle,
            IdeaDisplayed
        }

        private const string InitialText = "話題を思い浮かべる";
        private const string ButtonObjectName = "TalkTopicHintBubbleButton";
        private const string LabelObjectName = "TalkTopicHintBubbleLabel";
        private const string HighlightObjectName = "TalkTopicHintBubbleHighlight";
        private const string InterferenceObjectName = "TalkTopicHintBubbleInterference";
        private const string LowerHighlightObjectName = "TalkTopicHintBubbleLowerHighlight";
        private const string RippleObjectName = "TalkTopicHintBubbleRipple";
        private const string SparkleObjectPrefix = "TalkTopicHintBubbleSparkle";
        private const string UIStyleBorderObjectName = "UIStyleBorder";
        private const string DefaultResourcePath = "Dialogue/TalkTopicMaster";
        private const string DefaultProjectRelativePath = "TalkSource/Dialogue/TalkTopicMaster.csv";
        private const string PreviewBubbleSpriteResourcePath = "UIThemes/Sprites/talk_topic_bubble_circle";
        private const string BubbleShaderName = "Nekolpos/UI/SoapBubble";
        private const int SparkleCount = 5;
        private static readonly Color BubbleLabelTextColor = Color.white;
        private static readonly Color32 BubbleLabelOutlineColor = new Color32(0, 0, 0, 235);

        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private TextAsset talkTopicMasterCsv;
        [SerializeField] private Button bubbleButton;
        [SerializeField] private TMP_Text bubbleLabel;
        [SerializeField] private string resourcePath = DefaultResourcePath;
        [SerializeField] private string projectRelativePath = DefaultProjectRelativePath;

        private readonly List<TalkTopicData> candidates = new List<TalkTopicData>();
        private HintState state = HintState.Idle;
        private TalkTopicData selectedHint;
        private CanvasGroup bubbleGroup;
        private Image bubbleImage;
        private RectTransform bubbleRect;
        private RectTransform interferenceRect;
        private RectTransform highlightRect;
        private RectTransform lowerHighlightRect;
        private RectTransform rippleRect;
        private Image interferenceImage;
        private Image highlightImage;
        private Image lowerHighlightImage;
        private Image rippleImage;
        private readonly Image[] sparkleImages = new Image[SparkleCount];
        private readonly RectTransform[] sparkleRects = new RectTransform[SparkleCount];
        private readonly Vector2[] sparkleDirections =
        {
            new Vector2(-0.75f, 0.70f),
            new Vector2(-0.25f, 1.00f),
            new Vector2(0.45f, 0.86f),
            new Vector2(0.82f, -0.10f),
            new Vector2(-0.58f, -0.48f)
        };
        private bool warnedNoCandidates;
        private bool initialized;
        private bool isPlayingClickEffect;
        private Vector2 baseAnchoredPosition;
        private Vector3 baseScale = Vector3.one;
        private Sprite bubbleSprite;
        private Sprite crescentSprite;
        private Sprite lowerHighlightSprite;
        private Sprite interferenceSprite;
        private Sprite rippleSprite;
        private Sprite sparkleSprite;
        private Material bubbleMaterial;
        private Coroutine ideaEffectCoroutine;
        private Coroutine popEffectCoroutine;

        public void Configure(ChatUIController owner, TextAsset csvAsset = null)
        {
            if (owner != null)
            {
                chatUI = owner;
            }

            if (csvAsset != null)
            {
                talkTopicMasterCsv = csvAsset;
            }

            InitializeIfNeeded();
            RefreshVisibility();
        }

        public void RefreshVisibility()
        {
            InitializeIfNeeded();
            UpdateVisibility();
        }

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
            ResetBubble();
            UpdateVisibility();
        }

        private void Update()
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (!initialized || bubbleButton == null || chatUI == null || chatUI.chatInputField == null)
            {
                return;
            }

            bool shouldShow = chatUI.TalkTopicHintsAllowed && chatUI.chatInputField.gameObject.activeInHierarchy;
            if (!shouldShow && state != HintState.Idle)
            {
                ResetBubble();
            }

            if (bubbleButton.gameObject.activeSelf != shouldShow)
            {
                bubbleButton.gameObject.SetActive(shouldShow);
            }

            bool hasCandidates = candidates.Count > 0;
            bubbleButton.interactable = shouldShow && hasCandidates && !isPlayingClickEffect;
            if (bubbleGroup != null && !isPlayingClickEffect)
            {
                bubbleGroup.alpha = hasCandidates ? 1f : 0.55f;
            }

            if (shouldShow)
            {
                AnimateIdleBubble();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            chatUI?.SuppressNextEndEditSubmit();
            HideWebGLImeInputForBubbleClick();
        }

        private void InitializeIfNeeded()
        {
            if (initialized)
            {
                return;
            }

            if (chatUI == null)
            {
                chatUI = GetComponent<ChatUIController>() ?? GetComponentInParent<ChatUIController>(true);
            }

            EnsureBubbleUi();
            LoadCandidates();
            ResetBubble();
            initialized = true;
        }

        private void EnsureBubbleUi()
        {
            if (bubbleButton == null)
            {
                bubbleButton = GetComponent<Button>();
            }

            if (bubbleButton != null)
            {
                bubbleGroup = bubbleButton.GetComponent<CanvasGroup>() ?? bubbleButton.gameObject.AddComponent<CanvasGroup>();
                bubbleImage = bubbleButton.GetComponent<Image>();
                bubbleRect = bubbleButton.GetComponent<RectTransform>();
                EnsurePointerDownSuppressor(bubbleButton.gameObject);
                if (bubbleLabel == null)
                {
                    bubbleLabel = bubbleButton.GetComponentInChildren<TMP_Text>(true);
                }
                ConfigureBubbleVisuals();
                bubbleButton.onClick.RemoveListener(HandleBubbleClicked);
                bubbleButton.onClick.AddListener(HandleBubbleClicked);
                return;
            }

            Transform parent = chatUI != null && chatUI.chatInputField != null && chatUI.chatInputField.transform.parent != null
                ? chatUI.chatInputField.transform.parent
                : transform;

            GameObject buttonObject = new GameObject(ButtonObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(CanvasGroup));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(200f, 200f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);

            if (chatUI != null && chatUI.chatInputField != null)
            {
                RectTransform inputRect = chatUI.chatInputField.GetComponent<RectTransform>();
                if (inputRect != null)
                {
                    buttonRect.anchorMin = inputRect.anchorMin;
                    buttonRect.anchorMax = inputRect.anchorMax;
                    buttonRect.anchoredPosition = inputRect.anchoredPosition + new Vector2(-238f, 142f);
                }
            }

            bubbleImage = buttonObject.GetComponent<Image>();
            bubbleRect = buttonRect;

            Shadow shadow = buttonObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.05f, 0.07f, 0.09f, 0.10f);
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;

            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.12f);
            outline.effectDistance = new Vector2(0.8f, 0.8f);
            outline.useGraphicAlpha = true;

            bubbleButton = buttonObject.GetComponent<Button>();
            bubbleButton.targetGraphic = bubbleImage;
            bubbleButton.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = bubbleButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.16f);
            colors.pressedColor = new Color(0.84f, 0.93f, 1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.42f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            bubbleButton.colors = colors;
            bubbleButton.onClick.AddListener(HandleBubbleClicked);
            EnsurePointerDownSuppressor(buttonObject);

            bubbleGroup = buttonObject.GetComponent<CanvasGroup>();

            CreateHighlight(buttonObject.transform);
            CreateLabel(buttonObject.transform);
            ConfigureBubbleVisuals();
        }

        private void ConfigureBubbleVisuals()
        {
            if (bubbleButton == null)
            {
                return;
            }

            Transform parent = bubbleButton.transform;
            bubbleRect = bubbleButton.GetComponent<RectTransform>();
            if (bubbleRect != null)
            {
                baseAnchoredPosition = bubbleRect.anchoredPosition;
                baseScale = bubbleRect.localScale;
            }

            if (bubbleImage != null)
            {
                bubbleImage.sprite = GetPreviewBubbleSprite();
                bubbleImage.type = Image.Type.Simple;
                bubbleImage.preserveAspect = true;
                bubbleImage.fillCenter = true;
                bubbleImage.color = Color.white;
                bubbleImage.raycastTarget = true;
                bubbleImage.material = GetBubbleMaterial();
            }

            DisableStyleBorder(parent);

            Shadow shadow = bubbleButton.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.effectColor = new Color(0.05f, 0.07f, 0.09f, 0.10f);
                shadow.effectDistance = new Vector2(0f, -2f);
                shadow.useGraphicAlpha = true;
            }

            Outline outline = bubbleButton.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(1f, 1f, 1f, 0.12f);
                outline.effectDistance = new Vector2(0.8f, 0.8f);
                outline.useGraphicAlpha = true;
            }

            interferenceImage = GetOrCreateImageLayer(parent, InterferenceObjectName, out interferenceRect);
            ConfigureFullLayer(interferenceImage, interferenceRect, GetInterferenceSprite(), new Color(1f, 1f, 1f, 0.28f));

            highlightImage = GetOrCreateImageLayer(parent, HighlightObjectName, out highlightRect);
            ConfigureLayerRect(highlightRect, new Vector2(0.02f, 0.50f), new Vector2(0.58f, 1.04f), new Vector2(8f, -2f), new Vector2(-12f, -2f));
            ConfigureLayerImage(highlightImage, GetCrescentSprite(), new Color(1f, 1f, 1f, 0.60f));

            lowerHighlightImage = GetOrCreateImageLayer(parent, LowerHighlightObjectName, out lowerHighlightRect);
            ConfigureLayerRect(lowerHighlightRect, new Vector2(0.50f, 0.06f), new Vector2(0.96f, 0.42f), new Vector2(-12f, 0f), new Vector2(-6f, 4f));
            ConfigureLayerImage(lowerHighlightImage, GetLowerHighlightSprite(), new Color(0.78f, 0.92f, 1f, 0.24f));

            rippleImage = GetOrCreateImageLayer(parent, RippleObjectName, out rippleRect);
            ConfigureFullLayer(rippleImage, rippleRect, GetRippleSprite(), new Color(1f, 1f, 1f, 0f));
            rippleImage.gameObject.SetActive(false);

            for (int i = 0; i < SparkleCount; i++)
            {
                sparkleImages[i] = GetOrCreateImageLayer(parent, $"{SparkleObjectPrefix}{i + 1}", out sparkleRects[i]);
                ConfigureLayerRect(sparkleRects[i], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                sparkleRects[i].sizeDelta = new Vector2(10f, 10f);
                ConfigureLayerImage(sparkleImages[i], GetSparkleSprite(), new Color(1f, 1f, 1f, 0f));
                sparkleImages[i].gameObject.SetActive(false);
            }

            if (bubbleLabel == null)
            {
                bubbleLabel = bubbleButton.GetComponentInChildren<TMP_Text>(true);
            }

            ConfigureLabelReadability();
            bubbleLabel?.transform.SetAsLastSibling();
        }

        private void DisableStyleBorder(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            Transform borderTransform = parent.Find(UIStyleBorderObjectName);
            if (borderTransform == null)
            {
                return;
            }

            borderTransform.gameObject.SetActive(false);
        }

        private Image GetOrCreateImageLayer(Transform parent, string objectName, out RectTransform rect)
        {
            Transform existing = parent.Find(objectName);
            GameObject layerObject = existing != null
                ? existing.gameObject
                : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            if (existing == null)
            {
                layerObject.transform.SetParent(parent, false);
            }

            rect = layerObject.GetComponent<RectTransform>();
            Image image = layerObject.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigureFullLayer(Image image, RectTransform rect, Sprite sprite, Color color)
        {
            ConfigureLayerRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ConfigureLayerImage(image, sprite, color);
        }

        private static void ConfigureLayerRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void ConfigureLayerImage(Image image, Sprite sprite, Color color)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = color;
        }

        private void ConfigureLabelReadability()
        {
            if (bubbleLabel == null)
            {
                return;
            }

            bubbleLabel.color = BubbleLabelTextColor;
            bubbleLabel.outlineColor = BubbleLabelOutlineColor;
            bubbleLabel.outlineWidth = 0.18f;

            Shadow shadow = bubbleLabel.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = bubbleLabel.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = new Color(0f, 0f, 0f, 0.24f);
            shadow.effectDistance = new Vector2(0f, -1.25f);
            shadow.useGraphicAlpha = true;

            Outline outline = bubbleLabel.GetComponent<Outline>();
            if (outline == null)
            {
                outline = bubbleLabel.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = BubbleLabelOutlineColor;
            outline.effectDistance = new Vector2(1.15f, 1.15f);
            outline.useGraphicAlpha = true;
        }

        private void CreateHighlight(Transform parent)
        {
            GameObject highlightObject = new GameObject(HighlightObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            highlightObject.transform.SetParent(parent, false);
            RectTransform rect = highlightObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0.42f, 1f);
            rect.offsetMin = new Vector2(22f, -4f);
            rect.offsetMax = new Vector2(-8f, -10f);

            Image image = highlightObject.GetComponent<Image>();
            image.sprite = GetBubbleSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, 0.34f);
            image.raycastTarget = false;
        }

        private void EnsurePointerDownSuppressor(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            PointerDownSuppressor suppressor = target.GetComponent<PointerDownSuppressor>();
            if (suppressor == null)
            {
                suppressor = target.AddComponent<PointerDownSuppressor>();
            }

            suppressor.Owner = this;
        }

        private void CreateLabel(Transform parent)
        {
            GameObject labelObject = new GameObject(LabelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            bubbleLabel = labelObject.GetComponent<TextMeshProUGUI>();
            RectTransform labelRect = bubbleLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(28f, 36f);
            labelRect.offsetMax = new Vector2(-28f, -36f);

            TMP_Text sourceText = chatUI != null
                ? chatUI.messageText != null ? chatUI.messageText : chatUI.speakerNameText
                : null;
            if (sourceText != null)
            {
                bubbleLabel.font = sourceText.font;
                bubbleLabel.fontSharedMaterial = sourceText.fontSharedMaterial;
            }

            bubbleLabel.fontSize = 16f;
            bubbleLabel.fontSizeMin = 11f;
            bubbleLabel.fontSizeMax = 16f;
            bubbleLabel.enableAutoSizing = true;
            bubbleLabel.alignment = TextAlignmentOptions.Center;
            bubbleLabel.color = BubbleLabelTextColor;
            bubbleLabel.textWrappingMode = TextWrappingModes.Normal;
            bubbleLabel.overflowMode = TextOverflowModes.Ellipsis;
            bubbleLabel.raycastTarget = false;
        }

        private void LoadCandidates()
        {
            candidates.Clear();
            string csvText = LoadCsvText();
            if (string.IsNullOrWhiteSpace(csvText))
            {
                WarnNoCandidatesOnce("CSVを読み込めませんでした。");
                return;
            }

            try
            {
                candidates.AddRange(TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(csvText));
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException)
            {
                Debug.LogWarning($"[TalkTopicHintBubble] 会話ヒントCSVの読み込みに失敗しました: {exception.Message}");
            }

            if (candidates.Count == 0)
            {
                WarnNoCandidatesOnce("有効な候補がありません。");
            }
        }

        private string LoadCsvText()
        {
            if (talkTopicMasterCsv != null)
            {
                return talkTopicMasterCsv.text;
            }

            if (!string.IsNullOrWhiteSpace(resourcePath))
            {
                TextAsset resourceAsset = Resources.Load<TextAsset>(resourcePath);
                if (resourceAsset != null)
                {
                    talkTopicMasterCsv = resourceAsset;
                    return resourceAsset.text;
                }
            }

            string path = Path.Combine(Application.dataPath, "..", projectRelativePath ?? string.Empty);
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }

            return string.Empty;
        }

        private void HandleBubbleClicked()
        {
            chatUI?.SuppressNextEndEditSubmit();
            HideWebGLImeInputForBubbleClick();

            if (candidates.Count == 0)
            {
                WarnNoCandidatesOnce("有効な候補がありません。");
                if (bubbleButton != null)
                {
                    bubbleButton.interactable = false;
                }
                return;
            }

            if (state == HintState.Idle)
            {
                selectedHint = candidates[Random.Range(0, candidates.Count)];
                state = HintState.IdeaDisplayed;
                PlayIdeaDisplayEffect(ResolveDisplayText(selectedHint.Idea));
                SetSelectedVisual(true);
                return;
            }

            if (selectedHint != null)
            {
                chatUI?.SetInputTextFromTalkTopicHint(ResolveDisplayText(selectedHint.Import));
            }

            PlayPopEffectAndReset();
        }

        private void HideWebGLImeInputForBubbleClick()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLImeInputBridge bridge = chatUI != null ? chatUI.GetComponent<WebGLImeInputBridge>() : null;
            if (bridge == null)
            {
                bridge = GetComponent<WebGLImeInputBridge>();
            }

            bridge?.ForceHideDomInput();
#endif
        }

        private void PlayIdeaDisplayEffect(string ideaText)
        {
            if (ideaEffectCoroutine != null)
            {
                StopCoroutine(ideaEffectCoroutine);
            }

            if (popEffectCoroutine != null)
            {
                StopCoroutine(popEffectCoroutine);
                popEffectCoroutine = null;
                RestoreBubbleTransform();
            }

            ideaEffectCoroutine = StartCoroutine(PlayIdeaDisplayEffectCoroutine(ideaText));
        }

        private IEnumerator PlayIdeaDisplayEffectCoroutine(string ideaText)
        {
            isPlayingClickEffect = true;
            SetLabel(ideaText);
            if (bubbleLabel != null)
            {
                bubbleLabel.alpha = 0f;
            }

            float duration = 0.22f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(t * Mathf.PI) * 0.055f;
                ApplyBubbleScale(1f + pulse);
                if (bubbleLabel != null)
                {
                    bubbleLabel.alpha = Mathf.SmoothStep(0f, 1f, t);
                }

                if (rippleImage != null && rippleRect != null)
                {
                    rippleImage.gameObject.SetActive(true);
                    rippleImage.color = new Color(0.82f, 0.94f, 1f, 0.18f * (1f - t));
                    rippleRect.localScale = Vector3.one * Mathf.Lerp(0.88f, 1.18f, t);
                }

                yield return null;
            }

            if (bubbleLabel != null)
            {
                bubbleLabel.alpha = 1f;
            }

            if (rippleImage != null)
            {
                rippleImage.gameObject.SetActive(false);
            }

            RestoreBubbleTransform();
            isPlayingClickEffect = false;
            ideaEffectCoroutine = null;
        }

        private void PlayPopEffectAndReset()
        {
            if (popEffectCoroutine != null)
            {
                StopCoroutine(popEffectCoroutine);
            }

            if (ideaEffectCoroutine != null)
            {
                StopCoroutine(ideaEffectCoroutine);
                ideaEffectCoroutine = null;
            }

            popEffectCoroutine = StartCoroutine(PlayPopEffectAndResetCoroutine());
        }

        private IEnumerator PlayPopEffectAndResetCoroutine()
        {
            isPlayingClickEffect = true;
            state = HintState.Idle;
            selectedHint = null;

            SetSparklesActive(true);
            float duration = 0.34f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easeOut = 1f - Mathf.Pow(1f - t, 3f);

                if (bubbleGroup != null)
                {
                    bubbleGroup.alpha = 1f - easeOut;
                }

                ApplyBubbleScale(Mathf.Lerp(1f, 1.18f, easeOut));

                if (rippleImage != null && rippleRect != null)
                {
                    rippleImage.gameObject.SetActive(true);
                    rippleImage.color = new Color(1f, 1f, 1f, 0.30f * (1f - t));
                    rippleRect.localScale = Vector3.one * Mathf.Lerp(0.88f, 1.46f, easeOut);
                }

                for (int i = 0; i < SparkleCount; i++)
                {
                    if (sparkleImages[i] == null || sparkleRects[i] == null)
                    {
                        continue;
                    }

                    Vector2 direction = sparkleDirections[i].normalized;
                    sparkleRects[i].anchoredPosition = direction * Mathf.Lerp(10f, 46f, easeOut);
                    sparkleRects[i].localScale = Vector3.one * Mathf.Lerp(0.75f, 0.35f, t);
                    sparkleImages[i].color = new Color(1f, 1f, 1f, 0.72f * Mathf.Sin((1f - t) * Mathf.PI * 0.5f));
                }

                yield return null;
            }

            SetSparklesActive(false);
            if (rippleImage != null)
            {
                rippleImage.gameObject.SetActive(false);
            }

            ResetBubble();
            if (bubbleGroup != null)
            {
                bubbleGroup.alpha = 1f;
            }

            RestoreBubbleTransform();
            isPlayingClickEffect = false;
            popEffectCoroutine = null;
        }

        private void SetSparklesActive(bool active)
        {
            for (int i = 0; i < SparkleCount; i++)
            {
                if (sparkleImages[i] != null)
                {
                    sparkleImages[i].gameObject.SetActive(active);
                }

                if (sparkleRects[i] != null)
                {
                    sparkleRects[i].anchoredPosition = Vector2.zero;
                    sparkleRects[i].localScale = Vector3.one;
                }
            }
        }

        private void AnimateIdleBubble()
        {
            if (isPlayingClickEffect || bubbleRect == null)
            {
                return;
            }

            float time = Time.unscaledTime;
            float bob = Mathf.Sin(time * 1.25f) * 3.2f;
            float sway = Mathf.Sin(time * 0.82f + 0.7f) * 1.7f;
            float scale = 1f + Mathf.Sin(time * 1.05f + 1.2f) * 0.012f;
            bubbleRect.anchoredPosition = baseAnchoredPosition + new Vector2(sway, bob);
            ApplyBubbleScale(scale);

            if (bubbleMaterial != null)
            {
                bubbleMaterial.SetFloat("_Phase", time);
            }

            if (interferenceRect != null)
            {
                interferenceRect.localRotation = Quaternion.Euler(0f, 0f, time * 5.5f);
            }

            if (highlightRect != null)
            {
                highlightRect.anchoredPosition = new Vector2(Mathf.Sin(time * 0.6f) * 2.2f, Mathf.Cos(time * 0.52f) * 1.2f);
            }

            if (lowerHighlightRect != null)
            {
                lowerHighlightRect.anchoredPosition = new Vector2(Mathf.Cos(time * 0.48f) * 1.4f, Mathf.Sin(time * 0.58f) * 1.1f);
            }
        }

        private void ApplyBubbleScale(float scale)
        {
            if (bubbleRect != null)
            {
                bubbleRect.localScale = baseScale * scale;
            }
        }

        private void RestoreBubbleTransform()
        {
            if (bubbleRect != null)
            {
                bubbleRect.anchoredPosition = baseAnchoredPosition;
                bubbleRect.localScale = baseScale;
            }
        }

        private string ResolveDisplayText(string text)
        {
            return chatUI != null ? chatUI.ResolveDisplayText(text) : text ?? string.Empty;
        }

        private void ResetBubble()
        {
            state = HintState.Idle;
            selectedHint = null;
            SetLabel(InitialText);
            SetSelectedVisual(false);
        }

        private void SetLabel(string text)
        {
            if (bubbleLabel != null)
            {
                bubbleLabel.text = string.IsNullOrWhiteSpace(text) ? InitialText : text;
            }
        }

        private void SetSelectedVisual(bool selected)
        {
            if (bubbleImage != null)
            {
                bubbleImage.color = Color.white;
            }

            if (bubbleMaterial != null)
            {
                bubbleMaterial.SetFloat("_Selected", selected ? 1f : 0f);
            }

            if (interferenceImage != null)
            {
                interferenceImage.color = selected
                    ? new Color(1f, 1f, 1f, 0.36f)
                    : new Color(1f, 1f, 1f, 0.28f);
            }
        }

        private void WarnNoCandidatesOnce(string reason)
        {
            if (warnedNoCandidates)
            {
                return;
            }

            warnedNoCandidates = true;
            Debug.LogWarning($"[TalkTopicHintBubble] {reason}");
        }

        private Material GetBubbleMaterial()
        {
            if (bubbleMaterial != null)
            {
                return bubbleMaterial;
            }

            Shader shader = Shader.Find(BubbleShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[TalkTopicHintBubble] Shader '{BubbleShaderName}' が見つかりません。軽量スプライト表現にフォールバックします。");
                return null;
            }

            bubbleMaterial = new Material(shader)
            {
                name = "TalkTopicHintBubbleMaterial"
            };
            bubbleMaterial.SetFloat("_RimStrength", 0.58f);
            bubbleMaterial.SetFloat("_FilmStrength", 0.26f);
            bubbleMaterial.SetFloat("_SurfaceWobble", 0.14f);
            return bubbleMaterial;
        }

        private Sprite GetPreviewBubbleSprite()
        {
            Sprite resourceSprite = Resources.Load<Sprite>(PreviewBubbleSpriteResourcePath);
            return resourceSprite != null ? resourceSprite : GetBubbleSprite();
        }

        private Sprite GetBubbleSprite()
        {
            if (bubbleSprite != null)
            {
                return bubbleSprite;
            }

            const int size = 64;
            const float radius = 30f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TalkTopicHintBubbleSprite",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32 clear = new Color32(255, 255, 255, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - (size * 0.5f);
                    float dy = y + 0.5f - (size * 0.5f);
                    texture.SetPixel(x, y, dx * dx + dy * dy <= radius * radius ? white : clear);
                }
            }

            texture.Apply();
            bubbleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            bubbleSprite.name = "TalkTopicHintBubbleSprite";
            return bubbleSprite;
        }

        private Sprite GetCrescentSprite()
        {
            if (crescentSprite != null)
            {
                return crescentSprite;
            }

            const int size = 128;
            Texture2D texture = CreateTransparentTexture(size, "TalkTopicHintBubbleCrescent");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    Vector2 p = (uv - new Vector2(0.46f, 0.48f)) * new Vector2(1.1f, 1.7f);
                    float outer = 1f - SmoothStep(0.34f, 0.48f, p.magnitude);
                    Vector2 cut = (uv - new Vector2(0.54f, 0.45f)) * new Vector2(1.1f, 1.7f);
                    float inner = 1f - SmoothStep(0.23f, 0.39f, cut.magnitude);
                    float alpha = Mathf.Clamp01((outer - inner) * 0.72f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            crescentSprite = CreateSprite(texture, "TalkTopicHintBubbleCrescentSprite");
            return crescentSprite;
        }

        private Sprite GetLowerHighlightSprite()
        {
            if (lowerHighlightSprite != null)
            {
                return lowerHighlightSprite;
            }

            const int size = 96;
            Texture2D texture = CreateTransparentTexture(size, "TalkTopicHintBubbleLowerHighlight");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    Vector2 p = (uv - new Vector2(0.5f, 0.5f)) * new Vector2(1.5f, 1.0f);
                    float ring = SmoothStep(0.44f, 0.58f, p.magnitude) * (1f - SmoothStep(0.60f, 0.76f, p.magnitude));
                    float fade = SmoothStep(0.0f, 0.8f, uv.x) * (1f - SmoothStep(0.78f, 1f, uv.y));
                    texture.SetPixel(x, y, new Color(0.75f, 0.94f, 1f, ring * fade * 0.42f));
                }
            }

            texture.Apply();
            lowerHighlightSprite = CreateSprite(texture, "TalkTopicHintBubbleLowerHighlightSprite");
            return lowerHighlightSprite;
        }

        private Sprite GetInterferenceSprite()
        {
            if (interferenceSprite != null)
            {
                return interferenceSprite;
            }

            const int size = 128;
            Texture2D texture = CreateTransparentTexture(size, "TalkTopicHintBubbleInterference");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    Vector2 p = uv * 2f - Vector2.one;
                    float radius = p.magnitude;
                    float angle = Mathf.Atan2(p.y, p.x);
                    float rim = SmoothStep(0.58f, 0.95f, radius) * (1f - SmoothStep(0.98f, 1.05f, radius));
                    float uneven = 0.45f + 0.35f * Mathf.Sin(angle * 2.7f + radius * 5.0f) + 0.20f * Mathf.Sin(angle * 5.1f);
                    float alpha = Mathf.Clamp01(rim * uneven * 0.32f);
                    Color color = Color.Lerp(new Color(0.68f, 0.92f, 1f, alpha), new Color(1f, 0.70f, 0.88f, alpha), Mathf.Sin(angle + 1.3f) * 0.5f + 0.5f);
                    color = Color.Lerp(color, new Color(0.82f, 0.72f, 1f, alpha), Mathf.Sin(angle * 1.9f - 0.4f) * 0.18f + 0.18f);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            interferenceSprite = CreateSprite(texture, "TalkTopicHintBubbleInterferenceSprite");
            return interferenceSprite;
        }

        private Sprite GetRippleSprite()
        {
            if (rippleSprite != null)
            {
                return rippleSprite;
            }

            const int size = 128;
            Texture2D texture = CreateTransparentTexture(size, "TalkTopicHintBubbleRipple");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float radius = (uv * 2f - Vector2.one).magnitude;
                    float ring = SmoothStep(0.76f, 0.83f, radius) * (1f - SmoothStep(0.87f, 0.96f, radius));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, ring * 0.80f));
                }
            }

            texture.Apply();
            rippleSprite = CreateSprite(texture, "TalkTopicHintBubbleRippleSprite");
            return rippleSprite;
        }

        private Sprite GetSparkleSprite()
        {
            if (sparkleSprite != null)
            {
                return sparkleSprite;
            }

            const int size = 32;
            Texture2D texture = CreateTransparentTexture(size, "TalkTopicHintBubbleSparkle");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    Vector2 p = uv * 2f - Vector2.one;
                    float dot = Mathf.Max(0f, 1f - p.magnitude);
                    float cross = Mathf.Max(0f, 1f - Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.y)) * 6.0f) * Mathf.Max(0f, 1f - p.magnitude * 0.9f);
                    float alpha = Mathf.Clamp01(dot * 0.36f + cross * 0.48f);
                    texture.SetPixel(x, y, new Color(1f, 0.98f, 0.92f, alpha));
                }
            }

            texture.Apply();
            sparkleSprite = CreateSprite(texture, "TalkTopicHintBubbleSparkleSprite");
            return sparkleSprite;
        }

        private static Texture2D CreateTransparentTexture(int size, string textureName)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            return texture;
        }

        private static Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = spriteName;
            return sprite;
        }

        private static float SmoothStep(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }

        private void OnDestroy()
        {
            if (bubbleButton != null)
            {
                bubbleButton.onClick.RemoveListener(HandleBubbleClicked);
            }

            if (bubbleSprite != null && bubbleSprite.texture != null)
            {
                DestroySpriteWithTexture(bubbleSprite);
            }

            DestroySpriteWithTexture(crescentSprite);
            DestroySpriteWithTexture(lowerHighlightSprite);
            DestroySpriteWithTexture(interferenceSprite);
            DestroySpriteWithTexture(rippleSprite);
            DestroySpriteWithTexture(sparkleSprite);

            if (bubbleMaterial != null)
            {
                Object.Destroy(bubbleMaterial);
            }
        }

        private static void DestroySpriteWithTexture(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            Texture2D texture = sprite.texture;
            if (texture != null)
            {
                Object.Destroy(texture);
            }

            Object.Destroy(sprite);
        }

        private sealed class PointerDownSuppressor : MonoBehaviour, IPointerDownHandler
        {
            public TalkTopicHintBubble Owner;

            public void OnPointerDown(PointerEventData eventData)
            {
                Owner?.OnPointerDown(eventData);
            }
        }
    }
}
