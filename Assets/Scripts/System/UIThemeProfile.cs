using UnityEngine;

namespace Nekolpos.UI
{
    public enum UIThemeKind
    {
        Normal,
        Uneasy
    }

    [CreateAssetMenu(menuName = "Nekolpos/UI Theme Profile", fileName = "UIThemeProfile")]
    public sealed class UIThemeProfile : ScriptableObject
    {
        public UIThemeKind theme = UIThemeKind.Normal;
        public Sprite panelSprite;
        public Sprite buttonSprite;
        public Sprite inputSprite;
        public Sprite borderSprite;

        [Header("Base")]
        public Color panelColor = new Color(0.91f, 0.88f, 0.80f, 0.94f);
        public Color buttonColor = new Color(0.78f, 0.73f, 0.63f, 0.96f);
        public Color inputColor = new Color(0.96f, 0.94f, 0.88f, 0.98f);
        public Color borderColor = new Color(0.55f, 0.51f, 0.43f, 0.28f);
        public Color shadowColor = new Color(0.16f, 0.14f, 0.11f, 0.18f);
        public Color textColor = new Color(0.19f, 0.18f, 0.16f, 1f);
        public Color placeholderTextColor = new Color(0.39f, 0.37f, 0.33f, 0.58f);

        [Header("Button Tint")]
        public Color buttonHighlightedColor = new Color(0.86f, 0.82f, 0.72f, 0.98f);
        public Color buttonPressedColor = new Color(0.66f, 0.61f, 0.52f, 0.98f);
        public Color buttonDisabledColor = new Color(0.60f, 0.58f, 0.54f, 0.45f);
    }
}
