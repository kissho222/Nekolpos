using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Nekolpos.StatusSystem;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using UnityEngine;

namespace Nekolpos.ActionSystem
{
    public readonly struct ActionExecutionContext
    {
        public ActionExecutionContext(string triggerId, string sourceDialogue)
        {
            TriggerId = triggerId;
            SourceDialogue = sourceDialogue;
        }

        public string TriggerId { get; }

        public string SourceDialogue { get; }
    }

    public readonly struct ActionExecutionResult
    {
        public ActionExecutionResult(ActionData actionData, string message, IReadOnlyList<StatusChangeResult> statusChanges)
        {
            ActionData = actionData;
            Message = message;
            StatusChanges = statusChanges;
        }

        public ActionData ActionData { get; }

        public string Message { get; }

        public IReadOnlyList<StatusChangeResult> StatusChanges { get; }
    }

    public class ActionExecutor : MonoBehaviour
    {
        [SerializeField] private StatusManager statusManager;

        private void Awake()
        {
            EnsureReferences();
        }

        public async UniTask<ActionExecutionResult> ExecuteAsync(ActionData actionData, ActionExecutionContext context)
        {
            EnsureReferences();

            if (actionData == null)
            {
                throw new ArgumentNullException(nameof(actionData));
            }

            // 実ミニゲームや Timeline に差し替わる余地を残すため、Action 本体はここで完結させる。
            await UniTask.Yield();

            // TimeFlow advances the clock around this action's presentation; execution only applies action effects.
            IReadOnlyList<StatusChangeResult> statusChanges = statusManager.ApplyEffects(actionData.Effects, "Action", actionData.ActionId);
            string message = ComposeResultText(actionData);
            return new ActionExecutionResult(actionData, message, statusChanges);
        }

        private void EnsureReferences()
        {
            if (statusManager == null)
            {
                statusManager = GetComponent<StatusManager>() ?? FindFirstObjectByType<StatusManager>();
                if (statusManager == null)
                {
                    DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
                    GameObject host = dialogueManager != null ? dialogueManager.gameObject : gameObject;
                    statusManager = host.GetComponent<StatusManager>() ?? host.AddComponent<StatusManager>();
                    if (dialogueManager != null)
                    {
                        statusManager.ConfigureInitialData(dialogueManager.catData);
                    }
                }
            }
        }

        private static string ComposeResultText(ActionData actionData)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(ResolveBaseResultText(actionData));

            return builder.ToString().Trim();
        }

        private static string ResolveBaseResultText(ActionData actionData)
        {
            if (actionData == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(actionData.TimedEventKey))
            {
                return SystemTimedEventCatalog.Get(
                    actionData.TimedEventKey,
                    actionData.ResultText ?? string.Empty);
            }

            return actionData.ResultText ?? string.Empty;
        }
    }
}
