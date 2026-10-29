using UnityEngine;

/// <summary>
/// 通常プレイ用のCharacterController移動、Walkable判定、落下防止、SafePosition復帰を担当する。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerSafetyMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField] private bool enableMouseLook = true;
    [SerializeField, Min(0f)] private float mouseSensitivity = 3f;
    [SerializeField, Min(0f)] private float gravity = 9.81f;
    [SerializeField, Min(0f)] private float maxFallSpeed = 2f;
    [SerializeField, Min(0f)] private float groundedDownwardSpeed = 0.1f;
    [SerializeField, Min(0f)] private float jumpHeight = 0.02f;

    [Header("Walkable Surface")]
    [Tooltip("立ってよい面だけを含めるLayerMask。初期値は未設定のため、Inspectorで必ず指定する。Layer名はコードへ固定しない。")]
    [SerializeField] private LayerMask walkableLayers;
    [SerializeField, Range(0f, 89f)] private float maximumWalkableSlope = 55f;
    [SerializeField, Min(0.001f)] private float groundProbeRadius = 0.008f;
    [SerializeField, Min(0.001f)] private float groundProbeStartHeight = 0.02f;
    [SerializeField, Min(0.001f)] private float groundProbeDistance = 0.03f;

    [Header("Edge Protection")]
    [SerializeField] private bool edgeFallPreventionEnabled = true;
    [SerializeField, Min(0f)] private float edgeLookAheadDistance = 0.02f;
    [Tooltip("前方の下方にWalkable面がある時だけ、通常歩行で降りてよい最大落差です。虚空やこの値を超える落差はEdgeFallPreventionで止めます。")]
    [SerializeField, Min(0.001f)] private float maximumSafeStepDown = 0.03f;

    [Header("Safe Position")]
    [SerializeField, Min(0f)] private float safePositionStabilitySeconds = 0.1f;
    [SerializeField, Min(0.001f)] private float recoveryFallDistance = 0.25f;
    [SerializeField] private float recoveryWorldY = -1f;

    [Header("Runtime Debug")]
    [SerializeField] private bool isOnWalkableSurface;
    [SerializeField] private bool lastMovementBlockedByEdge;
    [SerializeField] private bool hasLastSafePosition;
    [SerializeField] private Vector3 lastSafePosition;
    [SerializeField] private bool safetyRecoverySuspended;

    private CharacterController characterController;
    private Nekolpos.Audio.CharacterMovementAudioController movementAudio;
    private Collider currentWalkableCollider;
    private float rotationX;
    private float rotationY;
    private float verticalVelocity;
    private float stableWalkableSeconds;
    private Vector2 externalMovementInput;
    private bool usesExternalMovementInput;

    public bool IsOnWalkableSurface => isOnWalkableSurface;
    public bool LastMovementBlockedByEdge => lastMovementBlockedByEdge;
    public bool HasLastSafePosition => hasLastSafePosition;
    public Vector3 LastSafePosition => lastSafePosition;
    public bool EdgeFallPreventionEnabled => edgeFallPreventionEnabled;
    public bool SafetyRecoverySuspended => safetyRecoverySuspended;
    public bool CanJump => CanStartJump(
        characterController != null && characterController.enabled,
        characterController != null && characterController.isGrounded,
        isOnWalkableSurface);

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        movementAudio = GetComponent<Nekolpos.Audio.CharacterMovementAudioController>();
        rotationX = transform.eulerAngles.x;
        rotationY = transform.eulerAngles.y;
    }

    private void Update()
    {
        if (Nekolpos.System.DebugConsoleInput.IsBlocked || Time.timeScale <= 0f || Nekolpos.System.SettingsPanel.IsOpen)
        {
            movementAudio?.SuspendMovementObservation();
            return;
        }
        UpdateLookRotation();
        RefreshWalkableState();
        RecoverFromUnsafeFallIfNeeded();
        TryStartJump();
        Vector3 positionBeforeMovement = transform.position;
        MovePlayer(ReadMovementInput());
        RefreshWalkableState();
        float horizontalDistance = Vector3.ProjectOnPlane(transform.position - positionBeforeMovement, Vector3.up).magnitude;
        float horizontalSpeed = Time.deltaTime > 0f ? horizontalDistance / Time.deltaTime : 0f;
        bool grounded = characterController != null && characterController.enabled && characterController.isGrounded &&
                        isOnWalkableSurface && verticalVelocity <= 0f;
        movementAudio?.ObserveMovement(horizontalSpeed, grounded, currentWalkableCollider, Time.deltaTime);
        UpdateLastSafePosition();
    }

    private void OnDisable() => movementAudio?.ResetMovementState();

    public void SetMovementInput(Vector2 input)
    {
        externalMovementInput = Vector2.ClampMagnitude(input, 1f);
        usesExternalMovementInput = true;
    }

    public void ClearMovementInputOverride()
    {
        externalMovementInput = Vector2.zero;
        usesExternalMovementInput = false;
    }

    public void SetEdgeFallPreventionEnabled(bool enabled)
    {
        edgeFallPreventionEnabled = enabled;
        lastMovementBlockedByEdge = false;
    }

    public void SuspendSafetyRecovery()
    {
        safetyRecoverySuspended = true;
    }

    public void ResumeSafetyRecovery(bool updateSafePosition = false)
    {
        safetyRecoverySuspended = false;
        if (updateSafePosition)
        {
            SetLastSafePosition(transform.position);
        }
    }

    public void SetLastSafePosition(Vector3 position)
    {
        lastSafePosition = position;
        hasLastSafePosition = true;
        stableWalkableSeconds = 0f;
    }

    public void ResetLastSafePosition()
    {
        hasLastSafePosition = false;
        stableWalkableSeconds = 0f;
    }

    public void TeleportTo(Vector3 destination, bool updateSafePosition = true)
    {
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        transform.position = destination;

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        verticalVelocity = 0f;
        lastMovementBlockedByEdge = false;
        movementAudio?.ResetMovementState();
        if (updateSafePosition)
        {
            SetLastSafePosition(destination);
        }
    }

    /// <summary>復帰した向きと視点操作の内部角度を同期し、次フレームの巻き戻りを防ぐ。</summary>
    public void SetLookRotation(Quaternion rotation)
    {
        Vector3 angles = rotation.eulerAngles;
        rotationX = Mathf.DeltaAngle(0f, angles.x);
        rotationY = angles.y;
        transform.rotation = rotation;
    }

    /// <summary>
    /// 現在の重力とInspector設定のJump Heightから、1回のジャンプ初速を求める。
    /// </summary>
    public static float CalculateJumpVelocity(float gravity, float jumpHeight)
    {
        return Mathf.Sqrt(2f * Mathf.Max(0f, gravity) * Mathf.Max(0f, jumpHeight));
    }

    public static bool CanStartJump(bool isCharacterControllerEnabled, bool isGrounded, bool isWalkable)
    {
        return isCharacterControllerEnabled && isGrounded && isWalkable;
    }

    public static bool IsWalkableSurfaceNormal(Vector3 normal, float maximumSlopeDegrees)
    {
        float minimumUpDot = Mathf.Cos(Mathf.Clamp(maximumSlopeDegrees, 0f, 89f) * Mathf.Deg2Rad);
        return Vector3.Dot(normal.normalized, Vector3.up) >= minimumUpDot;
    }

    private void UpdateLookRotation()
    {
        if (!enableMouseLook)
        {
            return;
        }

        Vector2 look = Nekolpos.System.SettingsManager.LookInput(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"), mouseSensitivity);
        rotationX = Mathf.Clamp(rotationX - look.y, -80f, 80f);
        rotationY += look.x;
        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }

    private Vector2 ReadMovementInput()
    {
        return usesExternalMovementInput
            ? externalMovementInput
            : Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
    }

    private void MovePlayer(Vector2 input)
    {
        if (characterController == null || !characterController.enabled)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 horizontalMove = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f) * moveSpeed;
        lastMovementBlockedByEdge = horizontalMove.sqrMagnitude > 0f && !CanMoveOntoWalkableSurface(horizontalMove.normalized, horizontalMove.magnitude * Time.deltaTime);
        if (lastMovementBlockedByEdge)
        {
            horizontalMove = Vector3.zero;
        }

        verticalVelocity = CalculateVerticalVelocity(
            characterController.isGrounded,
            verticalVelocity,
            gravity,
            groundedDownwardSpeed,
            maxFallSpeed,
            Time.deltaTime);
        CollisionFlags collisionFlags = characterController.Move((horizontalMove + Vector3.up * verticalVelocity) * Time.deltaTime);
        if ((collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }
    }

    private static float CalculateVerticalVelocity(
        bool isGrounded,
        float currentVerticalVelocity,
        float gravity,
        float groundedDownwardSpeed,
        float maxFallSpeed,
        float deltaTime)
    {
        if (isGrounded && currentVerticalVelocity <= 0f)
        {
            return -groundedDownwardSpeed;
        }

        return Mathf.Max(
            currentVerticalVelocity - gravity * deltaTime,
            -Mathf.Max(0f, maxFallSpeed));
    }

    private bool CanMoveOntoWalkableSurface(Vector3 direction, float movementDistance)
    {
        if (!edgeFallPreventionEnabled || direction.sqrMagnitude <= 0f)
        {
            return true;
        }

        float lookAhead = Mathf.Max(movementDistance + edgeLookAheadDistance, characterController.radius);
        Vector3 destinationProbePosition = transform.position + direction * lookAhead;
        bool hasNormalWalkableDestination = TryFindWalkableGround(destinationProbePosition, groundProbeDistance, out _);
        if (hasNormalWalkableDestination)
        {
            return true;
        }

        bool hasControlledStepDownLanding = TryFindControlledStepDownLanding(destinationProbePosition);
        if (hasControlledStepDownLanding)
        {
            return true;
        }

        // During ascent, accept only a nearby Walkable landing surface that the small jump can reach.
        // A missing landing surface still blocks movement, preserving edge-fall prevention.
        bool hasJumpReachableLanding = verticalVelocity > 0f &&
                                       TryFindWalkableGround(
                                           destinationProbePosition + Vector3.up * Mathf.Max(0f, jumpHeight),
                                           Mathf.Max(0f, jumpHeight) + maximumSafeStepDown,
                                           out _);
        return IsWalkableMoveAllowed(
            hasNormalWalkableDestination,
            hasControlledStepDownLanding,
            verticalVelocity,
            hasJumpReachableLanding);
    }

    public static bool IsWalkableMoveAllowed(bool hasNormalWalkableDestination, float currentVerticalVelocity, bool hasJumpReachableLanding)
    {
        return hasNormalWalkableDestination || (currentVerticalVelocity > 0f && hasJumpReachableLanding);
    }

    public static bool IsWalkableMoveAllowed(
        bool hasNormalWalkableDestination,
        bool hasControlledStepDownLanding,
        float currentVerticalVelocity,
        bool hasJumpReachableLanding)
    {
        return hasNormalWalkableDestination ||
               hasControlledStepDownLanding ||
               (currentVerticalVelocity > 0f && hasJumpReachableLanding);
    }

    public static bool IsControlledStepDownAllowed(
        bool hasWalkableLanding,
        float dropDistance,
        float maximumControlledStepDown)
    {
        return hasWalkableLanding &&
               dropDistance > 0f &&
               dropDistance <= Mathf.Max(0f, maximumControlledStepDown);
    }

    private void RefreshWalkableState()
    {
        isOnWalkableSurface = TryFindWalkableGround(transform.position, groundProbeDistance, out RaycastHit hit);
        currentWalkableCollider = isOnWalkableSurface ? hit.collider : null;
    }

    private bool TryFindControlledStepDownLanding(Vector3 destinationProbePosition)
    {
        if (!TryFindWalkableGround(destinationProbePosition, maximumSafeStepDown, out RaycastHit landingHit))
        {
            return false;
        }

        float currentFootHeight = characterController.bounds.min.y;
        float dropDistance = currentFootHeight - landingHit.point.y;
        return IsControlledStepDownAllowed(true, dropDistance, maximumSafeStepDown);
    }

    private void UpdateLastSafePosition()
    {
        if (!ShouldUpdateLastSafePosition(safetyRecoverySuspended, isOnWalkableSurface, verticalVelocity))
        {
            stableWalkableSeconds = 0f;
            return;
        }

        stableWalkableSeconds += Time.deltaTime;
        if (stableWalkableSeconds >= safePositionStabilitySeconds)
        {
            SetLastSafePosition(transform.position);
        }
    }

    public static bool ShouldUpdateLastSafePosition(bool safetyRecoveryIsSuspended, bool isWalkable, float currentVerticalVelocity)
    {
        return !safetyRecoveryIsSuspended && isWalkable && Mathf.Abs(currentVerticalVelocity) <= 0.5f;
    }

    private void TryStartJump()
    {
        if (WalkDialogueController.HandledInputThisFrame || !Input.GetKeyDown(KeyCode.Space) || !CanJump)
        {
            return;
        }

        verticalVelocity = CalculateJumpVelocity(gravity, jumpHeight);
        if (verticalVelocity > 0f) movementAudio?.NotifyJumpStarted();
    }

    private void RecoverFromUnsafeFallIfNeeded()
    {
        if (safetyRecoverySuspended || !hasLastSafePosition)
        {
            return;
        }

        bool hasFallenFarBelowSafePosition = lastSafePosition.y - transform.position.y >= recoveryFallDistance;
        if (hasFallenFarBelowSafePosition || transform.position.y <= recoveryWorldY)
        {
            TeleportTo(lastSafePosition, updateSafePosition: false);
        }
    }

    private bool TryFindWalkableGround(Vector3 position, float maximumDropDistance, out RaycastHit hit)
    {
        hit = default;
        if (walkableLayers.value == 0 || characterController == null)
        {
            return false;
        }

        Bounds bounds = characterController.bounds;
        Vector3 origin = new Vector3(position.x, bounds.min.y + groundProbeStartHeight, position.z);
        float radius = Mathf.Min(groundProbeRadius, Mathf.Max(0.001f, characterController.radius * 0.8f));
        float castDistance = groundProbeStartHeight + Mathf.Max(maximumDropDistance, 0.001f);
        return Physics.SphereCast(origin, radius, Vector3.down, out hit, castDistance, walkableLayers, QueryTriggerInteraction.Ignore)
               && IsWalkableSurfaceNormal(hit.normal, maximumWalkableSlope);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || characterController == null)
        {
            return;
        }

        Gizmos.color = isOnWalkableSurface ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, groundProbeRadius);
        if (hasLastSafePosition)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lastSafePosition, groundProbeRadius * 1.5f);
            Gizmos.DrawLine(transform.position, lastSafePosition);
        }
    }

    private void Reset()
    {
        characterController = GetComponent<CharacterController>();
        characterController.height = 0.05f;
        characterController.radius = 0.01f;
        characterController.center = new Vector3(0f, 0.025f, 0f);
        characterController.stepOffset = 0.01f;
        characterController.skinWidth = 0.001f;
        characterController.minMoveDistance = 0f;
    }
}
