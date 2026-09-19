using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public sealed class SecurityBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string KeyEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_KEY";
        private const string IvEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_IV";
        private const string PackagedCryptoSettingsFileName = "nekolpos_talkdata.key";
        private const string PackagedCryptoSettingsResourcePath = "Assets/Resources/BuildSecrets/nekolpos_talkdata.bytes";

        private static readonly string[] SensitiveBuildInputs =
        {
            "Assets/Resources/TalkCSV",
            "Assets/Resources/TalkData/KanjiReadingDictionary.csv",
            "Assets/Resources/TalkData/KanjiReadingDictionary.csv.meta",
            "Assets/Resources/TalkData/KanjiReadingDictionaryCandidates.csv",
            "Assets/Resources/TalkData/KanjiReadingDictionaryCandidates.csv.meta",
            "Assets/Resources/TalkData/decrypt_test.py",
            "Assets/Resources/TalkData/decrypt_test.py.meta",
            "DecryptTalkData.cs",
            "DecryptTalkData.exe",
            "debug_csv.txt"
        };

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool developmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            List<string> found = FindExistingSensitiveInputs();
            if (found.Count == 0)
            {
                string key = ResolveEnvironmentVariable(KeyEnvironmentVariable);
                string iv = ResolveEnvironmentVariable(IvEnvironmentVariable);
                bool hasValidCryptoSettings = ValidateCryptoSettings(key, iv, out string error);

                if (!developmentBuild)
                {
                    if (!hasValidCryptoSettings)
                    {
                        throw new BuildFailedException("[SecurityBuildGuard] " + error);
                    }
                }
                else if (!hasValidCryptoSettings)
                {
                    Debug.LogWarning("[SecurityBuildGuard] " + error);
                }

                if (hasValidCryptoSettings)
                {
                    WritePackagedCryptoSettingsResource(key, iv);
                }

                return;
            }

            string message =
                "Release build blocked because sensitive development assets are present:\n" +
                string.Join("\n", found) +
                "\nMove plaintext CSV/debug decrypt assets outside Resources or remove them before release.";

            if (developmentBuild)
            {
                Debug.LogWarning("[SecurityBuildGuard] " + message);
                return;
            }

            throw new BuildFailedException(message);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            DeletePackagedCryptoSettingsResource();

            bool developmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (developmentBuild)
            {
                return;
            }

            string key = ResolveEnvironmentVariable(KeyEnvironmentVariable);
            string iv = ResolveEnvironmentVariable(IvEnvironmentVariable);
            if (!ValidateCryptoSettings(key, iv, out string error))
            {
                throw new BuildFailedException("[SecurityBuildGuard] Release build cannot package TalkData key file: " + error);
            }

            if (report.summary.platform == BuildTarget.WebGL)
            {
                Debug.Log("[SecurityBuildGuard] WebGL uses packaged TalkData key resource; skipping external StreamingAssets key file.");
                return;
            }

            string streamingAssetsPath = ResolveStandaloneStreamingAssetsPath(report.summary.outputPath);
            if (string.IsNullOrWhiteSpace(streamingAssetsPath))
            {
                Debug.LogWarning("[SecurityBuildGuard] Unsupported output layout; TalkData key file was not packaged.");
                return;
            }

            Directory.CreateDirectory(streamingAssetsPath);
            string keyFilePath = Path.Combine(streamingAssetsPath, PackagedCryptoSettingsFileName);
            File.WriteAllLines(
                keyFilePath,
                new[]
                {
                    KeyEnvironmentVariable + "=" + key,
                    IvEnvironmentVariable + "=" + iv
                });

            Debug.Log("[SecurityBuildGuard] Packaged TalkData key file into StreamingAssets for release runtime.");
        }

        private static void WritePackagedCryptoSettingsResource(string key, string iv)
        {
            string directory = Path.GetDirectoryName(PackagedCryptoSettingsResourcePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(
                PackagedCryptoSettingsResourcePath,
                new[]
                {
                    KeyEnvironmentVariable + "=" + key,
                    IvEnvironmentVariable + "=" + iv
                });

            AssetDatabase.ImportAsset(PackagedCryptoSettingsResourcePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[SecurityBuildGuard] Packaged TalkData key resource for WebGL/release runtime.");
        }

        private static void DeletePackagedCryptoSettingsResource()
        {
            DeleteFileIfExists(PackagedCryptoSettingsResourcePath);
            DeleteFileIfExists(PackagedCryptoSettingsResourcePath + ".meta");

            string directory = Path.GetDirectoryName(PackagedCryptoSettingsResourcePath);
            if (!string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory) &&
                Directory.GetFiles(directory).Length == 0 &&
                Directory.GetDirectories(directory).Length == 0)
            {
                Directory.Delete(directory);
                DeleteFileIfExists(directory + ".meta");
            }

            AssetDatabase.Refresh();
        }

        private static void DeleteFileIfExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static List<string> FindExistingSensitiveInputs()
        {
            List<string> found = new List<string>();
            for (int i = 0; i < SensitiveBuildInputs.Length; i++)
            {
                string path = SensitiveBuildInputs[i];
                if (Directory.Exists(path) || File.Exists(path))
                {
                    found.Add(path);
                }
            }

            return found;
        }

        private static string ResolveStandaloneStreamingAssetsPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return string.Empty;
            }

            string outputDirectory = Path.GetDirectoryName(outputPath);
            string playerName = Path.GetFileNameWithoutExtension(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(playerName))
            {
                return string.Empty;
            }

            return Path.Combine(outputDirectory, playerName + "_Data", "StreamingAssets");
        }

        private static string ResolveEnvironmentVariable(string variableName)
        {
            string value = global::System.Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            value = global::System.Environment.GetEnvironmentVariable(variableName, global::System.EnvironmentVariableTarget.User);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            return global::System.Environment.GetEnvironmentVariable(variableName, global::System.EnvironmentVariableTarget.Machine);
        }

        private static bool ValidateCryptoSettings(string key, string iv, out string error)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(iv))
            {
                error = $"{KeyEnvironmentVariable} and {IvEnvironmentVariable} must be set before making a release build.";
                return false;
            }

            if (global::System.Text.Encoding.UTF8.GetByteCount(key) != 32 ||
                global::System.Text.Encoding.UTF8.GetByteCount(iv) != 16)
            {
                error = $"{KeyEnvironmentVariable} must be 32 bytes and {IvEnvironmentVariable} must be 16 bytes.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
