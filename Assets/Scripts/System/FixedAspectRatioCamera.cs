using UnityEngine;

namespace Nekolpos.System
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FixedAspectRatioCamera : MonoBehaviour
    {
        [SerializeField] private float targetAspectWidth = 16f;
        [SerializeField] private float targetAspectHeight = 9f;

        private Camera targetCamera;
        private int cachedScreenWidth = -1;
        private int cachedScreenHeight = -1;
        private float cachedAspectWidth = -1f;
        private float cachedAspectHeight = -1f;

        private void OnEnable()
        {
            ApplyAspect(force: true);
        }

        private void OnValidate()
        {
            targetAspectWidth = Mathf.Max(1f, targetAspectWidth);
            targetAspectHeight = Mathf.Max(1f, targetAspectHeight);
            ApplyAspect(force: true);
        }

        private void Update()
        {
            ApplyAspect(force: false);
        }

        private void OnDisable()
        {
            Camera cameraComponent = GetTargetCamera();
            if (cameraComponent != null)
            {
                cameraComponent.rect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private void ApplyAspect(bool force)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            if (!force
                && Screen.width == cachedScreenWidth
                && Screen.height == cachedScreenHeight
                && Mathf.Approximately(targetAspectWidth, cachedAspectWidth)
                && Mathf.Approximately(targetAspectHeight, cachedAspectHeight))
            {
                return;
            }

            Camera cameraComponent = GetTargetCamera();
            if (cameraComponent == null)
            {
                return;
            }

            float targetAspect = targetAspectWidth / targetAspectHeight;
            float currentAspect = (float)Screen.width / Screen.height;
            Rect nextRect = new Rect(0f, 0f, 1f, 1f);

            if (currentAspect > targetAspect)
            {
                float normalizedWidth = targetAspect / currentAspect;
                nextRect.width = normalizedWidth;
                nextRect.x = (1f - normalizedWidth) * 0.5f;
            }
            else if (currentAspect < targetAspect)
            {
                float normalizedHeight = currentAspect / targetAspect;
                nextRect.height = normalizedHeight;
                nextRect.y = (1f - normalizedHeight) * 0.5f;
            }

            cameraComponent.rect = nextRect;
            cachedScreenWidth = Screen.width;
            cachedScreenHeight = Screen.height;
            cachedAspectWidth = targetAspectWidth;
            cachedAspectHeight = targetAspectHeight;
        }

        private Camera GetTargetCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            return targetCamera;
        }
    }
}
