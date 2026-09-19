using System.Collections;
using Nekolpos.TimeSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    public enum TimeOfDay
    {
        Morning,
        Noon,
        Evening,
        Night
    }

    public sealed class TimeOfDayWidget : MonoBehaviour
    {
        private const string LogPrefix = "[TimeOfDayWidget]";

        [global::System.Diagnostics.Conditional("NEKOLPOS_VERBOSE_LOGS")]
        private static void VerboseLog(string message)
        {
            Debug.Log(message);
        }

        [Header("Text")]
        [SerializeField] private TMP_Text dayText;
        [SerializeField] private TMP_Text labelMorning;
        [SerializeField] private TMP_Text labelNoon;
        [SerializeField] private TMP_Text labelEvening;
        [SerializeField] private TMP_Text labelNight;

        [Header("Sectors")]
        [SerializeField] private Image sectorMorning;
        [SerializeField] private Image sectorNoon;
        [SerializeField] private Image sectorEvening;
        [SerializeField] private Image sectorNight;

        [Header("Arrow")]
        [SerializeField] private RectTransform arrowRectTransform;
        [SerializeField] private bool useAnimatedArrow = true;
        [SerializeField] [Min(0f)] private float arrowRotationDuration = 0.18f;

        [Header("Visibility")]
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private bool showAfterInitialConversation = true;

        [Header("Runtime Binding")]
        [SerializeField] private TimeManager timeManager;

        [Header("Colors")]
        [SerializeField] private Color activeSectorColor = new Color(1.0f, 0.88f, 0.48f, 0.92f);
        [SerializeField] private Color inactiveSectorColor = new Color(0.28f, 0.31f, 0.35f, 0.28f);
        [SerializeField] private Color activeLabelColor = new Color(1.0f, 0.94f, 0.70f, 1f);
        [SerializeField] private Color inactiveLabelColor = new Color(0.76f, 0.75f, 0.70f, 0.72f);

        [Header("Preview")]
        [SerializeField] [Min(1)] private int previewDay = 1;
        [SerializeField] private TimeOfDay previewTimeOfDay = TimeOfDay.Morning;
        [SerializeField] private bool applyPreviewOnValidate = true;

        private static Sprite generatedCircleSprite;
        private Coroutine arrowRotationCoroutine;
        private TimeOfDay currentTimeOfDay;
        private int currentDay = 1;
        private bool hasCurrentTimeOfDay;
        private CanvasGroup canvasGroup;
        private bool initialConversationCompleted;
        private bool subscribedToTimeManager;

        private void Awake()
        {
            ResolveVisibilityReferences();
            ResolveTimeManagerReference();
            EnsureSectorSprites();
            SetState(currentDay <= 0 ? previewDay : currentDay, currentTimeOfDay);
            SetVisible(!showAfterInitialConversation);
        }

        private void OnEnable()
        {
            SubscribeTimeManager();
        }

        private void OnDisable()
        {
            UnsubscribeTimeManager();
        }

        private void Update()
        {
            if (!subscribedToTimeManager)
            {
                SubscribeTimeManager();
            }

            DetectInitialConversationCompletion();
        }

        private void OnValidate()
        {
            arrowRotationDuration = Mathf.Max(0f, arrowRotationDuration);
            previewDay = Mathf.Max(1, previewDay);

            if (!applyPreviewOnValidate || Application.isPlaying)
            {
                return;
            }

            EnsureSectorSprites();
            SetState(previewDay, previewTimeOfDay, true);
        }

        [ContextMenu("Preview Morning")]
        private void PreviewMorning() => SetState(previewDay, TimeOfDay.Morning, true);

        [ContextMenu("Preview Noon")]
        private void PreviewNoon() => SetState(previewDay, TimeOfDay.Noon, true);

        [ContextMenu("Preview Evening")]
        private void PreviewEvening() => SetState(previewDay, TimeOfDay.Evening, true);

        [ContextMenu("Preview Night")]
        private void PreviewNight() => SetState(previewDay, TimeOfDay.Night, true);

        public void SetDay(int day)
        {
            currentDay = Mathf.Max(1, day);
            if (dayText != null)
            {
                dayText.text = $"{currentDay}日目";
            }
        }

        public void SetTimeOfDay(TimeOfDay timeOfDay)
        {
            SetTimeOfDay(timeOfDay, false);
        }

        public void SetState(int day, TimeOfDay timeOfDay)
        {
            SetState(day, timeOfDay, false);
        }

        private void SetState(int day, TimeOfDay timeOfDay, bool instantArrow)
        {
            SetDay(day);
            SetTimeOfDay(timeOfDay, instantArrow);
        }

        private void SetTimeOfDay(TimeOfDay timeOfDay, bool instantArrow)
        {
            bool timeChanged = !hasCurrentTimeOfDay || currentTimeOfDay != timeOfDay;
            TimeOfDay previousTimeOfDay = currentTimeOfDay;
            currentTimeOfDay = timeOfDay;
            hasCurrentTimeOfDay = true;

            SetSector(sectorMorning, timeOfDay == TimeOfDay.Morning);
            SetSector(sectorNoon, timeOfDay == TimeOfDay.Noon);
            SetSector(sectorEvening, timeOfDay == TimeOfDay.Evening);
            SetSector(sectorNight, timeOfDay == TimeOfDay.Night);

            SetLabel(labelMorning, timeOfDay == TimeOfDay.Morning);
            SetLabel(labelNoon, timeOfDay == TimeOfDay.Noon);
            SetLabel(labelEvening, timeOfDay == TimeOfDay.Evening);
            SetLabel(labelNight, timeOfDay == TimeOfDay.Night);

            if (timeChanged || instantArrow)
            {
                RotateArrow(timeOfDay, instantArrow || !useAnimatedArrow || !Application.isPlaying);
            }

            VerboseLog($"{LogPrefix} SetTimeOfDay {previousTimeOfDay}->{timeOfDay}, changed={timeChanged}, instantArrow={instantArrow}, arrow={(arrowRectTransform != null ? arrowRectTransform.name : "null")}, angle={ResolveArrowAngle(timeOfDay)}");
        }

        private void SetSector(Image image, bool active)
        {
            if (image == null)
            {
                return;
            }

            image.color = active ? activeSectorColor : inactiveSectorColor;
        }

        private void SetLabel(TMP_Text label, bool active)
        {
            if (label == null)
            {
                return;
            }

            label.color = active ? activeLabelColor : inactiveLabelColor;
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
        }

        private void RotateArrow(TimeOfDay timeOfDay, bool instant)
        {
            if (arrowRectTransform == null)
            {
                return;
            }

            Quaternion target = Quaternion.Euler(0f, 0f, ResolveArrowAngle(timeOfDay));
            if (arrowRotationCoroutine != null)
            {
                StopCoroutine(arrowRotationCoroutine);
                arrowRotationCoroutine = null;
            }

            if (instant || arrowRotationDuration <= 0f)
            {
                arrowRectTransform.localRotation = target;
                return;
            }

            arrowRotationCoroutine = StartCoroutine(RotateArrowRoutine(target));
        }

        private IEnumerator RotateArrowRoutine(Quaternion target)
        {
            Quaternion start = arrowRectTransform.localRotation;
            for (float elapsed = 0f; elapsed < arrowRotationDuration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / arrowRotationDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                arrowRectTransform.localRotation = Quaternion.Slerp(start, target, eased);
                yield return null;
            }

            arrowRectTransform.localRotation = target;
            arrowRotationCoroutine = null;
        }

        private static float ResolveArrowAngle(TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                    return 0f;
                case TimeOfDay.Noon:
                    return -90f;
                case TimeOfDay.Evening:
                    return 180f;
                case TimeOfDay.Night:
                    return 90f;
                default:
                    return 0f;
            }
        }

        private static TimeOfDay Convert(DayPeriod period)
        {
            switch (period)
            {
                case DayPeriod.Morning:
                    return TimeOfDay.Morning;
                case DayPeriod.Afternoon:
                    return TimeOfDay.Noon;
                case DayPeriod.Evening:
                    return TimeOfDay.Evening;
                case DayPeriod.Night:
                    return TimeOfDay.Night;
                default:
                    return TimeOfDay.Morning;
            }
        }

        private void EnsureSectorSprites()
        {
            ConfigureSectorImage(sectorMorning, 45f);
            ConfigureSectorImage(sectorNoon, -45f);
            ConfigureSectorImage(sectorEvening, 225f);
            ConfigureSectorImage(sectorNight, 135f);
        }

        private void ResolveVisibilityReferences()
        {
            canvasGroup ??= GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            chatUI ??= FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
        }

        private void ResolveTimeManagerReference()
        {
            timeManager ??= FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
        }

        private void SubscribeTimeManager()
        {
            ResolveTimeManagerReference();
            if (timeManager == null)
            {
                VerboseLog($"{LogPrefix} Subscribe skipped: timeManager=null");
                return;
            }

            timeManager.OnTimeAdvanced -= HandleTimeAdvanced;
            timeManager.OnTimeAdvanced += HandleTimeAdvanced;
            subscribedToTimeManager = true;
            VerboseLog($"{LogPrefix} Subscribed to {timeManager.name}.OnTimeAdvanced");
            SetState(timeManager.CurrentDay, Convert(timeManager.CurrentPeriod));
        }

        private void UnsubscribeTimeManager()
        {
            if (timeManager != null)
            {
                timeManager.OnTimeAdvanced -= HandleTimeAdvanced;
            }

            subscribedToTimeManager = false;
        }

        private void HandleTimeAdvanced(TimeAdvanceResult result)
        {
            VerboseLog($"{LogPrefix} HandleTimeAdvanced day {result.PreviousDay}->{result.CurrentDay}, period {result.PreviousPeriod}->{result.CurrentPeriod}");
            SetState(result.CurrentDay, Convert(result.CurrentPeriod));
        }

        private void DetectInitialConversationCompletion()
        {
            if (!showAfterInitialConversation || initialConversationCompleted)
            {
                return;
            }

            ResolveVisibilityReferences();
            if (chatUI == null)
            {
                return;
            }

            InputField input = chatUI.chatInputField;
            bool inputReady = input != null && input.gameObject.activeInHierarchy && input.interactable;
            if (!chatUI.IsTyping && inputReady)
            {
                initialConversationCompleted = true;
                SetVisible(true);
            }
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private static void ConfigureSectorImage(Image image, float rotationZ)
        {
            if (image == null)
            {
                return;
            }

            if (image.sprite == null)
            {
                image.sprite = CircleSprite;
            }

            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0.25f;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        }

        private static Sprite CircleSprite
        {
            get
            {
                if (generatedCircleSprite != null)
                {
                    return generatedCircleSprite;
                }

                const int size = 96;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "TimeOfDayWidgetCircle",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                float radius = (size - 2) * 0.5f;
                Color clear = new Color(1f, 1f, 1f, 0f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = Vector2.Distance(new Vector2(x, y), center);
                        texture.SetPixel(x, y, distance <= radius ? Color.white : clear);
                    }
                }

                texture.Apply();
                generatedCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                generatedCircleSprite.name = "TimeOfDayWidgetCircle";
                return generatedCircleSprite;
            }
        }
    }

}
