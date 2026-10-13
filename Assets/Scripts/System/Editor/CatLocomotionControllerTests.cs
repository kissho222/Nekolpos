using System.IO;
using Nekolpos.System;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.TestTools;

public sealed class CatLocomotionControllerTests
{
    [UnityTest]
    public IEnumerator Follow_SavedChabudaiLegIsAvoidedDuringMovement()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(useSavedFloor: true))
        {
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            Vector3 legCenter = new Vector3(-.346f - .28284276f, .05f, -1.52f - .28284276f);
            Assert.That(NavMesh.SamplePosition(legCenter - Vector3.right * .24f, out var start, .1f, filter), Is.True);
            Assert.That(NavMesh.SamplePosition(legCenter + Vector3.right * .24f, out var end, .1f, filter), Is.True);
            Assert.That(agent.Warp(start.position), Is.True);
            fixture.Player.transform.position = end.position;
            SetRuntimeField(fixture.Controller, "followStartDistance", .1f);
            SetRuntimeField(fixture.Controller, "followStopDistance", .03f);
            SetRuntimeField(fixture.Controller, "stoppingDistance", .01f);
            SetRuntimeField(fixture.Controller, "moveSpeed", .2f);
            // 小さい試験区間ではAwake時の通常停止距離も試験設定へ揃える。
            agent.stoppingDistance = .01f;
            agent.speed = .2f;
            fixture.Controller.FollowPlayer();
            Bounds forbidden = new Bounds(legCenter, new Vector3(.04f, 2, .04f));
            float deadline = Time.time + 8f;
            int observed = 0;
            while (Time.time < deadline && Vector3.Distance(fixture.Cat.transform.position, end.position) > .05f)
            {
                Assert.That(forbidden.Contains(fixture.Cat.transform.position), Is.False, "Follow crossed table leg");
                observed++;
                yield return null;
            }
            Assert.That(observed, Is.GreaterThan(5));
            Assert.That(Vector3.Distance(fixture.Cat.transform.position, end.position), Is.LessThan(.06f), "Follow must reach opposite side via detour");
        }
        yield return new ExitPlayMode();
    }

    [Test]
    public void MainScene_ChabudaiLegsForceCompleteCatDetours()
    {
        using var scope = new MainSceneScope();
        var floor = System.Array.Find(FindSceneComponents<NavMeshSurface>(scope.Scene), s => s.name == "CatNavigation");
        var filter = new NavMeshQueryFilter { agentTypeID = floor.agentTypeID, areaMask = NavMesh.AllAreas };
        var legs = System.Array.FindAll(FindSceneComponents<BoxCollider>(scope.Scene), b => b.name.StartsWith("ChabudaiLeg_"));
        Assert.That(legs, Has.Length.EqualTo(4));
        foreach (var leg in legs)
        {
            Vector3 center = leg.bounds.center;
            Assert.That(leg.enabled && !leg.isTrigger, Is.True);
            Assert.That(NavMesh.SamplePosition(new Vector3(center.x, .05f, center.z), out _, .03f, filter), Is.False, leg.name);
            Assert.That(NavMesh.SamplePosition(new Vector3(center.x - .24f, .05f, center.z), out var start, .15f, filter), Is.True);
            Assert.That(NavMesh.SamplePosition(new Vector3(center.x + .24f, .05f, center.z), out var end, .15f, filter), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(start.position, end.position, filter, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(path.corners.Length, Is.GreaterThan(2));
            Bounds obstacle = leg.bounds;
            obstacle.Expand(new Vector3(.02f, 2, .02f));
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 delta = path.corners[i] - path.corners[i - 1];
                Assert.That(obstacle.IntersectRay(new Ray(path.corners[i - 1], delta.normalized), out float distance) &&
                    distance <= delta.magnitude, Is.False, leg.name + " path crosses leg");
            }
        }
    }

    [TestCase(0.1f, -0.2f, 0.6f)]
    [TestCase(-0.6f, -0.1f, 0.3f)]
    [TestCase(0.01f, -0.4f, 0.01f)]
    public void WalkHeadAim_KeepsPlayerDistanceAndHeadAngleLimits(float x, float y, float z)
    {
        var host = new GameObject("Walk head aim test");
        host.SetActive(false);
        try
        {
            var look = host.AddComponent<NekomataLookRigController>();
            SetRuntimeField(look, "headBone", host.transform);
            SetRuntimeField(look, "referenceForward", host.transform);
            SetRuntimeField(look, "maxYawDegrees", 45f);
            SetRuntimeField(look, "maxPitchDegrees", 45f);
            SetRuntimeField(look, "maxDownPitchDegrees", 80f);
            Vector3 target = new Vector3(x, y, z);
            var method = typeof(NekomataLookRigController).GetMethod("ClampPositionToLookCone", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 clamped = (Vector3)method.Invoke(look, new object[] { target, true });
            Assert.That(clamped.magnitude, Is.EqualTo(target.magnitude).Within(0.0001f));
            float yaw = Mathf.Atan2(clamped.x, clamped.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(clamped.y, new Vector2(clamped.x, clamped.z).magnitude) * Mathf.Rad2Deg;
            Assert.That(Mathf.Abs(yaw), Is.LessThanOrEqualTo(45.001f));
            Assert.That(pitch, Is.InRange(-80.001f, 45.001f));
        }
        finally { Object.DestroyImmediate(host); }
    }

    [UnityTest]
    public IEnumerator Follow_WalksFromJustBeyondStartDistanceAndStopsNearPlayer()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Player.transform.position = new Vector3(0.60f, 0f, 0f);
            Assert.That(fixture.Controller.IsFollowing, Is.True, "Walk entry must start Follow without a Choice.");
            Vector3 start = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(1.5f);
            TestContext.WriteLine($"cat displacement={Vector3.Distance(start, fixture.Cat.transform.position)}; distance={Vector3.Distance(fixture.Player.transform.position, fixture.Cat.transform.position)}");
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.GreaterThan(0.1f));
            yield return WaitForLiveSeconds(2f);
            float distance = Vector3.Distance(fixture.Player.transform.position, fixture.Cat.transform.position);
            Assert.That(distance, Is.InRange(0.39f, 0.5f));
            fixture.Controller.WaitHere();
            Vector3 waitingPosition = fixture.Cat.transform.position;
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            yield return WaitForLiveSeconds(0.3f);
            Assert.That(Vector3.Distance(waitingPosition, fixture.Cat.transform.position), Is.LessThan(0.01f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Unreachable_BriefFailureKeepsPostureThenRestsOnce()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(7f))
        {
            fixture.Player.transform.position = new Vector3(-2f, 0f, 0f);
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.IsMoving, Is.True);
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            double deadline = EditorApplication.timeSinceStartup + 5d;
            while (!GetRuntimeField<bool>(fixture.Controller, "pathUnreachable"))
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Walk_F_IP"),
                "A brief failure must not immediately start lying down.");
            Vector3 stopped = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(1f);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_loop_1"));
            Assert.That(fixture.Controller.IsMoving, Is.False);
            Assert.That(Vector3.Distance(stopped, fixture.Cat.transform.position), Is.LessThan(0.01f));
            yield return WaitForLiveSeconds(1f);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_loop_1"));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Unreachable_SleepsAfterSixtySecondsAndWakeWaitsForEnd()
    {
        yield return new EnterPlayMode();
        float oldScale = Time.timeScale;
        try
        {
            Time.timeScale = 10f;
            using var fixture = new LiveFollowFixture(7f);
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            var animator = fixture.Cat.GetComponent<Animator>();
            Vector3 position = fixture.Cat.transform.position;
            double deadline = EditorApplication.timeSinceStartup + 25d;
            while (fixture.Controller.UnreachableWaitSeconds < 59f)
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_loop_1").Or.EqualTo("CatSimple_Lie_belly_loop_3"));
                Assert.That(fixture.Controller.IsSleeping, Is.False);
                Assert.That(fixture.Controller.IsSleepTransitioning, Is.False);
                yield return null;
            }
            while (!fixture.Controller.IsSleepTransitioning) yield return null;
            Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.GreaterThanOrEqualTo(60f));
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_sleep_start"));
            Time.timeScale = 1f;
            while (!fixture.Controller.IsSleeping)
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            fixture.Player.transform.position = new Vector3(0.6f, 0f, 0f);
            yield return WaitForLiveSeconds(0.5f);
            Assert.That(fixture.Controller.IsSleeping, Is.True, "Reachable Player must not automatically wake the cat.");
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_sleep"));
            Assert.That(GetRuntimeField<bool>(fixture.Controller, "sleepLookSuppressed"), Is.True);
            int windows = 0;
            Assert.That(fixture.Controller.TryWakeForInteraction(() => windows++), Is.True);
            Assert.That(fixture.Controller.TryWakeForInteraction(() => windows++), Is.False);
            Assert.That(windows, Is.Zero);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_sleep_End"));
            while (fixture.Controller.IsSleepTransitioning)
            {
                Assert.That(windows, Is.Zero, "UI must remain closed throughout the wake animation.");
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.That(windows, Is.EqualTo(1));
            Assert.That(GetRuntimeField<bool>(fixture.Controller, "sleepLookSuppressed"), Is.False);
            Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.Zero);
            Assert.That(Vector3.Distance(position, fixture.Cat.transform.position), Is.LessThan(0.01f));
        }
        finally { Time.timeScale = oldScale; }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Unreachable_TimelineResetsWaitAndCancelsPendingWakeUI()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(7f))
        {
            var owner = new GameObject("Sleep ownership test");
            owner.SetActive(false);
            try
            {
                var presentation = owner.AddComponent<CatPresentationModeController>();
                var mode = typeof(CatPresentationModeController).GetProperty("CurrentMode");
                mode.SetValue(presentation, CatPresentationMode.Conversation);
                SetRuntimeField(fixture.Controller, "presentationController", presentation);
                fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
                yield return WaitForLiveSeconds(2f);
                Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.GreaterThan(1f));
                var motion = owner.AddComponent<CatMotionController>();
                motion.Configure(fixture.Cat.GetComponent<Animator>(), null);
                SetRuntimeField(presentation, "motionController", motion);
                motion.PlayMotion("CatSimple_Lie_belly_end");
                yield return WaitForLiveSeconds(0.1f);
                Assert.That(motion.IsPlayingMotion, Is.True);
                Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.Zero);
                Assert.That(fixture.Controller.IsMoving, Is.False);
                motion.PlayIdle();
                yield return WaitForLiveSeconds(1f);
                mode.SetValue(presentation, CatPresentationMode.Timeline);
                yield return WaitForLiveSeconds(0.1f);
                Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.Zero);
                mode.SetValue(presentation, CatPresentationMode.Conversation);
                yield return WaitForLiveSeconds(1f);
                SetRuntimeField(fixture.Controller, "unreachableWaitSeconds", 59.9f);
                yield return WaitForLiveSeconds(1f);
                Assert.That(fixture.Controller.IsSleeping, Is.True);
                int windows = 0;
                Assert.That(fixture.Controller.TryWakeForInteraction(() => windows++), Is.True);
                mode.SetValue(presentation, CatPresentationMode.Event);
                yield return WaitForLiveSeconds(1f);
                Assert.That(windows, Is.Zero);
                Assert.That(fixture.Controller.IsSleepTransitioning, Is.False);
                Assert.That(fixture.Player.GetComponent<PlayerWalkModeController>().IsInteractionMenuOpen, Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Unreachable_PathRecoveryResetsWaitAndResumesFollow()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(7f))
        {
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.GreaterThan(1f));
            fixture.Player.transform.position = new Vector3(-1f, 0f, 0f);
            Vector3 start = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.UnreachableWaitSeconds, Is.Zero);
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.GreaterThan(0.1f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Follow_RespectsNotWalkableWithAndWithoutDetour()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(2f))
        {
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            float end = Time.time + 12f;
            while (Time.time < end)
            {
                Vector3 position = fixture.Cat.transform.position;
                Assert.That(position.x > 0.9f && position.x < 1.1f && Mathf.Abs(position.z) < 1f,
                    Is.False, "Follow must not cross the Not Walkable volume.");
                yield return null;
            }
            Assert.That(fixture.Cat.transform.position.x, Is.GreaterThan(1.1f), "Follow must take the available detour.");
        }
        using (var fixture = new LiveFollowFixture(7f))
        {
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath();
            agent.CalculatePath(fixture.Player.transform.position, path);
            Assert.That(path.status, Is.Not.EqualTo(NavMeshPathStatus.PathComplete));
            Vector3 start = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.IsFollowing, Is.True);
            Assert.That(fixture.Controller.IsMoving, Is.False);
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.LessThan(0.01f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Follow_ResumesAfterNearRestWithoutRestartingExitEveryFrame()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Player.transform.position = new Vector3(0.3f, 0f, 0f);
            fixture.Controller.FollowPlayer();
            yield return WaitForLiveSeconds(1.8f);
            TestContext.WriteLine($"rest={GetRuntimeField<bool>(fixture.Controller, "isRestingNearPlayer")}; enabled={fixture.Controller.IsLocomotionEnabled}; following={fixture.Controller.IsFollowing}; distance={fixture.Controller.DistanceToPlayer}; animation={fixture.Controller.CurrentLocomotionAnimation}; onMesh={fixture.Controller.IsOnNavMesh}");
            Assert.That(GetRuntimeField<bool>(fixture.Controller, "isRestingNearPlayer"), Is.True);
            fixture.Player.transform.position = new Vector3(2f, 0f, 0f);
            Vector3 start = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(2f);
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.GreaterThan(0.1f), "Rest exit must finish before walking resumes.");
        }
        yield return new ExitPlayMode();
    }

    private static void SetRuntimeField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    [UnityTest]
    public IEnumerator Rest_NearPlayerAndWaitBothSleepAfterOneMinuteIncludingYawn()
    {
        yield return new EnterPlayMode();
        float oldScale = Time.timeScale;
        try
        {
            foreach (bool wait in new[] { false, true })
            {
                Time.timeScale = 10f;
                using var fixture = new LiveFollowFixture();
                fixture.Player.transform.position = Vector3.forward * 0.3f;
                if (wait) fixture.Controller.WaitHere();
                float start = Time.time;
                bool sawYawn = false;
                double deadline = EditorApplication.timeSinceStartup + 20d;
                while (!fixture.Controller.IsSleepTransitioning)
                {
                    Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                    sawYawn |= fixture.Controller.IsYawning;
                    Assert.That(fixture.Controller.IsSleeping, Is.False);
                    yield return null;
                }
                Assert.That(sawYawn, Is.True);
                Assert.That(Time.time - start, Is.InRange(60f, 62f), "Yawn must not reset or pause the one-minute wait.");
                Time.timeScale = 1f;
                yield return WaitForLiveSeconds(0.6f);
                Assert.That(fixture.Controller.IsSleeping, Is.True);
                Assert.That(fixture.Controller.IsFollowing, Is.EqualTo(!wait));
                Assert.That(GetRuntimeField<bool>(fixture.Controller, "sleepLookSuppressed"), Is.True);
                int interactions = 0;
                Assert.That(fixture.Controller.TryWakeForInteraction(() => interactions++), Is.True);
                Assert.That(interactions, Is.Zero);
                yield return WaitForLiveSeconds(0.6f);
                Assert.That(interactions, Is.EqualTo(1));
                Assert.That(GetRuntimeField<bool>(fixture.Controller, "sleepLookSuppressed"), Is.False);
            }
        }
        finally { Time.timeScale = oldScale; }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Yawn_DefersOneInteractionUntilCompleteAndCancelsOnExit()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Controller.WaitHere();
            yield return null;
            float next = GetRuntimeField<float>(fixture.Controller, "nextYawnAt") - Time.time;
            Assert.That(next, Is.InRange(39.9f, 50f));
            SetRuntimeField(fixture.Controller, "nextYawnAt", 0.001f);
            yield return WaitForLiveSeconds(0.02f);
            if (!fixture.Controller.IsYawning)
            {
                SetRuntimeField(fixture.Controller, "nextYawnAt", Time.time);
                typeof(CatLocomotionController).GetMethod("TryBeginYawn", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(fixture.Controller, null);
            }
            Assert.That(fixture.Controller.IsYawning, Is.True);
            int count = 0;
            Assert.That(fixture.Controller.TryDeferInteractionUntilYawnEnds(() => count++), Is.True);
            Assert.That(fixture.Controller.TryDeferInteractionUntilYawnEnds(() => count++), Is.False);
            Assert.That(count, Is.Zero);
            yield return WaitForLiveSeconds(0.5f);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(fixture.Controller.IsYawning, Is.False);
            Assert.That(fixture.Player.GetComponent<PlayerWalkModeController>().IsInteractionMenuOpen, Is.False);
            SetRuntimeField(fixture.Controller, "nextYawnAt", Time.time);
            typeof(CatLocomotionController).GetMethod("TryBeginYawn", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.Controller, null);
            Assert.That(fixture.Controller.TryDeferInteractionUntilYawnEnds(() => count++), Is.True);
            fixture.Controller.SetLocomotionEnabled(false);
            yield return WaitForLiveSeconds(0.5f);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.False);
            Assert.That(fixture.Player.GetComponent<PlayerWalkModeController>().IsInteractionMenuOpen, Is.False);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StepBack_RisesMovesBackwardThenHoldsFollowForThreeSeconds()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Player.transform.position = Vector3.forward * 2f;
            Vector3 start = fixture.Cat.transform.position;
            Assert.That(fixture.Controller.TryStepBack(), Is.True);
            Assert.That(fixture.Controller.TryStepBack(), Is.False);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.EqualTo("CatSimple_Lie_belly_end"));
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.LessThan(0.001f));
            yield return WaitForLiveSeconds(1.4f);
            Assert.That(fixture.Cat.transform.position.z, Is.LessThan(start.z - 0.2f));
            Vector3 stopped = fixture.Cat.transform.position;
            Assert.That(fixture.Controller.IsFollowing, Is.True);
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.True);
            Assert.That(Vector3.Distance(stopped, fixture.Cat.transform.position), Is.LessThan(0.01f));
            yield return WaitForLiveSeconds(1.4f);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.False);
            Assert.That(fixture.Cat.transform.position.z, Is.GreaterThan(stopped.z + 0.05f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StepBack_SearchesRearFanWhenStraightBackIsBlocked()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(7f))
        {
            fixture.Controller.WaitHere();
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            Assert.That(agent.Warp(new Vector3(0.8f, 0f, 0f)), Is.True);
            fixture.Cat.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            fixture.Player.transform.position = fixture.Cat.transform.position + fixture.Cat.transform.forward * 0.3f;
            Vector3 start = fixture.Cat.transform.position;
            Quaternion facing = fixture.Cat.transform.rotation;
            Assert.That(agent.Raycast(start + Vector3.right * 0.25f, out _), Is.True);
            Assert.That(fixture.Controller.TryStepBack(), Is.True);
            yield return WaitForLiveSeconds(1.4f);
            Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.GreaterThan(0.2f));
            Assert.That(fixture.Cat.transform.position.x, Is.GreaterThan(start.x));
            Assert.That(fixture.Cat.transform.position.x, Is.LessThan(0.9f));
            Assert.That(Quaternion.Angle(facing, fixture.Cat.transform.rotation), Is.LessThan(0.1f));
            Vector3 stopped = fixture.Cat.transform.position;
            yield return WaitForLiveSeconds(2f);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.True);
            Assert.That(Vector3.Distance(stopped, fixture.Cat.transform.position), Is.LessThan(0.01f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StepBack_HeightDifferenceDoesNotKeepWalkingForever()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Controller.WaitHere();
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            agent.baseOffset = 0.03f;
            yield return null;
            Vector3 start = fixture.Cat.transform.position;
            Assert.That(fixture.Controller.TryStepBack(), Is.True);
            yield return WaitForLiveSeconds(1.8f);
            Assert.That(fixture.Cat.transform.position.z, Is.LessThan(start.z - 0.2f));
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.Not.EqualTo("CatSimple_Walk_B_IP"));
            yield return WaitForLiveSeconds(3.2f);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.False);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StepBack_NoTranslationStopsAndCanBeInterruptedForTalk()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture())
        {
            fixture.Controller.WaitHere();
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            agent.updatePosition = false;
            Assert.That(fixture.Controller.TryStepBack(), Is.True);
            yield return WaitForLiveSeconds(1.5f);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.Not.EqualTo("CatSimple_Walk_B_IP"));
            yield return WaitForLiveSeconds(3.2f);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.False);
            agent.updatePosition = true;
            Assert.That(fixture.Controller.TryStepBack(), Is.True);
            var talk = fixture.Cat.AddComponent<NerukoTalkInteractable>();
            Assert.That(talk.IsInteracting, Is.False, "後退中も会話入口を塞がない");
            fixture.Controller.InterruptStepBackForInteraction();
            Assert.That(fixture.Controller.IsSteppingBack, Is.False);
            Assert.That(fixture.Controller.IsWalkActionBusy, Is.False);
            yield return WaitForLiveSeconds(0.5f);
            Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.Not.EqualTo("CatSimple_Walk_B_IP"));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StepBack_SavedFloorAtEntranceKitchenAndWasherFinishes()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(useSavedFloor: true))
        {
            fixture.Controller.WaitHere();
            var agent = fixture.Cat.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            foreach (Vector3 point in new[] { new Vector3(-0.4f, 0.05f, 2.15f),
                new Vector3(0.35f, 0.05f, 2.12f), new Vector3(0.35f, 0.02f, 2.45f) })
            {
                Assert.That(NavMesh.SamplePosition(point, out var hit, 0.1f, filter), Is.True);
                Assert.That(agent.Warp(hit.position), Is.True);
                fixture.Cat.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Assert.That(fixture.Controller.TryStepBack(), Is.True);
                yield return WaitForLiveSeconds(2.4f);
                Assert.That(fixture.Controller.CurrentLocomotionAnimation, Is.Not.EqualTo("CatSimple_Walk_B_IP"), point.ToString());
                yield return WaitForLiveSeconds(3.2f);
                Assert.That(fixture.Controller.IsWalkActionBusy, Is.False, point.ToString());
                Assert.That(agent.isOnNavMesh, Is.True);
            }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FloorReentryAndLostNavMeshDoNotWarpToAreaAnchor()
    {
        yield return new EnterPlayMode();
        using (var fixture = new LiveFollowFixture(7f))
        {
            var anchor = new GameObject("Floor fallback test anchor");
            try
            {
                anchor.transform.position = new Vector3(-2f, 0f, 0f);
                SetRuntimeField(fixture.Controller, "areaAnchors", new[] { new CatLocomotionController.AreaAnchorBinding("Floor", anchor.transform) });
                SetRuntimeField(fixture.Controller, "lastNavigationAreaId", "Floor");
                fixture.Player.transform.position = Vector3.right * 2f;
                var handler = typeof(CatLocomotionController).GetMethod("HandleAreaChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                Vector3 start = fixture.Cat.transform.position;
                handler.Invoke(fixture.Controller, new object[] { "Floor", "None" });
                handler.Invoke(fixture.Controller, new object[] { "None", "Floor" });
                Assert.That(Vector3.Distance(start, fixture.Cat.transform.position), Is.LessThan(0.001f));
                var agent = fixture.Cat.GetComponent<NavMeshAgent>();
                agent.enabled = false;
                fixture.Cat.transform.position = new Vector3(10f, 0f, 0f);
                var ensure = typeof(CatLocomotionController).GetMethod("TryEnsureAgentOnNavMesh", BindingFlags.Instance | BindingFlags.NonPublic);
                LogAssert.Expect(LogType.Warning, "[CatLocomotionController] ネルコの現在位置近くに猫用NavMeshがないため、その場で追従を停止します。");
                Assert.That((bool)ensure.Invoke(fixture.Controller, null), Is.False);
                Assert.That(fixture.Cat.transform.position.x, Is.EqualTo(10f));
                // 明示的なArea移動は既存Anchorへ復帰できる。
                fixture.Cat.transform.position = start;
                agent.enabled = true;
                Assert.That(fixture.Controller.WarpToArea("Floor"), Is.True);
                Assert.That(Vector3.Distance(anchor.transform.position, fixture.Cat.transform.position), Is.LessThan(0.05f));
            }
            finally { Object.DestroyImmediate(anchor); }
        }
        yield return new ExitPlayMode();
    }

    private static IEnumerator WaitForLiveSeconds(float seconds)
    {
        float end = Time.time + seconds;
        double deadline = EditorApplication.timeSinceStartup + seconds + 15d;
        while (Time.time < end)
        {
            Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline), "Play Mode must advance simulation time.");
            yield return null;
        }
    }

    private static T GetRuntimeField<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static int FindNerukoAgentType()
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            var settings = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == "Neruko") return settings.agentTypeID;
        }
        Assert.Fail("Neruko NavMesh Agent Type is missing.");
        return 0;
    }

    private sealed class LiveFollowFixture : System.IDisposable
    {
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;
        private readonly AnimatorController animationController;
        private readonly AnimationClip animationClip;
        public readonly GameObject Cat;
        public readonly GameObject Player;
        public readonly CatLocomotionController Controller;

        public LiveFollowFixture(float blockerWidth = 0f, bool useSavedFloor = false)
        {
            var savedFloor = useSavedFloor ? AssetDatabase.LoadAssetAtPath<NavMeshData>("Assets/Scenes/MainScene/NavMesh-CatNavigation.asset") : null;
            NavMeshBuildSettings settings = useSavedFloor ? NavMesh.GetSettingsByID(FindNerukoAgentType()) : NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = 0.01f;
            settings.agentHeight = 0.05f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.01f;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(6f, 0.1f, 6f), transform = Matrix4x4.TRS(new Vector3(0f, -0.05f, 0f), Quaternion.identity, Vector3.one), area = 0 }
            };
            if (blockerWidth > 0f)
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                    size = new Vector3(0.2f, 1f, blockerWidth),
                    transform = Matrix4x4.TRS(new Vector3(1f, 0f, 0f), Quaternion.identity, Vector3.one),
                    area = NavMesh.GetAreaFromName("Not Walkable") });
            data = useSavedFloor ? Object.Instantiate(savedFloor) : NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(8f, 2f, 8f)), Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            instance = NavMesh.AddNavMeshData(data);

            Player = new GameObject("Follow test player");
            var walk = Player.AddComponent<PlayerWalkModeController>();
            // Scene/UIは不要。移動入力を止めた上で、Controllerが読む散歩状態だけを設定する。
            walk.enabled = false;
            Player.GetComponent<PlayerSafetyMovementController>().enabled = false;
            SetRuntimeField(walk, "isWalkMode", true);

            Cat = new GameObject("Follow test cat");
            Cat.SetActive(false);
            NavMeshAgent agent = Cat.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            agent.agentTypeID = settings.agentTypeID;
            agent.radius = 0.01f;
            agent.height = 0.05f;
            agent.baseOffset = 0f;
            Animator animator = Cat.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animationController = new AnimatorController();
            animationController.AddLayer("Base Layer");
            animationClip = new AnimationClip();
            animationClip.SetCurve("", typeof(Transform), "localScale.y", AnimationCurve.Constant(0f, 0.2f, 1f));
            foreach (string stateName in new[] { "CatSimple_Walk_F_IP", "CatSimple_Lie_belly_loop_1", "CatSimple_Lie_belly_start", "CatSimple_Lie_belly_end",
                "CatSimple_Lie_belly_sleep_start", "CatSimple_Lie_belly_sleep", "CatSimple_Lie_belly_sleep_End",
                "CatSimple_Lie_belly_loop_3", "CatSimple_Walk_B_IP" })
            {
                animationController.layers[0].stateMachine.AddState(stateName).motion = animationClip;
            }
            animator.runtimeAnimatorController = animationController;
            Controller = Cat.AddComponent<CatLocomotionController>();
            SetRuntimeField(Controller, "walkModeController", walk);
            SetRuntimeField(Controller, "playerTarget", Player.transform);
            Cat.SetActive(true);
            Assert.That(Controller.IsLocomotionEnabled, Is.True);
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Cat);
            Object.DestroyImmediate(Player);
            instance.Remove();
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(animationController);
            Object.DestroyImmediate(animationClip);
        }
    }

    [Test]
    public void ReloadedLookRig_ReplacesInitializedLayerWithMissingRuntimeConstraints()
    {
        var root = new GameObject("Reloaded Rig test");
        root.SetActive(false);
        try
        {
            root.AddComponent<Animator>();
            var builder = root.AddComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            var rig = root.AddComponent<UnityEngine.Animations.Rigging.Rig>();
            var stale = new UnityEngine.Animations.Rigging.RigLayer(rig, false);
            typeof(UnityEngine.Animations.Rigging.RigLayer)
                .GetField("<isInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(stale, true);
            Assert.That(stale.constraints, Is.Null);
            builder.layers.Add(stale);
            var look = root.AddComponent<NekomataLookRigController>();
            SetRuntimeField(look, "rigBuilder", builder);
            SetRuntimeField(look, "rig", rig);
            typeof(NekomataLookRigController).GetMethod("EnsureRigLayerRegistered", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(look, null);
            Assert.That(builder.layers, Has.Count.EqualTo(1));
            Assert.That(builder.layers[0], Is.Not.SameAs(stale));
            Assert.That(builder.layers[0].isInitialized, Is.False);
            Assert.That(builder.layers[0].active, Is.False);
            Assert.That(builder.layers[0].rig, Is.SameAs(rig));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void MainScene_FollowCanStartWithinTableAndAreaIncludesStandingPlayer()
    {
        using var scope = new MainSceneScope();
        var controller = FindSceneComponent<CatLocomotionController>(scope.Scene);
        Assert.That(controller.FollowStartDistance, Is.InRange(controller.FollowStopDistance + 0.05f, 0.6f));
        var walk = FindSceneComponent<PlayerWalkModeController>(scope.Scene);
        var serialized = new SerializedObject(walk);
        foreach (string destinationField in new[] { "walkStartDestination", "walkStartDeskDestination" })
        {
            var destination = (Transform)serialized.FindProperty(destinationField).objectReferenceValue;
            Assert.That(destination, Is.Not.Null);
            string areaId = destinationField == "walkStartDestination" ? "Table" : "Desk";
            var volume = System.Array.Find(FindSceneComponents<PlayerAreaVolume>(scope.Scene), v => v.AreaId == areaId);
            Assert.That(volume, Is.Not.Null);
            Vector3 center = destination.position + Vector3.up * 0.025f;
            Assert.That(volume.Contains(center), Is.True, areaId + " must include the standing Player center.");
        }
    }

    [Test]
    public void MainScene_WalkChoiceTargetsTheConfiguredCatLocomotion()
    {
        using var sceneScope = new MainSceneScope();
        CatLocomotionController locomotion = FindSceneComponent<CatLocomotionController>(sceneScope.Scene);
        WalkDialogueController dialogue = FindSceneComponent<WalkDialogueController>(sceneScope.Scene);
        Assert.That(locomotion, Is.Not.Null);
        Assert.That(dialogue, Is.Not.Null);
        var serialized = new SerializedObject(dialogue);
        Object actual = serialized.FindProperty("catLocomotionController").objectReferenceValue;
        TestContext.WriteLine($"WalkDialogue.catLocomotionController={actual}; expected={locomotion}");
        Assert.That(actual, Is.SameAs(locomotion), "Follow/Wait Choice must control the same cat as WalkMode.");
    }

    [Test]
    public void WalkExit_ReleasesPresentationOwnershipEvenWhenAlreadyIdle()
    {
        GameObject cat = new GameObject("Idle release cat");
        GameObject presentationObject = new GameObject("Idle release presentation");
        try
        {
            var presentation = presentationObject.AddComponent<CatPresentationModeController>();
            presentation.enabled = false;
            SetRuntimeField(presentation, "<CurrentMode>k__BackingField", CatPresentationMode.Conversation);
            presentation.SetLocomotionActive(true);
            var controller = cat.AddComponent<CatLocomotionController>();
            SetRuntimeField(controller, "presentationController", presentation);
            SetRuntimeField(controller, "isLocomotionEnabled", true);
            SetRuntimeField(controller, "currentLocomotionAnimation", "CatSimple_Lie_belly_loop_1");
            controller.SetLocomotionEnabled(false);
            Assert.That(GetRuntimeField<bool>(presentation, "isLocomotionActive"), Is.False);
            Assert.That(controller.IsLocomotionEnabled, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(presentationObject);
        }
    }

    [TestCase(false, 0.60f, false)]
    [TestCase(false, 0.76f, true)]
    [TestCase(false, 0.75f, true)]
    [TestCase(true, 0.60f, true)]
    [TestCase(true, 0.44f, false)]
    [TestCase(true, 0.45f, false)]
    public void EvaluateShouldMove_UsesSeparateStartAndStopDistances(
        bool wasMoving,
        float distance,
        bool expected)
    {
        bool actual = CatLocomotionController.EvaluateShouldMove(
            wasMoving,
            distance,
            startDistance: 0.75f,
            stopDistance: 0.45f);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(1f, 1f, 0.15f, 0.85f)]
    [TestCase(1f, 2f, 0.15f, 0.7f)]
    [TestCase(0f, 1f, 0.15f, 1f)]
    [TestCase(0.1f, 1f, 0.15f, 0f)]
    [TestCase(1f, 1f, -1f, 1f)]
    public void TransitionBlend_StartsBeforeClipEndAndClampsInvalidDurations(
        float length, float speed, float blend, float expected)
    {
        Assert.That(CatLocomotionController.GetTransitionBlendStart(length, speed, blend),
            Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void TryResolveAreaAnchor_MatchesAreaIdWithoutCaseSensitivity()
    {
        GameObject anchorObject = new GameObject("DeskAnchor");
        try
        {
            var bindings = new[]
            {
                new CatLocomotionController.AreaAnchorBinding("Desk", anchorObject.transform)
            };

            Assert.That(
                CatLocomotionController.TryResolveAreaAnchor(bindings, " desk ", out Transform resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(anchorObject.transform));
            Assert.That(
                CatLocomotionController.TryResolveAreaAnchor(bindings, "Floor", out resolved),
                Is.False);
            Assert.That(resolved, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(anchorObject);
        }
    }

    [Test]
    public void RuntimeSource_DoesNotUseHomeSnapForLocomotionAnimation()
    {
        string source = File.ReadAllText("Assets/Scripts/System/CatLocomotionController.cs");

        StringAssert.DoesNotContain("SnapToHome", source);
        StringAssert.DoesNotContain("CatMotionController", source);
        StringAssert.Contains("CatSimple_Walk_F_IP", source);
        StringAssert.Contains("CatSimple_Lie_belly_loop_1", source);
        StringAssert.DoesNotContain("CatSimple_Lie_belly_sleep", source);
        StringAssert.Contains("CatSimple_Lie_belly_start", source);
        StringAssert.Contains("CatSimple_Lie_belly_end", source);
        StringAssert.DoesNotContain("lookRig.enabled", source);
    }

    [Test]
    public void NormalCatAnimator_HasMotionAssignedForNearRestStartAndEnd()
    {
        const string controllerPath =
            "Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/NormalCatSimple_AnimContr 1.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

        Assert.That(controller, Is.Not.Null);
        AnimatorState walk = System.Array.Find(controller.layers[0].stateMachine.states,
            child => child.state.name == "CatSimple_Walk_F_IP").state;
        Assert.That(walk, Is.Not.Null);
        Assert.That(walk.motion, Is.TypeOf<AnimationClip>(), "Follow must play the walk clip, not an empty Blend Tree.");
        Assert.That(walk.motion.name, Is.EqualTo("CatSimple_Walk_F_IP"));
        AssertAnimatorStateHasMotion(controller, "CatSimple_Lie_belly_start");
        AssertAnimatorStateHasMotion(controller, "CatSimple_Lie_belly_loop_1");
        AssertAnimatorStateHasMotion(controller, "CatSimple_Lie_belly_end");
    }

    [Test]
    public void WalkDialogue_ConnectsFollowAndWaitChoicesToLocomotion()
    {
        var host = new GameObject("Walk choice toggle test");
        host.SetActive(false);
        GameObject canvas = null;
        try
        {
            var cat = host.AddComponent<CatLocomotionController>();
            var dialogue = host.AddComponent<WalkDialogueController>();
            SetRuntimeField(dialogue, "catLocomotionController", cat);
            var open = typeof(WalkDialogueController).GetMethod("OpenChoicePanel", BindingFlags.Instance | BindingFlags.NonPublic);
            cat.FollowPlayer();
            for (int index = 0; index < 3; index++)
            {
                open.Invoke(dialogue, null);
                canvas = (GameObject)typeof(WalkDialogueController).GetField("canvasObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialogue);
                var panel = canvas.transform.Find("WalkChoicePanel");
                var buttons = panel.GetComponentsInChildren<UnityEngine.UI.Button>();
                Assert.That(buttons, Has.Length.EqualTo(4), "Follow and Wait must share one button.");
                Assert.That(buttons[0].GetComponentInChildren<TMPro.TextMeshProUGUI>().text,
                    Is.EqualTo(cat.IsFollowing ? "待ってて" : "ついてきて"));
                bool wasFollowing = cat.IsFollowing;
                buttons[0].onClick.Invoke();
                Assert.That(cat.IsFollowing, Is.EqualTo(!wasFollowing));
                Assert.That(panel.gameObject.activeSelf, Is.False);
            }
        }
        finally
        {
            Object.DestroyImmediate(host);
            if (canvas != null) Object.DestroyImmediate(canvas);
        }
    }

    [Test]
    public void MainScene_WalkBlockersAreIncludedInCatBakeAndExcludeFloor()
    {
        using var scope = new MainSceneScope();
        var surfaces = FindSceneComponents<NavMeshSurface>(scope.Scene);
        // この契約の対象はIssue #34の3禁止領域。他の家具用Volumeは個別に検証する。
        var volumes = System.Array.FindAll(FindSceneComponents<NavMeshModifierVolume>(scope.Scene),
            v => v.name == "WalkBlocker_Both" || v.name == "WalkBlocker_CatOnly" || v.name == "ネコ侵入禁止TV");
        Assert.That(volumes, Has.Length.EqualTo(3));
        var floor = System.Array.Find(surfaces, x => x.name == "CatNavigation");
        var collect = typeof(NavMeshSurface).GetMethod("CollectSources", BindingFlags.Instance | BindingFlags.NonPublic);
        var sources = (List<NavMeshBuildSource>)collect.Invoke(floor, null);
        var filter = new NavMeshQueryFilter { agentTypeID = floor.agentTypeID, areaMask = NavMesh.AllAreas };
        foreach (var volume in volumes)
        {
            Assert.That(volume.isActiveAndEnabled, Is.True);
            Assert.That(volume.area, Is.EqualTo(NavMesh.GetAreaFromName("Not Walkable")));
            Assert.That(volume.AffectsAgentType(floor.agentTypeID), Is.True);
            Assert.That(floor.layerMask.value & (1 << volume.gameObject.layer), Is.Not.Zero);
            Vector3 center = volume.transform.TransformPoint(volume.center);
            Assert.That(sources.Exists(x => x.shape == NavMeshBuildSourceShape.ModifierBox &&
                x.area == volume.area && Vector3.Distance(x.transform.GetColumn(3), center) < 0.001f), Is.True);
            Assert.That(NavMesh.SamplePosition(new Vector3(center.x, 0.05f, center.z), out _, 0.05f, filter),
                Is.False, volume.name + " must remove the floor beneath it.");
        }
        var path = new NavMeshPath();
        Assert.That(NavMesh.CalculatePath(new Vector3(-0.9f, 0.05f, 0.1f),
            new Vector3(-0.9f, 0.05f, 1f), filter, path), Is.True);
        Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
        Assert.That(path.corners.Length, Is.GreaterThan(2), "Both blocker must force a detour.");
        var catOnly = System.Array.Find(volumes, x => x.name == "WalkBlocker_CatOnly");
        Assert.That(catOnly.GetComponent<BoxCollider>().enabled, Is.False, "Player must be able to pass CatOnly.");
        var both = System.Array.Find(volumes, x => x.name == "WalkBlocker_Both");
        Assert.That(both.GetComponent<BoxCollider>().enabled, Is.True);
        Assert.That(both.GetComponent<BoxCollider>().isTrigger, Is.False);
    }

    [Test]
    public void MainScene_HasCompleteCatNavigationContract()
    {
        using var sceneScope = new MainSceneScope();
        CatLocomotionController locomotion = FindSceneComponent<CatLocomotionController>(sceneScope.Scene);
        NavMeshSurface surface = FindSceneComponent<NavMeshSurface>(sceneScope.Scene);
        NavMeshSurface[] surfaces = FindSceneComponents<NavMeshSurface>(sceneScope.Scene);

        Assert.That(locomotion, Is.Not.Null);
        Assert.That(surface, Is.Not.Null);
        Assert.That(surfaces, Has.Length.EqualTo(3));
        foreach (NavMeshSurface configuredSurface in surfaces)
        {
            Assert.That(configuredSurface.navMeshData, Is.Not.Null);
            Assert.That(configuredSurface.layerMask.value, Is.EqualTo(1 << LayerMask.NameToLayer("Walkable")));
            Assert.That(configuredSurface.minRegionArea, Is.EqualTo(0.01f).Within(0.0001f));
        }

        NavMeshAgent agent = locomotion.GetComponent<NavMeshAgent>();
        Assert.That(agent, Is.Not.Null);
        Assert.That(agent.enabled, Is.False, "The cat agent must be dormant outside WalkMode.");
        foreach (NavMeshSurface configuredSurface in surfaces)
        {
            Assert.That(configuredSurface.agentTypeID, Is.EqualTo(agent.agentTypeID));
        }

        var serialized = new SerializedObject(locomotion);
        AssertAssigned(serialized, "navMeshAgent");
        AssertAssigned(serialized, "animator");
        AssertAssigned(serialized, "walkModeController");
        AssertAssigned(serialized, "playerAreaTracker");
        AssertAssigned(serialized, "playerTarget");
        AssertAssigned(serialized, "presentationController");
        AssertAssigned(serialized, "lookRig");
        Assert.That(
            serialized.FindProperty("nearRestStateName").stringValue,
            Is.EqualTo("CatSimple_Lie_belly_loop_1"));
        Assert.That(
            serialized.FindProperty("nearRestStartStateName").stringValue,
            Is.EqualTo("CatSimple_Lie_belly_start"));
        Assert.That(
            serialized.FindProperty("nearRestEndStateName").stringValue,
            Is.EqualTo("CatSimple_Lie_belly_end"));
        Assert.That(serialized.FindProperty("angularSpeed").floatValue, Is.LessThanOrEqualTo(120f));

        SerializedProperty bindings = serialized.FindProperty("areaAnchors");
        Assert.That(bindings.arraySize, Is.EqualTo(3));
        Assert.That(bindings.GetArrayElementAtIndex(0).FindPropertyRelative("areaId").stringValue, Is.EqualTo("Table"));
        Assert.That(bindings.GetArrayElementAtIndex(1).FindPropertyRelative("areaId").stringValue, Is.EqualTo("Desk"));
        Assert.That(bindings.GetArrayElementAtIndex(2).FindPropertyRelative("areaId").stringValue, Is.EqualTo("Floor"));
        for (int index = 0; index < bindings.arraySize; index++)
        {
            Assert.That(
                bindings.GetArrayElementAtIndex(index).FindPropertyRelative("spawnAnchor").objectReferenceValue,
                Is.Not.Null);
        }
    }

    [Test]
    public void MainScene_EachCatAreaAnchorSamplesToCatNavMesh()
    {
        using var sceneScope = new MainSceneScope();
        CatLocomotionController locomotion = FindSceneComponent<CatLocomotionController>(sceneScope.Scene);
        Assert.That(locomotion, Is.Not.Null);
        NavMeshAgent agent = locomotion.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = NavMesh.AllAreas
        };

        var serialized = new SerializedObject(locomotion);
        SerializedProperty bindings = serialized.FindProperty("areaAnchors");
        for (int index = 0; index < bindings.arraySize; index++)
        {
            SerializedProperty item = bindings.GetArrayElementAtIndex(index);
            string areaId = item.FindPropertyRelative("areaId").stringValue;
            var anchor = (Transform)item.FindPropertyRelative("spawnAnchor").objectReferenceValue;
            Assert.That(
                NavMesh.SamplePosition(anchor.position, out _, 0.35f, filter),
                Is.True,
                $"{areaId} anchor must be near the Neruko NavMesh.");
        }
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component != null && component.gameObject.scene == scene)
            {
                return component;
            }
        }

        return null;
    }

    private static void AssertAnimatorStateHasMotion(AnimatorController controller, string stateName)
    {
        foreach (ChildAnimatorState childState in controller.layers[0].stateMachine.states)
        {
            if (childState.state != null && childState.state.name == stateName)
            {
                Assert.That(childState.state.motion, Is.Not.Null, $"{stateName} must reference an existing clip.");
                return;
            }
        }

        Assert.Fail($"Animator state was not found: {stateName}");
    }

    private static T[] FindSceneComponents<T>(Scene scene) where T : Component
    {
        var matches = new System.Collections.Generic.List<T>();
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component != null && component.gameObject.scene == scene)
            {
                matches.Add(component);
            }
        }

        return matches.ToArray();
    }

    private static void AssertAssigned(SerializedObject serialized, string propertyName)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        Assert.That(property, Is.Not.Null, $"Missing serialized property: {propertyName}");
        Assert.That(property.objectReferenceValue, Is.Not.Null, $"MainScene reference is missing: {propertyName}");
    }

    private sealed class MainSceneScope : System.IDisposable
    {
        private const string ScenePath = "Assets/Scenes/MainScene.unity";
        private readonly bool closeOnDispose;

        public MainSceneScope()
        {
            Scene = SceneManager.GetSceneByPath(ScenePath);
            if (!Scene.IsValid() || !Scene.isLoaded)
            {
                Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                closeOnDispose = true;
            }
        }

        public Scene Scene { get; private set; }

        public void Dispose()
        {
            if (closeOnDispose && Scene.IsValid() && Scene.isLoaded)
            {
                EditorSceneManager.CloseScene(Scene, removeScene: true);
            }
        }
    }
}
