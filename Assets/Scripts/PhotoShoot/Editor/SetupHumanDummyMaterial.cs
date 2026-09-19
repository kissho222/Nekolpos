using UnityEngine;
using UnityEditor;

namespace Nekolpos.PhotoShoot
{
    public class SetupHumanDummyMaterial : Editor
    {
        public static void FixDummyMaterial()
        {
            string matPath = "Assets/Kevin Iglesias/Human Character Dummy/Materials/HumanDummy.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat != null)
            {
                // Set to Standard shader to allow Color tinting over the texture
                mat.shader = Shader.Find("Standard");
                
                // Set to a stylish dark grey/black
                mat.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                
                // Make it matte (0 smoothness) so it feels like a simple dummy proxy
                mat.SetFloat("_Glossiness", 0f);
                
                // Clear any emission
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                mat.SetColor("_EmissionColor", Color.black);

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                
                Debug.Log("[PhotoShoot] HumanDummyのマテリアルをStandardシェーダーに変更し、安定した黒・グレーに設定しました！");
            }
            else
            {
                Debug.LogError("[PhotoShoot] HumanDummy.mat が見つかりませんでした。");
            }
        }
    }
}
