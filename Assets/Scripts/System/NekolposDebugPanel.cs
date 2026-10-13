using Nekolpos.TimeSystem;
using Nekolpos.StatusSystem;
using Nekolpos.ActionSystem;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>
    /// A runtime-only development panel. It owns no game state; every change is delegated to TimeManager.
    /// </summary>
    public sealed class NekolposDebugPanel : MonoBehaviour
    {
        private const string PanelName = "Nekolpos Debug Window";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private TimeManager timeManager;
        private StatusManager statusManager;
        [SerializeField] private GameObject window;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button dayDownButton;
        [SerializeField] private Button dayButton;
        [SerializeField] private Button timeButton;
        [SerializeField] private Button weatherButton;
        [SerializeField] private Button tomorrowButton;
        [SerializeField] private Button octoberFirstSpecialDiaryButton;
        [SerializeField] private Button resetDateButton;
        [SerializeField] private Button resetDiaryButton;
        [SerializeField] private Button tableHomeButton;
        [SerializeField] private Button deskHomeButton;
        [SerializeField] private TMP_Text homeLocationTitle;
        private NekolposGameBootstrap titleBootstrap;
        private CatPresentationModeController presentationMode;
        private float refreshAt;
        [SerializeField] private Button[] statusDecreaseButtons = new Button[6];
        [SerializeField] private Button[] statusIncreaseButtons = new Button[6];
        [SerializeField] private TMP_Text[] statusValueTexts = new TMP_Text[6];
        private static readonly StatusType[] DebugStatusTypes =
        {
            StatusType.Affection,
            StatusType.Sadistic,
            StatusType.Concern,
            StatusType.Hostility,
            StatusType.Obedience,
            StatusType.Instinct
        };

        public static void EnsureVisibleForDebugStart()
        {
            var existing = FindFirstObjectByType<NekolposDebugPanel>(FindObjectsInactive.Include);
            if (existing != null) existing.Refresh();
        }

        private void Awake()
        {
            if (!Application.isPlaying) return;
            timeManager = FindFirstObjectByType<TimeManager>();
            statusManager = FindFirstObjectByType<StatusManager>();
            BindActions();
            Refresh();
        }
        private void OnEnable() { if (Application.isPlaying) Refresh(); }
        private void Update() { if (window != null && window.activeInHierarchy && Time.unscaledTime >= refreshAt) { Refresh(); refreshAt = Time.unscaledTime + .25f; } }
        private bool AllowMutation()
        {
            if (DebugConsoleGuard.CanMutate(out string reason)) return true;
            if (statusText != null) statusText.text = "操作を拒否: " + reason;
            return false;
        }
        public void ShowControls(bool value) { if (window != null) window.SetActive(value); }

#if UNITY_EDITOR
        public void BuildAuthoredUi() => BuildUi();
        private void BuildUi()
        {
            RectTransform root = (RectTransform)transform;
            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-190f, -24f);
            root.sizeDelta = new Vector2(104f, 40f);

            Button toggle = CreateButton(transform, "DebugButton", "Debug", new Vector2(104f, 40f));
            toggle.onClick.AddListener(ToggleWindow);

            window = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            window.transform.SetParent(transform, false);
            Image background = window.GetComponent<Image>();
            background.color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
            RectTransform panelRect = (RectTransform)window.transform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -48f);
            panelRect.sizeDelta = new Vector2(320f, 735f);

            // 上部のStatus領域を独立させ、時間操作や心理値と重ならないようにする。
            statusText = CreateText(window.transform, "Status", new Vector2(288f, 140f), 16f);
            SetRect(statusText.rectTransform, new Vector2(16f, -12f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            dayDownButton = CreateButton(window.transform, "DayDownButton", "Day -1", new Vector2(140f, 25f));
            dayButton = CreateButton(window.transform, "DayButton", "Day +1", new Vector2(140f, 25f));
            timeButton = CreateButton(window.transform, "TimeButton", string.Empty, new Vector2(288f, 25f));
            weatherButton = CreateButton(window.transform, "WeatherButton", string.Empty, new Vector2(288f, 25f));
            tomorrowButton = CreateButton(window.transform, "TomorrowButton", string.Empty, new Vector2(288f, 25f));
            octoberFirstSpecialDiaryButton = CreateButton(window.transform, "OctoberFirstSpecialDiaryButton", "10/1 特別日記を追加", new Vector2(288f, 25f));
            resetDateButton = CreateButton(window.transform, "ResetDateButton", "日付リセット", new Vector2(140f, 25f));
            resetDiaryButton = CreateButton(window.transform, "ResetDiaryButton", "日記リセット", new Vector2(140f, 25f));
            SetRect(dayDownButton.GetComponent<RectTransform>(), new Vector2(16f, -166f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(dayButton.GetComponent<RectTransform>(), new Vector2(164f, -166f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(timeButton.GetComponent<RectTransform>(), new Vector2(16f, -197f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(weatherButton.GetComponent<RectTransform>(), new Vector2(16f, -228f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(tomorrowButton.GetComponent<RectTransform>(), new Vector2(16f, -259f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(octoberFirstSpecialDiaryButton.GetComponent<RectTransform>(), new Vector2(16f, -290f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(resetDateButton.GetComponent<RectTransform>(), new Vector2(16f, -321f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(resetDiaryButton.GetComponent<RectTransform>(), new Vector2(164f, -321f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            homeLocationTitle = CreateText(window.transform, "HomeLocationTitle", new Vector2(288f, 20f), 14f);
            homeLocationTitle.text = "基本会話拠点";
            SetRect(homeLocationTitle.rectTransform, new Vector2(16f, -360f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            tableHomeButton = CreateButton(window.transform, "TableHomeButton", "ちゃぶ台", new Vector2(140f, 25f));
            deskHomeButton = CreateButton(window.transform, "DeskHomeButton", "机", new Vector2(140f, 25f));
            SetRect(tableHomeButton.GetComponent<RectTransform>(), new Vector2(16f, -385f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(deskHomeButton.GetComponent<RectTransform>(), new Vector2(164f, -385f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            TMP_Text psychologyTitle = CreateText(window.transform, "PsychologyTitle", new Vector2(288f, 20f), 14f);
            psychologyTitle.text = "心理値（StatusManager）";
            SetRect(psychologyTitle.rectTransform, new Vector2(16f, -420f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            for (int i = 0; i < DebugStatusTypes.Length; i++)
            {
                int statusIndex = i;
                float y = -450f - (i * 30f);
                statusDecreaseButtons[i] = CreateButton(window.transform, $"{DebugStatusTypes[i]}Decrease", "-10", new Vector2(42f, 25f));
                statusIncreaseButtons[i] = CreateButton(window.transform, $"{DebugStatusTypes[i]}Increase", "+10", new Vector2(42f, 25f));
                statusValueTexts[i] = CreateText(window.transform, $"{DebugStatusTypes[i]}Value", new Vector2(184f, 25f), 14f);
                statusValueTexts[i].alignment = TextAlignmentOptions.Center;
                SetRect(statusDecreaseButtons[i].GetComponent<RectTransform>(), new Vector2(16f, y), new Vector2(0f, 1f), new Vector2(0f, 1f));
                SetRect(statusValueTexts[i].rectTransform, new Vector2(66f, y), new Vector2(0f, 1f), new Vector2(0f, 1f));
                SetRect(statusIncreaseButtons[i].GetComponent<RectTransform>(), new Vector2(262f, y), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            window.SetActive(false);
        }
#endif
        private void BindActions()
        {
            for (int i = 0; i < DebugStatusTypes.Length; i++)
            {
                int statusIndex = i;
                statusDecreaseButtons[i]?.onClick.AddListener(() => ChangeStatus(DebugStatusTypes[statusIndex], -10));
                statusIncreaseButtons[i]?.onClick.AddListener(() => ChangeStatus(DebugStatusTypes[statusIndex], 10));
            }
            dayDownButton?.onClick.AddListener(DecreaseDay);
            dayButton?.onClick.AddListener(AdvanceDay);
            timeButton?.onClick.AddListener(AdvancePeriod);
            weatherButton?.onClick.AddListener(AdvanceWeather);
            tomorrowButton?.onClick.AddListener(AdvanceTomorrowWeather);
            octoberFirstSpecialDiaryButton?.onClick.AddListener(AddOctoberFirstSpecialDiary);
            resetDateButton?.onClick.AddListener(ResetDate);
            resetDiaryButton?.onClick.AddListener(ResetDiary);
            tableHomeButton?.onClick.AddListener(() => SwitchHomeLocationAsync(CatHomeLocation.Table).Forget());
            deskHomeButton?.onClick.AddListener(() => SwitchHomeLocationAsync(CatHomeLocation.Desk).Forget());
            window.SetActive(false);
        }

        private void ToggleWindow() => NekolposDebugConsole.Instance?.Open();

        private void AdvanceDay()
        {
            if (!AllowMutation()) return;
            if (timeManager == null) return;
            DialogueManager.Instance?.RequestTimeAdvanceAsync(
                new Nekolpos.TimeSystem.TimeAdvanceRequest(int.MaxValue, Nekolpos.TimeSystem.TimeAdvanceReason.Debug)).Forget();
            Refresh();
        }

        private void DecreaseDay()
        {
            if (!AllowMutation()) return;
            if (timeManager == null) return;
            timeManager.SetCurrentTime(Mathf.Max(1, timeManager.CurrentDay - 1), timeManager.CurrentPeriod);
            Refresh();
        }

        private void AdvancePeriod()
        {
            if (!AllowMutation()) return;
            if (timeManager == null) return;
            DialogueManager.Instance?.DebugAdvanceToNextPeriod();
            Refresh();
        }

        private void AdvanceWeather()
        {
            if (!AllowMutation()) return;
            if (timeManager == null) return;
            timeManager.SetWeather(Next(timeManager.Weather));
            Refresh();
        }

        private void AdvanceTomorrowWeather()
        {
            if (!AllowMutation()) return;
            if (timeManager == null) return;
            timeManager.SetTomorrowWeather(Next(timeManager.TomorrowWeather));
            Refresh();
        }

        private void AddOctoberFirstSpecialDiary()
        {
            if (!AllowMutation()) return;
            DiaryCalendarController diaryCalendar = DiaryCalendarController.Instance ??
                FindFirstObjectByType<DiaryCalendarController>(FindObjectsInactive.Include);
            if (diaryCalendar == null)
            {
                Debug.LogWarning("[NekolposDebugPanel] DiaryCalendarController が見つからないため、10/1特別日記を追加できません。");
                return;
            }

            if (diaryCalendar.AddDebugOctoberFirstSpecialDiary())
            {
                Refresh();
            }
        }

        private void ResetDate()
        {
            if (!AllowMutation()) return;
            if (timeManager == null)
            {
                return;
            }

            NekolposGameBootstrap.ResetDebugStartDay();
            timeManager.ResetGameStartDateForDebug();
            Refresh();
        }

        private void ResetDiary()
        {
            if (!AllowMutation()) return;
            DiaryCalendarController diaryCalendar = DiaryCalendarController.Instance ??
                FindFirstObjectByType<DiaryCalendarController>(FindObjectsInactive.Include);
            if (diaryCalendar == null)
            {
                Debug.LogWarning("[NekolposDebugPanel] DiaryCalendarController が見つからないため、日記をリセットできません。");
                return;
            }

            diaryCalendar.ResetAllDiaryDataForDebug();
            Refresh();
        }

        private void Refresh()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            ResolveHomeLocationReferences();
            ResolveStatusManager();
            if (timeManager == null || !timeManager.IsInitialized || statusText == null || window == null)
            {
                return;
            }

            string reason = DebugConsoleGuard.GetBlockReason();
            foreach (var button in window.GetComponentsInChildren<Button>(true)) button.interactable = reason == null;
            string currentState = statusManager != null && statusManager.IsInitialized ? statusManager.GetRepresentativeState().DisplayName : "<StatusManager未接続>";
            statusText.text = $"DEBUG\nDay: {timeManager.CurrentDay}\nTime: {timeManager.CurrentPeriod}\nWeather: {timeManager.Weather}\nTomorrow: {timeManager.TomorrowWeather}\nCurrent State: {currentState}\n{reason ?? "通常待機: 操作可能"}";
            SetButtonText(dayDownButton, "Day -1");
            SetButtonText(dayButton, $"Day +1  (現在: {timeManager.CurrentDay})");
            SetButtonText(timeButton, $"Time を次へ  (現在: {timeManager.CurrentPeriod})");
            SetButtonText(weatherButton, $"Weather を変更  (現在: {timeManager.Weather})");
            SetButtonText(tomorrowButton, $"Tomorrow を変更  (現在: {timeManager.TomorrowWeather})");
            SetButtonText(octoberFirstSpecialDiaryButton, "10/1 特別日記を追加");
            SetButtonText(resetDateButton, "日付リセット");
            SetButtonText(resetDiaryButton, "日記リセット");
            CatHomeLocation currentHomeLocation = presentationMode != null && presentationMode.PositionController != null
                ? presentationMode.PositionController.CurrentHomeLocation
                : CatHomeLocation.Table;
            SetButtonText(tableHomeButton, currentHomeLocation == CatHomeLocation.Table ? "ちゃぶ台 ✓" : "ちゃぶ台");
            SetButtonText(deskHomeButton, currentHomeLocation == CatHomeLocation.Desk ? "机 ✓" : "机");
            for (int i = 0; i < DebugStatusTypes.Length; i++)
            {
                if (statusValueTexts[i] != null)
                {
                    string value = statusManager != null && statusManager.IsInitialized ? statusManager.GetValue(DebugStatusTypes[i]).ToString() : "未接続";
                    statusValueTexts[i].text = $"{GetStatusLabel(DebugStatusTypes[i])}: {value}";
                }
            }
        }

        private void ResolveHomeLocationReferences()
        {
            if (titleBootstrap == null) titleBootstrap = FindFirstObjectByType<NekolposGameBootstrap>(FindObjectsInactive.Include);
            if (presentationMode == null) presentationMode = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
        }

        private UniTask SwitchHomeLocationAsync(CatHomeLocation location)
        {
            if (!AllowMutation()) return UniTask.CompletedTask;
            ResolveHomeLocationReferences();
            if (titleBootstrap == null || !titleBootstrap.TryPlayConversationTimeline(location == CatHomeLocation.Table ? "MoveToTable" : "MoveToDesk"))
                statusText.text = "操作を拒否: 正規の運搬経路が未接続です";
            return UniTask.CompletedTask;
        }

        private void ChangeStatus(StatusType statusType, int delta)
        {
            if (!AllowMutation()) return;
            ResolveStatusManager();
            if (statusManager == null)
            {
                return;
            }

            statusManager.ApplyDelta(statusType, delta, "Debug", "NekolposDebugPanel");
            Refresh();
        }

        private void ResolveStatusManager()
        {
            if (statusManager != null)
            {
                return;
            }

            statusManager = FindFirstObjectByType<StatusManager>(FindObjectsInactive.Include);
            if (statusManager != null)
            {
                return;
            }

        }

        private static string GetStatusLabel(StatusType statusType)
        {
            return statusType switch
            {
                StatusType.Affection => "Affection",
                StatusType.Sadistic => "Sadistic",
                StatusType.Concern => "Concern",
                StatusType.Hostility => "Hostility",
                StatusType.Obedience => "Obedience",
                StatusType.Instinct => "Instinct",
                _ => statusType.ToString()
            };
        }

        private static WeatherType Next(WeatherType value)
        {
            return (WeatherType)(((int)value + 1) % global::System.Enum.GetValues(typeof(WeatherType)).Length);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 size)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.26f, 0.36f, 0.48f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            TMP_Text text = CreateText(buttonObject.transform, "Label", size, 16f);
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;
            return button;
        }

        private static TMP_Text CreateText(Transform parent, string name, Vector2 size, float fontSize)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = size;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
        }

        private static void SetButtonText(Button button, string value)
        {
            if (button != null)
            {
                TMP_Text text = button.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = value;
            }
        }
#else
        public static void EnsureVisibleForDebugStart() { }
        private void Awake() { gameObject.SetActive(false); }
#endif
    }
}
