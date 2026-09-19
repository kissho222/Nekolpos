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
            // 入力フォルダの確認
            if (!Directory.Exists(INPUT_FOLDER))
            {
                Debug.LogError($"[CSVCryptoCompiler] 入力フォルダが見つかりません: {INPUT_FOLDER}");
                return;
            }

            // 出力フォルダの作成（なければ）
            if (!Directory.Exists(OUTPUT_FOLDER))
            {
                Directory.CreateDirectory(OUTPUT_FOLDER);
            }

            // フォルダ内の全CSVファイルを取得
            string[] csvFiles = Directory.GetFiles(INPUT_FOLDER, "*.csv");
            if (csvFiles.Length == 0)
            {
                Debug.LogWarning($"[CSVCryptoCompiler] {INPUT_FOLDER} にCSVファイルがありません。Notionからエクスポートしたファイルを配置してください。");
                return;
            }

            int successCount = 0;
            if (!TryResolveCryptoSettings(out string encryptionKey, out string encryptionIv))
            {
                return;
            }

            for (int i = 0; i < csvFiles.Length; i++)
            {
                ProcessCSV(csvFiles[i], encryptionKey, encryptionIv, ref successCount);
            }

            // Unityエクスプローラーを更新して見えるようにする
            AssetDatabase.Refresh();
            Debug.Log($"[CSVCryptoCompiler] 🎉 変換処理が完了しました！（成功: {successCount}件）\n出力先: {OUTPUT_FOLDER}");
            Backgammon.Conversation.KanjiReadingDictionary.ResetDefaultForTests();
        }

        /// <summary>
        /// 指定したキーワード（例:"正規表現"）を含むCSV群から、最も更新日時が新しいものを1つ選び、
        /// 指定した英語名（例:"RegexDict"）で暗号化して保存する
        /// </summary>
        private static void ProcessCSV(
            string csvPath,
            string encryptionKey,
            string encryptionIv,
            ref int successCount)
        {
            try
            {
                // \ を / に統一して扱いやすくする
                string normalizedPath = csvPath.Replace("\\", "/");
                
                // 生のテキストを読み込む
                string rawCsvData = File.ReadAllText(normalizedPath, Encoding.UTF8);

                // 暗号化処理
                byte[] encryptedData = EncryptString(rawCsvData, encryptionKey, encryptionIv);

                string outputName = Path.GetFileNameWithoutExtension(csvPath);
                string outPath = $"{OUTPUT_FOLDER}/{outputName}.bytes";

                // ファイルへ保存
                File.WriteAllBytes(outPath, encryptedData);
                successCount++;
                
                Debug.Log($"[CSVCryptoCompiler] 🔒 暗号化完了: {outputName}.bytes (元ファイル: {Path.GetFileName(csvPath)})");
            }
            catch (global::System.Exception e)
            {
                Debug.LogError($"[CSVCryptoCompiler] {csvPath} の暗号化中にエラーが発生しました。\n{e.Message}");
            }
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
