using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Backgammon.Conversation
{
    public sealed class ConversationChoiceButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;

        private int choiceIndex;
        private string choiceText = string.Empty;
        private Action<int, string> onSelected;

        public int ChoiceIndex => choiceIndex;
        public string ChoiceText => choiceText;

        public void Bind(int index, string text, bool showShortcutPrefix, Action<int, string> onSelectedCallback)
        {
            choiceIndex = index;
            choiceText = text ?? string.Empty;
            onSelected = onSelectedCallback;
            label.text = showShortcutPrefix
                ? $"{choiceIndex + 1}. {choiceText}"
                : choiceText;
            button.onClick.RemoveListener(HandleClicked);
            button.onClick.AddListener(HandleClicked);
        }

        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable;
        }

        private void Reset()
        {
            button = GetComponent<Button>();
            label = GetComponentInChildren<TMP_Text>(true);
        }

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (label == null)
            {
                label = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void HandleClicked()
        {
            onSelected?.Invoke(choiceIndex, choiceText);
        }
    }
}
