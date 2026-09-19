using UnityEngine;

namespace Nekolpos.System
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class CatPvWalkPreview : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private GameObject normalCat;
        [SerializeField] private GameObject[] hideObjects = global::System.Array.Empty<GameObject>();
        [SerializeField] private Animator catAnimator;
        [SerializeField] private RuntimeAnimatorController walkAnimatorController;
        [SerializeField] private AnimationClip editorPreviewClip;

        [Header("Walk")]
        [SerializeField] private string walkStateName = "CatSimple_Walk_F_RM";
        [SerializeField] private bool useRootMotion = true;
        [SerializeField] private float animatorSpeed = 1f;
        [SerializeField, Range(0f, 1f)] private float editorPreviewNormalizedTime;

        [Header("PV Override")]
        [SerializeField] private bool activateNormalCat = true;
        [SerializeField] private bool disablePresentationControllers = true;
        [SerializeField] private bool restartIfInterrupted;
        [SerializeField] private Transform manualMoveRoot;
        [SerializeField] private float manualForwardSpeed;

        [Header("Ride Camera")]
        [SerializeField] private Transform cameraRig;
        [SerializeField] private bool followCatWithCamera = true;
        [SerializeField] private Vector3 cameraLocalOffset = new Vector3(0f, 0.45f, 0.18f);
        [SerializeField] private Vector3 cameraLocalEulerAngles = Vector3.zero;

        [Header("Debug")]
        [SerializeField] private bool logRuntimeState = true;

        private int stateHash;
        private float nextLogTime;

        private void OnEnable()
        {
            ApplyEditorPreviewPose();
        }

        private void OnValidate()
        {
            ApplyEditorPreviewPose();
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyPreviewSetup();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyPreviewSetup();
            PlayWalkFromStart();
            LogRuntimeState("started");
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyAnimatorSettings();

            if (manualMoveRoot != null && !Mathf.Approximately(manualForwardSpeed, 0f))
            {
                manualMoveRoot.position += manualMoveRoot.forward * manualForwardSpeed * Time.deltaTime;
            }

            UpdateRideCamera();

            if (restartIfInterrupted && catAnimator != null && !catAnimator.IsInTransition(0) && !IsCurrentWalkState())
            {
                PlayWalkFromStart();
            }

            if (logRuntimeState && Time.time >= nextLogTime)
            {
                LogRuntimeState("tick");
                nextLogTime = Time.time + 2f;
            }
        }

        private void ApplyPreviewSetup()
        {
            if (normalCat != null)
            {
                if (activateNormalCat)
                {
                    normalCat.SetActive(true);
                }

                if (disablePresentationControllers)
                {
                    DisableAllInScene<CatAnimationRuntime>();
                    DisableAllInScene<CatPresentationModeController>();
                }
            }

            for (int i = 0; i < hideObjects.Length; i++)
            {
                if (hideObjects[i] != null)
                {
                    hideObjects[i].SetActive(false);
                }
            }

            if (disablePresentationControllers)
            {
                DisableAllInScene<CatPresentationModeController>();
            }

            if (catAnimator == null && normalCat != null)
            {
                catAnimator = normalCat.GetComponentInChildren<Animator>(true);
            }

            if (catAnimator != null && walkAnimatorController != null)
            {
                catAnimator.runtimeAnimatorController = walkAnimatorController;
            }

            stateHash = Animator.StringToHash($"Base Layer.{walkStateName}");
            ApplyAnimatorSettings();
        }

        private void ApplyAnimatorSettings()
        {
            if (catAnimator == null)
            {
                return;
            }

            catAnimator.enabled = true;
            catAnimator.speed = Mathf.Max(0f, animatorSpeed);
            catAnimator.applyRootMotion = useRootMotion;
        }

        private void PlayWalkFromStart()
        {
            if (catAnimator == null || string.IsNullOrWhiteSpace(walkStateName))
            {
                return;
            }

            ApplyAnimatorSettings();
            int fullPathHash = stateHash != 0 ? stateHash : Animator.StringToHash($"Base Layer.{walkStateName}");
            int shortNameHash = Animator.StringToHash(walkStateName);
            int resolvedHash = catAnimator.HasState(0, fullPathHash) ? fullPathHash : shortNameHash;
            catAnimator.Play(resolvedHash, 0, 0f);
            catAnimator.Update(0f);
            UpdateRideCamera();
        }

        private void ApplyEditorPreviewPose()
        {
            if (Application.isPlaying || !isActiveAndEnabled || normalCat == null)
            {
                return;
            }

            if (activateNormalCat)
            {
                normalCat.SetActive(true);
            }

            for (int i = 0; i < hideObjects.Length; i++)
            {
                if (hideObjects[i] != null)
                {
                    hideObjects[i].SetActive(false);
                }
            }

            if (catAnimator == null)
            {
                catAnimator = normalCat.GetComponentInChildren<Animator>(true);
            }

            if (catAnimator != null && walkAnimatorController != null)
            {
                catAnimator.runtimeAnimatorController = walkAnimatorController;
                catAnimator.applyRootMotion = false;
            }

            ResolveEditorPreviewClip();
            if (editorPreviewClip != null)
            {
                float previewTime = Mathf.Clamp01(editorPreviewNormalizedTime) * editorPreviewClip.length;
                editorPreviewClip.SampleAnimation(normalCat, previewTime);
            }

            UpdateRideCamera();
        }

        private void LogRuntimeState(string phase)
        {
            if (!logRuntimeState)
            {
                return;
            }

            Vector3 catPosition = normalCat != null ? normalCat.transform.position : Vector3.zero;
            Vector3 cameraPosition = cameraRig != null ? cameraRig.position : Vector3.zero;
            bool isWalkState = catAnimator != null && IsCurrentWalkState();
            Debug.Log($"[CatPvWalkPreview] {phase} walk={isWalkState} rootMotion={(catAnimator != null && catAnimator.applyRootMotion)} cat={catPosition:F3} camera={cameraPosition:F3}", this);
        }

        private bool IsCurrentWalkState()
        {
            if (catAnimator == null)
            {
                return false;
            }

            AnimatorStateInfo currentState = catAnimator.GetCurrentAnimatorStateInfo(0);
            int shortNameHash = Animator.StringToHash(walkStateName);
            return currentState.fullPathHash == stateHash || currentState.shortNameHash == shortNameHash;
        }

        private void UpdateRideCamera()
        {
            if (!followCatWithCamera || cameraRig == null || normalCat == null)
            {
                return;
            }

            Transform catTransform = normalCat.transform;
            cameraRig.SetPositionAndRotation(
                catTransform.TransformPoint(cameraLocalOffset),
                catTransform.rotation * Quaternion.Euler(cameraLocalEulerAngles));
        }

        private void ResolveEditorPreviewClip()
        {
            if (editorPreviewClip != null || walkAnimatorController == null || string.IsNullOrWhiteSpace(walkStateName))
            {
                return;
            }

            AnimationClip[] clips = walkAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == walkStateName)
                {
                    editorPreviewClip = clips[i];
                    return;
                }
            }
        }

        private static void DisableAllInScene<T>() where T : Behaviour
        {
            T[] components = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    components[i].enabled = false;
                }
            }
        }
    }
}
