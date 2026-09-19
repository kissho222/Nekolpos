using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nekolpos.CameraSystem
{
    public sealed class FocusTarget : MonoBehaviour
    {
        private static readonly List<FocusTarget> ActiveTargets = new List<FocusTarget>();

        [SerializeField] private string displayName;
        [SerializeField] private Vector3 focusOffset;
        [SerializeField] private int focusPriority;
        [SerializeField] private string dialogueKey;
        [SerializeField] private bool autoFocusable = true;

        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public Vector3 FocusOffset
        {
            get => focusOffset;
            set => focusOffset = value;
        }

        public int FocusPriority
        {
            get => focusPriority;
            set => focusPriority = value;
        }

        public string DialogueKey
        {
            get => dialogueKey;
            set => dialogueKey = value;
        }

        public bool AutoFocusable
        {
            get => autoFocusable;
            set => autoFocusable = value;
        }

        public Vector3 FocusPoint => transform.TransformPoint(focusOffset);

        private void OnEnable()
        {
            if (!ActiveTargets.Contains(this))
            {
                ActiveTargets.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveTargets.Remove(this);
        }

        public static bool TryFindById(string id, out FocusTarget target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            string trimmedId = id.Trim();
            int bestPriority = int.MinValue;
            for (int i = 0; i < ActiveTargets.Count; i++)
            {
                FocusTarget candidate = ActiveTargets[i];
                if (candidate == null || !candidate.isActiveAndEnabled)
                {
                    continue;
                }

                if (!candidate.MatchesId(trimmedId) || candidate.focusPriority < bestPriority)
                {
                    continue;
                }

                target = candidate;
                bestPriority = candidate.focusPriority;
            }

            return target != null;
        }

        private bool MatchesId(string id)
        {
            return string.Equals(dialogueKey, id, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, id, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(gameObject.name, id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
