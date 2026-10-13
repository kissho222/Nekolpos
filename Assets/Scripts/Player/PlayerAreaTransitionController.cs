using System;
using Cysharp.Threading.Tasks;
using Nekolpos.ActionSystem;
using UnityEngine;

/// <summary>
/// 意図的なエリア間移動で、Playerの安全移動とArea判定を一時停止・復元しながら再配置する。
/// フェードを所有する遷移と、既存フェードの完全暗転中に呼ぶ即時移動の両方を提供する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerSafetyMovementController))]
[RequireComponent(typeof(PlayerAreaTracker))]
public sealed class PlayerAreaTransitionController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private PlayerSafetyMovementController playerSafetyMovementController;
    [SerializeField] private PlayerAreaTracker playerAreaTracker;
    [SerializeField] private FadeController fadeController;

    private bool isTransitioning;
    private int transitionVersion;

    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        ResolveRequiredReferences();
    }

    /// <summary>
    /// 既存演出が完全暗転を所有している場合に、暗転中でPlayerを安全に移動する。
    /// </summary>
    public bool TeleportImmediately(Transform destination, string expectedAreaId = null)
    {
        return destination != null && TeleportImmediately(destination.position, expectedAreaId);
    }

    /// <summary>
    /// 既存演出が完全暗転を所有している場合に、暗転中でPlayerを安全に移動する。
    /// </summary>
    public bool TeleportImmediately(Vector3 destination, string expectedAreaId = null)
    {
        if (!TryBeginTransition(out TransitionState previousState))
        {
            return false;
        }

        try
        {
            TeleportAndRefreshArea(destination, expectedAreaId);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
        finally
        {
            RestoreTransitionState(previousState);
        }
    }

    /// <summary>
    /// このControllerが暗転を所有するエリア移動を実行する。完全暗転の確認後にだけPlayerを移動する。
    /// </summary>
    public async UniTask<bool> TransitionToAsync(Transform destination, string expectedAreaId = null)
    {
        if (destination == null)
        {
            Debug.LogWarning("[PlayerAreaTransition] 移動先Transformが未指定のため、遷移を開始しません。", this);
            return false;
        }

        return await TransitionToAsync(destination.position, expectedAreaId);
    }

    /// <summary>
    /// このControllerが暗転を所有するエリア移動を実行する。完全暗転の確認後にだけPlayerを移動する。
    /// </summary>
    public async UniTask<bool> TransitionToAsync(Vector3 destination, string expectedAreaId = null)
    {
        if (!TryBeginTransition(out TransitionState previousState))
        {
            return false;
        }

        int requestVersion = transitionVersion;
        bool fadeReachedOpaque = false;
        try
        {
            if (fadeController == null)
            {
                Debug.LogError("[PlayerAreaTransition] FadeControllerが未設定のため、フェード付き遷移を開始しません。", this);
                return false;
            }

            fadeController.BeginLocationTransition();
            await fadeController.FadeOutAsync();
            if (requestVersion != transitionVersion || this == null) return false;
            if (!fadeController.IsOpaque)
            {
                Debug.LogWarning("[PlayerAreaTransition] 暗転完了を確認できなかったため、Playerを移動しません。", this);
                return false;
            }

            fadeReachedOpaque = true;
            TeleportAndRefreshArea(destination, expectedAreaId);
            await fadeController.FadeInAsync();
            return requestVersion == transitionVersion;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
        finally
        {
            if (requestVersion == transitionVersion && fadeReachedOpaque && fadeController != null && fadeController.IsOpaque)
            {
                fadeController.FadeInAsync().Forget();
            }

            if (requestVersion == transitionVersion && this != null) RestoreTransitionState(previousState);
        }
    }

    public void ResetForTitleReload()
    {
        transitionVersion++;
        isTransitioning = false;
        playerSafetyMovementController?.ResumeSafetyRecovery();
        if (playerSafetyMovementController != null) playerSafetyMovementController.enabled = false;
    }

    private void OnDisable() => ResetForTitleReload();

    private bool TryBeginTransition(out TransitionState previousState)
    {
        previousState = default;
        if (isTransitioning)
        {
            Debug.LogWarning("[PlayerAreaTransition] すでに遷移中のため、重複した移動要求を無視します。", this);
            return false;
        }

        if (!ResolveRequiredReferences())
        {
            Debug.LogError("[PlayerAreaTransition] PlayerSafetyMovementControllerまたはPlayerAreaTrackerが未設定のため、遷移を開始しません。", this);
            return false;
        }

        isTransitioning = true;
        previousState = new TransitionState(
            playerSafetyMovementController.enabled,
            playerSafetyMovementController.SafetyRecoverySuspended);

        if (!previousState.SafetyRecoveryWasSuspended)
        {
            playerSafetyMovementController.SuspendSafetyRecovery();
        }

        playerSafetyMovementController.enabled = false;
        return true;
    }

    private void TeleportAndRefreshArea(Vector3 destination, string expectedAreaId)
    {
        playerSafetyMovementController.TeleportTo(destination, updateSafePosition: true);
        playerAreaTracker.RefreshAreaNow();

        if (!string.IsNullOrWhiteSpace(expectedAreaId) &&
            !string.Equals(playerAreaTracker.CurrentAreaId, expectedAreaId.Trim(), StringComparison.Ordinal))
        {
            Debug.LogWarning(
                $"[PlayerAreaTransition] 期待Area '{expectedAreaId.Trim()}' と実際のArea '{playerAreaTracker.CurrentAreaId}' が一致しません。",
                this);
        }
    }

    private void RestoreTransitionState(TransitionState previousState)
    {
        if (playerSafetyMovementController != null)
        {
            if (!previousState.SafetyRecoveryWasSuspended)
            {
                playerSafetyMovementController.ResumeSafetyRecovery();
            }

            playerSafetyMovementController.enabled = previousState.MovementControllerWasEnabled;
        }

        isTransitioning = false;
    }

    private bool ResolveRequiredReferences()
    {
        playerSafetyMovementController ??= GetComponent<PlayerSafetyMovementController>();
        playerAreaTracker ??= GetComponent<PlayerAreaTracker>();
        return playerSafetyMovementController != null && playerAreaTracker != null;
    }

    private void Reset()
    {
        ResolveRequiredReferences();
    }

    private readonly struct TransitionState
    {
        public TransitionState(bool movementControllerWasEnabled, bool safetyRecoveryWasSuspended)
        {
            MovementControllerWasEnabled = movementControllerWasEnabled;
            SafetyRecoveryWasSuspended = safetyRecoveryWasSuspended;
        }

        public bool MovementControllerWasEnabled { get; }
        public bool SafetyRecoveryWasSuspended { get; }
    }
}
