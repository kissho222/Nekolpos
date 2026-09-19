using System.Collections;
using Nekolpos.StatusSystem;
using UnityEngine;
using UnityEngine.Animations.Rigging;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#endif

namespace Nekolpos.System
{
    public enum NekomataLookMode
    {
        Idle,
        Conversation,
        Event
    }

    [ExecuteAlways]
    [DefaultExecutionOrder(10000)]
    public class NekomataLookRigController : MonoBehaviour
    {
        [Header("Editor Preview")]
        [Tooltip("オンにすると、再生していない時だけ視線をプレビュー対象へ向けます。プレイモード中の視線制御には影響しません。")]
        [SerializeField] private bool enableEditorPreview;
        [Tooltip("編集モードで注視する対象です。未設定で下の自動取得がオンなら MainCamera を使います。")]
        [SerializeField] private Transform editorPreviewTarget;
        [Tooltip("Editor Preview Target が未設定の時、MainCamera を編集モードの注視対象として使います。")]
        [SerializeField] private bool useMainCameraForEditorPreview = true;
        [Tooltip("編集モードのプレビューで目の視線も適用します。オフの場合は頭だけを向けます。")]
        [SerializeField] private bool previewEyesInEditor = true;
        [Tooltip("オンにすると、Timelineの編集中にもEditor視線プレビューを重ねます。オフならTimelineが頭・首・目を単独で制御するため、キー編集時はオフを推奨します。")]
        [SerializeField] private bool applyEditorPreviewDuringTimeline;

        [Header("Targets")]
        [Tooltip("視線ターゲットを管理するコンポーネント。会話・イベント時の注視先をここから取得します。")]
        [SerializeField] private LookTargetManager lookTargetManager;
        [Tooltip("MultiAimConstraint が実際に追いかける生成済みターゲット。未設定なら実行時に子オブジェクトとして作成します。")]
        [SerializeField] private Transform generatedAimTarget;
        [Tooltip("オンにすると、この猫又オブジェクト配下の LookTargetManager を優先して使用します。")]
        [SerializeField] private bool forceLocalLookTargetManager = true;
        [Tooltip("オンにすると、Hierarchy 上の共有 PlayerLookTarget_AdjustHere を視線調整ターゲットとして使います。")]
        [SerializeField] private bool useSharedScenePlayerLookTarget = true;
        [Tooltip("オンにすると、生成済み注視ターゲットがこの猫又オブジェクト配下にない場合は作り直します。")]
        [SerializeField] private bool forceLocalGeneratedAimTarget = true;
        [Tooltip("オンにすると、前方向の基準 Transform をこの猫又オブジェクト配下から探して使用します。")]
        [SerializeField] private bool forceLocalReferenceForward = true;
        [Tooltip("オンにすると、LookTargetManager の明示ターゲットよりカメラ由来のプレイヤー注視ターゲットを優先します。")]
        [SerializeField] private bool prioritizeCameraTarget = true;
        [Tooltip("カメラ由来のプレイヤー注視ターゲットを採用する最大距離です。0以下にすると距離制限なしでカメラを見ます。")]
        [SerializeField] private float maxCameraLookDistance;
        [Tooltip("プレイヤー視点を表すローカルターゲット。未設定なら LocalLookTargetManager 配下に自動作成します。")]
        [SerializeField] private PlayerLookTarget localPlayerLookTarget;

        [Header("Rigging")]
        [Tooltip("オンにすると、RigBuilder・Rig・MultiAimConstraint・骨参照を名前から自動設定します。")]
        [SerializeField] private bool autoSetupRig = true;
        [Tooltip("Animation Rigging の RigBuilder。未設定なら子オブジェクトから検索します。")]
        [SerializeField] private RigBuilder rigBuilder;
        [Tooltip("視線制御用の Rig。未設定なら子オブジェクトから検索または自動作成します。")]
        [SerializeField] private Rig rig;
        [Tooltip("頭の向きを制御する骨 Transform。未設定なら Head/head という名前から検索します。")]
        [SerializeField] private Transform headBone;
        [Tooltip("左目の向きを制御する骨 Transform。未設定なら Eye_L/LeftEye などの名前から検索します。")]
        [SerializeField] private Transform leftEyeBone;
        [Tooltip("右目の向きを制御する骨 Transform。未設定なら Eye_R/RightEye などの名前から検索します。")]
        [SerializeField] private Transform rightEyeBone;
        [Tooltip("頭用の MultiAimConstraint。生成済み注視ターゲットをソースに設定します。")]
        [SerializeField] private MultiAimConstraint headAim;
        [Tooltip("左目用の MultiAimConstraint。生成済み注視ターゲットをソースに設定します。")]
        [SerializeField] private MultiAimConstraint leftEyeAim;
        [Tooltip("右目用の MultiAimConstraint。生成済み注視ターゲットをソースに設定します。")]
        [SerializeField] private MultiAimConstraint rightEyeAim;
        [Tooltip("頭の視線追従の強さ。0で無効、1で最大です。")]
        [SerializeField] private float headWeight = 0.65f;
        [Tooltip("両目の視線追従の強さ。0で無効、1で最大です。")]
        [SerializeField] private float eyeWeight = 1f;
        [Tooltip("目だけが動いている時にも最低限動かす頭の追従量です。")]
        [SerializeField] private float minimumHeadWeightWhenEyeLookActive = 0.25f;
        [Tooltip("注視ターゲット位置の追従速度。大きいほど素早く追いかけます。")]
        [SerializeField] private float targetSmoothing = 8f;
        [Tooltip("Rig のウェイト変化速度。大きいほど視線のオン/オフが素早く切り替わります。")]
        [SerializeField] private float weightSmoothing = 8f;
        [Tooltip("外部制御による視線ウェイト倍率の変化速度。大きいほど素早く切り替わります。")]
        [SerializeField] private float externalWeightSmoothing = 6f;
        [Tooltip("頭ボーンのどのローカル軸をターゲットへ向けるかを指定します。")]
        [SerializeField] private MultiAimConstraintData.Axis headAimAxis = MultiAimConstraintData.Axis.Z_NEG;
        [Tooltip("目ボーンのどのローカル軸をターゲットへ向けるかを指定します。")]
        [SerializeField] private MultiAimConstraintData.Axis eyeAimAxis = MultiAimConstraintData.Axis.Z_NEG;
        [Tooltip("オンにすると、頭にも Eye Aim Axis と同じ軸設定を使います。")]
        [SerializeField] private bool useEyeAimAxisForHead;
        [Tooltip("Aim 回転の上方向として使う軸。Aim Axis と同じ軸系の場合は自動で互換軸に補正します。")]
        [SerializeField] private MultiAimConstraintData.Axis upAxis = MultiAimConstraintData.Axis.Y;
        [Tooltip("オンにすると MultiAimConstraint に加えて、このスクリプトが直接ボーン回転を補正します。")]
        [SerializeField] private bool useManualLookRotation = true;

        [Header("Angle Limits")]
        [Tooltip("正面方向の基準 Transform。未設定ならこのオブジェクトの forward を基準にします。")]
        [SerializeField] private Transform referenceForward;
        [Tooltip("正面基準から左右に向ける最大角度です。")]
        [SerializeField] private float maxYawDegrees = 45f;
        [Tooltip("正面基準から上下に向ける最大角度です。")]
        [SerializeField] private float maxPitchDegrees = 30f;
        [Tooltip("正面基準から下方向に向ける最大角度です。0以下なら Max Pitch Degrees と同じ値を使います。")]
        [SerializeField] private float maxDownPitchDegrees = 65f;
        [Tooltip("角度制限後の注視ターゲットを置く基準距離です。")]
        [SerializeField] private float targetDistance = 1f;

        [Header("Idle Random Look")]
        [Tooltip("Idle 中にたまに視線をそらす挙動を有効にします。")]
        [SerializeField] private bool enableIdleRandomLook;
        [Tooltip("Idle 中に視線をそらす判定を行う間隔の範囲（秒）です。")]
        [SerializeField] private Vector2 idleRandomIntervalRange = new Vector2(3f, 10f);
        [Tooltip("視線をそらし続ける時間の範囲（秒）です。")]
        [SerializeField] private Vector2 idleLookAwayDurationRange = new Vector2(0.5f, 2f);
        [Tooltip("Idle 中に視線を左右へそらす角度範囲です。")]
        [SerializeField] private Vector2 idleYawOffsetRange = new Vector2(-18f, 18f);
        [Tooltip("Idle 中に視線を上下へそらす角度範囲です。")]
        [SerializeField] private Vector2 idlePitchOffsetRange = new Vector2(-8f, 8f);
        [Tooltip("Idle のランダム視線ターゲットを作る時の基準距離です。")]
        [SerializeField] private float idleRandomTargetDistance = 0.75f;

        [Header("Emotion Influence")]
        [Tooltip("好感度・敵意・不安などの状態値を取得する StatusManager。未設定ならシーン内から検索します。")]
        [SerializeField] private StatusManager statusManager;
        [Tooltip("敵意が最大の時に、視線をそらす確率へ加算する値です。")]
        [SerializeField] private float hostilityLookAwayChanceBonus = 0.45f;
        [Tooltip("敵意が最大の時に、Idle の視線そらし判定間隔へ掛ける倍率です。小さいほど頻繁になります。")]
        [SerializeField] private float hostilityIntervalMultiplierAtMax = 0.55f;
        [Tooltip("敵意が最大の時に、通常まばたき間隔の計算へ使う倍率です。小さいほどまばたきが増えます。")]
        [SerializeField] private float hostilityBlinkMultiplierAtMax = 0.45f;
        [Tooltip("不安が最大の時に、視線をそらす継続時間へ掛ける倍率です。")]
        [SerializeField] private float concernLookAwayDurationMultiplierAtMax = 0.5f;

        [Header("Blink")]
        [Tooltip("まばたき Trigger を送る Animator。未設定なら子オブジェクトから検索します。")]
        [SerializeField] private Animator animator;
        [Tooltip("通常まばたきに使う Animator Trigger 名です。")]
        [SerializeField] private string blinkTrigger = "Blink";
        [Tooltip("ゆっくりまばたきに使う Animator Trigger 名です。")]
        [SerializeField] private string slowBlinkTrigger = "SlowBlink";
        [Tooltip("通常まばたきの発生間隔範囲（秒）です。")]
        [SerializeField] private Vector2 blinkIntervalRange = new Vector2(2.5f, 6f);
        [Tooltip("ゆっくりまばたきの判定間隔範囲（秒）です。")]
        [SerializeField] private Vector2 slowBlinkIntervalRange = new Vector2(8f, 18f);
        [Tooltip("好感度が最大の時に、ゆっくりまばたきが発生する確率です。")]
        [SerializeField] private float affectionSlowBlinkChanceAtMax = 0.65f;
        [Tooltip("オンにすると Animator Trigger でまばたきを制御します。オフの場合、このスクリプトからは Trigger を送りません。")]
        [SerializeField] private bool useAnimatorBlinkTriggers = true;

        [Header("Debug Visualization")]
        [Tooltip("オンにすると、最終的な注視ターゲット位置にデバッグ用の球を表示します。")]
        [SerializeField] private bool showDebugAimTarget;
        [Tooltip("デバッグ用注視ターゲット球の表示サイズです。")]
        [SerializeField] private Vector3 debugAimTargetScale = new Vector3(0.100000001f, 0.00999999978f, 0.100000001f);
        [Tooltip("デバッグ用注視ターゲット球の色です。")]
        [SerializeField] private Color debugAimTargetColor = new Color(1f, 0.85f, 0.05f, 0.9f);

        [Header("Debug Logging")]
        [Tooltip("オンにすると、視線ターゲットや角度制限などの状態を Console に定期出力します。")]
        [SerializeField] private bool enableDebugLogs;
        [Tooltip("デバッグログを出力する最短間隔（秒）です。")]
        [SerializeField] private float debugLogIntervalSeconds = 1f;
        [Tooltip("オンにすると、Constraint の対象や軸設定を更新した時もログ出力します。")]
        [SerializeField] private bool logConstraintUpdates = true;
        [Tooltip("Play開始・ON/OFF・Rig再構築直後に毎フレーム詳細ログを出すフレーム数です。")]
        [SerializeField] private int debugBurstFrameCount = 12;

        private NekomataLookMode mode = NekomataLookMode.Idle;
        private Vector3 smoothedLookPosition;
        private Vector3 randomLookOffset;
        private float nextRandomLookTime;
        private float randomLookEndTime;
        private float nextBlinkTime;
        private float nextSlowBlinkTime;
        private bool hasSmoothedPosition;
        private GameObject debugAimTargetMarker;
        private Renderer debugAimTargetRenderer;
        private float nextDebugLogTime;
        private Vector3 lastRawTargetPosition;
        private Vector3 lastClampedTargetPosition;
        private Vector3 lastLookOrigin;
        private Vector3 lastLocalLookDirection;
        private float lastYawBeforeClamp;
        private float lastPitchBeforeClamp;
        private float lastYawAfterClamp;
        private float lastPitchAfterClamp;
        private float externalWeightScale = 1f;
        private float targetExternalWeightScale = 1f;
        private float externalWeightFadeSpeed = 6f;
        private float? temporaryMaxDownPitchDegrees;
        private bool rigConfigurationDirty = true;
        private Coroutine delayedRigRefreshCoroutine;
        private int remainingDebugBurstFrames;
        private const string SharedLookTargetRootName = "LookTargetControls";
        private const string SharedPlayerLookTargetName = "PlayerLookTarget_AdjustHere";
        private Transform previewHeadBone;
        private Transform previewLeftEyeBone;
        private Transform previewRightEyeBone;
        private Quaternion previewHeadLocalRotation;
        private Quaternion previewLeftEyeLocalRotation;
        private Quaternion previewRightEyeLocalRotation;
        private bool hasEditorPreviewPose;
        private Quaternion lastEditorLookHeadLocalRotation;
        private Quaternion lastEditorLookLeftEyeLocalRotation;
        private Quaternion lastEditorLookRightEyeLocalRotation;
        private bool hasLastEditorLookPose;
        private bool isEvaluatingEditorPreviewRig;
        private bool isApplyingEditorPreviewFromOnEnable;
        private bool isApplyingEditorPreviewFromOnValidate;
#if UNITY_EDITOR
        private bool editorPreviewRigSuspendedForTimeline;
        private bool suspendedRigBuilderWasEnabled;
        private bool suspendedRigAnimatorWasEnabled;
#endif

        public NekomataLookMode Mode => mode;

        public string BuildShortDebugSummary()
        {
            Transform activeTarget = ResolveActiveTarget();
            return
                $"mode={mode} enabled={enabled} active={gameObject.activeInHierarchy} " +
                $"target={GetTransformPath(activeTarget)} generated={GetTransformPath(generatedAimTarget)} generatedPos={FormatVector(generatedAimTarget != null ? generatedAimTarget.position : Vector3.zero)} " +
                $"rig={(rig != null ? rig.weight.ToString("F2") : "null")} head={(headAim != null ? headAim.weight.ToString("F2") : "null")} " +
                $"leftEye={(leftEyeAim != null ? leftEyeAim.weight.ToString("F2") : "null")} rightEye={(rightEyeAim != null ? rightEyeAim.weight.ToString("F2") : "null")} " +
                $"manual={useManualLookRotation} yaw={lastYawBeforeClamp:F1}->{lastYawAfterClamp:F1} pitch={lastPitchBeforeClamp:F1}->{lastPitchAfterClamp:F1}";
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            EnsureGeneratedAimTarget();
            EnsureRigComponents();
            ConfigureRigTargets(false);
            ScheduleNextIdleRandomLook();
            ScheduleNextBlink();
            ScheduleNextSlowBlink();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                isApplyingEditorPreviewFromOnEnable = true;
                try
                {
                    ApplyEditorPreview();
                }
                finally
                {
                    isApplyingEditorPreviewFromOnEnable = false;
                }

                return;
            }

            StartDebugBurst();
            LogLifecycle("OnEnable begin");
            RefreshRigConfiguration(true, true);
            delayedRigRefreshCoroutine = StartCoroutine(RefreshRigConfigurationAfterInitialEvaluation());
            LogLifecycle("OnEnable end");
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                RestoreEditorPreviewPose();
                return;
            }

            LogLifecycle("OnDisable begin");
            if (delayedRigRefreshCoroutine != null)
            {
                StopCoroutine(delayedRigRefreshCoroutine);
                delayedRigRefreshCoroutine = null;
            }

            ApplyConstraintWeightsImmediately(0f);
            hasSmoothedPosition = false;
            randomLookOffset = Vector3.zero;
            UpdateDebugAimTargetMarker(false);
            LogLifecycle("OnDisable end");
        }

        private void OnValidate()
        {
            maxYawDegrees = Mathf.Max(0f, maxYawDegrees);
            maxPitchDegrees = Mathf.Max(0f, maxPitchDegrees);
            maxDownPitchDegrees = Mathf.Max(0f, maxDownPitchDegrees);
            targetDistance = Mathf.Max(0.01f, targetDistance);
            maxCameraLookDistance = Mathf.Max(0f, maxCameraLookDistance);
            idleRandomTargetDistance = Mathf.Max(0.01f, idleRandomTargetDistance);
            headWeight = Mathf.Clamp01(headWeight);
            eyeWeight = Mathf.Clamp01(eyeWeight);
            minimumHeadWeightWhenEyeLookActive = Mathf.Clamp01(minimumHeadWeightWhenEyeLookActive);
            targetSmoothing = Mathf.Max(0.01f, targetSmoothing);
            weightSmoothing = Mathf.Max(0.01f, weightSmoothing);
            externalWeightSmoothing = Mathf.Max(0.01f, externalWeightSmoothing);
            debugAimTargetScale = ClampDebugAimTargetScale(debugAimTargetScale);
            debugLogIntervalSeconds = Mathf.Max(0.05f, debugLogIntervalSeconds);
            debugBurstFrameCount = Mathf.Max(0, debugBurstFrameCount);
            rigConfigurationDirty = true;

            if (!Application.isPlaying)
            {
                isApplyingEditorPreviewFromOnValidate = true;
                try
                {
                    if (enableEditorPreview)
                    {
                        ApplyEditorPreview();
                    }
                    else
                    {
                        RestoreEditorPreviewPose();
                    }
                }
                finally
                {
                    isApplyingEditorPreviewFromOnValidate = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                ApplyEditorPreview();
                return;
            }

            if (NekomataLookRigSceneOverride.IsLookRigDisabledInScene())
            {
                ApplyConstraintWeightsImmediately(0f);
                hasSmoothedPosition = false;
                UpdateDebugAimTargetMarker(false);
                return;
            }

            UpdateExternalWeightScale();
            ResolveReferences();
            EnsureGeneratedAimTarget();
            EnsureRigComponents();
            ConfigureRigTargets(false);

            Transform activeTarget = ResolveActiveTarget();
            if (activeTarget == null)
            {
                ApplyConstraintWeights(0f);
                UpdateDebugAimTargetMarker(false);
                return;
            }

            Vector3 desiredPosition = ResolveDesiredLookPosition(activeTarget);
            if (!hasSmoothedPosition)
            {
                smoothedLookPosition = desiredPosition;
                hasSmoothedPosition = true;
            }

            float t = 1f - Mathf.Exp(-targetSmoothing * Time.deltaTime);
            smoothedLookPosition = Vector3.Lerp(smoothedLookPosition, desiredPosition, t);
            generatedAimTarget.position = smoothedLookPosition;

            UpdateDebugAimTargetMarker(true);
            ApplyConstraintWeights(1f);
            ApplyManualLookRotation();
            UpdateBlinking();
            LogDebugState(activeTarget, desiredPosition, ConsumeDebugBurstFrame());
        }

        public void SetConversationTarget()
        {
            mode = NekomataLookMode.Conversation;
            lookTargetManager?.ClearTarget();
        }

        public void SetEventTarget(Transform target)
        {
            mode = NekomataLookMode.Event;
            if (lookTargetManager != null)
            {
                lookTargetManager.SetTarget(target);
            }
        }

        public void ClearTarget()
        {
            mode = NekomataLookMode.Idle;
            lookTargetManager?.ClearTarget();
            ScheduleNextIdleRandomLook();
        }

        public void FadeExternalWeight(float targetWeight, float fadeSeconds)
        {
            targetExternalWeightScale = Mathf.Clamp01(targetWeight);
            externalWeightFadeSpeed = fadeSeconds > 0f
                ? Mathf.Max(0.01f, 4f / fadeSeconds)
                : 1000f;
            enabled = true;
        }

        public void ResetExternalWeight(bool immediate = false)
        {
            targetExternalWeightScale = 1f;
            if (immediate)
            {
                externalWeightScale = 1f;
            }
        }

        /// <summary>
        /// 一時的に下方向の視線可動域を狭めます。0 以下は通常の設定へ戻します。
        /// 通常設定より緩い値は適用しません。
        /// </summary>
        public void SetTemporaryMaxDownPitchDegrees(float maximumDegrees)
        {
            temporaryMaxDownPitchDegrees = maximumDegrees > 0f
                ? Mathf.Max(0f, maximumDegrees)
                : (float?)null;
        }

        public void ClearTemporaryMaxDownPitchDegrees()
        {
            temporaryMaxDownPitchDegrees = null;
        }

        public void ForceRuntimeRefresh(bool resetConstraintWeights = true, bool rebuildRigGraph = true)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            LogLifecycle($"ForceRuntimeRefresh begin resetWeights={resetConstraintWeights} rebuildGraph={rebuildRigGraph}");
            ResetExternalWeight(true);
            RefreshRigConfiguration(resetConstraintWeights, rebuildRigGraph);
            RestartDelayedRigRefresh();
            StartDebugBurst();
            LogLifecycle("ForceRuntimeRefresh end");
        }

        public void ResetForNormalCat(bool disableHeadLook)
        {
            mode = NekomataLookMode.Idle;
            lookTargetManager?.ClearTarget();
            randomLookOffset = Vector3.zero;
            hasSmoothedPosition = false;
            useManualLookRotation = false;
            useEyeAimAxisForHead = false;
            headAimAxis = MultiAimConstraintData.Axis.Z_NEG;
            eyeAimAxis = MultiAimConstraintData.Axis.Z_NEG;
            targetExternalWeightScale = 1f;
            externalWeightScale = 1f;

            if (disableHeadLook)
            {
                headWeight = 0f;
                minimumHeadWeightWhenEyeLookActive = 0f;
            }
            else
            {
                headWeight = Mathf.Max(headWeight, 0.65f);
                minimumHeadWeightWhenEyeLookActive = Mathf.Max(minimumHeadWeightWhenEyeLookActive, 0.25f);
            }

            eyeWeight = Mathf.Max(eyeWeight, 1f);

            if (headAim != null)
            {
                headAim.weight = 0f;
            }

            if (leftEyeAim != null)
            {
                leftEyeAim.weight = 0f;
            }

            if (rightEyeAim != null)
            {
                rightEyeAim.weight = 0f;
            }

            if (rig != null)
            {
                rig.weight = 1f;
            }

            ScheduleNextIdleRandomLook();
        }

        private Transform ResolveActiveTarget()
        {
            if (prioritizeCameraTarget && localPlayerLookTarget != null)
            {
                if (IsCameraLookTargetWithinDistance(localPlayerLookTarget.transform))
                {
                    return localPlayerLookTarget.transform;
                }

                Transform fallbackTarget = lookTargetManager != null ? lookTargetManager.CurrentTarget : null;
                return fallbackTarget != localPlayerLookTarget.transform ? fallbackTarget : null;
            }

            if (lookTargetManager != null)
            {
                Transform target = lookTargetManager.CurrentTarget;
                if (target == localPlayerLookTarget?.transform && !IsCameraLookTargetWithinDistance(target))
                {
                    return null;
                }

                return target;
            }

            return null;
        }

        private bool IsCameraLookTargetWithinDistance(Transform target)
        {
            if (target == null || maxCameraLookDistance <= 0f)
            {
                return true;
            }

            PlayerLookTarget playerLookTarget = target.GetComponent<PlayerLookTarget>();
            Vector3 distancePosition = playerLookTarget != null && playerLookTarget.Source != null
                ? playerLookTarget.Source.position
                : target.position;
            float maxDistanceSqr = maxCameraLookDistance * maxCameraLookDistance;
            return (distancePosition - GetLookOrigin()).sqrMagnitude <= maxDistanceSqr;
        }

        private Vector3 ResolveDesiredLookPosition(Transform activeTarget)
        {
            Vector3 targetPosition = activeTarget.position;
            if (mode == NekomataLookMode.Idle && enableIdleRandomLook)
            {
                UpdateIdleRandomLook(activeTarget);
                targetPosition += randomLookOffset;
            }
            else
            {
                randomLookOffset = Vector3.zero;
            }

            lastRawTargetPosition = targetPosition;
            return ClampPositionToLookCone(targetPosition);
        }

        private void UpdateIdleRandomLook(Transform activeTarget)
        {
            if (Time.time >= randomLookEndTime)
            {
                randomLookOffset = Vector3.zero;
            }

            if (Time.time < nextRandomLookTime)
            {
                return;
            }

            float hostility = GetNormalizedStatus(StatusType.Hostility);
            float chance = Mathf.Clamp01(0.35f + hostility * hostilityLookAwayChanceBonus);
            if (Random.value <= chance)
            {
                Vector3 origin = GetLookOrigin();
                Vector3 baseDirection = (activeTarget.position - origin).sqrMagnitude > 0.0001f
                    ? (activeTarget.position - origin).normalized
                    : GetReferenceForward();
                Quaternion offsetRotation = Quaternion.Euler(
                    Random.Range(idlePitchOffsetRange.x, idlePitchOffsetRange.y),
                    Random.Range(idleYawOffsetRange.x, idleYawOffsetRange.y),
                    0f);
                Vector3 randomPoint = origin + offsetRotation * baseDirection * idleRandomTargetDistance;
                randomLookOffset = randomPoint - activeTarget.position;
                float concern = GetNormalizedStatus(StatusType.Concern);
                float duration = Random.Range(idleLookAwayDurationRange.x, idleLookAwayDurationRange.y);
                duration *= Mathf.Lerp(1f, concernLookAwayDurationMultiplierAtMax, concern);
                randomLookEndTime = Time.time + Mathf.Max(0.1f, duration);
            }

            ScheduleNextIdleRandomLook();
        }

        private Vector3 ClampPositionToLookCone(Vector3 worldTarget)
        {
            Transform reference = referenceForward != null ? referenceForward : transform;
            Vector3 origin = GetLookOrigin();
            Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 toTarget = worldTarget - origin;
            Vector3 localDirection = new Vector3(
                Vector3.Dot(toTarget, right),
                Vector3.Dot(toTarget, Vector3.up),
                Vector3.Dot(toTarget, forward));
            if (localDirection.sqrMagnitude < 0.0001f)
            {
                localDirection = Vector3.forward;
            }

            float minAngleDistance = Mathf.Max(0.25f, targetDistance * 0.5f);
            if (localDirection.magnitude < minAngleDistance)
            {
                localDirection = localDirection.normalized * minAngleDistance;
            }

            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float horizontal = new Vector2(localDirection.x, localDirection.z).magnitude;
            float pitch = Mathf.Atan2(localDirection.y, horizontal) * Mathf.Rad2Deg;
            lastLookOrigin = origin;
            lastLocalLookDirection = localDirection;
            lastYawBeforeClamp = yaw;
            lastPitchBeforeClamp = pitch;
            yaw = Mathf.Clamp(yaw, -maxYawDegrees, maxYawDegrees);
            float configuredDownPitchLimit = maxDownPitchDegrees > 0f ? maxDownPitchDegrees : maxPitchDegrees;
            float downPitchLimit = temporaryMaxDownPitchDegrees.HasValue
                ? Mathf.Min(configuredDownPitchLimit, temporaryMaxDownPitchDegrees.Value)
                : configuredDownPitchLimit;
            pitch = Mathf.Clamp(pitch, -downPitchLimit, maxPitchDegrees);
            lastYawAfterClamp = yaw;
            lastPitchAfterClamp = pitch;

            float yawRadians = yaw * Mathf.Deg2Rad;
            float pitchRadians = pitch * Mathf.Deg2Rad;
            Vector3 clampedLocalDirection = new Vector3(
                Mathf.Sin(yawRadians) * Mathf.Cos(pitchRadians),
                Mathf.Sin(pitchRadians),
                Mathf.Cos(yawRadians) * Mathf.Cos(pitchRadians));
            Vector3 clampedDirection =
                right * clampedLocalDirection.x +
                Vector3.up * clampedLocalDirection.y +
                forward * clampedLocalDirection.z;
            float distance = targetDistance;
            lastClampedTargetPosition = origin + clampedDirection.normalized * distance;
            return lastClampedTargetPosition;
        }

        private void ApplyConstraintWeights(float desiredWeight)
        {
            desiredWeight *= externalWeightScale;
            float t = 1f - Mathf.Exp(-weightSmoothing * Time.deltaTime);
            if (headAim != null)
            {
                float resolvedHeadWeight = Mathf.Max(headWeight, eyeWeight > 0f ? minimumHeadWeightWhenEyeLookActive : 0f);
                headAim.weight = Mathf.Lerp(headAim.weight, desiredWeight * resolvedHeadWeight, t);
            }

            if (leftEyeAim != null)
            {
                leftEyeAim.weight = Mathf.Lerp(leftEyeAim.weight, desiredWeight * eyeWeight, t);
            }

            if (rightEyeAim != null)
            {
                rightEyeAim.weight = Mathf.Lerp(rightEyeAim.weight, desiredWeight * eyeWeight, t);
            }
        }

        private void ApplyConstraintWeightsImmediately(float weight)
        {
            weight = Mathf.Clamp01(weight);
            if (headAim != null)
            {
                headAim.weight = weight;
            }

            if (leftEyeAim != null)
            {
                leftEyeAim.weight = weight;
            }

            if (rightEyeAim != null)
            {
                rightEyeAim.weight = weight;
            }
        }

        private void ApplyManualLookRotation()
        {
            if (!useManualLookRotation || generatedAimTarget == null || externalWeightScale <= 0.001f)
            {
                return;
            }

            float resolvedHeadWeight = Mathf.Max(headWeight, eyeWeight > 0f ? minimumHeadWeightWhenEyeLookActive : 0f);
            ApplyManualAim(headBone, useEyeAimAxisForHead ? eyeAimAxis : headAimAxis, resolvedHeadWeight * externalWeightScale);
            ApplyManualAim(leftEyeBone, eyeAimAxis, eyeWeight * externalWeightScale);
            ApplyManualAim(rightEyeBone, eyeAimAxis, eyeWeight * externalWeightScale);
        }

        private void UpdateExternalWeightScale()
        {
            float speed = externalWeightFadeSpeed > 0f ? externalWeightFadeSpeed : externalWeightSmoothing;
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            externalWeightScale = Mathf.Lerp(externalWeightScale, targetExternalWeightScale, t);
            if (Mathf.Abs(externalWeightScale - targetExternalWeightScale) < 0.001f)
            {
                externalWeightScale = targetExternalWeightScale;
            }
        }

        private void ApplyManualAim(Transform bone, MultiAimConstraintData.Axis aimAxis, float weight)
        {
            if (generatedAimTarget == null)
            {
                return;
            }

            ApplyManualAim(bone, generatedAimTarget.position, aimAxis, weight);
        }

        private static void ApplyManualAim(
            Transform bone,
            Vector3 targetPosition,
            MultiAimConstraintData.Axis aimAxis,
            float weight)
        {
            if (bone == null || weight <= 0f)
            {
                return;
            }

            Vector3 targetDirection = targetPosition - bone.position;
            if (targetDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 currentAxis = bone.TransformDirection(ToLocalAxis(aimAxis));
            if (currentAxis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion delta = Quaternion.FromToRotation(currentAxis.normalized, targetDirection.normalized);
            Quaternion targetRotation = delta * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, targetRotation, Mathf.Clamp01(weight));
        }

        private void ApplyEditorPreview()
        {
            if (Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

#if UNITY_EDITOR
            if (IsInspectedTimelineBoundToThisLookRig())
            {
                if (!applyEditorPreviewDuringTimeline)
                {
                    SuspendEditorPreviewRigForTimelineEditing();
                    return;
                }
            }

            ResumeEditorPreviewRigAfterTimelineEditing();
#endif

            if (!enableEditorPreview)
            {
                return;
            }

#if UNITY_EDITOR
            if (ShouldHoldEditorLookForInspectedTimeline() && ApplyLastEditorLookPose())
            {
                return;
            }
#endif

            Transform previewTarget = ResolveEditorPreviewTarget();
            if (previewTarget == null)
            {
                return;
            }

            PlayerLookTarget playerLookTarget = previewTarget.GetComponent<PlayerLookTarget>();
            if (playerLookTarget != null)
            {
                playerLookTarget.SynchronizeNow();
            }

            ResolveBonesByName();
            CacheEditorPreviewPose();

            Vector3 previewPosition = ClampPositionToLookCone(previewTarget.position);
            float resolvedHeadWeight = Mathf.Max(headWeight, previewEyesInEditor && eyeWeight > 0f
                ? minimumHeadWeightWhenEyeLookActive
                : 0f);
            bool evaluatedRuntimeRig = TryEvaluateEditorPreviewRig(previewPosition, resolvedHeadWeight);
            // Editor PreviewもPlay時と同じ設定を使う。Runtimeで直接ボーン補正を
            // 無効にしている時に、Editorだけ追従が強く見える差を防ぐ。
            int manualIterations = useManualLookRotation
                ? (evaluatedRuntimeRig ? 8 : 1)
                : 0;
            for (int i = 0; i < manualIterations; i++)
            {
                ApplyManualAim(headBone, previewPosition, useEyeAimAxisForHead ? eyeAimAxis : headAimAxis, resolvedHeadWeight);

                if (previewEyesInEditor)
                {
                    ApplyManualAim(leftEyeBone, previewPosition, eyeAimAxis, eyeWeight);
                    ApplyManualAim(rightEyeBone, previewPosition, eyeAimAxis, eyeWeight);
                }
            }

            CacheLastEditorLookPose();
        }

#if UNITY_EDITOR
        private bool ShouldHoldEditorLookForInspectedTimeline()
        {
            return TryGetInspectedTimelineLookAtPolicy(out bool keepLookAt) && !keepLookAt;
        }

        private bool IsInspectedTimelineBoundToThisLookRig()
        {
            if (!AnimationMode.InAnimationMode())
            {
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            TimelineAsset timeline = TimelineEditor.inspectedAsset;
            if (director == null || timeline == null)
            {
                return false;
            }

            foreach (DialogueTimelineSequenceController sequenceController in
                     FindObjectsByType<DialogueTimelineSequenceController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Animator sequenceAnimator = sequenceController != null ? sequenceController.CatAnimator : null;
                if (sequenceAnimator != null &&
                    transform.IsChildOf(sequenceAnimator.transform) &&
                    IsTimelineBoundToAnimator(director, timeline, sequenceAnimator))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetInspectedTimelineLookAtPolicy(out bool keepLookAt)
        {
            keepLookAt = true;
            if (!IsInspectedTimelineBoundToThisLookRig())
            {
                return false;
            }

            TimelineAsset timeline = TimelineEditor.inspectedAsset;
            PlayableDirector director = TimelineEditor.inspectedDirector;
            foreach (DialogueTimelineSequenceController sequenceController in
                     FindObjectsByType<DialogueTimelineSequenceController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Animator sequenceAnimator = sequenceController != null ? sequenceController.CatAnimator : null;
                if (sequenceAnimator != null &&
                    transform.IsChildOf(sequenceAnimator.transform) &&
                    IsTimelineBoundToAnimator(director, timeline, sequenceAnimator) &&
                    sequenceController.TryGetTimelineLookAtSetting(timeline, out keepLookAt))
                {
                    return true;
                }
            }

            return false;
        }

        private void SuspendEditorPreviewRigForTimelineEditing()
        {
            if (editorPreviewRigSuspendedForTimeline)
            {
                return;
            }

            if (rigBuilder != null)
            {
                suspendedRigBuilderWasEnabled = rigBuilder.enabled;
                rigBuilder.enabled = false;

                Animator rigAnimator = rigBuilder.GetComponent<Animator>();
                if (rigAnimator != null)
                {
                    suspendedRigAnimatorWasEnabled = rigAnimator.enabled;
                    rigAnimator.enabled = false;
                }
            }

            editorPreviewRigSuspendedForTimeline = true;
        }

        private void ResumeEditorPreviewRigAfterTimelineEditing()
        {
            if (!editorPreviewRigSuspendedForTimeline)
            {
                return;
            }

            if (rigBuilder != null)
            {
                rigBuilder.enabled = suspendedRigBuilderWasEnabled;
                Animator rigAnimator = rigBuilder.GetComponent<Animator>();
                if (rigAnimator != null)
                {
                    rigAnimator.enabled = suspendedRigAnimatorWasEnabled;
                }
            }

            editorPreviewRigSuspendedForTimeline = false;
        }

        private static bool IsTimelineBoundToAnimator(
            PlayableDirector director,
            TimelineAsset timeline,
            Animator expectedAnimator)
        {
            foreach (PlayableBinding output in timeline.outputs)
            {
                if (!(output.sourceObject is AnimationTrack))
                {
                    continue;
                }

                Object binding = director.GetGenericBinding(output.sourceObject);
                Animator boundAnimator = binding as Animator;
                if (boundAnimator == null && binding is GameObject gameObject)
                {
                    boundAnimator = gameObject.GetComponent<Animator>();
                }
                else if (boundAnimator == null && binding is Component component)
                {
                    boundAnimator = component.GetComponent<Animator>();
                }

                if (boundAnimator == expectedAnimator)
                {
                    return true;
                }
            }

            return false;
        }
#endif

        private bool TryEvaluateEditorPreviewRig(Vector3 previewPosition, float resolvedHeadWeight)
        {
            // Animation Rigging can invoke OnEnable/OnValidate while its serialized
            // layer data is still being restored. Build on the following LateUpdate.
            if (isEvaluatingEditorPreviewRig ||
                isApplyingEditorPreviewFromOnEnable ||
                isApplyingEditorPreviewFromOnValidate)
            {
                return false;
            }

            isEvaluatingEditorPreviewRig = true;
            try
            {
                return TryEvaluateEditorPreviewRigCore(previewPosition, resolvedHeadWeight);
            }
            finally
            {
                isEvaluatingEditorPreviewRig = false;
            }
        }

        private bool TryEvaluateEditorPreviewRigCore(Vector3 previewPosition, float resolvedHeadWeight)
        {
            if (rigBuilder == null || rig == null || generatedAimTarget == null ||
                headAim == null || leftEyeAim == null || rightEyeAim == null)
            {
                return false;
            }

            if (rigBuilder.GetComponent<Animator>() == null ||
                !HasEditorPreviewConstrainedObject(headAim) ||
                !HasEditorPreviewConstrainedObject(leftEyeAim) ||
                !HasEditorPreviewConstrainedObject(rightEyeAim))
            {
                return false;
            }

            generatedAimTarget.position = previewPosition;
            bool sourcesChanged =
                AssignSingleSource(headAim) |
                AssignSingleSource(leftEyeAim) |
                AssignSingleSource(rightEyeAim);

            if (!rigBuilder.graph.IsValid() || sourcesChanged)
            {
#if UNITY_EDITOR
                // In Edit Mode, RigBuilder's graph is owned by the editor preview.
                // Building it here can race with domain reload / Timeline graph setup.
                // Fall back to the direct-bone preview until the editor has made it valid.
                if (!Application.isPlaying)
                {
                    return false;
                }
#endif
                rigBuilder.Clear();
                if (!rigBuilder.Build())
                {
                    return false;
                }
            }

            rig.weight = 1f;
            headAim.weight = resolvedHeadWeight;
            leftEyeAim.weight = previewEyesInEditor ? eyeWeight : 0f;
            rightEyeAim.weight = previewEyesInEditor ? eyeWeight : 0f;
            rigBuilder.Evaluate(0f);
            return true;
        }

        private static bool HasEditorPreviewConstrainedObject(MultiAimConstraint constraint)
        {
            return constraint != null && constraint.data.constrainedObject != null;
        }

        private void CacheLastEditorLookPose()
        {
            lastEditorLookHeadLocalRotation = headBone != null ? headBone.localRotation : Quaternion.identity;
            lastEditorLookLeftEyeLocalRotation = leftEyeBone != null ? leftEyeBone.localRotation : Quaternion.identity;
            lastEditorLookRightEyeLocalRotation = rightEyeBone != null ? rightEyeBone.localRotation : Quaternion.identity;
            hasLastEditorLookPose = true;
        }

        private bool ApplyLastEditorLookPose()
        {
            if (!hasLastEditorLookPose)
            {
                return false;
            }

            ApplyConstraintWeightsImmediately(0f);

            if (headBone != null)
            {
                headBone.localRotation = lastEditorLookHeadLocalRotation;
            }

            if (previewEyesInEditor && leftEyeBone != null)
            {
                leftEyeBone.localRotation = lastEditorLookLeftEyeLocalRotation;
            }

            if (previewEyesInEditor && rightEyeBone != null)
            {
                rightEyeBone.localRotation = lastEditorLookRightEyeLocalRotation;
            }

            return true;
        }

        private Transform ResolveEditorPreviewTarget()
        {
            if (editorPreviewTarget != null)
            {
                return editorPreviewTarget;
            }

            if (!useMainCameraForEditorPreview)
            {
                return null;
            }

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        private void CacheEditorPreviewPose()
        {
            if (hasEditorPreviewPose)
            {
                return;
            }

            previewHeadBone = headBone;
            previewLeftEyeBone = leftEyeBone;
            previewRightEyeBone = rightEyeBone;
            previewHeadLocalRotation = headBone != null ? headBone.localRotation : Quaternion.identity;
            previewLeftEyeLocalRotation = leftEyeBone != null ? leftEyeBone.localRotation : Quaternion.identity;
            previewRightEyeLocalRotation = rightEyeBone != null ? rightEyeBone.localRotation : Quaternion.identity;
            hasEditorPreviewPose = true;
        }

        private void RestoreEditorPreviewPose()
        {
            if (!hasEditorPreviewPose)
            {
                return;
            }

            ApplyConstraintWeightsImmediately(0f);

            if (previewHeadBone != null)
            {
                previewHeadBone.localRotation = previewHeadLocalRotation;
            }

            if (previewLeftEyeBone != null)
            {
                previewLeftEyeBone.localRotation = previewLeftEyeLocalRotation;
            }

            if (previewRightEyeBone != null)
            {
                previewRightEyeBone.localRotation = previewRightEyeLocalRotation;
            }

            previewHeadBone = null;
            previewLeftEyeBone = null;
            previewRightEyeBone = null;
            hasEditorPreviewPose = false;
            hasLastEditorLookPose = false;
        }

        private static Vector3 ToLocalAxis(MultiAimConstraintData.Axis axis)
        {
            return axis switch
            {
                MultiAimConstraintData.Axis.X => Vector3.right,
                MultiAimConstraintData.Axis.X_NEG => Vector3.left,
                MultiAimConstraintData.Axis.Y => Vector3.up,
                MultiAimConstraintData.Axis.Y_NEG => Vector3.down,
                MultiAimConstraintData.Axis.Z => Vector3.forward,
                MultiAimConstraintData.Axis.Z_NEG => Vector3.back,
                _ => Vector3.forward
            };
        }

        private void UpdateBlinking()
        {
            if (!useAnimatorBlinkTriggers || animator == null)
            {
                return;
            }

            if (Time.time >= nextBlinkTime)
            {
                animator.SetTrigger(blinkTrigger);
                ScheduleNextBlink();
            }

            if (Time.time >= nextSlowBlinkTime)
            {
                float affection = GetNormalizedStatus(StatusType.Affection);
                if (Random.value <= affection * affectionSlowBlinkChanceAtMax)
                {
                    animator.SetTrigger(slowBlinkTrigger);
                }

                ScheduleNextSlowBlink();
            }
        }

        private void ScheduleNextIdleRandomLook()
        {
            float hostility = GetNormalizedStatus(StatusType.Hostility);
            float interval = Random.Range(idleRandomIntervalRange.x, idleRandomIntervalRange.y);
            interval *= Mathf.Lerp(1f, hostilityIntervalMultiplierAtMax, hostility);
            nextRandomLookTime = Time.time + Mathf.Max(0.1f, interval);
        }

        private void ScheduleNextBlink()
        {
            float hostility = GetNormalizedStatus(StatusType.Hostility);
            float interval = Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
            interval /= Mathf.Max(0.01f, Mathf.Lerp(1f, hostilityBlinkMultiplierAtMax, hostility));
            nextBlinkTime = Time.time + Mathf.Max(0.1f, interval);
        }

        private void ScheduleNextSlowBlink()
        {
            nextSlowBlinkTime = Time.time + Random.Range(slowBlinkIntervalRange.x, slowBlinkIntervalRange.y);
        }

        private float GetNormalizedStatus(StatusType statusType)
        {
            if (statusManager == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(statusManager.GetValue(statusType) / 100f);
        }

        private Vector3 GetLookOrigin()
        {
            if (headAim != null && headAim.data.constrainedObject != null)
            {
                return headAim.data.constrainedObject.position;
            }

            if (leftEyeAim != null && leftEyeAim.data.constrainedObject != null)
            {
                return leftEyeAim.data.constrainedObject.position;
            }

            return transform.position;
        }

        private Vector3 GetReferenceForward()
        {
            Transform reference = referenceForward != null ? referenceForward : transform;
            return reference.forward.sqrMagnitude > 0.0001f ? reference.forward : Vector3.forward;
        }

        private void ConfigureRigTargets(bool rebuildRigGraph)
        {
            if (generatedAimTarget == null)
            {
                LogLifecycle($"ConfigureRigTargets skipped generatedAimTarget=null rebuildGraph={rebuildRigGraph}");
                return;
            }

            bool changed =
                AssignSingleSource(headAim) |
                AssignSingleSource(leftEyeAim) |
                AssignSingleSource(rightEyeAim);

            if (rigBuilder != null && (rigConfigurationDirty || changed || rebuildRigGraph))
            {
                if (rebuildRigGraph)
                {
                    LogLifecycle($"RigBuilder.Clear rebuildGraph={rebuildRigGraph} changed={changed} dirty={rigConfigurationDirty}");
                    rigBuilder.Clear();
                }

                bool built = rigBuilder.Build();
                LogLifecycle($"RigBuilder.Build result={built} rebuildGraph={rebuildRigGraph} changed={changed} dirty={rigConfigurationDirty}");
                rigConfigurationDirty = false;
            }
        }

        private IEnumerator RefreshRigConfigurationAfterInitialEvaluation()
        {
            yield return new WaitForEndOfFrame();
            LogLifecycle("DelayedRefresh after EndOfFrame");
            RefreshRigConfiguration(true, true);
            yield return null;
            LogLifecycle("DelayedRefresh after 1 frame");
            RefreshRigConfiguration(true, true);
            yield return null;
            LogLifecycle("DelayedRefresh after 2 frames");
            RefreshRigConfiguration(false, false);
            delayedRigRefreshCoroutine = null;
        }

        private void RestartDelayedRigRefresh()
        {
            if (delayedRigRefreshCoroutine != null)
            {
                LogLifecycle("RestartDelayedRigRefresh stop previous");
                StopCoroutine(delayedRigRefreshCoroutine);
            }

            delayedRigRefreshCoroutine = StartCoroutine(RefreshRigConfigurationAfterInitialEvaluation());
            LogLifecycle("RestartDelayedRigRefresh start");
        }

        private void RefreshRigConfiguration(bool resetConstraintWeights, bool rebuildRigGraph)
        {
            LogLifecycle($"RefreshRigConfiguration begin resetWeights={resetConstraintWeights} rebuildGraph={rebuildRigGraph}");
            ResolveReferences();
            EnsureGeneratedAimTarget();
            EnsureRigComponents();
            rigConfigurationDirty = true;
            ConfigureRigTargets(rebuildRigGraph);

            if (resetConstraintWeights)
            {
                ApplyConstraintWeightsImmediately(0f);
                hasSmoothedPosition = false;
            }

            StartDebugBurst();
            LogLifecycle($"RefreshRigConfiguration end resetWeights={resetConstraintWeights} rebuildGraph={rebuildRigGraph}");
        }

        private void EnsureRigComponents()
        {
            if (!autoSetupRig)
            {
                return;
            }

            ResolveBonesByName();
            ValidateLocalRigReferences();

            if (rigBuilder == null)
            {
                rigBuilder = GetComponent<RigBuilder>();
                if (rigBuilder == null)
                {
                    rigBuilder = gameObject.AddComponent<RigBuilder>();
                    rigConfigurationDirty = true;
                }
            }

            if (rig == null)
            {
                rig = GetComponentInChildren<Rig>();
                if (rig == null)
                {
                    GameObject rigObject = new GameObject("NekomataLookRig");
                    rigObject.transform.SetParent(transform, false);
                    rig = rigObject.AddComponent<Rig>();
                    rigConfigurationDirty = true;
                }
            }

            EnsureRigLayerRegistered();

            if (useEyeAimAxisForHead && headAimAxis != eyeAimAxis && enableDebugLogs)
            {
                Debug.LogWarning(
                    $"[NekomataLookRig] Head aim axis is being overridden by the eye aim axis. " +
                    $"path={GetTransformPath(transform)} head={headAimAxis} eye={eyeAimAxis}. " +
                    $"Disable useEyeAimAxisForHead if the head turns sideways.",
                    this);
            }

            MultiAimConstraintData.Axis effectiveHeadAimAxis = useEyeAimAxisForHead ? eyeAimAxis : headAimAxis;
            headAim = EnsureAimConstraint(headAim, "HeadLookAim", headBone, effectiveHeadAimAxis);
            leftEyeAim = EnsureAimConstraint(leftEyeAim, "LeftEyeLookAim", leftEyeBone, eyeAimAxis);
            rightEyeAim = EnsureAimConstraint(rightEyeAim, "RightEyeLookAim", rightEyeBone, eyeAimAxis);
        }

        private void EnsureRigLayerRegistered()
        {
            if (rigBuilder == null || rig == null)
            {
                return;
            }

            for (int i = 0; i < rigBuilder.layers.Count; i++)
            {
                if (rigBuilder.layers[i].rig == rig)
                {
                    return;
                }
            }

            rigBuilder.layers.Add(new RigLayer(rig));
            rigConfigurationDirty = true;
        }

        private MultiAimConstraint EnsureAimConstraint(
            MultiAimConstraint currentConstraint,
            string objectName,
            Transform constrainedBone,
            MultiAimConstraintData.Axis aimAxis)
        {
            if (currentConstraint == null && rig != null)
            {
                Transform existing = rig.transform.Find(objectName);
                if (existing != null)
                {
                    currentConstraint = existing.GetComponent<MultiAimConstraint>();
                }
            }

            if (currentConstraint == null && rig != null)
            {
                GameObject constraintObject = new GameObject(objectName);
                constraintObject.transform.SetParent(rig.transform, false);
                currentConstraint = constraintObject.AddComponent<MultiAimConstraint>();
                rigConfigurationDirty = true;
            }

            if (currentConstraint != null)
            {
                MultiAimConstraintData data = currentConstraint.data;
                Transform targetBone = constrainedBone != null ? constrainedBone : data.constrainedObject;
                MultiAimConstraintData.Axis resolvedUpAxis = ResolveCompatibleUpAxis(aimAxis, upAxis);
                if (data.constrainedObject != targetBone ||
                    data.aimAxis != aimAxis ||
                    data.upAxis != resolvedUpAxis)
                {
                    MultiAimConstraintData.Axis previousAimAxis = data.aimAxis;
                    MultiAimConstraintData.Axis previousUpAxis = data.upAxis;
                    Transform previousConstrainedObject = data.constrainedObject;
                    data.constrainedObject = targetBone;
                    data.aimAxis = aimAxis;
                    data.upAxis = resolvedUpAxis;
                    currentConstraint.data = data;
                    rigConfigurationDirty = true;
                    if (enableDebugLogs && logConstraintUpdates)
                    {
                        Debug.Log(
                            $"[NekomataLookRig] Constraint update path={GetTransformPath(transform)} constraint={objectName} " +
                            $"constrained:{FormatTransform(previousConstrainedObject)}->{FormatTransform(targetBone)} " +
                            $"aim:{previousAimAxis}->{aimAxis} up:{previousUpAxis}->{resolvedUpAxis}",
                            this);
                    }
                }
            }

            return currentConstraint;
        }

        private bool AssignSingleSource(MultiAimConstraint constraint)
        {
            if (constraint == null)
            {
                return false;
            }

            WeightedTransformArray sourceObjects = constraint.data.sourceObjects;
            if (sourceObjects.Count == 1 && sourceObjects[0].transform == generatedAimTarget)
            {
                return false;
            }

            sourceObjects.Clear();
            sourceObjects.Add(new WeightedTransform(generatedAimTarget, 1f));
            constraint.data.sourceObjects = sourceObjects;
            return true;
        }

        private void EnsureGeneratedAimTarget()
        {
            if (generatedAimTarget != null &&
                (!forceLocalGeneratedAimTarget || IsDescendantOf(generatedAimTarget, transform)))
            {
                return;
            }

            Transform previous = generatedAimTarget;
            Transform localTarget = transform.Find("GeneratedNekomataLookAimTarget");
            if (localTarget == null)
            {
                GameObject targetObject = new GameObject("GeneratedNekomataLookAimTarget");
                targetObject.transform.SetParent(transform, false);
                localTarget = targetObject.transform;
            }

            Transform activeTarget = ResolveActiveTarget();
            localTarget.position = activeTarget != null
                ? ClampPositionToLookCone(activeTarget.position)
                : transform.position + GetReferenceForward() * targetDistance;
            generatedAimTarget = localTarget;
            rigConfigurationDirty = true;

            if (enableDebugLogs && previous != null && previous != generatedAimTarget)
            {
                Debug.Log(
                    $"[NekomataLookRig] Replaced non-local generated target path={GetTransformPath(transform)} " +
                    $"old={FormatTransform(previous)} new={FormatTransform(generatedAimTarget)}",
                    this);
            }
        }

        private void UpdateDebugAimTargetMarker(bool hasActiveTarget)
        {
            if (!showDebugAimTarget || generatedAimTarget == null)
            {
                if (debugAimTargetMarker != null)
                {
                    debugAimTargetMarker.SetActive(false);
                }

                return;
            }

            EnsureDebugAimTargetMarker();
            debugAimTargetMarker.SetActive(hasActiveTarget);
            debugAimTargetMarker.transform.localPosition = Vector3.zero;
            debugAimTargetMarker.transform.localRotation = Quaternion.identity;
            debugAimTargetMarker.transform.localScale = debugAimTargetScale;
        }

        private void EnsureDebugAimTargetMarker()
        {
            if (debugAimTargetMarker != null)
            {
                return;
            }

            debugAimTargetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugAimTargetMarker.name = "Debug_FinalNekomataLookAimTarget";
            debugAimTargetMarker.transform.SetParent(generatedAimTarget, false);

            Collider markerCollider = debugAimTargetMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            debugAimTargetRenderer = debugAimTargetMarker.GetComponent<Renderer>();
            if (debugAimTargetRenderer != null)
            {
                debugAimTargetRenderer.material = new Material(ResolveDebugMarkerShader())
                {
                    color = debugAimTargetColor
                };
            }
        }

        private static Shader ResolveDebugMarkerShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            return shader != null ? shader : Shader.Find("Standard");
        }

        private static Vector3 ClampDebugAimTargetScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                Mathf.Max(0.001f, scale.z));
        }

        private bool ConsumeDebugBurstFrame()
        {
            if (remainingDebugBurstFrames <= 0)
            {
                return false;
            }

            remainingDebugBurstFrames--;
            return true;
        }

        private void StartDebugBurst()
        {
            remainingDebugBurstFrames = Mathf.Max(remainingDebugBurstFrames, debugBurstFrameCount);
        }

        private void LogLifecycle(string message)
        {
            if (!enableDebugLogs)
            {
                return;
            }

            Debug.Log(
                $"[NekomataLookRig][Lifecycle] frame={Time.frameCount} t={Time.time:F3} path={GetTransformPath(transform)} " +
                $"{message} enabled={enabled} active={gameObject.activeInHierarchy} mode={mode} " +
                $"external={externalWeightScale:F3}->{targetExternalWeightScale:F3} rig={FormatRig(rig)} rigBuilder={FormatRigBuilder(rigBuilder)} " +
                $"head={FormatAim(headAim)} leftEye={FormatAim(leftEyeAim)} rightEye={FormatAim(rightEyeAim)}",
                this);
        }

        private void LogDebugState(Transform activeTarget, Vector3 desiredPosition, bool force)
        {
            if (!enableDebugLogs || (!force && Time.unscaledTime < nextDebugLogTime))
            {
                return;
            }

            if (!force)
            {
                nextDebugLogTime = Time.unscaledTime + debugLogIntervalSeconds;
            }

            Transform reference = referenceForward != null ? referenceForward : transform;
            Debug.Log(
                $"[NekomataLookRig] frame={Time.frameCount} t={Time.time:F3} burst={force} path={GetTransformPath(transform)} mode={mode} idleRandom={enableIdleRandomLook} " +
                $"manager={FormatComponent(lookTargetManager)} explicitTarget={(lookTargetManager != null && lookTargetManager.HasExplicitTarget)} " +
                $"localRefs manager={FormatLocalFlag(lookTargetManager != null ? lookTargetManager.transform : null)} " +
                $"generated={FormatLocalFlag(generatedAimTarget)} reference={FormatLocalFlag(referenceForward)} " +
                $"active={FormatTransform(activeTarget)} raw={FormatVector(lastRawTargetPosition)} " +
                $"origin={FormatVector(lastLookOrigin)} desired={FormatVector(desiredPosition)} smoothed={FormatVector(smoothedLookPosition)} " +
                $"generated={FormatTransform(generatedAimTarget)} generatedPos={FormatTransformPosition(generatedAimTarget)} " +
                $"reference={FormatTransform(reference)} refForward={FormatVector(reference.forward)} refLocalDir={FormatVector(lastLocalLookDirection)} " +
                $"yaw={lastYawBeforeClamp:F1}->{lastYawAfterClamp:F1}/{maxYawDegrees:F1} pitch={lastPitchBeforeClamp:F1}->{lastPitchAfterClamp:F1}/up:{maxPitchDegrees:F1} down:{(maxDownPitchDegrees > 0f ? maxDownPitchDegrees : maxPitchDegrees):F1} " +
                $"randomOffset={FormatVector(randomLookOffset)} clamped={FormatVector(lastClampedTargetPosition)} " +
                $"externalWeight={externalWeightScale:F3}->{targetExternalWeightScale:F3} configuredWeights head={headWeight:F2} minHead={minimumHeadWeightWhenEyeLookActive:F2} eye={eyeWeight:F2} rig={FormatRig(rig)} rigBuilder={FormatRigBuilder(rigBuilder)} " +
                $"headBone={FormatTransform(headBone)} headAim={FormatAim(headAim)} leftEye={FormatAim(leftEyeAim)} rightEye={FormatAim(rightEyeAim)} " +
                $"configuredAxes head={headAimAxis} effectiveHead={(useEyeAimAxisForHead ? eyeAimAxis : headAimAxis)} eye={eyeAimAxis} up={upAxis} " +
                $"headAxisMatch={FormatAxisMatch(headBone, generatedAimTarget)} leftEyeAxisMatch={FormatAxisMatch(leftEyeBone, generatedAimTarget)} rightEyeAxisMatch={FormatAxisMatch(rightEyeBone, generatedAimTarget)}",
                this);
        }

        private static string FormatRig(Rig currentRig)
        {
            return currentRig != null
                ? $"{GetTransformPath(currentRig.transform)} weight={currentRig.weight:F2} active={currentRig.gameObject.activeInHierarchy}"
                : "null";
        }

        private static string FormatRigBuilder(RigBuilder currentRigBuilder)
        {
            return currentRigBuilder != null
                ? $"{GetTransformPath(currentRigBuilder.transform)} enabled={currentRigBuilder.enabled} active={currentRigBuilder.gameObject.activeInHierarchy} layers={currentRigBuilder.layers.Count}"
                : "null";
        }

        private static MultiAimConstraintData.Axis ResolveCompatibleUpAxis(
            MultiAimConstraintData.Axis aimAxis,
            MultiAimConstraintData.Axis configuredUpAxis)
        {
            if (GetAxisFamily(aimAxis) != GetAxisFamily(configuredUpAxis))
            {
                return configuredUpAxis;
            }

            return GetAxisFamily(aimAxis) == 'Y'
                ? MultiAimConstraintData.Axis.Z
                : MultiAimConstraintData.Axis.Y;
        }

        private static char GetAxisFamily(MultiAimConstraintData.Axis axis)
        {
            switch (axis)
            {
                case MultiAimConstraintData.Axis.X:
                case MultiAimConstraintData.Axis.X_NEG:
                    return 'X';
                case MultiAimConstraintData.Axis.Y:
                case MultiAimConstraintData.Axis.Y_NEG:
                    return 'Y';
                default:
                    return 'Z';
            }
        }

        private static string FormatAxisMatch(Transform constrainedObject, Transform target)
        {
            if (constrainedObject == null || target == null)
            {
                return "null";
            }

            Vector3 toTarget = target.position - constrainedObject.position;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return "too-close";
            }

            toTarget.Normalize();
            float x = Vector3.Dot(constrainedObject.right, toTarget);
            float y = Vector3.Dot(constrainedObject.up, toTarget);
            float z = Vector3.Dot(constrainedObject.forward, toTarget);
            return $"X:{x:F2} X_NEG:{-x:F2} Y:{y:F2} Y_NEG:{-y:F2} Z:{z:F2} Z_NEG:{-z:F2}";
        }

        private static string FormatAim(MultiAimConstraint constraint)
        {
            if (constraint == null)
            {
                return "null";
            }

            MultiAimConstraintData data = constraint.data;
            string source = data.sourceObjects.Count > 0 ? FormatTransform(data.sourceObjects[0].transform) : "no-source";
            return $"{GetTransformPath(constraint.transform)} enabled={constraint.enabled} active={constraint.gameObject.activeInHierarchy} weight={constraint.weight:F2} constrained={FormatTransform(data.constrainedObject)} aim={data.aimAxis} up={data.upAxis} sources={data.sourceObjects.Count} source={source}";
        }

        private static string FormatComponent(Component component)
        {
            return component != null ? GetTransformPath(component.transform) : "null";
        }

        private string FormatLocalFlag(Transform target)
        {
            if (target == null)
            {
                return "null";
            }

            return IsDescendantOf(target, transform) ? "local" : "external";
        }

        private static string FormatTransform(Transform target)
        {
            return target != null ? GetTransformPath(target) : "null";
        }

        private static string FormatTransformPosition(Transform target)
        {
            return target != null ? FormatVector(target.position) : "null";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F3},{value.y:F3},{value.z:F3})";
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
            {
                return "null";
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private void ResolveReferences()
        {
            if (useSharedScenePlayerLookTarget)
            {
                EnsureSharedSceneLookTargetManager();
            }
            else if (forceLocalLookTargetManager)
            {
                EnsureLocalLookTargetManager();
            }
            else if (lookTargetManager == null)
            {
                lookTargetManager = FindFirstObjectByType<LookTargetManager>();
            }

            if (forceLocalReferenceForward)
            {
                EnsureLocalReferenceForward();
            }

            ResolveStatusManager();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (rigBuilder == null)
            {
                rigBuilder = GetComponentInChildren<RigBuilder>();
            }

            if (rig == null)
            {
                rig = GetComponentInChildren<Rig>();
            }
        }

        private void ResolveStatusManager()
        {
            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            StatusManager dialogueStatusManager = dialogueManager != null
                ? dialogueManager.GetComponent<StatusManager>()
                : null;
            if (dialogueStatusManager != null)
            {
                statusManager = dialogueStatusManager;
                return;
            }

            if (statusManager == null)
            {
                statusManager = FindFirstObjectByType<StatusManager>();
            }
        }

        private void EnsureSharedSceneLookTargetManager()
        {
            if (lookTargetManager != null &&
                !IsDescendantOf(lookTargetManager.transform, transform) &&
                localPlayerLookTarget != null &&
                !IsDescendantOf(localPlayerLookTarget.transform, transform))
            {
                lookTargetManager.SetDefaultTarget(localPlayerLookTarget.transform);
                return;
            }

            LookTargetManager previousManager = lookTargetManager;
            PlayerLookTarget previousPlayerTarget = localPlayerLookTarget;
            LookTargetManager sharedManager = FindSharedLookTargetManager();
            PlayerLookTarget sharedPlayerTarget = FindSharedPlayerLookTarget();

            if (sharedManager == null)
            {
                GameObject managerObject = GameObject.Find(SharedLookTargetRootName);
                if (managerObject == null)
                {
                    managerObject = new GameObject(SharedLookTargetRootName);
                }

                sharedManager = GetOrAddComponent<LookTargetManager>(managerObject);
            }

            if (sharedPlayerTarget == null)
            {
                Transform existingTransform = sharedManager.transform.Find(SharedPlayerLookTargetName);
                GameObject targetObject = existingTransform != null
                    ? existingTransform.gameObject
                    : new GameObject(SharedPlayerLookTargetName);
                targetObject.transform.SetParent(sharedManager.transform, true);
                sharedPlayerTarget = GetOrAddComponent<PlayerLookTarget>(targetObject);
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                sharedPlayerTarget.Source = mainCamera.transform;
                sharedPlayerTarget.transform.position = mainCamera.transform.position;
            }

            lookTargetManager = sharedManager;
            localPlayerLookTarget = sharedPlayerTarget;
            lookTargetManager.SetDefaultTarget(localPlayerLookTarget.transform);

            if (enableDebugLogs &&
                (previousManager != lookTargetManager || previousPlayerTarget != localPlayerLookTarget))
            {
                Debug.Log(
                    $"[NekomataLookRig] Shared look target path={GetTransformPath(transform)} " +
                    $"manager:{FormatComponent(previousManager)}->{FormatComponent(lookTargetManager)} " +
                    $"playerTarget:{FormatComponent(previousPlayerTarget)}->{FormatComponent(localPlayerLookTarget)}",
                    this);
            }
        }

        private static LookTargetManager FindSharedLookTargetManager()
        {
            LookTargetManager[] managers = FindObjectsByType<LookTargetManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            LookTargetManager fallback = null;
            for (int i = 0; i < managers.Length; i++)
            {
                LookTargetManager manager = managers[i];
                if (manager == null)
                {
                    continue;
                }

                if (manager.gameObject.name == SharedLookTargetRootName)
                {
                    return manager;
                }

                fallback ??= manager;
            }

            return fallback;
        }

        private static PlayerLookTarget FindSharedPlayerLookTarget()
        {
            PlayerLookTarget[] targets = FindObjectsByType<PlayerLookTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            PlayerLookTarget fallback = null;
            for (int i = 0; i < targets.Length; i++)
            {
                PlayerLookTarget target = targets[i];
                if (target == null)
                {
                    continue;
                }

                if (target.gameObject.name == SharedPlayerLookTargetName)
                {
                    return target;
                }

                fallback ??= target;
            }

            return fallback;
        }

        private void EnsureLocalLookTargetManager()
        {
            if (lookTargetManager != null &&
                IsDescendantOf(lookTargetManager.transform, transform) &&
                localPlayerLookTarget != null &&
                IsDescendantOf(localPlayerLookTarget.transform, transform))
            {
                lookTargetManager.SetDefaultTarget(localPlayerLookTarget.transform);
                return;
            }

            LookTargetManager previousManager = lookTargetManager;
            PlayerLookTarget previousPlayerTarget = localPlayerLookTarget;
            Transform managerTransform = transform.Find("LocalLookTargetManager");
            if (managerTransform == null)
            {
                GameObject managerObject = new GameObject("LocalLookTargetManager");
                managerObject.transform.SetParent(transform, false);
                managerTransform = managerObject.transform;
            }

            lookTargetManager = GetOrAddComponent<LookTargetManager>(managerTransform.gameObject);

            Transform playerTargetTransform = managerTransform.Find("LocalPlayerLookTarget");
            if (playerTargetTransform == null)
            {
                GameObject playerTargetObject = new GameObject("LocalPlayerLookTarget");
                playerTargetObject.transform.SetParent(managerTransform, false);
                playerTargetTransform = playerTargetObject.transform;
            }

            localPlayerLookTarget = GetOrAddComponent<PlayerLookTarget>(playerTargetTransform.gameObject);
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                localPlayerLookTarget.Source = mainCamera.transform;
                localPlayerLookTarget.transform.position = mainCamera.transform.position;
            }

            lookTargetManager.SetDefaultTarget(localPlayerLookTarget.transform);

            if (enableDebugLogs &&
                (previousManager != lookTargetManager || previousPlayerTarget != localPlayerLookTarget))
            {
                Debug.Log(
                    $"[NekomataLookRig] Local look target path={GetTransformPath(transform)} " +
                    $"manager:{FormatComponent(previousManager)}->{FormatComponent(lookTargetManager)} " +
                    $"playerTarget:{FormatComponent(previousPlayerTarget)}->{FormatComponent(localPlayerLookTarget)}",
                    this);
            }
        }

        private void EnsureLocalReferenceForward()
        {
            if (referenceForward != null && IsDescendantOf(referenceForward, transform))
            {
                return;
            }

            Transform previous = referenceForward;
            Transform localReference = FindChildByNames(transform, "CatForwardReference");
            if (localReference == null)
            {
                if (enableDebugLogs && previous != null)
                {
                    Debug.LogWarning(
                        $"[NekomataLookRig] Ignoring non-local referenceForward path={GetTransformPath(transform)} " +
                        $"old={FormatTransform(previous)} local=not-found",
                        this);
                }

                referenceForward = null;
                return;
            }

            referenceForward = localReference;
            if (enableDebugLogs && previous != referenceForward)
            {
                Debug.Log(
                    $"[NekomataLookRig] Local referenceForward path={GetTransformPath(transform)} " +
                    $"old={FormatTransform(previous)} new={FormatTransform(referenceForward)}",
                    this);
            }
        }

        private void ValidateLocalRigReferences()
        {
            if (rigBuilder != null && !IsDescendantOf(rigBuilder.transform, transform))
            {
                rigBuilder = null;
            }

            if (rig != null && !IsDescendantOf(rig.transform, transform))
            {
                rig = null;
            }

            if (headAim != null && !IsDescendantOf(headAim.transform, transform))
            {
                headAim = null;
            }

            if (leftEyeAim != null && !IsDescendantOf(leftEyeAim.transform, transform))
            {
                leftEyeAim = null;
            }

            if (rightEyeAim != null && !IsDescendantOf(rightEyeAim.transform, transform))
            {
                rightEyeAim = null;
            }
        }

        private void ResolveBonesByName()
        {
            if (headBone == null)
            {
                headBone = FindChildByNames(transform, "Head", "head");
            }

            if (leftEyeBone == null)
            {
                leftEyeBone = FindChildByNames(transform, "Eye_L", "eye_l", "LeftEye", "left_eye");
            }

            if (rightEyeBone == null)
            {
                rightEyeBone = FindChildByNames(transform, "Eye_R", "eye_r", "RightEye", "right_eye");
            }
        }

        private static Transform FindChildByNames(Transform root, params string[] names)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (root.name == names[i])
                {
                    return root;
                }
            }

            foreach (Transform child in root)
            {
                Transform match = FindChildByNames(child, names);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static bool IsDescendantOf(Transform child, Transform root)
        {
            if (child == null || root == null)
            {
                return false;
            }

            Transform current = child;
            while (current != null)
            {
                if (current == root)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
