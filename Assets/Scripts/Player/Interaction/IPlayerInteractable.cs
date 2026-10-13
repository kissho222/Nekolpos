/// <summary>PlayerInteractorから実行できるWorld内オブジェクトの共通契約。</summary>
public interface IPlayerInteractable
{
    bool CanInteract(PlayerInteractor interactor);
    string PromptText { get; }
    bool IsInteracting { get; }
    bool TryInteract(PlayerInteractor interactor);
}
