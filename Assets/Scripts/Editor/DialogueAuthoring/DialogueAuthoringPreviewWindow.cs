using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools.Authoring
{
    public static class DialogueAuthoringJson
    {
        public static string Preview(DialogueAuthoringProject project) => JsonUtility.ToJson(project, true);
        public static string Schema()
        {
            string Quote(string s) => "\"" + s + "\"";
            string TypeSchema(Type type)
            {
                if (type == typeof(string)) return "{\"type\":\"string\"}";
                if (type == typeof(bool)) return "{\"type\":\"boolean\"}";
                if (type.IsEnum) return "{\"type\":\"integer\",\"enum\":[" + string.Join(",", Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32)) + "],\"description\":" + Quote(type.Name + ": " + string.Join(", ", Enum.GetNames(type))) + "}";
                if (type == typeof(int)) return "{\"type\":\"integer\"}";
                if (type == typeof(float)) return "{\"type\":\"number\"}";
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "{\"type\":\"array\",\"items\":" + TypeSchema(type.GetGenericArguments()[0]) + "}";
                return "{\"$ref\":\"#/$defs/" + type.Name + "\"}";
            }
            var types = typeof(DialogueAuthoringProject).Assembly.GetTypes().Where(t => t.Namespace == typeof(DialogueAuthoringProject).Namespace && t.IsClass && t.IsDefined(typeof(SerializableAttribute), false)).OrderBy(t => t.Name);
            var definitions = types.Select(t => Quote(t.Name) + ":{\"type\":\"object\",\"properties\":{" + string.Join(",", t.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => Quote(f.Name) + ":" + TypeSchema(f.FieldType))) + "}}");
            return "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"$ref\":\"#/$defs/DialogueAuthoringProject\",\"$defs\":{" + string.Join(",", definitions) + "}}";
        }
    }
    public sealed class DialogueAuthoringPreviewWindow : EditorWindow
    {
        private DialogueAuthoringProject project;
        private string json, summary, source;
        private string shadowSummary;
        private int selected;
        private Vector2 scroll;
        [MenuItem("Nekolpos/開発/会話Authoring Phase1 読取比較")]
        public static void OpenDefault() => Open(DialogueLegacyImporter.DefaultSource, 0);
        public static void Open(string path, int sourceOrdinal)
        {
            var window = GetWindow<DialogueAuthoringPreviewWindow>("Authoring読取比較");
            window.Load(path, sourceOrdinal); window.Show();
        }
        public void Load(string path, int sourceOrdinal)
        {
            source = path;
            shadowSummary = null;
            project = DialogueLegacyImporter.ImportFile(path);
            selected = project.dialogues.FindIndex(r => r.segments.Any(s => s.lines.Any(l => l.legacy.ordinal == sourceOrdinal)));
            selected = Mathf.Max(0, selected);
            var loss = DialogueAuthoringComparison.RoundTripLoss(project); var parity = DialogueAuthoringComparison.EditorParity(project, path);
            summary = $"{project.dialogues.Count} Root / {project.dialogues.Sum(r => r.segments.Count)} Segment / {project.dialogues.Sum(r => r.segments.Sum(s => s.lines.Count))} Line / {project.headers.Count}列\nRound-trip差分 {loss.Count} / Editor差分 {parity.Count}\n" + string.Join(" / ", project.diagnostics.GroupBy(d => d.severity).Select(g => g.Key + ":" + g.Count()));
            UpdateJson();
        }
        private void UpdateJson() => json = project.dialogues.Count > 0 ? JsonUtility.ToJson(project.dialogues[selected], true) : "{}";
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("読取専用・保存先とRuntimeは変更しません。表示は保存済みCSVからの解釈です。未保存の原稿変更は含みません。", MessageType.Info);
            if (project == null) { if (GUILayout.Button("通常CSVを読取")) Load(DialogueLegacyImporter.DefaultSource, 0); return; }
            EditorGUILayout.LabelField(summary, GUILayout.Height(58));
            if (!string.IsNullOrEmpty(shadowSummary)) EditorGUILayout.HelpBox(shadowSummary, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("再読込")) Load(source, project.dialogues.Count > 0 ? project.dialogues[selected].segments[0].lines[0].legacy.ordinal : 0);
                if (GUILayout.Button("選択Root JSONをコピー")) EditorGUIUtility.systemCopyBuffer = json;
                if (GUILayout.Button("全体JSONをコピー")) EditorGUIUtility.systemCopyBuffer = DialogueAuthoringJson.Preview(project);
                if (GUILayout.Button("JSON Schemaをコピー")) EditorGUIUtility.systemCopyBuffer = DialogueAuthoringJson.Schema();
            }
            if (GUILayout.Button("Phase2 Shadow生成・旧Engine比較"))
            {
                var report = DialogueShadowComparison.Export(project);
                shadowSummary = $"Shadow: 入力{report.inputComparisons} / 選択{report.selections} / 差分{report.differences.Count}\nデータ公開ゲート:{report.publishable}（Library/DialogueAuthoringShadow）\nRuntime切替は行いません。外部処理は実行予定データだけを比較します。";
            }
            if (project.dialogues.Count == 0) return;
            int next = EditorGUILayout.Popup("意味キー", selected, project.dialogues.Select(r => r.legacyLabel).ToArray());
            if (next != selected) { selected = next; UpdateJson(); }
            using (var area = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = area.scrollPosition;
                foreach (var diagnostic in project.diagnostics.Where(d => d.severity != DiagnosticSeverity.Info))
                    DrawDiagnostic(project.source, diagnostic);
                foreach (var action in project.actionResolutions)
                    EditorGUILayout.LabelField($"Action ID: {action.actionId} / {action.rowCount}行 / {action.dialogueCount}会話 / {action.kind}: {action.resolver}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.SelectableLabel(json, EditorStyles.textArea, GUILayout.Height(Mathf.Max(200, json.Count(c => c == '\n') * EditorGUIUtility.singleLineHeight)));
            }
        }
        internal static void DrawDiagnostic(string source, AuthoringDiagnostic diagnostic)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(diagnostic.message, diagnostic.severity == DiagnosticSeverity.Error ? MessageType.Error : MessageType.Warning);
                EditorGUILayout.LabelField(new GUIContent("診断情報", diagnostic.code));
                EditorGUILayout.LabelField($"CSV行{diagnostic.sourceRow} / id:{diagnostic.csvId} / regex_id:{diagnostic.regexId} / Pattern:{diagnostic.pattern} / order:{diagnostic.order} / response_type:{diagnostic.responseType}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"対象列:{diagnostic.column} / 現在値:{diagnostic.currentValue} / 本文:{diagnostic.textPreview}", EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(diagnostic.segmentId)) EditorGUILayout.LabelField("Editor DialogueID: " + diagnostic.dialogueId + " / Segment ID: " + diagnostic.segmentId, EditorStyles.wordWrappedLabel);
                if (diagnostic.sourceRow > 0 && GUILayout.Button("この会話を開く")) DialoguePreviewWindow.OpenDiagnostic(source, diagnostic.sourceRow);
            }
        }
    }
}
