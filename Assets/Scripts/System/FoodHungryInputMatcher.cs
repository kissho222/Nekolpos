using System;
using System.Text.RegularExpressions;
using Backgammon.Conversation;

namespace Nekolpos.System
{
    public static class FoodHungryInputMatcher
    {
        public const string TriggerType = "FoodHungry";
        public const string DialogueLabel = "system_food_hungry";
        public const string ConfirmDialogueKey = "system_food_hungry_confirm";
        public const string YesDialogueKey = "system_food_hungry_yes";
        public const string NoDialogueKey = "system_food_hungry_no";
        public const string CompleteActionId = "system_food_hungry_complete";

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private static readonly Regex HungryRegex = CreateRegex(
            "(?:おなか|はら)(?:が|は|も)?(?:すい|へっ)(?:た|てる|てきた)?|はらぺこ|くうふく");

        private static readonly Regex MealRegex = CreateRegex(
            "しょくじ|ごはん(?:を|が)?(?:たべたい|たべる|くいたい|くう|ほしい)?");

        private static readonly Regex EatWantRegex = CreateRegex(
            "なんか(?:を|が)?(?:たべたい|くいたい|くう|たべる)|^(?:たべたい|くいたい)$");

        private static readonly Regex FoodNameEatWantRegex = CreateRegex(
            "^(.+?)(?:を|が)?(?:たべたい|くいたい|たべる|くう)$");

        public static bool TryMatch(PlayerInputContext inputContext, out FoodHungryMatchResult result)
        {
            result = default;

            string normalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return false;
            }

            if (TryIsMatch(HungryRegex, normalizedInput))
            {
                result = new FoodHungryMatchResult(TriggerType, "hungry", string.Empty);
                return true;
            }

            if (TryIsMatch(MealRegex, normalizedInput))
            {
                result = new FoodHungryMatchResult(TriggerType, "meal", string.Empty);
                return true;
            }

            if (TryIsMatch(EatWantRegex, normalizedInput))
            {
                result = new FoodHungryMatchResult(TriggerType, "eatWant", string.Empty);
                return true;
            }

            Match foodNameMatch = MatchSafely(FoodNameEatWantRegex, normalizedInput);
            if (foodNameMatch.Success)
            {
                string foodName = foodNameMatch.Groups.Count > 1 ? foodNameMatch.Groups[1].Value.Trim() : string.Empty;
                result = new FoodHungryMatchResult(TriggerType, "foodNameEatWant", foodName);
                return true;
            }

            return false;
        }

        private static Regex CreateRegex(string pattern)
        {
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, MatchTimeout);
        }

        private static bool TryIsMatch(Regex regex, string input)
        {
            try
            {
                return regex.IsMatch(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static Match MatchSafely(Regex regex, string input)
        {
            try
            {
                return regex.Match(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return Match.Empty;
            }
        }
    }

    public readonly struct FoodHungryMatchResult
    {
        public FoodHungryMatchResult(string triggerType, string matchedPattern, string foodName)
        {
            TriggerType = triggerType ?? string.Empty;
            MatchedPattern = matchedPattern ?? string.Empty;
            FoodName = foodName ?? string.Empty;
        }

        public string TriggerType { get; }
        public string MatchedPattern { get; }
        public string FoodName { get; }
    }
}
