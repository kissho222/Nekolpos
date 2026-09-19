using Backgammon.Conversation;
using NUnit.Framework;

namespace Nekolpos.Conversation.Editor
{
    public class JapaneseTextNormalizerTests
    {
        [Test]
        public void NormalizeToken_KeepsFullWidthQuestionMarkAndRegexQuantifier()
        {
            Assert.AreEqual(
                "(は|って)(なんで|どうして|なぜ)?(かしこい)の？",
                JapaneseTextNormalizer.NormalizeToken("(は|って)(なんで|どうして|なぜ)?(かしこい)の？"));
        }

        [Test]
        public void NormalizeInput_KeepsQuestionMarkAsFullWidthQuestionMark()
        {
            Assert.AreEqual(
                "はどうしてかしこいの？",
                JapaneseTextNormalizer.NormalizeInput("はどうしてかしこいの?"));
        }

        [TestCase("今日は雨", "きょうはあめ")]
        [TestCase("今日から君はクロだよ", "きょうからきみはくろだよ")]
        [TestCase("私をご主人って呼んで", "わたしをご主人ってよんで")]
        [TestCase("ボクって名乗って", "ぼくってなのって")]
        [TestCase("クロに改名", "くろにかいめい")]
        [TestCase("ツシマヤマネコ", "つしまやまねこ")]
        [TestCase("対馬山猫", "つしまやまねこ")]
        [TestCase("猫又", "ねこまた")]
        [TestCase("猫股", "ねこまた")]
        [TestCase("お腹が空いた", "おなかがすいた")]
        [TestCase("餌が欲しい", "えさがほしい")]
        [TestCase("御飯を食べたい", "ごはんをたべたい")]
        [TestCase("お手", "おて")]
        [TestCase("御手", "おて")]
        [TestCase("三毛猫が好き", "みけねこがすき")]
        [TestCase("ﾂｼﾏﾔﾏﾈｺ", "つしまやまねこ")]
        [TestCase("ベンガルトラ", "べんがるとら")]
        [TestCase("サーバル", "さーばる")]
        [TestCase("チーター", "ちーたー")]
        [TestCase("ロシアンブルー", "ろしあんぶるー")]
        public void NormalizeInput_UsesProjectReadingDictionaryAndKatakana(string input, string expected)
        {
            Assert.AreEqual(expected, JapaneseTextNormalizer.NormalizeInput(input));
        }

        [Test]
        public void NormalizeInput_KeepsLongVowelMark()
        {
            Assert.AreEqual("ぱうばーと", JapaneseTextNormalizer.NormalizeInput("パウバート"));
            Assert.AreEqual("ぱうばーと", JapaneseTextNormalizer.NormalizeToken("パウバート"));
        }

        [TestCase("にゃ～", "にゃー")]
        [TestCase("にゃ〜", "にゃー")]
        [TestCase("にゃ~", "にゃー")]
        [TestCase("にゃ〰", "にゃー")]
        public void NormalizeInput_ConvertsWaveDashLikeCharactersToLongVowelMark(string input, string expected)
        {
            Assert.AreEqual(expected, JapaneseTextNormalizer.NormalizeInput(input));
            Assert.AreEqual(expected, JapaneseTextNormalizer.NormalizeToken(input));
            Assert.AreEqual(expected, JapaneseTextNormalizer.NormalizeNameCallToken(input));
        }

        [TestCase("おなかすいた")]
        [TestCase("ねむい")]
        [TestCase("つしまやまねこ")]
        [TestCase("ねこまた")]
        public void NormalizeInput_KeepsExistingHiraganaInputs(string input)
        {
            Assert.AreEqual(input, JapaneseTextNormalizer.NormalizeInput(input));
        }

        [TestCase("オナカスイタ", "おなかすいた")]
        [TestCase("ツシマヤマネコ", "つしまやまねこ")]
        [TestCase("ネコマタ", "ねこまた")]
        public void NormalizeInput_NormalizesKatakanaToExistingReadings(string input, string expected)
        {
            Assert.AreEqual(expected, JapaneseTextNormalizer.NormalizeInput(input));
        }

        [Test]
        public void NormalizeInput_PreservesOriginalRangeAfterDictionaryExpansion()
        {
            PlayerInputContext context = new PlayerInputContext("猫又");

            Assert.AreEqual("ねこまた", context.NormalizedInput);
            Assert.AreEqual("猫又", context.GetOriginalSubstringForNormalizedRange(0, context.NormalizedInput.Length));
        }

        [Test]
        public void DictionaryConverter_UsesLongestMatch()
        {
            KanjiReadingDictionary dictionary = KanjiReadingDictionary.FromCsv(
                "Surface,Reading,Enabled,NeedsReview,Category,SourceFiles,Note\n" +
                "山猫,やまねこ,true,false,FELIDAE,test,\n" +
                "対馬山猫,つしまやまねこ,true,false,FELIDAE,test,\n",
                "test.csv");

            string converted = new ProjectDictionaryReadingConverter(dictionary.Entries).Convert("対馬山猫");

            Assert.AreEqual("つしまやまねこ", converted);
        }

        [Test]
        public void DictionaryConverter_IgnoresDisabledRowsAndConflicts()
        {
            KanjiReadingDictionary dictionary = KanjiReadingDictionary.FromCsv(
                "Surface,Reading,Enabled,NeedsReview,Category,SourceFiles,Note\n" +
                "明日,あした,false,true,COMMON,test,\n" +
                "生物,せいぶつ,true,false,COMMON,test,\n" +
                "生物,なまもの,true,false,COMMON,test,\n",
                "test.csv");

            string disabledConverted = new ProjectDictionaryReadingConverter(dictionary.Entries).Convert("明日");
            string conflictedConverted = new ProjectDictionaryReadingConverter(dictionary.Entries).Convert("生物");

            Assert.AreEqual("明日", disabledConverted);
            Assert.AreEqual("生物", conflictedConverted);
        }
    }
}
