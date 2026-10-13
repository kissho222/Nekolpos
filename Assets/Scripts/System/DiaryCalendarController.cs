using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nekolpos.TimeSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>Tablet diary calendar. Night-writing flows can add entries through <see cref="AddEntry"/> later.</summary>
    public sealed class DiaryCalendarController : MonoBehaviour
    {
        private enum SlideDirection
        {
            Forward = 1,
            Backward = -1,
        }

        public static DiaryCalendarController Instance { get; private set; }
        [Serializable]
        private sealed class DiaryEntry
        {
            public string date;
            public string text;
            public bool isSpecial;
            public string diaryId;
        }
        [Serializable]
        private sealed class DiaryEntryCollection { public List<DiaryEntry> entries = new List<DiaryEntry>(); }

        private const string EntriesPrefsKey = "Nekolpos.Diary.Entries.v1";
        private const string PawSpriteResourcePath = "Diary/肉球マーク";
        private const string TemporarySpecialDiaryId = "TEMP_SPECIAL_DIARY_001";
        private const string DebugOctoberFirstSpecialDiaryId = "TEMP_SPECIAL_DIARY_OCT01";
        private const string DebugOctoberFirstSpecialDiaryBody = "今日は、ネルコに顔を近づけられた。\n目の前いっぱいに顔があって、息がかかるたびに身体ごと包まれているみたいだった。\nそのまま大きな舌先で頬をぺろっと舐められて、あまりの近さにしばらく動けなかった。\nたぶんネルコには、私がどれだけどきどきしていたか全部ばれていたと思う。";
        private const int DayNumberFontSize = 35;
        private const int DayPreviewFontSize = 16;
        private const int DayPreviewCharacterLimit = 12;
        private readonly Dictionary<string, List<string>> entriesByDate = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> specialEntriesByDate = new Dictionary<string, List<string>>();
        private readonly List<DiaryEntry> entryRecords = new List<DiaryEntry>();
        private TimeManager timeManager;
        private GameObject panel;
        private GameObject calendarView;
        private GameObject detailView;
        private GameObject writingView;
        private GameObject promptParentPanel;
        [Header("Slide Transition")]
        [SerializeField, Min(0.01f)] private float windowTransitionSeconds = 0.22f;
        [SerializeField, Min(0.01f)] private float detailPageTransitionSeconds = 0.16f;
        private DateTime viewedMonth;
        private DateTime detailDate;
        private bool hasDetailDate;
        private bool specialSortMode;
        private bool detailIsSpecialSort;
        private Sprite pawSprite;
        private Button previousMonthButton;
        private Button nextMonthButton;
        private Button todayButton;
        private Button closeButton;
        private Button backButton;
        private Button detailCloseButton;
        private Button beforeDayButton;
        private Button nextDayButton;
        private Button specialSortButton;
        private TMP_Text calendarTitleText;
        private TMP_Text detailTitleText;
        [SerializeField] private Image detailSpecialPaw;
        private TMP_Text detailBodyText;
        private GameObject detailBackImage;
        private TMP_Text writingPromptText;
        private TMP_Text writingDateText;
        private TMP_Text writingBodyText;
        private GameObject microphoneIndicator;
        private Button morningButton;
        private Button afternoonButton;
        private Button eveningButton;
        private Button writingFinishButton;
        private Button reWritingButton;
        private readonly List<Button> dayButtons = new List<Button>();
        private bool entriesLoaded;
        private Coroutine writingCoroutine;
        private Coroutine windowTransitionCoroutine;
        private Coroutine detailPageTransitionCoroutine;
        private GameObject activeWindow;
        private GameObject transitionFromWindow;
        private GameObject transitionToWindow;
        private GameObject incomingDetailBackImage;
        private bool isViewTransitioning;
        private bool isDetailPageTransitioning;
        private readonly List<DiaryJournalStore.DiaryDraft> entriesAwaitingConfirmation = new List<DiaryJournalStore.DiaryDraft>();
        private bool confirmingSpecialEntries;
        private bool isNightDiaryActive;
        public bool IsNightDiaryActive => isNightDiaryActive;
        public bool IsCalendarWindowOpen => activeWindow != null && activeWindow.activeInHierarchy;
        public bool IsWritingOrTransitioning => writingCoroutine != null || isViewTransitioning || isDetailPageTransitioning;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool TryAdvanceIsolatedNightDiary()
        {
            if (!DebugSaveSession.CanRunScenarios || DebugConsoleInput.ConsoleOwnsInput || !isNightDiaryActive || IsWritingOrTransitioning) return false;
            if (writingFinishButton != null && writingFinishButton.gameObject.activeInHierarchy && writingFinishButton.interactable)
            { writingFinishButton.onClick.Invoke(); return true; }
            foreach (var button in new[] { morningButton, afternoonButton, eveningButton })
                if (button != null && button.gameObject.activeInHierarchy && button.interactable) { button.onClick.Invoke(); return true; }
            return false;
        }
#endif
        private bool isDebugNightDiary;
        private int activeNightDiaryDay;
        private int completedNightDiaryDay;
        public bool HasCurrentNightDiary => timeManager != null &&
            ((isNightDiaryActive && activeNightDiaryDay == timeManager.CurrentDay) || completedNightDiaryDay == timeManager.CurrentDay);
        private ScrollRect writingBodyScroll;
        private ScrollRect detailBodyScroll;

        private void Awake()
        {
            Instance = this;
            ResolveUiReferences();
            GameObject icon = FindSceneObject("DiaryIcon");
            if (panel == null || icon == null)
            {
                Debug.LogWarning("[DiaryCalendar] DiaryPanel または DiaryIcon が見つからないため、日記UIを初期化できません。", this);
                return;
            }

            Button button = icon.GetComponent<Button>() ?? icon.AddComponent<Button>();
            button.targetGraphic = icon.GetComponent<Graphic>();
            button.onClick.AddListener(OpenCalendar);
            LoadEntries();
            ResolveCalendarReferences();
            BindCalendarControls();
            panel.SetActive(false);
        }

        private void OnDestroy()
        {
            ResetRuntimeStateForTitleReload();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OpenCalendar()
        {
            OpenCalendar(playIconTransition: true);
        }

        private void OpenCalendar(bool playIconTransition)
        {
            ResolveUiReferences();
            if (!TryResolveTimeManager() || panel == null)
            {
                Debug.LogWarning("[DiaryCalendar] TimeManager が準備されていないため、日記を開けません。", this);
                return;
            }

            DateTime currentDate = timeManager.GetCalendarDate(timeManager.CurrentDay);
            viewedMonth = new DateTime(currentDate.Year, currentDate.Month, 1);
            ResolveCalendarReferences();
            BindCalendarControls();
            StopWritingRoutine();
            if (promptParentPanel != null)
            {
                promptParentPanel.SetActive(false);
            }

            HideApplicationWindows();
            BuildCalendar(showWindow: false);
            if (playIconTransition)
            {
                calendarView.SetActive(true);
                SetWindowVisual(calendarView, 1f, 1f);
                activeWindow = calendarView;
                if (TabletHomeApplicationController.Instance == null ||
                    !TabletHomeApplicationController.Instance.OpenApplication(panel, playIconTransition: true))
                {
                    panel.SetActive(true);
                }
            }
            else
            {
                if (TabletHomeApplicationController.Instance != null)
                {
                    TabletHomeApplicationController.Instance.OpenApplication(panel, playIconTransition: false);
                }
                else
                {
                    panel.SetActive(true);
                }

                SetView(calendarView);
            }
        }

        public void AddEntry(string text)
        {
            AddEntryForDate(timeManager != null ? timeManager.GetCalendarDate(timeManager.CurrentDay) : DateTime.Today, text);
        }

        public void AddEntryForDate(DateTime date, string text)
        {
            AddEntryForDate(date, text, isSpecial: false, diaryId: string.Empty, refreshCalendar: true);
        }

        /// <summary>Adds a completed special diary entry and permanently unlocks the special-diary browser.</summary>
        public void AddSpecialEntryForDate(DateTime date, string text, string diaryId = "")
        {
            AddEntryForDate(date, text, isSpecial: true, diaryId: diaryId, refreshCalendar: true);
        }

        /// <summary>Clears saved diary entries and unfinished drafts through the Debug Panel only.</summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void ResetAllDiaryDataForDebug()
        {
            if (!DebugConsoleGuard.CanMutate(out string reason))
            {
                Debug.LogWarning("[DiaryCalendar] 日記リセットを拒否: " + reason, this);
                return;
            }

            StopWritingRoutine();
            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;
            isNightDiaryActive = false;
            isDebugNightDiary = false;
            activeNightDiaryDay = 0;
            entriesByDate.Clear();
            specialEntriesByDate.Clear();
            entryRecords.Clear();
            entriesLoaded = true;
            specialSortMode = false;
            detailIsSpecialSort = false;
            hasDetailDate = false;
            DiaryJournalStore.ResetAllForDebug();
            ProgressSaveStore.DeleteKey(EntriesPrefsKey);
            ProgressSaveStore.Save();
            if (panel != null && panel.activeInHierarchy)
            {
                BuildCalendar();
            }
        }
#endif

        [ContextMenu("Add Temporary Special Diary")]
        private void AddTemporarySpecialDiary()
        {
            DateTime date = TryResolveTimeManager()
                ? timeManager.GetCalendarDate(timeManager.CurrentDay)
                : DateTime.Today;
            string text = SystemTimedEventCatalog.Get(TemporarySpecialDiaryId, string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning($"[DiaryCalendar] 仮特別日記 '{TemporarySpecialDiaryId}' の本文が見つかりません。", this);
                return;
            }

            AddSpecialEntryForDate(date, text, TemporarySpecialDiaryId);
        }

        /// <summary>
        /// Debug-only test setup for crossing from September into October. The target is derived from the
        /// saved game-start date, never from the PC's current clock.
        /// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool AddDebugOctoberFirstSpecialDiary()
        {
            if (!TryResolveTimeManager())
            {
                Debug.LogWarning("[DiaryCalendar] TimeManager が見つからないため、10/1特別日記を追加できません。", this);
                return false;
            }

            DateTime targetDate = GetDebugOctoberFirstDate(timeManager.GameStartDate);
            int targetDay = CalculateDebugOctoberFirstDayNumber(timeManager.GameStartDate);
            timeManager.SetCurrentTime(targetDay, timeManager.CurrentPeriod);
            AddSpecialEntryForDate(targetDate, DebugOctoberFirstSpecialDiaryBody, DebugOctoberFirstSpecialDiaryId);
            Debug.Log($"[DiaryCalendar] デバッグ用特別日記を {targetDate:M/d}（Day {targetDay}）へ追加しました。", this);
            return true;
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ContextMenu("Add Debug October 1 Special Diary")]
        private void AddDebugOctoberFirstSpecialDiaryFromInspector()
        {
            AddDebugOctoberFirstSpecialDiary();
        }
#endif

        private static int CalculateDebugOctoberFirstDayNumber(DateTime gameStartDate)
        {
            return (GetDebugOctoberFirstDate(gameStartDate) - gameStartDate.Date).Days + 1;
        }

        private static DateTime GetDebugOctoberFirstDate(DateTime gameStartDate)
        {
            DateTime startDate = gameStartDate.Date;
            DateTime targetDate = new DateTime(startDate.Year, 10, 1);
            return targetDate < startDate ? targetDate.AddYears(1) : targetDate;
        }

        private void AddEntryForDate(DateTime date, string text, bool isSpecial, string diaryId, bool refreshCalendar)
        {
            text = ResolveDiaryBody(text);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string key = Key(date);
            if (!entriesByDate.TryGetValue(key, out List<string> values))
            {
                entriesByDate[key] = values = new List<string>();
            }
            values.Add(text.Trim());
            entryRecords.Add(new DiaryEntry
            {
                date = key,
                text = text.Trim(),
                isSpecial = isSpecial,
                diaryId = diaryId ?? string.Empty,
            });
            if (isSpecial)
            {
                if (!specialEntriesByDate.TryGetValue(key, out List<string> specialValues))
                {
                    specialEntriesByDate[key] = specialValues = new List<string>();
                }

                specialValues.Add(text.Trim());
                DiaryJournalStore.UnlockSpecialDiarySort();
            }

            SaveEntries();
            if (refreshCalendar && panel != null && panel.activeInHierarchy)
            {
                BuildCalendar();
            }
        }

        /// <summary>Night-only entry point. DiaryIcon always calls <see cref="OpenCalendar"/> instead.</summary>
        public void BeginNightDiary()
        {
            ResolveUiReferences();
            ResolveCalendarReferences();
            if (!TryResolveTimeManager() || panel == null || writingView == null)
            {
                Debug.LogWarning("[DiaryCalendar] 夜日記用UIまたはTimeManagerが見つかりません。", this);
                return;
            }

            if (completedNightDiaryDay == timeManager.CurrentDay)
            {
                OpenCalendar();
                return;
            }

            panel.SetActive(true);
            StopWritingRoutine();
            NekolposGameBootstrap bootstrap = FindFirstObjectByType<NekolposGameBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.SetHomeLocation(CatHomeLocation.Desk);
                bootstrap.EnterTabletCameraView();
            }

            bool keepWritingBackground = isNightDiaryActive && activeWindow == writingView;
            HideApplicationWindows();
            DateTime currentDate = timeManager.GetCalendarDate(timeManager.CurrentDay);
            isNightDiaryActive = true;
            DebugConsoleTrace.Diary(true);
            isDebugNightDiary = NekolposGameBootstrap.IsDebugStart;
            activeNightDiaryDay = timeManager.CurrentDay;
            viewedMonth = new DateTime(currentDate.Year, currentDate.Month, 1);
            BuildCalendar(showWindow: false);
            ShowWindowImmediately(keepWritingBackground ? writingView : calendarView);
            ResetWritingPresentation();
            UpdateWritingDate(activeNightDiaryDay);
            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;

            if (DiaryJournalStore.HasPendingSpecialEntries)
            {
                promptParentPanel.SetActive(false);
                writingPromptText.text = string.Empty;
                confirmingSpecialEntries = true;
                writingCoroutine = StartCoroutine(OpenPendingWritingAfterCalendarRoutine(DiaryJournalStore.GetPendingSpecialEntries()));
                return;
            }

            promptParentPanel.SetActive(true);
            SetPeriodButtonsVisible(true);
            writingPromptText.text = "いつのことを書こうかな？";
            morningButton.interactable = DiaryJournalStore.HasNormalDraft(timeManager.CurrentDay, DayPeriod.Morning);
            afternoonButton.interactable = DiaryJournalStore.HasNormalDraft(timeManager.CurrentDay, DayPeriod.Afternoon);
            eveningButton.interactable = DiaryJournalStore.HasNormalDraft(timeManager.CurrentDay, DayPeriod.Evening);
            if (!morningButton.interactable && !afternoonButton.interactable && !eveningButton.interactable)
            {
                // No authored normal text exists for this day. Allow confirmation without fabricating diary prose.
                promptParentPanel.SetActive(false);
                OpenWritingWindow();
                writingCoroutine = StartCoroutine(WriteEntriesRoutine(Array.Empty<DiaryJournalStore.DiaryDraft>()));
            }
        }

        private void BuildCalendar(bool showWindow = true, SlideDirection direction = SlideDirection.Forward)
        {
            if (panel == null || !TryResolveTimeManager() || calendarView == null || dayButtons.Count != 42) return;
            if (showWindow)
            {
                SetView(calendarView, direction);
            }

            RefreshSpecialSortButton();
            if (specialSortMode)
            {
                BuildSpecialCalendar();
                return;
            }

            if (calendarTitleText != null) calendarTitleText.text = $"{viewedMonth:yyyy年M月}";
            DateTime first = viewedMonth;
            int days = DateTime.DaysInMonth(first.Year, first.Month);
            int firstColumn = (int)first.DayOfWeek;
            for (int index = 0; index < dayButtons.Count; index++)
            {
                Button button = dayButtons[index];
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                int day = index - firstColumn + 1;
                bool isCurrentMonthDay = day >= 1 && day <= days;
                button.interactable = isCurrentMonthDay;
                if (label == null) continue;
                ConfigureDayLabel(label);
                if (!isCurrentMonthDay)
                {
                    label.text = string.Empty;
                    button.onClick.RemoveAllListeners();
                    SetDayCellPaw(button, visible: false);
                    continue;
                }

                DateTime date = first.AddDays(day - 1);
                string key = Key(date);
                string preview = entriesByDate.TryGetValue(key, out List<string> notes) && notes.Count > 0
                    ? ResolveDiaryBody(notes[0])
                    : string.Empty;
                label.text = FormatDayCellText(day, preview);
                SetDayCellPaw(button, specialEntriesByDate.ContainsKey(key));
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ShowEntries(date, specialOnly: false));
            }
            RefreshMonthButtons();
        }

        private void BuildSpecialCalendar()
        {
            if (calendarTitleText != null)
            {
                calendarTitleText.text = "特別な日記";
            }

            var grouped = BuildSpecialEntryGroups(out var repeatCounts);
            List<DateTime> dates = grouped.Keys.OrderBy(date => date).ToList();
            for (int index = 0; index < dayButtons.Count; index++)
            {
                Button button = dayButtons[index];
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                bool hasEntry = index < dates.Count;
                button.interactable = hasEntry;
                button.onClick.RemoveAllListeners();
                SetDayCellPaw(button, visible: hasEntry);
                if (label == null)
                {
                    continue;
                }

                ConfigureDayLabel(label);
                if (!hasEntry)
                {
                    label.text = string.Empty;
                    continue;
                }

                DateTime date = dates[index];
                string preview = grouped.TryGetValue(date, out List<string> notes) && notes.Count > 0
                    ? ResolveDiaryBody(notes[0]) : string.Empty;
                label.text = FormatSpecialCellText(FormatSpecialDateLabel(date, repeatCounts[date]), preview);
                button.onClick.AddListener(() => ShowEntries(date, specialOnly: true));
            }

            if (previousMonthButton != null) previousMonthButton.interactable = false;
            if (nextMonthButton != null) nextMonthButton.interactable = false;
        }

        private void ShowEntries(DateTime date, bool specialOnly)
        {
            if (!TryGetEntries(date, specialOnly, out List<string> notes)) return;

            ApplyDetailEntry(date, notes, specialOnly);
            SetView(detailView, SlideDirection.Forward);
        }

        private void MoveDetailDay(int direction)
        {
            if (!hasDetailDate)
            {
                return;
            }

            DateTime? adjacentDate = detailIsSpecialSort
                ? FindAdjacentSpecialEntryDate(detailDate, direction)
                : FindAdjacentEntryDate(detailDate, direction);
            if (adjacentDate.HasValue && TryGetEntries(adjacentDate.Value, detailIsSpecialSort, out List<string> notes))
            {
                StartDetailPageTransition(adjacentDate.Value, notes, direction > 0 ? SlideDirection.Forward : SlideDirection.Backward);
            }
        }

        private bool TryGetEntries(DateTime date, bool specialOnly, out List<string> notes)
        {
            if (specialOnly)
            {
                return BuildSpecialEntryGroups(out _).TryGetValue(date.Date, out notes) && notes.Count > 0;
            }
            return entriesByDate.TryGetValue(Key(date), out notes) && notes.Count > 0;
        }

        private void ApplyDetailEntry(DateTime date, List<string> notes, bool isSpecialSort)
        {
            detailDate = date.Date;
            hasDetailDate = true;
            detailIsSpecialSort = isSpecialSort;
            viewedMonth = new DateTime(detailDate.Year, detailDate.Month, 1);
            if (detailTitleText != null) detailTitleText.text = FormatDetailTitle(detailDate);
            if (isSpecialSort && detailTitleText != null)
            {
                BuildSpecialEntryGroups(out var repeatCounts);
                repeatCounts.TryGetValue(detailDate, out int repeats);
                detailTitleText.text = FormatSpecialDateLabel(detailDate, repeats) + "の日記";
            }
            if (detailBodyText != null) detailBodyText.text = string.Join("\n\n", notes.Select(ResolveDiaryBody));
            detailBodyScroll = EnsureBodyScroll(detailBodyText);
            ResetBodyScroll(detailBodyScroll, atBottom: false);
            SetDetailPaw(specialEntriesByDate.ContainsKey(Key(detailDate)));
            RefreshDetailDayButtons();
        }

        private DateTime? FindAdjacentEntryDate(DateTime currentDate, int direction)
        {
            if (direction == 0 || !TryResolveTimeManager())
            {
                return null;
            }

            DateTime minimum = timeManager.GameStartDate.Date;
            DateTime maximum = timeManager.GetCalendarDate(timeManager.CurrentDay).Date;
            int step = direction > 0 ? 1 : -1;
            DateTime candidate = currentDate.Date;
            while (true)
            {
                if ((step < 0 && candidate <= minimum) || (step > 0 && candidate >= maximum))
                {
                    return null;
                }

                candidate = candidate.AddDays(step);
                if (entriesByDate.TryGetValue(Key(candidate), out List<string> notes) && notes.Count > 0)
                {
                    return candidate;
                }
            }
        }

        private DateTime? FindAdjacentSpecialEntryDate(DateTime currentDate, int direction)
        {
            if (direction == 0)
            {
                return null;
            }

            List<DateTime> dates = GetSpecialEntryDates();
            int index = dates.IndexOf(currentDate.Date);
            if (index < 0)
            {
                return null;
            }

            int adjacentIndex = index + (direction > 0 ? 1 : -1);
            return adjacentIndex >= 0 && adjacentIndex < dates.Count ? dates[adjacentIndex] : (DateTime?)null;
        }

        private List<DateTime> GetSpecialEntryDates()
        {
            return BuildSpecialEntryGroups(out _).Keys.OrderBy(date => date).ToList();
        }

        private Dictionary<DateTime, List<string>> BuildSpecialEntryGroups(out Dictionary<DateTime, int> repeatCounts)
        {
            var grouped = new Dictionary<DateTime, List<string>>();
            repeatCounts = new Dictionary<DateTime, int>();
            var firstDates = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var pair in specialEntriesByDate.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!DateTime.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime date)) continue;
                foreach (string body in pair.Value)
                {
                    if (firstDates.TryGetValue(body, out DateTime firstDate))
                    {
                        repeatCounts[firstDate]++;
                        continue;
                    }
                    firstDates.Add(body, date);
                    if (!grouped.TryGetValue(date, out var notes))
                    {
                        grouped[date] = notes = new List<string>();
                        repeatCounts[date] = 0;
                    }
                    notes.Add(body);
                }
            }
            return grouped;
        }

        private static string FormatSpecialDateLabel(DateTime date, int repeats) =>
            $"{date.Month}/{date.Day}" + (repeats > 0 ? $" 他{repeats}件" : string.Empty);

        private void RefreshDetailDayButtons()
        {
            if (!hasDetailDate)
            {
                return;
            }

            if (beforeDayButton != null) beforeDayButton.interactable = (detailIsSpecialSort ? FindAdjacentSpecialEntryDate(detailDate, -1) : FindAdjacentEntryDate(detailDate, -1)).HasValue;
            if (nextDayButton != null) nextDayButton.interactable = (detailIsSpecialSort ? FindAdjacentSpecialEntryDate(detailDate, 1) : FindAdjacentEntryDate(detailDate, 1)).HasValue;
        }

        private void MoveMonth(int delta)
        {
            if (specialSortMode)
            {
                return;
            }
            try { viewedMonth = viewedMonth.AddMonths(delta); } catch (ArgumentOutOfRangeException) { return; }
            DateTime min = new DateTime(timeManager.GameStartDate.Year, timeManager.GameStartDate.Month, 1);
            DateTime max = FirstOfCurrentMonth();
            if (viewedMonth < min) viewedMonth = min;
            if (viewedMonth > max) viewedMonth = max;
            BuildCalendar();
        }

        private void RefreshMonthButtons()
        {
            DateTime minimum = new DateTime(timeManager.GameStartDate.Year, timeManager.GameStartDate.Month, 1);
            DateTime maximum = FirstOfCurrentMonth();
            if (previousMonthButton != null) previousMonthButton.interactable = viewedMonth > minimum;
            if (nextMonthButton != null) nextMonthButton.interactable = viewedMonth < maximum;
        }
        private DateTime FirstOfCurrentMonth() { DateTime today = timeManager.GetCalendarDate(timeManager.CurrentDay); return new DateTime(today.Year, today.Month, 1); }
        private static string Key(DateTime date) => date.ToString("yyyy-MM-dd");
        private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max) + "…";

        private static void ConfigureDayLabel(TMP_Text label)
        {
            label.enableAutoSizing = false;
            label.fontSize = DayPreviewFontSize;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.margin = new Vector4(8f, 8f, 8f, 8f);
        }

        private static string FormatDayCellText(int day, string preview)
        {
            string dayText = $"<size={DayNumberFontSize}>{day}</size>";
            if (string.IsNullOrWhiteSpace(preview))
            {
                return dayText;
            }

            return $"{dayText}\n<size={DayPreviewFontSize}>{Truncate(preview.Trim(), DayPreviewCharacterLimit)}</size>";
        }

        private static string FormatSpecialDayCellText(DateTime date, string preview)
            => FormatSpecialCellText(FormatSpecialDateLabel(date, 0), preview);

        private static string FormatSpecialCellText(string dateLabel, string preview)
        {
            string dateText = $"<size={DayNumberFontSize}>{dateLabel}</size>";
            return string.IsNullOrWhiteSpace(preview) ? dateText
                : $"{dateText}\n<size={DayPreviewFontSize}>{Truncate(preview.Trim(), DayPreviewCharacterLimit)}</size>";
        }

        private static string FormatDetailTitle(DateTime date) => $"{date.Month}/{date.Day}の日記";

        private void LoadEntries()
        {
            if (entriesLoaded)
            {
                return;
            }

            DiaryEntryCollection collection = JsonUtility.FromJson<DiaryEntryCollection>(ProgressSaveStore.GetString(EntriesPrefsKey, string.Empty)) ?? new DiaryEntryCollection();
            foreach (DiaryEntry entry in collection.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.date) || string.IsNullOrWhiteSpace(entry.text))
                {
                    continue;
                }

                if (!entriesByDate.TryGetValue(entry.date, out List<string> values))
                {
                    entriesByDate[entry.date] = values = new List<string>();
                }

                values.Add(entry.text);
                entryRecords.Add(new DiaryEntry
                {
                    date = entry.date,
                    text = entry.text,
                    isSpecial = entry.isSpecial,
                    diaryId = entry.diaryId ?? string.Empty,
                });
                if (entry.isSpecial)
                {
                    if (!specialEntriesByDate.TryGetValue(entry.date, out List<string> specialValues))
                    {
                        specialEntriesByDate[entry.date] = specialValues = new List<string>();
                    }

                    specialValues.Add(entry.text);
                    DiaryJournalStore.UnlockSpecialDiarySort();
                }
            }
            entriesLoaded = true;
        }

        private void ResolveCalendarReferences()
        {
            if (panel == null)
            {
                return;
            }

            calendarView ??= FindChild("CalendarView");
            detailView ??= FindChild("DetailView");
            writingView ??= FindChild("WritingView");
            promptParentPanel ??= FindChild("PromptParentPanel");
            calendarTitleText ??= FindChildComponent<TMP_Text>("CalendarTitle");
            detailTitleText ??= FindChildComponent<TMP_Text>("DetailTitle");
            detailBodyText ??= FindChildComponent<TMP_Text>("DetailBody");
            detailBackImage ??= FindChild("DetailBackImage");
            writingPromptText ??= FindChildComponent<TMP_Text>("WritingPrompt");
            writingDateText ??= FindChildComponent<TMP_Text>("WritingDate");
            writingBodyText ??= FindChildComponent<TMP_Text>("WritingBody");
            microphoneIndicator ??= FindChild("MicrophoneIndicator");
            previousMonthButton ??= FindChildComponent<Button>("PreviousButton");
            nextMonthButton ??= FindChildComponent<Button>("NextButton");
            todayButton ??= FindChildComponent<Button>("TodayButton");
            closeButton ??= FindChildComponent<Button>("CloseButton");
            backButton ??= FindChildComponent<Button>("BackButton");
            detailCloseButton ??= FindChildComponent<Button>("DetailCloseButton");
            beforeDayButton ??= FindChildComponent<Button>("BeforeDayButton");
            nextDayButton ??= FindChildComponent<Button>("NextDayButton");
            specialSortButton ??= EnsureSpecialSortButton();
            morningButton ??= FindChildComponent<Button>("MorningButton");
            afternoonButton ??= FindChildComponent<Button>("AfternoonButton");
            eveningButton ??= FindChildComponent<Button>("EveningButton");
            writingFinishButton ??= FindChildComponent<Button>("WritingFinishButton");
            reWritingButton ??= FindChildComponent<Button>("ReWritingButton");

            writingBodyScroll = EnsureBodyScroll(writingBodyText);
            detailBodyScroll = EnsureBodyScroll(detailBodyText);
            // Notes outside WritingView (including the beta notice) must not cover the action buttons.
            foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
                if (label.GetComponentInParent<Button>(true) == null && label.GetComponentInParent<TMP_InputField>(true) == null)
                    label.raycastTarget = false;
            if (writingView != null)
            {
                // Decorative graphics must not intercept the rewrite/finish buttons or scroll gestures.
                foreach (Graphic graphic in writingView.GetComponentsInChildren<Graphic>(true))
                    if (graphic.GetComponentInParent<Button>(true) == null && graphic.GetComponent<ScrollRect>() == null)
                        graphic.raycastTarget = false;
            }

            if (dayButtons.Count > 0)
            {
                return;
            }

            for (int index = 1; index <= 42; index++)
            {
                Button button = FindChildComponent<Button>($"Day{index:00}");
                if (button != null)
                {
                    dayButtons.Add(button);
                }
            }
        }

        private Button EnsureSpecialSortButton()
        {
            GameObject existing = FindChild("SpecialDiarySortButton");
            if (existing != null)
            {
                return existing.GetComponent<Button>();
            }

            if (calendarView == null)
            {
                return null;
            }

            GameObject buttonObject = new GameObject(
                "SpecialDiarySortButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(calendarView.transform, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = Vector2.up;
            buttonRect.anchorMax = Vector2.up;
            buttonRect.pivot = Vector2.up;
            buttonRect.anchoredPosition = new Vector2(-40f, -82f);
            buttonRect.sizeDelta = new Vector2(180f, 48f);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.24f, 0.18f, 0.28f, 0.92f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;

            GameObject pawObject = new GameObject("PawIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pawObject.transform.SetParent(buttonObject.transform, false);
            RectTransform pawRect = pawObject.GetComponent<RectTransform>();
            pawRect.anchorMin = new Vector2(0f, 0.5f);
            pawRect.anchorMax = new Vector2(0f, 0.5f);
            pawRect.pivot = new Vector2(0f, 0.5f);
            pawRect.anchoredPosition = new Vector2(12f, 0f);
            pawRect.sizeDelta = new Vector2(30f, 30f);
            Image pawImage = pawObject.GetComponent<Image>();
            pawImage.sprite = ResolvePawSprite();
            pawImage.preserveAspect = true;
            pawImage.raycastTarget = false;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(42f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "特別な日記";
            label.font = calendarTitleText != null ? calendarTitleText.font : TMP_Settings.defaultFontAsset;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return button;
        }

        private void ToggleSpecialSort()
        {
            if (!DiaryJournalStore.IsSpecialDiarySortUnlocked)
            {
                return;
            }

            specialSortMode = !specialSortMode;
            BuildCalendar();
        }

        private void RefreshSpecialSortButton()
        {
            if (specialSortButton == null)
            {
                return;
            }

            bool unlocked = DiaryJournalStore.IsSpecialDiarySortUnlocked;
            specialSortButton.gameObject.SetActive(unlocked);
            if (unlocked && specialSortButton.TryGetComponent(out Image background))
            {
                background.color = specialSortMode
                    ? new Color(0.62f, 0.35f, 0.6f, 0.96f)
                    : new Color(0.24f, 0.18f, 0.28f, 0.92f);
            }
        }

        private void SetDayCellPaw(Button dayButton, bool visible)
        {
            if (dayButton == null)
            {
                return;
            }

            Transform existing = dayButton.transform.Find("SpecialDiaryPaw");
            if (existing == null && visible)
            {
                GameObject pawObject = new GameObject("SpecialDiaryPaw", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                pawObject.transform.SetParent(dayButton.transform, false);
                RectTransform pawRect = pawObject.GetComponent<RectTransform>();
                pawRect.anchorMin = Vector2.one;
                pawRect.anchorMax = Vector2.one;
                pawRect.pivot = Vector2.one;
                pawRect.anchoredPosition = new Vector2(-6f, -6f);
                pawRect.sizeDelta = new Vector2(26f, 26f);
                Image pawImage = pawObject.GetComponent<Image>();
                pawImage.sprite = ResolvePawSprite();
                pawImage.preserveAspect = true;
                pawImage.raycastTarget = false;
                existing = pawObject.transform;
            }

            if (existing != null)
            {
                existing.gameObject.SetActive(visible);
            }
        }

        private Sprite ResolvePawSprite()
        {
            if (pawSprite == null)
            {
                pawSprite = Resources.Load<Sprite>(PawSpriteResourcePath);
            }

            return pawSprite;
        }

        private void SetDetailPaw(bool visible)
        {
            if (detailSpecialPaw == null && detailView != null)
                detailSpecialPaw = detailView.transform.Find("SpecialDiaryPaw")?.GetComponent<Image>();
            if (detailSpecialPaw != null) detailSpecialPaw.gameObject.SetActive(visible);
        }

        private static ScrollRect EnsureBodyScroll(TMP_Text body)
        {
            if (body == null) return null;
            if (body.transform.parent.TryGetComponent<ScrollRect>(out var existing)) return existing;
            RectTransform textRect = body.rectTransform;
            var viewportObject = new GameObject("DiaryBodyViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(textRect.parent, false);
            viewport.SetSiblingIndex(textRect.GetSiblingIndex());
            viewport.anchorMin = textRect.anchorMin;
            viewport.anchorMax = textRect.anchorMax;
            viewport.pivot = textRect.pivot;
            viewport.sizeDelta = textRect.sizeDelta;
            viewport.anchoredPosition = textRect.anchoredPosition;
            var image = viewportObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            textRect.SetParent(viewport, false);
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Overflow;
            body.raycastTarget = false;
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = textRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 50f;
            return scroll;
        }

        private static void ResetBodyScroll(ScrollRect scroll, bool atBottom)
        {
            if (scroll == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = atBottom ? 0f : 1f;
        }

        private void BindCalendarControls()
        {
            BindButton(previousMonthButton, () => MoveMonth(-1));
            BindButton(nextMonthButton, () => MoveMonth(1));
            BindButton(todayButton, () =>
            {
                specialSortMode = false;
                viewedMonth = FirstOfCurrentMonth();
                BuildCalendar();
            });
            BindButton(closeButton, Close);
            BindButton(backButton, () => BuildCalendar(direction: SlideDirection.Backward));
            BindButton(detailCloseButton, Close);
            BindButton(beforeDayButton, () => MoveDetailDay(-1));
            BindButton(nextDayButton, () => MoveDetailDay(1));
            BindButton(specialSortButton, ToggleSpecialSort);
            BindButton(morningButton, () => BeginNormalEntry(DayPeriod.Morning));
            BindButton(afternoonButton, () => BeginNormalEntry(DayPeriod.Afternoon));
            BindButton(eveningButton, () => BeginNormalEntry(DayPeriod.Evening));
            BindButton(writingFinishButton, ConfirmWritingAndOpenCalendar);
            BindButton(reWritingButton, RestartNightDiary);
        }

        private void BeginNormalEntry(DayPeriod period)
        {
            if (!TryResolveTimeManager())
            {
                return;
            }

            DiaryJournalStore.DiaryDraft draft = DiaryJournalStore.GetLatestNormalDraft(timeManager.CurrentDay, period);
            if (draft == null)
            {
                return;
            }

            promptParentPanel.SetActive(false);
            writingPromptText.text = string.Empty;
            OpenWritingWindow();
            writingCoroutine = StartCoroutine(WriteEntriesRoutine(new[] { draft }));
        }

        private IEnumerator WritePendingSpecialEntriesRoutine(IReadOnlyList<DiaryJournalStore.DiaryDraft> entries)
        {
            // One writing session covers one date; later dates stay in the persistent queue.
            var ordered = entries == null ? Array.Empty<DiaryJournalStore.DiaryDraft>() : entries
                .OrderBy(entry => entry.occurredDay)
                .ThenBy(entry => entry.period)
                .ThenBy(entry => entry.sequence)
                .ToArray();
            yield return WriteEntriesRoutine(ordered.Length > 0
                ? ordered.TakeWhile(entry => entry.occurredDay == ordered[0].occurredDay).ToArray()
                : ordered);
        }

        private IEnumerator OpenPendingWritingAfterCalendarRoutine(IReadOnlyList<DiaryJournalStore.DiaryDraft> entries)
        {
            // Keep CalendarView as the source page for at least one rendered frame.
            yield return null;
            OpenWritingWindow();
            yield return WritePendingSpecialEntriesRoutine(entries);
        }

        private IEnumerator WriteEntriesRoutine(IReadOnlyList<DiaryJournalStore.DiaryDraft> entries)
        {
            UpdateWritingDate(entries != null && entries.Count > 0 ? entries[0].occurredDay : activeNightDiaryDay);
            if (entries == null || entries.Count == 0)
            {
                ShowWritingFinish();
                yield break;
            }

            entriesAwaitingConfirmation.Clear();
            entriesAwaitingConfirmation.AddRange(entries);
            microphoneIndicator.SetActive(true);
            writingBodyText.text = string.Empty;
            for (int index = 0; index < entries.Count; index++)
            {
                DiaryJournalStore.DiaryDraft entry = entries[index];
                entry.body = ResolveDiaryBody(entry.body);
                string prefix = index > 0 && entries[index - 1].occurredDay == entry.occurredDay
                    ? "それと……\n" : string.Empty;
                writingBodyText.text += index > 0 ? "\n\n" + prefix : prefix;
                float characterDelay = Mathf.Clamp(2f / Mathf.Max(1, entry.body.Length), 0.005f, 0.035f);
                foreach (char character in entry.body)
                {
                    writingBodyText.text += character;
                    ResetBodyScroll(writingBodyScroll, atBottom: true);
                    yield return new WaitForSecondsRealtime(characterDelay);
                }
            }

            microphoneIndicator.SetActive(false);
            ShowWritingFinish();
            writingCoroutine = null;
        }

        private void UpdateWritingDate(int day)
        {
            if (writingDateText != null && TryResolveTimeManager())
            {
                writingDateText.text = FormatDetailTitle(timeManager.GetCalendarDate(day));
            }
        }

        private void ShowWritingFinish()
        {
            writingPromptText.text = string.Empty;
            writingFinishButton.gameObject.SetActive(true);
            reWritingButton.gameObject.SetActive(!confirmingSpecialEntries);
            writingFinishButton.transform.SetAsLastSibling();
            reWritingButton.transform.SetAsLastSibling();
            EnsureWritingButtonHitArea(writingFinishButton);
            EnsureWritingButtonHitArea(reWritingButton);
        }

        private static void EnsureWritingButtonHitArea(Button button)
        {
            if (button == null) return;
            var existing = button.transform.Find("DiaryButtonHitArea");
            if (existing != null)
            {
                // Reference refreshes can run while the button is hidden; always
                // restore its input surface when presenting the finish actions.
                var existingImage = existing.GetComponent<Image>();
                if (existingImage != null) existingImage.raycastTarget = true;
                return;
            }
            // The entire button must receive input even when its artwork is hidden or only the text has a raycast.
            var host = new GameObject("DiaryButtonHitArea", typeof(RectTransform), typeof(Image));
            host.transform.SetParent(button.transform, false);
            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var hitImage = host.GetComponent<Image>();
            hitImage.color = Color.clear;
            hitImage.raycastTarget = true;
            hitImage.canvasRenderer.cullTransparentMesh = false;
        }

        private void SetPeriodButtonsVisible(bool visible)
        {
            morningButton.gameObject.SetActive(visible);
            afternoonButton.gameObject.SetActive(visible);
            eveningButton.gameObject.SetActive(visible);
        }

        private void SetView(GameObject target, SlideDirection direction = SlideDirection.Forward)
        {
            if (target == null)
            {
                return;
            }

            StopDetailPageTransition();
            StopWindowTransition();
            if (activeWindow == target)
            {
                NormalizeWindow(target, visible: true);
                return;
            }

            windowTransitionCoroutine = StartCoroutine(SlideWindowRoutine(target, direction));
        }

        private void ShowWindowImmediately(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            StopDetailPageTransition();
            StopWindowTransition();
            NormalizeWindow(target, visible: true);
            activeWindow = target;
        }

        private void OpenWritingWindow()
        {
            SetView(writingView, SlideDirection.Forward);
        }

        private void HideApplicationWindows()
        {
            StopDetailPageTransition();
            StopWindowTransition();
            foreach (GameObject window in new[] { calendarView, detailView, writingView })
            {
                if (window == null)
                {
                    continue;
                }

                NormalizeWindow(window, visible: false);
            }

            activeWindow = null;
        }

        private void RestartNightDiary()
        {
            if (confirmingSpecialEntries) return;
            StopWritingRoutine();
            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;
            ResetWritingPresentation();
            BeginNightDiary();
        }

        private void ConfirmWritingAndOpenCalendar()
        {
            StopWritingRoutine();
            bool wasSpecial = confirmingSpecialEntries;
            bool wroteNormalDiary = isNightDiaryActive && !wasSpecial;
            foreach (DiaryJournalStore.DiaryDraft entry in entriesAwaitingConfirmation)
            {
                if (!TryResolveTimeManager())
                {
                    break;
                }

                AddEntryForDate(
                    timeManager.GetCalendarDate(entry.occurredDay),
                    ResolveDiaryBody(entry.body),
                    isSpecial: confirmingSpecialEntries,
                    diaryId: entry.diaryId,
                    refreshCalendar: false);
                if (confirmingSpecialEntries)
                {
                    DiaryJournalStore.RemovePendingSpecialEntries(new[] { entry });
                }
                else
                {
                    DiaryJournalStore.RemoveNormalDraft(entry);
                    wroteNormalDiary = true;
                }
            }

            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;
            if (wasSpecial && isNightDiaryActive)
            {
                BeginNightDiary();
                return;
            }
            if (wroteNormalDiary)
            {
                CompleteNightDiary();
            }
            BuildCalendar(direction: SlideDirection.Backward);
        }

        private void CompleteNightDiary()
        {
            if (!isNightDiaryActive || !TryResolveTimeManager())
            {
                return;
            }

            // Discard unused drafts now, but keep the current night until the player finishes viewing the tablet.
            DiaryJournalStore.DiscardNormalDraftsForDay(activeNightDiaryDay);
            completedNightDiaryDay = activeNightDiaryDay;
            DebugConsoleTrace.Diary(false);
            if (promptParentPanel != null) promptParentPanel.SetActive(false);
            isNightDiaryActive = false;
            isDebugNightDiary = false;
            activeNightDiaryDay = 0;
        }

        public bool TryBeginCompletedNightSleep()
        {
            if (completedNightDiaryDay == 0 || !TryResolveTimeManager()) return false;
            if (completedNightDiaryDay != timeManager.CurrentDay)
            {
                completedNightDiaryDay = 0;
                return false;
            }
            if (DialogueManager.Instance == null || !DialogueManager.Instance.TryBeginNightSleep()) return false;
            completedNightDiaryDay = 0;
            return true;
        }

        private static string ResolveDiaryBody(string body)
        {
            return DialogueManager.ResolveRuntimeText(body);
        }

        private void DiscardUnconfirmedDebugNightDiary()
        {
            if (!isNightDiaryActive)
            {
                return;
            }

            if (isDebugNightDiary)
            {
                DiaryJournalStore.DiscardNormalDraftsForDay(activeNightDiaryDay);
            }

            isNightDiaryActive = false;
            isDebugNightDiary = false;
            activeNightDiaryDay = 0;
        }

        private IEnumerator SlideWindowRoutine(GameObject target, SlideDirection direction)
        {
            GameObject previous = activeWindow;
            if (previous == target)
            {
                NormalizeWindow(target, visible: true);
                yield break;
            }

            isViewTransitioning = true;
            transitionFromWindow = previous;
            transitionToWindow = target;
            target.SetActive(true);
            RectTransform targetRect = target.transform as RectTransform;
            RectTransform previousRect = previous != null ? previous.transform as RectTransform : null;
            float distance = GetSlideDistance(targetRect != null ? targetRect : previousRect);
            float directionSign = (int)direction;
            NormalizeWindow(target, visible: true);
            SetWindowPosition(target, Vector2.right * distance * directionSign);
            SetWindowInteraction(target, false);
            if (previous != null)
            {
                NormalizeWindow(previous, visible: true);
                SetWindowInteraction(previous, false);
            }

            float duration = Mathf.Max(0.01f, windowTransitionSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                SetWindowPosition(target, Vector2.Lerp(Vector2.right * distance * directionSign, Vector2.zero, progress));
                if (previous != null)
                {
                    SetWindowPosition(previous, Vector2.Lerp(Vector2.zero, Vector2.left * distance * directionSign, progress));
                }

                yield return null;
            }

            NormalizeWindow(target, visible: true);
            if (previous != null)
            {
                NormalizeWindow(previous, visible: false);
            }

            activeWindow = target;
            transitionFromWindow = null;
            transitionToWindow = null;
            isViewTransitioning = false;
            windowTransitionCoroutine = null;
        }

        private void StartDetailPageTransition(DateTime nextDate, List<string> notes, SlideDirection direction)
        {
            if (isDetailPageTransitioning || detailBackImage == null || detailView == null || !detailView.activeInHierarchy)
            {
                return;
            }

            StopDetailPageTransition();
            detailPageTransitionCoroutine = StartCoroutine(SlideDetailPageRoutine(nextDate, notes, direction));
        }

        private IEnumerator SlideDetailPageRoutine(DateTime nextDate, List<string> notes, SlideDirection direction)
        {
            isDetailPageTransitioning = true;
            SetWindowInteraction(detailView, false);
            GameObject outgoingPage = detailBackImage;
            GameObject incomingPage = Instantiate(outgoingPage, outgoingPage.transform.parent, false);
            incomingDetailBackImage = incomingPage;
            incomingPage.name = "DetailBackImageIncoming";
            incomingPage.SetActive(true);
            incomingPage.transform.SetSiblingIndex(Mathf.Min(outgoingPage.transform.GetSiblingIndex() + 1, incomingPage.transform.parent.childCount - 1));
            TMP_Text incomingBodyText = incomingPage.GetComponentInChildren<TMP_Text>(true);
            if (incomingBodyText != null)
            {
                incomingBodyText.text = string.Join("\n\n", notes.Select(ResolveDiaryBody));
                ResetBodyScroll(EnsureBodyScroll(incomingBodyText), atBottom: false);
            }

            RectTransform outgoingRect = outgoingPage.transform as RectTransform;
            RectTransform incomingRect = incomingPage.transform as RectTransform;
            float distance = GetSlideDistance(incomingRect != null ? incomingRect : outgoingRect);
            float directionSign = (int)direction;
            SetPageVisual(outgoingPage, Vector2.zero);
            SetPageVisual(incomingPage, Vector2.right * distance * directionSign);

            float duration = Mathf.Max(0.01f, detailPageTransitionSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                SetPageVisual(outgoingPage, Vector2.Lerp(Vector2.zero, Vector2.left * distance * directionSign, progress));
                SetPageVisual(incomingPage, Vector2.Lerp(Vector2.right * distance * directionSign, Vector2.zero, progress));
                yield return null;
            }

            Destroy(outgoingPage);
            detailBackImage = incomingPage;
            incomingDetailBackImage = null;
            detailBackImage.name = "DetailBackImage";
            detailBodyText = incomingBodyText;
            SetPageVisual(detailBackImage, Vector2.zero);
            ApplyDetailEntry(nextDate, notes, detailIsSpecialSort);
            SetWindowInteraction(detailView, true);
            isDetailPageTransitioning = false;
            detailPageTransitionCoroutine = null;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            // Unity keeps a managed reference after Destroy until the frame ends.
            // Use Unity's null comparison so a destroyed CanvasGroup is replaced.
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private void NormalizeWindow(GameObject window, bool visible)
        {
            if (window == null)
            {
                return;
            }

            window.SetActive(visible);
            SetWindowPosition(window, Vector2.zero);
            SetWindowVisual(window, visible ? 1f : 0f, 1f);
            SetWindowInteraction(window, visible);
        }

        private static void SetWindowPosition(GameObject window, Vector2 position)
        {
            if (window != null && window.transform is RectTransform rect)
            {
                rect.anchoredPosition = position;
            }
        }

        private static float GetSlideDistance(RectTransform rect)
        {
            if (rect == null)
            {
                return 1f;
            }

            float parentWidth = rect.parent is RectTransform parent ? parent.rect.width : 0f;
            return Mathf.Max(1f, rect.rect.width, parentWidth);
        }

        private static void SetPageVisual(GameObject page, Vector2 position)
        {
            if (page == null)
            {
                return;
            }

            SetWindowPosition(page, position);
            CanvasGroup group = GetOrAddCanvasGroup(page);
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void SetWindowVisual(GameObject window, float alpha, float scale)
        {
            if (window == null)
            {
                return;
            }

            CanvasGroup group = GetOrAddCanvasGroup(window);
            group.alpha = alpha;
            bool acceptsInput = alpha >= 0.99f;
            group.interactable = acceptsInput;
            group.blocksRaycasts = acceptsInput;

            window.transform.localScale = Vector3.one * scale;
        }

        private static void SetWindowInteraction(GameObject window, bool enabled)
        {
            if (window == null)
            {
                return;
            }

            CanvasGroup group = GetOrAddCanvasGroup(window);
            group.interactable = enabled;
            group.blocksRaycasts = enabled;
        }

        private void StopWindowTransition()
        {
            if (windowTransitionCoroutine != null)
            {
                StopCoroutine(windowTransitionCoroutine);
                windowTransitionCoroutine = null;
            }

            if (transitionFromWindow != null)
            {
                NormalizeWindow(transitionFromWindow, visible: true);
                activeWindow = transitionFromWindow;
            }

            if (transitionToWindow != null && transitionToWindow != activeWindow)
            {
                NormalizeWindow(transitionToWindow, visible: false);
            }

            transitionFromWindow = null;
            transitionToWindow = null;
            isViewTransitioning = false;
        }

        private void StopDetailPageTransition()
        {
            if (detailPageTransitionCoroutine != null)
            {
                StopCoroutine(detailPageTransitionCoroutine);
                detailPageTransitionCoroutine = null;
            }

            if (incomingDetailBackImage != null)
            {
                Destroy(incomingDetailBackImage);
                incomingDetailBackImage = null;
            }

            if (detailBackImage != null)
            {
                SetPageVisual(detailBackImage, Vector2.zero);
            }

            if (detailView != null && detailView.activeInHierarchy && !isViewTransitioning)
            {
                SetWindowInteraction(detailView, true);
            }

            isDetailPageTransitioning = false;
        }

        private void StopWritingRoutine()
        {
            if (writingCoroutine != null)
            {
                StopCoroutine(writingCoroutine);
                writingCoroutine = null;
            }
        }

        private void ResetWritingPresentation()
        {
            if (writingBodyText != null)
            {
                writingBodyText.text = string.Empty;
            }

            if (microphoneIndicator != null)
            {
                microphoneIndicator.SetActive(false);
            }

            if (writingFinishButton != null)
            {
                writingFinishButton.gameObject.SetActive(false);
            }

            if (reWritingButton != null)
            {
                reWritingButton.gameObject.SetActive(false);
            }
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        public void ResetRuntimeStateForTitleReload()
        {
            completedNightDiaryDay = 0;
            StopWritingRoutine();
            StopDetailPageTransition();
            StopWindowTransition();
            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;
            DiscardUnconfirmedDebugNightDiary();
            if (promptParentPanel != null) promptParentPanel.SetActive(false);
            if (panel != null) panel.SetActive(false);
        }

        private void Close()
        {
            StopWritingRoutine();
            StopDetailPageTransition();
            StopWindowTransition();
            entriesAwaitingConfirmation.Clear();
            confirmingSpecialEntries = false;
            DiscardUnconfirmedDebugNightDiary();
            if (promptParentPanel != null)
            {
                promptParentPanel.SetActive(false);
            }

            if (TabletHomeApplicationController.Instance == null ||
                !TabletHomeApplicationController.Instance.CloseApplication(panel))
            {
                if (panel != null)
                {
                    panel.SetActive(false);
                }
            }
        }

        private GameObject FindChild(string objectName)
        {
            foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private T FindChildComponent<T>(string objectName) where T : Component
        {
            GameObject child = FindChild(objectName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private void SaveEntries()
        {
            DiaryEntryCollection collection = new DiaryEntryCollection
            {
                entries = entryRecords
                    .Select(entry => new DiaryEntry
                    {
                        date = entry.date,
                        text = entry.text,
                        isSpecial = entry.isSpecial,
                        diaryId = entry.diaryId,
                    })
                    .ToList(),
            };

            ProgressSaveStore.SetString(EntriesPrefsKey, JsonUtility.ToJson(collection));
            ProgressSaveStore.Save();
        }

        private bool TryResolveTimeManager()
        {
            timeManager ??= FindFirstObjectByType<TimeManager>();
            return timeManager != null;
        }

        private void ResolveUiReferences()
        {
            panel ??= FindSceneObject("DiaryPanel");
            EnsureTransitionViewportMask();
        }

        /// <summary>
        /// Keeps pages that are translated during a diary transition inside the physical tablet display.
        /// </summary>
        private void EnsureTransitionViewportMask()
        {
            if (panel == null)
            {
                return;
            }

            RectMask2D mask = panel.GetComponent<RectMask2D>();
            if (mask == null)
            {
                panel.AddComponent<RectMask2D>();
            }
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform transform in transforms)
            {
                if (transform.name == objectName && transform.gameObject.scene.IsValid())
                {
                    return transform.gameObject;
                }
            }

            return null;
        }
    }
}
