using Backgammon.Conversation;
using UnityEditor;

namespace Nekolpos.Conversation.Editor
{
    public sealed class KanjiReadingDictionaryAssetPostprocessor : AssetPostprocessor
    {
        private const string SourceCsvPath = "TalkSource/TalkCSV/KanjiReadingDictionary.csv";
        private const string EncryptedBytesPath = "Assets/Resources/TalkData/KanjiReadingDictionary.bytes";

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsDictionaryPath(importedAssets) ||
                ContainsDictionaryPath(deletedAssets) ||
                ContainsDictionaryPath(movedAssets) ||
                ContainsDictionaryPath(movedFromAssetPaths))
            {
                KanjiReadingDictionary.ResetDefaultForTests();
            }
        }

        private static bool ContainsDictionaryPath(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i]?.Replace("\\", "/");
                if (path == SourceCsvPath || path == EncryptedBytesPath)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
