using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Yarn.Unity;
using Yarn.Unity.Editor;

namespace Nekolpos.EditorTools
{
    public static class YarnLocalizationUtility
    {
        private const string BaseLocale = "ja";
        private const string EnglishLocale = "en";
        private const string ChineseHansLocale = "zh-Hans";
        private const string ExportFolderAssetPath = "Assets/Localization/Yarn";
        private const string YarnExportAssetPath = ExportFolderAssetPath + "/yarn_ja.csv";
        private const string MasterExportAssetPath = ExportFolderAssetPath + "/master_translation.csv";
        private const string DefaultPreviewCsvPath = "Assets/Resources/TalkCSV/DialoguePreview.csv";

        private sealed class YarnTranslationRow
        {
            public string Id;
            public string Node;
            public string Ja;
            public string En;
            public string ZhHans;
            public string Comment;
        }

        private sealed class MasterTranslationRow
        {
            public string SourceType;
            public string Id;
            public string Node;
            public string Ja;
            public string En;
            public string ZhHans;
        }

        public static void GenerateLineTags()
        {
            try
            {
                List<YarnProjectImporter> importers = FindYarnProjectImporters();
                if (importers.Count == 0)
                {
                    Debug.LogWarning("[YarnLocalizationUtility] YarnProject が見つかりません。");
                    return;
                }

                foreach (YarnProjectImporter importer in importers)
                {
                    InvokeYarnProjectUtility("AddLineTagsToFilesInYarnProject", importer, null, null);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[YarnLocalizationUtility] {importers.Count} 件の YarnProject に対して Line Tag 生成を実行しました。");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Yarn Localization", exception.Message, "OK");
            }
        }

        public static void ExportTranslationCsv()
        {
            try
            {
                EnsureExportFolderExists();
                GenerateLineTags();

                List<YarnProjectImporter> importers = FindYarnProjectImporters();
                if (importers.Count == 0)
                {
                    Debug.LogWarning("[YarnLocalizationUtility] Export 対象の YarnProject がありません。");
                    return;
                }

                List<YarnTranslationRow> rows = new List<YarnTranslationRow>();
                HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (YarnProjectImporter importer in importers)
                {
                    ConfigureLocalizationFiles(importer);

                    List<StringTableEntry> baseEntries = SyncBaseStrings(importer);
                    Dictionary<string, StringTableEntry> englishEntries = LoadStringTableById(GetStandardStringsFullPath(importer, EnglishLocale));
                    Dictionary<string, StringTableEntry> chineseEntries = LoadStringTableById(GetStandardStringsFullPath(importer, ChineseHansLocale));

                    EnsureTranslationCsvExists(importer, EnglishLocale, baseEntries, englishEntries);
                    EnsureTranslationCsvExists(importer, ChineseHansLocale, baseEntries, chineseEntries);

                    foreach (StringTableEntry baseEntry in baseEntries)
                    {
                        if (string.IsNullOrWhiteSpace(baseEntry.ID))
                        {
                            continue;
                        }

                        if (!seenIds.Add(baseEntry.ID))
                        {
                            Debug.LogWarning($"[YarnLocalizationUtility] 重複した line id を検出したため後続行をスキップします: {baseEntry.ID}");
                            continue;
                        }

                        rows.Add(new YarnTranslationRow
                        {
                            Id = baseEntry.ID,
                            Node = baseEntry.Node ?? string.Empty,
                            Ja = baseEntry.Text ?? string.Empty,
                            En = englishEntries.TryGetValue(baseEntry.ID, out StringTableEntry enEntry) ? enEntry.Text ?? string.Empty : string.Empty,
                            ZhHans = chineseEntries.TryGetValue(baseEntry.ID, out StringTableEntry zhEntry) ? zhEntry.Text ?? string.Empty : string.Empty,
                            Comment = baseEntry.Comment ?? string.Empty,
                        });
                    }
                }

                WriteYarnTranslationCsv(GetFullPath(YarnExportAssetPath), rows);
                AssetDatabase.Refresh();
                Debug.Log($"[YarnLocalizationUtility] Yarn 翻訳CSVを出力しました: {YarnExportAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Yarn Localization", exception.Message, "OK");
            }
        }

        public static void ImportTranslationCsv()
        {
            try
            {
                string sourcePath = GetFullPath(YarnExportAssetPath);
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException($"翻訳CSVが見つかりません: {sourcePath}");
                }

                GenerateLineTags();
                EnsureExportFolderExists();

                Dictionary<string, YarnTranslationRow> importedRows = LoadYarnTranslationRows(sourcePath)
                    .GroupBy(row => row.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

                foreach (YarnProjectImporter importer in FindYarnProjectImporters())
                {
                    ConfigureLocalizationFiles(importer);

                    List<StringTableEntry> baseEntries = SyncBaseStrings(importer);
                    Dictionary<string, StringTableEntry> existingEnglish = LoadStringTableById(GetStandardStringsFullPath(importer, EnglishLocale));
                    Dictionary<string, StringTableEntry> existingChinese = LoadStringTableById(GetStandardStringsFullPath(importer, ChineseHansLocale));

                    ValidateBaseJapanese(importer, baseEntries, importedRows);

                    List<StringTableEntry> englishOutput = BuildTranslatedEntries(baseEntries, existingEnglish, importedRows, EnglishLocale);
                    List<StringTableEntry> chineseOutput = BuildTranslatedEntries(baseEntries, existingChinese, importedRows, ChineseHansLocale);

                    WriteStringTable(GetStandardStringsFullPath(importer, EnglishLocale), englishOutput);
                    WriteStringTable(GetStandardStringsFullPath(importer, ChineseHansLocale), chineseOutput);
                }

                AssetDatabase.Refresh();

                foreach (YarnProjectImporter importer in FindYarnProjectImporters())
                {
                    AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
                }

                AssetDatabase.SaveAssets();
                Debug.Log($"[YarnLocalizationUtility] Yarn 翻訳CSVを取り込みました: {YarnExportAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Yarn Localization", exception.Message, "OK");
            }
        }

        public static void MergeDialoguePreviewCsv()
        {
            try
            {
                ExportTranslationCsv();

                List<MasterTranslationRow> rows = new List<MasterTranslationRow>();
                rows.AddRange(LoadReactiveRows());
                rows.AddRange(LoadYarnRowsForMaster());

                WriteMasterTranslationCsv(GetFullPath(MasterExportAssetPath), rows);
                AssetDatabase.Refresh();
                Debug.Log($"[YarnLocalizationUtility] master_translation.csv を出力しました: {MasterExportAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Yarn Localization", exception.Message, "OK");
            }
        }

        private static void ConfigureLocalizationFiles(YarnProjectImporter importer)
        {
            Yarn.Compiler.Project project = importer.GetProject();
            if (project == null)
            {
                throw new InvalidOperationException($"YarnProject を読み込めません: {importer.assetPath}");
            }

            if (!string.Equals(project.BaseLanguage, BaseLocale, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[YarnLocalizationUtility] BaseLanguage が {project.BaseLanguage} です。想定は {BaseLocale} ですが、このまま続行します: {importer.assetPath}");
            }

            SetLocalizationPath(project, importer.assetPath, BaseLocale, GetStandardStringsAssetPath(importer, BaseLocale));
            SetLocalizationPath(project, importer.assetPath, EnglishLocale, GetStandardStringsAssetPath(importer, EnglishLocale));
            SetLocalizationPath(project, importer.assetPath, ChineseHansLocale, GetStandardStringsAssetPath(importer, ChineseHansLocale));

            project.SaveToFile(importer.assetPath);
            AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void SetLocalizationPath(Yarn.Compiler.Project project, string yarnProjectAssetPath, string localeCode, string stringsAssetPath)
        {
            if (project.Localisation == null)
            {
                project.Localisation = new Dictionary<string, Yarn.Compiler.Project.LocalizationInfo>(StringComparer.OrdinalIgnoreCase);
            }

            Yarn.Compiler.Project.LocalizationInfo info = project.Localisation.TryGetValue(localeCode, out Yarn.Compiler.Project.LocalizationInfo existing)
                ? existing
                : new Yarn.Compiler.Project.LocalizationInfo();

            info.Strings = MakeRelativePathFromProject(yarnProjectAssetPath, stringsAssetPath);
            project.Localisation[localeCode] = info;
        }

        private static List<StringTableEntry> SyncBaseStrings(YarnProjectImporter importer)
        {
            string fullPath = GetStandardStringsFullPath(importer, BaseLocale);
            EnsureParentDirectoryExists(fullPath);
            InvokeYarnProjectUtility("WriteStringsFile", fullPath, importer);
            return LoadStringTableEntries(fullPath);
        }

        private static void EnsureTranslationCsvExists(
            YarnProjectImporter importer,
            string localeCode,
            List<StringTableEntry> baseEntries,
            Dictionary<string, StringTableEntry> existingEntries)
        {
            string fullPath = GetStandardStringsFullPath(importer, localeCode);
            if (File.Exists(fullPath))
            {
                return;
            }

            List<StringTableEntry> emptyEntries = BuildTranslatedEntries(
                baseEntries,
                existingEntries,
                new Dictionary<string, YarnTranslationRow>(StringComparer.Ordinal),
                localeCode);

            WriteStringTable(fullPath, emptyEntries);
        }

        private static List<StringTableEntry> BuildTranslatedEntries(
            List<StringTableEntry> baseEntries,
            Dictionary<string, StringTableEntry> existingEntries,
            Dictionary<string, YarnTranslationRow> overrides,
            string targetLocale)
        {
            List<StringTableEntry> output = new List<StringTableEntry>(baseEntries.Count);
            for (int i = 0; i < baseEntries.Count; i++)
            {
                StringTableEntry translated = new StringTableEntry(baseEntries[i]);
                translated.Language = targetLocale;

                string localizedText = string.Empty;
                if (overrides.TryGetValue(translated.ID, out YarnTranslationRow overrideRow))
                {
                    localizedText = targetLocale == EnglishLocale
                        ? overrideRow.En ?? string.Empty
                        : overrideRow.ZhHans ?? string.Empty;
                }
                else if (existingEntries.TryGetValue(translated.ID, out StringTableEntry existingEntry))
                {
                    localizedText = existingEntry.Text ?? string.Empty;
                }

                translated.Text = localizedText;
                output.Add(translated);
            }

            return output;
        }

        private static void ValidateBaseJapanese(
            YarnProjectImporter importer,
            List<StringTableEntry> baseEntries,
            Dictionary<string, YarnTranslationRow> importedRows)
        {
            for (int i = 0; i < baseEntries.Count; i++)
            {
                StringTableEntry entry = baseEntries[i];
                if (!importedRows.TryGetValue(entry.ID, out YarnTranslationRow imported))
                {
                    continue;
                }

                string baseText = entry.Text ?? string.Empty;
                string importedText = imported.Ja ?? string.Empty;
                if (!string.Equals(baseText, importedText, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[YarnLocalizationUtility] JA列は Yarn スクリプトを正とするため import では反映しません: {importer.assetPath} / {entry.ID}");
                }
            }
        }

        private static List<MasterTranslationRow> LoadReactiveRows()
        {
            string previewPath = ResolveDialoguePreviewPath();
            if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(GetFullPath(previewPath)))
            {
                Debug.LogWarning("[YarnLocalizationUtility] Dialogue Preview CSV が見つからないため Reactive 行は空になります。");
                return new List<MasterTranslationRow>();
            }

            DialogueDataLoader.DataSet dataSet = DialogueDataLoader.LoadFromPath(previewPath);
            List<MasterTranslationRow> rows = new List<MasterTranslationRow>(dataSet.Entries.Count);

            for (int i = 0; i < dataSet.Entries.Count; i++)
            {
                DialogueEntry entry = dataSet.Entries[i];
                rows.Add(new MasterTranslationRow
                {
                    SourceType = "Reactive",
                    Id = entry.InternalId ?? string.Empty,
                    Node = string.Empty,
                    Ja = entry.Text ?? string.Empty,
                    En = entry.TextEnglish ?? string.Empty,
                    ZhHans = entry.TextChinese ?? string.Empty,
                });
            }

            return rows;
        }

        private static List<MasterTranslationRow> LoadYarnRowsForMaster()
        {
            List<YarnTranslationRow> yarnRows = LoadYarnTranslationRows(GetFullPath(YarnExportAssetPath));
            return yarnRows.Select(row => new MasterTranslationRow
            {
                SourceType = "Yarn",
                Id = row.Id ?? string.Empty,
                Node = row.Node ?? string.Empty,
                Ja = row.Ja ?? string.Empty,
                En = row.En ?? string.Empty,
                ZhHans = row.ZhHans ?? string.Empty,
            }).ToList();
        }

        private static string ResolveDialoguePreviewPath()
        {
            if (File.Exists(GetFullPath(DefaultPreviewCsvPath)))
            {
                return DefaultPreviewCsvPath;
            }

            return string.Empty;
        }

        private static List<YarnProjectImporter> FindYarnProjectImporters()
        {
            List<YarnProjectImporter> importers = new List<YarnProjectImporter>();
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(YarnProject)}");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetImporter.GetAtPath(path) is YarnProjectImporter importer)
                {
                    importers.Add(importer);
                }
            }

            importers.Sort((left, right) => string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase));
            return importers;
        }

        private static Dictionary<string, StringTableEntry> LoadStringTableById(string fullPath)
        {
            Dictionary<string, StringTableEntry> result = new Dictionary<string, StringTableEntry>(StringComparer.Ordinal);
            if (!File.Exists(fullPath))
            {
                return result;
            }

            List<StringTableEntry> entries = LoadStringTableEntries(fullPath);
            for (int i = 0; i < entries.Count; i++)
            {
                result[entries[i].ID] = entries[i];
            }

            return result;
        }

        private static List<StringTableEntry> LoadStringTableEntries(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return new List<StringTableEntry>();
            }

            return StringTableEntry.ParseFromCSV(File.ReadAllText(fullPath, Encoding.UTF8)).ToList();
        }

        private static List<YarnTranslationRow> LoadYarnTranslationRows(string fullPath)
        {
            List<YarnTranslationRow> rows = new List<YarnTranslationRow>();
            if (!File.Exists(fullPath))
            {
                return rows;
            }

            List<string[]> csvRows = ParseCsv(File.ReadAllText(fullPath, Encoding.UTF8));
            if (csvRows.Count == 0)
            {
                return rows;
            }

            Dictionary<string, int> headerMap = BuildHeaderMap(csvRows[0]);
            for (int i = 1; i < csvRows.Count; i++)
            {
                string[] columns = csvRows[i];
                if (IsEmptyRow(columns))
                {
                    continue;
                }

                rows.Add(new YarnTranslationRow
                {
                    Id = GetColumn(columns, headerMap, "id"),
                    Node = GetColumn(columns, headerMap, "node"),
                    Ja = GetColumn(columns, headerMap, "ja"),
                    En = GetColumn(columns, headerMap, "en"),
                    ZhHans = GetColumn(columns, headerMap, "zhHans"),
                    Comment = GetColumn(columns, headerMap, "comment"),
                });
            }

            return rows;
        }

        private static void WriteYarnTranslationCsv(string fullPath, List<YarnTranslationRow> rows)
        {
            EnsureParentDirectoryExists(fullPath);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("id,node,ja,en,zhHans,comment");
            for (int i = 0; i < rows.Count; i++)
            {
                YarnTranslationRow row = rows[i];
                builder.Append(ToCsvField(row.Id)).Append(',')
                    .Append(ToCsvField(row.Node)).Append(',')
                    .Append(ToCsvField(row.Ja)).Append(',')
                    .Append(ToCsvField(row.En)).Append(',')
                    .Append(ToCsvField(row.ZhHans)).Append(',')
                    .Append(ToCsvField(row.Comment)).AppendLine();
            }

            File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(true));
        }

        private static void WriteMasterTranslationCsv(string fullPath, List<MasterTranslationRow> rows)
        {
            EnsureParentDirectoryExists(fullPath);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("sourceType,id,node,ja,en,zhHans");
            for (int i = 0; i < rows.Count; i++)
            {
                MasterTranslationRow row = rows[i];
                builder.Append(ToCsvField(row.SourceType)).Append(',')
                    .Append(ToCsvField(row.Id)).Append(',')
                    .Append(ToCsvField(row.Node)).Append(',')
                    .Append(ToCsvField(row.Ja)).Append(',')
                    .Append(ToCsvField(row.En)).Append(',')
                    .Append(ToCsvField(row.ZhHans)).AppendLine();
            }

            File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(true));
        }

        private static void WriteStringTable(string fullPath, IEnumerable<StringTableEntry> entries)
        {
            EnsureParentDirectoryExists(fullPath);
            string csv = StringTableEntry.CreateCSV(entries);
            File.WriteAllText(fullPath, csv, new UTF8Encoding(true));
        }

        private static void EnsureExportFolderExists()
        {
            string fullPath = GetFullPath(ExportFolderAssetPath);
            Directory.CreateDirectory(fullPath);
        }

        private static string GetStandardStringsAssetPath(YarnProjectImporter importer, string localeCode)
        {
            string safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(importer.assetPath));
            return $"{ExportFolderAssetPath}/{safeName}.strings.{localeCode}.csv";
        }

        private static string GetStandardStringsFullPath(YarnProjectImporter importer, string localeCode)
        {
            return GetFullPath(GetStandardStringsAssetPath(importer, localeCode));
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "yarn";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            }

            return builder.ToString();
        }

        private static string MakeRelativePathFromProject(string yarnProjectAssetPath, string targetAssetPath)
        {
            string fromDirectory = Path.GetDirectoryName(GetFullPath(yarnProjectAssetPath)) ?? Directory.GetCurrentDirectory();
            string toPath = GetFullPath(targetAssetPath);

            Uri fromUri = new Uri(AppendDirectorySeparator(fromDirectory));
            Uri toUri = new Uri(toPath);

            string relativePath = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
            return relativePath.Replace("\\", "/");
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static object InvokeYarnProjectUtility(string methodName, params object[] arguments)
        {
            Type utilityType = typeof(YarnProjectImporter).Assembly.GetType("Yarn.Unity.Editor.YarnProjectUtility");
            if (utilityType == null)
            {
                throw new InvalidOperationException("Yarn.Unity.Editor.YarnProjectUtility が見つかりません。");
            }

            MethodInfo method = utilityType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);

            if (method == null)
            {
                throw new MissingMethodException($"Yarn.Unity.Editor.YarnProjectUtility.{methodName} が見つかりません。");
            }

            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        private static Dictionary<string, int> BuildHeaderMap(string[] headers)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                string key = headers[i] ?? string.Empty;
                if (!map.ContainsKey(key))
                {
                    map.Add(key, i);
                }
            }

            return map;
        }

        private static string GetColumn(string[] columns, Dictionary<string, int> headerMap, string header)
        {
            return headerMap.TryGetValue(header, out int index) && index >= 0 && index < columns.Length
                ? columns[index]
                : string.Empty;
        }

        private static bool IsEmptyRow(string[] columns)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(columns[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string[]> ParseCsv(string text)
        {
            List<string[]> rows = new List<string[]>();
            List<string> currentRow = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        currentRow.Add(currentField.ToString());
                        currentField.Length = 0;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        currentRow.Add(currentField.ToString());
                        currentField.Length = 0;
                        rows.Add(currentRow.ToArray());
                        currentRow.Clear();
                        break;
                    default:
                        currentField.Append(c);
                        break;
                }
            }

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                rows.Add(currentRow.ToArray());
            }

            return rows;
        }

        private static string ToCsvField(string value)
        {
            string safeValue = value ?? string.Empty;
            if (safeValue.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return safeValue;
            }

            return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
        }

        private static void EnsureParentDirectoryExists(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string GetFullPath(string assetPath)
        {
            string normalized = assetPath.Replace("\\", "/");
            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(Directory.GetParent(Application.dataPath).FullName, normalized).Replace("\\", "/");
            }

            return Path.Combine(Directory.GetCurrentDirectory(), normalized).Replace("\\", "/");
        }
    }
}
