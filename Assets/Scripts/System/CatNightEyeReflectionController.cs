using System.Collections.Generic;
using UnityEngine;

namespace Nekolpos.System
{
    public sealed class CatNightEyeReflectionController : MonoBehaviour
    {
        [global::System.Serializable]
        public struct EyeCatchLightTransform
        {
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public Vector3 localScale;

            public static EyeCatchLightTransform DefaultLeft()
            {
                return new EyeCatchLightTransform
                {
                    localPosition = new Vector3(0.002f, 0.003f, 0.012f),
                    localEulerAngles = Vector3.zero,
                    localScale = new Vector3(0.005f, 0.005f, 0.005f),
                };
            }

            public static EyeCatchLightTransform DefaultRight()
            {
                return new EyeCatchLightTransform
                {
                    localPosition = new Vector3(-0.002f, 0.003f, 0.012f),
                    localEulerAngles = Vector3.zero,
                    localScale = new Vector3(0.005f, 0.005f, 0.005f),
                };
            }
        }

        private const string LeftEyeName = "eye.L";
        private const string RightEyeName = "eye.R";
        private const string LeftCatchLightName = "RuntimeCatCatchLight_L";
        private const string RightCatchLightName = "RuntimeCatCatchLight_R";
        private const string FaceFillLightName = "RuntimeCatNightFaceFillSpot";

        private static readonly List<CatNightEyeReflectionController> Instances = new List<CatNightEyeReflectionController>();
        private static bool currentNightActive;
        private static Light sharedFaceFillLight;
        private static CatNightEyeReflectionController sharedFaceFillOwner;

        [Header("Eye Targets")]
        [SerializeField] private string leftEyeName = LeftEyeName;
        [SerializeField] private string rightEyeName = RightEyeName;

        [Header("Catch Light")]
        [SerializeField] private Color catchLightColor = new Color(1.0f, 0.96f, 0.82f, 0.82f);
        [SerializeField] [Range(0f, 1f)] private float catchLightAlpha = 0.82f;
        [SerializeField] private EyeCatchLightTransform leftCatchLight = EyeCatchLightTransform.DefaultLeft();
        [SerializeField] private EyeCatchLightTransform rightCatchLight = EyeCatchLightTransform.DefaultRight();

        [Header("Night Face Fill")]
        [SerializeField] private bool createCameraFaceFillSpot = true;
        [SerializeField] [Min(0f)] private float faceFillIntensity = 0.05f;
        [SerializeField] [Min(0.01f)] private float faceFillRange = 5f;
        [SerializeField] [Range(1f, 80f)] private float faceFillSpotAngle = 50f;
        [SerializeField] private Color faceFillColor = new Color(0.86f, 0.78f, 0.62f);
        [SerializeField] private Vector3 faceFillCameraLocalOffset = new Vector3(0f, -0.07f, 0.02f);
        [SerializeField] private Vector3 faceFillTargetWorldOffset = new Vector3(0f, -0.035f, 0f);

        private Transform leftEyeTransform;
        private Transform rightEyeTransform;
        private GameObject leftCatchLightObject;
        private GameObject rightCatchLightObject;
        private bool nightActive;

        public static void EnsureForLoadedCats()
        {
            DestroyLegacyEyeGlowObjects();

            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            HashSet<Transform> visitedRoots = new HashSet<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform eye = transforms[i];
                if (eye == null || eye.name != LeftEyeName)
                {
                    continue;
                }

                Transform candidate = ResolveCatRootFromEye(eye);
                if (candidate == null || visitedRoots.Contains(candidate))
                {
                    continue;
                }

                visitedRoots.Add(candidate);

                if (FindDescendant(candidate, RightEyeName) == null)
                {
                    continue;
                }

                if (candidate.GetComponentInParent<CatNightEyeReflectionController>(true) != null)
                {
                    continue;
                }

                candidate.gameObject.AddComponent<CatNightEyeReflectionController>();
            }
        }

        public static void SetNightReflectionActiveForAll(bool active)
        {
            currentNightActive = active;
            EnsureForLoadedCats();

            for (int i = Instances.Count - 1; i >= 0; i--)
            {
                CatNightEyeReflectionController instance = Instances[i];
                if (instance == null)
                {
                    Instances.RemoveAt(i);
                    continue;
                }

                instance.SetNightActive(active);
            }

            UpdateSharedFaceFillLight(active);
        }

        private void Reset()
        {
            leftCatchLight = EyeCatchLightTransform.DefaultLeft();
            rightCatchLight = EyeCatchLightTransform.DefaultRight();
        }

        private void OnEnable()
        {
            if (!Instances.Contains(this))
            {
                Instances.Add(this);
            }

            EnsureRuntimeObjects();
            SetNightActive(currentNightActive);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            if (sharedFaceFillOwner == this)
            {
                sharedFaceFillOwner = null;
                UpdateSharedFaceFillLight(currentNightActive);
            }
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            if (sharedFaceFillOwner == this)
            {
                sharedFaceFillOwner = null;
                UpdateSharedFaceFillLight(currentNightActive);
            }
        }

        private void LateUpdate()
        {
            if (!nightActive)
            {
                return;
            }

            ApplyCatchLightTransforms();
            if (sharedFaceFillOwner == this)
            {
                UpdateSharedFaceFillLight(true);
            }
        }

        private void OnValidate()
        {
            catchLightAlpha = Mathf.Clamp01(catchLightAlpha);
            faceFillIntensity = Mathf.Max(0f, faceFillIntensity);
            faceFillRange = Mathf.Max(0.01f, faceFillRange);
            faceFillSpotAngle = Mathf.Clamp(faceFillSpotAngle, 1f, 80f);
            ApplyCatchLightTransforms();
            ApplyCatchLightMaterials();
        }

        private void SetNightActive(bool active)
        {
            nightActive = active;
            EnsureRuntimeObjects();
            ApplyCatchLightTransforms();
            ApplyCatchLightMaterials();

            if (leftCatchLightObject != null)
            {
                leftCatchLightObject.SetActive(active);
            }

            if (rightCatchLightObject != null)
            {
                rightCatchLightObject.SetActive(active);
            }
        }

        private void EnsureRuntimeObjects()
        {
            DestroyLegacyEyeGlowObjects();

            if (leftEyeTransform == null)
            {
                leftEyeTransform = FindDescendant(transform, leftEyeName);
            }

            if (rightEyeTransform == null)
            {
                rightEyeTransform = FindDescendant(transform, rightEyeName);
            }

            leftCatchLightObject = EnsureCatchLight(leftEyeTransform, LeftCatchLightName, leftCatchLightObject);
            rightCatchLightObject = EnsureCatchLight(rightEyeTransform, RightCatchLightName, rightCatchLightObject);
        }

        private GameObject EnsureCatchLight(Transform eyeParent, string objectName, GameObject current)
        {
            if (eyeParent == null)
            {
                return current;
            }

            Transform existing = eyeParent.Find(objectName);
            GameObject catchLight = existing != null ? existing.gameObject : current;
            if (catchLight == null)
            {
                catchLight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                catchLight.name = objectName;
                catchLight.transform.SetParent(eyeParent, false);
                Collider collider = catchLight.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyRuntimeObject(collider);
                }
            }

            MeshRenderer renderer = catchLight.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            catchLight.SetActive(nightActive);
            return catchLight;
        }

        private void ApplyCatchLightTransforms()
        {
            ApplyCatchLightTransform(leftCatchLightObject, leftCatchLight);
            ApplyCatchLightTransform(rightCatchLightObject, rightCatchLight);
        }

        private static void ApplyCatchLightTransform(GameObject target, EyeCatchLightTransform tuning)
        {
            if (target == null)
            {
                return;
            }

            target.transform.localPosition = tuning.localPosition;
            target.transform.localRotation = Quaternion.Euler(tuning.localEulerAngles);
            target.transform.localScale = tuning.localScale;
        }

        private void ApplyCatchLightMaterials()
        {
            Color color = catchLightColor;
            color.a = catchLightAlpha;
            SetCatchLightMaterial(leftCatchLightObject, color);
            SetCatchLightMaterial(rightCatchLightObject, color);
        }

        private static void SetCatchLightMaterial(GameObject target, Color color)
        {
            if (target == null)
            {
                return;
            }

            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            Material material = renderer.sharedMaterial;
            if (material == null || !material.name.StartsWith("Runtime_CatCatchLight", global::System.StringComparison.Ordinal))
            {
                material = CreateUnlitTransparentMaterial(target.name);
                renderer.sharedMaterial = material;
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }

        private static Material CreateUnlitTransparentMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = $"Runtime_CatCatchLight_{name}",
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent,
            };

            SetMaterialFloatIfPresent(material, "_Surface", 1f);
            SetMaterialFloatIfPresent(material, "_Blend", 0f);
            SetMaterialFloatIfPresent(material, "_ZWrite", 0f);
            SetMaterialFloatIfPresent(material, "_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_EMISSION");
            return material;
        }

        private static void SetMaterialFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void EnsureSharedFaceFillLight()
        {
            Camera mainCamera = Camera.main;
            Transform parent = mainCamera != null ? mainCamera.transform : sharedFaceFillOwner != null ? sharedFaceFillOwner.transform : null;
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(FaceFillLightName);
            GameObject lightObject = existing != null ? existing.gameObject : new GameObject(FaceFillLightName);
            if (existing == null)
            {
                lightObject.transform.SetParent(parent, false);
            }

            sharedFaceFillLight = lightObject.GetComponent<Light>();
            if (sharedFaceFillLight == null)
            {
                sharedFaceFillLight = lightObject.AddComponent<Light>();
            }

            sharedFaceFillLight.type = LightType.Spot;
            sharedFaceFillLight.shadows = LightShadows.None;
            sharedFaceFillLight.renderMode = LightRenderMode.ForcePixel;
        }

        private static void UpdateSharedFaceFillLight(bool active)
        {
            if (!active)
            {
                if (sharedFaceFillLight != null)
                {
                    sharedFaceFillLight.enabled = false;
                }

                return;
            }

            sharedFaceFillOwner = ResolveFaceFillOwner();
            if (sharedFaceFillOwner == null || !sharedFaceFillOwner.createCameraFaceFillSpot)
            {
                if (sharedFaceFillLight != null)
                {
                    sharedFaceFillLight.enabled = false;
                }

                return;
            }

            EnsureSharedFaceFillLight();
            if (sharedFaceFillLight == null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null && sharedFaceFillLight.transform.parent != mainCamera.transform)
            {
                sharedFaceFillLight.transform.SetParent(mainCamera.transform, false);
            }

            sharedFaceFillLight.transform.localPosition = sharedFaceFillOwner.faceFillCameraLocalOffset;
            Vector3 target = sharedFaceFillOwner.ResolveFaceTargetPosition();
            Vector3 direction = target - sharedFaceFillLight.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                sharedFaceFillLight.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            sharedFaceFillLight.color = sharedFaceFillOwner.faceFillColor;
            sharedFaceFillLight.intensity = sharedFaceFillOwner.faceFillIntensity;
            sharedFaceFillLight.range = sharedFaceFillOwner.faceFillRange;
            sharedFaceFillLight.spotAngle = sharedFaceFillOwner.faceFillSpotAngle;
            sharedFaceFillLight.enabled = true;
        }

        private Vector3 ResolveFaceTargetPosition()
        {
            if (leftEyeTransform != null && rightEyeTransform != null)
            {
                return ((leftEyeTransform.position + rightEyeTransform.position) * 0.5f) + faceFillTargetWorldOffset;
            }

            Transform head = FindDescendant(transform, "head");
            return head != null ? head.position + faceFillTargetWorldOffset : transform.position;
        }

        private static CatNightEyeReflectionController ResolveFaceFillOwner()
        {
            Camera mainCamera = Camera.main;
            Vector3 cameraPosition = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
            CatNightEyeReflectionController best = null;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < Instances.Count; i++)
            {
                CatNightEyeReflectionController instance = Instances[i];
                if (instance == null || !instance.nightActive || !instance.createCameraFaceFillSpot || !instance.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 target = instance.ResolveFaceTargetPosition();
                float distance = mainCamera != null ? (target - cameraPosition).sqrMagnitude : i;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = instance;
                }
            }

            return best;
        }

        private static void DestroyLegacyEyeGlowObjects()
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == null || !IsLegacyGeneratedEyeObject(current.name))
                {
                    continue;
                }

                if (Application.isPlaying)
                    Object.Destroy(current.gameObject);
                else
                    Object.DestroyImmediate(current.gameObject);
            }
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private static bool IsLegacyGeneratedEyeObject(string objectName)
        {
            return objectName == "Neruko_EyeGlow_L" ||
                   objectName == "Neruko_EyeGlow_R" ||
                   objectName == "IrisReflection" ||
                   objectName == "PupilMask" ||
                   objectName == "CatchLight" ||
                   objectName.StartsWith("RuntimeCatNightEyeReflection", global::System.StringComparison.Ordinal) ||
                   objectName.StartsWith(FaceFillLightName + "_", global::System.StringComparison.Ordinal);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrEmpty(objectName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == objectName)
                {
                    return children[i];
                }
            }

            return null;
        }

        private static Transform ResolveCatRootFromEye(Transform eye)
        {
            Transform current = eye;
            Transform fallback = eye.parent;
            while (current != null)
            {
                if (current.name == "Cat_Simple" ||
                    current.name == "Arm_Cat" ||
                    current.name == "Normal_Cat" ||
                    current.name == "OP_Cat")
                {
                    return current;
                }

                if (current.GetComponent<Animator>() != null)
                {
                    fallback = current;
                }

                current = current.parent;
            }

            return fallback;
        }
    }
}
