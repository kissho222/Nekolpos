using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace Nekolpos.System
{
    /// <summary>Shared input ownership. The closing frame remains blocked to prevent click-through.</summary>
    public static class DebugConsoleInput
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool owned;
        private static Lease activeLease;
        private static int releasedFrame = -1;
        public static bool IsConsoleBlocked => ShouldBlock(owned || activeLease != null, releasedFrame, Time.frameCount);
        public static bool IsBlocked => IsConsoleBlocked || DebugScenarioExecutor.IsRunning;
        public static bool ConsoleOwnsInput => owned || activeLease != null;
        public static bool ShouldBlock(bool ownsInput, int released, int frame) => ownsInput || (released >= 0 && released == frame);
        public static void SetOwned(bool value) { owned = value; if (!value) releasedFrame = Time.frameCount; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { activeLease?.Dispose(); activeLease = null; owned = false; releasedFrame = -1; }
        public static Lease Acquire(Transform owner)
        {
            if (activeLease != null || owner == null) return null;
            activeLease = new Lease(owner); return activeLease;
        }
        public static void ReleaseAll() { activeLease?.Dispose(); activeLease = null; SetOwned(false); }
        public sealed class Lease : IDisposable
        {
            private readonly Transform owner;
            private readonly GameObject selection;
            private readonly TMP_InputField tmp;
            private readonly InputField legacy;
            private readonly CursorLockMode cursorLock;
            private readonly bool cursorVisible, navigation;
            private readonly int anchor, focus;
            private readonly List<(GraphicRaycaster raycaster, bool enabled)> raycasters = new();
            private bool disposed;
            internal Lease(Transform owner)
            {
                this.owner = owner; cursorLock = Cursor.lockState; cursorVisible = Cursor.visible;
                selection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                tmp = selection != null ? selection.GetComponent<TMP_InputField>() : null;
                legacy = selection != null ? selection.GetComponent<InputField>() : null;
                anchor = tmp != null ? tmp.selectionAnchorPosition : legacy != null ? legacy.selectionAnchorPosition : 0;
                focus = tmp != null ? tmp.selectionFocusPosition : legacy != null ? legacy.selectionFocusPosition : 0;
                navigation = EventSystem.current != null && EventSystem.current.sendNavigationEvents;
                SetOwned(true);
                tmp?.DeactivateInputField(); legacy?.DeactivateInputField();
                if (EventSystem.current != null) { EventSystem.current.SetSelectedGameObject(null); EventSystem.current.sendNavigationEvents = false; }
                Refresh();
            }
            public void Refresh()
            {
                if (disposed || owner == null) return;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && !selected.transform.IsChildOf(owner))
                {
                    selected.GetComponent<TMP_InputField>()?.DeactivateInputField(); selected.GetComponent<InputField>()?.DeactivateInputField();
                    EventSystem.current.SetSelectedGameObject(null);
                }
                foreach (var r in UnityEngine.Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r.transform.IsChildOf(owner) || raycasters.Exists(x => x.raycaster == r)) continue;
                    raycasters.Add((r, r.enabled)); r.enabled = false;
                }
                foreach (var entry in raycasters) if (entry.raycaster != null) entry.raycaster.enabled = false;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                foreach (var entry in raycasters) if (entry.raycaster != null) entry.raycaster.enabled = entry.enabled;
                raycasters.Clear(); SetOwned(false); if (activeLease == this) activeLease = null;
                if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = navigation;
                Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
                if (NekolposDebugConsole.CanDisplay() && selection != null && selection.activeInHierarchy)
                {
                    EventSystem.current?.SetSelectedGameObject(selection);
                    if (tmp != null && tmp.interactable) { tmp.ActivateInputField(); tmp.selectionAnchorPosition = anchor; tmp.selectionFocusPosition = focus; }
                    if (legacy != null && legacy.interactable) { legacy.ActivateInputField(); legacy.selectionAnchorPosition = anchor; legacy.selectionFocusPosition = focus; }
                }
            }
        }
#else
        public static bool IsBlocked => false;
#endif
    }
}
