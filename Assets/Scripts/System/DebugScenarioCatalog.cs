#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekolpos.System
{
    public enum DebugScenarioKind { NormalTable, NormalDesk, Walk, TabletDesk, DiaryNormal, DiarySpecial, Transport, Death }
    public sealed class DebugScenarioDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Mode { get; }
        public string Fixture { get; }
        public DebugScenarioKind Kind { get; }
        public DebugScenarioDefinition(string id, string name, string mode, string fixture, DebugScenarioKind kind)
        { Id = id; DisplayName = name; Mode = mode; Fixture = fixture; Kind = kind; }
    }
    public static class DebugScenarioCatalog
    {
        private static readonly DebugScenarioDefinition[] entries = {
            new("normal.table", "ちゃぶ台の通常会話", "通常入力待機", "MoveToTable", DebugScenarioKind.NormalTable),
            new("normal.desk", "机の通常会話", "通常入力待機", "MoveToDesk", DebugScenarioKind.NormalDesk),
            new("walk", "散歩開始", "通常入力待機 → 散歩", "PlayerWalkModeController / Spawn", DebugScenarioKind.Walk),
            new("tablet.desk", "机のタブレット", "通常入力待機 → Tablet", "MoveToDesk / TabletDisplay", DebugScenarioKind.TabletDesk),
            new("diary.night.normal", "夜の通常日記", "通常入力待機 → 夜", "TimeFlow / Diary", DebugScenarioKind.DiaryNormal),
            new("diary.night.special", "夜の特別日記", "通常入力待機 → 夜", "既存特別日記CSV / Pending", DebugScenarioKind.DiarySpecial),
            new("transition.table-desk", "ちゃぶ台から机へ運搬", "通常入力待機 → 運搬", "MoveToDesk / TransportAnimation", DebugScenarioKind.Transport),
            new("death.respawn", "死亡と翌朝復帰", "通常入力待機 → 死亡 → 翌朝", "DialogueManager defeat flow", DebugScenarioKind.Death)
        };
        public static IReadOnlyList<DebugScenarioDefinition> Entries => Array.AsReadOnly(entries);
        public static DebugScenarioDefinition Find(string id) => Array.Find(entries, x => string.Equals(x.Id, id, StringComparison.Ordinal));
        public static string PrerequisiteReason(DebugScenarioDefinition entry)
        {
            if (entry == null) return "許可リストに存在しないシナリオです";
            if (!DebugSaveSession.CanRunScenarios) return Application.isEditor ? "隔離未保証: Playを停止し、Start Isolated Playを実行してください" : "隔離未保証: --nekolpos-isolated-debugを付けてDevelopment Playerを再起動してください";
            var b = Object.FindFirstObjectByType<NekolposGameBootstrap>();
            if (b == null) return "MainSceneのBootstrapが未接続です";
            if (entry.Kind == DebugScenarioKind.Walk && Object.FindFirstObjectByType<PlayerWalkModeController>() == null) return "散歩Controllerが未接続です";
            if (entry.Kind == DebugScenarioKind.Transport && !b.HasTransportPresentationForBinding("MoveToDesk")) return "運搬Preset・2D表示素材が未接続です";
            if ((entry.Kind == DebugScenarioKind.NormalTable && !b.HasConversationTimelineBinding("MoveToTable")) ||
                ((entry.Kind == DebugScenarioKind.NormalDesk || entry.Kind == DebugScenarioKind.TabletDesk || entry.Kind == DebugScenarioKind.Transport) && !b.HasConversationTimelineBinding("MoveToDesk"))) return "正規の運搬Bindingが未接続です";
            if (entry.Kind == DebugScenarioKind.DiaryNormal || entry.Kind == DebugScenarioKind.DiarySpecial)
                return DebugScenarioFixtures.CheckDiary(entry.Kind == DebugScenarioKind.DiarySpecial);
            if (entry.Kind == DebugScenarioKind.Death && (DialogueManager.Instance == null || Object.FindFirstObjectByType<Nekolpos.TimeSystem.TimeManager>() == null)) return "死亡・TimeFlow正本が未接続です";
            return null;
        }
    }
}
#endif
