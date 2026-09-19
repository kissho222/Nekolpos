using System;

namespace Nekolpos.System
{
    /// <summary>
    /// Matches a cat name inside a standalone vocative such as "ねるこー！".
    /// It intentionally rejects sentences such as "ねるこをなでたい" so the
    /// normal intent router can process them.
    /// </summary>
    public static class CatNameCallMatcher
    {
        public static bool ContainsName(string normalizedInput, string normalizedCatName)
        {
            if (string.IsNullOrWhiteSpace(normalizedInput) ||
                string.IsNullOrWhiteSpace(normalizedCatName))
            {
                return false;
            }

            return normalizedInput.IndexOf(
                normalizedCatName,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsNameCall(string normalizedInput, string normalizedCatName)
        {
            if (string.IsNullOrWhiteSpace(normalizedInput) ||
                string.IsNullOrWhiteSpace(normalizedCatName))
            {
                return false;
            }

            bool foundName = false;
            int segmentStart = -1;
            for (int i = 0; i <= normalizedInput.Length; i++)
            {
                bool isEnd = i >= normalizedInput.Length;
                bool isSegmentSeparator = !isEnd && IsSegmentSeparator(normalizedInput[i], normalizedCatName);
                if (!isEnd && !isSegmentSeparator)
                {
                    if (segmentStart < 0)
                    {
                        segmentStart = i;
                    }

                    continue;
                }

                if (segmentStart < 0)
                {
                    continue;
                }

                string segment = normalizedInput.Substring(segmentStart, i - segmentStart);
                segmentStart = -1;
                if (IsNameSegment(segment, normalizedCatName))
                {
                    foundName = true;
                    continue;
                }

                if (IsAllowedCallPhrase(segment))
                {
                    continue;
                }

                return false;
            }

            return foundName;
        }

        private static bool IsAllowedCallPhrase(string value)
        {
            value = TrimTrailingNameDecorations(value);
            return string.Equals(value, "おいで", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "きて", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "来て", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "こっちきて", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "こっち来て", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "こっちおいで", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNameSegment(string segment, string normalizedCatName)
        {
            if (string.Equals(segment, normalizedCatName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 「ネルコは……」のように、名前を話題に出したまま言い淀む入力も
            // 名前呼びとして先に処理する。後ろに本文が続く場合は通常の意図判定へ渡す。
            if (string.Equals(
                    segment,
                    normalizedCatName + "は",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!segment.StartsWith(normalizedCatName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = normalizedCatName.Length; i < segment.Length; i++)
            {
                if (!IsTrailingNameDecoration(segment[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string TrimTrailingNameDecorations(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int end = value.Length;
            while (end > 0 && IsTrailingNameDecoration(value[end - 1]))
            {
                end--;
            }

            return end == value.Length ? value : value.Substring(0, end);
        }

        private static bool IsSegmentSeparator(char value, string normalizedCatName)
        {
            if (IsLongSoundMark(value) &&
                !string.IsNullOrEmpty(normalizedCatName) &&
                normalizedCatName.IndexOf(value) >= 0)
            {
                return false;
            }

            return IsCallDecoration(value) && !IsTrailingNameDecoration(value);
        }

        private static bool IsTrailingNameDecoration(char value)
        {
            return value == 'っ' ||
                   IsLongSoundMark(value) ||
                   value == 'ぁ' ||
                   value == 'ぃ' ||
                   value == 'ぅ' ||
                   value == 'ぇ' ||
                   value == 'ぉ';
        }

        private static bool IsCallDecoration(char value)
        {
            return char.IsWhiteSpace(value) ||
                   value == 'ー' ||
                   value == '〜' ||
                   value == '～' ||
                   value == '~' ||
                   value == '!' ||
                   value == '！' ||
                   value == '?' ||
                   value == '？' ||
                   value == '。' ||
                   value == '、' ||
                   value == '・' ||
                   value == '…' ||
                   value == '‥' ||
                   value == 'っ' ||
                   value == 'ぁ' ||
                   value == 'ぃ' ||
                   value == 'ぅ' ||
                   value == 'ぇ' ||
                   value == 'ぉ';
        }

        private static bool IsLongSoundMark(char value)
        {
            return value == 'ー' ||
                   value == 'ｰ' ||
                   value == '－' ||
                   value == '―' ||
                   value == '‐' ||
                   value == '‑' ||
                   value == '–' ||
                   value == '—';
        }
    }
}
