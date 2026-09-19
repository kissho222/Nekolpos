using System.Collections.Generic;

namespace Nekolpos.System
{
    /// <summary>
    /// Keeps sent player input in memory and manages shell-style history navigation.
    /// </summary>
    public sealed class DialogueInputHistory
    {
        private readonly List<string> entries = new List<string>();
        private int navigationIndex;
        private string pendingInput = string.Empty;
        private bool isNavigating;

        public int Count => entries.Count;
        public bool IsNavigating => isNavigating;

        public void AddHistory(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // Deliberately keep duplicate entries: repeating the same phrase is gameplay.
            entries.Add(text);
            ResetNavigation();
        }

        public bool TryMovePrevious(string currentInput, out string result)
        {
            result = currentInput ?? string.Empty;
            if (entries.Count == 0)
            {
                return false;
            }

            if (!isNavigating)
            {
                pendingInput = result;
                navigationIndex = entries.Count;
                isNavigating = true;
            }

            if (navigationIndex > 0)
            {
                navigationIndex--;
            }

            result = entries[navigationIndex];
            return true;
        }

        public bool TryMoveNext(out string result)
        {
            result = string.Empty;
            if (!isNavigating)
            {
                return false;
            }

            if (navigationIndex < entries.Count - 1)
            {
                navigationIndex++;
                result = entries[navigationIndex];
                return true;
            }

            navigationIndex = entries.Count;
            result = pendingInput;
            isNavigating = false;
            pendingInput = string.Empty;
            return true;
        }

        public void ResetNavigation()
        {
            navigationIndex = entries.Count;
            pendingInput = string.Empty;
            isNavigating = false;
        }
    }
}
