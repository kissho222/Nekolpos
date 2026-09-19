using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class RegexInputParser
    {
        private static readonly TimeSpan DefaultRegexMatchTimeout = TimeSpan.FromMilliseconds(100);

        private readonly List<CompiledIntentRule> compiledIntentRules = new();
        private readonly List<CompiledCategoryRule> compiledCategoryRules = new();
        private readonly TimeSpan regexMatchTimeout;

        public RegexInputParser(ConversationParseConfig config, TimeSpan? regexMatchTimeout = null)
        {
            this.regexMatchTimeout = regexMatchTimeout.GetValueOrDefault(DefaultRegexMatchTimeout);

            if (config == null)
            {
                return;
            }

            BuildIntentRules(config.intentRules);
            BuildCategoryRules(config.categoryRules);
        }

        public ConversationParseResult Parse(string input)
        {
            return Parse(new PlayerInputContext(input));
        }

        public ConversationParseResult Parse(PlayerInputContext inputContext)
        {
            var result = new ConversationParseResult
            {
                input = inputContext != null ? inputContext.OriginalInput : string.Empty,
                normalizedInput = inputContext != null ? inputContext.NormalizedInput : string.Empty
            };

            var normalizedInput = result.normalizedInput;
            var intentMap = new Dictionary<string, IntentMatch>(StringComparer.Ordinal);
            var categoryMap = new Dictionary<string, CompiledCategoryRule>(StringComparer.Ordinal);

            foreach (var rule in compiledIntentRules)
            {
                bool matched;
                try
                {
                    matched = rule.Regex.IsMatch(normalizedInput);
                }
                catch (RegexMatchTimeoutException exception)
                {
                    Debug.LogWarning($"RegexInputParser skipped timed-out pattern '{rule.Pattern}': {exception.Message}");
                    continue;
                }

                if (!matched)
                {
                    continue;
                }

                if (!intentMap.TryGetValue(rule.Intent, out var existingMatch) || rule.Priority > existingMatch.priority)
                {
                    intentMap[rule.Intent] = new IntentMatch
                    {
                        intent = rule.Intent,
                        priority = rule.Priority
                    };
                }

                result.matchedRegexRules.Add(new RegexIntentRuleMatch
                {
                    pattern = rule.Pattern,
                    intent = rule.Intent,
                    priority = rule.Priority
                });
            }

            foreach (var rule in compiledCategoryRules)
            {
                if (!normalizedInput.Contains(rule.Word))
                {
                    continue;
                }

                var key = $"{rule.Type}\u001f{rule.Value}";
                if (!categoryMap.TryGetValue(key, out var existingRule) || rule.CompareTo(existingRule) > 0)
                {
                    categoryMap[key] = rule;
                }

                result.matchedCategoryRules.Add(new KeywordCategoryRuleMatch
                {
                    word = rule.Word,
                    type = rule.Type,
                    value = rule.Value,
                    priority = rule.Priority
                });
            }

            result.intents = intentMap.Values
                .OrderByDescending(match => match.priority)
                .ThenBy(match => match.intent, StringComparer.Ordinal)
                .ToList();

            result.categories = categoryMap.Values
                .OrderByDescending(rule => rule.Priority)
                .ThenByDescending(rule => rule.Word.Length)
                .ThenBy(rule => rule.Type, StringComparer.Ordinal)
                .ThenBy(rule => rule.Value, StringComparer.Ordinal)
                .Select(rule => new CategoryMatch
                {
                    type = rule.Type,
                    value = rule.Value
                })
                .ToList();

            return result;
        }

        public string ParseToJson(string input, bool prettyPrint = true)
        {
            return ParseToJson(new PlayerInputContext(input), prettyPrint);
        }

        public string ParseToJson(PlayerInputContext inputContext, bool prettyPrint = true)
        {
            return JsonUtility.ToJson(Parse(inputContext), prettyPrint);
        }

        private void BuildIntentRules(IEnumerable<RegexIntentRule> rules)
        {
            if (rules == null)
            {
                return;
            }

            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.pattern) || string.IsNullOrWhiteSpace(rule.intent))
                {
                    continue;
                }

                try
                {
                    var pattern = JapaneseTextNormalizer.NormalizeToken(rule.pattern);
                    compiledIntentRules.Add(new CompiledIntentRule(pattern, rule.intent, rule.priority, regexMatchTimeout));
                }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning($"RegexInputParser skipped invalid pattern '{rule.pattern}': {exception.Message}");
                }
            }
        }

        private void BuildCategoryRules(IEnumerable<KeywordCategoryRule> rules)
        {
            if (rules == null)
            {
                return;
            }

            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.word) || string.IsNullOrWhiteSpace(rule.type) || string.IsNullOrWhiteSpace(rule.value))
                {
                    continue;
                }

                var normalizedWord = InputNormalizer.NormalizeForMatching(rule.word);
                if (string.IsNullOrEmpty(normalizedWord))
                {
                    continue;
                }

                compiledCategoryRules.Add(new CompiledCategoryRule(
                    normalizedWord,
                    rule.type,
                    rule.value,
                    rule.priority));
            }
        }

        private sealed class CompiledIntentRule
        {
            public CompiledIntentRule(string pattern, string intent, int priority, TimeSpan matchTimeout)
            {
                Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeout);
                Pattern = pattern;
                Intent = intent;
                Priority = priority;
            }

            public Regex Regex { get; }
            public string Pattern { get; }
            public string Intent { get; }
            public int Priority { get; }
        }

        private sealed class CompiledCategoryRule
        {
            public CompiledCategoryRule(string word, string type, string value, int priority)
            {
                Word = word;
                Type = type;
                Value = value;
                Priority = priority;
            }

            public string Word { get; }
            public string Type { get; }
            public string Value { get; }
            public int Priority { get; }

            public int CompareTo(CompiledCategoryRule other)
            {
                if (other == null)
                {
                    return 1;
                }

                var priorityComparison = Priority.CompareTo(other.Priority);
                if (priorityComparison != 0)
                {
                    return priorityComparison;
                }

                return Word.Length.CompareTo(other.Word.Length);
            }
        }
    }
}
