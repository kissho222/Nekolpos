using System.Collections.Generic;

namespace Nekolpos.EditorTools
{
    public static class DialogueTimelineController
    {
        public static List<DialogueTimelineEvent> Build(DialogueEntry entry)
        {
            List<DialogueTimelineEvent> events = new List<DialogueTimelineEvent>();
            if (entry == null)
            {
                return events;
            }

            float waitTime = entry.WaitTime < 0f ? 0f : entry.WaitTime;
            float animationTime = waitTime > 0f ? waitTime * 0.5f : 0.5f;

            events.Add(new DialogueTimelineEvent(0f, "テキスト表示"));

            if (!string.IsNullOrWhiteSpace(entry.Animation))
            {
                events.Add(new DialogueTimelineEvent(animationTime, $"アニメーション再生: {entry.Animation}"));
            }

            if (entry.HideUI)
            {
                events.Add(new DialogueTimelineEvent(waitTime, "UI非表示"));
            }

            if (waitTime > 0f)
            {
                events.Add(new DialogueTimelineEvent(waitTime, $"待機終了 ({waitTime:0.##}s)"));
            }

            if (!string.IsNullOrWhiteSpace(entry.ActionId))
            {
                events.Add(new DialogueTimelineEvent(waitTime, $"Action: {entry.ActionId}"));
            }

            if (!string.IsNullOrWhiteSpace(entry.Condition))
            {
                events.Add(new DialogueTimelineEvent(0f, $"条件: {entry.Condition}"));
            }

            events.Sort((a, b) => a.Time.CompareTo(b.Time));
            return events;
        }
    }
}
