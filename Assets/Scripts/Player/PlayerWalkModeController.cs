using Nekolpos.System;
using UnityEngine;

/// <summary>
/// 会話とPlayer自由歩行の排他的な切替を担当する。
/// 散歩開始時は現在の会話拠点に対応するPlayerリスポーン地点へ安全に移動し、歩行終了時だけ既存の猫ホーム拠点へ復帰する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerSafetyMovementController))]
[RequireComponent(typeof(PlayerAreaTracker))]
public sealed class PlayerWalkModeController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private PlayerSafetyMovementController playerSafetyMovementController;
    [SerializeField] private PlayerAreaTracker playerAreaTracker;
    [SerializeField] private NekolposGameBootstrap gameBootstrap;
    [SerializeField] private ChatUIController chatUI;
    [Tooltip("散歩開始時にPlayerを配置するTable用リスポーン地点です。")]
    [SerializeField] private Transform walkStartDestination;
    [Tooltip("Deskで散歩を開始した時だけ使用するPlayerリスポーン地点です。未設定時はTable用地点へフォールバックします。")]
    [SerializeField] private Transform walkStartDeskDestination;

    [Header("Camera References")]
    [Tooltip("自由歩行中だけ有効にするPlayer Cameraです。")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("通常会話中だけ有効にするCameraです。")]
    [SerializeField] private Camera normalConversationCamera;
    [SerializeField] private WalkDialogueController walkDialogueController;

    [Header("Runtime Debug")]
    [SerializeField] private bool isWalkMode;
    [SerializeField] private bool isTransitioning;
    [SerializeField] private bool isInteractionMenuOpen;
    [SerializeField] private bool isPauseMenuOpen;
    [SerializeField] private bool isTabletInteractionActive;
    [SerializeField] private CatHomeLocation lastReturnHomeLocation = CatHomeLocation.Table;

    public bool IsWalkMode => isWalkMode;
    public Camera WalkCamera => playerCamera;
    public bool IsTransitioning => isTransitioning;
    public bool IsInteractionMenuOpen => isInteractionMenuOpen;
    public bool IsPauseMenuOpen => isPauseMenuOpen;
    public bool IsTabletInteractionActive => isTabletInteractionActive;
    public CatHomeLocation LastReturnHomeLocation => lastReturnHomeLocation;
    public event System.Action<bool> WalkModeChanged;
    public event System.Action<bool> TabletInteractionChanged;

    private bool hasTabletReturnPose;
    private Vector3 tabletReturnPlayerPosition;
    private Quaternion tabletReturnPlayerRotation;
    private Vector3 tabletReturnCameraLocalPosition;
    private Quaternion tabletReturnCameraLocalRotation;
    private Vector3 tabletReturnCameraPosition;
    private Quaternion tabletReturnCameraRotation;

    private void Awake()
    {
        playerSafetyMovementController ??= GetComponent<PlayerSafetyMovementController>();
        playerAreaTracker ??= GetComponent<PlayerAreaTracker>();
        walkDialogueController ??= GetComponent<WalkDialogueController>();
        Transform transitionPoints = GameObject.Find("PlayerTransitionPoints")?.transform;
        walkStartDestination ??= transitionPoints?.Find("PlayerSpawn_Table");
        walkStartDeskDestination ??= transitionPoints?.Find("PlayerSpawn_Desk");
        ApplyNormalConversationState();
        if (Application.isPlaying)
        {
            ControlGuideHUD guide = GetComponent<ControlGuideHUD>() ?? gameObject.AddComponent<ControlGuideHUD>();
            guide.Bind(this, chatUI);
        }
    }

    private void Update()
    {
        if (!DebugConsoleInput.IsBlocked && isWalkMode && !isTransitioning && !isInteractionMenuOpen && !isPauseMenuOpen && !isTabletInteractionActive && Input.GetKeyDown(KeyCode.P))
        {
            TryExitWalkMode();
        }
    }

    /// <summary>通常会話・ホーム移動を起動せず、一時入力と散歩状態だけ解除する。</summary>
    public void ResetForTitleReload()
    {
        bool wasWalking = isWalkMode;
        walkDialogueController?.ClearForWalkExit();
        GetComponent<PlayerInteractor>()?.ResetInteractionState();
        ApplyNormalConversationState();
        if (wasWalking) WalkModeChanged?.Invoke(false);
    }

    private void OnDisable() => ResetForTitleReload();

    public bool TryEnterWalkMode()
    {
        if (isWalkMode || isTransitioning || !HasRequiredReferences())
        {
            return false;
        }

        isTransitioning = true;
        try
        {
            chatUI.SetUiSuppressed(true);
            chatUI.HideChoices();
            playerSafetyMovementController.ClearMovementInputOverride();
            Transform walkStart = ResolveWalkStartDestination(
                playerAreaTracker.CurrentAreaId,
                walkStartDestination,
                walkStartDeskDestination);
            playerSafetyMovementController.TeleportTo(walkStart.position, updateSafePosition: true);
            playerAreaTracker.RefreshAreaNow();
            SetCameraMode(isPlayerCameraActive: true);
            isWalkMode = true;
            isPauseMenuOpen = false;
            WalkModeChanged?.Invoke(true);
            ApplyWalkInputState();
            return true;
        }
        finally
        {
            isTransitioning = false;
            // ApplyWalkInputState は遷移中には入力を止めるため、散歩開始完了後に
            // もう一度適用して初回から歩行・マウス視点を有効化する。
            if (isWalkMode)
            {
                ApplyWalkInputState();
            }
        }
    }

    /// <summary>散歩を維持したまま、現在のエリアに対応する開始地点へ復帰する。</summary>
    public bool TryUnstuck()
    {
        if (!isWalkMode || isTransitioning || isTabletInteractionActive ||
            playerSafetyMovementController == null || playerAreaTracker == null)
            return false;

        Transform destination = ResolveWalkStartDestination(
            playerAreaTracker.CurrentAreaId, walkStartDestination, walkStartDeskDestination);
        if (destination == null)
        {
            Debug.LogWarning("[PlayerWalkMode] スタック脱出先が設定されていません。", this);
            return false;
        }

        playerSafetyMovementController.ClearMovementInputOverride();
        playerSafetyMovementController.TeleportTo(destination.position, updateSafePosition: true);
        playerAreaTracker.RefreshAreaNow();
        return true;
    }

    public bool TryExitWalkMode()
    {
        if (!isWalkMode || isTransitioning)
        {
            return false;
        }

        isTransitioning = true;
        try
        {
            walkDialogueController?.ClearForWalkExit();
            playerSafetyMovementController.SetMovementInput(Vector2.zero);
            playerSafetyMovementController.ClearMovementInputOverride();
            playerSafetyMovementController.enabled = false;

            lastReturnHomeLocation = ResolveReturnHomeLocation(playerAreaTracker.CurrentAreaId);
            isWalkMode = false;
            isTabletInteractionActive = false;
            hasTabletReturnPose = false;
            WalkModeChanged?.Invoke(false);
            SetCameraMode(isPlayerCameraActive: false);
            gameBootstrap.SetHomeLocation(lastReturnHomeLocation);
            chatUI.SetUiSuppressed(false);
            chatUI.HideChoices();
            chatUI.RestoreNormalConversationInputMode();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            isInteractionMenuOpen = false;
            isPauseMenuOpen = false;
            return true;
        }
        finally
        {
            isTransitioning = false;
        }
    }

    public static CatHomeLocation ResolveReturnHomeLocation(string currentAreaId)
    {
        return string.Equals(currentAreaId?.Trim(), "Desk", System.StringComparison.OrdinalIgnoreCase)
            ? CatHomeLocation.Desk
            : CatHomeLocation.Table;
    }

    /// <summary>
    /// Deskから始める散歩だけはDesk側の安全な開始地点を使い、それ以外はTable側を使う。
    /// Desk地点が未設定の既存Sceneは、従来どおりTable地点へフォールバックする。
    /// </summary>
    public static Transform ResolveWalkStartDestination(
        string currentAreaId,
        Transform tableDestination,
        Transform deskDestination)
    {
        return string.Equals(currentAreaId?.Trim(), "Desk", System.StringComparison.OrdinalIgnoreCase) && deskDestination != null
            ? deskDestination
            : tableDestination;
    }

    /// <summary>
    /// 散歩中の専用UIだけが呼ぶ入力停止状態。PlayerCameraは維持し、通常会話へは遷移しない。
    /// </summary>
    public bool TrySetInteractionMenuOpen(bool open)
    {
        if (!isWalkMode || isTransitioning || isInteractionMenuOpen == open)
        {
            return isInteractionMenuOpen == open;
        }

        isInteractionMenuOpen = open;
        ApplyWalkInputState();
        return true;
    }

    /// <summary>
    /// 散歩中のワールドタブレットを開く前に、移動・PlayerCameraだけを一時停止する。
    /// 会話モードへは戻さないため、終了時に散歩をそのまま再開できる。
    /// </summary>
    public bool TryBeginTabletInteraction()
    {
        if (!isWalkMode || isTransitioning || isInteractionMenuOpen || isPauseMenuOpen || isTabletInteractionActive)
        {
            return false;
        }

        tabletReturnPlayerPosition = transform.position;
        tabletReturnPlayerRotation = transform.rotation;
        tabletReturnCameraLocalPosition = playerCamera.transform.localPosition;
        tabletReturnCameraLocalRotation = playerCamera.transform.localRotation;
        tabletReturnCameraPosition = playerCamera.transform.position;
        tabletReturnCameraRotation = playerCamera.transform.rotation;
        hasTabletReturnPose = true;
        isTabletInteractionActive = true;
        TabletInteractionChanged?.Invoke(true);
        ApplyWalkInputState();
        SetCameraMode(isPlayerCameraActive: false);
        return true;
    }

    public bool TryGetTabletReturnCameraPose(out Vector3 position, out Quaternion rotation)
    {
        position = tabletReturnCameraPosition;
        rotation = tabletReturnCameraRotation;
        return hasTabletReturnPose && isWalkMode;
    }

    /// <summary>タブレットカメラから戻った後、元の散歩Cameraと入力へ復帰する。</summary>
    public void CompleteTabletInteraction()
    {
        if (!isTabletInteractionActive)
        {
            return;
        }

        isTabletInteractionActive = false;
        if (isWalkMode)
        {
            if (hasTabletReturnPose)
            {
                playerSafetyMovementController.TeleportTo(tabletReturnPlayerPosition, updateSafePosition: false);
                playerSafetyMovementController.SetLookRotation(tabletReturnPlayerRotation);
                playerCamera.transform.SetLocalPositionAndRotation(tabletReturnCameraLocalPosition, tabletReturnCameraLocalRotation);
                playerAreaTracker.RefreshAreaNow();
            }
            SetCameraMode(isPlayerCameraActive: true);
            ApplyWalkInputState();
        }
        hasTabletReturnPose = false;
        TabletInteractionChanged?.Invoke(false);
    }

    /// <summary>
    /// ポーズメニューが散歩中に開閉したことを受け取り、移動・マウス視点操作とカーソル状態を同期する。
    /// </summary>
    public void SetPauseMenuOpen(bool open)
    {
        if (!isWalkMode || isTransitioning || isPauseMenuOpen == open)
        {
            return;
        }

        isPauseMenuOpen = open;
        ApplyWalkInputState();
    }

    private bool HasRequiredReferences()
    {
        if (playerSafetyMovementController != null &&
            playerAreaTracker != null &&
            gameBootstrap != null &&
            chatUI != null &&
            walkStartDestination != null &&
            playerCamera != null &&
            normalConversationCamera != null)
        {
            return true;
        }

        Debug.LogWarning("[PlayerWalkMode] 必須のInspector参照が不足しているため、散歩モードを開始できません。", this);
        return false;
    }

    private void ApplyNormalConversationState()
    {
        if (playerSafetyMovementController != null)
        {
            playerSafetyMovementController.ClearMovementInputOverride();
            playerSafetyMovementController.enabled = false;
        }

        SetCameraMode(isPlayerCameraActive: false);
        // 通常会話はUIをポインタ操作するため、直前の散歩・Play再開で残った
        // カーソルロック状態を引き継がない。
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        isWalkMode = false;
        isTransitioning = false;
        isInteractionMenuOpen = false;
        isPauseMenuOpen = false;
        isTabletInteractionActive = false;
        hasTabletReturnPose = false;
    }

    private void ApplyWalkInputState()
    {
        if (playerSafetyMovementController == null)
        {
            return;
        }

        bool acceptsWalkInput = isWalkMode &&
                                !isTransitioning &&
                                !isInteractionMenuOpen &&
                                !isPauseMenuOpen &&
                                !isTabletInteractionActive;
        playerSafetyMovementController.ClearMovementInputOverride();
        playerSafetyMovementController.enabled = acceptsWalkInput;
        Cursor.lockState = acceptsWalkInput ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !acceptsWalkInput;
    }

    private void SetCameraMode(bool isPlayerCameraActive)
    {
        SetCameraAndListenerEnabled(playerCamera, isPlayerCameraActive);
        SetCameraAndListenerEnabled(normalConversationCamera, !isPlayerCameraActive);
    }

    private static void SetCameraAndListenerEnabled(Camera camera, bool enabled)
    {
        if (camera == null)
        {
            return;
        }

        camera.enabled = enabled;
        AudioListener listener = camera.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = enabled;
        }
    }
}
