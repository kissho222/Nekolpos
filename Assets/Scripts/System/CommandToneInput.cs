using System;
using System.Text;
using System.Text.RegularExpressions;
using Nekolpos.StatusSystem;

namespace Nekolpos.System
{
    /// <summary>Conservative command rules, independent of dialogue matching and reading conversion.</summary>
    public static class CommandToneDetector
    {
        private const string End = @"(?:[よねぞ]+)?$";
        private const string Prefix = @"^(?:(?:そこ|ここ|あそこ|こっち|そっち|あっち)(?:に|へ|で)|(?:今すぐ|すぐ|早く|はやく|ちゃんと)|(?:命令|話|言うこと|いうこと)を)*";
        private static readonly Regex[] Rules =
        {
            new Regex(Prefix + @"(?:待て|まて|来い|こい|行け|いけ|黙れ|だまれ|やめろ|止めろ|しろ|せよ|やれ|いろ|居ろ|聞け|きけ|見ろ|みろ|座れ|すわれ|立て|たて|出ろ|でろ|寝ろ|ねろ)" + End),
            new Regex(Prefix + @"(?:触る|さわる|動く|うごく|行く|いく|来る|くる|話す|はなす|喋る|しゃべる|見る|みる|する|近づく|ちかづく)な" + End),
            new Regex(Prefix + @"(?:片付け|かたづけ|待ち|まち|来|き|行き|いき|聞き|きき|座り|すわり|寝|ね|見|み|やめ|し)なさい" + End),
            new Regex(Prefix + @"(?:見て|みて|聞いて|きいて|覚えて|おぼえて|待って|まって)(?:おけ|おいてくれ)" + End),
            new Regex(Prefix + @"(?:持ってきて|もってきて|持って来て|手伝って|てつだって|待って|まって|来て|きて|見て|みて|聞いて|きいて|やって|助けて|たすけて|片付けて|かたづけて)くれ" + End)
        };
        private static readonly Regex Explanatory = new Regex(@"(?:と言|とい|って言|ってい|という|っていう|命令形|命令口調|例文|言葉|ことば|台詞|せりふ|セリフ)");
        private static readonly Regex Clauses = new Regex(@"[\s、,。.!！?？;；]+|(?:そして|それから|それと)");

        public static bool IsCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string text = input.Normalize(NormalizationForm.FormKC);
            // Keep boundaries unlike the reading normalizer: punctuation distinguishes a command from a word inside prose.
            text = Regex.Replace(text, "「[^」]*」|『[^』]*』|\"[^\"]*\"|“[^”]*”|'[^']*'|（[^）]*）|\\([^)]*\\)", " ");
            if (text.IndexOfAny(new[] { '「', '『', '"', '“' }) >= 0 || Explanatory.IsMatch(text)) return false;
            foreach (string clause in Clauses.Split(text))
                foreach (Regex rule in Rules)
                    if (rule.IsMatch(clause)) return true;
            return false;
        }
    }

    /// <summary>One accepted UI submission; re-evaluation and playback cannot apply it twice.</summary>
    public sealed class CommandToneInput
    {
        private readonly bool isCommand;
        private readonly string sourceId = "FreeInput.CommandTone:" + Guid.NewGuid().ToString("N");
        private bool evaluated;

        public CommandToneInput(string input) => isCommand = CommandToneDetector.IsCommand(input);

        public bool TryApply(StatusManager status)
        {
            if (evaluated || status == null) return false;
            evaluated = true;
            return isCommand && status.ApplyDelta(StatusType.Obedience, 10, "CommandTone", sourceId).Delta != 0;
        }
    }
}
