using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nekolpos.Data;
using UnityEngine;

namespace Nekolpos.System
{
    public enum DialogueRuntimeSourceMode
    {
        LegacyCsv = 0,
        CompiledBundle = 1
    }

    [Serializable]
    public sealed class DialogueCompiledRuntimeBundle
    {
        public const int CurrentFormatVersion = 1;
        public int formatVersion = CurrentFormatVersion;
        public string source;
        public string sourceSha256;
        public bool publishable;
        public List<DialogueCompiledDiagnostic> diagnostics = new List<DialogueCompiledDiagnostic>();
        public List<DialogueCompiledPattern> patterns = new List<DialogueCompiledPattern>();
        public List<DialogueCompiledReaction> reactions = new List<DialogueCompiledReaction>();
    }

    [Serializable]
    public sealed class DialogueCompiledDiagnostic
    {
        public string severity;
        public string code;
        public int count;
    }

    [Serializable]
    public sealed class DialogueCompiledPattern
    {
        public string stableRootId;
        public int sourceRow;
        public string sourceRegex;
        public int labelHash;
        public int priority;
        public string sensitivity;
        public TeachingCategory teachingCategory;
        public string teachingEntryId;
        public string teachingSeriesGroupId;
        public string teachingDisplayName;
        public bool teachingPromptDisabled;
        public bool teachingRevisitDisabled;
    }

    [Serializable]
    public sealed class DialogueCompiledReaction
    {
        public string stableRootId;
        public string stableSegmentId;
        public string stableLineId;
        public int sourceRow;
        public int labelHash;
        public string patternId;
        public string diarySourceCsv;
        public string diarySourceEntryId;
        public string diarySourceRegexId;
        public SpeechControlType speechControl;
        public int order;
        public int repeatCount;
        public int randomRepeatLimit = -1;
        public string targetPatternId;
        public bool callOnly;
        public string choiceYesPatternId;
        public string choiceNoPatternId;
        public bool sequenceProgressHold;
        public string intentId;
        public bool intentColumnPresent;
        public string reactionType;
        public bool reactionTypeColumnPresent;
        public string topic;
        public string sourceRegexLabel;
        public string choiceQuestionJa;
        public string choiceYesLabel;
        public string choiceNoLabel;
        public string actionId;
        public string timedEventKey;
        public string speaker;
        public string nextAction;
        public bool nextActionColumnPresent;
        public string textJp;
        public string responseType;
        public string condition;
        public float waitTime;
        public bool hideUi;
        public string animation;
        public ReactionIcon reactionIcon;
        public string emotionChangeType;
        public int emotionChangeValue;
        public string branchResolver;
        public string teachingCategory;
        public string teachingEntryId;
        public string teachingSeriesGroupId;
        public string teachingDisplayName;
        public List<DialogueLogQuestionLink> questionLinks = new List<DialogueLogQuestionLink>();
    }

    public static class DialogueCompiledRuntimeLoader
    {
        public const string ResourcePath = "TalkData/DialoguePreviewRuntime";

        public static bool TryParse(
            string json,
            string legacyCsv,
            string expectedSource,
            out DialogueCompiledRuntimeBundle metadata,
            out List<RegexPatternData> patterns,
            out Dictionary<int, List<DialogueReactionData>> reactions,
            out string error)
        {
            metadata = null;
            patterns = null;
            reactions = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Compiled dialogue bundle is empty.";
                return false;
            }

            if (legacyCsv == null)
            {
                error = "Legacy dialogue source is unavailable for hash validation.";
                return false;
            }

            try
            {
                metadata = JsonUtility.FromJson<DialogueCompiledRuntimeBundle>(json.TrimStart('\uFEFF', '\u200B'));
            }
            catch (Exception exception)
            {
                error = "Compiled dialogue bundle JSON is invalid: " + exception.Message;
                return false;
            }

            if (metadata == null)
            {
                error = "Compiled dialogue bundle JSON produced no document.";
                return false;
            }

            if (metadata.formatVersion != DialogueCompiledRuntimeBundle.CurrentFormatVersion)
            {
                error = $"Unsupported compiled dialogue bundle version: {metadata.formatVersion}.";
                return false;
            }

            string sourceName = NormalizeSourceName(metadata.source);
            if (!string.IsNullOrWhiteSpace(expectedSource) &&
                !string.Equals(sourceName, NormalizeSourceName(expectedSource), StringComparison.OrdinalIgnoreCase))
            {
                error = $"Compiled dialogue source mismatch: expected {expectedSource}, actual {metadata.source}.";
                return false;
            }

            string actualHash = ComputeSha256(legacyCsv);
            if (string.IsNullOrWhiteSpace(metadata.sourceSha256) ||
                !string.Equals(metadata.sourceSha256, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "Compiled dialogue bundle is stale: source SHA-256 does not match the decrypted legacy CSV.";
                return false;
            }

            if (metadata.patterns == null || metadata.patterns.Count == 0 ||
                metadata.reactions == null || metadata.reactions.Count == 0)
            {
                error = "Compiled dialogue bundle has no patterns or reactions.";
                return false;
            }

            var lineIds = new HashSet<string>(StringComparer.Ordinal);
            var validLabels = new HashSet<int>();
            patterns = new List<RegexPatternData>(metadata.patterns.Count);
            reactions = new Dictionary<int, List<DialogueReactionData>>();

            for (int i = 0; i < metadata.patterns.Count; i++)
            {
                DialogueCompiledPattern item = metadata.patterns[i];
                if (item == null || string.IsNullOrWhiteSpace(item.stableRootId) || string.IsNullOrWhiteSpace(item.sourceRegex))
                {
                    return Fail($"Compiled dialogue pattern {i} is incomplete.", out patterns, out reactions, out error);
                }

                validLabels.Add(item.labelHash);
                patterns.Add(new RegexPatternData
                {
                    SourceRegex = item.sourceRegex,
                    LabelHash = item.labelHash,
                    Priority = item.priority,
                    Sensitivity = string.IsNullOrWhiteSpace(item.sensitivity) ? "SAFE" : item.sensitivity,
                    TeachingCategory = item.teachingCategory,
                    TeachingEntryId = item.teachingEntryId,
                    TeachingSeriesGroupId = item.teachingSeriesGroupId,
                    TeachingDisplayName = item.teachingDisplayName,
                    TeachingPromptDisabled = item.teachingPromptDisabled,
                    TeachingRevisitDisabled = item.teachingRevisitDisabled
                });
            }

            for (int i = 0; i < metadata.reactions.Count; i++)
            {
                DialogueCompiledReaction item = metadata.reactions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.stableRootId) ||
                    string.IsNullOrWhiteSpace(item.stableSegmentId) || string.IsNullOrWhiteSpace(item.stableLineId))
                {
                    return Fail($"Compiled dialogue reaction {i} is incomplete.", out patterns, out reactions, out error);
                }

                if (!lineIds.Add(item.stableLineId))
                {
                    return Fail("Compiled dialogue bundle contains duplicate line ID: " + item.stableLineId, out patterns, out reactions, out error);
                }

                if (!validLabels.Contains(item.labelHash))
                {
                    return Fail($"Compiled dialogue reaction {item.stableLineId} references an unknown label hash.", out patterns, out reactions, out error);
                }

                var reaction = new DialogueReactionData
                {
                    LabelHash = item.labelHash,
                    PatternID = item.patternId,
                    DiarySourceCsv = item.diarySourceCsv,
                    DiarySourceEntryId = item.diarySourceEntryId,
                    DiarySourceRegexId = item.diarySourceRegexId,
                    SpeechControl = item.speechControl,
                    Order = item.order,
                    RepeatCount = item.repeatCount,
                    RandomRepeatLimit = item.randomRepeatLimit,
                    TargetPatternID = item.targetPatternId,
                    CallOnly = item.callOnly,
                    ChoiceYesPatternID = item.choiceYesPatternId,
                    ChoiceNoPatternID = item.choiceNoPatternId,
                    SequenceProgressHold = item.sequenceProgressHold,
                    IntentID = item.intentColumnPresent ? item.intentId : null,
                    ReactionType = item.reactionTypeColumnPresent ? item.reactionType : null,
                    SourceRegexLabel = item.sourceRegexLabel,
                    ChoiceQuestionJa = item.choiceQuestionJa,
                    ChoiceYesLabel = item.choiceYesLabel,
                    ChoiceNoLabel = item.choiceNoLabel,
                    ActionId = item.actionId,
                    TimedEventKey = item.timedEventKey,
                    NextAction = item.nextActionColumnPresent ? item.nextAction : null,
                    TextJP = item.textJp,
                    ResponseType = item.responseType,
                    Condition = item.condition,
                    WaitTime = item.waitTime,
                    HideUI = item.hideUi,
                    Animation = item.animation,
                    ReactionIcon = item.reactionIcon,
                    EmotionChangeType = item.emotionChangeType,
                    EmotionChangeValue = item.emotionChangeValue,
                    TeachingCategory = item.teachingCategory,
                    TeachingEntryId = item.teachingEntryId,
                    TeachingSeriesGroupId = item.teachingSeriesGroupId,
                    TeachingDisplayName = item.teachingDisplayName,
                    QuestionLinks = item.questionLinks != null
                        ? new List<DialogueLogQuestionLink>(item.questionLinks)
                        : new List<DialogueLogQuestionLink>()
                };

                if (!reactions.TryGetValue(reaction.LabelHash, out List<DialogueReactionData> group))
                {
                    group = new List<DialogueReactionData>();
                    reactions.Add(reaction.LabelHash, group);
                }
                group.Add(reaction);
            }

            return true;
        }

        private static bool Fail(
            string message,
            out List<RegexPatternData> patterns,
            out Dictionary<int, List<DialogueReactionData>> reactions,
            out string error)
        {
            patterns = null;
            reactions = null;
            error = message;
            return false;
        }

        public static string ComputeSha256(string text)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty)).Select(value => value.ToString("x2")));
        }

        private static string NormalizeSourceName(string value)
        {
            string normalized = (value ?? string.Empty).Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            if (slash >= 0) normalized = normalized.Substring(slash + 1);
            int dot = normalized.LastIndexOf('.');
            return dot > 0 ? normalized.Substring(0, dot) : normalized;
        }
    }

    public static class DialogueRuntimeSourceSelection
    {
        public const string PlayerPrefsKey = "Nekolpos.Dialogue.RuntimeSource.v1";
        public const string CommandLineSwitch = "-dialogue-runtime-bundle";

        public static DialogueRuntimeSourceMode RequestedMode
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string[] arguments = Environment.GetCommandLineArgs();
                if (arguments.Any(argument => string.Equals(argument, CommandLineSwitch, StringComparison.OrdinalIgnoreCase)))
                    return DialogueRuntimeSourceMode.CompiledBundle;
                return WorkerPlayerPrefs.GetInt(PlayerPrefsKey, 0) == 1
                    ? DialogueRuntimeSourceMode.CompiledBundle
                    : DialogueRuntimeSourceMode.LegacyCsv;
#else
                return DialogueRuntimeSourceMode.LegacyCsv;
#endif
            }
        }

        public static void SetDevelopmentMode(DialogueRuntimeSourceMode mode)
        {
            if (DebugSaveSession.IsIsolated) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WorkerPlayerPrefs.SetInt(PlayerPrefsKey, mode == DialogueRuntimeSourceMode.CompiledBundle ? 1 : 0);
            WorkerPlayerPrefs.Save();
#endif
        }
    }
}
