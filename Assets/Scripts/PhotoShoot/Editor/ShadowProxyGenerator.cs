using UnityEngine;
using UnityEditor;

namespace Nekolpos.PhotoShoot
{
    public class ShadowProxyGenerator : Editor
    {
        public static void GenerateProxy()
        {
            // Create root object
            GameObject proxyRoot = new GameObject("ShadowProxy_Ragged");
            
            // Create materials folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            // Create a simple dark unlit material
            string matPath = "Assets/Materials/ShadowProxyMat.mat";
            Material shadowMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (shadowMat == null)
            {
                shadowMat = new Material(Shader.Find("Unlit/Color"));
                shadowMat.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Dark shadow gray
                AssetDatabase.CreateAsset(shadowMat, matPath);
                AssetDatabase.SaveAssets();
            }

            // Body (Capsule)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(proxyRoot.transform);
            body.transform.localPosition = new Vector3(0, 0.5f, 0);
            body.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f); // Make it slightly squat and wide like rags
            body.GetComponent<MeshRenderer>().sharedMaterial = shadowMat;

            // Head (Sphere)
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(proxyRoot.transform);
            head.transform.localPosition = new Vector3(0, 1.2f, 0);
            head.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            head.GetComponent<MeshRenderer>().sharedMaterial = shadowMat;
            
            // Optional: Remove colliders to prevent physics interference with the main scene
            DestroyImmediate(body.GetComponent<CapsuleCollider>());
            DestroyImmediate(head.GetComponent<SphereCollider>());

            // Position it in front of the camera or at origin
            if (SceneView.lastActiveSceneView != null)
            {
                proxyRoot.transform.position = SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 2f;
                proxyRoot.transform.position = new Vector3(proxyRoot.transform.position.x, 0, proxyRoot.transform.position.z);
            }
            
            // Focus the object in the editor
            Selection.activeGameObject = proxyRoot;
            EditorGUIUtility.PingObject(proxyRoot);
            
            Debug.Log("[ShadowProxy] ボロ布を纏った影のようなダミーモデル（簡易版）を生成しました！");
        }
    }
}
