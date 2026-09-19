using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    [InitializeOnLoad]
    public static class OneShotTitleSceneUiBuilder
    {
        private const string RequestPath = "Temp/BuildTitleSceneUi.request";

        static OneShotTitleSceneUiBuilder()
        {
            EditorApplication.delayCall += TryRun;
        }

        private static void TryRun()
        {
            if (!File.Exists(RequestPath))
            {
                return;
            }

            File.Delete(RequestPath);
            Debug.Log("[OneShotTitleSceneUiBuilder] Running requested TitleScene UI rebuild.");
            OpenBetaTitleSceneBuilder.BuildTitleSceneUi();
        }
    }
}
