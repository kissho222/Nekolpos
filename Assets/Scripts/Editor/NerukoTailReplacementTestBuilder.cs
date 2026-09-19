#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public static class NerukoTailReplacementTestBuilder
{
    private const string OriginalScenePath = "Assets/Scenes/TitleScene.unity";
    private const string TestScenePath = "Assets/Scenes/TitleScene_NerukoTailTest.unity";
    private const string SourceFbxPath = "Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Assets_Models_CatSimple_NerukoTail_LOD0_OB.fbx";
    private const string MaterialPath = "Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/Materials/CatSim_color_6.mat";
    private const string PrefabFolder = "Assets/Prefab";
    private const string PrefabPath = PrefabFolder + "/NerukoTailCat_LOD0_OB.prefab";

    public static void Build()
    {
        var log = new StringBuilder();
        EditorSceneManager.SaveOpenScenes();

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath))
        {
            if (!AssetDatabase.CopyAsset(OriginalScenePath, TestScenePath))
            {
                throw new IOException("Failed to copy test scene: " + OriginalScenePath + " -> " + TestScenePath);
            }

            AssetDatabase.Refresh();
            log.AppendLine("Created test scene: " + TestScenePath);
        }
        else
        {
            log.AppendLine("Using existing test scene: " + TestScenePath);
        }

        var scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        log.AppendLine("Opened scene: " + scene.path);

        var sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFbxPath);
        var catMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!sourceAsset) throw new FileNotFoundException("Missing source FBX asset", SourceFbxPath);
        if (!catMaterial) throw new FileNotFoundException("Missing cat material", MaterialPath);
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets", "Prefab");

        if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(sourceAsset);
            temp.name = "NerukoTailCat_LOD0_OB";
            PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            ApplyCatMaterial(temp, catMaterial);
            PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            log.AppendLine("Created prefab: " + PrefabPath);
        }
        else
        {
            log.AppendLine("Prefab already exists, reused: " + PrefabPath);
        }

        var testPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!testPrefab) throw new FileNotFoundException("Failed to load test prefab", PrefabPath);

        var oldNormal = FindRoot(scene, "Normal_Cat");
        var oldOp = FindRoot(scene, "OP_Cat B");
        if (!oldNormal) throw new MissingReferenceException("Normal_Cat not found in test scene.");
        if (!oldOp) throw new MissingReferenceException("OP_Cat B not found in test scene.");

        DestroyIfExists(scene, "Normal_Cat_NerukoTail_Test");
        DestroyIfExists(scene, "OP_Cat_B_NerukoTail_Test");

        var newNormal = BuildTestCat(scene, testPrefab, catMaterial, oldNormal, "Normal_Cat_NerukoTail_Test");
        var newOp = BuildTestCat(scene, testPrefab, catMaterial, oldOp, "OP_Cat_B_NerukoTail_Test");

        var objectMap = new Dictionary<Object, Object>();
        AddComponentMappings(oldNormal, newNormal, objectMap);
        AddComponentMappings(oldOp, newOp, objectMap);

        int remapped = RemapSceneSerializedReferences(scene, objectMap);
        int timelineBindings = RemapTimelineBindings(objectMap);

        oldNormal.SetActive(false);
        oldOp.SetActive(false);
        newNormal.SetActive(true);
        newOp.SetActive(true);

        EditorUtility.SetDirty(oldNormal);
        EditorUtility.SetDirty(oldOp);
        EditorUtility.SetDirty(newNormal);
        EditorUtility.SetDirty(newOp);

        WriteNote(oldNormal, oldOp, newNormal, newOp, remapped, timelineBindings);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.AppendLine("Created/updated test objects in test scene.");
        log.AppendLine("Remapped serialized references: " + remapped);
        log.AppendLine("Remapped timeline bindings: " + timelineBindings);
        log.AppendLine("Normal renderers: " + newNormal.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
        log.AppendLine("OP renderers: " + newOp.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
        Debug.Log("[NerukoTailReplacementTestBuilder]\n" + log);
    }

    private static GameObject BuildTestCat(Scene scene, GameObject testPrefab, Material catMaterial, GameObject oldRoot, string testName)
    {
        var newRoot = (GameObject)PrefabUtility.InstantiatePrefab(testPrefab, scene);
        newRoot.name = testName;
        newRoot.transform.SetPositionAndRotation(oldRoot.transform.position, oldRoot.transform.rotation);
        newRoot.transform.localScale = oldRoot.transform.localScale;
        ApplyCatMaterial(newRoot, catMaterial);
        CopyComponentsToMatchingHierarchy(oldRoot, newRoot);
        return newRoot;
    }

    private static void ApplyCatMaterial(GameObject root, Material catMaterial)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0) continue;
            for (int i = 0; i < shared.Length; i++) shared[i] = catMaterial;
            renderer.sharedMaterials = shared;
        }
    }

    private static void CopyComponentsToMatchingHierarchy(GameObject oldRoot, GameObject newRoot)
    {
        foreach (var oldTransform in oldRoot.GetComponentsInChildren<Transform>(true))
        {
            var newTransform = FindByRelativePath(newRoot.transform, RelativePath(oldRoot.transform, oldTransform));
            if (!newTransform) continue;

            foreach (var oldComp in oldTransform.GetComponents<Component>())
            {
                if (!oldComp || oldComp is Transform) continue;
                var type = oldComp.GetType();
                Component newComp = null;
                foreach (var candidate in newTransform.GetComponents(type))
                {
                    newComp = candidate;
                    break;
                }

                ComponentUtility.CopyComponent(oldComp);
                if (newComp)
                {
                    ComponentUtility.PasteComponentValues(newComp);
                }
                else
                {
                    ComponentUtility.PasteComponentAsNew(newTransform.gameObject);
                }
            }
        }
    }

    private static void AddComponentMappings(GameObject oldRoot, GameObject newRoot, IDictionary<Object, Object> map)
    {
        map[oldRoot] = newRoot;
        map[oldRoot.transform] = newRoot.transform;

        foreach (var oldTransform in oldRoot.GetComponentsInChildren<Transform>(true))
        {
            var newTransform = FindByRelativePath(newRoot.transform, RelativePath(oldRoot.transform, oldTransform));
            if (!newTransform) continue;
            map[oldTransform] = newTransform;
            map[oldTransform.gameObject] = newTransform.gameObject;

            var oldComps = oldTransform.GetComponents<Component>();
            var newComps = newTransform.GetComponents<Component>();
            var used = new HashSet<int>();
            foreach (var oldComp in oldComps)
            {
                if (!oldComp) continue;
                for (int i = 0; i < newComps.Length; i++)
                {
                    if (used.Contains(i) || !newComps[i]) continue;
                    if (newComps[i].GetType() != oldComp.GetType()) continue;
                    map[oldComp] = newComps[i];
                    used.Add(i);
                    break;
                }
            }
        }
    }

    private static int RemapSceneSerializedReferences(Scene scene, IDictionary<Object, Object> objectMap)
    {
        int remapped = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (!comp) continue;
                remapped += RemapSerializedReferences(comp, objectMap);
            }
        }

        return remapped;
    }

    private static int RemapSerializedReferences(Object owner, IDictionary<Object, Object> objectMap)
    {
        int count = 0;
        var so = new SerializedObject(owner);
        var prop = so.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
            var oldRef = prop.objectReferenceValue;
            if (!oldRef || !objectMap.TryGetValue(oldRef, out var newRef) || !newRef) continue;
            prop.objectReferenceValue = newRef;
            count++;
        }

        if (count > 0) so.ApplyModifiedPropertiesWithoutUndo();
        return count;
    }

    private static int RemapTimelineBindings(IDictionary<Object, Object> objectMap)
    {
        int timelineBindings = 0;
        foreach (var director in Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!director || !director.playableAsset) continue;
            foreach (var output in director.playableAsset.outputs)
            {
                var bound = director.GetGenericBinding(output.sourceObject);
                if (!bound || !objectMap.TryGetValue(bound, out var replacement) || !replacement) continue;
                director.SetGenericBinding(output.sourceObject, replacement);
                timelineBindings++;
            }
            EditorUtility.SetDirty(director);
        }

        return timelineBindings;
    }

    private static void WriteNote(GameObject oldNormal, GameObject oldOp, GameObject newNormal, GameObject newOp, int remapped, int timelineBindings)
    {
        var normalAnimator = newNormal.GetComponent<Animator>();
        var opAnimator = newOp.GetComponent<Animator>();
        var note = new StringBuilder();
        note.AppendLine("# NerukoTail Cat Replacement Test");
        note.AppendLine();
        note.AppendLine("Date: 2026-07-08");
        note.AppendLine();
        note.AppendLine("## Created assets");
        note.AppendLine("- Test scene: `" + TestScenePath + "`");
        note.AppendLine("- Test prefab: `" + PrefabPath + "`");
        note.AppendLine("- Normal test object: `Normal_Cat_NerukoTail_Test`");
        note.AppendLine("- OP test object: `OP_Cat_B_NerukoTail_Test`");
        note.AppendLine();
        note.AppendLine("## Source references observed");
        note.AppendLine("- Source FBX: `" + SourceFbxPath + "`");
        note.AppendLine("- Material assigned to renderers: `" + MaterialPath + "`");
        note.AppendLine("- Normal original Animator Controller: `" + AnimatorControllerPath(oldNormal) + "`");
        note.AppendLine("- OP original Animator Controller: `" + AnimatorControllerPath(oldOp) + "`");
        note.AppendLine("- Normal test Avatar: `" + AvatarPath(normalAnimator) + "`");
        note.AppendLine("- OP test Avatar: `" + AvatarPath(opAnimator) + "`");
        note.AppendLine();
        note.AppendLine("## Scene changes in test scene only");
        note.AppendLine("- Original `Normal_Cat` set inactive, not deleted.");
        note.AppendLine("- Original `OP_Cat B` set inactive, not deleted.");
        note.AppendLine("- Copied matching hierarchy components from originals to new full-FBX-derived objects.");
        note.AppendLine("- Remapped serialized object references from original cats to test cats: " + remapped);
        note.AppendLine("- Remapped PlayableDirector generic bindings from original cats to test cats: " + timelineBindings);
        note.AppendLine();
        note.AppendLine("## Quick structural checks");
        note.AppendLine("- Normal test Animator Controller: `" + AnimatorControllerPath(newNormal) + "`");
        note.AppendLine("- OP test Animator Controller: `" + AnimatorControllerPath(newOp) + "`");
        note.AppendLine("- Normal test SkinnedMeshRenderer count: " + newNormal.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
        note.AppendLine("- OP test SkinnedMeshRenderer count: " + newOp.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
        note.AppendLine();
        note.AppendLine("## Manual Play Mode checks still required");
        note.AppendLine("- Model does not collapse on Play.");
        note.AppendLine("- Idle animation plays on `Normal_Cat_NerukoTail_Test`.");
        note.AppendLine("- Forked tail is visible.");
        note.AppendLine("- Material appearance is correct.");
        note.AppendLine("- `CatPositionController`, `CatMotionController`, and `CatPurrAudioController` do not log errors.");
        note.AppendLine("- OP Timeline displays the cat and does not report Missing Binding or PlayableDirector errors.");

        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "NerukoTailReplacementTestNotes.md"), note.ToString(), new UTF8Encoding(false));
    }

    private static string AnimatorControllerPath(GameObject root)
    {
        var animator = root ? root.GetComponent<Animator>() : null;
        return animator && animator.runtimeAnimatorController ? AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) : "<null>";
    }

    private static string AvatarPath(Animator animator)
    {
        return animator && animator.avatar ? AssetDatabase.GetAssetPath(animator.avatar) : "<null>";
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == root) return string.Empty;
        var parts = new Stack<string>();
        var current = target;
        while (current && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts.ToArray());
    }

    private static Transform FindByRelativePath(Transform root, string relativePath)
    {
        return string.IsNullOrEmpty(relativePath) ? root : root.Find(relativePath);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root;
        }

        return null;
    }

    private static void DestroyIfExists(Scene scene, string name)
    {
        var existing = FindRoot(scene, name);
        if (existing) Object.DestroyImmediate(existing);
    }
}
#endif
