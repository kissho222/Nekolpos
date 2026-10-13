using UnityEngine;

[DisallowMultipleComponent]
public sealed class NerukoTalkInteractable : PlayerInteractable
{
    [SerializeField] private WalkDialogueController walkDialogueController;
    private Nekolpos.System.CatLocomotionController catLocomotionController;

    private void Awake() => catLocomotionController = GetComponent<Nekolpos.System.CatLocomotionController>();

    public override bool IsInteracting
    {
        get
        {
            catLocomotionController ??= GetComponent<Nekolpos.System.CatLocomotionController>();
            return (catLocomotionController != null && (catLocomotionController.IsSleepTransitioning || (catLocomotionController.IsWalkActionBusy && !catLocomotionController.IsSteppingBack))) ||
                (walkDialogueController != null && walkDialogueController.IsDialogueBusy);
        }
    }

    public override bool TryInteract(PlayerInteractor interactor)
    {
        if (!CanInteract(interactor) || interactor.WalkModeController == null || !interactor.WalkModeController.IsWalkMode) return false;
        walkDialogueController ??= interactor.GetComponent<WalkDialogueController>();
        if (walkDialogueController == null)
        {
            Debug.LogWarning($"[{nameof(NerukoTalkInteractable)}] Playerの{ nameof(WalkDialogueController) }参照が必要です。", this);
            return false;
        }

        catLocomotionController ??= GetComponent<Nekolpos.System.CatLocomotionController>();
        if (catLocomotionController != null && catLocomotionController.IsYawning)
            return catLocomotionController.TryDeferInteractionUntilYawnEnds(() => walkDialogueController.OpenWalkChoice());
        catLocomotionController?.InterruptStepBackForInteraction();
        return catLocomotionController != null && catLocomotionController.IsSleeping
            ? catLocomotionController.TryWakeForInteraction(() => walkDialogueController.OpenWalkChoice())
            : walkDialogueController.OpenWalkChoice();
    }
}
