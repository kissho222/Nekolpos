using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.Dialogue.UnknownWord
{
    public sealed class WordMemoController : MonoBehaviour
    {
        public Transform content;
        public Button rowPrefab, forgetButton;
        public TMP_Text detail;
        private readonly List<Button> rows = new List<Button>();
        private string selectedId;
        private bool busy;
        private NekolposGameBootstrap bootstrap;
        private ChatUIController chat;
        private void Awake()
        {
            forgetButton.onClick.AddListener(RequestForget);
            forgetButton.GetComponentInChildren<TMP_Text>(true).text = WordLearningDialogue.Format("button_forget");
            var close = transform.Find("CloseButton")?.GetComponentInChildren<TMP_Text>(true);
            if (close != null) close.text = WordLearningDialogue.Format("button_close");
        }
        private void OnEnable() { LearnedWordStore.Shared.Changed += Refresh; Refresh(); }
        private void OnDisable()
        {
            LearnedWordStore.Shared.Changed -= Refresh;
            if (busy) { StopAllCoroutines(); RestoreTablet(); }
        }
        public void Refresh()
        {
            foreach (var row in rows) Destroy(row.gameObject);
            rows.Clear();
            var words = LearnedWordStore.Shared.Words.OrderBy(w => w.display, StringComparer.Ordinal);
            foreach (var word in words)
            {
                var row = Instantiate(rowPrefab, content);
                row.gameObject.SetActive(true);
                var label = row.GetComponentInChildren<TMP_Text>(); label.richText = false;
                label.text = word.display + "（" + WordKnowledge.GenreLabels[(int)word.genre] + "）";
                string id = word.id;
                row.onClick.AddListener(() => Select(id)); rows.Add(row);
            }
            Select(selectedId);
        }
        public void Select(string id)
        {
            selectedId = id; var word = LearnedWordStore.Shared.Find(id);
            forgetButton.interactable = word != null && !busy;
            detail.richText = false;
            if (word == null) { detail.text = "一覧から言葉を選ぶ"; return; }
            var displayUi = FindFirstObjectByType<ChatUIController>();
            string answers = string.Join("\n", WordKnowledge.Questions(word.genre).Select(q => (displayUi != null ? displayUi.ResolveDisplayText(q.Prompt) : q.Prompt) + "：" +
                (!word.answers.TryGetValue(q.Id, out var a) ? "未回答" : a == WordAnswer.Unknown ? WordLearningDialogue.Format("button_unknown") : a == WordAnswer.Yes ? WordLearningDialogue.Format("button_yes") : WordLearningDialogue.Format("button_no"))));
            detail.text = word.display + "\nジャンル：" + WordKnowledge.GenreLabels[(int)word.genre] + "\n読み方：" + (word.reading ?? "—") + "\n" + answers;
            if (word.genre == WordGenre.Greeting)
                detail.text += "\n使用時間帯：" + Times(word.times) + "\n使用場面：" + Scenes(word.scenes);
        }
        private static string Times(GreetingTimes t) => t == GreetingTimes.None ? "制限なし" : string.Join("・", new[] { "朝", "昼", "夕", "夜" }.Where((_, i) => ((int)t & (1 << i)) != 0));
        private static string Scenes(GreetingScenes s) => s == GreetingScenes.None ? "制限なし" : string.Join("・", new[] { "会った時", "別れる時" }.Where((_, i) => ((int)s & (1 << i)) != 0));
        public void RequestForget()
        {
            var word = LearnedWordStore.Shared.Find(selectedId); if (word == null || busy) return;
            bootstrap = FindFirstObjectByType<NekolposGameBootstrap>(); chat = FindFirstObjectByType<ChatUIController>();
            if (bootstrap == null || chat == null || !bootstrap.BeginTabletWordConversation())
            { Debug.LogError("[WordMemo] 会話カメラへ移動できないため忘却を中止しました。"); return; }
            busy = true; forgetButton.interactable = false;
            StartCoroutine(ForgetRoutine(word));
        }
        private IEnumerator ForgetRoutine(LearnedWord word)
        {
            yield return new WaitUntil(() => bootstrap == null || !bootstrap.IsTabletCameraTransitioning);
            if (bootstrap == null) { RestoreTablet(); yield break; }
            chat.SetUiSuppressed(false); chat.EnterDialogueModeInstant();
            chat.ShowMessageInstant("{{CAT_NAME}}", WordLearningDialogue.Format("forget_prompt", word.display));
            chat.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_forget"), WordLearningDialogue.Format("button_cancel") }, i =>
            {
                if (i == 0)
                {
                    try
                    {
                        LearnedWordStore.Shared.Forget(word.id);
                        chat.ShowMessageInstant("{{CAT_NAME}}", WordLearningDialogue.Format("forget_complete"));
                    }
                    catch (Exception ex)
                    { Debug.LogError("[WordMemo] 忘却を中止しました。" + ex); RestoreTablet(); return; }
                    chat.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_return_memo") }, _ => RestoreTablet());
                }
                else RestoreTablet();
            });
        }
        private void RestoreTablet()
        {
            chat?.HideChoices(); chat?.ClearDialogueDisplay();
            bootstrap?.EndTabletWordConversation(); busy = false; Refresh();
        }
    }
}
