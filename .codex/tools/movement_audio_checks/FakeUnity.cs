using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class InspectorNameAttribute : Attribute { public string displayName; public InspectorNameAttribute(string value) => displayName = value; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public string tooltip; public TooltipAttribute(string value) => tooltip = value; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName = ""; public string menuName = ""; }
    public class Object { public string name = ""; }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T? GetComponentInParent<T>() where T : Component
        {
            for (Transform? current = transform; current != null; current = current.parent)
            {
                var found = current.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public Transform transform { get; }
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public GameObject(string value = "") { name = value; transform = new Transform { gameObject = this }; components.Add(transform); }
        public void SetActive(bool active) => activeSelf = active;
        public T AddComponent<T>() where T : Component, new() { var item = new T { gameObject = this }; components.Add(item); return item; }
        public T? GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault();
    }
    public class Transform : Component
    {
        public Transform? parent;
        public readonly List<Transform> children = new();
        public void SetParent(Transform? value, bool worldPositionStays = true) { parent?.children.Remove(this); parent = value; value?.children.Add(this); }
        public Transform? Find(string value) => children.FirstOrDefault(c => c.gameObject.name == value);
    }
    public class Collider : Component { }
    public class AudioClip : Object { public AudioClip(string value) => name = value; }
    public sealed record AudioCall(string Method, AudioClip? Clip, float Volume, float Pitch);
    public class AudioSource : Behaviour
    {
        public bool playOnAwake, loop, isPlaying, paused;
        public float spatialBlend, pitch = 1, volume = 1;
        public AudioClip? clip;
        public Nekolpos.System.SettingsAudioCategory? Route;
        public readonly List<AudioCall> Calls = new();
        public int Count(string method) => Calls.Count(c => c.Method == method);
        public void PlayOneShot(AudioClip value, float scale) { Calls.Add(new("OneShot", value, scale, pitch)); isPlaying = true; paused = false; }
        public void Play() { Calls.Add(new("Play", clip, volume, pitch)); isPlaying = true; paused = false; }
        public void Pause() { Calls.Add(new("Pause", clip, volume, pitch)); paused = isPlaying; isPlaying = false; }
        public void UnPause() { Calls.Add(new("UnPause", clip, volume, pitch)); if (paused) isPlaying = true; paused = false; }
        public void Stop() { Calls.Add(new("Stop", clip, volume, pitch)); isPlaying = false; paused = false; }
        public void CompleteForTest() { isPlaying = false; paused = false; }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static float Min(float a, float b) => Math.Min(a, b); }
    public static class Time { public static float time; public static float timeScale = 1; }
    public static class Random
    {
        public static int Selection;
        public static int Range(int min, int max) { if (max <= min) throw new InvalidOperationException("Invalid random range"); return min + Math.Abs(Selection++) % (max - min); }
        public static float Range(float min, float max) => (min + max) / 2;
    }
    public static class Resources
    {
        public static readonly Dictionary<string, Object> Assets = new();
        public static T? Load<T>(string path) where T : Object => Assets.TryGetValue(path, out var value) ? value as T : null;
    }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public sealed class UnityEvent
    {
        private readonly List<UnityAction> actions = new();
        public int Count => actions.Count;
        public void AddListener(UnityAction action) => actions.Add(action);
        public void Invoke() { foreach (var action in actions.ToArray()) action(); }
    }
}
namespace UnityEngine.Timeline
{
    public sealed class SignalAsset : UnityEngine.ScriptableObject { }
    public sealed class SignalReceiver : UnityEngine.MonoBehaviour
    {
        public readonly Dictionary<SignalAsset, UnityEngine.Events.UnityEvent> Reactions = new();
        public UnityEngine.Events.UnityEvent? GetReaction(SignalAsset signal) => Reactions.TryGetValue(signal, out var value) ? value : null;
        public void AddReaction(SignalAsset signal, UnityEngine.Events.UnityEvent reaction) => Reactions.Add(signal, reaction);
    }
}
namespace Nekolpos.System
{
    public enum SettingsAudioCategory { Se, CatVoice }
    public static class SettingsManager
    {
        public static readonly List<(UnityEngine.AudioSource Source, SettingsAudioCategory Category)> Routes = new();
        public static void RouteAudio(UnityEngine.AudioSource source, SettingsAudioCategory category) { source.Route = category; Routes.Add((source, category)); }
    }
}
