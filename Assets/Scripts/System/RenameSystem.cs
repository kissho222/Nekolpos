using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Backgammon.Conversation;
using Nekolpos.Data;
using UnityEngine;

namespace Nekolpos.System
{
    public enum PendingRenameType
    {
        None,
        CatName,
        PlayerCalling,
        CatPronoun
    }

    public static class RenameSystem
    {
        public const string PendingRenameTypeStateKey = "PendingRenameType";
        public const string PendingRenameValueStateKey = "PendingRenameValue";
        public const string PendingValuePlaceholderKey = "PENDING_VALUE";

        public const string RenameCatConfirmKey = "SYS_RENAME_CAT_CONFIRM";
        public const string RenameCallingConfirmKey = "SYS_RENAME_CALLING_CONFIRM";
        public const string RenamePronounConfirmKey = "SYS_RENAME_PRONOUN_CONFIRM";
        public const string RenameCatAcceptKey = "SYS_RENAME_CAT_ACCEPT";
        public const string RenameCallingAcceptKey = "SYS_RENAME_CALLING_ACCEPT";
        public const string RenamePronounAcceptKey = "SYS_RENAME_PRONOUN_ACCEPT";
        public const string RenameCatCancelKey = "SYS_RENAME_CAT_CANCEL";
        public const string RenameCallingCancelKey = "SYS_RENAME_CALLING_CANCEL";
        public const string RenamePronounCancelKey = "SYS_RENAME_PRONOUN_CANCEL";
        public const string RenameInvalidKey = "SYS_RENAME_INVALID";

        public const string SaveKeyPlayerName = "SaveData.PLAYER_NAME";
        public const string SaveKeyCatName = "SaveData.CAT_NAME";
        public const string SaveKeyPlayerCalling = "SaveData.PLAYER_CALLING";
        public const string SaveKeyCatPronoun = "SaveData.CAT_PRONOUN";
        private const string LegacyOpenBetaPlayerNameKey = "OpenBeta.PlayerName";
        private const string LegacyOpenBetaCatNameKey = "OpenBeta.CatName";
        private const string LegacyOpenBetaPlayerCallingKey = "OpenBeta.PlayerCalling";

        private static readonly RenamePattern[] RenamePatterns =
        {
            new RenamePattern(
                new Regex(@"きょうから(あなた|きみ|おまえ)は(.{1,12}?)(だよ|です)", RegexOptions.Compiled),
                PendingRenameType.CatName,
                2),
            new RenamePattern(
                new Regex(@"(.{1,12}?)にかいめい", RegexOptions.Compiled),
                PendingRenameType.CatName,
                1),
            new RenamePattern(
                new Regex(@"わたしを(.{1,12}?)ってよんで", RegexOptions.Compiled),
                PendingRenameType.PlayerCalling,
                1),
            new RenamePattern(
                new Regex(@"^(.{1,12}?)ってなのって$", RegexOptions.Compiled),
                PendingRenameType.CatPronoun,
                1)
        };

        private static readonly Regex ReservedVariableRegex =
            new Regex(@"^\s*(CAT_NAME|PLAYER_CALLING|CAT_PRONOUN|PENDING_VALUE|PLAYER_NAME|CAT_GENDER|PARENT)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly List<string> VulgarWords = new List<string>();
        private static bool vulgarWordsLoaded;

        public static bool TryCreateRenameRequestReaction(string input, out DialogueReactionData reaction)
        {
            return TryCreateRenameRequestReaction(new PlayerInputContext(input), out reaction);
        }

        public static bool TryCreateRenameRequestReaction(PlayerInputContext inputContext, out DialogueReactionData reaction)
        {
            reaction = null;
            if (!TryParseRequest(inputContext, out PendingRenameType type, out string newValue))
            {
                return false;
            }

            if (!IsValidValue(newValue))
            {
                ClearPending();
                reaction = BasicSystemDialogueCatalog.CreateReaction(RenameInvalidKey, null, "うーん……\nその名前はちょっと難しいかも。\n別の名前にしてくれる？");
                return true;
            }

            SetPending(type, newValue);
            reaction = BasicSystemDialogueCatalog.CreateReaction(GetConfirmKey(type), null, string.Empty);
            return reaction != null;
        }

        public static bool TryHandleChoiceBranch(string targetPatternId, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(targetPatternId))
            {
                return false;
            }

            if (!TryGetPending(out PendingRenameType pendingType, out string pendingValue))
            {
                return IsRenameBranchKey(targetPatternId);
            }

            if (accepted && IsAcceptKeyForType(targetPatternId, pendingType))
            {
                CatDataSO catData = ResolveCatData();
                ApplyRename(catData, pendingType, pendingValue);
                Save(catData);
                ClearPending();
                return true;
            }

            if (!accepted && IsCancelKeyForType(targetPatternId, pendingType))
            {
                ClearPending();
                return true;
            }

            return IsRenameBranchKey(targetPatternId);
        }

        public static void Load(CatDataSO catData)
        {
            if (catData == null)
            {
                return;
            }

            catData.playerName = SanitizeRenameValue(GetPersistentString(SaveKeyPlayerName, LegacyOpenBetaPlayerNameKey, catData.playerName));
            catData.catName = SanitizeRenameValue(GetPersistentString(SaveKeyCatName, LegacyOpenBetaCatNameKey, catData.catName));
            catData.playerCalling = SanitizeRenameValue(GetPersistentString(SaveKeyPlayerCalling, LegacyOpenBetaPlayerCallingKey, catData.playerCalling));
            catData.catPronoun = SanitizeRenameValue(PlayerPrefs.GetString(SaveKeyCatPronoun, catData.catPronoun));
            ApplyToState(catData);
        }

        public static void Save(CatDataSO catData)
        {
            if (catData == null)
            {
                return;
            }

            string playerName = SanitizeRenameValue(catData.playerName);
            string catName = SanitizeRenameValue(catData.catName);
            string playerCalling = SanitizeRenameValue(catData.playerCalling);
            string catPronoun = SanitizeRenameValue(catData.catPronoun);
            catData.playerName = playerName;
            catData.catName = catName;
            catData.playerCalling = playerCalling;
            catData.catPronoun = catPronoun;
            PlayerPrefs.SetString(SaveKeyPlayerName, playerName);
            PlayerPrefs.SetString(SaveKeyCatName, catName);
            PlayerPrefs.SetString(SaveKeyPlayerCalling, playerCalling);
            PlayerPrefs.SetString(SaveKeyCatPronoun, catPronoun);
            PlayerPrefs.SetString(LegacyOpenBetaPlayerNameKey, playerName);
            PlayerPrefs.SetString(LegacyOpenBetaCatNameKey, catName);
            PlayerPrefs.SetString(LegacyOpenBetaPlayerCallingKey, playerCalling);
            PlayerPrefs.Save();
            ApplyToState(catData);
        }

        public static bool UpdateAndSave(CatDataSO catData, PendingRenameType type, string value)
        {
            if (catData == null || type == PendingRenameType.None || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = SanitizeRenameValue(value);
            if (!IsValidValue(trimmed))
            {
                return false;
            }

            if (!ApplyRename(catData, type, trimmed))
            {
                return false;
            }

            Save(catData);
            return true;
        }

        public static bool TryResolvePlaceholder(string placeholderName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(placeholderName))
            {
                return false;
            }

            string key = placeholderName.Trim().Trim('{', '}').Trim().ToUpperInvariant();
            CatDataSO catData = ResolveCatData();
            switch (key)
            {
                case PendingValuePlaceholderKey:
                    if (TryGetPending(out _, out value))
                    {
                        return true;
                    }
                    return false;
                case "CAT_NAME":
                    value = catData != null ? SanitizeRenameValue(catData.catName) : string.Empty;
                    return catData != null;
                case "PLAYER_CALLING":
                    value = catData != null ? SanitizeRenameValue(catData.playerCalling) : string.Empty;
                    return catData != null;
                case "CAT_PRONOUN":
                    value = catData != null ? SanitizeRenameValue(catData.catPronoun) : string.Empty;
                    return catData != null;
                case "PLAYER_NAME":
                    value = catData != null ? SanitizeRenameValue(catData.playerName) : string.Empty;
                    return catData != null;
                case "CAT_GENDER":
                    value = catData != null ? catData.catGender ?? string.Empty : string.Empty;
                    return catData != null;
                case "PARENT":
                    value = catData != null ? catData.parentCall ?? string.Empty : string.Empty;
                    return catData != null;
                default:
                    return false;
            }
        }

        private static bool TryParseRequest(PlayerInputContext inputContext, out PendingRenameType type, out string newValue)
        {
            type = PendingRenameType.None;
            newValue = string.Empty;

            string normalized = inputContext != null ? inputContext.NormalizedInput : string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            for (int i = 0; i < RenamePatterns.Length; i++)
            {
                RenamePattern pattern = RenamePatterns[i];
                if (TryMatch(pattern, inputContext, normalized, out type, out newValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryMatch(RenamePattern pattern, PlayerInputContext inputContext, string normalizedInput, out PendingRenameType type, out string newValue)
        {
            type = PendingRenameType.None;
            newValue = string.Empty;

            Match match = pattern.Regex.Match(normalizedInput);
            if (!match.Success)
            {
                return false;
            }

            if (pattern.ValueGroupIndex <= 0 || pattern.ValueGroupIndex >= match.Groups.Count)
            {
                return false;
            }

            type = pattern.Type;
            Group valueGroup = match.Groups[pattern.ValueGroupIndex];
            string originalValue = inputContext != null
                ? inputContext.GetOriginalSubstringForNormalizedRange(valueGroup.Index, valueGroup.Length)
                : string.Empty;
            newValue = (!string.IsNullOrWhiteSpace(originalValue) ? originalValue : valueGroup.Value)
                .Trim(' ', '　', '。', '、', '！', '？', '!', '?');
            return true;
        }

        private readonly struct RenamePattern
        {
            public RenamePattern(Regex regex, PendingRenameType type, int valueGroupIndex)
            {
                Regex = regex;
                Type = type;
                ValueGroupIndex = valueGroupIndex;
            }

            public Regex Regex { get; }
            public PendingRenameType Type { get; }
            public int ValueGroupIndex { get; }
        }

        private static bool IsValidValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length < 1 || trimmed.Length > 12)
            {
                return false;
            }

            if (trimmed.Contains("{{") || trimmed.Contains("}}") || ReservedVariableRegex.IsMatch(trimmed))
            {
                return false;
            }

            if (ContainsVulgarLanguage(trimmed))
            {
                return false;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (char.IsLetterOrDigit(trimmed[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsVulgarLanguage(string value)
        {
            EnsureVulgarWordsLoaded();
            if (string.IsNullOrWhiteSpace(value) || VulgarWords.Count == 0)
            {
                return false;
            }

            string normalizedValue = InputNormalizer.NormalizeForMatching(value);
            for (int i = 0; i < VulgarWords.Count; i++)
            {
                string word = VulgarWords[i];
                if (!string.IsNullOrEmpty(word) && normalizedValue.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureVulgarWordsLoaded()
        {
            if (vulgarWordsLoaded)
            {
                return;
            }

            vulgarWordsLoaded = true;
            VulgarWords.Clear();

            Backgammon.Conversation.ConversationDataManager manager = UnityEngine.Object.FindFirstObjectByType<Backgammon.Conversation.ConversationDataManager>();
            if (manager != null)
            {
                List<string> csvTexts = manager.GetActiveVulgarCsvTexts();
                for (int i = 0; i < csvTexts.Count; i++)
                {
                    AddVulgarWordsFromCsv(csvTexts[i]);
                }
            }
        }

        private static void AddVulgarWordsFromCsv(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return;
            }

            string[] lines = csvText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string word = line.Split(',')[0].Trim(' ', '　', '\uFEFF', '\u200B', '\t');
                string normalizedWord = InputNormalizer.NormalizeForMatching(word);
                if (!string.IsNullOrEmpty(normalizedWord) && !VulgarWords.Contains(normalizedWord))
                {
                    VulgarWords.Add(normalizedWord);
                }
            }
        }

        private static void SetPending(PendingRenameType type, string value)
        {
            ConversationGameState state = ResolveState();
            if (state == null)
            {
                return;
            }

            state.SetString(PendingRenameTypeStateKey, type.ToString());
            state.SetString(PendingRenameValueStateKey, value ?? string.Empty);
            state.SetString(PendingValuePlaceholderKey, value ?? string.Empty);
        }

        private static bool TryGetPending(out PendingRenameType type, out string value)
        {
            type = PendingRenameType.None;
            value = string.Empty;

            ConversationGameState state = ResolveState();
            if (state == null ||
                !state.TryGetString(PendingRenameTypeStateKey, out string typeToken) ||
                !Enum.TryParse(typeToken, false, out type) ||
                type == PendingRenameType.None ||
                !state.TryGetString(PendingRenameValueStateKey, out value) ||
                string.IsNullOrWhiteSpace(value))
            {
                type = PendingRenameType.None;
                value = string.Empty;
                return false;
            }

            return true;
        }

        private static void ClearPending()
        {
            ConversationGameState state = ResolveState();
            if (state == null)
            {
                return;
            }

            state.Remove(PendingRenameTypeStateKey);
            state.Remove(PendingRenameValueStateKey);
            state.Remove(PendingValuePlaceholderKey);
        }

        private static bool ApplyRename(CatDataSO catData, PendingRenameType type, string value)
        {
            if (catData == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string sanitized = SanitizeRenameValue(value);
            switch (type)
            {
                case PendingRenameType.CatName:
                    catData.catName = sanitized;
                    return true;
                case PendingRenameType.PlayerCalling:
                    catData.playerCalling = sanitized;
                    return true;
                case PendingRenameType.CatPronoun:
                    catData.catPronoun = sanitized;
                    return true;
                default:
                    return false;
            }
        }

        private static string SanitizeRenameValue(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private static string GetPersistentString(string primaryKey, string fallbackKey, string defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(primaryKey) && PlayerPrefs.HasKey(primaryKey))
            {
                return PlayerPrefs.GetString(primaryKey, defaultValue);
            }

            if (!string.IsNullOrWhiteSpace(fallbackKey) && PlayerPrefs.HasKey(fallbackKey))
            {
                return PlayerPrefs.GetString(fallbackKey, defaultValue);
            }

            return defaultValue;
        }

        private static void ApplyToState(CatDataSO catData)
        {
            ConversationGameState state = ResolveState();
            if (state == null || catData == null)
            {
                return;
            }

            state.SetString("PlayerName", SanitizeRenameValue(catData.playerName));
            state.SetString("PLAYER_NAME", SanitizeRenameValue(catData.playerName));
            state.SetString("CatName", SanitizeRenameValue(catData.catName));
            state.SetString("CAT_NAME", SanitizeRenameValue(catData.catName));
            state.SetString("PlayerCalling", SanitizeRenameValue(catData.playerCalling));
            state.SetString("PLAYER_CALLING", SanitizeRenameValue(catData.playerCalling));
            state.SetString("CatPronoun", SanitizeRenameValue(catData.catPronoun));
            state.SetString("CAT_PRONOUN", SanitizeRenameValue(catData.catPronoun));
        }

        private static string GetConfirmKey(PendingRenameType type)
        {
            return type switch
            {
                PendingRenameType.CatName => RenameCatConfirmKey,
                PendingRenameType.PlayerCalling => RenameCallingConfirmKey,
                PendingRenameType.CatPronoun => RenamePronounConfirmKey,
                _ => string.Empty
            };
        }

        private static bool IsAcceptKeyForType(string key, PendingRenameType type)
        {
            return string.Equals(key, GetAcceptKey(type), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCancelKeyForType(string key, PendingRenameType type)
        {
            return string.Equals(key, GetCancelKey(type), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetAcceptKey(PendingRenameType type)
        {
            return type switch
            {
                PendingRenameType.CatName => RenameCatAcceptKey,
                PendingRenameType.PlayerCalling => RenameCallingAcceptKey,
                PendingRenameType.CatPronoun => RenamePronounAcceptKey,
                _ => string.Empty
            };
        }

        private static string GetCancelKey(PendingRenameType type)
        {
            return type switch
            {
                PendingRenameType.CatName => RenameCatCancelKey,
                PendingRenameType.PlayerCalling => RenameCallingCancelKey,
                PendingRenameType.CatPronoun => RenamePronounCancelKey,
                _ => string.Empty
            };
        }

        private static bool IsRenameBranchKey(string key)
        {
            return string.Equals(key, RenameCatAcceptKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, RenameCallingAcceptKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, RenamePronounAcceptKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, RenameCatCancelKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, RenameCallingCancelKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, RenamePronounCancelKey, StringComparison.OrdinalIgnoreCase);
        }

        private static ConversationGameState ResolveState()
        {
            ConversationGameStateManager manager = UnityEngine.Object.FindFirstObjectByType<ConversationGameStateManager>();
            return manager != null ? manager.State : null;
        }

        private static CatDataSO ResolveCatData()
        {
            DialogueManager dialogueManager = DialogueManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<DialogueManager>();
            return dialogueManager != null ? dialogueManager.catData : null;
        }
    }
}
