using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>Progress persistence; device preferences remain in PlayerPrefs.</summary>
    public static class ProgressSaveStore
    {
        public const string FileName = "progress-v1.json";
        private static ProgressSaveFile file;

        // Editor play/tests must not modify the installed game's cloud save.
        private static bool UsesFile => !Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => file = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (!DebugSaveSession.IsIsolated && UsesFile) GetFile();
        }

        private static ProgressSaveFile GetFile()
        {
            if (file != null) return file;
            var loaded = new ProgressSaveFile(Path.Combine(Application.persistentDataPath, "Saves", FileName));
            if (!loaded.Exists)
            {
                ImportLegacy(loaded);
                loaded.Save();
            }
            file = loaded;
            return file;
        }

        public static bool HasKey(string key)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) return DebugSaveSession.HasKey(key);
#endif
            return UsesFile ? GetFile().HasKey(key) : PlayerPrefs.HasKey(key);
        }
        public static int GetInt(string key, int fallback = 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) return DebugSaveSession.GetInt(key, fallback);
#endif
            return UsesFile ? GetFile().GetInt(key, fallback) : PlayerPrefs.GetInt(key, fallback);
        }
        public static string GetString(string key, string fallback = "")
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) return DebugSaveSession.GetString(key, fallback);
#endif
            return UsesFile ? GetFile().GetString(key, fallback) : PlayerPrefs.GetString(key, fallback);
        }
        public static void SetInt(string key, int value)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) { DebugSaveSession.SetInt(key, value); return; }
#endif
            if (UsesFile) GetFile().SetInt(key, value); else PlayerPrefs.SetInt(key, value);
        }
        public static void SetString(string key, string value)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) { DebugSaveSession.SetString(key, value); return; }
#endif
            if (UsesFile) GetFile().SetString(key, value); else PlayerPrefs.SetString(key, value);
        }
        public static void DeleteKey(string key)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) { DebugSaveSession.DeleteKey(key); return; }
#endif
            if (UsesFile) GetFile().DeleteKey(key); else PlayerPrefs.DeleteKey(key);
        }
        public static void Save()
        {
            if (DebugSaveSession.IsIsolated) return;
            if (UsesFile) GetFile().Save(); else PlayerPrefs.Save();
        }

        private static void ImportLegacy(ProgressSaveFile target)
        {
            foreach (string key in new[] { TimeSystem.TimeManager.CurrentDayPrefsKey, "Nekolpos.Diary.SpecialSortUnlocked.v1" })
                CopyInt(target, key);
            foreach (string key in new[] {
                StatusSystem.StatusManager.SaveKey,
                Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey,
                "Nekolpos.Time.GameStartDateTicks", "Nekolpos.Diary.Entries.v1", "Nekolpos.Diary.Drafts.v1",
                "Nekolpos.Diary.SpecialPending.v1", "Nekolpos.Diary.VariantSequence.v1", "Nekolpos.PlayerDefeat.LastReason",
                "SaveData.PLAYER_NAME", "SaveData.CAT_NAME", "SaveData.PLAYER_CALLING", "SaveData.CAT_PRONOUN",
                "OpenBeta.PlayerName", "OpenBeta.CatName", "OpenBeta.PlayerCalling" })
                CopyString(target, key);

            // Discovery lists are the existing portable index of dynamic PlayerPrefs keys.
            const string prefix = "Nekolpos.TeachingDiscovery.";
            foreach (TeachingCategory category in Enum.GetValues(typeof(TeachingCategory)))
            {
                if (category == TeachingCategory.None) continue;
                CopyInt(target, prefix + "Count." + category);
                string listKey = prefix + "KnownEntries." + category;
                CopyString(target, listKey);
                foreach (string entry in PlayerPrefs.GetString(listKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    CopyInt(target, prefix + "Entry." + category + "." + entry.Trim());
            }
            CopyInt(target, prefix + "Count.CatSeries.SeriesGroup");
            CopyString(target, prefix + "KnownSeries");
            foreach (string series in PlayerPrefs.GetString(prefix + "KnownSeries", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                CopyInt(target, prefix + "Series." + series.Trim());
            foreach (string reason in DialogueManager.PersistentPlayerDefeatReasons)
                CopyInt(target, DialogueManager.PlayerDefeatReasonPrefsPrefix + reason);
        }

        private static void CopyInt(ProgressSaveFile target, string key) { if (PlayerPrefs.HasKey(key)) target.SetInt(key, PlayerPrefs.GetInt(key)); }
        private static void CopyString(ProgressSaveFile target, string key) { if (PlayerPrefs.HasKey(key)) target.SetString(key, PlayerPrefs.GetString(key)); }
    }

    /// <summary>File backend with injectable path for isolated persistence tests.</summary>
    public sealed class ProgressSaveFile
    {
        private sealed class Document
        {
            [JsonProperty(Required = Required.Always)] public int version = 1;
            [JsonProperty(Required = Required.Always)] public Dictionary<string, int> integers = new Dictionary<string, int>();
            [JsonProperty(Required = Required.Always)] public Dictionary<string, string> strings = new Dictionary<string, string>();
        }

        private readonly string path;
        private Document data = new Document();
        public bool Exists { get; private set; }

        public ProgressSaveFile(string path)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            try
            {
                data = Read(path);
            }
            catch (NotSupportedException) { throw; }
            catch (Exception error) when (error is IOException || error is JsonException || error is ArgumentException)
            {
                // Never silently replace damaged progress with an empty or stale registry save.
                try { data = Read(path + ".bak"); }
                catch (Exception backupError) when (backupError is IOException || backupError is JsonException || backupError is ArgumentException)
                {
                    throw new InvalidDataException("Progress save and backup cannot be read: " + path,
                        new AggregateException(error, backupError));
                }
                Debug.LogWarning("Progress save recovered from local backup: " + error.Message);
                File.Copy(path + ".bak", path, true);
            }
            Exists = true;
        }

        private static Document Read(string path)
        {
            var document = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path));
            if (document == null || document.integers == null || document.strings == null)
                throw new InvalidDataException("Invalid progress save: " + path);
            if (document.version != 1) throw new NotSupportedException("Unsupported progress save version: " + document.version);
            if (document.integers.Keys.Concat(document.strings.Keys).Any(key => !IsProgressKey(key)) ||
                document.integers.Keys.Any(document.strings.ContainsKey) || document.strings.Values.Any(value => value == null))
                throw new InvalidDataException("Invalid progress save keys or values: " + path);
            return document;
        }

        private static bool IsProgressKey(string key) => key != null && (
            key == Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey || key == StatusSystem.StatusManager.SaveKey ||
            key == "Nekolpos.Debug.StartDay.v1" || key == "Nekolpos.Time.GameStartDateTicks" ||
            key.StartsWith("Nekolpos.Diary.", StringComparison.Ordinal) ||
            key.StartsWith("Nekolpos.TeachingDiscovery.", StringComparison.Ordinal) ||
            key.StartsWith("Nekolpos.PlayerDefeat.", StringComparison.Ordinal) ||
            key == "SaveData.PLAYER_NAME" || key == "SaveData.CAT_NAME" || key == "SaveData.PLAYER_CALLING" || key == "SaveData.CAT_PRONOUN" ||
            key == "OpenBeta.PlayerName" || key == "OpenBeta.CatName" || key == "OpenBeta.PlayerCalling");

        private static void ValidateKey(string key)
        {
            if (!IsProgressKey(key)) throw new ArgumentException("Not a progress save key: " + key, nameof(key));
        }

        public bool HasKey(string key) { ValidateKey(key); return data.integers.ContainsKey(key) || data.strings.ContainsKey(key); }
        public int GetInt(string key, int fallback = 0) { ValidateKey(key); return data.integers.TryGetValue(key, out int value) ? value : fallback; }
        public string GetString(string key, string fallback = "") { ValidateKey(key); return data.strings.TryGetValue(key, out string value) ? value : fallback; }
        public void SetInt(string key, int value) { ValidateKey(key); data.strings.Remove(key); data.integers[key] = value; }
        public void SetString(string key, string value) { ValidateKey(key); data.integers.Remove(key); data.strings[key] = value ?? ""; }
        public void DeleteKey(string key) { ValidateKey(key); data.integers.Remove(key); data.strings.Remove(key); }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            Exists = true;
        }
    }
}
