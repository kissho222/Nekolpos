using System;
using Backgammon.Conversation;
using Nekolpos.System;
using UnityEngine;

namespace Nekolpos.TimeSystem
{
    public readonly struct TimeAdvanceResult
    {
        public TimeAdvanceResult(DayPeriod previousPeriod, DayPeriod currentPeriod, int previousDay, int currentDay, int advancedMinutes)
        {
            PreviousPeriod = previousPeriod;
            CurrentPeriod = currentPeriod;
            PreviousDay = previousDay;
            CurrentDay = currentDay;
            AdvancedMinutes = advancedMinutes;
        }

        public DayPeriod PreviousPeriod { get; }

        public DayPeriod CurrentPeriod { get; }

        public int PreviousDay { get; }

        public int CurrentDay { get; }

        public int AdvancedMinutes { get; }

        public int AdvancedDayCount => CurrentDay - PreviousDay;

        public bool PeriodChanged => PreviousPeriod != CurrentPeriod || AdvancedDayCount > 0;
    }

    public class TimeManager : MonoBehaviour
    {
        private const int PeriodCountPerDay = 4;
        private const string LogPrefix = "[TimeManager]";

        [Header("Timeline")]
        [SerializeField] [Min(1)] private int minutesPerPeriod = 120;
        [SerializeField] [Min(1)] private int startingDay = 1;
        [SerializeField] private DayPeriod startingPeriod = DayPeriod.Morning;
        [SerializeField] private bool synchronizeGameState = true;
        [SerializeField] private ConversationGameStateManager conversationGameStateManager;
        [SerializeField] private ConversationDebugPanel conversationDebugPanel;
        [SerializeField] private bool logWeatherOnDayChange = true;
        [SerializeField] private WeatherType weather;
        [SerializeField] private WeatherType nextActualWeather;
        [SerializeField] private WeatherType tomorrowWeather;

        private int currentDay;
        private int minutesIntoDay;
        private bool initialized;
        private bool weatherInitialized;
        private GameManager subscribedGameManager;

        public event Action<TimeAdvanceResult> OnTimeAdvanced;

        public int CurrentDay
        {
            get
            {
                EnsureInitialized();
                return currentDay;
            }
        }

        public DayPeriod CurrentPeriod
        {
            get
            {
                EnsureInitialized();
                return ResolvePeriod(minutesIntoDay);
            }
        }

        public WeatherType Weather
        {
            get
            {
                EnsureInitialized();
                return weather;
            }
        }

        public WeatherType TomorrowWeather
        {
            get
            {
                EnsureInitialized();
                return tomorrowWeather;
            }
        }

        public WeatherType NextActualWeather
        {
            get
            {
                EnsureInitialized();
                return nextActualWeather;
            }
        }

        public bool ForecastCorrect
        {
            get
            {
                EnsureInitialized();
                return tomorrowWeather == nextActualWeather;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Start()
        {
            EnsureInitialized();
            BindGameManager();
            SyncFromGameManagerState(false);
        }

        private void OnDestroy()
        {
            UnbindGameManager();
        }

        public void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            currentDay = Mathf.Max(1, startingDay);
            minutesIntoDay = Mathf.Clamp((int)startingPeriod * minutesPerPeriod, 0, (minutesPerPeriod * PeriodCountPerDay) - 1);
            initialized = true;
            TrySyncFromGameManager();
            EnsureWeatherInitialized();
            SyncConversationState(CurrentPeriod);
        }

        public TimeAdvanceResult AdvanceTime(int minutes)
        {
            EnsureInitialized();

            int safeMinutes = Mathf.Max(0, minutes);
            DayPeriod previousPeriod = CurrentPeriod;
            int previousDay = currentDay;

            minutesIntoDay += safeMinutes;
            int dayLength = minutesPerPeriod * PeriodCountPerDay;
            while (minutesIntoDay >= dayLength)
            {
                minutesIntoDay -= dayLength;
                currentDay++;
                AdvanceWeatherToNextDay();
            }

            DayPeriod currentPeriod = CurrentPeriod;
            if (synchronizeGameState)
            {
                SyncGameManager(currentPeriod);
            }

            SyncConversationState(currentPeriod);
            ApplyLightingForPeriod(currentPeriod);

            TimeAdvanceResult result = new TimeAdvanceResult(previousPeriod, currentPeriod, previousDay, currentDay, safeMinutes);
            Debug.Log($"{LogPrefix} AdvanceTime minutes={safeMinutes}, day {previousDay}->{currentDay}, period {previousPeriod}->{currentPeriod}, changed={result.PeriodChanged}");
            OnTimeAdvanced?.Invoke(result);
            return result;
        }

        public TimeAdvanceResult AdvanceToNextMorning()
        {
            EnsureInitialized();

            DayPeriod previousPeriod = CurrentPeriod;
            int previousDay = currentDay;

            currentDay++;
            minutesIntoDay = GetStartMinuteForPeriod(DayPeriod.Morning);
            AdvanceWeatherToNextDay();

            if (synchronizeGameState)
            {
                SyncGameManager(DayPeriod.Morning);
            }

            SyncConversationState(DayPeriod.Morning);
            ApplyLightingForPeriod(DayPeriod.Morning);

            TimeAdvanceResult result = new TimeAdvanceResult(
                previousPeriod,
                DayPeriod.Morning,
                previousDay,
                currentDay,
                0);
            Debug.Log($"{LogPrefix} AdvanceToNextMorning day {previousDay}->{currentDay}, period {previousPeriod}->Morning, changed={result.PeriodChanged}");
            OnTimeAdvanced?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Sets the current calendar position through the same state, lighting, conversation, and HUD sync path
        /// used by normal time advancement. Intended for development tools and deterministic test setup.
        /// </summary>
        public TimeAdvanceResult SetCurrentTime(int day, DayPeriod period)
        {
            EnsureInitialized();

            DayPeriod previousPeriod = CurrentPeriod;
            int previousDay = currentDay;
            currentDay = Mathf.Max(1, day);
            minutesIntoDay = GetStartMinuteForPeriod(period);

            if (synchronizeGameState)
            {
                SyncGameManager(period);
            }

            SyncConversationState(period);
            ApplyLightingForPeriod(period);

            TimeAdvanceResult result = new TimeAdvanceResult(
                previousPeriod,
                period,
                previousDay,
                currentDay,
                0);
            OnTimeAdvanced?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Replaces the current weather and refreshes all existing weather consumers.
        /// </summary>
        public void SetWeather(WeatherType value)
        {
            EnsureInitialized();
            weather = value;
            WriteWeatherToState(ResolveConversationState());
            ApplyLightingForPeriod(CurrentPeriod);
            RefreshConversationDebugPanel();
        }

        /// <summary>
        /// Replaces the existing forecast value without creating a parallel debug-only forecast.
        /// </summary>
        public void SetTomorrowWeather(WeatherType value)
        {
            EnsureInitialized();
            tomorrowWeather = value;
            WriteWeatherToState(ResolveConversationState());
            RefreshConversationDebugPanel();
        }

        public string BuildTimePassageText(TimeAdvanceResult result)
        {
            return string.Empty;
        }

        public bool TryGetTransitionGreeting(TimeAdvanceResult result, out string greeting)
        {
            if (!result.PeriodChanged)
            {
                greeting = string.Empty;
                return false;
            }

            switch (result.CurrentPeriod)
            {
                case DayPeriod.Morning:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingMorningKey,
                        "おはよう！朝だね。今日はなにしようか。");
                    return true;
                case DayPeriod.Afternoon:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingDayKey,
                        "昼だね。お腹空いてない？");
                    return true;
                case DayPeriod.Evening:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingEveningKey,
                        "夕方だね。やり忘れたことはない？");
                    return true;
                case DayPeriod.Night:
                    greeting = BasicSystemDialogueCatalog.Get(
                        BasicSystemDialogueCatalog.GreetingNightKey,
                        "夜だね。寝る前に何かする？");
                    return true;
                default:
                    greeting = string.Empty;
                    return false;
            }
        }

        public static string GetPeriodLabel(DayPeriod period)
        {
            switch (period)
            {
                case DayPeriod.Morning:
                    return "朝";
                case DayPeriod.Afternoon:
                    return "昼";
                case DayPeriod.Evening:
                    return "夕方";
                case DayPeriod.Night:
                    return "夜";
                default:
                    return period.ToString();
            }
        }

        private DayPeriod ResolvePeriod(int minuteOfDay)
        {
            if (minuteOfDay < minutesPerPeriod)
            {
                return DayPeriod.Morning;
            }

            if (minuteOfDay < minutesPerPeriod * 2)
            {
                return DayPeriod.Afternoon;
            }

            if (minuteOfDay < minutesPerPeriod * 3)
            {
                return DayPeriod.Evening;
            }

            return DayPeriod.Night;
        }

        private void TrySyncFromGameManager()
        {
            BindGameManager();
            SyncFromGameManagerState(false);
        }

        private void SyncGameManager(DayPeriod period)
        {
            GameManager manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            switch (period)
            {
                case DayPeriod.Morning:
                    if (manager.CurrentState is StateMorning)
                    {
                        return;
                    }

                    manager.ChangeState(new StateMorning(manager), suppressGreeting: true);
                    break;
                case DayPeriod.Afternoon:
                    if (manager.CurrentState is StateDay)
                    {
                        return;
                    }

                    manager.ChangeState(new StateDay(manager), suppressGreeting: true);
                    break;
                case DayPeriod.Evening:
                    if (manager.CurrentState is StateEvening)
                    {
                        return;
                    }

                    manager.ChangeState(new StateEvening(manager), suppressGreeting: true);
                    break;
                case DayPeriod.Night:
                    if (manager.CurrentState is StateNight)
                    {
                        return;
                    }

                    manager.ChangeState(new StateNight(manager), suppressGreeting: true);
                    break;
            }
        }

        private void SyncConversationState(DayPeriod period)
        {
            ConversationGameState state = ResolveConversationState();
            if (state == null)
            {
                return;
            }

            state.SetString("Phase", ConvertPhase(period));
            state.SetString("CurrentPhase", ConvertPhase(period));
            state.SetInt("CurrentDay", currentDay);
            WriteWeatherToState(state);
            RefreshConversationDebugPanel();
        }

        private void EnsureWeatherInitialized()
        {
            if (weatherInitialized)
            {
                return;
            }

            ConversationGameState state = ResolveConversationState();
            if (state != null &&
                TryReadWeatherFromState(state, WeatherSystem.CurrentWeatherKey, WeatherSystem.WeatherKey, out weather) &&
                TryReadWeatherFromState(state, WeatherSystem.NextActualWeatherKey, null, out nextActualWeather) &&
                TryReadWeatherFromState(state, WeatherSystem.CurrentWeatherForecastKey, WeatherSystem.TomorrowWeatherKey, out tomorrowWeather))
            {
                weatherInitialized = true;
                WriteWeatherToState(state);
                return;
            }

            weather = WeatherSystem.GenerateRandomWeather();
            nextActualWeather = WeatherSystem.GenerateRandomWeather();
            tomorrowWeather = WeatherSystem.GenerateForecast(nextActualWeather);
            weatherInitialized = true;
            if (state != null)
            {
                WriteWeatherToState(state);
            }
        }

        private void AdvanceWeatherToNextDay()
        {
            EnsureWeatherInitialized();
            weather = nextActualWeather;
            nextActualWeather = WeatherSystem.GenerateRandomWeather();
            tomorrowWeather = WeatherSystem.GenerateForecast(nextActualWeather);
            ConversationGameState state = ResolveConversationState();
            if (state != null)
            {
                WriteWeatherToState(state);
            }

            LogWeatherState();
        }

        private void WriteWeatherToState(ConversationGameState state)
        {
            if (state == null || !weatherInitialized)
            {
                return;
            }

            state.SetString(WeatherSystem.WeatherKey, WeatherSystem.ToDisplayText(weather));
            state.SetString(WeatherSystem.CurrentWeatherKey, weather.ToString());
            state.SetString(WeatherSystem.TomorrowWeatherKey, WeatherSystem.ToDisplayText(tomorrowWeather));
            state.SetString(WeatherSystem.CurrentWeatherForecastKey, tomorrowWeather.ToString());
            state.SetString(WeatherSystem.ForecastWeatherKey, WeatherSystem.ToDisplayText(tomorrowWeather));
            state.SetString(WeatherSystem.ForecastWeatherMisspelledKey, WeatherSystem.ToDisplayText(tomorrowWeather));
            state.SetString(WeatherSystem.ForecastWeatherQuestionKey, WeatherSystem.ToDisplayText(tomorrowWeather));
            state.SetString(WeatherSystem.NextActualWeatherKey, nextActualWeather.ToString());
            state.SetBool(WeatherSystem.ForecastCorrectKey, ForecastCorrect);
        }

        private static bool TryReadWeatherFromState(ConversationGameState state, string primaryKey, string fallbackKey, out WeatherType value)
        {
            if (TryReadWeatherValue(state, primaryKey, out value))
            {
                return true;
            }

            return TryReadWeatherValue(state, fallbackKey, out value);
        }

        private static bool TryReadWeatherValue(ConversationGameState state, string key, out WeatherType value)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                state.TryGetString(key, out string token) &&
                WeatherSystem.TryParseWeather(token, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private void LogWeatherState()
        {
            if (!logWeatherOnDayChange)
            {
                return;
            }

            Debug.Log(
                $"[Weather] Current={WeatherSystem.ToDisplayText(weather)} / NextActual={WeatherSystem.ToDisplayText(nextActualWeather)} / Forecast={WeatherSystem.ToDisplayText(tomorrowWeather)} / ForecastCorrect={ForecastCorrect}",
                this);
        }

        private ConversationGameState ResolveConversationState()
        {
            if (conversationGameStateManager == null)
            {
                conversationGameStateManager = GetComponent<ConversationGameStateManager>();
                if (conversationGameStateManager == null)
                {
                    conversationGameStateManager = FindFirstObjectByType<ConversationGameStateManager>();
                }
            }

            return conversationGameStateManager != null ? conversationGameStateManager.State : null;
        }

        private void RefreshConversationDebugPanel()
        {
            if (conversationDebugPanel == null)
            {
                conversationDebugPanel = FindFirstObjectByType<ConversationDebugPanel>();
            }

            conversationDebugPanel?.RefreshStateDisplay();
        }

        private static string ConvertPhase(DayPeriod period)
        {
            switch (period)
            {
                case DayPeriod.Morning:
                    return "Morning";
                case DayPeriod.Afternoon:
                    return "Lunch";
                case DayPeriod.Evening:
                    return "Evening";
                case DayPeriod.Night:
                    return "Night";
                default:
                    return period.ToString();
            }
        }

        private void BindGameManager()
        {
            GameManager manager = GameManager.Instance;
            if (ReferenceEquals(subscribedGameManager, manager))
            {
                return;
            }

            UnbindGameManager();
            subscribedGameManager = manager;
            if (subscribedGameManager != null)
            {
                subscribedGameManager.OnStateChanged += HandleGameStateChanged;
                Debug.Log($"{LogPrefix} Bound GameManager {subscribedGameManager.name}");
            }
            else
            {
                Debug.Log($"{LogPrefix} GameManager not found while binding");
            }
        }

        private void UnbindGameManager()
        {
            if (subscribedGameManager == null)
            {
                return;
            }

            subscribedGameManager.OnStateChanged -= HandleGameStateChanged;
            subscribedGameManager = null;
        }

        private void HandleGameStateChanged(IGameState state)
        {
            EnsureInitialized();
            DayPeriod previousPeriod = CurrentPeriod;
            int previousDay = currentDay;
            string stateName = state != null ? state.GetType().Name : "null";

            if (!TryApplyStateToTimeline(state, true))
            {
                Debug.Log($"{LogPrefix} GameManager state changed to {stateName}, but no DayPeriod mapping was found");
                return;
            }

            DayPeriod currentPeriod = CurrentPeriod;
            SyncConversationState(currentPeriod);

            TimeAdvanceResult result = new TimeAdvanceResult(
                previousPeriod,
                currentPeriod,
                previousDay,
                currentDay,
                0);

            if (result.PeriodChanged)
            {
                Debug.Log($"{LogPrefix} GameManager state changed to {stateName}: day {previousDay}->{currentDay}, period {previousPeriod}->{currentPeriod}; invoking OnTimeAdvanced");
                OnTimeAdvanced?.Invoke(result);
            }
            else
            {
                Debug.Log($"{LogPrefix} GameManager state changed to {stateName}: day {previousDay}->{currentDay}, period {previousPeriod}->{currentPeriod}; no event because period did not change");
            }
        }

        private void SyncFromGameManagerState(bool allowDayWrap)
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState == null)
            {
                return;
            }

            if (!TryApplyStateToTimeline(manager.CurrentState, allowDayWrap))
            {
                return;
            }

            SyncConversationState(CurrentPeriod);
        }

        private bool TryApplyStateToTimeline(IGameState state, bool allowDayWrap)
        {
            if (!TryResolvePeriodFromState(state, out DayPeriod resolvedPeriod))
            {
                return false;
            }

            DayPeriod previousPeriod = CurrentPeriod;
            minutesIntoDay = GetStartMinuteForPeriod(resolvedPeriod);
            if (allowDayWrap && previousPeriod == DayPeriod.Night && resolvedPeriod == DayPeriod.Morning)
            {
                currentDay++;
                AdvanceWeatherToNextDay();
            }

            return true;
        }

        private void ApplyLightingForPeriod(DayPeriod period)
        {
            if (!TryConvertToLightTimeOfDay(period, out LightTimeOfDay lightTime))
            {
                Debug.LogWarning($"{LogPrefix} [Lighting][TimeChange] No LightTimeOfDay mapping for period={period}");
                return;
            }

            if (RoomLightingManager.Instance == null)
            {
                Debug.LogWarning($"{LogPrefix} [Lighting][TimeChange] period={period} -> RoomLightingManager.Instance is null");
                return;
            }

            Debug.Log($"{LogPrefix} [Lighting][TimeChange] period={period} -> request lighting={lightTime}, weather={Weather}");
            RoomLightingManager.Instance.SetTimeOfDay(lightTime);
        }

        private static bool TryConvertToLightTimeOfDay(DayPeriod period, out LightTimeOfDay lightTime)
        {
            switch (period)
            {
                case DayPeriod.Morning:
                    lightTime = LightTimeOfDay.Morning;
                    return true;
                case DayPeriod.Afternoon:
                    lightTime = LightTimeOfDay.Day;
                    return true;
                case DayPeriod.Evening:
                    lightTime = LightTimeOfDay.Evening;
                    return true;
                case DayPeriod.Night:
                    lightTime = LightTimeOfDay.Night;
                    return true;
                default:
                    lightTime = default;
                    return false;
            }
        }

        private int GetStartMinuteForPeriod(DayPeriod period)
        {
            return Mathf.Max(0, (int)period * minutesPerPeriod);
        }

        private static bool TryResolvePeriodFromState(IGameState state, out DayPeriod period)
        {
            if (state is StateMorning)
            {
                period = DayPeriod.Morning;
                return true;
            }

            if (state is StateDay)
            {
                period = DayPeriod.Afternoon;
                return true;
            }

            if (state is StateEvening)
            {
                period = DayPeriod.Evening;
                return true;
            }

            if (state is StateNight)
            {
                period = DayPeriod.Night;
                return true;
            }

            period = default;
            return false;
        }
    }
}
