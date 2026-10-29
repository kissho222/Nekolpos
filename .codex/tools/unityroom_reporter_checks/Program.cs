using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Nekolpos.System;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int failures;
#if UNITY_WEBGL && !UNITY_EDITOR
    private const bool platformAllowed = true;
#else
    private const bool platformAllowed = false;
#endif
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static unityroom.Api.ScoreCall Call(int board, float score, unityroom.Api.ScoreboardWriteMode mode = unityroom.Api.ScoreboardWriteMode.Always) => new(board, score, mode);
    private static unityroom.Api.ScoreCall[] Both() => new[] { Call(1, 7), Call(2, 11) };

    private static void Check(string name, bool isolated, Action<UnityRoomTeachingRankingReporter> action, unityroom.Api.ScoreCall[] expected, int warnings = 0)
    {
        TeachingDiscoveryStore.Reset();
        DebugSaveSession.IsIsolated = isolated;
        UnityEngine.Debug.Warnings.Clear();
        unityroom.Api.UnityroomApiClient.Reset();
        var reporter = new UnityRoomTeachingRankingReporter();
        bool passed;
        string error = "";
        try
        {
            Invoke(reporter, "OnEnable");
            action(reporter);
            bool allowed = platformAllowed && !isolated;
            var expectedCalls = allowed ? expected : Array.Empty<unityroom.Api.ScoreCall>();
            passed = unityroom.Api.UnityroomApiClient.Calls.SequenceEqual(expectedCalls)
                && UnityEngine.Debug.Warnings.Count == (allowed ? warnings : 0)
                && (allowed || unityroom.Api.UnityroomApiClient.InstanceReads == 0);
        }
        catch (Exception ex) { passed = false; error = ex.ToString(); }
        finally { Invoke(reporter, "OnDisable"); }
        if (!passed) failures++;
        Results.Add(new { name, isolated, passed, instanceReads = unityroom.Api.UnityroomApiClient.InstanceReads, sends = unityroom.Api.UnityroomApiClient.Calls.ToArray(), warningCount = UnityEngine.Debug.Warnings.Count, error });
    }

    private static void Entrypoints(bool isolated)
    {
        string prefix = isolated ? "isolated-" : "normal-";
        Check(prefix + "manual", isolated, r => r.SubmitCurrentScores(), Both());
        Check(prefix + "startup", isolated, r => Invoke(r, "Start"), Both());
        Check(prefix + "new-series", isolated, r => TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { Category = TeachingCategory.CatSeries, SeriesWasNew = true }), new[] { Call(1, 7) });
        Check(prefix + "new-expression", isolated, r => TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { Category = TeachingCategory.CatExpression, EntryWasNew = true }), new[] { Call(2, 11) });
        Check(prefix + "reset", isolated, r => TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { WasReset = true }), Both());
    }

    public static int Main()
    {
        Entrypoints(false);
        Entrypoints(true); // A policy double: release's actual IsIsolated is always false.
        Check("unchanged-discovery", false, r => TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { Category = TeachingCategory.CatSeries }), Array.Empty<unityroom.Api.ScoreCall>());
        Check("unsubscribed", false, r => { Invoke(r, "OnDisable"); TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { WasReset = true }); }, Array.Empty<unityroom.Api.ScoreCall>());
        Check("startup-disabled", false, r => { Set(r, "submitOnStart", false); Invoke(r, "Start"); }, Array.Empty<unityroom.Api.ScoreCall>());
        Check("changes-disabled", false, r => { Set(r, "submitWhenChanged", false); TeachingDiscoveryStore.Raise(new DiscoveryMarkResult { WasReset = true }); }, Array.Empty<unityroom.Api.ScoreCall>());
        Check("invalid-board", false, r => { Set(r, "catSeriesBoardNo", 0); r.SubmitCurrentScores(); }, new[] { Call(2, 11) }, 1);
        Check("configured-boards-and-mode", false, r => { Set(r, "catSeriesBoardNo", 3); Set(r, "catExpressionBoardNo", 4); Set(r, "scoreboardWriteMode", "HighScoreDesc"); r.SubmitCurrentScores(); }, new[] { Call(3, 7, unityroom.Api.ScoreboardWriteMode.HighScoreDesc), Call(4, 11, unityroom.Api.ScoreboardWriteMode.HighScoreDesc) });
        Check("invalid-mode-falls-back", false, r => { Set(r, "scoreboardWriteMode", "INVALID"); r.SubmitCurrentScores(); }, Both());
        Check("missing-client-warns-once", false, r => { unityroom.Api.UnityroomApiClient.Available = false; r.SubmitCurrentScores(); r.SubmitCurrentScores(); }, Array.Empty<unityroom.Api.ScoreCall>(), 1);
        Console.WriteLine(JsonSerializer.Serialize(new { platformAllowed, count = Results.Count, failures, results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}

namespace UnityEngine
{
    public class MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public static class Debug { public static readonly List<string> Warnings = new(); public static void LogWarning(object value) => Warnings.Add(value?.ToString() ?? ""); }
}
namespace Nekolpos.System
{
    public static class DebugSaveSession { public static bool IsIsolated { get; set; } }
    public enum TeachingCategory { None, CatSeries, CatExpression }
    public struct DiscoveryMarkResult { public bool WasReset; public TeachingCategory Category; public bool SeriesWasNew; public bool EntryWasNew; }
    public static class TeachingDiscoveryStore
    {
        public static event Action<DiscoveryMarkResult>? DiscoveryChanged;
        public static int GetCatSeriesRankingValue() => 7;
        public static int GetCatExpressionRankingValue() => 11;
        public static void Raise(DiscoveryMarkResult result) => DiscoveryChanged?.Invoke(result);
        public static void Reset() => DiscoveryChanged = null;
    }
}
namespace unityroom.Api
{
    public enum ScoreboardWriteMode { Always, HighScoreAsc, HighScoreDesc }
    public record ScoreCall(int Board, float Score, ScoreboardWriteMode Mode);
    public sealed class UnityroomApiClient
    {
        private static readonly UnityroomApiClient singleton = new();
        public static readonly List<ScoreCall> Calls = new();
        public static int InstanceReads;
        public static bool Available;
        public static UnityroomApiClient? Instance { get { InstanceReads++; return Available ? singleton : null; } }
        public void SendScore(int boardNo, float score, ScoreboardWriteMode mode) => Calls.Add(new ScoreCall(boardNo, score, mode));
        public static void Reset() { InstanceReads = 0; Available = true; Calls.Clear(); }
    }
}
