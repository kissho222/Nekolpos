using Cysharp.Threading.Tasks;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.ActionSystem
{
    public class ResultTextUI : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private TextMeshProUGUI resultText;

        private TMP_FontAsset cachedFont;

        public void Initialize(Canvas canvas, TMP_FontAsset font)
        {
            rootCanvas = canvas;
            cachedFont = font;
            EnsureUi();
            HideInstant();
        }

        public async UniTask ShowResultAsync(string message, float durationSeconds)
        {
            EnsureUi();
            if (rootGroup == null || resultText == null)
            {
                return;
            }

            resultText.text = message;
            BringToFront();
            rootGroup.gameObject.SetActive(true);
            rootGroup.alpha = 1f;
            await UniTask.Delay(global::System.TimeSpan.FromSeconds(Mathf.Max(0.1f, durationSeconds)));
            rootGroup.alpha = 0f;
            rootGroup.gameObject.SetActive(false);
        }

        public void HideInstant()
        {
            if (rootGroup == null)
            {
                return;
            }

            rootGroup.alpha = 0f;
            rootGroup.gameObject.SetActive(false);
        }

        public void BringToFront()
        {
            if (rootGroup != null)
            {
                rootGroup.transform.SetAsLastSibling();
            }
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

            if (rootCanvas == null || rootGroup != null)
            {
                return;
            }

            GameObject root = new GameObject("ActionResultPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.transform.SetParent(rootCanvas.transform, false);

            RectTransform rectTransform = root.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(980f, 340f);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.78f);

            rootGroup = root.GetComponent<CanvasGroup>();

            GameObject textObject = new GameObject("ResultText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            resultText = textObject.GetComponent<TextMeshProUGUI>();

            RectTransform textRect = resultText.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(42f, 32f);
            textRect.offsetMax = new Vector2(-42f, -32f);

            resultText.font = cachedFont != null ? cachedFont : TMP_Settings.defaultFontAsset;
            resultText.fontSize = 34f;
            resultText.color = Color.white;
            resultText.alignment = TextAlignmentOptions.Center;
            resultText.textWrappingMode = TextWrappingModes.Normal;
        }
    }
}
