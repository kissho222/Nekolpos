using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーの現在地として扱うScene上の領域を定義する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class PlayerAreaVolume : MonoBehaviour
{
    private static readonly List<PlayerAreaVolume> activeVolumes = new List<PlayerAreaVolume>();

    [Header("Area Definition")]
    [Tooltip("外部連携に使用するエリアID。Noneは未所属の予約値なので設定しない。")]
    [SerializeField] private string areaId;
    [Tooltip("重複時は大きい値を優先する。")]
    [SerializeField] private int priority;
    [Tooltip("Priorityが同じ場合は大きい値を優先する。")]
    [SerializeField] private int resolveOrder;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.75f, 1f, 0.35f);

    private Collider areaCollider;

    public string AreaId => areaId?.Trim() ?? string.Empty;
    public int Priority => priority;
    public int ResolveOrder => resolveOrder;
    public bool IsConfigured => !string.IsNullOrEmpty(AreaId);
    internal static IReadOnlyList<PlayerAreaVolume> GetActiveVolumes()
    {
        activeVolumes.RemoveAll(volume => volume == null || !volume.isActiveAndEnabled);

        // 実行中はOnEnable登録だけを使う。EditModeの明示再評価ではOnEnableが実行されないため、
        // 登録がまだない場合だけScene上の有効Volumeを補完する。
        if (activeVolumes.Count == 0)
        {
            PlayerAreaVolume[] discoveredVolumes = Resources.FindObjectsOfTypeAll<PlayerAreaVolume>();
            foreach (PlayerAreaVolume volume in discoveredVolumes)
            {
                if (volume != null
                    && volume.isActiveAndEnabled
                    && volume.gameObject.scene.IsValid()
                    && volume.gameObject.scene.isLoaded)
                {
                    activeVolumes.Add(volume);
                }
            }
        }

        return activeVolumes;
    }

    public bool Contains(Vector3 worldPosition)
    {
        EnsureCollider();
        if (!isActiveAndEnabled || areaCollider == null || !areaCollider.enabled)
        {
            return false;
        }

        Vector3 closestPoint = areaCollider.ClosestPoint(worldPosition);
        return (closestPoint - worldPosition).sqrMagnitude <= 0.00000001f;
    }

    private void Awake()
    {
        EnsureCollider();
    }

    private void OnEnable()
    {
        EnsureCollider();
        if (!activeVolumes.Contains(this))
        {
            activeVolumes.Add(this);
        }
    }

    private void OnDisable()
    {
        activeVolumes.Remove(this);
    }

    private void OnValidate()
    {
        areaId = areaId?.Trim() ?? string.Empty;
        EnsureCollider();
        if (areaCollider != null)
        {
            areaCollider.isTrigger = true;
        }
    }

    private void Reset()
    {
        EnsureCollider();
        if (areaCollider != null)
        {
            areaCollider.isTrigger = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        EnsureCollider();
        if (areaCollider == null)
        {
            return;
        }

        Bounds bounds = areaCollider.bounds;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    private void EnsureCollider()
    {
        if (areaCollider == null)
        {
            areaCollider = GetComponent<Collider>();
        }
    }
}
