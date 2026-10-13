using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nekolpos.System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekolpos.EditorTools
{
    public sealed class PcReleaseBuildTests
    {
        [TestCase("success")]
        [TestCase("empty")]
        [TestCase("missing")]
        [TestCase("invalid-key")]
        [TestCase("output-failure")]
        public void CsvCompilation_ProducesDecryptableDataAndRejectsFailures(string scenario)
        {
            string folder = Path.Combine(Path.GetTempPath(), "NekolposCsvBuildTest-" + Guid.NewGuid().ToString("N"));
            string input = Path.Combine(folder, "Input");
            string output = Path.Combine(folder, "Output");
            const string key = "01234567890123456789012345678901";
            const string iv = "0123456789012345";
            const string csv = "Id,Value\n1,検証用データ\n";
            try
            {
                Directory.CreateDirectory(folder);
                if (scenario != "missing") Directory.CreateDirectory(input);
                if (scenario != "missing" && scenario != "empty")
                    File.WriteAllText(Path.Combine(input, "Example.csv"), csv, new UTF8Encoding(false));
                if (scenario == "missing")
                    Assert.Throws<DirectoryNotFoundException>(() => CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, key, iv));
                else if (scenario == "empty")
                    Assert.Throws<InvalidOperationException>(() => CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, key, iv));
                else if (scenario == "invalid-key")
                    Assert.Throws<CryptographicException>(() => CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, "short", iv));
                else if (scenario == "output-failure")
                {
                    File.WriteAllText(output, "Occupied by a file");
                    Assert.Throws<IOException>(() => CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, key, iv));
                }
                else
                {
                    Assert.That(CSVCryptoCompiler.CompileDirectoryOrThrow(input, output, key, iv), Is.EqualTo(1));
                    byte[] bytes = File.ReadAllBytes(Path.Combine(output, "Example.bytes"));
                    using (Aes aes = Aes.Create())
                    {
                        aes.Key = Encoding.UTF8.GetBytes(key);
                        aes.IV = Encoding.UTF8.GetBytes(iv);
                        using (var decryptor = aes.CreateDecryptor())
                        using (var decrypted = new MemoryStream(decryptor.TransformFinalBlock(bytes, 0, bytes.Length)))
                        using (var reader = new StreamReader(decrypted, Encoding.UTF8, true))
                            Assert.That(reader.ReadToEnd(), Is.EqualTo(csv));
                    }
                }
                if (scenario != "success") Assert.That(File.Exists(Path.Combine(output, "Example.bytes")), Is.False);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void MainCameraCheck_AllowsDisabledWalkCameraButRequiresAnActiveCamera()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var startup = CreateObject(scene, "Startup camera").AddComponent<Camera>();
                startup.tag = "MainCamera";
                var walk = CreateObject(scene, "Walk camera").AddComponent<Camera>();
                walk.tag = "MainCamera";
                walk.enabled = false;
                Assert.That(PcReleaseBuildWindow.HasActiveMainCamera(scene), Is.True);
                startup.gameObject.SetActive(false);
                Assert.That(PcReleaseBuildWindow.HasActiveMainCamera(scene), Is.False);
                walk.enabled = true;
                Assert.That(PcReleaseBuildWindow.HasActiveMainCamera(scene), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase("1.2.3", true)]
        [TestCase("0.0.1", true)]
        [TestCase("1.2", false)]
        [TestCase("../1.2.3", false)]
        [TestCase("1.2.3\n", false)]
        [TestCase("", false)]
        public void BuildName_RequiresThreeNumericParts(string version, bool valid)
        {
            Assert.That(PcReleaseBuildWindow.IsValidVersion(version), Is.EqualTo(valid));
            if (valid) Assert.That(PcReleaseBuildWindow.BuildName(version), Is.EqualTo("Nekolpos " + version));
            else Assert.Throws<ArgumentException>(() => PcReleaseBuildWindow.BuildName(version));
        }

        [Test]
        public void InspectScene_DetectsInactiveParentAndDisabledMainCanvas()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = CreateObject(scene, "Inactive parent");
                GameObject canvas = CreateObject(scene, "OpenBetaCanvas");
                canvas.transform.SetParent(root.transform);
                Canvas component = canvas.AddComponent<Canvas>();
                root.SetActive(false);
                var inactive = new List<string>();
                var errors = new List<string>();
                PcReleaseBuildWindow.InspectScene(scene, inactive, errors);
                Assert.That(inactive.Count, Is.EqualTo(1));
                Assert.That(errors.Exists(e => e.Contains("OpenBetaCanvas") && e.Contains("Canvas")), Is.True);
                root.SetActive(true);
                component.enabled = false;
                errors.Clear();
                PcReleaseBuildWindow.InspectScene(scene, new List<string>(), errors);
                Assert.That(errors.Exists(e => e.Contains("Canvas")), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void InspectScene_AllowsInactiveEventPanel()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject panel = CreateObject(scene, "Pending dialogue panel");
                panel.SetActive(false);
                var inactive = new List<string>();
                var errors = new List<string>();
                PcReleaseBuildWindow.InspectScene(scene, inactive, errors);
                Assert.That(inactive.Count, Is.EqualTo(1));
                Assert.That(errors, Is.Empty);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void DisableDebug_DisablesDebugButtonAndKnownMarkerFlags()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject debugButton = CreateObject(scene, "DebugButton");
                GameObject marker = CreateObject(scene, "Player marker");
                marker.SetActive(false);
                var component = marker.AddComponent<PlayerLookTarget>();
                var serialized = new SerializedObject(component);
                serialized.FindProperty("enableDebugLogs").boolValue = true;
                serialized.FindProperty("showDebugMarker").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PcReleaseDebugSanitizer.DisableDebug(scene);
                serialized.Update();
                Assert.That(debugButton.activeSelf, Is.False);
                Assert.That(serialized.FindProperty("enableDebugLogs").boolValue, Is.False);
                Assert.That(serialized.FindProperty("showDebugMarker").boolValue, Is.False);
                Assert.That(component.enabled, Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static GameObject CreateObject(Scene scene, string name)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }
    }
}
