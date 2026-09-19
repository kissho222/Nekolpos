using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.ActionSystem
{
    public readonly struct ActionChoiceResult
    {
        public ActionChoiceResult(ActionData actionData, bool continueConversation)
        {
            ActionData = actionData;
            ContinueConversation = continueConversation;
        }

        public ActionData ActionData { get; }

        public bool ContinueConversation { get; }
    }

    public class ActionChoiceUI : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private RectTransform buttonContainer;
        [SerializeField] private TextMeshProUGUI titleText;

        private readonly List<GameObject> spawnedButtons = new List<GameObject>();
        private UniTaskCompletionSource<ActionChoiceResult> currentSelectionSource;
        private TMP_FontAsset cachedFont;

        public void Initialize(Canvas canvas, TMP_FontAsset font)
        {
            rootCanvas = canvas;
            cachedFont = font;
            EnsureUi();
            HideInstant();
        }

        public UniTask<ActionChoiceResult> ShowChoicesAsync(
            string prompt,
            IReadOnlyList<ActionData> actionChoices,
            string continueConversationLabel)
        {
            EnsureUi();
            if (rootGroup == null || buttonContainer == null)
            {
                return UniTask.FromResult(new ActionChoiceResult(null, true));
            }

            ClearButtons();

            if (titleText != null)
            {
                titleText.text = string.IsNullOrWhiteSpace(prompt) ? "どう過ごす？" : prompt;
            }

            currentSelectionSource = new UniTaskCompletionSource<ActionChoiceResult>();

            if (actionChoices != null)
            {
                for (int i = 0; i < actionChoices.Count; i++)
                {
                    ActionData action = actionChoices[i];
                    if (action == null)
                    {
                        continue;
                    }

                    CreateChoiceButton($"▶ {action.DisplayName}", () => SelectAction(action));
                }
            }

            CreateChoiceButton($"▶ {continueConversationLabel}", () => SelectContinueConversation());
            rootGroup.transform.SetAsLastSibling();
            rootGroup.alpha = 1f;
            rootGroup.blocksRaycasts = true;
            rootGroup.interactable = true;
            rootGroup.gameObject.SetActive(true);
            return currentSelectionSource.Task;
        }

        public void HideInstant()
        {
            if (rootGroup == null)
            {
                return;
            }

            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
            rootGroup.gameObject.SetActive(false);
        }

        private void SelectAction(ActionData actionData)
        {
            FinishSelection(new ActionChoiceResult(actionData, false));
        }

        private void SelectContinueConversation()
        {
            FinishSelection(new ActionChoiceResult(null, true));
        }

        private void FinishSelection(ActionChoiceResult result)
        {
            HideInstant();
            currentSelectionSource?.TrySetResult(result);
            currentSelectionSource = null;
        }

        private void EnsureUi()
        {
            if (rootCanvas == null)
            {
                ChatUIController chatUi = GetComponent<ChatUIController>();
                if (chatUi != null && chatUi.messageText != null)
                {
                    rootCanvas = chatUi.messageText.canvas;
                    cachedFont = chatUi.messageText.font;
                }
            }

            if (rootCanvas == null)
            {
                return;
            }

            if (rootGroup != null)
            {
                return;
            }

            GameObject root = new GameObject("ActionChoicePanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.transform.SetParent(rootCanvas.transform, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(720f, 480f);
            rootRect.anchoredPosition = new Vector2(0f, 30f);

            rootGroup = root.GetComponent<CanvasGroup>();
            Image background = root.GetComponent<Image>();
            background.color = new Color(0.06f, 0.06f, 0.08f, 0.92f);

            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 28, 28);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            titleText = CreateTextElement("ActionChoiceTitle", root.transform, 34f, FontStyles.Bold);
            titleText.alignment = TextAlignmentOptions.Center;
            LayoutElement titleLayout = titleText.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 56f;

            GameObject content = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(root.transform, false);
            buttonContainer = content.GetComponent<RectTransform>();
            VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 14f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandHeight = false;

            ContentSizeFitter contentFitter = content.GetComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void ClearButtons()
        {
            for (int i = 0; i < spawnedButtons.Count; i++)
            {
                Destroy(spawnedButtons[i]);
            }

            spawnedButtons.Clear();
        }

        private void CreateChoiceButton(string label, Action onClick)
        {
            GameObject buttonObject = new GameObject("ActionChoiceButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(buttonContainer, false);
            spawnedButtons.Add(buttonObject);

            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(0f, 72f);

            LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 72f;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.16f, 0.16f, 0.2f, 0.96f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(0.28f, 0.28f, 0.33f, 1f);
            colors.pressedColor = new Color(0.35f, 0.35f, 0.4f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            TextMeshProUGUI labelText = CreateTextElement("Label", buttonObject.transform, 28f, FontStyles.Normal);
            RectTransform labelRect = labelText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(24f, 10f);
            labelRect.offsetMax = new Vector2(-24f, -10f);
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private TextMeshProUGUI CreateTextElement(string objectName, Transform parent, float fontSize, FontStyles fontStyle)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = cachedFont != null ? cachedFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
    }
}
