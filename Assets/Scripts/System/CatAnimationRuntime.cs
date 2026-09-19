using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class CatAnimationRuntime : MonoBehaviour
    {
        [Header("Separated Cats")]
        [SerializeField] private GameObject opCat;
        [SerializeField] private GameObject normalCat;
        [SerializeField] private Transform normalCatHomeAnchor;

        [Header("Normal Cat Runtime")]
        [SerializeField] private CatPositionController positionController;
        [SerializeField] private Animator normalAnimator;
        [SerializeField] private NekomataLookRigController lookRig;
        [SerializeField] private ChatUIController chatUI;

        [Header("Animator States")]
        [SerializeField] private string defaultIdleStateName = "CatSimple_Lie_belly_loop_1";
        [SerializeField] private float defaultBlendSeconds = 0.15f;

        [Header("Idle Variations")]
        [SerializeField] private bool enableIdleVariations = true;
        [SerializeField] private string lookAroundIdleStateName = "CatSimple_Lie_belly_loop_2";
        [SerializeField] private string yawnIdleStateName = "CatSimple_Lie_belly_loop_3";
        [SerializeField] private float lookAroundIntervalMin = 5f;
        [SerializeField] private float lookAroundIntervalMax = 10f;
        [SerializeField] private float yawnIntervalMin = 10f;
        [SerializeField] private float yawnIntervalMax = 15f;

        [Header("Safety")]
        [SerializeField] private bool disableNormalCatRootMotion = true;
        [SerializeField] private bool disableNormalCatNeckLookAt = true;

        private Coroutine idleVariationCoroutine;
        private Coroutine returnToIdleCoroutine;
        private bool isPlayingIdleVariation;
        private bool isPlayingExternalMotion;
        private bool isDelayingDialogueResponse;
        private CatPresentationModeController presentationMode;

        public GameObject OpCat
        {
            get => opCat;
            set => opCat = value;
        }

        public GameObject NormalCat => normalCat;
        public Animator NormalAnimator => normalAnimator;
        public CatPositionController PositionController => positionController;
        public bool IsDelayingDialogueResponse => isDelayingDialogueResponse;

        private void OnDisable()
        {
            StopMotionCoroutines();
        }

        public void Configure(GameObject opCatRoot, GameObject normalCatRoot, Transform homeAnchor, ChatUIController runtimeChatUI)
        {
            opCat = opCatRoot;
            normalCat = normalCatRoot;
            normalCatHomeAnchor = homeAnchor;
            chatUI = runtimeChatUI;
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();
        }

        public void PrepareForOpening()
        {
            StopIdleVariationLoop();
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (opCat != null)
            {
                opCat.SetActive(true);
            }

            if (normalCat != null)
            {
                if (!ShouldKeepNormalCatActiveForStartup())
                {
                    normalCat.SetActive(false);
                }
            }
        }

        public void ActivateNormalCatAfterOp()
        {
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (opCat != null)
            {
                opCat.SetActive(false);
            }

            if (normalCat != null)
            {
                normalCat.SetActive(true);
            }

            ResetToDefault(false);
        }

        public void ResetToDefault(bool reenableConversationInput = true)
        {
            StopMotionCoroutines();
            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (normalCat != null && !normalCat.activeSelf)
            {
                normalCat.SetActive(true);
            }

            positionController?.MoveToHome();

            if (normalAnimator != null && !string.IsNullOrWhiteSpace(defaultIdleStateName))
            {
                normalAnimator.enabled = true;
                normalAnimator.speed = 1f;
                normalAnimator.applyRootMotion = false;
                if (defaultBlendSeconds <= 0f)
                {
                    normalAnimator.Play(defaultIdleStateName, 0, 0f);
                }
                else
                {
                    normalAnimator.CrossFadeInFixedTime(defaultIdleStateName, defaultBlendSeconds, 0, 0f);
                }

                normalAnimator.Update(0f);
                positionController?.MoveToHome();
            }

            lookRig?.ResetForNormalCat(disableNormalCatNeckLookAt);
            isPlayingExternalMotion = false;
            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
            StartIdleVariationLoop();

            if (reenableConversationInput && chatUI != null)
            {
                chatUI.RouteSubmittedInputToDialogueEngine = true;
                chatUI.SetLogButtonAllowedDuringConversation(true);
                chatUI.EnterPlayerInputMode();
            }
        }

        public void ResumeIdleAfterTimeline()
        {
            isPlayingExternalMotion = false;
            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
            ResolveNormalCatReferences();
            positionController?.MoveToHome();
            if (isActiveAndEnabled) StartIdleVariationLoop();
        }

        public void PlayMotionId(string motionId)
        {
            if (string.IsNullOrWhiteSpace(motionId))
            {
                ResetToDefault(false);
                return;
            }

            ResolveNormalCatReferences();
            ApplyNormalCatSafetySettings();

            if (normalAnimator == null)
            {
                return;
            }

            StopIdleVariationLoop();
            if (returnToIdleCoroutine != null)
            {
                StopCoroutine(returnToIdleCoroutine);
                returnToIdleCoroutine = null;
            }

            isPlayingExternalMotion = true;
            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
            normalAnimator.enabled = true;
            normalAnimator.speed = 1f;
            normalAnimator.applyRootMotion = false;
            normalAnimator.CrossFadeInFixedTime(motionId.Trim(), defaultBlendSeconds, 0, 0f);
            normalAnimator.Update(0f);
            positionController?.MoveToHome();
            returnToIdleCoroutine = StartCoroutine(ReturnToIdleAfterCurrentMotion());
        }

        private IEnumerator ReturnToIdleAfterCurrentMotion()
        {
            yield return null;

            while (normalAnimator != null && normalAnimator.IsInTransition(0))
            {
                positionController?.MoveToHome();
                yield return null;
            }

            while (normalAnimator != null)
            {
                AnimatorStateInfo state = normalAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.loop || state.normalizedTime >= 1f)
                {
                    break;
                }

                positionController?.MoveToHome();
                yield return null;
            }

            returnToIdleCoroutine = null;
            ResetToDefault(false);
        }

        private void StartIdleVariationLoop()
        {
            if (!enableIdleVariations || idleVariationCoroutine != null || normalAnimator == null || IsManagedByPresentationController())
            {
                return;
            }

            idleVariationCoroutine = StartCoroutine(IdleVariationLoop());
        }

        private void StopMotionCoroutines()
        {
            StopIdleVariationLoop();
            if (returnToIdleCoroutine != null)
            {
                StopCoroutine(returnToIdleCoroutine);
                returnToIdleCoroutine = null;
            }

            isPlayingExternalMotion = false;
            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
        }

        private void StopIdleVariationLoop()
        {
            if (idleVariationCoroutine != null)
            {
                StopCoroutine(idleVariationCoroutine);
                idleVariationCoroutine = null;
            }

            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
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
                    yield return PlayIdleVariation(lookAroundIdleStateName, false);
                    nextLookAroundAt = Time.time + RandomInterval(lookAroundIntervalMin, lookAroundIntervalMax);
                    continue;
                }

                if (now >= nextYawnAt && CanPlayIdleVariation(true))
                {
                    yield return PlayIdleVariation(yawnIdleStateName, true);
                    nextYawnAt = Time.time + RandomInterval(yawnIntervalMin, yawnIntervalMax);
                    continue;
                }

                yield return null;
            }
        }

        private IEnumerator PlayIdleVariation(string stateName, bool delayDialogueResponse)
        {
            if (normalAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                yield break;
            }

            isPlayingIdleVariation = true;
            isDelayingDialogueResponse = delayDialogueResponse;
            string trimmedStateName = stateName.Trim();
            normalAnimator.enabled = true;
            normalAnimator.speed = 1f;
            normalAnimator.applyRootMotion = false;
            normalAnimator.CrossFadeInFixedTime(trimmedStateName, defaultBlendSeconds, 0, 0f);
            normalAnimator.Update(0f);
            positionController?.MoveToHome();

            yield return null;

            while (normalAnimator != null && normalAnimator.IsInTransition(0))
            {
                positionController?.MoveToHome();
                yield return null;
            }

            float startedAt = Time.time;
            while (normalAnimator != null && Time.time - startedAt < 10f)
            {
                AnimatorStateInfo state = normalAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName(trimmedStateName) && state.normalizedTime >= 1f)
                {
                    break;
                }

                positionController?.MoveToHome();
                yield return null;
            }

            if (normalAnimator != null && !string.IsNullOrWhiteSpace(defaultIdleStateName))
            {
                normalAnimator.CrossFadeInFixedTime(defaultIdleStateName, defaultBlendSeconds, 0, 0f);
                normalAnimator.Update(0f);
                positionController?.MoveToHome();
            }

            isPlayingIdleVariation = false;
            isDelayingDialogueResponse = false;
        }

        private bool CanRunIdleVariationLoop()
        {
            return enableIdleVariations &&
                   normalAnimator != null &&
                   normalAnimator.enabled &&
                   normalCat != null &&
                   normalCat.activeInHierarchy &&
                   !isPlayingExternalMotion &&
                   !IsManagedByPresentationController();
        }

        private bool IsManagedByPresentationController()
        {
            if (normalCat == null)
            {
                return false;
            }

            if (presentationMode == null || presentationMode.NormalCat != normalCat)
            {
                presentationMode = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include);
            }

            return presentationMode != null &&
                   presentationMode.enabled &&
                   presentationMode.NormalCat == normalCat &&
                   presentationMode.CurrentMode == CatPresentationMode.NormalConversation;
        }

        private bool CanPlayIdleVariation(bool requiresNoConversation)
        {
            if (!CanRunIdleVariationLoop() || isPlayingIdleVariation || normalAnimator.IsInTransition(0))
            {
                return false;
            }

            if (requiresNoConversation && chatUI != null && (chatUI.IsInDialogueMode || chatUI.IsTyping))
            {
                return false;
            }

            AnimatorStateInfo state = normalAnimator.GetCurrentAnimatorStateInfo(0);
            return string.IsNullOrWhiteSpace(defaultIdleStateName) || state.IsName(defaultIdleStateName);
        }

        private static float RandomInterval(float minSeconds, float maxSeconds)
        {
            float min = Mathf.Max(0.1f, minSeconds);
            float max = Mathf.Max(min, maxSeconds);
            return Random.Range(min, max);
        }

        private static bool ShouldKeepNormalCatActiveForStartup()
        {
            return OpenBetaSceneNames.IsPvMorningScene(SceneManager.GetActiveScene().name);
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

            // CatPresentationModeController owns the active location. Only provide the
            // legacy table anchor when the position controller has not been configured yet.
            if (normalCatHomeAnchor != null && positionController.TableHomeAnchor == null)
            {
                positionController.SetHomeAnchor(normalCatHomeAnchor);
            }

            normalAnimator ??= normalCat.GetComponentInChildren<Animator>(true);
            lookRig ??= normalCat.GetComponentInChildren<NekomataLookRigController>(true);
        }

        private void ApplyNormalCatSafetySettings()
        {
            if (normalAnimator != null && disableNormalCatRootMotion)
            {
                normalAnimator.applyRootMotion = false;
            }

            if (normalCat == null)
            {
                return;
            }

            PlayableDirector[] directors = normalCat.GetComponentsInChildren<PlayableDirector>(true);
            foreach (PlayableDirector director in directors)
            {
                if (director == null)
                {
                    continue;
                }

                director.Stop();
                director.enabled = false;
            }
        }
    }
}
