using System.Diagnostics;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using Nekolpos.TimeSystem;
#endif

namespace Nekolpos.System
{
    /// <summary>No player utterances or arbitrary exception/log strings are accepted by this recorder.</summary>
    public static class DebugConsoleTrace
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Serializable] public sealed class Entry
        {
            public long sequence;
            public float elapsed;
            public int? day;
            public string period, kind, source, correlationId, sessionId, scenarioId, before, after, result;
        }
        private static Entry[] buffer = new Entry[500];
        private static int start, count;
        private static long sequence;
        public static string CorrelationId { get; private set; }
        public static string SessionId { get; private set; }
        public static string ScenarioId { get; private set; }
        public static int Count => count;
        public static int Capacity => buffer.Length;
        public static string SafeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.Length > 64) return "<masked-id>";
            foreach (char c in id) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.') return "<masked-id>";
            return id;
        }
        public static void SetCapacity(int capacity) { buffer = new Entry[Mathf.Clamp(capacity, 16, 2000)]; start = count = 0; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { buffer = new Entry[500]; start = count = 0; sequence = 0; CorrelationId = SessionId = ScenarioId = null; }
        public static void SetSession(string id, string scenario) { SessionId = id; ScenarioId = scenario; CorrelationId = Guid.NewGuid().ToString("N"); Add("Session", "DebugSession", null, scenario, "Started"); }
        private static void Add(string kind, string source, string before, string after, string result)
        {
            if (CorrelationId == null) CorrelationId = Guid.NewGuid().ToString("N");
            var time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            var entry = new Entry { sequence = ++sequence, elapsed = Time.realtimeSinceStartup, day = time != null && time.IsInitialized ? time.CurrentDay : null,
                period = time != null && time.IsInitialized ? time.CurrentPeriod.ToString() : null,
                kind = kind, source = source, correlationId = CorrelationId, sessionId = SessionId, scenarioId = ScenarioId, before = before, after = after, result = result };
            int index = (start + count) % buffer.Length;
            buffer[index] = entry;
            if (count < buffer.Length) count++; else start = (start + 1) % buffer.Length;
        }
        public static Entry[] Read(string kind = null, string correlationId = null)
        {
            var entries = new List<Entry>();
            for (int i = 0; i < count; i++) { var e = buffer[(start + i) % buffer.Length]; if ((string.IsNullOrEmpty(kind) || e.kind == kind) && (string.IsNullOrEmpty(correlationId) || e.correlationId == correlationId)) entries.Add(e); }
            return entries.ToArray();
        }
        public static string ToJson() => Newtonsoft.Json.JsonConvert.SerializeObject(Read(), Newtonsoft.Json.Formatting.Indented);
        public static string ExportLocal()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("WebGLではコピーを使用してください");
#else
            string folder = Path.Combine(Application.persistentDataPath, "DebugConsole"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "trace-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            File.WriteAllText(path, ToJson()); return path;
#endif
        }
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void InputAccepted(int length)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CorrelationId = Guid.NewGuid().ToString("N"); Add("Input", "DialogueEngine", null, "<masked:" + Mathf.Clamp(length, 0, 100000) + ">", "Accepted");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void DialogueSelected(int labelHash, int lines, string patternId = null, string regexId = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("Dialogue", "DialogueManager", null, labelHash + "/pattern=" + SafeId(patternId) + "/regex=" + SafeId(regexId) + "/lines=" + lines, "Selected");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void StatusChanged(int type, int before, int after)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("Status", "StatusManager/" + type, before.ToString(), after.ToString(), "Applied");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void TimeRequested(int minutes, int reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("TimeRequest", "TimeFlow", null, minutes + "/reason=" + reason, "Requested");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void TimeAdvanced(int previousDay, int day, int previousPeriod, int period)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("Time", "TimeManager", previousDay + "/" + previousPeriod, day + "/" + period, "Applied");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Diary(bool active)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("Diary", "DiaryCalendar", null, active.ToString(), active ? "Started" : "Completed");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void ReturnedToInput()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("UI", "DialogueManager", null, "ConversationInputMode", "Applied");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void ConversationResult(int statusType)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("ConversationResult", "DialogueManager/5", null, statusType.ToString(), "Selected");
#endif
        }
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void ScenarioOutcome(int status)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Add("Scenario", "DebugScenarioExecutor", null, status.ToString(), "Finished");
#endif
        }
    }
}
