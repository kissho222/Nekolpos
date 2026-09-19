using UnityEngine;
using UnityEditor;

namespace Nekolpos.PhotoShoot
{
    public class SetupCatURPMaterial : Editor
    {
        public static void CreateCatMaterial()
        {
            string folderPath = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string matPath = folderPath + "/CatFurURPMaterial.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                // In URP, we can use Universal Render Pipeline/Lit and tweak the emission and smoothness 
                // to fake the Rim & SSS effect programmatically, or provide a template for the user.
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                
                if (urpLit == null)
                {
                    Debug.LogError("[PhotoShoot] URP Lit シェーダーが見つかりません。URPが正しくインストールされているか確認してください。");
                    return;
                }

                mat = new Material(urpLit);
                
                // Base setup for fur
                mat.SetFloat("_Smoothness", 0.1f); // Rough fur
                mat.SetColor("_BaseColor", new Color(0.8f, 0.5f, 0.2f, 1f)); // Default orange-ish
                
                // To fake Rim Light / SSS without a custom shader graph, we enable Emission but leave it ready for tweaking.
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.1f, 0.05f, 0.0f, 1f)); // Slight warm glow for SSS fake
                
                // Normal Map prep
                mat.EnableKeyword("_NORMALMAP");

                AssetDatabase.CreateAsset(mat, matPath);
                
                Debug.Log($"[PhotoShoot] 猫用のマテリアル雛形を作成しました: {matPath}");
                
                // Also create a quick script template for a custom URP Shader Graph node setup
                CreateShaderGraphReadme(folderPath);
            }
            else
            {
                Debug.Log("[PhotoShoot] すでにマテリアルが存在しています。");
            }
            
            Selection.activeObject = mat;
            EditorGUIUtility.PingObject(mat);
        }

        private static void CreateShaderGraphReadme(string path)
        {
            string txtPath = path + "/HowToCreateCatFurShaderGraph.txt";
            string content = @"【猫の毛並み用 Shader Graph の作り方 (URP)】

URP環境で完璧なリムライト（Rim Light）と光の透過（SSS）を作るには、独自のShader Graphを作成するのが最適です。

1. プロジェクトウィンドウで右クリック > Create > Shader Graph > URP > Lit Shader Graph を作成。
2. 作成したグラフをダブルクリックしてエディタを開く。

◆ 1. リムライト（輪郭が光る）の作り方
- 右クリック > Create Node > 『Fresnel Effect』を作成。
- 『Color』ノードを作成し、好きな光の色（例：白や淡いオレンジ）に設定。
- 『Multiply』ノードを作成し、Fresnel EffectとColorを繋ぐ。
- このMultiplyの結果を、マスターノードの『Emission』に繋ぐ。
※Fresnelの「Power」数値をいじると、輪郭の光の細さを調整できます。

◆ 2. SSS（光が透ける / サブサーフェス・スキャタリング）の作り方
URP Lit Shader GraphでフェイクSSSを作る基本レシピです。
- 右クリック > Create Node > 『Normal Vector』を作成。
- 右クリック > Create Node > 『Light Direction』（※URP専用のMain Light Directionノード等）を作成。
- 上記2つを『Dot Product』で繋ぎます。
- 出力された値を『Negate』や『One Minus』で反転させると、「光の反対側（背中側）」が白くなります。
- この値を『Multiply』でベースカラーの赤・オレンジ成分と掛け合わせ、『Base Color』や『Emission』に足し合わせることで、耳や尻尾が透けて赤く見える表現になります。

※上記を組み合わせたマテリアルを Cat_Simple にアタッチしてください。
";
            global::System.IO.File.WriteAllText(txtPath, content);
            AssetDatabase.Refresh();
        }
    }
}
