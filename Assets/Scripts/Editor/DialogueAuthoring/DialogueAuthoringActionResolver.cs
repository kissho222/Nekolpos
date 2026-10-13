using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nekolpos.System;

namespace Nekolpos.EditorTools.Authoring
{
    // Read-only classification of existing Runtime entry points; never invoke an action.
    public static class DialogueAuthoringActionResolver
    {
        public static AuthoringActionResolution Classify(string id, HashSet<string> registry)
        {
            id = (id ?? "").Trim();
            var result = new AuthoringActionResolution { actionId = id, kind = ActionResolutionKind.Unresolved, resolver = "未解決" };
            if (registry.Contains(id)) Set(ActionResolutionKind.Registry, "通常Registry / ActionData・SystemTimedEvent正本");
            else if (Predicate("IsTeachingAction") || Predicate("IsRepeatLastPatternAction") ||
                id.Equals("walk", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("StartEvent", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("unknown_word_teach_event", StringComparison.OrdinalIgnoreCase) || IsDefeat())
                Set(ActionResolutionKind.Dedicated, "DialogueManager既存専用処理");
            else if (new[] { "Play_Game", "Punch", "Petting" }.Contains(id, StringComparer.OrdinalIgnoreCase))
                Set(ActionResolutionKind.Dedicated, "ActionManager既定ChoiceSet");
            else if (id.StartsWith("yarn:", StringComparison.OrdinalIgnoreCase) && id.Substring(5).Trim().Length > 0)
            {
                string node = id.Substring(5).Trim();
                if (Directory.Exists("Assets/Yarn") && Directory.GetFiles("Assets/Yarn", "*.yarn", SearchOption.AllDirectories)
                    .Any(path => File.ReadLines(path).Any(line => line.Trim().Equals("title: " + node, StringComparison.Ordinal))))
                    Set(ActionResolutionKind.RuntimeResolver, "DialogueManager Yarn resolver / node: " + node);
            }
            else if (id.StartsWith("play_conversation_timeline:", StringComparison.OrdinalIgnoreCase) && id.Substring(27).Trim().Length > 0)
                Set(ActionResolutionKind.RuntimeResolver, "DialogueManager会話Timeline resolver（Scene bindingは実再生確認が必要）");
            else if (id.StartsWith("move_home:", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<CatHomeLocation>(id.Substring(10).Trim(), out var location) && Enum.IsDefined(typeof(CatHomeLocation), location))
                Set(ActionResolutionKind.Dedicated, "DialogueManager基本会話地点処理");
            return result;

            void Set(ActionResolutionKind kind, string resolver) { result.kind = kind; result.resolver = resolver; }
            bool Predicate(string method) => (bool)typeof(DialogueManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { id });
            bool IsDefeat()
            {
                object[] args = { id, "" };
                return (bool)typeof(DialogueManager).GetMethod("TryParsePlayerDefeatReason", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            }
        }
    }
}
