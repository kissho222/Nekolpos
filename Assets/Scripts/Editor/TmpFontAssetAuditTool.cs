using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class TmpFontAssetAuditTool
    {
        private const string FixedUiFontPath = "Assets/Fonts/ZenMaruGothic-Regular SDF.asset";
        private const string ChineseFallbackFontPath = "Assets/Fonts/NotoSansSC-Regular SDF.asset";
        private const string ReportPath = "FontAssetAuditReport.md";
        private static readonly Regex SerializedTextRegex = new Regex(@"^\s*m_text:\s*(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

        [MenuItem("Nekolpos/Fonts/Audit Fixed Text TMP Font Coverage")]
        public static void AuditFixedTextCoverage()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FixedUiFontPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[TmpFontAssetAuditTool] Font asset not found: {FixedUiFontPath}");
                return;
            }

            string fixedText = CollectFixedText();
            SortedSet<int> missingWithoutFallback = FindMissingCharacters(fontAsset, fixedText, false);
            SortedSet<int> missingWithFallback = FindMissingCharacters(fontAsset, fixedText, true);
            SortedSet<int> verificationMissingWithFallback = FindMissingCharacters(
                fontAsset,
                "今日、天気、対馬山猫、猫又、お腹、御飯、可愛い、鬱陶しい、髙橋、る",
                true);

            File.WriteAllText(
                ReportPath,
                BuildReport(fontAsset, fixedText, missingWithoutFallback, missingWithFallback, verificationMissingWithFallback),
                Encoding.UTF8);

            AssetDatabase.Refresh();
            Debug.Log(
                "[TmpFontAssetAuditTool] Font coverage audit completed. " +
                $"fixedChars={EnumerateCodePoints(fixedText).Count()}, " +
                $"missingWithFallback={missingWithFallback.Count}, report={ReportPath}");
        }

        [MenuItem("Nekolpos/Fonts/Bake Fixed Text Into Existing TMP Font Assets")]
        public static void BakeFixedTextIntoExistingFontAssets()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FixedUiFontPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[TmpFontAssetAuditTool] Font asset not found: {FixedUiFontPath}");
                return;
            }

            TMP_FontAsset chineseFallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFallbackFontPath);
            EnsureFallback(fontAsset, chineseFallback);

            string fixedText = CollectFixedText();
            string verificationText = "今日、天気、対馬山猫、猫又、お腹、御飯、可愛い、鬱陶しい、髙橋、る";
            string textToBake = fixedText + verificationText;

            fontAsset.TryAddCharacters(textToBake);
            if (fontAsset.fallbackFontAssetTable != null)
            {
                for (int i = 0; i < fontAsset.fallbackFontAssetTable.Count; i++)
                {
                    TMP_FontAsset fallback = fontAsset.fallbackFontAssetTable[i];
                    if (fallback != null)
                    {
                        fallback.TryAddCharacters(textToBake);
                        EditorUtility.SetDirty(fallback);
                    }
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[TmpFontAssetAuditTool] Baked fixed text characters into existing TMP font assets.");
        }

        private static void EnsureFallback(TMP_FontAsset fontAsset, TMP_FontAsset fallback)
        {
            if (fontAsset == null || fallback == null)
            {
                return;
            }

            if (fontAsset.fallbackFontAssetTable == null)
            {
                fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }

            if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
            {
                fontAsset.fallbackFontAssetTable.Add(fallback);
            }
        }

        private static string CollectFixedText()
        {
            StringBuilder builder = new StringBuilder();

            foreach (string csvPath in Directory.GetFiles("Assets/Resources/Dialogue", "*.csv", SearchOption.AllDirectories))
            {
                builder.AppendLine(File.ReadAllText(csvPath, Encoding.UTF8));
            }

            foreach (string path in Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories)))
            {
                string contents = File.ReadAllText(path, Encoding.UTF8);
                MatchCollection matches = SerializedTextRegex.Matches(contents);
                for (int i = 0; i < matches.Count; i++)
                {
                    builder.AppendLine(DecodeSerializedText(matches[i].Groups[1].Value.Trim()));
                }
            }

            return builder.ToString();
        }

        private static string DecodeSerializedText(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
            {
                return string.Empty;
            }

            if (serialized.Length >= 2 && serialized[0] == '"' && serialized[serialized.Length - 1] == '"')
            {
                serialized = serialized.Substring(1, serialized.Length - 2);
            }

            return Regex.Unescape(serialized);
        }

        private static SortedSet<int> FindMissingCharacters(TMP_FontAsset fontAsset, string text, bool searchFallbacks)
        {
            SortedSet<int> missing = new SortedSet<int>();
            foreach (int codePoint in EnumerateCodePoints(text))
            {
                if (char.IsWhiteSpace((char)codePoint))
                {
                    continue;
                }

                if (codePoint > char.MaxValue ||
                    !fontAsset.HasCharacter((char)codePoint, searchFallbacks, false))
                {
                    missing.Add(codePoint);
                }
            }

            return missing;
        }

        private static IEnumerable<int> EnumerateCodePoints(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (char.IsHighSurrogate(current) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return char.ConvertToUtf32(current, text[++i]);
                }
                else
                {
                    yield return current;
                }
            }
        }

        private static string BuildReport(
            TMP_FontAsset fontAsset,
            string fixedText,
            SortedSet<int> missingWithoutFallback,
            SortedSet<int> missingWithFallback,
            SortedSet<int> verificationMissingWithFallback)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# TMP Font Asset Audit");
            report.AppendLine();
            report.AppendLine($"- Fixed UI font: `{FixedUiFontPath}`");
            report.AppendLine($"- Font asset name: `{fontAsset.name}`");
            report.AppendLine($"- Unique fixed text code points: `{EnumerateCodePoints(fixedText).Distinct().Count()}`");
            report.AppendLine($"- Missing without fallback: `{missingWithoutFallback.Count}`");
            report.AppendLine($"- Missing with fallback: `{missingWithFallback.Count}`");
            report.AppendLine($"- Verification text missing with fallback: `{verificationMissingWithFallback.Count}`");
            report.AppendLine();
            AppendCharacterList(report, "Missing Without Fallback", missingWithoutFallback);
            AppendCharacterList(report, "Missing With Fallback", missingWithFallback);
            AppendCharacterList(report, "Verification Missing With Fallback", verificationMissingWithFallback);
            return report.ToString();
        }

        private static void AppendCharacterList(StringBuilder report, string heading, SortedSet<int> codePoints)
        {
            report.AppendLine($"## {heading}");
            report.AppendLine();
            if (codePoints.Count == 0)
            {
                report.AppendLine("None.");
                report.AppendLine();
                return;
            }

            foreach (int codePoint in codePoints)
            {
                report.Append("- `")
                    .Append(char.ConvertFromUtf32(codePoint).Replace("`", "\\`"))
                    .Append("` U+")
                    .Append(codePoint.ToString("X4"))
                    .AppendLine();
            }

            report.AppendLine();
        }
    }
}
