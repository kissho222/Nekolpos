using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class ReleaseDiagBuildMenu
    {
        private const string OutputPath = "Build_Release_Diag/Nekolpos.exe";
        private const string WebGLOutputPath = "Build/WebGL";
        private static readonly string[] Scenes = { "Assets/Scenes/TitleScene.unity" };

        [MenuItem("Nekolpos/Build/Windows Release Diagnostic")]
        public static void BuildReleaseDiag()
        {
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CleanBuildCache
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[ReleaseDiagBuildMenu] Build failed: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}");
                return;
            }

            Debug.Log($"[ReleaseDiagBuildMenu] Build succeeded: {summary.outputPath}, warnings={summary.totalWarnings}");
        }

        [MenuItem("Nekolpos/Build/WebGL")]
        public static void BuildWebGL()
        {
            BuildWebGL(BuildOptions.None, "WebGL");
        }

        [MenuItem("Nekolpos/Build/WebGL Clean")]
        public static void BuildWebGLClean()
        {
            BuildWebGL(BuildOptions.CleanBuildCache, "WebGL clean");
        }

        private static void BuildWebGL(BuildOptions buildOptions, string label)
        {
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = WebGLOutputPath,
                target = BuildTarget.WebGL,
                options = buildOptions
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[ReleaseDiagBuildMenu] {label} build failed: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}");
                return;
            }

            Debug.Log($"[ReleaseDiagBuildMenu] {label} build succeeded: {summary.outputPath}, warnings={summary.totalWarnings}");
        }
    }
}
