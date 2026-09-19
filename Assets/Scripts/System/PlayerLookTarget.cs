using UnityEngine;

namespace Nekolpos.System
{
    [ExecuteAlways]
    public class PlayerLookTarget : MonoBehaviour
    {
        private const string DebugMarkerObjectName = "Debug_PlayerLookTarget";
        [SerializeField] private Transform source;
        [SerializeField] private bool autoResolveMainCamera = true;
        [SerializeField] private Vector3 localOffset;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private bool copyRotation;

        [Header("Debug Visualization")]
        [SerializeField] private bool showDebugMarker;
        [SerializeField] private Vector3 debugMarkerScale = new Vector3(0.100000001f, 0.00999999978f, 0.100000001f);
        [SerializeField] private Color debugMarkerColor = new Color(0.1f, 0.85f, 1f, 0.9f);

        [Header("Debug Logging")]
        [SerializeField] private bool enableDebugLogs;
        [SerializeField] private float debugLogIntervalSeconds = 1f;

        private GameObject debugMarker;
        private Renderer debugMarkerRenderer;
        private float nextDebugLogTime;

        public Transform Source
        {
            get => source;
            set => source = value;
        }

        private void Awake()
        {
            ResolveSource();
            UpdateDebugMarker();
        }

        private void OnDestroy()
        {
            DestroyDebugMarker();
        }

        private void OnValidate()
        {
            debugMarkerScale = ClampDebugMarkerScale(debugMarkerScale);
            debugLogIntervalSeconds = Mathf.Max(0.05f, debugLogIntervalSeconds);
        }

        private void LateUpdate()
        {
            SynchronizeNow();
            UpdateDebugMarker();
            LogDebugState();
        }

        /// <summary>
        /// Updates the target immediately so Editor preview and runtime LateUpdate
        /// use the same camera-relative offsets.
        /// </summary>
        public void SynchronizeNow()
        {
            ResolveSource();

            if (source == null)
            {
                return;
            }

            transform.position = source.TransformPoint(localOffset) + worldOffset;
            if (copyRotation)
            {
                transform.rotation = source.rotation;
            }
        }

        private void ResolveSource()
        {
            if (source != null || !autoResolveMainCamera)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                source = mainCamera.transform;
            }
        }

        private void UpdateDebugMarker()
        {
            ReuseExistingDebugMarkerAndRemoveDuplicates();

            if (!showDebugMarker)
            {
                if (debugMarker != null)
                {
                    debugMarker.SetActive(false);
                }

                return;
            }

            EnsureDebugMarker();
            debugMarker.SetActive(source != null);
            debugMarker.transform.localPosition = Vector3.zero;
            debugMarker.transform.localRotation = Quaternion.identity;
            debugMarker.transform.localScale = debugMarkerScale;
        }

        private void EnsureDebugMarker()
        {
            if (debugMarker != null)
            {
                return;
            }

            debugMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugMarker.name = DebugMarkerObjectName;
            debugMarker.transform.SetParent(transform, false);
            debugMarker.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            Collider markerCollider = debugMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(markerCollider);
                }
                else
                {
                    DestroyImmediate(markerCollider);
                }
            }

            debugMarkerRenderer = debugMarker.GetComponent<Renderer>();
            if (debugMarkerRenderer != null)
            {
                debugMarkerRenderer.material = new Material(ResolveDebugMarkerShader())
                {
                    color = debugMarkerColor
                };
            }
        }

        private void ReuseExistingDebugMarkerAndRemoveDuplicates()
        {
            GameObject retainedMarker = debugMarker;
            for (int childIndex = transform.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = transform.GetChild(childIndex);
                if (child == null || child.name != DebugMarkerObjectName)
                {
                    continue;
                }

                GameObject marker = child.gameObject;
                if (retainedMarker == null)
                {
                    retainedMarker = marker;
                    continue;
                }

                if (marker != retainedMarker)
                {
                    DestroyObject(marker);
                }
            }

            debugMarker = retainedMarker;
            if (debugMarker == null)
            {
                debugMarkerRenderer = null;
                return;
            }

            debugMarker.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            debugMarkerRenderer = debugMarker.GetComponent<Renderer>();
        }

        private void DestroyDebugMarker()
        {
            if (debugMarker == null)
            {
                return;
            }

            DestroyObject(debugMarker);
            debugMarker = null;
            debugMarkerRenderer = null;
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static Shader ResolveDebugMarkerShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            return shader != null ? shader : Shader.Find("Standard");
        }

        private static Vector3 ClampDebugMarkerScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                Mathf.Max(0.001f, scale.z));
        }

        private void LogDebugState()
        {
            if (!enableDebugLogs || Time.unscaledTime < nextDebugLogTime)
            {
                return;
            }

            nextDebugLogTime = Time.unscaledTime + debugLogIntervalSeconds;
            Debug.Log(
                $"[PlayerLookTarget] path={GetTransformPath(transform)} source={FormatTransform(source)} " +
                $"autoCamera={autoResolveMainCamera} localOffset={FormatVector(localOffset)} worldOffset={FormatVector(worldOffset)} " +
                $"position={FormatVector(transform.position)} sourcePos={FormatTransformPosition(source)} copyRotation={copyRotation}",
                this);
        }

        private static string FormatTransform(Transform target)
        {
            return target != null ? GetTransformPath(target) : "null";
        }

        private static string FormatTransformPosition(Transform target)
        {
            return target != null ? FormatVector(target.position) : "null";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F3},{value.y:F3},{value.z:F3})";
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
            {
                return "null";
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
