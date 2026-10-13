using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.System.Editor
{
    public sealed class TabletScreenWakeControllerTests
    {
        [Test]
        public void WakeAndSleep_ChangeOnlyTheDisplayState()
        {
            GameObject bootstrapObject = new GameObject("Tablet wake bootstrap fixture");
            GameObject canvas = new GameObject("Tablet wake canvas fixture");
            GameObject emission = new GameObject("Tablet wake emission fixture");
            canvas.SetActive(false);
            emission.SetActive(false);

            try
            {
                NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "tabletDisplayCanvas", canvas);
                SetField(bootstrap, "tabletEmissionPanel", emission);

                bootstrap.WakeTabletDisplay();
                Assert.That(bootstrap.IsTabletDisplayPowered, Is.True);
                Assert.That(emission.activeSelf, Is.True);

                bootstrap.SleepTabletDisplay();
                Assert.That(bootstrap.IsTabletDisplayPowered, Is.False);
                Assert.That(emission.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(emission);
            }
        }

        [Test]
        public void WorldWake_UsesExplicitSourcesAndDoesNotTreatTheCatBodyAsAPaw()
        {
            string source = File.ReadAllText("Assets/Scripts/System/TabletScreenWakeController.cs");
            string bootstrap = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");

            StringAssert.Contains("GetComponentInParent<TabletWakeSource>()", source);
            StringAssert.Contains("OnTriggerEnter", source);
            StringAssert.DoesNotContain("OnTriggerStay", source);
            StringAssert.Contains("foot_f.L", bootstrap);
            StringAssert.Contains("foot_f.R", bootstrap);
            StringAssert.Contains("foot_b.L", bootstrap);
            StringAssert.Contains("foot_b.R", bootstrap);
            StringAssert.DoesNotContain("normalCat.AddComponent<TabletWakeSource>()", bootstrap);
        }

        [Test]
        public void PawWakeSources_AddMissingTriggerColliderUsingUnityNullSemantics()
        {
            GameObject cat = new GameObject("Normal cat fixture");
            GameObject paw = new GameObject("foot_f.L");
            paw.transform.SetParent(cat.transform, false);

            try
            {
                MethodInfo configure = typeof(NekolposGameBootstrap).GetMethod(
                    "ConfigurePawWakeSources",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(configure, Is.Not.Null);

                configure.Invoke(null, new object[] { cat });

                SphereCollider collider = paw.GetComponent<SphereCollider>();
                Assert.That(paw.GetComponent<TabletWakeSource>(), Is.Not.Null);
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.isTrigger, Is.True);

                string bootstrap = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");
                StringAssert.Contains("if (collider == null)", bootstrap);
                StringAssert.Contains("if (source == null)", bootstrap);
                StringAssert.DoesNotContain("GetComponent<SphereCollider>() ??", bootstrap);
                StringAssert.DoesNotContain("GetComponent<TabletWakeSource>() ??", bootstrap);
            }
            finally
            {
                Object.DestroyImmediate(cat);
            }
        }

        [Test]
        public void TriggerEnter_WakesOnlyAnExplicitSource()
        {
            GameObject bootstrapObject = new GameObject("Tablet trigger bootstrap fixture");
            GameObject canvas = new GameObject("Tablet trigger canvas fixture");
            GameObject screen = new GameObject("Tablet screen trigger fixture", typeof(BoxCollider), typeof(Rigidbody));
            GameObject source = new GameObject("Tablet wake source fixture", typeof(BoxCollider));
            GameObject unrelated = new GameObject("Unrelated collider fixture", typeof(BoxCollider));
            canvas.SetActive(false);

            try
            {
                NekolposGameBootstrap bootstrap = bootstrapObject.AddComponent<NekolposGameBootstrap>();
                SetField(bootstrap, "tabletDisplayCanvas", canvas);
                TabletScreenWakeController wakeController = screen.AddComponent<TabletScreenWakeController>();
                wakeController.Configure(bootstrap, screen.GetComponent<BoxCollider>(), 8f);

                InvokeTrigger(wakeController, unrelated.GetComponent<BoxCollider>());
                Assert.That(bootstrap.IsTabletDisplayPowered, Is.False);

                source.AddComponent<TabletWakeSource>().Configure(TabletWakeSource.SourceKind.Player);
                InvokeTrigger(wakeController, source.GetComponent<BoxCollider>());
                Assert.That(bootstrap.IsTabletDisplayPowered, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(screen);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void WorldInteraction_PreservesTheExistingTabletWindowAndRestoresWalkMode()
        {
            string bootstrap = File.ReadAllText("Assets/Scripts/System/NekolposGameBootstrap.cs");
            string walkMode = File.ReadAllText("Assets/Scripts/Player/PlayerWalkModeController.cs");

            StringAssert.Contains("EnterTabletCameraViewFromWalk", bootstrap);
            StringAssert.Contains("EnterTabletCameraView()", bootstrap);
            StringAssert.Contains("RestoreTabletInputDestination()", bootstrap);
            StringAssert.Contains("CompleteTabletInteraction()", bootstrap);
            StringAssert.Contains("TryBeginTabletInteraction", walkMode);
            StringAssert.Contains("isTabletInteractionActive", walkMode);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} was not found.");
            field.SetValue(target, value);
        }

        private static void InvokeTrigger(TabletScreenWakeController controller, Collider collider)
        {
            MethodInfo method = typeof(TabletScreenWakeController).GetMethod(
                "OnTriggerEnter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, new object[] { collider });
        }
    }
}
