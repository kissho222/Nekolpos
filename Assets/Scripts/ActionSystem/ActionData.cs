using System;
using System.Collections.Generic;
using Nekolpos.StatusSystem;
using UnityEngine;

namespace Nekolpos.ActionSystem
{
    [CreateAssetMenu(fileName = "ActionData", menuName = "Nekolpos/Action/Action Data")]
    public sealed class ActionData : ScriptableObject
    {
        [SerializeField] private string actionId;
        [SerializeField] private string displayName;
        [SerializeField] private string timedEventKey;
        [TextArea(2, 5)]
        [SerializeField] private string resultText;
        [SerializeField] [Min(0)] private int timeCost = 60;
        [SerializeField] private StatEffect[] effects = Array.Empty<StatEffect>();

        public string ActionId => actionId;

        public string DisplayName => displayName;

        public string TimedEventKey => timedEventKey;

        public string ResultText => resultText;

        public int TimeCost => timeCost;

        public IReadOnlyList<StatEffect> Effects => effects;

        public void Initialize(
            string newActionId,
            string newDisplayName,
            string newResultText,
            int newTimeCost,
            StatEffect[] newEffects,
            string newTimedEventKey = "")
        {
            actionId = newActionId;
            displayName = newDisplayName;
            timedEventKey = newTimedEventKey ?? string.Empty;
            resultText = newResultText;
            timeCost = Mathf.Max(0, newTimeCost);
            effects = newEffects ?? Array.Empty<StatEffect>();
        }
    }
}
