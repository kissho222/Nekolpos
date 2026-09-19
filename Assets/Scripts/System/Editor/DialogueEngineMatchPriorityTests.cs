using System.Reflection;
using Nekolpos.Data;
using NUnit.Framework;

namespace Nekolpos.System.Editor
{
    public sealed class DialogueEngineMatchPriorityTests
    {
        [Test]
        public void IsBetterMatchCandidate_PrefersLongerMatchOverHigherPriorityShortMatch()
        {
            RegexPatternData longerQuestionPattern = new RegexPatternData
            {
                SourceRegex = "(は|って)(なんで|どうして|なぜ)?かしこいの？",
                Priority = 0,
                Sensitivity = "SAFE"
            };
            RegexPatternData shorterSleepPattern = new RegexPatternData
            {
                SourceRegex = "ねる",
                Priority = 999,
                Sensitivity = "SAFE"
            };

            MethodInfo method = typeof(DialogueEngine).GetMethod(
                "IsBetterMatchCandidate",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            bool selectsLongerQuestion = (bool)method.Invoke(
                null,
                new object[]
                {
                    longerQuestionPattern,
                    11,
                    shorterSleepPattern,
                    2
                });

            Assert.That(selectsLongerQuestion, Is.True);
        }
    }
}
