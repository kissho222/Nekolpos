#if UNITY_EDITOR
using Nekolpos.ActionSystem;
using Nekolpos.TimeSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System.Editor
{
    public sealed class TransitionBackgroundTests
    {
        private GameObject root, clockRoot;
        private TimeManager clock;
        private TransitionBackgroundView view;
        private bool hadDay;
        private int savedDay;

        [SetUp]
        public void SetUp()
        {
            hadDay = PlayerPrefs.HasKey(TimeManager.CurrentDayPrefsKey);
            savedDay = PlayerPrefs.GetInt(TimeManager.CurrentDayPrefsKey);
            clockRoot = new GameObject("Transition test clock");
            clock = clockRoot.AddComponent<TimeManager>();
            var clockData = new SerializedObject(clock);
            clockData.FindProperty("synchronizeGameState").boolValue = false;
            clockData.ApplyModifiedPropertiesWithoutUndo();
            clock.SetCurrentTime(1, DayPeriod.Morning);
            root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/TransitionBackground.prefab"));
            view = root.GetComponentInChildren<TransitionBackgroundView>(true);
            var data = new SerializedObject(view);
            data.FindProperty("clock").objectReferenceValue = clock;
            data.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            view.BeginLocation();
            view.EndPresentation();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(clockRoot);
            if (hadDay) PlayerPrefs.SetInt(TimeManager.CurrentDayPrefsKey, savedDay);
            else PlayerPrefs.DeleteKey(TimeManager.CurrentDayPrefsKey);
        }

        [Test]
        public void SunSprite_HasBrightBodyTransparentHalo_AndStaysBehindForeground()
        {
            var sun = root.transform.Find("Composition/Sun").GetComponent<Image>();
            Assert.That(AssetDatabase.GetAssetPath(sun.sprite), Is.EqualTo("Assets/Picture/Transition_sun.png"));
            var texture = new Texture2D(2, 2);
            try
            {
                texture.LoadImage(global::System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(sun.sprite)));
                Color center = texture.GetPixel(128, 128);
                Color halo = texture.GetPixel(234, 128);
                Color corner = texture.GetPixel(0, 0);
                Assert.That(center.a, Is.GreaterThan(.99f));
                Assert.That(halo.a, Is.InRange(.05f, .3f));
                Assert.That(corner.a, Is.Zero);
                foreach (Color pixel in texture.GetPixels())
                    if (pixel.a > 0f) Assert.That(Mathf.Min(pixel.r, pixel.g, pixel.b), Is.GreaterThan(.8f));
                Assert.That(center.b, Is.GreaterThan(halo.b));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            Assert.That(sun.transform.GetSiblingIndex(), Is.LessThan(root.transform.Find("Composition/ForegroundFrame").GetSiblingIndex()));
        }

        [TestCase(DayPeriod.Morning, "#95CDE8", true)]
        [TestCase(DayPeriod.Afternoon, "#69B9ED", true)]
        [TestCase(DayPeriod.Evening, "#A77EC8", true)]
        [TestCase(DayPeriod.Night, "#111B44", false)]
        public void OutsidePresentation_TracksClockImmediately(DayPeriod period, string hex, bool sunVisible)
        {
            clock.SetCurrentTime(1, period);
            ColorUtility.TryParseHtmlString(hex, out Color expected);
            Assert.That(view.SkyTop, Is.EqualTo(expected));
            Assert.That(view.SunColor.a > 0, Is.EqualTo(sunVisible));
            Assert.That(view.IsAnimating, Is.False);
        }

        [Test]
        public void Location_FreezesSkyAndSunUntilPresentationEnds()
        {
            view.BeginLocation();
            Color color = view.SkyTop;
            Vector2 position = view.SunPosition;
            Color sun = view.SunColor;
            clock.SetCurrentTime(1, DayPeriod.Evening);
            view.AdvancePresentation(10f);
            Assert.That(view.SkyTop, Is.EqualTo(color));
            Assert.That(view.SunPosition, Is.EqualTo(position));
            Assert.That(view.SunColor, Is.EqualTo(sun));
            Assert.That(view.IsAnimating, Is.False);
            view.EndPresentation();
            Assert.That(view.SkyTop, Is.Not.EqualTo(color));
        }

        [TestCase(DayPeriod.Morning, DayPeriod.Afternoon)]
        [TestCase(DayPeriod.Afternoon, DayPeriod.Evening)]
        [TestCase(DayPeriod.Evening, DayPeriod.Night)]
        public void Time_InterpolatesColorsAndArcThenFinishes(DayPeriod start, DayPeriod end)
        {
            clock.SetCurrentTime(1, start);
            view.BeginTime();
            Color initial = view.SkyTop;
            Vector2 from = view.SunPosition;
            clock.SetCurrentTime(1, end);
            Assert.That(view.IsAnimating, Is.True);
            Assert.That(view.SkyTop, Is.EqualTo(initial));
            view.AdvancePresentation(.5f);
            Color middle = view.SkyTop;
            Vector2 arc = view.SunPosition;
            view.AdvancePresentation(.5f);
            Vector2 target = view.SunPosition;
            Assert.That(middle, Is.Not.EqualTo(initial).And.Not.EqualTo(view.SkyTop));
            Assert.That(arc.x, Is.InRange(Mathf.Min(from.x, target.x), Mathf.Max(from.x, target.x)));
            Assert.That(arc.y, Is.GreaterThan((from.y + target.y) / 2f));
            Assert.That(view.IsAnimating, Is.False);
            if (end == DayPeriod.Night) Assert.That(view.SunColor.a, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void NightToMorning_RisesVerticallyFromBelowToMorningPosition(int morningDay)
        {
            Vector2 morning = view.SunPosition;
            clock.SetCurrentTime(1, DayPeriod.Night);
            view.BeginTime();
            clock.SetCurrentTime(morningDay, DayPeriod.Morning);
            Assert.That(view.SunColor.a, Is.Zero);
            Vector2 start = view.SunPosition;
            Assert.That(start.x, Is.EqualTo(morning.x));
            Assert.That(start.y, Is.LessThan(0f));
            view.AdvancePresentation(.25f);
            Vector2 rising = view.SunPosition;
            Assert.That(rising.x, Is.EqualTo(morning.x));
            Assert.That(rising.y, Is.GreaterThan(start.y).And.LessThan(morning.y));
            Assert.That(view.SunColor.a, Is.InRange(.01f, .99f));
            view.AdvancePresentation(.25f);
            Assert.That(view.SunPosition.y, Is.GreaterThan(rising.y).And.LessThan(morning.y));
            view.AdvancePresentation(.5f);
            Assert.That(view.SunPosition, Is.EqualTo(morning));
            Assert.That(view.SunColor.a, Is.EqualTo(1f));
        }

        [Test]
        public void VisibleSunAcrossDay_FadesBeforeRepositioningThenRises()
        {
            clock.SetCurrentTime(1, DayPeriod.Afternoon);
            view.BeginTime();
            Vector2 oldPosition = view.SunPosition;
            clock.SetCurrentTime(2, DayPeriod.Morning);
            view.AdvancePresentation(.25f);
            Assert.That(view.SunPosition, Is.EqualTo(oldPosition));
            view.AdvancePresentation(.25f);
            Assert.That(view.SunColor.a, Is.Zero);
            Vector2 start = view.SunPosition;
            Assert.That(start.y, Is.LessThan(0f));
            view.AdvancePresentation(.25f);
            Assert.That(view.SunPosition.x, Is.EqualTo(start.x));
            Assert.That(view.SunPosition.y, Is.GreaterThan(start.y));
            view.AdvancePresentation(.25f);
            Assert.That(view.SunColor.a, Is.EqualTo(1f));
            Assert.That(view.IsAnimating, Is.False);
        }

        [Test]
        public void InvalidAdvanceDoesNotCorruptPresentation_AndEndCancels()
        {
            view.BeginTime();
            clock.SetCurrentTime(1, DayPeriod.Afternoon);
            Color before = view.SkyTop;
            view.AdvancePresentation(float.NaN);
            view.AdvancePresentation(float.PositiveInfinity);
            view.AdvancePresentation(-1f);
            Assert.That(view.SkyTop, Is.EqualTo(before));
            view.EndPresentation();
            Assert.That(view.IsAnimating, Is.False);
            view.BeginLocation();
            Assert.That(view.IsAnimating, Is.False);
            Assert.That(view.SkyTop, Is.Not.EqualTo(before));
        }

        [Test]
        public void ForegroundIsOriginalSprite_AndSceneUsesAuthoredMaskAndExistingResultPanel()
        {
            var foreground = root.GetComponentsInChildren<Image>().Length;
            Assert.That(foreground, Is.EqualTo(3));
            Image image = root.transform.Find("Composition/ForegroundFrame/Foreground").GetComponent<Image>();
            Assert.That(AssetDatabase.GetAssetPath(image.sprite), Is.EqualTo("Assets/Picture/Transition_base.png"));
            Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(1920, 1080)));
            Assert.That(root.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(root.transform.Find("Composition/ForegroundFrame").GetComponent<AspectRatioFitter>().aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));
            Assert.That(root.transform.Find("Composition/ForegroundFrame").GetComponent<AspectRatioFitter>().aspectRatio, Is.EqualTo(16f / 9f));
        }

        [TestCase("sky")]
        [TestCase("sun")]
        [TestCase("foregroundFrame")]
        [TestCase("periods")]
        public void MissingConfigurationIsReported(string property)
        {
            Assert.That(view.ValidateConfiguration(out _), Is.True);
            var data = new SerializedObject(view);
            if (property == "periods") data.FindProperty(property).arraySize = 0;
            else data.FindProperty(property).objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(view.ValidateConfiguration(out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(1024, 768)]
        [TestCase(2560, 1080)]
        [TestCase(720, 1280)]
        public void AspectRatios_CoverWithoutStretchingForeground_AndKeepSunOnScreen(int width, int height)
        {
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            Canvas.ForceUpdateCanvases();
            view.BeginLocation();
            var frame = (RectTransform)root.transform.Find("Composition/ForegroundFrame");
            Assert.That(frame.rect.width, Is.GreaterThanOrEqualTo(width - .01f));
            Assert.That(frame.rect.height, Is.GreaterThanOrEqualTo(height - .01f));
            Assert.That(frame.rect.width / frame.rect.height, Is.EqualTo(16f / 9f).Within(.0001f));
            Assert.That(view.SunPosition.x, Is.EqualTo(.12f));
            Assert.That(view.SunPosition.y, Is.InRange(.23f, .98f));
            if (height > width) Assert.That(view.SunPosition.y, Is.GreaterThan(.23f));
        }

        [Test]
        public void Revival_HoldsDarkWhileClockChanges_ThenRestoresBothRegionsWithSunrise()
        {
            Vector2 morning = view.SunPosition;
            clock.SetCurrentTime(1, DayPeriod.Afternoon);
            view.BeginRevivalDark();
            Color dark = view.SkyTop;
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.black));
            Assert.That(view.SunPosition.y, Is.LessThan(0f));
            Assert.That(view.SunColor.a, Is.EqualTo(1f), "Sun is hidden by geometry, not by fading in.");
            clock.SetCurrentTime(2, DayPeriod.Morning);
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.black));
            Assert.That(view.SkyTop, Is.EqualTo(dark));
            view.StartRevivalSunrise();
            view.AdvancePresentation(.1f);
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.black));
            Assert.That(view.SkyTop, Is.EqualTo(dark));
            view.AdvancePresentation(1.5f);
            float restored = view.ForegroundTint.r;
            Assert.That(restored, Is.InRange(.01f, .99f));
            ColorUtility.TryParseHtmlString("#95CDE8", out Color morningTop);
            Assert.That(Vector4.Distance(view.SkyTop, Color.Lerp(dark, morningTop, restored)), Is.LessThan(.0001f));
            Assert.That(view.SunPosition.x, Is.EqualTo(morning.x));
            Assert.That(view.SunPosition.y, Is.LessThan(morning.y));
            view.AdvancePresentation(10f);
            Assert.That(view.IsAnimating, Is.False);
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.white));
            Assert.That(view.SkyTop, Is.EqualTo(morningTop));
            Assert.That(view.SunPosition, Is.EqualTo(morning));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Revival_CancelOrRestart_DoesNotLeakBlackTint(bool restart)
        {
            view.BeginRevivalDark();
            view.StartRevivalSunrise();
            view.AdvancePresentation(.2f);
            if (restart) view.BeginLocation();
            else view.EndPresentation();
            Assert.That(view.IsAnimating, Is.False);
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.white));
            Assert.That(view.SunPosition.y, Is.GreaterThan(0f));
            clock.SetCurrentTime(1, DayPeriod.Evening);
            Assert.That(view.ForegroundTint, Is.EqualTo(Color.white));
        }

        [TestCase("foreground")]
        [TestCase("revivalSeconds")]
        [TestCase("revivalColourStartOffset")]
        public void Revival_InvalidConfigurationIsReportedWithoutBlockingNormalTransitions(string property)
        {
            Assert.That(view.ValidateRevivalConfiguration(out _), Is.True);
            var data = new SerializedObject(view);
            if (property == "foreground") data.FindProperty(property).objectReferenceValue = null;
            else data.FindProperty(property).floatValue = float.NaN;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(view.ValidateRevivalConfiguration(out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(view.ValidateConfiguration(out _), Is.True);
        }
    }
}
#endif
