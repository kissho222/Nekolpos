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

        public bool IsPlayingMotion { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            DisableRootMotion();
        }

        public void PlayMotion(string motionId)
        {
            ResolveReferences();
            DisableRootMotion();

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
            animator.enabled = true;
            animator.speed = 1f;
            animator.applyRootMotion = false;

            if (blendSeconds <= 0f)
            {
                animator.Play(stateName, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(stateName, blendSeconds, 0, 0f);
            }

            animator.Update(0f);
            positionController?.SnapToHome();
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
