using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Nekolpos.System
{
    /// <summary>
    /// Adds a temporary, smoothed look-at layer on top of the OP Timeline camera pose.
    /// The Timeline remains the owner of position and post-impact camera motion.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class OpeningCatLookAtController : MonoBehaviour
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("OP猫を追う演出を有効にします。")]
            public bool enabled;
            [Min(0f)] public float lookAtStartTime;
            [Min(0.01f)] public float lookAtFullWeightTime;
            [Min(0f)] public float lookAtReleaseStartTime;
            [Min(0.01f)] public float lookAtReleaseEndTime;
            [Min(1f)] public float rotationDegreesPerSecond;
            [Min(0.01f)] public float targetPositionSmoothTime;
            [Tooltip("OP中に使う遠距離用FOVです。")]
            [Range(1f, 179f)] public float distantFov;
            [Range(1f, 179f)] public float normalFov;
            [Min(0.01f)] public float fovZoomInEndTime;
            [Min(0f)] public float fovRestoreStartTime;
            [Min(0.01f)] public float fovRestoreEndTime;

            public static Settings Default => new Settings
            {
                enabled = true,
                lookAtStartTime = 0f,
                lookAtFullWeightTime = 0.35f,
                lookAtReleaseStartTime = 5.55f,
                lookAtReleaseEndTime = 5.83f,
                rotationDegreesPerSecond = 360f,
                targetPositionSmoothTime = 0.08f,
                distantFov = 48f,
                normalFov = 65f,
                fovZoomInEndTime = 0.35f,
                fovRestoreStartTime = 4.7f,
                fovRestoreEndTime = 5.7f
            };

            public void Validate()
            {
                lookAtStartTime = Mathf.Max(0f, lookAtStartTime);
                lookAtFullWeightTime = Mathf.Max(lookAtStartTime + 0.01f, lookAtFullWeightTime);
                lookAtReleaseStartTime = Mathf.Max(lookAtFullWeightTime, lookAtReleaseStartTime);
                lookAtReleaseEndTime = Mathf.Max(lookAtReleaseStartTime + 0.01f, lookAtReleaseEndTime);
                rotationDegreesPerSecond = Mathf.Max(1f, rotationDegreesPerSecond);
                targetPositionSmoothTime = Mathf.Max(0.01f, targetPositionSmoothTime);
                normalFov = Mathf.Clamp(normalFov, 1f, 179f);
                distantFov = Mathf.Clamp(distantFov, 1f, 179f);
                fovZoomInEndTime = Mathf.Max(0.01f, fovZoomInEndTime);
                fovRestoreStartTime = Mathf.Max(fovZoomInEndTime, fovRestoreStartTime);
                fovRestoreEndTime = Mathf.Max(fovRestoreStartTime + 0.01f, fovRestoreEndTime);
            }
        }

        private PlayableDirector director;
        private Transform cameraPivot;
        private Camera targetCamera;
        private Transform focusTarget;
        private NekolposDepthOfFieldController depthOfFieldController;
        private Settings settings;
        private Vector3 smoothedFocusPosition;
        private Vector3 focusPositionVelocity;
        private Quaternion smoothedLookRotation;
        private bool hasLookState;
        private bool wasPlaying;

        public void Configure(
            PlayableDirector sourceDirector,
            Transform pivot,
            Camera camera,
            Transform explicitFocusTarget,
            Settings configuredSettings)
        {
            director = sourceDirector;
            cameraPivot = pivot;
            targetCamera = camera;
            focusTarget = explicitFocusTarget != null ? explicitFocusTarget : FindDescendant(transform, "head");
            depthOfFieldController = NekolposDepthOfFieldController.Instance != null
                ? NekolposDepthOfFieldController.Instance
                : FindFirstObjectByType<NekolposDepthOfFieldController>();
            settings = configuredSettings;
            settings.Validate();
            hasLookState = false;
            wasPlaying = false;
        }

        private void OnDisable()
        {
            EndOpeningDepthOfFieldFocus();
            RestoreNormalFov();
            hasLookState = false;
            wasPlaying = false;
        }

        private void LateUpdate()
        {
            if (!settings.enabled || director == null || cameraPivot == null || targetCamera == null || focusTarget == null)
            {
                return;
            }

            bool isPlaying = director.state == PlayState.Playing;
            if (!isPlaying)
            {
                if (wasPlaying)
                {
                    EndOpeningDepthOfFieldFocus();
                    RestoreNormalFov();
                    hasLookState = false;
                }

                wasPlaying = false;
                return;
            }

            wasPlaying = true;
            float timelineTime = Mathf.Max(0f, (float)director.time);
            UpdateOpeningDepthOfFieldFocus(timelineTime);
            ApplyFov(timelineTime);

            float weight = CalculateLookAtWeight(timelineTime);
            if (weight <= 0f)
            {
                return;
            }

            // LateUpdate runs after the Timeline has evaluated the existing CameraEventPivot track.
            // Read that pose first, then only layer this frame's rotational look-at over it.
            Quaternion timelineRotation = cameraPivot.rotation;
            Vector3 targetPosition = focusTarget.position;
            if (!hasLookState)
            {
                smoothedFocusPosition = targetPosition;
                smoothedLookRotation = timelineRotation;
                hasLookState = true;
            }

            smoothedFocusPosition = Vector3.SmoothDamp(
                smoothedFocusPosition,
                targetPosition,
                ref focusPositionVelocity,
                settings.targetPositionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            Vector3 lookDirection = smoothedFocusPosition - cameraPivot.position;
            if (lookDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Quaternion requestedRotation = Quaternion.LookRotation(lookDirection.normalized, timelineRotation * Vector3.up);
            smoothedLookRotation = Quaternion.RotateTowards(
                smoothedLookRotation,
                requestedRotation,
                settings.rotationDegreesPerSecond * Time.unscaledDeltaTime);
            cameraPivot.rotation = Quaternion.Slerp(timelineRotation, smoothedLookRotation, weight);
        }

        private float CalculateLookAtWeight(float timelineTime)
        {
            if (timelineTime < settings.lookAtStartTime || timelineTime >= settings.lookAtReleaseEndTime)
            {
                return 0f;
            }

            if (timelineTime < settings.lookAtFullWeightTime)
            {
                return SmoothProgress(timelineTime, settings.lookAtStartTime, settings.lookAtFullWeightTime);
            }

            if (timelineTime >= settings.lookAtReleaseStartTime)
            {
                return 1f - SmoothProgress(timelineTime, settings.lookAtReleaseStartTime, settings.lookAtReleaseEndTime);
            }

            return 1f;
        }

        private void ApplyFov(float timelineTime)
        {
            if (timelineTime < settings.lookAtStartTime || timelineTime >= settings.fovRestoreEndTime)
            {
                targetCamera.fieldOfView = settings.normalFov;
                return;
            }

            float fov = settings.distantFov;
            if (timelineTime < settings.fovZoomInEndTime)
            {
                fov = Mathf.Lerp(settings.normalFov, settings.distantFov, SmoothProgress(timelineTime, settings.lookAtStartTime, settings.fovZoomInEndTime));
            }
            else if (timelineTime >= settings.fovRestoreStartTime)
            {
                fov = Mathf.Lerp(settings.distantFov, settings.normalFov, SmoothProgress(timelineTime, settings.fovRestoreStartTime, settings.fovRestoreEndTime));
            }

            targetCamera.fieldOfView = fov;
        }

        private void RestoreNormalFov()
        {
            if (targetCamera != null && settings.normalFov > 0f)
            {
                targetCamera.fieldOfView = settings.normalFov;
            }
        }

        private void UpdateOpeningDepthOfFieldFocus(float timelineTime)
        {
            if (timelineTime < settings.lookAtStartTime || timelineTime >= settings.lookAtReleaseEndTime)
            {
                EndOpeningDepthOfFieldFocus();
                return;
            }

            if (depthOfFieldController == null)
            {
                depthOfFieldController = NekolposDepthOfFieldController.Instance != null
                    ? NekolposDepthOfFieldController.Instance
                    : FindFirstObjectByType<NekolposDepthOfFieldController>();
            }

            depthOfFieldController?.BeginOpeningFocus(focusTarget);
        }

        private void EndOpeningDepthOfFieldFocus()
        {
            depthOfFieldController?.EndOpeningFocus();
        }

        private static float SmoothProgress(float time, float start, float end)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, time));
        }

        private static Transform FindDescendant(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, childName, StringComparison.Ordinal))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
