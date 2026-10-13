using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Nekolpos.Nyanstagram
{
    /// <summary>StreamingAssets内の変換済みJPEGと私用カタログを読む、現在のローカル実装です。</summary>
    public sealed class LocalNyanstagramPhotoSource : INyanstagramPhotoSource
    {
        public const string CatalogRelativePath = "Nyanstagram/Local/nyanstagram-local-catalog.json";
        public const string ImagesRelativeDirectory = "Nyanstagram/Local/Images";

        public IEnumerator LoadCatalog(Action<NyanstagramPhotoCatalogLoadResult> completed)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(BuildStreamingAssetsUri(CatalogRelativePath)))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(new NyanstagramPhotoCatalogLoadResult(null,
                        $"ローカル写真カタログを読み込めません: {request.error}"));
                    yield break;
                }

                if (!NyanstagramPhotoCatalog.TryParse(request.downloadHandler.text, out NyanstagramPhotoCatalog catalog, out string error))
                {
                    completed?.Invoke(new NyanstagramPhotoCatalogLoadResult(null, error));
                    yield break;
                }

                completed?.Invoke(new NyanstagramPhotoCatalogLoadResult(catalog, string.Empty));
            }
        }

        public IEnumerator LoadImage(NyanstagramPhotoEntry photo, Action<NyanstagramPhotoImageLoadResult> completed)
        {
            if (photo == null || !photo.IsAvailableForGame)
            {
                completed?.Invoke(new NyanstagramPhotoImageLoadResult(null, "ゲーム利用可能な写真ではありません。"));
                yield break;
            }

            string imagePath = $"{ImagesRelativeDirectory}/{photo.ImageFileName}";
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(BuildStreamingAssetsUri(imagePath), true))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(new NyanstagramPhotoImageLoadResult(null,
                        $"ローカル写真を読み込めません ({photo.PhotoId}): {request.error}"));
                    yield break;
                }

                completed?.Invoke(new NyanstagramPhotoImageLoadResult(
                    DownloadHandlerTexture.GetContent(request), string.Empty));
            }
        }

        private static string BuildStreamingAssetsUri(string relativePath)
        {
            string root = Application.streamingAssetsPath;
            if (root.Contains("://", StringComparison.Ordinal) || root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
            {
                return $"{root.TrimEnd('/')}/{relativePath}";
            }

            string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return new Uri(fullPath).AbsoluteUri;
        }
    }
}
