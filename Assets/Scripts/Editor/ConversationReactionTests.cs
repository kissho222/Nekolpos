using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nekolpos.Data;
using Nekolpos.System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nekolpos.EditorTools
{
    public sealed class ConversationReactionTests
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        [TestCase("Heart", ReactionIcon.Heart)][TestCase(" question ", ReactionIcon.Question)][TestCase("", ReactionIcon.None)]
        public void NamedIconsAndMissingColumnResolveSafely(string value, ReactionIcon expected) => Assert.That(ReactionIconUtility.Parse(value), Is.EqualTo(expected));
        [Test] public void UnsupportedRuntimeIconWarnsAndUsesNone()
        {
            LogAssert.Expect(LogType.Warning, "[ReactionIcon] 未対応の値をNoneとして扱います: FutureIcon");
            Assert.That(ReactionIconUtility.Parse("FutureIcon"), Is.EqualTo(ReactionIcon.None));
        }
        [TestCase("DialoguePreview.csv")][TestCase("teacchFelidae.csv")][TestCase("teacchseries_dialogue_source_2201.csv")]
        [TestCase("teacchTalk_body.csv")][TestCase("teacchCatExpression.csv")][TestCase("teacchCatBreed.csv")]
        [TestCase("teacchFavoriteWeather.csv")][TestCase("PsychologyReactionDialogue.csv")][TestCase("InternalDialogue.csv")]
        public void EveryAuthoringProfileRoundTripsIconsAndPreservesUnknownValues(string file)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Reaction62_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var data = DialogueDataLoader.LoadFromPath("TalkSource/TalkCSV/" + file); var entry = data.Entries[0];
                string originalText = entry.Text; entry.ReactionIcon = "Heart";
                var copy = (DialogueEntry)typeof(DialogueDataLoader).GetMethod("CloneEntry", Static).Invoke(null, new object[] { entry });
                Assert.That(copy.ReactionIcon, Is.EqualTo("Heart"));
                data.SavePath = Path.Combine(directory, file); DialogueDataLoader.SaveToPath(data, data.SavePath);
                var restored = DialogueDataLoader.LoadFromPath(data.SavePath);
                Assert.That(restored.Headers.Any(header => header == "ReactionIcon"), Is.True);
                Assert.That(restored.Entries[0].ReactionIcon, Is.EqualTo("Heart")); Assert.That(restored.Entries[0].Text, Is.EqualTo(originalText));
                restored.Entries[0].ReactionIcon = "FutureIcon"; DialogueDataLoader.SaveToPath(restored, data.SavePath);
                Assert.That(DialogueDataLoader.LoadFromPath(data.SavePath).Entries[0].ReactionIcon, Is.EqualTo("FutureIcon"));
            }
            finally { Directory.Delete(directory, true); }
        }
        [Test] public void RuntimeClonesPreserveSelectedIcons()
        {
            var source = new DialogueReactionData { ReactionIcon = ReactionIcon.Shock };
            foreach (var type in new[] { typeof(DialogueEngine), typeof(InternalDialogueCatalog) })
            {
                var clone = (DialogueReactionData)type.GetMethod("CloneReaction", Static).Invoke(null, new object[] { source, null });
                Assert.That(clone.ReactionIcon, Is.EqualTo(ReactionIcon.Shock)); Assert.That(clone, Is.Not.SameAs(source));
            }
            var managerCopy = (List<DialogueReactionData>)typeof(DialogueManager).GetMethod("CloneReactionGroup", Static).Invoke(null, new object[] { new List<DialogueReactionData> { source } });
            Assert.That(managerCopy[0].ReactionIcon, Is.EqualTo(ReactionIcon.Shock));
        }
    }
}
