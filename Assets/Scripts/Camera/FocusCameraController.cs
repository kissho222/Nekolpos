using Nekolpos.System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;

namespace Nekolpos.CameraSystem
{
    [DefaultExecutionOrder(50)]
    public sealed class FocusCameraController : MonoBehaviour
    {
        public static FocusCameraController Instance { get; private set; }

        [Header("References")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private NekolposDepthOfFieldController depthOfFieldController;

        [Header("Raycast")]
        [SerializeField] private LayerMask focusRaycastMask = ~0;
        [SerializeField] private float maxFocusRayDistance = 120f;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

        [Header("Focus")]
        [SerializeField] private bool focusEnabled = true;
        [SerializeField] private float defaultFocusDistance = 8f;
        [SerializeField] private float minFocusDistance = 0.4f;
        [SerializeField] private float maxFocusDistance = 80f;
        [SerializeField] private float focusSmoothTime = 0.45f;
        [SerializeField] private float effectSmoothTime = 0.35f;
        [SerializeField] private bool disableWhenPointerOverUi = true;
        [SerializeField] private bool disableDuringTextInput = true;

        private FocusTarget forcedFocusTarget;
        private Transform forcedTransformTarget;
        private float currentFocusDistance;
        private float focusDistanceVelocity;
        private float currentEffectWeight;
        private float effectWeightVelocity;

        public FocusTarget CurrentFocusTarget { get; private set; }
        public bool HasExternalFocus => forcedFocusTarget != null || forcedTransformTarget != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(FocusCameraController)}] Multiple instances found. Using the latest active instance.", this);
            }

            Instance = this;
            ResolveReferences();
            ConfigureCameraForPostProcessing();
            currentFocusDistance = Mathf.Clamp(defaultFocusDistance, minFocusDistance, maxFocusDistance);
        }

        private void OnEnable()
        {
            ResolveReferences();
            ConfigureCameraForPostProcessing();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (depthOfFieldController != null)
            {
                depthOfFieldController.ClearFocusTarget();
                depthOfFieldController.SetModeDefault();
            }
        }

        private void OnValidate()
        {
            maxFocusRayDistance = Mathf.Max(0.1f, maxFocusRayDistance);
            minFocusDistance = Mathf.Max(0.1f, minFocusDistance);
            maxFocusDistance = Mathf.Max(minFocusDistance, maxFocusDistance);
            defaultFocusDistance = Mathf.Clamp(defaultFocusDistance, minFocusDistance, maxFocusDistance);
            focusSmoothTime = Mathf.Max(0.01f, focusSmoothTime);
            effectSmoothTime = Mathf.Max(0.01f, effectSmoothTime);
        }

        private void LateUpdate()
        {
            ResolveReferences();
            ConfigureCameraForPostProcessing();

            bool hasExternalFocus = HasExternalFocus;
            bool uiSuppressed = IsFocusSuppressedByUi();
            bool canUseExternalFocus = focusEnabled && hasExternalFocus;
            bool canUseManualFocus = focusEnabled && !uiSuppressed;
            FocusTarget manualTarget = canUseManualFocus && Input.GetMouseButton(1) ? RaycastFocusTarget() : null;
            FocusTarget resolvedTarget = canUseExternalFocus || canUseManualFocus ? ResolveActiveFocusTarget(manualTarget) : null;

            CurrentFocusTarget = resolvedTarget;

            bool hasTransformTarget = canUseExternalFocus && resolvedTarget == null && forcedTransformTarget != null;
            Vector3 focusPoint = resolvedTarget != null
                ? resolvedTarget.FocusPoint
                : hasTransformTarget
                    ? forcedTransformTarget.position
                    : transform.position + transform.forward * defaultFocusDistance;

            float targetDistance = targetCamera != null
                ? Vector3.Distance(targetCamera.transform.position, focusPoint)
                : defaultFocusDistance;
            targetDistance = Mathf.Clamp(targetDistance, minFocusDistance, maxFocusDistance);

            float targetEffectWeight = focusEnabled && (resolvedTarget != null || hasTransformTarget || manualTarget != null) ? 1f : 0f;
            currentFocusDistance = Mathf.SmoothDamp(
                currentFocusDistance,
                targetDistance,
                ref focusDistanceVelocity,
                focusSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            currentEffectWeight = Mathf.SmoothDamp(
                currentEffectWeight,
                targetEffectWeight,
                ref effectWeightVelocity,
                effectSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            ApplyFocusToDepthOfFieldController(currentFocusDistance, currentEffectWeight);
        }

        public void SetFocusTarget(Transform target)
        {
            forcedTransformTarget = target;
            forcedFocusTarget = target != null ? target.GetComponentInParent<FocusTarget>() : null;
        }

        public void SetFocusTargetById(string id)
        {
            forcedTransformTarget = null;
            forcedFocusTarget = FocusTarget.TryFindById(id, out FocusTarget target) ? target : null;
        }

        public void ClearFocus()
        {
            forcedFocusTarget = null;
            forcedTransformTarget = null;
        }

        public void SetFocusEnabled(bool enabled)
        {
            focusEnabled = enabled;
            if (!enabled)
            {
                ClearFocus();
            }
        }

        public void FocusCatEyes()
        {
            SetFocusTargetById("FocusCatEyes");
        }

        public void FocusCatPaw()
        {
            SetFocusTargetById("FocusCatPaw");
        }

        public void FocusCatNose()
        {
            SetFocusTargetById("FocusCatNose");
        }

        public void FocusCatTail()
        {
            SetFocusTargetById("FocusCatTail");
        }

        private FocusTarget ResolveActiveFocusTarget(FocusTarget manualTarget)
        {
            if (forcedFocusTarget != null && forcedFocusTarget.isActiveAndEnabled)
            {
                return forcedFocusTarget;
            }

            if (manualTarget != null)
            {
                return manualTarget;
            }

            return null;
        }

        private FocusTarget RaycastFocusTarget()
        {
            if (targetCamera == null)
            {
                return null;
            }

            Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, maxFocusRayDistance, focusRaycastMask, triggerInteraction))
            {
                return null;
            }

            FocusTarget target = hit.collider.GetComponentInParent<FocusTarget>();
            return target != null && target.AutoFocusable ? target : null;
        }

        private bool IsFocusSuppressedByUi()
        {
            if (disableWhenPointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return true;
            }

            if (!disableDuringTextInput || chatUI == null || chatUI.chatInputField == null)
            {
                return false;
            }

            return chatUI.chatInputField.gameObject.activeInHierarchy && chatUI.chatInputField.isFocused;
        }

        private void ResolveReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>() != null ? GetComponent<Camera>() : Camera.main;
            }

            if (depthOfFieldController == null)
            {
                depthOfFieldController = NekolposDepthOfFieldController.Instance != null
                    ? NekolposDepthOfFieldController.Instance
                    : Object.FindFirstObjectByType<NekolposDepthOfFieldController>();
            }

            if (chatUI == null)
            {
                chatUI = Object.FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
            }
        }

        private void ConfigureCameraForPostProcessing()
        {
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.depthTextureMode |= DepthTextureMode.Depth;

            UniversalAdditionalCameraData cameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                return;
            }

            cameraData.renderPostProcessing = true;
            cameraData.requiresDepthTexture = true;
        }

        private void ApplyFocusToDepthOfFieldController(float focusDistance, float effectWeight)
        {
            if (depthOfFieldController == null)
            {
                return;
            }

            if (focusEnabled && effectWeight > 0.02f)
            {
                depthOfFieldController.SetFocusDistance(focusDistance);
                depthOfFieldController.SetModeFocus();
                return;
            }

            depthOfFieldController.ClearFocusTarget();
            depthOfFieldController.SetModeDefault();
        }
    }
}
