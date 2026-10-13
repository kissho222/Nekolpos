using System;
using System.Collections;
using System.Collections.Generic;
using Nekolpos.StatusSystem;
using Nekolpos.System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>散歩中の一方通行会話、選択肢、Area反応を所有する専用UI Controller。</summary>
[DisallowMultipleComponent]
public sealed class WalkDialogueController : MonoBehaviour
{
    private const string NerukoTalkTrigger = "NerukoTalk";
    private const string WalkEndTrigger = "WalkEnd";
    private static readonly string[] WalkResultActionIds =
    {
        "action_walk_result",
        "action_walk_result2",
        "action_walk_result3"
    };

    [Header("Required References")]
    [SerializeField] private PlayerWalkModeController walkModeController;
    [SerializeField] private PlayerAreaTracker areaTracker;
    [SerializeField] private PlayerAreaTransitionController areaTransitionController;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform nerukoBubbleAnchor;
    [SerializeField] private Transform nerukoRootFallback;
    [SerializeField] private StatusManager statusManager;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private Transform tableConversationAnchor;
    [SerializeField] private Transform deskConversationAnchor;
    [SerializeField] private CatLocomotionController catLocomotionController;

    [Header("Presentation")]
    [SerializeField, Min(0.1f)] private float autoHideSeconds = 3.5f;
    [SerializeField] private Vector2 viewportPadding = new Vector2(0.08f, 0.12f);
    [SerializeField] private ChatUIController typingSpeedSource;
    [SerializeField, Min(0.001f)] private float typingSecondsPerCharacter = 0.05f;

    [Header("Authored View")]
    [SerializeField] private GameObject canvasObject;
    [SerializeField] private RectTransform nerukoBubble;
    [SerializeField] private TextMeshProUGUI nerukoText;
    [SerializeField] private RectTransform playerBubble;
    [SerializeField] private TextMeshProUGUI playerText;
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private TextMeshProUGUI followChoiceText;
    [SerializeField] private Button[] choiceButtons;

    private WalkDialogueCatalog catalog;
    private Coroutine runningSequence;
    private bool isDialogueBusy;
    private bool isChoiceOpen;
    private bool advanceRequested;
    private Coroutine typingRoutine;
    private TextMeshProUGUI activeText;
    private bool isTyping;
    private bool viewBound;
    private UnityEngine.Events.UnityAction[] choiceHandlers;
    private int inputReadyFrame;
    private int lastAdvanceFrame = -1;
    private int lastChoiceFrame = -1;
    private GameObject previousSelection;
    private static WalkDialogueController activeChoiceOwner;
    private static int cancelledFrame = -1;
    private static int handledInputFrame = -1;
    private readonly HashSet<string> playedOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsDialogueBusy => isDialogueBusy || isChoiceOpen;
    public bool IsTyping => isTyping;
    public bool IsChoiceOpen => isChoiceOpen;
    public static bool CancelledThisFrame => cancelledFrame == Time.frameCount;
    public static bool HandledInputThisFrame => handledInputFrame == Time.frameCount;

    private void Awake()
    {
        walkModeController ??= GetComponent<PlayerWalkModeController>();
        areaTracker ??= GetComponent<PlayerAreaTracker>();
        areaTransitionController ??= GetComponent<PlayerAreaTransitionController>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
        dialogueManager ??= DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
        catalog = WalkDialogueCatalog.LoadDefault();
        typingSpeedSource ??= dialogueManager != null ? dialogueManager.chatUI : null;
    }

    private void OnEnable()
    {
        // Awake is not repeated when Unity reloads scripts during Play Mode.
        catalog ??= WalkDialogueCatalog.LoadDefault();
        if (areaTracker != null) areaTracker.AreaChanged += HandleAreaChanged;
    }

    private void OnDisable()
    {
        if (areaTracker != null) areaTracker.AreaChanged -= HandleAreaChanged;
        ClearForWalkExit();
    }

    private void OnDestroy()
    {
        // The Scene owns the fixed view. Destruction must only release our state.
        ClearForWalkExit();
        if (choiceHandlers != null && choiceButtons != null)
            for (int i = 0; i < choiceHandlers.Length; i++)
                if (choiceButtons[i] != null) choiceButtons[i].onClick.RemoveListener(choiceHandlers[i]);
    }

    private void LateUpdate()
    {
        if (nerukoBubble != null && nerukoBubble.gameObject.activeSelf) PositionNerukoBubble();
    }

    private void Update()
    {
        if (!CanAcceptInput() || Time.frameCount < inputReadyFrame) return;
        if (isChoiceOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { TryCancelActiveChoice(); return; }
            // Vertical/Submit already map arrows, W/S, Enter and Space in InputManager.
            EnsureChoiceSelection();
            if (Input.GetKeyDown(KeyCode.E))
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                Button button = selected != null ? selected.GetComponent<Button>() : null;
                if (button != null && Array.IndexOf(choiceButtons, button) >= 0 && button.interactable)
                    button.onClick.Invoke();
            }
            return;
        }
        if (isDialogueBusy && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)))
            RequestAdvance();
    }

    private bool CanAcceptInput() => !Nekolpos.System.DebugConsoleInput.IsBlocked && Time.timeScale > 0f &&
        (walkModeController == null || (!walkModeController.IsPauseMenuOpen && !walkModeController.IsTransitioning));

    public void RequestAdvance()
    {
        if (!CanAcceptInput() || isChoiceOpen || Time.frameCount < inputReadyFrame || lastAdvanceFrame == Time.frameCount) return;
        lastAdvanceFrame = Time.frameCount;
        handledInputFrame = Time.frameCount;
        if (isTyping) { FinishTyping(); return; }
        if (isDialogueBusy) advanceRequested = true;
    }

    public static bool TryCancelActiveChoice()
    {
        if (activeChoiceOwner == null || !activeChoiceOwner.isChoiceOpen || !activeChoiceOwner.CanAcceptInput()) return false;
        cancelledFrame = Time.frameCount;
        handledInputFrame = Time.frameCount;
        activeChoiceOwner.CloseChoice();
        return true;
    }

    public bool OpenWalkChoice()
    {
        if (!CanStart(NerukoTalkTrigger) || !walkModeController.TrySetInteractionMenuOpen(true)) return false;
        List<WalkDialogueCatalog.Entry> entries = catalog.Find("Choice", NerukoTalkTrigger, ResolvePsychologyState());
        if (entries.Count == 0) entries = catalog.Find("Choice", NerukoTalkTrigger);
        StartSequence(entries, OpenChoicePanel, keepMovementPaused: true);
        return true;
    }

    public bool PlayInteraction(string interactionId, Action afterSequence = null)
    {
        if (!CanStart(interactionId)) return false;
        StartSequence(catalog.Find("Interaction", interactionId, ResolvePsychologyState()), afterSequence, keepMovementPaused: false);
        return true;
    }

    public bool PlayAreaComment(string areaId)
    {
        if (!CanStart(areaId)) return false;
        StartSequence(catalog.Find("Area", areaId, ResolvePsychologyState()), null, keepMovementPaused: false);
        return true;
    }

    public void ShowPlayerLine(string text) => ShowLine("Player", text);
    public void SayNeruko(string text) => ShowLine("Neruko", text);

    public void ClearForWalkExit()
    {
        if (runningSequence != null) StopCoroutine(runningSequence);
        runningSequence = null;
        FinishTyping();
        activeText = null;
        ReleaseChoiceSelection();
        isDialogueBusy = false;
        isChoiceOpen = false;
        advanceRequested = false;
        if (nerukoBubble != null) nerukoBubble.gameObject.SetActive(false);
        if (playerBubble != null) playerBubble.gameObject.SetActive(false);
        if (choicePanel != null) choicePanel.SetActive(false);
    }

    private bool CanStart(string triggerId)
    {
        return walkModeController != null && walkModeController.IsWalkMode && !IsDialogueBusy &&
            !string.IsNullOrWhiteSpace(triggerId) && EnsureView();
    }

    private void HandleAreaChanged(string _, string nextAreaId) => PlayAreaComment(nextAreaId);

    private void StartSequence(List<WalkDialogueCatalog.Entry> entries, Action completed, bool keepMovementPaused)
    {
        if (entries == null || entries.Count == 0) { completed?.Invoke(); return; }
        runningSequence = StartCoroutine(PlaySequence(entries, completed, keepMovementPaused));
    }

    private IEnumerator PlaySequence(List<WalkDialogueCatalog.Entry> entries, Action completed, bool keepMovementPaused)
    {
        isDialogueBusy = true;
        foreach (WalkDialogueCatalog.Entry entry in entries)
        {
            ShowLine(entry.Speaker, entry.Text);
            advanceRequested = false;
            while (isTyping) yield return null;
            float elapsed = 0f;
            while (!advanceRequested && elapsed < autoHideSeconds)
            {
                if (CanAcceptInput()) elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        HideLines();
        isDialogueBusy = false;
        runningSequence = null;
        completed?.Invoke();
        if (!keepMovementPaused && !isChoiceOpen) walkModeController?.TrySetInteractionMenuOpen(false);
    }

    private void OpenChoicePanel()
    {
        if (!EnsureView()) { CloseChoice(); return; }
        followChoiceText.text = catLocomotionController != null && catLocomotionController.IsFollowing
            ? "待ってて" : "ついてきて";
        isChoiceOpen = true;
        choicePanel.SetActive(true);
        inputReadyFrame = Time.frameCount + 1;
        activeChoiceOwner = this;
        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        EnsureChoiceSelection();
    }

    private void CloseChoice()
    {
        isChoiceOpen = false;
        if (choicePanel != null) choicePanel.SetActive(false);
        ReleaseChoiceSelection();
        walkModeController?.TrySetInteractionMenuOpen(false);
    }

    private void HandleChoice(string action)
    {
        if (!isChoiceOpen || !CanAcceptInput() || Time.frameCount < inputReadyFrame || lastChoiceFrame == Time.frameCount) return;
        lastChoiceFrame = Time.frameCount;
        handledInputFrame = Time.frameCount;
        if (string.Equals(action, "stepBack", StringComparison.OrdinalIgnoreCase))
        {
            isChoiceOpen = false;
            if (choicePanel != null) choicePanel.SetActive(false);
            ReleaseChoiceSelection();
            StartSequence(catalog.Find("ChoiceResult", "StepBack"), () =>
            {
                CloseChoice();
                if (catLocomotionController == null || !catLocomotionController.TryStepBack())
                    Debug.LogWarning($"[{nameof(WalkDialogueController)}] 後退を開始できませんでした。", this);
            }, keepMovementPaused: true);
            return;
        }
        if (string.Equals(action, "toggleFollow", StringComparison.OrdinalIgnoreCase))
            action = catLocomotionController != null && catLocomotionController.IsFollowing ? "wait" : "follow";
        if (string.Equals(action, "nothing", StringComparison.OrdinalIgnoreCase)) { CloseChoice(); return; }
        if (string.Equals(action, "follow", StringComparison.OrdinalIgnoreCase))
        {
            if (catLocomotionController == null)
            {
                Debug.LogWarning($"[{nameof(WalkDialogueController)}] FollowにはCatLocomotionController参照が必要です。", this);
                return;
            }

            catLocomotionController.FollowPlayer();
            CloseChoice();
            return;
        }

        if (string.Equals(action, "wait", StringComparison.OrdinalIgnoreCase))
        {
            if (catLocomotionController == null)
            {
                Debug.LogWarning($"[{nameof(WalkDialogueController)}] WaitにはCatLocomotionController参照が必要です。", this);
                return;
            }

            catLocomotionController.WaitHere();
            CloseChoice();
            return;
        }

        if (string.Equals(action, "end", StringComparison.OrdinalIgnoreCase))
        {
            isChoiceOpen = false;
            if (choicePanel != null) choicePanel.SetActive(false);
            ReleaseChoiceSelection();
            StartSequence(catalog.Find("ChoiceResult", WalkEndTrigger), EndWalkAfterResponse, keepMovementPaused: true);
        }
    }

    private void EndWalkAfterResponse()
    {
        dialogueManager ??= DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
        string actionId = WalkResultActionIds[UnityEngine.Random.Range(0, WalkResultActionIds.Length)];
        if (dialogueManager == null || !dialogueManager.TryRunSystemTimedEvent(actionId, CompleteWalkWhileOpaque))
        {
            Debug.LogWarning($"[{nameof(WalkDialogueController)}] 時間経過イベント '{actionId}' を開始できませんでした。", this);
            return;
        }
    }

    /// <summary>時間経過イベントが完全暗転した後にだけ、散歩から通常会話へ安全に戻す。</summary>
    private void CompleteWalkWhileOpaque()
    {
        Transform anchor = FindNearestActiveAnchor(transform.position, new[] { tableConversationAnchor, deskConversationAnchor });
        if (anchor == null || areaTransitionController == null)
        {
            Debug.LogWarning($"[{nameof(WalkDialogueController)}] 暗転中の散歩終了に必要なAnchorまたはAreaTransitionControllerが未設定です。", this);
            return;
        }

        string expectedArea = anchor.name.IndexOf("Desk", StringComparison.OrdinalIgnoreCase) >= 0 ? "Desk" : "Table";
        if (!areaTransitionController.TeleportImmediately(anchor, expectedArea))
        {
            Debug.LogWarning($"[{nameof(WalkDialogueController)}] 暗転中の散歩終了Player移動に失敗しました。", this);
            return;
        }

        if (walkModeController == null || !walkModeController.TryExitWalkMode())
        {
            Debug.LogWarning($"[{nameof(WalkDialogueController)}] 暗転中に通常会話モードへ復帰できませんでした。", this);
        }
    }

    public static Transform FindNearestActiveAnchor(Vector3 origin, IReadOnlyList<Transform> anchors)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.PositiveInfinity;
        if (anchors == null) return null;
        for (int index = 0; index < anchors.Count; index++)
        {
            Transform candidate = anchors[index];
            if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
            float distance = (candidate.position - origin).sqrMagnitude;
            if (distance < nearestSqrDistance) { nearest = candidate; nearestSqrDistance = distance; }
        }
        return nearest;
    }

    private string ResolvePsychologyState()
    {
        if (statusManager == null) return "NORMAL";
        StatusRepresentativeState state = statusManager.GetRepresentativeState();
        return state.IsNormal ? "NORMAL" : state.Primary.Value.ToString().ToUpperInvariant();
    }

    private void ShowLine(string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!EnsureView()) return;
        FinishTyping();
        bool player = string.Equals(speaker, "Player", StringComparison.OrdinalIgnoreCase);
        TextMeshProUGUI target = player ? playerText : nerukoText;
        target.text = text;
        target.transform.parent.gameObject.SetActive(true);
        if (player) nerukoBubble.gameObject.SetActive(false); else playerBubble.gameObject.SetActive(false);
        activeText = target;
        inputReadyFrame = Time.frameCount + 1;
        target.maxVisibleCharacters = int.MaxValue;
        if (!player)
        {
            target.maxVisibleCharacters = 0;
            target.ForceMeshUpdate();
            isTyping = true;
            typingRoutine = StartCoroutine(TypeLine(target.textInfo.characterCount));
        }
    }

    private void HideLines()
    {
        FinishTyping();
        activeText = null;
        if (nerukoBubble != null) nerukoBubble.gameObject.SetActive(false);
        if (playerBubble != null) playerBubble.gameObject.SetActive(false);
    }

    private bool EnsureView()
    {
        if (canvasObject == null || nerukoBubble == null || playerBubble == null || nerukoText == null || playerText == null || choicePanel == null ||
            followChoiceText == null || choiceButtons == null || choiceButtons.Length != 5)
        {
            Debug.LogError("[WalkDialogueController] Assign the authored WalkDialogueView and its five buttons in the Scene.", this);
            return false;
        }
        if (viewBound) return true;
        foreach (var button in choiceButtons)
            if (button == null) { Debug.LogError("[WalkDialogueController] A choice button reference is missing.", this); return false; }
        string[] actions = { "toggleFollow", "carry", "stepBack", "end", "nothing" };
        choiceHandlers = new UnityEngine.Events.UnityAction[choiceButtons.Length];
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            string action = actions[i];
            choiceHandlers[i] = () => HandleChoice(action);
            choiceButtons[i].onClick.AddListener(choiceHandlers[i]);
            var navigation = new Navigation { mode = Navigation.Mode.Explicit };
            // Disabled rows stay visible but never receive keyboard selection.
            int previous = i - 1;
            while (previous >= 0 && previous == 1) previous--;
            int next = i + 1;
            while (next < choiceButtons.Length && next == 1) next++;
            navigation.selectOnUp = previous >= 0 ? choiceButtons[previous] : null;
            navigation.selectOnDown = next < choiceButtons.Length ? choiceButtons[next] : null;
            choiceButtons[i].navigation = navigation;
        }
        choiceButtons[0].interactable = catLocomotionController != null;
        choiceButtons[1].interactable = false;
        choiceButtons[2].interactable = catLocomotionController != null;
        viewBound = true;
        return true;
    }

    private void EnsureChoiceSelection()
    {
        var events = EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;
        var button = selected != null ? selected.GetComponent<Button>() : null;
        if (button != null && Array.IndexOf(choiceButtons, button) >= 0 && button.interactable) return;
        foreach (var choice in choiceButtons)
            if (choice.interactable) { events.SetSelectedGameObject(choice.gameObject); break; }
    }

    private void ReleaseChoiceSelection()
    {
        if (activeChoiceOwner == this) activeChoiceOwner = null;
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null && choicePanel != null &&
            events.currentSelectedGameObject.transform.IsChildOf(choicePanel.transform))
            events.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection = null;
    }

    private IEnumerator TypeLine(int characterCount)
    {
        float elapsed = 0f;
        float interval = Mathf.Max(.001f, typingSpeedSource != null ? typingSpeedSource.typingSpeed : typingSecondsPerCharacter);
        while (activeText != null && activeText.maxVisibleCharacters < characterCount)
        {
            yield return null;
            if (!CanAcceptInput()) continue;
            elapsed += Time.unscaledDeltaTime;
            int count = Mathf.FloorToInt(elapsed / interval);
            activeText.maxVisibleCharacters = Mathf.Min(characterCount, count);
        }
        if (activeText != null) activeText.maxVisibleCharacters = int.MaxValue;
        isTyping = false;
        typingRoutine = null;
    }

    private void FinishTyping()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = null;
        if (activeText != null) activeText.maxVisibleCharacters = int.MaxValue;
        isTyping = false;
    }

    private void PositionNerukoBubble()
    {
        Transform target = nerukoBubbleAnchor != null ? nerukoBubbleAnchor : nerukoRootFallback;
        Vector3 point = playerCamera != null && target != null ? playerCamera.WorldToViewportPoint(target.position) : new Vector3(.85f, .72f, 1f);
        float x = point.z < 0f ? (point.x < .5f ? 1f - viewportPadding.x : viewportPadding.x) : Mathf.Clamp(point.x, viewportPadding.x, 1f - viewportPadding.x);
        float y = Mathf.Clamp(point.y, viewportPadding.y, 1f - viewportPadding.y);
        nerukoBubble.anchorMin = nerukoBubble.anchorMax = new Vector2(x, y);
        nerukoBubble.pivot = new Vector2(x > .5f ? 1f : 0f, .5f);
        var canvasRect = (RectTransform)canvasObject.transform;
        float bubbleWidth = nerukoBubble.rect.width / Mathf.Max(1f, canvasRect.rect.width);
        float halfHeight = nerukoBubble.rect.height / Mathf.Max(1f, canvasRect.rect.height) * .5f;
        x = Mathf.Clamp(x, Mathf.Max(viewportPadding.x, nerukoBubble.pivot.x * bubbleWidth),
            Mathf.Min(1f - viewportPadding.x, 1f - (1f - nerukoBubble.pivot.x) * bubbleWidth));
        y = Mathf.Clamp(y, Mathf.Max(viewportPadding.y, halfHeight), Mathf.Min(1f - viewportPadding.y, 1f - halfHeight));
        nerukoBubble.anchorMin = nerukoBubble.anchorMax = new Vector2(x, y);
    }

}
