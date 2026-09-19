using Backgammon.Conversation;
using NUnit.Framework;

namespace Backgammon.Conversation.Editor
{
    public sealed class TalkTopicHintCatalogTests
    {
        [Test]
        public void LoadEnabledHintsFromCsvText_FiltersInvalidRowsAndKeepsJapaneseText()
        {
            const string csv =
                "TalkID,TalkTitle,Source,SourceKey,Genre,Idea,Import,Keywords,Enabled\n" +
                "TALK_0001,有効,source.csv,key,雑談,\"{{CAT_NAME}}に,撫でてもらいたいな\",なでてほしい,,TRUE\n" +
                "TALK_0002,無効,source.csv,key,雑談,これは出ない,でない,,FALSE\n" +
                "TALK_0003,空Idea,source.csv,key,雑談,   ,でない,,TRUE\n" +
                "TALK_0004,空Import,source.csv,key,雑談,思いついた,   ,,TRUE\n";

            var hints = TalkTopicHintCatalog.LoadEnabledHintsFromCsvText(csv);

            Assert.That(hints, Has.Count.EqualTo(1));
            Assert.That(hints[0].TalkID, Is.EqualTo("TALK_0001"));
            Assert.That(hints[0].Idea, Is.EqualTo("{{CAT_NAME}}に,撫でてもらいたいな"));
            Assert.That(hints[0].Import, Is.EqualTo("なでてほしい"));
            Assert.That(hints[0].Enabled, Is.True);
        }
    }
}
