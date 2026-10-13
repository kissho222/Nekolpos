using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Nekolpos.EditorTools
{
    internal sealed class DialogueAuthoringProfile
    {
        public readonly string Name, FileName;
        public readonly bool Condition, Psychology, TimedEvent, TalkTopic, Readings, Diary, Teaching;
        public DialogueAuthoringProfile(string name, string file, bool teaching = false)
        {
            Name = name; FileName = file; Teaching = teaching;
            Condition = Psychology = Readings = true;
            TimedEvent = TalkTopic = Diary = !teaching;
        }
        public string Id => FileName == "DialoguePreview.csv" ? "normal-dialogue" : global::System.IO.Path.GetFileNameWithoutExtension(FileName);
        public string Path => "TalkSource/TalkCSV/" + FileName;
        public string NewItemRegexPrefix => FileName switch
        {
            "teacchseries_dialogue_source_2201.csv" => "series_",
            "teacchTalk_body.csv" => "body_",
            "PsychologyReactionDialogue.csv" => "PRD_",
            _ => string.Empty
        };
        public string[] Sections => Teaching ? new[] { "漢字読み", "教示設定" } : new[] { "時間経過・日記", "話題候補", "漢字読み" };
        public static readonly DialogueAuthoringProfile[] Known = {
            new("通常会話", "DialoguePreview.csv"),
            new("ネコ科", "teacchFelidae.csv", true),
            new("猫作品・シリーズ", "teacchseries_dialogue_source_2201.csv", true),
            new("身体", "teacchTalk_body.csv", true),
            new("猫の表現", "teacchCatExpression.csv", true),
            new("猫種", "teacchCatBreed.csv", true),
            new("好きな天気", "teacchFavoriteWeather.csv", true),
            new("心理反応", "PsychologyReactionDialogue.csv"),
            new("内部会話", "InternalDialogue.csv")
        };
        public static DialogueAuthoringProfile Resolve(string path) => Known.FirstOrDefault(p => p.FileName.Equals(global::System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
        public static List<DialogueAuthoringProfile> Available()
        {
            var result = Known.Where(p => File.Exists(p.Path)).ToList();
            foreach (string path in Directory.GetFiles("TalkSource/TalkCSV", "*.csv"))
            {
                if (Resolve(path) != null || global::System.IO.Path.GetFileName(path) == "DialoguePreview_Integrated.csv") continue;
                var doc = TalkTopicMasterCsvUtility.ReadDocument(path);
                if (doc.Headers.Contains("regex_jp") && doc.Headers.Contains("output_ja"))
                    result.Add(new DialogueAuthoringProfile("その他の会話（" + global::System.IO.Path.GetFileNameWithoutExtension(path) + "）", global::System.IO.Path.GetFileName(path)));
            }
            return result;
        }
    }
}
