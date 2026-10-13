using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nekolpos.System
{
    public enum NekolposDepthOfFieldMode
    {
        Default,
        Focus,
        Disabled
    }

    [DisallowMultipleComponent]
    public sealed class NekolposDepthOfFieldController : MonoBehaviour
    {
        public static NekolposDepthOfFieldController Instance { get; private set; }

        [Header("References")]
        [Tooltip("Depth of Fieldを適用するカメラです。未設定の場合はMainCameraタグのカメラを探します。")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Depth of Field設定を書き込むURP Volumeです。未設定の場合はシーン内のVolumeを探します。")]
        [SerializeField] private Volume targetVolume;
        [Tooltip("カメラやVolumeが未設定のとき、自動でシーン内から探します。")]
        [SerializeField] private bool autoResolveReferences = true;

        [Header("Default Far Blur")]
        [Tooltip("通常時に遠景ぼかしが始まるカメラからの距離です。5cm視点では室内の壁を遠景として扱うため、1m前後から始めると壁がぼけやすくなります。")]
        [SerializeField, Min(0f)] private float defaultBlurStartDistance = 1.2f;
        [Tooltip("通常時に遠景ぼかしが最大に近づくカメラからの距離です。壁や奥の家具をぼかしたい場合は3から5m程度が目安です。")]
        [SerializeField, Min(0.01f)] private float defaultBlurEndDistance = 4f;
        [Tooltip("通常時の遠景ぼかしの強さです。0でOFF相当、1で確認用に強め。壁を見える程度にぼかすなら0.35から0.55程度が目安です。")]
        [SerializeField, Range(0f, 1f)] private float defaultBlurStrength = 0.45f;
        [Tooltip("Gaussian DOFの高品質サンプリングを使います。見た目は安定しますが、低スペックやWebGLでは重くなる可能性があります。")]
        [SerializeField] private bool useHighQualitySampling = false;
        [Header("Focus Settings")]
        [Tooltip("注視・Timeline演出で使うピント距離です。Bokeh通常モードでは猫の前足や顔を残す基準距離として使います。")]
        [SerializeField, Min(0.1f)] private float focusDistance = 0.8f;
        [Tooltip("Bokeh DOFの絞り値です。小さいほど壁や奥家具がぼけやすくなります。")]
        [SerializeField, Range(1f, 32f)] private float aperture = 3.5f;
        [Tooltip("Bokeh DOFの焦点距離です。大きいほどピントが浅くなります。")]
        [SerializeField, Range(1f, 300f)] private float focalLength = 70f;
        [Tooltip("Focusモード時に使うぼかし強度です。通常時とは別に、注視やTimeline演出で一時的に強めたい場合に使います。")]
        [SerializeField, Range(0f, 1f)] private float focusBlurStrength = 0.45f;

        [Header("Smoothing")]
        [Tooltip("ぼかし開始距離や強度が変わるときの補間時間です。短すぎると急に見えて酔いやすくなります。")]
        [SerializeField, Min(0.01f)] private float transitionSeconds = 0.65f;
        [Tooltip("ピント距離や注視対象を追従するときの補間時間です。将来の手動ピントやTimeline演出で使います。")]
        [SerializeField, Min(0.01f)] private float focusFollowSeconds = 0.45f;
        [Tooltip("1秒あたりにピント距離が変化できる最大量です。急激なピント移動を防ぎます。")]
        [SerializeField, Min(0.01f)] private float maxFocusDistanceChangePerSecond = 4f;

        [Header("Runtime")]
        [Tooltip("開始時のDepth of Fieldモードです。Defaultは通常遠景ぼかし、Focusは将来の注視用、Disabledは完全OFFです。")]
        [SerializeField] private NekolposDepthOfFieldMode initialMode = NekolposDepthOfFieldMode.Default;
        [Tooltip("Depth of Field全体のON/OFFです。UI表示中や低スペック環境でOFFにできます。")]
        [SerializeField] private bool depthOfFieldEnabled = true;
        [Tooltip("VolumeやDepthOfField設定が見つからない場合に警告ログを出します。エラー停止はしません。")]
        [SerializeField] private bool logMissingVolumeWarnings = true;

        [Header("Opening Timeline Gate")]
        [Tooltip("OP用猫が表示されている間はDepth of Fieldを無効化し、通常猫に切り替わってから有効化します。")]
        [SerializeField] private bool disableWhileOpeningCatActive = true;
        [SerializeField] private GameObject openingCat;
        [SerializeField] private GameObject normalCat;

        [Header("Opening Focus")]
        [Tooltip("OP猫注視中だけ、注視Targetの距離からGaussian DoFの遠景ぼかし開始位置を決める余白です。猫自身をぼかさないため、通常は0以上にします。")]
        [SerializeField, Min(0f)] private float openingFocusStartOffset = 0.15f;
        [Tooltip("OP猫注視中に、ぼかし開始位置から最大ぼかしに達するまでの距離です。短いほど猫の背後が早くぼけます。")]
        [SerializeField, Min(0.01f)] private float openingFocusFalloffDistance = 1.5f;
        [Tooltip("OP猫注視中のGaussian DoF強度です。通常時の遠景ぼかしとは独立して調整できます。")]
        [SerializeField, Range(0f, 1f)] private float openingFocusBlurStrength = 0.45f;
        [Tooltip("OP猫の移動にDoF範囲が追従する補間時間です。短いほど接近時の追従が速くなります。")]
        [SerializeField, Min(0.01f)] private float openingFocusTransitionSeconds = 0.2f;

        private DepthOfField depthOfField;
        private NekolposDepthOfFieldMode mode;
        private Transform focusTarget;
        private bool warnedMissingVolume;
        private bool warnedMissingDepthOfField;
        private bool initialized;

        private float currentStart;
        private float currentEnd;
        private float currentStrength;
        private float currentFocusDistance;
        private float currentAperture;
        private float currentFocalLength;

        private float startVelocity;
        private float endVelocity;
        private float strengthVelocity;
        private float focusDistanceVelocity;
        private float apertureVelocity;
        private float focalLengthVelocity;

        private bool hasTemporaryFocus;
        private float temporaryFocusDistance;
        private float temporaryFocusRemaining;
        private float nextOpeningCatResolveTime;
        private Transform openingFocusTarget;
        private bool openingFocusActive;

        public NekolposDepthOfFieldMode Mode => mode;
        public bool DepthOfFieldEnabled => depthOfFieldEnabled;
        public float FocusDistance => focusDistance;
        public float DefaultBlurStrength => defaultBlurStrength;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            mode = initialMode;
            Initialize();
            SnapToTargets();
            ApplyCurrentValues();
        }

        private void OnEnable()
        {
            Initialize();
            ConfigureCameraForDepthOfField();
            ApplyCurrentValues();
        }

        private void OnValidate()
        {
            defaultBlurEndDistance = Mathf.Max(defaultBlurEndDistance, defaultBlurStartDistance + 0.01f);
            defaultBlurStrength = Mathf.Clamp01(defaultBlurStrength);
            focusBlurStrength = Mathf.Clamp01(focusBlurStrength);
            openingFocusBlurStrength = Mathf.Clamp01(openingFocusBlurStrength);
            focusDistance = Mathf.Max(0.1f, focusDistance);
            openingFocusFalloffDistance = Mathf.Max(0.01f, openingFocusFalloffDistance);
            openingFocusTransitionSeconds = Mathf.Max(0.01f, openingFocusTransitionSeconds);

            if (!Application.isPlaying)
            {
                initialized = false;
                Initialize();
                ConfigureCameraForDepthOfField();
                SnapToTargets();
                ApplyCurrentValues();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            Initialize();

            if (depthOfField == null)
            {
                return;
            }

            UpdateTemporaryFocus();
            UpdateFocusTargetDistance();
            SmoothValues();
            ApplyCurrentValues();
        }

        public void SetMode(NekolposDepthOfFieldMode nextMode)
        {
            mode = nextMode;
        }

        public void SetModeDefault()
        {
            SetMode(NekolposDepthOfFieldMode.Default);
        }

        public void SetModeFocus()
        {
            SetMode(NekolposDepthOfFieldMode.Focus);
        }

        public void SetModeDisabled()
        {
            SetMode(NekolposDepthOfFieldMode.Disabled);
        }

        public void SetFocusDistance(float distance)
        {
            focusDistance = Mathf.Max(0.1f, distance);
            hasTemporaryFocus = false;
        }

        public void SetDepthOfFieldEnabled(bool enabled)
        {
            depthOfFieldEnabled = enabled;
        }

        public void SetDefaultBlurStrength(float strength)
        {
            defaultBlurStrength = Mathf.Clamp01(strength);
        }

        /// <summary>
        /// OP猫注視中のGaussian DoF追従を開始する。実際のVolume更新はこのControllerだけが行う。
        /// </summary>
        public void BeginOpeningFocus(Transform target)
        {
            openingFocusTarget = target;
            openingFocusActive = target != null;
        }

        /// <summary>
        /// OP猫注視用のDoF追従を終了し、通常のOP猫DoF抑止ルールへ戻す。
        /// </summary>
        public void EndOpeningFocus()
        {
            openingFocusActive = false;
            openingFocusTarget = null;
        }

        public void ResetToDefault()
        {
            hasTemporaryFocus = false;
            focusTarget = null;
            depthOfFieldEnabled = true;
            mode = NekolposDepthOfFieldMode.Default;
        }

        public void SetFocusTarget(Transform target)
        {
            focusTarget = target;
            if (target != null)
            {
                mode = NekolposDepthOfFieldMode.Focus;
            }
        }

        public void ClearFocusTarget()
        {
            focusTarget = null;
        }

        public void SetTemporaryFocusDistance(float distance, float duration)
        {
            temporaryFocusDistance = Mathf.Max(0.1f, distance);
            temporaryFocusRemaining = Mathf.Max(0f, duration);
            hasTemporaryFocus = temporaryFocusRemaining > 0f;

            if (hasTemporaryFocus)
            {
                mode = NekolposDepthOfFieldMode.Focus;
            }
        }

        private void Initialize()
        {
            if (initialized && depthOfField != null)
            {
                return;
            }

            if (autoResolveReferences)
            {
                ResolveReferences();
            }

            if (targetVolume == null || targetVolume.profile == null)
            {
                WarnMissingVolume();
                return;
            }

            if (!targetVolume.profile.TryGet(out depthOfField) || depthOfField == null)
            {
                WarnMissingDepthOfField();
                return;
            }

            initialized = true;
            depthOfField.active = true;
            ConfigureCameraForDepthOfField();
            OverrideDepthOfFieldParameters(true);
        }

        private void ResolveReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetVolume == null)
            {
                targetVolume = Object.FindFirstObjectByType<Volume>();
            }
        }

        private void ConfigureCameraForDepthOfField()
        {
            if (targetCamera == null)
            {
                return;
            }

            UniversalAdditionalCameraData cameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                return;
            }

            cameraData.renderPostProcessing = true;
            cameraData.requiresDepthTexture = true;
        }

        private void WarnMissingVolume()
        {
            if (!logMissingVolumeWarnings || warnedMissingVolume)
            {
                return;
            }

            warnedMissingVolume = true;
            Debug.LogWarning($"[{nameof(NekolposDepthOfFieldController)}] Volume or VolumeProfile is not assigned. Depth of Field control is skipped.", this);
        }

        private void WarnMissingDepthOfField()
        {
            if (!logMissingVolumeWarnings || warnedMissingDepthOfField)
            {
                return;
            }

            warnedMissingDepthOfField = true;
            Debug.LogWarning($"[{nameof(NekolposDepthOfFieldController)}] DepthOfField override is missing from the assigned VolumeProfile.", this);
        }

        private void SnapToTargets()
        {
            currentStart = defaultBlurStartDistance;
            currentEnd = Mathf.Max(defaultBlurEndDistance, defaultBlurStartDistance + 0.01f);
            currentStrength = ResolveTargetStrength();
            currentFocusDistance = ResolveTargetFocusDistance();
            currentAperture = aperture;
            currentFocalLength = focalLength;
        }

        private void SmoothValues()
        {
            ResolveTargetBlurDistances(out float targetStart, out float targetEnd);
            float targetStrength = ResolveTargetStrength();
            float targetFocusDistance = ResolveTargetFocusDistance();
            float blurTransitionSeconds = openingFocusActive
                ? openingFocusTransitionSeconds
                : transitionSeconds;

            currentStart = Mathf.SmoothDamp(currentStart, targetStart, ref startVelocity, blurTransitionSeconds);
            currentEnd = Mathf.SmoothDamp(currentEnd, targetEnd, ref endVelocity, blurTransitionSeconds);
            currentStrength = Mathf.SmoothDamp(currentStrength, targetStrength, ref strengthVelocity, blurTransitionSeconds);
            currentFocusDistance = Mathf.SmoothDamp(
                currentFocusDistance,
                targetFocusDistance,
                ref focusDistanceVelocity,
                focusFollowSeconds,
                maxFocusDistanceChangePerSecond);
            currentAperture = Mathf.SmoothDamp(currentAperture, aperture, ref apertureVelocity, transitionSeconds);
            currentFocalLength = Mathf.SmoothDamp(currentFocalLength, focalLength, ref focalLengthVelocity, transitionSeconds);
        }

        private float ResolveTargetStrength()
        {
            if (!depthOfFieldEnabled || mode == NekolposDepthOfFieldMode.Disabled ||
                (!openingFocusActive && ShouldSuppressDepthOfFieldForOpening()))
            {
                return 0f;
            }

            if (openingFocusActive)
            {
                return Mathf.Clamp01(openingFocusBlurStrength);
            }

            return mode == NekolposDepthOfFieldMode.Focus
                ? Mathf.Clamp01(focusBlurStrength)
                : Mathf.Clamp01(defaultBlurStrength);
        }

        private void ResolveTargetBlurDistances(out float start, out float end)
        {
            if (openingFocusActive && openingFocusTarget != null && targetCamera != null)
            {
                float targetDistance = Vector3.Distance(targetCamera.transform.position, openingFocusTarget.position);
                start = Mathf.Max(0f, targetDistance + openingFocusStartOffset);
                end = start + openingFocusFalloffDistance;
                return;
            }

            start = defaultBlurStartDistance;
            end = Mathf.Max(defaultBlurEndDistance, start + 0.01f);
        }

        private float ResolveTargetFocusDistance()
        {
            if (hasTemporaryFocus)
            {
                return temporaryFocusDistance;
            }

            return Mathf.Max(0.1f, focusDistance);
        }

        private void UpdateTemporaryFocus()
        {
            if (!hasTemporaryFocus)
            {
                return;
            }

            temporaryFocusRemaining -= Time.deltaTime;
            if (temporaryFocusRemaining <= 0f)
            {
                hasTemporaryFocus = false;
                mode = NekolposDepthOfFieldMode.Default;
            }
        }

        private void UpdateFocusTargetDistance()
        {
            if (focusTarget == null || targetCamera == null)
            {
                return;
            }

            Vector3 cameraToTarget = focusTarget.position - targetCamera.transform.position;
            float forwardDistance = Vector3.Dot(targetCamera.transform.forward, cameraToTarget);
            focusDistance = Mathf.Max(0.1f, forwardDistance);
        }

        private void ApplyCurrentValues()
        {
            if (depthOfField == null)
            {
                return;
            }

            OverrideDepthOfFieldParameters(true);

            if (!depthOfFieldEnabled || mode == NekolposDepthOfFieldMode.Disabled ||
                (!openingFocusActive && ShouldSuppressDepthOfFieldForOpening()) || currentStrength <= 0.01f)
            {
                depthOfField.mode.value = DepthOfFieldMode.Off;
                return;
            }

            depthOfField.active = true;
            depthOfField.focusDistance.value = Mathf.Max(0.1f, currentFocusDistance);
            depthOfField.aperture.value = Mathf.Lerp(16f, currentAperture, Mathf.Clamp01(currentStrength));
            depthOfField.focalLength.value = Mathf.Lerp(30f, currentFocalLength, Mathf.Clamp01(currentStrength));

            depthOfField.mode.value = DepthOfFieldMode.Gaussian;
            depthOfField.gaussianStart.value = Mathf.Max(0f, currentStart);
            depthOfField.gaussianEnd.value = Mathf.Max(depthOfField.gaussianStart.value + 0.01f, currentEnd);
            depthOfField.gaussianMaxRadius.value = Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(currentStrength));
            depthOfField.highQualitySampling.value = useHighQualitySampling;
        }

        private void OverrideDepthOfFieldParameters(bool overrideState)
        {
            depthOfField.mode.overrideState = overrideState;
            depthOfField.gaussianStart.overrideState = overrideState;
            depthOfField.gaussianEnd.overrideState = overrideState;
            depthOfField.gaussianMaxRadius.overrideState = overrideState;
            depthOfField.highQualitySampling.overrideState = overrideState;
            depthOfField.focusDistance.overrideState = overrideState;
            depthOfField.aperture.overrideState = overrideState;
            depthOfField.focalLength.overrideState = overrideState;
        }

        private bool ShouldSuppressDepthOfFieldForOpening()
        {
            if (!disableWhileOpeningCatActive || !Application.isPlaying)
            {
                return false;
            }

            ResolveOpeningCatReferences();

            if (openingCat != null && openingCat.activeInHierarchy)
            {
                return true;
            }

            if (normalCat != null && normalCat.activeInHierarchy)
            {
                return false;
            }

            return normalCat != null && !normalCat.activeInHierarchy;
        }

        private void ResolveOpeningCatReferences()
        {
            if (Time.unscaledTime < nextOpeningCatResolveTime && (openingCat != null || normalCat != null))
            {
                return;
            }

            nextOpeningCatResolveTime = Time.unscaledTime + 0.5f;

            CatAnimationRuntime catRuntime = Object.FindFirstObjectByType<CatAnimationRuntime>(FindObjectsInactive.Include);
            if (catRuntime != null)
            {
                openingCat ??= catRuntime.OpCat;
                normalCat ??= catRuntime.NormalCat;
            }

            openingCat ??= FindGameObjectByNameIncludingInactive("OPCat", "OP_Cat", "OP Cat", "OpeningCat", "Opening Cat");
            normalCat ??= FindGameObjectByNameIncludingInactive("Normal_Cat", "NormalCat", "Normal Cat");
        }

        private static GameObject FindGameObjectByNameIncludingInactive(params string[] names)
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == null)
                {
                    continue;
                }

                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (current.name == names[nameIndex])
                    {
                        return current.gameObject;
                    }
                }
            }

            return null;
        }
    }
}
