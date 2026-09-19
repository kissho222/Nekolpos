using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Nekolpos.System
{
    public sealed class RuntimePerformanceDiagnostics : MonoBehaviour
    {
        private const string ObjectName = "RuntimePerformanceDiagnostics";
        private const string EnableCommandLineArgument = "-perfdiag";
        private const string EnableEnvironmentVariable = "NEKOLPOS_PERF_DIAG";
        private const string EnablePlayerPrefsKey = "Nekolpos.PerfDiag.Enabled";
        private const float DefaultLogIntervalSeconds = 30f;
        private const float SlowFrameThresholdMilliseconds = 50f;
        private const float LowFpsWarningThreshold = 25f;
        private const long MemoryGrowthWarningBytes = 32L * 1024L * 1024L;
        private const float DefaultObjectCountIntervalSeconds = 60f;
        private static readonly UTF8Encoding CsvEncoding = new UTF8Encoding(false);

        [SerializeField] private float logIntervalSeconds = DefaultLogIntervalSeconds;
        [SerializeField] private bool logOnlyWhenSuspicious;
        [SerializeField] private bool includeUnityObjectCounts;
        [SerializeField] private bool sampleObjectCountsWhenSuspicious = true;
        [SerializeField] private bool writeCsv = true;
        [SerializeField] private float objectCountIntervalSeconds = DefaultObjectCountIntervalSeconds;

        private float intervalElapsed;
        private float objectCountElapsed;
        private int intervalFrames;
        private int intervalSlowFrames;
        private int intervalLogMessages;
        private int intervalWarnings;
        private int intervalErrors;
        private float intervalMinFps = float.MaxValue;
        private float intervalMaxFrameMs;
        private long previousAllocatedMemory;
        private long previousManagedMemory;
        private int previousGen0Collections;
        private int previousGen1Collections;
        private int previousGen2Collections;
        private string csvPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateForDebugRuntime()
        {
            if (!ShouldRun())
            {
                return;
            }

            if (FindFirstObjectByType<RuntimePerformanceDiagnostics>() != null)
            {
                return;
            }

            GameObject diagnosticsObject = new GameObject(ObjectName);
            diagnosticsObject.AddComponent<RuntimePerformanceDiagnostics>();
            DontDestroyOnLoad(diagnosticsObject);
        }

        private static bool ShouldRun()
        {
#if PERF_DIAG
            return true;
#elif UNITY_EDITOR
            return true;
#elif DEVELOPMENT_BUILD
            return Debug.isDebugBuild || IsExplicitlyEnabled();
#else
            return false;
#endif
        }

        private static bool IsExplicitlyEnabled()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], EnableCommandLineArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string environmentValue = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
            if (!string.IsNullOrEmpty(environmentValue) &&
                (environmentValue == "1" ||
                 string.Equals(environmentValue, "true", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(environmentValue, "yes", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

#if UNITY_EDITOR
            return PlayerPrefs.GetInt(EnablePlayerPrefsKey, 0) != 0;
#else
            return false;
#endif
        }

        private void Awake()
        {
            if (!ShouldRun())
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            previousAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            previousManagedMemory = GC.GetTotalMemory(false);
            previousGen0Collections = GC.CollectionCount(0);
            previousGen1Collections = GC.CollectionCount(1);
            previousGen2Collections = GC.CollectionCount(2);
            Application.logMessageReceived += HandleLogMessageReceived;
            EnsureCsvHeader();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLogMessageReceived;
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            intervalElapsed += deltaTime;
            objectCountElapsed += deltaTime;
            intervalFrames++;

            float frameMs = deltaTime * 1000f;
            float fps = 1f / deltaTime;
            intervalMinFps = Mathf.Min(intervalMinFps, fps);
            intervalMaxFrameMs = Mathf.Max(intervalMaxFrameMs, frameMs);
            if (frameMs >= SlowFrameThresholdMilliseconds)
            {
                intervalSlowFrames++;
            }

            float interval = Mathf.Max(1f, logIntervalSeconds);
            if (intervalElapsed < interval)
            {
                return;
            }

            EmitReport();
            ResetInterval();
        }

        private void EmitReport()
        {
            float averageFps = intervalElapsed > 0f ? intervalFrames / intervalElapsed : 0f;
            long allocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            long reservedMemory = Profiler.GetTotalReservedMemoryLong();
            long monoUsedMemory = Profiler.GetMonoUsedSizeLong();
            long managedMemory = GC.GetTotalMemory(false);
            long allocatedDelta = allocatedMemory - previousAllocatedMemory;
            long managedDelta = managedMemory - previousManagedMemory;
            int gen0Delta = GC.CollectionCount(0) - previousGen0Collections;
            int gen1Delta = GC.CollectionCount(1) - previousGen1Collections;
            int gen2Delta = GC.CollectionCount(2) - previousGen2Collections;
            DialogueLogManager logManager = DialogueLogManager.Instance;
            int logCount = logManager != null ? logManager.RuntimeLogCount : -1;
            int historyCount = logManager != null ? logManager.RuntimeHistoryCacheCount : -1;
            bool historyLoaded = logManager != null && logManager.RuntimeHistoryCacheLoaded;

            bool suspicious =
                averageFps < LowFpsWarningThreshold ||
                intervalSlowFrames > 0 ||
                allocatedDelta >= MemoryGrowthWarningBytes ||
                managedDelta >= MemoryGrowthWarningBytes ||
                gen1Delta > 0 ||
                gen2Delta > 0;

            if (logOnlyWhenSuspicious && !suspicious)
            {
                StorePreviousCounters(allocatedMemory, managedMemory);
                return;
            }

            ObjectCountSnapshot objectCounts = ShouldSampleObjectCounts(suspicious)
                ? BuildObjectCountSnapshot()
                : ObjectCountSnapshot.Empty;

            string message =
                (suspicious ? "[PerfDiag][Suspicious] " : "[PerfDiag] ") +
                $"t={Time.realtimeSinceStartup:F0}s, scene={SceneManager.GetActiveScene().name}, " +
                $"fps(avg={averageFps:F1}, min={intervalMinFps:F1}), " +
                $"maxFrame={intervalMaxFrameMs:F1}ms, slowFrames>={SlowFrameThresholdMilliseconds:F0}ms:{intervalSlowFrames}, " +
                $"mem(alloc={FormatBytes(allocatedMemory)}, reserved={FormatBytes(reservedMemory)}, mono={FormatBytes(monoUsedMemory)}, managed={FormatBytes(managedMemory)}), " +
                $"delta(alloc={FormatSignedBytes(allocatedDelta)}, managed={FormatSignedBytes(managedDelta)}), " +
                $"gc(gen0={gen0Delta}, gen1={gen1Delta}, gen2={gen2Delta}), " +
                $"console(log={intervalLogMessages}, warn={intervalWarnings}, error={intervalErrors}), " +
                $"dialogueLogs(runtime={logCount}, history={historyCount}, historyLoaded={historyLoaded})" +
                objectCounts.ToLogSuffix();

            Debug.Log(message, this);

            AppendCsvReport(
                averageFps,
                allocatedMemory,
                reservedMemory,
                monoUsedMemory,
                managedMemory,
                allocatedDelta,
                managedDelta,
                gen0Delta,
                gen1Delta,
                gen2Delta,
                logCount,
                historyCount,
                historyLoaded,
                objectCounts);
            StorePreviousCounters(allocatedMemory, managedMemory);
        }

        private void StorePreviousCounters(long allocatedMemory, long managedMemory)
        {
            previousAllocatedMemory = allocatedMemory;
            previousManagedMemory = managedMemory;
            previousGen0Collections = GC.CollectionCount(0);
            previousGen1Collections = GC.CollectionCount(1);
            previousGen2Collections = GC.CollectionCount(2);
        }

        private void ResetInterval()
        {
            intervalElapsed = 0f;
            intervalFrames = 0;
            intervalSlowFrames = 0;
            intervalLogMessages = 0;
            intervalWarnings = 0;
            intervalErrors = 0;
            intervalMinFps = float.MaxValue;
            intervalMaxFrameMs = 0f;
        }

        private void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) && condition.StartsWith("[PerfDiag]", StringComparison.Ordinal))
            {
                return;
            }

            switch (type)
            {
                case LogType.Warning:
                    intervalWarnings++;
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    intervalErrors++;
                    break;
                default:
                    intervalLogMessages++;
                    break;
            }
        }

        private bool ShouldSampleObjectCounts(bool suspicious)
        {
            if (includeUnityObjectCounts)
            {
                return true;
            }

            if (!sampleObjectCountsWhenSuspicious || !suspicious)
            {
                return false;
            }

            if (objectCountElapsed < Mathf.Max(logIntervalSeconds, objectCountIntervalSeconds))
            {
                return false;
            }

            objectCountElapsed = 0f;
            return true;
        }

        private static ObjectCountSnapshot BuildObjectCountSnapshot()
        {
            return new ObjectCountSnapshot
            {
                HasValue = true,
                GameObject = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length,
                Transform = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length,
                MonoBehaviour = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Length,
                Canvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length,
                Graphic = FindObjectsByType<Graphic>(FindObjectsSortMode.None).Length,
                TmpText = FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Length,
                Renderer = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length,
                Animator = FindObjectsByType<Animator>(FindObjectsSortMode.None).Length,
                PlayableDirector = FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None).Length,
                AudioSource = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length,
                ParticleSystem = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length
            };
        }

        private void EnsureCsvHeader()
        {
            if (!writeCsv || !string.IsNullOrWhiteSpace(csvPath))
            {
                return;
            }

            string directory = Path.Combine(Application.persistentDataPath, "Logs", "PerfDiag");
            Directory.CreateDirectory(directory);
            csvPath = Path.Combine(directory, $"perfdiag_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(
                csvPath,
                "realtime,scene,avgFps,minFps,maxFrameMs,slowFrames,allocMiB,reservedMiB,monoMiB,managedMiB,allocDeltaMiB,managedDeltaMiB,gc0,gc1,gc2,logs,warnings,errors,dialogueRuntime,dialogueHistory,historyLoaded,gameObjects,transforms,monoBehaviours,canvases,graphics,tmpTexts,renderers,animators,playableDirectors,audioSources,particleSystems\n",
                CsvEncoding);
        }

        private void AppendCsvReport(
            float averageFps,
            long allocatedMemory,
            long reservedMemory,
            long monoUsedMemory,
            long managedMemory,
            long allocatedDelta,
            long managedDelta,
            int gen0Delta,
            int gen1Delta,
            int gen2Delta,
            int logCount,
            int historyCount,
            bool historyLoaded,
            ObjectCountSnapshot objectCounts)
        {
            if (!writeCsv)
            {
                return;
            }

            EnsureCsvHeader();
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                return;
            }

            string line =
                $"{Time.realtimeSinceStartup:F1},{EscapeCsv(SceneManager.GetActiveScene().name)},{averageFps:F2},{intervalMinFps:F2},{intervalMaxFrameMs:F2},{intervalSlowFrames}," +
                $"{BytesToMiB(allocatedMemory):F2},{BytesToMiB(reservedMemory):F2},{BytesToMiB(monoUsedMemory):F2},{BytesToMiB(managedMemory):F2}," +
                $"{BytesToMiB(allocatedDelta):F2},{BytesToMiB(managedDelta):F2},{gen0Delta},{gen1Delta},{gen2Delta}," +
                $"{intervalLogMessages},{intervalWarnings},{intervalErrors},{logCount},{historyCount},{historyLoaded}," +
                objectCounts.ToCsvColumns();
            File.AppendAllText(csvPath, line + Environment.NewLine, CsvEncoding);
        }

        private static string FormatBytes(long bytes)
        {
            double mib = BytesToMiB(bytes);
            return $"{mib:F1}MiB";
        }

        private static string FormatSignedBytes(long bytes)
        {
            string sign = bytes >= 0 ? "+" : "-";
            return sign + FormatBytes(Math.Abs(bytes));
        }

        private static double BytesToMiB(long bytes)
        {
            return bytes / (1024d * 1024d);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private struct ObjectCountSnapshot
        {
            public static readonly ObjectCountSnapshot Empty = default;

            public bool HasValue;
            public int GameObject;
            public int Transform;
            public int MonoBehaviour;
            public int Canvas;
            public int Graphic;
            public int TmpText;
            public int Renderer;
            public int Animator;
            public int PlayableDirector;
            public int AudioSource;
            public int ParticleSystem;

            public string ToLogSuffix()
            {
                if (!HasValue)
                {
                    return string.Empty;
                }

                return
                    ", objects(" +
                    $"GameObject={GameObject}, Transform={Transform}, MonoBehaviour={MonoBehaviour}, Canvas={Canvas}, " +
                    $"Graphic={Graphic}, TMP_Text={TmpText}, Renderer={Renderer}, Animator={Animator}, " +
                    $"PlayableDirector={PlayableDirector}, AudioSource={AudioSource}, ParticleSystem={ParticleSystem})";
            }

            public string ToCsvColumns()
            {
                if (!HasValue)
                {
                    return ",,,,,,,,,,";
                }

                return $"{GameObject},{Transform},{MonoBehaviour},{Canvas},{Graphic},{TmpText},{Renderer},{Animator},{PlayableDirector},{AudioSource},{ParticleSystem}";
            }
        }
    }
}
