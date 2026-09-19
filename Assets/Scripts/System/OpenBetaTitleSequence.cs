using System;
using System.Collections;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Audio;
using Nekolpos.Data;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.System
{
    public static class OpenBetaDialogueKeys
    {
        public const string IntroBody = "OBT_INTRO_BODY";
        public const string StartDemoButton = "OBT_BUTTON_START_DEMO";
        public const string AboutButton = "OBT_BUTTON_ABOUT";
        public const string QuitButton = "OBT_BUTTON_QUIT";
        public const string AboutBody = "OBT_ABOUT_BODY";
        public const string SteamButton = "OBT_BUTTON_STEAM";
        public const string WorldviewButton = "OBT_WorldviewButton";
        public const string Worldview = "OBT_Worldview";
        public const string TrainingDataButton = "OBT_TrainingDataButton";
        public const string TrainingData = "OBT_TrainingData";
        public const string LogButton = "OBT_LogButton";
        public const string Precautions = "OBT_Precautions";
        public const string LogPrecautions = "OBT_LOGPrecautions";
        public const string LogStatusUploaded = "OBT_LogStatusUploaded";
        public const string LogHistorySummary = "OBT_LogHistorySummary";
        public const string LogConfirmUploadSelectionButton = "OBT_LogConfirmUploadSelectionButton";
        public const string LogDeleteUnselectedButton = "OBT_LogDeleteUnselectedButton";
        public const string LogDeleteUnselectedButtonConfirm = "OBT_LogDeleteUnselectedButtonConfirm";
        public const string SubmissionConfirmation = "OBT_SubmissionConfirmation";
        public const string SubmissionConfirmationButton = "OBT_SubmissionConfirmationButton";
        public const string SubmissionConfirmationButtonSending = "OBT_SubmissionConfirmationButton2";
        public const string SubmissionConfirmationAfter = "OBT_SubmissionConfirmationAfter";
        public const string EnqueteButton = "OBT_EnqueteButton";
        public const string CloseButton = "OBT_CloseButton";
        public const string BackButton = "OBT_BUTTON_BACK";
        public const string CharacterSetupTitle = "OBT_CHARACTER_SETUP_TITLE";
        public const string PlayerNameLabel = "OBT_LABEL_PLAYER_NAME";
        public const string CatNameLabel = "OBT_LABEL_CAT_NAME";
        public const string PlayerCallingLabel = "OBT_LABEL_PLAYER_CALLING";
        public const string CatFirstPersonLabel = "OBT_CatFirstPersonInput";
        public const string ContinueButton = "OBT_BUTTON_CONTINUE";
        public const string RequiredNotice = "OBT_REQUIRED_NOTICE";
        public const string CallCatBody = "OBT_CALL_CAT_BODY";
        public const string SendButton = "OBT_BUTTON_SEND";
        public const string OpeningCorrect = "OBT_OPENING_CALLED_CORRECT";
        public const string OpeningIncorrect = "OBT_OPENING_CALLED_INCORRECT";
    }

    public enum OpenBetaTitleAnimationPatternSelection
    {
        Auto,
        PatternA,
        PatternB
    }

    [Serializable]
    public sealed class OpenBetaTitleAnimationPattern
    {
        [SerializeField] private string label;
        [SerializeField] private GameObject rootObject;
        [SerializeField] private Animator catAnimator;
        [SerializeField] private Animator cameraAnimator;
        [SerializeField] private PlayableDirector initialDirector;
        [SerializeField] private PlayableDirector moveStartDirector;
        [SerializeField] private PlayableDirector moveEndDirector;
        [SerializeField] private bool playInitialDirector;
        [SerializeField] private bool preserveMoveEndRootTransform;

        public OpenBetaTitleAnimationPattern()
        {
        }

        public OpenBetaTitleAnimationPattern(string label)
        {
            this.label = label;
            playInitialDirector = string.Equals(label, "A", StringComparison.OrdinalIgnoreCase);
            preserveMoveEndRootTransform = string.Equals(label, "B", StringComparison.OrdinalIgnoreCase);
        }

        public string Label => string.IsNullOrWhiteSpace(label) ? "Pattern" : label;
        public GameObject RootObject { get => rootObject; set => rootObject = value; }
        public Animator CatAnimator { get => catAnimator; set => catAnimator = value; }
        public Animator CameraAnimator { get => cameraAnimator; set => cameraAnimator = value; }
        public PlayableDirector InitialDirector { get => initialDirector; set => initialDirector = value; }
        public PlayableDirector MoveStartDirector { get => moveStartDirector; set => moveStartDirector = value; }
        public PlayableDirector MoveEndDirector { get => moveEndDirector; set => moveEndDirector = value; }
        public bool PlayInitialDirector { get => playInitialDirector; set => playInitialDirector = value; }
        public bool PreserveMoveEndRootTransform { get => preserveMoveEndRootTransform; set => preserveMoveEndRootTransform = value; }

        public bool HasAnyReference =>
            rootObject != null ||
            catAnimator != null ||
            cameraAnimator != null ||
            initialDirector != null ||
            moveStartDirector != null ||
            moveEndDirector != null;

        public bool HasPlayableReference =>
            initialDirector != null ||
            moveStartDirector != null ||
            moveEndDirector != null;

        public bool IsRootActive => rootObject != null && rootObject.activeInHierarchy;
    }

    [RequireComponent(typeof(BackgroundMusicController))]
    [DefaultExecutionOrder(20000)]





    public static class OpenBetaUiFactory
    {
        public static GameObject CreatePanel(string name, Transform parent)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 520f);
            rect.anchoredPosition = Vector2.zero;
            UIStyle.ApplyPanel(panel);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 34, 34);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            return panel;
        }

        public static CanvasGroup CreateCanvasGroupPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            UIStyle.ApplyPanel(panel);
            return panel.GetComponent<CanvasGroup>();
        }

        public static TextMeshProUGUI CreateTmpText(string name, Transform parent, float fontSize, FontStyles style)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = UIStyle.NormalText;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        public static TextMeshProUGUI CreateBodyText(Transform parent)
        {
            TextMeshProUGUI body = CreateTmpText("BodyText", parent, 24f, FontStyles.Normal);
            body.alignment = TextAlignmentOptions.Center;
            LayoutElement layout = body.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 660f;
            layout.preferredHeight = 220f;
            return body;
        }

        public static Button CreateButton(string name, Transform parent, out TextMeshProUGUI label, Vector2 size)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            label = CreateTmpText("Label", buttonObject.transform, 22f, FontStyles.Normal);
            Stretch(label.rectTransform);
            Button button = buttonObject.GetComponent<Button>();
            UIStyle.ApplyButton(button);
            return button;
        }

        public static InputField CreateInputField(string name, Transform parent, Vector2 size)
        {
            GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputFieldCaretNormalizer), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);
            inputObject.GetComponent<LayoutElement>().preferredWidth = size.x;
            inputObject.GetComponent<LayoutElement>().preferredHeight = size.y;
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(inputObject.transform, false);
            Text text = textObject.GetComponent<Text>();
            text.font = ResolveLegacyJapaneseInputFont();
            text.fontSize = 22;
            text.color = UIStyle.NormalText;
            text.alignment = TextAnchor.MiddleLeft;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            Stretch(textRect);
            textRect.offsetMin = new Vector2(12f, 6f);
            textRect.offsetMax = new Vector2(-12f, -6f);

            InputField input = inputObject.GetComponent<InputField>();
            input.textComponent = text;
            input.lineType = InputField.LineType.SingleLine;
            UIStyle.ApplyInputField(input);
            inputObject.GetComponent<InputFieldCaretNormalizer>()?.Normalize();
            return input;
        }

        public static Font ResolveLegacyJapaneseInputFont()
        {
            Font font = Resources.Load<Font>("Fonts/NotoSansJP-Regular");
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
