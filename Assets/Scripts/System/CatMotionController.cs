using System;
using System.Collections;
using UnityEngine;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class CatMotionController : MonoBehaviour
    {
        [Serializable]
        private struct MotionBinding
        {
            public string motionId;
            public string animatorStateName;
            public float blendSeconds;
            public bool returnToIdle;
        }

        [SerializeField] private Animator animator;
        [SerializeField] private CatPositionController positionController;
        [SerializeField] private string idleStateName = "CatSimple_Lie_belly_loop_1";
        [SerializeField] private float defaultBlendSeconds = 0.15f;
        [SerializeField] private MotionBinding[] motions = Array.Empty<MotionBinding>();

        private Coroutine returnToIdleCoroutine;
        private string currentMotionId;
        private string currentLocomotionState;

        public bool IsPlayingMotion { get; private set; }
        /// <summary>
        /// True while the Animator is being driven through the transform-neutral
        /// locomotion path. This is deliberately independent from conversation motion.
        /// </summary>
        public bool IsPlayingLocomotion => !string.IsNullOrEmpty(currentLocomotionState);
        public string CurrentLocomotionState => currentLocomotionState;

        private void Awake()
        {
            ResolveReferences();
            DisableRootMotion();
        }

        public void PlayMotion(string motionId)
        {
            ResolveReferences();
            DisableRootMotion();
            ClearLocomotionState();

            if (string.IsNullOrWhiteSpace(motionId))
            {
                PlayIdle();
                return;
            }

            if (animator == null)
            {
                Debug.LogWarning($"[CatMotionController] PlayMotion skipped: Animator is missing. motionId={motionId}", this);
                return;
            }

            MotionBinding binding = ResolveBinding(motionId.Trim());
            string stateName = string.IsNullOrWhiteSpace(binding.animatorStateName)
                ? motionId.Trim()
                : binding.animatorStateName.Trim();
            float blendSeconds = binding.blendSeconds > 0f ? binding.blendSeconds : defaultBlendSeconds;

            PlayAnimatorState(stateName, blendSeconds);
            currentMotionId = motionId.Trim();
            IsPlayingMotion = !string.Equals(stateName, idleStateName, StringComparison.Ordinal);

            if (returnToIdleCoroutine != null)
            {
                StopCoroutine(returnToIdleCoroutine);
                returnToIdleCoroutine = null;
            }

            if (binding.returnToIdle && IsPlayingMotion)
            {
                returnToIdleCoroutine = StartCoroutine(ReturnToIdleAfterCurrentMotion());
            }
        }

        public void PlayIdle()
        {
            ResolveReferences();
            DisableRootMotion();
            ClearLocomotionState();

            if (returnToIdleCoroutine != null)
            {
                StopCoroutine(returnToIdleCoroutine);
                returnToIdleCoroutine = null;
            }

            if (animator != null && !string.IsNullOrWhiteSpace(idleStateName))
            {
                PlayAnimatorState(idleStateName, defaultBlendSeconds);
            }

            currentMotionId = string.Empty;
            IsPlayingMotion = false;
        }

        public void ReturnToIdle()
        {
            PlayIdle();
        }

        /// <summary>
        /// Plays an in-place locomotion state without changing the normal cat's
        /// conversation home transform. Conversation and Timeline motions take priority;
        /// callers must wait until <see cref="IsPlayingMotion"/> is false.
        /// </summary>
        public bool PlayLocomotion(string stateName, float blendSeconds)
        {
            ResolveReferences();
            DisableRootMotion();

            if (string.IsNullOrWhiteSpace(stateName))
            {
                Debug.LogWarning("[CatMotionController] PlayLocomotion skipped: stateName is empty.", this);
                return false;
            }

            if (IsPlayingMotion)
            {
                Debug.LogWarning($"[CatMotionController] PlayLocomotion skipped while conversation motion is active. state={stateName}", this);
                return false;
            }

            if (!TryPlayAnimatorStateWithoutHomeSnap(stateName.Trim(), blendSeconds))
            {
                return false;
            }

            currentLocomotionState = stateName.Trim();
            return true;
        }

        /// <summary>
        /// Ends transform-neutral locomotion and returns to the configured idle state
        /// without snapping to a conversation home anchor.
        /// </summary>
        public bool StopLocomotionAndReturnToIdle()
        {
            ResolveReferences();
            DisableRootMotion();

            if (!IsPlayingLocomotion)
            {
                return false;
            }

            ClearLocomotionState();

            return !string.IsNullOrWhiteSpace(idleStateName) &&
                   TryPlayAnimatorStateWithoutHomeSnap(idleStateName, defaultBlendSeconds);
        }

        public void Configure(Animator normalAnimator, CatPositionController normalPositionController, string defaultIdleStateName = null)
        {
            animator = normalAnimator;
            positionController = normalPositionController;
            if (!string.IsNullOrWhiteSpace(defaultIdleStateName))
            {
                idleStateName = defaultIdleStateName;
            }

            DisableRootMotion();
        }

        private MotionBinding ResolveBinding(string motionId)
        {
            for (int i = 0; i < motions.Length; i++)
            {
                if (string.Equals(motions[i].motionId, motionId, StringComparison.OrdinalIgnoreCase))
                {
                    return motions[i];
                }
            }

            return new MotionBinding
            {
                motionId = motionId,
                animatorStateName = motionId,
                blendSeconds = defaultBlendSeconds,
                returnToIdle = false
            };
        }

        private void PlayAnimatorState(string stateName, float blendSeconds)
        {
            PrepareAnimator();
            PlayAnimatorStateUnchecked(stateName, blendSeconds);
            positionController?.SnapToHome();
        }

        private bool TryPlayAnimatorStateWithoutHomeSnap(string stateName, float blendSeconds)
        {
            if (animator == null)
            {
                Debug.LogWarning($"[CatMotionController] Locomotion skipped: Animator is missing. state={stateName}", this);
                return false;
            }

            int stateHash = Animator.StringToHash(stateName);
            int fullPathHash = Animator.StringToHash($"Base Layer.{stateName}");
            if (!animator.HasState(0, stateHash) && !animator.HasState(0, fullPathHash))
            {
                Debug.LogWarning($"[CatMotionController] Locomotion skipped: state '{stateName}' does not exist on layer 0.", this);
                return false;
            }

            PrepareAnimator();
            PlayAnimatorStateUnchecked(animator.HasState(0, stateHash) ? stateHash : fullPathHash, blendSeconds);
            return true;
        }

        private void PrepareAnimator()
        {
            animator.enabled = true;
            animator.speed = 1f;
            animator.applyRootMotion = false;
        }

        private void PlayAnimatorStateUnchecked(string stateName, float blendSeconds)
        {
            PlayAnimatorStateUnchecked(Animator.StringToHash(stateName), blendSeconds);
        }

        private void PlayAnimatorStateUnchecked(int stateHash, float blendSeconds)
        {
            if (blendSeconds <= 0f)
            {
                animator.Play(stateHash, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(stateHash, blendSeconds, 0, 0f);
            }

            animator.Update(0f);
        }

        private void ClearLocomotionState()
        {
            currentLocomotionState = string.Empty;
        }

        private IEnumerator ReturnToIdleAfterCurrentMotion()
        {
            yield return null;

            while (animator != null && animator.IsInTransition(0))
            {
                positionController?.SnapToHome();
                yield return null;
            }

            while (animator != null)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.loop || state.normalizedTime >= 1f)
                {
                    break;
                }

                positionController?.SnapToHome();
                yield return null;
            }

            returnToIdleCoroutine = null;
            if (IsPlayingMotion && !string.IsNullOrEmpty(currentMotionId))
            {
                PlayIdle();
            }
        }

        private void ResolveReferences()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (positionController == null)
            {
                positionController = GetComponent<CatPositionController>();
            }
        }

        private void DisableRootMotion()
        {
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }
    }
}
