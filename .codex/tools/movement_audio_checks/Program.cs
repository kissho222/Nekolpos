using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nekolpos.Audio;
using Nekolpos.System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Timeline;

static class Program
{
    static readonly List<object> results = new();
    static int failures;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, string field, object? value) => target.GetType().GetField(field, Hidden)!.SetValue(target, value);
    static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden)!.GetValue(target)!;
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, Hidden)!.Invoke(target, null);
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void Near(float actual, float expected) => Require(Math.Abs(actual - expected) < .0001, $"Expected {expected}, got {actual}");
    static void Check(string name, Action test)
    {
        Time.time = 0; Time.timeScale = 1; UnityEngine.Random.Selection = 0; Resources.Assets.Clear(); SettingsManager.Routes.Clear();
        try { test(); results.Add(new { name, passed = true }); }
        catch (Exception error) { failures++; results.Add(new { name, passed = false, error = (error.InnerException ?? error).Message }); }
    }
    static AudioClip Clip(string name = "clip") => new(name);
    static FootstepSurfaceProfile Profile(params AudioClip?[] clips)
    {
        var value = ScriptableObject.CreateInstance<FootstepSurfaceProfile>(); Set(value, "clips", clips); return value;
    }
    static Collider Floor(FootstepSurfaceProfile? profile, bool parent = false)
    {
        var root = new GameObject("floor"); Set(root.AddComponent<FootstepSurface>(), "profile", profile);
        if (!parent) return root.AddComponent<Collider>();
        var child = new GameObject("child"); child.transform.SetParent(root.transform); return child.AddComponent<Collider>();
    }
    static CharacterMovementAudioController Movement(FootstepSurfaceProfile? profile = null, FootstepPlaybackMode mode = FootstepPlaybackMode.AutomaticInterval)
    {
        var value = new GameObject("player").AddComponent<CharacterMovementAudioController>();
        Set(value, "defaultSurface", profile); Set(value, "footstepPlaybackMode", mode); Invoke(value, "Awake"); return value;
    }
    static AudioSource Source(object controller) => Get<AudioSource>(controller, "audioSource");
    static int Shots(object controller) => Source(controller).Count("OneShot");
    static void Observe(CharacterMovementAudioController c, float speed = 1, bool grounded = true, float dt = .02f, Collider? floor = null) => c.ObserveMovement(speed, grounded, floor!, dt);
    static void Disable(Behaviour value) { value.enabled = false; Invoke(value, "OnDisable"); }
    static void Enable(Behaviour value) { value.enabled = true; var m = value.GetType().GetMethod("OnEnable", Hidden); m?.Invoke(value, null); }
    static void ResetMovement(CharacterMovementAudioController value)
    {
        var method = value.GetType().GetMethod("ResetMovementState", BindingFlags.Public | BindingFlags.Instance);
        Require(method != null, "ResetMovementState API missing"); method!.Invoke(value, null);
    }
    static void SuspendMovement(CharacterMovementAudioController value)
    {
        var method = value.GetType().GetMethod("SuspendMovementObservation", BindingFlags.Public | BindingFlags.Instance);
        Require(method != null, "SuspendMovementObservation API missing"); method!.Invoke(value, null);
    }
    static CatVoiceCategory Category(params AudioClip?[] clips) { var v = new CatVoiceCategory(); Set(v, "clips", clips); return v; }
    static CatVoiceAudioController Voice(bool awake = true)
    {
        var v = new GameObject("cat").AddComponent<CatVoiceAudioController>(); if (awake) Invoke(v, "Awake"); return v;
    }
    static void SetCategories(CatVoiceAudioController c)
    {
        foreach (var name in new[] { "call", "angry", "happy", "displeased" }) Set(c, name, Category(Clip(name)));
    }

    static int Main()
    {
        Check("surface-direct", () => { var p = Profile(Clip()); Require(CharacterMovementAudioController.ResolveSurface(Floor(p), Profile()) == p, "direct profile lost"); });
        Check("surface-parent-collider", () => { var p = Profile(Clip()); Require(CharacterMovementAudioController.ResolveSurface(Floor(p, true), Profile()) == p, "parent profile lost"); });
        Check("surface-child-profile-precedence", () => { var p = Profile(Clip()); var c = Floor(Profile(), true); Set(c.gameObject.AddComponent<FootstepSurface>(), "profile", p); Require(CharacterMovementAudioController.ResolveSurface(c, null!) == p, "child profile not preferred"); });
        Check("surface-missing-fallback", () => { var p = Profile(Clip()); Require(CharacterMovementAudioController.ResolveSurface(new GameObject().AddComponent<Collider>(), p) == p, "fallback missing"); });
        Check("surface-unset-fallback", () => { var p = Profile(Clip()); Require(CharacterMovementAudioController.ResolveSurface(Floor(null), p) == p, "unset should fallback"); });
        Check("surface-null-collider-fallback", () => { var p = Profile(Clip()); Require(CharacterMovementAudioController.ResolveSurface(null!, p) == p, "null should fallback"); });
        Check("surface-empty-is-explicit-silence", () => { var c = Movement(Profile(Clip("fallback"))); Observe(c, floor: Floor(Profile())); Require(Shots(c) == 0, "silent floor replaced with fallback"); });
        Check("surface-all-null-is-explicit-silence", () => { var c = Movement(Profile(Clip("fallback"))); Observe(c, floor: Floor(Profile(null, null))); Require(Shots(c) == 0, "null clips replaced with fallback"); });
        Check("surface-and-fallback-unset-silent", () => { var c = Movement(); Observe(c); Require(Shots(c) == 0, "unset played"); });
        Check("selection-null-empty-all-null", () => { Require(AudioClipSelection.SelectAvoidingImmediateRepeat(null!, null!) == null, "null list"); Require(Profile().SelectClip(null!) == null, "empty list"); Require(Profile(null, null).SelectClip(null!) == null, "null list entries"); });
        Check("selection-single-can-repeat", () => { var a = Clip(); Require(Profile(null, a, a).SelectClip(a) == a, "single distinct rejected"); });
        Check("selection-multiple-covers-alternatives", () => { var a = Clip("a"); var b = Clip("b"); var c = Clip("c"); var p = Profile(a, null, a, b, c); var selected = Enumerable.Range(0, 12).Select(_ => p.SelectClip(a)).ToArray(); Require(selected.All(x => x != a && x != null), "immediate repeat"); Require(selected.Contains(b) && selected.Contains(c), "alternative omitted"); });
        Check("profile-name-volume", () => { var p = Profile(); p.name = "asset-name"; Set(p, "displayName", " "); Set(p, "volumeMultiplier", -1f); Require(p.DisplayName == "asset-name", "name fallback"); Near(p.VolumeMultiplier, 0); });
        Check("movement-auto-interval-and-no-event-double", () => { var c = Movement(Profile(Clip())); Observe(c); c.PlayFootstep(); Require(Shots(c) == 1, "auto/event duplicate"); Time.time = .2f; Observe(c); Require(Shots(c) == 1, "interval ignored"); Time.time = .43f; Observe(c); Require(Shots(c) == 2, "next interval missing"); });
        Check("movement-event-no-auto-and-minimum-interval", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); Require(Shots(c) == 0, "event mode auto"); c.PlayFootstep(); c.PlayFootstep(); Require(Shots(c) == 1, "minimum interval ignored"); Time.time = .081f; c.PlayFootstep(); Require(Shots(c) == 2, "event interval not resumed"); });
        Check("movement-stopped-silent", () => { var c = Movement(Profile(Clip())); Observe(c, 0); Require(Shots(c) == 0, "stopped auto"); Set(c, "footstepPlaybackMode", FootstepPlaybackMode.AnimationEvent); c.PlayFootstep(); Require(Shots(c) == 0, "stopped event"); });
        Check("movement-airborne-silent", () => { var c = Movement(Profile(Clip())); Observe(c, grounded: false); Set(c, "footstepPlaybackMode", FootstepPlaybackMode.AnimationEvent); c.PlayFootstep(); Require(Shots(c) == 0, "airborne played"); });
        Check("movement-speed-threshold", () => { var c = Movement(Profile(Clip())); Observe(c, .01f); Require(Shots(c) == 0, "threshold played"); Observe(c, .011f); Require(Shots(c) == 1, "movement suppressed"); });
        Check("movement-volume-and-se-routing", () => { var p = Profile(Clip()); Set(p, "volumeMultiplier", .4f); var c = Movement(p); Set(c, "footstepVolume", .5f); Observe(c); var a = Source(c); Near(a.Calls.Last().Volume, .2f); Near(a.spatialBlend, 0); Require(a.Route == SettingsAudioCategory.Se && !a.playOnAwake && !a.loop, "SE route/config"); });
        Check("movement-source-reused", () => { var go = new GameObject(); var child = new GameObject("MovementAudioSource"); child.transform.SetParent(go.transform); var expected = child.AddComponent<AudioSource>(); var c = go.AddComponent<CharacterMovementAudioController>(); Invoke(c, "Awake"); Require(Source(c) == expected && go.transform.children.Count == 1, "source duplicated"); });
        Check("movement-jump-land-once-per-transition", () => { var c = Movement(); var jump = Clip("jump"); var land = Clip("land"); Set(c, "jumpClip", jump); Set(c, "landingClip", land); c.NotifyJumpStarted(); Observe(c, 0, false, .05f); Observe(c, 0, false, .05f); Observe(c, 0); Observe(c, 0); var calls = Source(c).Calls.Where(x => x.Method == "OneShot").ToArray(); Require(calls.Length == 2 && calls[0].Clip == jump && calls[1].Clip == land, "jump/land count"); });
        Check("movement-tiny-airborne-no-landing", () => { var c = Movement(); Set(c, "landingClip", Clip()); Observe(c, 0, false, .02f); Observe(c, 0); Require(Shots(c) == 0, "tiny float landing"); });
        Check("movement-negative-airborne-time-no-landing", () => { var c = Movement(); Set(c, "landingClip", Clip()); Observe(c, 0, false, -.5f); Observe(c, 0); Require(Shots(c) == 0, "negative time landing"); });
        Check("movement-missing-jump-land-silent", () => { var c = Movement(); c.NotifyJumpStarted(); Observe(c, 0, false, .2f); Observe(c, 0); Require(Shots(c) == 0, "unset jump/land"); });
        Check("movement-pause-no-new-sounds", () => { var c = Movement(Profile(Clip())); Set(c, "jumpClip", Clip()); Set(c, "landingClip", Clip()); Time.timeScale = 0; Observe(c); c.PlayFootstep(); c.NotifyJumpStarted(); Observe(c, 0, false, 1); Observe(c, 0); Require(Shots(c) == 0, "paused request played"); });
        Check("movement-pause-resume-once-own-source", () => { var c = Movement(Profile(Clip())); var other = new GameObject().AddComponent<AudioSource>(); Observe(c); Time.timeScale = 0; Invoke(c, "Update"); Invoke(c, "Update"); Require(Source(c).Count("Pause") == 1 && other.Calls.Count == 0, "pause ownership"); Time.timeScale = 1; Invoke(c, "Update"); Invoke(c, "Update"); Require(Source(c).Count("UnPause") == 1 && Source(c).isPlaying, "resume ownership"); });
        Check("movement-disable-stops-paused-source", () => { var c = Movement(Profile(Clip())); Observe(c); Time.timeScale = 0; Invoke(c, "Update"); Disable(c); Enable(c); Time.timeScale = 1; Invoke(c, "Update"); Require(Source(c).Count("Stop") == 1 && Source(c).Count("UnPause") == 0 && !Source(c).isPlaying, "old playback resumed"); });
        Check("regression-disabled-observe-rejected", () => { var c = Movement(Profile(Clip())); Disable(c); Observe(c); Require(Shots(c) == 0, "disabled ObserveMovement played"); });
        Check("regression-disabled-jump-rejected", () => { var c = Movement(); Set(c, "jumpClip", Clip()); Disable(c); c.NotifyJumpStarted(); Require(Shots(c) == 0, "disabled jump played"); });
        Check("regression-disabled-event-rejected", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); Disable(c); c.PlayFootstep(); Require(Shots(c) == 0, "disabled footstep event played"); });
        Check("regression-reenable-stale-ground-event-rejected", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); Disable(c); Enable(c); c.PlayFootstep(); Require(Shots(c) == 0, "stale moving/ground state survived disable"); Observe(c); c.PlayFootstep(); Require(Shots(c) == 1, "new observation not accepted"); });
        Check("regression-reenable-stale-airborne-no-landing", () => { var c = Movement(); Set(c, "landingClip", Clip()); Observe(c, 0, false, .2f); Disable(c); Enable(c); Observe(c, 0); Require(Shots(c) == 0, "old airborne landed after reenable"); });
        Check("reset-ground-event-needs-fresh-observation", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); ResetMovement(c); c.PlayFootstep(); Require(Shots(c) == 0, "reset retained ground/moving event state"); Observe(c); c.PlayFootstep(); Require(Shots(c) == 1, "fresh observation rejected after reset"); });
        Check("reset-airborne-no-phantom-landing", () => { var c = Movement(); Set(c, "landingClip", Clip()); Observe(c, 0, false, .2f); ResetMovement(c); Observe(c, 0); Require(Shots(c) == 0, "reset retained airborne state"); });
        Check("reset-during-pause-retains-audio-pause-ownership", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); c.PlayFootstep(); Time.timeScale = 0; Invoke(c, "Update"); int before = Source(c).Calls.Count; ResetMovement(c); Require(Source(c).Calls.Count == before && Source(c).paused && !Source(c).isPlaying, "reset manipulated paused source"); c.PlayFootstep(); Require(Shots(c) == 1 && Source(c).Count("UnPause") == 0, "reset unpaused or replayed"); Time.timeScale = 1; Invoke(c, "Update"); Require(Source(c).Count("UnPause") == 1 && Source(c).isPlaying, "pause ownership lost after reset"); c.PlayFootstep(); Require(Shots(c) == 1, "stale event after paused reset"); });
        Check("suspend-stops-event-until-fresh-observation", () => { var c = Movement(Profile(Clip()), FootstepPlaybackMode.AnimationEvent); Observe(c); SuspendMovement(c); c.PlayFootstep(); Require(Shots(c) == 0, "suspended movement accepted event"); Observe(c); c.PlayFootstep(); Require(Shots(c) == 1, "fresh movement did not resume event"); });
        Check("suspend-preserves-airborne-landing-across-pause", () => { var c = Movement(); var land = Clip("landing"); Set(c, "landingClip", land); Observe(c, 0, false, .2f); Time.timeScale = 0; Invoke(c, "Update"); int before = Source(c).Calls.Count; SuspendMovement(c); Require(Source(c).Calls.Count == before, "suspend manipulated audio source"); Time.timeScale = 1; Invoke(c, "Update"); Observe(c, 0); Observe(c, 0); var calls = Source(c).Calls.Where(x => x.Method == "OneShot").ToArray(); Require(calls.Length == 1 && calls[0].Clip == land, "pause lost or duplicated landing"); });
        Check("movement-validation-nonnegative", () => { var c = Movement(); foreach (var n in new[] { "footstepVolume", "footstepInterval", "minimumFootstepInterval", "jumpVolume", "landingVolume", "minimumAirborneSeconds" }) Set(c, n, -1f); Invoke(c, "OnValidate"); foreach (var n in new[] { "footstepVolume", "minimumFootstepInterval", "jumpVolume", "landingVolume", "minimumAirborneSeconds" }) Near(Get<float>(c, n), 0); Near(Get<float>(c, "footstepInterval"), .05f); });

        foreach (var type in Enum.GetValues<CatVoiceType>())
            Check("cat-category-" + type, () => { var c = Voice(); SetCategories(c); Require(c.PlayCatVoice(type), "category refused"); Require(Source(c).clip!.name.Equals(type.ToString(), StringComparison.OrdinalIgnoreCase), "wrong category"); Require(Source(c).Count("Play") == 1, "play count"); });
        Check("cat-empty-and-invalid-silent", () => { var c = Voice(); foreach (var type in Enum.GetValues<CatVoiceType>()) Require(!c.PlayCatVoice(type), "empty category played"); Require(!c.PlayCatVoice((CatVoiceType)99), "invalid category played"); Require(Source(c).Count("Play") == 0, "empty calls"); });
        Check("cat-null-category-silent", () => { var c = Voice(); Set(c, "call", null); Require(!c.PlayCatVoice(CatVoiceType.Call), "null category played"); });
        Check("cat-multiple-no-immediate-repeat", () => { var c = Voice(); Set(c, "call", Category(Clip("a"), Clip("b"), null)); AudioClip? previous = null; for (int i = 0; i < 8; i++) { Source(c).CompleteForTest(); Require(c.PlayCatVoice(CatVoiceType.Call), "voice refused"); Require(Source(c).clip != previous, "voice immediate repeat"); previous = Source(c).clip; } });
        Check("cat-ignore-while-playing", () => { var c = Voice(); SetCategories(c); Require(c.PlayCatVoice(CatVoiceType.Call), "first refused"); Require(!c.PlayCatVoice(CatVoiceType.Angry), "overlap accepted"); Require(Source(c).Count("Play") == 1 && Source(c).Count("Stop") == 0 && Source(c).clip!.name == "call", "active voice changed"); });
        Check("cat-replace-while-playing", () => { var c = Voice(); SetCategories(c); Set(c, "overlapPolicy", CatVoiceOverlapPolicy.Replace); c.PlayCatVoice(CatVoiceType.Call); Require(c.PlayCatVoice(CatVoiceType.Angry), "replace refused"); Require(Source(c).Count("Play") == 2 && Source(c).Count("Stop") == 1 && Source(c).clip!.name == "angry", "replace count"); });
        Check("cat-empty-replace-preserves-current", () => { var c = Voice(); Set(c, "call", Category(Clip())); Set(c, "overlapPolicy", CatVoiceOverlapPolicy.Replace); c.PlayCatVoice(CatVoiceType.Call); Require(!c.PlayCatVoice(CatVoiceType.Angry) && Source(c).isPlaying && Source(c).Count("Stop") == 0, "empty replacement stopped current"); });
        Check("cat-volume-and-route", () => { var c = Voice(); var category = Category(Clip()); Set(category, "volume", .4f); Set(c, "call", category); Set(c, "baseVolume", .5f); c.PlayCatVoice(CatVoiceType.Call); Near(Source(c).volume, .2f); Near(Source(c).spatialBlend, 1); Require(Source(c).Route == SettingsAudioCategory.CatVoice && !Source(c).loop && !Source(c).playOnAwake, "voice route/config"); });
        Check("cat-preview-no-playback-or-routing", () => { var c = Voice(false); SetCategories(c); Require(c.TryGetPreview(CatVoiceType.Happy, out var clip, out var volume) && clip.name == "happy" && volume == 1, "preview result"); Require(c.gameObject.transform.children.Count == 0 && SettingsManager.Routes.Count == 0, "preview side effect"); });
        Check("cat-pause-reject-and-own-resume", () => { var c = Voice(); SetCategories(c); c.PlayCatVoice(CatVoiceType.Call); var other = new GameObject().AddComponent<AudioSource>(); Time.timeScale = 0; Invoke(c, "Update"); Require(!c.PlayCatVoice(CatVoiceType.Angry), "pause accepted"); Require(Source(c).Count("Pause") == 1 && Source(c).Count("Play") == 1 && other.Calls.Count == 0, "pause ownership"); Time.timeScale = 1; Invoke(c, "Update"); Invoke(c, "Update"); Require(Source(c).Count("UnPause") == 1 && Source(c).isPlaying, "resume count"); });
        Check("cat-disabled-reject-no-route", () => { var c = Voice(false); SetCategories(c); c.enabled = false; Require(!c.PlayCatVoice(CatVoiceType.Call) && c.transform.children.Count == 0 && SettingsManager.Routes.Count == 0, "disabled side effect"); });
        Check("cat-inactive-reject", () => { var c = Voice(); SetCategories(c); c.gameObject.SetActive(false); Require(!c.PlayCatVoice(CatVoiceType.Call) && Source(c).Count("Play") == 0, "inactive played"); });
        Check("cat-disable-reenable-no-old-resume", () => { var c = Voice(); SetCategories(c); c.PlayCatVoice(CatVoiceType.Call); Time.timeScale = 0; Invoke(c, "Update"); Disable(c); Enable(c); Time.timeScale = 1; Invoke(c, "Update"); Require(Source(c).Count("Stop") == 1 && Source(c).Count("UnPause") == 0 && !Source(c).isPlaying, "old voice resumed"); Require(c.PlayCatVoice(CatVoiceType.Angry) && Source(c).Count("Play") == 2, "new voice failed"); });
        Check("cat-source-reuse", () => { var c = Voice(false); var child = new GameObject("CatVoiceAudioSource"); child.transform.SetParent(c.transform); var expected = child.AddComponent<AudioSource>(); Invoke(c, "Awake"); Require(Source(c) == expected && c.transform.children.Count == 1, "cat source duplicated"); });
        Check("cat-timeline-four-signals-once", () => { foreach (var n in new[] { "Call", "Angry", "Happy", "Displeased" }) Resources.Assets["Audio/Signals/CatVoice_" + n] = new SignalAsset(); var c = Voice(); SetCategories(c); Invoke(c, "Awake"); var receiver = c.GetComponent<SignalReceiver>()!; Require(receiver.Reactions.Count == 4 && receiver.Reactions.Values.All(x => x.Count == 1), "signal duplicate/missing"); foreach (var item in Resources.Assets) { Source(c).CompleteForTest(); receiver.GetReaction((SignalAsset)item.Value)!.Invoke(); Require(Source(c).clip!.name.Equals(item.Key.Split('_').Last(), StringComparison.OrdinalIgnoreCase), "signal wrong category"); } Require(Source(c).Count("Play") == 4, "signal counts"); });
        Check("cat-timeline-existing-reaction-preserved", () => { var signal = new SignalAsset(); Resources.Assets["Audio/Signals/CatVoice_Call"] = signal; var c = Voice(false); var r = c.gameObject.AddComponent<SignalReceiver>(); var reaction = new UnityEvent(); int calls = 0; reaction.AddListener(() => calls++); r.AddReaction(signal, reaction); Invoke(c, "Awake"); r.GetReaction(signal)!.Invoke(); Require(calls == 1 && Source(c).Count("Play") == 0 && r.Reactions.Count == 1, "authored signal replaced"); });
        Check("cat-timeline-missing-assets-no-receiver", () => { var c = Voice(); Require(c.GetComponent<SignalReceiver>() == null, "missing signals created receiver"); });
        Check("cat-volume-validation", () => { var c = Voice(); Set(c, "baseVolume", -1f); Invoke(c, "OnValidate"); Near(Get<float>(c, "baseVolume"), 0); var category = Category(); Set(category, "volume", -1f); Near(category.Volume, 0); });
        foreach (var type in new[] { typeof(CharacterMovementAudioController), typeof(CatVoiceAudioController), typeof(CatVoiceCategory), typeof(FootstepSurfaceProfile), typeof(FootstepSurface) })
            Check("japanese-serialized-attributes-" + type.Name, () => { var fields = type.GetFields(Hidden).Where(f => f.IsDefined(typeof(SerializeField), false)).ToArray(); Require(fields.Length > 0, "no serialized fields"); foreach (var field in fields) { var label = field.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? ""; var help = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? ""; Require(Regex.IsMatch(label, "[ぁ-んァ-ン一-龯]") && Regex.IsMatch(help, "[ぁ-んァ-ン一-龯]"), "missing Japanese label/help: " + field.Name); } });
        foreach (var type in new[] { typeof(FootstepPlaybackMode), typeof(CatVoiceType), typeof(CatVoiceOverlapPolicy) })
            Check("japanese-enum-attributes-" + type.Name, () => { foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) Require(Regex.IsMatch(field.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? "", "[ぁ-んァ-ン一-龯]"), "missing Japanese enum: " + field.Name); });
        Console.WriteLine(JsonSerializer.Serialize(new { scope = "Whole unchanged production sources with fake Unity/Settings/Timeline; no real engine/audio/mixer/settings/persistence", count = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
