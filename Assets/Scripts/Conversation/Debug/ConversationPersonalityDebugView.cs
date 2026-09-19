using System;
using System.Collections.Generic;
using TMPro;
using Nekolpos.StatusSystem;
using UnityEngine;
using UnityEngine.UI;

namespace Backgammon.Conversation
{
    public sealed class ConversationPersonalityDebugView : MonoBehaviour
    {
        private const int MaxPersonalityValue = 100;

        public static readonly string[] PersonalityKeys =
        {
            "Affection",
            "Sadistic",
            "Concern",
            "Hostility",
            "Obedience",
            "Instinct"
        };

        public static string GetDisplayName(string key)
        {
            return key switch
            {
                "Affection" => "愛情",
                "Sadistic" => "ドS",
                "Concern" => "心配",
                "Hostility" => "敵意",
                "Obedience" => "服従",
                "Instinct" => "本能",
                _ => key
            };
        }

        [SerializeField] private ConversationRadarChartGraphic radarChart;
        [SerializeField] private TMP_Text[] axisLabels = Array.Empty<TMP_Text>();
        [SerializeField] private TMP_Text[] valueLabels = Array.Empty<TMP_Text>();
        [SerializeField] private Slider[] sliders = Array.Empty<Slider>();

        private ConversationGameState boundState;
        private StatusManager statusManager;

        public event Action<string, int> PersonalityValueChanged;

        private void Awake()
        {
            BindSliderCallbacks();
            RefreshLabels();
        }

        public void BindState(ConversationGameState state)
        {
            boundState = state;
            RefreshFromState(state);
        }

        public void BindStatusManager(StatusManager manager)
        {
            statusManager = manager;
            RefreshFromState(boundState);
        }

        public void RefreshFromState(ConversationGameState state)
        {
            var values = new List<float>(PersonalityKeys.Length);
            for (var i = 0; i < PersonalityKeys.Length; i++)
            {
                var value = 0;
                if (TryResolveStatusType(PersonalityKeys[i], out StatusType statusType) && statusManager != null)
                {
                    value = statusManager.GetValue(statusType);
                }
                else if (state != null && state.TryGetInt(PersonalityKeys[i], out var stateValue))
                {
                    value = Mathf.Clamp(stateValue, 0, MaxPersonalityValue);
                }

                values.Add(value);
                if (i < valueLabels.Length && valueLabels[i] != null)
                {
                    valueLabels[i].text = value.ToString();
                }

                if (i < sliders.Length && sliders[i] != null)
                {
                    sliders[i].SetValueWithoutNotify(value);
                }
            }

            radarChart?.SetValues(values);
        }

        public void SetValue(string key, int value)
        {
            int clampedValue = Mathf.Clamp(value, 0, MaxPersonalityValue);
            if (statusManager != null && TryResolveStatusType(key, out StatusType statusType))
            {
                statusManager.SetValue(statusType, clampedValue, "Debug", "ConversationDebugPanel");
            }

            RefreshFromState(boundState);
            PersonalityValueChanged?.Invoke(key, clampedValue);
        }

        private static bool TryResolveStatusType(string key, out StatusType statusType)
        {
            return System.Enum.TryParse(key, true, out statusType);
        }

        private void BindSliderCallbacks()
        {
            for (var i = 0; i < sliders.Length && i < PersonalityKeys.Length; i++)
            {
                if (sliders[i] == null)
                {
                    continue;
                }

                var axisIndex = i;
                sliders[i].minValue = 0f;
                sliders[i].maxValue = MaxPersonalityValue;
                sliders[i].wholeNumbers = true;
                sliders[i].onValueChanged.AddListener(value => SetValue(PersonalityKeys[axisIndex], Mathf.RoundToInt(value)));
            }
        }

        private void RefreshLabels()
        {
            for (var i = 0; i < axisLabels.Length && i < PersonalityKeys.Length; i++)
            {
                if (axisLabels[i] != null)
                {
                    axisLabels[i].text = GetDisplayName(PersonalityKeys[i]);
                }
            }
        }
    }
}
