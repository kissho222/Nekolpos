using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Nekolpos.EditorTools
{
    [InitializeOnLoad]
    internal static class VolumeInspectorSelectionGuard
    {
        private const int DeferredCleanupPassCount = 8;
        private static int deferredCleanupPassesRemaining;

        static VolumeInspectorSelectionGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearVolumeSelectionBeforeReload;
            ScheduleDeferredCleanup();
        }

        private static void ClearVolumeSelectionBeforeReload()
        {
            ClearVolumeSelection();
        }

        private static void ScheduleDeferredCleanup()
        {
            deferredCleanupPassesRemaining = DeferredCleanupPassCount;
            EditorApplication.update -= RunDeferredCleanup;
            EditorApplication.update += RunDeferredCleanup;
        }

        private static void RunDeferredCleanup()
        {
            if (deferredCleanupPassesRemaining <= 0)
            {
                EditorApplication.update -= RunDeferredCleanup;
                return;
            }

            deferredCleanupPassesRemaining--;

            if (deferredCleanupPassesRemaining == DeferredCleanupPassCount - 1)
            {
                SanitizeVolumeProfiles();
            }

            if (ClearVolumeSelection())
            {
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }

            RebuildVolumeInspectors();
        }

        private static void SanitizeVolumeProfiles()
        {
            bool changed = false;
            string[] guids = AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null || profile.components == null)
                {
                    continue;
                }

                int removedCount = profile.components.RemoveAll(component => component == null);
                if (removedCount <= 0)
                {
                    continue;
                }

                EditorUtility.SetDirty(profile);
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static bool ClearVolumeSelection()
        {
            Object[] selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                Object selectedObject = selectedObjects[i];
                if (selectedObject is VolumeProfile || selectedObject is VolumeComponent)
                {
                    Selection.objects = new Object[0];
                    return true;
                }
            }

            return false;
        }

        private static void RebuildVolumeInspectors()
        {
            global::System.Type inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            if (inspectorWindowType == null)
            {
                return;
            }

            Object[] inspectorWindows = Resources.FindObjectsOfTypeAll(inspectorWindowType);
            PropertyInfo trackerProperty = inspectorWindowType.GetProperty(
                "tracker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo lockedProperty = inspectorWindowType.GetProperty(
                "isLocked",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < inspectorWindows.Length; i++)
            {
                Object inspectorWindow = inspectorWindows[i];
                if (inspectorWindow == null)
                {
                    continue;
                }

                ActiveEditorTracker tracker = trackerProperty?.GetValue(inspectorWindow) as ActiveEditorTracker;
                if (tracker == null || !TrackerContainsVolumeEditor(tracker))
                {
                    continue;
                }

                if (lockedProperty != null && lockedProperty.CanWrite)
                {
                    lockedProperty.SetValue(inspectorWindow, false);
                }

                tracker.ForceRebuild();
                if (inspectorWindow is EditorWindow editorWindow)
                {
                    editorWindow.Repaint();
                }
            }
        }

        private static bool TrackerContainsVolumeEditor(ActiveEditorTracker tracker)
        {
            Editor[] activeEditors = tracker.activeEditors;
            if (activeEditors == null)
            {
                return false;
            }

            for (int i = 0; i < activeEditors.Length; i++)
            {
                Editor editor = activeEditors[i];
                if (editor == null)
                {
                    continue;
                }

                Object target = editor.target;
                if (target == null || target is VolumeProfile || target is VolumeComponent)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
