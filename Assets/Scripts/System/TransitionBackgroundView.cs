using System;
using Cysharp.Threading.Tasks;
using Nekolpos.TimeSystem;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    public sealed class TransitionBackgroundView : MonoBehaviour
    {
        [Serializable]
        public sealed class PeriodStyle
        {
            public DayPeriod period;
            public Color top, bottom, sunColor = Color.white;
            public Vector2 sunPosition;
        }

        [SerializeField] private TimeManager clock;
        [SerializeField] private TransitionSkyGraphic sky;
        [SerializeField] private Image sun;
        [SerializeField] private RectTransform foregroundFrame;
        [SerializeField] private Image foreground;
        [SerializeField, Min(.05f)] private float revivalSeconds = 2.5f;
        [SerializeField, Range(-.25f, .25f)] private float revivalColourStartOffset;
        [SerializeField] private Color revivalDarkTop = new Color(.012f, .02f, .05f, 1f);
        [SerializeField] private Color revivalDarkBottom = new Color(.025f, .04f, .08f, 1f);
        [SerializeField] private bool keepVisibleSunAboveForeground = true;
        [SerializeField] private float boundaryStartY = .03f;
        [SerializeField] private float boundarySlope = .94f;
        [SerializeField] private PeriodStyle[] periods = Array.Empty<PeriodStyle>();
        [SerializeField, Min(.05f)] private float transitionSeconds = 1f;
        [SerializeField, Range(0f, .3f)] private float arcHeight = .12f;
        private enum Presentation { None, Location, Time, Revival }
        private Presentation presentation;
        private TimeManager subscribedClock;
        private Color fromTop, fromBottom, fromSun;
        private Vector2 fromPosition, toPosition, dawnStartPosition;
        private PeriodStyle target;
        private float elapsed;
        private bool resetAtDawn;
        private float revivalColourStart;
        public bool IsAnimating { get; private set; }
        public Color SkyTop => sky != null ? sky.TopColor : Color.black;
        public Vector2 SunPosition => sun != null ? sun.rectTransform.anchorMin : Vector2.zero;
        public Color SunColor => sun != null ? sun.color : Color.clear;
        public Color ForegroundTint => foreground != null ? foreground.color : Color.white;

        public bool ValidateRevivalConfiguration(out string error)
        {
            if (!ValidateConfiguration(out error)) return false;
            if (foreground == null || !Finite(revivalSeconds) || revivalSeconds <= 0f
                || !Finite(revivalColourStartOffset) || !Finite(revivalDarkTop) || !Finite(revivalDarkBottom))
            { error = "Assign the foreground and finite revival colours, timing and a positive duration."; return false; }
            return true;
        }

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (sky == null || sun == null || sun.sprite == null || foregroundFrame == null || periods == null)
            { error = "Assign the sky, sun Sprite, foreground frame and four period styles."; return false; }
            var foregroundAspect = foregroundFrame.GetComponent<AspectRatioFitter>();
            if (foregroundAspect == null || !Finite(foregroundAspect.aspectRatio) || foregroundAspect.aspectRatio <= 0f
                || !Finite(boundaryStartY) || !Finite(boundarySlope))
            { error = "Foreground aspect and sky boundary must be finite, with a positive aspect."; return false; }
            if (float.IsNaN(transitionSeconds) || float.IsInfinity(transitionSeconds) || transitionSeconds <= 0f
                || float.IsNaN(arcHeight) || float.IsInfinity(arcHeight))
            { error = "Transition duration and arc must be finite, with a positive duration."; return false; }
            for (int i = 0; i < 4; i++)
            {
                int matches = 0;
                foreach (var style in periods)
                {
                    if (style == null || style.period != (DayPeriod)i) continue;
                    matches++;
                    if (!Finite(style.sunPosition.x) || !Finite(style.sunPosition.y)
                        || !Finite(style.top) || !Finite(style.bottom) || !Finite(style.sunColor))
                    { error = "Period colours and sun positions must be finite."; return false; }
                }
                if (matches != 1) { error = "Assign one style for each Morning/Afternoon/Evening/Night period."; return false; }
            }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Color value) => Finite(value.r) && Finite(value.g) && Finite(value.b) && Finite(value.a);

        private void OnEnable()
        {
            BindClock();
            if (presentation == Presentation.None) Synchronize();
        }

        private void OnDisable()
        {
            if (subscribedClock != null) subscribedClock.OnTimeAdvanced -= OnTimeAdvanced;
            subscribedClock = null;
            EndPresentation();
        }

        private void Update() => AdvancePresentation(Time.unscaledDeltaTime);

        public void BeginLocation() => Begin(Presentation.Location);
        public void BeginTime() => Begin(Presentation.Time);
        public void BeginRevivalDark()
        {
            Begin(Presentation.Revival);
            target = Resolve(DayPeriod.Morning);
            toPosition = ResolveSunPosition(target);
            dawnStartPosition = SunStartBelowViewport(toPosition.x);
            SetSunPosition(dawnStartPosition);
            sun.color = target.sunColor;
            sky.SetColors(revivalDarkTop, revivalDarkBottom);
            foreground.color = Color.black;
        }

        public void StartRevivalSunrise()
        {
            if (presentation != Presentation.Revival) return;
            float radius = -dawnStartPosition.y - .01f;
            // Start restoring both regions as the sun's outer glow clears the foreground.
            float visibleY = ForegroundBoundaryAt(toPosition.x) - radius;
            revivalColourStart = Mathf.Clamp((visibleY - dawnStartPosition.y)
                / Mathf.Max(.001f, toPosition.y - dawnStartPosition.y) + revivalColourStartOffset, 0f, .95f);
            elapsed = 0f;
            IsAnimating = true;
        }
        private void Begin(Presentation mode)
        {
            BindClock();
            Synchronize();
            presentation = mode;
        }

        private void BindClock()
        {
            if (clock == null) clock = FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            if (subscribedClock == clock) return;
            if (subscribedClock != null) subscribedClock.OnTimeAdvanced -= OnTimeAdvanced;
            subscribedClock = clock;
            if (subscribedClock != null) subscribedClock.OnTimeAdvanced += OnTimeAdvanced;
        }

        public void EndPresentation()
        {
            presentation = Presentation.None;
            Synchronize();
        }

        private PeriodStyle Resolve(DayPeriod period)
        {
            foreach (var style in periods) if (style != null && style.period == period) return style;
            return null;
        }

        public void Synchronize()
        {
            IsAnimating = false;
            if (foreground != null) foreground.color = Color.white;
            ApplyStyle(Resolve(clock != null ? clock.CurrentPeriod : DayPeriod.Morning));
        }

        private void ApplyStyle(PeriodStyle style)
        {
            if (style == null || sky == null || sun == null) return;
            sky.SetColors(style.top, style.bottom);
            sun.color = style.sunColor;
            SetSunPosition(ResolveSunPosition(style));
        }

        private void OnTimeAdvanced(TimeAdvanceResult result)
        {
            if (presentation == Presentation.Location || presentation == Presentation.Revival) return;
            if (presentation != Presentation.Time || !result.PeriodChanged)
            {
                Synchronize();
                return;
            }
            target = Resolve(result.CurrentPeriod);
            if (target == null || sky == null || sun == null) return;
            fromTop = sky.TopColor;
            fromBottom = sky.BottomColor;
            fromSun = sun.color;
            fromPosition = SunPosition;
            toPosition = ResolveSunPosition(target);
            resetAtDawn = result.AdvancedDayCount > 0 || result.PreviousPeriod == DayPeriod.Night;
            if (resetAtDawn)
            {
                dawnStartPosition = SunStartBelowViewport(toPosition.x);
                // Reposition below the viewport while invisible, then rise behind the foreground.
                if (fromSun.a <= 0f) SetSunPosition(dawnStartPosition);
            }
            elapsed = 0f;
            IsAnimating = true;
        }

        public void AdvancePresentation(float seconds)
        {
            if (!IsAnimating || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return;
            elapsed += seconds;
            if (presentation == Presentation.Revival)
            {
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(.05f, revivalSeconds)));
                SetSunPosition(Vector2.Lerp(dawnStartPosition, toPosition, rise));
                float colour = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(revivalColourStart, 1f, rise));
                sky.SetColors(Color.Lerp(revivalDarkTop, target.top, colour), Color.Lerp(revivalDarkBottom, target.bottom, colour));
                foreground.color = Color.Lerp(Color.black, Color.white, colour);
                if (elapsed >= revivalSeconds)
                {
                    ApplyStyle(target);
                    foreground.color = Color.white;
                    IsAnimating = false;
                }
                return;
            }
            float duration = float.IsNaN(transitionSeconds) || float.IsInfinity(transitionSeconds) ? 1f : Mathf.Max(.05f, transitionSeconds);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            sky.SetColors(Color.Lerp(fromTop, target.top, t), Color.Lerp(fromBottom, target.bottom, t));
            if (resetAtDawn)
            {
                if (fromSun.a > 0f && t < .5f)
                {
                    Color color = fromSun;
                    color.a *= 1f - t * 2f;
                    sun.color = color;
                    SetSunPosition(fromPosition);
                }
                else
                {
                    float rise = fromSun.a <= 0f ? t : Mathf.Clamp01((t - .5f) * 2f);
                    Color color = target.sunColor;
                    color.a *= rise;
                    sun.color = color;
                    SetSunPosition(Vector2.Lerp(dawnStartPosition, toPosition, rise));
                }
            }
            else
            {
                sun.color = Color.Lerp(fromSun, target.sunColor, t);
                SetSunPosition(Vector2.Lerp(fromPosition, toPosition, t) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * arcHeight));
            }
            if (elapsed >= duration)
            {
                ApplyStyle(target);
                IsAnimating = false;
            }
        }

        private void SetSunPosition(Vector2 position)
        {
            sun.rectTransform.anchorMin = sun.rectTransform.anchorMax = position;
        }

        private Vector2 ResolveSunPosition(PeriodStyle style)
        {
            Vector2 position = style.sunPosition;
            if (!keepVisibleSunAboveForeground || style.sunColor.a <= 0f || foregroundFrame == null) return position;
            var viewport = sun.rectTransform.parent as RectTransform;
            if (viewport == null || viewport.rect.width <= 0f || viewport.rect.height <= 0f) return position;
            float boundary = ForegroundBoundaryAt(position.x);
            float radius = sun.rectTransform.rect.height / (2f * viewport.rect.height);
            position.y = Mathf.Max(position.y, Mathf.Min(.98f, boundary + radius + .005f));
            return position;
        }

        private Vector2 SunStartBelowViewport(float x)
        {
            var viewport = sun.rectTransform.parent as RectTransform;
            float radius = viewport != null && viewport.rect.height > 0f
                ? sun.rectTransform.rect.height / (2f * viewport.rect.height) : .1f;
            return new Vector2(x, -radius - .01f);
        }

        private float ForegroundBoundaryAt(float x)
        {
            var viewport = sun.rectTransform.parent as RectTransform;
            if (viewport == null || viewport.rect.width <= 0f || viewport.rect.height <= 0f || foregroundFrame == null)
                return boundaryStartY + boundarySlope * x;
            var aspect = foregroundFrame.GetComponent<AspectRatioFitter>();
            float ratio = aspect != null ? aspect.aspectRatio : 16f / 9f;
            if (!Finite(ratio) || ratio <= 0f) return boundaryStartY + boundarySlope * x;
            float width = Mathf.Max(viewport.rect.width, viewport.rect.height * ratio);
            float height = width / ratio;
            float sourceX = .5f + (x - .5f) * viewport.rect.width / width;
            return .5f + (boundaryStartY + boundarySlope * sourceX - .5f) * height / viewport.rect.height;
        }

        public async UniTask WaitForCompletionAsync()
        {
            while (IsAnimating && isActiveAndEnabled) await UniTask.Yield(PlayerLoopTiming.Update);
        }
    }
}
