using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nekolpos.Nyanstagram
{
    /// <summary>JsonUtilityで読み書きするニャンスタグラム写真カタログのルートです。</summary>
    [Serializable]
    public sealed class NyanstagramPhotoCatalog
    {
        public List<NyanstagramPhotoEntry> Photos = new List<NyanstagramPhotoEntry>();

        /// <summary>UI用に、承認済みかつゲーム利用を許可された項目だけを返します。</summary>
        public IReadOnlyList<NyanstagramPhotoEntry> GetAvailableForGame()
        {
            return Photos
                .Where(photo => photo != null && photo.IsAvailableForGame)
                .ToList();
        }

        public static bool TryParse(string json, out NyanstagramPhotoCatalog catalog, out string error)
        {
            catalog = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "カタログJSONが空です。";
                return false;
            }

            try
            {
                catalog = JsonUtility.FromJson<NyanstagramPhotoCatalog>(json);
            }
            catch (ArgumentException exception)
            {
                error = $"カタログJSONを読み込めません: {exception.Message}";
                return false;
            }

            if (catalog == null)
            {
                error = "カタログJSONのルートが不正です。";
                return false;
            }

            catalog.Photos ??= new List<NyanstagramPhotoEntry>();
            HashSet<string> photoIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.Photos.Count; index++)
            {
                NyanstagramPhotoEntry photo = catalog.Photos[index];
                if (photo == null)
                {
                    error = $"Photos[{index}] が不正です: 項目がnullです。";
                    return false;
                }

                if (!photo.IsValid(out string entryError))
                {
                    error = $"Photos[{index}] が不正です: {entryError}";
                    return false;
                }

                if (!photoIds.Add(photo.PhotoId))
                {
                    error = $"PhotoId が重複しています: {photo.PhotoId}";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
