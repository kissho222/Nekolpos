using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nekolpos.PhotoShoot
{
    public class SetupPhotoShootURP : Editor
    {
        public static void SetupPostProcessing()
        {
            // 1. Remove old PPv2 volume if it exists
            GameObject oldVolumeObj = GameObject.Find("PhotoShoot_PostProcessVolume");
            if (oldVolumeObj != null)
            {
                DestroyImmediate(oldVolumeObj);
                Debug.Log("[PhotoShoot] 古い PPv2 Volume を削除しました。");
            }

            // 2. Setup Camera Post Processing toggle
            GameObject camObj = GameObject.Find("PhotoShootCamera");
            if (camObj == null)
            {
                Debug.LogError("[PhotoShoot] 'PhotoShootCamera' が見つかりません。先に「撮影モードのセットアップ」を実行してください。");
                return;
            }

            Camera cam = camObj.GetComponent<Camera>();
            UniversalAdditionalCameraData camData = camObj.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
            {
                // This gets automatically added by URP, but just in case
                camData = camObj.AddComponent<UniversalAdditionalCameraData>();
            }

            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // 3. Setup URP Global Volume Object
            GameObject volumeObj = GameObject.Find("PhotoShoot_URPVolume");
            if (volumeObj == null)
            {
                volumeObj = new GameObject("PhotoShoot_URPVolume");
            }

            Volume volume = volumeObj.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeObj.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 10; // High priority to override default scenes

            // 4. Create or find Profile
            string profilePath = "Assets/Materials/PhotoShootMacroURPProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                {
                    AssetDatabase.CreateFolder("Assets", "Materials");
                }
                AssetDatabase.CreateAsset(profile, profilePath);
                
                // Add URP DepthOfField
                DepthOfField dof = profile.Add<DepthOfField>();
                dof.active = true;
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.focusDistance.Override(0.5f); // 50cm initially
                dof.aperture.Override(1.2f);      // Very wide aperture for strong blur
                dof.focalLength.Override(50f);    // 50mm lens equivalent
                
                // Add URP Color Adjustments
                ColorAdjustments color = profile.Add<ColorAdjustments>();
                color.active = true;
                color.postExposure.Override(0.5f);
                color.contrast.Override(10f);
                
                // Add URP Tonemapping
                Tonemapping tone = profile.Add<Tonemapping>();
                tone.active = true;
                tone.mode.Override(TonemappingMode.ACES);

                AssetDatabase.SaveAssets();
            }

            volume.profile = profile;

            Selection.activeGameObject = volumeObj;
            EditorGUIUtility.PingObject(volumeObj);

            Debug.Log("[PhotoShoot] URP版マクロ撮影用PostProcessing（被写界深度）のセットアップが完了しました！\nPhotoShoot_URPVolumeオブジェクトの 'Depth of Field' 内の Focus Distance でピントを調整してください。");
        }
    }
}
