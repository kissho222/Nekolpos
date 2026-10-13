using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Nekolpos.ActionSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekolpos.System
{
    // Restore before normal UI updates so animation/state code always sees its own alpha.
    [DefaultExecutionOrder(-32000)]
    public sealed class WindowsScreenCaptureController : MonoBehaviour
    {
        [SerializeField] private Transform[] presentationRoots = Array.Empty<Transform>();
        private readonly Dictionary<CanvasGroup, float> suppressedGroups = new Dictionary<CanvasGroup, float>();
        private readonly List<Transform> exclusions = new List<Transform>();
        public bool HudHidden { get; private set; }
        public string LastCapturePath { get; private set; }

        private void OnEnable()
        {
            Canvas.preWillRenderCanvases += RestoreAlphas;
            Canvas.willRenderCanvases += ApplyHudVisibility;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            Canvas.preWillRenderCanvases -= RestoreAlphas;
            Canvas.willRenderCanvases -= ApplyHudVisibility;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SetHudHidden(false);
            StopAllCoroutines();
        }

        private void Update()
        {
            RestoreAlphas();
            if (DebugConsoleInput.IsBlocked) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (Input.GetKeyDown(KeyCode.F10)) SetHudHidden(!HudHidden);
            if (Input.GetKeyDown(KeyCode.Print)) CaptureScreenshot();
#endif
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SetHudHidden(false);
        private void OnActiveSceneChanged(Scene previous, Scene next) => SetHudHidden(false);

        public void SetHudHidden(bool hidden)
        {
            RestoreAlphas();
            HudHidden = hidden;
        }

        private void RestoreAlphas()
        {
            foreach (var entry in suppressedGroups)
                if (entry.Key != null) entry.Key.alpha = entry.Value;
            suppressedGroups.Clear();
        }

        public void ApplyHudVisibility()
        {
            RestoreAlphas();
            if (!HudHidden) return;
            exclusions.Clear();
            foreach (Transform root in presentationRoots)
                if (root != null) exclusions.Add(root);
            foreach (FadeController fade in FindObjectsByType<FadeController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (fade.OverlayRoot != null) exclusions.Add(fade.OverlayRoot);

            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas.rootCanvas != canvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                // Traverse below the Canvas: a Fade can share the same Canvas as conversation UI.
                foreach (Transform child in canvas.transform) SuppressBranch(child);
            }
        }

        private void SuppressBranch(Transform branch)
        {
            bool containsPresentation = false;
            foreach (Transform root in exclusions)
            {
                if (branch == root || branch.IsChildOf(root)) return;
                if (root.IsChildOf(branch)) containsPresentation = true;
            }
            if (containsPresentation)
            {
                foreach (Transform child in branch) SuppressBranch(child);
                return;
            }
            CanvasGroup group = branch.GetComponent<CanvasGroup>();
            if (group == null) group = branch.gameObject.AddComponent<CanvasGroup>();
            Suppress(group);
            // Dropdowns and popups may intentionally ignore their parent's CanvasGroup.
            foreach (CanvasGroup nested in branch.GetComponentsInChildren<CanvasGroup>(true))
                if (nested.ignoreParentGroups) Suppress(nested);
        }

        private void Suppress(CanvasGroup group)
        {
            if (suppressedGroups.ContainsKey(group)) return;
            suppressedGroups.Add(group, group.alpha);
            group.alpha = 0f;
        }

        public void CaptureScreenshot()
        {
            if (DebugSaveSession.IsIsolated) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (isActiveAndEnabled) StartCoroutine(CaptureFrame());
#endif
        }

        private IEnumerator CaptureFrame()
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null) throw new IOException("Screen capture returned no image.");
                LastCapturePath = SavePng(Application.persistentDataPath, texture.EncodeToPNG());
                Debug.Log("[Screenshot] Saved: " + LastCapturePath, this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Screenshot] Save failed: " + exception.Message, this);
            }
            finally
            {
                if (texture != null) Destroy(texture);
            }
        }

        public static string SavePng(string persistentDirectory, byte[] png)
        {
            if (png == null || png.Length == 0) throw new ArgumentException("PNG data is empty.", nameof(png));
            string directory = Path.Combine(persistentDirectory, "Screenshots");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Nekolpos_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff") + "_" + Guid.NewGuid().ToString("N") + ".png");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(png, 0, png.Length);
            return path;
        }
    }
}
