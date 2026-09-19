using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    [RequireComponent(typeof(InputField))]
    public sealed class InputFieldCaretNormalizer : MonoBehaviour
    {
        private InputField inputField;

        private void Awake()
        {
            Normalize();
        }

        private void OnEnable()
        {
            Normalize();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Normalize();
        }
#endif

        public void Normalize()
        {
            inputField ??= GetComponent<InputField>();
            if (inputField == null)
            {
                return;
            }

            string text = inputField.text ?? string.Empty;
            int caret = Mathf.Clamp(inputField.caretPosition, 0, text.Length);
            inputField.caretPosition = caret;
            inputField.selectionAnchorPosition = caret;
            inputField.selectionFocusPosition = caret;
        }
    }
}
