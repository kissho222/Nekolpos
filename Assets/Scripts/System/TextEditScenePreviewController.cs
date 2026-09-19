using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    [ExecuteAlways]
    public class TextEditScenePreviewController : MonoBehaviour
    {
        private const float PanelVerticalOffset = 205f;
        private const float EnglishPanelPosY = -62f;

        [SerializeField] private TextEditPreviewState previewState;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private TextMeshProUGUI infoText;

        private long appliedRevision = long.MinValue;
        private LanguagePanel chinesePanel;
        private LanguagePanel englishPanel;

        private sealed class LanguagePanel
        {
            public GameObject Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Speaker;
            public TextMeshProUGUI Message;
        }

        private void OnEnable()
        {
            ApplyPreview(force: true);
        }

        private void OnValidate()
        {
            ApplyPreview(force: true);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                ApplyPreview(force: false);
            }
        }

        public void Bind(TextEditPreviewState stateAsset, ChatUIController targetChatUI)
        {
            previewState = stateAsset;
            chatUI = targetChatUI;
            ApplyPreview(force: true);
        }

        public void RefreshPreview()
        {
            ApplyPreview(force: true);
        }

        private void ApplyPreview(bool force)
        {
            if (previewState == null)
            {
                return;
            }

            if (chatUI == null)
            {
                ChatUIController[] candidates = Resources.FindObjectsOfTypeAll<ChatUIController>();
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].gameObject.scene == gameObject.scene)
                    {
                        chatUI = candidates[i];
                        break;
                    }
                }

                if (chatUI == null)
                {
                    return;
                }
            }

            if (!force && appliedRevision == previewState.Revision)
            {
                return;
            }

            EnsurePanels();
            EnsureInfoText();

            ApplyToMainPanel();
            ApplyToLanguagePanel(chinesePanel, "猫又 [简中]", previewState.MessageChinese);
            ApplyToLanguagePanel(englishPanel, "猫又 [EN]", previewState.MessageEnglish);

            List<string> warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(previewState.Warnings))
            {
                warnings.Add(previewState.Warnings);
            }

            CollectOverflowWarning(chatUI.messageText, "JP", warnings);
            CollectOverflowWarning(chinesePanel != null ? chinesePanel.Message : null, "简中", warnings);
            CollectOverflowWarning(englishPanel != null ? englishPanel.Message : null, "EN", warnings);
            UpdateInfoText(warnings);

            appliedRevision = previewState.Revision;
        }

        private void EnsurePanels()
        {
            if (chatUI == null || chatUI.chatWindowGroup == null)
            {
                return;
            }

            if (chinesePanel == null || chinesePanel.Root == null)
            {
                chinesePanel = CreateLanguagePanel("TextEditPreview_Chinese");
            }

            if (englishPanel == null || englishPanel.Root == null)
            {
                englishPanel = CreateLanguagePanel("TextEditPreview_English");
            }

            ApplyPanelLayout();
        }

        private void EnsureInfoText()
        {
            if (chatUI == null || chatUI.chatWindowGroup == null)
            {
                return;
            }

            RectTransform templateRect = chatUI.chatWindowGroup.GetComponent<RectTransform>();
            if (templateRect == null || templateRect.parent == null)
            {
                return;
            }

            if (infoText == null)
            {
                Transform existing = templateRect.parent.Find("TextEditPreview_Info");
                if (existing != null)
                {
                    infoText = existing.GetComponent<TextMeshProUGUI>();
                }
            }

            if (infoText != null)
            {
                return;
            }

            GameObject infoObject = new GameObject("TextEditPreview_Info", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform infoRect = infoObject.GetComponent<RectTransform>();
            infoRect.SetParent(templateRect.parent, false);
            infoRect.anchorMin = new Vector2(0f, 1f);
            infoRect.anchorMax = new Vector2(0f, 1f);
            infoRect.pivot = new Vector2(0f, 1f);
            infoRect.anchoredPosition = new Vector2(32f, -24f);
            infoRect.sizeDelta = new Vector2(720f, 150f);

            infoText = infoObject.GetComponent<TextMeshProUGUI>();
            infoText.font = chatUI.messageText != null ? chatUI.messageText.font : TMP_Settings.defaultFontAsset;
            infoText.fontSize = 24f;
            infoText.color = new Color(0.16f, 0.16f, 0.16f, 1f);
            infoText.textWrappingMode = TextWrappingModes.Normal;
            infoText.alignment = TextAlignmentOptions.TopLeft;
        }

        private void ApplyToMainPanel()
        {
            if (chatUI.choicePanel != null)
            {
                chatUI.choicePanel.SetActive(false);
            }

            if (chatUI.logWindowPanel != null)
            {
                chatUI.logWindowPanel.SetActive(false);
            }

            if (chatUI.chatInputField != null)
            {
                chatUI.chatInputField.gameObject.SetActive(false);
            }

            if (chatUI.chatWindowGroup != null)
            {
                chatUI.chatWindowGroup.alpha = 1f;
                chatUI.chatWindowGroup.blocksRaycasts = true;
                chatUI.chatWindowGroup.interactable = true;
            }

            if (chatUI.speakerNameText != null)
            {
                chatUI.speakerNameText.text = string.IsNullOrEmpty(previewState.SpeakerName) ? "猫又" : previewState.SpeakerName;
            }

            if (chatUI.messageText != null)
            {
                chatUI.messageText.text = previewState.Message ?? string.Empty;
                chatUI.messageText.ForceMeshUpdate();
            }
        }

        private void ApplyToLanguagePanel(LanguagePanel panel, string speakerLabel, string message)
        {
            if (panel == null || panel.Root == null)
            {
                return;
            }

            panel.Root.SetActive(true);

            if (panel.Group != null)
            {
                panel.Group.alpha = 1f;
                panel.Group.blocksRaycasts = false;
                panel.Group.interactable = false;
            }

            if (panel.Speaker != null)
            {
                panel.Speaker.text = speakerLabel;
            }

            if (panel.Message != null)
            {
                panel.Message.text = message ?? string.Empty;
                panel.Message.ForceMeshUpdate();
            }
        }

        private void CollectOverflowWarning(TMP_Text text, string label, List<string> warnings)
        {
            if (text == null)
            {
                return;
            }

            text.ForceMeshUpdate();
            if (text.isTextOverflowing)
            {
                warnings.Add($"{label} テキストがボックスから溢れています");
            }
        }

        private void UpdateInfoText(List<string> warnings)
        {
            if (infoText == null)
            {
                return;
            }

            string warningText = warnings.Count > 0 ? string.Join("\n", warnings) : "警告なし";
            infoText.text =
                $"regex_id: {previewState.RegexId}\n" +
                $"pattern: {previewState.Pattern}\n" +
                $"line: {previewState.SequenceIndex}/{previewState.SequenceCount}\n" +
                $"{previewState.StateSummary}\n" +
                $"{warningText}";
        }

        private LanguagePanel CreateLanguagePanel(string panelName)
        {
            GameObject template = chatUI.chatWindowGroup != null ? chatUI.chatWindowGroup.gameObject : null;
            if (template == null)
            {
                return null;
            }

            Transform existing = template.transform.parent != null ? template.transform.parent.Find(panelName) : null;
            GameObject clone = existing != null ? existing.gameObject : null;
            if (clone == null)
            {
                clone = Instantiate(template, template.transform.parent);
                clone.name = panelName;
                clone.hideFlags = HideFlags.None;
            }

            RectTransform templateRect = template.GetComponent<RectTransform>();
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            if (templateRect != null && cloneRect != null)
            {
                cloneRect.anchorMin = templateRect.anchorMin;
                cloneRect.anchorMax = templateRect.anchorMax;
                cloneRect.pivot = templateRect.pivot;
                cloneRect.sizeDelta = templateRect.sizeDelta;
                cloneRect.localScale = templateRect.localScale;
            }

            CanvasGroup cloneGroup = clone.GetComponent<CanvasGroup>();
            if (cloneGroup != null)
            {
                cloneGroup.blocksRaycasts = false;
                cloneGroup.interactable = false;
            }

            DisableInteractiveComponents(clone);

            return new LanguagePanel
            {
                Root = clone,
                Group = cloneGroup,
                Speaker = FindTextByName(clone.transform, "SpeakerNameText"),
                Message = FindTextByName(clone.transform, "MessageText")
            };
        }

        private void ApplyPanelLayout()
        {
            if (chatUI == null || chatUI.chatWindowGroup == null)
            {
                return;
            }

            RectTransform japaneseRect = chatUI.chatWindowGroup.GetComponent<RectTransform>();
            if (japaneseRect == null)
            {
                return;
            }

            SetPanelOffset(chinesePanel, japaneseRect, 1);
            SetPanelOffset(englishPanel, japaneseRect, 2, EnglishPanelPosY);
        }

        private void SetPanelOffset(LanguagePanel panel, RectTransform japaneseRect, int stackIndex, float? explicitPosY = null)
        {
            if (panel == null || panel.Root == null || japaneseRect == null)
            {
                return;
            }

            RectTransform panelRect = panel.Root.GetComponent<RectTransform>();
            if (panelRect == null)
            {
                return;
            }

            panelRect.anchorMin = japaneseRect.anchorMin;
            panelRect.anchorMax = japaneseRect.anchorMax;
            panelRect.pivot = japaneseRect.pivot;
            panelRect.sizeDelta = japaneseRect.sizeDelta;
            panelRect.localScale = japaneseRect.localScale;
            Vector2 anchoredPosition = japaneseRect.anchoredPosition + new Vector2(0f, PanelVerticalOffset * stackIndex);
            if (explicitPosY.HasValue)
            {
                anchoredPosition.y = explicitPosY.Value;
            }

            panelRect.anchoredPosition = anchoredPosition;
        }

        private void DisableInteractiveComponents(GameObject root)
        {
            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].interactable = false;
            }

            InputField[] inputFields = root.GetComponentsInChildren<InputField>(true);
            for (int i = 0; i < inputFields.Length; i++)
            {
                inputFields[i].gameObject.SetActive(false);
            }
        }

        private TextMeshProUGUI FindTextByName(Transform root, string objectName)
        {
            if (root.name == objectName)
            {
                return root.GetComponent<TextMeshProUGUI>();
            }

            for (int i = 0; i < root.childCount; i++)
            {
                TextMeshProUGUI found = FindTextByName(root.GetChild(i), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
