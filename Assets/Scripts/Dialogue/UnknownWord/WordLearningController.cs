using System;
using System.Linq;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.Dialogue.UnknownWord
{
    /// <summary>Authored learning form; all changes are staged until explicit confirmation.</summary>
    public sealed class WordLearningController : MonoBehaviour
    {
        public GameObject form;
        public GameObject editSection, genreSection, greetingSection;
        public TMP_InputField wordInput, readingInput;
        public TMP_Dropdown genreInput;
        public Toggle[] timeToggles, sceneToggles;
        public TMP_Text prompt, errorText;
        public Button confirmButton, cancelButton;
        private ChatUIController ui;
        private LearnedWord draft;
        private string originalInput;
        private string stage;
        private Action completed;
        public bool IsActive { get; private set; }
        private bool initialized;
        private Action pendingSpeechCompleted;
        private CanvasGroup formInteraction;
        private float genreClickReadyAt;
        public bool CompletedTeaching { get; private set; }

        private void Awake() => Initialize();
        public void Initialize()
        {
            if (initialized) return;
            var genreLabels = WordKnowledge.GenreLabels.ToList();
            string confirmLabel = WordLearningDialogue.Format("button_confirm");
            string cancelLabel = WordLearningDialogue.Format("button_cancel");
            confirmButton.onClick.AddListener(ConfirmForm);
            cancelButton.onClick.AddListener(() => { if (Time.unscaledTime >= genreClickReadyAt) Cancel(); });
            formInteraction = form.GetComponent<CanvasGroup>();
            confirmButton.GetComponentInChildren<TMP_Text>(true).text = confirmLabel;
            cancelButton.GetComponentInChildren<TMP_Text>(true).text = cancelLabel;
            wordInput.onEndEdit.AddListener(_ => RunSafely(RefreshReading));
            // This input is separate from the chat input and its IME/filter handlers.
            wordInput.characterValidation = TMP_InputField.CharacterValidation.None;
            wordInput.characterLimit = readingInput.characterLimit = 64;
            genreInput.ClearOptions(); genreInput.AddOptions(genreLabels);
            form.SetActive(false);
            initialized = true;
        }
        private void OnDisable() { if (IsActive) Cancel(); }
        public void Begin(string original, string extracted, Action onCompleted, ChatUIController resolvedUi = null)
        {
            // Learning may be started explicitly while this component is disabled.
            // Its Update must run to release the temporary genre click lock.
            enabled = true;
            if (IsActive) return;
            ui = resolvedUi != null ? resolvedUi : FindFirstObjectByType<ChatUIController>();
            if (ui == null) { Debug.LogError("[WordLearning] ChatUI is missing."); onCompleted?.Invoke(); return; }
            CompletedTeaching = false; completed = onCompleted; originalInput = original ?? extracted ?? ""; IsActive = true;
            try
            {
                Initialize();
                ui.EnterDialogueModeInstant();
                draft = LearnedWordStore.Shared.Match(originalInput, out bool exact);
                if (draft == null) draft = LearnedWordStore.Shared.Match(extracted, out exact);
                if (draft == null)
                {
                    draft = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = extracted ?? originalInput, genre = WordGenre.Other };
                    Speak(WordLearningDialogue.Format("unknown_prompt", draft.display), () => ui.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_yes"), WordLearningDialogue.Format("button_no") }, choice =>
                    {
                        if (choice == 1) { Cancel(); return; }
                        stage = "meaning";
                        Speak(WordLearningDialogue.Format("meaning_prompt", draft.display),
                            () => ui.OnWaitInputCompleted += OpenEditingAfterMeaningPrompt);
                    }));
                }
                else
                {
                    Known(exact);
                }
            }
            catch (Exception ex) { AbortLearning(ex); }
        }
        private void Speak(string text, Action after = null)
        {
            ClearPendingSpeech(); ui.HideChoices();
            pendingSpeechCompleted = () => { ClearPendingSpeech(); if (IsActive) RunSafely(() => after?.Invoke()); };
            ui.OnTypingCompleted += pendingSpeechCompleted;
            ui.ShowMessage("{{CAT_NAME}}", text);
        }
        private void ClearPendingSpeech()
        {
            if (ui != null && pendingSpeechCompleted != null) ui.OnTypingCompleted -= pendingSpeechCompleted;
            pendingSpeechCompleted = null;
        }
        private void OpenEditingAfterMeaningPrompt()
        {
            ui.OnWaitInputCompleted -= OpenEditingAfterMeaningPrompt;
            if (IsActive && stage == "meaning") Edit();
        }
        private void Known(bool exact)
        {
            if (draft.genre == WordGenre.Greeting && exact)
            {
                bool matches = WordKnowledge.MatchesGreeting(draft, WordVariables.CurrentPeriod(), GreetingScenes.None);
                Speak(WordLearningDialogue.Format(WordKnowledge.Next(draft) == null ? "known_complete" : matches ? "greeting_match" : "greeting_mismatch", draft.display), () => ui.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_reteach"), WordLearningDialogue.Format("button_keep") }, i => { if (i == 0) GreetingForm(); else Cancel(); }));
                return;
            }
            Speak(WordLearningDialogue.Format(WordKnowledge.Next(draft) == null ? "known_complete" : exact ? "known_exact" : "known_partial", draft.display, WordKnowledge.Summary(draft)), () => ui.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_reteach"), WordLearningDialogue.Format("button_other_word"), WordLearningDialogue.Format("button_nothing") }, i =>
            {
                if (i == 0) Edit(true);
                else if (i == 1) { draft = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = originalInput, genre = WordGenre.Other }; Edit(); }
                else Cancel();
            }));
        }
        private void Ask(WordQuestion question)
        {
            form.SetActive(false);
            Speak(WordLearningDialogue.Format("question", draft.display, question: question.Prompt), () => ui.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_yes"), WordLearningDialogue.Format("button_no"), WordLearningDialogue.Format("button_unknown"), WordLearningDialogue.Format("button_cancel") }, i =>
            {
                if (i == 3) { Cancel(); return; }
                draft.answers[question.Id] = i == 0 ? WordAnswer.Yes : i == 1 ? WordAnswer.No : WordAnswer.Unknown;
                SaveAndFinish();
            }));
        }
        private void ShowForm(string nextStage, string title)
        {
            stage = nextStage; ui.HideChoices(); form.SetActive(true);
            editSection.SetActive(stage == "edit"); genreSection.SetActive(stage == "genre"); greetingSection.SetActive(stage == "greeting");
            prompt.text = title; errorText.text = "";
            genreClickReadyAt = stage == "genre" ? Time.unscaledTime + .5f : 0f;
            if (formInteraction != null) formInteraction.interactable = Time.unscaledTime >= genreClickReadyAt;
        }
        private void Update()
        {
            if (IsActive && formInteraction != null && !formInteraction.interactable && Time.unscaledTime >= genreClickReadyAt)
                formInteraction.interactable = true;
        }
        private void Edit(bool clearInput = false)
        {
            ShowForm("edit", "教える言葉");
            wordInput.text = clearInput ? "" : draft.display;
            readingInput.text = clearInput ? "" : string.IsNullOrWhiteSpace(draft.reading) ? originalInput : draft.reading;
            RefreshReading();
        }
        private void RefreshReading()
        {
            bool requires = LearnedWordStore.RequiresReading(wordInput.text);
            readingInput.transform.parent.gameObject.SetActive(requires);
            if (requires && string.IsNullOrEmpty(readingInput.text))
                readingInput.text = string.IsNullOrWhiteSpace(draft?.reading) ? originalInput : draft.reading;
        }

        private void GreetingForm()
        {
            ShowForm("greeting", "いつ使うあいさつ？");
            for (int i = 0; i < timeToggles.Length; i++) timeToggles[i].isOn = ((int)draft.times & (1 << i)) != 0;
            for (int i = 0; i < sceneToggles.Length; i++) sceneToggles[i].isOn = ((int)draft.scenes & (1 << i)) != 0;
        }
        public void ConfirmForm()
        {
            if (Time.unscaledTime < genreClickReadyAt) return;
            try
            {
                if (stage == "edit")
                {
                    LearnedWordStore.ValidateText(wordInput.text);
                    RefreshReading();
                    if (LearnedWordStore.RequiresReading(wordInput.text)) LearnedWordStore.ValidateText(readingInput.text);
                    draft.display = wordInput.text.Trim();
                    draft.reading = LearnedWordStore.RequiresReading(draft.display) ? readingInput.text.Trim() : draft.reading;
                    ShowForm("genre", "言葉のジャンルは？"); genreInput.value = (int)draft.genre;
                }
                else if (stage == "genre")
                {
                    draft.genre = (WordGenre)genreInput.value;
                    if (draft.genre == WordGenre.Greeting) GreetingForm();
                    else Ask(WordKnowledge.RandomQuestion(draft));
                }
                else if (stage == "greeting")
                {
                    draft.times = GreetingTimes.None; draft.scenes = GreetingScenes.None;
                    for (int i = 0; i < timeToggles.Length; i++) if (timeToggles[i].isOn) draft.times |= (GreetingTimes)(1 << i);
                    for (int i = 0; i < sceneToggles.Length; i++) if (sceneToggles[i].isOn) draft.scenes |= (GreetingScenes)(1 << i);
                    SaveAndFinish();
                }
            }
            catch (Exception ex) { AbortLearning(ex); }
        }
        private void SaveAndFinish()
        {
            try
            {
                LearnedWordStore.Shared.Put(draft);
                CompletedTeaching = true;
                form.SetActive(false);
                Speak(WordLearningDialogue.Format(WordKnowledge.Next(draft) == null ? "saved_complete" : "saved", draft.display, WordKnowledge.Summary(draft)), () => ui.ShowChoiceOptions(new[] { WordLearningDialogue.Format("button_teach_more"), WordLearningDialogue.Format("button_return_chat") }, i => { if (i == 0) BeginNextWord(); else Cancel(); }));
            }
            catch (Exception ex) { AbortLearning(ex); }
        }
        private void BeginNextWord()
        {
            originalInput = "";
            draft = new LearnedWord { id = Guid.NewGuid().ToString("N"), display = "", genre = WordGenre.Other };
            Speak(WordLearningDialogue.Format("teacch_more"), () => Edit(true));
        }
        private void RunSafely(Action action)
        {
            try { action(); }
            catch (Exception ex) { AbortLearning(ex); }
        }
        private void AbortLearning(Exception error)
        {
            CompletedTeaching = false;
            Debug.LogError("[WordLearning] 会話を終了しました。" + error);
            if (errorText != null) errorText.text = "";
            ui?.ClearDialogueDisplay();
            Cancel();
        }
        public void Cancel()
        {
            if (!IsActive) return;
            ClearPendingSpeech();
            if (ui != null) ui.OnWaitInputCompleted -= OpenEditingAfterMeaningPrompt;
            IsActive = false; form?.SetActive(false); ui?.HideChoices();
            draft = null; var callback = completed; completed = null; callback?.Invoke();
        }
    }
}
