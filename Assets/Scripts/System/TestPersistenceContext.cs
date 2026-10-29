using System;
using System.Collections.Generic;
using System.IO;

namespace Nekolpos.System
{
    /// <summary>Memory-only persistence capability for a dedicated, preconfigured test worker.</summary>
    public sealed class TestPersistenceDomain
    {
        public readonly struct Token
        {
            internal readonly string Epoch;
            internal readonly string Owner;
            internal Token(string epoch, string owner) { Epoch = epoch; Owner = owner; }
        }

        private readonly bool required;
        private readonly bool validManifest;
        private readonly string epoch = Guid.NewGuid().ToString("N");
        private readonly Action markStarted;
        private readonly string configuredOwner;
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();
        private string owner;
        private bool attempted;
        private bool ended;
        public bool Required => required;
        public string RunId => configuredOwner;

        public TestPersistenceDomain(bool required, bool validManifest, Action markStarted, string configuredOwner = null)
        {
            this.required = required;
            this.validManifest = validManifest;
            this.markStarted = markStarted;
            this.configuredOwner = configuredOwner;
        }

        public Token Begin(string runOwner)
        {
            RequireStartup();
            if (string.IsNullOrWhiteSpace(runOwner) || (configuredOwner != null && configuredOwner != runOwner))
                throw new InvalidOperationException("Test persistence context cannot be opened.");
            attempted = true;
            // Publish the process tombstone before granting access; reload must never reopen it.
            markStarted?.Invoke();
            owner = runOwner;
            return new Token(epoch, owner);
        }

        public void RequireStartup()
        {
            if (!required || !validManifest || attempted || ended)
                throw new InvalidOperationException("Test worker startup is unavailable or terminal.");
        }

        public void RejectStartup()
        {
            if (!required) return;
            attempted = true;
            ended = true;
            owner = null;
            values.Clear();
        }

        public Token Capture()
        {
            if (!required) return default;
            if (owner == null || ended) throw new InvalidOperationException("Test persistence access denied: no active capability.");
            return new Token(epoch, owner);
        }

        public void Validate(Token token)
        {
            if (!required) return;
            Capture();
            if (token.Epoch != epoch || token.Owner != owner)
                throw new InvalidOperationException("Test persistence access denied: stale or foreign capability.");
        }

        public void End(Token token)
        {
            Validate(token);
            ended = true;
            owner = null;
            values.Clear();
        }

        public bool Has(string key) { Capture(); return values.ContainsKey(key); }
        public T Get<T>(string key, T fallback) { Capture(); return values.TryGetValue(key, out object value) && value is T typed ? typed : fallback; }
        public void Set<T>(string key, T value) { Capture(); values[key] = value; }
        public void Delete(string key) { Capture(); values.Remove(key); }
    }

    /// <summary>Can publish failure even when the persistence context's static initializer failed.</summary>
    public static class TestWorkerFailureLatch
    {
        internal const string MarkerKey = "NEKOLPOS_TEST_STARTED";
        public static void Publish(Exception original)
        {
#if NEKOLPOS_TEST_WORKER
            PublishForHost(original,
                () => Environment.SetEnvironmentVariable(MarkerKey, "failed"),
                failure => Environment.FailFast("Test worker cannot publish its terminal marker.", failure));
#endif
        }

        internal static void PublishForHost(Exception original, Action publish, Action<Exception> terminate)
        {
            try { publish(); }
            catch (Exception cleanup)
            {
                var failure = new AggregateException("Worker startup and terminal-marker publication failed.", original, cleanup);
                terminate(failure);
                // A fake host may return; never allow that path to continue startup.
                throw failure;
            }
        }
    }

    public static class TestPersistenceContext
    {
#if NEKOLPOS_TEST_WORKER
        private const bool WorkerBuild = true;
#else
        private const bool WorkerBuild = false;
#endif
        private const string StartedKey = TestWorkerFailureLatch.MarkerKey;
        private static readonly TestPersistenceDomain domain = CreateDomain();
        public static bool Required => domain.Required;
        public static TestPersistenceDomain.Token Capture() => domain.Capture();
        public static void Validate(TestPersistenceDomain.Token token) => domain.Validate(token);
        public static void RequireAccess() => domain.Capture();
        public static void RequireStartup() => domain.RequireStartup();
        public static void RejectStartup() => domain.RejectStartup();

        private static TestPersistenceDomain CreateDomain()
        {
#if UNITY_EDITOR || NEKOLPOS_TEST_WORKER
            string markerValue = Environment.GetEnvironmentVariable("NEKOLPOS_TEST_REQUIRED");
            bool marker = markerValue == "1";
#else
            string markerValue = null;
            bool marker = false;
#endif
            bool required = WorkerBuild || markerValue != null;
            bool valid = false;
            string runId = null;
            if (required && WorkerBuild && marker && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StartedKey)))
            {
                try
                {
                    string manifest = Environment.GetEnvironmentVariable("NEKOLPOS_TEST_MANIFEST");
                    string run = Environment.GetEnvironmentVariable("NEKOLPOS_TEST_RUN_ID");
                    runId = run;
                    valid = Guid.TryParseExact(run, "N", out _) && !string.IsNullOrEmpty(manifest) &&
                        Path.IsPathRooted(manifest) && File.ReadAllText(manifest) == "NEKOLPOS_TEST_V1\n" + run + "\nMEMORY_ONLY\n";
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                { valid = false; }
            }
            return new TestPersistenceDomain(required, valid,
                () => Environment.SetEnvironmentVariable(StartedKey, "started"), runId);
        }

        // Called by the dedicated worker bootstrap before test discovery, never by RunStarted.
        public static TestPersistenceDomain.Token BeginDiscovery()
        {
            return domain.Begin(domain.RunId);
        }

        public static void EndDiscovery(TestPersistenceDomain.Token token) => domain.End(token);
        public static bool Has(string area, string key) => domain.Has(area + ":" + key);
        public static T Get<T>(string area, string key, T fallback) => domain.Get(area + ":" + key, fallback);
        public static void Set<T>(string area, string key, T value) => domain.Set(area + ":" + key, value);
        public static void Delete(string area, string key) => domain.Delete(area + ":" + key);
        public static void DenyDisk()
        {
            if (Required) throw new InvalidOperationException("Disk/PlayerPrefs adapter tests are disabled in the memory-only worker.");
        }
    }
}
