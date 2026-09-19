using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nekolpos.EditorTools
{
    public static class DepthOfFieldProfileRepair
    {
        private static readonly string[] ProfilePaths =
        {
            "Assets/Scenes/GameScene/Global Volume Profile.asset",
            "Assets/Settings/PV/PV_Morning_VolumeProfile.asset",
        };

        [MenuItem("Tools/Nekolpos/Repair Depth Of Field Profiles")]
        public static void Repair()
        {
            foreach (string path in ProfilePaths)
            {
                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null)
                {
                    Debug.LogWarning($"[DepthOfFieldProfileRepair] VolumeProfile not found: {path}");
                    continue;
                }

                for (int i = profile.components.Count - 1; i >= 0; i--)
                {
                    VolumeComponent component = profile.components[i];
                    if (component != null && component is not DepthOfField)
                    {
                        continue;
                    }

                    profile.components.RemoveAt(i);
                    if (component != null)
                    {
                        Object.DestroyImmediate(component, true);
                    }
                }

                DepthOfField depthOfField = ScriptableObject.CreateInstance<DepthOfField>();
                depthOfField.name = nameof(DepthOfField);
                depthOfField.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                profile.components.Add(depthOfField);
                AssetDatabase.AddObjectToAsset(depthOfField, profile);
                depthOfField.active = true;
                depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
                depthOfField.focusDistance.Override(8f);
                depthOfField.aperture.Override(16f);
                depthOfField.focalLength.Override(30f);
                depthOfField.gaussianStart.Override(10f);
                depthOfField.gaussianEnd.Override(30f);
                depthOfField.gaussianMaxRadius.Override(1f);
                depthOfField.highQualitySampling.Override(false);

                EditorUtility.SetDirty(profile);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DepthOfFieldProfileRepair] Depth Of Field profiles repaired.");
        }
    }
}
