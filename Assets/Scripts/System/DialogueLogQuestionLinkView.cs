using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Nekolpos.System
{
    /// <summary>
    /// Converts configured keywords to TMP links and forwards clicks to the input field.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class DialogueLogQuestionLinkView : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text targetText;
        private ChatUIController chatUI;
        private IReadOnlyList<DialogueLogQuestionLink> links;

        public void Configure(
            TMP_Text text,
            IReadOnlyList<DialogueLogQuestionLink> questionLinks,
            ChatUIController owner,
            Color linkColor)
        {
            targetText = text != null ? text : GetComponent<TMP_Text>();
            links = questionLinks;
            chatUI = owner;
            targetText.raycastTarget = true;
            targetText.text = BuildLinkedText(targetText.text, questionLinks, ColorUtility.ToHtmlStringRGB(linkColor));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (targetText == null || links == null || chatUI == null)
            {
                return;
            }

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(
                targetText,
                eventData.position,
                eventData.pressEventCamera);
            if (linkIndex < 0)
            {
                return;
            }

            TMP_LinkInfo linkInfo = targetText.textInfo.linkInfo[linkIndex];
            if (!int.TryParse(linkInfo.GetLinkID(), out int questionIndex) ||
                questionIndex < 0 ||
                questionIndex >= links.Count)
            {
                return;
            }

            DialogueLogQuestionLink link = links[questionIndex];
            if (link != null && !string.IsNullOrWhiteSpace(link.insertText))
            {
                chatUI.SetInputTextFromSuggestion(link.insertText);
            }
        }

        public static string BuildLinkedText(
            string source,
            IReadOnlyList<DialogueLogQuestionLink> questionLinks,
            string colorHex)
        {
            if (string.IsNullOrEmpty(source) || questionLinks == null || questionLinks.Count == 0)
            {
                return source ?? string.Empty;
            }

            var matches = new List<LinkMatch>();
            for (int i = 0; i < questionLinks.Count; i++)
            {
                DialogueLogQuestionLink link = questionLinks[i];
                if (link == null ||
                    string.IsNullOrEmpty(link.keyword) ||
                    string.IsNullOrWhiteSpace(link.insertText))
                {
                    continue;
                }

                int keywordIndex = source.IndexOf(link.keyword, global::System.StringComparison.Ordinal);
                if (keywordIndex < 0)
                {
                    continue;
                }

                matches.Add(new LinkMatch(keywordIndex, link.keyword.Length, i, link.keyword));
            }

            matches.Sort((left, right) => left.Start.CompareTo(right.Start));
            var builder = new StringBuilder(source.Length + matches.Count * 48);
            int sourceIndex = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                LinkMatch match = matches[i];
                if (match.Start < sourceIndex)
                {
                    continue;
                }

                builder.Append(source, sourceIndex, match.Start - sourceIndex);
                builder.Append("<link=\"").Append(match.LinkIndex).Append("\"><color=#")
                    .Append(colorHex).Append('>').Append(match.Keyword)
                    .Append("</color></link>");
                sourceIndex = match.Start + match.Length;
            }

            builder.Append(source, sourceIndex, source.Length - sourceIndex);
            return builder.ToString();
        }

        private readonly struct LinkMatch
        {
            public readonly int Start;
            public readonly int Length;
            public readonly int LinkIndex;
            public readonly string Keyword;

            public LinkMatch(int start, int length, int linkIndex, string keyword)
            {
                Start = start;
                Length = length;
                LinkIndex = linkIndex;
                Keyword = keyword;
            }
        }
    }
}
