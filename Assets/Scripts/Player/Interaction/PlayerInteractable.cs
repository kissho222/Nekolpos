using UnityEngine;

/// <summary>Inspectorで設定するPlayer用Interactableの共通基底。</summary>
public abstract class PlayerInteractable : MonoBehaviour, IPlayerInteractable
{
    [Tooltip("プレイヤーが近づいた時に表示する操作案内です。CableTravelInteractableでは移動先が分かる文言にします。")]
    [SerializeField, TextArea] private string promptText = "E：調べる";

    public virtual string PromptText => promptText;
    public abstract bool IsInteracting { get; }

    public virtual bool CanInteract(PlayerInteractor interactor)
    {
        return isActiveAndEnabled && !IsInteracting && interactor != null;
    }

    public abstract bool TryInteract(PlayerInteractor interactor);
}
