using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nekolpos.System
{
    public enum SettingsAudioCategory { Bgm, Se, CatVoice }

    [Serializable]
    public sealed class GameSettings
    {
        public int version = 1;
        public FullScreenMode displayMode = FullScreenMode.FullScreenWindow;
        public FullScreenMode lastFullscreenMode = FullScreenMode.FullScreenWindow;
        public int width;
        public int height;
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float seVolume = 1f;
        public float catVoiceVolume = 1f;
        // A multiplier preserves each existing controller's authored sensitivity at the default 1x.
        public float mouseSensitivity = 1f;
        public bool invertY;
        public bool vSync;

        public static GameSettings Defaults(int width, int height, bool vSync) =>
            new GameSettings { width = Mathf.Max(1, width), height = Mathf.Max(1, height), vSync = vSync };

        public void Validate(IReadOnlyList<Vector2Int> resolutions, Vector2Int fallback)
        {
            version = 1;
            if (!IsDisplayMode(displayMode)) displayMode = FullScreenMode.FullScreenWindow;
            if (!IsFullscreenMode(lastFullscreenMode)) lastFullscreenMode = FullScreenMode.FullScreenWindow;
            if (IsFullscreenMode(displayMode)) lastFullscreenMode = displayMode;
            if (width <= 0 || height <= 0 || !resolutions.Contains(new Vector2Int(width, height)))
            {
                width = Mathf.Max(1, fallback.x);
                height = Mathf.Max(1, fallback.y);
            }
            masterVolume = FiniteRange(masterVolume, 0f, 1f, 1f);
            bgmVolume = FiniteRange(bgmVolume, 0f, 1f, 1f);
            seVolume = FiniteRange(seVolume, 0f, 1f, 1f);
            catVoiceVolume = FiniteRange(catVoiceVolume, 0f, 1f, 1f);
            mouseSensitivity = FiniteRange(mouseSensitivity, 0.1f, 5f, 1f);
        }

        public void ToggleDisplayMode()
        {
            if (IsFullscreenMode(displayMode))
            {
                lastFullscreenMode = displayMode;
                displayMode = FullScreenMode.Windowed;
            }
            else displayMode = IsFullscreenMode(lastFullscreenMode) ? lastFullscreenMode : FullScreenMode.FullScreenWindow;
        }

        public static bool IsFullscreenMode(FullScreenMode mode) =>
            mode == FullScreenMode.FullScreenWindow || mode == FullScreenMode.ExclusiveFullScreen;
        public static bool IsDisplayMode(FullScreenMode mode) => mode == FullScreenMode.Windowed || IsFullscreenMode(mode);
        public static float FiniteRange(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        public static float VolumeToDecibels(float volume) => volume <= 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(volume));

        public static List<Vector2Int> ResolutionChoices(IEnumerable<Resolution> available, Vector2Int current) =>
            available.Select(r => new Vector2Int(r.width, r.height)).Append(current)
                .Where(r => r.x > 0 && r.y > 0).Distinct().OrderBy(r => r.x).ThenBy(r => r.y).ToList();
    }

    public static class GameSettingsStore
    {
        public const string Key = "Nekolpos.Settings.v1";
        public const string LegacyBgmKey = "OpenBeta.Audio.BgmMuted";
        public const string LegacySeKey = "OpenBeta.Audio.SeMuted";

        public static GameSettings Load(GameSettings defaults)
        {
            GameSettings pristine = GameSettings.Defaults(defaults.width, defaults.height, defaults.vSync);
            if (PlayerPrefs.HasKey(Key))
            {
                try
                {
                    // Missing fields inherit defaults, including the current project's VSync.
                    string json = PlayerPrefs.GetString(Key);
                    if (!json.TrimStart().StartsWith("{")) throw new ArgumentException("Invalid settings JSON.");
                    JsonUtility.FromJsonOverwrite(json, defaults);
                    if (defaults.version != 1) throw new ArgumentException("Unsupported settings version.");
                    return defaults;
                }
                catch (ArgumentException error)
                {
                    Debug.LogWarning("[GameSettings] 保存済み設定を読めないため初期値へ戻します: " + error.Message);
                    return pristine;
                }
            }
            defaults.bgmVolume = PlayerPrefs.GetInt(LegacyBgmKey, 0) != 0 ? 0f : 1f;
            defaults.seVolume = PlayerPrefs.GetInt(LegacySeKey, 0) != 0 ? 0f : 1f;
            defaults.catVoiceVolume = defaults.seVolume;
            return defaults;
        }

        public static void Save(GameSettings settings)
        {
            if (DebugSaveSession.IsIsolated) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(settings));
            PlayerPrefs.DeleteKey(LegacyBgmKey);
            PlayerPrefs.DeleteKey(LegacySeKey);
            PlayerPrefs.Save();
        }
    }
}
