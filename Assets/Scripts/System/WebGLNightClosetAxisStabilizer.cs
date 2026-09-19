using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    public sealed class WebGLNightClosetAxisStabilizer : MonoBehaviour
    {
        private const string DefaultTargetName = "クローゼット 操作軸-003";

        [Header("Target")]
        [SerializeField] private string targetName = DefaultTargetName;
        [SerializeField] private bool includeChildren = true;

        [Header("WebGL Night Stabilization")]
        [SerializeField] private bool applyOnlyAtNight = true;
        [SerializeField] private bool disableLightProbes = true;
        [SerializeField] private bool disableReflectionProbes = true;
        [SerializeField] private bool disableShadows = true;
        [SerializeField] private bool reduceSpecularResponse = true;
        [SerializeField] [Range(0f, 1f)] private float smoothness = 0.18f;
        [SerializeField] [Range(0f, 1f)] private float metallic = 0f;
        [SerializeField] private Color specularColor = new Color(0.06f, 0.055f, 0.045f, 1f);

        private RendererState[] rendererStates = Array.Empty<RendererState>();
        private bool isApplied;
        private float nextResolveTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (FindFirstObjectByType<WebGLNightClosetAxisStabilizer>() != null)
            {
                return;
            }

            GameObject host = new GameObject(nameof(WebGLNightClosetAxisStabilizer));
            host.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<WebGLNightClosetAxisStabilizer>();
#endif
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            StartCoroutine(ResolveForInitialFrames());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Restore();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextResolveTime && !HasValidRendererState())
            {
                ResolveTarget();
                nextResolveTime = Time.unscaledTime + 1f;
            }

            if (rendererStates.Length == 0)
            {
                return;
            }

            bool shouldApply = !applyOnlyAtNight || IsNight();
            if (shouldApply)
            {
                Apply();
            }
            else
            {
                Restore();
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Restore();
            rendererStates = Array.Empty<RendererState>();
            StartCoroutine(ResolveForInitialFrames());
        }

        private IEnumerator ResolveForInitialFrames()
        {
            for (int i = 0; i < 120 && rendererStates.Length == 0; i++)
            {
                ResolveTarget();
                yield return null;
            }
        }

        private void ResolveTarget()
        {
            GameObject target = FindSceneObjectByName(targetName);
            if (target == null)
            {
                rendererStates = Array.Empty<RendererState>();
                isApplied = false;
                return;
            }

            Renderer[] renderers = includeChildren
                ? target.GetComponentsInChildren<Renderer>(true)
                : target.GetComponents<Renderer>();

            rendererStates = new RendererState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                rendererStates[i] = new RendererState(renderers[i]);
            }

            isApplied = false;
        }

        private void Apply()
        {
            if (isApplied)
            {
                return;
            }

            for (int i = 0; i < rendererStates.Length; i++)
            {
                rendererStates[i].Apply(
                    disableLightProbes,
                    disableReflectionProbes,
                    disableShadows,
                    reduceSpecularResponse,
                    smoothness,
                    metallic,
                    specularColor);
            }

            isApplied = true;
        }

        private void Restore()
        {
            if (!isApplied)
            {
                return;
            }

            for (int i = 0; i < rendererStates.Length; i++)
            {
                rendererStates[i].Restore();
            }

            isApplied = false;
        }

        private bool HasValidRendererState()
        {
            for (int i = 0; i < rendererStates.Length; i++)
            {
                if (rendererStates[i].Renderer != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNight()
        {
            GameManager manager = GameManager.Instance;
            return manager != null && manager.CurrentState is StateNight;
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || !string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                {
                    continue;
                }

                Scene scene = candidate.scene;
                if (scene.IsValid() && scene.isLoaded)
                {
                    return candidate;
                }
            }

            return null;
        }

        private sealed class RendererState
        {
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly MaterialPropertyBlock[] originalBlocks;

            public RendererState(Renderer renderer)
            {
                Renderer = renderer;
                if (renderer == null)
                {
                    originalBlocks = Array.Empty<MaterialPropertyBlock>();
                    return;
                }

                lightProbeUsage = renderer.lightProbeUsage;
                reflectionProbeUsage = renderer.reflectionProbeUsage;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;

                int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
                originalBlocks = new MaterialPropertyBlock[materialCount];
                for (int i = 0; i < materialCount; i++)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, i);
                    originalBlocks[i] = block;
                }
            }

            public Renderer Renderer { get; }

            public void Apply(
                bool disableLightProbes,
                bool disableReflectionProbes,
                bool disableShadows,
                bool reduceSpecularResponse,
                float smoothness,
                float metallic,
                Color specularColor)
            {
                if (Renderer == null)
                {
                    return;
                }

                if (disableLightProbes)
                {
                    Renderer.lightProbeUsage = LightProbeUsage.Off;
                }

                if (disableReflectionProbes)
                {
                    Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                if (disableShadows)
                {
                    Renderer.shadowCastingMode = ShadowCastingMode.Off;
                    Renderer.receiveShadows = false;
                }

                if (!reduceSpecularResponse)
                {
                    return;
                }

                int materialCount = Mathf.Max(1, originalBlocks.Length);
                for (int i = 0; i < materialCount; i++)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    Renderer.GetPropertyBlock(block, i);
                    block.SetFloat("_Smoothness", smoothness);
                    block.SetFloat("_Glossiness", smoothness);
                    block.SetFloat("_Metallic", metallic);
                    block.SetColor("_SpecColor", specularColor);
                    Renderer.SetPropertyBlock(block, i);
                }
            }

            public void Restore()
            {
                if (Renderer == null)
                {
                    return;
                }

                Renderer.lightProbeUsage = lightProbeUsage;
                Renderer.reflectionProbeUsage = reflectionProbeUsage;
                Renderer.shadowCastingMode = shadowCastingMode;
                Renderer.receiveShadows = receiveShadows;

                for (int i = 0; i < originalBlocks.Length; i++)
                {
                    Renderer.SetPropertyBlock(originalBlocks[i], i);
                }
            }
        }
    }
}
