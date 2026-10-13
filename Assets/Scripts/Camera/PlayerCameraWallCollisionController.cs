using UnityEngine;

/// <summary>
/// 自由歩行用の一人称カメラが、指定された家の壁へ近づき過ぎないよう表示位置だけを補正する。
/// Player本体、通常会話カメラ、OPカメラのTransformには触れない。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCameraWallCollisionController : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("散歩中に有効になるPlayer Cameraです。無効中は補正せず、元のローカル位置へ戻します。")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("今回の補正対象にする家の壁Rootです。この配下のColliderだけを使うため、家具・猫・Triggerは対象になりません。")]
    [SerializeField] private Transform wallRoot;
    [Tooltip("壁と同じカメラ補正を行う追加対象です。ドア等を個別に指定し、家具全体を対象にしません。")]
    [SerializeField] private Transform[] additionalWallRoots = System.Array.Empty<Transform>();

    [Header("Wall Clearance")]
    [Tooltip("壁を検出するカメラ周辺の半径です。Near Clip Planeとは別に、壁へ入り込む前の余白を作ります。")]
    [SerializeField, Min(0.0001f)] private float probeRadius = 0.003f;
    [Tooltip("SphereCastがこの距離以内で壁を検出した時、カメラを手前へ押し戻します。Near Clip Planeより少し大きく設定します。")]
    [SerializeField, Min(0.0001f)] private float wallClearance = 0.016f;
    [Tooltip("壁へ近づいた時の補正追従時間です。小さいほど即座に壁から離れます。")]
    [SerializeField, Min(0f)] private float collisionSmoothTime = 0.015f;
    [Tooltip("壁から離れた時に通常のカメラ位置へ戻る追従時間です。")]
    [SerializeField, Min(0f)] private float returnSmoothTime = 0.06f;

    private readonly RaycastHit[] castHits = new RaycastHit[8];
    private readonly Vector3[] probeDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.right,
        Vector3.left,
        new Vector3(1f, 0f, 1f).normalized,
        new Vector3(1f, 0f, -1f).normalized,
        new Vector3(-1f, 0f, 1f).normalized,
        new Vector3(-1f, 0f, -1f).normalized
    };

    private Vector3 defaultLocalPosition;
    private Vector3 localPositionVelocity;
    private bool hasDefaultLocalPosition;
    private Collider[] wallColliders = System.Array.Empty<Collider>();

    private void Awake()
    {
        playerCamera ??= GetComponent<Camera>();
        CaptureDefaultLocalPosition();
        RebuildWallColliders();
    }

    private void OnEnable()
    {
        CaptureDefaultLocalPosition();
        RebuildWallColliders();
    }

    private void OnDisable()
    {
        RestoreDefaultPositionImmediately();
    }

    private void LateUpdate()
    {
        if (!hasDefaultLocalPosition)
        {
            CaptureDefaultLocalPosition();
        }

        if (playerCamera == null || !playerCamera.enabled || wallColliders.Length == 0)
        {
            RestoreDefaultPositionImmediately();
            return;
        }

        Vector3 desiredWorldPosition = transform.parent != null
            ? transform.parent.TransformPoint(defaultLocalPosition)
            : defaultLocalPosition;
        Vector3 worldCorrection = CalculateWorldCorrection(desiredWorldPosition);
        Vector3 targetWorldPosition = desiredWorldPosition + worldCorrection;
        Vector3 targetLocalPosition = transform.parent != null
            ? transform.parent.InverseTransformPoint(targetWorldPosition)
            : targetWorldPosition;
        float smoothTime = worldCorrection.sqrMagnitude > 0f ? collisionSmoothTime : returnSmoothTime;
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            targetLocalPosition,
            ref localPositionVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
    }

    /// <summary>
    /// SphereCastのヒット距離から、壁へ残す安全距離を満たすために必要な補正量を返す。
    /// </summary>
    public static float CalculateRequiredPushDistance(float hitDistance, float clearance)
    {
        return Mathf.Max(0f, clearance - hitDistance);
    }

    private Vector3 CalculateWorldCorrection(Vector3 desiredWorldPosition)
    {
        Vector3 correction = Vector3.zero;
        float strongestPush = 0f;

        foreach (Vector3 direction in probeDirections)
        {
            int hitCount = Physics.SphereCastNonAlloc(
                desiredWorldPosition,
                probeRadius,
                direction,
                castHits,
                wallClearance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = castHits[index];
                if (!IsWallCollider(hit.collider))
                {
                    continue;
                }

                float pushDistance = CalculateRequiredPushDistance(hit.distance, wallClearance);
                if (pushDistance <= strongestPush)
                {
                    continue;
                }

                strongestPush = pushDistance;
                correction = hit.normal.sqrMagnitude > 0f
                    ? hit.normal.normalized * pushDistance
                    : -direction * pushDistance;
            }
        }

        return correction;
    }

    private bool IsWallCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        for (int index = 0; index < wallColliders.Length; index++)
        {
            if (wallColliders[index] == collider)
            {
                return true;
            }
        }

        return false;
    }

    private void CaptureDefaultLocalPosition()
    {
        defaultLocalPosition = transform.localPosition;
        localPositionVelocity = Vector3.zero;
        hasDefaultLocalPosition = true;
    }

    private void RebuildWallColliders()
    {
        var colliders = new System.Collections.Generic.List<Collider>();
        if (wallRoot != null) colliders.AddRange(wallRoot.GetComponentsInChildren<Collider>(includeInactive: true));
        if (additionalWallRoots != null)
            foreach (Transform root in additionalWallRoots)
                if (root != null) colliders.AddRange(root.GetComponentsInChildren<Collider>(includeInactive: true));
        wallColliders = colliders.ToArray();
    }

    private void RestoreDefaultPositionImmediately()
    {
        if (!hasDefaultLocalPosition)
        {
            return;
        }

        transform.localPosition = defaultLocalPosition;
        localPositionVelocity = Vector3.zero;
    }
}
