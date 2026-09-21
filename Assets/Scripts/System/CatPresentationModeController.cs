using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    public enum CatPresentationMode
    {
        Opening,
        Idle,
        Conversation,
        Timeline,

        // Legacy names kept so existing scene bindings and callers remain valid.
        NormalConversation = Conversation,
        Event
    }

    [DisallowMultipleComponent]
    public sealed class CatPresentationModeController : MonoBehaviour
    {
        [Header("Separated Cats")]
        [FormerlySerializedAs("opCat")]
        [SerializeField] private GameObject openingCat;
        [SerializeField] private GameObject normalCat;
        [FormerlySerializedAs("homeAnchor")]
        [Tooltip("ちゃぶ台側の基本会話位置。")]
        [SerializeField] private Transform normalCatHomeAnchor;
        [Tooltip("机側の基本会話位置。")]
        [SerializeField] private Transform normalCatHomeDeskAnchor;

        [Header("Normal Cat Controllers")]
        [SerializeField] private CatPositionController positionController;
        [SerializeField] private CatMotionController motionController;
        [FormerlySerializedAs("normalAnimator")]
        [SerializeField] private Animator normalCatAnimator;
        [SerializeField] private Behaviour[] normalLookRigBehaviours = Array.Empty<Behaviour>();
        [FormerlySerializedAs("lookRig")]
        [SerializeField] private NekomataLookRigController lookRig;
        [SerializeField] private ChatUIController chatUI;

        [Header("Normal Cat Defaults")]
        [SerializeField] private string idleStateName = "CatSimple_Lie_belly_loop_1";
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool disableNormalRootMotion = true;
        [SerializeField] private bool stopPlayableDirectorsUnderNormalCat = true;
        [SerializeField] private bool hideOpeningCatWithRenderers = true;

        [Header("Idle Variations")]
        [SerializeField] private bool enableIdleVariations = true;
        [SerializeField] private string lookAroundIdleStateName = "CatSimple_Lie_belly_loop_2";
        [SerializeField] private string yawnIdleStateName = "CatSimple_Lie_belly_loop_3";
        [SerializeField] private float lookAroundIntervalMin = 5f;
        [SerializeField] private float lookAroundIntervalMax = 10f;
        [SerializeField] private float yawnIntervalMin = 10f;
        [SerializeField] private float yawnIntervalMax = 15f;
        [SerializeField] private float idleVariationBlendSeconds = 0.9f;
        [SerializeField] private float idleVariationReturnBlendSeconds = 0.7f;
        [Tooltip("CatSimple_Lie_belly_loop_3 のあくび再生中だけ適用する、頭の下方向の最大可動角度です。通常の制限より緩い値は適用されません。")]
        [Range(0f, 90f)] [SerializeField] private float yawnMaxDownPitchDegrees = 65f;
        [SerializeField] private float idleVariationLookRigFadeOutSeconds = 0.9f;
        [SerializeField] private float idleVariationLookRigFadeInSeconds = 1f;

        public CatPresentationMode CurrentMode { get; private set; } = CatPresentationMode.Opening;
        public GameObject OpCat => openingCat;
        public GameObject NormalCat => normalCat;
        public Animator NormalAnimator => normalCatAnimator;
        public CatPositionController PositionController => positionController;
        public CatMotionController MotionController => motionController;
        public bool IsDelayingDialogueResponse { get; private set; }

        private bool hasSwitchedToNormal;
        private Coroutine idleVariationCoroutine;
        private Coroutine restartIdleVariationCoroutine;
        private Coroutine normalLookRigRefreshCoroutine;
        private bool isPlayingIdleVariation;
        private CatPresentationMode modeBeforeTimeline = CatPresentationMode.Idle;

        private void OnDisable()
        {
            StopIdleVariationLoop();
            StopRestartIdleVariationCoroutine();
            StopNormalLookRigRefreshCoroutine();
        }

        private void Awake()
        {
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (initializeOnAwake)
            {
                EnterOpeningMode();
            }
        }

        public void Configure(GameObject opCatRoot, GameObject normalCatRoot, Transform normalHomeAnchor, ChatUIController runtimeChatUI)
        {
            Configure(opCatRoot, normalCatRoot, normalHomeAnchor, null, runtimeChatUI);
        }

        public void Configure(
            GameObject opCatRoot,
            GameObject normalCatRoot,
            Transform normalTableHomeAnchor,
            Transform normalDeskHomeAnchor,
            ChatUIController runtimeChatUI)
        {
            openingCat = opCatRoot;
            normalCat = normalCatRoot;
            normalCatHomeAnchor = normalTableHomeAnchor;
            normalCatHomeDeskAnchor = normalDeskHomeAnchor;
            chatUI = runtimeChatUI;
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();
        }

        public void SetHomeLocation(CatHomeLocation location)
        {
            ResolveNormalCatReferences();
            positionController?.SetHomeLocation(location);
        }

        public void MoveHomeToTable()
        {
            SetHomeLocation(CatHomeLocation.Table);
        }

        public void MoveHomeToDesk()
        {
            SetHomeLocation(CatHomeLocation.Desk);
        }

        /// <summary>
        /// Immediately rebuilds the normal cat's camera-priority look target after a
        /// hidden location transition. The caller is responsible for waiting a frame
        /// when Animation Rigging needs an additional evaluation pass.
        /// </summary>
        public void RefreshLookAtCameraImmediately()
        {
            ResolveNormalCatReferences();
            if (lookRig == null)
            {
                return;
            }

            lookRig.enabled = true;
            lookRig.ResetExternalWeight(true);
            lookRig.ForceRuntimeRefresh(true, true);
        }

        public void EnterOpeningMode()
        {
            StopIdleVariationLoop();
            StopRestartIdleVariationCoroutine();
            StopNormalLookRigRefreshCoroutine();
            CurrentMode = CatPresentationMode.Opening;
            hasSwitchedToNormal = false;
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();
            SetLookRigEnabled(false);
            SetConversationInputEnabled(false);
            PlaceNormalCatAtHome();

            if (openingCat != null)
            {
                openingCat.SetActive(true);
                SetOpeningCatVisible(true);
            }
            else
            {
                LogMissing(nameof(openingCat));
            }

            if (normalCat != null)
            {
                if (!ShouldKeepNormalCatActiveForStartup())
                {
                    normalCat.SetActive(false);
                }
            }
            else
            {
                LogMissing(nameof(normalCat));
            }
        }

        public void EnterNormalMode()
        {
            SwitchToNormalCat();
        }

        public void EnterIdleMode()
        {
            SwitchToNormalCat();
            if (CurrentMode == CatPresentationMode.Conversation)
            {
                CurrentMode = CatPresentationMode.Idle;
                SetConversationInputEnabled(false);
            }
        }

        public void BeginTimelinePresentation()
        {
            if (CurrentMode == CatPresentationMode.Timeline)
            {
                return;
            }

            modeBeforeTimeline = CurrentMode == CatPresentationMode.Opening
                ? CatPresentationMode.Idle
                : CurrentMode;
            ResolveNormalCatReferences();

            // 場所移動Timelineでは、開始Signalが暗転を始める前にホームへスナップしてはならない。
            // ここで位置を変えると、ネルコが暗転前に消える／跳ぶように見える。
            // 遷移先への配置はOpenBetaTitleBootstrapが暗転完了後に行う。
            CurrentMode = CatPresentationMode.Timeline;
            StopIdleVariationLoop();
            StopRestartIdleVariationCoroutine();
            SetConversationInputEnabled(false);
            Debug.Log($"[CatPresentationMode] Timeline started; previous={modeBeforeTimeline}", this);
        }

        public void EnterEventMode()
        {
            StopIdleVariationLoop();
            StopRestartIdleVariationCoroutine();
            StopNormalLookRigRefreshCoroutine();
            CurrentMode = CatPresentationMode.Event;
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();
            SetLookRigEnabled(false);

            if (openingCat != null)
            {
                SetOpeningCatVisible(false);
            }

            if (normalCat != null)
            {
                normalCat.SetActive(true);
            }

            PlaceNormalCatAtHome();
            SetConversationInputEnabled(false);
        }

        public void ReturnToNormalMode()
        {
            EnterNormalMode();
        }

        public void OnOpeningTimelineFinished()
        {
            SwitchToNormalCat();
        }

        public void SwitchToNormalCat()
        {
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (hasSwitchedToNormal && CurrentMode == CatPresentationMode.NormalConversation)
            {
                SetConversationInputEnabled(true);
                StartIdleVariationLoop();
                return;
            }

            if (!ValidateSwitchReferences())
            {
                return;
            }

            SetLookRigEnabled(false);
            PlaceNormalCatAtHome();

            if (openingCat != null)
            {
                SetOpeningCatVisible(false);
            }

            normalCat.SetActive(true);

            if (!TryPlayIdleFromStart())
            {
                return;
            }

            CurrentMode = CatPresentationMode.NormalConversation;
            hasSwitchedToNormal = true;
            SetLookRigEnabled(true);
            RefreshLookRigAfterNormalSwitch();
            SetConversationInputEnabled(true);
            StartIdleVariationLoop();

            Debug.Log($"[CatPresentationMode] SwitchToNormalCat normal={FormatTransform(normalCat.transform)} home={FormatTransform(normalCatHomeAnchor)} idle={idleStateName}", this);
        }

        public void PlayMotion(string motionId)
        {
            StopIdleVariationLoop();
            ResolveNormalCatReferences();
            motionController?.PlayMotion(motionId);
            positionController?.SnapToHome();
            RestartIdleVariationLoopAfterMotion();
        }

        public void ResumeIdleAfterTimeline()
        {
            CatPresentationMode restoredMode = modeBeforeTimeline;
            if (restoredMode != CatPresentationMode.Conversation && restoredMode != CatPresentationMode.Idle)
            {
                restoredMode = CatPresentationMode.Idle;
            }

            CurrentMode = restoredMode;
            if (restoredMode == CatPresentationMode.Conversation)
            {
                // Timeline終了は現在の会話行を送る操作ではない。ここで入力UIへ遷移すると、
                // 最終行の表示中に台詞が閉じてしまうため、会話ルーティングだけを復帰する。
                SetConversationInputEnabled(true, enterPlayerInputMode: false);
            }
            else
            {
                SetConversationInputEnabled(false);
            }

            // Timelineの制御解除後も、現在選択中の基本会話拠点へ復帰する。
            // Desk選択中にTableアンカーへ戻らないよう、直接アンカーを参照しない。
            ResolveNormalCatReferences();
            positionController?.MoveToHome();

            if (isActiveAndEnabled)
            {
                StartIdleVariationLoop();
            }

            Debug.Log($"[CatPresentationMode] Timeline ended; restored={CurrentMode}", this);
        }

        private void StartIdleVariationLoop()
        {
            if (!enableIdleVariations || idleVariationCoroutine != null || normalCatAnimator == null)
            {
                return;
            }

            idleVariationCoroutine = StartCoroutine(IdleVariationLoop());
        }

        private void StopIdleVariationLoop()
        {
            if (idleVariationCoroutine != null)
            {
                StopCoroutine(idleVariationCoroutine);
                idleVariationCoroutine = null;
            }

            isPlayingIdleVariation = false;
            IsDelayingDialogueResponse = false;
            lookRig?.ClearTemporaryMaxDownPitchDegrees();
            if (CurrentMode == CatPresentationMode.Conversation || CurrentMode == CatPresentationMode.Idle)
            {
                RestoreLookRigWeight(true);
            }
        }

        private void StopRestartIdleVariationCoroutine()
        {
            if (restartIdleVariationCoroutine != null)
            {
                StopCoroutine(restartIdleVariationCoroutine);
                restartIdleVariationCoroutine = null;
            }
        }

        private void StopNormalLookRigRefreshCoroutine()
        {
            if (normalLookRigRefreshCoroutine != null)
            {
                StopCoroutine(normalLookRigRefreshCoroutine);
                normalLookRigRefreshCoroutine = null;
            }
        }

        private void RefreshLookRigAfterNormalSwitch()
        {
            StopNormalLookRigRefreshCoroutine();
            if (lookRig != null)
            {
                lookRig.ForceRuntimeRefresh(true, true);
                normalLookRigRefreshCoroutine = StartCoroutine(RefreshLookRigAfterNormalSwitchCoroutine());
            }
        }

        private IEnumerator RefreshLookRigAfterNormalSwitchCoroutine()
        {
            yield return new WaitForEndOfFrame();
            if (lookRig != null && (CurrentMode == CatPresentationMode.Conversation || CurrentMode == CatPresentationMode.Idle))
            {
                lookRig.ForceRuntimeRefresh(true, true);
            }

            yield return null;
            if (lookRig != null && CurrentMode == CatPresentationMode.NormalConversation)
            {
                lookRig.ForceRuntimeRefresh(false, false);
            }

            normalLookRigRefreshCoroutine = null;
        }

        private void RestartIdleVariationLoopAfterMotion()
        {
            StopRestartIdleVariationCoroutine();
            restartIdleVariationCoroutine = StartCoroutine(RestartIdleVariationLoopAfterMotionCoroutine());
        }

        private IEnumerator RestartIdleVariationLoopAfterMotionCoroutine()
        {
            yield return null;
            while (motionController != null && motionController.IsPlayingMotion)
            {
                yield return null;
            }

            restartIdleVariationCoroutine = null;
            if (CurrentMode == CatPresentationMode.Conversation || CurrentMode == CatPresentationMode.Idle)
            {
                StartIdleVariationLoop();
            }
        }

        private IEnumerator IdleVariationLoop()
        {
            float nextLookAroundAt = Time.time + RandomInterval(lookAroundIntervalMin, lookAroundIntervalMax);
            float nextYawnAt = Time.time + RandomInterval(yawnIntervalMin, yawnIntervalMax);

            while (true)
            {
                if (!CanRunIdleVariationLoop())
                {
                    yield return null;
                    continue;
                }

                float now = Time.time;
                if (now >= nextLookAroundAt && CanPlayIdleVariation(false))
                {
                    yield return PlayIdleVariation(lookAroundIdleStateName, false, true);
                    nextLookAroundAt = Time.time + RandomInterval(lookAroundIntervalMin, lookAroundIntervalMax);
                    continue;
                }

                if (now >= nextYawnAt && CanPlayIdleVariation(true))
                {
                    yield return PlayIdleVariation(yawnIdleStateName, true, false, yawnMaxDownPitchDegrees);
                    nextYawnAt = Time.time + RandomInterval(yawnIntervalMin, yawnIntervalMax);
                    continue;
                }

                yield return null;
            }
        }

        private IEnumerator PlayIdleVariation(
            string stateName,
            bool delayDialogueResponse,
            bool disableLookRigDuringPlayback,
            float? maxDownPitchDegreesDuringPlayback = null)
        {
            if (normalCatAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                yield break;
            }

            if (!TryResolveAnimatorStateHash(normalCatAnimator, stateName.Trim(), out int stateHash, out string resolvedStateName))
            {
                Debug.LogWarning($"[CatPresentationMode] Idle variation skipped: state '{stateName}' does not exist on layer 0. animator={normalCatAnimator.name}", this);
                yield break;
            }

            isPlayingIdleVariation = true;
            IsDelayingDialogueResponse = delayDialogueResponse;
            if (maxDownPitchDegreesDuringPlayback.HasValue)
            {
                lookRig?.SetTemporaryMaxDownPitchDegrees(maxDownPitchDegreesDuringPlayback.Value);
            }

            if (disableLookRigDuringPlayback)
            {
                FadeLookRigWeight(0f, idleVariationLookRigFadeOutSeconds);
                if (idleVariationLookRigFadeOutSeconds > 0f)
                {
                    yield return new WaitForSeconds(idleVariationLookRigFadeOutSeconds);
                }
            }

            normalCatAnimator.enabled = true;
            normalCatAnimator.speed = 1f;
            normalCatAnimator.applyRootMotion = false;
            normalCatAnimator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, idleVariationBlendSeconds), 0, 0f);
            PlaceNormalCatAtHome();

            yield return null;
            while (normalCatAnimator != null && normalCatAnimator.IsInTransition(0))
            {
                PlaceNormalCatAtHome();
                yield return null;
            }

            float startedAt = Time.time;
            while (normalCatAnimator != null && Time.time - startedAt < 10f)
            {
                AnimatorStateInfo state = normalCatAnimator.GetCurrentAnimatorStateInfo(0);
                if ((state.fullPathHash == stateHash || state.shortNameHash == stateHash) && state.normalizedTime >= 1f)
                {
                    break;
                }

                PlaceNormalCatAtHome();
                yield return null;
            }

            if (maxDownPitchDegreesDuringPlayback.HasValue)
            {
                lookRig?.ClearTemporaryMaxDownPitchDegrees();
            }

            PlayIdleWithBlend(idleVariationReturnBlendSeconds);
            if (disableLookRigDuringPlayback && CurrentMode == CatPresentationMode.NormalConversation)
            {
                RestoreLookRigWeight(false);
            }

            isPlayingIdleVariation = false;
            IsDelayingDialogueResponse = false;
            Debug.Log($"[CatPresentationMode] Idle variation finished: state={resolvedStateName}", this);
        }

        private bool CanRunIdleVariationLoop()
        {
            return enableIdleVariations &&
                   (CurrentMode == CatPresentationMode.Conversation || CurrentMode == CatPresentationMode.Idle) &&
                   normalCat != null &&
                   normalCat.activeInHierarchy &&
                   normalCatAnimator != null &&
                   normalCatAnimator.enabled &&
                   (motionController == null || !motionController.IsPlayingMotion);
        }

        private bool CanPlayIdleVariation(bool requiresNoConversation)
        {
            if (!CanRunIdleVariationLoop() || isPlayingIdleVariation || normalCatAnimator.IsInTransition(0))
            {
                return false;
            }

            if (requiresNoConversation && chatUI != null && (chatUI.IsInDialogueMode || chatUI.IsTyping))
            {
                return false;
            }

            AnimatorStateInfo state = normalCatAnimator.GetCurrentAnimatorStateInfo(0);
            return state.IsName(idleStateName);
        }

        private static float RandomInterval(float minSeconds, float maxSeconds)
        {
            float min = Mathf.Max(0.1f, minSeconds);
            float max = Mathf.Max(min, maxSeconds);
            return UnityEngine.Random.Range(min, max);
        }

        private static bool ShouldKeepNormalCatActiveForStartup()
        {
            return OpenBetaSceneNames.IsPvMorningScene(SceneManager.GetActiveScene().name);
        }

        private void FadeLookRigWeight(float targetWeight, float fadeSeconds)
        {
            if (lookRig != null)
            {
                lookRig.enabled = true;
                lookRig.FadeExternalWeight(targetWeight, fadeSeconds);
            }
        }

        private void RestoreLookRigWeight(bool immediate)
        {
            if (lookRig != null)
            {
                lookRig.enabled = true;
                if (immediate)
                {
                    lookRig.ResetExternalWeight(true);
                }
                else
                {
                    lookRig.FadeExternalWeight(1f, idleVariationLookRigFadeInSeconds);
                }
            }
        }

        private void ResolveNormalCatReferences()
        {
            if (normalCat == null)
            {
                return;
            }

            positionController ??= normalCat.GetComponent<CatPositionController>();
            if (positionController == null)
            {
                positionController = normalCat.AddComponent<CatPositionController>();
            }

            if (normalCatHomeAnchor != null || normalCatHomeDeskAnchor != null)
            {
                positionController.SetHomeAnchors(normalCatHomeAnchor, normalCatHomeDeskAnchor);
            }

            normalCatAnimator ??= normalCat.GetComponentInChildren<Animator>(true);

            motionController ??= normalCat.GetComponent<CatMotionController>();
            if (motionController == null)
            {
                motionController = normalCat.AddComponent<CatMotionController>();
            }

            motionController.Configure(normalCatAnimator, positionController, idleStateName);
            lookRig ??= normalCat.GetComponentInChildren<NekomataLookRigController>(true);
            if ((normalLookRigBehaviours == null || normalLookRigBehaviours.Length == 0) && lookRig != null)
            {
                normalLookRigBehaviours = new Behaviour[] { lookRig };
            }
        }

        private void ApplyNormalCatSafetySettings()
        {
            if (normalCatAnimator != null && disableNormalRootMotion)
            {
                normalCatAnimator.applyRootMotion = false;
            }

            if (normalCat == null || !stopPlayableDirectorsUnderNormalCat)
            {
                return;
            }

            PlayableDirector[] directors = normalCat.GetComponentsInChildren<PlayableDirector>(true);
            for (int i = 0; i < directors.Length; i++)
            {
                if (directors[i] == null)
                {
                    continue;
                }

                directors[i].Stop();
                directors[i].enabled = false;
            }
        }

        private bool ValidateSwitchReferences()
        {
            bool valid = true;

            if (openingCat == null)
            {
                LogMissing(nameof(openingCat));
            }

            if (normalCat == null)
            {
                LogMissing(nameof(normalCat));
                valid = false;
            }

            if (normalCatHomeAnchor == null)
            {
                LogMissing(nameof(normalCatHomeAnchor));
                valid = false;
            }

            if (normalCatAnimator == null)
            {
                LogMissing(nameof(normalCatAnimator));
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(idleStateName))
            {
                Debug.LogError("[CatPresentationMode] SwitchToNormalCat skipped: idleStateName is empty.", this);
                valid = false;
            }

            return valid;
        }

        private void PlaceNormalCatAtHome()
        {
            if (normalCat == null)
            {
                return;
            }

            // 通常会話への復帰時も、ちゃぶ台固定ではなく現在選択中の拠点へ戻す。
            // 待機アニメーション開始直前にこの処理が走るため、ここで直接
            // normalCatHomeAnchor を使うと Desk 選択がちゃぶ台へ戻されてしまう。
            ResolveNormalCatReferences();
            if (positionController != null)
            {
                positionController.MoveToHome();
                return;
            }

            if (normalCatHomeAnchor == null)
            {
                return;
            }

            normalCat.transform.SetPositionAndRotation(
                normalCatHomeAnchor.position,
                normalCatHomeAnchor.rotation);
        }

        private bool TryPlayIdleFromStart()
        {
            if (normalCatAnimator == null)
            {
                LogMissing(nameof(normalCatAnimator));
                return false;
            }

            if (normalCatAnimator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[CatPresentationMode] SwitchToNormalCat skipped: normalCatAnimator has no RuntimeAnimatorController. animator={normalCatAnimator.name}", this);
                return false;
            }

            if (!TryResolveAnimatorStateHash(normalCatAnimator, idleStateName, out int stateHash, out string resolvedStateName))
            {
                Debug.LogError($"[CatPresentationMode] SwitchToNormalCat skipped: idle state '{idleStateName}' does not exist on layer 0. animator={normalCatAnimator.name}", this);
                return false;
            }

            normalCatAnimator.enabled = true;
            normalCatAnimator.speed = 1f;
            normalCatAnimator.applyRootMotion = false;
            normalCatAnimator.Play(stateHash, 0, 0f);
            normalCatAnimator.Update(0f);
            PlaceNormalCatAtHome();
            Debug.Log($"[CatPresentationMode] Idle started: state={resolvedStateName} animator={normalCatAnimator.name}", this);
            return true;
        }

        private bool PlayIdleWithBlend(float blendSeconds)
        {
            if (normalCatAnimator == null || string.IsNullOrWhiteSpace(idleStateName))
            {
                return false;
            }

            if (!TryResolveAnimatorStateHash(normalCatAnimator, idleStateName, out int stateHash, out _))
            {
                return false;
            }

            normalCatAnimator.enabled = true;
            normalCatAnimator.speed = 1f;
            normalCatAnimator.applyRootMotion = false;
            if (blendSeconds <= 0f)
            {
                normalCatAnimator.Play(stateHash, 0, 0f);
            }
            else
            {
                normalCatAnimator.CrossFadeInFixedTime(stateHash, blendSeconds, 0, 0f);
            }

            PlaceNormalCatAtHome();
            return true;
        }

        private static bool TryResolveAnimatorStateHash(Animator animator, string stateName, out int stateHash, out string resolvedStateName)
        {
            const int LayerIndex = 0;
            const string BaseLayerName = "Base Layer";

            string fullPathStateName = $"{BaseLayerName}.{stateName}";
            int fullPathHash = Animator.StringToHash(fullPathStateName);
            if (animator.HasState(LayerIndex, fullPathHash))
            {
                stateHash = fullPathHash;
                resolvedStateName = fullPathStateName;
                return true;
            }

            int shortNameHash = Animator.StringToHash(stateName);
            if (animator.HasState(LayerIndex, shortNameHash))
            {
                stateHash = shortNameHash;
                resolvedStateName = stateName;
                return true;
            }

            stateHash = 0;
            resolvedStateName = string.Empty;
            return false;
        }

        private void SetLookRigEnabled(bool enabled)
        {
            if (normalLookRigBehaviours != null)
            {
                for (int i = 0; i < normalLookRigBehaviours.Length; i++)
                {
                    if (normalLookRigBehaviours[i] != null)
                    {
                        normalLookRigBehaviours[i].enabled = enabled;
                    }
                }
            }

            if (lookRig != null)
            {
                if (enabled)
                {
                    lookRig.ResetExternalWeight(true);
                }

                lookRig.enabled = enabled;
            }
        }

        private void SetOpeningCatVisible(bool visible)
        {
            if (openingCat == null)
            {
                return;
            }

            if (!hideOpeningCatWithRenderers)
            {
                openingCat.SetActive(visible);
                return;
            }

            if (!openingCat.activeSelf)
            {
                openingCat.SetActive(true);
            }

            Renderer[] renderers = openingCat.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }

        private void LogMissing(string fieldName)
        {
            Debug.LogError($"[CatPresentationMode] Missing required reference: {fieldName}. controller={name}", this);
        }

        private void SetConversationInputEnabled(bool enabled, bool enterPlayerInputMode = true)
        {
            if (chatUI == null)
            {
                return;
            }

            chatUI.RouteSubmittedInputToDialogueEngine = enabled;
            chatUI.SetLogButtonAllowedDuringConversation(enabled);
            chatUI.SetTalkTopicHintsAllowed(enabled);
            if (enabled && enterPlayerInputMode)
            {
                chatUI.EnterPlayerInputMode();
            }
        }

        private static string FormatTransform(Transform target)
        {
            if (target == null)
            {
                return "null";
            }

            Vector3 position = target.position;
            Vector3 euler = target.rotation.eulerAngles;
            return $"{GetPath(target)} pos=({position.x:F3},{position.y:F3},{position.z:F3}) rot=({euler.x:F1},{euler.y:F1},{euler.z:F1})";
        }

        private static string GetPath(Transform target)
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
    }
}
