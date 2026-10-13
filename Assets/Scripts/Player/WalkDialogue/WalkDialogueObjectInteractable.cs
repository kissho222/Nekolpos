using UnityEngine;

/// <summary>InspectorでID・Prompt・再生制限を設定する汎用の散歩会話オブジェクト。</summary>
[DisallowMultipleComponent]
public sealed class WalkDialogueObjectInteractable : PlayerInteractable
{
    [SerializeField] private string interactionId;
    [SerializeField] private bool playOnce;
    [SerializeField, Min(0f)] private float cooldownSeconds = 5f;
    [SerializeField] private WalkDialogueController walkDialogueController;
    private bool played;
    private float nextAllowedTime;
    private bool isPlaying;

    public override bool IsInteracting => isPlaying;

    public override bool CanInteract(PlayerInteractor interactor)
    {
        return base.CanInteract(interactor) && !string.IsNullOrWhiteSpace(interactionId) &&
               (!playOnce || !played) && Time.unscaledTime >= nextAllowedTime;
    }

    public override bool TryInteract(PlayerInteractor interactor)
    {
        if (!CanInteract(interactor)) return false;
        walkDialogueController ??= interactor.GetComponent<WalkDialogueController>();
        if (walkDialogueController == null) return false;
        isPlaying = true;
        bool started = walkDialogueController.PlayInteraction(interactionId, Complete);
        if (!started) isPlaying = false;
        return started;
    }

    private void Complete()
    {
        played = true;
        nextAllowedTime = Time.unscaledTime + cooldownSeconds;
        isPlaying = false;
    }
}
