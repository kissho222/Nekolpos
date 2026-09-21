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
    [SerializeField, Min(0f)] private float groundedDownwardSpeed = 0.1f;

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
    [SerializeField, Min(0.001f)] private float maximumSafeStepDown = 0.02f;

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

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        rotationX = transform.eulerAngles.x;
        rotationY = transform.eulerAngles.y;
    }

    private void Update()
    {
        UpdateLookRotation();
        RefreshWalkableState();
        RecoverFromUnsafeFallIfNeeded();
        MovePlayer(ReadMovementInput());
        RefreshWalkableState();
        UpdateLastSafePosition();
    }

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
        if (updateSafePosition)
        {
            SetLastSafePosition(destination);
        }
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

        rotationX = Mathf.Clamp(rotationX - Input.GetAxis("Mouse Y") * mouseSensitivity, -80f, 80f);
        rotationY += Input.GetAxis("Mouse X") * mouseSensitivity;
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

        verticalVelocity = characterController.isGrounded && verticalVelocity <= 0f
            ? -groundedDownwardSpeed
            : verticalVelocity - gravity * Time.deltaTime;
        characterController.Move((horizontalMove + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    private bool CanMoveOntoWalkableSurface(Vector3 direction, float movementDistance)
    {
        if (!edgeFallPreventionEnabled || direction.sqrMagnitude <= 0f)
        {
            return true;
        }

        float lookAhead = Mathf.Max(movementDistance + edgeLookAheadDistance, characterController.radius);
        float allowedStepDown = Mathf.Max(maximumSafeStepDown, characterController.stepOffset);
        return TryFindWalkableGround(transform.position + direction * lookAhead, allowedStepDown, out _);
    }

    private void RefreshWalkableState()
    {
        isOnWalkableSurface = TryFindWalkableGround(transform.position, groundProbeDistance, out _);
    }

    private void UpdateLastSafePosition()
    {
        if (safetyRecoverySuspended || !isOnWalkableSurface || Mathf.Abs(verticalVelocity) > 0.5f)
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
