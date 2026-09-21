using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlayerAreaVolumeからプレイヤーの現在地エリアを決定し、外部参照用のAPIを提供する。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAreaTracker : MonoBehaviour
{
    public const string NoneAreaId = "None";

    [Header("Detection")]
    [Tooltip("CharacterControllerがあれば、その中心をエリア判定に使う。")]
    [SerializeField] private CharacterController characterController;

    [Header("Runtime Debug")]
    [SerializeField] private string currentAreaId = NoneAreaId;
    [SerializeField] private PlayerAreaVolume currentArea;
    [SerializeField] private List<PlayerAreaVolume> currentCandidateAreas = new List<PlayerAreaVolume>();

    public event Action<string, string> AreaChanged;

    public string CurrentAreaId => currentAreaId;
    public PlayerAreaVolume CurrentArea => currentArea;
    public IReadOnlyList<PlayerAreaVolume> CurrentCandidateAreas => currentCandidateAreas;
    public bool HasCurrentArea => currentArea != null;

    private void Awake()
    {
        EnsureCharacterController();
    }

    private void OnEnable()
    {
        RefreshAreaNow();
    }

    private void LateUpdate()
    {
        RefreshAreaNow();
    }

    public bool IsInArea(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId) || currentArea == null)
        {
            return false;
        }

        return string.Equals(currentAreaId, areaId.Trim(), StringComparison.Ordinal);
    }

    public void RefreshAreaNow()
    {
        EnsureCharacterController();
        currentCandidateAreas.Clear();

        Vector3 probePosition = characterController != null
            ? characterController.bounds.center
            : transform.position;

        IReadOnlyList<PlayerAreaVolume> activeVolumes = PlayerAreaVolume.GetActiveVolumes();
        for (int index = 0; index < activeVolumes.Count; index++)
        {
            PlayerAreaVolume volume = activeVolumes[index];
            if (volume != null && volume.IsConfigured && volume.Contains(probePosition))
            {
                currentCandidateAreas.Add(volume);
            }
        }

        currentCandidateAreas.Sort(CompareVolumes);
        SetCurrentArea(currentCandidateAreas.Count > 0 ? currentCandidateAreas[0] : null);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerAreaVolume>() != null)
        {
            RefreshAreaNow();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerAreaVolume>() != null)
        {
            RefreshAreaNow();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || currentArea == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, currentArea.transform.position);
    }

    private void Reset()
    {
        EnsureCharacterController();
    }

    private void EnsureCharacterController()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    private void SetCurrentArea(PlayerAreaVolume nextArea)
    {
        string nextAreaId = nextArea != null ? nextArea.AreaId : NoneAreaId;
        string previousAreaId = currentAreaId;
        currentArea = nextArea;
        currentAreaId = nextAreaId;

        if (!string.Equals(previousAreaId, nextAreaId, StringComparison.Ordinal))
        {
            AreaChanged?.Invoke(previousAreaId, nextAreaId);
        }
    }

    private static int CompareVolumes(PlayerAreaVolume left, PlayerAreaVolume right)
    {
        int priorityComparison = right.Priority.CompareTo(left.Priority);
        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        int resolveOrderComparison = right.ResolveOrder.CompareTo(left.ResolveOrder);
        if (resolveOrderComparison != 0)
        {
            return resolveOrderComparison;
        }

        int areaIdComparison = string.CompareOrdinal(left.AreaId, right.AreaId);
        if (areaIdComparison != 0)
        {
            return areaIdComparison;
        }

        return left.GetInstanceID().CompareTo(right.GetInstanceID());
    }
}
