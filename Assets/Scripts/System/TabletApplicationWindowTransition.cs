using System.Collections;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// Expands an application window from the selected home-screen icon.
    /// The component owns only one transition, so another window change can
    /// safely interrupt it without leaving the target transparent or scaled.
    /// </summary>
    public sealed class TabletApplicationWindowTransition : MonoBehaviour
    {
        private Coroutine openingCoroutine;
        private RectTransform activeTarget;
        private Vector3 activeTargetPosition;
        private Vector3 activeTargetScale;
        private CanvasGroup activeTargetGroup;

        public bool IsTransitioning => openingCoroutine != null;

        public void PlayOpen(RectTransform sourceIcon, GameObject targetWindow, float duration, float minimumStartScale)
        {
            Stop();

            if (targetWindow == null)
            {
                return;
            }

            RectTransform target = targetWindow.GetComponent<RectTransform>();
            if (sourceIcon == null || target == null)
            {
                ShowFully(targetWindow);
                return;
            }

            openingCoroutine = StartCoroutine(OpenRoutine(
                sourceIcon,
                target,
                Mathf.Max(0.01f, duration),
                Mathf.Clamp(minimumStartScale, 0.01f, 1f)));
        }

        /// <summary>
        /// Shrinks an application window back to its home-screen icon. The home window is made
        /// visible before the animation starts, so closing never leaves a blank tablet display.
        /// </summary>
        public void PlayClose(
            RectTransform destinationIcon,
            GameObject targetWindow,
            GameObject homeWindow,
            float duration,
            float minimumEndScale)
        {
            Stop();

            if (homeWindow != null)
            {
                ShowFully(homeWindow);
            }

            if (targetWindow == null)
            {
                return;
            }

            RectTransform target = targetWindow.GetComponent<RectTransform>();
            if (destinationIcon == null || target == null)
            {
                targetWindow.SetActive(false);
                return;
            }

            openingCoroutine = StartCoroutine(CloseRoutine(
                destinationIcon,
                target,
                Mathf.Max(0.01f, duration),
                Mathf.Clamp(minimumEndScale, 0.01f, 1f)));
        }

        public void Stop()
        {
            if (openingCoroutine != null)
            {
                StopCoroutine(openingCoroutine);
                openingCoroutine = null;
            }

            if (activeTarget != null)
            {
                RestoreActiveTarget();
                activeTarget = null;
                activeTargetGroup = null;
            }
        }

        /// <summary>Shows a window at its authored full-screen transform without an animation.</summary>
        public void ShowImmediately(GameObject targetWindow)
        {
            Stop();
            ShowFully(targetWindow);
        }

        private IEnumerator OpenRoutine(RectTransform sourceIcon, RectTransform target, float duration, float minimumStartScale)
        {
            target.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();

            Vector3 destinationPosition = target.position;
            Vector3 destinationScale = target.localScale;
            float sourceScale = CalculateSourceScale(sourceIcon, target, minimumStartScale);
            Vector3 sourcePosition = sourceIcon.position;
            CanvasGroup group = GetOrAddCanvasGroup(target.gameObject);
            BeginTransition(target, group, destinationPosition, destinationScale);

            target.position = sourcePosition;
            target.localScale = destinationScale * sourceScale;
            SetVisual(group, 0f);

            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                target.position = Vector3.LerpUnclamped(sourcePosition, destinationPosition, progress);
                target.localScale = Vector3.LerpUnclamped(destinationScale * sourceScale, destinationScale, progress);
                SetVisual(group, progress);
                yield return null;
            }

            if (target != null)
            {
                target.position = destinationPosition;
                target.localScale = destinationScale;
                SetVisual(group, 1f);
            }

            FinishTransition();
        }

        private IEnumerator CloseRoutine(RectTransform destinationIcon, RectTransform target, float duration, float minimumEndScale)
        {
            target.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();

            Vector3 sourcePosition = target.position;
            Vector3 sourceScale = target.localScale;
            float endScale = CalculateSourceScale(destinationIcon, target, minimumEndScale);
            Vector3 destinationPosition = destinationIcon.position;
            CanvasGroup group = GetOrAddCanvasGroup(target.gameObject);
            BeginTransition(target, group, sourcePosition, sourceScale);
            SetVisual(group, 1f);

            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                target.position = Vector3.LerpUnclamped(sourcePosition, destinationPosition, progress);
                target.localScale = Vector3.LerpUnclamped(sourceScale, sourceScale * endScale, progress);
                SetVisual(group, 1f - progress);
                yield return null;
            }

            if (target != null)
            {
                target.position = sourcePosition;
                target.localScale = sourceScale;
                SetVisual(group, 1f);
                target.gameObject.SetActive(false);
            }

            FinishTransition();
        }

        private void BeginTransition(
            RectTransform target,
            CanvasGroup group,
            Vector3 completedPosition,
            Vector3 completedScale)
        {
            activeTarget = target;
            activeTargetPosition = completedPosition;
            activeTargetScale = completedScale;
            activeTargetGroup = group;
        }

        private void FinishTransition()
        {
            activeTarget = null;
            activeTargetGroup = null;
            openingCoroutine = null;
        }

        private void RestoreActiveTarget()
        {
            activeTarget.position = activeTargetPosition;
            activeTarget.localScale = activeTargetScale;
            if (activeTargetGroup != null)
            {
                SetVisual(activeTargetGroup, 1f);
            }
        }

        private static float CalculateSourceScale(RectTransform source, RectTransform target, float fallback)
        {
            float sourceSize = Mathf.Max(GetWorldWidth(source), GetWorldHeight(source));
            float targetSize = Mathf.Max(GetWorldWidth(target), GetWorldHeight(target));
            if (sourceSize <= Mathf.Epsilon || targetSize <= Mathf.Epsilon)
            {
                return fallback;
            }

            return Mathf.Clamp(sourceSize / targetSize, fallback, 1f);
        }

        private static float GetWorldWidth(RectTransform transform)
        {
            Vector3[] corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            return Vector3.Distance(corners[0], corners[3]);
        }

        private static float GetWorldHeight(RectTransform transform)
        {
            Vector3[] corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            return Vector3.Distance(corners[0], corners[1]);
        }

        private static void ShowFully(GameObject targetWindow)
        {
            if (targetWindow == null)
            {
                return;
            }

            targetWindow.SetActive(true);
            RectTransform target = targetWindow.GetComponent<RectTransform>();
            if (target != null)
            {
                target.localScale = Vector3.one;
            }

            SetVisual(GetOrAddCanvasGroup(targetWindow), 1f);
        }

        private static void SetVisual(CanvasGroup group, float alpha)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = alpha;
            bool isInteractive = alpha >= 0.99f;
            group.interactable = isInteractive;
            group.blocksRaycasts = isInteractive;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject targetWindow)
        {
            if (!targetWindow.TryGetComponent(out CanvasGroup group))
            {
                group = targetWindow.AddComponent<CanvasGroup>();
            }

            return group;
        }
    }
}
