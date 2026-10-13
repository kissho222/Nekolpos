using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Nekolpos.System
{
    /// <summary>
    /// WalkMode中だけNormal_CatのNavMesh追従と待機を所有する。
    /// 通常会話拠点はCatPositionController、演出中の姿勢はTimeline側へ所有権を返す。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class CatLocomotionController : MonoBehaviour
    {
        [Serializable]
        public struct AreaAnchorBinding
        {
            [Tooltip("PlayerAreaTrackerと一致させるArea Idです。比較時は大文字小文字を区別しません。")]
            [SerializeField] private string areaId;
            [Tooltip("Area間移動後にネルコを配置する、NavMesh上またはその近傍の地点です。")]
            [SerializeField] private Transform spawnAnchor;

            public AreaAnchorBinding(string areaId, Transform spawnAnchor)
            {
                this.areaId = areaId;
                this.spawnAnchor = spawnAnchor;
            }

            public string AreaId => areaId;
            public Transform SpawnAnchor => spawnAnchor;
        }

        [Header("Required References")]
        [SerializeField] private NavMeshAgent navMeshAgent;
        [SerializeField] private Animator animator;
        [Tooltip("散歩状態の正本です。Player上のControllerを明示参照します。")]
        [SerializeField] private PlayerWalkModeController walkModeController;
        [Tooltip("Playerの現在AreaとAreaChangedを参照します。")]
        [SerializeField] private PlayerAreaTracker playerAreaTracker;
        [Tooltip("追従対象のPlayerルートです。Player位置そのものではなく、停止距離を空けた候補点へ移動します。")]
        [SerializeField] private Transform playerTarget;
        [Tooltip("Idle Variationとの競合停止に使います。")]
        [SerializeField] private CatPresentationModeController presentationController;
        [Tooltip("Warp後の正式な視線再評価APIを呼ぶための参照です。")]
        [SerializeField] private NekomataLookRigController lookRig;

        [Header("Area Anchors")]
        [SerializeField] private AreaAnchorBinding[] areaAnchors = Array.Empty<AreaAnchorBinding>();
        [SerializeField, Min(0.01f)] private float warpSampleDistance = 0.35f;

        [Header("Follow Distances")]
        [SerializeField, Min(0.01f)] private float followStartDistance = 0.55f;
        [SerializeField, Min(0.01f)] private float followStopDistance = 0.45f;
        [SerializeField, Min(0.01f)] private float stoppingDistance = 0.4f;
        [SerializeField, Min(0.01f)] private float repathInterval = 0.25f;
        [SerializeField, Min(0.001f)] private float repathTargetMoveDistance = 0.08f;
        [SerializeField, Min(0.01f)] private float destinationSampleDistance = 0.3f;

        [Header("Agent Tuning")]
        [SerializeField, Min(0.01f)] private float moveSpeed = 0.35f;
        [SerializeField, Min(0.01f)] private float acceleration = 2.5f;
        [Tooltip("歩行中の急なその場Yawを避けるための上限です。近距離停止中はAgentを停止して回転を追従させません。")]
        [SerializeField, Min(1f)] private float angularSpeed = 120f;

        [Header("Animation")]
        [SerializeField] private string walkStateName = "CatSimple_Walk_F_IP";
        [SerializeField] private string idleStateName = "CatSimple_Lie_belly_loop_1";
        [SerializeField, Min(0f)] private float animationBlendSeconds = 0.15f;

        [Header("Near Player Rest")]
        [Tooltip("散歩開始時と歩行停止後の伏せ待機Stateです。")]
        [SerializeField] private string nearRestStateName = "CatSimple_Lie_belly_loop_1";
        [Tooltip("休止姿勢へ入る時に一度だけ再生する、既存Animatorの開始Stateです。")]
        [SerializeField] private string nearRestStartStateName = "CatSimple_Lie_belly_start";
        [Tooltip("休止姿勢から歩行へ戻る時に一度だけ再生する、既存Animatorの終了Stateです。")]
        [SerializeField] private string nearRestEndStateName = "CatSimple_Lie_belly_end";

        [Header("Runtime Debug")]
        [SerializeField] private float unreachableWaitSeconds;
        private enum UnreachableState { None, Waiting, SleepStarting, Sleeping, Waking }
        [SerializeField] private UnreachableState unreachableState;
        private float unreachableSince = -1f;
        private bool pathUnreachable;
        private Coroutine sleepCoroutine;
        private const float SleepAfterSeconds = 60f;
        private const string SleepStartState = "CatSimple_Lie_belly_sleep_start";
        private const string SleepLoopState = "CatSimple_Lie_belly_sleep";
        private const string SleepEndState = "CatSimple_Lie_belly_sleep_End";
        [SerializeField] private bool isLocomotionEnabled;
        [SerializeField] private bool isFollowing;
        [SerializeField] private bool isMoving;
        [SerializeField] private bool isRestingNearPlayer;
        [SerializeField] private string currentAreaId = PlayerAreaTracker.NoneAreaId;
        [SerializeField] private Vector3 currentDestination;
        [SerializeField] private string currentLocomotionAnimation = string.Empty;
        [SerializeField] private float distanceToPlayer;
        [SerializeField] private bool isOnNavMesh;
        [SerializeField] private bool hasPath;
        [SerializeField] private NavMeshPathStatus pathStatus;
        [SerializeField] private float remainingDistance;

        private NavMeshPath reusablePath;
        private float nextRepathTime;
        private Vector3 lastRepathTargetPosition;
        private bool hasLastRepathTarget;
        private bool warnedMissingNavMesh;
        private string lastNavigationAreaId = PlayerAreaTracker.NoneAreaId;
        private Coroutine restTransitionCoroutine;
        private bool isRestTransitioning;
        private const string YawnState = "CatSimple_Lie_belly_loop_3";
        private const string BackwardState = "CatSimple_Walk_B_IP";
        [SerializeField, Min(0.01f)] private float stepBackDistance = 0.25f;
        private float nextYawnAt;
        private Coroutine walkActionCoroutine;
        private bool isYawning;
        private bool isSteppingBack;
        private bool interactionPending;
        private Action pendingInteraction;
        private bool sleepLookSuppressed;
        public bool IsWalkActionBusy => isSteppingBack || interactionPending;
        public bool IsYawning => isYawning;
        public bool IsSteppingBack => isSteppingBack;

        public void InterruptStepBackForInteraction()
        {
            if (!isSteppingBack) return;
            CancelWalkAction();
            CancelNearPlayerRest();
            StopMoving(playIdle: true);
        }

        public bool IsLocomotionEnabled => isLocomotionEnabled;
        public bool IsFollowing => isFollowing;
        public bool IsSleeping => unreachableState == UnreachableState.Sleeping;
        public bool IsSleepTransitioning => unreachableState == UnreachableState.SleepStarting || unreachableState == UnreachableState.Waking;
        public float UnreachableWaitSeconds => unreachableWaitSeconds;
        public bool IsMoving => isMoving;
        public Transform CurrentTarget => isFollowing ? playerTarget : null;
        public string CurrentAreaId => currentAreaId;
        public bool HasPath => navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh && navMeshAgent.hasPath;
        public float RemainingDistance => HasPath ? navMeshAgent.remainingDistance : float.PositiveInfinity;
        public string CurrentLocomotionAnimation => currentLocomotionAnimation;
        public float DistanceToPlayer => playerTarget != null ? PlanarDistance(transform.position, playerTarget.position) : float.PositiveInfinity;
        public float FollowStartDistance => followStartDistance;
        public float FollowStopDistance => followStopDistance;
        public bool IsOnNavMesh => navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
        public NavMeshPathStatus PathStatus => HasPath ? navMeshAgent.pathStatus : NavMeshPathStatus.PathInvalid;

        private void Awake()
        {
            reusablePath = new NavMeshPath();
            ResolveSiblingReferences();
            NormalizeConfiguration();
            ConfigureAgent();
            ShutdownAgent(restoreIdle: false);
        }

        private void OnEnable()
        {
            Subscribe();
            currentAreaId = playerAreaTracker != null
                ? playerAreaTracker.CurrentAreaId
                : PlayerAreaTracker.NoneAreaId;

            SetLocomotionEnabled(walkModeController != null && walkModeController.IsWalkMode);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ShutdownAgent(restoreIdle: false);
            isLocomotionEnabled = false;
        }

        private void Update()
        {
            RefreshDebugState();
            if (!isLocomotionEnabled)
            {
                return;
            }

            if (walkModeController == null || !walkModeController.IsWalkMode)
            {
                SetLocomotionEnabled(false);
                return;
            }

            currentAreaId = playerAreaTracker != null
                ? playerAreaTracker.CurrentAreaId
                : PlayerAreaTracker.NoneAreaId;

            if (!CanOwnTransform())
            {
                CancelWalkAction();
                ResetUnreachableWait(cancelSleep: true);
                CancelNearPlayerRest();
                StopMoving(playIdle: false);
                return;
            }

            if (presentationController != null) presentationController.SetLocomotionActive(true);

            if (isYawning)
            {
                // あくびは待機の一部。再生中も睡眠までの時間を進める。
                if (unreachableState == UnreachableState.Waiting && !interactionPending && !walkModeController.IsTabletInteractionActive)
                    unreachableWaitSeconds += Time.deltaTime;
                return;
            }
            if (isSteppingBack) return;

            // Sleep owns the resting pose until an interaction or higher-priority event interrupts it.
            if (IsSleeping || IsSleepTransitioning) return;

            if (walkModeController.IsTabletInteractionActive || walkModeController.IsInteractionMenuOpen)
            {
                ScheduleNextYawn();
                ResetUnreachableWait(cancelSleep: true);
                // 閲覧開始地点で待ち、閉じた後に現在のFollow/Wait指定を再開する。
                StopMoving(playIdle: true);
                return;
            }

            if (!isFollowing || playerTarget == null)
            {
                StopMoving(playIdle: true);
                UpdateRestSleep();
                return;
            }

            if (!TryEnsureAgentOnNavMesh())
            {
                HoldUnreachableRest();
                return;
            }

            float distance = PlanarDistance(transform.position, playerTarget.position);
            bool shouldMove = EvaluateShouldMove(isMoving, distance, followStartDistance, followStopDistance);
            if (!shouldMove)
            {
                StopMoving(playIdle: true);
                UpdateRestSleep();
                return;
            }

            // A valid path must exist before leaving the lying pose. Retry at the existing interval.
            if (ShouldRepath())
            {
                pathUnreachable = !TrySetPathToPlayer();
                nextRepathTime = Time.time + repathInterval;
            }
            if (pathUnreachable)
            {
                HoldUnreachableRest();
                return;
            }
            ResetUnreachableWait(cancelSleep: false);

            if (isRestTransitioning)
            {
                return;
            }

            if (isRestingNearPlayer)
            {
                BeginRestExit(resumeFollow: true);
                return;
            }

            SetMovingPresentation(true);
            ScheduleNextYawn();
        }

        private void HoldUnreachableRest()
        {
            if (unreachableSince < 0f) unreachableSince = Time.time;
            if (Time.time - unreachableSince < repathInterval)
            {
                // Brief path failures stop translation without immediately changing posture.
                if (IsOnNavMesh)
                {
                    navMeshAgent.isStopped = true;
                    navMeshAgent.ResetPath();
                    navMeshAgent.velocity = Vector3.zero;
                }
                return;
            }
            StopMoving(playIdle: true);
            UpdateRestSleep();
        }

        private void UpdateRestSleep()
        {
            if (isRestTransitioning) return;
            if (unreachableState != UnreachableState.Waiting)
            {
                unreachableState = UnreachableState.Waiting;
                unreachableWaitSeconds = 0f;
                return;
            }
            unreachableWaitSeconds += Time.deltaTime;
            if (unreachableWaitSeconds < SleepAfterSeconds && TryBeginYawn()) return;
            if (unreachableWaitSeconds < SleepAfterSeconds) return;
            if (!HasAnimatorState(SleepStartState) || !HasAnimatorState(SleepLoopState) || !HasAnimatorState(SleepEndState))
            {
                WarnOnce("睡眠Start/Loop/EndのAnimator Stateが不足しています。");
                return;
            }
            unreachableState = UnreachableState.SleepStarting;
            sleepLookSuppressed = true;
            lookRig?.FadeExternalWeight(0f, 0.15f);
            PlayAnimatorState(SleepStartState);
            sleepCoroutine = StartCoroutine(CompleteSleepStart());
        }

        private bool HasAnimatorState(string name) => animator != null &&
            (animator.HasState(0, Animator.StringToHash(name)) || animator.HasState(0, Animator.StringToHash("Base Layer." + name)));

        private IEnumerator CompleteSleepStart()
        {
            yield return WaitForAnimatorStateToComplete(SleepStartState, overlapEnd: false);
            sleepCoroutine = null;
            if (!CanOwnTransform()) { ResetUnreachableWait(cancelSleep: true); yield break; }
            if (!AnimatorStateCompleted(SleepStartState))
            {
                ResetUnreachableWait(cancelSleep: true);
                PlayAnimatorState(nearRestStateName);
                WarnOnce("睡眠Startの完了を確認できないため伏せ待機へ戻します。");
                yield break;
            }
            unreachableState = UnreachableState.Sleeping;
            PlayAnimatorState(SleepLoopState);
        }

        public bool TryWakeForInteraction(Action completed)
        {
            if (!IsSleeping || !CanOwnTransform() || walkModeController == null ||
                !walkModeController.TrySetInteractionMenuOpen(true)) return false;
            unreachableState = UnreachableState.Waking;
            unreachableWaitSeconds = 0f;
            PlayAnimatorState(SleepEndState);
            sleepCoroutine = StartCoroutine(CompleteWake(completed));
            return true;
        }

        private IEnumerator CompleteWake(Action completed)
        {
            yield return WaitForAnimatorStateToComplete(SleepEndState, overlapEnd: false);
            sleepCoroutine = null;
            if (!CanOwnTransform()) { ResetUnreachableWait(cancelSleep: true); yield break; }
            if (!AnimatorStateCompleted(SleepEndState))
            {
                ResetUnreachableWait(cancelSleep: true);
                PlayAnimatorState(nearRestStateName);
                WarnOnce("起床Endの完了を確認できないため会話を開始しません。");
                yield break;
            }
            ResetUnreachableWait(cancelSleep: false);
            isRestingNearPlayer = true;
            PlayAnimatorState(nearRestStateName);
            // The existing conversation UI starts only after the wake clip completes.
            completed?.Invoke();
            var dialogue = walkModeController != null ? walkModeController.GetComponent<WalkDialogueController>() : null;
            if (walkModeController != null && (dialogue == null || !dialogue.IsDialogueBusy))
                walkModeController.TrySetInteractionMenuOpen(false);
        }

        private void ResetUnreachableWait(bool cancelSleep)
        {
            unreachableWaitSeconds = 0f;
            unreachableSince = -1f;
            pathUnreachable = false;
            if (!cancelSleep && (IsSleeping || IsSleepTransitioning))
            {
                if (unreachableState != UnreachableState.Waking) return;
            }
            if (cancelSleep && sleepCoroutine != null) { StopCoroutine(sleepCoroutine); sleepCoroutine = null; }
            if (cancelSleep && unreachableState == UnreachableState.Waking && walkModeController != null)
                walkModeController.TrySetInteractionMenuOpen(false);
            unreachableState = UnreachableState.None;
            if (sleepLookSuppressed)
            {
                sleepLookSuppressed = false;
                lookRig?.ResetExternalWeight();
            }
        }

        private void ScheduleNextYawn() => nextYawnAt = Time.time + UnityEngine.Random.Range(40f, 50f);

        private bool TryBeginYawn()
        {
            if (nextYawnAt <= 0f) ScheduleNextYawn();
            if (Time.time < nextYawnAt || isRestTransitioning || !isRestingNearPlayer || !HasAnimatorState(YawnState)) return false;
            isYawning = true;
            lookRig?.SetTemporaryMaxDownPitchDegrees(65f);
            PlayAnimatorState(YawnState);
            walkActionCoroutine = StartCoroutine(CompleteYawn());
            return true;
        }

        public bool TryDeferInteractionUntilYawnEnds(Action completed)
        {
            if (!isYawning || interactionPending || walkModeController == null ||
                !walkModeController.TrySetInteractionMenuOpen(true)) return false;
            interactionPending = true;
            pendingInteraction = completed;
            return true;
        }

        private IEnumerator CompleteYawn()
        {
            yield return WaitForAnimatorStateToComplete(YawnState, overlapEnd: false);
            bool completed = AnimatorStateCompleted(YawnState);
            Action interaction = pendingInteraction;
            pendingInteraction = null;
            interactionPending = false;
            isYawning = false;
            walkActionCoroutine = null;
            lookRig?.ClearTemporaryMaxDownPitchDegrees();
            ScheduleNextYawn();
            if (!CanOwnTransform()) { ReleasePendingInteraction(); yield break; }
            PlayAnimatorState(nearRestStateName);
            if (!completed)
            {
                ReleasePendingInteraction();
                WarnOnce("あくびの完了を確認できないため会話を開始しません。");
                yield break;
            }
            interaction?.Invoke();
            if (interaction != null) ReleasePendingInteraction();
        }

        private void ReleasePendingInteraction()
        {
            var dialogue = walkModeController != null ? walkModeController.GetComponent<WalkDialogueController>() : null;
            if (walkModeController != null && (dialogue == null || !dialogue.IsDialogueBusy))
                walkModeController.TrySetInteractionMenuOpen(false);
        }

        public bool TryStepBack()
        {
            if (!isLocomotionEnabled || !CanOwnTransform() || !IsOnNavMesh || IsSleeping || IsSleepTransitioning ||
                isYawning || isSteppingBack || !HasAnimatorState(nearRestEndStateName) || !HasAnimatorState(BackwardState)) return false;
            ResetUnreachableWait(cancelSleep: true);
            CancelNearPlayerRest();
            StopMoving(playIdle: false);
            if (presentationController != null) presentationController.SetLocomotionActive(true);
            isSteppingBack = true;
            walkActionCoroutine = StartCoroutine(StepBack());
            return true;
        }

        private IEnumerator StepBack()
        {
            PlayAnimatorState(nearRestEndStateName);
            yield return WaitForAnimatorStateToComplete(nearRestEndStateName, overlapEnd: false);
            if (!AnimatorStateCompleted(nearRestEndStateName))
            {
                WarnOnce("起き上がりの完了を確認できないため後退を中止します。");
                FinishStepBack();
                yield break;
            }
            PlayAnimatorState(BackwardState);
            Vector3 goal = FindStepBackDestination();
            // NavMeshの高さは段差に沿って変わる。水平到達と実移動の進捗を別々に判定する。
            float deadline = Time.time + Mathf.Max(2f, stepBackDistance / Mathf.Max(0.01f, moveSpeed) * 3f);
            float lastProgressAt = Time.time;
            Vector3 progressPosition = transform.position;
            while (IsOnNavMesh && PlanarDistance(transform.position, goal) > 0.002f && Time.time < deadline)
            {
                if (walkModeController.IsInteractionMenuOpen || walkModeController.IsTabletInteractionActive)
                { FinishStepBack(); yield break; }
                Vector3 target = Vector3.MoveTowards(transform.position, goal, moveSpeed * Time.deltaTime);
                bool blocked = navMeshAgent.Raycast(target, out NavMeshHit hit);
                Vector3 destination = blocked ? hit.position : target;
                navMeshAgent.Move(destination - transform.position);
                if (blocked) break;
                yield return null;
                if (PlanarDistance(progressPosition, transform.position) >= 0.002f)
                {
                    progressPosition = transform.position;
                    lastProgressAt = Time.time;
                }
                else if (Time.time - lastProgressAt >= 0.5f) break;
            }
            BeginRestEntry();
            yield return new WaitForSeconds(3f);
            FinishStepBack();
        }

        private Vector3 FindStepBackDestination()
        {
            Vector3 origin = transform.position;
            Vector3 backward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
            Vector3 best = origin;
            float bestDistance = 0f;
            // Prefer straight back, then search both sides of the rear half-plane before accepting a short move.
            foreach (float angle in new[] { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f })
            {
                Vector3 target = origin + Quaternion.AngleAxis(angle, Vector3.up) * backward * stepBackDistance;
                bool blocked = navMeshAgent.Raycast(target, out NavMeshHit hit);
                Vector3 candidate = blocked ? hit.position : target;
                float distance = Vector3.Distance(origin, candidate);
                if (!blocked && TrySamplePosition(candidate, 0.1f, out Vector3 surfacePoint) &&
                    PlanarDistance(candidate, surfacePoint) < 0.01f && !navMeshAgent.Raycast(surfacePoint, out _))
                    return surfacePoint;
                if (distance > bestDistance)
                {
                    best = Vector3.MoveTowards(candidate, origin, 0.01f);
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void FinishStepBack()
        {
            isSteppingBack = false;
            walkActionCoroutine = null;
            hasLastRepathTarget = false;
            nextRepathTime = 0f;
            ScheduleNextYawn();
            if (CanOwnTransform()) StopMoving(playIdle: true);
        }

        private void CancelWalkAction()
        {
            if (walkActionCoroutine != null) StopCoroutine(walkActionCoroutine);
            walkActionCoroutine = null;
            bool releaseInteraction = interactionPending;
            if (isYawning) lookRig?.ClearTemporaryMaxDownPitchDegrees();
            pendingInteraction = null;
            interactionPending = false;
            isYawning = false;
            isSteppingBack = false;
            if (releaseInteraction) ReleasePendingInteraction();
            ScheduleNextYawn();
        }

        public void SetFollowEnabled(bool enabled)
        {
            if (enabled)
            {
                FollowPlayer();
            }
            else
            {
                WaitHere();
            }
        }

        public void FollowPlayer()
        {
            isFollowing = true;
            hasLastRepathTarget = false;
            nextRepathTime = 0f;
        }

        public void WaitHere()
        {
            if (!IsSleeping && !IsSleepTransitioning) ResetUnreachableWait(cancelSleep: true);
            isFollowing = false;
            StopMoving(playIdle: CanOwnTransform());
        }

        public void SetLocomotionEnabled(bool enabled)
        {
            if (isLocomotionEnabled == enabled)
            {
                return;
            }

            if (!enabled)
            {
                isLocomotionEnabled = false;
                isFollowing = false;
                CancelNearPlayerRest();
                ShutdownAgent(restoreIdle: CanOwnTransform());
                return;
            }

            ResolveSiblingReferences();
            NormalizeConfiguration();
            ConfigureAgent();
            currentAreaId = playerAreaTracker != null
                ? playerAreaTracker.CurrentAreaId
                : PlayerAreaTracker.NoneAreaId;

            if (!TryResolveInitialNavMeshPosition(out Vector3 initialPosition))
            {
                WarnOnce("散歩開始位置または現在Area Anchorの近くに有効な猫用NavMeshがありません。");
                return;
            }

            lastNavigationAreaId = currentAreaId;
            transform.position = initialPosition;
            navMeshAgent.enabled = true;
            if (!navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.enabled = false;
                WarnOnce("NavMeshAgentを猫用NavMeshへ配置できませんでした。");
                return;
            }

            warnedMissingNavMesh = false;
            isLocomotionEnabled = true;
            FollowPlayer();
            StopMoving(playIdle: true);
        }

        public bool WarpToArea(string areaId)
        {
            if (!isLocomotionEnabled || navMeshAgent == null)
            {
                return false;
            }

            if (!TryResolveAreaAnchor(areaAnchors, areaId, out Transform anchor))
            {
                Debug.LogWarning($"[{nameof(CatLocomotionController)}] Area '{areaId}' の猫用Anchorが未登録です。", this);
                return false;
            }

            if (!TrySamplePosition(anchor.position, warpSampleDistance, out Vector3 sampledPosition))
            {
                Debug.LogWarning($"[{nameof(CatLocomotionController)}] Area '{areaId}' のAnchor近くに猫用NavMeshがありません。", this);
                return false;
            }

            if (!navMeshAgent.enabled)
            {
                transform.position = sampledPosition;
                navMeshAgent.enabled = true;
            }

            if (!navMeshAgent.isOnNavMesh || !navMeshAgent.Warp(sampledPosition))
            {
                Debug.LogWarning($"[{nameof(CatLocomotionController)}] Area '{areaId}' へのNavMesh Warpに失敗しました。", this);
                return false;
            }

            CancelWalkAction();
            ResetUnreachableWait(cancelSleep: true);
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
            navMeshAgent.isStopped = true;
            currentAreaId = areaId?.Trim() ?? PlayerAreaTracker.NoneAreaId;
            lastNavigationAreaId = currentAreaId;
            currentDestination = sampledPosition;
            CancelNearPlayerRest();
            hasLastRepathTarget = false;
            nextRepathTime = 0f;
            SetMovingPresentation(false);
            lookRig?.ForceRuntimeRefresh(resetConstraintWeights: true, rebuildRigGraph: false);
            return true;
        }

        public static bool EvaluateShouldMove(bool wasMoving, float distance, float startDistance, float stopDistance)
        {
            float safeStop = Mathf.Max(0f, stopDistance);
            float safeStart = Mathf.Max(safeStop + 0.001f, startDistance);
            return wasMoving ? distance > safeStop : distance >= safeStart;
        }

        public static float GetTransitionBlendStart(float stateLength, float playbackSpeed, float blendSeconds)
        {
            return stateLength > 0f
                ? Mathf.Clamp01(1f - Mathf.Max(0f, blendSeconds) * Mathf.Abs(playbackSpeed) / stateLength)
                : 1f;
        }

        public static bool TryResolveAreaAnchor(
            AreaAnchorBinding[] bindings,
            string areaId,
            out Transform anchor)
        {
            anchor = null;
            if (bindings == null || string.IsNullOrWhiteSpace(areaId))
            {
                return false;
            }

            string normalizedAreaId = areaId.Trim();
            for (int index = 0; index < bindings.Length; index++)
            {
                if (bindings[index].SpawnAnchor != null &&
                    string.Equals(bindings[index].AreaId?.Trim(), normalizedAreaId, StringComparison.OrdinalIgnoreCase))
                {
                    anchor = bindings[index].SpawnAnchor;
                    return true;
                }
            }

            return false;
        }

        private void HandleWalkModeChanged(bool walkModeEnabled)
        {
            SetLocomotionEnabled(walkModeEnabled);
        }

        private void HandleTabletInteractionChanged(bool active)
        {
            if (active && isLocomotionEnabled)
                StopMoving(playIdle: CanOwnTransform());
        }

        private void HandleAreaChanged(string previousAreaId, string nextAreaId)
        {
            currentAreaId = nextAreaId;
            if (!TryResolveAreaAnchor(areaAnchors, nextAreaId, out _)) return;
            bool changedNavigationArea = TryResolveAreaAnchor(areaAnchors, lastNavigationAreaId, out _) &&
                !string.Equals(lastNavigationAreaId, nextAreaId, StringComparison.OrdinalIgnoreCase);
            lastNavigationAreaId = nextAreaId;
            // Noneを挟んでも、同じ床への再入場はArea間移動ではない。
            if (!changedNavigationArea) return;
            if (!isLocomotionEnabled || !isFollowing || IsSleeping || IsSleepTransitioning || isYawning || isSteppingBack || !CanOwnTransform())
            {
                return;
            }

            if (!CanReachPlayer())
            {
                WarpToArea(nextAreaId);
            }
            else
            {
                hasLastRepathTarget = false;
                nextRepathTime = 0f;
            }
        }

        private bool ShouldRepath()
        {
            if (Time.time < nextRepathTime)
            {
                return false;
            }

            if (!hasLastRepathTarget || !navMeshAgent.hasPath)
            {
                return true;
            }

            float minimumTargetMoveSqr = repathTargetMoveDistance * repathTargetMoveDistance;
            return (playerTarget.position - lastRepathTargetPosition).sqrMagnitude >= minimumTargetMoveSqr;
        }

        private bool TrySetPathToPlayer()
        {
            if (!TryBuildCompletePathToPlayer(out Vector3 destination))
            {
                return false;
            }

            if (!navMeshAgent.SetPath(reusablePath))
            {
                return false;
            }

            navMeshAgent.isStopped = isRestingNearPlayer || isRestTransitioning;
            currentDestination = destination;
            lastRepathTargetPosition = playerTarget.position;
            hasLastRepathTarget = true;
            nextRepathTime = Time.time + repathInterval;
            return true;
        }

        private bool CanReachPlayer()
        {
            return TryBuildCompletePathToPlayer(out _);
        }

        private bool TryBuildCompletePathToPlayer(out Vector3 destination)
        {
            destination = default;
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh || playerTarget == null)
            {
                return false;
            }

            Vector3 awayFromPlayer = transform.position - playerTarget.position;
            awayFromPlayer.y = 0f;
            if (awayFromPlayer.sqrMagnitude < 0.0001f)
            {
                awayFromPlayer = -playerTarget.forward;
                awayFromPlayer.y = 0f;
            }

            Vector3 approachPoint = playerTarget.position + awayFromPlayer.normalized * stoppingDistance;
            if (!TrySamplePosition(approachPoint, destinationSampleDistance, out destination))
            {
                return false;
            }

            reusablePath ??= new NavMeshPath();
            reusablePath.ClearCorners();
            return navMeshAgent.CalculatePath(destination, reusablePath) &&
                   reusablePath.status == NavMeshPathStatus.PathComplete;
        }

        private bool TryEnsureAgentOnNavMesh()
        {
            if (navMeshAgent == null)
            {
                return false;
            }

            if (navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                return true;
            }

            // 通常追従の復旧でArea初期位置へ戻すと、近傍のNavMesh欠落が遠距離Warpになる。
            if (!TrySamplePosition(transform.position, warpSampleDistance, out Vector3 position))
            {
                WarnOnce("ネルコの現在位置近くに猫用NavMeshがないため、その場で追従を停止します。");
                return false;
            }

            navMeshAgent.enabled = false;
            transform.position = position;
            navMeshAgent.enabled = true;
            bool resolved = navMeshAgent.isOnNavMesh;
            if (!resolved)
            {
                navMeshAgent.enabled = false;
                WarnOnce("NavMeshAgentを猫用NavMeshへ再配置できませんでした。");
            }

            return resolved;
        }

        private bool TryResolveInitialNavMeshPosition(out Vector3 position)
        {
            if (TrySamplePosition(transform.position, warpSampleDistance, out position))
            {
                return true;
            }

            return TryResolveAreaAnchor(areaAnchors, currentAreaId, out Transform anchor) &&
                   TrySamplePosition(anchor.position, warpSampleDistance, out position);
        }

        private bool TrySamplePosition(Vector3 source, float maxDistance, out Vector3 position)
        {
            position = default;
            int agentTypeId = navMeshAgent != null ? navMeshAgent.agentTypeID : 0;
            int areaMask = navMeshAgent != null ? navMeshAgent.areaMask : NavMesh.AllAreas;
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = agentTypeId,
                areaMask = areaMask
            };

            if (!NavMesh.SamplePosition(source, out NavMeshHit hit, Mathf.Max(0.01f, maxDistance), filter))
            {
                return false;
            }

            position = hit.position;
            return true;
        }

        private void SetMovingPresentation(bool moving)
        {
            if (isRestTransitioning)
            {
                return;
            }

            if (!moving && isRestingNearPlayer)
            {
                return;
            }

            if (!moving && isMoving && isLocomotionEnabled)
            {
                isMoving = false;
                BeginRestEntry();
                return;
            }

            if (moving && isRestingNearPlayer)
            {
                BeginRestExit(resumeFollow: true);
                return;
            }

            if (isMoving && moving &&
                string.Equals(currentLocomotionAnimation, walkStateName, StringComparison.Ordinal))
            {
                return;
            }

            isMoving = moving;
            if (presentationController != null) presentationController.SetLocomotionActive(isLocomotionEnabled);
            PlayAnimatorState(moving ? walkStateName : idleStateName);
            // 散歩開始時の待機も伏せ。移動前に必ず起き上がりを通す。
            isRestingNearPlayer = !moving && isLocomotionEnabled;
        }

        private void BeginRestEntry()
        {
            if (isRestingNearPlayer || isRestTransitioning)
            {
                return;
            }

            isRestingNearPlayer = true;
            isRestTransitioning = true;
            PlayAnimatorState(nearRestStartStateName);
            restTransitionCoroutine = StartCoroutine(CompleteRestEntryAfterStart());
        }

        private void BeginRestExit(bool resumeFollow)
        {
            if (!isRestingNearPlayer)
            {
                return;
            }

            if (restTransitionCoroutine != null)
            {
                StopCoroutine(restTransitionCoroutine);
                restTransitionCoroutine = null;
            }

            isRestingNearPlayer = false;
            isRestTransitioning = true;
            isMoving = false;
            if (IsOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }
            PlayAnimatorState(nearRestEndStateName);
            restTransitionCoroutine = StartCoroutine(CompleteRestExitAfterEnd(resumeFollow));
        }

        private IEnumerator CompleteRestEntryAfterStart()
        {
            yield return WaitForAnimatorStateToComplete(nearRestStartStateName);
            restTransitionCoroutine = null;
            isRestTransitioning = false;

            if (isRestingNearPlayer && CanOwnTransform())
            {
                PlayAnimatorState(nearRestStateName);
            }
        }

        private IEnumerator CompleteRestExitAfterEnd(bool resumeFollow)
        {
            yield return WaitForAnimatorStateToComplete(nearRestEndStateName);
            restTransitionCoroutine = null;
            isRestTransitioning = false;

            if (resumeFollow && isFollowing && playerTarget != null &&
                !pathUnreachable && DistanceToPlayer >= followStartDistance && CanOwnTransform())
            {
                hasLastRepathTarget = false;
                nextRepathTime = 0f;
                PlayAnimatorState(walkStateName);
            }
            else if (CanOwnTransform())
            {
                BeginRestEntry();
            }
        }

        private IEnumerator WaitForAnimatorStateToComplete(string stateName, bool overlapEnd = true)
        {
            int stateHash = Animator.StringToHash(stateName);
            int fullStateHash = Animator.StringToHash($"Base Layer.{stateName}");
            float deadline = Time.time + 5f;
            yield return null;

            while (animator != null && Time.time < deadline)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool isRequestedState = state.shortNameHash == stateHash || state.fullPathHash == fullStateHash;
                // 最後のBlend秒数を次のStateと重ね、非ループClipの終端で静止させない。
                float blendStart = overlapEnd ? GetTransitionBlendStart(
                    state.length, state.speed * state.speedMultiplier, animationBlendSeconds) : 1f;
                if (isRequestedState && !animator.IsInTransition(0) && state.normalizedTime >= blendStart)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private bool AnimatorStateCompleted(string name)
        {
            if (animator == null || animator.IsInTransition(0)) return false;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            return (state.shortNameHash == Animator.StringToHash(name) ||
                state.fullPathHash == Animator.StringToHash("Base Layer." + name)) && state.normalizedTime >= 1f;
        }

        private void CancelNearPlayerRest()
        {
            if (restTransitionCoroutine != null)
            {
                StopCoroutine(restTransitionCoroutine);
                restTransitionCoroutine = null;
            }

            isRestingNearPlayer = false;
            isRestTransitioning = false;
        }

        private void StopMoving(bool playIdle)
        {
            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
                navMeshAgent.velocity = Vector3.zero;
            }

            hasLastRepathTarget = false;
            if (playIdle)
            {
                SetMovingPresentation(false);
            }
            else
            {
                isMoving = false;
                if (presentationController != null) presentationController.SetLocomotionActive(false);
            }
        }

        private void PlayAnimatorState(string stateName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName) || !CanOwnTransform())
            {
                return;
            }

            int shortHash = Animator.StringToHash(stateName.Trim());
            int fullHash = Animator.StringToHash($"Base Layer.{stateName.Trim()}");
            int stateHash;
            if (animator.HasState(0, shortHash))
            {
                stateHash = shortHash;
            }
            else if (animator.HasState(0, fullHash))
            {
                stateHash = fullHash;
            }
            else
            {
                Debug.LogWarning($"[{nameof(CatLocomotionController)}] Animator State '{stateName}' がlayer 0にありません。", this);
                return;
            }

            animator.enabled = true;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            animator.CrossFadeInFixedTime(stateHash, animationBlendSeconds, 0, 0f);
            currentLocomotionAnimation = stateName.Trim();
        }

        private bool CanOwnTransform()
        {
            if (presentationController == null)
            {
                return true;
            }

            if (presentationController.MotionController != null && presentationController.MotionController.IsPlayingMotion)
                return false;

            return presentationController.CurrentMode == CatPresentationMode.Conversation ||
                   presentationController.CurrentMode == CatPresentationMode.Idle;
        }

        private void ShutdownAgent(bool restoreIdle)
        {
            CancelWalkAction();
            ResetUnreachableWait(cancelSleep: true);
            CancelNearPlayerRest();
            StopMoving(playIdle: restoreIdle);
            if (presentationController != null) presentationController.SetLocomotionActive(false);
            if (navMeshAgent != null)
            {
                navMeshAgent.enabled = false;
            }
        }

        private void ConfigureAgent()
        {
            if (navMeshAgent == null)
            {
                return;
            }

            navMeshAgent.speed = moveSpeed;
            navMeshAgent.acceleration = acceleration;
            navMeshAgent.angularSpeed = angularSpeed;
            // 目的地は既にPlayerの手前。Agentへ同じ距離を再指定すると
            // Start付近でも止まり、停止距離が二重に加算されてしまう。
            navMeshAgent.stoppingDistance = Mathf.Max(0f, followStopDistance - stoppingDistance);
            navMeshAgent.autoBraking = true;
            navMeshAgent.autoRepath = false;
            navMeshAgent.updatePosition = true;
            navMeshAgent.updateRotation = true;
            navMeshAgent.updateUpAxis = true;
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }

        private void Subscribe()
        {
            if (walkModeController != null)
            {
                walkModeController.WalkModeChanged -= HandleWalkModeChanged;
                walkModeController.WalkModeChanged += HandleWalkModeChanged;
                walkModeController.TabletInteractionChanged += HandleTabletInteractionChanged;
            }

            if (playerAreaTracker != null)
            {
                playerAreaTracker.AreaChanged -= HandleAreaChanged;
                playerAreaTracker.AreaChanged += HandleAreaChanged;
            }
        }

        private void Unsubscribe()
        {
            if (walkModeController != null)
            {
                walkModeController.WalkModeChanged -= HandleWalkModeChanged;
                walkModeController.TabletInteractionChanged -= HandleTabletInteractionChanged;
            }

            if (playerAreaTracker != null)
            {
                playerAreaTracker.AreaChanged -= HandleAreaChanged;
            }
        }

        private void ResolveSiblingReferences()
        {
            navMeshAgent ??= GetComponent<NavMeshAgent>();
            animator ??= GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            lookRig ??= GetComponentInChildren<NekomataLookRigController>(true);
        }

        private void NormalizeConfiguration()
        {
            followStopDistance = Mathf.Max(0.01f, followStopDistance);
            followStartDistance = Mathf.Max(followStopDistance + 0.01f, followStartDistance);
            stoppingDistance = Mathf.Clamp(stoppingDistance, 0.01f, followStopDistance);
            repathInterval = Mathf.Max(0.01f, repathInterval);
            repathTargetMoveDistance = Mathf.Max(0.001f, repathTargetMoveDistance);
        }

        private void WarnOnce(string message)
        {
            if (warnedMissingNavMesh)
            {
                return;
            }

            warnedMissingNavMesh = true;
            Debug.LogWarning($"[{nameof(CatLocomotionController)}] {message}", this);
        }

        private static float PlanarDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private void RefreshDebugState()
        {
            distanceToPlayer = DistanceToPlayer;
            isOnNavMesh = IsOnNavMesh;
            hasPath = HasPath;
            pathStatus = PathStatus;
            remainingDistance = RemainingDistance;
        }

        private void OnValidate()
        {
            ResolveSiblingReferences();
            NormalizeConfiguration();
            ConfigureAgent();
        }

        private void OnDrawGizmosSelected()
        {
            Transform target = playerTarget;
            if (target != null)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.9f);
                Gizmos.DrawWireSphere(target.position, followStartDistance);
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireSphere(target.position, followStopDistance);
            }

            if (isLocomotionEnabled)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, currentDestination);
                Gizmos.DrawWireSphere(currentDestination, 0.03f);
            }
        }
    }
}
