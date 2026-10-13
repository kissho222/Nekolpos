using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Nekolpos.EditorTools.Authoring
{
    public sealed class DialogueAuthoringDiagnosticsTests
    {
        [TestCase("same", " same ", 1)]
        [TestCase("SAME", "same", 1)]
        [TestCase("a", "b", 2)]
        [TestCase("", "b", 1)]
        public void TimedEventGroupsKeepDistinctSources(string action, string timed, int count)
        {
            var groups = DialoguePreviewWindow.TimedEventLinks(action, timed);
            Assert.That(groups.Count, Is.EqualTo(count));
            Assert.That(groups.Any(g => g.sources.Contains("timed_event_key") && g.dedicated), Is.True);
            if (count == 1 && !string.IsNullOrEmpty(action)) Assert.That(groups[0].sources, Does.Contain("ActionID"));
        }
        [Test] public void SharedTimedEventEditPersistsSingleCanonicalRow()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
            try
            {
                File.WriteAllText(path, "key,time_minutes\nsame,120\n");
                var repo = new DialogueRelatedCsvRepository(path);
                var group = DialoguePreviewWindow.TimedEventLinks("same", "same").Single();
                repo.Find("key", group.key).Single()["time_minutes"] = "90"; repo.Save();
                var reloaded = new DialogueRelatedCsvRepository(path);
                Assert.That(reloaded.Document.Rows.Count, Is.EqualTo(1));
                Assert.That(reloaded.Find("key", "same").Single()["time_minutes"], Is.EqualTo("90"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        [Test] public void LatestTimedEventAndRepeatedValidationClearStaleError()
        {
            var project = DialogueLegacyImporter.ImportFile();
            project.diagnostics.Add(new AuthoringDiagnostic { code = "MissingTimedEvent", severity = DiagnosticSeverity.Error, message = "stale" });
            DialogueAuthoringValidator.ResolveSharedReferences(project);
            Assert.That(project.diagnostics.Any(d => d.code == "MissingTimedEvent"), Is.False);
            int count = project.diagnostics.Count;
            DialogueAuthoringValidator.ResolveSharedReferences(project);
            Assert.That(project.diagnostics.Count, Is.EqualTo(count));
            Assert.That(project.actionResolutions.Single(r => r.actionId == "action_paw_press_death_result").kind, Is.EqualTo(ActionResolutionKind.Registry));
        }
        [Test] public void ZeroReferencesPreserveDataAndShowAuthorContext()
        {
            var project = DialogueLegacyImporter.ImportFile();
            var diagnostics = project.diagnostics.Where(d => d.code == "AmbiguousZeroTarget").OrderBy(d => d.sourceRow).ToArray();
            Assert.That(diagnostics.Select(d => d.sourceRow), Is.EqualTo(new[] { 276, 281 }));
            foreach (var d in diagnostics)
            {
                Assert.That(d.csvId, Is.Not.Empty); Assert.That(d.pattern, Is.Not.Empty);
                Assert.That(d.order, Is.Not.Empty); Assert.That(d.responseType, Is.EqualTo("Normal"));
                Assert.That(d.column, Is.EqualTo(d.sourceRow == 276 ? "choice_yes_pattern" : "yes_next_pattern")); Assert.That(d.currentValue, Is.EqualTo("0"));
                Assert.That(d.textPreview, Is.Not.Empty); Assert.That(d.dialogueId, Is.Not.Empty); Assert.That(d.segmentId, Is.Not.Empty);
                var line = project.dialogues.SelectMany(r => r.segments).SelectMany(s => s.lines).Single(l => l.legacy.sourceRow == d.sourceRow);
                Assert.That(d.regexId, Is.EqualTo(line.legacy.Read("regex_id")));
            }
            Assert.That(DialogueAuthoringComparison.RoundTripLoss(project), Is.Empty);
        }
        [TestCase("registry", ActionResolutionKind.Registry)]
        [TestCase("death:Bite", ActionResolutionKind.Dedicated)]
        [TestCase("RepeatLastPattern", ActionResolutionKind.Dedicated)]
        [TestCase("Punch", ActionResolutionKind.Dedicated)]
        [TestCase("TeachingMode:Confirm:Felidae", ActionResolutionKind.Dedicated)]
        [TestCase("yarn:MorningGreeting", ActionResolutionKind.RuntimeResolver)]
        [TestCase("yarn:missing_node", ActionResolutionKind.Unresolved)]
        [TestCase("unregistered_action", ActionResolutionKind.Unresolved)]
        [TestCase("move_home:missing", ActionResolutionKind.Unresolved)]
        public void ActionClassificationUsesExistingResolvers(string action, ActionResolutionKind expected)
        {
            Assert.That(DialogueAuthoringActionResolver.Classify(action, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "registry" }).kind, Is.EqualTo(expected));
        }
        [Test] public void ActionDiagnosticsAreUniqueAndOnlyUnresolvedAreErrors()
        {
            var project = DialogueLegacyImporter.ImportFile();
            Assert.That(project.actionResolutions.Select(r => r.actionId).Distinct().Count(), Is.EqualTo(project.actionResolutions.Count));
            Assert.That(project.actionResolutions.Single(r => r.actionId == "Punch").rowCount, Is.EqualTo(6));
            Assert.That(project.diagnostics.Count(d => d.code == "UnresolvedAction"), Is.EqualTo(project.actionResolutions.Count(r => r.kind == ActionResolutionKind.Unresolved)));
            Assert.That(project.diagnostics.Where(d => d.code == "UnresolvedAction").All(d => d.severity == DiagnosticSeverity.Error), Is.True);
            Assert.That(project.diagnostics.Any(d => d.code == "ActionRequiresRuntimeResolver"), Is.False);
        }
        [TestCase(276)] [TestCase(281)]
        public void DiagnosticJumpSelectsExactSourceLine(int row)
        {
            Assert.That(DialoguePreviewWindow.OpenDiagnostic(DialogueLegacyImporter.DefaultSource, row), Is.True);
            var window = EditorWindow.GetWindow<DialoguePreviewWindow>();
            var flags = global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic;
            var group = typeof(DialoguePreviewWindow).GetMethod("GetCurrentGroup", flags).Invoke(window, null);
            var entry = (DialogueEntry)typeof(DialoguePreviewWindow).GetMethod("GetCurrentEntry", flags).Invoke(window, new[] { group });
            Assert.That(entry.SourceLineNumber, Is.EqualTo(row));
        }
    }
}
