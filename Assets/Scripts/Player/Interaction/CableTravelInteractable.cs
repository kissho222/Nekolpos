using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CableTravelInteractable : PlayerInteractable
{
    [Header("移動先")]
    [Tooltip("暗転中にプレイヤーを配置するTransformです。移動先の床面に置いた専用Anchorを指定します。")]
    [SerializeField] private Transform destination;
    [Tooltip("移動後にPlayerAreaTrackerが判定すべきArea Idです。移動先のPlayerAreaVolumeと同じ文字列を指定します。空欄でも移動はしますが、到着確認を行いません。")]
    [SerializeField] private string expectedAreaId;

    [Header("必要なController")]
    [Tooltip("暗転・安全移動・Area再判定を担当するPlayerAreaTransitionControllerです。通常はPlayerに付いている同Componentを指定します。未指定時は操作したPlayerから自動取得します。")]
    [SerializeField] private PlayerAreaTransitionController areaTransitionController;
    [Tooltip("散歩中の反応会話を再生するWalkDialogueControllerです。通常はPlayerに付いている同Componentを指定します。未指定時は操作したPlayerから自動取得します。")]
    [SerializeField] private WalkDialogueController walkDialogueController;

    [Header("移動前の散歩会話")]
    [Tooltip("移動前に再生するWalkDialogueのinteraction_idです。空欄なら会話を出さず、すぐに移動します。")]
    [SerializeField] private string walkDialogueInteractionId = "CableDesk";
    private bool isTraveling;

    public override bool IsInteracting => isTraveling;

    public override bool TryInteract(PlayerInteractor interactor)
    {
        if (!CanInteract(interactor) || interactor.WalkModeController == null || !interactor.WalkModeController.IsWalkMode) return false;
        areaTransitionController ??= interactor.GetComponent<PlayerAreaTransitionController>();
        if (destination == null || areaTransitionController == null)
        {
            Debug.LogWarning($"[{nameof(CableTravelInteractable)}] {name} requires a destination and {nameof(PlayerAreaTransitionController)}.", this);
            return false;
        }
        walkDialogueController ??= interactor.GetComponent<WalkDialogueController>();
        if (walkDialogueController != null && !string.IsNullOrWhiteSpace(walkDialogueInteractionId))
        {
            if (!walkDialogueController.PlayInteraction(walkDialogueInteractionId, () => TransitionAsync().Forget())) return false;
        }
        else TransitionAsync().Forget();
        return true;
    }

    private async UniTaskVoid TransitionAsync()
    {
        isTraveling = true;
        try
        {
            if (!await areaTransitionController.TransitionToAsync(destination, expectedAreaId))
                Debug.LogWarning($"[{nameof(CableTravelInteractable)}] Failed to transition from {name}.", this);
        }
        finally { isTraveling = false; }
    }
}
