using UnityEngine;

namespace Nekolpos.UI
{
    [DisallowMultipleComponent]
    public sealed class UIThemeApplier : MonoBehaviour
    {
        [SerializeField] private UIThemeProfile profile;
        [SerializeField] private bool applyOnAwake = true;

        public UIThemeProfile Profile
        {
            get => profile;
            set => profile = value;
        }

        private void Awake()
        {
            if (applyOnAwake)
            {
                Apply();
            }
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= ApplyAfterValidate;
                UnityEditor.EditorApplication.delayCall += ApplyAfterValidate;
            }
#endif
        }

#if UNITY_EDITOR
        private void ApplyAfterValidate()
        {
            UnityEditor.EditorApplication.delayCall -= ApplyAfterValidate;
            if (this == null || Application.isPlaying)
            {
                return;
            }

            Apply();
        }
#endif

        public void Apply()
        {
            UIStyle.ApplyTree(transform, profile);
        }
    }
}
