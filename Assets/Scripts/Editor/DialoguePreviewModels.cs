using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nekolpos.EditorTools
{
    [Serializable]
    public enum DialoguePreviewTimeZone
    {
        Morning,
        Day,
        Evening,
        Night
    }

    [Serializable]
    public enum StateLevel
    {
        Low,
        High,
        Max
    }

    [Serializable]
    public class DialogueExtraField
    {
        public string Key;
        public string Value;

        public DialogueExtraField()
        {
        }

        public DialogueExtraField(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    [Serializable]
    public class DialogueEntry
    {
        private const string PreservedRegexSourceKey = "__source_regex";
        private const string PreservedRegexChineseSourceKey = "__source_regex_cn";
        private const string PreservedRegexEnglishSourceKey = "__source_regex_en";

        public int SourceLineNumber;
        public string InternalId;
        public string RegexPattern;
        public string RegexPatternChinese;
        public string RegexPatternEnglish;
        public string RegexId;
        public string Pattern;
        public int Order;
        public string Text;
        public string TextChinese;
        public string TextEnglish;
        public string ResponseType;
        public string ActionId;
        public string TimedEventKey;
        public string Condition;
        public string EmotionChangeType;
        public int EmotionChangeValue;
        public float WaitTime;
        public bool HideUI;
        public string Animation;
        public int Priority;
        public string Intent;
        public string ReactionType;
        public int RepeatCount;
        public int RandomRepeatLimit = -1;
        public string SpeechControl;
        public string TargetPattern;
        public bool CallOnly;
        public string ChoiceYesPattern;
        public string ChoiceNoPattern;
        public bool SequenceProgressHold;
        public List<DialogueExtraField> AdditionalFields = new List<DialogueExtraField>();

        public string GetValue(string header)
        {
            switch (NormalizeHeader(header))
            {
                case "id":
                    return InternalId;
                case "regex":
                case "inputregex":
                case "matchregex":
                case "testregex":
                case "regexjp":
                case "regexja":
                    return RegexPattern;
                case "regexcn":
                case "regexzh":
                case "regexzhcn":
                    return RegexPatternChinese;
                case "regexen":
                case "regexenglish":
                    return RegexPatternEnglish;
                case "regexid":
                    return RegexId;
                case "pattern":
                case "patternid":
                    return Pattern;
                case "order":
                    return Order.ToString(CultureInfo.InvariantCulture);
                case "text":
                case "textjp":
                case "textjp1":
                case "textja":
                case "outputja":
                case "line":
                case "jp":
                case "ja":
                    return Text;
                case "textcn":
                case "textzh":
                case "textzhcn":
                case "outputzh":
                case "cn":
                case "zh":
                    return TextChinese;
                case "texten":
                case "textenglish":
                case "outputen":
                case "en":
                    return TextEnglish;
                case "responsetype":
                    return ResponseType;
                case "actionid":
                case "nextaction":
                case "反応リンク":
                    return ActionId;
                case "timedeventkey":
                case "systemtimedeventkey":
                    return TimedEventKey;
                case "condition":
                case "tag":
                case "tags":
                case "条件":
                    return Condition;
                case "emotionchangetype":
                    return EmotionChangeType;
                case "emotionchangevalue":
                    return EmotionChangeValue.ToString(CultureInfo.InvariantCulture);
                case "waittime":
                    return WaitTime.ToString(CultureInfo.InvariantCulture);
                case "hideui":
                    return HideUI ? "true" : "false";
                case "animation":
                    return Animation;
                case "priority":
                    return Priority.ToString(CultureInfo.InvariantCulture);
                case "intent":
                    return Intent;
                case "reactiontype":
                    return ReactionType;
                case "repeatcount":
                    return RepeatCount.ToString(CultureInfo.InvariantCulture);
                case "randomrepeatlimit":
                case "randomlimit":
                    return RandomRepeatLimit >= 0 ? RandomRepeatLimit.ToString(CultureInfo.InvariantCulture) : string.Empty;
                case "speechcontrol":
                    return SpeechControl;
                case "targetpattern":
                    return TargetPattern;
                case "callonly":
                    return CallOnly ? "true" : "false";
                case "patternaccess":
                    return CallOnly ? "CallOnly" : "Normal";
                case "choiceyespattern":
                case "choiceyes":
                case "yespattern":
                    return ChoiceYesPattern;
                case "choicenopattern":
                case "choiceno":
                case "nopattern":
                    return ChoiceNoPattern;
                case "sequenceprogresshold":
                case "progresshold":
                case "進行保持":
                    return SequenceProgressHold ? "true" : "false";
                default:
                    return GetAdditionalValue(header);
            }
        }

        public void PreserveRegexSourceValues(string japanese, string chinese, string english)
        {
            SetAdditionalValue(PreservedRegexSourceKey, japanese ?? string.Empty);
            SetAdditionalValue(PreservedRegexChineseSourceKey, chinese ?? string.Empty);
            SetAdditionalValue(PreservedRegexEnglishSourceKey, english ?? string.Empty);
        }

        public string GetPreservedRegexSource()
        {
            return GetAdditionalValue(PreservedRegexSourceKey);
        }

        public string GetPreservedRegexChineseSource()
        {
            return GetAdditionalValue(PreservedRegexChineseSourceKey);
        }

        public string GetPreservedRegexEnglishSource()
        {
            return GetAdditionalValue(PreservedRegexEnglishSourceKey);
        }

        public void SetAdditionalValue(string key, string value)
        {
            for (int i = 0; i < AdditionalFields.Count; i++)
            {
                if (string.Equals(AdditionalFields[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    AdditionalFields[i].Value = value;
                    return;
                }
            }

            AdditionalFields.Add(new DialogueExtraField(key, value));
        }

        public string GetAdditionalValue(string key)
        {
            for (int i = 0; i < AdditionalFields.Count; i++)
            {
                if (string.Equals(AdditionalFields[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return AdditionalFields[i].Value;
                }
            }

            return string.Empty;
        }

        private string GetPreservedRegexSourceValue(string key, string fallback)
        {
            for (int i = 0; i < AdditionalFields.Count; i++)
            {
                if (string.Equals(AdditionalFields[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    string preservedValue = AdditionalFields[i].Value ?? string.Empty;
                    return string.IsNullOrWhiteSpace(preservedValue) ? (fallback ?? string.Empty) : preservedValue;
                }
            }

            return fallback;
        }

        public static string NormalizeHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            return header
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Trim('\uFEFF', '\u200B')
                .ToLowerInvariant();
        }
    }

    [Serializable]
    public class DialoguePatternGroup
    {
        public string RegexId;
        public string Pattern;
        public List<DialogueEntry> Entries = new List<DialogueEntry>();
        public int FirstEntryIndex;

        public string BuildDisplayName()
        {
            string regexLabel = string.IsNullOrWhiteSpace(RegexId) ? "regex:?" : $"regex:{RegexId}";
            string patternLabel = string.IsNullOrWhiteSpace(Pattern) ? "pattern:?" : $"pattern:{Pattern}";
            return $"{regexLabel} / {patternLabel}";
        }

        public DialogueEntry GetRepresentativeEntry()
        {
            return Entries.Count > 0 ? Entries[0] : null;
        }

        public string GetSharedResponseType()
        {
            return GetRepresentativeEntry()?.ResponseType ?? string.Empty;
        }

        public string GetSharedActionId()
        {
            return GetRepresentativeEntry()?.ActionId ?? string.Empty;
        }

        public string GetSharedCondition()
        {
            return GetRepresentativeEntry()?.Condition ?? string.Empty;
        }
    }

    [Serializable]
    public class DialogueState
    {
        public StateLevel affectionLevel = StateLevel.Low;
        public StateLevel sadisticLevel = StateLevel.Low;
        public StateLevel concernLevel = StateLevel.Low;
        public StateLevel hostilityLevel = StateLevel.Low;
        public StateLevel obedienceLevel = StateLevel.Low;
        public StateLevel instinctLevel = StateLevel.Low;
        public DialoguePreviewTimeZone timeZone = DialoguePreviewTimeZone.Day;
        public bool playerDefeatedByNekomata;
        public bool playerDefeatedByPredation;
        public bool playerDefeatedByAnger;
        public bool playerDefeatedByAccident;
        public string lastPlayerDefeatReason = string.Empty;

        public int AffectionValue => ToNumericValue(affectionLevel);

        public int SadisticValue => ToNumericValue(sadisticLevel);

        public int ConcernValue => ToNumericValue(concernLevel);

        public int HostilityValue => ToNumericValue(hostilityLevel);

        public int ObedienceValue => ToNumericValue(obedienceLevel);

        public int InstinctValue => ToNumericValue(instinctLevel);

        public int GetMetricValue(string metricKey)
        {
            switch (DialogueEntry.NormalizeHeader(metricKey))
            {
                case "affection":
                case "愛情":
                    return AffectionValue;
                case "sadistic":
                case "s":
                case "ドs":
                    return SadisticValue;
                case "concern":
                case "care":
                case "worry":
                case "心配":
                    return ConcernValue;
                case "hostility":
                case "enemy":
                case "敵対":
                    return HostilityValue;
                case "obedience":
                case "submissive":
                case "従順":
                    return ObedienceValue;
                case "instinct":
                case "本能":
                    return InstinctValue;
                default:
                    return 0;
            }
        }

        private static int ToNumericValue(StateLevel level)
        {
            switch (level)
            {
                case StateLevel.Max:
                    return 100;
                case StateLevel.High:
                    return 60;
                default:
                    return 0;
            }
        }
    }

    [Serializable]
    public class DialogueTimelineEvent
    {
        public float Time;
        public string Description;

        public DialogueTimelineEvent()
        {
        }

        public DialogueTimelineEvent(float time, string description)
        {
            Time = time;
            Description = description;
        }
    }
}
