using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nekolpos.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring only. Existing components and their empty fields are never changed.</summary>
public static class SoundEffectsSetupActions
{
    public const string TableProfilePath = "Assets/Settings/Audio/TableSurfaceUnconfirmed.asset";
    public const string DeskProfilePath = "Assets/Settings/Audio/DeskSurfaceUnconfirmed.asset";

    public sealed class Targets
    {
        public PlayerSafetyMovementController Player;
        public GameObject Cat;
        public Collider Table;
        public Collider Desk;
    }

    public static bool TryDescribeActiveScene(out string description)
    {
        try
        {
            Targets targets = FindActiveSceneTargets();
            ValidateTargets(targets);
            int count = CountMissing(targets);
            description = count == 0 ? "必要な4つの基盤は接続済みです。既存の設定は変更しません。" :
                $"現在のSceneへ不足するコンポーネントを{count}件追加します。既存の参照・空欄は保持し、Sceneは保存しません。";
            return count > 0;
        }
        catch (InvalidOperationException error)
        {
            description = error.Message;
            return false;
        }
    }

    public static int AddMissingToActiveScene()
    {
        Targets targets = FindActiveSceneTargets();
        ValidateTargets(targets);
        if (CountMissing(targets) == 0) return 0;

        bool needsTable = FindSurface(targets.Table) == null;
        bool needsDesk = FindSurface(targets.Desk) == null;
        // Check both destinations before any authoring. A foreign asset must never be replaced.
        FootstepSurfaceProfile table = needsTable ? ReadProfileDestination(TableProfilePath) : null;
        FootstepSurfaceProfile desk = needsDesk ? ReadProfileDestination(DeskProfilePath) : null;
        var createdProfiles = new List<FootstepSurfaceProfile>();
        try
        {
            if (needsTable && table == null)
                table = CreateEmptyProfile(TableProfilePath, "材質未確定（ちゃぶ台）", createdProfiles);
            if (needsDesk && desk == null)
                desk = CreateEmptyProfile(DeskProfilePath, "材質未確定（机）", createdProfiles);
            return AddMissingComponents(targets, table, desk);
        }
        catch
        {
            // Only assets created by this call are candidates for rollback; existing assets are untouched.
            foreach (FootstepSurfaceProfile profile in createdProfiles)
            {
                if (profile == null) continue;
                string path = AssetDatabase.GetAssetPath(profile);
                if ((path == TableProfilePath || path == DeskProfilePath) &&
                    AssetDatabase.LoadAssetAtPath<FootstepSurfaceProfile>(path) == profile && !AssetDatabase.DeleteAsset(path))
                    Debug.LogError("追加した空の床材を取り消せませんでした: " + path);
            }
            throw;
        }
    }

    /// <summary>Accepts explicit temporary targets/profiles for bounded Editor verification.</summary>
    public static int AddMissingComponents(Targets targets, FootstepSurfaceProfile table, FootstepSurfaceProfile desk)
    {
        ValidateTargets(targets);
        bool addPlayer = targets.Player.GetComponent<CharacterMovementAudioController>() == null;
        bool addCat = targets.Cat.GetComponent<CatVoiceAudioController>() == null;
        bool addTable = FindSurface(targets.Table) == null;
        bool addDesk = FindSurface(targets.Desk) == null;
        if (addTable && table == null || addDesk && desk == null)
            throw new InvalidOperationException("新しく追加する床のプロファイルがありません。既存の床設定は変更しません。");
        int count = (addPlayer ? 1 : 0) + (addCat ? 1 : 0) + (addTable ? 1 : 0) + (addDesk ? 1 : 0);
        if (count == 0) return 0;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("不足するSE基盤を追加");
        try
        {
            // Null clip/source/default-surface slots on new components are safe defaults, not guessed content.
            if (addPlayer) Undo.AddComponent<CharacterMovementAudioController>(targets.Player.gameObject);
            if (addCat) Undo.AddComponent<CatVoiceAudioController>(targets.Cat);
            if (addTable) AddSurface(targets.Table.gameObject, table);
            if (addDesk) AddSurface(targets.Desk.gameObject, desk);
            Undo.CollapseUndoOperations(group);
            return count;
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    public static void ValidateTargets(Targets targets)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Playとコンパイルの終了後に設定してください。");
        if (targets == null || targets.Player == null || targets.Cat == null || targets.Table == null || targets.Desk == null)
            throw new InvalidOperationException("Player、通常猫、ちゃぶ台、机の対象を一意に確認できません。");
        GameObject[] objects = { targets.Player.gameObject, targets.Cat, targets.Table.gameObject, targets.Desk.gameObject };
        Scene scene = objects[0].scene;
        if (!scene.IsValid() || !scene.isLoaded || objects.Distinct().Count() != objects.Length ||
            objects.Any(item => EditorUtility.IsPersistent(item) || item.scene != scene))
            throw new InvalidOperationException("同じ開いているSceneの異なる4対象が必要です。Prefab Assetは直接変更しません。");
        if (targets.Player.GetComponents<PlayerSafetyMovementController>().Length != 1 ||
            targets.Player.GetComponent<CharacterController>() == null ||
            targets.Cat.GetComponent<Animator>() == null ||
            targets.Player.GetComponents<CharacterMovementAudioController>().Length > 1 ||
            targets.Cat.GetComponents<CatVoiceAudioController>().Length > 1 ||
            targets.Table.GetComponents<FootstepSurface>().Length > 1 || targets.Desk.GetComponents<FootstepSurface>().Length > 1)
            throw new InvalidOperationException("移動・猫・床のコンポーネントが不足または重複しています。");
        if (targets.Player.GetComponentsInChildren<CharacterMovementAudioController>(true).Length !=
                targets.Player.GetComponents<CharacterMovementAudioController>().Length ||
            targets.Cat.GetComponentsInChildren<CatVoiceAudioController>(true).Length !=
                targets.Cat.GetComponents<CatVoiceAudioController>().Length)
            throw new InvalidOperationException("子オブジェクトに既存の再生器があります。二重追加を避けるため手動で接続を確認してください。");
        if (!targets.Table.enabled || targets.Table.isTrigger || !targets.Desk.enabled || targets.Desk.isTrigger)
            throw new InvalidOperationException("床には有効な非Trigger Colliderが必要です。");
        var player = new SerializedObject(targets.Player);
        SerializedProperty layers = player.FindProperty("walkableLayers");
        if (layers == null || (layers.intValue & (1 << targets.Table.gameObject.layer)) == 0 ||
            (layers.intValue & (1 << targets.Desk.gameObject.layer)) == 0)
            throw new InvalidOperationException("床のLayerがPlayerのWalkable対象に含まれていません。移動設定は自動変更しません。");
    }

    private static Targets FindActiveSceneTargets()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Prefab Stageを閉じ、設定対象のSceneを開いてください。");
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
            throw new InvalidOperationException("設定対象の通常Sceneを開いてください。");
        GameObject[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject).ToArray();
        PlayerSafetyMovementController[] players = objects.SelectMany(item => item.GetComponents<PlayerSafetyMovementController>()).ToArray();
        GameObject cat = FindNormalCat(objects);
        GameObject[] tables = objects.Where(item => item.name == "TableSurfaceProxy").ToArray();
        GameObject[] desks = objects.Where(item => item.name == "DeskSurfaceProxy").ToArray();
        if (players.Length != 1 || tables.Length != 1 || desks.Length != 1 ||
            tables[0].GetComponents<Collider>().Length != 1 || desks[0].GetComponents<Collider>().Length != 1)
            throw new InvalidOperationException("Player・通常猫・TableSurfaceProxy・DeskSurfaceProxyが各1件必要です。曖昧な対象は自動変更しません。");
        return new Targets { Player = players[0], Cat = cat,
            Table = tables[0].GetComponent<Collider>(), Desk = desks[0].GetComponent<Collider>() };
    }

    private static GameObject FindNormalCat(GameObject[] objects)
    {
        var assigned = new List<GameObject>();
        foreach (var bootstrap in objects.SelectMany(item => item.GetComponents<Nekolpos.System.NekolposGameBootstrap>()))
        {
            var serialized = new SerializedObject(bootstrap);
            SerializedProperty normal = serialized.FindProperty("normalCatRoot");
            if (normal == null) throw new InvalidOperationException("通常猫の基盤参照を確認できません。");
            if (normal.objectReferenceValue == null) continue;
            GameObject target = normal.objectReferenceValue as GameObject;
            SerializedProperty opening = serialized.FindProperty("opCatRoot");
            if (target == null || opening != null && opening.objectReferenceValue == target)
                throw new InvalidOperationException("通常猫とOP猫の基盤参照を確認してください。");
            assigned.Add(target);
        }
        GameObject[] candidates = assigned.Count > 0 ? assigned.Distinct().ToArray() :
            objects.Where(item => item.name == "Normal_Cat").ToArray();
        if (candidates.Length != 1 || candidates[0].GetComponent<Animator>() == null)
            throw new InvalidOperationException("通常猫の参照、またはroot Animatorを持つ唯一のNormal_Catが必要です。OP猫から推測しません。");
        return candidates[0];
    }

    private static int CountMissing(Targets targets) =>
        (targets.Player.GetComponent<CharacterMovementAudioController>() == null ? 1 : 0) +
        (targets.Cat.GetComponent<CatVoiceAudioController>() == null ? 1 : 0) +
        (FindSurface(targets.Table) == null ? 1 : 0) +
        (FindSurface(targets.Desk) == null ? 1 : 0);

    private static FootstepSurface FindSurface(Collider collider) => collider.GetComponent<FootstepSurface>() ??
        collider.GetComponentInParent<FootstepSurface>(true);

    private static void AddSurface(GameObject target, FootstepSurfaceProfile profile)
    {
        FootstepSurface surface = Undo.AddComponent<FootstepSurface>(target);
        var serialized = new SerializedObject(surface);
        SerializedProperty property = serialized.FindProperty("profile");
        if (property == null) throw new InvalidOperationException("床材の設定項目を確認できません。");
        property.objectReferenceValue = profile;
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
    }

    private static FootstepSurfaceProfile ReadProfileDestination(string path)
    {
        Object existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing != null && !(existing is FootstepSurfaceProfile) ||
            existing == null && (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta")))
            throw new InvalidOperationException("床材の保存先に別のファイルがあります: " + path);
        return existing as FootstepSurfaceProfile;
    }

    private static FootstepSurfaceProfile CreateEmptyProfile(string path, string label, List<FootstepSurfaceProfile> created)
    {
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(folder);
        if (ReadProfileDestination(path) != null)
            throw new InvalidOperationException("床材の保存先が変更されました。設定一覧を更新してください。");
        var profile = ScriptableObject.CreateInstance<FootstepSurfaceProfile>();
        try
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("displayName").stringValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            created.Add(profile);
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }
        catch
        {
            if (!EditorUtility.IsPersistent(profile)) Object.DestroyImmediate(profile);
            throw;
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        if (Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".meta"))
            throw new InvalidOperationException("床材フォルダーの既存ファイルを確認してください: " + folder);
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (string.IsNullOrEmpty(parent) || !folder.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException("床材の保存先を作成できません。");
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))))
            throw new InvalidOperationException("床材の保存先を作成できません: " + folder);
    }
}
