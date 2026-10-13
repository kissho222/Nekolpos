#if UNITY_EDITOR
using System.Linq;
using Nekolpos.ActionSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Nekolpos.System.Editor
{
    public sealed class TransportAnimationTests
    {
        private GameObject instance;
        private TransportAnimationView view;
        private int wipeOpaqueCount, wipeTransparentCount;
        private void CountOpaque() => wipeOpaqueCount++;
        private void CountTransparent() => wipeTransparentCount++;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/TransportAnimationView.prefab");
            Assert.That(prefab, Is.Not.Null);
            instance = UnityEngine.Object.Instantiate(prefab);
            view = instance.GetComponent<TransportAnimationView>();
            var data = new SerializedObject(view);
            data.FindProperty("soundSource").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown() { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }

        [TestCase("Basket", "プレイヤーカゴ", false)]
        [TestCase("Collar", "プレイヤー服襟", false)]
        [TestCase("Feet", "プレイヤー吊り", false)]
        [TestCase("FeetWeary", "プレイヤー吊り", true)]
        [TestCase("Head", "プレイヤー頭", false)]
        [TestCase("HeadWeary", "プレイヤー頭", true)]
        [TestCase("Back", "プレイヤー背中", false)]
        public void Preset_PlaysExpectedLayersInSyncAndLoops(string id, string player, bool weary)
        {
            Assert.That(view.ValidatePreset(id, out string error), Is.True, error);
            Assert.That(view.TryPlay(id), Is.True);
            var images = instance.GetComponentsInChildren<Image>().Where(x => x.enabled).ToArray();
            Assert.That(images.Any(x => x.name == player), Is.True);
            Assert.That(images.Any(x => x.name == "ネルコ不機嫌線"), Is.EqualTo(weary));
            Assert.That(images.Count(x => x.name.StartsWith("プレイヤー")), Is.EqualTo(1));
            for (int frame = 0; frame < 6; frame++)
            {
                Assert.That(view.CurrentFrame, Is.EqualTo(frame));
                Assert.That(images.All(x => x.sprite.name == x.name + (frame + 1)), Is.True);
                view.Advance(view.Presets.First(x => x.id == id).frameSeconds[frame]);
            }
            Assert.That(view.CurrentFrame, Is.Zero);
            Assert.That(images.All(x => x.sprite.name == x.name + "1"), Is.True);
        }

        [Test]
        public void RepeatedStart_IsRejected_AndStopClearsPreviousPlayerAndExpression()
        {
            Assert.That(view.TryPlay("FeetWeary"), Is.True);
            view.Advance(.6f);
            Assert.That(view.TryPlay("Back"), Is.False);
            view.StopPlayback();
            Assert.That(view.IsPlaying, Is.False);
            Assert.That(instance.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(instance.GetComponentsInChildren<Image>().All(x => !x.enabled && x.sprite == null), Is.True);
            Assert.That(view.TryPlay("Back"), Is.True);
            Assert.That(view.CurrentFrame, Is.Zero);
            Assert.That(instance.GetComponentsInChildren<Image>().Single(x => x.name == "ネルコ不機嫌線").enabled, Is.False);
        }

        [UnityTest]
        public global::System.Collections.IEnumerator Disable_CancelsPlayback_AndNextStartBeginsAtFirstFrame()
        {
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            yield return new EnterPlayMode();
            instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/TransportAnimationView.prefab"));
            view = instance.GetComponent<TransportAnimationView>();
            Assert.That(view.TryPlay("Collar"), Is.True);
            view.Advance(.5f);
            view.enabled = false;
            Assert.That(view.IsPlaying, Is.False);
            Assert.That(instance.GetComponent<CanvasGroup>().alpha, Is.Zero);
            view.enabled = true;
            Assert.That(view.TryPlay("Head"), Is.True);
            Assert.That(view.CurrentFrame, Is.Zero);
            UnityEngine.Object.Destroy(instance);
            instance = null;
            yield return new ExitPlayMode();
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("timing")]
        [TestCase("layer")]
        public void InvalidPreset_DoesNotShowOrStart(string failure)
        {
            var data = new SerializedObject(view);
            var frames = data.FindProperty("layers").GetArrayElementAtIndex(2).FindPropertyRelative("frames");
            if (failure == "missing") frames.GetArrayElementAtIndex(3).objectReferenceValue = null;
            if (failure == "duplicate") frames.GetArrayElementAtIndex(3).objectReferenceValue = frames.GetArrayElementAtIndex(0).objectReferenceValue;
            if (failure == "timing") data.FindProperty("presets").GetArrayElementAtIndex(1).FindPropertyRelative("frameSeconds").GetArrayElementAtIndex(0).floatValue = 0;
            if (failure == "layer") data.FindProperty("presets").GetArrayElementAtIndex(1).FindPropertyRelative("layers").GetArrayElementAtIndex(0).stringValue = "MissingLayer";
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(view.ValidatePreset("Collar", out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            LogAssert.Expect(LogType.Warning, new global::System.Text.RegularExpressions.Regex("\\[TransportAnimation\\] 再生できません:"));
            Assert.That(view.TryPlay("Collar"), Is.False);
            Assert.That(view.IsPlaying, Is.False);
            Assert.That(instance.GetComponent<CanvasGroup>().alpha, Is.Zero);
        }

        [Test]
        public void Overlay_IsAboveFadeCanvas_AndLayoutSurvivesPlayback()
        {
            var bounds = instance.GetComponentsInChildren<RectTransform>().Single(x => x.name == "DisplayBounds");
            bounds.anchorMin = new Vector2(.23f, .12f);
            bounds.anchorMax = new Vector2(.77f, .88f);
            view.TryPlay("Basket"); view.Advance(.4f); view.StopPlayback();
            Assert.That(bounds.anchorMin, Is.EqualTo(new Vector2(.23f, .12f)));
            Assert.That(bounds.anchorMax, Is.EqualTo(new Vector2(.77f, .88f)));
            Assert.That(instance.GetComponent<Canvas>().sortingOrder, Is.EqualTo(2000));
            Assert.That(instance.GetComponentInChildren<AspectRatioFitter>().aspectRatio, Is.EqualTo(16f / 9f));
        }

        [Test]
        public void NonLoopPreset_HoldsLastFrame()
        {
            view.Presets.First(x => x.id == "Collar").loop = false;
            view.TryPlay("Collar"); view.Advance(20);
            Assert.That(view.CurrentFrame, Is.EqualTo(5));
            Assert.That(view.IsPlaying, Is.True);
        }

        [Test]
        public void Travel_MovesFastSlowFastWithoutVerticalDrift_AndStopRestoresOrigin()
        {
            Canvas.ForceUpdateCanvases();
            var motion = instance.transform.Find("MotionRoot").GetComponent<RectTransform>();
            var artwork = motion.GetComponentsInChildren<RectTransform>().Single(x => x.name == "Layers");
            var origin = motion.anchoredPosition;
            Assert.That(artwork.localScale.x, Is.LessThan(0), "All layers share the mirrored parent.");
            Assert.That(view.TryPlay("Collar"), Is.True);
            float start = motion.anchoredPosition.x;
            view.Advance(.2f); float early = motion.anchoredPosition.x - start;
            view.Advance(.7f); float beforeCenter = motion.anchoredPosition.x;
            view.Advance(.2f); float center = motion.anchoredPosition.x - beforeCenter;
            view.Advance(.7f); float beforeEnd = motion.anchoredPosition.x;
            Assert.That(view.HasCompletedTravel, Is.False);
            view.Advance(.2f); float late = motion.anchoredPosition.x - beforeEnd;
            Assert.That(early, Is.GreaterThan(center * 2f));
            Assert.That(late, Is.GreaterThan(center * 2f));
            Assert.That(center, Is.GreaterThan(0), "The center must not stop.");
            Assert.That(motion.anchoredPosition.y, Is.EqualTo(origin.y));
            Assert.That(view.HasCompletedTravel, Is.True);
            Assert.That(view.CurrentFrame, Is.Zero, "Frame timing is independent from travel.");
            view.StopPlayback();
            Assert.That(motion.anchoredPosition, Is.EqualTo(origin));
            view.TryPlay("Head");
            Assert.That(motion.anchoredPosition.x, Is.EqualTo(start));
            view.StopPlayback();
        }

        [UnityTest]
        public global::System.Collections.IEnumerator Wipe_BothBoundariesMoveRight_EventsWaitForCompletion_AndInterruptionRestoresLayout()
        {
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            yield return new EnterPlayMode();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/MainScene.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>();
            yield return null;
            typeof(NekolposGameBootstrap).GetMethod("StartDebugGame", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic).Invoke(bootstrap, null);
            yield return null;
            var fade = (FadeController)typeof(NekolposGameBootstrap).GetMethod("ResolveLocationTransitionFadeController", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic).Invoke(bootstrap, null);
            Assert.That(fade, Is.Not.Null);
            fade.SetTransparent();
            var rect = (RectTransform)fade.OverlayRoot;
            Assert.That(rect, Is.Not.Null);
            Vector2 min = rect.anchorMin, max = rect.anchorMax, offsetMin = rect.offsetMin, offsetMax = rect.offsetMax;
            wipeOpaqueCount = wipeTransparentCount = 0;
            global::System.Action onOpaque = CountOpaque;
            global::System.Action onTransparent = CountTransparent;
            fade.BecameOpaque += onOpaque;
            fade.BecameTransparent += onTransparent;
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                fade.WipeToBlack(.4f);
                Assert.That(fade.IsOpaque, Is.False);
                Assert.That(rect.anchorMin.x, Is.Zero);
                Assert.That(fade.WipeBoundary, Is.Zero);
                yield return null;
                float progressDeadline = Time.realtimeSinceStartup + 3f;
                while (fade.WipeBoundary <= 0f)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(progressDeadline));
                    yield return null;
                }
                Assert.That(fade.WipeBoundary, Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(wipeOpaqueCount, Is.Zero);
                float deadline = Time.realtimeSinceStartup + 3f;
                float previous = fade.WipeBoundary;
                while (!fade.IsOpaque)
                {
                    Assert.That(fade.WipeBoundary, Is.GreaterThanOrEqualTo(previous));
                    previous = fade.WipeBoundary;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(wipeOpaqueCount, Is.EqualTo(1));
                fade.WipeFromBlack(.4f);
                Assert.That(rect.anchorMin.x, Is.Zero);
                yield return null;
                progressDeadline = Time.realtimeSinceStartup + 3f;
                while (fade.WipeBoundary <= 0f)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(progressDeadline));
                    yield return null;
                }
                Assert.That(fade.WipeBoundary, Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(rect.anchorMax.x, Is.EqualTo(1f));
                Assert.That(wipeTransparentCount, Is.Zero);
                previous = fade.WipeBoundary;
                while (!fade.IsTransparent)
                {
                    Assert.That(fade.WipeBoundary, Is.GreaterThanOrEqualTo(previous));
                    previous = fade.WipeBoundary;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(wipeTransparentCount, Is.EqualTo(1));
                Assert.That(rect.anchorMin, Is.EqualTo(min));
                Assert.That(rect.anchorMax, Is.EqualTo(max));
                fade.WipeToBlack(1f);
                yield return null;
                fade.SetTransparent();
                yield return null;
                Assert.That(fade.IsTransparent, Is.True);
                Assert.That(rect.anchorMin, Is.EqualTo(min));
                Assert.That(rect.anchorMax, Is.EqualTo(max));
                Assert.That(rect.offsetMin, Is.EqualTo(offsetMin));
                Assert.That(rect.offsetMax, Is.EqualTo(offsetMax));
                Assert.That(wipeOpaqueCount, Is.EqualTo(1));
                fade.WipeToBlack(1f);
                yield return null;
                fade.FadeFromBlack(.05f);
                while (!fade.IsTransparent) yield return null;
                Assert.That(rect.anchorMin, Is.EqualTo(min));
                Assert.That(rect.anchorMax, Is.EqualTo(max));
            }
            finally
            {
                Time.timeScale = scale;
                fade.BecameOpaque -= onOpaque;
                fade.BecameTransparent -= onTransparent;
                fade.SetTransparent();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public global::System.Collections.IEnumerator CommonEntry_SnapshotsRequestRejectsOverlapAndCancelsCleanly()
        {
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            yield return new EnterPlayMode();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/MainScene.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<NekolposGameBootstrap>();
            var runtimeView = UnityEngine.Object.FindFirstObjectByType<TransportAnimationView>();
            var fade = UnityEngine.Object.FindFirstObjectByType<FadeController>();
            var flags = global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic;
            typeof(NekolposGameBootstrap).GetMethod("StartDebugGame", flags).Invoke(bootstrap, null);
            var bindings = (NekolposGameBootstrap.ConversationTimelineBinding[])typeof(NekolposGameBootstrap)
                .GetField("conversationTimelineBindings", flags).GetValue(bootstrap);
            var desk = bindings.First(x => x.id == "MoveToDesk");
            var request = new NekolposGameBootstrap.ConversationTimelineBinding
            {
                id = "EventTransportTest",
                changeHomeLocation = true,
                destination = desk.destination,
                playerDestination = desk.playerDestination,
                expectedPlayerAreaId = desk.expectedPlayerAreaId,
                transportAnimationPreset = "Collar",
                transportAnimationSeconds = 10f
            };
            try
            {
                Assert.That(bootstrap.TryTransport(request), Is.True);
                Assert.That(bootstrap.IsTransporting, Is.True);
                request.destination = CatHomeLocation.Table;
                request.playerDestination = null;
                request.transportAnimationPreset = "Back";
                Assert.That(bootstrap.TryTransport("MoveToTable"), Is.False);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!runtimeView.IsPlaying)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(fade.IsOpaque, Is.False, "Transport should follow the covering wipe before it finishes.");
                while (!fade.IsOpaque)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(UnityEngine.Object.FindFirstObjectByType<PlayerAreaTracker>().CurrentAreaId, Is.EqualTo("Desk"));
                Assert.That(runtimeView.GetComponentsInChildren<Image>().Single(x => x.name == "プレイヤー服襟").enabled, Is.True);
                bootstrap.CancelTransport();
                Assert.That(bootstrap.IsTransporting, Is.False);
                Assert.That(runtimeView.IsPlaying, Is.False);
                Assert.That(runtimeView.GetComponent<AudioSource>().isPlaying, Is.False);
                Assert.That(fade.IsTransparent, Is.True);
                Assert.That(bootstrap.TryTransport("MoveToTable"), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (!runtimeView.IsPlaying)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(runtimeView.CurrentFrame, Is.Zero);
                bootstrap.CancelTransport();
                // A short caller hold must not hide the artwork before the right-hand exit.
                request.destination = desk.destination;
                request.playerDestination = desk.playerDestination;
                request.transportAnimationPreset = "Collar";
                request.transportAnimationSeconds = .05f;
                Assert.That(bootstrap.TryTransport(request), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (!runtimeView.IsPlaying)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                bool sawOpeningDuringTravel = false;
                while (!runtimeView.HasCompletedTravel)
                {
                    Assert.That(bootstrap.IsTransporting, Is.True);
                    Assert.That(runtimeView.IsPlaying, Is.True);
                    if (!fade.IsOpaque && runtimeView.ElapsedSeconds > .6f) sawOpeningDuringTravel = true;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(sawOpeningDuringTravel, Is.True, "The opening wipe must follow the image before travel ends.");
                while (bootstrap.IsTransporting)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(fade.IsTransparent, Is.True);
                Assert.That(runtimeView.IsPlaying, Is.False);
            }
            finally { bootstrap.CancelTransport(); }
            yield return new ExitPlayMode();
        }
    }
}
#endif
