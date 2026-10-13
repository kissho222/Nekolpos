using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>WalkMode中だけPlayerCamera中央からInteractableを検出してE入力を仲介する。</summary>
[DisallowMultipleComponent]
public sealed class PlayerInteractor : MonoBehaviour
{
    private const int MaxHits = 16;

    [Header("Required References")]
    [SerializeField] private PlayerWalkModeController walkModeController;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerInteractionPrompt prompt;

    [Header("Detection")]
    [SerializeField, Min(0.01f)] private float interactionDistance = 0.12f;
    [SerializeField, Min(0.001f)] private float sphereRadius = 0.008f;
    [SerializeField] private LayerMask interactionLayers = ~0;

    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
    private IPlayerInteractable currentInteractable;

    public IPlayerInteractable CurrentInteractable => currentInteractable;
    public PlayerWalkModeController WalkModeController => walkModeController;
    public bool IsInteractionAvailable => !Nekolpos.System.DebugConsoleInput.IsBlocked && isActiveAndEnabled && Time.timeScale > 0f &&
        walkModeController != null && walkModeController.IsWalkMode && !walkModeController.IsTransitioning &&
        !walkModeController.IsPauseMenuOpen && !walkModeController.IsInteractionMenuOpen && !walkModeController.IsTabletInteractionActive &&
        currentInteractable != null && !(currentInteractable is Object target && target == null) &&
        !currentInteractable.IsInteracting && currentInteractable.CanInteract(this) && !IsTextEntryFocused();

    private void Awake()
    {
        walkModeController ??= GetComponent<PlayerWalkModeController>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
        prompt ??= GetComponent<PlayerInteractionPrompt>() ?? gameObject.AddComponent<PlayerInteractionPrompt>();
    }

    private void Update()
    {
        if (Nekolpos.System.DebugConsoleInput.IsBlocked || Time.timeScale <= 0f || IsTextEntryFocused() || walkModeController == null || !walkModeController.IsWalkMode || walkModeController.IsTransitioning || walkModeController.IsPauseMenuOpen || walkModeController.IsInteractionMenuOpen || walkModeController.IsTabletInteractionActive)
        {
            SetCurrentInteractable(null);
            return;
        }

        SetCurrentInteractable(FindInteractable());
        if (currentInteractable != null && !WalkDialogueController.HandledInputThisFrame && Input.GetKeyDown(KeyCode.E) && !IsTextEntryFocused())
        {
            currentInteractable.TryInteract(this);
        }
    }

    private void OnEnable()
    {
        if (walkModeController != null) walkModeController.WalkModeChanged += HandleWalkModeChanged;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        if (walkModeController != null) walkModeController.WalkModeChanged -= HandleWalkModeChanged;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        ResetInteractionState();
    }

    private void OnDestroy() => ResetInteractionState();

    public void ResetInteractionState()
    {
        currentInteractable = null;
        prompt?.Hide();
    }

    private void HandleWalkModeChanged(bool active)
    {
        if (!active) ResetInteractionState();
    }

    private void HandleActiveSceneChanged(Scene previous, Scene next) => ResetInteractionState();

    public IPlayerInteractable FindInteractable()
    {
        if (playerCamera == null || interactionDistance <= 0f || sphereRadius <= 0f)
        {
            return null;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            playerCamera.transform.position,
            sphereRadius,
            playerCamera.transform.forward,
            hits,
            interactionDistance,
            interactionLayers,
            QueryTriggerInteraction.Collide);

        IPlayerInteractable closest = null;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = hits[index];
            PlayerInteractable candidate = hit.collider != null
                ? hit.collider.GetComponentInParent<PlayerInteractable>()
                : null;
            if (candidate == null || !candidate.CanInteract(this) || hit.distance >= closestDistance)
            {
                continue;
            }

            closest = candidate;
            closestDistance = hit.distance;
        }

        return closest;
    }

    private void SetCurrentInteractable(IPlayerInteractable interactable)
    {
        currentInteractable = interactable;
        if (currentInteractable == null || (currentInteractable is Object target && target == null) || currentInteractable.IsInteracting)
        {
            ResetInteractionState();
            return;
        }

        prompt?.SetPrompt(currentInteractable.PromptText);
    }

    private static bool IsTextEntryFocused()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null &&
               (selected.GetComponentInParent<TMP_InputField>() != null || selected.GetComponentInParent<InputField>() != null);
    }
}
