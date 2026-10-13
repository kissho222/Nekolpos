using System;
using Nekolpos.System;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Nekolpos.EditorTools
{
    public static class DebugSessionMenu
    {
        [MenuItem("Nekolpos/Debug/Start Isolated Play")]
        public static void StartIsolatedPlay()
        {
            if (!OpenBetaSceneNames.IsMainGameplayScene(SceneManager.GetActiveScene().name)) throw new InvalidOperationException("MainSceneを開いてください。Sceneは自動保存しません。");
            DebugSaveSession.ArmNextPlay();
            EditorApplication.isPlaying = true;
        }
        [MenuItem("Nekolpos/Debug/End Isolated Play")]
        public static void StopIsolatedPlay() { DebugSaveSession.Finish(); EditorApplication.isPlaying = false; }
    }
}
