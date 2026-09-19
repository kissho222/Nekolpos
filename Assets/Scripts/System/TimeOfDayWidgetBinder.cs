using Nekolpos.TimeSystem;
using UnityEngine;

namespace Nekolpos.System
{
    public sealed class TimeOfDayWidgetBinder : MonoBehaviour
    {
        private const string LogPrefix = "[TimeOfDayWidgetBinder]";

        [global::System.Diagnostics.Conditional("NEKOLPOS_VERBOSE_LOGS")]
        private static void VerboseLog(string message)
        {
            Debug.Log(message);
        }

        [SerializeField] private TimeOfDayWidget widget;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private bool findReferencesOnAwake = true;

        private void Awake()
        {
            if (findReferencesOnAwake)
            {
                ResolveReferences();
            }
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeTimeManager();
            Refresh();
        }

        private void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.OnTimeAdvanced -= HandleTimeAdvanced;
            }
        }

        private void Update()
        {
            if (timeManager != null)
            {
                return;
            }

            ResolveReferences();
            SubscribeTimeManager();
            Refresh();
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (widget == null || timeManager == null)
            {
                VerboseLog($"{LogPrefix} Refresh skipped: widget={(widget != null ? widget.name : "null")}, timeManager={(timeManager != null ? timeManager.name : "null")}");
                return;
            }

            VerboseLog($"{LogPrefix} Refresh day={timeManager.CurrentDay}, period={timeManager.CurrentPeriod}, widget={widget.name}, timeManager={timeManager.name}");
            widget.SetState(timeManager.CurrentDay, Convert(timeManager.CurrentPeriod));
        }

        private void ResolveReferences()
        {
            widget ??= GetComponent<TimeOfDayWidget>();
            timeManager ??= FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            VerboseLog($"{LogPrefix} ResolveReferences widget={(widget != null ? widget.name : "null")}, timeManager={(timeManager != null ? timeManager.name : "null")}");
        }

        private void SubscribeTimeManager()
        {
            if (timeManager == null)
            {
                VerboseLog($"{LogPrefix} Subscribe skipped: timeManager=null");
                return;
            }

            timeManager.OnTimeAdvanced -= HandleTimeAdvanced;
            timeManager.OnTimeAdvanced += HandleTimeAdvanced;
            VerboseLog($"{LogPrefix} Subscribed to {timeManager.name}.OnTimeAdvanced");
        }

        private void HandleTimeAdvanced(TimeAdvanceResult result)
        {
            VerboseLog($"{LogPrefix} HandleTimeAdvanced day {result.PreviousDay}->{result.CurrentDay}, period {result.PreviousPeriod}->{result.CurrentPeriod}, widget={(widget != null ? widget.name : "null")}");
            if (widget != null)
            {
                widget.SetState(result.CurrentDay, Convert(result.CurrentPeriod));
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
    }
}
