using UnityEngine;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class PettingSurfaceFollower : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Transform pettingGuide;
        [SerializeField] private Transform handRoot;
        [SerializeField] private Transform pettableRoot;
        [SerializeField] private LayerMask pettableMask = ~0;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float surfaceOffset = 0.025f;
        [SerializeField] private Vector3 handLocalEulerAngles = new Vector3(90f, 0f, 0f);
        [SerializeField] private bool followGuideWhenNoHit = true;
        [SerializeField] private bool hideHandWhenNoHit;

        private Renderer[] handRenderers;
        private readonly RaycastHit[] raycastHits = new RaycastHit[16];
        private bool hasInitialHandPose;
        private Vector3 initialHandLocalPosition;
        private Quaternion initialHandLocalRotation;

        private void Awake()
        {
            ResolveMissingReferences();
        }

        private void LateUpdate()
        {
            ResolveMissingReferences();

            if (mainCamera == null || pettingGuide == null || handRoot == null)
            {
                SetHandVisible(false);
                return;
            }

            Vector3 screenPoint = mainCamera.WorldToScreenPoint(pettingGuide.position);
            Ray ray = mainCamera.ScreenPointToRay(screenPoint);

            if (TryRaycastPettable(ray, out RaycastHit hit))
            {
                handRoot.position = hit.point + hit.normal * surfaceOffset;
                ApplyHandRotation();
                SetHandVisible(true);
                return;
            }

            if (hideHandWhenNoHit)
            {
                SetHandVisible(false);
                return;
            }

            if (followGuideWhenNoHit)
            {
                FollowGuideWithoutSurface();
                return;
            }

            RestoreInitialHandPose();
        }

        private void ResolveMissingReferences()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            }

            if (pettingGuide == null)
            {
                GameObject guide = GameObject.Find("PettingGuide");
                pettingGuide = guide != null ? guide.transform : null;
            }

            if (handRoot == null)
            {
                GameObject root = GameObject.Find("HandRoot");
                handRoot = root != null ? root.transform : null;
            }

            CaptureInitialHandPose();

            if (pettableRoot == null)
            {
                GameObject cat = GameObject.Find("Normal_Cat") ??
                                 GameObject.Find("Normal_Cat_NerukoTail_Test") ??
                                 GameObject.Find("OP_Cat_B_NerukoTail_Test");
                pettableRoot = cat != null ? cat.transform : null;
            }

            if (handRenderers == null && handRoot != null)
            {
                handRenderers = handRoot.GetComponentsInChildren<Renderer>(true);
            }
        }

        private void CaptureInitialHandPose()
        {
            if (hasInitialHandPose || handRoot == null)
            {
                return;
            }

            initialHandLocalPosition = handRoot.localPosition;
            initialHandLocalRotation = handRoot.localRotation;
            hasInitialHandPose = true;
        }

        private void RestoreInitialHandPose()
        {
            if (!hasInitialHandPose || handRoot == null)
            {
                return;
            }

            handRoot.localPosition = initialHandLocalPosition;
            ApplyHandRotation();
            SetHandVisible(true);
        }

        private void FollowGuideWithoutSurface()
        {
            handRoot.position = pettingGuide.position;
            ApplyHandRotation();
            SetHandVisible(true);
        }

        private void ApplyHandRotation()
        {
            handRoot.localRotation = Quaternion.Euler(handLocalEulerAngles);
        }

        private bool TryRaycastPettable(Ray ray, out RaycastHit selectedHit)
        {
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                raycastHits,
                maxDistance,
                pettableMask,
                QueryTriggerInteraction.Ignore);

            selectedHit = default;
            if (hitCount <= 0)
            {
                return false;
            }

            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = raycastHits[i];
                if (hit.collider == null || !IsPettableCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    selectedHit = hit;
                }
            }

            return closestDistance < float.PositiveInfinity;
        }

        private bool IsPettableCollider(Collider targetCollider)
        {
            if (pettableRoot == null)
            {
                return true;
            }

            Transform target = targetCollider.transform;
            return target == pettableRoot || target.IsChildOf(pettableRoot);
        }

        private void SetHandVisible(bool visible)
        {
            if (handRenderers == null)
            {
                return;
            }

            for (int i = 0; i < handRenderers.Length; i++)
            {
                if (handRenderers[i] != null)
                {
                    handRenderers[i].enabled = visible;
                }
            }
        }
    }
}
