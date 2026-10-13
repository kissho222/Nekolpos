using UnityEngine;
using Nekolpos.Data;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
#endif

namespace Nekolpos.System
{
    /// <summary>Isolation begins before any scene Awake and lasts through shutdown. Never restores live normal saves.</summary>
    public static class DebugSaveSession
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string PendingKey = "Nekolpos.DebugConsole.NextPlaySession";
        private static readonly Dictionary<string, int> integers = new();
        private static readonly Dictionary<string, string> strings = new();
        private static readonly Dictionary<CatDataSO, CatDataSO> catCopies = new();
        public static bool IsIsolated { get; private set; }
        public static bool IsFinished { get; private set; }
        public static string SessionId { get; private set; }
        public static string FileRoot => global::System.IO.Path.Combine(Application.temporaryCachePath, "NekolposDebug", SessionId ?? "unavailable");
        public static bool CanRunScenarios => IsIsolated && !IsFinished;
#if UNITY_EDITOR
        public static void ArmNextPlay()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Playを停止してから隔離セッションを開始してください");
            UnityEditor.SessionState.SetBool(PendingKey, true);
        }
        public static void ReleaseAfterPlayStopped()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play終了後だけ隔離を解除できます");
            IsIsolated = false; IsFinished = false; SessionId = null;
            integers.Clear(); strings.Clear(); catCopies.Clear();
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            IsIsolated = false; IsFinished = false; SessionId = null;
            integers.Clear(); strings.Clear(); catCopies.Clear();
#if UNITY_EDITOR
            IsIsolated = UnityEditor.SessionState.GetBool(PendingKey, false);
            UnityEditor.SessionState.EraseBool(PendingKey);
#else
            IsIsolated = Array.IndexOf(Environment.GetCommandLineArgs(), "--nekolpos-isolated-debug") >= 0;
#endif
            if (IsIsolated) SessionId = Guid.NewGuid().ToString("N");
        }
        // Finishing does not switch stores back: OnDisable/OnQuit must still target the test domain.
        public static void Finish() { IsFinished = true; }
        public static bool HasKey(string key) => integers.ContainsKey(key) || strings.ContainsKey(key);
        public static int GetInt(string key, int fallback = 0) => integers.TryGetValue(key, out int value) ? value : fallback;
        public static string GetString(string key, string fallback = "") => strings.TryGetValue(key, out string value) ? value : fallback;
        public static void SetInt(string key, int value) { strings.Remove(key); integers[key] = value; }
        public static void SetString(string key, string value) { integers.Remove(key); strings[key] = value; }
        public static void DeleteKey(string key) { integers.Remove(key); strings.Remove(key); }
        public static CatDataSO GetRuntimeCatData(CatDataSO source)
        {
            if (!IsIsolated || source == null || catCopies.ContainsValue(source)) return source;
            if (!catCopies.TryGetValue(source, out var clone)) { clone = UnityEngine.Object.Instantiate(source); clone.hideFlags = HideFlags.DontSave; catCopies.Add(source, clone); }
            return clone;
        }
#else
        public static bool IsIsolated => false;
        public static CatDataSO GetRuntimeCatData(CatDataSO source) => source;
#endif
    }
}
