using System.Collections.Generic;
using System.Reflection;
using Nekolpos.Data;
using NUnit.Framework;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueInputAssistanceTests
    {
        [Test]
        public void History_PreservesDuplicatesAndRestoresPendingInput()
        {
            var history = new DialogueInputHistory();
            history.AddHistory("なでたい");
            history.AddHistory("なでたい");
            history.AddHistory("おはよう");

            Assert.That(history.TryMovePrevious("ねるこ", out string value), Is.True);
            Assert.That(value, Is.EqualTo("おはよう"));
            history.TryMovePrevious(value, out value);
            Assert.That(value, Is.EqualTo("なでたい"));
            history.TryMovePrevious(value, out value);
            Assert.That(value, Is.EqualTo("なでたい"));
            history.TryMovePrevious(value, out value);
            Assert.That(value, Is.EqualTo("なでたい"));

            history.TryMoveNext(out value);
            history.TryMoveNext(out value);
            history.TryMoveNext(out value);
            Assert.That(value, Is.EqualTo("ねるこ"));
            Assert.That(history.IsNavigating, Is.False);
        }

        [Test]
        public void QuestionLinks_OnlyTagConfiguredKeywords()
        {
            var links = new List<DialogueLogQuestionLink>
            {
                new DialogueLogQuestionLink("悪魔", "悪魔ってどういうこと？"),
                new DialogueLogQuestionLink("小さくした", "小さくしたってどういうこと？")
            };

            string result = DialogueLogQuestionLinkView.BuildLinkedText(
                "悪魔にお願いして、あなたを小さくしたの。",
                links,
                "66CCFF");

            Assert.That(result, Does.Contain("<link=\"0\"><color=#66CCFF>悪魔</color></link>"));
            Assert.That(result, Does.Contain("<link=\"1\"><color=#66CCFF>小さくした</color></link>"));
            Assert.That(result, Does.Contain("にお願いして、あなたを"));
        }

        [Test]
        public void QuestionLinks_MissingKeywordLeavesTextUnchanged()
        {
            const string source = "普通の台詞";
            string result = DialogueLogQuestionLinkView.BuildLinkedText(
                source,
                new[] { new DialogueLogQuestionLink("猫又", "猫又ってどういうこと？") },
                "66CCFF");

            Assert.That(result, Is.EqualTo(source));
        }

        [Test]
        public void RandomReactionSelection_PrefersCurrentlyUnspokenCandidates()
        {
            var state = new ConversationState();
            var pool = new List<DialogueReactionData>
            {
                new DialogueReactionData { PatternID = "random_a", SpeechControl = SpeechControlType.Random },
                new DialogueReactionData { PatternID = "random_b", SpeechControl = SpeechControlType.Random },
                new DialogueReactionData { PatternID = "random_c", SpeechControl = SpeechControlType.Random }
            };

            var selectedIds = new HashSet<string>();
            for (int i = 0; i < pool.Count; i++)
            {
                DialogueReactionData selected = InvokeSelectRandomReaction(pool, state);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selectedIds.Add(selected.PatternID), Is.True);
            }
        }

        [Test]
        public void RandomReactionSelection_AvoidsImmediateRepeatAfterAllCandidatesSpoken()
        {
            var state = new ConversationState
            {
                lastRandomPatternId = "random_b"
            };
            state.spokenRandomPatternIds.Add("random_a");
            state.spokenRandomPatternIds.Add("random_b");
            state.spokenRandomPatternIds.Add("random_c");

            var pool = new List<DialogueReactionData>
            {
                new DialogueReactionData { PatternID = "random_a", SpeechControl = SpeechControlType.Random },
                new DialogueReactionData { PatternID = "random_b", SpeechControl = SpeechControlType.Random },
                new DialogueReactionData { PatternID = "random_c", SpeechControl = SpeechControlType.Random }
            };

            DialogueReactionData selected = InvokeSelectRandomReaction(pool, state);

            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.PatternID, Is.Not.EqualTo("random_b"));
        }

        [Test]
        public void RepeatEvent_DoesNotFireOnFirstRepeatCount()
        {
            var reactions = new List<DialogueReactionData>
            {
                new DialogueReactionData
                {
                    PatternID = "repeat",
                    SpeechControl = SpeechControlType.RepeatEvent,
                    RepeatCount = 1
                }
            };

            DialogueReactionData selected = InvokeFindRepeatEvent(reactions, 1);

            Assert.That(selected, Is.Null);
        }

        [Test]
        public void RepeatEvent_TreatsRepeatCountOneAsSecondInputTrigger()
        {
            var repeat = new DialogueReactionData
            {
                PatternID = "repeat",
                SpeechControl = SpeechControlType.RepeatEvent,
                RepeatCount = 1
            };

            DialogueReactionData selected = InvokeFindRepeatEvent(
                new List<DialogueReactionData> { repeat },
                2);

            Assert.That(selected, Is.SameAs(repeat));
        }

        private static DialogueReactionData InvokeSelectRandomReaction(
            List<DialogueReactionData> pool,
            ConversationState state)
        {
            MethodInfo method = typeof(DialogueEngine).GetMethod(
                "SelectRandomReaction",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { pool, state }) as DialogueReactionData;
        }

        private static DialogueReactionData InvokeFindRepeatEvent(
            List<DialogueReactionData> reactions,
            int currentRepeatCount)
        {
            MethodInfo method = typeof(DialogueEngine).GetMethod(
                "FindRepeatEvent",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { reactions, currentRepeatCount }) as DialogueReactionData;
        }
    }
}
