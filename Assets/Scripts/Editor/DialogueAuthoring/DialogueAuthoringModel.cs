using System;
using System.Collections.Generic;

namespace Nekolpos.EditorTools.Authoring
{
    public enum DiagnosticSeverity { Info, Warning, Error }
    public enum AuthoringSpeech { Random, Sequence, RepeatEvent, Call, Return }
    public enum SequenceProgressPolicy { Resettable, HoldResetAnchor }
    public enum TerminalKind { End, Choice, Action, Event, Unknown }
    public enum ReferenceKind { Segment, Action, TimedEvent, Diary, Topic, LegacyEditorIdentity }
    public enum ConditionScope { RepresentativeSelection, LegacyLine, PostActionLine }

    [Serializable] public sealed class AuthoringDiagnostic
    {
        public DiagnosticSeverity severity;
        public string code, message, stableId, column;
        public string csvId, regexId, pattern, order, responseType, currentValue, textPreview, dialogueId, segmentId;
        public int sourceRow;
    }
    public enum ActionResolutionKind { Registry, RuntimeResolver, Dedicated, Unresolved }
    [Serializable] public sealed class AuthoringActionResolution
    {
        public string actionId, resolver;
        public ActionResolutionKind kind;
        public int rowCount, dialogueCount;
        public List<int> sourceRows = new();
    }
    [Serializable] public sealed class LegacyCell { public string column, value; }
    [Serializable] public sealed class LegacyRow
    {
        public int ordinal, sourceRow;
        public List<LegacyCell> cells = new();
        public string Read(params string[] names)
        {
            foreach (string name in names)
                foreach (var cell in cells)
                    if (string.Equals(cell.column, name, StringComparison.OrdinalIgnoreCase)) return cell.value ?? "";
            return "";
        }
    }
    [Serializable] public sealed class LocalizedValue { public string locale, value; }
    [Serializable] public sealed class AuthoringReference
    {
        public ReferenceKind kind;
        public string legacyKey, stableTargetId;
        public bool resolved;
    }
    [Serializable] public sealed class AuthoringCondition
    {
        public string expression, evaluator = "ConversationGameStateConditionEvaluator";
        public ConditionScope scope;
        public bool unconditional;
    }
    [Serializable] public sealed class AuthoringMatcher
    {
        public string legacyRegex, normalizedJapaneseRegex;
        public int priority, firstSourceRow;
        public List<LocalizedValue> expressions = new();
    }
    [Serializable] public sealed class AuthoringPresentation
    {
        public float waitTime;
        public bool hideUi;
        public string animation, reactionIcon;
        public bool normalRuntimeAppliesWaitAndHide;
    }
    [Serializable] public sealed class AuthoringChoice
    {
        public List<LocalizedValue> question = new(), yesLabel = new(), noLabel = new();
        public AuthoringReference yesTarget, noTarget;
        public string yesOriginColumn, noOriginColumn;
        public string legacyRuntimeQuestionJa, legacyRuntimeYesLabel, legacyRuntimeNoLabel;
        public bool directSegmentPlayback = true, reevaluateSelectionCondition;
    }
    [Serializable] public sealed class AuthoringTerminal
    {
        public TerminalKind kind;
        public string legacyResponseType;
        public AuthoringChoice choice;
        public AuthoringReference action, timedEvent, continuation;
        public bool externalCommandOnlyOnExecutionPath = true;
    }
    [Serializable] public sealed class AuthoringLine
    {
        public string stableId;
        public int order;
        public string legacyRuntimeJapaneseText;
        public List<LocalizedValue> texts = new();
        public AuthoringPresentation presentation;
        public AuthoringCondition condition;
        public AuthoringSpeech speech;
        public AuthoringReference callTarget;
        public AuthoringTerminal legacyLineTerminal;
        public LegacyRow legacy;
    }
    [Serializable] public sealed class AuthoringSegment
    {
        public string stableId, legacyPattern;
        public List<string> legacyEditorIdentityIds = new();
        public List<AuthoringLine> lines = new();
        public AuthoringCondition selectionCondition;
        public AuthoringSpeech representativeSpeech;
        public SequenceProgressPolicy progressPolicy;
        public bool anyLineCallOnly, eligibleForNormalSelection, repeatCallOnlyException;
        public int repeatThreshold, randomRepeatLimit;
        public string emotionChangeType;
        public int emotionChangeValue;
        public AuthoringTerminal terminal;
        public List<AuthoringReference> externalRefs = new();
    }
    [Serializable] public sealed class LegacyHybridPolicy
    {
        public string kind = "LegacyHybridPolicy";
        public List<string> repeatOverrides = new(), randomCandidates = new(), sequenceCandidates = new(), patternOrder = new();
        public int defaultRandomRepeatLimit = 3, staleGameDays = 3;
        public bool repeatBeforeOrdinarySelection = true, repeatFirstInputExcluded = true;
        public bool preferUnspokenRandom = true, avoidImmediateRandomRepeatAfterExhaustion = true;
        public bool numericPatternOrderOtherwiseFirstAppearance = true;
        public bool sequenceCursorUsesConditionFilteredCandidates = true;
        public bool resetAnchorUpdatedOnlyWhenHoldSegmentSelected = true;
        public bool holdIsSavePersistence, conversationProgressIsSaved;
        public string resetTrigger = "NextSelectionWhenCurrentDayMinusLastSpokenDayAtLeast3";
        public string tailReplay = "ExplicitHoldAnchorElsePreviousNonExternalSegmentElseLast";
        public string conditionPolicy = "RepresentativeThenPreferPlayerDefeatCondition";
        public string postActionConditionPolicy = "PerLineFilterBeforePlayback";
    }
    [Serializable] public sealed class DialogueAuthoringRoot
    {
        public string stableId, legacyLabel, normalizedMeaningKey;
        public int legacyLabelHash;
        public List<string> legacyCsvIds = new();
        public List<AuthoringMatcher> matchers = new();
        public List<AuthoringSegment> segments = new();
        public LegacyHybridPolicy selectionPolicy = new();
    }
    [Serializable] public sealed class DialogueAuthoringProject
    {
        public int schemaVersion = 1;
        public string source, sourceSha256, originalCsv;
        public List<string> headers = new();
        public List<DialogueAuthoringRoot> dialogues = new();
        public List<AuthoringDiagnostic> diagnostics = new();
        public List<AuthoringActionResolution> actionResolutions = new();
    }
}
