#if UNITY_EDITOR
using Nekolpos.System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Nekolpos.EditorTools
{
    public static class EventReactionTimelineSceneMenu
    {
        public static void OpenTimelineProductionScene()
        {
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(OpenBetaSceneNames.EventTimelineProductionPath, OpenSceneMode.Single);
        }
    }
}
#endif
