using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Nekolpos.PhotoShoot;
using System.IO;

public class SetupPhotoShootEnvironment : Editor
{
    public static void Setup()
    {
        // 1. Find Characters in the Scene
        GameObject cat = GameObject.Find("Cat_Simple");
        GameObject human = GameObject.Find("HumanDummy_F White");
        
        // If not found at root, check if it's nested under Player or somewhere else by scanning everything
        if (human == null)
        {
            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in allTransforms)
            {
                if (t.name == "HumanDummy_F White") 
                {
                    human = t.gameObject;
                    break;
                }
            }
        }

        if (cat == null)
        {
            Debug.LogError("[PhotoShoot] 'Cat_Simple' がシーンに見つかりません。");
            return;
        }

        if (human == null)
        {
            Debug.LogError("[PhotoShoot] 'HumanDummy_F White' がシーンに見つかりません。Prefabsからシーンに配置してください。");
            return;
        }

        // 3. Create or Setup Free Camera
        GameObject camObj = GameObject.Find("PhotoShootCamera");
        if (camObj == null)
        {
            camObj = new GameObject("PhotoShootCamera");
            Camera cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
            
            // Disable existing main camera
            if (Camera.main != null && Camera.main.gameObject != camObj)
            {
                Camera.main.gameObject.SetActive(false);
            }
            
            camObj.tag = "MainCamera";
            
            // Position near the cat
            camObj.transform.position = cat.transform.position + new Vector3(0, 1, -2);
        }

        Camera psCam = camObj.GetComponent<Camera>();
        if (psCam != null)
        {
            // Set max priority to remain on top in Play Mode
            psCam.depth = 100;
        }

        PhotoShootCameraController camController = camObj.GetComponent<PhotoShootCameraController>();
        if (camController == null) camController = camObj.AddComponent<PhotoShootCameraController>();
        
        PhotoShootLightingController lightController = camObj.GetComponent<PhotoShootLightingController>();
        if (lightController == null) lightController = camObj.AddComponent<PhotoShootLightingController>();

        // Disable all other active cameras in the scene to prevent conflicts
        Camera[] allCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in allCameras)
        {
            if (c != psCam && c.gameObject.activeInHierarchy)
            {
                c.gameObject.SetActive(false);
                Debug.Log($"[PhotoShoot] 重複・競合を防ぐため '{c.name}' を一時的に無効化しました。");
            }
        }



        // 4. Create or Find Photo Shoot Manager
        GameObject managerObj = GameObject.Find("PhotoShootManager");
        if (managerObj == null)
        {
            managerObj = new GameObject("PhotoShootManager");
        }
        
        PhotoShootManager manager = managerObj.GetComponent<PhotoShootManager>();
        if (manager == null) manager = managerObj.AddComponent<PhotoShootManager>();

        // Link manager to camera
        manager.cameraTransform = camObj.transform;
        manager.cameraController = camController;

        // Clear and setup character list
        manager.characters.Clear();
        
        // Find all animations in the project
        AnimationClip[] allClips = FindAllAnimationClips();

        // Setup Cat Data
        var catData = new PhotoShootManager.CharacterData
        {
            modeType = PhotoShootManager.ControlMode.Cat,
            characterName = "Cat",
            rootObject = cat,
            animator = cat.GetComponent<Animator>(),
            moveSpeed = 0.75f, // Reduced to 1/4
            rotationSpeed = 360f,
            availableClips = new global::System.Collections.Generic.List<AnimationClip>()
        };
        
        // Setup Human Data
        var humanData = new PhotoShootManager.CharacterData
        {
            modeType = PhotoShootManager.ControlMode.Human,
            characterName = "Human",
            rootObject = human,
            animator = human.GetComponent<Animator>(),
            moveSpeed = 0.25f, // Reduced to 1/4
            rotationSpeed = 360f,
            availableClips = new global::System.Collections.Generic.List<AnimationClip>()
        };

        // Assign clips based on simple naming or path heuristics
        foreach (var clip in allClips)
        {
            string clipPath = AssetDatabase.GetAssetPath(clip).ToLower().Replace('\\', '/');
            bool isKevinIglesias = clipPath.Contains("kevin iglesias/") || clipPath.Contains("human animations/");

            if (clip.name.ToLower().Contains("cat") || clip.name.StartsWith("Cat"))
            {
                catData.availableClips.Add(clip);
            }
            // For the human character, firmly only load from the Iglesias package so we don't accidentally get UI or enemy clips
            else if (isKevinIglesias && !clip.name.StartsWith("__preview__")) 
            {
                humanData.availableClips.Add(clip);
            }
        }

        manager.characters.Add(catData);
        manager.characters.Add(humanData);

        // 5. Disable conflicting GameManager if it exists to allow free camera override
        GameObject gameManager = GameObject.Find("GameManager");
        if (gameManager != null)
        {
            gameManager.SetActive(false);
            Debug.Log("[PhotoShoot] 既存の GameManager を一時的に無効化しました（競合回避のため）");
        }

        // 6. Disable conflicting old camera
        GameObject fpsRig = GameObject.Find("CameraRig");
        if (fpsRig != null)
        {
            fpsRig.SetActive(false);
            Debug.Log("[PhotoShoot] 既存の CameraRig を一時的に無効化しました");
        }

        // 7. Fix HumanDummy floating issue (Physics overlap)
        GameObject playerRoot = GameObject.Find("Player");
        if (playerRoot != null && human != null && human.transform.IsChildOf(playerRoot.transform))
        {
            // Destroy parent physics components completely
            Rigidbody parentRb = playerRoot.GetComponent<Rigidbody>();
            if (parentRb != null) DestroyImmediate(parentRb);

            CapsuleCollider parentCol = playerRoot.GetComponent<CapsuleCollider>();
            if (parentCol != null) DestroyImmediate(parentCol);

            // Destroy child (HumanDummy) physics components completely
            Rigidbody childRb = human.GetComponent<Rigidbody>();
            if (childRb != null) DestroyImmediate(childRb);

            CapsuleCollider childCol = human.GetComponent<CapsuleCollider>();
            if (childCol != null) DestroyImmediate(childCol);

            // Turn off Animator Root Motion on Human so it doesn't walk away from its origin
            Animator humanAnim = human.GetComponent<Animator>();
            if (humanAnim != null) humanAnim.applyRootMotion = false;
        }

        // 8. Fix Cat Animator Root Motion
        if (cat != null)
        {
            Animator catAnim = cat.GetComponent<Animator>();
            if (catAnim != null) catAnim.applyRootMotion = false;
        }

        Debug.Log("[PhotoShoot] ✨ セットアップ完了！ 再生ボタンを押して撮影モードを開始できます。");
    }

    private static AnimationClip[] FindAllAnimationClips()
    {
        // 1. Find standalone .anim files
        string[] animGuids = AssetDatabase.FindAssets("t:AnimationClip");
        var clips = new global::System.Collections.Generic.List<AnimationClip>();
        
        foreach (string guid in animGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && !clips.Contains(clip))
            {
                clips.Add(clip);
            }
        }

        // 2. Find animations embedded inside Models/FBXs
        string[] modelGuids = AssetDatabase.FindAssets("t:Model");
        foreach (string guid in modelGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);

            foreach (var asset in allAssets)
            {
                if (asset is AnimationClip clip)
                {
                    // Skip __preview__ clips generated by Unity
                    if (!clip.name.StartsWith("__preview__") && !clips.Contains(clip))
                    {
                        clips.Add(clip);
                    }
                }
            }
        }
        
        return clips.ToArray();
    }
}
