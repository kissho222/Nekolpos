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
        private const string ActiveKey = "Nekolpos.DebugConsole.ActivePlaySession";
        private static readonly Dictionary<string, int> integers = new();
        private static readonly Dictionary<string, string> strings = new();
        private static readonly Dictionary<CatDataSO, CatDataSO> catCopies = new();
        private static bool isolated;
        private static bool initialized;
        public static bool IsIsolated
        {
            get
            {
#if UNITY_EDITOR
                // SessionState survives reload. Losing managed memory must never expose normal stores.
                return isolated || !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(ActiveKey, ""));
#else
                return isolated;
#endif
            }
        }
        public static bool IsFinished { get; private set; }
        public static string SessionId { get; private set; }
        public static string FileRoot => global::System.IO.Path.Combine(Application.temporaryCachePath, "NekolposDebug", SessionId ?? "unavailable");
        public static bool CanRunScenarios => IsIsolated && initialized && !IsFinished;
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void StopAfterLostIsolationMemory()
        {
            if (string.IsNullOrEmpty(UnityEditor.SessionState.GetString(ActiveKey, "")) ||
                UnityEditor.SessionState.GetBool(PendingKey, false)) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (!initialized && IsIsolated && UnityEditor.EditorApplication.isPlaying)
                {
                    IsFinished = true;
                    Debug.LogError("隔離セッションのメモリーが再読み込みで失われたためPlayを停止します。");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
            };
        }
        public static void ArmNextPlay()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Playを停止してから隔離セッションを開始してください");
            if (IsIsolated) throw new InvalidOperationException("前の隔離セッションの終了処理が完了していません");
            UnityEditor.SessionState.SetString(ActiveKey, Guid.NewGuid().ToString("N"));
            UnityEditor.SessionState.SetBool(PendingKey, true);
        }
        public static void ReleaseAfterPlayStopped()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play終了後だけ隔離を解除できます");
            UnityEditor.SessionState.EraseString(ActiveKey);
            UnityEditor.SessionState.EraseBool(PendingKey);
            isolated = false; initialized = false; IsFinished = false; SessionId = null;
            integers.Clear(); strings.Clear(); catCopies.Clear();
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            isolated = false; initialized = false; IsFinished = false; SessionId = null;
            integers.Clear(); strings.Clear(); catCopies.Clear();
#if UNITY_EDITOR
            bool armed = UnityEditor.SessionState.GetBool(PendingKey, false);
            string activeId = UnityEditor.SessionState.GetString(ActiveKey, "");
            UnityEditor.SessionState.EraseBool(PendingKey);
            if (!string.IsNullOrEmpty(activeId))
            {
                isolated = true;
                SessionId = activeId;
                initialized = armed;
                // A reload cannot reconstruct the old memory/capabilities. Deny further execution.
                IsFinished = !armed;
            }
            else if (armed)
            {
                isolated = true; IsFinished = true;
                UnityEditor.SessionState.SetString(ActiveKey, "denied-" + Guid.NewGuid().ToString("N"));
                throw new InvalidOperationException("隔離セッションの開始マーカーがありません");
            }
#else
            isolated = Array.IndexOf(Environment.GetCommandLineArgs(), "--nekolpos-isolated-debug") >= 0;
            initialized = isolated;
            if (isolated) SessionId = Guid.NewGuid().ToString("N");
#endif
        }
        // Finishing does not switch stores back: OnDisable/OnQuit must still target the test domain.
        public static void Finish() { IsFinished = true; }
        public static void RequireAccess()
        {
            if (!isolated || !initialized) throw new InvalidOperationException("隔離メモリーを利用できません。Playを終了して新しい隔離セッションを開始してください");
        }
        public static bool HasKey(string key) { RequireAccess(); return integers.ContainsKey(key) || strings.ContainsKey(key); }
        public static int GetInt(string key, int fallback = 0) { RequireAccess(); return integers.TryGetValue(key, out int value) ? value : fallback; }
        public static string GetString(string key, string fallback = "") { RequireAccess(); return strings.TryGetValue(key, out string value) ? value : fallback; }
        public static void SetInt(string key, int value) { RequireAccess(); strings.Remove(key); integers[key] = value; }
        public static void SetString(string key, string value) { RequireAccess(); integers.Remove(key); strings[key] = value; }
        public static void DeleteKey(string key) { RequireAccess(); integers.Remove(key); strings.Remove(key); }
        public static CatDataSO GetRuntimeCatData(CatDataSO source)
        {
            if (!IsIsolated || source == null) return source;
            RequireAccess();
            if (catCopies.ContainsValue(source)) return source;
            if (!catCopies.TryGetValue(source, out var clone)) { clone = UnityEngine.Object.Instantiate(source); clone.hideFlags = HideFlags.DontSave; catCopies.Add(source, clone); }
            return clone;
        }
#else
        public static bool IsIsolated => false;
        public static CatDataSO GetRuntimeCatData(CatDataSO source) => source;
#endif
    }
}
