using NUnit.Framework;

namespace Nekolpos.System.Editor
{
    public sealed class CatNameCallMatcherTests
    {
        [TestCase("ねるこ")]
        [TestCase("ねるこー")]
        [TestCase("ねるこー！")]
        [TestCase("…ねるこ～")]
        [TestCase("ねるこっ")]
        [TestCase("ねるこは……")]
        [TestCase("おいで！ねるこ！")]
        [TestCase("こっちきて、ねるこ！")]
        public void IsNameCall_AcceptsNameWithCallingDecoration(string input)
        {
            Assert.That(CatNameCallMatcher.IsNameCall(input, "ねるこ"), Is.True);
        }

        [Test]
        public void IsNameCall_PrefersCatNameForKatakanaTopicHesitation()
        {
            string normalizedInput = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken("ネルコは……　　");
            string normalizedCatName = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken("ネルコ");

            Assert.That(CatNameCallMatcher.IsNameCall(normalizedInput, normalizedCatName), Is.True);
        }

        [TestCase("みー")]
        [TestCase("ミー！")]
        [TestCase("おいで！ミー！")]
        [TestCase("ミーー！")]
        [TestCase("ルーシー！")]
        [TestCase("おいでー！ルーシー！")]
        public void IsNameCall_AcceptsCatNameContainingLongSoundMark(string input)
        {
            string catName = input.Contains("ル") ? "るーしー" : "みー";
            string normalizedInput = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken(input);
            Assert.That(CatNameCallMatcher.IsNameCall(normalizedInput, catName), Is.True);
        }

        [Test]
        public void IsNameCall_AcceptsPronounWithCallingPhrase()
        {
            Assert.That(CatNameCallMatcher.IsNameCall("おいで！私！", "私"), Is.True);
        }

        [TestCase("夜雲")]
        [TestCase("夜雲ー！")]
        [TestCase("おいで！夜雲！")]
        public void IsNameCall_AcceptsKanjiCatNameByExactGlyphMatch(string input)
        {
            string normalizedInput = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken(input);
            string normalizedCatName = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken("夜雲");

            Assert.That(CatNameCallMatcher.IsNameCall(normalizedInput, normalizedCatName), Is.True);
        }

        [Test]
        public void IsNameCall_DoesNotConvertKanjiCatNameToReading()
        {
            string normalizedInput = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken("やくも");
            string normalizedCatName = Backgammon.Conversation.JapaneseTextNormalizer.NormalizeNameCallToken("夜雲");

            Assert.That(CatNameCallMatcher.IsNameCall(normalizedInput, normalizedCatName), Is.False);
        }

        [Test]
        public void ContainsName_AcceptsCatNameInsideOpeningCallSentence()
        {
            Assert.That(CatNameCallMatcher.ContainsName("おいで！ねるこ！", "ねるこ"), Is.True);
            Assert.That(CatNameCallMatcher.ContainsName("ねるこをなでたい", "ねるこ"), Is.True);
            Assert.That(CatNameCallMatcher.ContainsName("ねる", "ねるこ"), Is.False);
        }

        [TestCase("ねるこをなでたい")]
        [TestCase("ねること遊びたい")]
        [TestCase("ねるこはどのくらい寝るの？")]
        [TestCase("ねるこじゃない")]
        [TestCase("おいでねるこ")]
        [TestCase("ねる")]
        [TestCase("")]
        public void IsNameCall_RejectsOtherSentencesAndPartialNames(string input)
        {
            Assert.That(CatNameCallMatcher.IsNameCall(input, "ねるこ"), Is.False);
        }
    }
}
