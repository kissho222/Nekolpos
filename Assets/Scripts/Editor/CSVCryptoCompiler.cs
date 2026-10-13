using UnityEngine;
using UnityEditor;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nekolpos.EditorTools
{
    /// <summary>
    /// 【CSV暗号化コンパイラ】
    /// Notionから出力した人間が読めるCSVファイルを、
    /// ゲーム配布用に解析不能なバイナリファイル（.bytes）に変換・暗号化します。
    /// これにより、プレイヤーに正規表現の辞書やセリフの中身を覗き見されるのを防ぎます。
    /// </summary>
    public class CSVCryptoCompiler : EditorWindow
    {
        // =======================================================
        // ⚠️ セキュリティに関する最重要設定
        // =======================================================
        // Set these environment variables in the build environment.
        private const string KeyEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_KEY";
        private const string IvEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_IV";

        private const string INPUT_FOLDER = "TalkSource/TalkCSV";
        private const string OUTPUT_FOLDER = "Assets/Resources/TalkData";

        [MenuItem("Nekolpos/CSVを暗号化バイナリへ変換")]
        public static void CompileCSVToBytes()
        {
            try
            {
                int count = CompileForBuildOrThrow();
                Debug.Log($"[CSVCryptoCompiler] 変換完了: {count}件。出力先: {OUTPUT_FOLDER}");
            }
            catch (global::System.Exception exception)
            {
                Debug.LogError("[CSVCryptoCompiler] 変換失敗: " + exception.Message);
            }
        }

        public static int CompileForBuildOrThrow()
        {
            TalkTopicMasterCsvUtility.Synchronize(TalkTopicMasterCsvUtility.SourcePath, TalkTopicMasterCsvUtility.RuntimePath);
            if (!TryResolveCryptoSettings(out string key, out string iv))
                throw new global::System.InvalidOperationException("CSV暗号化の環境変数が未設定、または鍵/IVの長さが不正です。");
            Nekolpos.Dialogue.UnknownWord.WordKnowledge.ParseCsv(File.ReadAllText(Nekolpos.Dialogue.UnknownWord.WordKnowledge.SourcePath, Encoding.UTF8));
            Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.ParseCsv(File.ReadAllText(Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.SourcePath, Encoding.UTF8));
            byte[] diaryData = EncryptString(DialogueDiaryAuthoring.CompileJson(), key, iv);
            string compiledDialogueJson = Authoring.DialogueCompiledBundleCompiler.CompileJson();
            byte[] compiledDialogueData = EncryptString(compiledDialogueJson, key, iv);
            int count = CompileDirectoryOrThrow(INPUT_FOLDER, OUTPUT_FOLDER, key, iv);
            TalkTopicMasterCsvUtility.AtomicWrite(Path.Combine(OUTPUT_FOLDER, "DialogueDiary.bytes"), diaryData);
            count++;
            TalkTopicMasterCsvUtility.AtomicWrite(Path.Combine(OUTPUT_FOLDER, "DialoguePreviewRuntime.bytes"), compiledDialogueData);
            count++;
            Nekolpos.Dialogue.UnknownWord.WordKnowledge.ResetCache();
            Nekolpos.System.DialogueDiaryCatalog.ResetCache();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Backgammon.Conversation.KanjiReadingDictionary.ResetDefaultForTests();
            return count;
        }

        public static int CompileDirectoryOrThrow(string inputFolder, string outputFolder, string key, string iv)
        {
            if (!Directory.Exists(inputFolder))
                throw new DirectoryNotFoundException("CSV入力フォルダーがありません: " + inputFolder);
            string[] files = Directory.GetFiles(inputFolder, "*.csv");
            if (files.Length == 0)
                throw new global::System.InvalidOperationException("CSV入力フォルダーにCSVがありません: " + inputFolder);
            global::System.Array.Sort(files, global::System.StringComparer.Ordinal);
            var compiled = new global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, byte[]>>();
            // Read and encrypt every source first so a malformed/unreadable input cannot publish a partial conversion.
            foreach (string file in files)
            {
                if (Path.GetFileName(file).Equals("DialoguePreview_Integrated.csv", global::System.StringComparison.OrdinalIgnoreCase)) continue;
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (Path.GetFileName(file).Equals("WordLearningQuestions.csv", global::System.StringComparison.Ordinal)) Nekolpos.Dialogue.UnknownWord.WordKnowledge.ParseCsv(text);
                if (Path.GetFileName(file).Equals("WordLearningDialogue.csv", global::System.StringComparison.Ordinal)) Nekolpos.Dialogue.UnknownWord.WordLearningDialogue.ParseCsv(text);
                byte[] bytes = EncryptString(text, key, iv);
                compiled.Add(new global::System.Collections.Generic.KeyValuePair<string, byte[]>(Path.GetFileNameWithoutExtension(file) + ".bytes", bytes));
            }
            Directory.CreateDirectory(outputFolder);
            foreach (var item in compiled)
                TalkTopicMasterCsvUtility.AtomicWrite(Path.Combine(outputFolder, item.Key), item.Value);
            return compiled.Count;
        }
        /// <summary>
        /// AES-256 (CBCモード) で文字列を暗号化し、バイト配列を返す
        /// </summary>
        public static byte[] EncryptString(string plainText, string keyString, string ivString)
        {
            byte[] key = Encoding.UTF8.GetBytes(keyString);
            byte[] iv = Encoding.UTF8.GetBytes(ivString);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt, Encoding.UTF8))
                        {
                            swEncrypt.Write(plainText);
                        }
                    }
                    return msEncrypt.ToArray();
                }
            }
        }

        private static bool TryResolveCryptoSettings(out string encryptionKey, out string encryptionIv)
        {
            encryptionKey = ResolveEnvironmentVariable(KeyEnvironmentVariable);
            encryptionIv = ResolveEnvironmentVariable(IvEnvironmentVariable);

            if (string.IsNullOrEmpty(encryptionKey) || string.IsNullOrEmpty(encryptionIv))
            {
                Debug.LogError(
                    $"[CSVCryptoCompiler] {KeyEnvironmentVariable} and {IvEnvironmentVariable} must be set before encrypting CSV files.");
                return false;
            }

            if (Encoding.UTF8.GetByteCount(encryptionKey) != 32 || Encoding.UTF8.GetByteCount(encryptionIv) != 16)
            {
                Debug.LogError(
                    $"[CSVCryptoCompiler] Invalid key/IV length. {KeyEnvironmentVariable} must be 32 bytes and {IvEnvironmentVariable} must be 16 bytes.");
                return false;
            }

            return true;
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
    }
}
