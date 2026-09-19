using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Nekolpos.System
{
    [DefaultExecutionOrder(-30000)]
    public sealed class FixedWindowResolution : MonoBehaviour
    {
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;

        private const float TargetAspect = (float)ReferenceWidth / ReferenceHeight;
        private const int MinimumWidth = 960;
        private const int MinimumHeight = 540;
        private const float ResizeSettleSeconds = 0.08f;

        private static FixedWindowResolution instance;
        private int lastSeenWidth = -1;
        private int lastSeenHeight = -1;
        private float lastResizeTime;
        private bool hasPendingResize;
        private Coroutine restorePositionCoroutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstance()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            if (instance != null || Application.isBatchMode)
            {
                return;
            }

            GameObject gameObject = new GameObject(nameof(FixedWindowResolution));
            instance = gameObject.AddComponent<FixedWindowResolution>();
            DontDestroyOnLoad(gameObject);
#endif
        }

        private void Awake()
        {
#if !UNITY_STANDALONE || UNITY_EDITOR
            Destroy(gameObject);
            return;
#else
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            TrackScreenSize();
            ApplyAspectRatio(force: true);
#endif
        }

        private void Update()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            if (!IsWindowed())
            {
                TrackScreenSize();
                hasPendingResize = false;
                return;
            }

            if (Screen.width != lastSeenWidth || Screen.height != lastSeenHeight)
            {
                TrackScreenSize();
                lastResizeTime = Time.unscaledTime;
                hasPendingResize = true;
                return;
            }

            if (hasPendingResize && Time.unscaledTime - lastResizeTime >= ResizeSettleSeconds)
            {
                hasPendingResize = false;
                ApplyAspectRatio(force: false);
            }
#endif
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ApplyAspectRatio(force: false);
            }
        }

        private static bool IsWindowed()
        {
            return Screen.fullScreenMode == FullScreenMode.Windowed || !Screen.fullScreen;
        }

        private void TrackScreenSize()
        {
            lastSeenWidth = Screen.width;
            lastSeenHeight = Screen.height;
        }

        private void ApplyAspectRatio(bool force)
        {
            if (!IsWindowed() || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            int currentWidth = Screen.width;
            int currentHeight = Screen.height;
            int widthFromHeight = Mathf.RoundToInt(currentHeight * TargetAspect);
            int heightFromWidth = Mathf.RoundToInt(currentWidth / TargetAspect);

            int targetWidth;
            int targetHeight;

            if (Mathf.Abs(widthFromHeight - currentWidth) <= Mathf.Abs(heightFromWidth - currentHeight))
            {
                targetWidth = widthFromHeight;
                targetHeight = currentHeight;
            }
            else
            {
                targetWidth = currentWidth;
                targetHeight = heightFromWidth;
            }

            if (targetWidth < MinimumWidth)
            {
                targetWidth = MinimumWidth;
                targetHeight = Mathf.RoundToInt(targetWidth / TargetAspect);
            }

            if (targetHeight < MinimumHeight)
            {
                targetHeight = MinimumHeight;
                targetWidth = Mathf.RoundToInt(targetHeight * TargetAspect);
            }

            targetWidth = Mathf.Max(2, targetWidth - targetWidth % 2);
            targetHeight = Mathf.Max(2, targetHeight - targetHeight % 2);

            if (!force && Mathf.Abs(targetWidth - currentWidth) <= 1 && Mathf.Abs(targetHeight - currentHeight) <= 1)
            {
                return;
            }

            if (targetWidth == currentWidth && targetHeight == currentHeight)
            {
                return;
            }

            TryGetWindowPosition(out int windowX, out int windowY);
            Screen.SetResolution(targetWidth, targetHeight, FullScreenMode.Windowed);
            RestoreWindowPosition(windowX, windowY);
            lastSeenWidth = targetWidth;
            lastSeenHeight = targetHeight;
        }

        private void RestoreWindowPosition(int windowX, int windowY)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (windowX == int.MinValue || windowY == int.MinValue)
            {
                return;
            }

            if (restorePositionCoroutine != null)
            {
                StopCoroutine(restorePositionCoroutine);
            }

            restorePositionCoroutine = StartCoroutine(RestoreWindowPositionAfterResolutionChange(windowX, windowY));
#endif
        }

        private static void TryGetWindowPosition(out int windowX, out int windowY)
        {
            windowX = int.MinValue;
            windowY = int.MinValue;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr window = GetActiveWindow();
            if (window == IntPtr.Zero || !GetWindowRect(window, out RectIntNative rect))
            {
                return;
            }

            windowX = rect.Left;
            windowY = rect.Top;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private IEnumerator RestoreWindowPositionAfterResolutionChange(int windowX, int windowY)
        {
            yield return null;
            yield return null;

            IntPtr window = GetActiveWindow();
            if (window == IntPtr.Zero || !GetWindowRect(window, out RectIntNative rect))
            {
                restorePositionCoroutine = null;
                yield break;
            }

            SetWindowPos(
                window,
                IntPtr.Zero,
                windowX,
                windowY,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top,
                SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate);

            restorePositionCoroutine = null;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RectIntNative rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RectIntNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [global::System.Flags]
        private enum SetWindowPosFlags : uint
        {
            NoZOrder = 0x0004,
            NoActivate = 0x0010
        }
#endif
    }
}
