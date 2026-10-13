using System;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Data;
using Nekolpos.System;
using UnityEngine;
using Nekolpos.TimeSystem;
using Newtonsoft.Json;

namespace Nekolpos.StatusSystem
{
    public readonly struct StatusChangeResult
    {
        public StatusChangeResult(StatusType statusType, int previousValue, int currentValue, string reason = null, string sourceId = null)
        {
            StatusType = statusType;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
            Reason = reason ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
        }

        public StatusType StatusType { get; }

        public int PreviousValue { get; }

        public int CurrentValue { get; }

        public int Delta => CurrentValue - PreviousValue;

        public string Reason { get; }

        public string SourceId { get; }
    }

    public readonly struct StatusRepresentativeState
    {
        public StatusRepresentativeState(StatusType? primary, StatusType? secondary)
        {
            Primary = primary;
            Secondary = secondary;
        }

        public StatusType? Primary { get; }

        public StatusType? Secondary { get; }

        public bool IsNormal => !Primary.HasValue;

        public string DisplayName => IsNormal
            ? "Normal"
            : Secondary.HasValue
                ? $"{Primary.Value} + {Secondary.Value}"
                : Primary.Value.ToString();
    }

    public class StatusManager : MonoBehaviour
    {
        public const int MinimumValue = 0;
        public const int MaximumValue = 100;
        public const int ActiveThreshold = 70;
        private const int MaximumChangeHistoryCount = 64;

        [Header("Optional Shared Data")]
        [SerializeField] private CatDataSO catData;
        [SerializeField] private ConversationGameStateManager conversationGameStateManager;
        [SerializeField] private ConversationDebugPanel conversationDebugPanel;
        [SerializeField] private bool initializeFromCatData = true;

        [Header("Fallback Defaults")]
        [SerializeField] [Range(0, 100)] private int defaultAffection = 50;
        [SerializeField] [Range(0, 100)] private int defaultHostility = 0;
        [SerializeField] [Range(0, 100)] private int defaultConcern = 30;

        private readonly Dictionary<StatusType, int> values = new Dictionary<StatusType, int>();
        private readonly Dictionary<StatusType, long> lastChangeOrder = new Dictionary<StatusType, long>();
        private readonly List<StatusChangeResult> changeHistory = new List<StatusChangeResult>();
        private bool initialized;
        private long nextChangeOrder;
        public const string SaveKey = "Nekolpos.Status.State.v1";
        private readonly Dictionary<StatusType, int> lastRiseDays = new Dictionary<StatusType, int>();
        private int lastDecayDay;
        private bool applyingNaturalRecovery;

        [Serializable] private sealed class SavedStatus
        {
            public StatusType type;
            public int value;
            public int? lastRiseDay;
            public long riseOrder;
        }
        [Serializable] private sealed class SavedState
        {
            public int version = 1;
            public int? lastDecayDay;
            public List<SavedStatus> statuses;
        }

        private int CurrentGameDay => FindFirstObjectByType<TimeManager>()?.CurrentDay ?? Mathf.Max(1, ProgressSaveStore.GetInt(TimeManager.CurrentDayPrefsKey, 1));
        public StatusRepresentativeState RepresentativeState => GetRepresentativeState();
        public bool IsInitialized => initialized;

        public int GetBaseline(StatusType type)
        {
            int value = type switch
            {
                StatusType.Affection => initializeFromCatData && catData != null ? catData.affectionLevel : defaultAffection,
                StatusType.Sadistic => initializeFromCatData && catData != null ? catData.sadisticLevel : 30,
                StatusType.Concern => initializeFromCatData && catData != null ? catData.concernLevel : defaultConcern,
                StatusType.Hostility => initializeFromCatData && catData != null ? catData.hostilityLevel : defaultHostility,
                StatusType.Obedience => initializeFromCatData && catData != null ? catData.obedienceLevel : 30,
                StatusType.Instinct => initializeFromCatData && catData != null ? catData.instinctLevel : 0,
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            return Mathf.Clamp(value, MinimumValue, MaximumValue);
        }

        private void SaveState()
        {
            var saved = new SavedState { lastDecayDay = lastDecayDay, statuses = new List<SavedStatus>() };
            foreach (StatusType type in Enum.GetValues(typeof(StatusType)))
                saved.statuses.Add(new SavedStatus { type = type, value = values[type], lastRiseDay = lastRiseDays[type], riseOrder = lastChangeOrder[type] });
            ProgressSaveStore.SetString(SaveKey, JsonConvert.SerializeObject(saved));
            ProgressSaveStore.Save();
        }

        private void RestoreState(string json, int day)
        {
            var saved = JsonConvert.DeserializeObject<SavedState>(json);
            if (saved == null || saved.version != 1 || saved.statuses == null)
                throw new InvalidOperationException("心理値保存データの形式が不正です。既存データを上書きせず読み込みを中止します。");
            var seen = new HashSet<StatusType>();
            foreach (var entry in saved.statuses)
            {
                if (entry == null || !Enum.IsDefined(typeof(StatusType), entry.type) || !seen.Add(entry.type) || entry.value < 0 || entry.value > 100 || entry.riseOrder < 0 || entry.lastRiseDay < 1)
                    throw new InvalidOperationException("心理値保存データに不正な値または重複があります。");
            }
            if (seen.Count != Enum.GetValues(typeof(StatusType)).Length || saved.lastDecayDay < 1)
                throw new InvalidOperationException("心理値保存データが不足しています。");
            foreach (var entry in saved.statuses)
            {
                values[entry.type] = entry.value;
                lastRiseDays[entry.type] = entry.lastRiseDay ?? day;
                lastChangeOrder[entry.type] = entry.riseOrder;
                nextChangeOrder = Math.Max(nextChangeOrder, entry.riseOrder);
            }
            lastDecayDay = saved.lastDecayDay ?? day;
        }

        public void ProcessDateUpdate(int day)
        {
            EnsureInitialized();
            if (day <= lastDecayDay) return;
            // The saved checkpoint prevents duplicate decay, including after a reload.
            for (int date = lastDecayDay + 1; date <= day; date++)
            {
                applyingNaturalRecovery = true;
                try
                {
                    foreach (StatusType type in Enum.GetValues(typeof(StatusType)))
                    {
                        if ((long)date - lastRiseDays[type] < 2) continue;
                        int baseline = GetBaseline(type);
                        int current = values[type];
                        int next = current > baseline ? Math.Max(baseline, current - 10) : Math.Min(baseline, current + 10);
                        if (next != current) SetValue(type, next, "NaturalRecovery", "Day:" + date);
                    }
                    lastDecayDay = date;
                }
                finally { applyingNaturalRecovery = false; }
            }
            SyncAllToConversationState();
            if (Application.isPlaying) SaveState();
        }

        public event Action<StatusType, int> OnStatusChanged;
        public event Action<StatusChangeResult> OnStatusChangeApplied;

        /// <summary>Initial-value source only. Runtime values are never written back to this asset.</summary>
        public CatDataSO InitialData => catData;

        public IReadOnlyList<StatusChangeResult> ChangeHistory => changeHistory;

        public static StatusManager FindOrCreate(GameObject fallbackHost = null)
        {
            StatusManager existing = FindFirstObjectByType<StatusManager>();
            if (existing != null)
            {
                return existing;
            }

            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            GameObject host = dialogueManager != null ? dialogueManager.gameObject : fallbackHost;
            if (host == null)
            {
                return null;
            }

            StatusManager created = host.GetComponent<StatusManager>() ?? host.AddComponent<StatusManager>();
            if (dialogueManager != null)
            {
                created.ConfigureInitialData(dialogueManager.catData);
            }

            return created;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            ResolveInitialData();

            if (initializeFromCatData && catData != null)
            {
                values[StatusType.Affection] = catData.affectionLevel;
                values[StatusType.Sadistic] = catData.sadisticLevel;
                values[StatusType.Concern] = catData.concernLevel;
                values[StatusType.Hostility] = catData.hostilityLevel;
                values[StatusType.Obedience] = catData.obedienceLevel;
                values[StatusType.Instinct] = catData.instinctLevel;
            }
            else
            {
                values[StatusType.Affection] = defaultAffection;
                values[StatusType.Sadistic] = 30;
                values[StatusType.Concern] = defaultConcern;
                values[StatusType.Hostility] = defaultHostility;
                values[StatusType.Obedience] = 30;
                values[StatusType.Instinct] = 0;
            }

            foreach (StatusType statusType in Enum.GetValues(typeof(StatusType)))
            {
                values[statusType] = Mathf.Clamp(values[statusType], MinimumValue, MaximumValue);
                lastChangeOrder[statusType] = 0;
                lastRiseDays[statusType] = CurrentGameDay;
            }

            lastDecayDay = CurrentGameDay;
            if (Application.isPlaying && ProgressSaveStore.HasKey(SaveKey)) RestoreState(ProgressSaveStore.GetString(SaveKey), CurrentGameDay);
            initialized = true;
            SyncAllToConversationState();
            if (Application.isPlaying) { ProcessDateUpdate(CurrentGameDay); SaveState(); }
        }

        public void ResetForNewGame()
        {
            ProgressSaveStore.DeleteKey(SaveKey);
            nextChangeOrder = 0;
            changeHistory.Clear();
            initialized = false;
            EnsureInitialized();
        }

        public int GetValue(StatusType statusType)
        {
            EnsureInitialized();
            return values.TryGetValue(statusType, out int value) ? value : 0;
        }

        public void ConfigureInitialData(CatDataSO initialData, bool resetRuntimeValues = false)
        {
            if (initialData == null || ReferenceEquals(catData, initialData))
            {
                return;
            }

            catData = initialData;
            if (resetRuntimeValues || !initialized)
            {
                initialized = false;
                EnsureInitialized();
            }
        }

        public IReadOnlyList<StatusChangeResult> ApplyEffects(
            IReadOnlyList<StatEffect> effects,
            string reason = "",
            string sourceId = "")
        {
            EnsureInitialized();

            if (effects == null || effects.Count == 0)
            {
                return Array.Empty<StatusChangeResult>();
            }

            List<StatusChangeResult> results = new List<StatusChangeResult>(effects.Count);
            for (int i = 0; i < effects.Count; i++)
            {
                StatEffect effect = effects[i];
                results.Add(ApplyDelta(effect.statusType, effect.value, reason, sourceId));
            }

            return results;
        }

        public StatusChangeResult ApplyDelta(StatusType statusType, int delta, string reason = "", string sourceId = "")
        {
            return SetValue(statusType, GetValue(statusType) + delta, reason, sourceId);
        }

        public StatusChangeResult SetValue(StatusType statusType, int value, string reason = "", string sourceId = "")
        {
            EnsureInitialized();

            int previous = GetValue(statusType);
            int next = Mathf.Clamp(value, MinimumValue, MaximumValue);
            values[statusType] = next;
            if (next > previous && !applyingNaturalRecovery)
            {
                lastChangeOrder[statusType] = ++nextChangeOrder;
                lastRiseDays[statusType] = CurrentGameDay;
            }

            SyncToConversationState(statusType, next);
            StatusChangeResult result = new StatusChangeResult(statusType, previous, next, reason, sourceId);
            RecordChange(result);
            Nekolpos.System.DebugConsoleTrace.StatusChanged((int)statusType, previous, next);
            OnStatusChanged?.Invoke(statusType, next);
            OnStatusChangeApplied?.Invoke(result);
            if (Application.isPlaying && !applyingNaturalRecovery) SaveState();
            return result;
        }

        public bool TryApplyConversationEffect(
            ConversationGameStateEffect effect,
            string reason,
            string sourceId,
            out StatusChangeResult result)
        {
            result = default;
            if (effect == null || !TryResolveStatusTypeKey(effect.key, out StatusType statusType))
            {
                return false;
            }

            string effectType = effect.type?.Trim().ToLowerInvariant();
            int amount = effect.value != null && effect.value.kind == ConversationGameStateValueKind.Integer
                ? effect.value.intValue
                : 1;
            switch (effectType)
            {
                case "set":
                    if (effect.value == null || effect.value.kind != ConversationGameStateValueKind.Integer)
                    {
                        throw new InvalidOperationException($"Status effect '{effect.key}' requires an integer value.");
                    }

                    result = SetValue(statusType, amount, reason, sourceId);
                    return true;
                case "increment":
                    result = ApplyDelta(statusType, amount, reason, sourceId);
                    return true;
                case "decrement":
                    result = ApplyDelta(statusType, -amount, reason, sourceId);
                    return true;
                default:
                    throw new InvalidOperationException($"Status '{effect.key}' does not support effect type '{effect.type}'.");
            }
        }

        public static bool TryResolveStatusTypeKey(string key, out StatusType statusType)
        {
            switch (key?.Trim().ToLowerInvariant())
            {
                case "affection": statusType = StatusType.Affection; return true;
                case "sadistic":
                case "sadism": statusType = StatusType.Sadistic; return true;
                case "concern":
                case "anxiety": statusType = StatusType.Concern; return true;
                case "hostility": statusType = StatusType.Hostility; return true;
                case "obedience":
                case "submission": statusType = StatusType.Obedience; return true;
                case "instinct": statusType = StatusType.Instinct; return true;
                default: statusType = default; return false;
            }
        }

        private void SyncAllToConversationState()
        {
            foreach (StatusType statusType in Enum.GetValues(typeof(StatusType)))
            {
                SyncToConversationState(statusType, GetValue(statusType));
            }

            SyncRepresentativePsychologyState(ResolveConversationState());
            RefreshConversationDebugPanel();
        }

        private void SyncToConversationState(StatusType statusType, int value)
        {
            ConversationGameState state = ResolveConversationState();
            if (state == null)
            {
                return;
            }

            int debugScaleValue = Mathf.Clamp(value, MinimumValue, MaximumValue);
            switch (statusType)
            {
                case StatusType.Affection:
                    state.SetInt("Affection", debugScaleValue);
                    state.SetInt("affection", debugScaleValue);
                    break;
                case StatusType.Sadistic:
                    state.SetInt("Sadistic", debugScaleValue);
                    state.SetInt("sadistic", debugScaleValue);
                    state.SetInt("Sadism", debugScaleValue);
                    state.SetInt("sadism", debugScaleValue);
                    break;
                case StatusType.Concern:
                    state.SetInt("Concern", debugScaleValue);
                    state.SetInt("concern", debugScaleValue);
                    state.SetInt("Anxiety", debugScaleValue);
                    state.SetInt("anxiety", debugScaleValue);
                    break;
                case StatusType.Hostility:
                    state.SetInt("Hostility", debugScaleValue);
                    state.SetInt("hostility", debugScaleValue);
                    break;
                case StatusType.Obedience:
                    state.SetInt("Obedience", debugScaleValue);
                    state.SetInt("obedience", debugScaleValue);
                    break;
                case StatusType.Instinct:
                    state.SetInt("Instinct", debugScaleValue);
                    state.SetInt("instinct", debugScaleValue);
                    break;
            }

            SyncRepresentativePsychologyState(state);
            RefreshConversationDebugPanel();
        }

        private void SyncRepresentativePsychologyState(ConversationGameState state)
        {
            if (state == null)
            {
                return;
            }

            string psychologyState = BuildPsychologyStateKey(GetRepresentativeState());
            state.SetString("PsychologyState", psychologyState);
            state.SetString("psychology_state", psychologyState);
        }

        private static string BuildPsychologyStateKey(StatusRepresentativeState representativeState)
        {
            if (representativeState.IsNormal)
            {
                return "NORMAL";
            }

            List<string> activeStates = new List<string>(2)
            {
                representativeState.Primary.Value.ToString().ToUpperInvariant()
            };
            if (representativeState.Secondary.HasValue)
            {
                activeStates.Add(representativeState.Secondary.Value.ToString().ToUpperInvariant());
            }

            activeStates.Sort(StringComparer.Ordinal);
            return string.Join("_", activeStates);
        }

        public StatusRepresentativeState GetRepresentativeState()
        {
            EnsureInitialized();
            List<StatusType> active = new List<StatusType>();
            foreach (StatusType statusType in Enum.GetValues(typeof(StatusType)))
            {
                if (GetValue(statusType) >= ActiveThreshold)
                {
                    active.Add(statusType);
                }
            }

            active.Sort(CompareRepresentativePriority);
            return active.Count switch
            {
                0 => new StatusRepresentativeState(null, null),
                1 => new StatusRepresentativeState(active[0], null),
                _ => new StatusRepresentativeState(active[0], active[1])
            };
        }

        public StatusType? GetHighestPriorityStatus(IEnumerable<StatusType> candidates)
        {
            EnsureInitialized();
            StatusType? selected = null;
            if (candidates == null) return selected;
            foreach (StatusType candidate in candidates)
            {
                if (!selected.HasValue || CompareRepresentativePriority(candidate, selected.Value) < 0)
                    selected = candidate;
            }
            return selected;
        }

        private int CompareRepresentativePriority(StatusType left, StatusType right)
        {
            // Keep the tie policy separate so future special priorities can be added here.
            int valueComparison = GetValue(right).CompareTo(GetValue(left));
            if (valueComparison != 0)
            {
                return valueComparison;
            }

            long leftOrder = lastChangeOrder.TryGetValue(left, out long leftValue) ? leftValue : 0;
            long rightOrder = lastChangeOrder.TryGetValue(right, out long rightValue) ? rightValue : 0;
            int changeComparison = rightOrder.CompareTo(leftOrder);
            return changeComparison != 0 ? changeComparison : left.CompareTo(right);
        }

        private void RecordChange(StatusChangeResult result)
        {
            changeHistory.Add(result);
            if (changeHistory.Count > MaximumChangeHistoryCount)
            {
                changeHistory.RemoveAt(0);
            }
        }

        private void ResolveInitialData()
        {
            if (catData != null)
            {
                return;
            }

            DialogueManager dialogueManager = GetComponent<DialogueManager>() ?? DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            if (dialogueManager != null)
            {
                catData = dialogueManager.catData;
            }
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
    }
}
