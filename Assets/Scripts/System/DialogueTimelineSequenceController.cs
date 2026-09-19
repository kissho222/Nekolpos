using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class DialogueTimelineSequenceController : MonoBehaviour
    {
        [Serializable]
        public sealed class Sequence
        {
            public string id;
            public TimelineAsset start;
            public TimelineAsset loop;
            public TimelineAsset end;
            [Tooltip("オン: Timeline中も既存の視線制御を継続。オフ: 演出中だけ停止し、終了後に元の設定で復帰。")]
            public bool keepLookAtDuringTimeline;
            [Tooltip("Timelineと実際に競合する場合だけ一時停止するBehaviour。通常待機AnimatorやPresentation Controllerはここへ登録しない。")]
            public Behaviour[] additionalBehavioursToSuspend = Array.Empty<Behaviour>();
            [Min(0f)]
            [Tooltip("Start・Loop・Endを切り替える時に、直前の骨姿勢から補間する時間（秒）。0で即時切替。")]
            public float phaseTransitionSeconds = 0.12f;
        }

        public enum Phase { Idle, Start, Loop, End }
        public const string Prefix = "timeline_sequence:";
        [SerializeField] private Animator catAnimator;
        [SerializeField] private Sequence[] sequences = Array.Empty<Sequence>();
        [SerializeField] private string idleStateName = "CatSimple_Lie_belly_loop_1";
        private PlayableDirector director;
        private Sequence current;
        private double elapsed;
        private Vector3 rootPosition;
        private Quaternion rootRotation;
        private Vector3 rootScale;
        private bool rootMotion;
        private bool singleTimelinePlayback;
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private readonly List<LocalPose> phaseTransitionSource = new List<LocalPose>();
        private double phaseTransitionElapsed;
        private float phaseTransitionDuration;
        private CatPresentationModeController presentation;
        private bool presentationTimelineActive;
        public Phase CurrentPhase { get; private set; }
        public Animator CatAnimator => catAnimator;

        private struct LocalPose
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        public static bool TryParse(string command, out string id, out bool end)
        {
            id = null;
            end = false;
            if (string.IsNullOrWhiteSpace(command) || !command.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            string value = command.Trim().Substring(Prefix.Length).Trim();
            end = value.EndsWith("End", StringComparison.OrdinalIgnoreCase);
            if (!end && !value.EndsWith("Start", StringComparison.OrdinalIgnoreCase)) return false;
            id = value.Substring(0, value.Length - (end ? 3 : 5)).Trim().TrimEnd(':').Trim();
            return !string.IsNullOrWhiteSpace(id);
        }

        /// <summary>
        /// Resolves the authored look-at policy for a Timeline registered in a sequence.
        /// Editor preview uses the same setting as runtime instead of maintaining a second list.
        /// </summary>
        public bool TryGetTimelineLookAtSetting(TimelineAsset timeline, out bool keepLookAt)
        {
            keepLookAt = false;
            if (timeline == null || sequences == null)
            {
                return false;
            }

            foreach (Sequence sequence in sequences)
            {
                if (sequence == null ||
                    (sequence.start != timeline && sequence.loop != timeline && sequence.end != timeline))
                {
                    continue;
                }

                keepLookAt = sequence.keepLookAtDuringTimeline;
                return true;
            }

            return false;
        }

        public void Configure(Animator animator, Sequence[] bindings)
        {
            if (CurrentPhase != Phase.Idle) throw new InvalidOperationException("Cannot configure an active Timeline sequence.");
            catAnimator = animator;
            sequences = bindings ?? Array.Empty<Sequence>();
        }

        /// <summary>
        /// Plays one authored Timeline through the same arbitration and recovery path as
        /// dialogue sequences. Callers do not need Timeline-specific glue code.
        /// </summary>
        public bool PlayTimeline(TimelineAsset timeline, bool keepLookAtDuringTimeline = false, float phaseTransitionSeconds = 0.12f)
        {
            if (timeline == null || double.IsNaN(timeline.duration) || double.IsInfinity(timeline.duration) || timeline.duration <= 0d)
            {
                Debug.LogError("[DialogueTimelineSequence] A finite, non-empty Timeline is required.", this);
                return false;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogError("[DialogueTimelineSequence] Controller must be active to play a Timeline.", this);
                return false;
            }

            if (catAnimator == null)
            {
                catAnimator = FindFirstObjectByType<CatPresentationModeController>(FindObjectsInactive.Include)?.NormalAnimator;
            }

            if (catAnimator == null || !catAnimator.gameObject.activeInHierarchy)
            {
                Debug.LogError("[DialogueTimelineSequence] An active cat Animator is required.", this);
                return false;
            }

            if (!ValidateTimeline(timeline))
            {
                return false;
            }

            Finish();
            singleTimelinePlayback = true;
            current = new Sequence
            {
                id = timeline.name,
                start = timeline,
                keepLookAtDuringTimeline = keepLookAtDuringTimeline,
                phaseTransitionSeconds = Mathf.Max(0f, phaseTransitionSeconds)
            };
            CaptureAndSuspend();
            PlayPhase(Phase.Start);
            Debug.Log($"[DialogueTimelineSequence] Timeline started: {timeline.name}", this);
            return true;
        }

        public bool Execute(string command)
        {
            if (!TryParse(command, out string id, out bool end))
            {
                Debug.LogError($"[DialogueTimelineSequence] Invalid command: {command}", this);
                return false;
            }
            if (!isActiveAndEnabled || catAnimator == null || !catAnimator.gameObject.activeInHierarchy)
            {
                Debug.LogError("[DialogueTimelineSequence] Active controller and Normal_Cat Animator are required.", this);
                return false;
            }
            Sequence binding = Array.Find(sequences, s => s != null && string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase));
            if (binding == null)
            {
                Debug.LogError($"[DialogueTimelineSequence] Unknown sequence: {id}", this);
                return false;
            }
            if (end)
            {
                if (current == null) return true; // An optional End is safe when no sequence was started.
                if (current != binding)
                {
                    Debug.LogError($"[DialogueTimelineSequence] Cannot end {id}; {current.id} is active.", this);
                    return false;
                }
                if (CurrentPhase != Phase.End) PlayPhase(Phase.End);
                return true;
            }
            if (current == binding && CurrentPhase != Phase.End) return true;
            if (!Validate(binding)) return false;
            Finish();
            singleTimelinePlayback = false;
            current = binding;
            CaptureAndSuspend();
            PlayPhase(Phase.Start);
            Debug.Log($"[DialogueTimelineSequence] Timeline started: {binding.id} phase={CurrentPhase}", this);
            return true;
        }

        private bool Validate(Sequence binding)
        {
            foreach (var asset in new[] { binding.start, binding.loop, binding.end })
            {
                if (asset == null || double.IsNaN(asset.duration) || double.IsInfinity(asset.duration) || asset.duration <= 0)
                {
                    Debug.LogError($"[DialogueTimelineSequence] {binding.id} needs three finite, non-empty Timelines.", this);
                    return false;
                }
                foreach (var output in asset.outputs)
                    if (!(output.sourceObject is AnimationTrack))
                    {
                        Debug.LogError($"[DialogueTimelineSequence] {asset.name}: only cat Animation tracks are supported.", this);
                        return false;
                    }
            }
            if (catAnimator.runtimeAnimatorController == null || !catAnimator.HasState(0, Animator.StringToHash(idleStateName)))
            {
                Debug.LogError($"[DialogueTimelineSequence] Missing idle state: {idleStateName}", this);
                return false;
            }
            return true;
        }

        private bool ValidateTimeline(TimelineAsset asset)
        {
            foreach (var output in asset.outputs)
            {
                if (!(output.sourceObject is AnimationTrack))
                {
                    Debug.LogError($"[DialogueTimelineSequence] {asset.name}: only cat Animation tracks are supported.", this);
                    return false;
                }
            }

            return true;
        }

        private void CaptureAndSuspend()
        {
            presentation = null;
            foreach (var candidate in FindObjectsByType<CatPresentationModeController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.NormalAnimator == catAnimator) { presentation = candidate; break; }
            presentationTimelineActive = presentation != null;
            presentation?.BeginTimelinePresentation();

            // BeginTimelinePresentationで現在の基本会話拠点へ揃えた後の姿勢を基準にする。
            // TimelineのTransformキーで、Desk選択中の猫がTable側へ引かれないようにする。
            Transform root = catAnimator.transform;
            rootPosition = root.localPosition;
            rootRotation = root.localRotation;
            rootScale = root.localScale;
            rootMotion = catAnimator.applyRootMotion;
            if (!current.keepLookAtDuringTimeline)
                FadeLookRigs(0f, current.phaseTransitionSeconds);
            if (current.additionalBehavioursToSuspend != null)
            {
                foreach (Behaviour behaviour in current.additionalBehavioursToSuspend)
                {
                    Suspend(behaviour);
                }
            }
            foreach (var other in catAnimator.GetComponentsInChildren<PlayableDirector>(true))
            {
                other.Stop();
                Suspend(other);
            }
            catAnimator.applyRootMotion = false;
            if (director == null)
            {
                var host = new GameObject("DialogueTimelineSequencePlayer");
                host.transform.SetParent(transform, false);
                director = host.AddComponent<PlayableDirector>();
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
            }
        }

        private void Suspend(Behaviour component)
        {
            if (component == null || !component.enabled) return;
            suspended.Add(component);
            component.enabled = false;
        }

        private void FadeLookRigs(float targetWeight, float fadeSeconds)
        {
            if (catAnimator == null) return;
            foreach (var rig in catAnimator.GetComponentsInChildren<NekomataLookRigController>(true))
            {
                // Preserve intentionally disabled rigs. Active rigs fade their influence while
                // continuing to retain the same look target and smoothing state.
                if (rig != null && rig.enabled)
                    rig.FadeExternalWeight(targetWeight, fadeSeconds);
            }
        }

        private void PlayPhase(Phase phase)
        {
            CapturePhaseTransitionSource();
            director.Stop();
            director.playableAsset = singleTimelinePlayback
                ? current.start
                : phase == Phase.Start ? current.start : phase == Phase.Loop ? current.loop : current.end;
            foreach (var output in director.playableAsset.outputs)
                director.SetGenericBinding(output.sourceObject, catAnimator);
            CurrentPhase = phase;
            elapsed = 0;
            director.time = 0;
            director.Play();
            director.Evaluate();
            ApplyPhaseTransition(0d);
            LockCatRootPose();
        }

        private void CapturePhaseTransitionSource()
        {
            phaseTransitionSource.Clear();
            phaseTransitionElapsed = 0d;
            phaseTransitionDuration = current != null ? Mathf.Max(0f, current.phaseTransitionSeconds) : 0f;
            if (CurrentPhase == Phase.Idle || phaseTransitionDuration <= 0f || catAnimator == null)
            {
                return;
            }

            // The model hierarchy also contains look-rig helpers. Capture only transforms that
            // participate in skinning, so a transition never moves the unchanged gaze target.
            var skeleton = new HashSet<Transform>();
            foreach (var renderer in catAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in renderer.bones)
                {
                    for (Transform node = bone; node != null; node = node.parent)
                    {
                        skeleton.Add(node);
                        if (node == catAnimator.transform) break;
                    }
                }
            }

            foreach (Transform transform in skeleton)
            {
                phaseTransitionSource.Add(new LocalPose
                {
                    transform = transform,
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                });
            }
        }

        private void ApplyPhaseTransition(double deltaSeconds)
        {
            if (phaseTransitionSource.Count == 0) return;
            phaseTransitionElapsed += Math.Max(0d, deltaSeconds);
            float linear = phaseTransitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01((float)(phaseTransitionElapsed / phaseTransitionDuration));
            // Ease-in/out avoids a visible change in velocity at both ends of the handoff.
            float t = linear * linear * (3f - 2f * linear);
            foreach (var source in phaseTransitionSource)
            {
                if (source.transform == null) continue;
                source.transform.localPosition = Vector3.LerpUnclamped(source.localPosition, source.transform.localPosition, t);
                source.transform.localRotation = Quaternion.SlerpUnclamped(source.localRotation, source.transform.localRotation, t);
                source.transform.localScale = Vector3.LerpUnclamped(source.localScale, source.transform.localScale, t);
            }

            if (linear >= 1f) phaseTransitionSource.Clear();
        }

        private void LockCatRootPose()
        {
            if (catAnimator == null)
            {
                return;
            }

            catAnimator.transform.localPosition = rootPosition;
            catAnimator.transform.localRotation = rootRotation;
            catAnimator.transform.localScale = rootScale;
        }

        // A manual Director evaluated in Update is overwritten by the normal Animator's
        // animation pass before rendering. Evaluate after that pass, before the look rig's
        // LateUpdate (execution order 10000), so optional gaze can follow the Timeline pose.
        private void LateUpdate()
        {
            if (CurrentPhase == Phase.Idle)
            {
                ApplyPhaseTransition(Time.deltaTime);
                return;
            }

            Advance(Time.deltaTime);
            if (CurrentPhase != Phase.Idle)
            {
                LockCatRootPose();
            }
        }

        // Manual clock keeps phase transitions deterministic and avoids stopped-event
        // callbacks caused by replacing the Director's Timeline.
        public void Advance(double seconds)
        {
            if (CurrentPhase == Phase.Idle) return;
            if (catAnimator == null || !catAnimator.gameObject.activeInHierarchy || director == null)
            {
                Finish();
                return;
            }
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            elapsed += seconds;
            double duration = director.duration;
            double evaluatedSeconds = seconds;
            if (elapsed >= duration)
            {
                if (singleTimelinePlayback)
                {
                    director.time = duration;
                    director.Evaluate();
                    Finish(blendToIdle: true);
                    return;
                }

                if (CurrentPhase == Phase.End)
                {
                    director.time = duration;
                    director.Evaluate();
                    Finish(blendToIdle: true);
                    return;
                }
                if (CurrentPhase == Phase.Start)
                {
                    double remainder = elapsed - duration;
                    // Sample the last pose of Start before replacing its Director asset.
                    director.time = duration;
                    director.Evaluate();
                    PlayPhase(Phase.Loop);
                    elapsed = remainder;
                    duration = director.duration;
                    evaluatedSeconds = remainder;
                }
                else
                {
                    // Loop assets are authored independently too. Blend their last frame to
                    // frame zero so a small difference in recorded keys cannot pop each lap.
                    double remainder = elapsed % duration;
                    director.time = duration;
                    director.Evaluate();
                    CapturePhaseTransitionSource();
                    elapsed = remainder;
                    evaluatedSeconds = remainder;
                }
                elapsed %= duration;
            }
            director.time = elapsed;
            director.Evaluate();
            ApplyPhaseTransition(evaluatedSeconds);
            LockCatRootPose();
        }

        private void OnDisable() => Finish();

        public void Finish(bool blendToIdle = false)
        {
            if (current == null) return;
            string finishedId = current.id;
            Phase finishedPhase = CurrentPhase;
            bool restoreLookRigs = !current.keepLookAtDuringTimeline;
            float lookFadeSeconds = current.phaseTransitionSeconds;
            if (blendToIdle)
            {
                CapturePhaseTransitionSource();
            }
            if (director != null) director.Stop();
            current = null;
            singleTimelinePlayback = false;
            CurrentPhase = Phase.Idle;
            if (!blendToIdle) phaseTransitionSource.Clear();
            bool resumePresentation = presentationTimelineActive && presentation != null;
            CatPositionController homeController = presentation != null
                ? presentation.PositionController
                : catAnimator != null ? catAnimator.GetComponentInParent<CatPositionController>() : null;
            bool animatorIsHomeRoot = homeController != null && catAnimator != null && homeController.transform == catAnimator.transform;
            if (catAnimator != null)
            {
                if (catAnimator.gameObject.activeInHierarchy)
                {
                    catAnimator.Play(idleStateName, 0, 0f);
                    catAnimator.Update(0f);
                }
                // CatPositionControllerがAnimator自身を拠点管理している場合、保存した
                // Timeline開始前のlocalPositionを復元すると、DeskからTable側の姿勢が
                // 一瞬表示されることがある。拠点ルートの姿勢は現在のHomeに任せる。
                if (!animatorIsHomeRoot)
                {
                    catAnimator.transform.localPosition = rootPosition;
                    catAnimator.transform.localRotation = rootRotation;
                    catAnimator.transform.localScale = rootScale;
                }
                catAnimator.applyRootMotion = rootMotion;
            }

            // Rebuild gaze only after restoring the base pose. ResetToDefault is a full
            // character reset: it changes the authored gaze axes, mode and manual-aim setting.
            // Ending an animation must preserve those settings and the existing target.
            for (int i = suspended.Count - 1; i >= 0; i--)
                if (suspended[i] != null) suspended[i].enabled = true;
            suspended.Clear();
            if (restoreLookRigs) FadeLookRigs(1f, lookFadeSeconds);
            if (resumePresentation) presentation.ResumeIdleAfterTimeline();
            else homeController?.MoveToHome();
            // Timeline終了処理の最終地点を必ず現在の基本拠点に揃える。
            // 他の復帰処理が途中で姿勢を更新しても、Desk選択をTableへ戻さない。
            homeController?.MoveToHome();
            presentationTimelineActive = false;
            Debug.Log($"[DialogueTimelineSequence] Timeline {(blendToIdle ? "finished" : "interrupted")}: {finishedId} phase={finishedPhase}", this);
        }
    }
}
