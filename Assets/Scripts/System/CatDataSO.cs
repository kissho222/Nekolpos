using UnityEngine;

namespace Nekolpos.Data
{
    /// <summary>
    /// 【ScriptableObject：猫又（パートナー）のデータ設計図】
    /// パートナーである猫又の名前、代名詞、プレイヤーとの関係性などの「データ」を保存する箱です。
    /// インスペクターから右クリック（Create > Nekolpos > Nekomata Data）でファイルを作り、
    /// プログラムを書き直さなくても自由に変更・調整できるようにします。
    /// </summary>
    [CreateAssetMenu(fileName = "NewNekomataData", menuName = "Nekolpos/Nekomata Data")]
    public class CatDataSO : ScriptableObject
    {
        [Header("基本設定 (Basic Information)")]
        [Tooltip("猫又の名前（例：タロウ、クロ、等）")]
        public string catName = "クロ";

        [Tooltip("猫又の一人称（例：オレ、ボク、ワタシ、オイラ）")]
        public string catPronoun = "オレ";

        [Tooltip("猫又の性別代名詞（彼、彼女等 英語ならHe/She/They）")]
        public string catGender = "彼";

        [Header("プレイヤー情報との連動")]
        [Tooltip("プレイヤー自身の名前")]
        public string playerName = "プレイヤー";

        [Tooltip("プレイヤーの親の呼び方（例：パパママ、お父さんお母さん）")]
        public string parentCall = "パパママ";

        [Tooltip("猫又からプレイヤーへの呼び方（例：お前、キミ、ご主人様、〇〇(名前)）")]
        public string playerCalling = "お前";

        [Header("パラメータ (Stats)")]
        [Tooltip("食事の名前・好みなど")]
        public string favoriteFoodName = "マグロの刺身";

        [Tooltip("プレイヤーへの『好感度』。高いと専用イベントが起きるかも？")]
        [Range(0, 100)]
        public int affectionLevel = 50;

        [Range(0, 100)]
        public int sadisticLevel = 50;

        [Range(0, 100)]
        public int concernLevel = 50;

        [Range(0, 100)]
        public int hostilityLevel = 50;

        [Range(0, 100)]
        public int obedienceLevel = 50;

        [Range(0, 100)]
        public int instinctLevel = 50;
    }
}
