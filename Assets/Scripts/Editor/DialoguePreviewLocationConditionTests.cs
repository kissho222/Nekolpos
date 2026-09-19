using System.Reflection;
using NUnit.Framework;

namespace Nekolpos.EditorTools
{
    public sealed class DialoguePreviewLocationConditionTests
    {
        [Test]
        public void ConditionEditor_ParsesAndBuildsEachNegatedDropdownCondition()
        {
            object parsed = ParseCondition("!Rain && !Spring && !Morning && H: !Table");
            Assert.That(GetParsedValue<string[]>(parsed, "SelectedOptions"), Is.EqualTo(new[] { "Rain", "Spring", "Morning", "H: Table" }));
            Assert.That(GetParsedValue<bool[]>(parsed, "IsNegatedOptions"), Is.EqualTo(new[] { true, true, true, true }));

            string result = BuildCondition(parsed);

            Assert.That(result, Is.EqualTo("!Rain && !Spring && !Morning && H: !Table"));
        }

        [Test]
        public void LocationConditionEditor_PreservesMultiLocationConditionWithoutChangingIt()
        {
            object parsed = ParseCondition("H: Desk Table Bathroom");
            Assert.That(GetParsedValue<string[]>(parsed, "SelectedOptions"), Is.EqualTo(new string[] { null, null, null, null }));

            string result = BuildCondition(parsed);

            Assert.That(result, Is.EqualTo("H: Desk Table Bathroom"));
        }

        [Test]
        public void DialogueStateSimulator_EvaluatesNegatedLocationConditionWithoutParseError()
        {
            var entry = new DialogueEntry { Condition = "H:!Desk" };

            DialogueStateSimulator.EvaluationResult result = DialogueStateSimulator.Evaluate(entry, new DialogueState());

            Assert.That(result.Message, Does.Not.Contain("解釈できない条件"));
        }

        private static object ParseCondition(string condition)
        {
            MethodInfo method = typeof(DialoguePreviewWindow).GetMethod(
                "ParseConditionDropdownState",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { condition });
        }

        private static string BuildCondition(object parsed)
        {
            MethodInfo method = typeof(DialoguePreviewWindow).GetMethod(
                "BuildConditionFromSelections",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(
                null,
                new object[]
                {
                    GetParsedValue<string[]>(parsed, "SelectedOptions"),
                    GetParsedValue<bool[]>(parsed, "IsNegatedOptions"),
                    parsed
                });
        }

        private static T GetParsedValue<T>(object parsed, string fieldName)
        {
            FieldInfo field = parsed.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(parsed);
        }
    }
}
