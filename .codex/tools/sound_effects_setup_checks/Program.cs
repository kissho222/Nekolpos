using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using UnityEditor;
using Nekolpos.Audio;
using Object = UnityEngine.Object;

static class Program
{
    static readonly List<string> Passed = new();
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Passed.Add(name); }
    static SoundEffectsSetupActions.Targets Fixture()
    {
        World.Reset();
        var player = new GameObject("Player"); player.AddComponent<CharacterController>();
        var movement = player.AddComponent<PlayerSafetyMovementController>(); movement.walkableLayers = 1 << 7;
        var cat = new GameObject("Normal_Cat"); cat.AddComponent<Animator>();
        var opening = new GameObject("OP_Cat B"); opening.AddComponent<CatFootstepAudioController>(); opening.AddComponent<Animator>();
        var table = new GameObject("TableSurfaceProxy") { layer = 7 };
        var desk = new GameObject("DeskSurfaceProxy") { layer = 7 };
        return new() { Player = movement, Cat = cat, Table = table.AddComponent<BoxCollider>(), Desk = desk.AddComponent<BoxCollider>() };
    }
    static void Reject(string name, Action<SoundEffectsSetupActions.Targets> alter)
    {
        var target = Fixture(); alter(target);
        int count = World.ComponentCount;
        try { SoundEffectsSetupActions.AddMissingComponents(target, new(), new()); throw new Exception("accepted " + name); }
        catch (InvalidOperationException) { Check(World.ComponentCount == count && Undo.AddCalls == 0, name); }
    }
    static void Main()
    {
        var t = Fixture(); var table = new FootstepSurfaceProfile(); var desk = new FootstepSurfaceProfile();
        Check(SoundEffectsSetupActions.AddMissingComponents(t, table, desk) == 4, "four missing components added");
        Check(t.Table.GetComponent<FootstepSurface>().Profile == table && t.Desk.GetComponent<FootstepSurface>().Profile == desk, "new surfaces use explicit supplied profiles");
        Check(t.Player.GetComponent<CharacterMovementAudioController>().source == null && t.Cat.GetComponent<CatVoiceAudioController>().clips.Length == 0, "new audio slots remain empty");
        int groups = Undo.Groups; int calls = Undo.AddCalls;
        Check(SoundEffectsSetupActions.AddMissingComponents(t, null, null) == 0 && Undo.Groups == groups && Undo.AddCalls == calls, "repeat is no-op without profile requirement");
        Undo.PerformUndo();
        Check(t.Player.GetComponent<CharacterMovementAudioController>() == null && t.Cat.GetComponent<CatVoiceAudioController>() == null && t.Table.GetComponent<FootstepSurface>() == null && t.Desk.GetComponent<FootstepSurface>() == null, "undo removes only added components");
        Check(t.Player.GetComponent<CharacterController>() != null && t.Cat.GetComponent<Animator>() != null, "undo preserves original components");

        t = Fixture(); var existingPlayer = t.Player.gameObject.AddComponent<CharacterMovementAudioController>();
        var source = new Object(); existingPlayer.source = source;
        var existingVoice = t.Cat.AddComponent<CatVoiceAudioController>(); var clips = new[] { new Object(), new Object() }; existingVoice.clips = clips;
        var existingTable = t.Table.gameObject.AddComponent<FootstepSurface>(); // null means deliberately unspecified
        var existingDesk = t.Desk.gameObject.AddComponent<FootstepSurface>(); var emptyProfile = new FootstepSurfaceProfile(); existingDesk.profile = emptyProfile;
        Check(SoundEffectsSetupActions.AddMissingComponents(t, new(), new()) == 0 && existingPlayer.source == source && existingVoice.clips == clips && existingTable.profile == null && existingDesk.profile == emptyProfile, "existing nonempty empty and null values preserved");
        Check(SoundEffectsSetupActions.AddMissingToActiveScene() == 0 && AssetDatabase.LoadCalls == 0, "already configured scene does not inspect or create assets");
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _), "configured scene disables add button");

        t = Fixture(); var parent = new GameObject("surface parent") { activeSelf = false }; t.Table.gameObject.parent = parent;
        var inherited = parent.AddComponent<FootstepSurface>(); inherited.profile = null;
        Check(SoundEffectsSetupActions.AddMissingComponents(t, null, new()) == 3 && t.Table.GetComponent<FootstepSurface>() == null && inherited.profile == null, "inactive inherited surface null is preserved");
        var inheritedEmpty = new FootstepSurfaceProfile(); inherited.profile = inheritedEmpty;
        Check(SoundEffectsSetupActions.AddMissingComponents(t, new(), new()) == 0 && t.Table.GetComponent<FootstepSurface>() == null && inherited.profile == inheritedEmpty && inheritedEmpty.clips.Length == 0, "inactive inherited empty profile is preserved");

        Reject("foreign scene denied", x => x.Desk.gameObject.scene = new(2));
        Reject("persistent prefab denied", x => x.Cat.persistent = true);
        Reject("missing collider denied", x => x.Table = null);
        Reject("duplicate target denied", x => x.Desk = x.Table);
        Reject("disabled collider denied", x => x.Table.enabled = false);
        Reject("trigger collider denied", x => x.Desk.isTrigger = true);
        Reject("unwalkable layer denied", x => x.Player.walkableLayers = 0);
        Reject("missing character controller denied", x => x.Player.gameObject.Remove(x.Player.GetComponent<CharacterController>()));
        Reject("missing normal cat root animator denied", x => x.Cat.Remove(x.Cat.GetComponent<Animator>()));
        Reject("duplicate player denied", x => x.Player.gameObject.AddComponent<PlayerSafetyMovementController>());
        Reject("duplicate voice denied", x => { x.Cat.AddComponent<CatVoiceAudioController>(); x.Cat.AddComponent<CatVoiceAudioController>(); });
        Reject("child voice denied", x => { var child = new GameObject("voice child") { parent = x.Cat }; child.AddComponent<CatVoiceAudioController>(); });
        Reject("child movement denied", x => { var child = new GameObject("audio child") { parent = x.Player.gameObject }; child.AddComponent<CharacterMovementAudioController>(); });
        Reject("playing denied", x => EditorApplication.isPlayingOrWillChangePlaymode = true);
        Reject("compiling denied", x => EditorApplication.isCompiling = true);

        t = Fixture(); int original = World.ComponentCount;
        try { SoundEffectsSetupActions.AddMissingComponents(t, null, new()); throw new Exception("missing profile accepted"); }
        catch (InvalidOperationException) { Check(World.ComponentCount == original && Undo.AddCalls == 0, "missing new profile rejects before mutation"); }
        t = Fixture(); original = World.ComponentCount; Undo.FailAtAdd = 2;
        try { SoundEffectsSetupActions.AddMissingComponents(t, new(), new()); throw new Exception("failure injection did not fail"); }
        catch (InvalidOperationException) { Check(World.ComponentCount == original, "partial component addition rolled back"); }

        t = Fixture(); Check(SoundEffectsSetupActions.TryDescribeActiveScene(out _) && AssetDatabase.Assets.Count == 0 && Undo.AddCalls == 0, "description only has no mutation");
        Check(SoundEffectsSetupActions.AddMissingToActiveScene() == 4 && AssetDatabase.Assets.Count == 2, "scene command creates two absent profiles and components");
        Check(t.Cat.GetComponent<CatVoiceAudioController>() != null && World.Objects.Single(x => x.name == "OP_Cat B").GetComponent<CatVoiceAudioController>() == null, "voice binds normal cat not legacy OP footstep owner");
        Check(AssetDatabase.Assets.Values.OfType<FootstepSurfaceProfile>().All(p => p.clips.Length == 0 && p.displayName.StartsWith("材質未確定")), "created profiles empty and material unconfirmed");
        Undo.PerformUndo(); Check(AssetDatabase.Assets.Count == 2 && t.Table.GetComponent<FootstepSurface>() == null, "undo affects scene components not standalone profile assets");
        t = Fixture(); var foreign = new Object(); AssetDatabase.Assets[SoundEffectsSetupActions.DeskProfilePath] = foreign;
        try { SoundEffectsSetupActions.AddMissingToActiveScene(); throw new Exception("foreign destination accepted"); }
        catch (InvalidOperationException) { Check(AssetDatabase.Assets.Count == 1 && AssetDatabase.Assets.Values.Single() == foreign && Undo.AddCalls == 0, "foreign profile destination rejected before any authoring"); }
        t = Fixture(); var authoredProfile = new FootstepSurfaceProfile(); authoredProfile.clips = new[] { new Object() };
        AssetDatabase.Assets[SoundEffectsSetupActions.TableProfilePath] = authoredProfile;
        Check(SoundEffectsSetupActions.AddMissingToActiveScene() == 4 && authoredProfile.clips.Length == 1 && t.Table.GetComponent<FootstepSurface>().Profile == authoredProfile, "existing authored profile reused without editing");
        t = Fixture(); original = World.ComponentCount; Undo.FailAtAdd = 2;
        try { SoundEffectsSetupActions.AddMissingToActiveScene(); throw new Exception("failure injection did not fail"); }
        catch (InvalidOperationException) { Check(World.ComponentCount == original && AssetDatabase.Assets.Count == 0, "failed command rolls back new components and only own profiles"); }
        t = Fixture(); new GameObject("duplicate player").AddComponent<PlayerSafetyMovementController>();
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _) && Undo.AddCalls == 0, "ambiguous player scene denied");
        t = Fixture(); UnityEditor.SceneManagement.PrefabStageUtility.Current = new();
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _), "prefab stage denied");
        t = Fixture(); UnityEditor.SceneManagement.EditorSceneManager.Preview = true;
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _), "preview discovery denied");
        t = Fixture(); new GameObject("extra rig") { parent = t.Cat }.AddComponent<Animator>();
        Check(SoundEffectsSetupActions.TryDescribeActiveScene(out _), "normal cat root plus child animator accepted");
        t = Fixture(); new GameObject("Normal_Cat").AddComponent<Animator>();
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _) && Undo.AddCalls == 0, "ambiguous normal cat names rejected without binding");
        t = Fixture(); t.Cat.name = "Authored normal cat";
        var bootstrap = new GameObject("Bootstrap").AddComponent<Nekolpos.System.NekolposGameBootstrap>(); bootstrap.normalCatRoot = t.Cat;
        new GameObject("Normal_Cat").AddComponent<Animator>();
        Check(SoundEffectsSetupActions.AddMissingToActiveScene() == 4 && t.Cat.GetComponent<CatVoiceAudioController>() != null && World.Objects.Single(x => x.name == "Normal_Cat").GetComponent<CatVoiceAudioController>() == null, "bootstrap normal reference preferred over name");
        t = Fixture(); bootstrap = new GameObject("Bootstrap").AddComponent<Nekolpos.System.NekolposGameBootstrap>(); bootstrap.normalCatRoot = t.Cat;
        var another = new GameObject("Bootstrap2").AddComponent<Nekolpos.System.NekolposGameBootstrap>(); another.normalCatRoot = new GameObject("Other cat"); another.normalCatRoot.AddComponent<Animator>();
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _), "conflicting bootstrap normal references rejected");
        t = Fixture(); bootstrap = new GameObject("Bootstrap").AddComponent<Nekolpos.System.NekolposGameBootstrap>(); bootstrap.normalCatRoot = bootstrap.opCatRoot = t.Cat;
        Check(!SoundEffectsSetupActions.TryDescribeActiveScene(out _), "same opening and normal reference rejected");
        Console.WriteLine(JsonSerializer.Serialize(new { passed = Passed.Count, total = Passed.Count, cases = Passed, scope = "exact helper source plus managed doubles; no Unity/native/normal save" }));
    }
}

static class World
{
    public static List<GameObject> Objects = new();
    public static int ComponentCount => Objects.Sum(x => x.parts.Count);
    public static void Reset() { Objects.Clear(); Undo.Reset(); AssetDatabase.Reset(); EditorApplication.isCompiling = EditorApplication.isPlayingOrWillChangePlaymode = false; UnityEditor.SceneManagement.PrefabStageUtility.Current = null; UnityEditor.SceneManagement.EditorSceneManager.Preview = false; }
}
namespace UnityEngine
{
    public class Object { public string name; public bool persistent; public static void DestroyImmediate(Object value) { } }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
        public T GetComponentInParent<T>(bool includeInactive = false) where T : Component => gameObject.GetComponentInParent<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component { }
    public class Collider : Component { public bool enabled = true; public bool isTrigger; }
    public class BoxCollider : Collider { }
    public class CharacterController : Collider { }
    public class Animator : Component { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new(); }
    public class GameObject : Object
    {
        public List<Component> parts = new(); public GameObject parent; public int layer; public SceneManagement.Scene scene = new(1); public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (parent == null || parent.activeInHierarchy);
        public GameObject(string label) { name = label; World.Objects.Add(this); AddComponent<Transform>(); }
        public T AddComponent<T>() where T : Component, new() { var part = new T { gameObject = this }; parts.Add(part); return part; }
        public void Remove(Component value) => parts.Remove(value);
        public T GetComponent<T>() where T : Component => parts.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() where T : Component => parts.OfType<T>().ToArray();
        public T GetComponentInParent<T>(bool includeInactive = false) where T : Component
        { for (GameObject node = this; node != null; node = node.parent) if (node == this || includeInactive || node.activeInHierarchy) { T found = node.GetComponent<T>(); if (found != null) return found; } return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => GetComponents<T>().Concat(World.Objects.Where(x => x.parent == this).SelectMany(x => x.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }
    public static class Debug { public static void LogError(string message) => throw new Exception("rollback error: " + message); }
}
namespace UnityEngine.SceneManagement
{
    public readonly struct Scene : IEquatable<Scene>
    {
        readonly int id; public Scene(int id) { this.id = id; } public bool isLoaded => id != 0; public bool IsValid() => id != 0;
        public GameObject[] GetRootGameObjects() { Scene self = this; return World.Objects.Where(x => x.scene == self && x.parent == null).ToArray(); }
        public bool Equals(Scene other) => id == other.id; public override bool Equals(object value) => value is Scene other && Equals(other); public override int GetHashCode() => id;
        public static bool operator ==(Scene a, Scene b) => a.Equals(b); public static bool operator !=(Scene a, Scene b) => !a.Equals(b);
    }
    public static class SceneManager { public static Scene GetActiveScene() => new(1); }
}
public class PlayerSafetyMovementController : MonoBehaviour { public int walkableLayers; }
namespace Nekolpos.System { public class NekolposGameBootstrap : MonoBehaviour { public GameObject normalCatRoot; public GameObject opCatRoot; } }
namespace Nekolpos.Audio
{
    public class CharacterMovementAudioController : MonoBehaviour { public Object source; }
    public class CatVoiceAudioController : MonoBehaviour { public Object[] clips = Array.Empty<Object>(); }
    public class CatFootstepAudioController : MonoBehaviour { }
    public class FootstepSurface : MonoBehaviour { public FootstepSurfaceProfile profile; public FootstepSurfaceProfile Profile => profile; }
    public class FootstepSurfaceProfile : ScriptableObject { public string displayName = "その他"; public Object[] clips = Array.Empty<Object>(); }
}
namespace UnityEditor
{
    public static class EditorApplication { public static bool isPlayingOrWillChangePlaymode, isCompiling; }
    public static class EditorUtility { public static bool IsPersistent(Object obj) => obj.persistent; }
    public static class PrefabUtility { public static void RecordPrefabInstancePropertyModifications(Object obj) { } }
    public static class Undo
    {
        public static int Groups, AddCalls, FailAtAdd; static List<(int group, Action undo)> history = new();
        public static void Reset() { Groups = AddCalls = FailAtAdd = 0; history.Clear(); }
        public static void IncrementCurrentGroup() => Groups++;
        public static int GetCurrentGroup() => Groups;
        public static void SetCurrentGroupName(string name) { }
        public static T AddComponent<T>(GameObject obj) where T : Component, new()
        { if (++AddCalls == FailAtAdd) throw new InvalidOperationException("injected addition failure"); var component = obj.AddComponent<T>(); history.Add((Groups, () => obj.Remove(component))); return component; }
        public static void CollapseUndoOperations(int group) { }
        public static void RevertAllDownToGroup(int group) { for (int i = history.Count - 1; i >= 0; i--) if (history[i].group >= group) { history[i].undo(); history.RemoveAt(i); } }
        public static void PerformUndo() => RevertAllDownToGroup(Groups);
    }
    public class SerializedObject
    {
        readonly Object obj; public SerializedObject(Object obj) { this.obj = obj; }
        public SerializedProperty FindProperty(string name) { var field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); return field == null ? null : new(obj, field); }
        public void ApplyModifiedProperties() { } public void ApplyModifiedPropertiesWithoutUndo() { }
    }
    public class SerializedProperty
    {
        readonly Object obj; readonly FieldInfo reflectedField; public SerializedProperty(Object obj, FieldInfo field) { this.obj = obj; reflectedField = field; }
        public int intValue { get => (int)reflectedField.GetValue(obj); set => reflectedField.SetValue(obj, value); }
        public string stringValue { get => (string)reflectedField.GetValue(obj); set => reflectedField.SetValue(obj, value); }
        public Object objectReferenceValue { get => (Object)reflectedField.GetValue(obj); set => reflectedField.SetValue(obj, value); }
    }
    public static class AssetDatabase
    {
        public static Dictionary<string, Object> Assets = new(); static HashSet<string> folders = new(); public static int LoadCalls;
        public static void Reset() { Assets.Clear(); folders.Clear(); folders.Add("Assets"); LoadCalls = 0; }
        public static Object LoadMainAssetAtPath(string path) { LoadCalls++; Assets.TryGetValue(path, out var obj); return obj; }
        public static T LoadAssetAtPath<T>(string path) where T : Object => LoadMainAssetAtPath(path) as T;
        public static string GetAssetPath(Object obj) => Assets.FirstOrDefault(x => x.Value == obj).Key ?? "";
        public static bool DeleteAsset(string path) => Assets.Remove(path);
        public static void CreateAsset(Object obj, string path) { Assets.Add(path, obj); obj.persistent = true; }
        public static bool IsValidFolder(string folder) => folders.Contains(folder);
        public static string CreateFolder(string parent, string name) { folders.Add(parent + "/" + name); return Guid.NewGuid().ToString("N"); }
    }
}
namespace UnityEditor.SceneManagement
{
    public static class PrefabStageUtility { public static Object Current; public static Object GetCurrentPrefabStage() => Current; }
    public static class EditorSceneManager { public static bool Preview; public static bool IsPreviewScene(UnityEngine.SceneManagement.Scene scene) => Preview; }
}
