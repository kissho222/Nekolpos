using UnityEngine;
using UnityEngine.Serialization;

namespace Nekolpos.System
{
    public enum CatHomeLocation
    {
        Table,
        Desk,
        Bathroom,
        Bed
    }

    /// <summary>
    /// Owns the root transform for the normal conversation cat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatPositionController : MonoBehaviour
    {
        [Tooltip("ちゃぶ台側の基本会話位置。既定の拠点です。")]
        [SerializeField, FormerlySerializedAs("homeAnchor")] private Transform tableHomeAnchor;
        [Tooltip("机側の基本会話位置。")]
        [SerializeField] private Transform deskHomeAnchor;
        [Tooltip("浴室側の基本会話位置。")]
        [SerializeField] private Transform bathroomHomeAnchor;
        [Tooltip("ベッド側の基本会話位置。")]
        [SerializeField] private Transform bedHomeAnchor;
        [Tooltip("現在選択中の基本会話拠点。")]
        [SerializeField] private CatHomeLocation currentHomeLocation = CatHomeLocation.Table;
        [SerializeField] private bool captureInitialTransformAsHome = true;

        private Vector3 initialHomePosition;
        private Quaternion initialHomeRotation;
        private bool hasInitialHome;

        private Transform currentHomeAnchor;
        private static CatPositionController activeConversationLocationController;

        public static event global::System.Action<CatHomeLocation> CurrentConversationLocationChanged;

        /// <summary>
        /// Returns the location already owned by the active normal-conversation cat controller.
        /// This is a state lookup only; it never derives a location from transforms or distance.
        /// </summary>
        public static CatHomeLocation CurrentConversationLocation =>
            activeConversationLocationController != null
                ? activeConversationLocationController.currentHomeLocation
                : CatHomeLocation.Table;

        public Transform HomeAnchor
        {
            get => currentHomeAnchor != null ? currentHomeAnchor : ResolveHomeAnchor(currentHomeLocation);
            set
            {
                tableHomeAnchor = value;
                SetHomeLocation(CatHomeLocation.Table);
            }
        }

        public CatHomeLocation CurrentHomeLocation => currentHomeLocation;
        public Transform TableHomeAnchor => tableHomeAnchor;
        public Transform DeskHomeAnchor => deskHomeAnchor;
        public Transform BathroomHomeAnchor => bathroomHomeAnchor;
        public Transform BedHomeAnchor => bedHomeAnchor;

        private void Awake()
        {
            CaptureInitialHomeIfNeeded();
            activeConversationLocationController = this;
            CurrentConversationLocationChanged?.Invoke(currentHomeLocation);
        }

        private void OnDestroy()
        {
            if (activeConversationLocationController == this)
            {
                activeConversationLocationController = null;
            }
        }

        public void SetHome(Vector3 position, Quaternion rotation)
        {
            initialHomePosition = position;
            initialHomeRotation = rotation;
            hasInitialHome = true;
        }

        public void SetHomeAnchor(Transform anchor)
        {
            HomeAnchor = anchor;
        }

        public void SetHomeAnchors(Transform tableAnchor, Transform deskAnchor)
        {
            tableHomeAnchor = tableAnchor;
            deskHomeAnchor = deskAnchor;
            SetHomeLocation(currentHomeLocation, false);
        }

        public void SetHomeAnchor(CatHomeLocation location, Transform anchor)
        {
            switch (location)
            {
                case CatHomeLocation.Table:
                    tableHomeAnchor = anchor;
                    break;
                case CatHomeLocation.Desk:
                    deskHomeAnchor = anchor;
                    break;
                case CatHomeLocation.Bathroom:
                    bathroomHomeAnchor = anchor;
                    break;
                case CatHomeLocation.Bed:
                    bedHomeAnchor = anchor;
                    break;
            }

            if (currentHomeLocation == location)
            {
                currentHomeAnchor = ResolveHomeAnchor(location);
            }
        }

        public void SetHomeLocation(CatHomeLocation location)
        {
            SetHomeLocation(location, true);
        }

        private void SetHomeLocation(CatHomeLocation location, bool moveImmediately)
        {
            currentHomeLocation = location;
            currentHomeAnchor = ResolveHomeAnchor(location);
            if (activeConversationLocationController == this)
            {
                CurrentConversationLocationChanged?.Invoke(location);
            }
            if (moveImmediately)
            {
                MoveToHome();
            }
        }

        public void MoveToAnchor(Transform anchor)
        {
            if (anchor == null)
            {
                Debug.LogWarning("[CatPositionController] MoveToAnchor skipped because anchor is null.", this);
                return;
            }

            MoveTo(anchor.position, anchor.rotation);
        }

        public void ResetToHome()
        {
            MoveToHome();
        }

        public void SnapToHome()
        {
            MoveToHome();
        }

        public void MoveToHome()
        {
            Transform anchor = HomeAnchor;
            if (anchor != null)
            {
                MoveToAnchor(anchor);
                return;
            }

            CaptureInitialHomeIfNeeded();
            if (hasInitialHome)
            {
                MoveTo(initialHomePosition, initialHomeRotation);
            }
        }

        public void MoveTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        private void CaptureInitialHomeIfNeeded()
        {
            if (hasInitialHome || !captureInitialTransformAsHome)
            {
                return;
            }

            initialHomePosition = transform.position;
            initialHomeRotation = transform.rotation;
            hasInitialHome = true;
        }

        private Transform ResolveHomeAnchor(CatHomeLocation location)
        {
            switch (location)
            {
                case CatHomeLocation.Table:
                    return tableHomeAnchor;
                case CatHomeLocation.Desk:
                    return deskHomeAnchor != null ? deskHomeAnchor : tableHomeAnchor;
                case CatHomeLocation.Bathroom:
                    return bathroomHomeAnchor != null ? bathroomHomeAnchor : tableHomeAnchor;
                case CatHomeLocation.Bed:
                    return bedHomeAnchor != null ? bedHomeAnchor : tableHomeAnchor;
                default:
                    return null;
            }
        }
    }
}
