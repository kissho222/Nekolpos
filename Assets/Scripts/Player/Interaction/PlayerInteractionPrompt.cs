using UnityEngine;

/// <summary>既存InteractorのPromptを共通HUDへ渡す互換窓口。</summary>
public sealed class PlayerInteractionPrompt : MonoBehaviour
{
    private ControlGuideHUD guide;
    private string currentPrompt;
    public bool IsVisible => !string.IsNullOrWhiteSpace(currentPrompt);

    public void SetPrompt(string text)
    {
        currentPrompt = text;
        if (guide == null) guide = GetComponent<ControlGuideHUD>();
        if (guide != null) guide.SetInteractionPrompt(text);
    }

    public void Hide()
    {
        currentPrompt = null;
        if (guide == null) guide = GetComponent<ControlGuideHUD>();
        if (guide != null) guide.SetInteractionPrompt(null);
    }

    private void OnDisable() => Hide();
    private void OnDestroy() => Hide();
}
