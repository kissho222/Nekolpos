using UnityEngine;

namespace Nekolpos.Data
{
    /// <summary>
    /// 【ScriptableObject：夜に書く「日記」のデータ設計図】
    /// 日常の出来事から、死亡時の特別な反省まで、発生する日記の内容を保存します。
    /// それぞれに「優先度」と「トリガー条件（フラグ名）」を持たせます。
    /// </summary>
    [CreateAssetMenu(fileName = "NewDiaryEntry", menuName = "Nekolpos/Diary Entry")]
    public class DiaryDataSO : ScriptableObject
    {
        [Header("日記の基本情報")]
        [Tooltip("この日記を呼び出すためのユニークなID（例：Diary_Death_01）")]
        public string entryID;

        [Tooltip("これが表示されるための必須フラグ名（空欄なら無条件）\n" +
                 "例：「IsDeadToday」というフラグがONなら表示する")]
        public string requiredFlag;

        [Tooltip("この日記を表示する優先度（数字が大きいほど偉い）。\n" +
                 "通常の日記は「1」、死亡時は「100」等にすれば、必ず死亡日記が優先されます。")]
        public int priority = 1;

        [Header("日記の中身")]
        [TextArea(3, 10)]
        [Tooltip("夜になった時に表示される、あるいは主人公が書き記すテキスト")]
        public string content = "今日は普通の1日だった。";
    }
}
