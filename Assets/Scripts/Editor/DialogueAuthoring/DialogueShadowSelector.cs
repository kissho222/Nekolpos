using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nekolpos.EditorTools.Authoring
{
    public sealed class ShadowProgress
    {
        public int repeat, cursor, anchor, lastDay = -1;
        public string lastRandom = "";
        public readonly HashSet<string> spoken = new(StringComparer.Ordinal);
    }
    // Editor-only and instance-local. No save store, DialogueManager or external command executor.
    public sealed class DialogueShadowSelector
    {
        public readonly Dictionary<int, ShadowProgress> states = new();
        public int lastHash;
        public static ShadowRoot Match(DialogueShadowBundle bundle, string normalizedInput, Func<string, bool> condition)
        {
            ShadowRoot best = null; int length = -1, priority = int.MinValue; bool explicitMatch = false;
            // Preserve global CSV matcher appearance order, even when roots interleave.
            foreach (var candidate in bundle.roots.SelectMany(r => r.matchers.Select(m => (root: r, matcher: m))).OrderBy(x => x.matcher.sourceRow))
            {
                if (!candidate.root.normalOrder.Any(i => string.IsNullOrWhiteSpace(candidate.root.segments[i].condition) || condition(candidate.root.segments[i].condition))) continue;
                foreach (Match match in Regex.Matches(normalizedInput, candidate.matcher.regex, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                {
                    bool isExplicit = candidate.matcher.sensitivity.Equals("EXPLICIT", StringComparison.OrdinalIgnoreCase);
                    if (match.Success && (best == null || match.Length > length || match.Length == length && (candidate.matcher.priority > priority || candidate.matcher.priority == priority && isExplicit && !explicitMatch)))
                    { best = candidate.root; length = match.Length; priority = candidate.matcher.priority; explicitMatch = isExplicit; }
                }
            }
            return best;
        }
        public ShadowSegment Select(ShadowRoot root, int day, Func<string, bool> condition, Func<int, int> draw)
        {
            if (root == null || condition == null || draw == null) throw new ArgumentNullException();
            var candidates = root.normalOrder.Select(i => root.segments[i]).Where(s => string.IsNullOrWhiteSpace(s.condition) || condition(s.condition)).ToList();
            var defeat = candidates.Where(s => s.condition?.IndexOf("PlayerDefeatedBy", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (defeat.Count > 0) candidates = defeat;
            if (candidates.Count == 0) return null;
            if (!states.TryGetValue(root.legacyHash, out var state)) states.Add(root.legacyHash, state = new ShadowProgress());
            day = Math.Max(1, day);
            var sequence = candidates.Where(s => s.speech == AuthoringSpeech.Sequence).ToList();
            if (state.lastDay >= 0 && day - state.lastDay >= root.policy.staleGameDays)
            {
                if (lastHash == root.legacyHash) lastHash = 0;
                state.repeat = 0;
                if (sequence.Count > 0) { state.anchor = Math.Clamp(state.anchor, 0, sequence.Count - 1); state.cursor = state.anchor; }
            }
            state.repeat = lastHash == root.legacyHash ? state.repeat + 1 : 1; lastHash = root.legacyHash;
            ShadowSegment selected = null, maxRepeat = null;
            if (state.repeat >= 2)
            {
                foreach (var candidate in candidates.Where(s => s.speech == AuthoringSpeech.RepeatEvent))
                {
                    if (candidate.repeatThreshold == state.repeat) { selected = candidate; break; }
                    if (maxRepeat == null || candidate.repeatThreshold > maxRepeat.repeatThreshold) maxRepeat = candidate;
                }
                if (selected == null && maxRepeat != null && state.repeat >= maxRepeat.repeatThreshold) selected = maxRepeat;
            }
            if (selected == null)
            {
                var flow = candidates.Where(s => s.speech == AuthoringSpeech.Random || s.speech == AuthoringSpeech.Sequence).ToList();
                int limit = flow.FirstOrDefault(s => s.randomLimit >= 0)?.randomLimit ?? root.policy.defaultRandomRepeatLimit;
                var random = flow.Where(s => s.speech == AuthoringSpeech.Random).ToList();
                if (state.repeat <= limit && random.Count > 0) selected = Random(random);
                else if (sequence.Count > 0)
                {
                    int index;
                    if (state.cursor < sequence.Count) index = state.cursor++;
                    else
                    {
                        if (state.anchor >= 0 && state.anchor < sequence.Count && sequence[state.anchor].hold)
                        {
                            index = state.anchor;
                        }
                        else
                        {
                            index = sequence.Count - 1;
                            if (External(sequence[index].representativeResponse))
                                for (int i = index - 1; i >= 0; i--) if (!External(sequence[i].representativeResponse)) { index = i; break; }
                        }
                    }
                    selected = sequence[index]; if (selected.hold) state.anchor = index;
                }
                else if (flow.Count > 0) selected = Random(flow);
            }
            if (selected != null) state.lastDay = day;
            return selected;
            ShadowSegment Random(List<ShadowSegment> pool)
            {
                var unseen = pool.Where(s => !state.spoken.Contains(s.pattern)).ToList();
                var available = unseen.Count > 0 ? unseen : pool.Where(s => pool.Count <= 1 || s.pattern != state.lastRandom).ToList();
                if (available.Count == 0) available = pool;
                int index = draw(available.Count);
                if (index < 0 || index >= available.Count) throw new ArgumentOutOfRangeException(nameof(draw), "Random index outside candidate pool.");
                var item = available[index]; state.spoken.Add(item.pattern); state.lastRandom = item.pattern; return item;
            }
        }
        private static bool External(string type) => new[] { "Action", "Event", "Choice" }.Contains((type ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
