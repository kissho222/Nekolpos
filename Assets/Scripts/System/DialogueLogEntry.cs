using System;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Nekolpos.System
{
    [Serializable]
    public class DialogueLogEntry
    {
        // "pending" is retained for compatibility with logs created before consent was explicit.
        public const string UploadStatusPending = "pending";
        public const string UploadStatusReview = "review";
        public const string UploadStatusQueued = "queued";
        public const string UploadStatusUploaded = "uploaded";
        public const string UploadStatusFailed = "failed";
        public const string UploadStatusExcluded = "excluded";

        [JsonProperty("log_id")]
        public string logId;

        [JsonProperty("timestamp")]
        public string timestamp;

        [JsonProperty("language", NullValueHandling = NullValueHandling.Ignore)]
        public string language;

        [JsonProperty("speaker")]
        public string speaker;

        [JsonProperty("speaker_display_name", NullValueHandling = NullValueHandling.Ignore)]
        public string speakerDisplayName;

        [JsonProperty("source")]
        public string source;

        [JsonProperty("text")]
        public string text;

        [JsonProperty("intent", NullValueHandling = NullValueHandling.Ignore)]
        public string intent;

        [JsonProperty("nodeId", NullValueHandling = NullValueHandling.Ignore)]
        public string nodeId;

        [JsonProperty("raw_input", NullValueHandling = NullValueHandling.Ignore)]
        public string rawInput;

        [JsonProperty("normalized_input", NullValueHandling = NullValueHandling.Ignore)]
        public string normalizedInput;

        [JsonProperty("matched_regex", NullValueHandling = NullValueHandling.Ignore)]
        public string matchedRegex;

        [JsonProperty("intent_candidates", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> intentCandidates = new List<string>();

        [JsonProperty("selected_intent", NullValueHandling = NullValueHandling.Ignore)]
        public string selectedIntent;

        [JsonProperty("selected_response_id", NullValueHandling = NullValueHandling.Ignore)]
        public string selectedResponseId;

        [JsonProperty("unknown_words", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> unknownWords = new List<string>();

        [JsonProperty("reaction_result", NullValueHandling = NullValueHandling.Ignore)]
        public string reactionResult;

        [JsonProperty("trigger_type", NullValueHandling = NullValueHandling.Ignore)]
        public string triggerType;

        [JsonProperty("matched_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public string matchedPattern;

        [JsonProperty("food_name", NullValueHandling = NullValueHandling.Ignore)]
        public string foodName;

        [JsonProperty("player_choice", NullValueHandling = NullValueHandling.Ignore)]
        public string playerChoice;

        [JsonProperty("talk_topic_hint_used")]
        public bool talkTopicHintUsed;

        [JsonProperty("game_state_snapshot", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> gameStateSnapshot;

        [JsonProperty("upload_status")]
        public string uploadStatus;

        // UI-only metadata. It must never be included in uploaded conversation logs.
        [JsonIgnore]
        public List<DialogueLogQuestionLink> questionLinks = new List<DialogueLogQuestionLink>();

        [JsonIgnore]
        public string LogId
        {
            get => logId;
            set => logId = value;
        }

        [JsonIgnore]
        public string Timestamp
        {
            get => timestamp;
            set => timestamp = value;
        }

        [JsonIgnore]
        public string Language
        {
            get => language;
            set => language = value;
        }

        [JsonIgnore]
        public string Speaker
        {
            get => speaker;
            set => speaker = value;
        }

        [JsonIgnore]
        public string SpeakerDisplayName
        {
            get => speakerDisplayName;
            set => speakerDisplayName = value;
        }

        [JsonIgnore]
        public string Source
        {
            get => source;
            set => source = value;
        }

        [JsonIgnore]
        public string Text
        {
            get => text;
            set => text = value;
        }

        [JsonIgnore]
        public string Intent
        {
            get => intent;
            set => intent = value;
        }

        [JsonIgnore]
        public string NodeId
        {
            get => nodeId;
            set => nodeId = value;
        }

        [JsonIgnore]
        public string RawInput
        {
            get => rawInput;
            set => rawInput = value;
        }

        [JsonIgnore]
        public string NormalizedInput
        {
            get => normalizedInput;
            set => normalizedInput = value;
        }

        [JsonIgnore]
        public string MatchedRegex
        {
            get => matchedRegex;
            set => matchedRegex = value;
        }

        [JsonIgnore]
        public List<string> IntentCandidates
        {
            get => intentCandidates;
            set => intentCandidates = value ?? new List<string>();
        }

        [JsonIgnore]
        public string SelectedIntent
        {
            get => selectedIntent;
            set => selectedIntent = value;
        }

        [JsonIgnore]
        public string SelectedResponseId
        {
            get => selectedResponseId;
            set => selectedResponseId = value;
        }

        [JsonIgnore]
        public List<string> UnknownWords
        {
            get => unknownWords;
            set => unknownWords = value ?? new List<string>();
        }

        [JsonIgnore]
        public string ReactionResult
        {
            get => reactionResult;
            set => reactionResult = value;
        }

        [JsonIgnore]
        public string TriggerType
        {
            get => triggerType;
            set => triggerType = value;
        }

        [JsonIgnore]
        public string MatchedPattern
        {
            get => matchedPattern;
            set => matchedPattern = value;
        }

        [JsonIgnore]
        public string FoodName
        {
            get => foodName;
            set => foodName = value;
        }

        [JsonIgnore]
        public string PlayerChoice
        {
            get => playerChoice;
            set => playerChoice = value;
        }

        [JsonIgnore]
        public bool TalkTopicHintUsed
        {
            get => talkTopicHintUsed;
            set => talkTopicHintUsed = value;
        }

        [JsonIgnore]
        public Dictionary<string, object> GameStateSnapshot
        {
            get => gameStateSnapshot;
            set => gameStateSnapshot = value;
        }

        [JsonIgnore]
        public string UploadStatus
        {
            get => uploadStatus;
            set => uploadStatus = value;
        }

        [JsonIgnore]
        public List<DialogueLogQuestionLink> QuestionLinks
        {
            get => questionLinks;
            set => questionLinks = value ?? new List<DialogueLogQuestionLink>();
        }

        public static string CreateTimestamp()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        public static string CreateLogId()
        {
            string randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{randomSuffix}";
        }

        public DialogueLogEntry Clone()
        {
            return new DialogueLogEntry
            {
                logId = logId,
                timestamp = timestamp,
                language = language,
                speaker = speaker,
                speakerDisplayName = speakerDisplayName,
                source = source,
                text = text,
                intent = intent,
                nodeId = nodeId,
                rawInput = rawInput,
                normalizedInput = normalizedInput,
                matchedRegex = matchedRegex,
                intentCandidates = intentCandidates != null ? new List<string>(intentCandidates) : new List<string>(),
                selectedIntent = selectedIntent,
                selectedResponseId = selectedResponseId,
                unknownWords = unknownWords != null ? new List<string>(unknownWords) : new List<string>(),
                reactionResult = reactionResult,
                triggerType = triggerType,
                matchedPattern = matchedPattern,
                foodName = foodName,
                playerChoice = playerChoice,
                talkTopicHintUsed = talkTopicHintUsed,
                gameStateSnapshot = CloneSnapshot(gameStateSnapshot),
                uploadStatus = uploadStatus,
                questionLinks = CloneQuestionLinks(questionLinks)
            };
        }

        private static List<DialogueLogQuestionLink> CloneQuestionLinks(
            IReadOnlyList<DialogueLogQuestionLink> source)
        {
            var result = new List<DialogueLogQuestionLink>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    result.Add(source[i].Clone());
                }
            }

            return result;
        }

        private static Dictionary<string, object> CloneSnapshot(Dictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            string json = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        }
    }
}
