using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using UnityEngine;
using Nekolpos.Data;
using UnityEditor;

namespace Nekolpos.EditorTools
{
    /// <summary>Local hashes only. Never serializes normal progress, registry values, names or utterances.</summary>
    public static class DebugNormalSaveAudit
    {
        public static Dictionary<string, string> Capture()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string folder = Application.persistentDataPath;
            if (Directory.Exists(folder))
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = path.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (relative.StartsWith("DebugConsole" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                    using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    result["file/" + relative] = Hash(file);
                }
#if UNITY_EDITOR_WIN
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Unity\UnityEditor\" + Application.companyName + @"\" + Application.productName);
            if (key != null)
                foreach (string name in key.GetValueNames())
                {
                    // Unity changes these three engine session values on every Play; they are not game saves.
                    if (name.StartsWith("unity.player_session_count_h", StringComparison.Ordinal) || name.StartsWith("unity.player_sessionid_h", StringComparison.Ordinal) || name.StartsWith("unity_connect.session_id_h", StringComparison.Ordinal)) continue;
                    object value = key.GetValue(name);
                    byte[] data = value is byte[] bytes ? bytes : Encoding.UTF8.GetBytes(Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture) ?? "");
                    result["prefs/" + name] = Hash(new MemoryStream(data));
                }
#endif
            foreach (string guid in AssetDatabase.FindAssets("t:CatDataSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<CatDataSO>(path);
                if (data != null) result["cat/" + guid] = Hash(new MemoryStream(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(data))));
            }
            return result;
        }
        private static string Hash(Stream data) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", ""); }
        public static bool Matches(Dictionary<string, string> before, Dictionary<string, string> after) => before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string hash) && hash == pair.Value);
    }
}
