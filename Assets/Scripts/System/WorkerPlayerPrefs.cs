using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>Preserves normal preferences; dedicated workers can only access their memory domain.</summary>
    public static class WorkerPlayerPrefs
    {
        public static int GetInt(string key, int fallback = 0)
        {
            if (TestPersistenceContext.Required) return TestPersistenceContext.Get("legacyPrefs", key, fallback);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) return DebugSaveSession.GetInt("legacyPrefs/" + key, fallback);
#endif
            return PlayerPrefs.GetInt(key, fallback);
        }
        public static string GetString(string key, string fallback = "")
        {
            if (TestPersistenceContext.Required) return TestPersistenceContext.Get("legacyPrefs", key, fallback);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugSaveSession.IsIsolated) return DebugSaveSession.GetString("legacyPrefs/" + key, fallback);
#endif
            return PlayerPrefs.GetString(key, fallback);
        }
        public static void SetInt(string key, int value)
        {
            if (TestPersistenceContext.Required) TestPersistenceContext.Set("legacyPrefs", key, value);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else if (DebugSaveSession.IsIsolated) DebugSaveSession.SetInt("legacyPrefs/" + key, value);
#endif
            else PlayerPrefs.SetInt(key, value);
        }
        public static void SetString(string key, string value)
        {
            if (TestPersistenceContext.Required) TestPersistenceContext.Set("legacyPrefs", key, value);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else if (DebugSaveSession.IsIsolated) DebugSaveSession.SetString("legacyPrefs/" + key, value);
#endif
            else PlayerPrefs.SetString(key, value);
        }
        public static void Save()
        {
            if (TestPersistenceContext.Required) TestPersistenceContext.RequireAccess();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else if (DebugSaveSession.IsIsolated) DebugSaveSession.RequireAccess();
#endif
            else PlayerPrefs.Save();
        }
    }
}
