using System;
using System.Collections;
using UnityEngine;

namespace Nekolpos.Nyanstagram
{
    /// <summary>
    /// Nyansta UIが画像の保管場所を意識しないための境界です。
    /// Remote/Cache対応時は同じ契約を実装し、UIや日付更新側は変更しません。
    /// </summary>
    public interface INyanstagramPhotoSource
    {
        IEnumerator LoadCatalog(Action<NyanstagramPhotoCatalogLoadResult> completed);
        IEnumerator LoadImage(NyanstagramPhotoEntry photo, Action<NyanstagramPhotoImageLoadResult> completed);
    }

    public sealed class NyanstagramPhotoCatalogLoadResult
    {
        public NyanstagramPhotoCatalogLoadResult(NyanstagramPhotoCatalog catalog, string error)
        {
            Catalog = catalog;
            Error = error ?? string.Empty;
        }

        public NyanstagramPhotoCatalog Catalog { get; }
        public string Error { get; }
        public bool Succeeded => Catalog != null && string.IsNullOrEmpty(Error);
    }

    public sealed class NyanstagramPhotoImageLoadResult
    {
        public NyanstagramPhotoImageLoadResult(Texture2D texture, string error)
        {
            Texture = texture;
            Error = error ?? string.Empty;
        }

        public Texture2D Texture { get; }
        public string Error { get; }
        public bool Succeeded => Texture != null && string.IsNullOrEmpty(Error);
    }
}
