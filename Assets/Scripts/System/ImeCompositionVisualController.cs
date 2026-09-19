using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    public sealed class ImeCompositionVisualController : MonoBehaviour
    {
        [SerializeField] private InputField legacyInputField;
        [SerializeField] private TMP_InputField tmpInputField;

        private Canvas rootCanvas;
        private const string OverlayObjectName = "ImeCompositionOverlay";

        public void Bind(InputField inputField)
        {
            legacyInputField = inputField;
            tmpInputField = null;
            RemoveOverlay();
        }

        public void Bind(TMP_InputField inputField)
        {
            tmpInputField = inputField;
            legacyInputField = null;
            ConfigureTmpInputField();
            RemoveOverlay();
        }

        private void Awake()
        {
            if (legacyInputField == null)
            {
                legacyInputField = GetComponent<InputField>();
            }

            if (tmpInputField == null)
            {
                tmpInputField = GetComponent<TMP_InputField>();
            }

            ConfigureTmpInputField();
            RemoveOverlay();
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#else
            Refresh();
#endif
        }

        public void Refresh()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#else
            string composition = Input.compositionString;
            if (!IsFocused() || string.IsNullOrEmpty(composition))
            {
                return;
            }

            UpdateCompositionCursorPosition(GetTextBeforeCaret());
#endif
        }

        private bool IsFocused()
        {
            if (tmpInputField != null)
            {
                return tmpInputField.isFocused && tmpInputField.gameObject.activeInHierarchy;
            }

            return legacyInputField != null &&
                   legacyInputField.isFocused &&
                   legacyInputField.gameObject.activeInHierarchy;
        }

        private string GetTextBeforeCaret()
        {
            string text = tmpInputField != null
                ? tmpInputField.text ?? string.Empty
                : legacyInputField != null ? legacyInputField.text ?? string.Empty : string.Empty;
            int caret = tmpInputField != null
                ? tmpInputField.caretPosition
                : legacyInputField != null ? legacyInputField.caretPosition : 0;
            caret = Mathf.Clamp(caret, 0, text.Length);
            return text.Substring(0, caret);
        }

        private void ConfigureTmpInputField()
        {
            if (tmpInputField == null)
            {
                return;
            }

            tmpInputField.richText = true;
            tmpInputField.isRichTextEditingAllowed = false;
            if (tmpInputField.textComponent != null)
            {
                tmpInputField.textComponent.richText = true;
            }
        }

        private void RemoveOverlay()
        {
            Transform parent = tmpInputField != null
                ? tmpInputField.transform
                : legacyInputField != null ? legacyInputField.transform : transform;
            Transform overlay = parent != null ? parent.Find(OverlayObjectName) : null;
            if (overlay != null)
            {
                Destroy(overlay.gameObject);
            }
        }

        private void UpdateCompositionCursorPosition(string prefix)
        {
            rootCanvas ??= GetComponentInParent<Canvas>();
            Camera camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;

            RectTransform textRect = null;
            float prefixWidth = 0f;
            if (tmpInputField != null && tmpInputField.textComponent != null)
            {
                TMP_Text text = tmpInputField.textComponent;
                textRect = text.rectTransform;
                prefixWidth = text.GetPreferredValues(RichTextEscaper.Escape(prefix), 10000f, textRect.rect.height).x;
            }
            else if (legacyInputField != null && legacyInputField.textComponent != null)
            {
                Text text = legacyInputField.textComponent;
                textRect = text.rectTransform;
                TextGenerationSettings settings = text.GetGenerationSettings(textRect.rect.size);
                prefixWidth = text.cachedTextGeneratorForLayout.GetPreferredWidth(prefix, settings) / text.pixelsPerUnit;
            }

            if (textRect == null)
            {
                return;
            }

            float x = textRect.rect.xMin + Mathf.Max(0f, prefixWidth);
            float y = textRect.rect.center.y;
            Vector3 world = textRect.TransformPoint(new Vector3(x, y, 0f));
            Input.compositionCursorPos = RectTransformUtility.WorldToScreenPoint(camera, world);
        }
    }
}
