using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class DebugConsoleReleaseProbe
    {
        public static string Start(BuildTarget target, bool development = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Play/コンパイル終了後に実行してください");
            var player = CompilationPipeline.GetAssemblies(AssembliesType.Player).Single(a => a.name == "Assembly-CSharp");
            string folder = "Library/DebugConsole/ReleaseProbe/" + target + (development ? "-development" : "");
            Directory.CreateDirectory(folder);
            string path = folder + (development ? "/DevelopmentRuntimeProbe.dll" : "/ReleaseRuntimeProbe.dll");
            var builder = new AssemblyBuilder(path, player.sourceFiles) { buildTarget = target, buildTargetGroup = BuildPipeline.GetBuildTargetGroup(target), flags = AssemblyBuilderFlags.None };
            builder.additionalDefines = player.defines.Where(d => d != "UNITY_EDITOR" && !d.StartsWith("UNITY_EDITOR_", StringComparison.Ordinal) && d != "DEVELOPMENT_BUILD" && !d.StartsWith("UNITY_STANDALONE", StringComparison.Ordinal) && d != "UNITY_WEBGL" && d != "UNITY_ANDROID" && d != "UNITY_IOS").ToArray();
            if (development) builder.additionalDefines = builder.additionalDefines.Concat(new[] { "DEVELOPMENT_BUILD" }).ToArray();
            builder.additionalReferences = player.compiledAssemblyReferences;
            if (target == BuildTarget.WebGL)
            {
                string module = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/WebGLSupport/Managed/UnityEngine.WebGLModule.dll");
                if (!File.Exists(module)) throw new FileNotFoundException("Installed WebGL runtime module is unavailable.");
                builder.additionalReferences = builder.additionalReferences.Concat(new[] { module }).ToArray();
            }
            builder.excludeReferences = new[] { player.outputPath };
            builder.buildFinished += (builtPath, messages) =>
            {
                int errors = messages.Count(m => m.type == CompilerMessageType.Error);
                bool hidden = false;
                if (errors == 0)
                {
                    hidden = InspectDebugApis(builtPath);
                }
                File.WriteAllText(folder + "/result.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { target = target.ToString(), development, errors,
                    warnings = messages.Count(m => m.type == CompilerMessageType.Warning), debugApisAbsent = hidden,
                    expectedApiCheckPassed = development ? errors == 0 && !hidden : hidden,
                    warningSamples = messages.Where(m => m.type == CompilerMessageType.Warning).Take(3).Select(m => m.message).ToArray(),
                    diagnostics = messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message).ToArray() }, Newtonsoft.Json.Formatting.Indented));
            };
            File.Delete(folder + "/result.json");
            if (!builder.Build()) throw new InvalidOperationException("Release条件コンパイルを開始できません");
            return folder + "/result.json";
        }
        // Inspect metadata without loading another game assembly or running its static initialization.
        public static bool InspectDebugApis(string path)
        {
            var reader = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Unity.Cecil").GetType("Mono.Cecil.AssemblyDefinition");
            using var assembly = (IDisposable)reader.GetMethod("ReadAssembly", new[] { typeof(string) }).Invoke(null, new object[] { Path.GetFullPath(path) });
            var module = reader.GetProperty("MainModule").GetValue(assembly);
            var types = ((global::System.Collections.IEnumerable)module.GetType().GetProperty("Types").GetValue(module)).Cast<object>().ToArray();
            object Find(string name) => types.FirstOrDefault(t => (string)t.GetType().GetProperty("FullName").GetValue(t) == name);
            bool HasMethod(string type, string method)
            {
                var t = Find(type); if (t == null) return false;
                return ((global::System.Collections.IEnumerable)t.GetType().GetProperty("Methods").GetValue(t)).Cast<object>().Any(m => (string)m.GetType().GetProperty("Name").GetValue(m) == method);
            }
            return Find("Nekolpos.System.DebugScenarioExecutor") == null && Find("Nekolpos.System.DebugScenarioCatalog") == null && Find("Nekolpos.System.DebugConsoleGuard") == null &&
                !HasMethod("Nekolpos.System.NekolposDebugConsole", "Open") && !HasMethod("Nekolpos.System.NekolposGameBootstrap", "BeginIsolatedGameplay") &&
                !HasMethod("Backgammon.Conversation.ConversationDebugPanel", "ApplyStateEdit") && !HasMethod("Backgammon.Conversation.ConversationPersonalityDebugView", "SetValue") &&
                !HasMethod("Nekolpos.System.DialogueManager", "DebugAdvanceToNextPeriod") && !HasMethod("Nekolpos.TimeSystem.TimeManager", "ResetGameStartDateForDebug");
        }
    }
}
