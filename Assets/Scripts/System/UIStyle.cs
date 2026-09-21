using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nekolpos.System;

namespace Nekolpos.UI
{
    public static class UIStyle
    {
        private const string BorderObjectName = "UIStyleBorder";
        private static Sprite panelSprite;
        private static Sprite buttonSprite;
        private static Sprite inputSprite;
        private static Sprite borderSprite;

        public static readonly Color NormalPanel = new Color(0.91f, 0.88f, 0.80f, 0.94f);
        public static readonly Color NormalButton = new Color(0.78f, 0.73f, 0.63f, 0.96f);
        public static readonly Color NormalInput = new Color(0.96f, 0.94f, 0.88f, 0.98f);
        public static readonly Color NormalHighlighted = new Color(0.86f, 0.82f, 0.72f, 0.98f);
        public static readonly Color NormalPressed = new Color(0.66f, 0.61f, 0.52f, 0.98f);
        public static readonly Color NormalDisabled = new Color(0.60f, 0.58f, 0.54f, 0.45f);
        public static readonly Color NormalBorder = new Color(0.55f, 0.51f, 0.43f, 0.28f);
        public static readonly Color NormalShadow = new Color(0.16f, 0.14f, 0.11f, 0.18f);
        public static readonly Color NormalText = new Color(0.19f, 0.18f, 0.16f, 1f);
        public static readonly Color ButtonText = new Color(0.96f, 0.96f, 0.92f, 1f);
        public static readonly Color NormalPlaceholder = new Color(0.39f, 0.37f, 0.33f, 0.58f);

        public static readonly Color UneasyPanel = new Color(0.12f, 0.13f, 0.15f, 0.96f);
        public static readonly Color UneasyButton = new Color(0.23f, 0.25f, 0.28f, 0.98f);
        public static readonly Color UneasyInput = new Color(0.18f, 0.19f, 0.21f, 0.98f);
        public static readonly Color UneasyText = new Color(0.78f, 0.77f, 0.72f, 1f);
        public static readonly Color UneasyBorder = new Color(0.58f, 0.57f, 0.52f, 0.20f);
        public static readonly Color UneasyShadow = new Color(0f, 0f, 0f, 0.34f);

        public static void ApplyPanel(GameObject target, UIThemeProfile profile = null, bool shadow = true)
        {
            if (target == null)
            {
                return;
            }

            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                ConfigureSlicedImage(image, profile != null && profile.panelSprite != null ? profile.panelSprite : PanelSprite);
                image.color = profile != null ? profile.panelColor : NormalPanel;
                image.raycastTarget = true;
                EnsureBorder(image, profile);
            }

            if (shadow)
            {
                EnsureShadow(target, profile != null ? profile.shadowColor : NormalShadow);
            }
        }

        public static void ApplyButton(Button button, UIThemeProfile profile = null)
        {
            if (button == null)
            {
                return;
            }

            if (IsTalkTopicHintBubbleButton(button))
            {
                RemoveBorder(button.transform);
                return;
            }

            if (IsSteamButton(button))
            {
                ApplySteamButton(button);
                return;
            }

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                ConfigureSlicedImage(image, profile != null && profile.buttonSprite != null ? profile.buttonSprite : ButtonSprite);
                // Selectable already supplies the full tint; multiplying it by itself washes out contrast.
                image.color = Color.white;
                EnsureBorder(image, profile);
            }

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = profile != null ? profile.buttonColor : NormalButton;
            colors.highlightedColor = profile != null ? profile.buttonHighlightedColor : NormalHighlighted;
            colors.pressedColor = profile != null ? profile.buttonPressedColor : NormalPressed;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = profile != null ? profile.buttonDisabledColor : NormalDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            EnsureShadow(button.gameObject, profile != null ? profile.shadowColor : NormalShadow);
            ApplyButtonTextColors(button.transform,
                profile != null && profile.theme == UIThemeKind.Uneasy ? ButtonText : NormalText);
        }

        private static void ApplySteamButton(Button button)
        {
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = Color.white;
                image.raycastTarget = true;
                RemoveBorder(image.transform);
            }

            Shadow shadow = button.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.enabled = false;
            }

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.66f, 0.86f, 1f, 1f);
            colors.pressedColor = new Color(0.28f, 0.55f, 0.88f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.62f, 0.68f, 0.74f, 0.48f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            ApplyButtonTextColors(button.transform);
        }

        public static void ApplyInputField(InputField input, UIThemeProfile profile = null)
        {
            if (input == null)
            {
                return;
            }

            ApplyInputBackground(input.gameObject, profile);
            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                input.targetGraphic = image;
            }

            input.transition = Selectable.Transition.ColorTint;
            input.colors = CreateInputColorBlock(profile);

            if (input.textComponent != null)
            {
                input.textComponent.font = OpenBetaUiFactory.ResolveLegacyJapaneseInputFont();
                input.textComponent.color = profile != null ? profile.textColor : NormalText;
            }
        }

        public static void ApplyInputField(TMP_InputField input, UIThemeProfile profile = null)
        {
            if (input == null)
            {
                return;
            }

            ApplyInputBackground(input.gameObject, profile);
            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                input.targetGraphic = image;
            }

            input.transition = Selectable.Transition.ColorTint;
            input.colors = CreateInputColorBlock(profile);
            if (input.textComponent != null)
            {
                input.textComponent.color = profile != null ? profile.textColor : NormalText;
                PlayerInputFontAssetProvider.ApplyToPlayerInput(input);
                input.textComponent.richText = true;
            }

            input.richText = true;
            input.isRichTextEditingAllowed = false;
            if (input.GetComponent<ImeCompositionVisualController>() == null)
            {
                input.gameObject.AddComponent<ImeCompositionVisualController>().Bind(input);
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.color = profile != null ? profile.placeholderTextColor : NormalPlaceholder;
            }
        }

        public static void ApplyTextColors(Transform root, UIThemeProfile profile = null)
        {
            if (root == null)
            {
                return;
            }

            Color textColor = profile != null ? profile.textColor : NormalText;
            TMP_Text[] tmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                if (tmpTexts[i] != null && !IsInsideAutomaticButtonStyleExclusion(tmpTexts[i].transform))
                {
                    tmpTexts[i].color = textColor;
                }
            }

            Text[] legacyTexts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacyTexts.Length; i++)
            {
                if (legacyTexts[i] != null && !IsInsideAutomaticButtonStyleExclusion(legacyTexts[i].transform))
                {
                    legacyTexts[i].color = textColor;
                }
            }
        }

        public static void ApplyButtonTextColors(Transform root, Color? textColor = null)
        {
            if (root == null)
            {
                return;
            }

            TMP_Text[] tmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                if (tmpTexts[i] != null)
                {
                    tmpTexts[i].color = textColor ?? ButtonText;
                }
            }

            Text[] legacyTexts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacyTexts.Length; i++)
            {
                if (legacyTexts[i] != null)
                {
                    legacyTexts[i].color = textColor ?? ButtonText;
                }
            }
        }

        public static void ApplyTree(Transform root, UIThemeProfile profile = null)
        {
            if (root == null)
            {
                return;
            }

            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.GetComponent<Button>() != null || image.GetComponent<InputField>() != null || image.GetComponent<TMP_InputField>() != null)
                {
                    continue;
                }

                if (LooksLikePanel(image.gameObject.name))
                {
                    ApplyPanel(image.gameObject, profile, true);
                }
            }

            ApplyTextColors(root, profile);

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!IsAutomaticButtonStyleExcluded(buttons[i]))
                {
                    ApplyButton(buttons[i], profile);
                }
            }

            InputField[] inputFields = root.GetComponentsInChildren<InputField>(true);
            for (int i = 0; i < inputFields.Length; i++)
            {
                ApplyInputField(inputFields[i], profile);
            }

            TMP_InputField[] tmpInputFields = root.GetComponentsInChildren<TMP_InputField>(true);
            for (int i = 0; i < tmpInputFields.Length; i++)
            {
                ApplyInputField(tmpInputFields[i], profile);
            }
        }

        private static void ApplyInputBackground(GameObject target, UIThemeProfile profile)
        {
            Image image = target.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            ConfigureSlicedImage(image, profile != null && profile.inputSprite != null ? profile.inputSprite : InputSprite);
            image.color = profile != null ? profile.inputColor : NormalInput;
            EnsureBorder(image, profile);
            EnsureShadow(target, profile != null ? profile.shadowColor : NormalShadow);
        }

        private static ColorBlock CreateInputColorBlock(UIThemeProfile profile)
        {
            Color normal = profile != null ? profile.inputColor : NormalInput;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = Color.Lerp(normal, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.08f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = profile != null ? profile.buttonDisabledColor : NormalDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        private static bool LooksLikePanel(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            return objectName.Contains("Panel") ||
                   objectName.Contains("Window") ||
                   objectName.Contains("Section") ||
                   objectName.Contains("ScrollView") ||
                   objectName.Contains("Background");
        }

        private static void ConfigureSlicedImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.fillCenter = true;
        }

        private static void EnsureShadow(GameObject target, Color color)
        {
            Shadow shadow = target.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = target.AddComponent<Shadow>();
            }

            shadow.effectColor = color;
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;
        }

        private static void EnsureBorder(Image baseImage, UIThemeProfile profile)
        {
            Transform existing = baseImage.transform.Find(BorderObjectName);
            GameObject borderObject = existing != null ? existing.gameObject : new GameObject(BorderObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null)
            {
                borderObject.transform.SetParent(baseImage.transform, false);
            }
            borderObject.transform.SetAsFirstSibling();

            RectTransform rect = borderObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            Image border = borderObject.GetComponent<Image>();
            border.sprite = profile != null && profile.borderSprite != null ? profile.borderSprite : BorderSprite;
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.color = profile != null ? profile.borderColor : NormalBorder;
            border.raycastTarget = false;
        }

        private static void RemoveBorder(Transform target)
        {
            Transform existing = target != null ? target.Find(BorderObjectName) : null;
            if (existing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(existing.gameObject);
            }
            else
            {
                Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static bool IsSteamButton(Button button)
        {
            return button != null && button.gameObject.name == "SteamButton";
        }

        private static bool IsAutomaticButtonStyleExcluded(Button button)
        {
            return button != null && button.gameObject.name == "TabletButton";
        }

        private static bool IsInsideAutomaticButtonStyleExclusion(Transform target)
        {
            Button button = target != null ? target.GetComponentInParent<Button>() : null;
            return IsAutomaticButtonStyleExcluded(button);
        }

        private static bool IsTalkTopicHintBubbleButton(Button button)
        {
            return button != null &&
                   (button.GetComponent<Nekolpos.System.TalkTopicHintBubble>() != null ||
                    button.gameObject.name == "TalkTopicHintBubbleButton");
        }

        private static Sprite PanelSprite => panelSprite ??= CreateRoundedSprite("NekolposPanelRound", 18f, 8f);
        private static Sprite ButtonSprite => buttonSprite ??= CreateRoundedSprite("NekolposButtonRound", 13f, 6f);
        private static Sprite InputSprite => inputSprite ??= CreateRoundedSprite("NekolposInputRound", 11f, 6f);
        private static Sprite BorderSprite => borderSprite ??= CreateRoundedSprite("NekolposBorderRound", 18f, 8f);

        private static Sprite CreateRoundedSprite(string name, float radius, float border)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32 clear = new Color32(255, 255, 255, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            float r = Mathf.Clamp(radius, 1f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r), 0f);
                    float dy = Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r), 0f);
                    texture.SetPixel(x, y, dx * dx + dy * dy <= r * r ? white : clear);
                }
            }

            texture.Apply();
            Vector4 borders = new Vector4(border, border, border, border);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, borders);
        }
    }
}
