using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Backgammon.Conversation
{
    public sealed class ConversationDataManager : MonoBehaviour
    {
        public readonly struct CsvSource
        {
            public CsvSource(string name, string text)
            {
                Name = name ?? string.Empty;
                Text = text ?? string.Empty;
            }

            public string Name { get; }
            public string Text { get; }
        }

        private static readonly string[] DefaultCsvResourcePaths =
        {
            "TalkCSV/DialoguePreview_Integrated",
            "TalkCSV/CatCharacters",
            "TalkCSV/teacchseries_dialogue_source_2201",
            "TalkCSV/teacchTalk_body",
            "TalkCSV/teacchCatExpression",
            "TalkCSV/teacchFelidae",
            "TalkCSV/teacchCatBreed",
            "TalkCSV/teacchFavoriteWeather"
        };

        private const string DefaultVulgarCsvResourcePath = "TalkCSV/VulgarLanguage 2fd3350ea12d8001872cf0e28346f230";
        private const string DefaultEncryptedCsvResourceFolder = "TalkData";
        private const string DefaultEncryptedVulgarCsvResourcePath = "TalkData/VulgarLanguage 2fd3350ea12d8001872cf0e28346f230";
        private const string KeyEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_KEY";
        private const string IvEnvironmentVariable = "NEKOLPOS_CSV_ENCRYPTION_IV";
        private const string PackagedCryptoSettingsFileName = "nekolpos_talkdata.key";
        private const string PackagedCryptoSettingsResourcePath = "BuildSecrets/nekolpos_talkdata";

        [SerializeField] private TextAsset jsonConversationData;
        [SerializeField] private TextAsset[] jsonConversationDataFiles;
        [SerializeField] private TextAsset csvConversationData;
        [SerializeField] private TextAsset[] csvConversationDataFiles;
        [SerializeField] private TextAsset vulgarCsvConversationData;
        [SerializeField] private TextAsset[] vulgarCsvConversationDataFiles;
        [SerializeField] private TextAsset encryptedCsvConversationData;
        [SerializeField] private TextAsset[] encryptedCsvConversationDataFiles;
        [SerializeField] private TextAsset encryptedVulgarCsvConversationData;
        [SerializeField] private TextAsset[] encryptedVulgarCsvConversationDataFiles;
        [SerializeField] private bool allowPlaintextCsvResourceFallbackInDevelopment = true;
        [SerializeField] private bool useCsv;
        [SerializeField] private string locale = "ja";

        private ConversationRouteCatalog loadedCatalog = new(null);

        public ConversationRouteCatalog LoadedCatalog => loadedCatalog;
        public string Locale => string.IsNullOrWhiteSpace(locale) ? "ja" : locale;
        public event Action<string> LocaleChanged;

        public void SetLocale(string newLocale, bool reload = true)
        {
            string normalizedLocale = NormalizeLocaleCode(newLocale);
            bool changed = string.Equals(Locale, normalizedLocale, StringComparison.OrdinalIgnoreCase) == false;

            locale = normalizedLocale;

            if (reload)
            {
                Reload();
            }

            if (changed || reload)
            {
                LocaleChanged?.Invoke(Locale);
            }
        }

        public List<TextAsset> GetActiveCsvAssets()
        {
            EnsureRuntimeDefaults();

            var activeAssets = new List<TextAsset>();
            if (csvConversationDataFiles != null)
            {
                for (var i = 0; i < csvConversationDataFiles.Length; i++)
                {
                    if (ShouldSkipLegacyDialoguePreview(csvConversationDataFiles[i], csvConversationDataFiles))
                    {
                        continue;
                    }

                    if (IsConversationRouteCsvAsset(csvConversationDataFiles[i]))
                    {
                        activeAssets.Add(csvConversationDataFiles[i]);
                    }
                }
            }

            if (activeAssets.Count == 0 && IsConversationRouteCsvAsset(csvConversationData))
            {
                activeAssets.Add(csvConversationData);
            }

            return activeAssets;
        }

        public List<CsvSource> GetActiveCsvSources()
        {
            EnsureRuntimeDefaults();

            var activeSources = new List<CsvSource>();
            AddDecryptedCsvSources(activeSources, encryptedCsvConversationDataFiles);
            if (!ShouldSkipLegacyDialoguePreview(encryptedCsvConversationData, encryptedCsvConversationDataFiles))
            {
                AddDecryptedCsvSource(activeSources, encryptedCsvConversationData);
            }

            if (activeSources.Count > 0)
            {
                return activeSources;
            }

            if (csvConversationDataFiles != null)
            {
                for (int i = 0; i < csvConversationDataFiles.Length; i++)
                {
                    TextAsset asset = csvConversationDataFiles[i];
                    if (ShouldSkipLegacyDialoguePreview(asset, csvConversationDataFiles))
                    {
                        continue;
                    }

                    if (IsDialogueEngineCsvAsset(asset))
                    {
                        activeSources.Add(new CsvSource(asset.name, asset.text));
                    }
                }
            }

            if (activeSources.Count == 0 && IsDialogueEngineCsvAsset(csvConversationData))
            {
                activeSources.Add(new CsvSource(csvConversationData.name, csvConversationData.text));
            }

            return activeSources;
        }

        public List<TextAsset> GetActiveVulgarCsvAssets()
        {
            EnsureRuntimeDefaults();

            var activeAssets = new List<TextAsset>();
            if (vulgarCsvConversationDataFiles != null)
            {
                for (var i = 0; i < vulgarCsvConversationDataFiles.Length; i++)
                {
                    if (vulgarCsvConversationDataFiles[i] != null)
                    {
                        activeAssets.Add(vulgarCsvConversationDataFiles[i]);
                    }
                }
            }

            if (activeAssets.Count == 0 && vulgarCsvConversationData != null)
            {
                activeAssets.Add(vulgarCsvConversationData);
            }

            return activeAssets;
        }

        public List<string> GetActiveVulgarCsvTexts()
        {
            EnsureRuntimeDefaults();

            var activeTexts = new List<string>();
            AddDecryptedTexts(activeTexts, encryptedVulgarCsvConversationDataFiles);
            AddDecryptedText(activeTexts, encryptedVulgarCsvConversationData);
            if (activeTexts.Count > 0)
            {
                return activeTexts;
            }

            List<TextAsset> plaintextAssets = GetActiveVulgarCsvAssets();
            for (int i = 0; i < plaintextAssets.Count; i++)
            {
                if (plaintextAssets[i] != null)
                {
                    activeTexts.Add(plaintextAssets[i].text);
                }
            }

            return activeTexts;
        }

        public ConversationDataLoadResult Reload()
        {
            EnsureRuntimeDefaults();
            EnsureActiveSourceMode();

            var options = new ConversationDataLoadOptions
            {
                locale = string.IsNullOrWhiteSpace(locale) ? "ja" : locale,
                logErrorsToConsole = true
            };

            ConversationDataLoadResult result;
            if (useCsv)
            {
                if (HasAnyEncryptedCsvSource())
                {
                    result = ReloadEncryptedCsvAssets(encryptedCsvConversationDataFiles, encryptedCsvConversationData, options);
                    if ((result.catalog == null || result.catalog.Routes.Count == 0) &&
                        allowPlaintextCsvResourceFallbackInDevelopment &&
                        IsDevelopmentRuntime() &&
                        (HasAnyAsset(csvConversationDataFiles) || csvConversationData != null))
                    {
                        Debug.LogWarning("[ConversationDataManager] Encrypted TalkData could not be loaded. Falling back to plaintext CSV sources for development runtime.");
                        result = ReloadAssets(csvConversationDataFiles, csvConversationData, options, false);
                    }
                }
                else
                {
                    result = ReloadAssets(csvConversationDataFiles, csvConversationData, options, false);
                }
            }
            else
            {
                result = ReloadAssets(jsonConversationDataFiles, jsonConversationData, options, true);
            }

            loadedCatalog = result.catalog ?? new ConversationRouteCatalog(null);
            return result;
        }

        public ConversationRouter CreateRouter()
        {
            return new ConversationRouter(loadedCatalog);
        }

        private void Awake()
        {
            EnsureRuntimeDefaults();
            Reload();
        }

        private void EnsureRuntimeDefaults()
        {
            if (HasAnyConversationSource())
            {
                EnsureDefaultCsvSourcesPresent();
                return;
            }

            useCsv = true;
            encryptedCsvConversationDataFiles = LoadAllResources(DefaultEncryptedCsvResourceFolder);
            encryptedVulgarCsvConversationData = Resources.Load<TextAsset>(DefaultEncryptedVulgarCsvResourcePath);

            if (encryptedCsvConversationDataFiles.Length > 0 || encryptedVulgarCsvConversationData != null)
            {
                Debug.Log("[ConversationDataManager] Serialized source was not assigned. Loaded encrypted TalkData assets from Resources/TalkData.");
                return;
            }

            if (allowPlaintextCsvResourceFallbackInDevelopment && IsDevelopmentRuntime())
            {
                csvConversationDataFiles = LoadResources(DefaultCsvResourcePaths);
                vulgarCsvConversationData = Resources.Load<TextAsset>(DefaultVulgarCsvResourcePath);
                if (csvConversationDataFiles.Length > 0 || vulgarCsvConversationData != null)
                {
                    Debug.LogWarning("[ConversationDataManager] Loaded plaintext TalkCSV assets from Resources for development runtime only.");
                    return;
                }
            }

            if (IsDevelopmentRuntime())
            {
                Debug.LogWarning("[ConversationDataManager] No encrypted TalkData resources were found.");
            }
            else
            {
                Debug.LogError("[ConversationDataManager] Release runtime requires encrypted TalkData resources. Plaintext TalkCSV fallback is disabled.");
            }
        }

        private void EnsureDefaultCsvSourcesPresent()
        {
            if (!useCsv)
            {
                return;
            }

            TextAsset[] defaultEncryptedCsvResources = LoadAllResources(DefaultEncryptedCsvResourceFolder);
            if (HasAnyAsset(encryptedCsvConversationDataFiles) ||
                encryptedCsvConversationData != null ||
                HasAnyAsset(defaultEncryptedCsvResources))
            {
                encryptedCsvConversationDataFiles = AppendMissingResources(
                    encryptedCsvConversationDataFiles,
                    encryptedCsvConversationData,
                    defaultEncryptedCsvResources);
            }

            if (!HasAnyAsset(encryptedVulgarCsvConversationDataFiles) && encryptedVulgarCsvConversationData == null)
            {
                encryptedVulgarCsvConversationData = Resources.Load<TextAsset>(DefaultEncryptedVulgarCsvResourcePath);
            }

            if (HasAnyAsset(csvConversationDataFiles) || csvConversationData != null)
            {
                csvConversationDataFiles = AppendMissingResources(
                    csvConversationDataFiles,
                    csvConversationData,
                    DefaultCsvResourcePaths);
            }
        }

        private static TextAsset[] AppendMissingResources(TextAsset[] currentAssets, TextAsset legacyAsset, string[] resourcePaths)
        {
            var result = new List<TextAsset>();
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddKnownAsset(result, knownNames, legacyAsset);
            if (currentAssets != null)
            {
                for (int i = 0; i < currentAssets.Length; i++)
                {
                    AddKnownAsset(result, knownNames, currentAssets[i]);
                }
            }

            if (resourcePaths != null)
            {
                for (int i = 0; i < resourcePaths.Length; i++)
                {
                    TextAsset asset = Resources.Load<TextAsset>(resourcePaths[i]);
                    AddKnownAsset(result, knownNames, asset);
                }
            }

            return result.ToArray();
        }

        private static TextAsset[] AppendMissingResources(TextAsset[] currentAssets, TextAsset legacyAsset, TextAsset[] resourceAssets)
        {
            var result = new List<TextAsset>();
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddKnownAsset(result, knownNames, legacyAsset);
            if (currentAssets != null)
            {
                for (int i = 0; i < currentAssets.Length; i++)
                {
                    AddKnownAsset(result, knownNames, currentAssets[i]);
                }
            }

            if (resourceAssets != null)
            {
                for (int i = 0; i < resourceAssets.Length; i++)
                {
                    AddKnownAsset(result, knownNames, resourceAssets[i]);
                }
            }

            return result.ToArray();
        }

        private static void AddKnownAsset(List<TextAsset> assets, HashSet<string> knownNames, TextAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            string name = asset.name ?? string.Empty;
            if (!knownNames.Add(name))
            {
                return;
            }

            assets.Add(asset);
        }

        private bool HasAnyConversationSource()
        {
            if (jsonConversationData != null ||
                csvConversationData != null ||
                vulgarCsvConversationData != null ||
                encryptedCsvConversationData != null ||
                encryptedVulgarCsvConversationData != null)
            {
                return true;
            }

            return HasAnyAsset(jsonConversationDataFiles) ||
                   HasAnyAsset(csvConversationDataFiles) ||
                   HasAnyAsset(vulgarCsvConversationDataFiles) ||
                   HasAnyAsset(encryptedCsvConversationDataFiles) ||
                   HasAnyAsset(encryptedVulgarCsvConversationDataFiles);
        }

        private void EnsureActiveSourceMode()
        {
            if (useCsv)
            {
                if (!HasAnyCsvSource() && HasAnyJsonSource())
                {
                    useCsv = false;
                }

                return;
            }

            if (!HasAnyJsonSource() && HasAnyCsvSource())
            {
                useCsv = true;
            }
        }

        private bool HasAnyJsonSource()
        {
            return jsonConversationData != null || HasAnyAsset(jsonConversationDataFiles);
        }

        private bool HasAnyCsvSource()
        {
            return csvConversationData != null ||
                   vulgarCsvConversationData != null ||
                   encryptedCsvConversationData != null ||
                   encryptedVulgarCsvConversationData != null ||
                   HasAnyAsset(csvConversationDataFiles) ||
                   HasAnyAsset(vulgarCsvConversationDataFiles) ||
                   HasAnyAsset(encryptedCsvConversationDataFiles) ||
                   HasAnyAsset(encryptedVulgarCsvConversationDataFiles);
        }

        private bool HasAnyEncryptedCsvSource()
        {
            return encryptedCsvConversationData != null || HasAnyAsset(encryptedCsvConversationDataFiles);
        }

        private static bool HasAnyAsset(TextAsset[] assets)
        {
            if (assets == null)
            {
                return false;
            }

            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static TextAsset[] LoadResources(string[] resourcePaths)
        {
            var assets = new List<TextAsset>(resourcePaths.Length);
            for (var i = 0; i < resourcePaths.Length; i++)
            {
                TextAsset asset = Resources.Load<TextAsset>(resourcePaths[i]);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets.ToArray();
        }

        private static TextAsset[] LoadAllResources(string resourceFolder)
        {
            if (string.IsNullOrWhiteSpace(resourceFolder))
            {
                return Array.Empty<TextAsset>();
            }

            TextAsset[] assets = Resources.LoadAll<TextAsset>(resourceFolder);
            return assets ?? Array.Empty<TextAsset>();
        }

        private static bool ShouldSkipLegacyDialoguePreview(TextAsset asset, TextAsset[] availableAssets)
        {
            if (!IsLegacyDialoguePreviewAsset(asset))
            {
                return false;
            }

            if (availableAssets == null)
            {
                return false;
            }

            for (int i = 0; i < availableAssets.Length; i++)
            {
                if (IsIntegratedDialoguePreviewAsset(availableAssets[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLegacyDialoguePreviewAsset(TextAsset asset)
        {
            return asset != null &&
                   string.Equals(asset.name?.Trim(), "DialoguePreview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIntegratedDialoguePreviewAsset(TextAsset asset)
        {
            return asset != null &&
                   string.Equals(asset.name?.Trim(), "DialoguePreview_Integrated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDevelopmentRuntime()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }

        public static bool TryLoadEncryptedCsvResource(string resourcePath, out string csvText, out string error)
        {
            csvText = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                error = "Encrypted resource path is empty.";
                return false;
            }

            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                error = $"Encrypted resource was not found: Resources/{resourcePath}";
                return false;
            }

            return TryDecryptCsvText(asset, out csvText, out error);
        }

        private static string NormalizeLocaleCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "ja";
            }

            string normalized = value.Trim();

            if (normalized.Equals("jp", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "ja";
            }

            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return normalized;
        }

        private static ConversationDataLoadResult ReloadAssets(
            TextAsset[] assets,
            TextAsset legacyAsset,
            ConversationDataLoadOptions options,
            bool isJson)
        {
            var activeAssets = new List<TextAsset>();
            if (assets != null)
            {
                for (var i = 0; i < assets.Length; i++)
                {
                    if (!isJson && !IsConversationRouteCsvAsset(assets[i]))
                    {
                        continue;
                    }

                    if (assets[i] != null)
                    {
                        activeAssets.Add(assets[i]);
                    }
                }
            }

            if (activeAssets.Count == 0)
            {
                if (!isJson && legacyAsset != null && !IsConversationRouteCsvAsset(legacyAsset))
                {
                    legacyAsset = null;
                }

                if (legacyAsset == null)
                {
                    return new ConversationDataLoadResult();
                }

                return isJson
                    ? ConversationDataLoader.LoadFromJsonTextAsset(legacyAsset, options)
                    : ConversationDataLoader.LoadFromCsvTextAsset(legacyAsset, options);
            }

            var results = new List<ConversationDataLoadResult>(activeAssets.Count);
            for (var i = 0; i < activeAssets.Count; i++)
            {
                results.Add(isJson
                    ? ConversationDataLoader.LoadFromJsonTextAsset(activeAssets[i], options)
                    : ConversationDataLoader.LoadFromCsvTextAsset(activeAssets[i], options));
            }

            return ConversationDataLoader.Merge(
                results,
                isJson ? nameof(jsonConversationDataFiles) : nameof(csvConversationDataFiles));
        }

        private static ConversationDataLoadResult ReloadEncryptedCsvAssets(
            TextAsset[] assets,
            TextAsset legacyAsset,
            ConversationDataLoadOptions options)
        {
            var results = new List<ConversationDataLoadResult>();
            if (assets != null)
            {
                for (var i = 0; i < assets.Length; i++)
                {
                    if (ShouldSkipLegacyDialoguePreview(assets[i], assets))
                    {
                        continue;
                    }

                    if (!IsConversationRouteCsvAsset(assets[i]))
                    {
                        continue;
                    }

                    AddEncryptedCsvLoadResult(results, assets[i], options);
                }
            }

            if (results.Count == 0)
            {
                if (IsConversationRouteCsvAsset(legacyAsset))
                {
                    AddEncryptedCsvLoadResult(results, legacyAsset, options);
                }
            }

            return ConversationDataLoader.Merge(results, nameof(encryptedCsvConversationDataFiles));
        }

        private static void AddEncryptedCsvLoadResult(
            List<ConversationDataLoadResult> results,
            TextAsset asset,
            ConversationDataLoadOptions options)
        {
            if (asset == null)
            {
                return;
            }

            if (!TryDecryptCsvText(asset, out string csvText, out string error))
            {
                return;
            }

            if (!LooksLikeConversationRouteCsv(csvText))
            {
                return;
            }

            results.Add(ConversationDataLoader.LoadFromCsvText(csvText, asset.name, options));
        }

        private static void AddDecryptedTexts(List<string> texts, TextAsset[] assets)
        {
            if (assets == null)
            {
                return;
            }

            for (int i = 0; i < assets.Length; i++)
            {
                AddDecryptedText(texts, assets[i]);
            }
        }

        private static void AddDecryptedCsvSources(List<CsvSource> sources, TextAsset[] assets)
        {
            if (assets == null)
            {
                return;
            }

            for (int i = 0; i < assets.Length; i++)
            {
                if (ShouldSkipLegacyDialoguePreview(assets[i], assets))
                {
                    continue;
                }

                AddDecryptedCsvSource(sources, assets[i]);
            }
        }

        private static void AddDecryptedCsvSource(List<CsvSource> sources, TextAsset asset)
        {
            if (!IsDialogueEngineCsvAsset(asset))
            {
                return;
            }

            if (TryDecryptCsvText(asset, out string csvText, out string error))
            {
                if (LooksLikeDialogueEngineCsv(csvText))
                {
                    sources.Add(new CsvSource(asset.name, csvText));
                }
            }
        }

        private static void AddDecryptedText(List<string> texts, TextAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            if (TryDecryptCsvText(asset, out string csvText, out string error))
            {
                texts.Add(csvText);
            }
            else
            {
                Debug.LogError($"[ConversationDataManager] Failed to decrypt {asset.name}: {error}");
            }
        }

        private static bool TryDecryptCsvText(TextAsset asset, out string csvText, out string error)
        {
            csvText = string.Empty;
            error = string.Empty;
            if (asset == null)
            {
                error = "Encrypted TextAsset is null.";
                return false;
            }

            if (!TryResolveCryptoSettings(out byte[] key, out byte[] iv, out error))
            {
                return false;
            }

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using MemoryStream memoryStream = new MemoryStream(asset.bytes);
                using CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
                using StreamReader reader = new StreamReader(cryptoStream, Encoding.UTF8);
                csvText = reader.ReadToEnd();
                return true;
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or ArgumentException)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool LooksLikeConversationRouteCsv(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return false;
            }

            string firstLine = ReadFirstCsvLine(csvText);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return false;
            }

            string normalizedHeader = "," + firstLine.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant() + ",";
            bool hasDialoguePreviewShape =
                normalizedHeader.Contains(",regexid,") &&
                normalizedHeader.Contains(",pattern,") &&
                normalizedHeader.Contains(",order,") &&
                (normalizedHeader.Contains(",outputja,") || normalizedHeader.Contains(",textjp,") || normalizedHeader.Contains(",textjp1,"));

            bool hasGenericRouteShape =
                normalizedHeader.Contains(",id,") &&
                (normalizedHeader.Contains(",responses,") ||
                 normalizedHeader.Contains(",response,") ||
                 normalizedHeader.Contains(",regexjp,") ||
                 normalizedHeader.Contains(",outputja,") ||
                 normalizedHeader.Contains(",textjp,") ||
                 normalizedHeader.Contains(",pattern,"));

            return hasDialoguePreviewShape || hasGenericRouteShape;
        }

        private static bool LooksLikeDialogueEngineCsv(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return false;
            }

            string firstLine = ReadFirstCsvLine(csvText);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return false;
            }

            string normalizedHeader = "," + firstLine.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant() + ",";
            bool hasReactionStyleShape =
                (normalizedHeader.Contains(",regex,") || normalizedHeader.Contains(",regexjp,") || normalizedHeader.Contains(",正規表現,") || normalizedHeader.Contains(",patterntext,")) &&
                (normalizedHeader.Contains(",outputja,") || normalizedHeader.Contains(",textjp1,") || normalizedHeader.Contains(",text,") || normalizedHeader.Contains(",line,") || normalizedHeader.Contains(",regexid,") || normalizedHeader.Contains(",pattern,"));

            bool hasCatCharactersShape =
                normalizedHeader.Contains(",pokemon,") ||
                normalizedHeader.Contains(",animalcrossing,");

            return hasReactionStyleShape || hasCatCharactersShape;
        }

        private static string ReadFirstCsvLine(string csvText)
        {
            bool inQuotes = false;
            for (int i = 0; i < csvText.Length; i++)
            {
                char current = csvText[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < csvText.Length && csvText[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && (current == '\r' || current == '\n'))
                {
                    return csvText.Substring(0, i);
                }
            }

            return csvText;
        }

        private static bool TryResolveCryptoSettings(out byte[] key, out byte[] iv, out string error)
        {
            string keyString = ResolveCryptoSetting(KeyEnvironmentVariable);
            string ivString = ResolveCryptoSetting(IvEnvironmentVariable);
            key = Array.Empty<byte>();
            iv = Array.Empty<byte>();
            error = string.Empty;

            if (string.IsNullOrEmpty(keyString) || string.IsNullOrEmpty(ivString))
            {
                error = $"{KeyEnvironmentVariable} and {IvEnvironmentVariable} must be set in environment variables or StreamingAssets/{PackagedCryptoSettingsFileName} to decrypt TalkData bytes.";
                return false;
            }

            key = Encoding.UTF8.GetBytes(keyString);
            iv = Encoding.UTF8.GetBytes(ivString);
            if (key.Length != 32 || iv.Length != 16)
            {
                error = $"{KeyEnvironmentVariable} must be 32 bytes and {IvEnvironmentVariable} must be 16 bytes.";
                return false;
            }

            return true;
        }

        private static string ResolveCryptoSetting(string variableName)
        {
            string value = ResolveEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            return ResolvePackagedCryptoSetting(variableName);
        }

        private static string ResolveEnvironmentVariable(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            value = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine);
        }

        private static string ResolvePackagedCryptoSetting(string variableName)
        {
            string resourceValue = ResolvePackagedCryptoSettingFromResource(variableName);
            if (!string.IsNullOrEmpty(resourceValue))
            {
                return resourceValue;
            }

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, PackagedCryptoSettingsFileName);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return string.Empty;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    line = line.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string keyName = line.Substring(0, separatorIndex).Trim();
                    if (!string.Equals(keyName, variableName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return line.Substring(separatorIndex + 1).Trim();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Debug.LogError($"[ConversationDataManager] Failed to read packaged TalkData key file: {exception.Message}");
            }

            return string.Empty;
        }

        private static string ResolvePackagedCryptoSettingFromResource(string variableName)
        {
            TextAsset asset = Resources.Load<TextAsset>(PackagedCryptoSettingsResourcePath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return string.Empty;
            }

            return ResolveSettingFromLines(asset.text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None), variableName);
        }

        private static string ResolveSettingFromLines(string[] lines, string variableName)
        {
            if (lines == null || string.IsNullOrEmpty(variableName))
            {
                return string.Empty;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string keyName = line.Substring(0, separatorIndex).Trim();
                if (!string.Equals(keyName, variableName, StringComparison.Ordinal))
                {
                    continue;
                }

                return line.Substring(separatorIndex + 1).Trim();
            }

            return string.Empty;
        }

        private static ConversationDataLoadResult CreateLoadError(string sourceName, string message, ConversationDataLoadOptions options)
        {
            var result = new ConversationDataLoadResult();
            result.errors.Add(new ConversationDataValidationError
            {
                sourceName = sourceName ?? string.Empty,
                lineNumber = 1,
                routeId = string.Empty,
                message = message ?? "Unknown error."
            });

            if (options?.logErrorsToConsole ?? true)
            {
                Debug.LogError($"[ConversationDataLoader] {result.errors[0]}");
            }

            return result;
        }

        private static bool IsConversationRouteCsvAsset(TextAsset asset)
        {
            return asset != null &&
                   !IsStandaloneCatalogAssetName(asset.name) &&
                   !string.Equals(asset.name, "CatCharacters", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDialogueEngineCsvAsset(TextAsset asset)
        {
            return asset != null &&
                   !IsStandaloneCatalogAssetName(asset.name);
        }

        private static bool IsStandaloneCatalogAssetName(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                return false;
            }

            string normalized = assetName.Trim();
            return string.Equals(normalized, "BasicSystemDialogue", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "InternalDialogue", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "SystemTimedEvent", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "OBTTutorial", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Regular Expression", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "RegexDict", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("RegexDict ", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "VulgarLanguage", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("VulgarLanguage ", StringComparison.OrdinalIgnoreCase);
        }
    }
}
