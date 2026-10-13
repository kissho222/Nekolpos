using UnityEngine;

/// <summary>骨追従の身体Colliderから散歩中のPlayer本体とカメラを保護する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(11000)]
public sealed class PlayerCatBodyCollisionController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("侵入防止用だけを登録します。会話・撫で・点灯用Triggerは含めません。")]
    [SerializeField] private Collider[] bodyColliders = System.Array.Empty<Collider>();

    [Header("Clearance")]
    [Tooltip("単純Colliderと曲面メッシュの隙間を補うカメラ用余白。近接描画面の保護半径に加算します。")]
    [SerializeField, Min(0f)] private float cameraSurfaceAllowance = 0.02f;
    [SerializeField, Min(0.0001f)] private float separationMargin = 0.001f;
    [SerializeField, Range(1, 24)] private int maximumIterations = 12;

    private const int HistoryCapacity = 64;
    private readonly Vector3[] clearPositions = new Vector3[HistoryCapacity];
    private readonly Collider[] worldOverlaps = new Collider[64];
    private CharacterController characterController;
    private PlayerSafetyMovementController safetyMovement;
    private CapsuleCollider bodyProbe;
    private SphereCollider cameraProbe;
    private GameObject probeObject;
    private int historyCount;
    private int historyIndex;
    private bool wasActive;
    private bool reportedFailure;
    private Vector3 lastResolvedPosition;

    public bool HasUnresolvedOverlap { get; private set; }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        safetyMovement = GetComponent<PlayerSafetyMovementController>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
    }

    private void LateUpdate() => ResolveBodyOverlap();

    private void OnDisable()
    {
        wasActive = false;
        historyCount = 0;
        HasUnresolvedOverlap = false;
    }

    private void OnDestroy()
    {
        if (probeObject == null) return;
        if (Application.isPlaying) Destroy(probeObject);
        else DestroyImmediate(probeObject);
    }

    /// <summary>近接描画面の四隅を含む保護球の半径。アスペクト比・FOV変更にも追従する。</summary>
    public static float CalculateCameraRadius(Camera camera)
    {
        float halfHeight = camera.orthographic
            ? camera.orthographicSize
            : camera.nearClipPlane * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * camera.aspect;
        return Mathf.Sqrt(camera.nearClipPlane * camera.nearClipPlane + halfHeight * halfHeight + halfWidth * halfWidth);
    }

    /// <summary>通常のCharacterController移動後・骨の最終姿勢確定後に呼ぶ。</summary>
    public bool ResolveBodyOverlap()
    {
        characterController ??= GetComponent<CharacterController>();
        bool active = isActiveAndEnabled && characterController != null && characterController.enabled &&
                      playerCamera != null && playerCamera.isActiveAndEnabled && bodyColliders.Length > 0;
        if (!active)
        {
            wasActive = false;
            historyCount = 0;
            HasUnresolvedOverlap = false;
            return true;
        }

        // 正規の場所移動や散歩再開を、古い位置への復帰で取り消さない。
        if (!wasActive || (transform.position - lastResolvedPosition).sqrMagnitude > 0.25f)
        {
            historyCount = 0;
            historyIndex = 0;
        }
        wasActive = true;
        EnsureProbes();
        bodyProbe.radius = characterController.radius;
        bodyProbe.height = characterController.height;
        bodyProbe.center = Vector3.zero;
        cameraProbe.radius = CalculateCameraRadius(playerCamera) + cameraSurfaceAllowance + separationMargin;

        // Animator/Rigによる骨移動をCharacterController.Moveの物理クエリへ反映する。
        Physics.SyncTransforms();
        for (int iteration = 0; iteration < maximumIterations; iteration++)
        {
            Vector3 correction = FindStrongestCorrection(transform.position);
            if (correction.sqrMagnitude == 0f) break;
            characterController.Move(correction);
        }

        HasUnresolvedOverlap = FindStrongestCorrection(transform.position).sqrMagnitude > 0f;
        if (HasUnresolvedOverlap)
        {
            // 壁・床に挟まれた時は、現時点でも空いている検証済みの位置へ戻す。
            for (int offset = 1; offset <= historyCount; offset++)
            {
                Vector3 candidate = clearPositions[(historyIndex - offset + HistoryCapacity) % HistoryCapacity];
                if (TryRestoreClearPosition(candidate)) break;
            }
            if (HasUnresolvedOverlap && safetyMovement != null && safetyMovement.HasLastSafePosition)
                TryRestoreClearPosition(safetyMovement.LastSafePosition);
        }

        if (!HasUnresolvedOverlap)
        {
            clearPositions[historyIndex] = transform.position;
            historyIndex = (historyIndex + 1) % HistoryCapacity;
            historyCount = Mathf.Min(historyCount + 1, HistoryCapacity);
            reportedFailure = false;
        }
        else if (!reportedFailure)
        {
            Debug.LogWarning("ネルコとの重なりを解消できません。侵入防止Colliderと散歩開始位置・安全復帰位置を確認してください。", this);
            reportedFailure = true;
        }
        lastResolvedPosition = transform.position;
        return !HasUnresolvedOverlap;
    }

    private void EnsureProbes()
    {
        if (probeObject != null) return;
        // Unity 6000.3では無効ColliderのComputePenetrationがfalseになるため、
        // 全Layerを除外したTriggerを形状クエリ専用に使う。
        probeObject = new GameObject("PlayerCatBodyCollisionProbes") { hideFlags = HideFlags.HideAndDontSave };
        probeObject.transform.SetParent(transform, false);
        probeObject.layer = bodyColliders[0] != null ? bodyColliders[0].gameObject.layer : gameObject.layer;
        bodyProbe = probeObject.AddComponent<CapsuleCollider>();
        cameraProbe = probeObject.AddComponent<SphereCollider>();
        bodyProbe.isTrigger = true;
        cameraProbe.isTrigger = true;
        bodyProbe.excludeLayers = ~0;
        cameraProbe.excludeLayers = ~0;
    }

    private Vector3 FindStrongestCorrection(Vector3 rootPosition)
    {
        Vector3 strongest = Vector3.zero;
        float greatestDepth = 0f;
        Vector3 cameraPosition = playerCamera.transform.position + rootPosition - transform.position;
        // CharacterControllerの中心は回転するが、カプセル軸は視点のPitchに関係なく鉛直。
        Vector3 bodyPosition = rootPosition + transform.TransformVector(characterController.center);
        foreach (Collider body in bodyColliders)
        {
            if (body == null || !body.enabled || body.isTrigger || !body.gameObject.activeInHierarchy) continue;
            AccumulateCorrection(bodyProbe, bodyPosition, Quaternion.identity, body, ref strongest, ref greatestDepth);
            AccumulateCorrection(cameraProbe, cameraPosition, Quaternion.identity, body, ref strongest, ref greatestDepth);
        }
        return strongest;
    }

    private void AccumulateCorrection(Collider probe, Vector3 position, Quaternion rotation, Collider obstacle,
        ref Vector3 strongest, ref float greatestDepth)
    {
        if (Physics.ComputePenetration(probe, position, rotation, obstacle,
            obstacle.transform.position, obstacle.transform.rotation, out Vector3 direction, out float depth) && depth > greatestDepth)
        {
            greatestDepth = depth;
            strongest = direction * (depth + separationMargin);
        }
    }

    private bool TryRestoreClearPosition(Vector3 candidate)
    {
        if (FindStrongestCorrection(candidate).sqrMagnitude > 0f || !IsWorldClear(candidate)) return false;
        if (safetyMovement != null) safetyMovement.TeleportTo(candidate, updateSafePosition: false);
        else
        {
            characterController.enabled = false;
            transform.position = candidate;
            characterController.enabled = true;
        }
        HasUnresolvedOverlap = false;
        return true;
    }

    private bool IsWorldClear(Vector3 rootPosition)
    {
        // 床面の通常のSkin接触を許容しながら、復帰先の壁・家具への埋まりを拒否する。
        Vector3 center = rootPosition + transform.TransformVector(characterController.center);
        float radius = Mathf.Max(0.0001f, characterController.radius - characterController.skinWidth);
        float halfSegment = Mathf.Max(0f, characterController.height * 0.5f - characterController.radius);
        Vector3 axis = Vector3.up * halfSegment;
        int count = Physics.OverlapCapsuleNonAlloc(center - axis, center + axis, radius, worldOverlaps,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == worldOverlaps.Length) return false;
        for (int index = 0; index < count; index++)
        {
            Collider obstacle = worldOverlaps[index];
            if (obstacle.transform.IsChildOf(transform) || Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer)) continue;
            return false;
        }
        Vector3 cameraPosition = playerCamera.transform.position + rootPosition - transform.position;
        count = Physics.OverlapSphereNonAlloc(cameraPosition, cameraProbe.radius, worldOverlaps,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == worldOverlaps.Length) return false;
        for (int index = 0; index < count; index++)
        {
            Collider obstacle = worldOverlaps[index];
            if (obstacle.transform.IsChildOf(transform) || Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer)) continue;
            return false;
        }
        return true;
    }
}
