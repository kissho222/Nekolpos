using System.Collections.Generic;
using NUnit.Framework;
using Nekolpos.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Nekolpos.System.Editor
{
    public sealed class TimelineAvatarMaskGeneratorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            created.Clear();
        }

        [Test]
        public void CollectsUnionOfFiniteClipTransformPathsOnly()
        {
            var timeline = CreateTimeline();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "Reaction");

            TimelineClip finite = track.CreateClip<AnimationPlayableAsset>();
            ((AnimationPlayableAsset)finite.asset).clip = CreateTransformClip("FiniteA", "Body/Head");
            TimelineClip anotherFinite = track.CreateClip<AnimationPlayableAsset>();
            ((AnimationPlayableAsset)anotherFinite.asset).clip = CreateTransformClip("FiniteB", "Body/LeftFrontLeg");
            var materialOnly = new AnimationClip();
            created.Add(materialOnly);
            AnimationUtility.SetEditorCurve(
                materialOnly,
                EditorCurveBinding.FloatCurve("Renderer", typeof(Renderer), "material._Color.r"),
                AnimationCurve.Constant(0f, 1f, 1f));
            TimelineClip materialClip = track.CreateClip<AnimationPlayableAsset>();
            ((AnimationPlayableAsset)materialClip.asset).clip = materialOnly;

            Assert.That(TimelineAvatarMaskGenerator.TryGetAnimatedTransformPaths(track, out HashSet<string> paths, out string error), Is.True, error);
            Assert.That(paths, Is.EquivalentTo(new[] { "Body/Head", "Body/LeftFrontLeg" }));
        }

        [Test]
        public void CollectsTransformPathsFromInfiniteClip()
        {
            var timeline = CreateTimeline();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "InfiniteReaction");
            track.CreateInfiniteClip("Infinite");
            AnimationUtility.SetEditorCurve(
                track.infiniteClip,
                EditorCurveBinding.FloatCurve("Body/Tail", typeof(Transform), "localEulerAnglesRaw.y"),
                AnimationCurve.Linear(0f, 0f, 1f, 1f));

            Assert.That(TimelineAvatarMaskGenerator.TryGetAnimatedTransformPaths(track, out HashSet<string> paths, out string error), Is.True, error);
            Assert.That(paths, Is.EquivalentTo(new[] { "Body/Tail" }));
        }

        [Test]
        public void PopulatesOnlyCurveTargetsAndTheirAncestors()
        {
            Transform root = CreateHierarchy();
            var mask = new AvatarMask();
            created.Add(mask);

            Assert.That(
                TimelineAvatarMaskGenerator.TryPopulateMask(mask, root, new[] { "Body/LeftFrontLeg/Foot" }, out string error),
                Is.True,
                error);

            var entries = new Dictionary<string, bool>();
            for (int index = 0; index < mask.transformCount; index++)
            {
                entries.Add(mask.GetTransformPath(index), mask.GetTransformActive(index));
            }

            Assert.That(entries.Keys, Is.EquivalentTo(new[] { "", "Body", "Body/LeftFrontLeg", "Body/LeftFrontLeg/Foot" }));
            Assert.That(entries[""], Is.False);
            Assert.That(entries["Body"], Is.False);
            Assert.That(entries["Body/LeftFrontLeg"], Is.False);
            Assert.That(entries["Body/LeftFrontLeg/Foot"], Is.True);
            Assert.That(entries.ContainsKey("Body/RightFrontLeg"), Is.False);
            Assert.That(entries.ContainsKey("Body/LeftFrontLeg/Foot/Claw"), Is.False);
        }

        [Test]
        public void AssignMaskEnablesApplyAvatarMask()
        {
            var timeline = CreateTimeline();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "HeadOnly");
            var mask = new AvatarMask();
            created.Add(mask);

            TimelineAvatarMaskGenerator.AssignMask(track, mask);

            Assert.That(track.avatarMask, Is.SameAs(mask));
            Assert.That(track.applyAvatarMask, Is.True);
        }

        private TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            created.Add(timeline);
            return timeline;
        }

        private AnimationClip CreateTransformClip(string name, string path)
        {
            var clip = new AnimationClip();
            clip.name = name;
            created.Add(clip);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
            return clip;
        }

        private Transform CreateHierarchy()
        {
            var root = new GameObject("MaskRoot");
            created.Add(root);
            Transform body = CreateChild(root.transform, "Body");
            Transform leftLeg = CreateChild(body, "LeftFrontLeg");
            Transform foot = CreateChild(leftLeg, "Foot");
            CreateChild(foot, "Claw");
            CreateChild(body, "RightFrontLeg");
            return root.transform;
        }

        private Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent);
            return child;
        }
    }
}
