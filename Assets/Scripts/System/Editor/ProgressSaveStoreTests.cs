using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.System.Editor.Tests
{
    public sealed class ProgressSaveStoreTests
    {
        private string directory;
        private string path;
        private const string DayKey = "Nekolpos.Debug.StartDay.v1";
        private const string NameKey = "SaveData.PLAYER_NAME";

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "NekolposProgressTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, ProgressSaveStore.FileName);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        [Test]
        public void Save_RoundTripsProgressAndUnicodeInNewInstance()
        {
            var first = new ProgressSaveFile(path);
            Assert.That(first.Exists, Is.False);
            first.SetInt(DayKey, 3);
            first.SetString(NameKey, "プレイヤー");
            first.SetString("Nekolpos.Diary.Entries.v1", "{\"entries\":[]}");
            first.Save();
            var second = new ProgressSaveFile(path);
            Assert.That(second.Exists, Is.True);
            Assert.That(second.GetInt(DayKey), Is.EqualTo(3));
            Assert.That(second.GetString(NameKey), Is.EqualTo("プレイヤー"));
            Assert.That(second.GetString("Nekolpos.Diary.Entries.v1"), Is.EqualTo("{\"entries\":[]}"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [Test]
        public void Delete_DoesNotResurrectProgressAfterReload()
        {
            var save = new ProgressSaveFile(path);
            save.SetInt(DayKey, 3);
            save.Save();
            save.DeleteKey(DayKey);
            save.Save();
            var reloaded = new ProgressSaveFile(path);
            Assert.That(reloaded.HasKey(DayKey), Is.False);
            Assert.That(reloaded.GetInt(DayKey, 1), Is.EqualTo(1));
        }

        [Test]
        public void CorruptPrimary_RecoversPreviousSave()
        {
            var save = new ProgressSaveFile(path);
            save.SetInt(DayKey, 2);
            save.Save();
            save.SetInt(DayKey, 3);
            save.Save();
            File.WriteAllText(path, "broken");
            Assert.That(new ProgressSaveFile(path).GetInt(DayKey), Is.EqualTo(2));
            Assert.That(new ProgressSaveFile(path).GetInt(DayKey), Is.EqualTo(2));
        }

        [TestCase("broken")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("{\"version\":1,\"integers\":null,\"strings\":{}}")]
        [TestCase("{\"version\":1,\"integers\":{},\"strings\":{\"nekolpos.client_install_id\":\"x\"}}")]
        public void InvalidSave_IsPreservedAndReported(string contents)
        {
            File.WriteAllText(path, contents);
            Assert.Throws<InvalidDataException>(() => new ProgressSaveFile(path));
            Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
        }

        [Test]
        public void FutureVersion_IsNotReplacedByBackup()
        {
            var save = new ProgressSaveFile(path);
            save.Save();
            save.Save();
            const string future = "{\"version\":2,\"integers\":{},\"strings\":{}}";
            File.WriteAllText(path, future);
            Assert.Throws<NotSupportedException>(() => new ProgressSaveFile(path));
            Assert.That(File.ReadAllText(path), Is.EqualTo(future));
        }

        [TestCase("nekolpos.client_install_id")]
        [TestCase("Nekolpos.PerfDiag.Enabled")]
        [TestCase("OpenBeta.BgmMuted")]
        public void DevicePreferences_AreRejected(string key)
        {
            var save = new ProgressSaveFile(path);
            Assert.Throws<ArgumentException>(() => save.SetString(key, "value"));
            Assert.Throws<ArgumentException>(() => save.SetInt(key, 1));
        }

        [Test]
        public void SaveFailure_PreservesPreviousFile()
        {
            var save = new ProgressSaveFile(path);
            save.SetInt(DayKey, 3);
            save.Save();
            Directory.CreateDirectory(path + ".tmp");
            save.SetInt(DayKey, 4);
            Assert.Throws<UnauthorizedAccessException>(() => save.Save());
            Assert.That(new ProgressSaveFile(path).GetInt(DayKey), Is.EqualTo(3));
        }

        [Test]
        public void LegacyMigration_CopiesIndexedDiscoveriesAndExcludesInstallIdentity()
        {
            const string list = "Nekolpos.TeachingDiscovery.KnownEntries.CatSeries";
            const string entry = "Nekolpos.TeachingDiscovery.Entry.CatSeries.codex-save-test";
            string oldList = PlayerPrefs.HasKey(list) ? PlayerPrefs.GetString(list) : null;
            bool hadEntry = PlayerPrefs.HasKey(entry);
            int oldEntry = PlayerPrefs.GetInt(entry);
            try
            {
                PlayerPrefs.SetString(list, "codex-save-test");
                PlayerPrefs.SetInt(entry, 1);
                var save = new ProgressSaveFile(path);
                typeof(ProgressSaveStore).GetMethod("ImportLegacy", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { save });
                save.Save();
                var reloaded = new ProgressSaveFile(path);
                Assert.That(reloaded.GetInt(entry), Is.EqualTo(1));
                Assert.That(reloaded.GetString(list), Is.EqualTo("codex-save-test"));
                Assert.That(File.ReadAllText(path), Does.Not.Contain("client_install_id"));
                Assert.That(PlayerPrefs.GetInt(entry), Is.EqualTo(1));
            }
            finally
            {
                if (oldList == null) PlayerPrefs.DeleteKey(list); else PlayerPrefs.SetString(list, oldList);
                if (hadEntry) PlayerPrefs.SetInt(entry, oldEntry); else PlayerPrefs.DeleteKey(entry);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void LearnedWordsAndPsychologyRoundTripThroughPlayerFileBackend()
        {
            var file = new ProgressSaveFile(path);
            file.SetString(Nekolpos.StatusSystem.StatusManager.SaveKey, "existing psychology");
            var words = new Nekolpos.Dialogue.UnknownWord.LearnedWordStore(() => file.GetString(Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey),
                json => { file.SetString(Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey, json); file.Save(); });
            var word = new Nekolpos.Dialogue.UnknownWord.LearnedWord { id = Guid.NewGuid().ToString("N"), display = "たらこ" };
            words.Put(word);
            var reloaded = new ProgressSaveFile(path);
            var recalled = new Nekolpos.Dialogue.UnknownWord.LearnedWordStore(() => reloaded.GetString(Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey), _ => {});
            Assert.That(recalled.Match("たらこ", out _).id, Is.EqualTo(word.id));
            Assert.That(reloaded.GetString(Nekolpos.StatusSystem.StatusManager.SaveKey), Is.EqualTo("existing psychology"));
            words.Forget(word.id);
            Assert.That(new Nekolpos.Dialogue.UnknownWord.LearnedWordStore(() => new ProgressSaveFile(path).GetString(Nekolpos.Dialogue.UnknownWord.LearnedWordStore.SaveKey), _ => {}).Words, Is.Empty);
        }
    }
}
