using UnityEngine;
using Nekolpos.StatusSystem;

namespace Backgammon.Conversation
{
    public sealed class ConversationGameStateManager : MonoBehaviour
    {
        [SerializeField] [TextArea(4, 12)] private string initialStateJson =
            "{\n" +
            "  \"MealRefusalCount\": 2,\n" +
            "  \"MealTakenToday\": false,\n" +
            "  \"KnownFoodCategories\": [\"Fish\"]\n" +
            "}";

        [SerializeField] private bool loadInitialStateOnAwake = true;

        private readonly ConversationGameState state = new();

        public ConversationGameState State => state;
        public string InitialStateJson => initialStateJson;

        private void Awake()
        {
            if (loadInitialStateOnAwake)
            {
                LoadFromJson(initialStateJson);
            }
        }

        public string SaveToJson(bool prettyPrint = true)
        {
            return state.SaveToJson(prettyPrint);
        }

        public void LoadFromJson(string json)
        {
            state.LoadFromJson(json);
        }

        public bool EvaluateCondition(string expression)
        {
            return ConversationGameStateConditionEvaluator.Evaluate(state, expression);
        }

        public bool EvaluateAllConditions(string[] expressions)
        {
            return ConversationGameStateConditionEvaluator.EvaluateAll(state, expressions);
        }

        public void ApplyEffects(ConversationGameStateEffectSet effectSet)
        {
            if (effectSet == null)
            {
                return;
            }

            StatusManager statusManager = StatusManager.FindOrCreate(gameObject);
            for (int i = 0; i < effectSet.effects.Count; i++)
            {
                ConversationGameStateEffect effect = effectSet.effects[i];
                if (statusManager != null && statusManager.TryApplyConversationEffect(effect, "ConversationGameState", string.Empty, out _))
                {
                    continue;
                }

                ConversationGameStateEffectApplier.Apply(state, effect);
            }
        }

        public void ApplyEffectsFromJson(string json)
        {
            ApplyEffects(ConversationGameStateEffectApplier.ParseEffectSetJson(json));
        }

        [ContextMenu("Reset State From Initial JSON")]
        public void ResetStateFromInitialJson()
        {
            LoadFromJson(initialStateJson);
        }
    }
}
