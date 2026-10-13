using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Nekolpos.Nyanstagram
{
    /// <summary>ゲーム内で表示する猫写真の、画像本体と分離した許諾メタデータです。</summary>
    [Serializable]
    public sealed class NyanstagramPhotoEntry
    {
        private static readonly Regex PhotoIdPattern = new Regex(@"^CAT_\d{6}$", RegexOptions.CultureInvariant);

        public string PhotoId;
        public string CatName;
        public string ContributorName;
        public string ContributorUrl;
        public string ImageFileName;
        public bool Approved;
        public bool AllowGameUse;
        public bool AllowPromotionUse;

        /// <summary>UIに載せてよい、承認済みのゲーム内利用写真かを返します。</summary>
        public bool IsAvailableForGame =>
            Approved && AllowGameUse && IsValid(out _);

        /// <summary>
        /// ローカル/リモートのどちらのカタログでも使う最低限の安全性検査です。
        /// ImageFileNameはパスでなく、変換済みJPEGのファイル名だけを許可します。
        /// </summary>
        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(PhotoId) || !PhotoIdPattern.IsMatch(PhotoId))
            {
                error = "PhotoId は CAT_000001 形式で指定してください。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CatName))
            {
                error = "CatName が空です。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ContributorName))
            {
                error = "ContributorName が空です。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ImageFileName) ||
                !string.Equals(ImageFileName, Path.GetFileName(ImageFileName), StringComparison.Ordinal) ||
                !ImageFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                error = "ImageFileName はパスを含まない .jpg ファイル名にしてください。";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
